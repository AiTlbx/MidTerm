using System.IO.Pipes;

namespace Ai.Tlbx.MiddleManager.Services;

/// <summary>
/// IPC client for a single mm-con-host process.
/// </summary>
public sealed class ConHostClient : IAsyncDisposable
{
    private readonly string _sessionId;
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private bool _disposed;

    public string SessionId => _sessionId;
    public bool IsConnected => _pipe?.IsConnected ?? false;

    public event Action<string, ReadOnlyMemory<byte>>? OnOutput;
    public event Action<string>? OnStateChanged;

    public ConHostClient(string sessionId)
    {
        _sessionId = sessionId;
        _pipeName = ConHostProtocol.GetPipeName(sessionId);
    }

    public async Task<bool> ConnectAsync(int timeoutMs = 5000, CancellationToken ct = default)
    {
        if (_disposed) return false;

        try
        {
            _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
            // Don't start ReadLoopAsync here - wait until after initial handshake (GetInfoAsync)
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConHostClient] Connect to {_pipeName} failed: {ex.Message}");
            _pipe?.Dispose();
            _pipe = null;
            return false;
        }
    }

    public void StartReadLoop()
    {
        if (_readTask is not null) return;
        _readCts = new CancellationTokenSource();
        _readTask = ReadLoopAsync(_readCts.Token);
    }

    public async Task<SessionInfo?> GetInfoAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return null;

        try
        {
            var request = ConHostProtocol.CreateInfoRequest();
            await _pipe!.WriteAsync(request, ct).ConfigureAwait(false);

            var response = await ReadMessageAsync(ct).ConfigureAwait(false);
            if (response is null) return null;

            var (type, payload) = response.Value;
            if (type != ConHostMessageType.Info) return null;

            return ConHostProtocol.ParseInfo(payload.Span);
        }
        catch
        {
            return null;
        }
    }

    public async Task SendInputAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (!IsConnected) return;

        try
        {
            var msg = ConHostProtocol.CreateInputMessage(data.Span);
            await _pipe!.WriteAsync(msg, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConHostClient] SendInput failed: {ex.Message}");
        }
    }

    public async Task<bool> ResizeAsync(int cols, int rows, CancellationToken ct = default)
    {
        if (!IsConnected) return false;

        try
        {
            var msg = ConHostProtocol.CreateResizeMessage(cols, rows);
            await _pipe!.WriteAsync(msg, ct).ConfigureAwait(false);

            var response = await ReadMessageAsync(ct).ConfigureAwait(false);
            return response?.type == ConHostMessageType.ResizeAck;
        }
        catch
        {
            return false;
        }
    }

    public async Task<byte[]?> GetBufferAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return null;

        try
        {
            var msg = ConHostProtocol.CreateGetBuffer();
            await _pipe!.WriteAsync(msg, ct).ConfigureAwait(false);

            var response = await ReadMessageAsync(ct).ConfigureAwait(false);
            if (response is null || response.Value.type != ConHostMessageType.Buffer)
            {
                return null;
            }

            return response.Value.payload.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SetNameAsync(string? name, CancellationToken ct = default)
    {
        if (!IsConnected) return false;

        try
        {
            var msg = ConHostProtocol.CreateSetName(name);
            await _pipe!.WriteAsync(msg, ct).ConfigureAwait(false);

            var response = await ReadMessageAsync(ct).ConfigureAwait(false);
            return response?.type == ConHostMessageType.SetNameAck;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CloseAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return false;

        try
        {
            var msg = ConHostProtocol.CreateClose();
            await _pipe!.WriteAsync(msg, ct).ConfigureAwait(false);

            var response = await ReadMessageAsync(ct).ConfigureAwait(false);
            return response?.type == ConHostMessageType.CloseAck;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var headerBuffer = new byte[ConHostProtocol.HeaderSize];
        var payloadBuffer = new byte[ConHostProtocol.MaxPayloadSize];

        while (!ct.IsCancellationRequested && IsConnected)
        {
            try
            {
                var bytesRead = await _pipe!.ReadAsync(headerBuffer, ct).ConfigureAwait(false);
                if (bytesRead == 0) break;

                while (bytesRead < ConHostProtocol.HeaderSize)
                {
                    var more = await _pipe.ReadAsync(headerBuffer.AsMemory(bytesRead), ct).ConfigureAwait(false);
                    if (more == 0) return;
                    bytesRead += more;
                }

                if (!ConHostProtocol.TryReadHeader(headerBuffer, out var msgType, out var payloadLength))
                {
                    break;
                }

                if (payloadLength > 0)
                {
                    var totalRead = 0;
                    while (totalRead < payloadLength)
                    {
                        var chunk = await _pipe.ReadAsync(payloadBuffer.AsMemory(totalRead, payloadLength - totalRead), ct).ConfigureAwait(false);
                        if (chunk == 0) return;
                        totalRead += chunk;
                    }
                }

                var payload = payloadBuffer.AsMemory(0, payloadLength);

                switch (msgType)
                {
                    case ConHostMessageType.Output:
                        OnOutput?.Invoke(_sessionId, payload);
                        break;

                    case ConHostMessageType.StateChange:
                        OnStateChanged?.Invoke(_sessionId);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConHostClient] Read error: {ex.Message}");
                break;
            }
        }

        OnStateChanged?.Invoke(_sessionId);
    }

    private async Task<(ConHostMessageType type, Memory<byte> payload)?> ReadMessageAsync(CancellationToken ct)
    {
        if (!IsConnected) return null;

        var headerBuffer = new byte[ConHostProtocol.HeaderSize];
        var bytesRead = await _pipe!.ReadAsync(headerBuffer, ct).ConfigureAwait(false);
        if (bytesRead == 0) return null;

        while (bytesRead < ConHostProtocol.HeaderSize)
        {
            var more = await _pipe.ReadAsync(headerBuffer.AsMemory(bytesRead), ct).ConfigureAwait(false);
            if (more == 0) return null;
            bytesRead += more;
        }

        if (!ConHostProtocol.TryReadHeader(headerBuffer, out var msgType, out var payloadLength))
        {
            return null;
        }

        var payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            var totalRead = 0;
            while (totalRead < payloadLength)
            {
                var chunk = await _pipe.ReadAsync(payload.AsMemory(totalRead), ct).ConfigureAwait(false);
                if (chunk == 0) return null;
                totalRead += chunk;
            }
        }

        return (msgType, payload);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _readCts?.Cancel();

        if (_readTask is not null)
        {
            try { await _readTask.ConfigureAwait(false); } catch { }
        }

        _readCts?.Dispose();
        _pipe?.Dispose();
    }
}
