namespace GPUDIAG.Core.Models;

public class EventLogItem
{
    public DateTime TimeCreated { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string LogName { get; set; } = string.Empty;
    public long EventId { get; set; }
    public string LevelName { get; set; } = string.Empty;
    public int Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
    public EventCategory Category { get; set; }
    public List<string> CorrelatedWith { get; set; } = new();
}

public enum EventCategory
{
    General,
    BugCheck,
    KernelPower,
    Whea,
    TdrDisplay,
    NvidiaDriver,
    Disk,
    Filesystem,
    Storage,
    ServiceControl,
    AppCrash,
    MemoryDiagnostics,
    IntelRst,
    PerformanceDiag,
    SecurityCheck
}

public class TimelineEvent
{
    public DateTime Timestamp { get; set; }
    public string Summary { get; set; } = string.Empty;
    public EventSeverity Severity { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<EventLogItem> CorrelatedEvents { get; set; } = new();
    public bool IsAnchorEvent { get; set; }
}

public enum EventSeverity
{
    Info,
    Warning,
    Error,
    Critical
}
