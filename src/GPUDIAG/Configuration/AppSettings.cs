namespace GPUDIAG.Configuration;

public sealed class AppSettings
{
    public int EventLookbackDays { get; set; } = 30;
    public bool EnableWerCollector { get; set; } = true;
    public bool EnableJavaCollector { get; set; } = true;
    public bool EnableWmiStorageCollector { get; set; } = true;

    public AppSettings Clone() => new()
    {
        EventLookbackDays = EventLookbackDays,
        EnableWerCollector = EnableWerCollector,
        EnableJavaCollector = EnableJavaCollector,
        EnableWmiStorageCollector = EnableWmiStorageCollector
    };
}
