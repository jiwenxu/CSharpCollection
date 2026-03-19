using System;

namespace QiuniuPlayer.UI.Models;

public class AudioFile
{
    public int Id { get; set; }
    public int SongCollectionId{ get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string CoverImagePath { get; set; } = string.Empty;
    public AudioSourceType SourceType { get; set; }
    public DateTime AddedDate { get; set; } = DateTime.Now;
}

public enum AudioSourceType
{
    Local,
    Network,
    Bilibili
}