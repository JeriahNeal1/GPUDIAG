using System.IO.Compression;
using GPUDIAG.Core.Analysis;
using GPUDIAG.Core.Models;
using GPUDIAG.Core.Parsers;

namespace GPUDIAG.Core.Export;

/// <summary>
/// Creates a zipped evidence bundle containing the HTML report, JSON report,
/// and any additional text summaries.
/// </summary>
public static class EvidenceBundleExporter
{
    public static string Export(DiagnosticReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var timestamp = report.GeneratedAt.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(outputDirectory, $"GPUDIAG_Evidence_{timestamp}.zip");

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // HTML report
        var html = HtmlReportGenerator.Generate(report);
        AddText(zip, $"GPUDIAG_Report_{timestamp}.html", html);

        // JSON report
        var json = JsonReportGenerator.Generate(report);
        AddText(zip, $"GPUDIAG_Report_{timestamp}.json", json);

        // Event log summary
        if (report.Events.Any())
        {
            var evtSummary = BuildEventSummary(report);
            AddText(zip, "event_log_summary.txt", evtSummary);
            var timelineCsv = BuildTimelineCsv(report);
            AddText(zip, "timeline.csv", timelineCsv);
        }

        // WHEA summary
        if (report.WheaEvents.Any())
        {
            var wheaSummary = BuildWheaSummary(report);
            AddText(zip, "whea_summary.txt", wheaSummary);
        }

        // Dump analysis outputs
        foreach (var dump in report.CrashDumps.Where(d => !string.IsNullOrEmpty(d.AnalyzeOutput)))
        {
            var safeName = SanitizeFileName(dump.FileName) + "_analysis.txt";
            AddText(zip, Path.Combine("dumps", safeName), dump.AnalyzeOutput);
        }

        // Java crash logs
        foreach (var jLog in report.JavaCrashLogs.Where(jl => !string.IsNullOrEmpty(jl.RawHeader)))
        {
            var safeName = SanitizeFileName(jLog.FileName) + "_header.txt";
            AddText(zip, Path.Combine("java_crashes", safeName), jLog.RawHeader);
        }

        // Collection errors
        if (report.CollectionErrors.Any())
            AddText(zip, "collection_errors.txt", string.Join(Environment.NewLine, report.CollectionErrors));

        return zipPath;
    }

    private static void AddText(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), System.Text.Encoding.UTF8);
        writer.Write(content);
    }

    private static string SanitizeFileName(string name)
        => string.Concat(Path.GetInvalidFileNameChars().Aggregate(name, (s, c) => s.Replace(c, '_')));

    private static string BuildEventSummary(DiagnosticReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Event Log Summary — {report.GeneratedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"Total events collected: {report.Events.Count}");
        sb.AppendLine();

        var grouped = report.Events
            .GroupBy(e => e.Category)
            .OrderByDescending(g => g.Count());

        foreach (var grp in grouped)
        {
            sb.AppendLine($"[{grp.Key}] — {grp.Count()} event(s)");
            foreach (var evt in grp.OrderByDescending(e => e.TimeCreated).Take(10))
                sb.AppendLine($"  {evt.TimeCreated:yyyy-MM-dd HH:mm:ss}  ID={evt.EventId}  {evt.Message?.Split('\n')[0].Trim()}");
        }

        return sb.ToString();
    }

    private static string BuildWheaSummary(DiagnosticReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"WHEA Event Summary — {report.GeneratedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"Total WHEA events: {report.WheaEvents.Count}");
        sb.AppendLine();

        foreach (var w in report.WheaEvents.OrderByDescending(x => x.TimeCreated))
        {
            sb.AppendLine($"{w.TimeCreated:yyyy-MM-dd HH:mm:ss}  ID={w.EventId}  Severity={w.Severity}");
            sb.AppendLine($"  Subsystem: {WheaDecoder.GetSubsystemDisplayName(w.ClassifiedSubsystem)}" +
                          (w.ClassificationUncertain ? " (uncertain)" : ""));
            sb.AppendLine($"  Reason: {w.ClassificationReason}");
            sb.AppendLine($"  Message: {w.Message?.Split('\n')[0].Trim()}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildTimelineCsv(DiagnosticReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DateTime,Provider,EventId,Severity,Category,Summary");
        foreach (var t in report.Timeline.OrderByDescending(t => t.Timestamp).Take(1000))
        {
            sb.AppendLine(string.Join(",",
                Csv(t.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(t.Source),
                Csv(t.EventId.ToString()),
                Csv(t.Severity.ToString()),
                Csv(t.Category.ToString()),
                Csv(t.Summary.Replace('\r', ' ').Replace('\n', ' ').Trim())));
        }

        var serviceFailures = ServiceFailureAnalyzer.Summarize(report.Events, minimumCount: 3);
        if (serviceFailures.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Service,Count,FirstOccurrenceUtc,LastOccurrenceUtc,GpuRelated");
            foreach (var svc in serviceFailures)
            {
                sb.AppendLine(string.Join(",",
                    Csv(svc.ServiceName),
                    Csv(svc.Count.ToString()),
                    Csv(svc.FirstOccurrenceUtc.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(svc.LastOccurrenceUtc.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(svc.IsGpuRelated ? "Yes" : "No")));
            }
        }

        return sb.ToString();
    }

    private static string Csv(string value)
    {
        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");
        return $"\"{value}\"";
    }
}
