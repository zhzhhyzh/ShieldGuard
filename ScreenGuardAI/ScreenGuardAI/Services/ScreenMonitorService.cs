using System.Drawing;
using ScreenGuardAI.Models;

namespace ScreenGuardAI.Services;

/// <summary>
/// Monitors the screen periodically and uses the AI model to detect LeetCode/coding problems.
/// Instead of local OCR, sends a lightweight prompt to the AI asking if a coding question is visible.
/// When detected, fires an event to auto-trigger the full AI analysis.
/// </summary>
public class ScreenMonitorService : IDisposable
{
    private readonly ScreenCaptureService _captureService;
    private readonly AIProviderService _aiService;
    private readonly SettingsService _settingsService;
    private System.Threading.Timer? _scanTimer;
    private bool _isRunning;
    private bool _isScanning;
    private string _lastDetectedTitle = string.Empty;
    private DateTime _lastTriggerTime = DateTime.MinValue;
    private readonly object _lock = new();

    // Configurable scan interval (seconds)
    public int ScanIntervalSeconds { get; set; } = 5;

    // Minimum seconds between auto-triggers (debounce)
    public int CooldownSeconds { get; set; } = 20;

    // Events
    public event Action<string>? LeetCodeDetected;
    public event Action<string>? StatusChanged;

    public bool IsRunning => _isRunning;

    public ScreenMonitorService(ScreenCaptureService captureService, AIProviderService aiService, SettingsService settingsService)
    {
        _captureService = captureService;
        _aiService = aiService;
        _settingsService = settingsService;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _lastDetectedTitle = string.Empty;

        _scanTimer = new System.Threading.Timer(
            async _ => await ScanScreen(),
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(ScanIntervalSeconds));

        StatusChanged?.Invoke("AUTO-DETECT ON :: scanning...");
    }

    public void Stop()
    {
        _isRunning = false;
        _scanTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _scanTimer?.Dispose();
        _scanTimer = null;
        StatusChanged?.Invoke("AUTO-DETECT OFF");
    }

    private async Task ScanScreen()
    {
        if (!_isRunning || _isScanning) return;

        lock (_lock)
        {
            if ((DateTime.Now - _lastTriggerTime).TotalSeconds < CooldownSeconds) return;
            _isScanning = true;
        }

        try
        {
            // Capture screen and convert to base64
            using var bitmap = _captureService.CapturePrimaryScreen();
            var base64 = _captureService.BitmapToBase64(bitmap);

            // Get provider settings
            var providerKey = _settingsService.Settings.SelectedProvider;
            var activeConfig = _settingsService.Settings.GetActiveProvider();
            var apiKey = activeConfig.ApiKey;

            if (string.IsNullOrWhiteSpace(apiKey)) return;

            StatusChanged?.Invoke($"SCANNING... (img size: {base64.Length / 1024}KB)");

            // Ask the AI a simple yes/no detection question (cheap, fast)
            var result = await _aiService.DetectCodingProblemAsync(base64, providerKey, apiKey, activeConfig.Model);

            if (result.IsDetected)
            {
                // Check if it's a new problem (different title)
                lock (_lock)
                {
                    if (result.Title == _lastDetectedTitle) return;
                    _lastDetectedTitle = result.Title;
                    _lastTriggerTime = DateTime.Now;
                }

                StatusChanged?.Invoke($"DETECTED: {result.Title}");
                LeetCodeDetected?.Invoke(result.Title);
            }
            else if (!string.IsNullOrEmpty(result.Error))
            {
                StatusChanged?.Invoke($"SCAN ERROR: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"SCAN EXCEPTION: {ex.Message}");
        }
        finally
        {
            _isScanning = false;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
