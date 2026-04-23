using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenGuardAI.Services;
using ScreenGuardAI.Helpers;
using ScreenGuardAI.Views;

namespace ScreenGuardAI;

public partial class MainWindow : Window
{
    private readonly CaptureProtectionService _protectionService = new();
    private readonly ScreenCaptureService _captureService = new();
    private readonly HotkeyService _hotkeyService = new();
    private readonly AIProviderService _aiService = new();
    private readonly SettingsService _settingsService = new();
    private readonly ProfileService _profileService = new();
    private ScreenMonitorService? _monitorService;
    private AudioCaptureService? _audioCaptureService;
    private TranscriptionService? _transcriptionService;
    private AudioTranscriptManager? _audioManager;
    private OverlayWindow? _overlayWindow;
    private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? _trayIcon;
    private bool _isAnalyzing;
    private bool _forceClose;
    private InterviewMode _currentMode = InterviewMode.QA;
    private string _lastResponse = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Load settings
        _settingsService.Load();
        _profileService.Load();

        // Restore interview mode from settings
        if (_settingsService.Settings.InterviewMode == "Coding")
        {
            CodingModeRadio.IsChecked = true;
            _currentMode = InterviewMode.Coding;
        }

        // Register global hotkey
        bool hotkeyRegistered = _hotkeyService.Register(this);
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        if (hotkeyRegistered)
        {
            StatusBarText.Text = "Ready - Ctrl+Shift+Q registered";
        }
        else
        {
            StatusBarText.Text = "Warning: Hotkey Ctrl+Shift+Q could not be registered";
            StatusBarText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#FF3333"));
        }

        // Auto-enable capture protection (always on, no toggle)
        var hwnd = new WindowInteropHelper(this).Handle;
        _protectionService.EnableProtection(hwnd);
        NativeMethods.HideFromAltTab(hwnd);

        // Setup tray icon
        SetupTrayIcon();

        // Check if API key is configured for the selected provider
        var provider = _settingsService.Settings.SelectedProvider;
        var activeConfig = _settingsService.Settings.GetActiveProvider();
        bool hasKey = !string.IsNullOrWhiteSpace(activeConfig.ApiKey);

        if (!hasKey)
        {
            var providerName = AIProviderService.Providers.TryGetValue(provider, out var def) ? def.Name : provider;
            StatusBarText.Text = $"Please set your {providerName} API key in Settings first";
            StatusBarText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#FF6B00"));
        }

        UpdateModeDescription();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon
        {
            ToolTipText = "ScreenGuard AI - Interview Assistant",
            Icon = GenerateTrayIcon(),
        };

        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show Window" };
        showItem.Click += (s, e) => { Show(); WindowState = WindowState.Normal; Activate(); };

        var captureItem = new System.Windows.Controls.MenuItem { Header = "Capture & Analyze" };
        captureItem.Click += (s, e) => _ = PerformAIAnalysis();

        var qaItem = new System.Windows.Controls.MenuItem { Header = "Switch to Q&A Mode" };
        qaItem.Click += (s, e) => Dispatcher.Invoke(() => { QAModeRadio.IsChecked = true; });

