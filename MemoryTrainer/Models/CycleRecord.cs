namespace MemoryTrainer.Models;

public class CycleRecord
{
    public int Id { get; set; }
    public int SessionCycleConfigId { get; set; }
    public int CycleNumber { get; set; }
    public long ActualDurationTicks { get; set; }
    public DateTime CycleStartUtc { get; set; }
    public DateTime ScheduledScreenshotUtc { get; set; }
    public DateTime? ScreenshotTakenUtc { get; set; }
    public DateTime? PromptDueUtc { get; set; }
    public DateTime? PromptShownUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public CycleStatus Status { get; set; }
    public string? MissedReason { get; set; }
}
