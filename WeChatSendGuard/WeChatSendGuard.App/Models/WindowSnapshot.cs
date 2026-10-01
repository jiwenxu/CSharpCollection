namespace WeChatSendGuard.Models;

/// <summary>微信窗口的一次定位结果。</summary>
public sealed record WindowSnapshot
{
    public IntPtr MainWindow { get; init; }

    /// <summary>渲染子窗口 MMUIRenderSubWindowHW；未找到时为 IntPtr.Zero。</summary>
    public IntPtr RenderWindow { get; init; }

    /// <summary>渲染子窗口的屏幕矩形（物理像素），覆盖层与裁剪区均以此为基准。</summary>
    public PixelRect RenderRect { get; init; }

    public int Dpi { get; init; }

    public bool Minimized { get; init; }

    public bool Visible { get; init; }

    public bool IsForeground { get; init; }

    public bool Found => RenderWindow != IntPtr.Zero && !RenderRect.IsEmpty;

    public string MainWindowText => $"0x{MainWindow.ToInt64():X8}";

    public string RenderWindowText => $"0x{RenderWindow.ToInt64():X8}";
}
