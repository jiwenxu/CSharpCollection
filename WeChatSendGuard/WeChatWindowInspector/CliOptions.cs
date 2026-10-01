using System.IO;

namespace WeChatWindowInspector;

/// <summary>命令行参数</summary>
internal sealed class CliOptions
{
    public bool Once { get; private set; }

    public bool IncludeAllProcesses { get; private set; }

    public bool ShowHelp { get; private set; }

    public bool IntervalSpecified { get; private set; }

    public int IntervalMs { get; private set; } = 300;

    public string LogPath { get; private set; } = string.Empty;

    /// <summary>启用 UI Automation 探测模式</summary>
    public bool UiaMode { get; private set; }

    /// <summary>UIA 模式下持续监视（事件 + 轮询），而非一次性导出树</summary>
    public bool Watch { get; private set; }

    /// <summary>UIA 树遍历是否使用 Raw 视图（默认 Control 视图）</summary>
    public bool RawView { get; private set; }

    /// <summary>UIA 树遍历最大深度</summary>
    public int MaxDepth { get; private set; } = 8;

    /// <summary>截图模式：抓取渲染子窗口并退出（对应 Spike-05）</summary>
    public bool ShotMode { get; private set; }

    /// <summary>截图改用屏幕 BitBlt（默认 PrintWindow）</summary>
    public bool UseScreenCapture { get; private set; }

    /// <summary>在整图上叠加坐标刻度网格</summary>
    public bool OverlayGrid { get; private set; }

    /// <summary>按相对渲染子窗口的偏移裁剪标题区</summary>
    public RegionSpec? Region { get; private set; }

    /// <summary>人工标注的场景标签（ground truth），形如 "技术交流群/群"</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>截图输出目录（默认 logs/spike05-<时间戳>）</summary>
    public string OutputDir { get; private set; } = string.Empty;

    /// <summary>Phase B OCR 探针模式</summary>
    public bool OcrMode { get; private set; }

    /// <summary>OCR 探针的采集目录（读取其中的 *-title.png 与同名 .json）</summary>
    public string OcrDir { get; private set; } = string.Empty;

    /// <summary>OCR 前放大倍数（Spike-05 实测 5x 最优：3x=40%、5x=60%、6x=30%）</summary>
    public int OcrScale { get; private set; } = 5;

    /// <summary>风险名单（逗号分隔），用于演示"包含即提醒"的匹配方式</summary>
    public string Names { get; private set; } = string.Empty;

    /// <summary>把各预处理变体导出为 PNG 便于肉眼核对（默认不导出）</summary>
    public string OcrDumpDir { get; private set; } = string.Empty;

    /// <summary>
    /// OCR 裁剪区覆盖（物理像素）：指定后改为从 `-full.png` 按该区域重裁剪，
    /// 用于验证 DPI 变化时标题区偏移的缩放规则（默认用已裁剪好的 `-title.png`）
    /// </summary>
    public RegionSpec? OcrRegion { get; private set; }

    /// <summary>
    /// 把 <see cref="OcrRegion"/> 视为 96 DPI 基准：区域按 `dpi/96` 放大、<see cref="OcrScale"/> 按 `dpi/96` 缩小，
    /// 使"等效放大 = scale × dpi/96"在不同 DPI 下恒定。
    /// 用于验证"裁剪区偏移与放大倍数均需随 DPI 归一化"这条修复规则。
    /// </summary>
    public bool OcrRegionScaleByDpi { get; private set; }

    /// <summary>Phase C：OCR Benchmark 模式（自动遍历 Scale × 预处理 × 阈值）</summary>
    public bool OcrBenchMode { get; private set; }

    /// <summary>Benchmark 的 Scale 轴（默认 3,4,5,6；Spike-05 实测 5x 最优，此处扫周边）</summary>
    public int[] BenchScales { get; private set; } = [3, 4, 5, 6];

    /// <summary>Benchmark 的固定阈值轴（仅用于展开 fixed-T 变体；invert / invert+stretch 不含阈值）</summary>
    public int[] BenchThresholds { get; private set; } = [140, 180];

    /// <summary>Benchmark 结果 CSV 输出路径（默认写入采集目录）</summary>
    public string BenchCsv { get; private set; } = string.Empty;

