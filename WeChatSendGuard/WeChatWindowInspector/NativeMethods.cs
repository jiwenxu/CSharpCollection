using System.Runtime.InteropServices;
using System.Text;

namespace WeChatWindowInspector;

/// <summary>
/// user32.dll 的 P/Invoke 封装。
/// 仅使用只读类 API，不注入、不修改任何目标进程。
/// </summary>
internal static class NativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // 窗口枚举与基础信息
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    // 截图相关（Spike-05）：只读抓取，不修改目标窗口
    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hGdiObj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    // DPI 相关（Windows 10 1607+；缺失时由调用方捕获异常）
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

    public const uint GA_PARENT = 1;
    public const uint GA_ROOT = 2;
    public const uint GW_OWNER = 4;
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    /// <summary>PrintWindow 标志：连同 DWM 合成内容一起抓取（Windows 8.1+）</summary>
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>DwmGetWindowAttribute 属性：窗口可见扩展边框（不含 DWM 不可见边框）</summary>
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>BitBlt 光栅操作码：源拷贝</summary>
    public const int SRCCOPY = 0x00CC0020;

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 的句柄值（-4）</summary>
    private static readonly IntPtr DpiAwarenessContextPerMonitorV2 = new(-4);

    private static bool _dpiAwarenessApplied;

    /// <summary>
    /// 以 Per-Monitor V2 声明 DPI 感知。
    /// 必须尽早调用：否则本进程拿到的窗口坐标会被系统虚拟化，
    /// 导致 Scout 结论（尤其 Spike-03 DPI 矩阵）不可信。
    /// </summary>
    public static void EnablePerMonitorDpiAwareness()
    {
        if (_dpiAwarenessApplied)
        {
            return;
        }

        _dpiAwarenessApplied = true;
        try
        {
            if (!SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorV2))
            {
                SetProcessDPIAware();
            }
        }
        catch
        {
            // 老版本系统不支持该 API，忽略即可
        }
    }

    public static string GetProcessAwarenessText()
    {
        try
        {
            return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) switch
            {
                -1 => "Invalid",
                0 => "Unaware（未感知，坐标会被系统虚拟化）",
                1 => "System DPI Aware",
                2 => "Per-Monitor DPI Aware（V2 上下文亦返回此值）",
                _ => "未知",
            };
        }
        catch
        {
            return "未知";
        }
    }

    public static uint GetWindowDpi(IntPtr hWnd)
    {
        try
        {
            return GetDpiForWindow(hWnd);
        }
        catch
        {
            return 0;
        }
    }

    public static uint GetSystemDpi()
    {
        try
        {
            return GetDpiForSystem();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>读取窗口类名</summary>
    public static string GetClassNameText(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    /// <summary>读取窗口标题</summary>
    public static string GetWindowTextText(IntPtr hWnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
