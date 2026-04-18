namespace MemoryTrainer.Models;

public class ScreenshotRecord
{
    public int Id { get; set; }
    public int CycleRecordId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime TakenAtUtc { get; set; }
    public bool IsMain { get; set; }
    public int? OffsetMinutes { get; set; }
    public bool IsDeleted { get; set; }
}
