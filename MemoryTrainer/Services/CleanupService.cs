using MemoryTrainer.Helpers;
using MemoryTrainer.Models;

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

        foreach (var capture in captures.Where(c => c.TakenAtUtc < cutoffUtc && !c.IsDeleted))
        {
            try
            {
                if (capture.FilePath != null && File.Exists(capture.FilePath))
                    File.Delete(capture.FilePath);
                await _db.MarkCaptureDeletedAsync(capture.Id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupService] Failed to delete capture {capture.Id}: {ex.Message}");
            }
        }
    }

    public async Task DeleteSessionCaptureFilesAsync(int sessionId)
    {
        var captures = await _db.GetCapturesBySessionAsync(sessionId);
        foreach (var capture in captures)
        {
            try
            {
                if (capture.FilePath != null && File.Exists(capture.FilePath))
                    File.Delete(capture.FilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupService] Failed to delete capture file {capture.FilePath}: {ex.Message}");
            }
            await _db.MarkCaptureDeletedAsync(capture.Id);
        }
    }

    public async Task DeleteAllCapturedMediaAsync()
    {
        DeleteFolder(PathHelper.ScreenshotsPath);
        DeleteFolder(PathHelper.AudioPath);
        DeleteFolder(PathHelper.CameraPath);

        // Recreate empty folders so the app continues to function
        Directory.CreateDirectory(PathHelper.ScreenshotsPath);
        Directory.CreateDirectory(PathHelper.AudioPath);
        Directory.CreateDirectory(PathHelper.CameraPath);

        await _db.MarkAllCapturesDeletedAsync();
    }

    private static void DeleteFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupService] Failed to delete {file}: {ex.Message}");
            }
        }
    }
}
