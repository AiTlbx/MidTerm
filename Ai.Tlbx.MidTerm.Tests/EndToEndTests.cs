using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Ai.Tlbx.MidTerm.Models;
using Ai.Tlbx.MidTerm.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ai.Tlbx.MidTerm.Tests;

public class EndToEndTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly List<WebSocket> _webSockets = [];
    private readonly List<string> _createdSessionIds = [];

    public EndToEndTests(WebApplicationFactory<Program> factory)
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
    public async Task EndToEnd_CreateSession_ReceivesInitialOutput()
    {
        var session = await CreateSessionAndTrackAsync(80, 24);

        Assert.NotEmpty(session.Id);
        Assert.True(session.IsRunning, "Session should be running after creation");
        Assert.True(session.Pid > 0, "Session should have a valid PID");

        var ws = await ConnectWebSocket("/ws/mux");

        var output = await ReceiveTerminalOutputAsync(ws, session.Id, TimeSpan.FromSeconds(10));

        Assert.True(output.Length > 0 || (await PollForBufferContentAsync(session.Id, TimeSpan.FromSeconds(5))).Length > 0,
            "Should receive some initial output");
    }

    [Fact]
    public async Task EndToEnd_SendCommand_ReceivesResponse()
    {
        var session = await CreateSessionAndTrackAsync(80, 24);
        Assert.True(session.Pid > 0, "Session should have a valid PID");

        await WaitForSessionRunningAsync(session.Id, TimeSpan.FromSeconds(5));

        var ws = await ConnectWebSocket("/ws/mux");
        var initialOutput = await ReceiveTerminalOutputAsync(ws, session.Id, TimeSpan.FromSeconds(2));

        var command = OperatingSystem.IsWindows() ? "echo TEST\r\n" : "echo TEST\n";
        await SendTerminalInputAsync(ws, session.Id, command);

        var buffer = await PollForBufferContentAsync(session.Id, TimeSpan.FromSeconds(5));

        Assert.True(initialOutput.Length > 0 || buffer.Length > 0,
            $"Should receive some output. WSOutput={initialOutput.Length}chars, Buffer={buffer.Length}chars");
    }

    [Fact]
    public async Task EndToEnd_CloseSession_SessionStops()
    {
        var session = await CreateSessionAndTrackAsync(80, 24);
        await WaitForSessionRunningAsync(session.Id, TimeSpan.FromSeconds(5));

        var listResponse = await _client.GetAsync("/api/sessions");
        var sessions = await listResponse.Content.ReadFromJsonAsync<SessionListDto>(AppJsonContext.Default.SessionListDto);
        Assert.Contains(sessions!.Sessions, s => s.Id == session.Id);

        var deleteResponse = await _client.DeleteAsync($"/api/sessions/{session.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        await Task.Delay(200);

        listResponse = await _client.GetAsync("/api/sessions");
        sessions = await listResponse.Content.ReadFromJsonAsync<SessionListDto>(AppJsonContext.Default.SessionListDto);
        Assert.DoesNotContain(sessions!.Sessions, s => s.Id == session.Id);

        _createdSessionIds.Remove(session.Id);
    }

    [Fact]
    public async Task EndToEnd_StateWebSocket_ReceivesUpdatesOnSessionCreate()
    {
        var stateWs = await ConnectWebSocket("/ws/state");

        var initialState = await ReceiveStateUpdateAsync(stateWs, TimeSpan.FromSeconds(5));
        Assert.NotNull(initialState);

        var session = await CreateSessionAndTrackAsync(80, 24);

        StateUpdate? updatedState = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var state = await ReceiveStateUpdateAsync(stateWs, TimeSpan.FromSeconds(2));
            if (state?.Sessions?.Sessions?.Any(s => s.Id == session.Id) == true)
            {
                updatedState = state;
                break;
            }
        }

        Assert.NotNull(updatedState?.Sessions?.Sessions);
        Assert.Contains(updatedState.Sessions.Sessions, s => s.Id == session.Id);
    }

    [Fact]
    public async Task EndToEnd_StateWebSocket_ReceivesUpdatesOnSessionDelete()
    {
        var session = await CreateSessionAndTrackAsync(80, 24);
        await WaitForSessionRunningAsync(session.Id, TimeSpan.FromSeconds(5));

        var stateWs = await ConnectWebSocket("/ws/state");
        var initialState = await ReceiveStateUpdateAsync(stateWs, TimeSpan.FromSeconds(5));
        Assert.Contains(initialState!.Sessions!.Sessions, s => s.Id == session.Id);

        await _client.DeleteAsync($"/api/sessions/{session.Id}");
        _createdSessionIds.Remove(session.Id);

        StateUpdate? updatedState = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var state = await ReceiveStateUpdateAsync(stateWs, TimeSpan.FromSeconds(2));
            if (state?.Sessions?.Sessions?.All(s => s.Id != session.Id) == true)
            {
                updatedState = state;
                break;
            }
        }

        Assert.NotNull(updatedState);
        Assert.DoesNotContain(updatedState.Sessions!.Sessions, s => s.Id == session.Id);
    }

    [Fact]
    public async Task EndToEnd_Resize_UpdatesDimensions()
    {
        var session = await CreateSessionAndTrackAsync(80, 24);
        Assert.Equal(80, session.Cols);
        Assert.Equal(24, session.Rows);

        await WaitForSessionRunningAsync(session.Id, TimeSpan.FromSeconds(5));

        var ws = await ConnectWebSocket("/ws/mux");
        await DrainFramesAsync(ws, TimeSpan.FromMilliseconds(500));

        await SendResizeAsync(ws, session.Id, 120, 40);
        await Task.Delay(300);

        var listResponse = await _client.GetAsync("/api/sessions");
        var sessions = await listResponse.Content.ReadFromJsonAsync<SessionListDto>(AppJsonContext.Default.SessionListDto);
        var updated = sessions!.Sessions.FirstOrDefault(s => s.Id == session.Id);

        Assert.NotNull(updated);
        Assert.Equal(120, updated.Cols);
        Assert.Equal(40, updated.Rows);
    }

    #region Helper Methods

    private async Task<WebSocket> ConnectWebSocket(string path)
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new Uri(_factory.Server.BaseAddress, path);
        var wsUri = new UriBuilder(uri) { Scheme = uri.Scheme == "https" ? "wss" : "ws" }.Uri;

        var socket = await wsClient.ConnectAsync(wsUri, CancellationToken.None);
        _webSockets.Add(socket);
        return socket;
    }

    private static async Task SendTerminalInputAsync(WebSocket ws, string sessionId, string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var frame = new byte[MuxProtocol.HeaderSize + inputBytes.Length];
        frame[0] = MuxProtocol.TypeTerminalInput;
        Encoding.ASCII.GetBytes(sessionId.AsSpan(0, Math.Min(8, sessionId.Length)), frame.AsSpan(1, 8));
        inputBytes.CopyTo(frame.AsSpan(MuxProtocol.HeaderSize));

        await ws.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    private static async Task SendResizeAsync(WebSocket ws, string sessionId, int cols, int rows)
    {
        var payload = MuxProtocol.CreateResizePayload(cols, rows);
        var frame = new byte[MuxProtocol.HeaderSize + payload.Length];
        frame[0] = MuxProtocol.TypeResize;
        Encoding.ASCII.GetBytes(sessionId.AsSpan(0, Math.Min(8, sessionId.Length)), frame.AsSpan(1, 8));
        payload.CopyTo(frame.AsSpan(MuxProtocol.HeaderSize));

        await ws.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    private static async Task<string> ReceiveTerminalOutputAsync(WebSocket ws, string sessionId, TimeSpan timeout)
    {
        var output = new StringBuilder();
        var buffer = new byte[MuxProtocol.MaxFrameSize];
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            try
            {
                var result = await ws.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Binary && result.Count >= MuxProtocol.HeaderSize)
                {
                    var frameType = buffer[0];
                    var frameSessionId = Encoding.ASCII.GetString(buffer, 1, 8).TrimEnd('\0');

                    if (frameType == MuxProtocol.TypeTerminalOutput && frameSessionId == sessionId[..Math.Min(8, sessionId.Length)])
                    {
                        var payloadLength = result.Count - MuxProtocol.HeaderSize;
                        if (payloadLength > 0)
                        {
                            output.Append(Encoding.UTF8.GetString(buffer, MuxProtocol.HeaderSize, payloadLength));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (output.Length > 0)
                {
                    break;
                }
            }
        }

        return output.ToString();
    }

    private static async Task<StateUpdate?> ReceiveStateUpdateAsync(WebSocket ws, TimeSpan timeout)
    {
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            var result = await ws.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                return System.Text.Json.JsonSerializer.Deserialize<StateUpdate>(json, AppJsonContext.Default.StateUpdate);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return null;
    }

    private static async Task DrainFramesAsync(WebSocket ws, TimeSpan duration)
    {
        var buffer = new byte[MuxProtocol.MaxFrameSize];
        var deadline = DateTime.UtcNow + duration;

        while (DateTime.UtcNow < deadline)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
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

    #endregion
}
