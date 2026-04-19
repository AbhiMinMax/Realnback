namespace MemoryTrainer.Models;

public class CaptureRecord
{
    public int Id { get; set; }
    public int CycleRecordId { get; set; }
    public CaptureType Type { get; set; }
    public bool IsMain { get; set; }
    public int? DecoyOffsetMinutes { get; set; }
    public string? FilePath { get; set; }
    public DateTime? TakenAtUtc { get; set; }
    public CaptureAvailability Availability { get; set; }
    public bool IsDeleted { get; set; }
}
