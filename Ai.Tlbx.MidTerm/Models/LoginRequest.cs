namespace Ai.Tlbx.MidTerm.Models;

/// <summary>
/// Request payload for user authentication.
/// </summary>
public sealed class LoginRequest
{
    public string Password { get; init; } = "";
}
