using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using WeChatSendGuard.Models;
using Rect = System.Windows.Rect;

namespace WeChatSendGuard.Services;

/// <summary>一次标题识别的结果。</summary>
internal sealed record OcrReadResult(
    string Text,
    RegionSpec Region,
    RegionSpec BaselineRegion,
    double RenderScale,
    int Dpi,
    long Milliseconds);

/// <summary>
/// 标题区 OCR 读取器（引擎：PaddleOCR PP-OCRv5 mobile，Paddle Inference + oneDNN，CPU）。
///
/// 为什么不用 Windows.Media.Ocr：同一批 50 帧采集样本（light/dark × 100%/125%）上，
/// Windows.Media.Ocr 存在漏字（如 BUG→BIJG）；PP-OCRv5 mobile 单路 raw@2x 即
/// 命中率 100%、字符级准确率 100%。代价是单次约 600ms（Windows OCR 约 3~4ms），
/// 模型 20MB + 原生推理库约 230MB —— 均已在 Spike-05 13.8 中确认接受。
///
/// 关键约束（来自 Spike-05 实测）：
/// 1. 裁剪区按 dpi/96 归一化，且裁剪后必须先缩回 96 DPI 基准尺寸，再按整数 baseScale 放大：
///    直接按 baseScale ÷ (dpi/96) 放大时，150% 的倍数 5/1.5=3.33 非整数，
///    最近邻插值会畸变字形，导致漏报。
/// 2. PaddleOCR 自带 DBNet 检测，不再需要 raw/stretch/otsu 三路变体并集
///    （实测四组样本上 raw 单路即 100%，变体轴已无区分度），故这里只走 raw 一路，
///    把单次耗时控制在 1 次推理而不是 3 次。
/// </summary>
internal sealed class TitleOcrReader : IDisposable
{
    /// <summary>PP-OCRv5 mobile 模型的相对存放位置（相对可执行文件目录）。</summary>
    private const string ModelRoot = @"models\paddle-ppocrv5-mobile";

    /// <summary>
    /// 引擎只建一次（构造 ≈0.6s + 首帧 ≈0.9s，共约 1.5s 冷启动）。
    /// 用 Lazy 的 ExecutionAndPublication：并发首调只跑一次；构造失败后异常被缓存，
    /// 后续调用立即抛出同一异常，不会每次都付 1.5s 代价。
    /// </summary>
    private readonly Lazy<PaddleOcrAll> _engine = new(CreateEngine, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>上一次识别的指纹与结果：标题区画面未变时直接复用，跳过最耗 CPU 的放大与推理。</summary>
    private ulong _lastSignature;
    private OcrReadResult? _lastResult;

    /// <summary>启动时调用一次：把模型加载与首帧推理的冷启动成本挪到后台，避免拖慢第一次真识别。</summary>
    public void Warmup() => _ = _engine.Value;

    public OcrReadResult Read(BitmapSource fullImage, int dpi, OcrOptions options)
    {
        var stopwatch = Stopwatch.StartNew();

        var factor = options.NormalizeByDpi && dpi > 0 ? dpi / 96.0 : 1.0;
        var region = options.NormalizeByDpi ? options.BaseRegion.ScaleBy(factor) : options.BaseRegion;
        var cropped = ScreenCapturer.Crop(fullImage, region);

        // 物理裁剪图先缩回 96 DPI 基准尺寸，再按整数 baseScale 放大。
        // 等价于"裁剪区 ×dpi/96、放大 ÷dpi/96"，但把非整数放大倍数挪到了缩图这一步（HighQuality），
        // 使进入 OCR 的位图在任何 DPI 下都与 100% 完全一致。
        var baseline = ResampleToBaseline(cropped, factor);

        // 画面未变则直接复用上次结果：标题区像素（连同 DPI / 裁剪尺寸 / 放大倍数）与上次一致时，
        // 跳过最耗 CPU 的放大 + Paddle 推理。这样"静止停留在同一会话"这一常态几乎不占 CPU，
        // 只有真正切换会话（标题变化）才付一次推理开销。
        var signature = ComputeSignature(baseline, options.BaseScale, dpi);
        if (_lastResult is not null && signature == _lastSignature)
        {
            return _lastResult;
        }

        var rendered = ScaleUp(baseline, options.BaseScale);
        Dump(rendered, options.DumpDirectory);

        // Bgra32 的字节序即 OpenCV 的 BGRA；识别只吃 3 通道，去掉 alpha
        using var bgra = ToBgra32(rendered);
        using var bgr = bgra.CvtColor(ColorConversionCodes.BGRA2BGR);
        var text = _engine.Value.Run(bgr).Text ?? string.Empty;

        var result = new OcrReadResult(
            text,
            region,
            new RegionSpec(0, 0, baseline.PixelWidth, baseline.PixelHeight),
            options.BaseScale,
            dpi,
            stopwatch.ElapsedMilliseconds);

        _lastSignature = signature;
        _lastResult = result;
        return result;
    }

    /// <summary>
    /// 标题区画面 + 关键参数的指纹（FNV-1a 64）。像素、裁剪尺寸、放大倍数或 DPI 任一变化都算"画面变化"，
    /// 需要重新识别；只有全部一致才复用缓存结果。
    /// </summary>
    private static ulong ComputeSignature(BitmapSource image, int scale, int dpi)
    {
        var stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);

        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var value in pixels)
            {
                hash = (hash ^ value) * 1099511628211UL;
            }

