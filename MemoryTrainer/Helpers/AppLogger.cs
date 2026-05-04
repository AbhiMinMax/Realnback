namespace MemoryTrainer.Helpers;

public static class AppLogger
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private static readonly object _lock = new();
    private static string? _logPath;

    private static string LogPath => _logPath ??= PathHelper.AppLogPath;

    public static void Log(string caller, string message)
        => Write("INFO", caller, message);

    public static void Warn(string caller, string message)
        => Write("WARN", caller, message);

    public static void Error(string caller, string message, Exception? ex = null)
        => Write("ERROR", caller, ex != null ? $"{message}: {ex}" : message);

    private static void Write(string level, string caller, string message)
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}] [{level}] [{caller}] {message}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        try
        {
            lock (_lock)
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                {
                    var backup = LogPath + ".1";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(LogPath, backup);
                }
                File.AppendAllText(LogPath, line);
            }
        }
        catch { }
    }
}
