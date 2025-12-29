using Ai.Tlbx.MiddleManager.Settings;
using System.Text.Json.Serialization;

namespace Ai.Tlbx.MiddleManager.Settings
{
    [JsonSerializable(typeof(MiddleManagerSettings))]
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true,
        UseStringEnumConverter = true)]
    public partial class SettingsJsonContext : JsonSerializerContext
    {
    }
}
