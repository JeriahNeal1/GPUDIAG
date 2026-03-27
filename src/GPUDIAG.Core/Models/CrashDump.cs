namespace GPUDIAG.Core.Models;

public class CrashDump
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long FileSizeBytes { get; set; }
    public DumpType Type { get; set; }

    // From WinDbg analysis
    public string BugCheckCode { get; set; } = string.Empty;
    public string BugCheckCodeHex { get; set; } = string.Empty;
    public string[] BugCheckParameters { get; set; } = Array.Empty<string>();
    public string ProbableCause { get; set; } = string.Empty;
    public string FaultingModule { get; set; } = string.Empty;
    public string StackSummary { get; set; } = string.Empty;
    public string AnalyzeOutput { get; set; } = string.Empty;
    public bool AnalysisSucceeded { get; set; }
    public string AnalysisError { get; set; } = string.Empty;

    // Parsed from WER
    public string WerEventName { get; set; } = string.Empty;
    public string FaultingProcess { get; set; } = string.Empty;
    public string ExceptionCode { get; set; } = string.Empty;

    public bool IsKernelSecurityCheckFailure => BugCheckCodeHex.Contains("139", StringComparison.OrdinalIgnoreCase);
    public bool IsWin32kPowerWatchdogTimeout => BugCheckCodeHex.Contains("19C", StringComparison.OrdinalIgnoreCase);
    public bool IsGraphicsRelated =>
        (FaultingModule ?? string.Empty).Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase) ||
        (FaultingModule ?? string.Empty).Contains("nvgpucomp", StringComparison.OrdinalIgnoreCase) ||
        (FaultingModule ?? string.Empty).Contains("dxgkrnl", StringComparison.OrdinalIgnoreCase) ||
        (StackSummary ?? string.Empty).Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase) ||
        IsWin32kPowerWatchdogTimeout;
}

public enum DumpType
{
    Minidump,
    Kernel,
    Full,
    Unknown
}

public class WerEntry
{
    public DateTime TimeCreated { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string FaultingProcess { get; set; } = string.Empty;
    public string FaultingModule { get; set; } = string.Empty;
    public string ExceptionCode { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string ModuleVersion { get; set; } = string.Empty;
    public string ReportFolder { get; set; } = string.Empty;
    public WerEntryType EntryType { get; set; }
    public bool IsGraphicsRelated { get; set; }
    public bool IsLiveKernelEvent => EventName.StartsWith("LiveKernelEvent", StringComparison.OrdinalIgnoreCase);
    public bool IsBlueScreen => EventName.Equals("BlueScreen", StringComparison.OrdinalIgnoreCase);
    public bool IsAppCrash => EventName.Equals("AppCrash", StringComparison.OrdinalIgnoreCase);
}

public enum WerEntryType
{
    AppCrash,
    BlueScreen,
    LiveKernelEvent,
    Other
}
