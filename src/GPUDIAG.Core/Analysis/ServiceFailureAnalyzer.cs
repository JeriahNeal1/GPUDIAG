using System.Text.RegularExpressions;
using GPUDIAG.Core.Models;

namespace GPUDIAG.Core.Analysis;

public sealed class ServiceFailureSummary
{
    public string ServiceName { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime FirstOccurrenceUtc { get; set; }
    public DateTime LastOccurrenceUtc { get; set; }
    public bool IsGpuRelated { get; set; }
}

public static class ServiceFailureAnalyzer
{
    private static readonly Regex ServiceNameRegex = new(
        @"\b(?:The|Service)\s+['""]?(?<name>.+?)['""]?\s+service\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] FailureHints =
    {
        "terminated unexpectedly", "unexpectedly", "failed", "failure",
        "stopped", "restart", "restarted", "cannot start", "timed out", "crash"
    };

    private static readonly string[] GpuServiceHints =
    {
        "nvidia", "nvdisplay", "nvcontainer", "nvlddmkm", "amd", "amdrs", "intel graphics"
    };

    public static int RepeatedServiceFailureCount(IEnumerable<EventLogItem> events, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return 0;

        return events.Count(e =>
            e.Category == EventCategory.ServiceControl &&
            e.Level <= 3 &&
            IsFailureEvent(e.Message) &&
            e.Message.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
    }

    public static List<ServiceFailureSummary> Summarize(IEnumerable<EventLogItem> events, int minimumCount = 1)
    {
        return events
            .Where(e => e.Category == EventCategory.ServiceControl && e.Level <= 3 && IsFailureEvent(e.Message))
            .Select(e => new { Event = e, ServiceName = TryExtractServiceName(e.Message) ?? "Unknown service" })
            .GroupBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ServiceFailureSummary
            {
                ServiceName = g.Key,
                Count = g.Count(),
                FirstOccurrenceUtc = g.Min(x => x.Event.TimeCreated),
                LastOccurrenceUtc = g.Max(x => x.Event.TimeCreated),
                IsGpuRelated = IsGpuRelatedService(g.Key)
            })
            .Where(x => x.Count >= minimumCount)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsGpuRelatedService(string serviceName)
        => GpuServiceHints.Any(h => serviceName.Contains(h, StringComparison.OrdinalIgnoreCase));

    private static string? TryExtractServiceName(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var m = ServiceNameRegex.Match(message);
        if (!m.Success)
            return null;

        var value = m.Groups["name"].Value.Trim().Trim('\'', '"', '.');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool IsFailureEvent(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return FailureHints.Any(h => message.Contains(h, StringComparison.OrdinalIgnoreCase));
    }
}
