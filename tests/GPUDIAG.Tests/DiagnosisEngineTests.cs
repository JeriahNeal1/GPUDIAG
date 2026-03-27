using GPUDIAG.Core.Engine;
using GPUDIAG.Core.Models;
using Xunit;

namespace GPUDIAG.Tests;

public class DiagnosisEngineTests
{
    private static DiagnosticReport BuildReport(Action<DiagnosticReport> configure)
    {
        var report = new DiagnosticReport();
        configure(report);
        return report;
    }

    [Fact]
    public void Analyze_GpuEvidenceHigherThanStorage_WhenTdrAndNvidiaCrash()
    {
        var report = BuildReport(r =>
        {
            // Add TDR events
            r.Events.AddRange(Enumerable.Range(0, 5).Select(_ => new EventLogItem
            {
                Category = EventCategory.TdrDisplay,
                TimeCreated = DateTime.UtcNow,
                Level = 2
            }));

            // Add NVIDIA driver events
            r.Events.AddRange(Enumerable.Range(0, 3).Select(_ => new EventLogItem
            {
                Category = EventCategory.NvidiaDriver,
                TimeCreated = DateTime.UtcNow,
                Level = 2
            }));

            // Java crash with nvgpucomp
            r.JavaCrashLogs.Add(new JavaCrashLog
            {
                HasNvgpucomp = true,
                InNativeGraphicsCode = true,
                IsAccessViolation = true
            });

            // SFC failure (should NOT drive storage to top)
            r.Storage = new StorageEvidence { SfcFailed = true };
        });

        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        Assert.NotEmpty(result.TopHypotheses);
        var top = result.TopHypotheses[0];
        Assert.Equal(HypothesisType.NvidiaGpuVram, top.Type);

        // Storage should not be #1
        var storageHyp = result.TopHypotheses.FirstOrDefault(h => h.Type == HypothesisType.StorageFilesystem);
        if (storageHyp != null)
            Assert.True(top.ConfidenceScore > storageHyp.ConfidenceScore,
                "GPU/VRAM should score higher than storage when TDR + GPU crash evidence is present");
    }

    [Fact]
    public void Analyze_SmartCriticalWarning_ElevatesStorageScore()
    {
        var report = BuildReport(r =>
        {
            r.Storage = new StorageEvidence
            {
                SmartCriticalWarningFound = true
            };
        });

        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        var storageHyp = result.TopHypotheses.FirstOrDefault(h => h.Type == HypothesisType.StorageFilesystem);
        Assert.NotNull(storageHyp);
        Assert.True(storageHyp.ConfidenceScore > 0.3, "SMART critical warning should significantly elevate storage score");
    }

    [Fact]
    public void Analyze_SfcFailureAlone_DoesNotMakeStorageTop()
    {
        var report = BuildReport(r =>
        {
            // Only SFC failure — no SMART, no disk events
            r.Storage = new StorageEvidence { SfcFailed = true };
        });

        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        var storageHyp = result.TopHypotheses.FirstOrDefault(h => h.Type == HypothesisType.StorageFilesystem);
        Assert.NotNull(storageHyp);

        // Storage score from SFC alone should be very low
        Assert.True(storageHyp.ConfidenceScore < 0.5,
            "SFC failure alone should NOT make storage a high-confidence hypothesis");
    }

    [Fact]
    public void Analyze_WheaPcieEvents_ElevatesPcieScore()
    {
        var report = BuildReport(r =>
        {
            r.WheaEvents.AddRange(Enumerable.Range(0, 3).Select(_ => new WheaEvent
            {
                ClassifiedSubsystem = WheaSubsystem.PcieRootPort,
                TimeCreated = DateTime.UtcNow
            }));
        });

        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        var pcieHyp = result.TopHypotheses.FirstOrDefault(h => h.Type == HypothesisType.PcieMotherboardPower);
        Assert.NotNull(pcieHyp);
        Assert.True(pcieHyp.ConfidenceScore > 0.3, "WHEA PCIe events should elevate PCIe hypothesis");
    }

