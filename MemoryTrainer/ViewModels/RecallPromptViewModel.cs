using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTrainer.Helpers;
using MemoryTrainer.Models;
using MemoryTrainer.Services;

namespace MemoryTrainer.ViewModels;

public partial class RecognitionOptionViewModel : ObservableObject
{
    public ScreenshotRecord Screenshot { get; set; } = null!;
    public string Label { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isFileAvailable;
}

public partial class RecallPromptViewModel : ObservableObject
{
    private readonly CycleRecord _record;
    private readonly SessionCycleConfig _config;
    private readonly DatabaseService _db;
    private readonly SessionEngine _engine;
    private ScreenshotRecord? _mainScreenshot;

    [ObservableProperty] private string _headerText = string.Empty;
    [ObservableProperty] private bool _freeRecallEnabled;
    [ObservableProperty] private bool _recognitionEnabled;

    // Free recall state
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanVerify))] private string _recallText = string.Empty;
    [ObservableProperty] private bool _isVerified;
    [ObservableProperty] private bool _freeRecallCompleted;
    [ObservableProperty] private bool _mainScreenshotAvailable;
    [ObservableProperty] private EvaluationResult? _freeRecallResult;

    // Recognition state
    [ObservableProperty] private bool _recognitionAvailable;
    [ObservableProperty] private bool _recognitionCompleted;
    [ObservableProperty] private bool? _recognitionCorrect;

    [ObservableProperty] private bool _canDismiss;

    public event Action? DismissRequested;
    public bool CanVerify => !string.IsNullOrEmpty(RecallText);
    public List<RecognitionOptionViewModel> RecognitionOptions { get; } = new();

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
        HeaderText = $"What were you doing {TimeFormatter.FormatDuration(duration)} ago?";

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var screenshots = await _db.GetScreenshotsByCycleAsync(_record.Id);
        _mainScreenshot = screenshots.FirstOrDefault(s => s.IsMain && !s.IsDeleted);
        MainScreenshotAvailable = _mainScreenshot != null && File.Exists(_mainScreenshot.FilePath);

        if (RecognitionEnabled)
            await BuildRecognitionOptionsAsync(screenshots);

        CheckCanDismiss();
    }

    private async Task BuildRecognitionOptionsAsync(List<ScreenshotRecord> screenshots)
    {
        var decoys = screenshots.Where(s => !s.IsMain && !s.IsDeleted).ToList();
        var options = new List<RecognitionOptionViewModel>();

        if (_mainScreenshot == null)
        {
            RecognitionAvailable = false;
            return;
        }

        // Add main screenshot as correct option
        options.Add(new RecognitionOptionViewModel
        {
            Screenshot = _mainScreenshot,
            IsCorrect = true,
            IsFileAvailable = File.Exists(_mainScreenshot.FilePath),
            Label = $"Option A — {_mainScreenshot.TakenAtUtc.ToLocalTime():HH:mm:ss}"
        });

        // Fill decoy slots — use configured decoys first, then random fallbacks
        if (decoys.Count == 0)
        {
            var random = await _db.GetRandomAvailableScreenshotAsync(_record.Id);
            if (random != null) decoys.Add(random);
        }

        if (decoys.Count == 0)
        {
            // No decoys at all — skip recognition
            RecognitionAvailable = false;
            return;
        }

        foreach (var decoy in decoys)
        {
            options.Add(new RecognitionOptionViewModel
            {
                Screenshot = decoy,
                IsCorrect = false,
                IsFileAvailable = File.Exists(decoy.FilePath),
                Label = string.Empty // assigned after shuffle
            });
        }

        // Shuffle
        var rng = new Random();
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }

        // Assign labels A, B, C, ...
        for (int i = 0; i < options.Count; i++)
        {
            if (string.IsNullOrEmpty(options[i].Label))
                options[i].Label = $"Option {(char)('A' + i)} — {options[i].Screenshot.TakenAtUtc.ToLocalTime():HH:mm:ss}";
            else
                options[i].Label = $"Option {(char)('A' + i)} — {options[i].Screenshot.TakenAtUtc.ToLocalTime():HH:mm:ss}";
        }

        RecognitionOptions.AddRange(options);
        RecognitionAvailable = true;
    }

    [RelayCommand]
    private void OpenMainScreenshot()
    {
        if (_mainScreenshot == null || !File.Exists(_mainScreenshot.FilePath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_mainScreenshot.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RecallPromptViewModel] Failed to open screenshot: {ex.Message}"); }
    }

    [RelayCommand]
    private void OpenOption(RecognitionOptionViewModel option)
    {
        if (!option.IsFileAvailable) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(option.Screenshot.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RecallPromptViewModel] Failed to open screenshot: {ex.Message}"); }
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
        CheckCanDismiss();
    }

    [RelayCommand]
    private async Task SelectOptionAsync(RecognitionOptionViewModel option)
    {
        foreach (var o in RecognitionOptions)
            o.IsSelected = false;
        option.IsSelected = true;

        bool correct = option.IsCorrect;
        RecognitionCorrect = correct;

        var correctOption = RecognitionOptions.First(o => o.IsCorrect);
        var result = new RecognitionResult
        {
            CycleRecordId = _record.Id,
            SelectedScreenshotRecordId = option.Screenshot.Id,
            CorrectScreenshotRecordId = correctOption.Screenshot.Id,
            IsCorrect = correct,
            EvaluatedAtUtc = DateTime.UtcNow
        };
        await _db.CreateRecognitionResultAsync(result);
        RecognitionCompleted = true;
        CheckCanDismiss();
    }

    [RelayCommand]
    private async Task DoneAsync()
    {
        _record.PromptShownUtc = DateTime.UtcNow;
        await _engine.EvaluationCompleteAsync(_record);
        DismissRequested?.Invoke();
    }

    private void CheckCanDismiss()
    {
        bool freeOk = !FreeRecallEnabled || FreeRecallCompleted;
        bool recogOk = !RecognitionEnabled || !RecognitionAvailable || RecognitionCompleted;
        CanDismiss = freeOk && recogOk;
    }
}
