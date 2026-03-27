namespace GPUDIAG.Core.Models;

public class DiagnosticReport
{
    public SystemInfo? System { get; set; }
    public List<EventLogItem> Events { get; set; } = new();
    public List<TimelineEvent> Timeline { get; set; } = new();
    public List<WheaEvent> WheaEvents { get; set; } = new();
    public List<CrashDump> CrashDumps { get; set; } = new();
    public List<WerEntry> WerEntries { get; set; } = new();
    public List<JavaCrashLog> JavaCrashLogs { get; set; } = new();
    public StorageEvidence? Storage { get; set; }
    public MemoryInfo? Memory { get; set; }
    public DiagnosisResult? Diagnosis { get; set; }
    public List<string> CollectionErrors { get; set; } = new();
    public List<string> CollectionWarnings { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string ScanType { get; set; } = "Quick";

    // Summary helpers
    public int GraphicsEventCount => Events.Count(e =>
        e.Category is EventCategory.TdrDisplay or EventCategory.NvidiaDriver);
    public int WheaPcieCount => WheaEvents.Count(w =>
        w.ClassifiedSubsystem is WheaSubsystem.PcieRootPort or WheaSubsystem.GpuDevice);
    public int BugCheckCount => Events.Count(e => e.Category == EventCategory.BugCheck);
}
