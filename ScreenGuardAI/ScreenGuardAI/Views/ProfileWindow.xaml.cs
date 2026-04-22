using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using ScreenGuardAI.Helpers;
using ScreenGuardAI.Models;
using ScreenGuardAI.Services;

namespace ScreenGuardAI.Views;

public partial class ProfileWindow : Window
{
    private readonly ProfileService _profileService;

    public ProfileWindow(ProfileService profileService)
    {
        InitializeComponent();
        _profileService = profileService;
        LoadProfileToUI(_profileService.Profile);
        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                NativeMethods.HideFromAltTab(hwnd);
            }
        };
    }

    private void LoadProfileToUI(UserProfile profile)
    {
        NameBox.Text = profile.FullName;
        CurrentRoleBox.Text = profile.CurrentRole;
        ExperienceBox.Text = profile.YearsOfExperience;
        TargetRoleBox.Text = profile.TargetRole;
        TechStackBox.Text = profile.TechStack;
        ProjectsBox.Text = profile.KeyProjects;
        EducationBox.Text = profile.Education;
        StrengthsBox.Text = profile.Strengths;
        WorkStyleBox.Text = profile.WorkStyle;
        NotesBox.Text = profile.AdditionalNotes;
    }

    private UserProfile CollectFromUI()
    {
        return new UserProfile
        {
            FullName = NameBox.Text.Trim(),
            CurrentRole = CurrentRoleBox.Text.Trim(),
            YearsOfExperience = ExperienceBox.Text.Trim(),
            TargetRole = TargetRoleBox.Text.Trim(),
            TechStack = TechStackBox.Text.Trim(),
            KeyProjects = ProjectsBox.Text.Trim(),
            Education = EducationBox.Text.Trim(),
            Strengths = StrengthsBox.Text.Trim(),
            WorkStyle = WorkStyleBox.Text.Trim(),
            AdditionalNotes = NotesBox.Text.Trim()
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var profile = CollectFromUI();
        _profileService.UpdateProfile(profile);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        // Save current form data first
        var profile = CollectFromUI();
        _profileService.UpdateProfile(profile);

        var dialog = new SaveFileDialog
        {
            Title = "Export Interview Profile",
            Filter = "Markdown Files (*.md)|*.md",
            FileName = "interview-profile.md",
            DefaultExt = ".md"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _profileService.ExportToMarkdown(dialog.FileName);
                MessageBox.Show($"Profile exported to:\n{dialog.FileName}\n\nYou can import this file on any device running ScreenGuard AI.",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Interview Profile",
            Filter = "Markdown Files (*.md)|*.md|All Files (*.*)|*.*",
            DefaultExt = ".md"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var profile = _profileService.ImportFromMarkdown(dialog.FileName);
                LoadProfileToUI(profile);
                MessageBox.Show("Profile imported successfully!", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Clear all profile fields?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            LoadProfileToUI(new UserProfile());
        }
    }
}
