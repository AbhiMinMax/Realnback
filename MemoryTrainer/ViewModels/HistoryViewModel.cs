using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MemoryTrainer.Helpers;
using MemoryTrainer.Models;
using MemoryTrainer.Services;
using System.Collections.ObjectModel;

namespace MemoryTrainer.ViewModels;

public partial class HistoryRowViewModel : ObservableObject
{
    public int CycleRecordId { get; set; }
    public string ScreenshotTime { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
    public string DurationSlot { get; set; } = string.Empty;
    public int CycleNumber { get; set; }
    public string FreeRecallResult { get; set; } = "—";
    public string RecognitionResult { get; set; } = "—";
    public string AudioResult { get; set; } = "—";
    public string CameraResult { get; set; } = "—";
    public string FreeRecallTextShort { get; set; } = string.Empty;
    public string? FreeRecallTextFull { get; set; }
    public bool IsMissed { get; set; }

    [ObservableProperty] private bool _isExpanded;
}

public partial class HistoryViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly CleanupService _cleanupService;
    private readonly Action _onBack;

    [ObservableProperty] private string _datePreset = "Week";
    [ObservableProperty] private DateTime _customFrom = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime _customTo = DateTime.Today;
    [ObservableProperty] private bool _showMissed;
    [ObservableProperty] private string _recallModeFilter = "All";
    [ObservableProperty] private string _captureTypeFilter = "All";
    [ObservableProperty] private string _sortBy = "Date";
    [ObservableProperty] private bool _sortDesc = true;
    [ObservableProperty] private int? _sessionIdFilter;

    [ObservableProperty] private string _totalFreeRecall = "0";
    [ObservableProperty] private string _correctFreeRecallPct = "—";
    [ObservableProperty] private string _partialFreeRecallPct = "—";
    [ObservableProperty] private string _wrongFreeRecallPct = "—";
    [ObservableProperty] private string _freeRecallStreak = "0";
    [ObservableProperty] private string _totalRecognition = "0";
    [ObservableProperty] private string _correctRecognitionPct = "—";
    [ObservableProperty] private string _wrongRecognitionPct = "—";
    [ObservableProperty] private string _recognitionStreak = "0";
    [ObservableProperty] private string _totalAudio = "0";
    [ObservableProperty] private string _correctAudioPct = "—";
    [ObservableProperty] private string _partialAudioPct = "—";
    [ObservableProperty] private string _wrongAudioPct = "—";
    [ObservableProperty] private string _totalCamera = "0";
    [ObservableProperty] private string _correctCameraPct = "—";
    [ObservableProperty] private string _partialCameraPct = "—";
    [ObservableProperty] private string _wrongCameraPct = "—";

    [ObservableProperty] private bool _hasNoResults;
    [ObservableProperty] private ObservableCollection<SessionModel> _sessions = new();
    [ObservableProperty] private SessionModel? _selectedSession;
    [ObservableProperty] private List<string> _durationSlots = new();

    public ObservableCollection<HistoryRowViewModel> Rows { get; } = new();
    public ISeries[] ChartSeries { get; private set; } = Array.Empty<ISeries>();
    public Axis[] ChartXAxes { get; private set; } = Array.Empty<Axis>();

    public HistoryViewModel(DatabaseService db, CleanupService cleanupService, Action onBack)
    {
        _db = db;
        _cleanupService = cleanupService;
        _onBack = onBack;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var allSessions = await _db.GetAllSessionsAsync();
        Sessions = new ObservableCollection<SessionModel>(allSessions);

        var durationTicks = await _db.GetDistinctDurationTicksAsync();
        DurationSlots = durationTicks.Select(t => TimeFormatter.FormatDuration(TimeSpan.FromTicks(t))).ToList();

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var filters = BuildFilters();
        var rows = await _db.GetFilteredCycleRecordsAsync(filters);

        Rows.Clear();
        foreach (var r in rows)
        {
            var freeResult = r.FreeRecallResult.HasValue ? r.FreeRecallResult.Value.ToString() : "—";
            var recogResult = r.RecognitionCorrect.HasValue ? (r.RecognitionCorrect.Value ? "Correct" : "Wrong") : "—";
            var audioResult = r.AudioRecallResult.HasValue ? r.AudioRecallResult.Value.ToString() : "—";
            var cameraResult = r.CameraRecallResult.HasValue ? r.CameraRecallResult.Value.ToString() : "—";
            var text = r.FreeRecallText ?? string.Empty;

            Rows.Add(new HistoryRowViewModel
            {
                CycleRecordId = r.CycleRecordId,
                ScreenshotTime = r.ScreenshotTakenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                SessionName = r.SessionName,
                DurationSlot = TimeFormatter.FormatDuration(TimeSpan.FromTicks(r.BaseDurationTicks)),
                CycleNumber = r.CycleNumber,
                FreeRecallResult = freeResult,
                RecognitionResult = recogResult,
                AudioResult = audioResult,
                CameraResult = cameraResult,
                FreeRecallTextShort = text.Length > 80 ? text[..80] + "…" : text,
                FreeRecallTextFull = text,
                IsMissed = r.Status == CycleStatus.Missed,
            });
        }

        HasNoResults = Rows.Count == 0;
        ComputeAggregations(rows);
        BuildChart(rows);
    }

