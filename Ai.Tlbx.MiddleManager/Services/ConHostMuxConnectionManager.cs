using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace Ai.Tlbx.MiddleManager.Services;

/// <summary>
/// WebSocket mux manager for con-host mode.
/// </summary>
public sealed class ConHostMuxConnectionManager
{
    private readonly ConHostSessionManager _sessionManager;
    private readonly ConcurrentDictionary<string, MuxClient> _clients = new();
    private readonly Channel<(string sessionId, byte[] data)> _outputQueue = Channel.CreateUnbounded<(string, byte[])>();
    private Task? _outputProcessor;
    private CancellationTokenSource? _cts;

    public ConHostMuxConnectionManager(ConHostSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        _sessionManager.OnOutput += HandleOutput;

        _cts = new CancellationTokenSource();
        _outputProcessor = ProcessOutputQueueAsync(_cts.Token);
    }

    private void HandleOutput(string sessionId, ReadOnlyMemory<byte> data)
    {
        _outputQueue.Writer.TryWrite((sessionId, data.ToArray()));
    }

    private async Task ProcessOutputQueueAsync(CancellationToken ct)
    {
        await foreach (var (sessionId, data) in _outputQueue.Reader.ReadAllAsync(ct))
        {
            var frame = MuxProtocol.CreateOutputFrame(sessionId, data);
            foreach (var client in _clients.Values)
            {
                if (client.WebSocket.State == WebSocketState.Open)
                {
                    await client.SendAsync(frame).ConfigureAwait(false);
                }
            }
        }
    }

    public MuxClient AddClient(string clientId, WebSocket webSocket)
    {
        var client = new MuxClient(clientId, webSocket);
        _clients[clientId] = client;
        return client;
    }

    public void RemoveClient(string clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    public async Task HandleInputAsync(string sessionId, ReadOnlyMemory<byte> data, string clientId)
    {
        await _sessionManager.SendInputAsync(sessionId, data).ConfigureAwait(false);
    }

    public async Task HandleResizeAsync(string sessionId, int cols, int rows, string clientId)
    {
        await _sessionManager.ResizeSessionAsync(sessionId, cols, rows).ConfigureAwait(false);
    }

    public async Task BroadcastTerminalOutputAsync(string sessionId, ReadOnlyMemory<byte> data)
    {
        var frame = MuxProtocol.CreateOutputFrame(sessionId, data.Span);
        foreach (var client in _clients.Values)
        {
            if (client.WebSocket.State == WebSocketState.Open)
            {
                await client.SendAsync(frame).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_outputProcessor is not null)
        {
            try { await _outputProcessor.ConfigureAwait(false); } catch { }
        }
        _outputQueue.Writer.Complete();
        _cts?.Dispose();
    }
}
