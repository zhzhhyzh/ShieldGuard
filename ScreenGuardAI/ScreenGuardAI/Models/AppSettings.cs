namespace ScreenGuardAI.Models;

public class AppSettings
{
    public string SelectedProvider { get; set; } = "Gemini";
    public ProviderConfig OpenAI { get; set; } = new() { ApiKey = "", Model = "gpt-4o" };
    public ProviderConfig Gemini { get; set; } = new() { ApiKey = "", Model = "gemini-2.0-flash" };
    public ProviderConfig Groq { get; set; } = new() { ApiKey = "", Model = "llama-4-scout-17b-16e-instruct" };
    public ProviderConfig OpenRouter { get; set; } = new() { ApiKey = "", Model = "google/gemini-2.0-flash-exp:free" };
    public HotkeySettings Hotkey { get; set; } = new();
    public string InterviewMode { get; set; } = "QA";

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
