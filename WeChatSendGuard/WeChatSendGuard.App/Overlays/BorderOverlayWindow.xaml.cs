using System.Windows;
using System.Windows.Media;
using WeChatSendGuard.Models;

namespace WeChatSendGuard.Overlays;

/// <summary>红框提醒覆盖层：贴齐微信窗口四边，可选一行文字标签。</summary>
public partial class BorderOverlayWindow : OverlayWindowBase
{
    private string _appliedKey = string.Empty;

    public BorderOverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>应用样式；仅在配置变化时真正更新，避免每 100ms 触发重绘。</summary>
    public void Configure(BorderOptions options, string? matchedEntry)
    {
        var label = options.ShowLabel && !string.IsNullOrEmpty(matchedEntry)
            ? string.Format(options.LabelFormat, matchedEntry)
            : string.Empty;

        var key = $"{options.Color}|{options.Thickness}|{label}";
        if (key == _appliedKey)
        {
            return;
        }

        _appliedKey = key;

        Frame.BorderBrush = ParseBrush(options.Color);
        Frame.BorderThickness = new Thickness(options.Thickness);

        if (label.Length > 0)
        {
            LabelText.Text = label;
            LabelHost.Visibility = Visibility.Visible;
        }
        else
        {
            LabelHost.Visibility = Visibility.Collapsed;
        }
    }

    private static Brush ParseBrush(string value)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // 颜色非法时回退到默认红
        }

        return Brushes.Red;
    }
}
