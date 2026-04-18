using MemoryTrainer.Models;

namespace MemoryTrainer.Services;

public class CycleRunner
{
    private readonly SessionCycleConfig _config;
    private readonly List<DecoyOffset> _decoyOffsets;
    private readonly DatabaseService _db;
    private readonly ScreenshotService _screenshotService;
    private readonly int _sessionId;

    private CancellationTokenSource _cts = new();
    private TimeSpan? _pausedScreenshotRemaining;
    private TimeSpan? _pausedPromptRemaining;
    private Dictionary<int, TimeSpan> _pausedDecoyRemaining = new();
    private int _cycleNumber;
    private CycleRecord? _currentRecord;

    public event Func<CycleRecord, Task>? PromptReady;
    public event Action<CycleRunner, string>? StatusChanged;

    public CycleRecord? CurrentRecord => _currentRecord;
    public SessionCycleConfig Config => _config;

    public CycleRunner(SessionCycleConfig config, List<DecoyOffset> decoyOffsets,
        DatabaseService db, ScreenshotService screenshotService, int sessionId)
    {
        _config = config;
        _decoyOffsets = decoyOffsets;
        _db = db;
        _screenshotService = screenshotService;
        _sessionId = sessionId;
    }

    public void Start(int startingCycleNumber = 1)
    {
        _cycleNumber = startingCycleNumber;
        _ = RunLoopAsync(_cts.Token);
    }

