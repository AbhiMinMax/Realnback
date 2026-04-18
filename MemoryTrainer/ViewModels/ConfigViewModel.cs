using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTrainer.Services;
using System.Collections.ObjectModel;

namespace MemoryTrainer.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly SessionEngine _engine;
    private readonly Action _onSessionStarted;

    [ObservableProperty] private string _sessionName = string.Empty;

    public ObservableCollection<SessionCycleConfigViewModel> Cycles { get; } = new();

    public bool CanStart => Cycles.Count > 0 && Cycles.All(c => c.IsValid);

    public ConfigViewModel(SessionEngine engine, Action onSessionStarted)
    {
        _engine = engine;
        _onSessionStarted = onSessionStarted;
        AddCycle();
    }

    [RelayCommand]
    private void AddCycle()
    {
        var vm = new SessionCycleConfigViewModel();
        vm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanStart));
        Cycles.Add(vm);
        OnPropertyChanged(nameof(CanStart));
    }

    [RelayCommand]
    private void RemoveCycle(SessionCycleConfigViewModel cycle)
    {
        Cycles.Remove(cycle);
        OnPropertyChanged(nameof(CanStart));
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartSessionAsync()
    {
        var name = string.IsNullOrWhiteSpace(SessionName)
            ? $"Session — {DateTime.Now:yyyy-MM-dd HH:mm}"
            : SessionName.Trim();

        var cycles = Cycles.Select(c => c.ToModel()).ToList();
        await _engine.StartSessionAsync(name, cycles);
        _onSessionStarted();
    }

    partial void OnSessionNameChanged(string value) => OnPropertyChanged(nameof(CanStart));
}
