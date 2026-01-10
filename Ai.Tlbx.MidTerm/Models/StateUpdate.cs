using Ai.Tlbx.MidTerm.Models.Update;

namespace Ai.Tlbx.MidTerm.Models;

/// <summary>
/// WebSocket state update message sent to clients.
/// </summary>
public sealed class StateUpdate
{
    public SessionListDto? Sessions { get; init; }
    public UpdateInfo? Update { get; init; }
}
