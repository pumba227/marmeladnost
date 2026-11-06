using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static Marmelad.Native.NativeMethods;

namespace Marmelad.Native;

public static class WindowEffects
{
    private static void SetAttribute(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    public static void SetDarkFrame(Window window, bool dark) =>
        SetAttribute(new WindowInteropHelper(window).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);

    public static void SetRoundedCorners(Window window) =>
        SetAttribute(new WindowInteropHelper(window).Handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

    /// <summary>Without this the fullscreen overlay would fade in and lag behind the hotkey.</summary>
    public static void DisableTransitions(Window window) =>
        SetAttribute(new WindowInteropHelper(window).Handle, DWMWA_TRANSITIONS_FORCEDISABLED, 1);

    /// <summary>
    /// Blurs what is behind the window through a semi-transparent background. Windows 10 ignores
    /// the attribute, and the window then simply stays solid.
    /// </summary>
    public static void SetAcrylic(Window window, bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (enabled && HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = Colors.Transparent;

        SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, enabled ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE);
    }
}
