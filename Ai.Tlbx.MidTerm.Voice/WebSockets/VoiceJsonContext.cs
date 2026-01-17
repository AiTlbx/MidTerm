using System.Text.Json;
using System.Text.Json.Serialization;
using Ai.Tlbx.MidTerm.Voice.Services;

namespace Ai.Tlbx.MidTerm.Voice.WebSockets;

/// <summary>
/// Source-generated JSON context for AOT-safe serialization of voice WebSocket messages.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(VoiceControlMessage))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(VoiceProviderInfo))]
[JsonSerializable(typeof(VoiceInfo))]
[JsonSerializable(typeof(VoiceDefaults))]
[JsonSerializable(typeof(List<VoiceProviderInfo>))]
public partial class VoiceJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Health response with provider information.
/// </summary>
public class HealthResponse
{
    public string Status { get; set; } = "ok";
    public string Version { get; set; } = "";
    public List<VoiceProviderInfo>? Providers { get; set; }
    public VoiceDefaults? Defaults { get; set; }
}
