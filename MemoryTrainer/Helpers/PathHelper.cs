namespace MemoryTrainer.Helpers;

public static class PathHelper
{
    public static string AppFolder => AppContext.BaseDirectory;
    public static string DataFolder => Path.Combine(AppFolder, "data");
    public static string DatabasePath => Path.Combine(DataFolder, "memorytrainer.db");
    public static string ScreenshotsPath => Path.Combine(DataFolder, "screenshots");
    public static string ErrorLogPath => Path.Combine(DataFolder, "error.log");

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(ScreenshotsPath);
    }
}
