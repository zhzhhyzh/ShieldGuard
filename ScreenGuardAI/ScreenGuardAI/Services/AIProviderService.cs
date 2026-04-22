using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ScreenGuardAI.Models;

namespace ScreenGuardAI.Services;

/// <summary>
/// Unified AI provider service that supports OpenAI, Gemini, Groq, and OpenRouter.
/// Groq and OpenRouter use OpenAI-compatible API format.
/// </summary>
public class AIProviderService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(90) };

    // --- Provider definitions ---
    public static readonly Dictionary<string, ProviderDefinition> Providers = new()
    {
        ["OpenAI"] = new ProviderDefinition
        {
            Name = "OpenAI",
            Description = "GPT-4o and GPT-4.1 models",
            ApiFormat = ApiFormat.OpenAI,
            BaseUrl = "https://api.openai.com/v1",
            AuthType = AuthType.Bearer,
            Models = ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini"],
            DefaultModel = "gpt-4o",
            KeyUrl = "https://platform.openai.com/api-keys",
            FreeTier = false,
            TestEndpoint = "models"
        },
        ["Gemini"] = new ProviderDefinition
        {
            Name = "Google Gemini",
            Description = "Fast, free tier available",
            ApiFormat = ApiFormat.Gemini,
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            AuthType = AuthType.QueryParam,
            Models = ["gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-2.5-flash-preview-04-17", "gemini-2.5-pro-preview-03-25"],
            DefaultModel = "gemini-2.0-flash",
            KeyUrl = "https://aistudio.google.com/apikey",
            FreeTier = true,
            TestEndpoint = "models"
        },
        ["Groq"] = new ProviderDefinition
        {
            Name = "Groq",
            Description = "Ultra-fast inference, free",
            ApiFormat = ApiFormat.OpenAI,
            BaseUrl = "https://api.groq.com/openai/v1",
            AuthType = AuthType.Bearer,
            Models = ["meta-llama/llama-4-scout-17b-16e-instruct", "llama-3.3-70b-versatile", "llama-3.1-8b-instant"],
            DefaultModel = "meta-llama/llama-4-scout-17b-16e-instruct",
            KeyUrl = "https://console.groq.com/keys",
            FreeTier = true,
            TestEndpoint = "models"
        },
        ["OpenRouter"] = new ProviderDefinition
        {
            Name = "OpenRouter",
            Description = "Many free models available",
            ApiFormat = ApiFormat.OpenAI,
            BaseUrl = "https://openrouter.ai/api/v1",
            AuthType = AuthType.Bearer,
            Models = ["google/gemini-2.0-flash-exp:free", "meta-llama/llama-4-scout:free", "qwen/qwen-2.5-vl-72b-instruct:free", "deepseek/deepseek-chat-v3-0324:free"],
            DefaultModel = "google/gemini-2.0-flash-exp:free",
            KeyUrl = "https://openrouter.ai/keys",
            FreeTier = true,
            TestEndpoint = "models"
        }
    };

    // --- Prompts (shared across all providers) ---
    private const string QA_SYSTEM_PROMPT =
        "You are an expert interview coach. The user is in a live interview happening right now. " +
        "The screenshot is from a video meeting platform (Zoom, Google Meet, Microsoft Teams, Webex, or similar). " +
        "The interview question may appear in any of these locations:\n" +
        "- Live captions / subtitles at the bottom of the screen\n" +
        "- The meeting chat panel\n" +
        "- A shared screen or document being presented\n" +
        "- Text overlay or whiteboard in the meeting\n" +
        "- A separate browser tab or application visible on screen\n\n" +
        "Your job:\n" +
        "1. Carefully scan ALL visible text to find the interview question being asked.\n" +
        "2. If you see captions/subtitles, those likely contain the most recent spoken question.\n" +
        "3. Generate a clear, confident, well-structured ANSWER the interviewee can read aloud naturally.\n" +
        "4. The answer should sound like a real person speaking - conversational but professional.\n" +
        "5. Keep it concise (2-4 paragraphs max) but thorough enough to impress.\n" +
        "6. Use the STAR method (Situation, Task, Action, Result) for behavioral questions if appropriate.\n" +
        "7. For technical questions, give a clear and accurate explanation.\n\n" +
        "Format your response EXACTLY like this:\n" +
        "QUESTION: [the extracted question]\n\n" +
        "ANSWER:\n[the ready-to-read answer paragraph(s)]";

    private const string CODING_SYSTEM_PROMPT =
        "You are an expert software engineer and coding interview coach. The user is in a live coding interview or practical test. " +
        "The screenshot may show:\n" +
        "- A coding platform (LeetCode, HackerRank, CodeSignal, CoderPad, etc.)\n" +
        "- A shared IDE or code editor in a meeting (Zoom, Google Meet, Teams)\n" +
        "- A whiteboard or document with a coding problem description\n" +
        "- A terminal, console, or online coding environment\n" +
        "- The problem statement in a chat message or shared screen\n\n" +
        "Your job:\n" +
        "1. Identify the coding problem or task shown on screen. Read ALL visible text carefully.\n" +
        "2. Detect the programming language expected (from existing code, file extension, or platform hints).\n" +
        "3. Provide a COMPLETE, working solution with clean code in the detected language.\n" +
        "4. The code must be ready to type in - no placeholders, no TODOs, no incomplete parts.\n" +
        "5. After the code, provide a brief EXPLANATION (2-3 sentences) the user can say aloud if asked why they chose this approach.\n" +
        "6. If you see existing code with errors, fix them and explain what was wrong.\n" +
        "7. Include time/space complexity.\n\n" +
        "Format your response EXACTLY like this:\n" +
        "PROBLEM: [brief description of what's being asked]\n\n" +
        "CODE:\n```\n[complete solution code]\n```\n\n" +
        "EXPLANATION:\n[brief explanation of approach, why this solution works, and complexity]";

    // --- Public API ---

    /// <summary>
    /// Lightweight detection prompt: asks the AI if a coding problem is visible on screen.
    /// Uses minimal tokens for fast, cheap scanning.
    /// </summary>
    public async Task<CodingProblemDetection> DetectCodingProblemAsync(string base64Image, string providerKey, string apiKey, string model)
    {
        var systemPrompt = "You are a screen scanner. Look at the screenshot and determine if it shows a coding problem, " +
            "LeetCode question, HackerRank challenge, or any programming task/assignment. " +
            "Respond ONLY in this exact format with nothing else:\n" +
            "YES|<problem title or brief description>\n" +
            "or\n" +
            "NO\n\n" +
            "Examples:\n" +
            "YES|Two Sum\n" +
            "YES|Binary Tree Level Order Traversal\n" +
            "YES|SQL query to find duplicates\n" +
            "NO";

        var userPrompt = "Is there a coding problem visible on this screen?";

        try
        {
            var response = await CallProviderAsync(providerKey, base64Image, apiKey, model, systemPrompt, userPrompt, maxTokens: 50);
            if (!response.Success)
                return new CodingProblemDetection { IsDetected = false, Error = response.ErrorMessage ?? "Unknown error" };

            var text = response.Content.Trim();
            if (text.StartsWith("YES", StringComparison.OrdinalIgnoreCase))
            {
                var parts = text.Split('|', 2);
                var title = parts.Length > 1 ? parts[1].Trim() : "Coding Problem";
                return new CodingProblemDetection { IsDetected = true, Title = title };
            }

            return new CodingProblemDetection { IsDetected = false };
        }
        catch (Exception ex)
        {
            return new CodingProblemDetection { IsDetected = false, Error = ex.Message };
        }
    }

    public async Task<AIResponse> AnalyzeInterviewQAAsync(string base64Image, string providerKey, string apiKey, string model, string? additionalContext = null)
    {
        var userPrompt = "Look at this screenshot from my live interview (video meeting). " +
            "Find the question being asked - check captions, chat, shared content, and any visible text. " +
            "Give me a ready-to-read answer I can say naturally.";
        if (!string.IsNullOrWhiteSpace(additionalContext))
            userPrompt += $"\n\nAdditional context: {additionalContext}";

        return await CallProviderAsync(providerKey, base64Image, apiKey, model, QA_SYSTEM_PROMPT, userPrompt);
    }

    public async Task<AIResponse> AnalyzeCodingPracticalAsync(string base64Image, string providerKey, string apiKey, string model, string? additionalContext = null)
    {
        var userPrompt = "Look at this screenshot from my coding interview/test. " +
            "Read the problem statement, any constraints, and existing code. " +
            "Give me the complete working code solution with explanation.";
        if (!string.IsNullOrWhiteSpace(additionalContext))
            userPrompt += $"\n\nAdditional context: {additionalContext}";

        return await CallProviderAsync(providerKey, base64Image, apiKey, model, CODING_SYSTEM_PROMPT, userPrompt, maxTokens: 4096);
    }

    public async Task<(bool Success, string Message)> TestApiKeyAsync(string providerKey, string apiKey)
    {
        if (!Providers.TryGetValue(providerKey, out var provider))
            return (false, $"Unknown provider: {providerKey}");

        try
        {
            if (provider.ApiFormat == ApiFormat.Gemini)
                return await TestGeminiKeyAsync(provider, apiKey);
            else
                return await TestOpenAICompatibleKeyAsync(provider, apiKey);
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timed out. Check your internet connection.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    // --- Private implementation ---

    private async Task<AIResponse> CallProviderAsync(string providerKey, string base64Image, string apiKey,
        string model, string systemPrompt, string userPrompt, int maxTokens = 2048)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new AIResponse { Success = false, ErrorMessage = $"API key not configured for {providerKey}. Please set it in Settings." };

        if (!Providers.TryGetValue(providerKey, out var provider))
            return new AIResponse { Success = false, ErrorMessage = $"Unknown provider: {providerKey}" };

        try
        {
            if (provider.ApiFormat == ApiFormat.Gemini)
                return await CallGeminiAsync(provider, base64Image, apiKey, model, systemPrompt, userPrompt, maxTokens);
            else
                return await CallOpenAICompatibleAsync(provider, base64Image, apiKey, model, systemPrompt, userPrompt, maxTokens);
        }
        catch (TaskCanceledException)
        {
            return new AIResponse { Success = false, ErrorMessage = "Request timed out. Please check your internet connection and try again." };
        }
        catch (Exception ex)
        {
            return new AIResponse { Success = false, ErrorMessage = $"Error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Calls OpenAI-compatible APIs (OpenAI, Groq, OpenRouter).
    /// Uses explicit JsonNode construction to avoid serialization issues with anonymous types.
    /// Groq requires: no "detail" field in image_url, system prompt merged into user content.
    /// </summary>
    private async Task<AIResponse> CallOpenAICompatibleAsync(ProviderDefinition provider, string base64Image,
        string apiKey, string model, string systemPrompt, string userPrompt, int maxTokens)
    {
        var url = $"{provider.BaseUrl}/chat/completions";
        bool isGroq = provider.Name == "Groq";

        // Build image_url object - Groq doesn't support "detail"
        var imageUrlNode = new JsonObject { ["url"] = $"data:image/jpeg;base64,{base64Image}" };
        if (!isGroq)
            imageUrlNode["detail"] = "high";

        // For Groq: merge system prompt into user text
        var userText = isGroq ? $"{systemPrompt}\n\n{userPrompt}" : userPrompt;

        // Build user content array: [text, image]
        var userContentArray = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = userText },
            new JsonObject { ["type"] = "image_url", ["image_url"] = imageUrlNode }
        };

        // Build messages array
        var messagesArray = new JsonArray();
        if (!isGroq)
            messagesArray.Add(new JsonObject { ["role"] = "system", ["content"] = systemPrompt });
        messagesArray.Add(new JsonObject { ["role"] = "user", ["content"] = userContentArray });

        // Build full request body
        var requestBody = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messagesArray,
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0.3
        };

        var json = requestBody.ToJsonString();

        // DEBUG: Log request structure (truncate base64 for readability)
        try
        {
            var debugJson = json;
            var b64Idx = debugJson.IndexOf("data:image/jpeg;base64,");
            if (b64Idx > 0)
            {
                var endIdx = debugJson.IndexOf("\"", b64Idx + 30);
                if (endIdx > 0)
                    debugJson = debugJson[..(b64Idx + 30)] + "...[TRUNCATED]" + debugJson[endIdx..];
            }
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScreenGuardAI", "debug_request.json");
            System.IO.File.WriteAllText(logPath, debugJson);
        }
        catch { }

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        // OpenRouter requires extra headers
        if (provider.Name == "OpenRouter")
        {
            request.Headers.Add("HTTP-Referer", "https://screenguardai.app");
            request.Headers.Add("X-Title", "ScreenGuard AI");
        }

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return new AIResponse { Success = false, ErrorMessage = $"{provider.Name} API Error ({response.StatusCode}): {responseBody}" };

        var result = JsonSerializer.Deserialize<OpenAIResponse>(responseBody);
        var content = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "No response received.";

        return new AIResponse { Success = true, Content = content };
    }

    /// <summary>
    /// Calls Google Gemini API (different format from OpenAI).
    /// </summary>
    private async Task<AIResponse> CallGeminiAsync(ProviderDefinition provider, string base64Image,
        string apiKey, string model, string systemPrompt, string userPrompt, int maxTokens)
    {
        var url = $"{provider.BaseUrl}/models/{model}:generateContent?key={apiKey}";

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = userPrompt },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = "image/jpeg",
                                data = base64Image
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = maxTokens,
                temperature = 0.3
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return new AIResponse { Success = false, ErrorMessage = $"Gemini API Error ({response.StatusCode}): {responseBody}" };

        var result = JsonSerializer.Deserialize<GeminiResponse>(responseBody);
        var content = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
                      ?? "No response received.";

        return new AIResponse { Success = true, Content = content };
    }

    // --- Test methods ---

    private async Task<(bool, string)> TestOpenAICompatibleKeyAsync(ProviderDefinition provider, string apiKey)
    {
        var url = $"{provider.BaseUrl}/{provider.TestEndpoint}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        if (provider.Name == "OpenRouter")
        {
            request.Headers.Add("HTTP-Referer", "https://screenguardai.app");
            request.Headers.Add("X-Title", "ScreenGuard AI");
        }

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            return (int)response.StatusCode switch
            {
                401 => (false, $"Invalid {provider.Name} API key. Please check and re-enter."),
                403 => (false, $"{provider.Name} access denied. Check your account settings."),
                429 => (false, $"Rate limited on {provider.Name}. Try again in a moment."),
                _ => (false, $"{provider.Name} error ({(int)response.StatusCode}): {body}")
            };
        }

        return (true, $"{provider.Name} API key is valid! Ready to use.");
    }

    private async Task<(bool, string)> TestGeminiKeyAsync(ProviderDefinition provider, string apiKey)
    {
        var url = $"{provider.BaseUrl}/{provider.TestEndpoint}?key={apiKey}";
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode == 400 || body.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase))
                return (false, "Invalid Gemini API key. Please check and re-enter.");
            if ((int)response.StatusCode == 403)
                return (false, "Gemini API key forbidden. Check your Google Cloud project settings.");
            if ((int)response.StatusCode == 429)
                return (false, "Rate limited. Try again in a moment.");
            return (false, $"Gemini error ({(int)response.StatusCode}): {body}");
        }

        return (true, "Gemini API key is valid! Ready to use.");
    }

    // --- JSON models ---

    private class OpenAIResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }
    private class Choice
    {
        [JsonPropertyName("message")] public MessageContent? Message { get; set; }
    }
    private class MessageContent
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")] public List<GeminiCandidate>? Candidates { get; set; }
    }
    private class GeminiCandidate
    {
        [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
    }
    private class GeminiContent
    {
        [JsonPropertyName("parts")] public List<GeminiPart>? Parts { get; set; }
    }
    private class GeminiPart
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}

// --- Provider definition types ---

public enum ApiFormat { OpenAI, Gemini }
public enum AuthType { Bearer, QueryParam }

public class ProviderDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ApiFormat ApiFormat { get; set; }
    public string BaseUrl { get; set; } = "";
    public AuthType AuthType { get; set; }
    public string[] Models { get; set; } = [];
    public string DefaultModel { get; set; } = "";
    public string KeyUrl { get; set; } = "";
    public bool FreeTier { get; set; }
    public string TestEndpoint { get; set; } = "models";
}

public class CodingProblemDetection
{
    public bool IsDetected { get; set; }
    public string Title { get; set; } = "";
    public string Error { get; set; } = "";
}
