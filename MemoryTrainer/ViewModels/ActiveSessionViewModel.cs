using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTrainer.Services;
using System.Collections.ObjectModel;

namespace MemoryTrainer.ViewModels;

public partial class ActiveSessionViewModel : ObservableObject, IDisposable
{
    private readonly SessionEngine _engine;
    private readonly Action _onSessionStopped;

    [ObservableProperty] private string _sessionName = string.Empty;
    [ObservableProperty] private string _sessionStartTime = string.Empty;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private int _queuedPromptCount;

    public ObservableCollection<CycleStatusViewModel> CycleStatuses { get; } = new();

    public ActiveSessionViewModel(SessionEngine engine, Action onSessionStopped)
    {
        _engine = engine;
        _onSessionStopped = onSessionStopped;
        engine.SessionStateChanged += OnSessionStateChanged;
        Refresh();
    }

    private void Refresh()
    {
        var session = _engine.ActiveSession;
        if (session == null) return;

        SessionName = session.Name;
        SessionStartTime = session.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        IsPaused = session.IsPaused;
        QueuedPromptCount = _engine.QueuedPromptCount;

        // Rebuild cycle status VMs if runners changed
        if (CycleStatuses.Count != _engine.Runners.Count)
        {
            foreach (var vm in CycleStatuses) vm.Dispose();
            CycleStatuses.Clear();
            foreach (var runner in _engine.Runners)
                CycleStatuses.Add(new CycleStatusViewModel(runner));
        }
    }

    private void OnSessionStateChanged()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            IsPaused = _engine.IsPaused;
            QueuedPromptCount = _engine.QueuedPromptCount;
        });
    }

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (_engine.IsPaused)
            await _engine.ResumeAsync();
        else
            await _engine.PauseAsync();
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "Stop session? All scores are saved.", "Stop Session",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        await _engine.StopAsync();
        _onSessionStopped();
    }

    public void Dispose()
    {
        _engine.SessionStateChanged -= OnSessionStateChanged;
        foreach (var vm in CycleStatuses) vm.Dispose();
    }
}
