using System.Net.WebSockets;
using System.Threading.Channels;

namespace Ai.Tlbx.MiddleManager.Services;

public sealed class MuxClient : IAsyncDisposable
{
    private const int ResyncThreshold = 200;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Channel<byte[]> _outputQueue;
    private readonly Channel<byte[]> _pendingQueue; // Frames arriving during resync
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _outputProcessor;
    private volatile bool _needsResync;
    private volatile bool _isResyncing;

    public string Id { get; }
    public WebSocket WebSocket { get; }
    public bool NeedsResync => _needsResync;

    public MuxClient(string id, WebSocket webSocket)
    {
        Id = id;
        WebSocket = webSocket;
        _outputQueue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _pendingQueue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _outputProcessor = ProcessOutputQueueAsync(_cts.Token);
    }

    public void QueueOutput(byte[] frame)
    {
        if (_cts.IsCancellationRequested) return;

        // During resync, queue to pending (these arrive AFTER buffer capture)
        if (_isResyncing)
        {
            _pendingQueue.Writer.TryWrite(frame);
            return;
        }

        // Check if queue is backing up - trigger resync
        var count = _outputQueue.Reader.Count;
        if (count >= ResyncThreshold && !_needsResync)
        {
            _needsResync = true;
            DebugLogger.Log($"[MuxClient] {Id}: Queue backed up ({count} frames), will resync");
        }

        // Always queue - we'll discard stale frames during resync
        _outputQueue.Writer.TryWrite(frame);
    }

    public async Task PerformResyncAsync(Func<MuxClient, Task> sendBuffersAsync)
    {
        _isResyncing = true;
        DebugLogger.Log($"[MuxClient] {Id}: Starting resync");

        // Drain main queue (stale frames - buffer has fresher data)
        var discarded = 0;
        while (_outputQueue.Reader.TryRead(out _)) discarded++;
        DebugLogger.Log($"[MuxClient] {Id}: Discarded {discarded} stale frames");

        // Send fresh buffer content directly (bypasses queue)
        await sendBuffersAsync(this).ConfigureAwait(false);

        // Now drain pending queue - these frames arrived AFTER buffer capture
        var pending = 0;
        while (_pendingQueue.Reader.TryRead(out var frame))
        {
            await SendDirectAsync(frame).ConfigureAwait(false);
            pending++;
        }
        if (pending > 0)
        {
            DebugLogger.Log($"[MuxClient] {Id}: Sent {pending} pending frames");
        }

        _needsResync = false;
        _isResyncing = false;
        DebugLogger.Log($"[MuxClient] {Id}: Resync complete");
    }

    private async Task ProcessOutputQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _outputQueue.Reader.ReadAllAsync(ct))
            {
                await SendDirectAsync(frame).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DebugLogger.LogException($"MuxClient.ProcessOutputQueue({Id})", ex);
        }
    }

    private async Task SendDirectAsync(byte[] data)
    {
        await _sendLock.WaitAsync();
        try
        {
            if (WebSocket.State == WebSocketState.Open)
            {
                await WebSocket.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[MuxClient] {Id}: Send failed: {ex.Message}");
        }
        finally
        {
            _sendLock.Release();
        }
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

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _outputQueue.Writer.Complete();

        try
        {
            await _outputProcessor.ConfigureAwait(false);
        }
        catch
        {
        }

        _cts.Dispose();
        _sendLock.Dispose();
    }
}