    [Fact]
    public void Analyze_MemoryDiagErrors_ElevatesRamScore()
    {
        var report = BuildReport(r =>
        {
            r.Memory = new MemoryInfo
            {
                LastDiagResult = new MemoryDiagResult
                {
                    ErrorsFound = true,
                    ResultText = "Detected memory errors"
                }
            };
        });

        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        var ramHyp = result.TopHypotheses.FirstOrDefault(h => h.Type == HypothesisType.RamImcCpu);
        Assert.NotNull(ramHyp);
        Assert.True(ramHyp.ConfidenceScore > 0.3, "Memory diagnostic errors should elevate RAM hypothesis");
    }

    [Fact]
    public void Analyze_Win32kPowerWatchdogDump_StronglyFavorsGpu()
    {
        var report = BuildReport(r =>
        {
            r.CrashDumps.Add(new CrashDump
            {
                BugCheckCodeHex = "19C",
                BugCheckCode = "WIN32K_POWER_WATCHDOG_TIMEOUT (0x19C)",
                FaultingModule = "win32k.sys"
            });
        });

        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        var gpuHyp = result.TopHypotheses.FirstOrDefault(h => h.Type == HypothesisType.NvidiaGpuVram);
        Assert.NotNull(gpuHyp);
        Assert.True(gpuHyp.ConfidenceScore > 0.3, "0x19C dump should favor GPU hypothesis");
    }

    [Fact]
    public void Analyze_EmptyReport_ReturnsAllHypotheses()
    {
        var report = new DiagnosticReport();
        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        Assert.Equal(5, result.TopHypotheses.Count);
        Assert.NotEmpty(result.ExecutiveSummary);
    }

    [Fact]
    public void Analyze_ConfidenceScoresNormalized_MaxIsOne()
    {
        var report = BuildReport(r =>
        {
            for (int i = 0; i < 10; i++)
                r.Events.Add(new EventLogItem { Category = EventCategory.TdrDisplay, Level = 2 });
            r.JavaCrashLogs.Add(new JavaCrashLog { HasNvgpucomp = true, InNativeGraphicsCode = true });
        });

        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        foreach (var h in result.TopHypotheses)
        {
            Assert.True(h.ConfidenceScore is >= 0.0 and <= 1.0,
                $"Confidence score {h.ConfidenceScore} for {h.Name} should be between 0 and 1");
        }
    }

    [Fact]
    public void Analyze_ConfidenceScores_AreNormalizedToTotalMass()
    {
        var report = BuildReport(r =>
        {
            r.Events.Add(new EventLogItem { Category = EventCategory.TdrDisplay, Level = 1, TimeCreated = DateTime.UtcNow });
            r.Events.Add(new EventLogItem { Category = EventCategory.KernelPower, Level = 2, TimeCreated = DateTime.UtcNow });
            r.WheaEvents.Add(new WheaEvent
            {
                ClassifiedSubsystem = WheaSubsystem.PcieRootPort,
                Severity = "Error",
                TimeCreated = DateTime.UtcNow
            });
            r.Storage = new StorageEvidence { SfcFailed = true };
        });

        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        var total = result.TopHypotheses.Sum(h => h.ConfidenceScore);
        Assert.InRange(total, 0.99, 1.01);
    }

    [Fact]
    public void Analyze_ThirdPartyDriver_DetectedWhenRepeatedModule()
    {
        var report = BuildReport(r =>
        {
            for (int i = 0; i < 3; i++)
                r.CrashDumps.Add(new CrashDump { FaultingModule = "thirdpartyantivirus.sys" });
        });

        var engine = new DiagnosisEngine(report);
        var result = engine.Analyze();

        var driverHyp = result.TopHypotheses.FirstOrDefault(h => h.Type == HypothesisType.ThirdPartyDriver);
        Assert.NotNull(driverHyp);
        Assert.NotEmpty(driverHyp.SupportingEvidence);
    }
}
