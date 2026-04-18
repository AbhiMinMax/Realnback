using CommunityToolkit.Mvvm.ComponentModel;
using MemoryTrainer.Helpers;
using MemoryTrainer.Models;
using MemoryTrainer.Services;
using System.Windows.Threading;

namespace MemoryTrainer.ViewModels;

public partial class CycleStatusViewModel : ObservableObject, IDisposable
{
    private readonly CycleRunner _runner;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string _statusText = "Pending";
    [ObservableProperty] private string _countdownText = string.Empty;
    [ObservableProperty] private string _durationLabel = string.Empty;
    [ObservableProperty] private int _cycleNumber;

    public CycleStatusViewModel(CycleRunner runner)
    {
        _runner = runner;
        _durationLabel = TimeFormatter.FormatDuration(TimeSpan.FromTicks(runner.Config.BaseDurationTicks));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateCountdown();
        _timer.Start();

        runner.StatusChanged += OnStatusChanged;
        UpdateFromRunner();
    }

    private void OnStatusChanged(CycleRunner runner, string status)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            StatusText = status;
            UpdateFromRunner();
        });
    }

    private void UpdateFromRunner()
    {
        var record = _runner.CurrentRecord;
        if (record != null)
        {
            CycleNumber = record.CycleNumber;
            StatusText = record.Status switch
            {
                CycleStatus.WaitingForScreenshot => "Waiting for screenshot",
                CycleStatus.ScreenshotTaken => "Screenshot taken — waiting for prompt",
                CycleStatus.PromptQueued => "Prompt queued",
                CycleStatus.PromptShown => "Prompt shown",
                CycleStatus.Completed => "Completed",
                CycleStatus.Missed => "Missed",
                CycleStatus.Incomplete => "Incomplete",
                _ => "Pending"
            };
        }
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        var screenshotIn = _runner.GetScreenshotCountdown();
        if (screenshotIn.HasValue && screenshotIn.Value > TimeSpan.Zero)
        {
            CountdownText = $"{TimeFormatter.FormatCountdown(screenshotIn.Value)} remaining";
            return;
        }

        var promptIn = _runner.GetPromptCountdown();
        if (promptIn.HasValue && promptIn.Value > TimeSpan.Zero)
        {
            CountdownText = $"Prompt in {TimeFormatter.FormatCountdown(promptIn.Value)}";
            return;
        }

        CountdownText = string.Empty;
    }

    public void Dispose()
    {
        _timer.Stop();
        _runner.StatusChanged -= OnStatusChanged;
    }
}
