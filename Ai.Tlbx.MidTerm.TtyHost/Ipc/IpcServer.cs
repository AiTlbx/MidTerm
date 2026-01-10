using System.IO.Pipes;
using System.Net.Sockets;

namespace Ai.Tlbx.MidTerm.TtyHost.Ipc;

public interface IIpcClientConnection : IDisposable
{
    Stream Stream { get; }
    bool IsConnected { get; }
}

public interface IIpcServer : IDisposable
{
    Task<IIpcClientConnection> AcceptAsync(CancellationToken ct);
}

#if WINDOWS
public sealed class WindowsNamedPipeServer : IIpcServer
{
    private readonly string _pipeName;
    private bool _disposed;

    public WindowsNamedPipeServer(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<IIpcClientConnection> AcceptAsync(CancellationToken ct)
    {
        var pipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
        return new NamedPipeConnection(pipe);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    private sealed class NamedPipeConnection : IIpcClientConnection
    {
        private readonly NamedPipeServerStream _pipe;
        private bool _disposed;

        public NamedPipeConnection(NamedPipeServerStream pipe) => _pipe = pipe;
        public Stream Stream => _pipe;
        public bool IsConnected => !_disposed && _pipe.IsConnected;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _pipe.Dispose(); } catch { }
        }
    }
}
#else
public sealed class UnixSocketServer : IIpcServer
{
    private readonly string _socketPath;
    private Socket? _listener;
    private bool _disposed;

    public UnixSocketServer(string socketPath)
    {
        _socketPath = socketPath;

        // Clean up any stale socket file; ignore errors (file may be locked or already gone)
        try
        {
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }
        }
        catch { }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(10);
    }

    public async Task<IIpcClientConnection> AcceptAsync(CancellationToken ct)
    {
        if (_listener is null)
        {
            throw new ObjectDisposedException(nameof(UnixSocketServer));
        }

        var client = await _listener.AcceptAsync(ct).ConfigureAwait(false);
        return new UnixSocketConnection(client);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _listener?.Close(); } catch { }
        _listener = null;

        // Clean up socket file
        try
        {
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }
        }
        catch { }
    }

    private sealed class UnixSocketConnection : IIpcClientConnection
    {
        private readonly Socket _socket;
        private readonly NetworkStream _stream;
        private bool _disposed;

        public UnixSocketConnection(Socket socket)
        {
            _socket = socket;
            _stream = new NetworkStream(socket, ownsSocket: true);
        }

        public Stream Stream => _stream;
        public bool IsConnected => !_disposed && _socket.Connected;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream.Dispose(); } catch { }
        }
    }
}
#endif

public static class IpcServerFactory
{
    public static IIpcServer Create(string endpoint)
    {
#if WINDOWS
        return new WindowsNamedPipeServer(endpoint);
#else
        return new UnixSocketServer(endpoint);
#endif
    }
}