            hash = (hash ^ (ulong)image.PixelWidth) * 1099511628211UL;
            hash = (hash ^ (ulong)image.PixelHeight) * 1099511628211UL;
            hash = (hash ^ (ulong)scale) * 1099511628211UL;
            return (hash ^ (ulong)dpi) * 1099511628211UL;
        }
    }

    /// <summary>把物理裁剪图按 1/factor 缩回 96 DPI 基准尺寸（factor 为 1 时原样返回）。</summary>
    private static BitmapSource ResampleToBaseline(BitmapSource source, double factor)
    {
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth / factor));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight / factor));
        if (width == source.PixelWidth && height == source.PixelHeight)
        {
            return source;
        }

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>按整数倍数放大裁剪图（衬白底，HighQuality 插值），输出 OCR 可直接消费的 Bgra32 位图。</summary>
    private static BitmapSource ScaleUp(BitmapSource source, int scale)
    {
        var width = Math.Max(1, source.PixelWidth * scale);
        var height = Math.Max(1, source.PixelHeight * scale);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawImage(source, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();

        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>把位图转成 OpenCV 的 BGRA Mat（包装托管数组，不复制像素）。</summary>
    private static Mat ToBgra32(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        return Mat.FromPixelData(height, width, MatType.CV_8UC4, pixels);
    }

    /// <summary>构造 PaddleOCR 引擎：从磁盘加载 PP-OCRv5 mobile 的 det / rec。</summary>
    private static PaddleOcrAll CreateEngine()
    {
        var root = Path.Combine(AppContext.BaseDirectory, ModelRoot);
        var detDir = Path.Combine(root, "det");
        var recDir = Path.Combine(root, "rec");
        if (!Directory.Exists(detDir) || !Directory.Exists(recDir))
        {
            throw new DirectoryNotFoundException(
                $"未找到 PP-OCRv5 mobile 模型目录：{root}（需含 det / rec 两个子目录，"
                + "各放 inference.json、inference.pdiparams、inference.yml）");
        }

        var model = new FullOcrModel(
            DetectionModel.FromDirectory(detDir, ModelVersion.V5),
            RecognizationModel.FromDirectoryV5(recDir));

        // 限制推理线程数：Paddle 默认吃满所有逻辑核，OCR 一跑整机 CPU 就冲顶；标题区是小图，
        // 取 min(4, 核数/2) 已足够，能明显降温且几乎不增加单次耗时。
        var threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        var engine = new PaddleOcrAll(model, PaddleDevice.Mkldnn(cpuMathThreadCount: threads))
        {
            // 标题栏是水平且正向的文字：关掉这两条分支既提速，也避免角度误判
            AllowRotateDetection = false,
            Enable180Classification = false,
        };

        // 标题条又扁又长（放大后约 1100~2300 px 宽），检测默认按长边 960 缩图会丢掉放大后的字形细节；
        // 这里禁用缩图，让检测阶段直接看到放大后的像素。
        engine.Detector.MaxSize = null;
        return engine;
    }

    private static void Dump(BitmapSource image, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(directory, $"{DateTime.Now:HHmmssfff}.png"));
            encoder.Save(stream);
        }
        catch
        {
            // 调试落盘失败不影响识别
        }
    }

    /// <summary>校验 OCR 模型可用性（启动时调用一次，尽早暴露"模型缺失"问题）。</summary>
    public static string DescribeEngine()
    {
        var root = Path.Combine(AppContext.BaseDirectory, ModelRoot);
        if (!Directory.Exists(Path.Combine(root, "det")) || !Directory.Exists(Path.Combine(root, "rec")))
        {
            throw new DirectoryNotFoundException($"模型目录不完整：{root}");
        }

        return "PaddleOCR PP-OCRv5 mobile（Paddle Inference + oneDNN，CPU）";
    }

    public void Dispose()
    {
        if (_engine.IsValueCreated)
        {
            _engine.Value.Dispose();
        }
    }
}
