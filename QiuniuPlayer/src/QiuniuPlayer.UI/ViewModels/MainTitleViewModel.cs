

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QiuniuPlayer.UI.Services;

namespace QiuniuPlayer.UI.ViewModels;

public partial class MainTitleViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title;

    public MainTitleViewModel()
    {
        _title = LocalizationService.T("Title");
        LocalizationService.LanguageChanged += OnLanguageChanged; // 订阅事件
    }
    /**
     * 语言切换事件处理
     */
    private void OnLanguageChanged()
    {
        Title = LocalizationService.T("Title");
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    public void SwitchLanguage()
    {
        LocalizationService.SwitchLanguage();
    }
    [RelayCommand]
    public void SwitchTheme()
    {
        ThemeService.SwitchTheme();
    }
    [RelayCommand]
    public async Task ExitAsync()
    {
        var confirm = await DialogService.ConfirmAsync("提示", "确定要退出吗？");
        // if (confirm == false) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Shutdown(0);  // 0=正常，非0=异常
        }
    }

}