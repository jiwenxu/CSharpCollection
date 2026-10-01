using System.Windows;

namespace WeChatSendGuard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 兜底：即便 manifest 未被采用，也在任何窗口创建前声明 Per-Monitor V2
        Native.NativeMethods.EnablePerMonitorDpiAwareness();
        base.OnStartup(e);
    }
}