    /// <summary>
    /// Benchmark 变体子集（逗号列表，空 = 全部变体）。
    /// 用于消融对比：例如只跑现生产口径的 raw,stretch,otsu，或只跑深色主题的 invert,invert+stretch。
    /// </summary>
    public string BenchVariants { get; private set; } = string.Empty;

    /// <summary>Benchmark 使用的 OCR 引擎：windows（生产基线）/ paddle（PaddleOCR PP-OCRv5）</summary>
    public string BenchEngine { get; private set; } = OcrEngines.Windows;

    /// <summary>
    /// PaddleOCR 模型档位：server（随 NuGet 分发的 PP-OCRv5 mobile 中文模型）/ mobile（从磁盘加载的同一套模型）；
    /// 仅 --bench-engine paddle 时生效。两者 SHA256 一致，保留两档只为验证加载路径。
    /// </summary>
    public string BenchPaddleModel { get; private set; } = PaddleOcrEngine.Server;

    /// <summary>PaddleOCR 推理线程数（0 = 交给 Paddle 决定）；仅 --bench-engine paddle 时生效</summary>
    public int BenchThreads { get; private set; }

    /// <summary>PaddleOCR 推理后端：mkldnn（默认，oneDNN）/ blas（纯 CPU 数学库）；仅 --bench-engine paddle 时生效</summary>
    public string BenchDevice { get; private set; } = "mkldnn";

    /// <summary>Phase C 性能剖析模式（冷启动 vs 热态 + 阶段拆分）</summary>
    public bool BenchProfileMode { get; private set; }

    /// <summary>性能剖析：每 Scale 的预热次数（不计入统计）</summary>
    public int BenchWarmup { get; private set; } = 5;

    /// <summary>性能剖析：每 Scale 的测量次数</summary>
    public int BenchIterations { get; private set; } = 20;

    /// <summary>性能剖析：使用的样本数（轮流喂入，模拟会话切换）</summary>
    public int BenchSamples { get; private set; } = 1;

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions
        {
            LogPath = Path.Combine("logs", $"inspector-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"),
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLowerInvariant();

            switch (arg)
            {
                case "--once":
                case "-1":
                    options.Once = true;
                    break;

                case "--all":
                case "-a":
                    options.IncludeAllProcesses = true;
                    break;

                case "--interval":
                case "-i":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var interval) && interval >= 50)
                    {
                        options.IntervalMs = interval;
                        options.IntervalSpecified = true;
                    }
                    break;

                case "--log":
                case "-l":
                    if (i + 1 < args.Length)
                    {
                        options.LogPath = args[++i];
                    }
                    break;

                case "--uia":
                    options.UiaMode = true;
                    break;

                case "--watch":
                case "-w":
                    options.Watch = true;
                    break;

                case "--raw":
                    options.RawView = true;
                    break;

