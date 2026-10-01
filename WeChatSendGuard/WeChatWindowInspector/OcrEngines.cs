using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;

namespace WeChatWindowInspector;

/// <summary>
/// OCR 引擎抽象：Benchmark 通过它切换引擎，其余环节（裁剪 / DPI 归一化 / 预处理变体 / 放大 /
/// 变体并集 / 三态判定 / 字符级统计）完全共用，从而保证「引擎」是唯一变量。
/// </summary>
internal interface IOcrEngine : IDisposable
{
    /// <summary>命令行短名（windows / paddle）</summary>
    string Name { get; }

    /// <summary>日志用的引擎描述（型号 / 语言 / 推理后端）</summary>
    string Describe();

    /// <summary>识别一张已预处理并放大的位图，返回纯文本；失败时抛异常，由调用方兜底为 Unknown</summary>
    string Recognize(BitmapSource prepared);
}

internal static class OcrEngines
{
    public const string Windows = "windows";
    public const string Paddle = "paddle";

    /// <summary>
    /// <paramref name="paddleModel"/> 仅在 <paramref name="name"/> 为 paddle 时生效，
    /// 取值见 <see cref="PaddleOcrEngine.Server"/> / <see cref="PaddleOcrEngine.Mobile"/>；
    /// <paramref name="threads"/> 为 Paddle 推理线程数（0 = 交给 Paddle 决定）。
    /// </summary>
    public static IOcrEngine Create(string name, string paddleModel = PaddleOcrEngine.Server, int threads = 0, string device = PaddleOcrEngine.MkldnnDevice)
    {
        if (name.Equals(Windows, StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsOcrEngine();
        }

        if (name.Equals(Paddle, StringComparison.OrdinalIgnoreCase))
        {
            return new PaddleOcrEngine(paddleModel, threads, device);
        }

        throw new ArgumentException($"未知 OCR 引擎「{name}」，可用：{Windows} / {Paddle}");
    }
}

/// <summary>Windows.Media.Ocr（WinRT）—— 生产基线，零外部依赖</summary>
internal sealed class WindowsOcrEngine : IOcrEngine
{
    /// <summary>优先使用的识别语言（微信界面为简体中文）</summary>
    private const string PreferredLanguage = "zh-Hans-CN";

    private Windows.Media.Ocr.OcrEngine? _engine;

    public string Name => OcrEngines.Windows;

    public string Describe()
    {
        var languages = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages
            .Select(language => language.LanguageTag)
            .ToArray();

        if (languages.Length == 0)
        {
            throw new InvalidOperationException("系统未安装任何 OCR 识别语言包");
        }

        return languages.Contains(PreferredLanguage)
            ? $"Windows.Media.Ocr（{PreferredLanguage}）"
            : $"Windows.Media.Ocr（用户默认，可用：{string.Join(", ", languages)}）";
    }

    public string Recognize(BitmapSource prepared)
    {
        var width = prepared.PixelWidth;
        var height = prepared.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        prepared.CopyPixels(pixels, stride, 0);

        // WinRT 异步调用放到线程池（MTA）执行，避免在 STA 主线程上阻塞等待
        return Task.Run(() =>
        {
            var buffer = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(pixels);
            using var bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                buffer,
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                width,
                height,
                Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);

            var engine = _engine ??= Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(
                                        new Windows.Globalization.Language(PreferredLanguage))
                                    ?? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                                    ?? throw new InvalidOperationException("系统未安装可用的 OCR 识别语言包");

            var result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
            return result.Text ?? string.Empty;
        }).GetAwaiter().GetResult();
    }

    public void Dispose() => _engine = null;
}

/// <summary>
/// PaddleOCR（PP-OCRv5 中文模型，Paddle Inference + Mkldnn，CPU）。
/// 与 Windows.Media.Ocr 的关键差异：
///   1. 自带文本检测（DBNet），会先框出文字行再做识别，不依赖整图单行假设；
///   2. 输入为 OpenCV Mat，本类直接由 Bgra32 像素构造，不经过 PNG 编解码。
///
/// 两档模型：
///   server —— 随 NuGet 包分发（<see cref="LocalFullModels.ChineseV5"/>），精度优先；
///   mobile —— PP-OCRv5 移动版（det 4.7MB + rec 16MB），速度优先，模型放 models\paddle-ppocrv5-mobile。
/// </summary>
internal sealed class PaddleOcrEngine : IOcrEngine
{
    /// <summary>PP-OCRv5 服务器版（精度优先，随 NuGet 包分发）</summary>
    public const string Server = "server";

