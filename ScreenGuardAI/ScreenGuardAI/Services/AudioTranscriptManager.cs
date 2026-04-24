namespace ScreenGuardAI.Services;

/// <summary>
/// Orchestrates AudioCaptureService and TranscriptionService.
/// Captures system audio, transcribes speech paragraphs, and maintains a rolling transcript.
/// Supports smart auto-analysis with question detection and cooldown to protect API rate limits.
/// </summary>
public class AudioTranscriptManager : IDisposable
{
    private readonly AudioCaptureService _captureService;
    private readonly TranscriptionService _transcriptionService;
    private readonly List<string> _transcriptParagraphs = new();
    private readonly object _transcriptLock = new();
    private DateTime _lastAnalysisTime = DateTime.MinValue;

    // Sliding-window rate limiter: tracks timestamps of recent API calls
    private readonly Queue<DateTime> _callTimestamps = new();
    private const int MaxCallsPerWindow = 3;          // max 3 calls per window
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(2); // 2-minute window

    /// <summary>Max paragraphs to keep in rolling transcript.</summary>
    public int MaxParagraphs { get; set; } = 20;

    /// <summary>Whether to auto-trigger AI analysis when a new paragraph is transcribed.</summary>
    public bool AutoAnalyzeOnSpeech { get; set; }

    /// <summary>
    /// When true, auto-analysis only triggers when a question is detected in the transcript.
    /// When false (and AutoAnalyzeOnSpeech is true), every paragraph triggers analysis.
    /// </summary>
    public bool SmartQuestionDetection { get; set; } = true;

    /// <summary>
    /// Minimum seconds between auto-analysis calls to protect API rate limits.
    /// Manual hotkey (Ctrl+Shift+Q) bypasses this cooldown.
    /// </summary>
    public int AnalysisCooldownSeconds { get; set; } = 30;

    // Events
    public event Action<string, string>? TranscriptUpdated;  // (fullTranscript, latestParagraph)
    public event Action? AnalysisRequested;                   // fired when auto-analyze triggers
    public event Action<float>? VolumeChanged;
    public event Action<string>? StatusChanged;
    public event Action<int>? DownloadProgressChanged;

    public bool IsListening => _captureService.IsCapturing;
    public bool IsModelReady => _transcriptionService.IsInitialized;

    // ── Question detection patterns ──
    private static readonly string[] QuestionKeywords = new[]
    {
        "what is", "what are", "what do", "what would", "what can",
        "how do", "how would", "how can", "how does", "how is",
        "why do", "why is", "why would", "why does",
        "can you", "could you", "would you",
        "tell me about", "explain how", "explain the", "explain your",
        "describe how", "describe the", "describe your", "describe a time",
        "walk me through",
        "have you", "do you", "did you", "are you", "is there",
        "where do", "where is", "when do", "when did",
        "which one", "which is",
        "give me an example", "what's your", "what about",
    };

    /// <summary>
    /// Common rhetorical / filler phrases that should NOT trigger auto-analysis.
    /// Matched after trimming and lowercasing.
    /// </summary>
    private static readonly string[] RhetoricalBlocklist = new[]
    {
        "right?", "right.", "okay?", "okay.", "ok?", "ok.",
        "you know?", "you know what i mean?", "know what i mean?",
        "makes sense?", "make sense?", "does that make sense?",
        "got it?", "get it?", "see what i mean?",
        "isn't it?", "wasn't it?", "don't you think?",
        "yeah?", "yes?", "no?", "huh?", "eh?",
        "sounds good?", "fair enough?", "correct?",
        "understood?", "clear?", "alright?",
    };

    /// <summary>Minimum word count for a transcript to qualify as a real question.</summary>
    private const int MinQuestionWordCount = 8;

    public AudioTranscriptManager(AudioCaptureService captureService, TranscriptionService transcriptionService)
    {
        _captureService = captureService;
        _transcriptionService = transcriptionService;

        // Wire events
        _captureService.ParagraphCompleted += OnParagraphCompleted;
        _captureService.VolumeChanged += level => VolumeChanged?.Invoke(level);
        _captureService.StatusChanged += status => StatusChanged?.Invoke(status);
        _transcriptionService.StatusChanged += status => StatusChanged?.Invoke(status);
        _transcriptionService.DownloadProgressChanged += mb => DownloadProgressChanged?.Invoke(mb);
    }

    /// <summary>
    /// Initialize the Whisper model. Must be called before StartListening.
    /// Downloads the model on first run.
    /// </summary>
    public async Task InitializeModelAsync(string modelSize = "base", string language = "en")
    {
        _transcriptionService.ModelSize = modelSize;
        _transcriptionService.Language = language;
        await _transcriptionService.InitializeAsync();
    }

    /// <summary>
    /// Start capturing and transcribing system audio.
    /// </summary>
    public async Task StartListeningAsync()
    {
        if (_captureService.IsCapturing) return;

        if (!_transcriptionService.IsInitialized)
        {
            await InitializeModelAsync();
        }

        _captureService.Start();
    }

    /// <summary>
    /// Stop capturing audio.
    /// </summary>
    public void StopListening()
    {
        _captureService.Stop();
    }

    /// <summary>
    /// Gets the full rolling transcript.
    /// </summary>
    public string GetFullTranscript()
    {
        lock (_transcriptLock)
        {
            return string.Join("\n", _transcriptParagraphs);
        }
    }

