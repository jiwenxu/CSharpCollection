

using System;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using QiuniuPlayer.UI.Common.Enums;
using QiuniuPlayer.UI.Models;

namespace QiuniuPlayer.UI.Services;

public static class ThemeService
{
    public static event Action<ThemeMode>? ThemeChanged;

    private static ThemeMode _currentTheme = ThemeMode.Dark;

    public static ThemeMode CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme == value)
                return;

            _currentTheme = value;
            _ = App.Store.SetConfig(ConfigKeys.Theme, value.ToString());
            ThemeChanged?.Invoke(value);
        }
    }
    
    public static void SwitchTheme()
    {
        CurrentTheme = CurrentTheme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
    }
}