using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using ClearCut.Services;
using ClearCut.ViewModels;
using ClearCut.Views;
using System.Runtime.InteropServices;
using ClearCut.Environments;

namespace ClearCut;

public partial class App : Application
{
    private ITempPathService? _tempPathService;
    private IFfmpegService? _ffmpegService;
    private ICommandService? _commandService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _tempPathService = new TempPathService();
        _ffmpegService = new FfmpegService(_tempPathService);
        _commandService = new CommandService(_tempPathService);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            EnvironmentInitialization();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(
                    _ffmpegService ?? new FfmpegService(),
                    _commandService ?? new CommandService()),
            };

            desktop.ShutdownRequested += async (_, _) =>
            {
                if (_commandService is not null)
                {
                    await _commandService.CleanupTempFilesAsync().ConfigureAwait(false);
                }
            };
            desktop.Exit += (_, __) => EnvironmentCleanup();
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

    private static void EnvironmentInitialization()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            LinuxApi.Initialize();
        }
    }

    private static void EnvironmentCleanup()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            LinuxApi.Shutdown();
        }
    }
}