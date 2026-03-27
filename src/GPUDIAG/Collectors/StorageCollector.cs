using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using GPUDIAG.Core.Models;

namespace GPUDIAG.Collectors;

[SupportedOSPlatform("windows")]
public class StorageCollector
{
    private readonly Action<string> _log;

    public StorageCollector(Action<string> log) => _log = log;

    public async Task<StorageEvidence> CollectAsync(List<EventLogItem>? existingEvents = null)
    {
        var evidence = new StorageEvidence();
        await Task.Run(() =>
        {
            CollectDrives(evidence);
            CollectSmartHealth(evidence);
            CollectPartitions(evidence);
            AssessPrimaryVsSecondary(evidence, existingEvents ?? new());
        });
        return evidence;
    }

    private void CollectDrives(StorageEvidence evidence)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                var dev = new StorageDevice
                {
                    DeviceId = obj["DeviceID"]?.ToString() ?? "",
                    Model = (obj["Model"]?.ToString() ?? "").Trim(),
                    SerialNumber = (obj["SerialNumber"]?.ToString() ?? "").Trim(),
                    InterfaceType = obj["InterfaceType"]?.ToString() ?? "",
                    Status = obj["Status"]?.ToString() ?? "",
                    MediaType = obj["MediaType"]?.ToString() ?? ""
                };
                if (ulong.TryParse(obj["Size"]?.ToString(), out var size)) dev.SizeBytes = size;
                evidence.Devices.Add(dev);
            }
        }
        catch (Exception ex) { _log($"DiskDrives: {ex.Message}"); }
    }

    private void CollectSmartHealth(StorageEvidence evidence)
    {
        foreach (var dev in evidence.Devices)
        {
            try
            {
                // Try WMI SMART data first
                CollectSmartWmi(dev);

                // If NVMe, try PowerShell Get-PhysicalDisk
                if (dev.InterfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                    dev.Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                {
                    CollectNvmeHealth(dev);
                }
            }
            catch (Exception ex) { _log($"SMART {dev.Model}: {ex.Message}"); }

            if (dev.SmartHealth.CriticalWarning)
                evidence.SmartCriticalWarningFound = true;
        }
    }

    private void CollectSmartWmi(StorageDevice dev)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM MSStorageDriver_FailurePredictStatus WHERE InstanceName like '%" +
                dev.SerialNumber.Replace("\\", "\\\\").Replace("'", "\\'") + "%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var predictFailure = obj["PredictFailure"]?.ToString();
                dev.SmartHealth.Available = true;
                dev.SmartHealth.OverallStatus = predictFailure == "True" ? "Failure Predicted" : "OK";
                dev.SmartHealth.CriticalWarning = predictFailure == "True";
            }
        }
        catch { }
    }

    private void CollectNvmeHealth(StorageDevice dev)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NonInteractive -NoProfile -Command \"Get-PhysicalDisk | Where-Object {{$_.FriendlyName -like '*{EscapePs(dev.Model.Split(' ').First())}*'}} | Get-StorageReliabilityCounter | ConvertTo-Csv -NoTypeInformation\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);

            dev.SmartHealth.RawOutput = output;
            dev.SmartHealth.IsNvme = true;

            if (!string.IsNullOrEmpty(output))
            {
                dev.SmartHealth.Available = true;
                if (output.Contains("ReadErrorsTotal", StringComparison.OrdinalIgnoreCase))
                    dev.SmartHealth.OverallStatus = "NVMe reliability data collected";
            }
        }
        catch { }
    }

    private void CollectPartitions(StorageEvidence evidence)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                try
                {
                    var part = new PartitionInfo
                    {
                        DriveLetter = drive.Name,
                        FileSystem = drive.DriveFormat,
                        TotalBytes = (ulong)drive.TotalSize,
                        FreeBytes = (ulong)drive.AvailableFreeSpace,
                        Label = drive.VolumeLabel
                    };

                    // Check dirty bit
                    part.IsDirty = CheckDirtyBit(drive.Name[0]);

                    // Add to first device (approximate)
                    if (evidence.Devices.Any())
                        evidence.Devices[0].Partitions.Add(part);
                }
                catch { }
            }

            evidence.DirtyFilesystemFound = evidence.Devices
                .SelectMany(d => d.Partitions)
                .Any(p => p.IsDirty);
        }
        catch (Exception ex) { _log($"Partitions: {ex.Message}"); }
    }

    private bool CheckDirtyBit(char driveLetter)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe",
                $"/c fsutil dirty query {driveLetter}:")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return output.Contains("is Dirty", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void AssessPrimaryVsSecondary(StorageEvidence evidence, List<EventLogItem> events)
    {
        evidence.DiskEvents = events.Where(e =>
            e.Category is EventCategory.Disk or EventCategory.Filesystem
                       or EventCategory.Storage or EventCategory.IntelRst)
            .ToList();

        bool hasSmartFailure = evidence.SmartCriticalWarningFound;
        bool hasDiskEvents = evidence.DiskEvents.Count > 3;
        bool hasDirty = evidence.DirtyFilesystemFound;

        if (hasSmartFailure)
        {
            evidence.PrimaryStorageFailureLikely = true;
            evidence.PrimaryVsSecondaryAssessment =
                "SMART/NVMe critical warning detected. Storage primary failure is plausible. " +
                "Confirm with manufacturer diagnostics and backup data immediately.";
        }
        else if (hasDiskEvents && hasDirty)
        {
            evidence.PrimaryVsSecondaryAssessment =
                $"Moderate disk event count ({evidence.DiskEvents.Count}) and dirty filesystem found. " +
                "This could be primary storage degradation or secondary fallout from repeated system crashes. " +
                "Run chkdsk /r and manufacturer diagnostics to distinguish.";
        }
        else if (evidence.SfcFailed)
        {
            evidence.PrimaryVsSecondaryAssessment =
                "SFC failure detected. This is likely secondary damage from repeated crashes " +
                "(OS files corrupted by unclean shutdowns), NOT primary storage failure. " +
                "Run DISM /Online /Cleanup-Image /RestoreHealth to repair. " +
                "Do NOT assume storage is the root cause based on SFC failure alone.";
        }
        else
        {
            evidence.PrimaryVsSecondaryAssessment =
                "No strong primary storage failure indicators found. " +
                "Any file system or OS corruption is most likely secondary fallout from repeated crashes.";
        }
    }

    private static string EscapePs(string s)
        => s.Replace("'", "''").Replace("`", "``");
}
