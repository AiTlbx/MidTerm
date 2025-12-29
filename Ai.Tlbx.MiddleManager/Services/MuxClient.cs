using System.Net.WebSockets;

namespace Ai.Tlbx.MiddleManager.Services;

public sealed class MuxClient
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public string Id { get; }
    public WebSocket WebSocket { get; }

    public MuxClient(string id, WebSocket webSocket)
    {
        Id = id;
        WebSocket = webSocket;
    }

    public async Task SendAsync(byte[] data)
    {
        await _sendLock.WaitAsync();
        try
        {
            if (WebSocket.State == WebSocketState.Open)
            {
                await WebSocket.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
