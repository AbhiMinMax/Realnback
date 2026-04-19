using AForge.Video.DirectShow;
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
            System.Diagnostics.Debug.WriteLine($"[CameraCaptureService] Failed to enumerate devices: {ex.Message}");
            return null;
        }

        if (devices.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[CameraCaptureService] No video capture devices found");
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
            System.Diagnostics.Debug.WriteLine($"[CameraCaptureService] Capture failed: {ex.Message}");
        }
        finally
        {
            try { device?.SignalToStop(); device?.WaitForStop(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CameraCaptureService] Stop failed: {ex.Message}"); }
        }

        if (frame == null)
        {
            System.Diagnostics.Debug.WriteLine("[CameraCaptureService] No frame captured within timeout");
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            frame.Save(outputPath, ImageFormat.Png);
            return outputPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraCaptureService] Failed to save frame: {ex.Message}");
            return null;
        }
        finally
        {
            frame.Dispose();
        }
    }
}
