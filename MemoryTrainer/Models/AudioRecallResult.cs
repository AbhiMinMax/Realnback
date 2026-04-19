namespace MemoryTrainer.Models;

public class AudioRecallResult
{
    public int Id { get; set; }
    public int CycleRecordId { get; set; }
    public string RecallText { get; set; } = string.Empty;
    public EvaluationResult Result { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
}
