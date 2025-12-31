using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Ai.Tlbx.MiddleManager.Services;

public sealed class SidecarMuxConnectionManager
{
    private readonly ConcurrentDictionary<string, MuxClient> _clients = new();
    private readonly SidecarSessionManager _sessionManager;

    public SidecarMuxConnectionManager(SidecarSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public int ClientCount => _clients.Count;

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

    public async Task BroadcastTerminalOutputAsync(string sessionId, ReadOnlyMemory<byte> data)
    {
        var frame = MuxProtocol.CreateOutputFrame(sessionId, data.Span);
        await BroadcastFrameAsync(frame);
    }

    public async Task BroadcastSessionStateAsync(string sessionId, bool created)
    {
        var frame = MuxProtocol.CreateStateFrame(sessionId, created);
        await BroadcastFrameAsync(frame);
    }

    private async Task BroadcastFrameAsync(byte[] frame)
    {
        var deadClients = new List<string>();

        // Sequential sends to preserve ordering across all clients
        foreach (var (clientId, client) in _clients)
        {
            if (client.WebSocket.State != WebSocketState.Open)
            {
                deadClients.Add(clientId);
                continue;
            }

            try
            {
                await client.SendAsync(frame).ConfigureAwait(false);
            }
            catch
            {
                deadClients.Add(clientId);
            }
        }

        foreach (var id in deadClients)
        {
            _clients.TryRemove(id, out _);
        }
    }

    public async Task HandleInputAsync(string sessionId, ReadOnlyMemory<byte> data, string clientId)
    {
        var session = _sessionManager.GetSession(sessionId);
        if (session is null)
        {
            return;
        }

        await _sessionManager.SendInputAsync(sessionId, data);
    }

    public async Task HandleResizeAsync(string sessionId, int cols, int rows, string clientId)
    {
        var session = _sessionManager.GetSession(sessionId);
        if (session is null)
        {
            return;
        }

        await _sessionManager.ResizeAsync(sessionId, cols, rows);
    }
}
