using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTrainer.Models;
using System.Collections.ObjectModel;

namespace MemoryTrainer.ViewModels;

public partial class DecoyOffsetViewModel : ObservableObject
{
    [ObservableProperty] private int _offsetMinutes;
    [ObservableProperty] private string _sign = "+";

    public int ActualOffsetMinutes => Sign == "-" ? -Math.Abs(OffsetMinutes) : Math.Abs(OffsetMinutes);
}

public partial class SessionCycleConfigViewModel : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValid))] private int _durationDays;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValid))] private int _durationHours;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValid))] private int _durationMinutes = 15;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValid))] private int _waitingWindowMinutes = 5;
    [ObservableProperty] private int _driftMinutes;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValid))] private bool _freeRecallEnabled = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValid))] private bool _recognitionEnabled = true;
    [ObservableProperty] private bool _audioEnabled;
    [ObservableProperty] private bool _cameraEnabled;
    [ObservableProperty] private bool _decoyOffsetExpanded;

    public ObservableCollection<DecoyOffsetViewModel> DecoyOffsets { get; } = new();

    public bool IsValid
    {
        get
        {
            if (!FreeRecallEnabled && !RecognitionEnabled && !AudioEnabled && !CameraEnabled) return false;
            var duration = TotalDuration;
            if (duration <= TimeSpan.Zero) return false;
            var window = TimeSpan.FromMinutes(WaitingWindowMinutes);
            if (window <= TimeSpan.Zero) return false;
            if (window >= duration) return false;
            return true;
        }
    }

    public string? ValidationError
    {
        get
        {
            if (!FreeRecallEnabled && !RecognitionEnabled && !AudioEnabled && !CameraEnabled)
                return "At least one recall type must be enabled.";
            var duration = TotalDuration;
            if (duration <= TimeSpan.Zero)
                return "Duration must be at least 1 minute.";
            var window = TimeSpan.FromMinutes(WaitingWindowMinutes);
            if (window <= TimeSpan.Zero)
                return "Waiting window must be at least 1 minute.";
            if (window >= duration)
                return "Waiting window must be less than duration.";
            return null;
        }
    }

    public TimeSpan TotalDuration =>
        TimeSpan.FromDays(DurationDays) + TimeSpan.FromHours(DurationHours) + TimeSpan.FromMinutes(DurationMinutes);

    [RelayCommand]
    private void AddDecoyOffset()
    {
        DecoyOffsets.Add(new DecoyOffsetViewModel { OffsetMinutes = 5, Sign = "-" });
    }

    [RelayCommand]
    private void RemoveDecoyOffset(DecoyOffsetViewModel offset)
    {
        DecoyOffsets.Remove(offset);
    }

    public (SessionCycleConfig config, List<DecoyOffset> decoys) ToModel()
    {
        var config = new SessionCycleConfig
        {
            BaseDurationTicks = TotalDuration.Ticks,
            WaitingWindowTicks = TimeSpan.FromMinutes(WaitingWindowMinutes).Ticks,
            DriftMinutes = DriftMinutes,
            FreeRecallEnabled = FreeRecallEnabled,
            RecognitionEnabled = RecognitionEnabled,
            AudioEnabled = AudioEnabled,
            CameraEnabled = CameraEnabled,
        };

        var decoys = DecoyOffsets
            .Select(d => new DecoyOffset { OffsetMinutes = d.ActualOffsetMinutes })
            .ToList();

        return (config, decoys);
    }
}
