namespace ClearCut.Models;

public sealed class FfmpegProcessResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public bool IsSuccess => ExitCode == 0;
}
