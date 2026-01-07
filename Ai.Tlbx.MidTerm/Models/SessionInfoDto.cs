namespace Ai.Tlbx.MidTerm.Models;

public sealed class SessionInfoDto
{
    public string Id { get; set; } = string.Empty;
    public int Pid { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRunning { get; set; }
    public int? ExitCode { get; set; }
    public string? CurrentWorkingDirectory { get; set; }
    public int Cols { get; set; }
    public int Rows { get; set; }
    public string ShellType { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? TerminalTitle { get; set; }
    public bool ManuallyNamed { get; set; }
}
