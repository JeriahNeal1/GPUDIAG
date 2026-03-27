using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GPUDIAG.Core.Models;

namespace GPUDIAG.Collectors;

[SupportedOSPlatform("windows")]
public class MinidumpAnalyzer
{
    private readonly Action<string> _log;
    private static readonly string DefaultDumpFolder = @"C:\Windows\Minidump";

    public MinidumpAnalyzer(Action<string> log) => _log = log;

    public async Task<List<CrashDump>> AnalyzeAsync(string? customFolder = null)
    {
        var results = new List<CrashDump>();
        var folders = new List<string> { DefaultDumpFolder };

        if (!string.IsNullOrEmpty(customFolder) && Directory.Exists(customFolder))
            folders.Add(customFolder);

        // Also check user crash dump folders
        try
        {
            foreach (var profile in Directory.GetDirectories(@"C:\Users"))
            {
                var userDumps = Path.Combine(profile, @"AppData\Local\CrashDumps");
                if (Directory.Exists(userDumps)) folders.Add(userDumps);
            }
        }
        catch { }

        var dbgPaths = FindDebugger();

        await Task.Run(() =>
        {
            foreach (var folder in folders.Distinct())
            {
                if (!Directory.Exists(folder)) continue;

                try
                {
                    var dmpFiles = Directory.GetFiles(folder, "*.dmp");
                    _log($"Found {dmpFiles.Length} dump(s) in {folder}.");

                    foreach (var file in dmpFiles.OrderByDescending(f => File.GetLastWriteTime(f)).Take(20))
                    {
                        var dump = AnalyzeDump(file, dbgPaths);
                        results.Add(dump);
                    }
                }
                catch (Exception ex) { _log($"Dump folder {folder}: {ex.Message}"); }
            }
        });

        return results;
    }

    private CrashDump AnalyzeDump(string filePath, string? debuggerPath)
    {
        var fi = new FileInfo(filePath);
        var dump = new CrashDump
        {
            FilePath = filePath,
            FileName = fi.Name,
            LastModified = fi.LastWriteTime,
            FileSizeBytes = fi.Length,
            Type = fi.Length < 500 * 1024 * 1024 ? DumpType.Minidump : DumpType.Kernel
        };

        // Try WinDbg analysis
        if (!string.IsNullOrEmpty(debuggerPath))
        {
            try
            {
                var output = RunWinDbg(debuggerPath, filePath);
                ParseWinDbgOutput(dump, output);
            }
            catch (Exception ex)
            {
                dump.AnalysisError = ex.Message;
                _log($"WinDbg analysis of {fi.Name}: {ex.Message}");
            }
        }
        else
        {
            // Best-effort metadata from dump header
            ParseDumpHeader(dump);
            dump.AnalysisError = "WinDbg/CDB not found. Install Windows Debugging Tools for full analysis.";
        }

        // Flag specific bugchecks
        ParseBugcheckFromDumpName(dump);
        return dump;
    }

    private string RunWinDbg(string cdbPath, string dumpFile)
    {
        var cmds = "!analyze -v; .bugcheck; lmvm; q";
        var psi = new ProcessStartInfo(cdbPath,
            $"-z \"{dumpFile}\" -c \"{cmds}\" -nosqm -nologo")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(120000); // 2 minute timeout
        return output;
    }

