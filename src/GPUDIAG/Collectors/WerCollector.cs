using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GPUDIAG.Core.Models;

namespace GPUDIAG.Collectors;

[SupportedOSPlatform("windows")]
public class WerCollector
{
    private readonly Action<string> _log;

    private static readonly string[] GraphicsModules =
    {
        "nvlddmkm", "nvgpucomp", "nvcuda", "nvwgf2umx", "dxgkrnl",
        "d3d11", "d3d12", "dxgi", "igdumdim", "amdvlk"
    };

    public WerCollector(Action<string> log) => _log = log;

    public async Task<List<WerEntry>> CollectAsync()
    {
        var entries = new List<WerEntry>();
        await Task.Run(() =>
        {
            CollectFromWerFolder(@"C:\ProgramData\Microsoft\Windows\WER\ReportArchive", entries);
            CollectFromWerFolder(@"C:\ProgramData\Microsoft\Windows\WER\ReportQueue", entries);
            CollectFromUserWer(entries);
            CollectLiveKernelEventEntries(entries);
        });
        return entries.OrderByDescending(e => e.TimeCreated).ToList();
    }

    private void CollectFromWerFolder(string folder, List<WerEntry> entries)
    {
        if (!Directory.Exists(folder)) return;

        try
        {
            foreach (var dir in Directory.GetDirectories(folder))
            {
                try
                {
                    var reportFile = Path.Combine(dir, "Report.wer");
                    if (!File.Exists(reportFile)) continue;

                    var entry = ParseWerReport(reportFile, dir);
                    if (entry != null) entries.Add(entry);
                }
                catch (Exception ex) { _log($"WER dir {dir}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { _log($"WER folder {folder}: {ex.Message}"); }
    }

    private void CollectFromUserWer(List<WerEntry> entries)
    {
        try
        {
            var profiles = Directory.GetDirectories(@"C:\Users");
            foreach (var profile in profiles)
            {
                var folder = Path.Combine(profile, @"AppData\Local\CrashDumps");
                if (Directory.Exists(folder))
                {
                    _log($"Found user crash dumps: {folder}");
                    // Just note the dumps exist; they'll be analyzed by MinidumpAnalyzer
                }

                var werFolder = Path.Combine(profile, @"AppData\Local\Microsoft\Windows\WER\ReportArchive");
                if (Directory.Exists(werFolder))
                    CollectFromWerFolder(werFolder, entries);
            }
        }
        catch (Exception ex) { _log($"User WER: {ex.Message}"); }
    }

    private void CollectLiveKernelEventEntries(List<WerEntry> entries)
    {
        // LiveKernelEvent reports are in ProgramData WER with specific names
        // Also check for WER event log entries
        try
        {
            var werLog = @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive";
            if (!Directory.Exists(werLog)) return;

            foreach (var dir in Directory.GetDirectories(werLog, "LiveKernelEvent*"))
            {
                try
                {
                    var reportFile = Path.Combine(dir, "Report.wer");
                    if (!File.Exists(reportFile)) continue;
                    var entry = ParseWerReport(reportFile, dir);
                    if (entry != null)
                    {
                        entry.EntryType = WerEntryType.LiveKernelEvent;
                        entries.Add(entry);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex) { _log($"LiveKernel WER: {ex.Message}"); }
    }

    private WerEntry? ParseWerReport(string reportFile, string reportDir)
    {
        try
        {
            var lines = File.ReadAllLines(reportFile);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    fields[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }

            var entry = new WerEntry
            {
                ReportFolder = reportDir,
                EventName = fields.GetValueOrDefault("EventType", fields.GetValueOrDefault("EventName", "")),
                FaultingProcess = fields.GetValueOrDefault("Param0", fields.GetValueOrDefault("P0", "")),
                AppVersion = fields.GetValueOrDefault("Param1", fields.GetValueOrDefault("P1", "")),
                FaultingModule = fields.GetValueOrDefault("Param3", fields.GetValueOrDefault("P3",
                    fields.GetValueOrDefault("Param5", ""))),
                ModuleVersion = fields.GetValueOrDefault("Param4", fields.GetValueOrDefault("P4", "")),
                ExceptionCode = fields.GetValueOrDefault("Param6", fields.GetValueOrDefault("P6",
                    fields.GetValueOrDefault("Param2", ""))),
            };

            // Try to get timestamp from directory creation or report content
            if (fields.TryGetValue("ReportTime", out var ts) && DateTime.TryParse(ts, out var dt))
                entry.TimeCreated = dt;
            else
                entry.TimeCreated = Directory.GetCreationTime(reportDir);

            // Classify entry type
            if (entry.EventName.Contains("BlueScreen", StringComparison.OrdinalIgnoreCase))
                entry.EntryType = WerEntryType.BlueScreen;
            else if (entry.EventName.Contains("LiveKernelEvent", StringComparison.OrdinalIgnoreCase))
                entry.EntryType = WerEntryType.LiveKernelEvent;
            else if (entry.EventName.Contains("AppCrash", StringComparison.OrdinalIgnoreCase))
                entry.EntryType = WerEntryType.AppCrash;

            entry.IsGraphicsRelated = IsGraphicsRelated(entry);
            return entry;
        }
        catch (Exception ex)
        {
            _log($"ParseWER {reportFile}: {ex.Message}");
            return null;
        }
    }

    private static bool IsGraphicsRelated(WerEntry entry)
    {
        var check = $"{entry.FaultingModule} {entry.EventName} {entry.ExceptionCode}";
        return GraphicsModules.Any(m => check.Contains(m, StringComparison.OrdinalIgnoreCase)) ||
               entry.IsLiveKernelEvent;
    }
}
