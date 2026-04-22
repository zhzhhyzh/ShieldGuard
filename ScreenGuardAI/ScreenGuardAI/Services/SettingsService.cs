using System.IO;
using System.Text.Json;
using ScreenGuardAI.Models;

namespace ScreenGuardAI.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public AppSettings Settings => _settings;

    public SettingsService()
    {
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenGuardAI",
            "appsettings.json");
    }

    /// <summary>
    /// Loads settings from the user's AppData folder, or from the app directory as fallback.
    /// Handles migration from old format (Provider -> SelectedProvider).
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            string? json = null;
            if (File.Exists(_settingsPath))
            {
                json = File.ReadAllText(_settingsPath);
            }
            else
            {
                var appDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(appDirPath))
                    json = File.ReadAllText(appDirPath);
            }

            if (json != null)
            {
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                // Migrate from old "Provider" field if present
                if (json.Contains("\"Provider\"") && !json.Contains("\"SelectedProvider\""))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Provider", out var oldProvider))
                        _settings.SelectedProvider = oldProvider.GetString() ?? "Gemini";
                }
            }
        }
        catch
        {
            _settings = new AppSettings();
        }

        return _settings;
    }

    /// <summary>
    /// Saves the current settings to the user's AppData folder.
    /// </summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the API key and saves.
    /// </summary>
    public void UpdateApiKey(string apiKey)
    {
        _settings.OpenAI.ApiKey = apiKey;
        Save();
    }

    /// <summary>
    /// Updates the model and saves.
    /// </summary>
    public void UpdateModel(string model)
    {
        _settings.OpenAI.Model = model;
        Save();
    }
}
