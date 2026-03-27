using GPUDIAG.Core.Analysis;
using GPUDIAG.Core.Models;
using Xunit;

namespace GPUDIAG.Tests;

public class ServiceFailureAnalyzerTests
{
    [Fact]
    public void RepeatedServiceFailureCount_MatchesByServiceName()
    {
        var events = Enumerable.Range(0, 4).Select(i => new EventLogItem
        {
            Category = EventCategory.ServiceControl,
            Level = 2,
            TimeCreated = DateTime.UtcNow.AddMinutes(-i),
            Message = "The NVIDIA LocalSystem Container service terminated unexpectedly."
        }).ToList();

        var count = ServiceFailureAnalyzer.RepeatedServiceFailureCount(events, "NVIDIA LocalSystem Container");
        Assert.Equal(4, count);
    }

    [Fact]
    public void Summarize_GroupsAndDetectsGpuRelatedService()
    {
        var now = DateTime.UtcNow;
        var events = new List<EventLogItem>
        {
            new() { Category = EventCategory.ServiceControl, Level = 2, TimeCreated = now.AddMinutes(-10), Message = "The NVIDIA LocalSystem Container service terminated unexpectedly." },
            new() { Category = EventCategory.ServiceControl, Level = 2, TimeCreated = now.AddMinutes(-8), Message = "The NVIDIA LocalSystem Container service terminated unexpectedly." },
            new() { Category = EventCategory.ServiceControl, Level = 2, TimeCreated = now.AddMinutes(-6), Message = "The NVIDIA LocalSystem Container service terminated unexpectedly." },
            new() { Category = EventCategory.ServiceControl, Level = 2, TimeCreated = now.AddMinutes(-4), Message = "The DiskIndexer service terminated unexpectedly." }
        };

        var summaries = ServiceFailureAnalyzer.Summarize(events, minimumCount: 2);
        Assert.NotEmpty(summaries);
        var nvidia = summaries.FirstOrDefault(s => s.ServiceName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(nvidia);
        Assert.True(nvidia.IsGpuRelated);
        Assert.Equal(3, nvidia.Count);
    }
}
