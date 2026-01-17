using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Ai.Tlbx.MidTerm.Common.Logging;

namespace Ai.Tlbx.MidTerm.Services;

/// <summary>
/// WebSocket client with per-session output buffering.
/// Active session gets immediate delivery; background sessions batch for efficiency.
/// </summary>
public sealed class MuxClient : IAsyncDisposable
{
    private const int FlushThresholdBytes = MuxProtocol.CompressionThreshold;
    private const int MaxBufferBytesPerSession = 65536;
    private const int MaxQueuedItems = 1000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LoopCheckInterval = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Channel<OutputItem> _inputChannel;
    private readonly Dictionary<string, SessionBuffer> _sessionBuffers = new();
    private readonly ConcurrentQueue<string> _sessionsToRemove = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processor;

    private volatile string? _activeSessionId;
    private int _droppedFrameCount;

    public string Id { get; }
    public WebSocket WebSocket { get; }

    private readonly record struct OutputItem(string SessionId, int Cols, int Rows, byte[] Data);

    private sealed class SessionBuffer
    {
        public Queue<byte[]> DataChunks { get; } = new();
        public int TotalBytes { get; set; }
        public int LastCols { get; set; }
        public int LastRows { get; set; }
        public long LastFlushTicks { get; set; } = Environment.TickCount64;
    }

    public MuxClient(string id, WebSocket webSocket)
    {
        Id = id;
        WebSocket = webSocket;
        _inputChannel = Channel.CreateBounded<OutputItem>(new BoundedChannelOptions(MaxQueuedItems)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _processor = ProcessLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Queue raw terminal output for buffered delivery.
    /// </summary>
    public void QueueOutput(string sessionId, int cols, int rows, byte[] data)
    {
        if (_cts.IsCancellationRequested) return;
        if (WebSocket.State != WebSocketState.Open) return;

        var queueCount = _inputChannel.Reader.Count;
        if (queueCount >= MaxQueuedItems - 1)
        {
            var newCount = Interlocked.Increment(ref _droppedFrameCount);
            if (newCount == 1)
            {
                Log.Warn(() => $"[MuxClient] {Id}: Input queue full, dropping items");
            }
        }

        _inputChannel.Writer.TryWrite(new OutputItem(sessionId, cols, rows, data));
    }

    /// <summary>
    /// Set the active session for priority delivery.
    /// </summary>
    public void SetActiveSession(string? sessionId)
    {
        _activeSessionId = sessionId;
    }

    /// <summary>
    /// Check if frames were dropped and a resync is needed.
    /// </summary>
    public bool CheckAndResetDroppedFrames()
    {
        var count = Interlocked.Exchange(ref _droppedFrameCount, 0);
        return count > 0;
    }

    /// <summary>
    /// Queue session buffer removal (thread-safe, processed by loop).
    /// </summary>
    public void RemoveSession(string sessionId)
    {
        _sessionsToRemove.Enqueue(sessionId);
    }

    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        var reader = _inputChannel.Reader;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 1. Process pending session removals
                while (_sessionsToRemove.TryDequeue(out var sessionId))
                {
                    _sessionBuffers.Remove(sessionId);
                }

                // 2. Drain all immediately available items into buffers
                while (reader.TryRead(out var item))
                {
                    BufferOutput(item);
                }

                // 3. Flush what's due (active immediately, background if threshold/time)
                var now = Environment.TickCount64;
                await FlushDueBuffersAsync(now).ConfigureAwait(false);

                // 4. Wait for more data OR timeout (to check time-based flushes)
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(LoopCheckInterval);
                    await reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Timeout - continue to check time-based flushes
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Log.Exception(ex, $"MuxClient.ProcessLoop({Id})");
        }
    }

    private void BufferOutput(OutputItem item)
    {
        if (!_sessionBuffers.TryGetValue(item.SessionId, out var buffer))
        {
            buffer = new SessionBuffer();
            _sessionBuffers[item.SessionId] = buffer;
        }

        // Enforce per-session limit (drop oldest if exceeded)
        while (buffer.TotalBytes + item.Data.Length > MaxBufferBytesPerSession
               && buffer.DataChunks.Count > 0)
        {
            var oldest = buffer.DataChunks.Dequeue();
            buffer.TotalBytes -= oldest.Length;
        }

        buffer.DataChunks.Enqueue(item.Data);
        buffer.TotalBytes += item.Data.Length;
        buffer.LastCols = item.Cols;
        buffer.LastRows = item.Rows;
    }

    private async Task FlushDueBuffersAsync(long nowTicks)
    {
        if (WebSocket.State != WebSocketState.Open) return;

        foreach (var (sessionId, buffer) in _sessionBuffers)
        {
            if (buffer.DataChunks.Count == 0) continue;

            bool shouldFlush;
            if (sessionId == _activeSessionId)
            {
                // Active: ALWAYS flush immediately
                shouldFlush = true;
            }
            else
            {
                // Background: flush if size threshold OR time elapsed
                var elapsedMs = nowTicks - buffer.LastFlushTicks;
                shouldFlush = buffer.TotalBytes >= FlushThresholdBytes
                           || elapsedMs >= (long)FlushInterval.TotalMilliseconds;
            }

            if (shouldFlush)
            {
                await FlushBufferAsync(sessionId, buffer).ConfigureAwait(false);
                buffer.LastFlushTicks = nowTicks;
            }
        }
    }

    private async Task FlushBufferAsync(string sessionId, SessionBuffer buffer)
    {
        if (buffer.DataChunks.Count == 0) return;

        // Combine all chunks into pooled buffer (reduces GC pressure)
        var totalLen = buffer.DataChunks.Sum(c => c.Length);
        var combined = ArrayPool<byte>.Shared.Rent(totalLen);
        try
        {
            var offset = 0;
            foreach (var chunk in buffer.DataChunks)
            {
                Buffer.BlockCopy(chunk, 0, combined, offset, chunk.Length);
                offset += chunk.Length;
            }

            // Create frame using exact span (rented buffer may be larger)
            var data = combined.AsSpan(0, totalLen);
            var frame = totalLen > MuxProtocol.CompressionThreshold
                ? MuxProtocol.CreateCompressedOutputFrame(sessionId, buffer.LastCols, buffer.LastRows, data)
                : MuxProtocol.CreateOutputFrame(sessionId, buffer.LastCols, buffer.LastRows, data);

            // Send first, clear after - prevents data loss on send failure
            await SendFrameAsync(frame).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(combined);
        }

        buffer.DataChunks.Clear();
        buffer.TotalBytes = 0;
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
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Queue a pre-built frame to be sent immediately (fire-and-forget).
    /// Used for process events and foreground changes.
    /// </summary>
    public void QueueFrame(byte[] frame)
    {
        if (_cts.IsCancellationRequested) return;
        if (WebSocket.State != WebSocketState.Open) return;

        _ = SendFrameAsync(frame);
    }

    /// <summary>
    /// Send a frame directly (bypassing buffering) - used for init/sync frames.
    /// </summary>
    public async Task<bool> TrySendAsync(byte[] data)
    {
        if (WebSocket.State != WebSocketState.Open) return false;

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (WebSocket.State == WebSocketState.Open)
            {
                await WebSocket.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(() => $"[MuxClient] {Id}: TrySend failed: {ex.Message}");
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _inputChannel.Writer.Complete();

        try
        {
            await _processor.ConfigureAwait(false);
        }
        catch
        {
            // Ignore shutdown errors
        }

        _cts.Dispose();
        _sendLock.Dispose();
    }
}