    private static void ParseWinDbgOutput(CrashDump dump, string output)
    {
        dump.AnalyzeOutput = output.Length > 50000 ? output[..50000] : output;
        dump.AnalysisSucceeded = true;

        // Bugcheck code
        var bcMatch = Regex.Match(output, @"BugCheck\s+([0-9A-Fa-f]+),?\s+\{?([^}]+)\}?");
        if (bcMatch.Success)
        {
            dump.BugCheckCodeHex = bcMatch.Groups[1].Value;
            dump.BugCheckParameters = bcMatch.Groups[2].Value.Split(',').Select(s => s.Trim()).ToArray();
            dump.BugCheckCode = MapBugCheckCode(dump.BugCheckCodeHex);
        }

        // Probable cause
        var probMatch = Regex.Match(output, @"Probably caused by\s*:\s*(\S+)", RegexOptions.IgnoreCase);
        if (probMatch.Success) dump.ProbableCause = probMatch.Groups[1].Value;

        // Faulting module from probable cause
        dump.FaultingModule = ExtractModule(dump.ProbableCause);

        // Stack summary — grab first 30 lines of stack
        var stackStart = output.IndexOf("STACK_TEXT:", StringComparison.OrdinalIgnoreCase);
        if (stackStart >= 0)
        {
            var stackEnd = output.IndexOf("\n\n", stackStart);
            var stackSection = stackEnd > stackStart
                ? output[stackStart..stackEnd]
                : output[stackStart..Math.Min(stackStart + 3000, output.Length)];
            dump.StackSummary = string.Join("\n",
                stackSection.Split('\n').Take(30).Select(l => l.TrimEnd()));
        }
    }

    private static void ParseDumpHeader(CrashDump dump)
    {
        // Try to read minimal info from the binary header
        try
        {
            using var fs = File.OpenRead(dump.FilePath);
            var header = new byte[32];
            if (fs.Read(header, 0, header.Length) < 32) return;

            // MDMP signature check
            if (header[0] == 'M' && header[1] == 'D' && header[2] == 'M' && header[3] == 'P')
                dump.Type = DumpType.Minidump;
        }
        catch { }

        dump.BugCheckCode = "(WinDbg required for full analysis)";
    }

    private static void ParseBugcheckFromDumpName(CrashDump dump)
    {
        // Windows minidumps are named like: Mini012345-01.dmp
        // BugCheck is not in the filename but in the dump; we annotate from analysis
        if (dump.BugCheckCodeHex.Contains("139", StringComparison.OrdinalIgnoreCase))
            dump.BugCheckCode = "KERNEL_SECURITY_CHECK_FAILURE (0x139)";
        else if (dump.BugCheckCodeHex.Contains("19C", StringComparison.OrdinalIgnoreCase))
            dump.BugCheckCode = "WIN32K_POWER_WATCHDOG_TIMEOUT (0x19C)";
        else if (!string.IsNullOrEmpty(dump.BugCheckCodeHex) && string.IsNullOrEmpty(dump.BugCheckCode))
            dump.BugCheckCode = MapBugCheckCode(dump.BugCheckCodeHex);
    }

    private static string MapBugCheckCode(string hex) => hex.ToUpperInvariant() switch
    {
        "139" => "KERNEL_SECURITY_CHECK_FAILURE (0x139)",
        "19C" => "WIN32K_POWER_WATCHDOG_TIMEOUT (0x19C)",
        "3B" => "SYSTEM_SERVICE_EXCEPTION (0x3B)",
        "7E" => "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED (0x7E)",
        "50" => "PAGE_FAULT_IN_NONPAGED_AREA (0x50)",
        "D1" => "DRIVER_IRQL_NOT_LESS_OR_EQUAL (0xD1)",
        "1E" => "KMODE_EXCEPTION_NOT_HANDLED (0x1E)",
        "1000007E" => "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED (0x1000007E)",
        "116" => "VIDEO_TDR_FAILURE (0x116)",
        "117" => "VIDEO_TDR_TIMEOUT_DETECTED (0x117)",
        _ => $"0x{hex}"
    };

    private static string ExtractModule(string probableCause)
    {
        if (string.IsNullOrEmpty(probableCause)) return "";
        var parts = probableCause.Split('+');
        return parts[0].Trim();
    }

    private static string? FindDebugger()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe",
            @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\kd.exe",
            @"C:\Program Files\Windows Kits\10\Debuggers\x64\cdb.exe",
            @"C:\Program Files\Debugging Tools for Windows (x64)\cdb.exe",
            @"C:\WinDbg\cdb.exe"
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
