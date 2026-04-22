using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenGuardAI.Helpers;
using ScreenGuardAI.Models;
using ScreenGuardAI.Services;

namespace ScreenGuardAI.Views;

public partial class OverlayWindow : Window
{
    private string _responseContent = string.Empty;
    private string _codeContent = string.Empty;
    private InterviewMode _currentMode;
    private bool _shielded;

    public OverlayWindow()
    {
        InitializeComponent();
        PositionWindow();
        Loaded += (_, _) => ApplyShield();
        IsVisibleChanged += (_, _) => { if (IsVisible) ApplyShield(); };
    }

    /// <summary>
    /// Applies capture protection so this window is invisible to screen capture/recording.
    /// </summary>
    private void ApplyShield()
    {
        if (_shielded) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            NativeMethods.HideFromAltTab(hwnd);
            _shielded = true;
        }
    }

    /// <summary>
    /// Shows a loading state with mode-specific message.
    /// </summary>
    public void ShowLoading(InterviewMode mode)
    {
        _currentMode = mode;
        UpdateModeBadge(mode);

        QuestionSection.Visibility = Visibility.Collapsed;
        CopyCodeBtn.Visibility = Visibility.Collapsed;

        if (mode == InterviewMode.Coding)
        {
            ResponseText.Text = "Reading coding problem and generating solution...";
        }
        else
        {
            ResponseText.Text = "Listening for the interview question...";
        }

        ResponseText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#00CC33"));
        TimestampText.Text = DateTime.Now.ToString("HH:mm:ss");
        Show();
        Activate();
    }

    /// <summary>
    /// Displays an AI response, parsing it based on interview mode.
    /// </summary>
    public void ShowResponse(AIResponse response, InterviewMode mode)
    {
        _currentMode = mode;
        UpdateModeBadge(mode);

        if (!response.Success)
        {
            _responseContent = response.ErrorMessage ?? "Unknown error";
            ResponseText.Text = _responseContent;
            ResponseText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#FF3333"));
            QuestionSection.Visibility = Visibility.Collapsed;
            CopyCodeBtn.Visibility = Visibility.Collapsed;
            TimestampText.Text = response.Timestamp.ToString("HH:mm:ss");
            Show();
            Activate();
            return;
        }

        _responseContent = response.Content;
        TimestampText.Text = response.Timestamp.ToString("HH:mm:ss");

        if (mode == InterviewMode.QA)
        {
            DisplayQAResponse(response.Content);
        }
        else
        {
            DisplayCodingResponse(response.Content);
        }

        Show();
        Activate();
    }

    private void DisplayQAResponse(string content)
    {
        CopyCodeBtn.Visibility = Visibility.Collapsed;

        // Try to extract QUESTION and ANSWER sections
        var questionMatch = Regex.Match(content, @"QUESTION:\s*(.+?)(?=\n\s*\n|ANSWER:)", RegexOptions.Singleline);
        var answerMatch = Regex.Match(content, @"ANSWER:\s*(.+)", RegexOptions.Singleline);

        if (questionMatch.Success && answerMatch.Success)
        {
            QuestionSection.Visibility = Visibility.Visible;
            DetectedQuestionText.Text = questionMatch.Groups[1].Value.Trim();
            ResponseText.Text = answerMatch.Groups[1].Value.Trim();
        }
        else
        {
            QuestionSection.Visibility = Visibility.Collapsed;
            ResponseText.Text = content;
        }

        ResponseText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#00DD33"));
        ResponseText.FontSize = 14;
        ResponseText.LineHeight = 24;
    }

    private void DisplayCodingResponse(string content)
    {
        QuestionSection.Visibility = Visibility.Collapsed;

        // Extract code block if present
        var codeMatch = Regex.Match(content, @"```[\w]*\n?(.*?)```", RegexOptions.Singleline);
        if (codeMatch.Success)
        {
            _codeContent = codeMatch.Groups[1].Value.Trim();
            CopyCodeBtn.Visibility = Visibility.Visible;
        }
        else
        {
            _codeContent = string.Empty;
            CopyCodeBtn.Visibility = Visibility.Collapsed;
        }

        ResponseText.Text = content;
        ResponseText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#00DD33"));
        ResponseText.FontSize = 12;
        ResponseText.LineHeight = 20;
        ResponseText.FontFamily = new FontFamily("Consolas, Segoe UI");
    }

    private void UpdateModeBadge(InterviewMode mode)
    {
        if (mode == InterviewMode.Coding)
        {
            ModeBadgeText.Text = "CODING";
            ModeBadge.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#00CC33"));
        }
        else
        {
            ModeBadgeText.Text = "Q&A";
            ModeBadge.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#00FF41"));
        }
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_responseContent))
        {
            Clipboard.SetText(_responseContent);
        }
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_codeContent))
        {
            Clipboard.SetText(_codeContent);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }
}
