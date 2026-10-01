using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace WeChatWindowInspector;

/// <summary>
/// Spike-05 截图执行器：定位微信渲染子窗口 → 抓图（可选网格 / 裁剪）→ 落盘 PNG 与 sidecar → 追加汇总表。
/// 支持单次（--shot）与连拍（--shot --watch）两种模式，对应《Spike-05验证设计.md》第 5~7 节。
/// </summary>
internal static class ShotRunner
{
    /// <summary>微信主窗口候选类名：4.x 已实测；3.9.x 为历史资料待测</summary>
    private static readonly string[] MainWindowClassWhitelist =
    {
        "Qt51514QWindowIcon",
        "WeChatMainWndForPC",
    };

    /// <summary>渲染子窗口类名：会话内容统一绘制在该表面（4.x 实测）</summary>
    private const string RenderSubWindowClass = "MMUIRenderSubWindowHW";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run(WeChatWindowScanner scanner, LogWriter log, CliOptions options)
    {
        var outputDir = ResolveOutputDir(options.OutputDir);
        Directory.CreateDirectory(outputDir);

        // 单次模式
        if (!options.Watch)
        {
            return CaptureOnce(scanner, log, options, options.Label, outputDir) ? 0 : 1;
        }

        // 连拍模式：每个场景拍一张，逐张标注 ground truth
        log.Write(string.Empty);
        log.Write("连拍模式说明：");
        log.Write("  1) 在微信中切换会话 / 调整窗口大小 / 切换显示器；");
        log.Write("  2) 回到本窗口，输入标签（形如 技术交流群/群，/ 前为会话名、后为类型）后回车即拍；");
        log.Write("  3) 直接回车沿用上次标签；输入 q 回车结束。");
        log.Write($"  输出目录：{outputDir}");

        if (options.UseScreenCapture)
        {
            log.Write("  [警告] 当前为屏幕 BitBlt 模式：拍摄时微信必须可见且未被本窗口遮挡，否则会拍到错误内容。");
        }

        var currentLabel = options.Label;
        while (true)
        {
            var hint = currentLabel.Length > 0 ? $"回车沿用「{currentLabel}」" : "回车留空";
            Console.Write($"标签（{hint}，q 结束）：");
            var input = Console.ReadLine();
            if (input is null)
            {
                break;
            }

            input = input.Trim();
            if (input.Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (input.Length > 0)
            {
                currentLabel = input;
            }

            CaptureOnce(scanner, log, options, currentLabel, outputDir);
        }

        log.Write("连拍结束。");
        return 0;
    }

    /// <summary>采集一次：解析目标 → 抓图 → 落盘 PNG / sidecar → 追加汇总表</summary>
    private static bool CaptureOnce(
        WeChatWindowScanner scanner,
        LogWriter log,
        CliOptions options,
        string label,
        string outputDir)
    {
        var main = FindMainWindow(scanner);
        if (main == IntPtr.Zero)
        {
            log.Write("未找到微信主窗口：请确认微信已启动且未最小化（支持 Weixin.exe / WeChat.exe）。");
            return false;
        }

        var render = FindRenderSubWindow(main);
        var target = render != IntPtr.Zero ? render : main;

        NativeMethods.GetWindowRect(target, out var rect);
        if (rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
        {
            log.Write($"目标窗口矩形无效：({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})");
            return false;
        }

        NativeMethods.GetWindowRect(main, out var mainRect);
        NativeMethods.GetClientRect(target, out var clientRect);
        var dpi = NativeMethods.GetWindowDpi(main);
        var foreground = NativeMethods.GetForegroundWindow();
        var method = options.UseScreenCapture ? ScreenCapture.MethodScreenBitBlt : ScreenCapture.MethodPrintWindow;

        log.Write(string.Empty);
        log.Write($"Spike-05 截图目标  {DateTime.Now:HH:mm:ss}");
        log.Write($"  标签        : {(label.Length > 0 ? label : "（未标注）")}");
        log.Write($"  主窗口      : 0x{main.ToInt64():X8} [{NativeMethods.GetClassNameText(main)}] \"{NativeMethods.GetWindowTextText(main)}\" Rect={Format(mainRect)}");
        log.Write($"  渲染子窗口  : {(render != IntPtr.Zero ? $"0x{render.ToInt64():X8} [{RenderSubWindowClass}]" : "未找到，回退为主窗口")}");
        log.Write($"  对齐目标    : 0x{target.ToInt64():X8} [{NativeMethods.GetClassNameText(target)}] Rect={Format(rect)}");
        log.Write($"  DPI / 缩放  : {dpi} ({(dpi == 0 ? 0 : dpi * 100 / 96)}%)");
        log.Write($"  前台状态    : {(foreground == main ? "微信在前台" : $"非微信前台（0x{foreground.ToInt64():X8}）")}");
        log.Write($"  截图方式    : {method}{(options.UseScreenCapture ? "（需微信可见且未被遮挡）" : string.Empty)}");

        BitmapSource image;
        try
        {
            image = ScreenCapture.Capture(target, rect, options.UseScreenCapture);
        }
        catch (Exception ex)
        {
            log.Write($"截图失败（{method}）：{ex.Message}");
            if (!options.UseScreenCapture)
            {
                log.Write("提示：可改用 --shot --screen（屏幕 BitBlt），需保证微信可见且未被遮挡。");
            }

            return false;
        }

        var index = Directory.EnumerateFiles(outputDir, "*-full.png").Count() + 1;
        var prefix = label.Length > 0 ? $"{index:D2}-{Sanitize(label)}" : $"{index:D2}";
        var files = new List<string>();

        var fullImage = image;
        if (options.OverlayGrid)
        {
            try
            {
                fullImage = ScreenCapture.DrawGrid(image);
            }
            catch (Exception ex)
            {
                log.Write($"[警告] 网格标注失败，改为输出原图：{ex.Message}");
            }
        }

        var fullName = $"{prefix}-full.png";
        ScreenCapture.SavePng(fullImage, Path.Combine(outputDir, fullName));
        files.Add(fullName);

        if (options.Region is { } region)
        {
            try
            {
                var titleName = $"{prefix}-title.png";
                ScreenCapture.SavePng(ScreenCapture.Crop(image, region), Path.Combine(outputDir, titleName));
                files.Add(titleName);
            }
            catch (Exception ex)
            {
                log.Write($"[警告] 标题区裁剪失败：{ex.Message}");
            }
        }

        var sidecarName = $"{prefix}.json";
        var sidecar = BuildSidecar(options, label, index, method, rect, mainRect, clientRect, dpi, main, render, target, foreground, files);
        File.WriteAllText(Path.Combine(outputDir, sidecarName), JsonSerializer.Serialize(sidecar, JsonOptions), new UTF8Encoding(true));

        AppendSummary(outputDir, options, label, index, rect, dpi, method, files);

        log.Write($"  已保存      : {string.Join("、", files)}、{sidecarName}");
        return true;
    }

    private static object BuildSidecar(
        CliOptions options,
        string label,
        int index,
        string method,
        NativeMethods.RECT rect,
        NativeMethods.RECT mainRect,
        NativeMethods.RECT clientRect,
        uint dpi,
        IntPtr main,
        IntPtr render,
        IntPtr target,
        IntPtr foreground,
        IReadOnlyList<string> files)
    {
        NativeMethods.GetWindowRect(foreground, out var foregroundRect);
        var hasDwmBounds = NativeMethods.DwmGetWindowAttribute(
            main, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out var dwmRect, sizeof(int) * 4) == 0;

        var (scene, kind) = SplitLabel(label);

        return new
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            index,
            label,
            scene,
            kind,
            captureMethod = method,
            gridOverlay = options.OverlayGrid,
            region = options.Region is null
                ? null
                : new { x = options.Region.X, y = options.Region.Y, width = options.Region.Width, height = options.Region.Height },
            dpi,
            scalePercent = dpi == 0 ? 0 : (int)(dpi * 100 / 96),
            runningProcessAwareness = NativeMethods.GetProcessAwarenessText(),
            wechatIsForeground = foreground == main,
            foreground = new
            {
                hwnd = $"0x{foreground.ToInt64():X8}",
                windowText = NativeMethods.GetWindowTextText(foreground),
                rect = Rects.ToRect(foregroundRect),
            },
            mainWindow = new
            {
                hwnd = $"0x{main.ToInt64():X8}",
                className = NativeMethods.GetClassNameText(main),
                windowText = NativeMethods.GetWindowTextText(main),
                rect = Rects.ToRect(mainRect),
                dwmExtendedFrameBounds = hasDwmBounds ? Rects.ToRect(dwmRect) : null,
            },
            renderSubWindow = render == IntPtr.Zero
                ? null
                : new
                {
                    hwnd = $"0x{render.ToInt64():X8}",
                    className = NativeMethods.GetClassNameText(render),
                    rect = Rects.ToRect(rect),
                },
            target = new
            {
                hwnd = $"0x{target.ToInt64():X8}",
                className = NativeMethods.GetClassNameText(target),
                rect = Rects.ToRect(rect),
                clientRect = Rects.ToRect(clientRect),
                size = $"{rect.Right - rect.Left}x{rect.Bottom - rect.Top}",
            },
            files,
        };
    }

    private static void AppendSummary(
        string outputDir,
        CliOptions options,
        string label,
        int index,
        NativeMethods.RECT rect,
        uint dpi,
        string method,
        IReadOnlyList<string> files)
    {
        var summaryPath = Path.Combine(outputDir, "spike05-summary.md");
        var encoding = new UTF8Encoding(true);

        if (!File.Exists(summaryPath))
        {
            File.WriteAllLines(summaryPath, new[]
            {
                "# Spike-05 采集汇总",
                string.Empty,
                "| # | 场景(会话名) | 类型 | 窗口尺寸 | DPI | 渲染区 Rect | 标题区相对偏移 | 偏移是否稳定 | 截图方式 | 文件 |",
                "|---|--------------|------|----------|-----|-------------|----------------|--------------|----------|------|",
            }, encoding);
        }

        var (scene, kind) = SplitLabel(label);
        var region = options.Region is null
            ? string.Empty
            : $"x={options.Region.X},y={options.Region.Y},{options.Region.Width}x{options.Region.Height}";
        var scale = dpi == 0 ? 0 : (int)(dpi * 100 / 96);

        var row = $"| {index} | {scene} | {kind} | {rect.Right - rect.Left}x{rect.Bottom - rect.Top} | {scale}% " +
                  $"| ({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) | {region} |  | {method} | {string.Join(", ", files)} |";

        File.AppendAllText(summaryPath, row + Environment.NewLine, encoding);
    }

    private static string ResolveOutputDir(string configured) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine("logs", $"spike05-{DateTime.Now:yyyyMMdd-HHmmss}")
            : configured);

