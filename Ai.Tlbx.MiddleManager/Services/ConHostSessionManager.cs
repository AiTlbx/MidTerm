using System.Collections.Concurrent;
using System.IO.Pipes;
using Ai.Tlbx.MiddleManager.Models;

namespace Ai.Tlbx.MiddleManager.Services;

/// <summary>
/// Manages mm-con-host processes. Spawns new sessions, discovers existing ones on startup.
/// </summary>
public sealed class ConHostSessionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ConHostClient> _clients = new();
    private readonly ConcurrentDictionary<string, SessionInfo> _sessionCache = new();
    private readonly ConcurrentDictionary<string, Action> _stateListeners = new();
    private bool _disposed;

    public event Action<string, ReadOnlyMemory<byte>>? OnOutput;
    public event Action<string>? OnStateChanged;

    /// <summary>
    /// Discover and connect to existing mm-con-host sessions.
    /// Called on startup to reconnect to sessions from previous mm.exe instance.
    /// </summary>
    public async Task DiscoverExistingSessionsAsync(CancellationToken ct = default)
    {
        Console.WriteLine("[ConHostSessionManager] Discovering existing sessions...");

        var pipeDir = @"\\.\pipe\";
        var existingPipes = new List<string>();

        try
        {
            foreach (var pipePath in Directory.GetFiles(pipeDir))
            {
                var pipeName = Path.GetFileName(pipePath);
                if (pipeName.StartsWith("mm-con-"))
                {
                    existingPipes.Add(pipeName);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConHostSessionManager] Pipe enumeration failed: {ex.Message}");
            return;
        }

        Console.WriteLine($"[ConHostSessionManager] Found {existingPipes.Count} existing session pipes");

        foreach (var pipeName in existingPipes)
        {
            if (ct.IsCancellationRequested) break;

            var sessionId = pipeName.Replace("mm-con-", "");
            if (_clients.ContainsKey(sessionId)) continue;

            try
            {
                var client = new ConHostClient(sessionId);
                if (await client.ConnectAsync(2000, ct).ConfigureAwait(false))
                {
                    var info = await client.GetInfoAsync(ct).ConfigureAwait(false);
                    if (info is not null)
                    {
                        SubscribeToClient(client);
                        _clients[sessionId] = client;
                        _sessionCache[sessionId] = info;
                        Console.WriteLine($"[ConHostSessionManager] Reconnected to session {sessionId}");
                    }
                    else
                    {
                        await client.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConHostSessionManager] Failed to reconnect to {sessionId}: {ex.Message}");
            }
        }

        Console.WriteLine($"[ConHostSessionManager] Discovered {_clients.Count} active sessions");
    }

    public async Task<SessionInfo?> CreateSessionAsync(
        string? shellType,
        int cols,
        int rows,
        string? workingDirectory,
        CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..8];

#if WINDOWS
#pragma warning disable CA1416 // Platform compatibility - already guarded by #if WINDOWS
        if (!ConHostSpawner.SpawnConHost(sessionId, shellType, workingDirectory, cols, rows, out _))
        {
            return null;
        }
#pragma warning restore CA1416
#else
        // TODO: Unix spawner
        Console.WriteLine("[ConHostSessionManager] Unix spawner not implemented");
        return null;
#endif

        // Wait for pipe to become available
        await Task.Delay(500, ct).ConfigureAwait(false);

        // Connect to the new session
        var client = new ConHostClient(sessionId);
        var connected = false;

        for (var attempt = 0; attempt < 10 && !connected; attempt++)
        {
            connected = await client.ConnectAsync(1000, ct).ConfigureAwait(false);
            if (!connected)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
        }

        if (!connected)
        {
            Console.WriteLine($"[ConHostSessionManager] Failed to connect to new session {sessionId}");
            await client.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        var info = await client.GetInfoAsync(ct).ConfigureAwait(false);
        if (info is null)
        {
            Console.WriteLine($"[ConHostSessionManager] Failed to get info for session {sessionId}");
            await client.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        SubscribeToClient(client);
        _clients[sessionId] = client;
        _sessionCache[sessionId] = info;

        Console.WriteLine($"[ConHostSessionManager] Created session {sessionId}");
        OnStateChanged?.Invoke(sessionId);
        NotifyStateChange();

        return info;
    }

    public SessionInfo? GetSession(string sessionId)
    {
        return _sessionCache.TryGetValue(sessionId, out var info) ? info : null;
    }

    public IReadOnlyList<SessionInfo> GetAllSessions()
    {
        return _sessionCache.Values.ToList();
    }

    public SessionListDto GetSessionList()
    {
        return new SessionListDto
        {
            Sessions = _sessionCache.Values.Select(s => new SessionInfoDto
            {
                Id = s.Id,
                Pid = s.Pid,
                CreatedAt = s.CreatedAt,
                IsRunning = s.IsRunning,
                ExitCode = s.ExitCode,
                CurrentWorkingDirectory = s.CurrentWorkingDirectory,
                Cols = s.Cols,
                Rows = s.Rows,
                ShellType = s.ShellType,
                Name = s.Name
            }).ToList()
        };
    }

    public async Task<bool> CloseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_clients.TryRemove(sessionId, out var client))
        {
            return false;
        }

        _sessionCache.TryRemove(sessionId, out _);

        await client.CloseAsync(ct).ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);

        OnStateChanged?.Invoke(sessionId);
        NotifyStateChange();
        return true;
    }

    public async Task<bool> ResizeSessionAsync(string sessionId, int cols, int rows, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(sessionId, out var client))
        {
            return false;
        }

        var success = await client.ResizeAsync(cols, rows, ct).ConfigureAwait(false);

        if (success && _sessionCache.TryGetValue(sessionId, out var info))
        {
            info.Cols = cols;
            info.Rows = rows;
            OnStateChanged?.Invoke(sessionId);
            NotifyStateChange();
        }

        return success;
    }

    public async Task SendInputAsync(string sessionId, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_clients.TryGetValue(sessionId, out var client))
        {
            await client.SendInputAsync(data, ct).ConfigureAwait(false);
        }
    }

    public async Task<byte[]?> GetBufferAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(sessionId, out var client))
        {
            return null;
        }

        return await client.GetBufferAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> SetSessionNameAsync(string sessionId, string? name, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(sessionId, out var client))
        {
            return false;
        }

        var success = await client.SetNameAsync(name, ct).ConfigureAwait(false);

        if (success && _sessionCache.TryGetValue(sessionId, out var info))
        {
            info.Name = name;
            OnStateChanged?.Invoke(sessionId);
            NotifyStateChange();
        }

        return success;
    }

    public string AddStateListener(Action callback)
    {
        var id = Guid.NewGuid().ToString("N");
        _stateListeners[id] = callback;
        return id;
    }

    public void RemoveStateListener(string id)
    {
        _stateListeners.TryRemove(id, out _);
    }

    private void NotifyStateChange()
    {
        foreach (var listener in _stateListeners.Values)
        {
            try { listener(); } catch { }
        }
    }

    private void SubscribeToClient(ConHostClient client)
    {
        client.OnOutput += (sessionId, data) => OnOutput?.Invoke(sessionId, data);
        client.OnStateChanged += async sessionId =>
        {
            // Update cached info
            if (_clients.TryGetValue(sessionId, out var c))
            {
                var info = await c.GetInfoAsync().ConfigureAwait(false);
                if (info is not null)
                {
                    _sessionCache[sessionId] = info;
                }

                if (info is null || !info.IsRunning)
                {
                    // Session ended - clean up
                    if (_clients.TryRemove(sessionId, out var removed))
                    {
                        await removed.DisposeAsync().ConfigureAwait(false);
                    }
                    _sessionCache.TryRemove(sessionId, out _);
                }
            }

            OnStateChanged?.Invoke(sessionId);
            NotifyStateChange();
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var client in _clients.Values)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); } catch { }
        }

        _clients.Clear();
        _sessionCache.Clear();
        _stateListeners.Clear();
    }
}
