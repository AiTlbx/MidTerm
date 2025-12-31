namespace Ai.Tlbx.MiddleManager.Services;

public static class DebugLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MiddleManager", "logs");

    private static readonly string LogPath = Path.Combine(LogDir, "mm-debug.log");

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
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
