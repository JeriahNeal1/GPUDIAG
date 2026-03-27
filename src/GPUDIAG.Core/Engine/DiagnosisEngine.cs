using GPUDIAG.Core.Models;

namespace GPUDIAG.Core.Engine;

/// <summary>
/// Heuristic scoring engine that ranks root-cause hypotheses based on collected evidence.
/// </summary>
public class DiagnosisEngine
{
    private readonly DiagnosticReport _report;

    public DiagnosisEngine(DiagnosticReport report)
    {
        _report = report;
    }

    public DiagnosisResult Analyze()
    {
        var scores = new Dictionary<HypothesisType, double>
        {
            [HypothesisType.NvidiaGpuVram] = 0.0,
            [HypothesisType.PcieMotherboardPower] = 0.0,
            [HypothesisType.RamImcCpu] = 0.0,
            [HypothesisType.StorageFilesystem] = 0.0,
            [HypothesisType.ThirdPartyDriver] = 0.0
        };

        var evidence = new Dictionary<HypothesisType, (List<string> For, List<string> Against)>();
        foreach (var k in scores.Keys)
            evidence[k] = (new List<string>(), new List<string>());

        ScoreNvidiaGpu(scores, evidence);
        ScorePcie(scores, evidence);
        ScoreRam(scores, evidence);
        ScoreStorage(scores, evidence);
        ScoreThirdPartyDriver(scores, evidence);

        // Normalize so max is ~1.0, but use a minimum denominator so weak evidence
        // doesn't get artificially inflated to high confidence.
        // A "strong evidence" set (e.g., 5 TDR events + nvgpucomp dump) scores ~20+.
        // SFC failure alone scores 0.5, which should normalize to ~0.025 if other evidence is present.
        const double MinNormDenominator = 15.0;
        double max = Math.Max(scores.Values.Max(), MinNormDenominator);
        foreach (var key in scores.Keys.ToList())
            scores[key] = Math.Min(scores[key] / max, 1.0);

        var hypotheses = scores
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new Hypothesis
            {
                Type = kv.Key,
                Name = GetHypothesisName(kv.Key),
                ConfidenceScore = Math.Round(kv.Value, 2),
                ConfidenceLabel = GetConfidenceLabel(kv.Value),
                SupportingEvidence = evidence[kv.Key].For,
                CounterEvidence = evidence[kv.Key].Against,
                RecommendedTest = GetRecommendedTest(kv.Key)
            })
            .ToList();

        var result = new DiagnosisResult
        {
            TopHypotheses = hypotheses,
            KeyFindings = BuildKeyFindings(),
            ExecutiveSummary = BuildExecutiveSummary(hypotheses),
            RecommendedNextAction = hypotheses.FirstOrDefault()?.RecommendedTest ?? "Collect more evidence.",
            GeneratedAt = DateTime.UtcNow
        };

