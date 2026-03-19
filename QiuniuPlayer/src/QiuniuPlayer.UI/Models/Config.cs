
using Avalonia.Input;

namespace QiuniuPlayer.UI.Models;

public partial class Config
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}

public partial class ConfigKeys
{
    public const string Theme = "Theme";
    public const string Language = "Language";
    public const string DownloadFolder = "DownloadFolder";
}