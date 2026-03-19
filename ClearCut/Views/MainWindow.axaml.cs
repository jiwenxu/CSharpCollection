using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClearCut.ViewModels;

namespace ClearCut.Views;

public partial class MainWindow : Window
{
    private bool _isFullscreen = false;
    private bool _isDragging = false;
    private WindowState _previousState;

    public MainWindow()
    {
        InitializeComponent();
        this.Loaded += MainWindow_Loaded;
        this.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape && _isFullscreen)
            {
                ToggleFullscreen();
            }
        };
        // 获取 Slider 控件引用（需要在 axaml 中给 Slider 添加 x:Name）
        var seekSlider = this.FindControl<Slider>("SeekSlider");
        if (seekSlider != null)
        {
            // 注册隧道事件，在事件冒泡前捕获
            seekSlider.AddHandler(InputElement.PointerPressedEvent, OnSeekStart, RoutingStrategies.Tunnel);
            seekSlider.AddHandler(InputElement.PointerReleasedEvent, OnSeekEnd, RoutingStrategies.Tunnel);
        }
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        // 确保 VideoView 在加载后绑定 MediaPlayer
        if (DataContext is MainWindowViewModel vm)
        {
            VideoView.MediaPlayer = vm.MediaPlayer;
        }

        DispatcherTimer _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _timer.Tick += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm && !_isDragging)
            {
                var mp = vm.MediaPlayer;

                vm.Progress = mp.Position * 100;

                vm.CurrentTimeText = TimeSpan.FromMilliseconds(mp.Time).ToString(@"mm\:ss");
                vm.TotalTimeText = TimeSpan.FromMilliseconds(mp.Length).ToString(@"mm\:ss");
            }
        };

        _timer.Start();
    }

    private async void OnPickFileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "选择 MP4 文件",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MP4 Video")
                {
                    Patterns = new[] { "*.mp4" }
                }
            }
        });

        var first = files.FirstOrDefault();
        var localPath = first?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await vm.LoadInputFileAsync(localPath);
        }
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !e.DataTransfer.Contains(DataFormat.File))
        {
            return;
        }

        var files = e.DataTransfer.GetItems(DataFormat.File);
        var file = files?.OfType<IStorageFile>().FirstOrDefault();
        var localPath = file?.TryGetLocalPath();

        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await vm.LoadInputFileAsync(localPath);
        }
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Video double tapped.");
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _previousState = this.WindowState;
            this.WindowState = WindowState.FullScreen;
            _isFullscreen = true;
        }
        else
        {
            this.WindowState = _previousState;
            _isFullscreen = false;
        }
    }

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        var player = VideoView?.MediaPlayer;
        if (player != null)
        {
            if (player.IsPlaying)
            {
                player.Pause();
            }
            else if (player.Media != null)
            {
                player.Play();
            }
        }
    }

    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.MediaPlayer.Volume = (int)e.NewValue;
        }
    }
    private bool _wasPlaying = false;
    private void OnSeekStart(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            _wasPlaying = vm.MediaPlayer.IsPlaying;
            vm.MediaPlayer.Pause(); // ⛔ 先暂停
        }
        Console.WriteLine($"Seek start");
        _isDragging = true;
    }

    private void OnSeekEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var mp = vm.MediaPlayer;

            var targetTime = (long)(vm.Progress / 100.0 * mp.Length);

            Console.WriteLine($"Seek end {targetTime}");

            mp.Time = targetTime; // ✅ 更稳

            if (_wasPlaying)
            {
                mp.Play(); // ▶ 恢复播放
            }
        }

        _isDragging = false;
    }
}