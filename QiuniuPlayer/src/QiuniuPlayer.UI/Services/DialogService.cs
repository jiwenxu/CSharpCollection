
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace QiuniuPlayer.UI.Services;

public static class DialogService
{
    public static async Task<bool> ConfirmAsync(string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        var result = false;

        var panel = new StackPanel 
        { 
            Margin = new Thickness(20),
            Spacing = 20 
        };

        panel.Children.Add(new TextBlock 
        { 
            Text = message,
            TextWrapping = TextWrapping.Wrap 
        });

        var btnPanel = new StackPanel 
        { 
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10
        };

        var btnYes = new Button { Content = "确定", IsDefault = true };
        var btnNo = new Button { Content = "取消", IsCancel = true };

        btnYes.Click += (_, _) => { result = true; window.Close(); };
        btnNo.Click += (_, _) => { result = false; window.Close(); };

        btnPanel.Children.Add(btnYes);
        btnPanel.Children.Add(btnNo);
        panel.Children.Add(btnPanel);

        window.Content = panel;

        // 模态显示
        var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var owner = lifetime?.MainWindow;
        
        if (owner != null)
            await window.ShowDialog<bool>(owner);
        else
            window.Show();

        return result;
    }
}