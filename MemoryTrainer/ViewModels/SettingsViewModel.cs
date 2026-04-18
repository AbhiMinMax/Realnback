using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTrainer.Helpers;
using MemoryTrainer.Services;

namespace MemoryTrainer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly CleanupService _cleanupService;
    private readonly Action _onBack;

    [ObservableProperty] private string _defaultSessionNameTemplate = "Session — {date} {time}";
    [ObservableProperty] private string _statusMessage = string.Empty;

    public SettingsViewModel(CleanupService cleanupService, Action onBack)
    {
        _cleanupService = cleanupService;
        _onBack = onBack;
    }

    [RelayCommand]
    private async Task DeleteAllScreenshotsAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "This will delete all screenshot files. Score history is not affected.",
            "Delete All Screenshots",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        await _cleanupService.DeleteAllScreenshotsAsync(PathHelper.ScreenshotsPath);
        StatusMessage = "All screenshots deleted.";
    }

    [RelayCommand]
    private void GoBack() => _onBack();
}
