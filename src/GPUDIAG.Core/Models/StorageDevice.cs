namespace GPUDIAG.Core.Models;

public class StorageDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public ulong SizeBytes { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public SmartHealth SmartHealth { get; set; } = new();
    public List<PartitionInfo> Partitions { get; set; } = new();
}

public class SmartHealth
{
    public bool Available { get; set; }
    public string OverallStatus { get; set; } = string.Empty;
    public bool CriticalWarning { get; set; }
    public int? TemperatureCelsius { get; set; }
    public ulong? PowerOnHours { get; set; }
    public ulong? DataUnitsWritten { get; set; }
    public ulong? MediaErrors { get; set; }
    public string RawOutput { get; set; } = string.Empty;
    public bool IsNvme { get; set; }
}

public class PartitionInfo
{
    public string DriveLetter { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public ulong TotalBytes { get; set; }
    public ulong FreeBytes { get; set; }
    public bool IsDirty { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class StorageEvidence
{
    public List<StorageDevice> Devices { get; set; } = new();
    public List<EventLogItem> DiskEvents { get; set; } = new();
    public bool SfcFailed { get; set; }
    public string SfcFailureReason { get; set; } = string.Empty;
    public bool SmartCriticalWarningFound { get; set; }
    public bool DirtyFilesystemFound { get; set; }
    public bool PrimaryStorageFailureLikely { get; set; }
    public string PrimaryVsSecondaryAssessment { get; set; } = string.Empty;
}
