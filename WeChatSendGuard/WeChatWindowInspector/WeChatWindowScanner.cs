using System.Diagnostics;

namespace WeChatWindowInspector;

/// <summary>
/// 微信窗口侦察器：定位微信进程相关的顶层窗口及其所有子窗口。
/// 只读取窗口信息，不做任何修改（对应需求文档 v1.1 第 7.1 节 Spike-01）。
/// </summary>
internal sealed class WeChatWindowScanner
{
    /// <summary>微信进程名关键字：3.9.x 为 WeChat.exe，4.x 为 Weixin.exe</summary>
    private static readonly string[] WeChatProcessKeywords = { "wechat", "weixin" };

    private static readonly TimeSpan ProcessCacheTtl = TimeSpan.FromSeconds(2);

    private readonly Dictionary<int, string> _processNames = new();
    private HashSet<int> _wechatProcessIds = new();
    private DateTime _processCacheTime = DateTime.MinValue;

    /// <summary>抓取一帧窗口快照</summary>
    /// <param name="includeAllProcesses">true 时包含所有进程的顶层窗口（便于对照前台窗口归属）</param>
    public IReadOnlyList<WindowInfo> Capture(bool includeAllProcesses)
    {
        RefreshProcessCache();

        var foreground = NativeMethods.GetForegroundWindow();
        var result = new List<WindowInfo>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var isWeChat = _wechatProcessIds.Contains(GetProcessId(hwnd));

            if (!isWeChat && !includeAllProcesses)
            {
                return true;
            }

            result.Add(Build(hwnd, 0, foreground));

            // 聊天窗口往往是微信主窗口的子窗口，因此必须继续枚举子孙窗口
            if (isWeChat)
            {
                NativeMethods.EnumChildWindows(hwnd, (child, __) =>
                {
                    result.Add(Build(child, DepthOf(child), foreground));
                    return true;
                }, IntPtr.Zero);
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>抓取单个窗口（用于前台窗口等不在快照内的窗口）</summary>
    public WindowInfo CaptureWindow(IntPtr hwnd) =>
        Build(hwnd, DepthOf(hwnd), NativeMethods.GetForegroundWindow());

    public bool IsWeChatProcess(int processId)
    {
        RefreshProcessCache();
        return _wechatProcessIds.Contains(processId);
    }

    public string GetProcessName(int processId)
    {
        RefreshProcessCache();

        if (_processNames.TryGetValue(processId, out var name))
        {
            return name;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
            _processNames[processId] = name;
        }
        catch
        {
            name = "?";
        }

        return name;
    }

    private void RefreshProcessCache()
    {
        if (DateTime.UtcNow - _processCacheTime < ProcessCacheTtl)
        {
            return;
        }

        _processNames.Clear();
        var wechatIds = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                _processNames[process.Id] = name;

                if (WeChatProcessKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    wechatIds.Add(process.Id);
                }
            }
            catch
            {
                // 系统进程可能因权限无法访问，忽略
            }
            finally
            {
                process.Dispose();
            }
        }

        _wechatProcessIds = wechatIds;
        _processCacheTime = DateTime.UtcNow;
    }

    private WindowInfo Build(IntPtr hwnd, int depth, IntPtr foreground)
    {
        var processId = GetProcessId(hwnd);
        NativeMethods.GetWindowRect(hwnd, out var rect);

        return new WindowInfo
        {
            Hwnd = hwnd,
            ClassName = NativeMethods.GetClassNameText(hwnd),
            Text = NativeMethods.GetWindowTextText(hwnd),
            Parent = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_PARENT),
            Owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER),
            Root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT),
            Depth = depth,
            ProcessId = processId,
            ProcessName = GetProcessName(processId),
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom,
            Visible = NativeMethods.IsWindowVisible(hwnd),
            Enabled = NativeMethods.IsWindowEnabled(hwnd),
            Minimized = NativeMethods.IsIconic(hwnd),
            Maximized = NativeMethods.IsZoomed(hwnd),
            IsForeground = hwnd == foreground,
            Dpi = NativeMethods.GetWindowDpi(hwnd),
            Style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE),
            ExStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE),
        };
    }

    private static int GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return (int)processId;
    }

    /// <summary>
    /// 计算窗口层级深度。
    /// 注意：不能用 GetParent —— 对顶层窗口它会返回 Owner 而非真实父窗口，
    /// 因此这里统一使用 GetAncestor(GA_PARENT) 向上追溯。
    /// </summary>
    private static int DepthOf(IntPtr hwnd)
    {
        var depth = 0;
        var current = hwnd;

        while (depth < 64)
        {
            var root = NativeMethods.GetAncestor(current, NativeMethods.GA_ROOT);
            if (root == current || root == IntPtr.Zero)
            {
                break;
            }

            current = NativeMethods.GetAncestor(current, NativeMethods.GA_PARENT);
            if (current == IntPtr.Zero)
            {
                break;
            }

            depth++;
        }

        return depth;
    }
}
