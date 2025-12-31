using System.IO.Pipes;
using System.Text;
using Ai.Tlbx.MiddleManager.Ipc;
using Ai.Tlbx.MiddleManager.Services;
using Xunit;

namespace Ai.Tlbx.MiddleManager.Tests;

public class SidecarClientTests : IAsyncLifetime
{
    private readonly string _pipeName = $"mm-test-{Guid.NewGuid():N}";
    private NamedPipeServerStream? _serverPipe;
    private CancellationTokenSource? _cts;

    public async Task InitializeAsync()
    {
        _cts = new CancellationTokenSource();
        _serverPipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    public async Task DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        if (_serverPipe is not null)
        {
            await _serverPipe.DisposeAsync();
        }
    }

    [Fact]
    public async Task SidecarClient_ReceivesPingAfterHandshake_RespondsWithPong()
    {
        var serverTask = RunMockServerAsync(async (stream, ct) =>
        {
            // Wait for handshake
            var handshakeFrame = await ReadFrameFromPipeAsync(stream, ct);
            Assert.NotNull(handshakeFrame);
            Assert.Equal(IpcMessageType.Handshake, handshakeFrame.Value.Type);

            // Send handshake ack
            await WriteFrameToPipeAsync(stream, new IpcFrame(IpcMessageType.HandshakeAck), ct);

            // Wait a bit for read loop to start
            await Task.Delay(100, ct);

            // Send Ping
            await WriteFrameToPipeAsync(stream, new IpcFrame(IpcMessageType.Ping), ct);

            // Wait for Pong
            var pongFrame = await ReadFrameFromPipeAsync(stream, ct);
            Assert.NotNull(pongFrame);
            Assert.Equal(IpcMessageType.Pong, pongFrame.Value.Type);
        });

        var clientTransport = new TestNamedPipeTransport(_pipeName);
        await using var client = new SidecarClient();

        // Use reflection to inject our test transport
        var transportField = typeof(SidecarClient).GetField("_transport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var connected = await ConnectClientWithTestTransportAsync(client, clientTransport);
        Assert.True(connected);

        // Wait for server to complete its assertions
        using var timeout = new CancellationTokenSource(5000);
        await serverTask.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task SidecarClient_ReadLoopDoesNotBlock_WhenProcessingMultipleFrames()
    {
        var framesReceived = 0;
        var serverCompleted = new TaskCompletionSource();

        var serverTask = RunMockServerAsync(async (stream, ct) =>
        {
            try
            {
                // Wait for handshake
                var handshakeFrame = await ReadFrameFromPipeAsync(stream, ct);
                Assert.NotNull(handshakeFrame);
                await WriteFrameToPipeAsync(stream, new IpcFrame(IpcMessageType.HandshakeAck), ct);

                // Wait for read loop
                await Task.Delay(200, ct);

                // Send multiple Pings with small delays
                for (var i = 0; i < 3; i++)
                {
                    await WriteFrameToPipeAsync(stream, new IpcFrame(IpcMessageType.Ping), ct);

                    // Wait for Pong before sending next Ping
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readCts.CancelAfter(2000);
                    var pongFrame = await ReadFrameFromPipeAsync(stream, readCts.Token);
                    if (pongFrame?.Type == IpcMessageType.Pong)
                    {
                        Interlocked.Increment(ref framesReceived);
                    }
                }
                serverCompleted.TrySetResult();
            }
            catch (Exception ex)
            {
                serverCompleted.TrySetException(ex);
            }
        });

        var clientTransport = new TestNamedPipeTransport(_pipeName);
        await using var client = new SidecarClient();
        var connected = await ConnectClientWithTestTransportAsync(client, clientTransport);
        Assert.True(connected);

        using var timeout = new CancellationTokenSource(10000);
        await serverCompleted.Task.WaitAsync(timeout.Token);

        Assert.Equal(3, framesReceived);
    }

    [Fact]
    public async Task SidecarClient_CreateSession_ReturnsSessionSnapshot()
    {
        var testSession = new SessionSnapshot
        {
            Id = "test1234",
            ShellType = "pwsh",
            IsRunning = true,
            Cols = 80,
            Rows = 24,
            CreatedAt = DateTime.UtcNow,
            Pid = 12345
        };

        var serverTask = RunMockServerAsync(async (stream, ct) =>
        {
            // Handshake
            await ReadFrameFromPipeAsync(stream, ct);
            await WriteFrameToPipeAsync(stream, new IpcFrame(IpcMessageType.HandshakeAck), ct);
            await Task.Delay(100, ct);

            // Wait for CreateSession request
            var createFrame = await ReadFrameFromPipeAsync(stream, ct);
            Assert.NotNull(createFrame);
            Assert.Equal(IpcMessageType.CreateSession, createFrame.Value.Type);

            // Send SessionCreated response with same requestId
            var payload = SidecarProtocol.CreateSessionCreatedPayload(testSession);
            await WriteFrameToPipeAsync(stream,
                new IpcFrame(IpcMessageType.SessionCreated, createFrame.Value.SessionId, payload), ct);
        });

        var clientTransport = new TestNamedPipeTransport(_pipeName);
        await using var client = new SidecarClient();
        var connected = await ConnectClientWithTestTransportAsync(client, clientTransport);
        Assert.True(connected);

        var request = new IpcCreateSessionRequest { Cols = 80, Rows = 24 };
        var result = await client.CreateSessionAsync(request);

        Assert.NotNull(result);
        Assert.Equal("test1234", result.Id);
        Assert.Equal("pwsh", result.ShellType);
        Assert.True(result.IsRunning);

        using var timeout = new CancellationTokenSource(5000);
        await serverTask.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task SidecarClient_ListSessions_ReturnsSessionList()
    {
        var testSessions = new List<SessionSnapshot>
        {
            new() { Id = "sess1234", ShellType = "pwsh", IsRunning = true, Cols = 80, Rows = 24, CreatedAt = DateTime.UtcNow, Pid = 111 },
            new() { Id = "sess5678", ShellType = "bash", IsRunning = true, Cols = 120, Rows = 40, CreatedAt = DateTime.UtcNow, Pid = 222 }
        };

        var serverTask = RunMockServerAsync(async (stream, ct) =>
        {
            // Handshake
            await ReadFrameFromPipeAsync(stream, ct);
            await WriteFrameToPipeAsync(stream, new IpcFrame(IpcMessageType.HandshakeAck), ct);
            await Task.Delay(100, ct);

            // Wait for ListSessions
            var listFrame = await ReadFrameFromPipeAsync(stream, ct);
            Assert.NotNull(listFrame);
            Assert.Equal(IpcMessageType.ListSessions, listFrame.Value.Type);

            // Send SessionList response
            var payload = SidecarProtocol.CreateSessionListPayload(testSessions);
            await WriteFrameToPipeAsync(stream,
                new IpcFrame(IpcMessageType.SessionList, string.Empty, payload), ct);
        });

        var clientTransport = new TestNamedPipeTransport(_pipeName);
        await using var client = new SidecarClient();
        var connected = await ConnectClientWithTestTransportAsync(client, clientTransport);
        Assert.True(connected);

        var sessions = await client.ListSessionsAsync();

        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.Id == "sess1234");
        Assert.Contains(sessions, s => s.Id == "sess5678");

        using var timeout = new CancellationTokenSource(5000);
        await serverTask.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task SidecarClient_SequentialRequests_AllSucceed()
    {
        var requestCount = 0;
        var serverCompleted = new TaskCompletionSource();

        var serverTask = RunMockServerAsync(async (stream, ct) =>
        {
            try
            {
                // Handshake
                await ReadFrameFromPipeAsync(stream, ct);
                await WriteFrameToPipeAsync(stream, new IpcFrame(IpcMessageType.HandshakeAck), ct);
                await Task.Delay(100, ct);

                // Handle 2 sequential CreateSession requests
                for (var i = 0; i < 2; i++)
                {
                    var frame = await ReadFrameFromPipeAsync(stream, ct);
                    if (frame?.Type == IpcMessageType.CreateSession)
                    {
                        Interlocked.Increment(ref requestCount);
                        var session = new SessionSnapshot
                        {
                            Id = $"sess{i:00}",
                            ShellType = "pwsh",
                            IsRunning = true,
                            Cols = 80,
                            Rows = 24,
                            CreatedAt = DateTime.UtcNow,
                            Pid = 1000 + i
                        };
                        var payload = SidecarProtocol.CreateSessionCreatedPayload(session);
                        await WriteFrameToPipeAsync(stream,
                            new IpcFrame(IpcMessageType.SessionCreated, frame.Value.SessionId, payload), ct);
                    }
                }
                serverCompleted.TrySetResult();
            }
            catch (Exception ex)
            {
                serverCompleted.TrySetException(ex);
            }
        });

        var clientTransport = new TestNamedPipeTransport(_pipeName);
        await using var client = new SidecarClient();
        var connected = await ConnectClientWithTestTransportAsync(client, clientTransport);
        Assert.True(connected);

        // Make sequential requests (simpler than concurrent)
        var request = new IpcCreateSessionRequest { Cols = 80, Rows = 24 };
        var result1 = await client.CreateSessionAsync(request);
        var result2 = await client.CreateSessionAsync(request);

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal("sess00", result1.Id);
        Assert.Equal("sess01", result2.Id);

        using var timeout = new CancellationTokenSource(5000);
        await serverCompleted.Task.WaitAsync(timeout.Token);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task SidecarClient_IsHealthy_UpdatesOnPing()
    {
        var serverTask = RunMockServerAsync(async (stream, ct) =>
        {
            // Handshake
            await ReadFrameFromPipeAsync(stream, ct);
            await WriteFrameToPipeAsync(stream, new IpcFrame(IpcMessageType.HandshakeAck), ct);
            await Task.Delay(100, ct);

            // Send Ping
            await WriteFrameToPipeAsync(stream, new IpcFrame(IpcMessageType.Ping), ct);
            await ReadFrameFromPipeAsync(stream, ct); // Pong

            // Keep alive for a bit
            await Task.Delay(500, ct);
        });

        var clientTransport = new TestNamedPipeTransport(_pipeName);
        await using var client = new SidecarClient();
        var connected = await ConnectClientWithTestTransportAsync(client, clientTransport);
        Assert.True(connected);
        Assert.True(client.IsHealthy);

        // Wait for ping and verify still healthy
        await Task.Delay(200);
        Assert.True(client.IsHealthy);

        using var timeout = new CancellationTokenSource(5000);
        await serverTask.WaitAsync(timeout.Token);
    }

    private async Task<bool> ConnectClientWithTestTransportAsync(SidecarClient client, TestNamedPipeTransport transport)
    {
        // We need to inject the transport and connect manually
        // This is a bit hacky but necessary for testing without changing production code
        var transportField = typeof(SidecarClient).GetField("_transport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var connectedField = typeof(SidecarClient).GetField("_connected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var lastPingField = typeof(SidecarClient).GetField("_lastPingTicks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var readCtsField = typeof(SidecarClient).GetField("_readCts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        try
        {
            await transport.ConnectAsync();

            // Send handshake
            var handshakePayload = SidecarProtocol.CreateHandshakePayload(string.Empty);
            await transport.WriteFrameAsync(new IpcFrame(IpcMessageType.Handshake, string.Empty, handshakePayload));

            var response = await transport.ReadFrameAsync();
            if (response?.Type != IpcMessageType.HandshakeAck)
            {
                return false;
            }

            // Set fields via reflection
            transportField?.SetValue(client, transport);
            connectedField?.SetValue(client, 1);
            lastPingField?.SetValue(client, DateTime.UtcNow.Ticks);

            // Start read loop
            var readCts = new CancellationTokenSource();
            readCtsField?.SetValue(client, readCts);

            var readLoopMethod = typeof(SidecarClient).GetMethod("ReadLoopAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _ = (Task?)readLoopMethod?.Invoke(client, [readCts.Token]);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RunMockServerAsync(Func<Stream, CancellationToken, Task> serverLogic)
    {
        await _serverPipe!.WaitForConnectionAsync(_cts!.Token);
        await serverLogic(_serverPipe, _cts.Token);
    }

    private static async Task<IpcFrame?> ReadFrameFromPipeAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[IpcFrame.HeaderSize];
        var bytesRead = 0;
        while (bytesRead < IpcFrame.HeaderSize)
        {
            var read = await stream.ReadAsync(header.AsMemory(bytesRead, IpcFrame.HeaderSize - bytesRead), ct);
            if (read == 0)
            {
                return null;
            }
            bytesRead += read;
        }

        if (!SidecarProtocol.TryParseHeader(header, out var type, out var sessionId, out var payloadLength))
        {
            return null;
        }

        ReadOnlyMemory<byte> payload = ReadOnlyMemory<byte>.Empty;
        if (payloadLength > 0)
        {
            var payloadBytes = new byte[payloadLength];
            bytesRead = 0;
            while (bytesRead < payloadLength)
            {
                var read = await stream.ReadAsync(payloadBytes.AsMemory(bytesRead, payloadLength - bytesRead), ct);
                if (read == 0)
                {
                    return null;
                }
                bytesRead += read;
            }
            payload = payloadBytes;
        }

        return new IpcFrame(type, sessionId, payload);
    }

    private static async Task WriteFrameToPipeAsync(Stream stream, IpcFrame frame, CancellationToken ct)
    {
        var bytes = SidecarProtocol.SerializeFrame(frame);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }
}

internal sealed class TestNamedPipeTransport : IIpcTransport
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;

    public TestNamedPipeTransport(string pipeName)
    {
        _pipeName = pipeName;
    }

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(cancellationToken);
    }

    public async Task<IpcFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        if (_pipe is null || !_pipe.IsConnected)
        {
            return null;
        }

        var header = new byte[IpcFrame.HeaderSize];
        var bytesRead = 0;
        while (bytesRead < IpcFrame.HeaderSize)
        {
            var read = await _pipe.ReadAsync(header.AsMemory(bytesRead, IpcFrame.HeaderSize - bytesRead), cancellationToken);
            if (read == 0)
            {
                return null;
            }
            bytesRead += read;
        }

        if (!SidecarProtocol.TryParseHeader(header, out var type, out var sessionId, out var payloadLength))
        {
            return null;
        }

        ReadOnlyMemory<byte> payload = ReadOnlyMemory<byte>.Empty;
        if (payloadLength > 0)
        {
            var payloadBytes = new byte[payloadLength];
            bytesRead = 0;
            while (bytesRead < payloadLength)
            {
                var read = await _pipe.ReadAsync(payloadBytes.AsMemory(bytesRead, payloadLength - bytesRead), cancellationToken);
                if (read == 0)
                {
                    return null;
                }
                bytesRead += read;
            }
            payload = payloadBytes;
        }

        return new IpcFrame(type, sessionId, payload);
    }

    public async Task WriteFrameAsync(IpcFrame frame, CancellationToken cancellationToken = default)
    {
        if (_pipe is null || !_pipe.IsConnected)
        {
            return;
        }

        var bytes = SidecarProtocol.SerializeFrame(frame);
        await _pipe.WriteAsync(bytes, cancellationToken);
        await _pipe.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync();
        }
    }
}
