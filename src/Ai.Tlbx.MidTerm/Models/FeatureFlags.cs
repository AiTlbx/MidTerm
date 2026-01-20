namespace Ai.Tlbx.MidTerm.Models;

/// <summary>
/// Feature flags passed to the frontend during bootstrap.
/// Used to conditionally enable/disable UI features.
/// </summary>
public sealed class FeatureFlags
{
    /// <summary>
    /// Whether the voice chat panel should be available.
    /// Enabled only in dev environments.
    /// </summary>
    public bool VoiceChat { get; init; }
}
