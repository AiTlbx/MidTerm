using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ai.Tlbx.MiddleManager.Pty;

public sealed class UnixPtyConnection : IPtyConnection
{
    private readonly object _lock = new();
    private int _masterFd = -1;
    private Process? _process;
    private FileStream? _writerStream;
    private FileStream? _readerStream;
    private bool _disposed;

    public Stream WriterStream
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _writerStream ?? throw new InvalidOperationException("Writer stream not initialized");
        }
    }

    public Stream ReaderStream
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _readerStream ?? throw new InvalidOperationException("Reader stream not initialized");
        }
    }

    public int Pid => _process?.Id ?? -1;

    public bool IsRunning
    {
        get
        {
            if (_disposed || _process is null)
            {
                return false;
            }

            try
            {
                return !_process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public int? ExitCode
    {
        get
        {
            if (_process is null)
            {
                return null;
            }

            try
            {
                return _process.HasExited ? _process.ExitCode : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private UnixPtyConnection()
    {
    }

    public static UnixPtyConnection Start(
        string app,
        string[] args,
        string workingDirectory,
        int cols,
        int rows,
        IDictionary<string, string>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(cols, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);

        var connection = new UnixPtyConnection();
        try
        {
            connection.StartInternal(app, args, workingDirectory, cols, rows, environment);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void StartInternal(
        string app,
        string[] args,
        string workingDirectory,
        int cols,
        int rows,
        IDictionary<string, string>? environment)
    {
        _masterFd = posix_openpt(O_RDWR | O_NOCTTY);
        if (_masterFd < 0)
        {
            throw new InvalidOperationException($"posix_openpt failed with errno {Marshal.GetLastWin32Error()}");
        }

        if (grantpt(_masterFd) != 0)
        {
            throw new InvalidOperationException($"grantpt failed with errno {Marshal.GetLastWin32Error()}");
        }

        if (unlockpt(_masterFd) != 0)
        {
            throw new InvalidOperationException($"unlockpt failed with errno {Marshal.GetLastWin32Error()}");
        }

        var slaveNamePtr = ptsname(_masterFd);
        if (slaveNamePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("ptsname failed");
        }
        var slaveName = Marshal.PtrToStringAnsi(slaveNamePtr)!;

        var winSize = new WinSize
        {
            ws_col = (ushort)cols,
            ws_row = (ushort)rows
        };
        ioctl(_masterFd, TIOCSWINSZ, ref winSize);

        var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle((IntPtr)_masterFd, ownsHandle: false);
        _writerStream = new FileStream(safeHandle, FileAccess.Write, bufferSize: 4096, isAsync: false);
        _readerStream = new FileStream(
            new Microsoft.Win32.SafeHandles.SafeFileHandle((IntPtr)_masterFd, ownsHandle: false),
            FileAccess.Read,
            bufferSize: 4096,
            isAsync: false);

        var argsString = args.Length > 0 ? " " + string.Join(" ", args) : "";

        ProcessStartInfo psi;
        if (OperatingSystem.IsMacOS())
        {
            // On macOS: use native helper for fast setsid + PTY setup
            // Falls back to Python if helper not available
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            var helperPath = Path.Combine(exeDir, "pty_helper");
            if (File.Exists(helperPath))
            {
                var helperArgs = $"{slaveName} {app}" + (args.Length > 0 ? " " + string.Join(" ", args) : "");
                psi = new ProcessStartInfo
                {
                    FileName = helperPath,
                    Arguments = helperArgs,
                };
            }
            else
            {
                var pyArgs = args.Length > 0 ? ", " + string.Join(", ", args.Select(a => $"'{a}'")) : "";
                var pyScript = $"import os; os.setsid(); fd=os.open('{slaveName}',os.O_RDWR); os.dup2(fd,0); os.dup2(fd,1); os.dup2(fd,2); os.close(fd); os.execvp('{app}',['{app}'{pyArgs}])";
                psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/python3",
                    Arguments = $"-c \"{pyScript}\"",
                };
            }
        }
        else
        {
            // On Linux: setsid properly sets up controlling terminal
            var execCmd = $"exec setsid {app}{argsString} <'{slaveName}' >'{slaveName}' 2>&1";
            psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"{execCmd}\"",
            };
        }

        psi.WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = false;
        psi.RedirectStandardOutput = false;
        psi.RedirectStandardError = false;

        if (environment is not null)
        {
            foreach (var kvp in environment)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }
        }

        _process = Process.Start(psi);
        if (_process is null)
        {
            throw new InvalidOperationException("Failed to start process");
        }
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed || _masterFd < 0)
        {
            return;
        }

        lock (_lock)
        {
            if (_masterFd >= 0)
            {
                var winSize = new WinSize
                {
                    ws_col = (ushort)cols,
                    ws_row = (ushort)rows
                };
                ioctl(_masterFd, TIOCSWINSZ, ref winSize);
            }
        }
    }

    public void Kill()
    {
        if (_disposed || _process is null)
        {
            return;
        }

        lock (_lock)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
    }

    public bool WaitForExit(int milliseconds)
    {
        if (_disposed || _process is null)
        {
            return true;
        }

        try
        {
            return _process.WaitForExit(milliseconds);
        }
        catch
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            try { _writerStream?.Dispose(); } catch { }
            try { _readerStream?.Dispose(); } catch { }

            if (_masterFd >= 0)
            {
                try { close(_masterFd); } catch { }
                _masterFd = -1;
            }

            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(1000);
                    }
                }
                catch { }
                try { _process.Dispose(); } catch { }
                _process = null;
            }
        }

        GC.SuppressFinalize(this);
    }

    ~UnixPtyConnection()
    {
        Dispose();
    }

    #region Native Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    private static readonly nuint TIOCSWINSZ = OperatingSystem.IsMacOS()
        ? 0x80087467
        : 0x5414;

    private const int O_RDWR = 2;
    private const int O_NOCTTY = 256;

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_openpt(int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int grantpt(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int unlockpt(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr ptsname(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, ref WinSize winsize);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    #endregion
}
