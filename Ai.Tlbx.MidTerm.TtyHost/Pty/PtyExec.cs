#if !WINDOWS
using System.Runtime.InteropServices;

namespace Ai.Tlbx.MidTerm.TtyHost.Pty;

/// <summary>
/// PTY exec helper for Unix systems. Called via mthost --pty-exec.
/// Sets up controlling terminal and replaces process with shell.
/// </summary>
internal static class PtyExec
{
    private const int O_RDWR = 2;

    [DllImport("libc", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int setsid();

    [DllImport("libc", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

    [DllImport("libc", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int dup2(int oldfd, int newfd);

    [DllImport("libc", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int execvp(
        [MarshalAs(UnmanagedType.LPStr)] string file,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string?[] argv);

    /// <summary>
    /// PTY exec mode: setsid, open slave, dup2, execvp.
    /// NEVER returns on success - execvp replaces the process.
    /// </summary>
    /// <returns>
    /// Exit code on failure:
    /// 1 = setsid failed, 2 = open failed, 3 = dup2 failed, 4 = execvp failed, 5 = invalid args
    /// </returns>
    public static int Execute(string slavePath, string[] execArgs)
    {
        // Validate inputs
        if (string.IsNullOrEmpty(slavePath) || execArgs is null || execArgs.Length == 0)
        {
            return 5;
        }

        // Become session leader (required for controlling terminal)
        if (setsid() < 0)
        {
            return 1;
        }

        // Open PTY slave - this sets it as controlling terminal
        int fd = open(slavePath, O_RDWR);
        if (fd < 0)
        {
            return 2;
        }

        // Redirect stdin/stdout/stderr to PTY
        if (dup2(fd, 0) < 0 || dup2(fd, 1) < 0 || dup2(fd, 2) < 0)
        {
            close(fd);
            return 3;
        }

        // Close original fd if > 2
        if (fd > 2)
        {
            close(fd);
        }

        // Build null-terminated argv array for execvp
        // execvp expects: { "program", "arg1", "arg2", ..., null }
        var argv = new string?[execArgs.Length + 1];
        Array.Copy(execArgs, argv, execArgs.Length);
        argv[execArgs.Length] = null;

        // Replace process image - never returns on success
        execvp(execArgs[0], argv);

        // If we get here, execvp failed
        return 4;
    }
}
#endif
