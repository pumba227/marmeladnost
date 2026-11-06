using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Marmelad.Core;
using Marmelad.Native;
using Marmelad.Themes;
using Marmelad.Translators;
using Marmelad.Views;

namespace Marmelad;

/// <summary>Connects the hotkey, the selection overlay, OCR, translation and the popup.</summary>
public sealed class AppController : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PopupFadeDelay = TimeSpan.FromMilliseconds(60);

    /// <summary>Long enough to keep everything warm between a few translations in a row.</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(10);

    private readonly AppSettings _settings;
    private readonly ResidentHost _host;
    private readonly OcrService _ocr = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = SaveDelay };
    private readonly DispatcherTimer _idleTimer = new() { Interval = IdleDelay };

    private HttpClient? _http;
    private TranslationService? _translator;
    private SettingsWindow? _settingsWindow;
    private PopupWindow? _popup;
    private bool _capturing;

    public AppController(AppSettings settings, ResidentHost host)
    {
        _settings = settings;
        _host = host;

        Theme.Apply(settings);

        _saveTimer.Tick += (_, _) => SaveNow();
        _idleTimer.Tick += (_, _) => OnIdle();
        _settings.PropertyChanged += OnSettingChanged;

        var dispatcher = Application.Current.Dispatcher;
        _host.Requested += request => dispatcher.BeginInvoke(() => Handle(request));
    }

    public void Start(AppRequest firstRequest, bool hotkeyFree)
    {
        Handle(firstRequest);

        if (!hotkeyFree)
        {
            var message = $"Сочетание {_settings.Hotkey.DisplayText} занято другой программой. Выберите другое.";
            _settingsWindow?.ShowHotkeyError(message);
            _host.ShowNotice(message);
        }
    }

    private void Handle(AppRequest request)
    {
        switch (request)
        {
            case AppRequest.Capture:
                StartCapture();
                break;
            case AppRequest.OpenSettings:
                OpenSettings();
                break;
            case AppRequest.Exit:
                Application.Current.Shutdown();
                break;
            case AppRequest.ThemeChanged when _settings.ThemeMode == AppTheme.System:
                Theme.Apply(_settings);
                break;
        }
    }

    private void OpenSettings()
    {
        _idleTimer.Stop();

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, _ocr, _host);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                ScheduleIdle();
            };
        }

        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private async void StartCapture()
    {
        if (_capturing) return;
        _capturing = true;
        _idleTimer.Stop();

        List<MonitorShot>? shots = null;
        var picker = new RegionPicker();
        try
        {
            if (_popup is not null)
            {
                _popup.Close();
                await Task.Delay(PopupFadeDelay); // Keep the old card out of the new screenshot.
            }

            var previousWindow = NativeMethods.GetForegroundWindow();
            shots = ScreenCapture.CaptureAll();
            _ocr.Warm(); // Engines load while the user is still selecting.

            var region = await picker.PickAsync(shots);
            if (region is null)
            {
                picker.Close();
                NativeMethods.SetForegroundWindow(previousWindow);
                return;
            }

            var image = region.Shot.Crop(region.Area);
            var popup = OpenPopup(region, previousWindow);
            picker.Close();
            _ = TranslateImageAsync(popup, image);
        }
        catch (Exception error)
        {
            Log.Error("Захват экрана", error);
            picker.Close();
            _host.ShowNotice("Не удалось захватить экран. Подробности в журнале.");
        }
        finally
        {
            shots?.ForEach(shot => shot.Dispose());
            _capturing = false;
            ScheduleIdle();
        }
    }

    private PopupWindow OpenPopup(PickedRegion region, IntPtr previousWindow)
    {
        var popup = new PopupWindow(_settings, region.Area, region.Shot.WorkArea, previousWindow, TranslateEditedAsync);
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_popup, popup)) _popup = null;
            ScheduleIdle();
        };
        _popup = popup;
        popup.Show();
        popup.Activate();
        return popup;
    }

    private async Task TranslateImageAsync(PopupWindow popup, Bitmap image)
    {
        var ct = popup.Lifetime;
        try
        {
            OcrOutcome outcome;
            using (image)
            {
                outcome = await _ocr.RecognizeAsync(image, _settings.OcrLanguage, _settings.TargetLanguage, ct);
            }

            if (outcome.Best is null)
            {
                popup.ShowNoText(_ocr.InstalledLanguages.Count == 0
                    ? "В Windows не установлены языки распознавания. Добавьте их в настройках."
                    : "Попробуйте выделить ровнее. Если язык с другим алфавитом, добавьте его в настройках.");
                return;
            }

            popup.ShowRecognized(outcome.Best.Text, outcome.Best.Language);
            await TranslateRecognizedAsync(popup, outcome, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The window was closed before the work finished.
        }
        catch (Exception error)
        {
            Log.Error("Распознавание", error);
            popup.ShowNoText("Не удалось распознать текст. Подробности в журнале.");
        }
    }

    private async Task TranslateRecognizedAsync(PopupWindow popup, OcrOutcome outcome, CancellationToken ct)
    {
        try
        {
            var result = await Translator.TranslateRecognizedAsync(outcome, CurrentOptions(), ct);
            popup.ShowRecognized(result.SourceText, result.Translation.SourceLanguage);
            popup.ShowTranslation(result.Translation);
        }
        catch (TranslationFailedException error)
        {
            Log.Error("Перевод", error);
            popup.ShowFailure(
                "Перевод не получился",
                error.Message + ". Проверьте интернет.",
                () => TranslateRecognizedAsync(popup, outcome, ct));
        }
    }

    private Task<Translation> TranslateEditedAsync(string text, string languageHint, CancellationToken ct) =>
        Translator.TranslateAsync(text, languageHint, CurrentOptions(), ct);

    /// <summary>The network stack is loaded only when a translation is actually needed.</summary>
    private TranslationService Translator
    {
        get
        {
            if (_translator is not null) return _translator;

            _http = new HttpClient { Timeout = RequestTimeout };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            _translator = new TranslationService(new ITranslator[]
            {
                new GoogleTranslator(_http),
                new MyMemoryTranslator(_http),
            });
            return _translator;
        }
    }

    private TranslationOptions CurrentOptions() =>
        new(_settings.OcrLanguage, _settings.TargetLanguage, _settings.FallbackLanguage);

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ThemeMode):
            case nameof(AppSettings.Accent):
            case nameof(AppSettings.Transparency):
                Theme.Apply(_settings);
                break;
            case nameof(AppSettings.HotkeyText):
                _host.ShowHotkey(_settings.Hotkey);
                break;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        SettingsStore.Save(_settings);
    }

    private void ScheduleIdle()
    {
        _idleTimer.Stop();
        if (_popup is null && _settingsWindow is null && !_capturing) _idleTimer.Start();
    }

    /// <summary>Back to waiting in the tray: drop everything that a next capture can rebuild.</summary>
    private void OnIdle()
    {
        _idleTimer.Stop();

        _ocr.Release();
        _translator = null;
        _http?.Dispose();
        _http = null;

        MemoryTrimmer.Trim();
    }

    public void Dispose()
    {
        SaveNow();
        _idleTimer.Stop();
        _settingsWindow?.Close();
        _popup?.Close();
        _http?.Dispose();
    }
}
