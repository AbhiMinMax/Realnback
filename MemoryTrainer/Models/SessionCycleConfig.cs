namespace MemoryTrainer.Models;

public class SessionCycleConfig
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public long BaseDurationTicks { get; set; }
    public long WaitingWindowTicks { get; set; }
    public int DriftMinutes { get; set; }
    public bool FreeRecallEnabled { get; set; }
    public bool RecognitionEnabled { get; set; }
    // Decoy offsets stored in separate table, linked by SessionCycleConfigId
}
