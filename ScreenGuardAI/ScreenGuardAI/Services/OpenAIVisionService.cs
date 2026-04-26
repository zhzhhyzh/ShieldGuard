using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenGuardAI.Models;

namespace ScreenGuardAI.Services;

public enum InterviewMode
{
    QA,
    Coding,
    Behavioral
}

public class OpenAIVisionService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private const string API_URL = "https://api.openai.com/v1/chat/completions";

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

    private const string BEHAVIORAL_SYSTEM_PROMPT =
        "You are an expert interview coach specializing in behavioral and situational interview questions. " +
        "The user is in a live interview right now. The screenshot is from a video meeting platform. " +
        "The interview question may appear in live captions, chat, shared screen, or any visible text.\n\n" +
        "Your job:\n" +
        "1. Scan ALL visible text to find the behavioral/situational interview question being asked.\n" +
        "2. Generate a natural, conversational answer the interviewee can read aloud as if speaking from memory.\n" +
        "3. Internally structure your answer using the STAR method (Situation, Task, Action, Result), but DO NOT label or mention these sections.\n" +
        "4. The answer MUST flow as one continuous, natural narrative — like telling a story to a friend. No bullet points, no section headers, no bold labels.\n" +
        "5. Start with a brief setup (a real-sounding scenario from work), naturally transition into what you did and why, then land on the outcome and what you learned.\n" +
        "6. Use first-person perspective and conversational transitions like 'So what I did was...', 'That led to...', 'In the end...'.\n" +
        "7. Keep it 3-5 sentences. Sound confident and authentic, not rehearsed or robotic.\n" +
        "8. NEVER use phrases like 'The situation was', 'My task was', 'The action I took', 'The result was', 'In terms of', 'background', 'the difficult part' — these sound scripted.\n" +
        "9. Tailor the answer to the user's profile if provided (role, tech stack, experience level, projects).\n\n" +
        "Format your response EXACTLY like this:\n" +
        "QUESTION: [the extracted question]\n\n" +
        "ANSWER:\n[the ready-to-speak natural answer — no headers, no labels, just a flowing story]";

    /// <summary>
    /// Analyzes a screenshot in interview Q&A mode.
    /// </summary>
    public async Task<AIResponse> AnalyzeInterviewQAAsync(string base64Image, string apiKey, string model = "gpt-4o", string? additionalContext = null)
    {
        var userPrompt = "Look at this screenshot from my live interview (video meeting). " +
            "Find the question being asked - check captions, chat, shared content, and any visible text. " +
            "Give me a ready-to-read answer I can say naturally.";
        if (!string.IsNullOrWhiteSpace(additionalContext))
            userPrompt += $"\n\nAdditional context: {additionalContext}";

        return await CallVisionAPIAsync(base64Image, apiKey, model, QA_SYSTEM_PROMPT, userPrompt);
    }

    /// <summary>
    /// Analyzes a screenshot in coding practical mode.
    /// </summary>
    public async Task<AIResponse> AnalyzeCodingPracticalAsync(string base64Image, string apiKey, string model = "gpt-4o", string? additionalContext = null)
    {
        var userPrompt = "Look at this screenshot from my coding interview/test. " +
            "Read the problem statement, any constraints, and existing code. " +
            "Give me the complete working code solution with explanation.";
        if (!string.IsNullOrWhiteSpace(additionalContext))
            userPrompt += $"\n\nAdditional context: {additionalContext}";

        return await CallVisionAPIAsync(base64Image, apiKey, model, CODING_SYSTEM_PROMPT, userPrompt, maxTokens: 2048);
    }

    /// <summary>
    /// Analyzes a screenshot in behavioral interview mode.
    /// </summary>
    public async Task<AIResponse> AnalyzeBehavioralAsync(string base64Image, string apiKey, string model = "gpt-4o", string? additionalContext = null)
    {
        var userPrompt = "Look at this screenshot from my live interview (video meeting). " +
            "Find the behavioral or situational question being asked - check captions, chat, shared content. " +
            "Give me a natural, story-like answer I can say as if recalling a real experience.";
        if (!string.IsNullOrWhiteSpace(additionalContext))
            userPrompt += $"\n\nAdditional context: {additionalContext}";

        return await CallVisionAPIAsync(base64Image, apiKey, model, BEHAVIORAL_SYSTEM_PROMPT, userPrompt);
    }

    /// <summary>
    /// Core method to call the OpenAI Vision API.
    /// </summary>
    private async Task<AIResponse> CallVisionAPIAsync(string base64Image, string apiKey, string model,
        string systemPrompt, string userPrompt, int maxTokens = 1024)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AIResponse
            {
                Success = false,
                ErrorMessage = "OpenAI API key is not configured. Please set it in Settings."
            };
        }

        try
        {
            var userContent = new List<object>
            {
                new { type = "text", text = userPrompt },
                new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = $"data:image/jpeg;base64,{base64Image}",
                        detail = "high"
                    }
                }
            };

            var requestBody = new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                max_tokens = maxTokens,
                temperature = 0.3
            };

            var json = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, API_URL)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new AIResponse
                {
                    Success = false,
                    ErrorMessage = $"API Error ({response.StatusCode}): {responseBody}"
                };
            }

            var result = JsonSerializer.Deserialize<OpenAIResponse>(responseBody);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "No response received.";

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

    /// <summary>
    /// Tests if the API key is valid by making a simple request.
    /// Returns (success, errorDetail) so the UI can show what went wrong.
    /// </summary>
    public async Task<(bool Success, string Message)> TestApiKeyAsync(string apiKey)
    {
        try
        {
            // First try the models endpoint - lightweight, no tokens consumed
            var modelsRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            modelsRequest.Headers.Add("Authorization", $"Bearer {apiKey}");

            var modelsResponse = await _httpClient.SendAsync(modelsRequest);
            if (!modelsResponse.IsSuccessStatusCode)
            {
                var errorBody = await modelsResponse.Content.ReadAsStringAsync();
                if ((int)modelsResponse.StatusCode == 401)
                    return (false, "Invalid API key. Please check and re-enter.");
                if ((int)modelsResponse.StatusCode == 429)
                    return (false, "Rate limited or quota exceeded. Check your OpenAI billing/usage.");
                return (false, $"API error {(int)modelsResponse.StatusCode}: {errorBody}");
            }

            // Key is valid - now check if gpt-4o is accessible with a tiny request
            var requestBody = new
            {
                model = "gpt-4o",
                messages = new object[]
                {
                    new { role = "user", content = "Say OK" }
                },
                max_tokens = 5
            };

            var json = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, API_URL)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return (true, "API key is valid! GPT-4o access confirmed.");

            var body = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode == 404 || body.Contains("model_not_found", StringComparison.OrdinalIgnoreCase))
                return (false, "API key works but does NOT have access to gpt-4o. Check your OpenAI plan.");
            if ((int)response.StatusCode == 429)
                return (false, "API key works but you've hit rate/quota limits. Check your OpenAI billing.");
            if ((int)response.StatusCode == 403)
                return (false, "API key works but access denied to gpt-4o. You may need to add billing or upgrade plan.");
            if ((int)response.StatusCode == 401)
                return (false, "Invalid API key. Please check and re-enter.");

            return (false, $"API key accepted but got error ({(int)response.StatusCode}): {body}");
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

    // JSON deserialization models for OpenAI response
    private class OpenAIResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public MessageContent? Message { get; set; }
    }

    private class MessageContent
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
