using System.Text;
using Ai.Tlbx.MiddleManager.Settings;
using Ai.Tlbx.MiddleManager.Shells;
using Ai.Tlbx.MiddleManager.Services;
using Xunit;

namespace Ai.Tlbx.MiddleManager.Tests;

public class MultiplexingTests : IDisposable
{
    private readonly SessionManager _sessionManager = new(new ShellRegistry(), new SettingsService());
    private readonly MuxConnectionManager _muxManager;

    public MultiplexingTests()
    {
        _muxManager = new MuxConnectionManager(_sessionManager);
        _sessionManager.SetMuxManager(_muxManager);
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
    }

    [Fact]
    public async Task MultipleSessions_CanBeCreated()
    {
        var session1 = _sessionManager.CreateSession();
        var session2 = _sessionManager.CreateSession();

        // Wait for both to produce output
        await WaitForAnyBufferContent(session1, 5000);
        await WaitForAnyBufferContent(session2, 5000);

        Assert.NotEqual(session1.Id, session2.Id);
        Assert.True(session1.IsRunning);
        Assert.True(session2.IsRunning);
        Assert.NotEmpty(session1.GetBuffer());
        Assert.NotEmpty(session2.GetBuffer());
    }

    [Fact]
    public void MuxProtocol_CreateOutputFrame_ValidFormat()
    {
        var sessionId = "abcd1234";
        var payload = Encoding.UTF8.GetBytes("test output");

        var frame = MuxProtocol.CreateOutputFrame(sessionId, payload);

        Assert.Equal(MuxProtocol.HeaderSize + payload.Length, frame.Length);
        Assert.Equal(MuxProtocol.TypeTerminalOutput, frame[0]);

        var extractedId = Encoding.ASCII.GetString(frame, 1, 8);
        Assert.Equal(sessionId, extractedId);
    }

    [Fact]
    public void MuxProtocol_TryParseFrame_ParsesCorrectly()
    {
        var sessionId = "test1234";
        var payload = Encoding.UTF8.GetBytes("hello world");
        var frame = MuxProtocol.CreateOutputFrame(sessionId, payload);

        var success = MuxProtocol.TryParseFrame(frame, out var type, out var parsedId, out var parsedPayload);

        Assert.True(success);
        Assert.Equal(MuxProtocol.TypeTerminalOutput, type);
        Assert.Equal(sessionId, parsedId);
        Assert.Equal(payload, parsedPayload.ToArray());
    }

    [Fact]
    public void MuxProtocol_TryParseFrame_FailsOnShortFrame()
    {
        var shortFrame = new byte[5];

        var success = MuxProtocol.TryParseFrame(shortFrame, out _, out _, out _);

        Assert.False(success);
    }

    [Fact]
    public void MuxProtocol_ResizePayload_RoundTrips()
    {
        var cols = 120;
        var rows = 40;

        var payload = MuxProtocol.CreateResizePayload(cols, rows);
        var (parsedCols, parsedRows) = MuxProtocol.ParseResizePayload(payload);

        Assert.Equal(cols, parsedCols);
        Assert.Equal(rows, parsedRows);
    }

    [Fact]
    public void MuxProtocol_CreateStateFrame_ValidFormat()
    {
        var sessionId = "test1234";

        var frame = MuxProtocol.CreateStateFrame(sessionId, created: true);

        Assert.Equal(MuxProtocol.HeaderSize + 1, frame.Length);
        Assert.Equal(MuxProtocol.TypeSessionState, frame[0]);

        var extractedId = Encoding.ASCII.GetString(frame, 1, 8);
        Assert.Equal(sessionId, extractedId);
    }

    [Fact]
    public void HandleResize_RoutesToCorrectSession()
    {
        var session1 = _sessionManager.CreateSession(cols: 80, rows: 24);
        var session2 = _sessionManager.CreateSession(cols: 80, rows: 24);

        _muxManager.HandleResize(session1.Id, 120, 40, "client1");

        Assert.Equal(120, session1.Cols);
        Assert.Equal(40, session1.Rows);
        Assert.Equal(80, session2.Cols);
        Assert.Equal(24, session2.Rows);
    }

    [Fact]
    public void HandleResize_InvalidSession_DoesNotThrow()
    {
        _muxManager.HandleResize("nonexistent", 120, 40, "client1");
    }

    private static async Task<bool> WaitForAnyBufferContent(TerminalSession session, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.IsNullOrEmpty(session.GetBuffer()))
            {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }
}
