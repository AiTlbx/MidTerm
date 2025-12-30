using System.Diagnostics;

namespace Ai.Tlbx.MiddleManager.Host.Services;

public sealed class WebServerSupervisor : IAsyncDisposable
{
    private const int MaxBackoffDelayMs = 30_000;
    private const int StableRunDurationMs = 60_000;

    private readonly int _port;
    private readonly string _bindAddress;
    private Process? _webProcess;
    private int _restartCount;
    private DateTime _lastStart;
    private CancellationTokenSource? _stableCheckCts;
    private bool _disposed;

    public WebServerSupervisor(int port = 2000, string bindAddress = "0.0.0.0")
    {
        _port = port;
        _bindAddress = bindAddress;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SpawnWebServer();

                if (_webProcess is null)
                {
                    Console.WriteLine("Failed to spawn mm.exe, retrying in 5 seconds...");
                    await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _webProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var exitCode = _webProcess.ExitCode;
                var runtime = DateTime.UtcNow - _lastStart;
                Console.WriteLine($"mm.exe exited with code {exitCode} after {runtime.TotalSeconds:F1}s");

                _restartCount++;
                var delay = CalculateBackoffDelay();
                Console.WriteLine($"Restarting mm.exe in {delay}ms (attempt {_restartCount})...");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Supervisor error: {ex.Message}");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }

        await StopWebServerAsync().ConfigureAwait(false);
    }

    private void SpawnWebServer()
    {
        var webServerPath = GetWebServerPath();
        if (string.IsNullOrEmpty(webServerPath) || !File.Exists(webServerPath))
        {
            Console.WriteLine($"mm.exe not found at: {webServerPath}");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = webServerPath,
            Arguments = $"--spawned --port {_port} --bind {_bindAddress}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        CopyEnvironmentVariables(psi);

        try
        {
            _webProcess = Process.Start(psi);
            if (_webProcess is not null)
            {
                _lastStart = DateTime.UtcNow;
                Console.WriteLine($"Spawned mm.exe (PID: {_webProcess.Id}) on port {_port}");
                StartStableCheck();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start mm.exe: {ex.Message}");
            _webProcess = null;
        }
    }

    private void CopyEnvironmentVariables(ProcessStartInfo psi)
    {
        var runAsUser = Environment.GetEnvironmentVariable("MM_RUN_AS_USER");
        var runAsUserSid = Environment.GetEnvironmentVariable("MM_RUN_AS_USER_SID");
        var runAsUid = Environment.GetEnvironmentVariable("MM_RUN_AS_UID");
        var runAsGid = Environment.GetEnvironmentVariable("MM_RUN_AS_GID");

        if (!string.IsNullOrEmpty(runAsUser))
        {
            psi.Environment["MM_RUN_AS_USER"] = runAsUser;
        }
        if (!string.IsNullOrEmpty(runAsUserSid))
        {
            psi.Environment["MM_RUN_AS_USER_SID"] = runAsUserSid;
        }
        if (!string.IsNullOrEmpty(runAsUid))
        {
            psi.Environment["MM_RUN_AS_UID"] = runAsUid;
        }
        if (!string.IsNullOrEmpty(runAsGid))
        {
            psi.Environment["MM_RUN_AS_GID"] = runAsGid;
        }
    }

    private void StartStableCheck()
    {
        _stableCheckCts?.Cancel();
        _stableCheckCts = new CancellationTokenSource();
        var token = _stableCheckCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StableRunDurationMs, token).ConfigureAwait(false);
                if (_webProcess?.HasExited == false)
                {
                    if (_restartCount > 0)
                    {
                        Console.WriteLine($"mm.exe stable for {StableRunDurationMs / 1000}s, resetting restart counter");
                    }
                    _restartCount = 0;
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private int CalculateBackoffDelay()
    {
        var exponent = Math.Min(_restartCount, 5);
        return Math.Min(MaxBackoffDelayMs, 1000 * (1 << exponent));
    }

    private static string GetWebServerPath()
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

        var webName = OperatingSystem.IsWindows() ? "mm.exe" : "mm";
        return Path.Combine(dir, webName);
    }

    private async Task StopWebServerAsync()
    {
        _stableCheckCts?.Cancel();

        if (_webProcess is null || _webProcess.HasExited)
        {
            return;
        }

        Console.WriteLine("Stopping mm.exe...");
        try
        {
            _webProcess.Kill(entireProcessTree: true);
            await _webProcess.WaitForExitAsync(new CancellationTokenSource(5000).Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping mm.exe: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        await StopWebServerAsync().ConfigureAwait(false);
        _stableCheckCts?.Dispose();
        _webProcess?.Dispose();
    }
}
