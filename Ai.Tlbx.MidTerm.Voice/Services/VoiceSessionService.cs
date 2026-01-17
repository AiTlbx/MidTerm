namespace Ai.Tlbx.MidTerm.Voice.Services;

/// <summary>
/// Provides configuration and settings for voice sessions.
/// </summary>
public sealed class VoiceSessionService
{
    private readonly IConfiguration _configuration;

    public VoiceSessionService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string? GetOpenAiApiKey()
    {
        return _configuration["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    public string? GetGoogleApiKey()
    {
        return _configuration["Google:ApiKey"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    }

    public string? GetXaiApiKey()
    {
        return _configuration["XAi:ApiKey"]
            ?? Environment.GetEnvironmentVariable("XAI_API_KEY");
    }

    public string GetSystemPrompt()
    {
        return _configuration["Voice:SystemPrompt"]
            ?? """
            You are a helpful voice assistant for MidTerm, a terminal multiplexer.
            You can help users with terminal operations, answer questions, and provide assistance.
            Keep responses concise and conversational since this is a voice interface.
            """;
    }

    public string GetMidTermServerUrl()
    {
        return _configuration["Voice:MidTermServerUrl"]
            ?? "https://localhost:2000";
    }

    /// <summary>
    /// Get available voice providers with their voices.
    /// </summary>
    public List<VoiceProviderInfo> GetAvailableProviders()
    {
        return
        [
            new VoiceProviderInfo
            {
                Id = "openai",
                Name = "OpenAI",
                Available = !string.IsNullOrEmpty(GetOpenAiApiKey()),
                Voices =
                [
                    new VoiceInfo { Id = "alloy", Name = "Alloy" },
                    new VoiceInfo { Id = "ash", Name = "Ash" },
                    new VoiceInfo { Id = "ballad", Name = "Ballad" },
                    new VoiceInfo { Id = "coral", Name = "Coral" },
                    new VoiceInfo { Id = "echo", Name = "Echo" },
                    new VoiceInfo { Id = "sage", Name = "Sage" },
                    new VoiceInfo { Id = "shimmer", Name = "Shimmer" },
                    new VoiceInfo { Id = "verse", Name = "Verse" }
                ]
            },
            new VoiceProviderInfo
            {
                Id = "google",
                Name = "Google Gemini",
                Available = !string.IsNullOrEmpty(GetGoogleApiKey()),
                Voices =
                [
                    new VoiceInfo { Id = "puck", Name = "Puck" },
                    new VoiceInfo { Id = "charon", Name = "Charon" },
                    new VoiceInfo { Id = "kore", Name = "Kore" },
                    new VoiceInfo { Id = "fenrir", Name = "Fenrir" },
                    new VoiceInfo { Id = "aoede", Name = "Aoede" }
                ]
            },
            new VoiceProviderInfo
            {
                Id = "xai",
                Name = "xAI Grok",
                Available = !string.IsNullOrEmpty(GetXaiApiKey()),
                Voices =
                [
                    new VoiceInfo { Id = "grok", Name = "Grok" }
                ]
            }
        ];
    }

    /// <summary>
    /// Get default voice settings.
    /// </summary>
    public VoiceDefaults GetDefaults()
    {
        // Find first available provider
        var providers = GetAvailableProviders();
        var firstAvailable = providers.FirstOrDefault(p => p.Available);

        return new VoiceDefaults
        {
            Provider = firstAvailable?.Id ?? "openai",
            Voice = firstAvailable?.Voices.FirstOrDefault()?.Id ?? "alloy",
            Speed = 1.0
        };
    }
}

/// <summary>
/// Information about a voice provider.
/// </summary>
public class VoiceProviderInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Available { get; set; }
    public List<VoiceInfo> Voices { get; set; } = [];
}

/// <summary>
/// Information about a voice.
/// </summary>
public class VoiceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// Default voice settings.
/// </summary>
public class VoiceDefaults
{
    public string Provider { get; set; } = "";
    public string Voice { get; set; } = "";
    public double Speed { get; set; } = 1.0;
}
