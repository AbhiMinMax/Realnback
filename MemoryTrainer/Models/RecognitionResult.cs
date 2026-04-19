namespace MemoryTrainer.Models;

public class RecognitionResult
{
    public int Id { get; set; }
    public int CycleRecordId { get; set; }
    public int SelectedCaptureRecordId { get; set; }
    public int CorrectCaptureRecordId { get; set; }
    public bool IsCorrect { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
}
