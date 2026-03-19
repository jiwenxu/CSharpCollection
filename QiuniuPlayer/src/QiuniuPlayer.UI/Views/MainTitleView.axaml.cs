using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using QiuniuPlayer.UI.Services;
using QiuniuPlayer.UI.ViewModels;

namespace QiuniuPlayer.UI.Views;

public partial class MainTitleView : UserControl
{
    public MainTitleView()
    {
        InitializeComponent();
        DataContext = new MainTitleViewModel();
    }
}