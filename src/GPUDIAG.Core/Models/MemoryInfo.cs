namespace GPUDIAG.Core.Models;

public class MemoryInfo
{
    public ulong TotalPhysicalBytes { get; set; }
    public ulong AvailableBytes { get; set; }
    public List<DimmInfo> Dimms { get; set; } = new();
    public MemoryDiagResult? LastDiagResult { get; set; }
    public bool MemtestResultsAvailable { get; set; }
    public string PageFilePath { get; set; } = string.Empty;
    public ulong PageFileSizeBytes { get; set; }
    public List<EventLogItem> MemoryEvents { get; set; } = new();
    public bool RandomCrashPatternAcrossModules { get; set; }
    public bool GraphicsPathPatternDetected { get; set; }
}

public class MemoryDiagResult
{
    public DateTime RunTime { get; set; }
    public string ResultText { get; set; } = string.Empty;
    public bool ErrorsFound { get; set; }
    public string Details { get; set; } = string.Empty;
}
