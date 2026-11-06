using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Marmelad.Core;
using static Marmelad.Native.NativeMethods;

namespace Marmelad.Native;

public enum AppRequest { Capture, OpenSettings, Exit, ThemeChanged }

/// <summary>
/// Everything that stays alive in the tray: one hidden window with the icon, its menu and the
/// hotkey, all on plain Win32. It uses no WPF and no timers, so an idle app costs no CPU at all.
/// </summary>
public sealed class ResidentHost : IDisposable
{
    private const string AppName = "Мармеладность";
    private const string WindowClass = "Marmeladnost.Resident";
    private const int HotkeyId = 0x4D41;
    private const uint IconId = 1;
    private const uint TrayMessage = WM_APP + 1;
    private const uint ShowSettingsMessage = WM_APP + 2;
    private const uint MenuCapture = 1;
    private const uint MenuSettings = 2;
    private const uint MenuExit = 3;

    // Native code keeps the pointer, so the delegate must outlive the window.
    private readonly WndProc _windowProc;
    private readonly IntPtr _window;
    private readonly IntPtr _icon;
    private readonly uint _taskbarCreated;

    private string _hotkeyText = "";
    private bool _hotkeyRegistered;
    private AppRequest? _firstRequest;

    public ResidentHost()
    {
        _windowProc = OnMessage;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        var windowClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = WindowClass,
        };
        if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception();

        // Not message-only: those windows never get the theme-change broadcast.
        _window = CreateWindowEx(0, WindowClass, AppName, WS_POPUP, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, windowClass.hInstance, IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new Win32Exception();

        ExtractIconEx(Environment.ProcessPath!, 0, IntPtr.Zero, out _icon, 1);
        UpdateIcon(NIM_ADD);
    }

    /// <summary>Set once the interface runs. Handlers are called inside the window procedure.</summary>
    public event Action<AppRequest>? Requested;

    /// <summary>Asks an already running copy to open its settings.</summary>
    public static void ShowRunningInstance()
    {
        var window = FindWindow(WindowClass, null);
        if (window == IntPtr.Zero) return;

        GetWindowThreadProcessId(window, out var processId);
        AllowSetForegroundWindow(processId);
        PostMessage(window, ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>The waiting loop before the interface exists; blocks without using the CPU.</summary>
    public AppRequest WaitForFirstRequest()
    {
        while (_firstRequest is null && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        return _firstRequest ?? AppRequest.Exit;
    }

    /// <summary>Returns false when another program already owns this combination.</summary>
    public bool RegisterHotkey(Hotkey hotkey)
    {
        UnregisterHotkey();

        var modifiers = MOD_NOREPEAT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= MOD_ALT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= MOD_CONTROL;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= MOD_SHIFT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= MOD_WIN;

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        _hotkeyRegistered = RegisterHotKey(_window, HotkeyId, modifiers, virtualKey);
        return _hotkeyRegistered;
    }

    public void UnregisterHotkey()
    {
        if (!_hotkeyRegistered) return;
        UnregisterHotKey(_window, HotkeyId);
        _hotkeyRegistered = false;
    }

    /// <summary>Shows the combination in the tooltip and the tray menu.</summary>
    public void ShowHotkey(Hotkey hotkey)
    {
        _hotkeyText = hotkey.DisplayText;
        UpdateIcon(NIM_MODIFY);
    }

    public void ShowNotice(string text)
    {
        var data = NewIconData(NIF_INFO);
        data.szInfoTitle = AppName;
        data.szInfo = text;
        data.dwInfoFlags = NIIF_WARNING;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATA NewIconData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window,
        uID = IconId,
        uFlags = flags,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    private void UpdateIcon(uint action)
    {
        var data = NewIconData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        data.uCallbackMessage = TrayMessage;
        data.hIcon = _icon;
        data.szTip = _hotkeyText.Length == 0 ? AppName : $"{AppName}: {_hotkeyText}";
        Shell_NotifyIcon(action, ref data);
    }

    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WM_HOTKEY when wParam.ToInt32() == HotkeyId:
                Raise(AppRequest.Capture);
                break;

            case TrayMessage:
                var mouseMessage = (uint)lParam.ToInt64() & 0xFFFF;
                if (mouseMessage == WM_LBUTTONUP) Raise(AppRequest.OpenSettings);
                else if (mouseMessage == WM_RBUTTONUP) ShowMenu();
                break;

            case ShowSettingsMessage:
                Raise(AppRequest.OpenSettings);
                break;

            case WM_SETTINGCHANGE when lParam != IntPtr.Zero
                                       && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                Raise(AppRequest.ThemeChanged);
                break;

            default:
                // Explorer restarted and forgot every tray icon.
                if (message == _taskbarCreated) UpdateIcon(NIM_ADD);
                break;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private void Raise(AppRequest request)
    {
        if (Requested is { } handler) handler(request);
        else if (request != AppRequest.ThemeChanged) _firstRequest = request;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, (UIntPtr)MenuCapture, $"Перевести область\t{_hotkeyText}");
        AppendMenu(menu, MF_STRING, (UIntPtr)MenuSettings, "Настройки");
        AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
        AppendMenu(menu, MF_STRING, (UIntPtr)MenuExit, "Выход");

        ApplyMenuTheme();
        GetCursorPos(out var cursor);

        // Without the foreground window the menu would not close on a click elsewhere.
        SetForegroundWindow(_window);
        var choice = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, cursor.X, cursor.Y, _window, IntPtr.Zero);
        PostMessage(_window, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        switch (choice)
        {
            case MenuCapture: Raise(AppRequest.Capture); break;
            case MenuSettings: Raise(AppRequest.OpenSettings); break;
            case MenuExit: Raise(AppRequest.Exit); break;
        }
    }

    private static void ApplyMenuTheme()
    {
        var theme = SettingsStore.Load().ThemeMode;
        try
        {
            SetPreferredAppMode(SystemTheme.IsDark(theme) ? APPMODE_FORCE_DARK : APPMODE_FORCE_LIGHT);
            FlushMenuThemes();
        }
        catch (EntryPointNotFoundException)
        {
            // An old Windows build: the menu simply stays light.
        }
    }

    public void Dispose()
    {
        UnregisterHotkey();
        var data = NewIconData(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        DestroyWindow(_window);
    }
}
