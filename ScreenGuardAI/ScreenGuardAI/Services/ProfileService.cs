using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScreenGuardAI.Models;

namespace ScreenGuardAI.Services;

public class ProfileService
{
    private readonly string _profilePath;
    private UserProfile _profile = new();

    public UserProfile Profile => _profile;

    public ProfileService()
    {
        _profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenGuardAI",
            "profile.json");
    }

    public UserProfile Load()
    {
        try
        {
            if (File.Exists(_profilePath))
            {
                var json = File.ReadAllText(_profilePath);
                _profile = JsonSerializer.Deserialize<UserProfile>(json) ?? new UserProfile();
            }
        }
        catch
        {
            _profile = new UserProfile();
        }
        return _profile;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_profilePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_profilePath, json);
        }
        catch { }
    }

    public void UpdateProfile(UserProfile profile)
    {
        _profile = profile;
        Save();
    }

    /// <summary>
    /// Exports the profile as a Markdown file to the specified path.
    /// </summary>
    public void ExportToMarkdown(string filePath)
    {
        var md = _profile.ToMarkdown();
        File.WriteAllText(filePath, md);
    }

    /// <summary>
    /// Imports profile data from a Markdown file.
    /// Parses sections and populates profile fields.
    /// </summary>
    public UserProfile ImportFromMarkdown(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var profile = new UserProfile();

        profile.FullName = ExtractField(content, "Name:");
        profile.CurrentRole = ExtractField(content, "Current Role:");
        profile.YearsOfExperience = ExtractField(content, "Years of Experience:");
        profile.TargetRole = ExtractField(content, "Target Role:");
        profile.TechStack = ExtractSection(content, "## Technical Skills");
        profile.KeyProjects = ExtractSection(content, "## Key Projects & Achievements");
        profile.Education = ExtractSection(content, "## Education");
        profile.Strengths = ExtractSection(content, "## Strengths");
        profile.WorkStyle = ExtractSection(content, "## Work Style & Approach");
        profile.AdditionalNotes = ExtractSection(content, "## Additional Notes");

        _profile = profile;
        Save();
        return profile;
    }

    private static string ExtractField(string content, string fieldName)
    {
        var pattern = $@"\*\*{Regex.Escape(fieldName)}\*\*\s*(.+)";
        var match = Regex.Match(content, pattern);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string ExtractSection(string content, string sectionHeader)
    {
        var escapedHeader = Regex.Escape(sectionHeader);
        var pattern = $@"{escapedHeader}\s*\n([\s\S]*?)(?=\n##|\n---|\z)";
        var match = Regex.Match(content, pattern);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    /// <summary>
    /// Gets the profile context string for injecting into AI prompts.
    /// Returns empty string if no profile data exists.
    /// </summary>
    public string GetPromptContext()
    {
        return _profile.HasData() ? _profile.ToPromptContext() : string.Empty;
    }
}
