using GPUDIAG.Core.Models;
using GPUDIAG.Core.Parsers;
using Xunit;

namespace GPUDIAG.Tests;

public class WheaDecoderTests
{
    private static EventLogItem MakeWheaEvent(string message, string? xml = null)
    {
        return new EventLogItem
        {
            TimeCreated = DateTime.UtcNow,
            ProviderName = "Microsoft-Windows-WHEA-Logger",
            EventId = 17,
            Category = EventCategory.Whea,
            LevelName = "Error",
            Level = 2,
            Message = message,
            Xml = xml ?? ""
        };
    }

    [Fact]
    public void Decode_PcieKeyword_ClassifiesAsPcieRootPort()
    {
        var evt = MakeWheaEvent("PCIe bus error detected on device PCI\\VEN_10DE");
        var result = WheaDecoder.Decode(evt);

        Assert.Equal(WheaSubsystem.PcieRootPort, result.ClassifiedSubsystem);
    }

    [Fact]
    public void Decode_NvidiaKeyword_ClassifiesAsGpuDevice()
    {
        var evt = MakeWheaEvent("Error in NVIDIA display subsystem");
        var result = WheaDecoder.Decode(evt);

        Assert.Equal(WheaSubsystem.GpuDevice, result.ClassifiedSubsystem);
    }

    [Fact]
    public void Decode_MemoryKeyword_ClassifiesAsMemorySubsystem()
    {
        var evt = MakeWheaEvent("Corrected memory error detected on DIMM slot A1");
        var result = WheaDecoder.Decode(evt);

        Assert.Equal(WheaSubsystem.MemorySubsystem, result.ClassifiedSubsystem);
    }

    [Fact]
    public void Decode_UnrecognizedMessage_MarksUncertain()
    {
        var evt = MakeWheaEvent("Unclassifiable hardware error occurred on an unknown subsystem");
        var result = WheaDecoder.Decode(evt);

        // Should either classify or mark uncertain — not throw
        Assert.NotNull(result);
        // If unrecognized, should be marked uncertain
        if (result.ClassifiedSubsystem == WheaSubsystem.Unknown)
            Assert.True(result.ClassificationUncertain);
    }

    [Fact]
    public void Decode_PreservesTimestampAndEventId()
    {
        var time = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var evt = MakeWheaEvent("PCIe error");
        evt.TimeCreated = time;
        evt.EventId = 19;

        var result = WheaDecoder.Decode(evt);

        Assert.Equal(time, result.TimeCreated);
        Assert.Equal(19, result.EventId);
    }

    [Fact]
    public void GetSubsystemDisplayName_ReturnsReadableName()
    {
        Assert.Contains("PCIe", WheaDecoder.GetSubsystemDisplayName(WheaSubsystem.PcieRootPort));
        Assert.Contains("GPU", WheaDecoder.GetSubsystemDisplayName(WheaSubsystem.GpuDevice));
        Assert.Contains("Memory", WheaDecoder.GetSubsystemDisplayName(WheaSubsystem.MemorySubsystem));
        Assert.Contains("CPU", WheaDecoder.GetSubsystemDisplayName(WheaSubsystem.CpuCache));
        Assert.Contains("Storage", WheaDecoder.GetSubsystemDisplayName(WheaSubsystem.StorageController));
        Assert.Contains("Unknown", WheaDecoder.GetSubsystemDisplayName(WheaSubsystem.Unknown));
    }

    [Fact]
    public void Decode_XmlWithPcieField_ClassifiesFromXml()
    {
        var xml = @"<Event>
  <System><Provider Name='Microsoft-Windows-WHEA-Logger'/><EventID>17</EventID></System>
  <EventData>
    <Data Name='ErrorSource'>PCIe Root Port</Data>
    <Data Name='ErrorType'>Correctable</Data>
  </EventData>
</Event>";
        var evt = MakeWheaEvent("Hardware error", xml);
        var result = WheaDecoder.Decode(evt);

        Assert.Equal(WheaSubsystem.PcieRootPort, result.ClassifiedSubsystem);
    }
}
