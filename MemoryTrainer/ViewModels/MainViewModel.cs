using CommunityToolkit.Mvvm.ComponentModel;
using MemoryTrainer.Models;
using MemoryTrainer.Services;

namespace MemoryTrainer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SessionEngine _engine;
    private readonly DatabaseService _db;
    private readonly CleanupService _cleanupService;

    [ObservableProperty] private object? _currentView;

    public MainViewModel(SessionEngine engine, DatabaseService db, CleanupService cleanupService)
    {
        _engine = engine;
        _db = db;
        _cleanupService = cleanupService;

        _engine.ShowPromptRequested += ShowRecallPrompt;
    }

    public void ShowConfig()
    {
        CurrentView = new ConfigViewModel(_engine, () =>
        {
            ShowActiveSession();
        });
    }

    public void ShowActiveSession()
    {
        CurrentView = new ActiveSessionViewModel(_engine, () =>
        {
            ShowConfig();
        });
    }

    public void ShowHistory()
    {
        CurrentView = new HistoryViewModel(_db, () => ShowConfig());
    }

    public void ShowSettings()
    {
        CurrentView = new SettingsViewModel(_cleanupService, () => ShowConfig());
    }

    public void OfferSessionRestore(SessionModel session)
    {
        var result = System.Windows.MessageBox.Show(
            $"You have an active session from {session.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}. Resume it?",
            "Resume Session",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _ = _engine.RestoreSessionAsync(session).ContinueWith(_ =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(ShowActiveSession);
            });
        }
        else
        {
            _ = _db.AbandonSessionAsync(session.Id);
            ShowConfig();
        }
    }

    private void ShowRecallPrompt(CycleRecord record)
    {
        _ = LoadAndShowPromptAsync(record);
    }

    private async Task LoadAndShowPromptAsync(CycleRecord record)
    {
        var configs = await _db.GetConfigsBySessionAsync(_engine.ActiveSession!.Id);
        var config = configs.FirstOrDefault(c => c.Id == record.SessionCycleConfigId);
        if (config == null) return;

        var vm = new RecallPromptViewModel(record, config, _db, _engine);
        var window = new Views.RecallPromptWindow(vm);
        window.Show();
    }
}
