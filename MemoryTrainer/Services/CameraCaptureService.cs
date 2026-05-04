using AForge.Video.DirectShow;
using MemoryTrainer.Helpers;
using System.Drawing;
using System.Drawing.Imaging;

namespace MemoryTrainer.Services;

public class CameraCaptureService
{
    public string? Capture(string outputPath)
    {
        FilterInfoCollection devices;
        try
        {
            devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
        }
        catch (Exception ex)
        {
            AppLogger.Error("CameraCaptureService", "Failed to enumerate video devices", ex);
            return null;
        }

        if (devices.Count == 0)
        {
            AppLogger.Warn("CameraCaptureService", "No video capture devices found");
            return null;
        }

        Bitmap? frame = null;
        var captured = new ManualResetEventSlim(false);
        VideoCaptureDevice? device = null;

        try
        {
            device = new VideoCaptureDevice(devices[0].MonikerString);
            device.NewFrame += (_, args) =>
            {
                if (!captured.IsSet)
                {
                    frame = (Bitmap)args.Frame.Clone();
                    captured.Set();
                }
            };
            device.Start();
            captured.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            AppLogger.Error("CameraCaptureService", "Camera capture failed", ex);
        }
        finally
        {
            try { device?.SignalToStop(); device?.WaitForStop(); }
            catch (Exception ex) { AppLogger.Error("CameraCaptureService", "Failed to stop camera device", ex); }
        }

        if (frame == null)
        {
            AppLogger.Warn("CameraCaptureService", "No frame captured within 5s timeout");
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            frame.Save(outputPath, ImageFormat.Png);
            AppLogger.Log("CameraCaptureService", $"Frame saved → {outputPath}");
            return outputPath;
        }
        catch (Exception ex)
        {
            AppLogger.Error("CameraCaptureService", $"Failed to save frame to {outputPath}", ex);
            return null;
        }
        finally
        {
            frame.Dispose();
        }
    }
}
