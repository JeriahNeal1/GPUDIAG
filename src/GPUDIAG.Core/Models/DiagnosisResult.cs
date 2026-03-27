namespace GPUDIAG.Core.Models;

public class DiagnosisResult
{
    public List<Hypothesis> TopHypotheses { get; set; } = new();
    public List<string> KeyFindings { get; set; } = new();
    public string ExecutiveSummary { get; set; } = string.Empty;
    public string RecommendedNextAction { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class Hypothesis
{
    public HypothesisType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }  // 0.0–1.0
    public string ConfidenceLabel { get; set; } = string.Empty;
    public List<string> SupportingEvidence { get; set; } = new();
    public List<string> CounterEvidence { get; set; } = new();
    public string RecommendedTest { get; set; } = string.Empty;
}

public enum HypothesisType
{
    NvidiaGpuVram,
    PcieMotherboardPower,
    CpuCacheSubsystem,
    RamImcCpu,
    StorageFilesystem,
    ThirdPartyDriver
}
