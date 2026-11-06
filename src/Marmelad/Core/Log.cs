using System;
using System.IO;

namespace Marmelad.Core;

public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string context, Exception error) => Write("ERROR", $"{context}: {error}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataFolder);
                var path = AppPaths.LogFile;
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // A log that cannot be written must never take the app down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public static class AppPaths
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Мармеладность");

    public static string SettingsFile { get; } = Path.Combine(DataFolder, "settings.json");

    public static string LogFile { get; } = Path.Combine(DataFolder, "log.txt");
}
