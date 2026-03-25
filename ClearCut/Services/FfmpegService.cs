using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClearCut.Models;

namespace ClearCut.Services;

public sealed class FfmpegService : IFfmpegService
{
    private static readonly Regex DurationRegex = new(@"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled);
    private static readonly Regex ResolutionRegex = new(@"\b(\d{2,5})x(\d{2,5})\b", RegexOptions.Compiled);
    private static readonly SemaphoreSlim PermissionLock = new(1, 1);
    private static bool _permissionEnsured;
    private readonly ITempPathService _tempPathService;

    public FfmpegService()
        : this(new TempPathService())
    {
    }

    public FfmpegService(ITempPathService tempPathService)
    {
        _tempPathService = tempPathService ?? throw new ArgumentNullException(nameof(tempPathService));
    }

    public async Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var args = $"-hide_banner -i \"{inputPath}\"";
        var result = await ExecuteProcessAsync(args, cancellationToken);
        var probeText = $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";

        var duration = ParseDuration(probeText);
        var resolution = ParseResolution(probeText);

        if (duration == TimeSpan.Zero && resolution == (0, 0))
        {
            throw new InvalidOperationException("无法解析输入媒体信息，请确认是 MP4(H.264) 文件。");
        }

        return new MediaInfo
        {
            Duration = duration,
            Width = resolution.width,
            Height = resolution.height
        };
    }

    public Task<FfmpegProcessResult> CompressVideoAsync(
        string inputPath,
        string outputPath,
        VideoQualityPreset preset,
        bool preview10Seconds,
        string? advancedArgs,
        CancellationToken cancellationToken = default)
    {
        var (crf, x264Preset) = preset switch
        {
            VideoQualityPreset.High => (23, "fast"),
            VideoQualityPreset.Medium => (26, "medium"),
            VideoQualityPreset.Low => (28, "slower"),
            _ => (26, "medium")
        };

        var args = BuildCompressArguments(
            inputPath,
            outputPath,
            x264Preset,
            crf,
            preview10Seconds,
            advancedArgs);
        return ExecuteProcessAsync(args, cancellationToken);
    }

    public Task<FfmpegProcessResult> ExtractAudioAsync(
        string inputPath,
        string outputPath,
        bool preview10Seconds,
        string? advancedArgs,
        CancellationToken cancellationToken = default)
    {
        var args = BuildExtractAudioArguments(inputPath, outputPath, preview10Seconds, advancedArgs);

        return ExecuteProcessAsync(args, cancellationToken);
    }

    public async Task<string?> CaptureVideoPreviewFrameAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        var outputImagePath = BuildTempPreviewPath("video_preview", ".jpg");
        var args = $"-y -hide_banner -ss 00:00:00.5 -i \"{inputPath}\" -frames:v 1 -q:v 2 \"{outputImagePath}\"";
        var result = await ExecuteProcessAsync(args, cancellationToken);
        return result.IsSuccess && File.Exists(outputImagePath) ? outputImagePath : null;
    }

    public async Task<string?> CaptureAudioWaveformAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        var outputImagePath = BuildTempPreviewPath("audio_waveform", ".png");
        var args = $"-y -hide_banner -i \"{inputPath}\" -filter_complex \"showwavespic=s=1000x220:colors=#2F6FEB\" -frames:v 1 \"{outputImagePath}\"";
        var result = await ExecuteProcessAsync(args, cancellationToken);
        return result.IsSuccess && File.Exists(outputImagePath) ? outputImagePath : null;
    }

    private static TimeSpan ParseDuration(string text)
    {
        var match = DurationRegex.Match(text);
        if (!match.Success)
        {
            return TimeSpan.Zero;
        }

        var hours = int.Parse(match.Groups[1].Value);
        var minutes = int.Parse(match.Groups[2].Value);
        var seconds = int.Parse(match.Groups[3].Value);
        var centiseconds = int.Parse(match.Groups[4].Value);

        return new TimeSpan(0, hours, minutes, seconds, centiseconds * 10);
    }

    private static (int width, int height) ParseResolution(string text)
    {
        foreach (Match match in ResolutionRegex.Matches(text))
        {
            if (!match.Success)
            {
                continue;
            }

            var width = int.Parse(match.Groups[1].Value);
            var height = int.Parse(match.Groups[2].Value);

            if (width >= 160 && height >= 120)
            {
                return (width, height);
            }
        }

        return (0, 0);
    }

    private async Task<FfmpegProcessResult> ExecuteProcessAsync(string args, CancellationToken cancellationToken)
    {
        var ffmpegPath = ResolveFfmpegPath();
        await EnsureExecutablePermissionsAsync(ffmpegPath, cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        process.Exited += (_, _) => exitTcs.TrySetResult(process.ExitCode);

        if (!process.Start())
        {
            throw new InvalidOperationException("FFmpeg 进程启动失败。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var ctr = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore cancellation race
            }
        });

        var exitCode = await exitTcs.Task.ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new FfmpegProcessResult
        {
            ExitCode = exitCode,
            StandardOutput = stdoutBuilder.ToString(),
            StandardError = stderrBuilder.ToString()
        };
    }

    private static string BuildCompressArguments(
        string inputPath,
        string outputPath,
        string x264Preset,
        int crf,
        bool preview10Seconds,
        string? advancedArgs)
    {
        var preview = BuildPreviewArgument(preview10Seconds);
        var extra = BuildAdvancedArguments(advancedArgs);
        return $"-y -hide_banner -i \"{inputPath}\" {preview}-c:v libx264 -preset {x264Preset} -crf {crf} -c:a aac -b:a 128k -movflags +faststart{extra} \"{outputPath}\"";
    }

    private static string BuildExtractAudioArguments(
        string inputPath,
        string outputPath,
        bool preview10Seconds,
        string? advancedArgs)
    {
        var preview = BuildPreviewArgument(preview10Seconds);
        var extra = BuildAdvancedArguments(advancedArgs);
        return $"-y -hide_banner -i \"{inputPath}\" {preview}-vn -codec:a libmp3lame -q:a 2{extra} \"{outputPath}\"";
    }

    private static string BuildPreviewArgument(bool preview10Seconds)
    {
        return preview10Seconds ? "-t 10 " : string.Empty;
    }

    private static string BuildAdvancedArguments(string? advancedArgs)
    {
        return string.IsNullOrWhiteSpace(advancedArgs) ? string.Empty : $" {advancedArgs.Trim()}";
    }

    private static async Task EnsureExecutablePermissionsAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (_permissionEnsured)
        {
            return;
        }

        await PermissionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_permissionEnsured)
            {
                return;
            }

            using var chmodProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x \"{ffmpegPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (chmodProcess is null)
            {
                throw new InvalidOperationException("无法设置 FFmpeg 可执行权限。");
            }

            await chmodProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            _permissionEnsured = true;
        }
        finally
        {
            PermissionLock.Release();
        }
    }

    private static string ResolveFfmpegPath()
    {
        var baseDir = AppContext.BaseDirectory;

        string path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(baseDir, "Binaries", "ffmpeg", "win-x64", "ffmpeg.exe")
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Path.Combine(baseDir, "Binaries", "ffmpeg", "osx-universal", "ffmpeg")
                : throw new PlatformNotSupportedException("当前 MVP 仅支持 Windows 与 macOS。");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"未找到 FFmpeg 二进制文件：{path}");
        }

        return path;
    }

    private string BuildTempPreviewPath(string prefix, string extension)
    {
        var dir = _tempPathService.GetPreviewDirectory();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{prefix}_{Guid.NewGuid():N}{extension}");
    }
}
