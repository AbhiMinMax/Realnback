using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTrainer.Helpers;
using MemoryTrainer.Models;
using MemoryTrainer.Services;

namespace MemoryTrainer.ViewModels;

public enum StepState { Pending, Current, Completed }

public partial class StepIndicatorItem : ObservableObject
{
    public int Number { get; set; }
    public string Label { get; set; } = string.Empty;
    [ObservableProperty] private StepState _state;
}

public partial class RecognitionOptionViewModel : ObservableObject
{
    public CaptureRecord Capture { get; set; } = null!;
    public string Label { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isFileAvailable;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlayStopLabel))] private bool _isPlaying;
    public string PlayStopLabel => IsPlaying ? "⏹ Stop" : "▶ Play";
}

public partial class RecallPromptViewModel : ObservableObject
{
    private readonly CycleRecord _record;
    private readonly SessionCycleConfig _config;
    private readonly DatabaseService _db;
    private readonly SessionEngine _engine;

    // ── Tab management ──
    private readonly List<string> _tabs = new();
    [ObservableProperty] private int _stepIndex;

    public bool OnScreenshotTab => _tabs.Count > _stepIndex && _tabs[_stepIndex] == "Screenshot";
    public bool OnAudioTab => _tabs.Count > _stepIndex && _tabs[_stepIndex] == "Audio";
    public bool OnCameraTab => _tabs.Count > _stepIndex && _tabs[_stepIndex] == "Camera";
    public bool IsLastTab => _stepIndex == _tabs.Count - 1;
    public string NextButtonText => IsLastTab ? "Done" : "Next →";
    public System.Collections.ObjectModel.ObservableCollection<StepIndicatorItem> StepIndicators { get; } = new();

