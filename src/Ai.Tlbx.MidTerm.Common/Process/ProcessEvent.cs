namespace Ai.Tlbx.MidTerm.Common.Process;

/// <summary>
/// Type of process event.
/// </summary>
public enum ProcessEventType
{
    Fork,
    Exec,
    Exit
}

/// <summary>
/// Represents a process lifecycle event (fork, exec, exit).
/// </summary>
public sealed class ProcessEvent
{
    public ProcessEventType Type { get; init; }
    public int Pid { get; init; }
    public int ParentPid { get; init; }
    public string? Name { get; init; }
    public string? CommandLine { get; init; }
    public int? ExitCode { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Information about the current foreground process.
/// </summary>
public sealed class ForegroundProcessInfo
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? CommandLine { get; init; }
    public string? Cwd { get; init; }
}

/// <summary>
/// Snapshot of the process tree under a shell.
/// </summary>
public sealed class ProcessTreeSnapshot
{
    public int ShellPid { get; init; }
    public string? ShellCwd { get; init; }
    public ForegroundProcessInfo? Foreground { get; init; }
    public IReadOnlyList<ProcessInfo> Processes { get; init; } = [];
}

/// <summary>
/// Basic information about a process.
/// </summary>
public sealed class ProcessInfo
{
    public int Pid { get; init; }
    public int ParentPid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? CommandLine { get; init; }
    public string? Cwd { get; init; }
}
