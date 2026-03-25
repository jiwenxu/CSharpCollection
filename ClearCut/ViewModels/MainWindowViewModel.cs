using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ClearCut.Models;
using ClearCut.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;

namespace ClearCut.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int MaxLogLength = 2000;
    private readonly IFfmpegService _ffmpegService;
    private readonly ICommandService _commandService;
    private readonly LibVLC _libVlc = new();
    public MediaPlayer MediaPlayer { get; }
    private Media? _currentMedia;
    public LibVLC LibVLC
    {
        get { return _libVlc; }
    }

    [ObservableProperty]
    private string? inputFilePath;

    [ObservableProperty]
    private string mediaInfoText = "未选择文件";

    [ObservableProperty]
    private VideoQualityPreset selectedQuality = VideoQualityPreset.Medium;

    [ObservableProperty]
    private string videoAdvancedArgs = string.Empty;

    [ObservableProperty]
    private string audioAdvancedArgs = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = "就绪";

    [ObservableProperty]
    private string lastOutputPath = string.Empty;

    [ObservableProperty]
    private string logText = string.Empty;

    [ObservableProperty]
    private Bitmap? previewBitmap;

    [ObservableProperty]
    private string previewTitle = "暂无预览";
    [ObservableProperty]
    private bool isControlVisible = true;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private double volume = 80;

    [ObservableProperty]
    private string currentTimeText = "00:00";

    [ObservableProperty]
    private string totalTimeText = "00:00";
    [ObservableProperty]
    private string playButtonText = "▶";

    public MainWindowViewModel()
        : this(new FfmpegService(), new CommandService())
    {
    }

    public MainWindowViewModel(IFfmpegService ffmpegService, ICommandService commandService)
    {
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        MediaPlayer = new MediaPlayer(_libVlc);
    }

    public string InputDisplayName => string.IsNullOrWhiteSpace(InputFilePath)
        ? "点击或拖拽 MP4(H.264) 文件到这里"
        : Path.GetFileName(InputFilePath);

    public IReadOnlyList<VideoQualityPreset> QualityOptions { get; } = Enum.GetValues<VideoQualityPreset>();

    public bool HasInput => !string.IsNullOrWhiteSpace(InputFilePath);

    public bool CanRunActions => HasInput && !IsBusy;

    public bool CanPickFile => !IsBusy;

    public bool HasPreview => PreviewBitmap is not null;

    public async Task LoadInputFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            StatusText = "文件不存在。";
            return;
        }

        if (!string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "仅支持 .mp4 文件。";
            return;
        }

        IsBusy = true;
        StatusText = "正在解析媒体信息...";
        LogText = string.Empty;

        try
        {
            var mediaInfo = await _ffmpegService.ProbeAsync(path);
            if (mediaInfo.Duration == TimeSpan.Zero)
            {
                StatusText = "解析失败：无法读取时长。";
                return;
            }

            InputFilePath = path;
            MediaInfoText = $"时长 {mediaInfo.Duration:hh\\:mm\\:ss} | 分辨率 {mediaInfo.Width}x{mediaInfo.Height}";
            StatusText = "文件已加载，可以开始预览处理。";
            await RefreshVideoPreviewAsync(path, "原视频预览");
        }
        catch (Exception ex)
        {
            StatusText = "解析失败，请确认文件是 MP4(H.264)。";
            LogText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshUiState(includeInputState: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task CompressPreviewAsync() => await RunCompressAsync(preview10Seconds: true);

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task CompressFullAsync() => await RunCompressAsync(preview10Seconds: false);

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task ExtractPreviewAsync() => await RunExtractAudioAsync(preview10Seconds: true);

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task ExtractFullAsync() => await RunExtractAudioAsync(preview10Seconds: false);

    partial void OnIsBusyChanged(bool value)
    {
        RefreshUiState();
    }

    partial void OnInputFilePathChanged(string? value)
    {
        RefreshUiState(includeInputState: true);
    }

    private async Task RunCompressAsync(bool preview10Seconds)
    {
        if (string.IsNullOrWhiteSpace(InputFilePath))
        {
            return;
        }

        IsBusy = true;
        var outputPath = !preview10Seconds ? BuildCompressedOutputPath(InputFilePath, SelectedQuality, preview10Seconds)
        : Path.Combine(AppContext.BaseDirectory, "temp", "temp.mp4");
        StatusText = preview10Seconds ? "正在压缩前10秒预览..." : "正在压缩完整视频...";
        await _commandService.CleanupTempFilesAsync();
        try
        {
            var result = await _ffmpegService.CompressVideoAsync(
                InputFilePath,
                outputPath,
                SelectedQuality,
                preview10Seconds,
                VideoAdvancedArgs);

            if (!result.IsSuccess)
            {
                StatusText = "压缩失败，请查看日志。";
                LogText = result.StandardError;
                return;
            }

            LastOutputPath = outputPath;
            StatusText = preview10Seconds ? "压缩预览完成。" : "完整压缩完成。";
            LogText = TruncateLog(result.StandardError);
            await RefreshVideoPreviewAsync(outputPath, preview10Seconds ? "压缩预览（前10秒）" : "压缩完整视频结果");
            if (preview10Seconds)
            {
                Play(outputPath);
            }
        }
        catch (Exception ex)
        {
            StatusText = "压缩失败。";
            LogText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunExtractAudioAsync(bool preview10Seconds)
    {
        if (string.IsNullOrWhiteSpace(InputFilePath))
        {
            return;
        }

        IsBusy = true;
        var outputPath = BuildAudioOutputPath(InputFilePath, preview10Seconds);
        StatusText = preview10Seconds ? "正在提取前10秒音频..." : "正在提取完整音频...";

        try
        {
            var result = await _ffmpegService.ExtractAudioAsync(
                InputFilePath,
                outputPath,
                preview10Seconds,
                AudioAdvancedArgs);

            if (!result.IsSuccess)
            {
                StatusText = "提取失败，请查看日志。";
                LogText = result.StandardError;
                return;
            }

            LastOutputPath = outputPath;
            StatusText = preview10Seconds ? "音频预览提取完成。" : "完整音频提取完成。";
            LogText = TruncateLog(result.StandardError);
            await RefreshAudioPreviewAsync(outputPath, preview10Seconds ? "音频预览（前10秒）" : "完整音频结果");
        }
        catch (Exception ex)
        {
            StatusText = "提取失败。";
            LogText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildCompressedOutputPath(string inputPath, VideoQualityPreset preset, bool preview10Seconds)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? AppContext.BaseDirectory;
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var quality = preset.ToString().ToLower(CultureInfo.InvariantCulture);
        var suffix = preview10Seconds ? $"_compressed_{quality}_preview10" : $"_compressed_{quality}";
        return Path.Combine(dir, $"{fileName}{suffix}.mp4");
    }

    private static string BuildAudioOutputPath(string inputPath, bool preview10Seconds)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? AppContext.BaseDirectory;
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var suffix = preview10Seconds ? "_audio_preview10" : "_audio";
        return Path.Combine(dir, $"{fileName}{suffix}.mp3");
    }

    private static string TruncateLog(string text)
    {
        return text.Length <= MaxLogLength
            ? text
            : text[..MaxLogLength] + Environment.NewLine + "...(日志已截断)";
    }

    private async Task RefreshVideoPreviewAsync(string videoPath, string title)
    {
        var imagePath = await _ffmpegService.CaptureVideoPreviewFrameAsync(videoPath);
        SetPreviewBitmapFromPath(imagePath, title);
    }

    private async Task RefreshAudioPreviewAsync(string audioPath, string title)
    {
        var imagePath = await _ffmpegService.CaptureAudioWaveformAsync(audioPath);
        SetPreviewBitmapFromPath(imagePath, title);
    }

    private void SetPreviewBitmapFromPath(string? imagePath, string title)
    {
        PreviewBitmap?.Dispose();
        PreviewBitmap = null;

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            PreviewTitle = "暂无预览";
            OnPropertyChanged(nameof(HasPreview));
            return;
        }

        using var stream = File.OpenRead(imagePath);
        PreviewBitmap = new Bitmap(stream);
        PreviewTitle = title;
        OnPropertyChanged(nameof(HasPreview));
    }

    private void RefreshUiState(bool includeInputState = false)
    {
        if (includeInputState)
        {
            OnPropertyChanged(nameof(InputDisplayName));
            OnPropertyChanged(nameof(HasInput));
        }

        OnPropertyChanged(nameof(CanRunActions));
        OnPropertyChanged(nameof(CanPickFile));
        OnPropertyChanged(nameof(HasPreview));
        NotifyActionCommandsChanged();
    }

    private void NotifyActionCommandsChanged()
    {
        CompressPreviewCommand.NotifyCanExecuteChanged();
        CompressFullCommand.NotifyCanExecuteChanged();
        ExtractPreviewCommand.NotifyCanExecuteChanged();
        ExtractFullCommand.NotifyCanExecuteChanged();
    }

    public void Play(string videoPath)
    {
        if (Design.IsDesignMode)
        {
            return;
        }
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, new Uri(videoPath));
        MediaPlayer.Play(_currentMedia);
    }
    public void PlayPause()
    {
        if (MediaPlayer.IsPlaying)
        {
            MediaPlayer.Pause();
        }
        else if (MediaPlayer.Media != null)
        {
            MediaPlayer.Play();
        }
    }
    public void Stop()
    {
        MediaPlayer.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;
    }
    public void Dispose()
    {
        MediaPlayer.Stop();
        MediaPlayer.Dispose();
        _currentMedia?.Dispose();
        _libVlc.Dispose();
        GC.SuppressFinalize(this);
    }
}
