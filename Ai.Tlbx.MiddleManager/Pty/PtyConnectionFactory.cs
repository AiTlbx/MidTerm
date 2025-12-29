namespace Ai.Tlbx.MiddleManager.Pty;

public static class PtyConnectionFactory
{
    public static IPtyConnection Create(
        string app,
        string[] args,
        string workingDirectory,
        int cols,
        int rows,
        IDictionary<string, string>? environment = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsPtyConnection.Start(app, args, workingDirectory, cols, rows, environment);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return UnixPtyConnection.Start(app, args, workingDirectory, cols, rows, environment);
        }

        throw new PlatformNotSupportedException("PTY is only supported on Windows, Linux, and macOS");
    }
}
