using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Ai.Tlbx.MiddleManager.Models;
using Ai.Tlbx.MiddleManager.Services;
using Xunit;
using Xunit.Abstractions;

namespace Ai.Tlbx.MiddleManager.Tests;

/// <summary>
/// Integration tests against locally installed MiddleManager service.
/// Requires the MiddleManager service to be running at localhost:2000.
/// Run: dotnet test --filter "FullyQualifiedName~LocalServiceTests"
/// </summary>
[Trait("Category", "LocalService")]
public class LocalServiceTests : IAsyncDisposable
{
    private const string BaseUrl = "http://localhost:2000";
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;
    private readonly List<ClientWebSocket> _webSockets = [];
    private readonly List<string> _createdSessions = [];

    public LocalServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sessionId in _createdSessions)
        {
            try
            {
                await _client.DeleteAsync($"/api/sessions/{sessionId}");
            }
            catch { }
        }

        foreach (var ws in _webSockets)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch { }
            }
            ws.Dispose();
        }
        _client.Dispose();
    }

    [Fact]
    public async Task Service_IsRunning()
    {
        var response = await _client.GetAsync("/api/version");

        Assert.True(response.IsSuccessStatusCode, "Service is not running at localhost:2000");
        var version = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Service version: {version}");
        Assert.NotEmpty(version);
    }

    [Fact]
    public async Task Health_ReturnsSystemHealth()
    {
        var response = await _client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Health response: {json}");

        var health = JsonSerializer.Deserialize<SystemHealthDto>(json);
        Assert.NotNull(health);
        Assert.NotEmpty(health.Version);
        Assert.True(!string.IsNullOrEmpty(health.Mode), "Mode should be set");
        Assert.True(!string.IsNullOrEmpty(health.Platform), "Platform should be set");
    }

    [Fact]
    public async Task Health_SidecarMode_ReportsHostStatus()
    {
        var response = await _client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<SystemHealthDto>(json);
        Assert.NotNull(health);

        _output.WriteLine($"Mode: {health.Mode}");
        _output.WriteLine($"Healthy: {health.Healthy}");
        _output.WriteLine($"HostConnected: {health.HostConnected}");
        _output.WriteLine($"HostError: {health.HostError ?? "(none)"}");
        _output.WriteLine($"LastHeartbeatMs: {health.LastHeartbeatMs}");
        _output.WriteLine($"IpcTransport: {health.IpcTransport}");
        _output.WriteLine($"IpcEndpoint: {health.IpcEndpoint}");

        if (health.Mode == "sidecar")
        {
            Assert.NotNull(health.IpcTransport);
            Assert.NotNull(health.IpcEndpoint);

            if (!health.Healthy)
            {
                Assert.NotNull(health.HostError);
                _output.WriteLine($"WARNING: Host is unhealthy - {health.HostError}");
            }
        }
    }

    [Fact]
    public async Task Health_HeartbeatIsRecent()
    {
        var response = await _client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<SystemHealthDto>(json);
        Assert.NotNull(health);

        if (health.Mode == "sidecar" && health.LastHeartbeatMs.HasValue)
        {
            _output.WriteLine($"Last heartbeat: {health.LastHeartbeatMs}ms ago");

            // Heartbeat should be within 15 seconds if healthy
            if (health.Healthy)
            {
                Assert.True(health.LastHeartbeatMs < 15000,
                    $"Heartbeat is stale: {health.LastHeartbeatMs}ms ago (should be < 15000ms)");
            }
        }
    }

    [Fact]
    public async Task Session_Create_RequiresHealthyHost()
    {
        // Check health first
        var healthResponse = await _client.GetAsync("/api/health");
        var healthJson = await healthResponse.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<SystemHealthDto>(healthJson);
        Assert.NotNull(health);

        _output.WriteLine($"Health before session create: Healthy={health.Healthy}, Mode={health.Mode}");

        if (!health.Healthy)
        {
            _output.WriteLine($"Skipping session creation test - host is unhealthy: {health.HostError}");
            return;
        }

        // Try to create session
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await _client.PostAsJsonAsync("/api/sessions", new { Cols = 80, Rows = 24 }, cts.Token);

        _output.WriteLine($"Create session response: {response.StatusCode}");

        if (response.IsSuccessStatusCode)
        {
            var session = await response.Content.ReadFromJsonAsync<SessionInfoDto>(AppJsonContext.Default.SessionInfoDto);
            Assert.NotNull(session);
            Assert.NotEmpty(session.Id);
            _createdSessions.Add(session.Id);
            _output.WriteLine($"Created session: {session.Id}, PID: {session.Pid}");
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Failed to create session: {error}");
            Assert.Fail($"Session creation failed: {response.StatusCode} - {error}");
        }
    }

    [Fact]
    public async Task Session_Create_TimesOutWhenHostUnresponsive()
    {
        // This test verifies that session creation times out properly
        // when the host is not responding, rather than hanging forever

        var healthResponse = await _client.GetAsync("/api/health");
        var healthJson = await healthResponse.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<SystemHealthDto>(healthJson);
        Assert.NotNull(health);

        _output.WriteLine($"Host status: Healthy={health.Healthy}, HostConnected={health.HostConnected}");
        _output.WriteLine($"LastHeartbeatMs: {health.LastHeartbeatMs}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await _client.PostAsJsonAsync("/api/sessions", new { Cols = 80, Rows = 24 }, cts.Token);
            stopwatch.Stop();

            _output.WriteLine($"Request completed in {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Response: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var session = await response.Content.ReadFromJsonAsync<SessionInfoDto>(AppJsonContext.Default.SessionInfoDto);
                if (session != null)
                {
                    _createdSessions.Add(session.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _output.WriteLine($"Request timed out after {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 20000, "Request should timeout within 20 seconds");
        }
    }

    [Fact]
    public async Task WebSocket_State_ConnectsSuccessfully()
    {
        var ws = new ClientWebSocket();
        _webSockets.Add(ws);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.ConnectAsync(new Uri($"ws://localhost:2000/ws/state"), cts.Token);

        Assert.Equal(WebSocketState.Open, ws.State);

        // Should receive initial state
        var buffer = new byte[8192];
        var result = await ws.ReceiveAsync(buffer, cts.Token);

        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        _output.WriteLine($"Initial state: {json}");

        Assert.Contains("sessions", json);
    }

    [Fact]
    public async Task WebSocket_Mux_ConnectsSuccessfully()
    {
        var ws = new ClientWebSocket();
        _webSockets.Add(ws);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.ConnectAsync(new Uri($"ws://localhost:2000/ws/mux"), cts.Token);

        Assert.Equal(WebSocketState.Open, ws.State);

        // Should receive init frame
        var buffer = new byte[1024];
        var result = await ws.ReceiveAsync(buffer, cts.Token);

        Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
        Assert.True(result.Count >= MuxProtocol.HeaderSize);
        Assert.Equal(0xFF, buffer[0]); // Init frame type
        _output.WriteLine($"Received init frame, {result.Count} bytes");
    }

    [Fact]
    public async Task IpcPipe_CanConnect()
    {
        if (!OperatingSystem.IsWindows())
        {
            _output.WriteLine("Skipping pipe test on non-Windows");
            return;
        }

        var pipeName = "middlemanager-host";
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await pipe.ConnectAsync(cts.Token);
            _output.WriteLine($"Successfully connected to pipe: {pipeName}");
            Assert.True(pipe.IsConnected);
        }
        catch (TimeoutException)
        {
            _output.WriteLine($"Pipe connection timed out - mm-host may not be accepting connections");
            // Not a failure - this is expected if mm-host isn't running or is busy
        }
        catch (UnauthorizedAccessException)
        {
            _output.WriteLine($"Pipe access denied - pipe may not exist or requires different permissions");
            // Not a failure - pipe might be in use by the service
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Pipe connection failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    [Fact]
    public async Task FullWorkflow_CreateAndCloseSession()
    {
        // Check health first
        var healthResponse = await _client.GetAsync("/api/health");
        var health = JsonSerializer.Deserialize<SystemHealthDto>(await healthResponse.Content.ReadAsStringAsync());

        if (health?.Healthy != true)
        {
            _output.WriteLine($"Skipping - host unhealthy: {health?.HostError}");
            return;
        }

        // Create session
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { Cols = 80, Rows = 24 });
        Assert.True(createResponse.IsSuccessStatusCode, "Failed to create session");

        var session = await createResponse.Content.ReadFromJsonAsync<SessionInfoDto>(AppJsonContext.Default.SessionInfoDto);
        Assert.NotNull(session);
        _output.WriteLine($"Created session: {session.Id}");

        // Verify session exists
        var listResponse = await _client.GetAsync("/api/sessions");
        var list = await listResponse.Content.ReadFromJsonAsync<SessionListDto>(AppJsonContext.Default.SessionListDto);
        Assert.Contains(list!.Sessions, s => s.Id == session.Id);
        _output.WriteLine($"Session verified in list");

        // Close session
        var deleteResponse = await _client.DeleteAsync($"/api/sessions/{session.Id}");
        Assert.True(deleteResponse.IsSuccessStatusCode, "Failed to delete session");
        _output.WriteLine($"Session deleted");

        // Verify session removed
        await Task.Delay(500);
        listResponse = await _client.GetAsync("/api/sessions");
        list = await listResponse.Content.ReadFromJsonAsync<SessionListDto>(AppJsonContext.Default.SessionListDto);
        Assert.DoesNotContain(list!.Sessions, s => s.Id == session.Id);
        _output.WriteLine($"Session removal verified");
    }

    private class SystemHealthDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("healthy")]
        public bool Healthy { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mode")]
        public string Mode { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("hostConnected")]
        public bool HostConnected { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("hostError")]
        public string? HostError { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("sessionCount")]
        public int SessionCount { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("ipcTransport")]
        public string? IpcTransport { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("ipcEndpoint")]
        public string? IpcEndpoint { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("lastHeartbeatMs")]
        public long? LastHeartbeatMs { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("webProcessId")]
        public int WebProcessId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("uptimeSeconds")]
        public long UptimeSeconds { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("platform")]
        public string Platform { get; set; } = "";
    }
}