        var codingItem = new System.Windows.Controls.MenuItem { Header = "Switch to Coding Mode" };
        codingItem.Click += (s, e) => Dispatcher.Invoke(() => { CodingModeRadio.IsChecked = true; });

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (s, e) => Dispatcher.Invoke(() => Settings_Click(s, e));

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (s, e) =>
        {
            _forceClose = true;
            Dispatcher.Invoke(() => Close());
        };

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(captureItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(qaItem);
        contextMenu.Items.Add(codingItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;
        _trayIcon.TrayMouseDoubleClick += (s, e) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
    }

    private void OnHotkeyPressed()
    {
        Dispatcher.Invoke(() => _ = PerformAIAnalysis());
    }

    private async Task PerformAIAnalysis()
    {
        if (_isAnalyzing) return;
        _isAnalyzing = true;

        var providerKey = _settingsService.Settings.SelectedProvider;
        var activeConfig = _settingsService.Settings.GetActiveProvider();
        var apiKey = activeConfig.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var providerName = AIProviderService.Providers.TryGetValue(providerKey, out var def) ? def.Name : providerKey;
            MessageBox.Show($"Please configure your {providerName} API key in Settings first.",
                "ScreenGuard AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            _isAnalyzing = false;
            return;
        }

        // Update UI for loading state
        CaptureButton.IsEnabled = false;
        CaptureButton.Content = "Analyzing...";
        StatusBarText.Text = "Capturing screen...";
        StatusBarText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#00CC33"));
        LastResponseText.Text = "Capturing and analyzing...";
        LastResponseText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#00CC33"));

        // IMPORTANT: Hide the overlay BEFORE capturing so it doesn't appear in the screenshot.
        // This is critical for meeting scenarios where the overlay would cover the question.
        if (_overlayWindow != null && _overlayWindow.IsVisible)
            _overlayWindow.Hide();

        // Brief delay to let the overlay fully disappear and let the meeting app render
        await Task.Delay(250);

        try
        {
            // Capture the primary screen (captures what's visible including Zoom/Meet/Teams)
            using var bitmap = _captureService.CapturePrimaryScreen();
            var base64 = _captureService.BitmapToBase64(bitmap);

            // NOW show overlay with loading state (after capture is done)
            EnsureOverlayWindow();
            _overlayWindow!.ShowLoading(_currentMode);

            StatusBarText.Text = "Sending to AI...";

            // Get optional context + profile context + audio transcript
            string? context = null;
            var profileContext = _profileService.GetPromptContext();
            var extraContext = QuestionBox.Text?.Trim();
            var audioTranscript = _audioManager?.GetRecentTranscript(5);

            var contextParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(profileContext))
                contextParts.Add($"INTERVIEWEE PROFILE:\n{profileContext}");
            if (!string.IsNullOrWhiteSpace(audioTranscript))
                contextParts.Add($"RECENT MEETING AUDIO TRANSCRIPT (what was spoken):\n{audioTranscript}");
            if (!string.IsNullOrWhiteSpace(extraContext))
                contextParts.Add($"Additional context: {extraContext}");

            if (contextParts.Count > 0)
                context = string.Join("\n\n", contextParts);

            // Call the AI provider
            Models.AIResponse response;
            var model = activeConfig.Model;
            response = _currentMode == InterviewMode.Coding
                ? await _aiService.AnalyzeCodingPracticalAsync(base64, providerKey, apiKey, model, context)
                : await _aiService.AnalyzeInterviewQAAsync(base64, providerKey, apiKey, model, context);

            // Display in overlay
            _overlayWindow.ShowResponse(response, _currentMode);

            // Also update main window
            if (response.Success)
            {
                _lastResponse = response.Content;
                LastResponseText.Text = response.Content;
                LastResponseText.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#00DD33"));
                ResponseHeaderText.Text = _currentMode == InterviewMode.Coding
                    ? "Code Solution" : "Interview Answer";
                CopyResponseBtn.Visibility = Visibility.Visible;
                StatusBarText.Text = $"Done at {response.Timestamp:HH:mm:ss}";
                StatusBarText.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#00FF41"));
            }
            else
            {
                LastResponseText.Text = response.ErrorMessage ?? "Unknown error";
                LastResponseText.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#FF3333"));
                StatusBarText.Text = "Analysis failed";
                StatusBarText.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#FF3333"));
            }
        }
        catch (Exception ex)
        {
            LastResponseText.Text = $"Error: {ex.Message}";
            LastResponseText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#FF3333"));
            StatusBarText.Text = "Error occurred";
        }
        finally
        {
            _isAnalyzing = false;
            CaptureButton.IsEnabled = true;
            CaptureButton.Content = "Capture & Analyze";
        }
    }

    private void EnsureOverlayWindow()
    {
        if (_overlayWindow == null || !_overlayWindow.IsLoaded)
        {
            _overlayWindow = new OverlayWindow();
        }
    }

