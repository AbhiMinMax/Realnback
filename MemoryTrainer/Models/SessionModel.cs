namespace MemoryTrainer.Models;

public class SessionModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsPaused { get; set; }
    public DateTime? PausedAtUtc { get; set; }
}
