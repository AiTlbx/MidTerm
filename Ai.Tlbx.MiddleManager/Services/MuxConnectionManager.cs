using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Ai.Tlbx.MiddleManager.Services;

public sealed class MuxConnectionManager
{
    private readonly ConcurrentDictionary<string, MuxClient> _clients = new();
    private readonly SessionManager _sessionManager;

    public MuxConnectionManager(SessionManager sessionManager)
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

    public async Task RemoveClientAsync(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void BroadcastTerminalOutput(string sessionId, ReadOnlyMemory<byte> data)
    {
        var session = _sessionManager.GetSession(sessionId);
        var cols = session?.Cols ?? 80;
        var rows = session?.Rows ?? 24;

        var frame = MuxProtocol.CreateOutputFrame(sessionId, cols, rows, data.Span);
        BroadcastFrame(frame);
    }

    public void BroadcastSessionState(string sessionId, bool created)
    {
        var frame = MuxProtocol.CreateStateFrame(sessionId, created);
        BroadcastFrame(frame);
    }

    private void BroadcastFrame(byte[] frame)
    {
        foreach (var (clientId, client) in _clients)
        {
            if (client.WebSocket.State == WebSocketState.Open)
            {
                client.QueueOutput(frame);
            }
        }
    }

    public async Task HandleInputAsync(string sessionId, ReadOnlyMemory<byte> data)
    {
        var session = _sessionManager.GetSession(sessionId);
        if (session is null)
        {
            return;
        }

        var input = System.Text.Encoding.UTF8.GetString(data.Span);
        await session.SendInputAsync(input);
    }

    public void HandleResize(string sessionId, int cols, int rows)
    {
        var session = _sessionManager.GetSession(sessionId);
        session?.Resize(cols, rows);
    }
}