    public void Pause()
    {
        if (_currentRecord == null) return;
        var now = DateTime.UtcNow;

        if (_currentRecord.Status == CycleStatus.WaitingForScreenshot)
        {
            var remaining = _currentRecord.ScheduledScreenshotUtc - now;
            _pausedScreenshotRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        else if (_currentRecord.Status == CycleStatus.ScreenshotTaken && _currentRecord.PromptDueUtc.HasValue)
        {
            var remaining = _currentRecord.PromptDueUtc.Value - now;
            _pausedPromptRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        _cts.Cancel();
    }

    public void Resume()
    {
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token, resuming: true);
    }

    public void Stop()
    {
        _cts.Cancel();
    }

    public async Task RestoreAsync(CycleRecord record)
    {
        _cycleNumber = record.CycleNumber;
        _currentRecord = record;
        var now = DateTime.UtcNow;

        switch (record.Status)
        {
            case CycleStatus.WaitingForScreenshot:
                if (record.ScheduledScreenshotUtc <= now)
                {
                    record.Status = CycleStatus.Missed;
                    record.MissedReason = "App was closed";
                    await _db.UpdateCycleRecordAsync(record);
                    StatusChanged?.Invoke(this, "Missed (app was closed)");
                    _cycleNumber++;
                    Start(_cycleNumber);
                }
                else
                {
                    _pausedScreenshotRemaining = record.ScheduledScreenshotUtc - now;
                    if (record.PromptDueUtc.HasValue)
                        _pausedPromptRemaining = record.PromptDueUtc.Value - now;
                    Resume();
                }
                break;

            case CycleStatus.ScreenshotTaken:
                if (record.PromptDueUtc.HasValue && record.PromptDueUtc.Value <= now)
                {
                    record.Status = CycleStatus.PromptQueued;
                    await _db.UpdateCycleRecordAsync(record);
                    StatusChanged?.Invoke(this, "Prompt queued");
                    await (PromptReady?.Invoke(record) ?? Task.CompletedTask);
                }
                else
                {
                    _pausedPromptRemaining = record.PromptDueUtc.HasValue ? record.PromptDueUtc.Value - now : TimeSpan.FromMinutes(1);
                    Resume();
                }
                break;

            case CycleStatus.PromptQueued:
            case CycleStatus.PromptShown:
                record.Status = CycleStatus.PromptQueued;
                await _db.UpdateCycleRecordAsync(record);
                await (PromptReady?.Invoke(record) ?? Task.CompletedTask);
                break;

            default:
                _cycleNumber++;
                Start(_cycleNumber);
                break;
        }
    }

    private async Task RunLoopAsync(CancellationToken ct, bool resuming = false)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOneCycleAsync(ct, resuming);
                resuming = false;
                _cycleNumber++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CycleRunner:{_config.Id}] Unhandled exception: {ex}");
                break;
            }
        }
    }

    private async Task RunOneCycleAsync(CancellationToken ct, bool resuming)
    {
        var rng = new Random();
        var now = DateTime.UtcNow;

        CycleRecord record;

        if (resuming && _currentRecord != null)
        {
            record = _currentRecord;
        }
        else
        {
            int driftRange = _config.DriftMinutes;
            int driftOffset = driftRange > 0 ? rng.Next(-driftRange, driftRange + 1) : 0;
            long baseTicks = _config.BaseDurationTicks;
            long driftTicks = TimeSpan.FromMinutes(driftOffset).Ticks;
            long actualTicks = Math.Max(baseTicks + driftTicks, TimeSpan.FromMinutes(1).Ticks);

            var waitWindow = TimeSpan.FromTicks(_config.WaitingWindowTicks);
            double randomSeconds = rng.NextDouble() * waitWindow.TotalSeconds;
            var screenshotTime = now + TimeSpan.FromSeconds(randomSeconds);
            var promptDue = now + TimeSpan.FromTicks(actualTicks);

            record = new CycleRecord
            {
                SessionCycleConfigId = _config.Id,
                CycleNumber = _cycleNumber,
                ActualDurationTicks = actualTicks,
                CycleStartUtc = now,
                ScheduledScreenshotUtc = screenshotTime,
                PromptDueUtc = promptDue,
                Status = CycleStatus.WaitingForScreenshot
            };
            record.Id = await _db.CreateCycleRecordAsync(record);
            _currentRecord = record;
            StatusChanged?.Invoke(this, "Waiting for screenshot");
        }

        // Wait until screenshot time
        TimeSpan screenshotWait = resuming && _pausedScreenshotRemaining.HasValue
            ? _pausedScreenshotRemaining.Value
            : record.ScheduledScreenshotUtc - DateTime.UtcNow;

        if (record.Status == CycleStatus.WaitingForScreenshot)
        {
            // Schedule decoys concurrently
            var decoyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var decoyTasks = ScheduleDecoys(record, resuming, decoyCts.Token);

            if (screenshotWait > TimeSpan.Zero)
                await Task.Delay(screenshotWait, ct);

            ct.ThrowIfCancellationRequested();

            // Capture main screenshot
            var path = _screenshotService.Capture(_sessionId, record.Id, isMain: true, offsetMinutes: null);
            var screenshotRecord = new ScreenshotRecord
            {
                CycleRecordId = record.Id,
                FilePath = path,
                TakenAtUtc = DateTime.UtcNow,
                IsMain = true
            };
            screenshotRecord.Id = await _db.CreateScreenshotRecordAsync(screenshotRecord);

            record.ScreenshotTakenUtc = DateTime.UtcNow;
            record.Status = CycleStatus.ScreenshotTaken;
            await _db.UpdateCycleRecordAsync(record);
            StatusChanged?.Invoke(this, "Screenshot taken, waiting for prompt");

            _pausedScreenshotRemaining = null;

            // Wait for decoys too (fire and forget, they cancel on pause/stop)
            _ = Task.WhenAll(decoyTasks);
        }

        // Wait until prompt due
        TimeSpan promptWait = resuming && _pausedPromptRemaining.HasValue
            ? _pausedPromptRemaining.Value
            : (record.PromptDueUtc.HasValue ? record.PromptDueUtc.Value - DateTime.UtcNow : TimeSpan.Zero);

        if (record.Status == CycleStatus.ScreenshotTaken)
        {
            if (promptWait > TimeSpan.Zero)
                await Task.Delay(promptWait, ct);

            ct.ThrowIfCancellationRequested();

            record.Status = CycleStatus.PromptQueued;
            await _db.UpdateCycleRecordAsync(record);
            StatusChanged?.Invoke(this, "Prompt queued");
            _pausedPromptRemaining = null;

            await (PromptReady?.Invoke(record) ?? Task.CompletedTask);
        }
    }

    private List<Task> ScheduleDecoys(CycleRecord record, bool resuming, CancellationToken ct)
    {
        var tasks = new List<Task>();
        foreach (var decoy in _decoyOffsets)
        {
            var captureTime = record.ScheduledScreenshotUtc + TimeSpan.FromMinutes(decoy.OffsetMinutes);
            var wait = captureTime - DateTime.UtcNow;

            if (resuming && _pausedDecoyRemaining.TryGetValue(decoy.Id, out var remaining))
                wait = remaining;

            if (wait <= TimeSpan.Zero)
            {
                System.Diagnostics.Debug.WriteLine($"[CycleRunner:{_config.Id}] Decoy offset {decoy.OffsetMinutes}m skipped — time already passed");
                continue;
            }

            tasks.Add(CaptureDecoyAsync(record, decoy, wait, ct));
        }
        _pausedDecoyRemaining.Clear();
        return tasks;
    }

    private async Task CaptureDecoyAsync(CycleRecord record, DecoyOffset decoy, TimeSpan wait, CancellationToken ct)
    {
        try
        {
            await Task.Delay(wait, ct);
            ct.ThrowIfCancellationRequested();

            var path = _screenshotService.Capture(_sessionId, record.Id, isMain: false, offsetMinutes: decoy.OffsetMinutes);
            var screenshotRecord = new ScreenshotRecord
            {
                CycleRecordId = record.Id,
                FilePath = path,
                TakenAtUtc = DateTime.UtcNow,
                IsMain = false,
                OffsetMinutes = decoy.OffsetMinutes
            };
            await _db.CreateScreenshotRecordAsync(screenshotRecord);
        }
        catch (OperationCanceledException)
        {
            // Record remaining time for resume
            var captureTime = record.ScheduledScreenshotUtc + TimeSpan.FromMinutes(decoy.OffsetMinutes);
            var remaining = captureTime - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
                _pausedDecoyRemaining[decoy.Id] = remaining;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CycleRunner:{_config.Id}] Decoy capture failed: {ex.Message}");
        }
    }

    public void OnEvaluationComplete()
    {
        // Called by SessionEngine after evaluation is saved — loop continues automatically
    }

    public TimeSpan? GetScreenshotCountdown()
    {
        if (_currentRecord?.Status == CycleStatus.WaitingForScreenshot)
            return _currentRecord.ScheduledScreenshotUtc - DateTime.UtcNow;
        return null;
    }

    public TimeSpan? GetPromptCountdown()
    {
        if (_currentRecord?.Status == CycleStatus.ScreenshotTaken && _currentRecord.PromptDueUtc.HasValue)
            return _currentRecord.PromptDueUtc.Value - DateTime.UtcNow;
        return null;
    }
}