        return result;
    }

    // ─── Individual hypothesis scorers ─────────────────────────────────────────

    private void ScoreNvidiaGpu(
        Dictionary<HypothesisType, double> scores,
        Dictionary<HypothesisType, (List<string> For, List<string> Against)> evidence)
    {
        var key = HypothesisType.NvidiaGpuVram;
        var (forList, against) = evidence[key];

        // TDR / display reset events
        int tdrCount = _report.Events.Count(e => e.Category == EventCategory.TdrDisplay);
        if (tdrCount > 0)
        {
            scores[key] += 3.0 * Math.Min(tdrCount, 5);
            forList.Add($"{tdrCount} TDR/display reset event(s) found in event log.");
        }

        // NVIDIA driver events
        int nvidiaEvents = _report.Events.Count(e => e.Category == EventCategory.NvidiaDriver);
        if (nvidiaEvents > 0)
        {
            scores[key] += 2.5 * Math.Min(nvidiaEvents, 5);
            forList.Add($"{nvidiaEvents} NVIDIA driver event(s) in event log.");
        }

        // Crash dumps with graphics modules
        int graphicsDumps = _report.CrashDumps.Count(d => d.IsGraphicsRelated);
        if (graphicsDumps > 0)
        {
            scores[key] += 4.0 * Math.Min(graphicsDumps, 3);
            forList.Add($"{graphicsDumps} crash dump(s) implicate graphics modules.");
        }

        // 0x19C WIN32K_POWER_WATCHDOG_TIMEOUT
        int watchdog19c = _report.CrashDumps.Count(d => d.IsWin32kPowerWatchdogTimeout);
        if (watchdog19c > 0)
        {
            scores[key] += 5.0;
            forList.Add($"{watchdog19c} dump(s) show 0x19C WIN32K_POWER_WATCHDOG_TIMEOUT — strongly implicates display/graphics power path.");
        }

        // WER LiveKernelEvent 141 / 117 (graphics TDR)
        int lke = _report.WerEntries.Count(w =>
            w.IsLiveKernelEvent &&
            (w.EventName.Contains("141") || w.EventName.Contains("117")));
        if (lke > 0)
        {
            scores[key] += 3.5 * Math.Min(lke, 3);
            forList.Add($"{lke} LiveKernelEvent 141/117 WER entries (graphics TDR).");
        }

        // Java crash with nvgpucomp64.dll
        int javaGpu = _report.JavaCrashLogs.Count(j => j.HasNvgpucomp || j.InNativeGraphicsCode);
        if (javaGpu > 0)
        {
            scores[key] += 3.0 * Math.Min(javaGpu, 3);
            forList.Add($"{javaGpu} Java crash log(s) with native GPU code (nvgpucomp64.dll or similar).");
        }

        // WER AppCrash with STATUS_ACCESS_VIOLATION in graphics process
        int werGraphics = _report.WerEntries.Count(w => w.IsGraphicsRelated);
        if (werGraphics > 0)
        {
            scores[key] += 2.0 * Math.Min(werGraphics, 3);
            forList.Add($"{werGraphics} WER crash entry(ies) implicating graphics modules.");
        }

        // GPU detected as NVIDIA
        bool hasNvidia = _report.System?.Gpus.Any(g => g.IsNvidia) == true;
        if (hasNvidia)
            forList.Add("NVIDIA GPU detected in system inventory.");
        else
            against.Add("No NVIDIA GPU detected (or inventory not collected).");

        // No TDR or graphics events
        if (tdrCount == 0 && nvidiaEvents == 0 && graphicsDumps == 0)
            against.Add("No direct TDR/display or GPU-specific events found.");
    }

    private void ScorePcie(
        Dictionary<HypothesisType, double> scores,
        Dictionary<HypothesisType, (List<string> For, List<string> Against)> evidence)
    {
        var key = HypothesisType.PcieMotherboardPower;
        var (forList, against) = evidence[key];

        // WHEA PCIe/root-port events
        int wheaPcie = _report.WheaEvents.Count(w =>
            w.ClassifiedSubsystem is WheaSubsystem.PcieRootPort or WheaSubsystem.GpuDevice);
        if (wheaPcie > 0)
        {
            scores[key] += 5.0 * Math.Min(wheaPcie, 4);
            forList.Add($"{wheaPcie} WHEA event(s) classified as PCIe/root-port or GPU device.");
        }

        // Any WHEA events at all
        int totalWhea = _report.WheaEvents.Count;
        if (totalWhea > 0 && wheaPcie == 0)
        {
            scores[key] += 1.0;
            forList.Add($"{totalWhea} WHEA event(s) found (subsystem undetermined).");
        }

        // Kernel-Power events (power delivery issues)
        int kernelPower = _report.Events.Count(e => e.Category == EventCategory.KernelPower);
        if (kernelPower > 0)
        {
            scores[key] += 2.0 * Math.Min(kernelPower, 3);
            forList.Add($"{kernelPower} Kernel-Power event(s) (possible power delivery issue).");
        }

        // 0x19C subtype 0x50 specifically implicates display/power
        bool has19c = _report.CrashDumps.Any(d => d.IsWin32kPowerWatchdogTimeout);
        if (has19c)
        {
            scores[key] += 3.0;
            forList.Add("0x19C WIN32K_POWER_WATCHDOG_TIMEOUT detected — subtype 0x50 variant implicated in display power path on this machine.");
        }

        if (wheaPcie == 0 && kernelPower == 0 && totalWhea == 0)
            against.Add("No WHEA or Kernel-Power events collected.");
    }

    private void ScoreRam(
        Dictionary<HypothesisType, double> scores,
        Dictionary<HypothesisType, (List<string> For, List<string> Against)> evidence)
    {
        var key = HypothesisType.RamImcCpu;
        var (forList, against) = evidence[key];

        // Memory diagnostics errors
        if (_report.Memory?.LastDiagResult?.ErrorsFound == true)
        {
            scores[key] += 6.0;
            forList.Add($"Windows Memory Diagnostic reported errors: {_report.Memory.LastDiagResult.ResultText}");
        }

        // WHEA memory events
        int wheaMem = _report.WheaEvents.Count(w => w.ClassifiedSubsystem == WheaSubsystem.MemorySubsystem);
        if (wheaMem > 0)
        {
            scores[key] += 4.0 * Math.Min(wheaMem, 3);
            forList.Add($"{wheaMem} WHEA event(s) implicate memory subsystem.");
        }

        // Random access violations across unrelated modules (pattern of generalized corruption)
        bool randomPattern = _report.Memory?.RandomCrashPatternAcrossModules == true;
        if (randomPattern)
        {
            scores[key] += 2.5;
            forList.Add("Crash pattern suggests random module faults across unrelated code paths — consistent with RAM corruption.");
        }

        // 0x139 KERNEL_SECURITY_CHECK_FAILURE can indicate memory corruption
        int ksecDumps = _report.CrashDumps.Count(d => d.IsKernelSecurityCheckFailure);
        if (ksecDumps > 0)
        {
            scores[key] += 1.5;
            forList.Add($"{ksecDumps} dump(s) with 0x139 KERNEL_SECURITY_CHECK_FAILURE (can indicate memory corruption, but not conclusive).");
        }

        // No memtest results — note uncertainty
        if (_report.Memory?.MemtestResultsAvailable != true)
            against.Add("MemTest86 results not available — RAM cannot be conclusively ruled in or out without extended testing.");

        // Strong graphics path pattern actually argues against RAM
        bool strongGpu = _report.CrashDumps.Any(d => d.IsGraphicsRelated) ||
                         _report.Events.Any(e => e.Category == EventCategory.TdrDisplay);
        if (strongGpu && wheaMem == 0 && _report.Memory?.LastDiagResult?.ErrorsFound != true)
            against.Add("Crash pattern is more consistent with graphics-path failure than generalized RAM corruption.");
    }

    private void ScoreStorage(
        Dictionary<HypothesisType, double> scores,
        Dictionary<HypothesisType, (List<string> For, List<string> Against)> evidence)
    {
        var key = HypothesisType.StorageFilesystem;
        var (forList, against) = evidence[key];

        // SMART critical warnings
        bool smartCritical = _report.Storage?.SmartCriticalWarningFound == true;
        if (smartCritical)
        {
            scores[key] += 5.0;
            forList.Add("SMART/NVMe health reports a critical warning — storage health is compromised.");
        }

        // Disk / filesystem events
        int diskEvents = _report.Events.Count(e => e.Category is EventCategory.Disk or EventCategory.Filesystem or EventCategory.Storage);
        if (diskEvents > 0)
        {
            scores[key] += 2.0 * Math.Min(diskEvents, 5);
            forList.Add($"{diskEvents} disk/filesystem/storage event(s) in event log.");
        }

        // Dirty filesystem
        bool dirty = _report.Storage?.DirtyFilesystemFound == true;
        if (dirty)
        {
            scores[key] += 2.0;
            forList.Add("Dirty filesystem flag detected — filesystem was not cleanly unmounted.");
        }

        // SFC failure alone does NOT call storage primary
        if (_report.Storage?.SfcFailed == true)
        {
            forList.Add("SFC /scannow failed — but this alone does NOT confirm primary storage failure (may be secondary crash fallout).");
            // Actually add a small penalty for overcalling storage from SFC alone
            if (!smartCritical && diskEvents == 0 && !dirty)
            {
                scores[key] += 0.5;  // very small bump — not primary evidence
                against.Add("SFC failure without corroborating SMART/disk-event evidence is more likely secondary crash damage than primary storage failure.");
            }
        }

        // No SMART critical, no disk events, no dirty FS
        if (!smartCritical && diskEvents == 0 && !dirty)
            against.Add("No SMART critical warning, no disk events, and no dirty filesystem detected — storage primary failure is unlikely.");

        // Strong graphics pattern argues against storage as primary
        bool strongGpu = _report.CrashDumps.Any(d => d.IsGraphicsRelated) ||
                         _report.Events.Any(e => e.Category == EventCategory.TdrDisplay) ||
                         _report.WheaEvents.Any(w => w.ClassifiedSubsystem is WheaSubsystem.PcieRootPort or WheaSubsystem.GpuDevice);
        if (strongGpu)
            against.Add("Strong GPU/PCIe evidence present — storage damage is more likely secondary fallout from repeated GPU crashes.");
    }

    private void ScoreThirdPartyDriver(
        Dictionary<HypothesisType, double> scores,
        Dictionary<HypothesisType, (List<string> For, List<string> Against)> evidence)
    {
        var key = HypothesisType.ThirdPartyDriver;
        var (forList, against) = evidence[key];

        // Repeated non-Microsoft faulting modules in dumps
        var thirdPartyModules = _report.CrashDumps
            .Where(d => !string.IsNullOrEmpty(d.FaultingModule))
            .Select(d => d.FaultingModule.ToLowerInvariant())
            .Where(m => !IsWindowsSystemModule(m))
            .GroupBy(m => m)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (thirdPartyModules.Any())
        {
            foreach (var mod in thirdPartyModules.Take(3))
            {
                scores[key] += 2.5 * Math.Min(mod.Count(), 3);
                forList.Add($"Third-party module '{mod.Key}' appears in {mod.Count()} dump(s).");
            }
        }

        // Service Control Manager failures
        int scmFails = _report.Events.Count(e => e.Category == EventCategory.ServiceControl && e.Level <= 3);
        if (scmFails > 0)
        {
            scores[key] += 1.0 * Math.Min(scmFails, 3);
            forList.Add($"{scmFails} Service Control Manager failure event(s).");
        }

        if (!thirdPartyModules.Any() && scmFails == 0)
            against.Add("No specific third-party driver repeatedly identified as faulting module.");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsWindowsSystemModule(string module)
    {
        var systemModules = new[]
        {
            "ntoskrnl", "ntkrnl", "hal.dll", "win32k", "win32kfull",
            "ntdll", "kernel32", "kernelbase", "user32", "gdi32",
            "msvcrt", "clr.dll", "clrjit", "mscorwks",
            "wdf", "netio", "tcpip", "ndis"
        };
        return systemModules.Any(s => module.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetHypothesisName(HypothesisType type) => type switch
    {
        HypothesisType.NvidiaGpuVram => "NVIDIA dGPU / VRAM / Graphics Driver Path",
        HypothesisType.PcieMotherboardPower => "PCIe / Root Complex / Motherboard / Power Delivery",
        HypothesisType.RamImcCpu => "RAM / IMC / CPU-Side Memory Instability",
        HypothesisType.StorageFilesystem => "Storage / Filesystem Corruption",
        HypothesisType.ThirdPartyDriver => "Third-Party Kernel Driver Interference",
        _ => type.ToString()
    };

    private static string GetConfidenceLabel(double score) => score switch
    {
        >= 0.8 => "High",
        >= 0.5 => "Medium",
        >= 0.25 => "Low",
        _ => "Very Low"
    };

    private static string GetRecommendedTest(HypothesisType type) => type switch
    {
        HypothesisType.NvidiaGpuVram =>
            "Run GPU stress test (e.g., FurMark) and monitor for TDR. Use DDU to clean-install the latest NVIDIA driver. Run VRAM test with GPU-Z. Check GPU temps and power limits.",
        HypothesisType.PcieMotherboardPower =>
            "Reseat GPU in PCIe slot. Test with a known-good external PSU/power supply. Check for PCIe AER errors in Device Manager. Run HWiNFO64 to monitor PCIe error counters.",
        HypothesisType.RamImcCpu =>
            "Run MemTest86 overnight (minimum 2 passes). Test each DIMM individually. Check RAM XMP/EXPO settings and try running at JEDEC spec.",
        HypothesisType.StorageFilesystem =>
            "Run CrystalDiskInfo to check SMART health. Run chkdsk /r on the system drive (requires restart). Check NVMe health via manufacturer tool. Note: SFC failure alone is not conclusive.",
        HypothesisType.ThirdPartyDriver =>
            "Boot to Safe Mode and test — if stable, a third-party driver is implicated. Use MSConfig or Autoruns to isolate. Run Driver Verifier on suspects.",
        _ => "Collect additional evidence and run targeted hardware tests."
    };

    private List<string> BuildKeyFindings()
    {
        var findings = new List<string>();

        int bugchecks = _report.BugCheckCount;
        if (bugchecks > 0)
            findings.Add($"{bugchecks} BugCheck event(s) in event log.");

        int tdr = _report.Events.Count(e => e.Category == EventCategory.TdrDisplay);
        if (tdr > 0)
            findings.Add($"{tdr} TDR / display reset event(s) detected.");

        int whea = _report.WheaEvents.Count;
        if (whea > 0)
            findings.Add($"{whea} WHEA hardware error event(s) — {_report.WheaPcieCount} classified as PCIe/GPU.");

        int dumps = _report.CrashDumps.Count;
        if (dumps > 0)
        {
            findings.Add($"{dumps} crash dump(s) found.");
            int graphicsDumps = _report.CrashDumps.Count(d => d.IsGraphicsRelated);
            if (graphicsDumps > 0)
                findings.Add($"{graphicsDumps} dump(s) implicate graphics modules.");
        }

        int javaGpu = _report.JavaCrashLogs.Count(j => j.HasNvgpucomp || j.InNativeGraphicsCode);
        if (javaGpu > 0)
            findings.Add($"{javaGpu} Java crash log(s) show nvgpucomp64.dll or other GPU native code.");

        if (_report.Storage?.SfcFailed == true)
            findings.Add("SFC /scannow failure noted — assessed as likely secondary crash fallout, not primary storage fault.");

        if (_report.Memory?.LastDiagResult?.ErrorsFound == true)
            findings.Add("Windows Memory Diagnostic found memory errors.");

        if (!_report.Memory?.MemtestResultsAvailable == true)
            findings.Add("MemTest86 results not available — RAM not ruled out, but pattern suggests graphics path.");

        return findings;
    }

    private string BuildExecutiveSummary(List<Hypothesis> hypotheses)
    {
        var top = hypotheses.FirstOrDefault();
        if (top == null) return "Insufficient evidence to generate a diagnosis. Run a scan first.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Based on collected evidence, the leading hypothesis is: {top.Name} (confidence: {top.ConfidenceLabel} / {top.ConfidenceScore:P0}).");
        sb.AppendLine();

        if (top.SupportingEvidence.Any())
        {
            sb.AppendLine("Key supporting evidence:");
            foreach (var e in top.SupportingEvidence.Take(3))
                sb.AppendLine($"  • {e}");
        }

        if (hypotheses.Count > 1)
        {
            var second = hypotheses[1];
            sb.AppendLine();
            sb.AppendLine($"Second hypothesis: {second.Name} ({second.ConfidenceLabel}).");
        }

        sb.AppendLine();
        sb.AppendLine($"Recommended next action: {top.RecommendedTest}");

        return sb.ToString().TrimEnd();
    }
}