    private void ComputeAggregations(List<DatabaseService.CycleRecordHistoryRow> rows)
    {
        var freeRows = rows.Where(r => r.FreeRecallResult.HasValue).ToList();
        var recogRows = rows.Where(r => r.RecognitionCorrect.HasValue).ToList();
        var audioRows = rows.Where(r => r.AudioRecallResult.HasValue).ToList();
        var cameraRows = rows.Where(r => r.CameraRecallResult.HasValue).ToList();

        TotalFreeRecall = freeRows.Count.ToString();
        if (freeRows.Count > 0)
        {
            CorrectFreeRecallPct = $"{100.0 * freeRows.Count(r => r.FreeRecallResult == EvaluationResult.Correct) / freeRows.Count:F0}%";
            PartialFreeRecallPct = $"{100.0 * freeRows.Count(r => r.FreeRecallResult == EvaluationResult.Partial) / freeRows.Count:F0}%";
            WrongFreeRecallPct = $"{100.0 * freeRows.Count(r => r.FreeRecallResult == EvaluationResult.Wrong) / freeRows.Count:F0}%";
        }
        else { CorrectFreeRecallPct = PartialFreeRecallPct = WrongFreeRecallPct = "—"; }

        int freeStreak = 0;
        foreach (var r in freeRows.OrderByDescending(r => r.ScreenshotTakenUtc))
        {
            if (r.FreeRecallResult == EvaluationResult.Correct) freeStreak++;
            else break;
        }
        FreeRecallStreak = freeStreak.ToString();

        TotalRecognition = recogRows.Count.ToString();
        if (recogRows.Count > 0)
        {
            CorrectRecognitionPct = $"{100.0 * recogRows.Count(r => r.RecognitionCorrect == true) / recogRows.Count:F0}%";
            WrongRecognitionPct = $"{100.0 * recogRows.Count(r => r.RecognitionCorrect == false) / recogRows.Count:F0}%";
        }
        else { CorrectRecognitionPct = WrongRecognitionPct = "—"; }

        int recogStreak = 0;
        foreach (var r in recogRows.OrderByDescending(r => r.ScreenshotTakenUtc))
        {
            if (r.RecognitionCorrect == true) recogStreak++;
            else break;
        }
        RecognitionStreak = recogStreak.ToString();

        TotalAudio = audioRows.Count.ToString();
        if (audioRows.Count > 0)
        {
            CorrectAudioPct = $"{100.0 * audioRows.Count(r => r.AudioRecallResult == EvaluationResult.Correct) / audioRows.Count:F0}%";
            PartialAudioPct = $"{100.0 * audioRows.Count(r => r.AudioRecallResult == EvaluationResult.Partial) / audioRows.Count:F0}%";
            WrongAudioPct = $"{100.0 * audioRows.Count(r => r.AudioRecallResult == EvaluationResult.Wrong) / audioRows.Count:F0}%";
        }
        else { CorrectAudioPct = PartialAudioPct = WrongAudioPct = "—"; }

        TotalCamera = cameraRows.Count.ToString();
        if (cameraRows.Count > 0)
        {
            CorrectCameraPct = $"{100.0 * cameraRows.Count(r => r.CameraRecallResult == EvaluationResult.Correct) / cameraRows.Count:F0}%";
            PartialCameraPct = $"{100.0 * cameraRows.Count(r => r.CameraRecallResult == EvaluationResult.Partial) / cameraRows.Count:F0}%";
            WrongCameraPct = $"{100.0 * cameraRows.Count(r => r.CameraRecallResult == EvaluationResult.Wrong) / cameraRows.Count:F0}%";
        }
        else { CorrectCameraPct = PartialCameraPct = WrongCameraPct = "—"; }
    }

    private void BuildChart(List<DatabaseService.CycleRecordHistoryRow> rows)
    {
        if (rows.Count == 0)
        {
            ChartSeries = Array.Empty<ISeries>();
            OnPropertyChanged(nameof(ChartSeries));
            return;
        }

        var (from, to) = GetDateRange();
        var span = to - from;
        string bucketFormat;
        Func<DateTime, DateTime> bucket;

        if (span.TotalDays <= 1) { bucketFormat = "HH:00"; bucket = d => new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0); }
        else if (span.TotalDays <= 7) { bucketFormat = "ddd"; bucket = d => d.Date; }
        else if (span.TotalDays <= 31) { bucketFormat = "MM/dd"; bucket = d => d.Date.AddDays(-(int)d.DayOfWeek); }
        else { bucketFormat = "MMM"; bucket = d => new DateTime(d.Year, d.Month, 1); }

