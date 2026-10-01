using System.Diagnostics;
using System.Windows.Automation;

namespace WeChatWindowInspector;

/// <summary>
/// UI Automation 探测 —— 对应《需求文档v1.1》第 7.6 节"待决策项"。
/// 需回答两个问题（微信 4.x，不注入进程）：
///   Q1 能否<b>读取</b>当前会话标题？
///   Q2 能否<b>感知</b>会话切换（UIA 事件，或可接受的轮询成本）？
/// 两条通道各自独立记录，结论取决于哪条通道真正捕获到会话名称变化。
/// </summary>
internal static class UiaProbe
{
    private const int LineWidth = 110;

    /// <summary>单次遍历的节点上限，防止 Qt 暴露超大自动化树时卡死</summary>
    private const int NodeCap = 3000;

    /// <summary>必须借助 UIA 才能判定"可读/可感知"，因此只用只读接口</summary>
    private static long _eventCount;

    public static int Run(WeChatWindowScanner scanner, LogWriter log, CliOptions options)
    {
        var targets = ResolveTargets(scanner, log);
        if (targets.Count == 0)
        {
            log.Write("[UIA] 未找到可见的微信主窗口，无法探测。请先启动并打开微信主界面。");
            return 1;
        }

        var view = options.RawView ? TreeWalker.RawViewWalker : TreeWalker.ControlViewWalker;

        foreach (var target in targets)
        {
            DumpTree(target, log, view, options.MaxDepth);
        }

        if (!options.Watch)
        {
            PrintDumpHints(log);
            return 0;
        }

        return Watch(scanner, targets, log, view, options);
    }

    /// <summary>定位探测目标：优先标题为"微信"的主窗口，其次取面积最大的可见微信顶层窗口</summary>
    private static List<WindowInfo> ResolveTargets(WeChatWindowScanner scanner, LogWriter log)
    {
        var visible = scanner.Capture(includeAllProcesses: false)
            .Where(w => w.Depth == 0 && w.Visible && IsQtTopLevel(w.ClassName))
            .ToList();

        if (visible.Count == 0)
        {
            return visible;
        }

        var main = visible.FirstOrDefault(w => w.Text == "微信")
                   ?? visible.OrderByDescending(w => (long)w.Width * w.Height).First();

        log.Write($"[UIA] 探测目标：0x{main.Hwnd.ToInt64():X8} [{main.ClassName}] \"{main.Text}\" Rect={main.RectText}");
        return new List<WindowInfo> { main };
    }

    private static bool IsQtTopLevel(string className) =>
        className.StartsWith("Qt", StringComparison.Ordinal)
        && !className.Contains("TrayIcon", StringComparison.Ordinal)
        && !className.Contains("ToolTip", StringComparison.Ordinal)
        && !className.Contains("ToolSaveBits", StringComparison.Ordinal);

    // ---------------------------------------------------------------- 树导出

