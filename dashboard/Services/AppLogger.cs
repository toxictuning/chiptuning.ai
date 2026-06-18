using System.IO;

namespace ChiptuningAi.Dashboard.Services;

public static class AppLogger
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChiptuningAi", "dashboard.log");

    private static readonly object _lock = new();

    public static void Info(string message)  => Write("INFO ", message);
    public static void Warn(string message)  => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", message);
        if (ex is not null) Write("     ", $"{ex.GetType().Name}: {ex.Message}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
                File.AppendAllText(_logPath, line + Environment.NewLine);
                TrimIfLarge();
            }
        }
        catch { }
    }

    private static void TrimIfLarge()
    {
        try
        {
            var info = new FileInfo(_logPath);
            if (info.Length < 5 * 1024 * 1024) return;
            var lines = File.ReadAllLines(_logPath);
            File.WriteAllLines(_logPath, lines.Skip(lines.Length / 2));
        }
        catch { }
    }
}
