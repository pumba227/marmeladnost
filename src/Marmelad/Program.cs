using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Marmelad.Core;
using Marmelad.Native;

namespace Marmelad;

/// <summary>
/// Waiting in the tray needs only an icon and a hotkey, so the app starts without WPF and loads
/// it on the first real request. Until then it does not even touch the video driver.
/// </summary>
internal static class Program
{
    private const string InstanceName = @"Local\Marmeladnost.Instance";

    [STAThread]
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Сбой программы", (Exception)e.ExceptionObject);

        using var instance = new Mutex(true, InstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Starting the program again means "show me the window", not a second copy.
            ResidentHost.ShowRunningInstance();
            return;
        }

        var firstRun = !SettingsStore.Exists;
        var settings = SettingsStore.Load();
        if (firstRun) SettingsStore.Save(settings);
        Autostart.RefreshPath();

        using var host = new ResidentHost();
        host.ShowHotkey(settings.Hotkey);
        var hotkeyFree = host.RegisterHotkey(settings.Hotkey);

        AppRequest request;
        if (firstRun || !hotkeyFree || Array.IndexOf(args, Autostart.BackgroundArgument) < 0)
        {
            request = AppRequest.OpenSettings;
        }
        else
        {
            MemoryTrimmer.Trim();
            request = host.WaitForFirstRequest();
        }

        if (request != AppRequest.Exit) RunInterface(settings, host, request, hotkeyFree);
    }

    /// <summary>Kept apart from Main, so the WPF assemblies load only when this runs.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunInterface(AppSettings settings, ResidentHost host, AppRequest request, bool hotkeyFree)
    {
        var app = new App();
        app.InitializeComponent();

        using var controller = new AppController(settings, host);
        app.Startup += (_, _) => controller.Start(request, hotkeyFree);
        app.Run();
    }
}
