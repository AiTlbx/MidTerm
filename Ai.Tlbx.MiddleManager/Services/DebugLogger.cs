namespace Ai.Tlbx.MiddleManager.Services;

public static class DebugLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MiddleManager", "logs");

    private static readonly string StartTimestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
    private static readonly string LogPath = Path.Combine(LogDir, $"mm-debug-{StartTimestamp}.log");
    private static readonly string ExceptionLogPath = Path.Combine(LogDir, $"mm-exceptions-{StartTimestamp}.log");
    private static readonly object _exceptionLock = new();

    public static bool Enabled { get; set; } = false;

    public static void ClearLogs()
    {
        try
        {
            if (Directory.Exists(LogDir))
            {
                foreach (var file in Directory.GetFiles(LogDir, "*.log"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    public static void Log(string message)
    {
        if (!Enabled) return;

        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>
    /// Logs an exception to a dedicated exception log file. Always enabled regardless of debug mode.
    /// </summary>
    public static void LogException(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}" +
                        $"  StackTrace: {ex.StackTrace}{Environment.NewLine}";

            if (ex.InnerException is not null)
            {
                entry += $"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}{Environment.NewLine}";
            }

            entry += Environment.NewLine;

            lock (_exceptionLock)
            {
                File.AppendAllText(ExceptionLogPath, entry);
            }
        }
        catch { }
    }

    /// <summary>
    /// Logs an error message to the exception log. Always enabled regardless of debug mode.
    /// </summary>
    public static void LogError(string context, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] ERROR: {message}{Environment.NewLine}";

            lock (_exceptionLock)
            {
                File.AppendAllText(ExceptionLogPath, entry);
            }
        }
        catch { }
    }
}
