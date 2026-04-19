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

public enum CaptureType
{
    Screenshot = 0,
    Audio = 1,
    Camera = 2
}

public enum CaptureAvailability
{
    Captured = 0,
    Skipped_DeviceUnavailable = 1,
    Skipped_NoAudio = 2,
    Skipped_AppClosed = 3
}
