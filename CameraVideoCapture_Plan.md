# Camera Video Capture — Implementation Plan

## What to add
A `CameraVideoEnabled` flag per `SessionCycleConfig`. When true, capture a 5-second video clip instead of a single photo. Photo capture code stays untouched; video is opt-in.

## NuGet dependency
Drop `AForge.Video.DirectShow` for video encoding — it has no built-in video writer.
Add `OpenCvSharp4.Windows` instead (wraps OpenCV, actively maintained on .NET 9, handles both photo and video).
Keep `AForge.Video.DirectShow` only if photo path is kept on that library; otherwise unify on OpenCvSharp.

Alternatively: keep AForge for frame capture + add `AForge.Video.FFMPEG` for writing — but AForge.Video.FFMPEG is abandoned and unreliable on .NET 9. Avoid.

Recommended stack: OpenCvSharp4.Windows for everything camera-related.

## DB schema change
```sql
ALTER TABLE SessionCycleConfigs ADD COLUMN CameraVideoEnabled INTEGER NOT NULL DEFAULT 0;
```

## CameraCaptureService changes
Add `CaptureVideoAsync(string outputPath, int durationSeconds = 5)`:
```csharp
public string? CaptureVideo(string outputPath, int durationSeconds = 5)
{
    // open first VideoCapture device
    using var cap = new VideoCapture(0);
    if (!cap.IsOpened()) return null;

    double fps = cap.Get(VideoCaptureProperties.Fps);
    if (fps <= 0) fps = 15;
    var size = new OpenCvSharp.Size((int)cap.Get(VideoCaptureProperties.FrameWidth),
                                    (int)cap.Get(VideoCaptureProperties.FrameHeight));

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    using var writer = new VideoWriter(outputPath, FourCC.MP4V, fps, size);
    if (!writer.IsOpened()) return null;

    int totalFrames = (int)(fps * durationSeconds);
    using var frame = new Mat();
    for (int i = 0; i < totalFrames; i++)
    {
        cap.Read(frame);
        if (!frame.Empty()) writer.Write(frame);
    }
    return outputPath;
}
```

Existing `Capture(string outputPath)` photo method stays unchanged.

## CycleRunner changes
- When `_config.CameraVideoEnabled` is true, call `CaptureVideo(path)` instead of `Capture(path)`.
- File path: `{cameraPath}/{sessionId}/{cycleRecordId}_camera_{guid}.mp4`
- Same `CaptureRecord` model, same `Availability` logic.

## SessionCycleConfig model
```csharp
public bool CameraVideoEnabled { get; set; }  // NEW — false = photo (default), true = video
```

## Config UI
Add a toggle/checkbox "Video (5s)" next to the Camera toggle. Only enabled when CameraEnabled is on.

## Recall prompt — camera tab
After Verify, currently shows "Open Photo" (Process.Start). When the capture is a video:
- Show "▶ Play Video" button (Process.Start with .mp4 — opens in Windows Media Player / Films & TV)
- Self-evaluation: same Correct/Partial/Wrong buttons

Detect whether a capture is video by checking file extension (`.mp4`) or add a `CaptureSubType` field.
Simplest: check `capture.FilePath?.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)`.

## Complexity summary
- ~2-3 hours total
- Largest risk: OpenCvSharp codec availability on target machine (MP4V codec needs Windows Media Feature Pack on N editions). Fallback: write as AVI (lossless, bigger). Use `FourCC.MJPG` or `FourCC.DIVX` for better compatibility.
- No schema migration needed beyond one ALTER TABLE (already handled by the DB init pattern).
- No changes to scoring, history, or aggregation logic.

## Files to touch
1. `MemoryTrainer.csproj` — add OpenCvSharp4.Windows package
2. `Services/CameraCaptureService.cs` — add `CaptureVideo` method, switch from AForge to OpenCvSharp (or keep both)
3. `Services/CycleRunner.cs` — branch on `CameraVideoEnabled` flag
4. `Models/SessionCycleConfig.cs` — add `CameraVideoEnabled` bool
5. `Services/DatabaseService.cs` — add column in init SQL + map property
6. `Views/ConfigView.xaml` — add toggle
7. `ViewModels/ConfigViewModel.cs` — wire toggle
8. `Views/RecallPromptWindow.xaml` — show Play Video vs Open Photo based on file extension
