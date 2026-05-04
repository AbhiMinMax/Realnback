using MemoryTrainer.Helpers;
using MemoryTrainer.Models;
using static MemoryTrainer.Helpers.AppLogger;

namespace MemoryTrainer.Services;

public class CleanupService
{
    private readonly DatabaseService _db;

    public CleanupService(DatabaseService db)
    {
        _db = db;
    }

    public async Task RunAfterEvaluationAsync(CycleRecord cycleRecord)
    {
        if (cycleRecord.ScreenshotTakenUtc == null) return;

        var cutoffUtc = cycleRecord.ScreenshotTakenUtc.Value - TimeSpan.FromTicks(4 * cycleRecord.ActualDurationTicks);
        var captures = await _db.GetCapturesByConfigAsync(cycleRecord.SessionCycleConfigId);
        var toDelete = captures.Where(c => c.TakenAtUtc < cutoffUtc && !c.IsDeleted).ToList();

        if (toDelete.Count > 0)
            Log("CleanupService", $"Deleting {toDelete.Count} capture(s) older than {cutoffUtc:HH:mm:ss}Z (cycle {cycleRecord.Id})");

        foreach (var capture in toDelete)
        {
            try
            {
                if (capture.FilePath != null && File.Exists(capture.FilePath))
                    File.Delete(capture.FilePath);
                await _db.MarkCaptureDeletedAsync(capture.Id);
            }
            catch (Exception ex)
            {
                Error("CleanupService", $"Failed to delete capture {capture.Id} ({capture.FilePath})", ex);
            }
        }
    }

    public async Task DeleteSessionCaptureFilesAsync(int sessionId)
    {
        var captures = await _db.GetCapturesBySessionAsync(sessionId);
        Log("CleanupService", $"Deleting {captures.Count} capture file(s) for session {sessionId}");
        foreach (var capture in captures)
        {
            try
            {
                if (capture.FilePath != null && File.Exists(capture.FilePath))
                    File.Delete(capture.FilePath);
            }
            catch (Exception ex)
            {
                Error("CleanupService", $"Failed to delete capture file {capture.FilePath}", ex);
            }
            await _db.MarkCaptureDeletedAsync(capture.Id);
        }
    }

    public async Task DeleteAllCapturedMediaAsync()
    {
        Log("CleanupService", "Deleting all captured media");
        DeleteFolder(PathHelper.ScreenshotsPath);
        DeleteFolder(PathHelper.AudioPath);
        DeleteFolder(PathHelper.CameraPath);

        Directory.CreateDirectory(PathHelper.ScreenshotsPath);
        Directory.CreateDirectory(PathHelper.AudioPath);
        Directory.CreateDirectory(PathHelper.CameraPath);

        await _db.MarkAllCapturesDeletedAsync();
        Log("CleanupService", "All captured media deleted");
    }

    private static void DeleteFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); }
            catch (Exception ex)
            {
                Error("CleanupService", $"Failed to delete file {file}", ex);
            }
        }
    }
}
