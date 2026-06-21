using System.IO;

namespace ChiptuningAi.Dashboard.Services;

public static class AppLogger
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChiptuningAi", "dashboard.log");

    private static readonly object _lock = new();

    public static string LogPath => _logPath;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    /// <summary>
    /// Logs the full exception (type, message, stack trace, inner exception) and returns a
    /// short reference code the UI can show to the user instead of raw technical details.
    /// Format: E-YYMMDD-HHmmss  (UTC, matches log timestamp for easy lookup)
    /// </summary>
    public static string Error(string message, Exception? ex = null)
    {
        var code = $"E-{DateTime.UtcNow:yyMMdd-HHmmss}";
        Write("ERROR", $"[{code}] {message}");
        if (ex is not null)
        {
            Write("     ", $"[{code}] {ex.GetType().Name}: {ex.Message}");
            if (ex.StackTrace is { } st)
                foreach (var line in st.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Write("     ", $"[{code}]   {line.Trim()}");
            if (ex.InnerException is { } inner)
                Write("     ", $"[{code}] Inner: {inner.GetType().Name}: {inner.Message}");
        }
        return code;
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
