using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Marmelad.Core;

public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static readonly Hotkey Default = new(ModifierKeys.Alt | ModifierKeys.Shift, Key.S);

    /// <summary>A bare letter would fire while typing, so only function keys may go without a modifier.</summary>
    public bool IsValid => Key != Key.None && !IsModifierKey(Key) && (Modifiers != ModifierKeys.None || IsFunctionKey(Key));

    /// <summary>Key caps for display, for example Alt, Shift, S.</summary>
    public IReadOnlyList<string> Parts
    {
        get
        {
            var parts = ModifierNames().ToList();
            parts.Add(DisplayName(Key));
            return parts;
        }
    }

    public string DisplayText => string.Join(" + ", Parts);

    public override string ToString() => string.Join("+", ModifierNames().Append(Key.ToString()));

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = ModifierKeys.None;
        var key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (key != Key.None || !Enum.TryParse(part, true, out key)) return false;
                    break;
            }
        }

        hotkey = new Hotkey(modifiers, key);
        return hotkey.IsValid;
    }

    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    private IEnumerable<string> ModifierNames()
    {
        if (Modifiers.HasFlag(ModifierKeys.Windows)) yield return "Win";
        if (Modifiers.HasFlag(ModifierKeys.Control)) yield return "Ctrl";
        if (Modifiers.HasFlag(ModifierKeys.Alt)) yield return "Alt";
        if (Modifiers.HasFlag(ModifierKeys.Shift)) yield return "Shift";
    }

    private static string DisplayName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + ((int)key - (int)Key.NumPad0),
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.Space => "Пробел",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Capital => "Caps Lock",
        Key.Prior => "Page Up",
        Key.Next => "Page Down",
        Key.Snapshot => "Print Screen",
        _ => key.ToString(),
    };
}
