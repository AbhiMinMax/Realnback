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
        var filename = $"{cycleRecordId}_{(isMain ? "main" : $"decoy_{offsetMinutes}")}_{Guid.NewGuid():N}.png";
        var dir = Path.Combine(_screenshotsBasePath, sessionId.ToString());
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, filename);

        var screen = Screen.PrimaryScreen!.Bounds;
        var workArea = Screen.PrimaryScreen.WorkingArea;
        using var bmp = new Bitmap(screen.Width, screen.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size);

        // Black out the notification area (clock + tray icons) to prevent timestamp inference.
        // Handles each taskbar edge; falls back to 50px if taskbar is hidden/auto-hidden.
        int tbBottom = screen.Bottom - workArea.Bottom;
        int tbTop    = workArea.Top - screen.Top;
        int tbRight  = screen.Right - workArea.Right;
        int tbLeft   = workArea.Left - screen.Left;
        const int NotifWidth = 260; // wide enough to cover clock, date, and adjacent icons

        if (tbBottom > 0) // taskbar at bottom — clock is bottom-right
            g.FillRectangle(Brushes.Black, screen.Width - NotifWidth, screen.Height - tbBottom, NotifWidth, tbBottom);
        else if (tbTop > 0) // taskbar at top — clock is top-right
            g.FillRectangle(Brushes.Black, screen.Width - NotifWidth, 0, NotifWidth, tbTop);
        else if (tbRight > 0) // taskbar on right — clock is bottom of the strip
            g.FillRectangle(Brushes.Black, screen.Width - tbRight, screen.Height - NotifWidth, tbRight, NotifWidth);
        else if (tbLeft > 0) // taskbar on left — clock is bottom of the strip
            g.FillRectangle(Brushes.Black, 0, screen.Height - NotifWidth, tbLeft, NotifWidth);
        else // auto-hidden taskbar: black out bottom-right corner with a safe default
            g.FillRectangle(Brushes.Black, screen.Width - NotifWidth, screen.Height - 50, NotifWidth, 50);

        bmp.Save(fullPath, ImageFormat.Png);
        return fullPath;
    }
}
