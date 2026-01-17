using System.Text.Json.Serialization;

namespace Ai.Tlbx.MidTerm.Services;

[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubAsset))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class GitHubReleaseContext : JsonSerializerContext
{
}
