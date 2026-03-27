namespace GPUDIAG.Configuration;

public sealed class AppSettings
{
    public int EventLookbackDays { get; set; } = 30;
    public bool EnableWerCollector { get; set; } = true;
    public bool EnableJavaCollector { get; set; } = true;
    public bool EnableWmiStorageCollector { get; set; } = true;
    public string TimelineCategory { get; set; } = "All";
    public string ThemeMode { get; set; } = "Dark";
    public string AccentColorHex { get; set; } = "#00D4FF";

    public AppSettings Clone() => new()
    {
        EventLookbackDays = EventLookbackDays,
        EnableWerCollector = EnableWerCollector,
        EnableJavaCollector = EnableJavaCollector,
        EnableWmiStorageCollector = EnableWmiStorageCollector,
        TimelineCategory = TimelineCategory,
        ThemeMode = ThemeMode,
        AccentColorHex = AccentColorHex
    };
}