                case "--depth":
                case "-d":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var depth) && depth >= 1 && depth <= 40)
                    {
                        options.MaxDepth = depth;
                    }
                    break;

                case "--shot":
                    options.ShotMode = true;
                    break;

                case "--screen":
                    options.UseScreenCapture = true;
                    break;

                case "--grid":
                    options.OverlayGrid = true;
                    break;

                case "--region":
                    if (i + 1 < args.Length && RegionSpec.TryParse(args[++i], out var region))
                    {
                        options.Region = region;
                    }
                    else
                    {
                        Console.WriteLine("[警告] --region 参数无效，应为 x,y,w,h（相对渲染子窗口的物理像素）。");
                    }
                    break;

                case "--label":
                    if (i + 1 < args.Length)
                    {
                        options.Label = args[++i];
                    }
                    break;

                case "--out":
                case "-o":
                    if (i + 1 < args.Length)
                    {
                        options.OutputDir = args[++i];
                    }
                    break;

                case "--ocr":
                    options.OcrMode = true;
                    break;

                case "--ocr-dir":
                    if (i + 1 < args.Length)
                    {
                        options.OcrDir = args[++i];
                    }
                    break;

                case "--ocr-scale":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var scale) && scale >= 1 && scale <= 8)
                    {
                        options.OcrScale = scale;
                    }
                    break;

                case "--names":
                    if (i + 1 < args.Length)
                    {
                        options.Names = args[++i];
                    }
                    break;

                case "--ocr-dump":
                    if (i + 1 < args.Length)
                    {
                        options.OcrDumpDir = args[++i];
                    }
                    break;

                case "--ocr-region":
                    if (i + 1 < args.Length && RegionSpec.TryParse(args[++i], out var ocrRegion))
                    {
                        options.OcrRegion = ocrRegion;
                    }
                    else
                    {
                        Console.WriteLine("[警告] --ocr-region 参数无效，应为 x,y,w,h（相对渲染子窗口的物理像素）。");
                    }
                    break;

                case "--ocr-region-dpi96":
                    options.OcrRegionScaleByDpi = true;
                    break;

                case "--ocr-bench":
                    options.OcrBenchMode = true;
                    break;

                case "--bench-scales":
                    if (i + 1 < args.Length && TryParseIntList(args[++i], 1, 8, out var benchScales))
                    {
                        options.BenchScales = benchScales;
                    }
                    else
                    {
                        Console.WriteLine("[警告] --bench-scales 无效，应为 1~8 的逗号列表，如 3,4,5,6。");
                    }
                    break;

                case "--bench-thresholds":
                    if (i + 1 < args.Length && TryParseIntList(args[++i], 1, 254, out var benchThresholds))
                    {
                        options.BenchThresholds = benchThresholds;
                    }
                    else
                    {
                        Console.WriteLine("[警告] --bench-thresholds 无效，应为 1~254 的逗号列表，如 140,180。");
                    }
                    break;

                case "--bench-csv":
                    if (i + 1 < args.Length)
                    {
                        options.BenchCsv = args[++i];
                    }
                    break;

                case "--bench-variants":
                    if (i + 1 < args.Length)
                    {
                        options.BenchVariants = args[++i];
                    }
                    break;

                case "--bench-engine":
                    if (i + 1 < args.Length)
                    {
                        options.BenchEngine = args[++i];
                    }
                    break;

                case "--bench-paddle-model":
                    if (i + 1 < args.Length)
                    {
                        options.BenchPaddleModel = args[++i];
                    }
                    break;

                case "--bench-threads":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var threads) && threads >= 0 && threads <= 64)
                    {
                        options.BenchThreads = threads;
                    }
                    break;

                case "--bench-device":
                    if (i + 1 < args.Length)
                    {
                        options.BenchDevice = args[++i];
                    }
                    break;

                case "--bench-profile":
                    options.BenchProfileMode = true;
                    break;

                case "--bench-warmup":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var warmup) && warmup >= 0 && warmup <= 200)
                    {
                        options.BenchWarmup = warmup;
                    }
                    break;

                case "--bench-iterations":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var iterations) && iterations >= 1 && iterations <= 2000)
                    {
                        options.BenchIterations = iterations;
                    }
                    break;

                case "--bench-samples":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var benchSamples) && benchSamples >= 1 && benchSamples <= 500)
                    {
                        options.BenchSamples = benchSamples;
                    }
                    break;

                case "--help":
                case "-h":
                case "-?":
                    options.ShowHelp = true;
                    break;
            }
        }

        return options;
    }

    /// <summary>解析逗号分隔的整数列表（如 "3,4,5,6"），并要求每项落在 [min, max] 且不重复</summary>
    private static bool TryParseIntList(string text, int min, int max, out int[] values)
    {
        values = [];

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var parsed = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var value) || value < min || value > max || parsed.Contains(value))
            {
                return false;
            }

            parsed.Add(value);
        }

        values = parsed.ToArray();
        return true;
    }
}

/// <summary>相对渲染子窗口的裁剪区域（物理像素），格式：x,y,w,h</summary>
internal sealed record RegionSpec(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"{X},{Y},{Width},{Height}";

    public static bool TryParse(string text, out RegionSpec region)
    {
        region = null!;

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var x) ||
            !int.TryParse(parts[1], out var y) ||
            !int.TryParse(parts[2], out var width) ||
            !int.TryParse(parts[3], out var height))
        {
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        region = new RegionSpec(x, y, width, height);
        return true;
    }
}