    /// <summary>PP-OCRv5 移动版（速度优先，模型位于 models\paddle-ppocrv5-mobile）</summary>
    public const string Mobile = "mobile";

    /// <summary>mobile 模型的相对存放位置（相对可执行文件目录）</summary>
    private const string MobileModelRoot = @"models\paddle-ppocrv5-mobile";

    /// <summary>oneDNN（MKL-DNN）后端</summary>
    public const string MkldnnDevice = "mkldnn";

    /// <summary>纯 CPU BLAS 后端（无 oneDNN 图优化，用于排查 oneDNN 开销）</summary>
    public const string BlasDevice = "blas";

    private readonly PaddleOcrAll _all;
    private readonly string _modelKey;
    private readonly int _threads;
    private readonly string _device;

    public PaddleOcrEngine(string modelKey = Server, int cpuMathThreadCount = 0, string device = MkldnnDevice)
    {
        _modelKey = modelKey;
        _threads = cpuMathThreadCount;
        _device = device;
        FullOcrModel model = Resolve(modelKey);

        Action<PaddleConfig> configure = device.Equals(BlasDevice, StringComparison.OrdinalIgnoreCase)
            ? PaddleDevice.Blas(cpuMathThreadCount)
            : PaddleDevice.Mkldnn(cpuMathThreadCount: cpuMathThreadCount);

        _all = new PaddleOcrAll(model, configure)
        {
            // 标题栏是水平且正向的文字：关掉这两条分支既提速，也避免角度误判
            AllowRotateDetection = false,
            Enable180Classification = false,
        };

        // 标题条又扁又长（放大后约 1100~2300 px 宽），检测默认按长边 960 缩图会丢掉放大后的字形细节；
        // 这里禁用缩图，让检测阶段直接看到放大后的像素。
        _all.Detector.MaxSize = null;
    }

    public string Name => OcrEngines.Paddle;

    public string Describe() => _modelKey.Equals(Mobile, StringComparison.OrdinalIgnoreCase)
        ? $"PaddleOCR PP-OCRv5 mobile（mobile det+rec，{_device}，检测不缩图，线程 {ThreadText(_threads)}）"
        : $"PaddleOCR PP-OCRv5（ChineseV5 det+rec，{_device}，检测不缩图，线程 {ThreadText(_threads)}）";

    private static string ThreadText(int threads) => threads == 0 ? "auto" : threads.ToString();

    /// <summary>仅供性能剖析使用：暴露底层实例以便单独给检测/识别阶段计时</summary>
    internal PaddleOcrAll Inner => _all;

    /// <summary>把模型档位解析为可加载的全模型；mobile 从磁盘目录加载，缺失时抛出可读错误</summary>
    private static FullOcrModel Resolve(string modelKey)
    {
        if (modelKey.Equals(Server, StringComparison.OrdinalIgnoreCase))
        {
            return LocalFullModels.ChineseV5;
        }

        if (!modelKey.Equals(Mobile, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"未知 PaddleOCR 模型档位「{modelKey}」，可用：{Server} / {Mobile}");
        }

        var root = Path.Combine(AppContext.BaseDirectory, MobileModelRoot);
        var detDir = Path.Combine(root, "det");
        var recDir = Path.Combine(root, "rec");
        if (!Directory.Exists(detDir) || !Directory.Exists(recDir))
        {
            throw new DirectoryNotFoundException(
                $"未找到 PP-OCRv5 mobile 模型目录：{root}（需含 det / rec 两个子目录，各放 inference.json、inference.pdiparams、inference.yml）");
        }

        return new FullOcrModel(
            DetectionModel.FromDirectory(detDir, ModelVersion.V5),
            RecognizationModel.FromDirectoryV5(recDir));
    }

    public string Recognize(BitmapSource prepared)
    {
        // Bgra32 的字节序即 OpenCV 的 BGRA；识别只吃 3 通道，去掉 alpha
        using var bgra = ToBgra32(prepared);
        using var bgr = bgra.CvtColor(ColorConversionCodes.BGRA2BGR);
        return _all.Run(bgr).Text ?? string.Empty;
    }

    /// <summary>把预处理后的位图转成 OpenCV 的 BGRA Mat（包装托管数组，不复制像素）</summary>
    internal static Mat ToBgra32(BitmapSource prepared)
    {
        var width = prepared.PixelWidth;
        var height = prepared.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        prepared.CopyPixels(pixels, stride, 0);
        return Mat.FromPixelData(height, width, MatType.CV_8UC4, pixels);
    }

    public void Dispose() => _all.Dispose();
}
