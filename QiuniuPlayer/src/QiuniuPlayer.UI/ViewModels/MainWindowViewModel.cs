using CommunityToolkit.Mvvm.ComponentModel;
using QiuniuPlayer.UI.Common.Enums;
using QiuniuPlayer.UI.Services;

namespace QiuniuPlayer.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{

    [ObservableProperty]
    private DisplayMode _currentDisplayMode = DisplayMode.Normal;
    [ObservableProperty]
    private string _songList;

    public MainWindowViewModel()
    {
        //https://www.zhihu.com/question/1920892831981106398/answer/1930304825150669292
        _songList = LocalizationService.T("SongList");
        LocalizationService.LanguageChanged += OnLanguageChanged; // 订阅事件
    }
    
    /**
     * 语言切换事件处理
     */
    private void OnLanguageChanged()
    {
        SongList = LocalizationService.T("SongList");
        OnPropertyChanged(nameof(SongList));
    }
    
}
