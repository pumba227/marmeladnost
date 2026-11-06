using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marmelad.Core;

public enum AppTheme { System, Dark, Light }

public enum PopupSide { Right, Left, Bottom, Top }

public sealed class AppSettings : INotifyPropertyChanged
{
    public const int MinTextSize = 12;
    public const int MaxTextSize = 24;
    public const int MaxTransparency = 60;

    private string _hotkeyText = Hotkey.Default.ToString();
    private string _targetLanguage = "ru";
    private string _fallbackLanguage = "en";
    private string _ocrLanguage = Languages.Auto;
    private AppTheme _themeMode = AppTheme.System;
    private string _accent = "#F2811D";
    private int _textSize = 15;
    private int _transparency = 15;
    private PopupSide _popupSide = PopupSide.Right;
    private bool _showSource = true;
    private bool _closeOnClickOutside = true;
    private int _autoCloseSeconds;
    private bool _autoCopy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string HotkeyText { get => _hotkeyText; set => Set(ref _hotkeyText, value); }
    public string TargetLanguage { get => _targetLanguage; set => Set(ref _targetLanguage, value); }
    public string FallbackLanguage { get => _fallbackLanguage; set => Set(ref _fallbackLanguage, value); }
    public string OcrLanguage { get => _ocrLanguage; set => Set(ref _ocrLanguage, value); }
    public AppTheme ThemeMode { get => _themeMode; set => Set(ref _themeMode, value); }
    public string Accent { get => _accent; set => Set(ref _accent, value); }
    public int TextSize { get => _textSize; set => Set(ref _textSize, value); }
    public int Transparency { get => _transparency; set => Set(ref _transparency, value); }
    public PopupSide PopupSide { get => _popupSide; set => Set(ref _popupSide, value); }
    public bool ShowSource { get => _showSource; set => Set(ref _showSource, value); }
    public bool CloseOnClickOutside { get => _closeOnClickOutside; set => Set(ref _closeOnClickOutside, value); }
    public int AutoCloseSeconds { get => _autoCloseSeconds; set => Set(ref _autoCloseSeconds, value); }
    public bool AutoCopy { get => _autoCopy; set => Set(ref _autoCopy, value); }

    [JsonIgnore]
    public Hotkey Hotkey => Hotkey.TryParse(HotkeyText, out var hotkey) ? hotkey : Hotkey.Default;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>A hand-edited or outdated file must not break the app.</summary>
    public void Normalize()
    {
        if (!Hotkey.TryParse(HotkeyText, out _)) HotkeyText = Hotkey.Default.ToString();
        TextSize = Math.Clamp(TextSize, MinTextSize, MaxTextSize);
        Transparency = Math.Clamp(Transparency, 0, MaxTransparency);
        AutoCloseSeconds = Math.Max(0, AutoCloseSeconds);
    }
}

/// <summary>Generated at build time, so reading settings needs no reflection at startup.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;

public static class SettingsStore
{
    public static bool Exists => File.Exists(AppPaths.SettingsFile);

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (Exists)
                settings = JsonSerializer.Deserialize(File.ReadAllText(AppPaths.SettingsFile), SettingsJson.Default.AppSettings) ?? settings;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Настройки не прочитаны, используются значения по умолчанию", error);
        }

        settings.Normalize();
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);

            // Through a temporary file, so a crash mid-write cannot leave a broken settings file.
            var temporary = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings));
            File.Move(temporary, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error("Настройки не сохранены", error);
        }
    }
}
