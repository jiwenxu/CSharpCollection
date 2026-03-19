using System;

namespace ClearCut.Models;

public sealed class MediaInfo
{
    public TimeSpan Duration { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}
