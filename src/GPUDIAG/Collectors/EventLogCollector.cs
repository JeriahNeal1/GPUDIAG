using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using GPUDIAG.Core.Models;
using GPUDIAG.Core.Parsers;

namespace GPUDIAG.Collectors;

[SupportedOSPlatform("windows")]
public class EventLogCollector
{
    private readonly Action<string> _log;
    private readonly int _dayRange;

    private static readonly Dictionary<string, EventCategory> ProviderCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Microsoft-Windows-WHEA-Logger", EventCategory.Whea },
        { "BugCheck", EventCategory.BugCheck },
        { "Microsoft-Windows-Kernel-Power", EventCategory.KernelPower },
        { "nvlddmkm", EventCategory.NvidiaDriver },
        { "nvkflt", EventCategory.NvidiaDriver },
        { "Display", EventCategory.TdrDisplay },
        { "Microsoft-Windows-DisplaySwitch", EventCategory.TdrDisplay },
        { "Microsoft-Windows-Kernel-PnP", EventCategory.TdrDisplay },
        { "Disk", EventCategory.Disk },
        { "disk", EventCategory.Disk },
        { "Ntfs", EventCategory.Filesystem },
        { "ntfs", EventCategory.Filesystem },
        { "volmgr", EventCategory.Storage },
        { "stornvme", EventCategory.Storage },
        { "storahci", EventCategory.Storage },
        { "iaStor", EventCategory.IntelRst },
        { "iaStorV", EventCategory.IntelRst },
        { "Microsoft-Windows-Storage*", EventCategory.Storage },
        { "Service Control Manager", EventCategory.ServiceControl },
        { "Microsoft-Windows-Diagnostics-Performance", EventCategory.PerformanceDiag },
        { "Microsoft-Windows-MemoryDiagnostics-Results", EventCategory.MemoryDiagnostics },
        { "Microsoft-Windows-Security-Auditing", EventCategory.SecurityCheck },
        { "Application Error", EventCategory.AppCrash },
        { "Application Hang", EventCategory.AppCrash },
        { "Windows Error Reporting", EventCategory.AppCrash },
    };

    private static readonly string[] AppCrashProcesses =
    {
        "unity", "unityhub", "code", "chrome", "msedge", "firefox",
        "java", "javaw", "prismlauncher", "multimc", "minecraft"
    };

    public EventLogCollector(Action<string> log, int dayRange = 30)
    {
        _log = log;
        _dayRange = dayRange;
    }

    public async Task<List<EventLogItem>> CollectAsync()
    {
        var items = new List<EventLogItem>();
        await Task.Run(() =>
        {
            CollectFromChannel("System", items);
            CollectFromChannel("Application", items);
            CollectWheaChannel(items);
            CollectFromChannel("Microsoft-Windows-Diagnostics-Performance/Operational", items);
        });

        // Sort by time
        items.Sort((a, b) => a.TimeCreated.CompareTo(b.TimeCreated));
        return items;
    }

    private void CollectFromChannel(string channelPath, List<EventLogItem> items)
    {
        try
        {
            var since = DateTime.UtcNow.AddDays(-_dayRange);
            var query = new EventLogQuery(channelPath, PathType.LogName,
                $"*[System[TimeCreated[@SystemTime>='{since:O}'] and (Level<=3 or EventID=41 or EventID=6008 or EventID=7001 or EventID=7026 or EventID=100 or EventID=200 or EventID=4608)]]");

            using var reader = new EventLogReader(query);
            EventRecord? record;
            int count = 0;
            while ((record = reader.ReadEvent()) != null && count < 2000)
            {
                using (record)
                {
                    try
                    {
                        var item = ToItem(record, channelPath);
                        if (item != null) { items.Add(item); count++; }
                    }
                    catch { }
                }
            }
            _log($"Collected {count} events from {channelPath}.");
        }
        catch (Exception ex)
        {
            _log($"Event log '{channelPath}': {ex.Message}");
        }
    }

    private void CollectWheaChannel(List<EventLogItem> items)
    {
        try
        {
            var since = DateTime.UtcNow.AddDays(-_dayRange);
            var query = new EventLogQuery(
                "Microsoft-Windows-WHEA-Logger/Operational",
                PathType.LogName,
                $"*[System[TimeCreated[@SystemTime>='{since:O}']]]");

            using var reader = new EventLogReader(query);
            EventRecord? record;
            int count = 0;
            while ((record = reader.ReadEvent()) != null && count < 500)
            {
                using (record)
                {
                    try
                    {
                        var item = ToItem(record, "WHEA");
                        if (item != null) { item.Category = EventCategory.Whea; items.Add(item); count++; }
                    }
                    catch { }
                }
            }
            _log($"Collected {count} WHEA events.");
        }
        catch (Exception ex)
        {
            _log($"WHEA log: {ex.Message}");
        }
    }

    private EventLogItem? ToItem(EventRecord record, string logName)
    {
        var provider = record.ProviderName ?? "";
        var item = new EventLogItem
        {
            TimeCreated = record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
            ProviderName = provider,
            LogName = logName,
            EventId = record.Id,
            Level = record.Level ?? 0,
            LevelName = LevelName(record.Level),
        };

        try { item.Message = record.FormatDescription() ?? ""; }
        catch { item.Message = ""; }

        try { item.Xml = record.ToXml(); }
        catch { }

        item.Category = ClassifyCategory(item);
        return item;
    }

    private static EventCategory ClassifyCategory(EventLogItem item)
    {
        // Explicit bugcheck events
        if (item.EventId == 1001 && item.ProviderName.Equals("BugCheck", StringComparison.OrdinalIgnoreCase))
            return EventCategory.BugCheck;
        if (item.EventId == 41 && item.ProviderName.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase))
            return EventCategory.KernelPower;
        if (item.EventId == 6008)
            return EventCategory.KernelPower;

        // TDR events
        if (item.EventId == 4101 || item.EventId == 4097 || item.EventId == 4098)
            return EventCategory.TdrDisplay;
        if (item.Message.Contains("Display driver stopped responding", StringComparison.OrdinalIgnoreCase))
            return EventCategory.TdrDisplay;

        // NVIDIA events
        if (item.ProviderName.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase) ||
            item.ProviderName.Contains("nvkflt", StringComparison.OrdinalIgnoreCase))
            return EventCategory.NvidiaDriver;

        // WHEA
        if (item.ProviderName.Contains("WHEA", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Whea;

        // Disk/storage
        if (item.ProviderName.Equals("disk", StringComparison.OrdinalIgnoreCase) ||
            item.ProviderName.Equals("Disk", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Disk;
        if (item.ProviderName.Equals("ntfs", StringComparison.OrdinalIgnoreCase) ||
            item.ProviderName.Equals("Ntfs", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Filesystem;
        if (item.ProviderName.Contains("stornvme", StringComparison.OrdinalIgnoreCase) ||
            item.ProviderName.Contains("storahci", StringComparison.OrdinalIgnoreCase) ||
            item.ProviderName.Contains("volmgr", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Storage;
        if (item.ProviderName.Contains("iaStor", StringComparison.OrdinalIgnoreCase))
            return EventCategory.IntelRst;

        // Service Control Manager
        if (item.ProviderName.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase))
            return EventCategory.ServiceControl;

        // Memory diagnostics
        if (item.ProviderName.Contains("MemoryDiagnostics", StringComparison.OrdinalIgnoreCase))
            return EventCategory.MemoryDiagnostics;

        // Application crashes
        if (item.ProviderName.Equals("Application Error", StringComparison.OrdinalIgnoreCase) ||
            item.ProviderName.Contains("Windows Error Reporting", StringComparison.OrdinalIgnoreCase))
            return EventCategory.AppCrash;

        // Check provider dictionary
        foreach (var kv in ProviderCategories)
        {
            if (item.ProviderName.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return EventCategory.General;
    }

    private static string LevelName(byte? level) => level switch
    {
        1 => "Critical",
        2 => "Error",
        3 => "Warning",
        4 => "Information",
        5 => "Verbose",
        _ => "Unknown"
    };

    public List<WheaEvent> ExtractWheaEvents(List<EventLogItem> events)
    {
        return events
            .Where(e => e.Category == EventCategory.Whea)
            .Select(e => WheaDecoder.Decode(e))
            .ToList();
    }

    public List<TimelineEvent> BuildTimeline(List<EventLogItem> events)
    {
        var anchors = events.Where(e =>
            e.Category is EventCategory.BugCheck or EventCategory.KernelPower
                       or EventCategory.TdrDisplay or EventCategory.Whea
                       or EventCategory.AppCrash)
            .OrderBy(e => e.TimeCreated)
            .ToList();

        var timeline = new List<TimelineEvent>();
        var window = TimeSpan.FromMinutes(10);

        foreach (var anchor in anchors)
        {
            var te = new TimelineEvent
            {
                Timestamp = anchor.TimeCreated,
                Summary = BuildSummary(anchor),
                Severity = anchor.Level <= 2 ? EventSeverity.Critical :
                           anchor.Level == 3 ? EventSeverity.Warning : EventSeverity.Error,
                Source = anchor.ProviderName,
                IsAnchorEvent = true
            };

            te.CorrelatedEvents = events
                .Where(e => Math.Abs((e.TimeCreated - anchor.TimeCreated).TotalMinutes) <= 10
                            && e != anchor)
                .OrderBy(e => e.TimeCreated)
                .Take(20)
                .ToList();

            timeline.Add(te);
        }

        return timeline.OrderBy(t => t.Timestamp).ToList();
    }

    private static string BuildSummary(EventLogItem e)
    {
        var msg = e.Message?.Split('\n').FirstOrDefault()?.Trim() ?? "";
        var prefix = e.Category switch
        {
            EventCategory.BugCheck => "BUGCHECK",
            EventCategory.TdrDisplay => "TDR/DISPLAY",
            EventCategory.KernelPower => "KERNEL-POWER",
            EventCategory.Whea => "WHEA",
            EventCategory.AppCrash => "APP-CRASH",
            _ => e.Category.ToString().ToUpper()
        };
        return $"[{prefix}] {e.ProviderName} ID={e.EventId}: {(msg.Length > 120 ? msg[..120] + "…" : msg)}";
    }
}
