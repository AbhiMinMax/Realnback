namespace MemoryTrainer.Models;

public class PlanRecord
{
    public int Id { get; set; }
    public int CycleRecordId { get; set; }
    public string? PriorPlanText { get; set; }
    public string? RecalledText { get; set; }
    public bool? SelfRatedCorrect { get; set; }
    public string NextPlanText { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
