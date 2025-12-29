using System.Text.Json.Serialization;
using Ai.Tlbx.MiddleManager.Models;
using Ai.Tlbx.MiddleManager.Settings;

namespace Ai.Tlbx.MiddleManager.Services;

[JsonSerializable(typeof(SessionListDto))]
[JsonSerializable(typeof(SessionInfoDto))]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(RenameSessionRequest))]
[JsonSerializable(typeof(ResizeRequest))]
[JsonSerializable(typeof(ResizeResponse))]
[JsonSerializable(typeof(List<NetworkInterfaceDto>))]
[JsonSerializable(typeof(ShellInfoDto))]
[JsonSerializable(typeof(List<ShellInfoDto>))]
[JsonSerializable(typeof(MiddleManagerSettings))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
public partial class AppJsonContext : JsonSerializerContext
{
}
