using System.Collections.Concurrent;
using Ai.Tlbx.MiddleManager.Models;
using Ai.Tlbx.MiddleManager.Settings;
using Ai.Tlbx.MiddleManager.Shells;

namespace Ai.Tlbx.MiddleManager.Services;

public sealed class SessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Action> _stateListeners = new();
    private readonly ShellRegistry _shellRegistry;
    private readonly SettingsService _settingsService;
    private MuxConnectionManager? _muxManager;
    private bool _disposed;

    public IReadOnlyCollection<TerminalSession> Sessions => _sessions.Values.ToList();
    public ShellRegistry ShellRegistry => _shellRegistry;
    public SettingsService SettingsService => _settingsService;

    public SessionManager(ShellRegistry shellRegistry, SettingsService settingsService)
    {
        _shellRegistry = shellRegistry;
        _settingsService = settingsService;
    }

    public void SetMuxManager(MuxConnectionManager muxManager)
    {
        _muxManager = muxManager;
    }

    public TerminalSession CreateSession(int cols = 120, int rows = 30, ShellType? shellType = null)
    {
        var settings = _settingsService.Load();
        var effectiveShellType = shellType ?? settings.DefaultShell;
        var shellConfig = _shellRegistry.GetConfigurationOrDefault(effectiveShellType);

        var workingDirectory = GetDefaultWorkingDirectory(settings);

        // Pass runAs settings for privilege de-elevation when running as service
        var session = TerminalSession.Create(
            workingDirectory,
            cols,
            rows,
            shellConfig,
            settings.RunAsUser,
            settings.RunAsUserSid,
            settings.RunAsUid,
            settings.RunAsGid);

        session.OnStateChanged += () => NotifyStateChange();
        session.OnOutput += (sessionId, data) =>
        {
            _muxManager?.BroadcastTerminalOutput(sessionId, data);
            return Task.CompletedTask;
        };

        _sessions[session.Id] = session;
        NotifyStateChange();

        return session;
    }

    public TerminalSession? GetSession(string id)
    {
        _sessions.TryGetValue(id, out var session);
        return session;
    }

    public void CloseSession(string id)
    {
        if (_sessions.TryRemove(id, out var session))
        {
            session.Dispose();
            NotifyStateChange();
        }
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
            try
            {
                listener();
            }
            catch
            {
            }
        }
    }

    public SessionListDto GetSessionList()
    {
        return new SessionListDto
        {
            Sessions = _sessions.Values.Select(s => new SessionInfoDto
            {
                Id = s.Id,
                Pid = s.Pid,
                CreatedAt = s.CreatedAt,
                IsRunning = s.IsRunning,
                ExitCode = s.ExitCode,
                CurrentWorkingDirectory = s.CurrentWorkingDirectory,
                Cols = s.Cols,
                Rows = s.Rows,
                ShellType = s.ShellType.ToString(),
                Name = s.Name
            }).OrderBy(s => s.CreatedAt).ToList()
        };
    }

    public bool RenameSession(string id, string? name)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            return false;
        }
        session.SetName(name);
        return true;
    }

    private static string GetDefaultWorkingDirectory(Settings.MiddleManagerSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.DefaultWorkingDirectory) &&
            Directory.Exists(settings.DefaultWorkingDirectory))
        {
            return settings.DefaultWorkingDirectory;
        }

        try
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        catch
        {
            return Environment.CurrentDirectory;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        foreach (var session in _sessions.Values)
        {
            try
            {
                session.Dispose();
            }
            catch
            {
            }
        }

        _sessions.Clear();
        _stateListeners.Clear();
    }
}