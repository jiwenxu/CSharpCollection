using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WeChatSendGuard.Models;
using WeChatSendGuard.Native;

namespace WeChatSendGuard.Overlays;

/// <summary>
/// 覆盖层基类：无边框、透明、鼠标穿透、不抢焦点的独立窗口。
/// 定位一律走 SetWindowPos（物理像素），避免 WPF 的 DIP 换算误差。
/// </summary>
public abstract class OverlayWindowBase : Window
{
    protected OverlayWindowBase()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        Left = 0;
        Top = 0;
        Width = 1;
        Height = 1;
    }

    protected IntPtr Handle { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Handle = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_LAYERED
                   | NativeMethods.WS_EX_TRANSPARENT
                   | NativeMethods.WS_EX_TOOLWINDOW
                   | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    /// <summary>把覆盖层移动到指定的屏幕物理矩形并按需显隐。</summary>
    public void Apply(PixelRect rect, bool visible)
    {
        if (!visible || rect.IsEmpty)
        {
            HideOverlay();
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public void HideOverlay()
    {
        if (IsVisible)
        {
            Hide();
        }
    }
}
