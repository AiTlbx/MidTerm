using System.Net.WebSockets;
using System.Threading.Channels;

namespace Ai.Tlbx.MiddleManager.Services;

public sealed class MuxClient : IAsyncDisposable
{
    private const int MaxQueuedFrames = 500;

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
        _outputQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaxQueuedFrames)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
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
        if (WebSocket.State != WebSocketState.Open) return;

        // During resync, queue to pending (will be sent after buffer)
        if (_isResyncing)
        {
            _pendingQueue.Writer.TryWrite(frame);
            return;
        }

        // Check if queue is full - if so, we're about to drop frames
        var queueCount = _outputQueue.Reader.Count;
        if (queueCount >= MaxQueuedFrames - 1 && !_needsResync)
        {
            _needsResync = true;
            DebugLogger.Log($"[MuxClient] {Id}: Queue full ({queueCount}), flagged for resync");
        }

        _outputQueue.Writer.TryWrite(frame);
    }

    public async Task PerformResyncAsync(Func<MuxClient, Task> sendBuffersAsync)
    {
        _isResyncing = true;
        DebugLogger.Log($"[MuxClient] {Id}: Starting resync");

        // Drain main queue (these frames are incomplete/corrupted due to drops)
        var discarded = 0;
        while (_outputQueue.Reader.TryRead(out _)) discarded++;
        DebugLogger.Log($"[MuxClient] {Id}: Discarded {discarded} stale frames");

        // Send fresh buffer content (complete, consistent state)
        await sendBuffersAsync(this).ConfigureAwait(false);

        // Send any frames that arrived during resync (these are NEW, after buffer snapshot)
        var pending = 0;
        while (_pendingQueue.Reader.TryRead(out var frame))
        {
            await SendFrameAsync(frame).ConfigureAwait(false);
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
                // Skip sending if we need resync (frames are corrupted anyway)
                if (_needsResync) continue;

                if (WebSocket.State != WebSocketState.Open)
                {
                    while (_outputQueue.Reader.TryRead(out _)) { }
                    break;
                }

                await SendFrameAsync(frame).ConfigureAwait(false);
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

    private async Task SendFrameAsync(byte[] data)
    {
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (WebSocket.State == WebSocketState.Open)
            {
                await WebSocket.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
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
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (WebSocket.State == WebSocketState.Open)
            {
                await WebSocket.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
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
        _pendingQueue.Writer.Complete();

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