    /// <summary>定位微信主窗口：优先匹配类名白名单，否则取面积最大的可见未最小化窗口</summary>
    private static IntPtr FindMainWindow(WeChatWindowScanner scanner)
    {
        var whitelisted = IntPtr.Zero;
        var largest = IntPtr.Zero;
        var largestArea = 0L;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (!scanner.IsWeChatProcess((int)processId) ||
                !NativeMethods.IsWindowVisible(hwnd) ||
                NativeMethods.IsIconic(hwnd))
            {
                return true;
            }

            if (MainWindowClassWhitelist.Contains(NativeMethods.GetClassNameText(hwnd)))
            {
                whitelisted = hwnd;
                return false;
            }

            NativeMethods.GetWindowRect(hwnd, out var rect);
            var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area > largestArea)
            {
                largestArea = area;
                largest = hwnd;
            }

            return true;
        }, IntPtr.Zero);

        return whitelisted != IntPtr.Zero ? whitelisted : largest;
    }

    /// <summary>定位渲染子窗口（会话内容自绘表面）</summary>
    private static IntPtr FindRenderSubWindow(IntPtr main)
    {
        var found = IntPtr.Zero;

        NativeMethods.EnumChildWindows(main, (child, _) =>
        {
            if (NativeMethods.GetClassNameText(child) != RenderSubWindowClass)
            {
                return true;
            }

            found = child;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>拆分标签 "会话名/类型"，类型取值如 群 / 私</summary>
    private static (string Scene, string Kind) SplitLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return (string.Empty, string.Empty);
        }

        var parts = label.Split('/', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], string.Empty);
    }

    private static string Sanitize(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(label.Length);

        foreach (var ch in label)
        {
            builder.Append(invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch);
        }

        return builder.ToString().Trim();
    }

    private static string Format(NativeMethods.RECT rect) =>
        $"({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) {rect.Right - rect.Left}x{rect.Bottom - rect.Top}";

    /// <summary>RECT 到 JSON 对象的统一转换</summary>
    private static class Rects
    {
        public static object ToRect(NativeMethods.RECT rect) => new
        {
            left = rect.Left,
            top = rect.Top,
            right = rect.Right,
            bottom = rect.Bottom,
            width = rect.Right - rect.Left,
            height = rect.Bottom - rect.Top,
        };
    }
}
