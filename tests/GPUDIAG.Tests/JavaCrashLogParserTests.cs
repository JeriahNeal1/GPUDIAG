using GPUDIAG.Core.Models;
using GPUDIAG.Core.Parsers;
using Xunit;

namespace GPUDIAG.Tests;

public class JavaCrashLogParserTests
{
    private static string[] BuildHsErrLines(string extraLines = "")
    {
        var lines = new List<string>
        {
            "#",
            "# A fatal error has been detected by the Java Runtime Environment:",
            "#",
            "#  EXCEPTION_ACCESS_VIOLATION (0xc0000005) at pc=0x00007ffb12345678, pid=1234, tid=5678",
            "#",
            "# JRE version: OpenJDK (21.0.2) (build 21.0.2+13)",
            "#",
            "# Problematic frame:",
            "# C  [nvgpucomp64.dll+0x1a2b3c4d]",
            "#",
            "siginfo: EXCEPTION_ACCESS_VIOLATION (0xc0000005), reading address 0x0000000000000010",
            "",
            "Stack: [0x000000...]"
        };

        if (!string.IsNullOrEmpty(extraLines))
            lines.Add(extraLines);

        return lines.ToArray();
    }

    [Fact]
    public void ParseLines_DetectsAccessViolation()
    {
        var lines = BuildHsErrLines();
        var log = JavaCrashLogParser.ParseLines("test.log", lines);

        Assert.True(log.IsAccessViolation);
    }

    [Fact]
    public void ParseLines_DetectsJvmVersion()
    {
        var lines = BuildHsErrLines();
        var log = JavaCrashLogParser.ParseLines("test.log", lines);

        Assert.Contains("OpenJDK", log.JvmVersion);
    }

    [Fact]
    public void ParseLines_DetectsNvgpucomp()
    {
        var lines = BuildHsErrLines();
        var log = JavaCrashLogParser.ParseLines("test.log", lines);

        Assert.True(log.HasNvgpucomp, "Should detect nvgpucomp64.dll");
        Assert.True(log.InNativeGraphicsCode, "Should be in native graphics code");
    }

    [Fact]
    public void ParseLines_ExtractsProblematicFrame()
    {
        var lines = BuildHsErrLines();
        var log = JavaCrashLogParser.ParseLines("test.log", lines);

        Assert.Contains("nvgpucomp64.dll", log.ProblematicFrame);
    }

    [Fact]
    public void ParseLines_NoGraphicsCrash_ReturnsCleanLog()
    {
        var lines = new[]
        {
            "# JRE version: OpenJDK (11.0.20) (build 11.0.20+8)",
            "# Problematic frame:",
            "# C  [msvcrt.dll+0x1234]",
            "siginfo: EXCEPTION_ACCESS_VIOLATION (0xc0000005), reading address 0x0000000000000000"
        };

        var log = JavaCrashLogParser.ParseLines("test.log", lines);

        Assert.False(log.HasNvgpucomp);
        Assert.False(log.HasNvlddmkm);
    }

    [Fact]
    public void ParseLines_DetectsNvlddmkm()
    {
        var lines = new[]
        {
            "# JRE version: OpenJDK (17.0.9) (build 17.0.9+9)",
            "# Problematic frame:",
            "# C  [nvlddmkm.sys+0xabcdef]",
            "siginfo: EXCEPTION_ACCESS_VIOLATION (0xc0000005)"
        };

        var log = JavaCrashLogParser.ParseLines("test.log", lines);

        Assert.True(log.HasNvlddmkm);
        Assert.True(log.InNativeGraphicsCode);
    }

    [Fact]
    public void ParseLines_SetsFileName()
    {
        var log = JavaCrashLogParser.ParseLines("hs_err_pid1234.log", new[] { "# stub" });
        Assert.Equal("hs_err_pid1234.log", log.FileName);
    }

    [Fact]
    public void ParseLines_EmptyFile_ReturnsEmptyLog()
    {
        var log = JavaCrashLogParser.ParseLines("empty.log", Array.Empty<string>());
        Assert.NotNull(log);
        Assert.False(log.HasNvgpucomp);
        Assert.False(log.IsAccessViolation);
    }
}
