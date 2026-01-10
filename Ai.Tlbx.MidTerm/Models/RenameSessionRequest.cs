namespace Ai.Tlbx.MidTerm.Models;

/// <summary>
/// Request payload for renaming a terminal session.
/// </summary>
public sealed class RenameSessionRequest
{
    public string? Name { get; set; }
}
