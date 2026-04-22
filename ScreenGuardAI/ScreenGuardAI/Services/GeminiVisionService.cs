using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenGuardAI.Models;

namespace ScreenGuardAI.Services;

public class GeminiVisionService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private const string API_BASE = "https://generativelanguage.googleapis.com/v1beta/models";

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

    public async Task<AIResponse> AnalyzeInterviewQAAsync(string base64Image, string apiKey, string model = "gemini-2.0-flash", string? additionalContext = null)
    {
        var userPrompt = "Look at this screenshot from my live interview (video meeting). " +
            "Find the question being asked - check captions, chat, shared content, and any visible text. " +
            "Give me a ready-to-read answer I can say naturally.";
        if (!string.IsNullOrWhiteSpace(additionalContext))
            userPrompt += $"\n\nAdditional context: {additionalContext}";

        return await CallGeminiVisionAsync(base64Image, apiKey, model, QA_SYSTEM_PROMPT, userPrompt);
    }

    public async Task<AIResponse> AnalyzeCodingPracticalAsync(string base64Image, string apiKey, string model = "gemini-2.0-flash", string? additionalContext = null)
    {
        var userPrompt = "Look at this screenshot from my coding interview/test. " +
            "Read the problem statement, any constraints, and existing code. " +
            "Give me the complete working code solution with explanation.";
        if (!string.IsNullOrWhiteSpace(additionalContext))
            userPrompt += $"\n\nAdditional context: {additionalContext}";

        return await CallGeminiVisionAsync(base64Image, apiKey, model, CODING_SYSTEM_PROMPT, userPrompt, maxTokens: 4096);
    }

    private async Task<AIResponse> CallGeminiVisionAsync(string base64Image, string apiKey, string model,
        string systemPrompt, string userPrompt, int maxTokens = 2048)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AIResponse
            {
                Success = false,
                ErrorMessage = "Gemini API key is not configured. Please set it in Settings."
            };
        }

        try
        {
            var url = $"{API_BASE}/{model}:generateContent?key={apiKey}";

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
            {
                return new AIResponse
                {
                    Success = false,
                    ErrorMessage = $"Gemini API Error ({response.StatusCode}): {responseBody}"
                };
            }

            var result = JsonSerializer.Deserialize<GeminiResponse>(responseBody);
            var content = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
                          ?? "No response received.";

            return new AIResponse
            {
                Success = true,
                Content = content
            };
        }
        catch (TaskCanceledException)
        {
            return new AIResponse
            {
                Success = false,
                ErrorMessage = "Request timed out. Please check your internet connection and try again."
            };
        }
        catch (Exception ex)
        {
            return new AIResponse
            {
                Success = false,
                ErrorMessage = $"Error: {ex.Message}"
            };
        }
    }

    public async Task<(bool Success, string Message)> TestApiKeyAsync(string apiKey)
    {
        try
        {
            // Simple test: list models to verify the key
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
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
                return (false, $"Gemini API error ({(int)response.StatusCode}): {body}");
            }

            return (true, "Gemini API key is valid! Ready to use.");
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

    // Gemini response models
    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
