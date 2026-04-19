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

    [ObservableProperty] private string _defaultSessionNameTemplate = "Session — {date} {time}";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _screenshotsFolder = PathHelper.ScreenshotsPath;

    public SettingsViewModel(CleanupService cleanupService, Action onBack)
    {
        _cleanupService = cleanupService;
        _onBack = onBack;
        _screenshotsFolder = PathHelper.ScreenshotsPath;
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
