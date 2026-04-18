namespace MemoryTrainer.Models;

public enum CycleStatus
{
    Pending,
    WaitingForScreenshot,
    ScreenshotTaken,
    PromptQueued,
    PromptShown,
    Completed,
    Missed,
    Incomplete
}

public enum EvaluationResult
{
    Correct = 0,
    Partial = 1,
    Wrong = 2
}

public enum RecallMode
{
    Free = 0,
    Recognition = 1
}
