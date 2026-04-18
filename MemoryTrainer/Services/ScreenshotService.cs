using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MemoryTrainer.Services;

public class ScreenshotService
{
    private readonly string _screenshotsBasePath;

    public ScreenshotService(string screenshotsBasePath)
    {
        _screenshotsBasePath = screenshotsBasePath;
    }

    public string Capture(int sessionId, int cycleRecordId, bool isMain, int? offsetMinutes)
    {
        var filename = $"{cycleRecordId}_{(isMain ? "main" : $"decoy_{offsetMinutes}")}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
        var dir = Path.Combine(_screenshotsBasePath, sessionId.ToString());
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, filename);

        var screen = Screen.PrimaryScreen!.Bounds;
        using var bmp = new Bitmap(screen.Width, screen.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size);
        bmp.Save(fullPath, ImageFormat.Png);
        return fullPath;
    }
}
