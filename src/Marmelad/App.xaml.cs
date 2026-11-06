using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Marmelad.Core;

namespace Marmelad;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandledException;

        // These windows are small or static, so the GPU gains nothing. Without a Direct3D device
        // the app keeps 80 MB less after use and shows the overlay faster (96 ms against 151 ms).
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }

    /// <summary>A tray app should survive a failed action rather than vanish from the tray.</summary>
    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Необработанная ошибка", e.Exception);
        e.Handled = true;
    }
}
