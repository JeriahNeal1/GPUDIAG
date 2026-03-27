namespace GPUDIAG.Core.Models;

public class WheaEvent
{
    public DateTime TimeCreated { get; set; }
    public int EventId { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RawXml { get; set; } = string.Empty;
    public WheaSubsystem ClassifiedSubsystem { get; set; }
    public string ClassificationReason { get; set; } = string.Empty;
    public bool ClassificationUncertain { get; set; }
    public string ErrorSource { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string BusAddress { get; set; } = string.Empty;
}

public enum WheaSubsystem
{
    Unknown,
    PcieRootPort,
    GpuDevice,
    MemorySubsystem,
    CpuCache,
    StorageController,
    Platform
}
