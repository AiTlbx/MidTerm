namespace Ai.Tlbx.MidTerm.TtyHost.Pty;

public static class PtyConnectionFactory
{
#pragma warning disable CA1416 // Validate platform compatibility (compile-time guard via WINDOWS constant)
    public static IPtyConnection Create(
        string app,
        string[] args,
        string workingDirectory,
        int cols,
        int rows,
        IDictionary<string, string>? environment = null)
    {
#if WINDOWS
        return WindowsPty.Start(app, args, workingDirectory, cols, rows, environment);
#else
        return UnixPty.Start(app, args, workingDirectory, cols, rows, environment);
#endif
    }
#pragma warning restore CA1416
}
