using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Marmelad.Core;

namespace Marmelad.Themes;

public sealed record AccentOption(string Name, string Hex)
{
    public Brush Brush { get; } = Frozen((Color)ColorConverter.ConvertFromString(Hex));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public static class Theme
{
    private sealed record Palette(
        string Window, string Card, string Control, string Hover, string Border, string Text, string Muted);

    private static readonly Palette Dark = new(
        "#1D1A19", "#272321", "#332E2B", "#3D3734", "#3A3431", "#F5F0EB", "#AAA098");

    private static readonly Palette Light = new(
        "#F7F2ED", "#FFFFFF", "#F0E9E2", "#E8DFD7", "#E4DBD2", "#26211E", "#6E645C");

    public static IReadOnlyList<AccentOption> Accents { get; } = new AccentOption[]
    {
        new("Мандарин", "#F2811D"),
        new("Малина", "#E5396F"),
        new("Слива", "#8E5BE8"),
        new("Черника", "#3D7BF0"),
        new("Мята", "#12A594"),
        new("Лайм", "#6FA512"),
    };

    public static bool IsDark { get; private set; }

    public static void Apply(AppSettings settings)
    {
        IsDark = SystemTheme.IsDark(settings.ThemeMode);

        var palette = IsDark ? Dark : Light;
        var accent = ParseAccent(settings.Accent);
        var resources = Application.Current.Resources;

        resources["Brush.Window"] = Solid(Parse(palette.Window));
        resources["Brush.Card"] = Solid(Parse(palette.Card));
        resources["Brush.Control"] = Solid(Parse(palette.Control));
        resources["Brush.Hover"] = Solid(Parse(palette.Hover));
        resources["Brush.Border"] = Solid(Parse(palette.Border));
        resources["Brush.Text"] = Solid(Parse(palette.Text));
        resources["Brush.Muted"] = Solid(Parse(palette.Muted));
        resources["Brush.Accent"] = Solid(accent);
        resources["Brush.AccentSoft"] = Solid(Color.FromArgb(0x38, accent.R, accent.G, accent.B));
        resources["Brush.OnAccent"] = Solid(Luminance(accent) > 0.55 ? Parse("#1D1A19") : Colors.White);

        var popup = Parse(palette.Window);
        var alpha = (byte)Math.Round(255 * (1 - settings.Transparency / 100.0));
        resources["Brush.PopupBg"] = Solid(Color.FromArgb(alpha, popup.R, popup.G, popup.B));

        var backdrop = new LinearGradientBrush(accent, Darken(accent), 35);
        backdrop.Freeze();
        resources["Brush.PreviewBackdrop"] = backdrop;
    }

    private static Color ParseAccent(string hex)
    {
        try
        {
            return Parse(hex);
        }
        catch (FormatException)
        {
            return Parse(Accents[0].Hex);
        }
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    private static Color Darken(Color c) => Color.FromRgb((byte)(c.R * 0.45), (byte)(c.G * 0.45), (byte)(c.B * 0.45));
}
