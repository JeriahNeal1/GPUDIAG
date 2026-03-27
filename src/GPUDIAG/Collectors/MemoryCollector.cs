using System.Management;
using System.Runtime.Versioning;
using GPUDIAG.Core.Models;

namespace GPUDIAG.Collectors;

[SupportedOSPlatform("windows")]
public class MemoryCollector
{
    private readonly Action<string> _log;

    public MemoryCollector(Action<string> log) => _log = log;

    public async Task<MemoryInfo> CollectAsync(List<EventLogItem>? existingEvents = null)
    {
        var info = new MemoryInfo();
        await Task.Run(() =>
        {
            CollectBasicInfo(info);
            CollectMemoryDiagResults(info, existingEvents ?? new());
            AssessPattern(info, existingEvents ?? new());
        });
        return info;
    }

    private void CollectBasicInfo(MemoryInfo info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (ulong.TryParse(obj["TotalVisibleMemorySize"]?.ToString(), out var total))
                    info.TotalPhysicalBytes = total * 1024;
                if (ulong.TryParse(obj["FreePhysicalMemory"]?.ToString(), out var free))
                    info.AvailableBytes = free * 1024;
            }
        }
        catch (Exception ex) { _log($"RAM basic: {ex.Message}"); }

        try
        {
            // Page file info
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PageFileSetting");
            foreach (ManagementObject obj in searcher.Get())
            {
                info.PageFilePath = obj["Name"]?.ToString() ?? "";
                if (ulong.TryParse(obj["MaximumSize"]?.ToString(), out var pf))
                    info.PageFileSizeBytes = pf * 1024 * 1024;
                break;
            }
        }
        catch (Exception ex) { _log($"PageFile: {ex.Message}"); }
    }

    private void CollectMemoryDiagResults(MemoryInfo info, List<EventLogItem> events)
    {
        // Look for memory diagnostics results in event log
        var memEvents = events.Where(e => e.Category == EventCategory.MemoryDiagnostics).ToList();
        info.MemoryEvents = memEvents;

        if (memEvents.Any())
        {
            var latest = memEvents.OrderByDescending(e => e.TimeCreated).First();
            info.LastDiagResult = new MemoryDiagResult
            {
                RunTime = latest.TimeCreated,
                ResultText = latest.Message?.Split('\n').FirstOrDefault()?.Trim() ?? "",
                ErrorsFound = latest.Message?.Contains("error", StringComparison.OrdinalIgnoreCase) == true &&
                              !latest.Message.Contains("no error", StringComparison.OrdinalIgnoreCase),
                Details = latest.Message ?? ""
            };
        }
    }

    private static void AssessPattern(MemoryInfo info, List<EventLogItem> events)
    {
        // Detect if crash pattern is random across many unrelated modules (RAM indicator)
        var appCrashes = events.Where(e => e.Category == EventCategory.AppCrash).ToList();
        var tdrEvents = events.Where(e => e.Category == EventCategory.TdrDisplay).ToList();
        var gpuEvents = events.Where(e => e.Category == EventCategory.NvidiaDriver).ToList();

        // If TDR/GPU events dominate, pattern is GPU-path, not random RAM
        if (tdrEvents.Count + gpuEvents.Count >= 3)
        {
            info.GraphicsPathPatternDetected = true;
            info.RandomCrashPatternAcrossModules = false;
        }
        else if (appCrashes.Count >= 5)
        {
            // Check diversity of crashing processes
            var processes = appCrashes
                .Select(e => e.Message?.Split('\n').FirstOrDefault() ?? "")
                .Distinct()
                .Count();
            info.RandomCrashPatternAcrossModules = processes >= 3;
        }
    }
}