    /// <summary>
    /// Gets the most recent N paragraphs as context for AI analysis.
    /// </summary>
    public string GetRecentTranscript(int paragraphs = 5)
    {
        lock (_transcriptLock)
        {
            var recent = _transcriptParagraphs.Skip(Math.Max(0, _transcriptParagraphs.Count - paragraphs));
            return string.Join("\n", recent);
        }
    }

    /// <summary>
    /// Clears the transcript history.
    /// </summary>
    public void ClearTranscript()
    {
        lock (_transcriptLock)
        {
            _transcriptParagraphs.Clear();
        }
    }

    private async void OnParagraphCompleted(float[] samples)
    {
        try
        {
            var text = await _transcriptionService.TranscribeAsync(samples);

            if (string.IsNullOrWhiteSpace(text)) return;

            // Filter out common Whisper hallucination artifacts
            var trimmed = text.Trim();
            if (trimmed.Length < 3) return;
            if (trimmed.All(c => c == '.' || c == '!' || c == '?' || char.IsWhiteSpace(c))) return;
            // Common whisper hallucinations when silence/noise is transcribed
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) return;
            if (trimmed.Contains("Thank you for watching", StringComparison.OrdinalIgnoreCase)) return;
            if (trimmed.Contains("Thanks for watching", StringComparison.OrdinalIgnoreCase)) return;

            lock (_transcriptLock)
            {
                _transcriptParagraphs.Add(trimmed);
                // Trim to max paragraphs
                while (_transcriptParagraphs.Count > MaxParagraphs)
                    _transcriptParagraphs.RemoveAt(0);
            }

            var fullTranscript = GetFullTranscript();
            TranscriptUpdated?.Invoke(fullTranscript, trimmed);
            StatusChanged?.Invoke($"TRANSCRIBED: \"{(trimmed.Length > 60 ? trimmed[..60] + "..." : trimmed)}\"");

            if (AutoAnalyzeOnSpeech)
            {
                TryAutoAnalyze(trimmed);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"TRANSCRIPTION ERROR: {ex.Message}");
        }
    }

    /// <summary>
    /// Decides whether to fire AnalysisRequested based on question detection and cooldown.
    /// Gate order: cooldown → min-length → rhetorical blocklist → question detection.
    /// </summary>
    private void TryAutoAnalyze(string latestParagraph)
    {
        // ── Cooldown check ──
        var elapsed = (DateTime.UtcNow - _lastAnalysisTime).TotalSeconds;
        if (elapsed < AnalysisCooldownSeconds)
        {
            var remaining = AnalysisCooldownSeconds - (int)elapsed;
            StatusChanged?.Invoke($"AUTO-ANALYZE: cooldown ({remaining}s remaining)");
            return;
        }

        // ── Sliding-window rate limiter ──
        var now = DateTime.UtcNow;
        // Evict expired timestamps
        while (_callTimestamps.Count > 0 && (now - _callTimestamps.Peek()) > RateWindow)
            _callTimestamps.Dequeue();

        if (_callTimestamps.Count >= MaxCallsPerWindow)
        {
            var oldest = _callTimestamps.Peek();
            var waitSec = (int)(RateWindow - (now - oldest)).TotalSeconds + 1;
            StatusChanged?.Invoke($"AUTO-ANALYZE: rate limit ({MaxCallsPerWindow} calls/{(int)RateWindow.TotalMinutes}min, retry in {waitSec}s)");
            return;
        }

        // ── Question detection (with rhetorical + min-length filters) ──
        if (SmartQuestionDetection)
        {
            // Min word count — short utterances are almost never real questions
            var wordCount = latestParagraph.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount < MinQuestionWordCount)
            {
                StatusChanged?.Invoke($"AUTO-ANALYZE: skipped (too short, {wordCount} words)");
                return;
            }

            // Rhetorical blocklist — skip common filler phrases
            if (IsRhetorical(latestParagraph))
            {
                StatusChanged?.Invoke("AUTO-ANALYZE: skipped (rhetorical phrase)");
                return;
            }

            if (!IsQuestion(latestParagraph))
            {
                StatusChanged?.Invoke("AUTO-ANALYZE: skipped (no question detected)");
                return;
            }
            StatusChanged?.Invoke("AUTO-ANALYZE: question detected → triggering analysis");
        }

        _lastAnalysisTime = DateTime.UtcNow;
        _callTimestamps.Enqueue(DateTime.UtcNow);
        AnalysisRequested?.Invoke();
    }

    /// <summary>
    /// Determines if a transcribed paragraph likely contains a question.
    /// Checks for question marks and common interview question patterns.
    /// </summary>
    private static bool IsQuestion(string text)
    {
        // Direct question mark
        if (text.Contains('?')) return true;

        var lower = text.ToLowerInvariant();

        // Match any known question pattern
        foreach (var pattern in QuestionKeywords)
        {
            if (lower.Contains(pattern)) return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the text is a common rhetorical filler phrase that should be ignored.
    /// </summary>
    private static bool IsRhetorical(string text)
    {
        var lower = text.Trim().ToLowerInvariant().TrimEnd('.', '!');
        // Exact match against blocklist (after normalizing trailing punctuation)
        foreach (var phrase in RhetoricalBlocklist)
        {
            var normalizedPhrase = phrase.TrimEnd('.', '!', '?');
            if (lower == normalizedPhrase || lower == phrase)
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        _captureService.ParagraphCompleted -= OnParagraphCompleted;
        _captureService.Dispose();
        _transcriptionService.Dispose();
    }
}
