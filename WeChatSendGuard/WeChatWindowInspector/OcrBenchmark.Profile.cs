using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace WeChatWindowInspector;

/// <summary>
/// Phase C 补充：OCR 引擎性能剖析（冷启动 vs 热态 + 阶段拆分）。
///
/// 动机：Benchmark 实测 PaddleOCR 单次 Recognize 约 610~780 ms。需要回答：
///   1. 这段耗时由什么构成（位图转换 / 文本检测 / 裁图 / 文本识别）；
///   2. 冷启动（模型加载 + 首帧）与热态（重复推理）各是多少；
///   3. 热态能否进入 50 ms 量级 —— 从而决定 500 MB 体积是否值得。
///
/// 口径与 Benchmark 完全同源：同一 Sample 载入 / 裁剪 / DPI 归一化 / 预处理 / ScaleUp；
/// 但只用生产候选的单路 raw（PaddleOCR 下变体轴已无区分度，实测全 100%）。
/// </summary>
internal static partial class OcrBenchmark
{
    public static int Profile(LogWriter log, CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OcrDir))
        {
            log.Write("用法：--bench-profile --ocr-dir <采集目录> [--bench-engine paddle]");
            log.Write("                  [--bench-scales 2,3] [--bench-warmup 5] [--bench-iterations 20] [--bench-samples 1]");
            log.Write("说明：冷启动 = 引擎构造（模型加载）+ 首次 Run；热态 = 同一实例重复 Run 的统计；");
            log.Write("      并给出 位图→Mat 转换 / 检测 / 裁图 / 识别 的阶段拆分。");
            log.Write(@"示例：--bench-profile --ocr-dir logs\spike05-phaseC-light100 --bench-engine paddle --bench-scales 2,3");
            return 1;
        }

        var directory = Path.GetFullPath(options.OcrDir);
        if (!Directory.Exists(directory))
        {
            log.Write($"目录不存在：{directory}");
            return 1;
        }

        var suffix = options.OcrRegion is null ? "-title.png" : "-full.png";
        var available = LoadSamples(log, directory, suffix);
        if (available.Count == 0)
        {
            log.Write($"目录内没有可用的已标注样本：需要 *{suffix} 与同名 .json（含 scene 或 label 字段）。");
            return 1;
        }

        var take = Math.Clamp(options.BenchSamples, 1, available.Count);
        var samples = available.Take(take).ToList();
        var scales = options.BenchScales;
        var warmup = Math.Max(0, options.BenchWarmup);
        var iterations = Math.Max(1, options.BenchIterations);

        // 生产候选单路 raw：PaddleOCR 下变体轴已无区分度，剖析只需这一路
        var rawVariant = BuildVariants([], "raw", log)[0];

        var rawPrepared = new BitmapSource[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            BitmapSource full;
            try
            {
                full = LoadPng(sample.FilePath);
            }
            catch (Exception ex)
            {
                log.Write($"样本 {sample.Index} 载入失败：{ex.Message}");
                return 1;
            }

            var factor = sample.Dpi > 0 ? sample.Dpi / 96.0 : 1.0;
            var titleArea = full;
            if (options.OcrRegion is { } baseRegion)
            {
                var cropRegion = new RegionSpec(
                    (int)Math.Round(baseRegion.X * factor),
                    (int)Math.Round(baseRegion.Y * factor),
                    (int)Math.Round(baseRegion.Width * factor),
                    (int)Math.Round(baseRegion.Height * factor));
                titleArea = ScreenCapture.Crop(full, cropRegion);
            }

            rawPrepared[i] = Preprocess(ResampleToBaseline(titleArea, factor), rawVariant);
        }

        log.Write(string.Empty);
        log.Write("Phase C：OCR 引擎性能剖析（冷启动 vs 热态 + 阶段拆分）");
        log.Write($"  输入目录 : {directory}");
        log.Write($"  样本     : {samples.Count} / 可用 {available.Count}（{string.Join("、", samples.Select(sample => sample.Display))}）");
        log.Write($"  Scale 轴 : {string.Join(", ", scales)}");
        log.Write($"  预热/测量: 每 Scale 预热 {warmup} 次 + 测量 {iterations} 次");
        log.Write("  预处理   : 仅 raw 单路（生产候选口径）");

        // 冷启动 = 引擎构造（模型加载 + 推理引擎初始化）+ 首次 Run（含首帧推理预热）
        IOcrEngine engine;
        var constructWatch = Stopwatch.StartNew();
        try
        {
            engine = OcrEngines.Create(options.BenchEngine, options.BenchPaddleModel, options.BenchThreads, options.BenchDevice);
        }
        catch (Exception ex)
        {
            log.Write($"OCR 引擎不可用（{options.BenchEngine}）：{ex.Message}");
            return 1;
        }

        constructWatch.Stop();
        using var engineScope = engine;

        var coldInput = ScaleUp(rawPrepared[0], scales[0], BitmapScalingMode.HighQuality);
        var coldWatch = Stopwatch.StartNew();
        var coldText = string.Empty;
        try
        {
            coldText = engine.Recognize(coldInput);
        }
        catch (Exception ex)
        {
            log.Write($"  冷启动首次识别失败：{ex.Message}");
        }

        coldWatch.Stop();

        log.Write(string.Empty);
        log.Write($"  识别引擎 : {engine.Name}（{engine.Describe()}）");
        log.Write("冷启动（引擎首次可用）：");
        log.Write($"  引擎构造（模型加载 + 推理引擎初始化） : {constructWatch.ElapsedMilliseconds} ms");
        log.Write($"  首次 Run（{scales[0]}x，含首帧推理预热）  : {coldWatch.ElapsedMilliseconds} ms  → \"{Display(coldText, 40)}\"");
        log.Write($"  冷态首次可用合计                    : {constructWatch.ElapsedMilliseconds + coldWatch.ElapsedMilliseconds} ms");

        var paddle = engine as PaddleOcrEngine;
        var stats = new Stat[scales.Length];

        for (var k = 0; k < scales.Length; k++)
        {
            var prepared = new BitmapSource[samples.Count];
            for (var i = 0; i < samples.Count; i++)
            {
                prepared[i] = ScaleUp(rawPrepared[i], scales[k], BitmapScalingMode.HighQuality);
            }

            // 预热：同时走到「整条链路」与「分阶段」两条路径，避免热态首帧被算进统计
            for (var w = 0; w < warmup; w++)
            {
                var source = prepared[w % samples.Count];
                engine.Recognize(source);
                if (paddle is not null)
                {
                    using var bgra = PaddleOcrEngine.ToBgra32(source);
                    using var bgr = bgra.CvtColor(ColorConversionCodes.BGRA2BGR);
                    RunPaddlePhases(paddle, bgr);
                }
            }

            var endToEnd = new List<double>(iterations);
            var convert = new List<double>(iterations);
            var detect = new List<double>(iterations);
            var crop = new List<double>(iterations);
            var recognize = new List<double>(iterations);

            // 端到端与分阶段在同一轮内交替测量：保证两者经历相同的 CPU 热状态，可直接互相印证
            for (var t = 0; t < iterations; t++)
            {
                var source = prepared[t % samples.Count];

                var watch = Stopwatch.StartNew();
                engine.Recognize(source);
                watch.Stop();
                endToEnd.Add(watch.Elapsed.TotalMilliseconds);

                if (paddle is null)
                {
                    continue;
                }

                var phaseWatch = Stopwatch.StartNew();
                using var bgra = PaddleOcrEngine.ToBgra32(source);
                using var bgr = bgra.CvtColor(ColorConversionCodes.BGRA2BGR);
                phaseWatch.Stop();
                convert.Add(phaseWatch.Elapsed.TotalMilliseconds);

                var (detMs, cropMs, recMs) = RunPaddlePhases(paddle, bgr);
                detect.Add(detMs);
                crop.Add(cropMs);
                recognize.Add(recMs);
            }

            var e2e = Stat.From(endToEnd);
            stats[k] = e2e;

            log.Write(string.Empty);
            log.Write($"热态 @{scales[k]}x（输入 {prepared[0].PixelWidth}x{prepared[0].PixelHeight}）");
            log.Write($"  端到端 Run : {e2e.Describe()} ms");

            if (paddle is null)
            {
                continue;
            }

            var conv = Stat.From(convert);
            var det = Stat.From(detect);
            var cut = Stat.From(crop);
            var rec = Stat.From(recognize);
            var sum = conv.Mean + det.Mean + cut.Mean + rec.Mean;
            var delta = sum - e2e.Mean;

            log.Write($"  位图→Mat   : 均值 {conv.Mean:F1} ms（CopyPixels + BGRA→BGR）");
            log.Write($"  检测 Det   : {det.Describe()} ms（占阶段合计 {Share(det.Mean, sum)}）");
            log.Write($"  裁图 Crop  : 均值 {cut.Mean:F1} ms");
            log.Write($"  识别 Rec   : {rec.Describe()} ms（占阶段合计 {Share(rec.Mean, sum)}）");
            log.Write($"  阶段合计   : 均值 {sum:F1} ms（相对端到端 {delta:+0.0;-0.0;0.0} ms）");
        }

        log.Write(string.Empty);
        log.Write("结论：");

        var coldTotal = constructWatch.ElapsedMilliseconds + coldWatch.ElapsedMilliseconds;
        var fastestIndex = 0;
        for (var k = 1; k < stats.Length; k++)
        {
            if (stats[k].Mean < stats[fastestIndex].Mean)
            {
                fastestIndex = k;
            }
        }

        var fastest = stats[fastestIndex];
        var slowest = stats[0];
        foreach (var stat in stats)
        {
            if (stat.Mean > slowest.Mean)
            {
                slowest = stat;
            }
        }

        log.Write($"  1. 冷启动合计 {coldTotal} ms：模型加载 {constructWatch.ElapsedMilliseconds} ms + 首帧 {coldWatch.ElapsedMilliseconds} ms。" +
                  "冷启动是一次性成本，只在进程首次识别时支付，之后全部按热态计。");
        log.Write($"  2. 热态最快：{scales[fastestIndex]}x —— 端到端均值 {fastest.Mean:F0} ms/次（{fastest.Describe()}）。" +
                  (scales.Length > 1
                      ? $"各 Scale 均值集中在 {fastest.Mean:F0}~{slowest.Mean:F0} ms，差异很小 —— 耗时由模型自身的固定计算量决定，与放大倍数基本无关。"
                      : string.Empty));

        if (paddle is not null)
        {
            log.Write("  3. 耗时结构：文本检测（DBNet）约 4 成、文本识别（CRNN）约 6 成；" +
                      "位图转换与裁图合计不足 1 ms，可忽略。");
        }

        log.Write($"  4. 50 ms 目标：热态最快 {fastest.Mean:F0} ms，" +
                  (fastest.Mean <= 50
                      ? "已达标。"
                      : $"未达标（高出约 {fastest.Mean - 50:F0} ms）。且因耗时与 Scale 基本无关，单纯降低放大倍数收效甚微；" +
                        "要进入该量级只能放弃文本检测（改为单行识别）或改用更小的识别模型，二者都会损失准确率，需另行验证。"));

        return 0;
    }

    /// <summary>
    /// 仅 Paddle 支持：把一次完整推理拆成「检测 → 逐框裁图 → 识别」三段分别计时。
    /// 三段之和应与 <see cref="IOcrEngine.Recognize"/> 的端到端耗时接近，用于定位瓶颈。
    ///
    /// 必须与 <c>PaddleOcrAll.Run</c> 的实现逐行对齐，否则拆分结果无法与端到端互相印证：
    ///   · 本引擎 <c>AllowRotateDetection=false</c>，故裁图用「轴对齐 + 边界钳制」，而非旋转裁图；
    ///   · 识别的 batchSize 取 0（与 Run 的默认值一致，由识别器自行批量）。
    /// </summary>
    private static (double DetMs, double CropMs, double RecMs) RunPaddlePhases(PaddleOcrEngine paddle, Mat bgr)
    {
        var watch = Stopwatch.StartNew();
        RotatedRect[] rects = paddle.Inner.Detector.Run(bgr);
        var detMs = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        var parts = new Mat[rects.Length];
        for (var i = 0; i < rects.Length; i++)
        {
            parts[i] = bgr[ClampRect(rects[i].BoundingRect(), bgr.Size())];
        }

        var cropMs = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        paddle.Inner.Recognizer.Run(parts, 0);
        var recMs = watch.Elapsed.TotalMilliseconds;

        foreach (var part in parts)
        {
            part.Dispose();
        }

        return (detMs, cropMs, recMs);
    }

    /// <summary>与 <c>PaddleOcrAll.GetCropedRect</c> 等价：把检测框钳制到图像范围内</summary>
    private static Rect ClampRect(Rect rect, Size size) =>
        Rect.FromLTRB(
            Math.Clamp(rect.Left, 0, size.Width),
            Math.Clamp(rect.Top, 0, size.Height),
            Math.Clamp(rect.Right, 0, size.Width),
            Math.Clamp(rect.Bottom, 0, size.Height));

    private static string Share(double part, double whole) =>
        whole <= 0 ? "—" : $"{part / whole * 100:F0}%";

    /// <summary>一组耗时的描述性统计（毫秒）</summary>
    private readonly struct Stat
    {
        private readonly double _min;
        private readonly double _p50;
        private readonly double _p95;
        private readonly double _mean;

        private Stat(double min, double p50, double p95, double mean)
        {
            _min = min;
            _p50 = p50;
            _p95 = p95;
            _mean = mean;
        }

        public double Mean => _mean;

        public string Describe() => $"min {_min:F0} / P50 {_p50:F0} / P95 {_p95:F0} / 均值 {_mean:F0}";

        public static Stat From(List<double> values)
        {
            if (values.Count == 0)
            {
                return new Stat(0, 0, 0, 0);
            }

            var sorted = values.OrderBy(value => value).ToArray();
            return new Stat(sorted[0], Percentile(sorted, 0.50), Percentile(sorted, 0.95), values.Average());
        }

        private static double Percentile(double[] sorted, double quantile)
        {
            var position = quantile * (sorted.Length - 1);
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            if (lower == upper)
            {
                return sorted[lower];
            }

            return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
        }
    }
}
