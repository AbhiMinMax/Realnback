namespace MemoryTrainer.Models;

public class RecognitionResult
{
    public int Id { get; set; }
    public int CycleRecordId { get; set; }
    public int SelectedScreenshotRecordId { get; set; }
    public int CorrectScreenshotRecordId { get; set; }
    public bool IsCorrect { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
}
