using Microsoft.Win32;

namespace Marmelad.Core;

/// <summary>Kept free of WPF types: the tray menu needs this before WPF is loaded.</summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark(AppTheme mode) => mode switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => WindowsUsesDarkTheme(),
    };

    private static bool WindowsUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int and 0;
    }
}
