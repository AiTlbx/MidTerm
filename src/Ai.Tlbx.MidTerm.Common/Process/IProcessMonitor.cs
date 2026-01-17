namespace Ai.Tlbx.MidTerm.Common.Process;

/// <summary>
/// Platform abstraction for monitoring process events and retrieving process information.
/// </summary>
public interface IProcessMonitor : IDisposable
{
    /// <summary>
    /// Fired when a process event (fork, exec, exit) occurs for a descendant of the monitored root process.
    /// </summary>
    event Action<ProcessEvent>? OnProcessEvent;

    /// <summary>
    /// Fired when the foreground process changes.
    /// </summary>
    event Action<ForegroundProcessInfo>? OnForegroundChanged;

    /// <summary>
    /// Start monitoring process events for descendants of the specified root PID.
    /// </summary>
    void StartMonitoring(int rootPid);

    /// <summary>
    /// Stop monitoring process events.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Get the current working directory of a process.
    /// Returns null if the CWD cannot be determined.
    /// </summary>
    string? GetProcessCwd(int pid);

    /// <summary>
    /// Get the name/executable of a process.
    /// </summary>
    string? GetProcessName(int pid);

    /// <summary>
    /// Get the command line of a process.
    /// </summary>
    string? GetProcessCommandLine(int pid);

    /// <summary>
    /// Get immediate child process PIDs of a process.
    /// </summary>
    IReadOnlyList<int> GetChildProcesses(int pid);

    /// <summary>
    /// Get the "foreground" process - the leaf process attached to the PTY.
    /// Returns the shell PID if no foreground process can be identified.
    /// </summary>
    int GetForegroundProcess(int shellPid);

    /// <summary>
    /// Get a complete snapshot of the process tree.
    /// </summary>
    ProcessTreeSnapshot GetProcessTreeSnapshot(int shellPid);

    /// <summary>
    /// Whether real-time event monitoring is available (vs polling fallback).
    /// </summary>
    bool SupportsRealTimeEvents { get; }
}
