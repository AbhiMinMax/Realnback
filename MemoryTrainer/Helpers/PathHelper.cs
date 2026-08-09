using System.Text.Json;

namespace MemoryTrainer.Helpers;

public static class PathHelper
{
    public static string AppFolder => AppContext.BaseDirectory;
    public static string DataFolder => Path.Combine(AppFolder, "data");
    public static string DatabasePath => Path.Combine(DataFolder, "memorytrainer.db");
    public static string ErrorLogPath => Path.Combine(DataFolder, "error.log");
    public static string AppLogPath => Path.Combine(DataFolder, "app.log");
    public static string SettingsFilePath => Path.Combine(DataFolder, "appsettings.json");

    private static string? _screenshotsPathOverride;
    private static string? _defaultSessionNameTemplate;
    private static int _planRevealDelaySeconds = 5;
    private static int _minRecallCharacters = 50;

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

    public static int PlanRevealDelaySeconds
    {
        get => _planRevealDelaySeconds;
        set
        {
            _planRevealDelaySeconds = value < 1 ? 1 : value;
            SaveSettings();
        }
    }

    public static int MinRecallCharacters
    {
        get => _minRecallCharacters;
        set
        {
            _minRecallCharacters = value < 0 ? 0 : value;
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
            if (doc.RootElement.TryGetProperty("PlanRevealDelaySeconds", out var el3) && el3.TryGetInt32(out var delay) && delay >= 1)
            {
                _planRevealDelaySeconds = delay;
            }
            if (doc.RootElement.TryGetProperty("MinRecallCharacters", out var el4) && el4.TryGetInt32(out var minChars) && minChars >= 0)
            {
                _minRecallCharacters = minChars;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("PathHelper", "Failed to load settings", ex);
        }
    }

    public static void SaveSettings()
    {
        try
        {
            var settings = new { ScreenshotsPath = _screenshotsPathOverride, DefaultSessionNameTemplate = _defaultSessionNameTemplate, PlanRevealDelaySeconds = _planRevealDelaySeconds, MinRecallCharacters = _minRecallCharacters };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PathHelper", "Failed to save settings", ex);
        }
    }
}
