using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Avalonia.Platform;
using QiuniuPlayer.UI.Models;

namespace QiuniuPlayer.UI.Services;

public static class LocalizationService
{
    private static Dictionary<string, string> _dict = new();
    public static string CurrentCulture = "zh-CN";

    public static event Action? LanguageChanged;

    public static void SwitchLanguage()
    {
        CurrentCulture = CurrentCulture == "zh-CN"
            ? "en-US"
            : "zh-CN";
        _ = App.Store.SetConfig(ConfigKeys.Language, CurrentCulture);
        Load();
        LanguageChanged?.Invoke();
    }
    public static void Load()
    {
        var uri = new Uri($"avares://{Assembly.GetExecutingAssembly().GetName().Name}" +
        $"/Assets/i18n/{CurrentCulture}.json");
        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        _dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();

        LanguageChanged?.Invoke();
    }

    public static string T(string key)
        => _dict.TryGetValue(key, out var value) ? value : key;
}