using System.Runtime.InteropServices;
using System.Text;

namespace WeChatWindowInspector;

/// <summary>
/// 微信窗口侦察工具 —— 对应需求文档 v1.1 第 7.1 节 Spike-01。
/// 目的：回答"到底哪个窗口代表当前聊天、标题能否稳定拿到聊天对象名称"。
/// </summary>
internal static class Program
{
    private const string Title = "WeChat Window Inspector (Spike-01/05)";
    private const int LineWidth = 110;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // 某些宿主不支持设置输出编码，忽略
        }

        var options = CliOptions.Parse(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        // 尽早声明 DPI 感知，否则获取到的坐标会被系统虚拟化
        NativeMethods.EnablePerMonitorDpiAwareness();

        using var log = new LogWriter(options.LogPath);
        var scanner = new WeChatWindowScanner();

        PrintHeader(log, options);

        if (options.BenchProfileMode)
        {
            return OcrBenchmark.Profile(log, options);
        }

        if (options.OcrBenchMode)
        {
            return OcrBenchmark.Run(log, options);
        }

        if (options.OcrMode)
        {
            return OcrProbe.Run(log, options);
        }

        if (options.ShotMode)
        {
            return ShotRunner.Run(scanner, log, options);
        }

        if (options.UiaMode)
        {
            return UiaProbe.Run(scanner, log, options);
        }

        return options.Once
            ? RunOnce(scanner, log, options)
            : RunLive(scanner, log, options);
    }

    private static int RunOnce(WeChatWindowScanner scanner, LogWriter log, CliOptions options)
    {
        var windows = scanner.Capture(options.IncludeAllProcesses);
        PrintSnapshot(log, windows, NativeMethods.GetForegroundWindow());
        PrintHints(log);
        return 0;
    }

    private static int RunLive(WeChatWindowScanner scanner, LogWriter log, CliOptions options)
    {
        var state = new SessionState();
        var commands = ConsoleKeyWatcher.Start(() => state.Running);

        var previous = new Dictionary<IntPtr, WindowInfo>();
        var lastForeground = IntPtr.Zero;

        log.Write(string.Empty);
        log.Write($"开始实时监视（采样间隔 {options.IntervalMs}ms）。按 [S] 打印完整快照，按 [Q] 退出。");
        log.Write(new string('-', LineWidth));

        while (state.Running)
        {
            var current = new Dictionary<IntPtr, WindowInfo>();
            foreach (var window in scanner.Capture(options.IncludeAllProcesses))
            {
                current[window.Hwnd] = window;
            }

            ReportForeground(scanner, log, ref lastForeground);
            ReportDiff(previous, current, log);
            previous = current;

            while (commands.TryDequeue(out var key))
            {
                switch (key)
                {
                    case 'q':
                        state.Running = false;
                        break;

                    case 's':
                        PrintSnapshot(log, current.Values.ToList(), NativeMethods.GetForegroundWindow());
                        break;
                }
            }

            if (state.Running)
            {
                Thread.Sleep(options.IntervalMs);
            }
        }

        log.Write(string.Empty);
        log.Write($"监视结束。日志已保存到：{log.FilePath ?? "（未启用日志文件）"}");
        return 0;
    }

    private static void ReportForeground(WeChatWindowScanner scanner, LogWriter log, ref IntPtr lastForeground)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == lastForeground)
        {
            return;
        }

        lastForeground = foreground;
        if (foreground == IntPtr.Zero)
        {
            return;
        }

        var info = scanner.CaptureWindow(foreground);
        var affiliation = scanner.IsWeChatProcess(info.ProcessId) ? "微信" : "非微信";
        log.Write($"[{Stamp()}] > FG   {info.Describe()} [{affiliation} / {info.ProcessName}]");
    }

    private static void ReportDiff(
        IReadOnlyDictionary<IntPtr, WindowInfo> previous,
        IReadOnlyDictionary<IntPtr, WindowInfo> current,
        LogWriter log)
    {
        var newCount = 0;
        var goneCount = 0;
        var changedCount = 0;

        foreach (var (hwnd, info) in current)
        {
            if (!previous.TryGetValue(hwnd, out var old))
            {
                log.Write($"[{Stamp()}] + NEW  {info.Describe()}");
                newCount++;
                continue;
            }

            var changes = info.DiffFrom(old);
            if (changes.Count > 0)
            {
                log.Write($"[{Stamp()}] ~ CHG  {info.HwndText} [{info.ClassName}] | {string.Join(" | ", changes)}");
                changedCount++;
            }
        }

        foreach (var (hwnd, old) in previous)
        {
            if (!current.ContainsKey(hwnd))
            {
                log.Write($"[{Stamp()}] - GONE {old.HwndText} [{old.ClassName}] \"{old.Text}\"");
                goneCount++;
            }
        }

        if (newCount + goneCount + changedCount > 0)
        {
            log.Write($"[{Stamp()}] 本次变化：新增 {newCount}，消失 {goneCount}，变更 {changedCount}");
        }
    }

    private static void PrintSnapshot(LogWriter log, IReadOnlyList<WindowInfo> windows, IntPtr foreground)
    {
        log.Write(string.Empty);
        log.Write(new string('=', LineWidth));
        log.Write($"微信窗口快照  {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  共 {windows.Count} 个窗口");
        log.Write($"当前前台窗口：{DescribeForeground(windows, foreground)}");
        log.Write(new string('=', LineWidth));

        if (windows.Count == 0)
        {
            log.Write("未发现微信窗口。请确认微信已启动（支持 WeChat.exe / Weixin.exe）。");
            log.Write(string.Empty);
            return;
        }

        var index = 0;
        foreach (var window in windows
                     .OrderBy(w => w.Depth)
                     .ThenBy(w => w.Top)
                     .ThenBy(w => w.Left))
        {
            index++;
            log.Write($"-- [#{index:D2}] {window.HwndText}  {window.LevelText}{(window.IsForeground ? "  ★前台" : string.Empty)}");
            log.Write($"     ProcessId   : {window.ProcessId} ({window.ProcessName})");
            log.Write($"     ClassName   : {window.ClassName}");
            log.Write($"     WindowText  : \"{window.Text}\"");
            log.Write($"     Parent      : 0x{window.Parent.ToInt64():X8}");
            log.Write($"     Owner       : 0x{window.Owner.ToInt64():X8}");
            log.Write($"     Root        : 0x{window.Root.ToInt64():X8}");
            log.Write($"     Rect        : {window.RectText}");
            log.Write($"     State       : Visible={window.Visible} Enabled={window.Enabled} Minimized={window.Minimized} Maximized={window.Maximized}");
            log.Write($"     DPI         : {window.DpiText}");
            log.Write($"     Style       : 0x{window.Style:X8}  ExStyle=0x{window.ExStyle:X8}");
        }

        log.Write(string.Empty);
    }

    private static string DescribeForeground(IReadOnlyList<WindowInfo> windows, IntPtr foreground)
    {
        var match = windows.FirstOrDefault(w => w.Hwnd == foreground);
        return match is null
            ? $"0x{foreground.ToInt64():X8}（不在本次快照范围内）"
            : match.Describe();
    }

    private static void PrintHeader(LogWriter log, CliOptions options)
    {
        var systemDpi = NativeMethods.GetSystemDpi();

        string mode;
        if (options.BenchProfileMode)
        {
            mode = $"Phase C：OCR 性能剖析（冷启动 vs 热态，Scale {string.Join("/", options.BenchScales)}）";
        }
        else if (options.OcrBenchMode)
        {
            mode = $"Phase C：OCR Benchmark（Scale {string.Join("/", options.BenchScales)}，阈值 {string.Join("/", options.BenchThresholds)}）";
        }
        else if (options.OcrMode)
        {
            mode = $"Phase B：标题区 OCR 探针（放大 {options.OcrScale}x）";
        }
        else if (options.ShotMode)
        {
            mode = $"截图模式（{(options.Watch ? "连拍" : "单次")}，{(options.UseScreenCapture ? "屏幕 BitBlt" : "PrintWindow")}" +
                   $"{(options.OverlayGrid ? "，网格标注" : string.Empty)}" +
                   $"{(options.Region is not null ? "，裁剪标题区" : string.Empty)}）";
        }
        else if (options.UiaMode)
        {
            mode = $"UI Automation 探测（{(options.Watch ? "监视模式" : "树导出")}{(options.RawView ? "，Raw 视图" : string.Empty)}）";
        }
        else
        {
            mode = options.Once ? "单次快照" : $"实时监视（采样间隔 {options.IntervalMs}ms）";
        }

        var builder = new StringBuilder();
        builder.AppendLine(new string('=', LineWidth));
        builder.AppendLine($"{Title}  —— 微信窗口侦察（Spike-01）与标题区截图（Spike-05）");
        builder.AppendLine($"启动时间  : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"操作系统  : {RuntimeInformation.OSDescription} (Build {Environment.OSVersion.Version.Build})");
        builder.AppendLine($"运行环境  : .NET {Environment.Version}");
        builder.AppendLine($"DPI 感知  : 本进程={NativeMethods.GetProcessAwarenessText()}   系统 DPI={systemDpi} ({(systemDpi == 0 ? 0 : systemDpi * 100 / 96)}%)");
        builder.AppendLine($"运行模式  : {mode}{(options.IncludeAllProcesses ? "  含全部进程顶层窗口" : string.Empty)}");
        builder.AppendLine($"日志文件  : {log.FilePath ?? "（未启用）"}");
        builder.AppendLine(new string('=', LineWidth));

        log.Write(builder.ToString().TrimEnd());
    }

    private static void PrintHints(LogWriter log)
    {
        log.Write("下一步（Spike-01 验证场景）：依次执行下列操作，观察哪些窗口的 WindowText / Rect / Visible / Minimized 发生变化：");
        log.Write("  ① 微信主界面   ② 打开私聊   ③ 打开群聊   ④ 在私聊与群聊之间切换");
        log.Write("  ⑤ 拖动微信窗口   ⑥ 最大化微信   ⑦ 最小化微信   ⑧ 切换到 Chrome 等其他程序");
        log.Write("需要回答：");
        log.Write("  Q1 微信主窗口 HWND 与类名是什么？");
        log.Write("  Q2 当前聊天对象对应哪个 HWND？");
        log.Write("  Q3 WindowText 能否稳定获得聊天对象名称？");
        log.Write("  Q4 切换聊天时，哪个窗口的哪个属性发生变化？");
        log.Write("  Q5 群聊与私聊的窗口结构是否存在稳定差异？");
        log.Write(string.Empty);
        log.Write("提示：使用实时模式（默认）能把上述变化逐条记录下来，比单次快照更适合回答 Q4/Q5。");
    }

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss.fff");

    private static void PrintHelp()
    {
        Console.WriteLine($"{Title}");
        Console.WriteLine("微信窗口结构侦察工具：");
        Console.WriteLine("  默认模式：枚举微信进程相关的顶层窗口与全部子窗口，输出句柄、类名、标题、层级、矩形、状态、DPI 与样式。");
        Console.WriteLine("  --uia   ：UI Automation 探测，导出微信主窗口的自动化树，用于验证\"不注入前提下能否读取会话信息\"。");
        Console.WriteLine("  --shot  ：Spike-05 截图模式，抓取微信渲染子窗口并保存 PNG 与 sidecar，用于验证标题区视觉可观测性。");
        Console.WriteLine("  --ocr-bench：Phase C OCR Benchmark，对已采集样本自动遍历 Scale × 预处理 × 阈值 组合并对比结果。");
        Console.WriteLine("  --bench-profile：Phase C OCR 性能剖析，把单次识别耗时拆成「冷启动 / 热态 + 阶段」并统计。");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  WeChatWindowInspector [选项]");
        Console.WriteLine();
        Console.WriteLine("通用选项：");
        Console.WriteLine("  --log, -l PATH     指定日志文件路径（默认：logs/inspector-log-<时间戳>.txt）");
        Console.WriteLine("  --interval, -i N   采样/轮询间隔毫秒数，最小 50（窗口模式默认 300，UIA 监视默认 1000）");
        Console.WriteLine("  --help, -h         显示本帮助");
        Console.WriteLine();
        Console.WriteLine("窗口模式选项：");
        Console.WriteLine("  --once, -1         只抓取一次快照后退出（默认：实时监视）");
        Console.WriteLine("  --all, -a          同时列出所有进程的顶层窗口（用于对照前台窗口归属）");
        Console.WriteLine();
        Console.WriteLine("UIA 探测选项：");
        Console.WriteLine("  --uia              导出微信主窗口的 UI Automation 树");
        Console.WriteLine("  --uia --watch      进入 UIA 监视模式：事件通道 + 轮询差分通道并行记录");
        Console.WriteLine("  --depth, -d N      树的最大遍历深度（默认：8）");
        Console.WriteLine("  --raw              UIA 使用 Raw 视图（默认 Control 视图）");
        Console.WriteLine();
        Console.WriteLine("截图模式选项（Spike-05）：");
        Console.WriteLine("  --shot             抓取微信渲染子窗口（MMUIRenderSubWindowHW）并退出");
        Console.WriteLine("  --shot --watch     连拍模式：每输入一次标签回车即拍一张，自动编号并累积到同一目录，输入 q 结束");
        Console.WriteLine("  --screen           改用屏幕 BitBlt（默认 PrintWindow；屏幕方式需微信可见且未被遮挡）");
        Console.WriteLine("  --grid             在整图上叠加坐标刻度网格，便于目视标注标题区偏移");
        Console.WriteLine("  --region x,y,w,h   按相对渲染子窗口的偏移裁剪标题区，额外输出 <n>-title.png");
        Console.WriteLine("  --label TEXT       人工标注 ground truth，形如 \"技术交流群/群\"（/ 前为会话名，后为类型）");
        Console.WriteLine("  --out, -o DIR      输出目录（默认 logs/spike05-<时间戳>；指定同一目录可累积多场景并自动编号）");
        Console.WriteLine();
        Console.WriteLine("Phase B OCR 探针（Spike-05）：");
        Console.WriteLine("  --ocr              对采集目录内的 *-title.png 做本地 OCR（Windows.Media.Ocr，零外部依赖）");
        Console.WriteLine("  --ocr-dir DIR      采集目录，需含 *-title.png 与同名 .json（人工标注）");
        Console.WriteLine("  --ocr-scale N      OCR 前放大倍数，默认 5（实测 5x 优于 3x/6x）");
        Console.WriteLine("  --names \"a,b\"      风险名单演示：识别文本包含名单项即判定\"提醒\"");
        Console.WriteLine("  --ocr-dump DIR     把各预处理变体导出为 PNG，便于肉眼核对二值化效果");
        Console.WriteLine("  --ocr-region x,y,w,h  用物理像素覆盖裁剪区，改为从整图重裁剪（验证 DPI 缩放用）");
        Console.WriteLine("  --ocr-region-dpi96  把 --ocr-region 视为 96 DPI 基准：区域×dpi/96、放大÷dpi/96，使等效放大恒定");
        Console.WriteLine();
        Console.WriteLine("Phase C OCR Benchmark（自动遍历 Scale × 预处理 × 阈值 并对比结果）：");
        Console.WriteLine("  --ocr-bench        开启 Benchmark：对采集目录逐样本跑完所有组合，输出对比表、三态分布与 CSV");
        Console.WriteLine("  --ocr-dir DIR      采集目录（同 --ocr；含 *-title.png / *-full.png 与同名 .json 标注）");
        Console.WriteLine("  --bench-scales S    Scale 轴，逗号列表，默认 3,4,5,6（取值 1~8）");
        Console.WriteLine("  --bench-thresholds T 阈值轴，逗号列表，默认 140,180（取值 1~254；用于展开 fixed-T 变体）");
        Console.WriteLine("  --bench-csv PATH   结果 CSV 输出路径（默认 <采集目录>\\ocr-benchmark-<时间戳>.csv）");
        Console.WriteLine("  --bench-variants V 只跑指定变体（逗号列表，用于消融对比）。例如 raw,stretch,otsu 即现生产口径；");
        Console.WriteLine("                     可选名：raw / stretch / otsu / fixed-140 / fixed-180 / invert / invert+stretch");
        Console.WriteLine("  --bench-engine E   OCR 引擎，默认 windows（Windows.Media.Ocr，生产基线）；可选 paddle（PaddleOCR PP-OCRv5）");
        Console.WriteLine("                     两引擎共用同一套裁剪/归一化/变体/并集/统计口径，便于直接对比");
        Console.WriteLine("  --bench-paddle-model M  PaddleOCR 模型档位（仅 --bench-engine paddle 生效）：");
        Console.WriteLine("                     server（默认，随 NuGet 分发的 PP-OCRv5 mobile 中文 det+rec）");
        Console.WriteLine("                     mobile（从 models\\paddle-ppocrv5-mobile 加载，与 server 为同一套模型）");
        Console.WriteLine("  --bench-threads N  Paddle 推理线程数（0=auto，默认）；仅 --bench-engine paddle 生效");
        Console.WriteLine("  --bench-device D   Paddle 推理后端：mkldnn（默认，oneDNN）/ blas（纯 CPU 数学库，排查 oneDNN 开销）");
        Console.WriteLine("  --ocr-region x,y,w,h  96 DPI 基准裁剪区，本模式固定按 dpi/96 归一化（与生产一致）");
        Console.WriteLine("  --ocr-dump DIR     导出每样本每变体的基线预处理图，便于肉眼核对阈值效果");
        Console.WriteLine("  --names \"a,b\"      风险名单（匹配方式：标准化后包含即命中），用于统计 Risk / Unknown / Private 与漏报/误报");
        Console.WriteLine("  说明：变体轴固定为 raw、stretch、otsu、fixed-T（T 取自 --bench-thresholds）、invert、invert+stretch；");
        Console.WriteLine("        Scale 轴与变体轴会做笛卡尔积，逐组合统计命中率、字准、三态分布、用时并排名；");
        Console.WriteLine("        另输出「错字清单」（形近字混淆对），存在混淆时写成 <主 CSV 去扩展名>-confusions.csv。");
        Console.WriteLine();
        Console.WriteLine("Phase C OCR 性能剖析（冷启动 vs 热态 + 阶段拆分）：");
        Console.WriteLine("  --bench-profile    开启剖析：先测引擎构造（模型加载）与首次 Run，再测热态重复 Run，");
        Console.WriteLine("                     并（PaddleOCR）把每次推理拆成 位图→Mat / 检测 / 裁图 / 识别 四段计时。");
        Console.WriteLine("  --ocr-dir DIR      采集目录（取前 --bench-samples 个样本轮流喂入）");
        Console.WriteLine("  --bench-engine E   默认 windows；剖析阶段拆分仅 paddle 引擎支持（--bench-engine paddle）");
        Console.WriteLine("  --bench-paddle-model M  PaddleOCR 模型档位：server（默认）/ mobile（模型在 models\\paddle-ppocrv5-mobile，与 server 同一套模型）");
        Console.WriteLine("  --bench-threads N  Paddle 推理线程数（0=auto，默认）");
        Console.WriteLine("  --bench-device D   Paddle 推理后端：mkldnn（默认）/ blas");
        Console.WriteLine("  --bench-scales S   要剖析的 Scale，逗号列表，默认 3,4,5,6");
        Console.WriteLine("  --bench-warmup N   每 Scale 预热次数（不计入统计），默认 5");
        Console.WriteLine("  --bench-iterations N  每 Scale 测量次数，默认 20");
        Console.WriteLine("  --bench-samples N  使用的样本数，默认 1（>1 时轮流喂入，模拟会话切换）");
        Console.WriteLine("  说明：剖析只用生产候选单路 raw（PaddleOCR 下变体轴已无区分度）；输出 min/P50/P95/均值。");
        Console.WriteLine();
        Console.WriteLine("窗口模式快捷键：");
        Console.WriteLine("  S  打印一次完整快照     Q  退出");
        Console.WriteLine();
        Console.WriteLine("示例：");
        Console.WriteLine("  WeChatWindowInspector                       # 窗口实时监视");
        Console.WriteLine("  WeChatWindowInspector --once --all          # 单次快照，含全部进程");
        Console.WriteLine("  WeChatWindowInspector --uia                 # 导出 UIA 树");
        Console.WriteLine("  WeChatWindowInspector --uia --watch         # UIA 事件 + 轮询监视");
        Console.WriteLine("  WeChatWindowInspector --uia --raw --depth 12");
        Console.WriteLine("  WeChatWindowInspector --shot --grid --label \"技术交流群/群\"     # 带网格整图");
        Console.WriteLine("  WeChatWindowInspector --shot --region 600,20,900,40 --label \"技术交流群/群\"   # 裁剪标题区");
        Console.WriteLine("  WeChatWindowInspector --shot --screen --out logs/spike05      # 屏幕抓取并累积到同一目录");
        Console.WriteLine("  WeChatWindowInspector --shot --watch --region 300,33,580,30 --out logs/spike05   # 连拍采集场景矩阵");
        Console.WriteLine("  WeChatWindowInspector --ocr --ocr-dir logs/spike05 --names \"培立优BUG,徐二狗\"     # Phase B 批量 OCR + 命中判定");
        Console.WriteLine("  WeChatWindowInspector --ocr-bench --ocr-dir logs/spike05 --ocr-region 300,33,580,30 --names \"培立优BUG\"   # Phase C 扫描对比");
        Console.WriteLine("  WeChatWindowInspector --ocr-bench --ocr-dir logs/spike05 --bench-scales 4,5,6 --bench-thresholds 120,140,160,180");
        Console.WriteLine("  WeChatWindowInspector --ocr-bench --ocr-dir logs/spike05-phaseC --bench-variants raw,stretch,otsu   # 现生产口径消融");
        Console.WriteLine("  WeChatWindowInspector --ocr-bench --ocr-dir logs/spike05-phaseC --bench-engine paddle   # 换 PaddleOCR 跑同一口径");
        Console.WriteLine("  WeChatWindowInspector --bench-profile --ocr-dir logs/spike05-phaseC-light100 --bench-engine paddle --bench-scales 2,3   # 性能剖析");
        Console.WriteLine("  WeChatWindowInspector --bench-profile --ocr-dir logs/spike05-phaseC-light100 --bench-engine paddle --bench-paddle-model mobile   # 换 mobile 模型压测");
        Console.WriteLine("  WeChatWindowInspector --bench-profile --ocr-dir logs/spike05-phaseC-light100 --bench-engine paddle --bench-threads 8   # 指定推理线程数");
    }
}
