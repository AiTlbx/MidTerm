using System.Collections.Concurrent;
using Ai.Tlbx.MiddleManager.Host.Ipc;
using static Ai.Tlbx.MiddleManager.Host.Log;

namespace Ai.Tlbx.MiddleManager.Host.Services;

public sealed class SidecarServer : IAsyncDisposable
{
    private readonly IIpcServer _server;
    private readonly SessionManager _sessionManager;
    private readonly ConcurrentDictionary<int, ClientHandler> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextClientId;
    private bool _disposed;

    public SidecarServer(SessionManager sessionManager)
    {
        _server = IpcServerFactory.Create();
        _sessionManager = sessionManager;

        _sessionManager.OnOutput += BroadcastOutput;
        _sessionManager.OnStateChanged += BroadcastStateChange;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _server.StartAsync(cancellationToken).ConfigureAwait(false);
        Write($"mm-host listening on {IpcServerFactory.GetEndpointDescription()}");

        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Write("Waiting for client connection...");
                var transport = await _server.AcceptAsync(cancellationToken).ConfigureAwait(false);
                var clientId = Interlocked.Increment(ref _nextClientId);
                Write($"Client {clientId} connected");
                var handler = new ClientHandler(clientId, transport, _sessionManager, RemoveClient);
                _clients[clientId] = handler;
                _ = handler.RunAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Write($"Accept error: {ex.Message}");
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void RemoveClient(int clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    private void BroadcastOutput(string sessionId, ReadOnlyMemory<byte> data)
    {
        var frame = new IpcFrame(IpcMessageType.Output, sessionId, data);
        foreach (var client in _clients.Values)
        {
            _ = client.SendFrameAsync(frame);
        }
    }

    private void BroadcastStateChange(string sessionId)
    {
        var session = _sessionManager.GetSession(sessionId);
        var snapshot = session?.ToSnapshot() ?? new SessionSnapshot
        {
            Id = sessionId,
            ShellType = string.Empty,
            IsRunning = false
        };

        var payload = SidecarProtocol.CreateStateChangePayload(snapshot);
        var frame = new IpcFrame(IpcMessageType.StateChange, sessionId, payload);

        foreach (var client in _clients.Values)
        {
            _ = client.SendFrameAsync(frame);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _cts.Cancel();

        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        _clients.Clear();

        await _server.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}

internal sealed class ClientHandler : IAsyncDisposable
{
    private const int PingIntervalMs = 5000;
    private const int PongTimeoutMs = 3000;

    private readonly int _clientId;
    private readonly IIpcTransport _transport;
    private readonly SessionManager _sessionManager;
    private readonly Action<int> _onDisconnect;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _heartbeatCts = new();
    private long _lastPongTicks;
    private bool _disposed;

    public ClientHandler(
        int clientId,
        IIpcTransport transport,
        SessionManager sessionManager,
        Action<int> onDisconnect)
    {
        _clientId = clientId;
        _transport = transport;
        _sessionManager = sessionManager;
        _onDisconnect = onDisconnect;
        _lastPongTicks = DateTime.UtcNow.Ticks;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Write($"Client {_clientId} RunAsync started");
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _heartbeatCts.Token);
        var linkedToken = linkedCts.Token;

        _ = HeartbeatLoopAsync(linkedToken);

        try
        {
            while (!linkedToken.IsCancellationRequested && _transport.IsConnected)
            {
                var frame = await _transport.ReadFrameAsync(linkedToken).ConfigureAwait(false);
                if (frame is null)
                {
                    Write($"Client {_clientId} received null frame, disconnecting");
                    break;
                }

                Write($"Client {_clientId} received frame type: {frame.Value.Type}");
                await HandleFrameAsync(frame.Value, linkedToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Write($"Client {_clientId} cancelled");
        }
        catch (Exception ex)
        {
            Write($"Client {_clientId} error: {ex.Message}");
        }
        finally
        {
            Write($"Client {_clientId} disconnected");
            _onDisconnect(_clientId);
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        Write($"Client {_clientId} heartbeat loop started");
        try
        {
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            Write($"Client {_clientId} heartbeat loop: initial delay complete, starting pings");

            while (!cancellationToken.IsCancellationRequested && _transport.IsConnected)
            {
                try
                {
                    Write($"Client {_clientId} sending Ping");
                    await SendFrameAsync(new IpcFrame(IpcMessageType.Ping)).ConfigureAwait(false);
                    Write($"Client {_clientId} Ping sent successfully");
                }
                catch (Exception ex)
                {
                    Write($"Client {_clientId} heartbeat send failed: {ex.Message}");
                    break;
                }

                await Task.Delay(PongTimeoutMs, cancellationToken).ConfigureAwait(false);

                var elapsed = DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastPongTicks);
                if (TimeSpan.FromTicks(elapsed).TotalMilliseconds > PingIntervalMs + PongTimeoutMs)
                {
                    Write($"Client {_clientId} heartbeat timeout (no Pong received), closing connection");
                    _heartbeatCts.Cancel();
                    break;
                }

                var remaining = PingIntervalMs - PongTimeoutMs;
                if (remaining > 0)
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
            }
            Write($"Client {_clientId} heartbeat loop exiting (cancelled={cancellationToken.IsCancellationRequested}, connected={_transport.IsConnected})");
        }
        catch (OperationCanceledException)
        {
            Write($"Client {_clientId} heartbeat loop cancelled");
        }
        catch (Exception ex)
        {
            Write($"Client {_clientId} heartbeat loop error: {ex.Message}");
        }
    }

    private async Task HandleFrameAsync(IpcFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case IpcMessageType.Handshake:
                await HandleHandshakeAsync(frame, cancellationToken).ConfigureAwait(false);
                break;

            case IpcMessageType.CreateSession:
                await HandleCreateSessionAsync(frame, cancellationToken).ConfigureAwait(false);
                break;

            case IpcMessageType.CloseSession:
                HandleCloseSession(frame);
                break;

            case IpcMessageType.Input:
                await HandleInputAsync(frame).ConfigureAwait(false);
                break;

            case IpcMessageType.Resize:
                HandleResize(frame);
                break;

            case IpcMessageType.ListSessions:
                await HandleListSessionsAsync(cancellationToken).ConfigureAwait(false);
                break;

            case IpcMessageType.GetBuffer:
                await HandleGetBufferAsync(frame, cancellationToken).ConfigureAwait(false);
                break;

            case IpcMessageType.Pong:
                Interlocked.Exchange(ref _lastPongTicks, DateTime.UtcNow.Ticks);
                break;

            case IpcMessageType.Ping:
                await SendFrameAsync(new IpcFrame(IpcMessageType.Pong)).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleHandshakeAsync(IpcFrame frame, CancellationToken cancellationToken)
    {
        var (version, _) = SidecarProtocol.ParseHandshakePayload(frame.Payload.Span);
        if (version != SidecarProtocol.ProtocolVersion)
        {
            var error = SidecarProtocol.CreateErrorPayload($"Protocol version mismatch: expected {SidecarProtocol.ProtocolVersion}, got {version}");
            await SendFrameAsync(new IpcFrame(IpcMessageType.Error, string.Empty, error)).ConfigureAwait(false);
            return;
        }

        var ackPayload = SidecarProtocol.CreateHandshakePayload(string.Empty);
        await SendFrameAsync(new IpcFrame(IpcMessageType.HandshakeAck, string.Empty, ackPayload)).ConfigureAwait(false);
    }

    private async Task HandleCreateSessionAsync(IpcFrame frame, CancellationToken cancellationToken)
    {
        try
        {
            var request = SidecarProtocol.ParseCreateSessionPayload(frame.Payload.Span);
            var session = _sessionManager.CreateSession(request);
            var payload = SidecarProtocol.CreateSessionCreatedPayload(session.ToSnapshot());
            // Use frame.SessionId (the requestId) for correlation, not session.Id
            await SendFrameAsync(new IpcFrame(IpcMessageType.SessionCreated, frame.SessionId, payload)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var error = SidecarProtocol.CreateErrorPayload(ex.Message);
            await SendFrameAsync(new IpcFrame(IpcMessageType.Error, frame.SessionId, error)).ConfigureAwait(false);
        }
    }

    private void HandleCloseSession(IpcFrame frame)
    {
        _sessionManager.CloseSession(frame.SessionId);
    }

    private async Task HandleInputAsync(IpcFrame frame)
    {
        await _sessionManager.SendInputAsync(frame.SessionId, frame.Payload).ConfigureAwait(false);
    }

    private void HandleResize(IpcFrame frame)
    {
        var (cols, rows) = SidecarProtocol.ParseResizePayload(frame.Payload.Span);
        _sessionManager.ResizeSession(frame.SessionId, cols, rows);
    }

    private async Task HandleListSessionsAsync(CancellationToken cancellationToken)
    {
        var snapshots = _sessionManager.GetAllSnapshots();
        var payload = SidecarProtocol.CreateSessionListPayload(snapshots);
        await SendFrameAsync(new IpcFrame(IpcMessageType.SessionList, string.Empty, payload)).ConfigureAwait(false);
    }

    private async Task HandleGetBufferAsync(IpcFrame frame, CancellationToken cancellationToken)
    {
        var buffer = _sessionManager.GetBuffer(frame.SessionId);
        if (buffer is not null)
        {
            await SendFrameAsync(new IpcFrame(IpcMessageType.Buffer, frame.SessionId, buffer)).ConfigureAwait(false);
        }
    }

    public async Task SendFrameAsync(IpcFrame frame)
    {
        if (_disposed || !_transport.IsConnected)
        {
            return;
        }

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _transport.WriteFrameAsync(frame).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _heartbeatCts.Cancel();
        await _transport.DisposeAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _heartbeatCts.Dispose();
    }
}
