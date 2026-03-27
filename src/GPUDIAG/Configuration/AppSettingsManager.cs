using System.Text.Json;

namespace GPUDIAG.Configuration;

public static class AppSettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GPUDIAG",
        "config.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new AppSettings();

            var json = File.ReadAllText(ConfigPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings == null)
                return new AppSettings();

            settings.EventLookbackDays = Math.Clamp(settings.EventLookbackDays, 1, 365);
            settings.TimelineCategory = string.IsNullOrWhiteSpace(settings.TimelineCategory)
                ? "All"
                : settings.TimelineCategory;
            settings.ThemeMode = string.IsNullOrWhiteSpace(settings.ThemeMode)
                ? "Dark"
                : settings.ThemeMode;
            settings.AccentColorHex = string.IsNullOrWhiteSpace(settings.AccentColorHex)
                ? "#00D4FF"
                : settings.AccentColorHex;
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        settings.EventLookbackDays = Math.Clamp(settings.EventLookbackDays, 1, 365);

        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
