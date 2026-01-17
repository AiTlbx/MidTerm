using System.Collections.Concurrent;
using System.Net.WebSockets;
using Ai.Tlbx.MidTerm.Common.Logging;

namespace Ai.Tlbx.MidTerm.Services;

/// <summary>
/// Per-client state for log WebSocket connections.
/// Thread-safe for concurrent broadcast and message handling.
/// </summary>
public sealed class LogClient : IDisposable
{
    private readonly WebSocket _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _subscribedSessions = new();
    private volatile bool _disposed;

    public string Id { get; }
    public volatile bool SubscribedToMt;

    public bool IsOpen => !_disposed && _ws.State == WebSocketState.Open;

    public LogClient(string id, WebSocket ws)
    {
        Id = id;
        _ws = ws;
    }

    public void AddSubscribedSession(string sessionId) => _subscribedSessions.TryAdd(sessionId, 0);
    public void RemoveSubscribedSession(string sessionId) => _subscribedSessions.TryRemove(sessionId, out _);
    public bool IsSubscribedToSession(string sessionId) => _subscribedSessions.ContainsKey(sessionId);

    public async Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken ct)
    {
        return await _ws.ReceiveAsync(buffer, ct);
    }

    public async Task<bool> TrySendAsync(byte[] bytes, int timeoutMs)
    {
        if (_disposed || _ws.State != WebSocketState.Open) return false;

        var acquired = false;
        try
        {
            acquired = await _sendLock.WaitAsync(timeoutMs).ConfigureAwait(false);
            if (!acquired)
            {
                Log.Warn(() => $"[LogWS] Send timeout for client {Id}, dropping message");
                return false;
            }

            if (_disposed || _ws.State != WebSocketState.Open) return false;

            using var cts = new CancellationTokenSource(timeoutMs);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Warn(() => $"[LogWS] Send cancelled for client {Id}");
            return false;
        }
        catch (WebSocketException)
        {
            return false;
        }
        finally
        {
            if (acquired) _sendLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sendLock.Dispose();
    }
}
