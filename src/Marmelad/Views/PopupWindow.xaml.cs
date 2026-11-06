using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Marmelad.Core;
using Marmelad.Native;
using Marmelad.Themes;
using Marmelad.Translators;

namespace Marmelad.Views;

/// <summary>The small card next to the selection: recognised text, translation, dictionary.</summary>
public partial class PopupWindow : Window
{
    private const int GapFromSelection = 12;
    private const string CopyGlyph = "";
    private const string CopiedGlyph = "";

    private static readonly TimeSpan EditDelay = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan NoTextCloseDelay = TimeSpan.FromSeconds(4);

    private readonly AppSettings _settings;
    private readonly PxRect _anchor;
    private readonly PxRect _workArea;
    private readonly IntPtr _previousWindow;
    private readonly Func<string, string, CancellationToken, Task<Translation>> _translate;
    private readonly DispatcherTimer _editTimer = new() { Interval = EditDelay };
    private readonly DispatcherTimer _closeTimer = new();
    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private readonly CancellationTokenSource _lifetime = new();

    private CancellationTokenSource? _request;
    private Func<Task>? _retry;
    private string _languageHint = Languages.Auto;
    private bool _userMoved;
    private bool _settingText;
    private bool _closing;

    public PopupWindow(
        AppSettings settings,
        PxRect anchor,
        PxRect workArea,
        IntPtr previousWindow,
        Func<string, string, CancellationToken, Task<Translation>> translate)
    {
        InitializeComponent();

        _settings = settings;
        _anchor = anchor;
        _workArea = workArea;
        _previousWindow = previousWindow;
        _translate = translate;

        SourceBox.FontSize = Math.Round(settings.TextSize * 0.88);
        ResultBox.FontSize = settings.TextSize + 1;

        _editTimer.Tick += (_, _) => OnEditSettled();
        _closeTimer.Tick += (_, _) => OnCloseTimer();
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            CopyButton.Content = CopyGlyph;
        };

