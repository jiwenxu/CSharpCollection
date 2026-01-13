using CommunityToolkit.Mvvm.ComponentModel;
using QiuniuPlayer.Core.Common.Enums;

namespace QiuniuPlayer.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public string Greeting { get; } = "Welcome to Avalonia!";

    [ObservableProperty]
    private string _windowTitle = "囚牛播放器 - QiuniuPlayer";

    [ObservableProperty]
    private DisplayMode _currentDisplayMode = DisplayMode.Normal;

    [ObservableProperty]
    private ThemeMode _currentTheme = ThemeMode.Dark;
}
