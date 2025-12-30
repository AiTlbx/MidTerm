using System.Diagnostics;
using Ai.Tlbx.MiddleManager.Ipc;
using Ai.Tlbx.MiddleManager.Settings;

namespace Ai.Tlbx.MiddleManager.Services;

public sealed class SidecarLifecycle : IAsyncDisposable
{
    private readonly SettingsService _settingsService;
    private readonly SidecarClient _client;
    private readonly bool _wasSpawned;
    private Process? _sidecarProcess;
    private bool _disposed;

    public SidecarClient Client => _client;
    public bool IsConnected => _client.IsConnected;

    public SidecarLifecycle(SettingsService settingsService, bool wasSpawned = false)
    {
        _settingsService = settingsService;
        _wasSpawned = wasSpawned;
        _client = new SidecarClient();
    }

    public async Task<bool> StartAndConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_wasSpawned)
        {
            return await ConnectToParentHostAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_settingsService.IsRunningAsService)
        {
            return await WaitForServiceHostAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ConnectOrSpawnHostAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ConnectToParentHostAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Connecting to parent mm-host...");
        for (var i = 0; i < 30; i++)
        {
            if (await _client.ConnectWithAutoReconnectAsync(cancellationToken).ConfigureAwait(false))
            {
                Console.WriteLine("Connected to mm-host (with auto-reconnect)");
                return true;
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine("Warning: Could not connect to parent mm-host");
        return false;
    }

    private async Task<bool> WaitForServiceHostAsync(CancellationToken cancellationToken)
    {
        if (await _client.ConnectWithAutoReconnectAsync(cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine("Connected to mm-host (with auto-reconnect)");
            return true;
        }

        Console.WriteLine("Waiting for mm-host to start...");
        for (var i = 0; i < 50; i++)
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            if (await _client.ConnectWithAutoReconnectAsync(cancellationToken).ConfigureAwait(false))
            {
                Console.WriteLine("Connected to mm-host (with auto-reconnect)");
                return true;
            }
        }

        Console.WriteLine("Warning: Could not connect to mm-host after waiting 10 seconds");
        return false;
    }

    private async Task<bool> ConnectOrSpawnHostAsync(CancellationToken cancellationToken)
    {
        if (await _client.ConnectAsync(cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine("Connected to existing mm-host");
            return true;
        }

        if (!await SpawnSidecarAsync(cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine("Failed to spawn mm-host");
            return false;
        }

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            if (await _client.ConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                Console.WriteLine("Connected to mm-host");
                return true;
            }
        }

        Console.WriteLine("Failed to connect to mm-host after spawn");
        return false;
    }

    private async Task<bool> SpawnSidecarAsync(CancellationToken cancellationToken)
    {
        var hostPath = GetSidecarPath();
        if (string.IsNullOrEmpty(hostPath) || !File.Exists(hostPath))
        {
            Console.WriteLine($"mm-host not found at expected path: {hostPath}");
            return false;
        }

        var settings = _settingsService.Load();

        var psi = new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        // Pass runAs settings via environment
        if (!string.IsNullOrEmpty(settings.RunAsUser))
        {
            psi.Environment["MM_RUN_AS_USER"] = settings.RunAsUser;
        }
        if (!string.IsNullOrEmpty(settings.RunAsUserSid))
        {
            psi.Environment["MM_RUN_AS_USER_SID"] = settings.RunAsUserSid;
        }
        if (settings.RunAsUid.HasValue)
        {
            psi.Environment["MM_RUN_AS_UID"] = settings.RunAsUid.Value.ToString();
        }
        if (settings.RunAsGid.HasValue)
        {
            psi.Environment["MM_RUN_AS_GID"] = settings.RunAsGid.Value.ToString();
        }

        try
        {
            _sidecarProcess = Process.Start(psi);
            return _sidecarProcess is not null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start mm-host: {ex.Message}");
            return false;
        }
    }

    private static string GetSidecarPath()
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
        {
            return string.Empty;
        }

        var dir = Path.GetDirectoryName(currentExe);
        if (string.IsNullOrEmpty(dir))
        {
            return string.Empty;
        }

        var hostName = OperatingSystem.IsWindows() ? "mm-host.exe" : "mm-host";
        return Path.Combine(dir, hostName);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        await _client.DisposeAsync().ConfigureAwait(false);

        // Don't kill the sidecar - it should keep running for session persistence
        // The sidecar will clean up when the system shuts down or when explicitly stopped
    }
}
