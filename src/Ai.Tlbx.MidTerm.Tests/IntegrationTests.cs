using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Ai.Tlbx.MidTerm.Models;
using Ai.Tlbx.MidTerm.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ai.Tlbx.MidTerm.Tests;

public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly List<WebSocket> _webSockets = [];
    private readonly List<string> _createdSessionIds = [];

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await CleanupAllSessionsAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var ws in _webSockets)
        {
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { }
            }
            ws.Dispose();
        }

        await CleanupAllSessionsAsync();
        _client.Dispose();
    }

    private async Task CleanupAllSessionsAsync()
    {
        try
        {
            var sessions = await _client.GetFromJsonAsync<SessionListDto>("/api/sessions", AppJsonContext.Default.SessionListDto);
            foreach (var s in sessions?.Sessions ?? [])
            {
                try { await _client.DeleteAsync($"/api/sessions/{s.Id}"); }
                catch { }
            }
        }
        catch { }
    }

    private async Task<SessionInfoDto> CreateSessionAndTrackAsync(int cols = 80, int rows = 24)
    {
        var response = await _client.PostAsJsonAsync("/api/sessions", new { Cols = cols, Rows = rows });
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<SessionInfoDto>(AppJsonContext.Default.SessionInfoDto);
        Assert.NotNull(session);
        _createdSessionIds.Add(session.Id);
        return session;
    }

    private async Task<SessionInfoDto?> WaitForSessionAsync(string sessionId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var sessions = await _client.GetFromJsonAsync<SessionListDto>("/api/sessions", AppJsonContext.Default.SessionListDto);
            var found = sessions?.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (found is not null) return found;
            await Task.Delay(100);
        }
        return null;
    }

    private async Task WaitForSessionRunningAsync(string sessionId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var sessions = await _client.GetFromJsonAsync<SessionListDto>("/api/sessions", AppJsonContext.Default.SessionListDto);
            var found = sessions?.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (found?.IsRunning == true) return;
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Api_GetVersion_ReturnsVersion()
    {
        var response = await _client.GetAsync("/api/version");

        response.EnsureSuccessStatusCode();
        var version = await response.Content.ReadAsStringAsync();

        Assert.NotEmpty(version);
    }

    [Fact]
    public async Task Api_CreateSession_ReturnsSessionInfo()
    {
        var session = await CreateSessionAndTrackAsync(80, 24);

        Assert.NotEmpty(session.Id);
        Assert.True(session.Pid > 0);
        Assert.True(session.IsRunning);
        Assert.Equal(80, session.Cols);
        Assert.Equal(24, session.Rows);
    }

    [Fact]
    public async Task Api_GetSessions_ListsCreatedSessions()
    {
        var created = await CreateSessionAndTrackAsync(100, 40);

        var found = await WaitForSessionAsync(created.Id, TimeSpan.FromSeconds(5));

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
    }

    [Fact]
    public async Task Api_Resize_UpdatesSessionDimensions()
    {
        var session = await CreateSessionAndTrackAsync(80, 24);
        await WaitForSessionRunningAsync(session.Id, TimeSpan.FromSeconds(5));

        var resizeResponse = await _client.PostAsJsonAsync($"/api/sessions/{session.Id}/resize", new { Cols = 120, Rows = 40 });
        resizeResponse.EnsureSuccessStatusCode();
        var resizeResult = await resizeResponse.Content.ReadFromJsonAsync<ResizeResponse>(AppJsonContext.Default.ResizeResponse);

        Assert.NotNull(resizeResult);
        Assert.True(resizeResult.Accepted);
        Assert.Equal(120, resizeResult.Cols);
        Assert.Equal(40, resizeResult.Rows);
    }

    private async Task<string> PollForBufferContentAsync(string sessionId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await _client.GetAsync($"/api/sessions/{sessionId}/buffer");
                if (response.IsSuccessStatusCode)
                {
                    var buffer = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(buffer)) return buffer;
                }
            }
            catch { }
            await Task.Delay(200);
        }
        return "";
    }

    [Fact]
    public async Task WebSocket_Mux_ReceivesInitFrame()
    {
        var ws = await ConnectWebSocket("/ws/mux");

        var buffer = new byte[1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await ws.ReceiveAsync(buffer, cts.Token);

        Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
        Assert.True(result.Count >= MuxProtocol.HeaderSize);
        Assert.Equal(0xFF, buffer[0]);
    }

    [Fact]
    public async Task WebSocket_Mux_SendInput_FrameSentSuccessfully()
    {
        var session = await CreateSessionAndTrackAsync(80, 24);
        await WaitForSessionRunningAsync(session.Id, TimeSpan.FromSeconds(5));

        var ws = await ConnectWebSocket("/ws/mux");
        await DrainInitialFrames(ws);

        var inputBytes = Encoding.UTF8.GetBytes("echo test\r\n");
        var frame = new byte[MuxProtocol.HeaderSize + inputBytes.Length];
        frame[0] = MuxProtocol.TypeTerminalInput;
        Encoding.ASCII.GetBytes(session.Id.AsSpan(0, 8), frame.AsSpan(1, 8));
        inputBytes.CopyTo(frame.AsSpan(MuxProtocol.HeaderSize));

        await ws.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);

        await Task.Delay(200);

        var found = await WaitForSessionAsync(session.Id, TimeSpan.FromSeconds(5));
        Assert.NotNull(found);
    }

    [Fact]
    public async Task WebSocket_Mux_SessionsHaveSeparatePidsAndIds()
    {
        var session1 = await CreateSessionAndTrackAsync(80, 24);
        var session2 = await CreateSessionAndTrackAsync(80, 24);

        Assert.NotEqual(session1.Id, session2.Id);
        Assert.NotEqual(session1.Pid, session2.Pid);
        Assert.True(session1.IsRunning);
        Assert.True(session2.IsRunning);

        await WaitForSessionRunningAsync(session1.Id, TimeSpan.FromSeconds(5));
        await WaitForSessionRunningAsync(session2.Id, TimeSpan.FromSeconds(5));

        var ws = await ConnectWebSocket("/ws/mux");
        await DrainInitialFrames(ws);

        var resizePayload = MuxProtocol.CreateResizePayload(100, 30);
        var frame = new byte[MuxProtocol.HeaderSize + resizePayload.Length];
        frame[0] = MuxProtocol.TypeResize;
        Encoding.ASCII.GetBytes(session1.Id.AsSpan(0, 8), frame.AsSpan(1, 8));
        resizePayload.CopyTo(frame.AsSpan(MuxProtocol.HeaderSize));

        await ws.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);
        await Task.Delay(300);

        var sessions = await _client.GetFromJsonAsync<SessionListDto>("/api/sessions", AppJsonContext.Default.SessionListDto);
        Assert.NotNull(sessions);

        var s1 = sessions.Sessions.FirstOrDefault(s => s.Id == session1.Id);
        var s2 = sessions.Sessions.FirstOrDefault(s => s.Id == session2.Id);

        Assert.NotNull(s1);
        Assert.NotNull(s2);
        Assert.Equal(100, s1.Cols);
        Assert.Equal(30, s1.Rows);
        Assert.Equal(80, s2.Cols);
        Assert.Equal(24, s2.Rows);
    }

    [Fact]
    public async Task WebSocket_Mux_Resize_Works()
    {
        var session = await CreateSessionAndTrackAsync(80, 24);
        await WaitForSessionRunningAsync(session.Id, TimeSpan.FromSeconds(5));

        var ws = await ConnectWebSocket("/ws/mux");
        await DrainInitialFrames(ws);

        var resizePayload = MuxProtocol.CreateResizePayload(160, 50);
        var frame = new byte[MuxProtocol.HeaderSize + resizePayload.Length];
        frame[0] = MuxProtocol.TypeResize;
        Encoding.ASCII.GetBytes(session.Id.AsSpan(0, 8), frame.AsSpan(1, 8));
        resizePayload.CopyTo(frame.AsSpan(MuxProtocol.HeaderSize));

        await ws.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);

        await Task.Delay(300);
        var sessions = await _client.GetFromJsonAsync<SessionListDto>("/api/sessions", AppJsonContext.Default.SessionListDto);
        Assert.NotNull(sessions);

        var updated = sessions.Sessions.FirstOrDefault(s => s.Id == session.Id);
        Assert.NotNull(updated);
        Assert.Equal(160, updated.Cols);
        Assert.Equal(50, updated.Rows);
    }

    private async Task<WebSocket> ConnectWebSocket(string path)
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, path);
        var wsUri = new UriBuilder(uri) { Scheme = uri.Scheme == "https" ? "wss" : "ws" }.Uri;

        var socket = await wsClient.ConnectAsync(wsUri, CancellationToken.None);
        _webSockets.Add(socket);
        return socket;
    }

    [Fact]
    public async Task WebSocket_State_ReceivesInitialSessionList()
    {
        var ws = await ConnectWebSocket("/ws/state");

        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await ws.ReceiveAsync(buffer, cts.Token);

        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

        var state = System.Text.Json.JsonSerializer.Deserialize<StateUpdate>(json, AppJsonContext.Default.StateUpdate);
        Assert.NotNull(state);
        Assert.NotNull(state.Sessions);
        Assert.NotNull(state.Sessions.Sessions);
    }

    [Fact]
    public async Task WebSocket_State_UpdatesWhenSessionCreated()
    {
        var ws = await ConnectWebSocket("/ws/state");

        var buffer = new byte[8192];
        using var initCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.ReceiveAsync(buffer, initCts.Token);

        var session = await CreateSessionAndTrackAsync(80, 24);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        StateUpdate? foundState = null;

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                using var recvCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var result = await ws.ReceiveAsync(buffer, recvCts.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var state = System.Text.Json.JsonSerializer.Deserialize<StateUpdate>(json, AppJsonContext.Default.StateUpdate);

                    if (state?.Sessions?.Sessions?.Any(s => s.Id == session.Id) == true)
                    {
                        foundState = state;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        Assert.NotNull(foundState);
        Assert.Contains(foundState.Sessions!.Sessions, s => s.Id == session.Id);
    }

    [Fact]
    public async Task WebSocket_State_SessionListHasCorrectStructure()
    {
        var session = await CreateSessionAndTrackAsync(100, 40);
        await WaitForSessionRunningAsync(session.Id, TimeSpan.FromSeconds(5));

        var ws = await ConnectWebSocket("/ws/state");
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await ws.ReceiveAsync(buffer, cts.Token);

        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        var state = System.Text.Json.JsonSerializer.Deserialize<StateUpdate>(json, AppJsonContext.Default.StateUpdate);

        Assert.NotNull(state);
        Assert.NotNull(state.Sessions);
        Assert.NotEmpty(state.Sessions.Sessions);

        var sessionInfo = state.Sessions.Sessions.FirstOrDefault(s => s.Id == session.Id);
        Assert.NotNull(sessionInfo);
        Assert.Equal(100, sessionInfo.Cols);
        Assert.Equal(40, sessionInfo.Rows);
        Assert.True(sessionInfo.IsRunning);
    }

    private static async Task DrainInitialFrames(WebSocket ws)
    {
        var buffer = new byte[MuxProtocol.MaxFrameSize];
        var deadline = DateTime.UtcNow.AddMilliseconds(2000);

        while (DateTime.UtcNow < deadline)
        {
            using var cts = new CancellationTokenSource(100);
            try
            {
                await ws.ReceiveAsync(buffer, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