    // ── Screenshot tab ──
    [ObservableProperty] private string _headerText = string.Empty;
    [ObservableProperty] private bool _freeRecallEnabled;
    [ObservableProperty] private bool _recognitionEnabled;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanVerify))] private string _recallText = string.Empty;
    [ObservableProperty] private bool _isVerified;
    [ObservableProperty] private bool _freeRecallCompleted;
    [ObservableProperty] private bool _mainCaptureAvailable;
    [ObservableProperty] private EvaluationResult? _freeRecallResult;
    [ObservableProperty] private bool _recognitionAvailable;
    [ObservableProperty] private bool _recognitionCompleted;
    [ObservableProperty] private bool? _recognitionCorrect;
    private CaptureRecord? _mainCapture;

    public bool CanVerify => !string.IsNullOrEmpty(RecallText);
    public System.Collections.ObjectModel.ObservableCollection<RecognitionOptionViewModel> RecognitionOptions { get; } = new();

    // ── Audio tab ──
    [ObservableProperty] private string _audioHeaderText = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanVerifyAudio))] private string _audioRecallText = string.Empty;
    [ObservableProperty] private bool _audioIsVerified;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowAudioRecognition))] private bool _audioRecallCompleted;
    [ObservableProperty] private EvaluationResult? _audioEvalResult;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AudioPlayLabel))] private bool _audioIsPlaying;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowAudioRecognition))] private bool _audioRecognitionAvailable;
    [ObservableProperty] private bool _audioRecognitionCompleted;
    private CaptureRecord? _audioCapture;
    private readonly AudioPlayerService _audioPlayer = new();
    private readonly AudioPlayerService _audioOptionPlayer = new();
    private RecognitionOptionViewModel? _playingAudioOption;

    public bool CanVerifyAudio => !string.IsNullOrEmpty(AudioRecallText);
    public string AudioPlayLabel => AudioIsPlaying ? "⏸ Pause" : "▶ Play";
    public bool ShowAudioRecognition => AudioRecallCompleted && AudioRecognitionAvailable;
    public System.Collections.ObjectModel.ObservableCollection<RecognitionOptionViewModel> AudioRecognitionOptions { get; } = new();

    // ── Camera tab ──
    [ObservableProperty] private string _cameraHeaderText = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanVerifyCamera))] private string _cameraRecallText = string.Empty;
    [ObservableProperty] private bool _cameraIsVerified;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowCameraRecognition))] private bool _cameraRecallCompleted;
    [ObservableProperty] private EvaluationResult? _cameraEvalResult;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowCameraRecognition))] private bool _cameraRecognitionAvailable;
    [ObservableProperty] private bool _cameraRecognitionCompleted;
    private CaptureRecord? _cameraCapture;

    public bool CanVerifyCamera => !string.IsNullOrEmpty(CameraRecallText);
    public bool ShowCameraRecognition => CameraRecallCompleted && CameraRecognitionAvailable;
    public System.Collections.ObjectModel.ObservableCollection<RecognitionOptionViewModel> CameraRecognitionOptions { get; } = new();

    // ── Navigation ──
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _canDismiss;

    public event Action? DismissRequested;

    public RecallPromptViewModel(CycleRecord record, SessionCycleConfig config,
        DatabaseService db, SessionEngine engine)
    {
        _record = record;
        _config = config;
        _db = db;
        _engine = engine;

        FreeRecallEnabled = config.FreeRecallEnabled;
        RecognitionEnabled = config.RecognitionEnabled;

        var duration = TimeSpan.FromTicks(record.ActualDurationTicks);
        var durationLabel = TimeFormatter.FormatDuration(duration);
        HeaderText = $"What were you doing {durationLabel} ago?";
        AudioHeaderText = $"What were you hearing {durationLabel} ago?";
        CameraHeaderText = $"How were you {durationLabel} ago?";

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var captures = await _db.GetCapturesByCycleAsync(_record.Id);

        // Screenshot tab
        _mainCapture = captures.FirstOrDefault(c => c.Type == CaptureType.Screenshot && c.IsMain && !c.IsDeleted);
        MainCaptureAvailable = _mainCapture != null && _mainCapture.FilePath != null && File.Exists(_mainCapture.FilePath);

        if (RecognitionEnabled)
            await BuildRecognitionOptionsAsync(captures);

        // Audio tab (only if enabled and captured successfully)
        _audioCapture = captures.FirstOrDefault(c => c.Type == CaptureType.Audio && c.IsMain
            && c.Availability == CaptureAvailability.Captured && !c.IsDeleted);

        if (RecognitionEnabled && _audioCapture != null)
            await BuildAudioRecognitionOptionsAsync(captures);

        // Camera tab
        _cameraCapture = captures.FirstOrDefault(c => c.Type == CaptureType.Camera && c.IsMain
            && c.Availability == CaptureAvailability.Captured && !c.IsDeleted);

        if (RecognitionEnabled && _cameraCapture != null)
            await BuildCameraRecognitionOptionsAsync(captures);

        BuildTabs();
        UpdateNavigation();
    }

    private void BuildTabs()
    {
        _tabs.Clear();
        _tabs.Add("Screenshot");
        if (_config.AudioEnabled && _audioCapture != null) _tabs.Add("Audio");
        if (_config.CameraEnabled && _cameraCapture != null) _tabs.Add("Camera");

        StepIndicators.Clear();
        for (int i = 0; i < _tabs.Count; i++)
        {
            StepIndicators.Add(new StepIndicatorItem
            {
                Number = i + 1,
                Label = _tabs[i],
                State = i == 0 ? StepState.Current : StepState.Pending
            });
        }

        OnPropertyChanged(nameof(OnScreenshotTab));
        OnPropertyChanged(nameof(OnAudioTab));
        OnPropertyChanged(nameof(OnCameraTab));
        OnPropertyChanged(nameof(IsLastTab));
        OnPropertyChanged(nameof(NextButtonText));
    }

    private const int RecognitionOptionCount = 4;

    private async Task BuildRecognitionOptionsAsync(List<CaptureRecord> captures)
    {
        if (_mainCapture == null) { RecognitionAvailable = false; return; }

        var configuredDecoys = captures
            .Where(c => c.Type == CaptureType.Screenshot && !c.IsMain && !c.IsDeleted)
            .ToList();

        // Pad to RecognitionOptionCount - 1 decoys using nearest-in-time main captures from other cycles
        int needed = RecognitionOptionCount - 1 - configuredDecoys.Count;
        if (needed > 0)
        {
            var extras = await _db.GetRandomAvailableScreenshotCapturesAsync(_record.Id, needed);
            var existingIds = configuredDecoys.Select(d => d.Id).ToHashSet();
            configuredDecoys.AddRange(extras.Where(e => !existingIds.Contains(e.Id)));
        }

        // Still need at least 1 decoy to make recognition meaningful
        if (configuredDecoys.Count == 0) { RecognitionAvailable = false; return; }

        var options = new List<RecognitionOptionViewModel>
        {
            new RecognitionOptionViewModel
            {
                Capture = _mainCapture,
                IsCorrect = true,
                IsFileAvailable = _mainCapture.FilePath != null && File.Exists(_mainCapture.FilePath),
                Label = string.Empty
            }
        };

        foreach (var decoy in configuredDecoys)
        {
            options.Add(new RecognitionOptionViewModel
            {
                Capture = decoy,
                IsCorrect = false,
                IsFileAvailable = decoy.FilePath != null && File.Exists(decoy.FilePath!),
                Label = string.Empty
            });
        }

        var rng = new Random();
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }

        for (int i = 0; i < options.Count; i++)
            options[i].Label = $"Option {(char)('A' + i)}";

        foreach (var o in options) RecognitionOptions.Add(o);
        RecognitionAvailable = true;
    }

    private async Task BuildAudioRecognitionOptionsAsync(List<CaptureRecord> captures)
    {
        if (_audioCapture == null) { AudioRecognitionAvailable = false; return; }

        var configuredDecoys = captures
            .Where(c => c.Type == CaptureType.Audio && !c.IsMain && c.Availability == CaptureAvailability.Captured && !c.IsDeleted)
            .ToList();

        int needed = RecognitionOptionCount - 1 - configuredDecoys.Count;
        if (needed > 0)
        {
            var extras = await _db.GetRandomAvailableAudioCapturesAsync(_record.Id, needed);
            var existingIds = configuredDecoys.Select(d => d.Id).ToHashSet();
            configuredDecoys.AddRange(extras.Where(e => !existingIds.Contains(e.Id)));
        }

        if (configuredDecoys.Count == 0) { AudioRecognitionAvailable = false; return; }

        var options = new List<RecognitionOptionViewModel>
        {
            new RecognitionOptionViewModel
            {
                Capture = _audioCapture,
                IsCorrect = true,
                IsFileAvailable = _audioCapture.FilePath != null && File.Exists(_audioCapture.FilePath),
                Label = string.Empty
            }
        };

        foreach (var decoy in configuredDecoys)
        {
            options.Add(new RecognitionOptionViewModel
            {
                Capture = decoy,
                IsCorrect = false,
                IsFileAvailable = decoy.FilePath != null && File.Exists(decoy.FilePath!),
                Label = string.Empty
            });
        }

        var rng = new Random();
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }

        for (int i = 0; i < options.Count; i++)
            options[i].Label = $"Option {(char)('A' + i)}";

        foreach (var o in options) AudioRecognitionOptions.Add(o);
        AudioRecognitionAvailable = true;
    }

    private async Task BuildCameraRecognitionOptionsAsync(List<CaptureRecord> captures)
    {
        if (_cameraCapture == null) { CameraRecognitionAvailable = false; return; }

        var configuredDecoys = captures
            .Where(c => c.Type == CaptureType.Camera && !c.IsMain && c.Availability == CaptureAvailability.Captured && !c.IsDeleted)
            .ToList();

        int needed = RecognitionOptionCount - 1 - configuredDecoys.Count;
        if (needed > 0)
        {
            var extras = await _db.GetRandomAvailableCameraCapturesAsync(_record.Id, needed);
            var existingIds = configuredDecoys.Select(d => d.Id).ToHashSet();
            configuredDecoys.AddRange(extras.Where(e => !existingIds.Contains(e.Id)));
        }

        if (configuredDecoys.Count == 0) { CameraRecognitionAvailable = false; return; }

        var options = new List<RecognitionOptionViewModel>
        {
            new RecognitionOptionViewModel
            {
                Capture = _cameraCapture,
                IsCorrect = true,
                IsFileAvailable = _cameraCapture.FilePath != null && File.Exists(_cameraCapture.FilePath),
                Label = string.Empty
            }
        };

        foreach (var decoy in configuredDecoys)
        {
            options.Add(new RecognitionOptionViewModel
            {
                Capture = decoy,
                IsCorrect = false,
                IsFileAvailable = decoy.FilePath != null && File.Exists(decoy.FilePath!),
                Label = string.Empty
            });
        }

        var rng = new Random();
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }

        for (int i = 0; i < options.Count; i++)
            options[i].Label = $"Option {(char)('A' + i)}";

        foreach (var o in options) CameraRecognitionOptions.Add(o);
        CameraRecognitionAvailable = true;
    }

    // ── Navigation ──

    private void UpdateNavigation()
    {
        if (_tabs.Count == 0) { CanGoNext = false; return; }

        CanGoNext = _tabs[_stepIndex] switch
        {
            "Screenshot" => IsScreenshotTabComplete,
            "Audio" => IsAudioTabComplete,
            "Camera" => IsCameraTabComplete,
            _ => false
        };
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(IsLastTab));
        UpdateStepIndicators();
    }

    private bool IsScreenshotTabComplete =>
        (!FreeRecallEnabled || FreeRecallCompleted) &&
        (!RecognitionEnabled || !RecognitionAvailable || RecognitionCompleted);

    private bool IsAudioTabComplete =>
        AudioRecallCompleted &&
        (!RecognitionEnabled || !AudioRecognitionAvailable || AudioRecognitionCompleted);

    private bool IsCameraTabComplete =>
        CameraRecallCompleted &&
        (!RecognitionEnabled || !CameraRecognitionAvailable || CameraRecognitionCompleted);

    private void UpdateStepIndicators()
    {
        for (int i = 0; i < StepIndicators.Count; i++)
        {
            StepIndicators[i].State = i < _stepIndex ? StepState.Completed
                : i == _stepIndex ? StepState.Current
                : StepState.Pending;
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (IsLastTab) { await DoneAsync(); return; }
        _stepIndex++;
        OnPropertyChanged(nameof(StepIndex));
        OnPropertyChanged(nameof(OnScreenshotTab));
        OnPropertyChanged(nameof(OnAudioTab));
        OnPropertyChanged(nameof(OnCameraTab));
        UpdateNavigation();
    }

    [RelayCommand]
    private async Task DoneAsync()
    {
        _audioPlayer.Stop();
        _audioPlayer.Dispose();
        _audioOptionPlayer.Stop();
        _audioOptionPlayer.Dispose();
        _record.PromptShownUtc = DateTime.UtcNow;
        await _engine.EvaluationCompleteAsync(_record);
        CanDismiss = true;
        DismissRequested?.Invoke();
    }

    // ── Screenshot tab commands ──

    [RelayCommand]
    private void OpenMainCapture()
    {
        if (_mainCapture?.FilePath == null || !File.Exists(_mainCapture.FilePath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_mainCapture.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RecallPromptViewModel] Failed to open screenshot: {ex.Message}"); }
    }

    [RelayCommand]
    private void OpenOption(RecognitionOptionViewModel option)
    {
        if (!option.IsFileAvailable || option.Capture.FilePath == null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(option.Capture.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RecallPromptViewModel] Failed to open option: {ex.Message}"); }
    }

    [RelayCommand]
    private void Verify()
    {
        if (string.IsNullOrEmpty(RecallText)) return;
        IsVerified = true;
    }

    [RelayCommand]
    private async Task EvaluateFreeRecallAsync(string result)
    {
        if (!Enum.TryParse<EvaluationResult>(result, out var evalResult)) return;
        FreeRecallResult = evalResult;
        var freeResult = new FreeRecallResult
        {
            CycleRecordId = _record.Id,
            RecallText = RecallText,
            Result = evalResult,
            EvaluatedAtUtc = DateTime.UtcNow
        };
        await _db.CreateFreeRecallResultAsync(freeResult);
        FreeRecallCompleted = true;
        UpdateNavigation();
    }

    [RelayCommand]
    private async Task SelectOptionAsync(RecognitionOptionViewModel option)
    {
        foreach (var o in RecognitionOptions) o.IsSelected = false;
        option.IsSelected = true;
        RecognitionCorrect = option.IsCorrect;

        var correct = RecognitionOptions.First(o => o.IsCorrect);
        var result = new RecognitionResult
        {
            CycleRecordId = _record.Id,
            SelectedCaptureRecordId = option.Capture.Id,
            CorrectCaptureRecordId = correct.Capture.Id,
            IsCorrect = option.IsCorrect,
            EvaluatedAtUtc = DateTime.UtcNow
        };
        await _db.CreateRecognitionResultAsync(result);
        RecognitionCompleted = true;
        UpdateNavigation();
    }

    // ── Audio tab commands ──

    [RelayCommand]
    private void VerifyAudio()
    {
        if (string.IsNullOrEmpty(AudioRecallText)) return;
        AudioIsVerified = true;
        _audioPlayer.PlaybackStopped += () =>
        {
            AudioIsPlaying = false;
        };
    }

    [RelayCommand]
    private void PlayPauseAudio()
    {
        if (_audioCapture?.FilePath == null || !File.Exists(_audioCapture.FilePath)) return;
        if (_audioPlayer.IsPlaying)
        {
            _audioPlayer.Pause();
            AudioIsPlaying = false;
        }
        else if (_audioPlayer.IsPaused)
        {
            _audioPlayer.Resume();
            AudioIsPlaying = true;
        }
        else
        {
            _audioPlayer.Play(_audioCapture.FilePath);
            AudioIsPlaying = true;
        }
    }

    [RelayCommand]
    private void StopAudio()
    {
        _audioPlayer.Stop();
        AudioIsPlaying = false;
    }

    [RelayCommand]
    private async Task EvaluateAudioAsync(string result)
    {
        if (!Enum.TryParse<EvaluationResult>(result, out var evalResult)) return;
        AudioEvalResult = evalResult;
        var audioResult = new AudioRecallResult
        {
            CycleRecordId = _record.Id,
            RecallText = AudioRecallText,
            Result = evalResult,
            EvaluatedAtUtc = DateTime.UtcNow
        };
        await _db.CreateAudioRecallResultAsync(audioResult);
        AudioRecallCompleted = true;
        UpdateNavigation();
    }

    [RelayCommand]
    private void PlayStopAudioOption(RecognitionOptionViewModel option)
    {
        // If this option is already playing, stop it
        if (_playingAudioOption == option && _audioOptionPlayer.IsPlaying)
        {
            _audioOptionPlayer.Stop();
            option.IsPlaying = false;
            _playingAudioOption = null;
            return;
        }

        // Stop any previously playing option
        if (_playingAudioOption != null)
        {
            _audioOptionPlayer.Stop();
            _playingAudioOption.IsPlaying = false;
            _playingAudioOption = null;
        }

        if (!option.IsFileAvailable || option.Capture.FilePath == null) return;

        _playingAudioOption = option;
        _audioOptionPlayer.PlaybackStopped += OnAudioOptionStopped;
        _audioOptionPlayer.Play(option.Capture.FilePath);
        option.IsPlaying = true;
    }

    private void OnAudioOptionStopped()
    {
        _audioOptionPlayer.PlaybackStopped -= OnAudioOptionStopped;
        if (_playingAudioOption != null)
        {
            _playingAudioOption.IsPlaying = false;
            _playingAudioOption = null;
        }
    }

    [RelayCommand]
    private async Task SelectAudioOptionAsync(RecognitionOptionViewModel option)
    {
        foreach (var o in AudioRecognitionOptions) o.IsSelected = false;
        option.IsSelected = true;

        var correct = AudioRecognitionOptions.First(o => o.IsCorrect);
        var result = new RecognitionResult
        {
            CycleRecordId = _record.Id,
            SelectedCaptureRecordId = option.Capture.Id,
            CorrectCaptureRecordId = correct.Capture.Id,
            IsCorrect = option.IsCorrect,
            EvaluatedAtUtc = DateTime.UtcNow
        };
        await _db.CreateRecognitionResultAsync(result);
        AudioRecognitionCompleted = true;
        UpdateNavigation();
    }

    // ── Camera tab commands ──

    [RelayCommand]
    private void VerifyCamera()
    {
        if (string.IsNullOrEmpty(CameraRecallText)) return;
        CameraIsVerified = true;
    }

    [RelayCommand]
    private void OpenCameraCapture()
    {
        if (_cameraCapture?.FilePath == null || !File.Exists(_cameraCapture.FilePath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_cameraCapture.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RecallPromptViewModel] Failed to open camera photo: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task EvaluateCameraAsync(string result)
    {
        if (!Enum.TryParse<EvaluationResult>(result, out var evalResult)) return;
        CameraEvalResult = evalResult;
        var cameraResult = new CameraRecallResult
        {
            CycleRecordId = _record.Id,
            RecallText = CameraRecallText,
            Result = evalResult,
            EvaluatedAtUtc = DateTime.UtcNow
        };
        await _db.CreateCameraRecallResultAsync(cameraResult);
        CameraRecallCompleted = true;
        UpdateNavigation();
    }

    [RelayCommand]
    private void OpenCameraOption(RecognitionOptionViewModel option)
    {
        if (!option.IsFileAvailable || option.Capture.FilePath == null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(option.Capture.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RecallPromptViewModel] Failed to open camera option: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task SelectCameraOptionAsync(RecognitionOptionViewModel option)
    {
        foreach (var o in CameraRecognitionOptions) o.IsSelected = false;
        option.IsSelected = true;

        var correct = CameraRecognitionOptions.First(o => o.IsCorrect);
        var result = new RecognitionResult
        {
            CycleRecordId = _record.Id,
            SelectedCaptureRecordId = option.Capture.Id,
            CorrectCaptureRecordId = correct.Capture.Id,
            IsCorrect = option.IsCorrect,
            EvaluatedAtUtc = DateTime.UtcNow
        };
        await _db.CreateRecognitionResultAsync(result);
        CameraRecognitionCompleted = true;
        UpdateNavigation();
    }
}
