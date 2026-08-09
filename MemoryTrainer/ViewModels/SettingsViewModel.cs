using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTrainer.Helpers;
using MemoryTrainer.Services;
using System.Windows.Forms;

namespace MemoryTrainer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly CleanupService _cleanupService;
    private readonly Action _onBack;

    [ObservableProperty] private string _defaultSessionNameTemplate = PathHelper.DefaultSessionNameTemplate;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _screenshotsFolder = PathHelper.ScreenshotsPath;
    [ObservableProperty] private int _planRevealDelaySeconds = PathHelper.PlanRevealDelaySeconds;
    [ObservableProperty] private int _minRecallCharacters = PathHelper.MinRecallCharacters;

    public SettingsViewModel(CleanupService cleanupService, Action onBack)
    {
        _cleanupService = cleanupService;
        _onBack = onBack;
        _screenshotsFolder = PathHelper.ScreenshotsPath;
        _defaultSessionNameTemplate = PathHelper.DefaultSessionNameTemplate;
        _planRevealDelaySeconds = PathHelper.PlanRevealDelaySeconds;
        _minRecallCharacters = PathHelper.MinRecallCharacters;
    }

    [RelayCommand]
    private void BrowseScreenshotsFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select screenshots folder",
            SelectedPath = ScreenshotsFolder,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            ScreenshotsFolder = dialog.SelectedPath;
    }

    [RelayCommand]
    private void SaveScreenshotsFolder()
    {
        if (string.IsNullOrWhiteSpace(ScreenshotsFolder)) return;
        PathHelper.ScreenshotsPath = ScreenshotsFolder;
        StatusMessage = "Screenshots folder saved.";
    }

    [RelayCommand]
    private void SaveSessionNameTemplate()
    {
        if (string.IsNullOrWhiteSpace(DefaultSessionNameTemplate)) return;
        PathHelper.DefaultSessionNameTemplate = DefaultSessionNameTemplate;
        StatusMessage = "Session name template saved.";
    }

    [RelayCommand]
    private void SavePlanRevealDelay()
    {
        if (PlanRevealDelaySeconds < 1) return;
        PathHelper.PlanRevealDelaySeconds = PlanRevealDelaySeconds;
        StatusMessage = "Plan reveal delay saved.";
    }

    [RelayCommand]
    private void SaveMinRecallCharacters()
    {
        if (MinRecallCharacters < 0) return;
        PathHelper.MinRecallCharacters = MinRecallCharacters;
        StatusMessage = "Minimum recall length saved.";
    }

    [RelayCommand]
    private async Task DeleteAllCapturedMediaAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "This will delete all screenshot, audio, and camera files. Score history is not affected.",
            "Delete All Captured Media",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        await _cleanupService.DeleteAllCapturedMediaAsync();
        StatusMessage = "All captured media deleted.";
    }

    [RelayCommand]
    private void GoBack() => _onBack();
}
