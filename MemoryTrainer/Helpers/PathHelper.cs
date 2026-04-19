using System.Text.Json;

namespace MemoryTrainer.Helpers;

public static class PathHelper
{
    public static string AppFolder => AppContext.BaseDirectory;
    public static string DataFolder => Path.Combine(AppFolder, "data");
    public static string DatabasePath => Path.Combine(DataFolder, "memorytrainer.db");
    public static string ErrorLogPath => Path.Combine(DataFolder, "error.log");
    public static string SettingsFilePath => Path.Combine(DataFolder, "appsettings.json");

    private static string? _screenshotsPathOverride;
    private static string? _defaultSessionNameTemplate;

    public static string ScreenshotsPath
    {
        get => _screenshotsPathOverride ?? Path.Combine(DataFolder, "screenshots");
        set
        {
            _screenshotsPathOverride = string.IsNullOrWhiteSpace(value) ? null : value;
            Directory.CreateDirectory(ScreenshotsPath);
            SaveSettings();
        }
    }

    public static string DefaultSessionNameTemplate
    {
        get => _defaultSessionNameTemplate ?? "Session — {date} {time}";
        set
        {
            _defaultSessionNameTemplate = string.IsNullOrWhiteSpace(value) ? null : value;
            SaveSettings();
        }
    }

    public static string AudioPath => Path.Combine(DataFolder, "audio");
    public static string CameraPath => Path.Combine(DataFolder, "camera");

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataFolder);
        LoadSettings();
        Directory.CreateDirectory(ScreenshotsPath);
        Directory.CreateDirectory(AudioPath);
        Directory.CreateDirectory(CameraPath);
    }

    public static void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return;
            var json = File.ReadAllText(SettingsFilePath);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ScreenshotsPath", out var el))
            {
                var val = el.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                    _screenshotsPathOverride = val;
            }
            if (doc.RootElement.TryGetProperty("DefaultSessionNameTemplate", out var el2))
            {
                var val = el2.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                    _defaultSessionNameTemplate = val;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PathHelper] Failed to load settings: {ex.Message}");
        }
    }

    public static void SaveSettings()
    {
        try
        {
            var settings = new { ScreenshotsPath = _screenshotsPathOverride, DefaultSessionNameTemplate = _defaultSessionNameTemplate };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PathHelper] Failed to save settings: {ex.Message}");
        }
    }
}
