using System.Text.RegularExpressions;
using System.Xml.Linq;
using GPUDIAG.Core.Models;

namespace GPUDIAG.Core.Parsers;

/// <summary>
/// Decodes WHEA (Windows Hardware Error Architecture) events and classifies them
/// to a subsystem (PCIe, GPU, Memory, CPU, Storage, etc.).
/// </summary>
public static class WheaDecoder
{
    // WHEA Event IDs
    // 17 = WHEA_ERROR_RECORD (machine check / corrected hardware error)
    // 18 = WHEA_UNCORRECTABLE_ERROR
    // 19 = WHEA_CORRECTED_ERROR
    // 20 = XpfMcaError (generic MCA)

    private static readonly Dictionary<string, WheaSubsystem> ErrorSourceHints = new(StringComparer.OrdinalIgnoreCase)
    {
        // PCIe / Root Port
        { "PCIe", WheaSubsystem.PcieRootPort },
        { "PCI Express", WheaSubsystem.PcieRootPort },
        { "root port", WheaSubsystem.PcieRootPort },
        { "AER", WheaSubsystem.PcieRootPort },
        { "PCIE_ERROR", WheaSubsystem.PcieRootPort },

        // GPU device (a downstream PCIe device)
        { "display", WheaSubsystem.GpuDevice },
        { "gpu", WheaSubsystem.GpuDevice },
        { "vga", WheaSubsystem.GpuDevice },
        { "nv ", WheaSubsystem.GpuDevice },
        { "nvidia", WheaSubsystem.GpuDevice },
        { "dxgk", WheaSubsystem.GpuDevice },

        // Memory
        { "memory", WheaSubsystem.MemorySubsystem },
        { "DIMM", WheaSubsystem.MemorySubsystem },
        { "ECC", WheaSubsystem.MemorySubsystem },
        { "cache", WheaSubsystem.CpuCache },
        { "MCA_ERROR_TYPE_CACHE", WheaSubsystem.CpuCache },
        { "MCA_ERROR_TYPE_BUS", WheaSubsystem.PcieRootPort },
        { "MCA_ERROR_TYPE_MEM", WheaSubsystem.MemorySubsystem },

        // CPU / internal bus
        { "processor", WheaSubsystem.CpuCache },
        { "cpu", WheaSubsystem.CpuCache },
        { "internal bus", WheaSubsystem.CpuCache },

        // Storage
        { "sata", WheaSubsystem.StorageController },
        { "nvme", WheaSubsystem.StorageController },
        { "ahci", WheaSubsystem.StorageController },
        { "storage", WheaSubsystem.StorageController },
    };

    public static WheaEvent Decode(EventLogItem raw)
    {
        var evt = new WheaEvent
        {
            TimeCreated = raw.TimeCreated,
            EventId = (int)raw.EventId,
            Severity = raw.LevelName,
            Message = raw.Message,
            RawXml = raw.Xml
        };

        ClassifyFromXml(evt);
        if (evt.ClassifiedSubsystem == WheaSubsystem.Unknown)
            ClassifyFromMessage(evt);

        return evt;
    }

    private static void ClassifyFromXml(WheaEvent evt)
    {
        if (string.IsNullOrEmpty(evt.RawXml)) return;

        try
        {
            var doc = XDocument.Parse(evt.RawXml);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            // Look for ErrorSource, ErrorType, BusAddress in event data
            var eventData = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName.Equals("EventData", StringComparison.OrdinalIgnoreCase));
            if (eventData == null) return;

            foreach (var data in eventData.Elements())
            {
                var name = data.Attribute("Name")?.Value ?? data.Name.LocalName;
                var value = data.Value;

                if (name.Equals("ErrorSource", StringComparison.OrdinalIgnoreCase))
                    evt.ErrorSource = value;
                else if (name.Equals("ErrorType", StringComparison.OrdinalIgnoreCase))
                    evt.ErrorType = value;
                else if (name.Equals("Address", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("BusAddress", StringComparison.OrdinalIgnoreCase))
                    evt.BusAddress = value;

                // Try to classify based on known field values
                foreach (var hint in ErrorSourceHints)
                {
                    if (value.Contains(hint.Key, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(hint.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        if (evt.ClassifiedSubsystem == WheaSubsystem.Unknown ||
                            hint.Value != WheaSubsystem.Unknown)
                        {
                            evt.ClassifiedSubsystem = hint.Value;
                            evt.ClassificationReason = $"XML field '{name}' contains '{hint.Key}'";
                        }
                    }
                }
            }
        }
        catch
        {
            evt.ClassificationUncertain = true;
        }
    }

    private static void ClassifyFromMessage(WheaEvent evt)
    {
        var combined = $"{evt.Message} {evt.ErrorSource} {evt.ErrorType}";

        foreach (var hint in ErrorSourceHints)
        {
            if (combined.Contains(hint.Key, StringComparison.OrdinalIgnoreCase))
            {
                evt.ClassifiedSubsystem = hint.Value;
                evt.ClassificationReason = $"Message contains '{hint.Key}'";
                return;
            }
        }

        // Heuristic: WHEA event near a TDR/display event often means PCIe/GPU
        evt.ClassificationUncertain = true;
        evt.ClassificationReason = "Could not determine subsystem from available data";
    }

    public static string GetSubsystemDisplayName(WheaSubsystem subsystem) => subsystem switch
    {
        WheaSubsystem.PcieRootPort => "PCIe / Root Port / Motherboard",
        WheaSubsystem.GpuDevice => "GPU Device",
        WheaSubsystem.MemorySubsystem => "Memory Subsystem (RAM/IMC)",
        WheaSubsystem.CpuCache => "CPU / Cache / Internal Bus",
        WheaSubsystem.StorageController => "Storage Controller",
        WheaSubsystem.Platform => "Platform / Firmware",
        _ => "Unknown (insufficient data)"
    };
}
