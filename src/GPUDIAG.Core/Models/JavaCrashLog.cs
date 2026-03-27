namespace GPUDIAG.Core.Models;

public class JavaCrashLog
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime FileDate { get; set; }
    public string JvmVersion { get; set; } = string.Empty;
    public string ExceptionCode { get; set; } = string.Empty;
    public string ProblematicFrame { get; set; } = string.Empty;
    public string ProblematicModule { get; set; } = string.Empty;
    public bool InNativeGraphicsCode { get; set; }
    public bool HasNvgpucomp { get; set; }
    public bool HasNvlddmkm { get; set; }
    public bool IsAccessViolation { get; set; }
    public List<string> StackLines { get; set; } = new();
    public string RawHeader { get; set; } = string.Empty;
    public string ErrorDetails { get; set; } = string.Empty;
}