    private static void DumpTree(WindowInfo target, LogWriter log, TreeWalker walker, int maxDepth)
    {
        var root = SafeFromHandle(target.Hwnd);
        if (root is null)
        {
            log.Write($"[UIA] 无法从窗口 0x{target.Hwnd.ToInt64():X8} 创建 AutomationElement（该窗口可能未提供自动化接口）。");
            return;
        }

        log.Write(string.Empty);
        log.Write(new string('=', LineWidth));
        log.Write($"UI Automation 树导出  0x{target.Hwnd.ToInt64():X8} [{target.ClassName}] \"{target.Text}\"");
        log.Write($"视图={(walker == TreeWalker.RawViewWalker ? "Raw" : "Control")}  最大深度={maxDepth}  节点上限={NodeCap}");
        log.Write($"开始时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        log.Write(new string('=', LineWidth));

        var count = 0;
        var truncated = false;
        var stopwatch = Stopwatch.StartNew();
        Walk(root, 0);
        stopwatch.Stop();

        log.Write(new string('-', LineWidth));
        log.Write($"导出完成：节点 {count} 个，耗时 {stopwatch.ElapsedMilliseconds}ms{(truncated ? $"，已达节点上限 {NodeCap} 被截断" : string.Empty)}");

        if (count <= 1)
        {
            log.Write("⚠️ 自动化树为空（只有根节点）：微信 4.x 在当前条件下未暴露 UIA 结构，");
            log.Write("   若不注入进程则无法通过 UIA 读取会话信息。");
        }

        return;

        void Walk(AutomationElement element, int depth)
        {
            if (depth > maxDepth || count >= NodeCap)
            {
                return;
            }

            count++;
            log.Write($"{new string(' ', depth * 2)}{(depth == 0 ? "●" : "-")} {Describe(element)}");

            AutomationElement? child = SafeFirstChild(walker, element);
            while (child is not null)
            {
                Walk(child, depth + 1);
                if (count >= NodeCap)
                {
                    truncated = true;
                    return;
                }

                child = SafeNextSibling(walker, child);
            }
        }
    }

    private static void PrintDumpHints(LogWriter log)
    {
        log.Write(string.Empty);
        log.Write("请回答下列问题（对照上面的树）：");
        log.Write("  Q1a 树中是否存在名称等于【当前会话名】的元素？若有，其 ControlType / 层级路径是什么？");
        log.Write("  Q1b 是否存在代表会话列表的元素（如 List / ListItem，且支持 Selection 模式）？");
        log.Write("  Q1c 是否存在代表消息区 / 输入框的元素（Edit、支持 Text / Value 模式）？");
        log.Write("  Q1d 群聊与私聊的元素结构是否存在可区分的差异（如群成员列表）？");
        log.Write(string.Empty);
        log.Write("接着执行 `--uia --watch` 进入监视模式，切换几次会话，观察哪条通道捕获到变化。");
    }

    // ---------------------------------------------------------------- 监视模式

    private static int Watch(
        WeChatWindowScanner scanner,
        IReadOnlyList<WindowInfo> targets,
        LogWriter log,
        TreeWalker walker,
        CliOptions options)
    {
        var root = SafeFromHandle(targets[0].Hwnd);
        if (root is null)
        {
            log.Write("[UIA] 无法创建 AutomationElement，监视模式中止。");
            return 1;
        }

        var state = new SessionState();
        var keys = ConsoleKeyWatcher.Start(() => state.Running);
        var pollInterval = options.IntervalSpecified ? options.IntervalMs : 1000;

        log.Write(string.Empty);
        log.Write(new string('=', LineWidth));
        log.Write($"进入 UIA 监视模式：视图=Control 深度={options.MaxDepth} 轮询间隔={pollInterval}ms");
        log.Write("请在此期间反复切换【私聊】与【群聊】。按 [Q] 退出。");
        log.Write("通道说明：[UIA:EVT] = UI Automation 事件；[UIA:POLL] = 主动轮询差分。");
        log.Write(new string('=', LineWidth));

        RegisterEvents(scanner, root, log);

        SortedSet<string> previous = new(StringComparer.Ordinal);
        var first = true;
        var pollCount = 0L;
        var pollTotalMs = 0L;
        var changeCount = 0;
        var stopwatch = new Stopwatch();

        while (state.Running)
        {
            while (keys.TryDequeue(out var key))
            {
                if (key == 'q')
                {
                    state.Running = false;
                }
            }

            if (!state.Running)
            {
                break;
            }

            stopwatch.Restart();
            var (signature, nodeCount) = BuildSignature(root, walker, options.MaxDepth);
            stopwatch.Stop();

            pollCount++;
            pollTotalMs += stopwatch.ElapsedMilliseconds;

            if (first)
            {
                previous = signature;
                first = false;
                log.Write($"[{Stamp()}] [UIA:POLL] 初始树：节点 {nodeCount} 个，签名 {signature.Count} 条，耗时 {stopwatch.ElapsedMilliseconds}ms");
            }
            else if (!previous.SetEquals(signature))
            {
                changeCount++;
                log.Write($"[{Stamp()}] [UIA:POLL] ★ 树内容变化（节点 {nodeCount}，耗时 {stopwatch.ElapsedMilliseconds}ms）");

                foreach (var added in signature.Except(previous))
                {
                    log.Write($"             + {added}");
                }

                foreach (var removed in previous.Except(signature))
                {
                    log.Write($"             - {removed}");
                }

                previous = signature;
            }

            if (pollCount % 30 == 0)
            {
                log.Write($"[{Stamp()}] [UIA:POLL] 统计：轮询 {pollCount} 次，平均 {pollTotalMs / pollCount}ms/次，节点 {nodeCount}，检出变化 {changeCount} 次");
            }

            Thread.Sleep(pollInterval);
        }

        Automation.RemoveAllEventHandlers();
        PrintWatchSummary(log, pollCount, pollTotalMs, changeCount);
        return 0;
    }

    private static void RegisterEvents(WeChatWindowScanner scanner, AutomationElement root, LogWriter log)
    {
        try
        {
            // 名称变化：会话标题最可能以 Name 属性变化的形式出现
            Automation.AddAutomationPropertyChangedEventHandler(
                root,
                TreeScope.Subtree,
                (sender, e) =>
                {
                    Interlocked.Increment(ref _eventCount);
                    var element = sender as AutomationElement;
                    log.Write($"[{Stamp()}] [UIA:EVT] 属性变化 {e.Property.ProgrammaticName} = \"{e.NewValue}\" @ {DescribeOrUnknown(element)}");
                },
                AutomationElement.NameProperty,
                AutomationElement.BoundingRectangleProperty);

            // 结构变化：会话切换可能整体替换子树
            Automation.AddStructureChangedEventHandler(
                root,
                TreeScope.Subtree,
                (sender, _) =>
                {
                    Interlocked.Increment(ref _eventCount);
                    log.Write($"[{Stamp()}] [UIA:EVT] 结构变化 @ {DescribeOrUnknown(sender as AutomationElement)}");
                });

            // 焦点变化（全局事件，仅记录微信进程内的元素）
            Automation.AddAutomationFocusChangedEventHandler((sender, _) =>
            {
                var element = sender as AutomationElement;
                if (element is null)
                {
                    return;
                }

                var processId = SafeGet(() => element.Current.ProcessId, 0);
                if (!scanner.IsWeChatProcess(processId))
                {
                    return;
                }

                Interlocked.Increment(ref _eventCount);
                log.Write($"[{Stamp()}] [UIA:EVT] 焦点变化 @ {Describe(element)}");
            });

            log.Write("[UIA] 已注册事件：Name/BoundingRectangle 属性变化、子树结构变化、焦点变化（限微信进程）。");
        }
        catch (Exception ex)
        {
            log.Write($"[UIA] ⚠️ 注册事件失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void PrintWatchSummary(LogWriter log, long pollCount, long pollTotalMs, int changeCount)
    {
        var events = Interlocked.Read(ref _eventCount);
        var average = pollCount == 0 ? 0 : pollTotalMs / pollCount;

        log.Write(string.Empty);
        log.Write(new string('=', LineWidth));
        log.Write("UIA 监视结束，判定依据：");
        log.Write($"  轮询通道：{pollCount} 次，平均 {average}ms/次，检出树变化 {changeCount} 次");
        log.Write($"  事件通道：{events} 条 UIA 事件");
        log.Write(new string('-', LineWidth));
        log.Write("  · 若上述变化行中出现【会话名称】→ 该通道可感知会话切换；");
        log.Write("  · 若轮询平均耗时过高（如 > 100ms）→ 实时性不达标，需改事件通道或降低频率；");
        log.Write("  · 若两条通道均无变化 → 不注入前提下无法感知会话切换，FR-05 的 UIA 方案不成立。");
        log.Write(new string('=', LineWidth));
    }

    // ---------------------------------------------------------------- 工具方法

    private static (SortedSet<string> Signature, int NodeCount) BuildSignature(
        AutomationElement root,
        TreeWalker walker,
        int maxDepth)
    {
        var signature = new SortedSet<string>(StringComparer.Ordinal);
        var count = 0;

        Walk(root, 0);
        return (signature, count);

        void Walk(AutomationElement element, int depth)
        {
            if (depth > maxDepth || count >= NodeCap)
            {
                return;
            }

            count++;
            signature.Add($"{ControlTypeOf(element)}|{NameOf(element)}");

            AutomationElement? child = SafeFirstChild(walker, element);
            while (child is not null && count < NodeCap)
            {
                Walk(child, depth + 1);
                child = SafeNextSibling(walker, child);
            }
        }
    }

    private static string Describe(AutomationElement element)
    {
        var controlType = ControlTypeOf(element);
        var name = NameOf(element);
        var automationId = SafeGet(() => element.Current.AutomationId, string.Empty);
        var className = SafeGet(() => element.Current.ClassName, string.Empty);
        var enabled = SafeGet(() => element.Current.IsEnabled, false);
        var offscreen = SafeGet(() => element.Current.IsOffscreen, false);

        var rect = "?";
        try
        {
            var bounds = element.Current.BoundingRectangle;
            rect = $"({(int)bounds.Left},{(int)bounds.Top},{(int)bounds.Right},{(int)bounds.Bottom})";
        }
        catch
        {
            // 元素可能已失效，忽略
        }

        return $"[{controlType}] Name=\"{name}\" Id=\"{automationId}\" Class=\"{className}\" Rect={rect} Enabled={enabled} Offscreen={offscreen} 模式=[{DetectPatterns(element)}]";
    }

    private static string DescribeOrUnknown(AutomationElement? element) =>
        element is null ? "（未知元素）" : Describe(element);

    private static string ControlTypeOf(AutomationElement element)
    {
        try
        {
            return element.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", string.Empty) ?? "?";
        }
        catch
        {
            return "?";
        }
    }

    private static string NameOf(AutomationElement element) =>
        SafeGet(() => element.Current.Name, string.Empty);

    private static readonly AutomationPattern[] CandidatePatterns =
    {
        InvokePattern.Pattern,
        TextPattern.Pattern,
        ValuePattern.Pattern,
        SelectionPattern.Pattern,
        SelectionItemPattern.Pattern,
        ExpandCollapsePattern.Pattern,
        ScrollPattern.Pattern,
        TogglePattern.Pattern,
        WindowPattern.Pattern,
    };

    private static string DetectPatterns(AutomationElement element)
    {
        var supported = new List<string>();

        foreach (var pattern in CandidatePatterns)
        {
            try
            {
                element.GetCurrentPattern(pattern);
                supported.Add(pattern.ProgrammaticName.Replace("PatternIdentifiers.Pattern", string.Empty));
            }
            catch
            {
                // 不支持该模式，忽略
            }
        }

        return supported.Count == 0 ? "无" : string.Join(",", supported);
    }

    private static AutomationElement? SafeFromHandle(IntPtr hwnd)
    {
        try
        {
            return AutomationElement.FromHandle(hwnd);
        }
        catch
        {
            return null;
        }
    }

    private static AutomationElement? SafeFirstChild(TreeWalker walker, AutomationElement element)
    {
        try
        {
            return walker.GetFirstChild(element);
        }
        catch
        {
            return null;
        }
    }

    private static AutomationElement? SafeNextSibling(TreeWalker walker, AutomationElement element)
    {
        try
        {
            return walker.GetNextSibling(element);
        }
        catch
        {
            return null;
        }
    }

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss.fff");
}