        var labels = rows
            .GroupBy(r => bucket(r.ScreenshotTakenUtc.ToLocalTime()))
            .OrderBy(g => g.Key)
            .Select(g => g.Key.ToString(bucketFormat))
            .ToArray();

        double[] SeriesByBucket<T>(IEnumerable<T> source, Func<T, DateTime> time, Func<T, bool> correct)
        {
            return source.GroupBy(r => bucket(time(r).ToLocalTime()))
                .OrderBy(g => g.Key)
                .Select(g => (double)g.Count(r => correct(r)) / g.Count() * 100)
                .ToArray();
        }

        var freeData = SeriesByBucket(
            rows.Where(r => r.FreeRecallResult.HasValue),
            r => r.ScreenshotTakenUtc,
            r => r.FreeRecallResult == EvaluationResult.Correct);

        var recogData = SeriesByBucket(
            rows.Where(r => r.RecognitionCorrect.HasValue),
            r => r.ScreenshotTakenUtc,
            r => r.RecognitionCorrect == true);

        var audioData = SeriesByBucket(
            rows.Where(r => r.AudioRecallResult.HasValue),
            r => r.ScreenshotTakenUtc,
            r => r.AudioRecallResult == EvaluationResult.Correct);

        var cameraData = SeriesByBucket(
            rows.Where(r => r.CameraRecallResult.HasValue),
            r => r.ScreenshotTakenUtc,
            r => r.CameraRecallResult == EvaluationResult.Correct);

        var series = new List<ISeries>();
        if (freeData.Length > 0) series.Add(new LineSeries<double> { Values = freeData, Name = "Free Recall %" });
        if (recogData.Length > 0) series.Add(new LineSeries<double> { Values = recogData, Name = "Recognition %" });
        if (audioData.Length > 0) series.Add(new LineSeries<double> { Values = audioData, Name = "Audio %" });
        if (cameraData.Length > 0) series.Add(new LineSeries<double> { Values = cameraData, Name = "Camera %" });

        ChartSeries = series.ToArray();
        ChartXAxes = new Axis[] { new Axis { Labels = labels } };

        OnPropertyChanged(nameof(ChartSeries));
        OnPropertyChanged(nameof(ChartXAxes));
    }

    private (DateTime from, DateTime to) GetDateRange()
    {
        var to = DateTime.UtcNow;
        var from = DatePreset switch
        {
            "Day" => to.AddDays(-1),
            "Week" => to.AddDays(-7),
            "Month" => to.AddMonths(-1),
            "Year" => to.AddYears(-1),
            "Custom" => CustomFrom.ToUniversalTime(),
            _ => to.AddDays(-7)
        };
        return (from, DatePreset == "Custom" ? CustomTo.ToUniversalTime() : to);
    }

    private DatabaseService.HistoryFilters BuildFilters()
    {
        var (from, to) = GetDateRange();
        RecallMode? modeFilter = RecallModeFilter switch
        {
            "Free Recall" => RecallMode.Free,
            "Recognition" => RecallMode.Recognition,
            _ => null
        };
        string? captureTypeFilter = CaptureTypeFilter == "All" ? null : CaptureTypeFilter;
        return new DatabaseService.HistoryFilters(
            from, to, null,
            SelectedSession?.Id,
            ShowMissed, modeFilter, captureTypeFilter,
            SortBy, SortDesc
        );
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(SessionModel session)
    {
        if (session == null) return;
        var result = System.Windows.MessageBox.Show(
            $"Delete session '{session.Name}'? This will remove all score records and captured media files.",
            "Delete Session", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        await _cleanupService.DeleteSessionCaptureFilesAsync(session.Id);
        await _db.DeleteSessionAsync(session.Id);
        Sessions.Remove(session);
        if (SelectedSession == session) SelectedSession = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private void GoBack() => _onBack();

    partial void OnDatePresetChanged(string value) => _ = RefreshAsync();
    partial void OnShowMissedChanged(bool value) => _ = RefreshAsync();
    partial void OnRecallModeFilterChanged(string value) => _ = RefreshAsync();
    partial void OnCaptureTypeFilterChanged(string value) => _ = RefreshAsync();
    partial void OnSortByChanged(string value) => _ = RefreshAsync();
    partial void OnSortDescChanged(bool value) => _ = RefreshAsync();
    partial void OnSelectedSessionChanged(SessionModel? value) => _ = RefreshAsync();
}
