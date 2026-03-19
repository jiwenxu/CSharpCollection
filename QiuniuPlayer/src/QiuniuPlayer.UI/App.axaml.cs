using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using QiuniuPlayer.UI.ViewModels;
using QiuniuPlayer.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using QiuniuPlayer.UI.Services;
using Avalonia.Markup.Xaml.MarkupExtensions;
using QiuniuPlayer.UI.Common.Enums;
using Avalonia.Markup.Xaml.Styling;
using System;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using QiuniuPlayer.UI.Services.Storage;
using QiuniuPlayer.UI.Models;

namespace QiuniuPlayer.UI;

public partial class App : Application
{
    public static Db Store { get; private set; } = null!;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Store = new Db();
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 创建服务容器
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // 主题切换
            ThemeService.ThemeChanged += OnThemeChanged;
            var theme = await Store.GetConfig("Theme");
            Enum.TryParse(theme, out ThemeMode themeMode);
            ThemeService.CurrentTheme = themeMode;
            LoadThemeResources(themeMode);
            // 初始化语言管理器
            LocalizationService.CurrentCulture = await Store.GetConfig("Language") ?? "zh-CN";
            LocalizationService.Load();
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            //DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // 注册视图模型
        services.AddSingleton<MainWindowViewModel>();
    }

    private async void OnThemeChanged(ThemeMode themeMode)
    {
        await Store.SetConfig(ConfigKeys.Theme, themeMode.ToString());
        LoadThemeResources(themeMode);
    }

    private void LoadThemeResources(ThemeMode themeMode)
    {
        // 清空现有主题资源
        Resources.MergedDictionaries.Clear();
        Styles.Clear();
        Styles.Add(new FluentTheme());

        // 添加图标资源（始终需要）
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://QiuniuPlayer.UI"))
        {
            Source = new Uri("avares://QiuniuPlayer.UI/Assets/Icons.axaml")
        });
        // 添加颜色资源
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://QiuniuPlayer.UI"))
        {
            Source = new Uri($"avares://QiuniuPlayer.UI/Assets/Themes/{(themeMode == ThemeMode.Dark ? "Dark" : "Light")}Color.axaml")
        });

        // 添加主题样式资源
        Styles.Add(new StyleInclude(new Uri("avares://QiuniuPlayer.UI"))
        {
            Source = new Uri($"avares://QiuniuPlayer.UI/Assets/Themes/{(themeMode == ThemeMode.Dark ? "Dark" : "Light")}Style.axaml")
        });

        // 重新加载所有样式
        Current?.Styles.Add(new Style());  // 触发变更通知
        Current?.Styles.RemoveAt(Application.Current.Styles.Count - 1);
    }

}