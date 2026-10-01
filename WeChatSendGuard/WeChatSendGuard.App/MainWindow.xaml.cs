using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using WeChatSendGuard.Models;
using WeChatSendGuard.Services;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace WeChatSendGuard;

/// <summary>
/// 极简状态窗口：显示当前会话识别结果与覆盖层状态，并提供"设置 / 重载配置 / 退出"。
/// 同时托管托盘图标与右键菜单（启用/停用、设置、重载配置、打开配置目录、显示窗口、退出）。
/// 配置改动走 <see cref="SettingsWindow"/>，也可继续手工编辑 config.json。
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxLogLines = 200;

    /// <summary>应用图标（源文件在仓库 resource\wcsg.ico，由 csproj 以 Resource 方式嵌入）。</summary>
    private const string AppIconUri = "pack://application:,,,/Assets/wcsg.ico";

    private static readonly Brush DangerBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));

    private readonly ObservableCollection<string> _logLines = new();
    private readonly LogWriter _log;
    private readonly GuardEngine _engine;
    private readonly string _configPath;
    private readonly string _baseDirectory;

    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _enableItem;
    private Drawing.Icon? _iconActive;
    private Drawing.Icon? _iconPaused;
    private GuardStatus? _lastStatus;
    private bool _exiting;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();

        LogList.ItemsSource = _logLines;

        _baseDirectory = AppContext.BaseDirectory;
        _configPath = System.IO.Path.Combine(_baseDirectory, "config.json");
        ConfigPathText.Text = _configPath;

        _log = new LogWriter(System.IO.Path.Combine(_baseDirectory, "logs"));
        _log.LineWritten += AppendLog;

        var config = ConfigService.Load(_configPath, _log);
        _engine = new GuardEngine(config, _log, Dispatcher);
        _engine.StatusChanged += OnStatusChanged;

        CreateTrayIcon();

        Loaded += (_, _) => _engine.Start();
        Closed += OnClosed;
    }

    private void OnStatusChanged(GuardStatus status)
    {
        _lastStatus = status;

        WindowStateText.Text = status.WindowStateText;
        RenderRectText.Text = status.RenderRect;
        DpiText.Text = status.DpiText;
        OcrTextView.Text = string.IsNullOrEmpty(status.Context.OcrText) ? "—" : status.Context.OcrText;
        RiskText.Text = status.RiskText;
        RiskText.Foreground = status.Context.Risk == RiskState.Risk ? DangerBrush : NormalBrush;
        OverlayStateText.Text = status.OverlayStateText;
        TimingText.Text = status.OcrMilliseconds > 0 ? $"{status.OcrMilliseconds} ms / 次" : "—";

        UpdateTrayState();
    }

    private void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLog(line));
            return;
        }

        _logLines.Add(line);
        while (_logLines.Count > MaxLogLines)
        {
            _logLines.RemoveAt(0);
        }

        LogList.ScrollIntoView(_logLines[^1]);
    }

    // ---------- 托盘 ----------

    private void CreateTrayIcon()
    {
        _iconActive = LoadAppIcon(paused: false);
        _iconPaused = LoadAppIcon(paused: true);

        _enableItem = new WinForms.ToolStripMenuItem("启用提醒")
        {
            CheckOnClick = true,
            Checked = _engine.Config.Enabled,
        };
        _enableItem.Click += (_, _) =>
        {
            _engine.SetEnabled(_enableItem.Checked);
            UpdateTrayState();
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem("显示状态窗口", null, (_, _) => ShowStatusWindow()));
        menu.Items.Add(_enableItem);
        menu.Items.Add(new WinForms.ToolStripMenuItem("设置…", null, (_, _) => OpenSettings()));
        menu.Items.Add(new WinForms.ToolStripMenuItem("重载配置", null, (_, _) => ReloadConfig()));
        menu.Items.Add(new WinForms.ToolStripMenuItem("打开配置目录", null, (_, _) => OpenConfigDirectory()));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("退出", null, (_, _) => ExitApplication()));

        _tray = new WinForms.NotifyIcon
        {
            Icon = _iconActive,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowStatusWindow();

        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        if (_tray is null)
        {
            return;
        }

        var enabled = _engine.Config.Enabled;
        _tray.Icon = enabled ? _iconActive : _iconPaused;

        var text = enabled ? "微信防错发提醒助手（已启用）" : "微信防错发提醒助手（已停用）";
        if (enabled && _lastStatus?.Context.Risk == RiskState.Risk)
        {
            text += " · 命中风险";
        }

        _tray.Text = text;
    }

    /// <summary>
    /// 由嵌入的 wcsg.ico 生成托盘图标；停用态做灰度化，保留"是否在提醒"的视觉区分。
    /// </summary>
    private static Drawing.Icon LoadAppIcon(bool paused)
    {
        const int size = 32;

        using var stream = Application.GetResourceStream(new Uri(AppIconUri))?.Stream
                           ?? throw new InvalidOperationException($"未找到嵌入图标：{AppIconUri}");
        using var source = new Drawing.Icon(stream).ToBitmap();

        using var bitmap = new Drawing.Bitmap(size, size);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.Clear(Drawing.Color.Transparent);

            var destination = new Drawing.Rectangle(0, 0, size, size);
            if (!paused)
            {
                graphics.DrawImage(source, destination);
            }
            else
            {
                using var attributes = new Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(new Drawing.Imaging.ColorMatrix(
                [
                    [0.30f, 0.30f, 0.30f, 0f, 0f],
                    [0.59f, 0.59f, 0.59f, 0f, 0f],
                    [0.11f, 0.11f, 0.11f, 0f, 0f],
                    [0f, 0f, 0f, 1f, 0f],
                    [0f, 0f, 0f, 0f, 1f],
                ]));
                graphics.DrawImage(
                    source,
                    destination,
                    0,
                    0,
                    source.Width,
                    source.Height,
                    Drawing.GraphicsUnit.Pixel,
                    attributes);
            }
        }

        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private void ShowStatusWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            // 关闭窗口 = 收进托盘；真正退出走托盘菜单的"退出"
            e.Cancel = true;
            Hide();

            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray?.ShowBalloonTip(3000, "微信防错发提醒助手",
                    "已收进托盘继续运行；右键托盘图标可重新打开或退出。", WinForms.ToolTipIcon.Info);
            }

            return;
        }

        base.OnClosing(e);
    }

    // ---------- 状态窗口按钮 ----------

    private void OnReloadClick(object sender, RoutedEventArgs e) => ReloadConfig();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OnOpenConfigClick(object sender, RoutedEventArgs e) => OpenConfigDirectory();

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitApplication();

    /// <summary>打开设置窗口（模态）。保存时由窗口回调 <see cref="ApplyConfig"/> 写回并热重载。</summary>
    private void OpenSettings()
    {
        var settings = new SettingsWindow(_engine.Config, _configPath, ApplyConfig);
        if (IsVisible)
        {
            settings.Owner = this;
        }

        settings.ShowDialog();
    }

    private void ApplyConfig(GuardConfig config)
    {
        _engine.Reload(config);

        if (_enableItem is not null)
        {
            _enableItem.Checked = config.Enabled;
        }

        UpdateTrayState();
    }

    private void ReloadConfig()
    {
        var config = ConfigService.Load(_configPath, _log);
        _engine.Reload(config);

        if (_enableItem is not null)
        {
            _enableItem.Checked = _engine.Config.Enabled;
        }

        UpdateTrayState();
    }

    private void OpenConfigDirectory()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_baseDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _log.Write($"打开目录失败：{exception.Message}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _engine.StatusChanged -= OnStatusChanged;
        _engine.Dispose();
        _log.LineWritten -= AppendLog;

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        _iconActive?.Dispose();
        _iconPaused?.Dispose();

        Application.Current.Shutdown();
    }
}
