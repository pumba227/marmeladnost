using System;
using Microsoft.Win32;

namespace Marmelad.Core;

public static class Autostart
{
    public const string BackgroundArgument = "--background";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Мармеладность";

    private static string Command => $"\"{Environment.ProcessPath}\" {BackgroundArgument}";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Keeps the entry valid after the program file has been moved.</summary>
    public static void RefreshPath()
    {
        if (IsEnabled) Set(true);
    }
}
