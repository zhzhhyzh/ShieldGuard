namespace ScreenGuardAI.Services;

/// <summary>
/// Orchestrates AudioCaptureService and TranscriptionService.
/// Captures system audio, transcribes speech paragraphs, and maintains a rolling transcript.
/// </summary>
public class AudioTranscriptManager : IDisposable
{
    private readonly AudioCaptureService _captureService;
    private readonly TranscriptionService _transcriptionService;
    private readonly List<string> _transcriptParagraphs = new();
    private readonly object _transcriptLock = new();

    /// <summary>Max paragraphs to keep in rolling transcript.</summary>
    public int MaxParagraphs { get; set; } = 20;

    /// <summary>Whether to auto-trigger AI analysis when a new paragraph is transcribed.</summary>
    public bool AutoAnalyzeOnSpeech { get; set; }

    // Events
    public event Action<string, string>? TranscriptUpdated;  // (fullTranscript, latestParagraph)
    public event Action? AnalysisRequested;                   // fired when auto-analyze triggers
    public event Action<float>? VolumeChanged;
    public event Action<string>? StatusChanged;
    public event Action<int>? DownloadProgressChanged;

    public bool IsListening => _captureService.IsCapturing;
    public bool IsModelReady => _transcriptionService.IsInitialized;

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
                AnalysisRequested?.Invoke();
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"TRANSCRIPTION ERROR: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _captureService.ParagraphCompleted -= OnParagraphCompleted;
        _captureService.Dispose();
        _transcriptionService.Dispose();
    }
}
