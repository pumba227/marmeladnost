using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Marmelad.Core;
using Marmelad.Native;
using Marmelad.Themes;

namespace Marmelad.Views;

public partial class SettingsWindow : Window
{
    private const string LanguageSettingsUri = "ms-settings:regionlanguage";

    private readonly AppSettings _settings;
    private readonly OcrService _ocr;
    private readonly ResidentHost _host;
    private bool _recording;

    public SettingsWindow(AppSettings settings, OcrService ocr, ResidentHost host)
    {
        _settings = settings;
        _ocr = ocr;
        _host = host;

        InitializeComponent();

        TargetCombo.ItemsSource = Languages.All;
        FallbackCombo.ItemsSource = Languages.All;
        AccentList.ItemsSource = Theme.Accents;
        SideCombo.ItemsSource = new[]
        {
            new Choice(PopupSide.Right, "Справа от области"),
            new Choice(PopupSide.Left, "Слева от области"),
            new Choice(PopupSide.Bottom, "Под областью"),
            new Choice(PopupSide.Top, "Над областью"),
        };
        AutoCloseCombo.ItemsSource = new[]
        {
            new Choice(0, "Не закрывать"),
            new Choice(5, "Через 5 секунд"),
            new Choice(10, "Через 10 секунд"),
            new Choice(20, "Через 20 секунд"),
            new Choice(30, "Через 30 секунд"),
        };

        RefreshOcrLanguages();
        DataContext = settings;

        AutostartSwitch.IsChecked = Autostart.IsEnabled;
        ShowHotkey();

        SourceInitialized += (_, _) =>
        {
            WindowEffects.SetDarkFrame(this, Theme.IsDark);
            FitToScreen();
        };
        Activated += (_, _) => RefreshOcrLanguages();
    }

    /// <summary>Tells the user about a problem found before the window was opened.</summary>
    public void ShowHotkeyError(string message)
    {
        HotkeyError.Text = message;
        HotkeyError.Visibility = Visibility.Visible;
    }

    private void FitToScreen()
    {
        var available = SystemParameters.WorkArea.Height;
        if (Height > available * 0.94) Height = available * 0.94;
    }

    private void RefreshOcrLanguages()
    {
        var codes = _ocr.InstalledLanguages;
        var installed = codes.Select(Languages.NameOf).ToList();

        var choices = installed.Count == 0
            ? new[] { new Choice(Languages.Auto, "Определять автоматически") }
            : codes
                .Select(code => new Choice(code, Languages.NameOf(code)))
                .Prepend(new Choice(Languages.Auto, "Определять автоматически"))
                .ToArray();

        var selected = _settings.OcrLanguage;
        OcrCombo.ItemsSource = choices;
        OcrCombo.SelectedValue = choices.Any(c => (string)c.Value == selected) ? selected : Languages.Auto;

        OcrInstalled.Text = installed.Count == 0
            ? "В Windows нет языков распознавания текста, добавьте хотя бы один."
            : $"Windows умеет читать: {string.Join(", ", installed)}. Для других алфавитов, например японского, нужен языковой пакет.";
    }

    private void ShowHotkey()
    {
        HotkeyCaps.ItemsSource = _settings.Hotkey.Parts;
        HotkeyCaps.Visibility = Visibility.Visible;
        HotkeyPrompt.Visibility = Visibility.Collapsed;
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recording) return;

        _recording = true;
        _host.UnregisterHotkey(); // Otherwise the current combination could not be pressed again.
        HotkeyError.Visibility = Visibility.Collapsed;
        HotkeyCaps.Visibility = Visibility.Collapsed;
        HotkeyPrompt.Visibility = Visibility.Visible;
        HotkeyButton.Focus();
    }

    private void HotkeyButton_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_recording) StopRecording();
    }

    private void StopRecording()
    {
        _recording = false;
        if (!_host.RegisterHotkey(_settings.Hotkey))
            ShowHotkeyError("Это сочетание занято другой программой. Выберите другое.");
        ShowHotkey();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_recording) return;

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }
        if (Hotkey.IsModifierKey(key)) return; // Wait for the main key of the combination.

        var modifiers = Keyboard.Modifiers;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= ModifierKeys.Windows;

        var candidate = new Hotkey(modifiers, key);
        if (!candidate.IsValid)
        {
            ShowHotkeyError("Добавьте Ctrl, Alt, Shift или Win, иначе клавиша будет срабатывать при обычном наборе.");
            return;
        }

        _recording = false;
        if (_host.RegisterHotkey(candidate))
        {
            _settings.HotkeyText = candidate.ToString();
            HotkeyError.Visibility = Visibility.Collapsed;
        }
        else
        {
            _host.RegisterHotkey(_settings.Hotkey);
            ShowHotkeyError($"Сочетание {candidate.DisplayText} занято другой программой. Выберите другое.");
        }
        ShowHotkey();
    }

    private void AddLanguage_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(LanguageSettingsUri) { UseShellExecute = true });

    private void AutostartSwitch_Click(object sender, RoutedEventArgs e) =>
        Autostart.Set(AutostartSwitch.IsChecked == true);

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
