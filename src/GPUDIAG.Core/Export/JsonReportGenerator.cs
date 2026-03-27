using System.Text.Json;
using System.Text.Json.Serialization;
using GPUDIAG.Core.Models;

namespace GPUDIAG.Core.Export;

public static class JsonReportGenerator
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Generate(DiagnosticReport report)
        => JsonSerializer.Serialize(report, Options);

    public static DiagnosticReport? Deserialize(string json)
        => JsonSerializer.Deserialize<DiagnosticReport>(json, Options);
}
