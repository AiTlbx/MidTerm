namespace Ai.Tlbx.MidTerm.Models.Update;

/// <summary>
/// Information about a locally available update (dev environment only).
/// </summary>
public sealed class LocalUpdateInfo
{
    public bool Available { get; init; }
    public string Version { get; init; } = "";
    public string Path { get; init; } = "";
    public UpdateType Type { get; init; } = UpdateType.Full;
    public bool SessionsPreserved => Type == UpdateType.WebOnly;
}
