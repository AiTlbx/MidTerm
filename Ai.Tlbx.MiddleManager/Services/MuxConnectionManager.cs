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
        var sendTasks = new List<Task<(string clientId, bool success)>>();

        foreach (var (clientId, client) in _clients)
        {
            if (client.WebSocket.State != WebSocketState.Open)
            {
                deadClients.Add(clientId);
                continue;
            }

            sendTasks.Add(SendToClientAsync(clientId, client, frame));
        }

        if (sendTasks.Count > 0)
        {
            var results = await Task.WhenAll(sendTasks);
            foreach (var (clientId, success) in results)
            {
                if (!success)
                {
                    deadClients.Add(clientId);
                }
            }
        }

        foreach (var id in deadClients)
        {
            _clients.TryRemove(id, out _);
        }
    }

    private static async Task<(string clientId, bool success)> SendToClientAsync(string clientId, MuxClient client, byte[] frame)
    {
        try
        {
            await client.SendAsync(frame);
            return (clientId, true);
        }
        catch
        {
            return (clientId, false);
        }
    }

    public async Task HandleInputAsync(string sessionId, ReadOnlyMemory<byte> data, string clientId)
    {
        var session = _sessionManager.GetSession(sessionId);
        if (session is null)
        {
            return;
        }

        var input = System.Text.Encoding.UTF8.GetString(data.Span);
        await session.SendInputAsync(input, clientId);
    }

    public void HandleResize(string sessionId, int cols, int rows, string clientId)
    {
        var session = _sessionManager.GetSession(sessionId);
        session?.Resize(cols, rows, clientId);
    }
}
