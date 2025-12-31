using System.Buffers;
using System.Text;
using Ai.Tlbx.MiddleManager.Host.Ipc;
using Ai.Tlbx.MiddleManager.Host.Pty;
using Ai.Tlbx.MiddleManager.Host.Shells;

namespace Ai.Tlbx.MiddleManager.Host.Services;

public sealed class TerminalSession : IDisposable
{
    private readonly IPtyConnection _connection;
    private readonly CancellationTokenSource _cts;
    private readonly StringBuilder _outputBuffer = new();
    private readonly object _bufferLock = new();
    private const int MaxBufferSize = 100_000;
    private bool _disposed;

    public string Id { get; }
    public int Pid => _connection.Pid;
    public DateTime CreatedAt { get; }
    public bool IsRunning => !_disposed && _connection.IsRunning;
    public int? ExitCode => _connection.ExitCode;
    public string? CurrentWorkingDirectory { get; private set; }
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public ShellType ShellType { get; }
    public string? Name { get; private set; }

    public event Action<string, ReadOnlyMemory<byte>>? OnOutput;
    public event Action<string>? OnStateChanged;

    private TerminalSession(string id, IPtyConnection connection, ShellType shellType)
    {
        Id = id;
        _connection = connection;
        ShellType = shellType;
        _cts = new CancellationTokenSource();
        CreatedAt = DateTime.UtcNow;
    }

    public static TerminalSession Create(
        string workingDirectory,
        int cols,
        int rows,
        IShellConfiguration shellConfig,
        string? runAsUser = null,
        string? runAsUserSid = null,
        int? runAsUid = null,
        int? runAsGid = null)
    {
        var id = Guid.NewGuid().ToString("N")[..8];

        var connection = PtyConnectionFactory.Create(
            shellConfig.ExecutablePath,
            shellConfig.Arguments,
            workingDirectory,
            cols,
            rows,
            shellConfig.GetEnvironmentVariables(),
            runAsUser,
            runAsUserSid,
            runAsUid,
            runAsGid);

        var session = new TerminalSession(id, connection, shellConfig.ShellType)
        {
            Cols = cols,
            Rows = rows
        };
        session.StartReadLoop();
        return session;
    }

    public void SetName(string? name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        OnStateChanged?.Invoke(Id);
    }

    private void StartReadLoop()
    {
        _ = ReadLoopAsync();
    }

    private async Task ReadLoopAsync()
    {
        const int bufferSize = 8192;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            var reader = _connection.ReaderStream;

            while (!_cts.Token.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await reader.ReadAsync(
                        buffer.AsMemory(0, bufferSize),
                        _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }

                if (bytesRead == 0)
                {
                    break;
                }

                var data = buffer.AsMemory(0, bytesRead);

                AppendToBuffer(data.Span);
                ParseOscSequences(data.Span);

                // Must copy data before invoking event because the buffer is reused
                // and fire-and-forget async sends would read corrupted data
                var dataCopy = data.ToArray();
                OnOutput?.Invoke(Id, dataCopy);
            }
        }
        catch
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            OnStateChanged?.Invoke(Id);
        }
    }

    private void AppendToBuffer(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        lock (_bufferLock)
        {
            _outputBuffer.Append(text);
            if (_outputBuffer.Length > MaxBufferSize)
            {
                _outputBuffer.Remove(0, _outputBuffer.Length - MaxBufferSize);
            }
        }
    }

    private void ParseOscSequences(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        var path = ParseOsc7Path(text);
        if (path is not null && CurrentWorkingDirectory != path)
        {
            CurrentWorkingDirectory = path;
            OnStateChanged?.Invoke(Id);
        }
    }

    internal static string? ParseOsc7Path(string text)
    {
        var oscStart = text.IndexOf("\x1b]7;", StringComparison.Ordinal);
        if (oscStart < 0)
        {
            return null;
        }

        var uriStart = oscStart + 4;
        var oscEnd = text.IndexOfAny(['\x07', '\x1b'], uriStart);
        if (oscEnd <= uriStart)
        {
            return null;
        }

        var uri = text.Substring(uriStart, oscEnd - uriStart);
        if (!uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var pathStart = uri.IndexOf('/', 7);
            if (pathStart < 0)
            {
                return null;
            }

            var path = Uri.UnescapeDataString(uri.Substring(pathStart));
            if (path.Length > 2 && path[0] == '/' && path[2] == ':')
            {
                path = path.Substring(1);
            }
            return path;
        }
        catch
        {
            return null;
        }
    }

    public byte[] GetBuffer()
    {
        lock (_bufferLock)
        {
            return Encoding.UTF8.GetBytes(_outputBuffer.ToString());
        }
    }

    public async Task SendInputAsync(ReadOnlyMemory<byte> data)
    {
        if (_disposed || data.IsEmpty)
        {
            return;
        }

        try
        {
            Log.Write($"Session {Id}: Writing {data.Length} bytes to PTY input");
            await _connection.WriterStream.WriteAsync(data).ConfigureAwait(false);
            await _connection.WriterStream.FlushAsync().ConfigureAwait(false);
            Log.Write($"Session {Id}: Input write+flush complete");
        }
        catch (Exception ex)
        {
            Log.Write($"Session {Id}: Input write failed: {ex.Message}");
        }
    }

    public bool Resize(int cols, int rows)
    {
        if (_disposed)
        {
            return false;
        }

        if (Cols == cols && Rows == rows)
        {
            return true;
        }

        Cols = cols;
        Rows = rows;
        _connection.Resize(cols, rows);
        OnStateChanged?.Invoke(Id);
        return true;
    }

    public SessionSnapshot ToSnapshot()
    {
        return new SessionSnapshot
        {
            Id = Id,
            Name = Name,
            ShellType = ShellType.ToString(),
            IsRunning = IsRunning,
            ExitCode = ExitCode,
            Cols = Cols,
            Rows = Rows,
            CurrentWorkingDirectory = CurrentWorkingDirectory,
            CreatedAt = CreatedAt,
            Pid = Pid
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try { _cts.Cancel(); } catch { }
        try { _cts.Dispose(); } catch { }
        try { _connection.Dispose(); } catch { }
    }
}
