using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using GPUDIAG.Core.Models;

namespace GPUDIAG.Collectors;

[SupportedOSPlatform("windows")]
public class SystemInfoCollector
{
    private readonly Action<string> _log;

    public SystemInfoCollector(Action<string> log) => _log = log;

    public async Task<SystemInfo> CollectAsync()
    {
        var info = new SystemInfo
        {
            IsElevated = IsRunningAsAdmin()
        };

        await Task.Run(() =>
        {
            CollectOsInfo(info);
            CollectHardwareInfo(info);
            CollectGpus(info);
            CollectDisplays(info);
            CollectPowerInfo(info);
            CollectUpdates(info);
        });

        return info;
    }

    private void CollectOsInfo(SystemInfo info)
    {
        try
        {
            var os = RunPowerShell("[System.Environment]::OSVersion.VersionString").FirstOrDefault();
            info.OsVersion = os ?? Environment.OSVersion.VersionString;

            var build = RunPowerShell("(Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion').CurrentBuildNumber").FirstOrDefault();
            var ubr = RunPowerShell("(Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion').UBR").FirstOrDefault();
            info.OsBuild = $"{build}.{ubr}";

            info.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch (Exception ex) { _log($"OsInfo: {ex.Message}"); }
    }

    private void CollectHardwareInfo(SystemInfo info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
            foreach (ManagementObject obj in searcher.Get())
            {
                info.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "";
                info.BiosDate = ManagementDateToString(obj["ReleaseDate"]?.ToString());
            }
        }
        catch (Exception ex) { _log($"BIOS: {ex.Message}"); }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var mfr = obj["Manufacturer"]?.ToString() ?? "";
                var model = obj["Model"]?.ToString() ?? "";
                info.Motherboard = $"{mfr} {model}".Trim();
                var ramBytes = obj["TotalPhysicalMemory"]?.ToString();
                if (ulong.TryParse(ramBytes, out var ram)) info.TotalRamBytes = ram;
            }
        }
        catch (Exception ex) { _log($"Computer: {ex.Message}"); }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                info.CpuModel = obj["Name"]?.ToString() ?? "";
                break;
            }
        }
        catch (Exception ex) { _log($"CPU: {ex.Message}"); }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            foreach (ManagementObject obj in searcher.Get())
            {
                var dimm = new DimmInfo
                {
                    Slot = obj["DeviceLocator"]?.ToString() ?? "",
                    Manufacturer = obj["Manufacturer"]?.ToString() ?? "",
                    PartNumber = (obj["PartNumber"]?.ToString() ?? "").Trim(),
                    FormFactor = obj["FormFactor"]?.ToString() ?? ""
                };
                if (ulong.TryParse(obj["Capacity"]?.ToString(), out var cap)) dimm.CapacityBytes = cap;
                if (uint.TryParse(obj["Speed"]?.ToString(), out var spd)) dimm.SpeedMhz = spd;
                info.DimmSlots.Add(dimm);
            }
        }
        catch (Exception ex) { _log($"RAM: {ex.Message}"); }
    }

    private void CollectGpus(SystemInfo info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var gpu = new GpuInfo
                {
                    Name = obj["Name"]?.ToString() ?? "",
                    DriverVersion = obj["DriverVersion"]?.ToString() ?? "",
                    DriverDate = ManagementDateToString(obj["DriverDate"]?.ToString()),
                    Status = obj["Status"]?.ToString() ?? "",
                    InfSection = obj["InfSection"]?.ToString() ?? ""
                };

                if (ulong.TryParse(obj["AdapterRAM"]?.ToString(), out var vram))
                    gpu.DedicatedVramBytes = vram;

                gpu.IsNvidia = gpu.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                               gpu.DriverVersion.StartsWith("31.") || gpu.DriverVersion.StartsWith("30.") ||
                               gpu.DriverVersion.StartsWith("27.") || gpu.DriverVersion.StartsWith("25.");
                gpu.IsIntel = gpu.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase);
                gpu.IsAmd = gpu.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                            gpu.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase);

                // Try to get PCI location
                try
                {
                    var pciLines = RunPowerShell(
                        $"(Get-PnpDevice | Where-Object {{$_.FriendlyName -like '*{EscapePs(gpu.Name.Split(' ').FirstOrDefault() ?? "")}*'}}).DeviceID | Select-Object -First 1");
                    gpu.PciLocation = pciLines.FirstOrDefault() ?? "";
                }
                catch { }

                info.Gpus.Add(gpu);
            }
        }
        catch (Exception ex) { _log($"GPU: {ex.Message}"); }
    }

    private void CollectDisplays(SystemInfo info)
    {
        try
        {
            var lines = RunPowerShell(
                "Get-PnpDevice | Where-Object {$_.Class -eq 'Monitor'} | Select-Object FriendlyName, DeviceID, Status | ConvertTo-Csv -NoTypeInformation");

            bool hasInternal = false;
            bool hasExternal = false;

            foreach (var line in lines.Skip(1))
            {
                var parts = ParseCsvLine(line);
                if (parts.Length < 3) continue;
                var name = parts[0].Trim('"');
                var devId = parts[1].Trim('"');
                var status = parts[2].Trim('"');

                var isInternal = devId.Contains("DISPLAY\\INTL", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Internal", StringComparison.OrdinalIgnoreCase) ||
                                 devId.Contains("EDP", StringComparison.OrdinalIgnoreCase);

                info.Displays.Add(new DisplayDevice
                {
                    Name = name,
                    DeviceId = devId,
                    Active = status.Equals("OK", StringComparison.OrdinalIgnoreCase),
                    IsInternal = isInternal
                });

                if (isInternal) hasInternal = true;
                else hasExternal = true;
            }

            info.OnlyExternalDisplay = !hasInternal && hasExternal;
        }
        catch (Exception ex) { _log($"Displays: {ex.Message}"); }
    }

    private void CollectPowerInfo(SystemInfo info)
    {
        try
        {
            var plan = RunPowerShell("(powercfg /getactivescheme)").FirstOrDefault() ?? "";
            // Extract friendly name
            var match = System.Text.RegularExpressions.Regex.Match(plan, @"\(([^)]+)\)");
            info.PowerPlan = match.Success ? match.Groups[1].Value : plan;
        }
        catch (Exception ex) { _log($"PowerPlan: {ex.Message}"); }

        try
        {
            // Fast startup = HibernateEnabled + FastBoot registry key
            var hib = RunPowerShell("(Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power').HiberbootEnabled").FirstOrDefault();
            info.FastStartupEnabled = hib == "1";

            var hibEnabled = RunPowerShell("(Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Power').HibernateEnabled").FirstOrDefault();
            info.HibernateEnabled = hibEnabled == "1";
        }
        catch (Exception ex) { _log($"Power: {ex.Message}"); }
    }

    private void CollectUpdates(SystemInfo info)
    {
        try
        {
            var lines = RunPowerShell(
                "Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 20 | Select-Object HotFixID, Description, InstalledOn | ConvertTo-Csv -NoTypeInformation");

            foreach (var line in lines.Skip(1))
            {
                var parts = ParseCsvLine(line);
                if (parts.Length < 3) continue;
                var entry = new WindowsUpdateEntry
                {
                    HotFixId = parts[0].Trim('"'),
                    Title = parts[1].Trim('"')
                };
                if (DateTime.TryParse(parts[2].Trim('"'), out var dt))
                    entry.InstalledOn = dt;
                info.RecentUpdates.Add(entry);
            }
        }
        catch (Exception ex) { _log($"Updates: {ex.Message}"); }
    }

    public static bool IsRunningAsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var p = new System.Security.Principal.WindowsPrincipal(id);
            return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static List<string> RunPowerShell(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NonInteractive -NoProfile -Command \"{command.Replace("\"", "\\\"")}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30000);

        return output.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
    }

    private static string ManagementDateToString(string? dmtfDate)
    {
        if (string.IsNullOrEmpty(dmtfDate) || dmtfDate.Length < 8) return "";
        try
        {
            return $"{dmtfDate[..4]}-{dmtfDate[4..6]}-{dmtfDate[6..8]}";
        }
        catch { return dmtfDate; }
    }

    private static string EscapePs(string s)
        => s.Replace("'", "''").Replace("`", "``");

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}
