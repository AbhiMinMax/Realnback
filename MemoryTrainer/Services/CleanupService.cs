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
        var screenshots = await _db.GetScreenshotsByCycleConfigAsync(cycleRecord.SessionCycleConfigId);

        foreach (var screenshot in screenshots.Where(s => s.TakenAtUtc < cutoffUtc && !s.IsDeleted))
        {
            try
            {
                if (File.Exists(screenshot.FilePath))
                    File.Delete(screenshot.FilePath);
                await _db.MarkScreenshotDeletedAsync(screenshot.Id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupService] Failed to delete screenshot {screenshot.Id}: {ex.Message}");
            }
        }
    }

    public async Task DeleteAllScreenshotsAsync(string screenshotsBasePath)
    {
        if (Directory.Exists(screenshotsBasePath))
        {
            foreach (var file in Directory.EnumerateFiles(screenshotsBasePath, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CleanupService] Failed to delete {file}: {ex.Message}");
                }
            }
        }
    }
}
