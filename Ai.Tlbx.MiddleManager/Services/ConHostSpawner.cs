#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ai.Tlbx.MiddleManager.Services;

/// <summary>
/// Spawns mm-con-host processes. When running as SYSTEM (service mode),
/// uses CreateProcessAsUser to spawn in user session for correct ConPTY behavior.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ConHostSpawner
{
    private static readonly string ConHostPath = GetConHostPath();

    public static bool SpawnConHost(
        string sessionId,
        string? shellType,
        string? workingDirectory,
        int cols,
        int rows,
        out int processId)
    {
        processId = 0;

        if (!File.Exists(ConHostPath))
        {
            Console.WriteLine($"[ConHostSpawner] mm-con-host not found at: {ConHostPath}");
            return false;
        }

        var args = BuildArgs(sessionId, shellType, workingDirectory, cols, rows);
        var commandLine = $"\"{ConHostPath}\" {args}";

        if (IsRunningAsSystem())
        {
            return SpawnAsUser(commandLine, out processId);
        }
        else
        {
            return SpawnDirect(commandLine, out processId);
        }
    }

    private static string BuildArgs(string sessionId, string? shellType, string? workingDirectory, int cols, int rows)
    {
        var args = $"--session {sessionId} --cols {cols} --rows {rows}";
        if (!string.IsNullOrEmpty(shellType))
        {
            args += $" --shell {shellType}";
        }
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            args += $" --cwd \"{workingDirectory}\"";
        }
        return args;
    }

    private static bool SpawnDirect(string commandLine, out int processId)
    {
        processId = 0;

        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf<STARTUPINFO>();

        var success = CreateProcess(
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            CREATE_NO_WINDOW,
            IntPtr.Zero,
            null,
            ref si,
            out var pi);

        if (!success)
        {
            Console.WriteLine($"[ConHostSpawner] CreateProcess failed: {Marshal.GetLastWin32Error()}");
            return false;
        }

        processId = pi.dwProcessId;
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);

        Console.WriteLine($"[ConHostSpawner] Spawned mm-con-host (PID: {processId})");
        return true;
    }

    private static bool SpawnAsUser(string commandLine, out int processId)
    {
        processId = 0;

        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            Console.WriteLine("[ConHostSpawner] No active console session");
            return false;
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            Console.WriteLine($"[ConHostSpawner] WTSQueryUserToken failed: {Marshal.GetLastWin32Error()}");
            return false;
        }

        try
        {
            if (!CreateEnvironmentBlock(out var envBlock, userToken, false))
            {
                Console.WriteLine($"[ConHostSpawner] CreateEnvironmentBlock failed: {Marshal.GetLastWin32Error()}");
                return false;
            }

            try
            {
                var si = new STARTUPINFO();
                si.cb = Marshal.SizeOf<STARTUPINFO>();
                si.lpDesktop = Marshal.StringToHGlobalUni("winsta0\\default");
                si.dwFlags = STARTF_USESHOWWINDOW;
                si.wShowWindow = SW_HIDE;

                try
                {
                    var success = CreateProcessAsUser(
                        userToken,
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                        envBlock,
                        null,
                        ref si,
                        out var pi);

                    if (!success)
                    {
                        Console.WriteLine($"[ConHostSpawner] CreateProcessAsUser failed: {Marshal.GetLastWin32Error()}");
                        return false;
                    }

                    processId = pi.dwProcessId;
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);

                    Console.WriteLine($"[ConHostSpawner] Spawned mm-con-host as user (PID: {processId}, Session: {sessionId})");
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(si.lpDesktop);
                }
            }
            finally
            {
                DestroyEnvironmentBlock(envBlock);
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    private static bool IsRunningAsSystem()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return identity.IsSystem;
        }
        catch
        {
            return false;
        }
    }

    private static string GetConHostPath()
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
        {
            return string.Empty;
        }

        var dir = Path.GetDirectoryName(currentExe);
        if (string.IsNullOrEmpty(dir))
        {
            return string.Empty;
        }

        return Path.Combine(dir, "mm-con-host.exe");
    }

    #region P/Invoke

    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESHOWWINDOW = 0x00000001;
    private const short SW_HIDE = 0;

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    #endregion
}
#endif