        SourceInitialized += (_, _) => PrepareWindow();
        SizeChanged += (_, _) => Reposition();
        Deactivated += (_, _) => OnDeactivated();
        Loaded += (_, _) => PlayEntrance();
    }

    /// <summary>Cancelled when the window closes; work for it should stop then.</summary>
    public CancellationToken Lifetime => _lifetime.Token;

    public void ShowRecognized(string text, string languageHint)
    {
        _languageHint = languageHint;

        _settingText = true;
        SourceBox.Text = text;
        _settingText = false;

        SourceLanguage.Text = "Перевожу…";

        if (_settings.ShowSource)
        {
            SourceBox.Visibility = Visibility.Visible;
            Divider.Visibility = Visibility.Visible;
        }
    }

    public void ShowTranslation(Translation translation)
    {
        StopSkeleton();
        MessagePanel.Visibility = Visibility.Collapsed;

        SourceLanguage.Text = Languages.NameOf(translation.SourceLanguage);
        TargetLanguage.Text = Languages.NameOf(translation.TargetLanguage);
        LanguageArrow.Visibility = Visibility.Visible;

        ResultBox.Text = translation.Text;
        ResultBox.Visibility = Visibility.Visible;

        DictionaryList.ItemsSource = translation.Dictionary;
        DictionaryList.Visibility = translation.Dictionary.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var reserve = translation.Provider != "Google";
        ProviderNote.Text = reserve ? $"Google не ответил, перевёл {translation.Provider}" : "";
        ProviderNote.Visibility = reserve ? Visibility.Visible : Visibility.Collapsed;

        if (_settings.AutoCopy) CopyResult();
        StartCloseTimer(_settings.AutoCloseSeconds);
    }

    public void ShowNoText(string hint)
    {
        ShowMessage("Текст не найден", hint, retry: null);
        StartCloseTimer(NoTextCloseDelay.TotalSeconds);
    }

    public void ShowFailure(string title, string details, Func<Task> retry) => ShowMessage(title, details, retry);

    private void ShowMessage(string title, string details, Func<Task>? retry)
    {
        StopSkeleton();
        SourceLanguage.Text = "Мармеладность";
        LanguageArrow.Visibility = Visibility.Collapsed;
        ResultBox.Visibility = Visibility.Collapsed;
        DictionaryList.Visibility = Visibility.Collapsed;
        ProviderNote.Visibility = Visibility.Collapsed;

        MessageTitle.Text = title;
        MessageText.Text = details;
        MessagePanel.Visibility = Visibility.Visible;

        _retry = retry;
        RetryButton.Visibility = retry is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PrepareWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // Open on the selection's monitor at once, so the first layout already uses its DPI.
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, _anchor.X, _anchor.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        WindowEffects.SetRoundedCorners(this);
        WindowEffects.SetDarkFrame(this, Theme.IsDark);
        WindowEffects.SetAcrylic(this, _settings.Transparency > 0);

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var available = _workArea.Height / scale;
        SourceBox.MaxHeight = available * 0.2;
        ResultBox.MaxHeight = available * 0.45;
    }

    private void Reposition()
    {
        if (_userMoved) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var gap = (int)Math.Round(GapFromSelection * dpi.DpiScaleX);

        var (x, y) = PopupPlacement.Place(_anchor, width, height, _workArea, _settings.PopupSide, gap);
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void PlayEntrance()
    {
        ((Storyboard)Resources["Pulse"]).Begin(this, isControllable: true);

        var ease = (IEasingFunction)FindResource("Ease.Out");
        var duration = TimeSpan.FromMilliseconds(150);
        Body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        BodyShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, duration) { EasingFunction = ease });
    }

    private void StopSkeleton()
    {
        ((Storyboard)Resources["Pulse"]).Stop(this);
        Skeleton.Visibility = Visibility.Collapsed;
    }

    private void StartCloseTimer(double seconds)
    {
        _closeTimer.Stop();
        if (seconds <= 0 || PinButton.IsChecked == true) return;

        _closeTimer.Interval = TimeSpan.FromSeconds(seconds);
        _closeTimer.Start();
    }

    private void OnCloseTimer()
    {
        // Someone reading or editing the card should not lose it mid-way.
        if (IsMouseOver || SourceBox.IsKeyboardFocused) return;
        Close();
    }

    private void OnDeactivated()
    {
        if (_settings.CloseOnClickOutside && PinButton.IsChecked != true && !_closing)
            Dispatcher.BeginInvoke(Close);
    }

    private void SourceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingText) return;
        _closeTimer.Stop();
        _editTimer.Stop();
        _editTimer.Start();
    }

    private async void OnEditSettled()
    {
        _editTimer.Stop();
        var text = SourceBox.Text.Trim();
        if (text.Length > 0) await RequestAsync(text);
    }

    private async Task RequestAsync(string text)
    {
        _request?.Cancel();
        _request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _request.Token;

        try
        {
            ShowTranslation(await _translate(text, _languageHint, token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer edit replaced this request, or the window closed.
        }
        catch (TranslationFailedException error)
        {
            Log.Error("Перевод", error);
            ShowFailure("Перевод не получился", error.Message + ". Проверьте интернет.", () => RequestAsync(text));
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _userMoved = true;
        DragMove();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyResult();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (PinButton.IsChecked == true) _closeTimer.Stop();
        else StartCloseTimer(_settings.AutoCloseSeconds);
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_retry is null) return;
        MessagePanel.Visibility = Visibility.Collapsed;
        Skeleton.Visibility = Visibility.Visible;
        ((Storyboard)Resources["Pulse"]).Begin(this, isControllable: true);
        await _retry();
    }

    private void CopyResult()
    {
        if (ResultBox.Text.Length == 0) return;
        try
        {
            Clipboard.SetDataObject(ResultBox.Text, copy: true);
            CopyButton.Content = CopiedGlyph;
            _copiedTimer.Stop();
            _copiedTimer.Start();
        }
        catch (ExternalException error)
        {
            // Another program can hold the clipboard for a moment.
            Log.Warn($"Буфер обмена занят: {error.Message}");
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control
                 && ResultBox.SelectionLength == 0 && SourceBox.SelectionLength == 0)
        {
            e.Handled = true;
            CopyResult();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;

        // If the user clicked elsewhere, focus is already where they want it.
        if (IsActive && _previousWindow != IntPtr.Zero) NativeMethods.SetForegroundWindow(_previousWindow);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _editTimer.Stop();
        _closeTimer.Stop();
        _copiedTimer.Stop();
        _lifetime.Cancel();
    }
}
