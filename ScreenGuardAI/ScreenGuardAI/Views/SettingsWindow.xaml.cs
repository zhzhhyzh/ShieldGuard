using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenGuardAI.Helpers;
using ScreenGuardAI.Services;

namespace ScreenGuardAI.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly AIProviderService _aiService;
    private bool _isInitializing = true;

    // Store keys per provider so switching doesn't lose them
    private readonly Dictionary<string, string> _providerKeys = new();
    // Store failover checkbox references
    private readonly Dictionary<string, System.Windows.Controls.CheckBox> _failoverCheckBoxes = new();

    public SettingsWindow(SettingsService settingsService, AIProviderService aiService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _aiService = aiService;
        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                NativeMethods.HideFromAltTab(hwnd);
            }
        };

        // Pre-load all saved keys into dictionary
        var settings = _settingsService.Settings;
        _providerKeys["OpenAI"] = settings.OpenAI.ApiKey;
        _providerKeys["Gemini"] = settings.Gemini.ApiKey;
        _providerKeys["Groq"] = settings.Groq.ApiKey;
        _providerKeys["OpenRouter"] = settings.OpenRouter.ApiKey;

        // Populate provider dropdown
        foreach (var kvp in AIProviderService.Providers)
        {
            var def = kvp.Value;
            var label = def.FreeTier ? $"{def.Name} - {def.Description} (FREE)" : $"{def.Name} - {def.Description}";
            var item = new ComboBoxItem
            {
                Content = label,
                Tag = kvp.Key
            };
            ProviderCombo.Items.Add(item);
        }

        // Select current provider
        var selectedKey = settings.SelectedProvider;
        for (int i = 0; i < ProviderCombo.Items.Count; i++)
        {
            if (((ComboBoxItem)ProviderCombo.Items[i]).Tag?.ToString() == selectedKey)
            {
                ProviderCombo.SelectedIndex = i;
                break;
            }
        }

        if (ProviderCombo.SelectedIndex < 0 && ProviderCombo.Items.Count > 0)
            ProviderCombo.SelectedIndex = 0;

        _isInitializing = false;
        UpdateProviderUI();
        PopulateFailoverCheckboxes();
    }

    private void PopulateFailoverCheckboxes()
    {
        FailoverPanel.Children.Clear();
        _failoverCheckBoxes.Clear();

        var savedFailovers = _settingsService.Settings.FailoverProviders ?? new List<string>();

        foreach (var kvp in AIProviderService.Providers)
        {
            var providerKey = kvp.Key;
            var def = kvp.Value;

            var cb = new CheckBox
            {
                Content = $"  {def.Name}" + (def.FreeTier ? " (FREE)" : ""),
                Tag = providerKey,
                IsChecked = savedFailovers.Contains(providerKey),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00CC33")),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            _failoverCheckBoxes[providerKey] = cb;
            FailoverPanel.Children.Add(cb);
        }

        UpdateFailoverVisibility();
    }

    /// <summary>
    /// Disables the failover checkbox for the currently selected primary provider.
    /// </summary>
    private void UpdateFailoverVisibility()
    {
        var selectedKey = GetSelectedProviderKey();
        foreach (var kvp in _failoverCheckBoxes)
        {
            if (kvp.Key == selectedKey)
            {
                kvp.Value.IsEnabled = false;
                kvp.Value.IsChecked = false;
                kvp.Value.Opacity = 0.4;
            }
            else
            {
                kvp.Value.IsEnabled = true;
                kvp.Value.Opacity = 1.0;
            }
        }
    }

    private string GetSelectedProviderKey()
    {
        return (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Gemini";
    }

    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        // Save current key before switching
        if (e.RemovedItems.Count > 0)
        {
            var oldKey = (e.RemovedItems[0] as ComboBoxItem)?.Tag?.ToString();
            if (oldKey != null)
                _providerKeys[oldKey] = ApiKeyBox.Password;
        }

        UpdateProviderUI();
    }

    private void UpdateProviderUI()
    {
        if (ModelCombo == null || ProviderCombo?.SelectedItem == null) return;

        var providerKey = GetSelectedProviderKey();
        if (!AIProviderService.Providers.TryGetValue(providerKey, out var provider)) return;

        // Update info text
        ProviderInfoText.Text = provider.Description;
        GetKeyLinkText.Text = $"Get {provider.Name} API Key";
        ApiKeyLabel.Text = $"{provider.Name} API Key";

        // Show/hide free badge
        FreeBadge.Visibility = provider.FreeTier ? Visibility.Visible : Visibility.Collapsed;

        // Load saved key for this provider
        ApiKeyBox.Password = _providerKeys.GetValueOrDefault(providerKey, "");

        // Populate models
        ModelCombo.Items.Clear();
        var currentModel = providerKey switch
        {
            "OpenAI" => _settingsService.Settings.OpenAI.Model,
            "Gemini" => _settingsService.Settings.Gemini.Model,
            "Groq" => _settingsService.Settings.Groq.Model,
            "OpenRouter" => _settingsService.Settings.OpenRouter.Model,
            _ => provider.DefaultModel
        };

        foreach (var model in provider.Models)
        {
            var item = new ComboBoxItem { Content = model };
            if (model == currentModel)
                item.IsSelected = true;
            ModelCombo.Items.Add(item);
        }

        if (ModelCombo.SelectedItem == null && ModelCombo.Items.Count > 0)
            ((ComboBoxItem)ModelCombo.Items[0]!).IsSelected = true;

        StatusText.Text = "";

        // Update failover checkboxes (disable current primary)
        if (_failoverCheckBoxes.Count > 0)
            UpdateFailoverVisibility();
    }

    private void GetKeyLink_Click(object sender, MouseButtonEventArgs e)
    {
        var providerKey = GetSelectedProviderKey();
        if (AIProviderService.Providers.TryGetValue(providerKey, out var provider))
        {
            try { Process.Start(new ProcessStartInfo(provider.KeyUrl) { UseShellExecute = true }); }
            catch { }
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Password;
        var providerKey = GetSelectedProviderKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetStatus($"Please enter an API key for {providerKey} first.", false);
            return;
        }

        SetStatus("Testing API key...", null);
        TestButton.IsEnabled = false;

        var (isValid, message) = await _aiService.TestApiKeyAsync(providerKey, apiKey);

        TestButton.IsEnabled = true;
        SetStatus(message, isValid);
    }

    private void SetStatus(string message, bool? success)
    {
        StatusText.Text = message;
        string color = success switch
        {
            true => "#00FF41",
            false => "#FF3333",
            null => "#00CC33"
        };
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var providerKey = GetSelectedProviderKey();

        // Save current key to dictionary
        _providerKeys[providerKey] = ApiKeyBox.Password;

        // Save selected provider
        _settingsService.Settings.SelectedProvider = providerKey;

        // Save failover providers (checked checkboxes, in order)
        var failovers = new List<string>();
        foreach (var kvp in _failoverCheckBoxes)
        {
            if (kvp.Key != providerKey && kvp.Value.IsChecked == true)
                failovers.Add(kvp.Key);
        }
        _settingsService.Settings.FailoverProviders = failovers;

        // Save all provider keys & selected model
        var selectedModel = (ModelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();

        _settingsService.Settings.OpenAI.ApiKey = _providerKeys.GetValueOrDefault("OpenAI", "");
        _settingsService.Settings.Gemini.ApiKey = _providerKeys.GetValueOrDefault("Gemini", "");
        _settingsService.Settings.Groq.ApiKey = _providerKeys.GetValueOrDefault("Groq", "");
        _settingsService.Settings.OpenRouter.ApiKey = _providerKeys.GetValueOrDefault("OpenRouter", "");

        // Save model for the currently selected provider
        if (!string.IsNullOrEmpty(selectedModel))
        {
            switch (providerKey)
            {
                case "OpenAI": _settingsService.Settings.OpenAI.Model = selectedModel; break;
                case "Gemini": _settingsService.Settings.Gemini.Model = selectedModel; break;
                case "Groq": _settingsService.Settings.Groq.Model = selectedModel; break;
                case "OpenRouter": _settingsService.Settings.OpenRouter.Model = selectedModel; break;
            }
        }

        _settingsService.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
