namespace GPUDIAG.Core.Models;

public class SystemInfo
{
    public string OsVersion { get; set; } = string.Empty;
    public string OsBuild { get; set; } = string.Empty;
    public TimeSpan Uptime { get; set; }
    public string BiosVersion { get; set; } = string.Empty;
    public string BiosDate { get; set; } = string.Empty;
    public string Motherboard { get; set; } = string.Empty;
    public string CpuModel { get; set; } = string.Empty;
    public ulong TotalRamBytes { get; set; }
    public List<DimmInfo> DimmSlots { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public List<DisplayDevice> Displays { get; set; } = new();
    public bool OnlyExternalDisplay { get; set; }
    public string PowerPlan { get; set; } = string.Empty;
    public bool FastStartupEnabled { get; set; }
    public bool HibernateEnabled { get; set; }
    public List<WindowsUpdateEntry> RecentUpdates { get; set; } = new();
    public bool IsElevated { get; set; }
    public string CollectedAt { get; set; } = DateTime.UtcNow.ToString("O");
}

public class DimmInfo
{
    public string Slot { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public ulong CapacityBytes { get; set; }
    public uint SpeedMhz { get; set; }
    public string FormFactor { get; set; } = string.Empty;
}

public class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public string DriverDate { get; set; } = string.Empty;
    public string PciLocation { get; set; } = string.Empty;
    public ulong DedicatedVramBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsNvidia { get; set; }
    public bool IsIntel { get; set; }
    public bool IsAmd { get; set; }
    public string InfSection { get; set; } = string.Empty;
}

public class DisplayDevice
{
    public string Name { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool IsInternal { get; set; }
    public string ConnectionType { get; set; } = string.Empty;
}

public class WindowsUpdateEntry
{
    public string Title { get; set; } = string.Empty;
    public DateTime InstalledOn { get; set; }
    public string HotFixId { get; set; } = string.Empty;
}