    private void UpdateModeDescription()
    {
        // Guard against being called before XAML controls are initialized
        if (ModeDescriptionText == null) return;

        if (_currentMode == InterviewMode.Coding)
        {
            ModeDescriptionText.Text = "> Captures meeting screen, reads the coding problem, generates complete code + explanation";
        }
        else
        {
            ModeDescriptionText.Text = "> Captures meeting screen (Zoom/Meet/Teams), finds question from captions/chat/shared content";
        }
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        // Guard against XAML initialization triggering this before controls exist
        if (!IsLoaded) return;

        if (CodingModeRadio?.IsChecked == true)
        {
            _currentMode = InterviewMode.Coding;
            _settingsService.Settings.InterviewMode = "Coding";
        }
        else
        {
            _currentMode = InterviewMode.QA;
            _settingsService.Settings.InterviewMode = "QA";
        }
        _settingsService.Save();
        UpdateModeDescription();
    }

    private void AskAI_Click(object sender, RoutedEventArgs e)
    {
        _ = PerformAIAnalysis();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settingsService, _aiService)
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        var profileWindow = new ProfileWindow(_profileService)
        {
            Owner = this
        };
        profileWindow.ShowDialog();
    }

    private void CopyResponse_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastResponse))
        {
            Clipboard.SetText(_lastResponse);
            StatusBarText.Text = "Copied to clipboard!";
            StatusBarText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#00FF41"));
        }
    }

    // --- Auto-Detect LeetCode ---

    private void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorService != null && _monitorService.IsRunning)
        {
            StopAutoDetect();
        }
        else
        {
            StartAutoDetect();
        }
    }

    private void StartAutoDetect()
    {
        // Ensure we have an API key before starting
        var activeConfig = _settingsService.Settings.GetActiveProvider();
        if (string.IsNullOrWhiteSpace(activeConfig.ApiKey))
        {
            MessageBox.Show("Please configure your API key in Settings before using auto-detect.",
                "ScreenGuard AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Force Coding mode for LeetCode
        CodingModeRadio.IsChecked = true;
        _currentMode = InterviewMode.Coding;

        _monitorService = new ScreenMonitorService(_captureService, _aiService, _settingsService);

        _monitorService.LeetCodeDetected += (title) =>
        {
            Dispatcher.Invoke(() =>
            {
                StatusBarText.Text = $"AUTO: Detected \"{title}\" - analyzing...";
                StatusBarText.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#00FF41"));
                _ = PerformAIAnalysis();
            });
        };

        _monitorService.StatusChanged += (status) =>
        {
            Dispatcher.Invoke(() =>
            {
                StatusBarText.Text = status;
                StatusBarText.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#00CC33"));
            });
        };

        _monitorService.Start();

        // Update UI
        AutoDetectBtn.Content = "[ ON ]";
        AutoDetectBtn.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#00FF41"));
        AutoDetectBtn.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#0D2B0D"));
        AutoDetectBtn.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#1A5A1A"));
        AutoDetectIndicator.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#00FF41"));
    }

    private void StopAutoDetect()
    {
        _monitorService?.Stop();
        _monitorService?.Dispose();
        _monitorService = null;

        // Update UI
        AutoDetectBtn.Content = "[ OFF ]";
        AutoDetectBtn.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#555555"));
        AutoDetectBtn.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#141414"));
        AutoDetectBtn.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#1A3A1A"));
        AutoDetectIndicator.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#555555"));

        StatusBarText.Text = "Auto-detect stopped";
        StatusBarText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#1A6B1A"));
    }

    // --- Audio Listen (Whisper STT) ---

    private async void Listen_Click(object sender, RoutedEventArgs e)
    {
        if (_audioManager != null && _audioManager.IsListening)
        {
            StopListening();
        }
        else
        {
            await StartListeningAsync();
        }
    }

    private async Task StartListeningAsync()
    {
        try
        {
            ListenBtn.IsEnabled = false;
            ListenBtn.Content = "[ LOADING ]";
            StatusBarText.Text = "Initializing Whisper model...";
            StatusBarText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#00CC33"));

            // Create services on first use
            if (_audioManager == null)
            {
                _audioCaptureService = new AudioCaptureService();
                _transcriptionService = new TranscriptionService();
                _audioManager = new AudioTranscriptManager(_audioCaptureService, _transcriptionService);

                // Apply settings
                var audioSettings = _settingsService.Settings.Audio;
                _audioCaptureService.SilenceTimeoutMs = audioSettings.SilenceThresholdMs;
                _audioCaptureService.SilenceThreshold = audioSettings.SilenceLevel;
                _audioManager.AutoAnalyzeOnSpeech = audioSettings.AutoAnalyzeOnSpeech;

                // Wire events
                _audioManager.TranscriptUpdated += (fullTranscript, latest) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TranscriptText.Text = fullTranscript;
                        TranscriptBorder.Visibility = Visibility.Visible;
                    });
                };

                _audioManager.VolumeChanged += (level) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        double width = Math.Min(level * 400, 40); // scale to max 40px
                        VolumeBar.Width = width;
                    });
                };

                _audioManager.StatusChanged += (status) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusBarText.Text = status;
                        StatusBarText.Foreground = new SolidColorBrush(
                            (Color)ColorConverter.ConvertFromString("#00CC33"));
                    });
                };

                _audioManager.DownloadProgressChanged += (mb) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusBarText.Text = $"Downloading Whisper model... {mb} MB";
                    });
                };

                _audioManager.AnalysisRequested += () =>
                {
                    Dispatcher.Invoke(() => _ = PerformAIAnalysis());
                };
            }

            await _audioManager.StartListeningAsync();

            // Update UI
            ListenBtn.Content = "[ ON ]";
            ListenBtn.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#00FF41"));
            ListenBtn.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#0D2B0D"));
            ListenBtn.BorderBrush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#1A5A1A"));
            AudioIndicator.Fill = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#00FF41"));
            TranscriptBorder.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            StatusBarText.Text = $"Audio error: {ex.Message}";
            StatusBarText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#FF3333"));
        }
        finally
        {
            ListenBtn.IsEnabled = true;
        }
    }

    private void StopListening()
    {
        _audioManager?.StopListening();

        // Update UI
        ListenBtn.Content = "[ OFF ]";
        ListenBtn.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#555555"));
        ListenBtn.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#141414"));
        ListenBtn.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#1A3A1A"));
        AudioIndicator.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#555555"));
        VolumeBar.Width = 0;

        StatusBarText.Text = "Audio listening stopped";
        StatusBarText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#1A6B1A"));
    }

    private void ClearTranscript_Click(object sender, RoutedEventArgs e)
    {
        _audioManager?.ClearTranscript();
        TranscriptText.Text = "";
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _trayIcon?.ShowBalloonTip("ScreenGuard AI",
            "Running in background. Press Ctrl+Shift+Q to capture & analyze.",
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Cleanup
        _audioManager?.Dispose();
        _monitorService?.Dispose();
        _hotkeyService.Dispose();
        _overlayWindow?.Close();
        _trayIcon?.Dispose();
    }

    /// <summary>
    /// Generates a 32x32 green shield icon programmatically for the system tray.
    /// No external .ico file needed.
    /// </summary>
    private static System.Drawing.Icon GenerateTrayIcon()
    {
        const int size = 32;
        using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        var green = System.Drawing.Color.FromArgb(0, 255, 65);       // #00FF41
        var darkGreen = System.Drawing.Color.FromArgb(0, 180, 40);

        // Shield shape
        var shieldPath = new System.Drawing.Drawing2D.GraphicsPath();
        shieldPath.AddLines(new System.Drawing.PointF[]
        {
            new(16, 2),    // top center
            new(28, 7),    // top right
            new(27, 19),   // mid right
            new(16, 29),   // bottom center (point)
            new(5, 19),    // mid left
            new(4, 7),     // top left
        });
        shieldPath.CloseFigure();

        // Fill with gradient
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new System.Drawing.Rectangle(0, 0, size, size),
            green, darkGreen,
            System.Drawing.Drawing2D.LinearGradientMode.Vertical);
        g.FillPath(brush, shieldPath);

        // Shield border
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(200, 0, 220, 50), 1.5f);
        g.DrawPath(pen, shieldPath);

        // "AI" text in the center
        using var font = new System.Drawing.Font("Consolas", 9f, System.Drawing.FontStyle.Bold);
        using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(10, 10, 10));
        var textSize = g.MeasureString("AI", font);
        g.DrawString("AI", font, textBrush,
            (size - textSize.Width) / 2,
            (size - textSize.Height) / 2 + 1);

        // Convert bitmap to icon
        var hIcon = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(hIcon);
    }
}
