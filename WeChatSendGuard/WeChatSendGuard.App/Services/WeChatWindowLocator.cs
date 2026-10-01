using System.Diagnostics;
using WeChatSendGuard.Models;
using WeChatSendGuard.Native;

namespace WeChatSendGuard.Services;

/// <summary>
/// 微信窗口定位器：只读枚举窗口，定位主窗口与渲染子窗口。
/// 依据 Spike-01 实测结论——进程名关键字 + 类名白名单，并过滤辅助窗口。
/// </summary>
internal sealed class WeChatWindowLocator
{
    /// <summary>微信进程名关键字：3.9.x 为 WeChat.exe，4.x 为 Weixin.exe</summary>
    private static readonly string[] ProcessKeywords = { "wechat", "weixin" };

    private const string MainWindowClass = "Qt51514QWindowIcon";
    private const string RenderWindowClass = "MMUIRenderSubWindowHW";

    private static readonly TimeSpan ProcessCacheTtl = TimeSpan.FromSeconds(2);

    private readonly Dictionary<int, string> _processNames = new();
    private HashSet<int> _wechatProcessIds = new();
    private DateTime _processCacheTime = DateTime.MinValue;

    public WindowSnapshot Locate()
    {
        RefreshProcessCache();

        var foreground = NativeMethods.GetForegroundWindow();
        var mainWindow = IntPtr.Zero;
        var renderWindow = IntPtr.Zero;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!_wechatProcessIds.Contains(GetProcessId(hwnd)))
            {
                return true;
            }

            if (!string.Equals(NativeMethods.GetClassNameText(hwnd), MainWindowClass, StringComparison.Ordinal))
            {
                return true;
            }

            // 主窗口可能有多个（如预览窗），优先取可见的那个
            if (mainWindow != IntPtr.Zero && !NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            mainWindow = hwnd;
            return true;
        }, IntPtr.Zero);

        if (mainWindow != IntPtr.Zero)
        {
            NativeMethods.EnumChildWindows(mainWindow, (child, _) =>
            {
                if (!string.Equals(NativeMethods.GetClassNameText(child), RenderWindowClass, StringComparison.Ordinal))
                {
                    return true;
                }

                renderWindow = child;
                return false;
            }, IntPtr.Zero);
        }

        if (renderWindow == IntPtr.Zero)
        {
            return new WindowSnapshot { MainWindow = mainWindow };
        }

        NativeMethods.GetWindowRect(renderWindow, out var rect);

        return new WindowSnapshot
        {
            MainWindow = mainWindow,
            RenderWindow = renderWindow,
            RenderRect = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            Dpi = (int)NativeMethods.GetWindowDpi(renderWindow),
            Minimized = NativeMethods.IsIconic(mainWindow),
            Visible = NativeMethods.IsWindowVisible(mainWindow),
            IsForeground = IsOwnedBy(foreground, mainWindow),
        };
    }

    /// <summary>前台窗口是否属于微信主窗口（含其子窗口，如渲染子窗口）。</summary>
    private static bool IsOwnedBy(IntPtr foreground, IntPtr mainWindow)
    {
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        return foreground == mainWindow
            || NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT) == mainWindow;
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

                if (ProcessKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
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

    private static int GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return (int)processId;
    }
}
