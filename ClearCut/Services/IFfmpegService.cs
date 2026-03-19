using System.Threading;
using System.Threading.Tasks;
using ClearCut.Models;

namespace ClearCut.Services;

public interface IFfmpegService
{
    Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default);

    Task<FfmpegProcessResult> CompressVideoAsync(
        string inputPath,
        string outputPath,
        VideoQualityPreset preset,
        bool preview10Seconds,
        string? advancedArgs,
        CancellationToken cancellationToken = default);

    Task<FfmpegProcessResult> ExtractAudioAsync(
        string inputPath,
        string outputPath,
        bool preview10Seconds,
        string? advancedArgs,
        CancellationToken cancellationToken = default);

    Task<string?> CaptureVideoPreviewFrameAsync(
        string inputPath,
        CancellationToken cancellationToken = default);

    Task<string?> CaptureAudioWaveformAsync(
        string inputPath,
        CancellationToken cancellationToken = default);
}
