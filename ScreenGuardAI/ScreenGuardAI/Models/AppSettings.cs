namespace ScreenGuardAI.Models;

public class AppSettings
{
    public string SelectedProvider { get; set; } = "Gemini";
    public ProviderConfig OpenAI { get; set; } = new() { ApiKey = "", Model = "gpt-4o" };
    public ProviderConfig Gemini { get; set; } = new() { ApiKey = "", Model = "gemini-2.0-flash" };
    public ProviderConfig Groq { get; set; } = new() { ApiKey = "", Model = "llama-4-scout-17b-16e-instruct" };
    public ProviderConfig OpenRouter { get; set; } = new() { ApiKey = "", Model = "google/gemini-2.0-flash-exp:free" };
    public HotkeySettings Hotkey { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public string InterviewMode { get; set; } = "QA";

    /// <summary>
    /// Failover providers tried in order when the primary provider hits rate limits (429) or server errors (5xx).
    /// Example: ["Groq", "OpenRouter"] — tried after SelectedProvider fails.
    /// </summary>
    public List<string> FailoverProviders { get; set; } = new();

    /// <summary>
    /// Gets the provider config for the currently selected provider.
    /// </summary>
    public ProviderConfig GetActiveProvider()
    {
        return SelectedProvider switch
        {
            "OpenAI" => OpenAI,
            "Gemini" => Gemini,
            "Groq" => Groq,
            "OpenRouter" => OpenRouter,
            _ => Gemini
        };
    }

    /// <summary>
    /// Gets the provider config by provider key name.
    /// </summary>
    public ProviderConfig? GetProviderConfig(string providerKey)
    {
        return providerKey switch
        {
            "OpenAI" => OpenAI,
            "Gemini" => Gemini,
            "Groq" => Groq,
            "OpenRouter" => OpenRouter,
            _ => null
        };
    }

    /// <summary>
    /// Builds the full provider chain: primary + failover providers, filtered to those with API keys.
    /// </summary>
    public List<string> GetProviderChain()
    {
        var chain = new List<string>();

        // Primary provider first
        var primary = GetProviderConfig(SelectedProvider);
        if (primary != null && !string.IsNullOrWhiteSpace(primary.ApiKey))
            chain.Add(SelectedProvider);

        // Then failover providers
        foreach (var key in FailoverProviders)
        {
            if (key == SelectedProvider) continue; // skip duplicate
            var config = GetProviderConfig(key);
            if (config != null && !string.IsNullOrWhiteSpace(config.ApiKey))
                chain.Add(key);
        }

        return chain;
    }
}

public class ProviderConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public class HotkeySettings
{
    public string Modifiers { get; set; } = "Ctrl+Shift";
    public string Key { get; set; } = "Q";
}

public class AudioSettings
{
    public string WhisperModel { get; set; } = "base";
    public string Language { get; set; } = "en";
    public int SilenceThresholdMs { get; set; } = 1500;
    public float SilenceLevel { get; set; } = 0.01f;
    public bool AutoAnalyzeOnSpeech { get; set; } = false;
    public bool EnableAudioCapture { get; set; } = false;
    /// <summary>Only auto-analyze when a question is detected in the transcript.</summary>
    public bool SmartQuestionDetection { get; set; } = true;
    /// <summary>Minimum seconds between auto-analysis API calls (protects rate limits).</summary>
    public int AnalysisCooldownSeconds { get; set; } = 30;
}
