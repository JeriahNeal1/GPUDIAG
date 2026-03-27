using System.Text;
using GPUDIAG.Core.Models;
using GPUDIAG.Core.Parsers;

namespace GPUDIAG.Core.Export;

/// <summary>
/// Generates a human-readable HTML diagnostic report.
/// </summary>
public static class HtmlReportGenerator
{
    public static string Generate(DiagnosticReport report)
    {
        var sb = new StringBuilder();
        var gen = report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss UTC");

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='en'><head><meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1'>");
        sb.AppendLine("<title>GPUDIAG Report</title>");
        sb.AppendLine(GetCss());
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>GPUDIAG Diagnostic Report</h1>");
        sb.AppendLine($"<p class='meta'>Generated: {gen} | Scan type: {report.ScanType}</p>");

        // Executive summary
        if (report.Diagnosis != null)
        {
            sb.AppendLine("<section class='section'><h2>Executive Summary</h2>");
            sb.AppendLine($"<pre class='summary'>{HE(report.Diagnosis.ExecutiveSummary)}</pre>");
            sb.AppendLine("</section>");
        }

        // System inventory
        if (report.System != null)
        {
            sb.AppendLine("<section class='section'><h2>System Inventory</h2>");
            sb.AppendLine("<table>");
            Row(sb, "OS", $"{report.System.OsVersion} (Build {report.System.OsBuild})");
            Row(sb, "Uptime", report.System.Uptime.ToString(@"d\.hh\:mm\:ss"));
            Row(sb, "BIOS", $"{report.System.BiosVersion} ({report.System.BiosDate})");
            Row(sb, "Motherboard", report.System.Motherboard);
            Row(sb, "CPU", report.System.CpuModel);
            Row(sb, "RAM", $"{report.System.TotalRamBytes / (1024 * 1024 * 1024.0):F1} GB");
            Row(sb, "Power Plan", report.System.PowerPlan);
            Row(sb, "Fast Startup", report.System.FastStartupEnabled ? "Enabled" : "Disabled");
            Row(sb, "Running as Admin", report.System.IsElevated ? "Yes" : "No");
            sb.AppendLine("</table>");

            if (report.System.Gpus.Any())
            {
                sb.AppendLine("<h3>GPU(s)</h3><table>");
                foreach (var gpu in report.System.Gpus)
                {
                    Row(sb, "Name", gpu.Name);
                    Row(sb, "Driver Version", gpu.DriverVersion);
                    Row(sb, "Driver Date", gpu.DriverDate);
                    Row(sb, "PCI Location", gpu.PciLocation);
                    Row(sb, "VRAM", $"{gpu.DedicatedVramBytes / (1024 * 1024 * 1024.0):F1} GB");
                    sb.AppendLine("<tr><td colspan='2'><hr></td></tr>");
                }
                sb.AppendLine("</table>");
            }

            sb.AppendLine("</section>");
        }

        // Ranked hypotheses
        if (report.Diagnosis?.TopHypotheses.Any() == true)
        {
            sb.AppendLine("<section class='section'><h2>Root-Cause Hypotheses (Ranked)</h2>");
            foreach (var h in report.Diagnosis.TopHypotheses)
            {
                var cls = h.ConfidenceScore >= 0.6 ? "high" : h.ConfidenceScore >= 0.3 ? "medium" : "low";
                sb.AppendLine($"<div class='hypothesis {cls}'>");
                sb.AppendLine($"<h3>{HE(h.Name)} — <span class='score'>{h.ConfidenceLabel} ({h.ConfidenceScore:P0})</span></h3>");
                if (h.SupportingEvidence.Any())
                {
                    sb.AppendLine("<p><strong>Supporting evidence:</strong></p><ul>");
                    foreach (var e in h.SupportingEvidence)
                        sb.AppendLine($"<li>{HE(e)}</li>");
                    sb.AppendLine("</ul>");
                }
                if (h.CounterEvidence.Any())
                {
                    sb.AppendLine("<p><strong>Counter-evidence:</strong></p><ul class='counter'>");
                    foreach (var e in h.CounterEvidence)
                        sb.AppendLine($"<li>{HE(e)}</li>");
                    sb.AppendLine("</ul>");
                }
                sb.AppendLine($"<p><strong>Recommended test:</strong> {HE(h.RecommendedTest)}</p>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</section>");
        }

        // Key findings
        if (report.Diagnosis?.KeyFindings.Any() == true)
        {
            sb.AppendLine("<section class='section'><h2>Key Findings</h2><ul>");
            foreach (var f in report.Diagnosis.KeyFindings)
                sb.AppendLine($"<li>{HE(f)}</li>");
            sb.AppendLine("</ul></section>");
        }

        // Event timeline
        if (report.Timeline.Any())
        {
            sb.AppendLine("<section class='section'><h2>Event Timeline</h2>");
            sb.AppendLine("<table><tr><th>Time</th><th>Severity</th><th>Source</th><th>Summary</th></tr>");
            foreach (var t in report.Timeline.Take(200))
            {
                var cls = t.Severity == EventSeverity.Critical ? "sev-crit" :
                          t.Severity == EventSeverity.Error ? "sev-err" :
                          t.Severity == EventSeverity.Warning ? "sev-warn" : "";
                sb.AppendLine($"<tr class='{cls}'><td>{t.Timestamp:yyyy-MM-dd HH:mm:ss}</td>" +
                    $"<td>{t.Severity}</td><td>{HE(t.Source)}</td><td>{HE(t.Summary)}</td></tr>");
            }
            sb.AppendLine("</table></section>");
        }

        // WHEA summary
        if (report.WheaEvents.Any())
        {
            sb.AppendLine("<section class='section'><h2>WHEA Hardware Errors</h2>");
            sb.AppendLine("<table><tr><th>Time</th><th>EventID</th><th>Severity</th><th>Subsystem</th><th>Details</th></tr>");
            foreach (var w in report.WheaEvents)
            {
                sb.AppendLine($"<tr><td>{w.TimeCreated:yyyy-MM-dd HH:mm:ss}</td><td>{w.EventId}</td>" +
                    $"<td>{HE(w.Severity)}</td><td>{HE(WheaDecoder.GetSubsystemDisplayName(w.ClassifiedSubsystem))}" +
                    $"{(w.ClassificationUncertain ? " (?)" : "")}</td>" +
                    $"<td>{HE(w.Message.Length > 200 ? w.Message[..200] + "…" : w.Message)}</td></tr>");
            }
            sb.AppendLine("</table></section>");
        }

        // Crash dumps
        if (report.CrashDumps.Any())
        {
            sb.AppendLine("<section class='section'><h2>Crash Dumps</h2>");
            foreach (var d in report.CrashDumps)
            {
                var flags = new List<string>();
                if (d.IsKernelSecurityCheckFailure) flags.Add("0x139 KSEC");
                if (d.IsWin32kPowerWatchdogTimeout) flags.Add("0x19C WATCHDOG");
                if (d.IsGraphicsRelated) flags.Add("GRAPHICS");
                sb.AppendLine($"<div class='dump{(d.IsGraphicsRelated ? " highlight" : "")}'>");
                sb.AppendLine($"<h3>{HE(d.FileName)} {(flags.Any() ? "— " + string.Join(", ", flags) : "")}</h3>");
                sb.AppendLine("<table>");
                Row(sb, "Bugcheck", $"{d.BugCheckCode} ({d.BugCheckCodeHex})");
                Row(sb, "Faulting Module", d.FaultingModule);
                Row(sb, "Probable Cause", d.ProbableCause);
                Row(sb, "Modified", d.LastModified.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("</table>");
                if (!string.IsNullOrEmpty(d.StackSummary))
                    sb.AppendLine($"<pre class='stack'>{HE(d.StackSummary)}</pre>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</section>");
        }

        // WER entries
        if (report.WerEntries.Any())
        {
            sb.AppendLine("<section class='section'><h2>WER / Crash Reports</h2>");
            sb.AppendLine("<table><tr><th>Time</th><th>Type</th><th>Process</th><th>Module</th><th>Exception</th></tr>");
            foreach (var w in report.WerEntries.OrderByDescending(x => x.TimeCreated).Take(50))
            {
                var cls = w.IsGraphicsRelated ? " class='highlight'" : "";
                sb.AppendLine($"<tr{cls}><td>{w.TimeCreated:yyyy-MM-dd HH:mm:ss}</td>" +
                    $"<td>{HE(w.EventName)}</td><td>{HE(w.FaultingProcess)}</td>" +
                    $"<td>{HE(w.FaultingModule)}</td><td>{HE(w.ExceptionCode)}</td></tr>");
            }
            sb.AppendLine("</table></section>");
        }

        // Java crash logs
        if (report.JavaCrashLogs.Any())
        {
            sb.AppendLine("<section class='section'><h2>Java / JVM Crash Logs</h2>");
            foreach (var j in report.JavaCrashLogs)
            {
                sb.AppendLine($"<div class='dump{(j.InNativeGraphicsCode ? " highlight" : "")}'>");
                sb.AppendLine($"<h3>{HE(j.FileName)}</h3><table>");
                Row(sb, "JVM Version", j.JvmVersion);
                Row(sb, "Exception", j.ExceptionCode);
                Row(sb, "Problematic Frame", j.ProblematicFrame);
                Row(sb, "Faulting Module", j.ProblematicModule);
                Row(sb, "GPU Code Implicated", j.InNativeGraphicsCode ? "YES" : "No");
                if (j.HasNvgpucomp) Row(sb, "nvgpucomp64.dll", "⚠ DETECTED");
                sb.AppendLine("</table></div>");
            }
            sb.AppendLine("</section>");
        }

        // Storage
        if (report.Storage != null)
        {
            sb.AppendLine("<section class='section'><h2>Storage Evidence</h2>");
            sb.AppendLine($"<p><strong>Assessment:</strong> {HE(report.Storage.PrimaryVsSecondaryAssessment)}</p>");
            foreach (var dev in report.Storage.Devices)
            {
                sb.AppendLine($"<h3>{HE(dev.Model)}</h3><table>");
                Row(sb, "Interface", dev.InterfaceType);
                Row(sb, "Size", $"{dev.SizeBytes / (1024 * 1024 * 1024.0):F0} GB");
                Row(sb, "Status", dev.Status);
                if (dev.SmartHealth.Available)
                {
                    Row(sb, "SMART/NVMe Status", dev.SmartHealth.OverallStatus);
                    Row(sb, "Critical Warning", dev.SmartHealth.CriticalWarning ? "YES ⚠" : "No");
                }
                sb.AppendLine("</table>");
            }
            sb.AppendLine("</section>");
        }

        // Memory
        if (report.Memory != null)
        {
            sb.AppendLine("<section class='section'><h2>Memory / RAM Evidence</h2><table>");
            Row(sb, "Total RAM", $"{report.Memory.TotalPhysicalBytes / (1024 * 1024 * 1024.0):F1} GB");
            Row(sb, "Memory Diagnostic Result",
                report.Memory.LastDiagResult != null
                    ? $"{report.Memory.LastDiagResult.ResultText} (errors: {report.Memory.LastDiagResult.ErrorsFound})"
                    : "No recent result");
            Row(sb, "MemTest86 Available", report.Memory.MemtestResultsAvailable ? "Yes" : "No — cannot rule out RAM");
            sb.AppendLine("</table></section>");
        }

        // Collection errors
        if (report.CollectionErrors.Any())
        {
            sb.AppendLine("<section class='section'><h2>Collection Warnings / Errors</h2><ul>");
            foreach (var e in report.CollectionErrors)
                sb.AppendLine($"<li class='warn'>{HE(e)}</li>");
            sb.AppendLine("</ul></section>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string label, string value)
        => sb.AppendLine($"<tr><td class='label'>{HE(label)}</td><td>{HE(value)}</td></tr>");

    private static string HE(string? s) =>
        System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

    private static string GetCss() => @"<style>
body { font-family: Consolas, 'Courier New', monospace; background: #1a1a2e; color: #e0e0e0; margin: 20px; }
h1 { color: #00d4ff; border-bottom: 2px solid #00d4ff; }
h2 { color: #00aaff; }
h3 { color: #88ccff; }
.meta { color: #888; font-size: 0.9em; }
.section { margin: 20px 0; padding: 15px; background: #16213e; border-radius: 8px; }
table { border-collapse: collapse; width: 100%; margin: 10px 0; }
th { background: #0f3460; color: #00d4ff; padding: 8px; text-align: left; }
td { padding: 6px 8px; border-bottom: 1px solid #2a2a4a; }
td.label { color: #aaa; width: 200px; font-weight: bold; }
.hypothesis { margin: 10px 0; padding: 10px; border-left: 4px solid #555; border-radius: 4px; }
.hypothesis.high { border-left-color: #ff4444; background: #2a0000; }
.hypothesis.medium { border-left-color: #ffaa00; background: #2a2000; }
.hypothesis.low { border-left-color: #44aa44; background: #002a00; }
.score { font-weight: bold; }
.hypothesis.high .score { color: #ff4444; }
.hypothesis.medium .score { color: #ffaa00; }
.hypothesis.low .score { color: #44aa44; }
ul.counter { color: #aaa; }
.dump { margin: 10px 0; padding: 10px; background: #0f1e3d; border-radius: 4px; }
.dump.highlight { border-left: 4px solid #ff6600; }
.highlight { background: #2a1500 !important; }
pre.stack { background: #0a0a1a; padding: 10px; overflow: auto; font-size: 0.85em; color: #ccc; }
pre.summary { white-space: pre-wrap; color: #e0e0e0; }
.sev-crit { background: #2a0000; }
.sev-err { background: #1a0a00; }
.sev-warn { background: #1a1a00; }
.warn { color: #ffaa44; }
</style>";
}
