using System.Collections.Concurrent;
using MemoryTrainer.Models;

namespace MemoryTrainer.Services;

public class SessionEngine
{
    private readonly DatabaseService _db;
    private readonly ScreenshotService _screenshotService;
    private readonly CleanupService _cleanupService;

    private SessionModel? _activeSession;
    private readonly List<CycleRunner> _runners = new();
    private readonly ConcurrentQueue<CycleRecord> _promptQueue = new();
    private bool _promptWindowOpen;
    private readonly object _promptLock = new();

    public event Action<CycleRecord>? ShowPromptRequested;
    public event Action? SessionStateChanged;

    public SessionModel? ActiveSession => _activeSession;
    public IReadOnlyList<CycleRunner> Runners => _runners;
    public bool HasActiveSession => _activeSession != null && !_activeSession.IsCompleted;
    public bool IsPaused => _activeSession?.IsPaused ?? false;
    public int QueuedPromptCount => _promptQueue.Count;

    public SessionEngine(DatabaseService db, ScreenshotService screenshotService, CleanupService cleanupService)
    {
        _db = db;
        _screenshotService = screenshotService;
        _cleanupService = cleanupService;
    }

    public async Task StartSessionAsync(string name, List<(SessionCycleConfig config, List<DecoyOffset> decoys)> cycles)
    {
        var session = new SessionModel
        {
            Name = name,
            StartedAtUtc = DateTime.UtcNow
        };
        session.Id = await _db.CreateSessionAsync(session);
        _activeSession = session;

        foreach (var (config, decoys) in cycles)
        {
            config.SessionId = session.Id;
            config.Id = await _db.CreateConfigAsync(config);
            foreach (var d in decoys)
            {
                d.SessionCycleConfigId = config.Id;
                await _db.CreateDecoyOffsetAsync(d);
            }

            var runner = CreateRunner(config, decoys, session.Id);
            _runners.Add(runner);
            runner.Start();
        }

        SessionStateChanged?.Invoke();
    }

    public async Task PauseAsync()
    {
        if (_activeSession == null) return;
        foreach (var runner in _runners)
            runner.Pause();
        _activeSession.IsPaused = true;
        await _db.UpdateSessionPausedAsync(_activeSession.Id, true);
        SessionStateChanged?.Invoke();
    }

    public async Task ResumeAsync()
    {
        if (_activeSession == null) return;
        foreach (var runner in _runners)
            runner.Resume();
        _activeSession.IsPaused = false;
        await _db.UpdateSessionPausedAsync(_activeSession.Id, false);
        SessionStateChanged?.Invoke();
    }

    public async Task StopAsync(bool closePromptWindow = false)
    {
        if (_activeSession == null) return;

        foreach (var runner in _runners)
        {
            runner.Stop();
            // Mark in-progress cycle as incomplete
            var record = runner.CurrentRecord;
            if (record != null && record.Status != CycleStatus.Completed
                && record.Status != CycleStatus.Missed && record.Status != CycleStatus.Incomplete)
            {
                record.Status = CycleStatus.Incomplete;
                await _db.UpdateCycleRecordAsync(record);
            }
        }

        await _db.CompleteSessionAsync(_activeSession.Id);
        _activeSession.IsCompleted = true;
        _runners.Clear();
        while (_promptQueue.TryDequeue(out _)) { }
        SessionStateChanged?.Invoke();
    }

    public async Task RestoreSessionAsync(SessionModel session)
    {
        _activeSession = session;
        var configs = await _db.GetConfigsBySessionAsync(session.Id);

        foreach (var config in configs)
        {
            var decoys = await _db.GetDecoyOffsetsAsync(config.Id);
            var runner = CreateRunner(config, decoys, session.Id);
            _runners.Add(runner);

            var activeCycle = await _db.GetLatestActiveCycleAsync(config.Id);
            if (activeCycle != null)
                await runner.RestoreAsync(activeCycle);
            else
                runner.Start();
        }

        SessionStateChanged?.Invoke();
    }

    public async Task EvaluationCompleteAsync(CycleRecord record)
    {
        record.Status = CycleStatus.Completed;
        record.CompletedUtc = DateTime.UtcNow;
        await _db.UpdateCycleRecordAsync(record);
        await _cleanupService.RunAfterEvaluationAsync(record);

        lock (_promptLock)
        {
            _promptWindowOpen = false;
        }

        DequeueNextPrompt();
        SessionStateChanged?.Invoke();
    }

    public void EnqueuePrompt(CycleRecord record)
    {
        // Sort by duration descending — insert in order
        // Since ConcurrentQueue doesn't support ordered insert, we rebuild
        var list = new List<CycleRecord>(_promptQueue) { record };
        list.Sort((a, b) => b.ActualDurationTicks.CompareTo(a.ActualDurationTicks));

        while (_promptQueue.TryDequeue(out _)) { }
        foreach (var r in list) _promptQueue.Enqueue(r);

        lock (_promptLock)
        {
            if (!_promptWindowOpen)
            {
                _promptWindowOpen = true;
                DequeueNextPrompt();
            }
        }

        SessionStateChanged?.Invoke();
    }

    private void DequeueNextPrompt()
    {
        if (_promptQueue.TryDequeue(out var next))
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ShowPromptRequested?.Invoke(next);
            });
        }
    }

    private CycleRunner CreateRunner(SessionCycleConfig config, List<DecoyOffset> decoys, int sessionId)
    {
        var runner = new CycleRunner(config, decoys, _db, _screenshotService, sessionId);
        runner.PromptReady += async (record) =>
        {
            await Task.Run(() => EnqueuePrompt(record));
        };
        runner.StatusChanged += (r, status) =>
        {
            System.Diagnostics.Debug.WriteLine($"[SessionEngine] Runner {r.Config.Id}: {status}");
            SessionStateChanged?.Invoke();
        };
        return runner;
    }
}
