namespace Ai.Tlbx.MiddleManager.Models;

public sealed class ShellInfoDto
{
    public string Type { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool SupportsOsc7 { get; set; }
}
