using System.Text.RegularExpressions;
using GPUDIAG.Core.Models;

namespace GPUDIAG.Core.Parsers;

/// <summary>
/// Parses Java hs_err_pid*.log crash files produced by the JVM.
/// </summary>
public static class JavaCrashLogParser
{
    private static readonly string[] GraphicsModules =
    {
        "nvgpucomp64.dll", "nvgpucomp32.dll", "nvlddmkm.sys",
        "nvwgf2umx.dll", "nvcuvid.dll", "nvencodeapi64.dll",
        "igdumdim64.dll", "ig75icd64.dll", "amdvlk64.dll",
        "d3d11.dll", "d3d12.dll", "dxgi.dll", "dxgkrnl.sys"
    };

    public static JavaCrashLog? ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var lines = File.ReadAllLines(filePath);
            return ParseLines(filePath, lines);
        }
        catch (Exception ex)
        {
            return new JavaCrashLog
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                ErrorDetails = ex.Message
            };
        }
    }

    public static JavaCrashLog ParseLines(string filePath, string[] lines)
    {
        var log = new JavaCrashLog
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileDate = File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.MinValue
        };

        bool inStack = false;
        var headerLines = new List<string>();
        int headerCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Capture raw header (first 60 lines)
            if (headerCount < 60)
            {
                headerLines.Add(line);
                headerCount++;
            }

            // JVM version
            if (log.JvmVersion.Length == 0)
            {
                var vmMatch = Regex.Match(line, @"# JRE version: (.+)");
                if (vmMatch.Success) log.JvmVersion = vmMatch.Groups[1].Value.Trim();

                var jvmMatch = Regex.Match(line, @"vm_info: (.+)");
                if (jvmMatch.Success && log.JvmVersion.Length == 0)
                    log.JvmVersion = jvmMatch.Groups[1].Value.Trim();
            }

            // Exception / signal
            var sigMatch = Regex.Match(line, @"siginfo:\s+(.+)");
            if (sigMatch.Success && log.ExceptionCode.Length == 0)
            {
                log.ExceptionCode = sigMatch.Groups[1].Value.Trim();
                if (log.ExceptionCode.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase) ||
                    log.ExceptionCode.Contains("0xc0000005", StringComparison.OrdinalIgnoreCase))
                    log.IsAccessViolation = true;
            }

            // EXCEPTION_ACCESS_VIOLATION in header lines too
            if (line.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("0xc0000005", StringComparison.OrdinalIgnoreCase))
                log.IsAccessViolation = true;

            // Problematic frame
            var frameMatch = Regex.Match(line, @"^# Problematic frame:$");
            if (frameMatch.Success && i + 1 < lines.Length)
            {
                log.ProblematicFrame = lines[i + 1].TrimStart('#', ' ');
                log.ProblematicModule = ExtractModuleFromFrame(log.ProblematicFrame);
            }

            // Also match inline problematic frame (some JVM versions)
            var inlineFrame = Regex.Match(line, @"# Problematic frame:\s+(.+)");
            if (inlineFrame.Success)
            {
                log.ProblematicFrame = inlineFrame.Groups[1].Value.Trim();
                log.ProblematicModule = ExtractModuleFromFrame(log.ProblematicFrame);
            }

            // Stack section
            if (line.StartsWith("Stack: [", StringComparison.Ordinal) ||
                line.Equals("Stack:", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(line, @"^Java\s+frames:") ||
                line.StartsWith("Stack Trace"))
                inStack = true;

            if (inStack && log.StackLines.Count < 40)
                log.StackLines.Add(line);

            // Graphics module detection anywhere in the file
            foreach (var mod in GraphicsModules)
            {
                if (line.Contains(mod, StringComparison.OrdinalIgnoreCase))
                {
                    log.InNativeGraphicsCode = true;
                    if (mod.Contains("nvgpucomp", StringComparison.OrdinalIgnoreCase))
                        log.HasNvgpucomp = true;
                    if (mod.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase))
                        log.HasNvlddmkm = true;
                }
            }
        }

        log.RawHeader = string.Join(Environment.NewLine, headerLines);

        // Check problematic module for graphics
        if (!string.IsNullOrEmpty(log.ProblematicModule))
        {
            foreach (var mod in GraphicsModules)
            {
                if (log.ProblematicModule.Contains(mod.Split('.')[0], StringComparison.OrdinalIgnoreCase))
                {
                    log.InNativeGraphicsCode = true;
                    if (mod.Contains("nvgpucomp", StringComparison.OrdinalIgnoreCase))
                        log.HasNvgpucomp = true;
                    if (mod.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase))
                        log.HasNvlddmkm = true;
                }
            }
        }

        return log;
    }

    private static string ExtractModuleFromFrame(string frame)
    {
        // e.g. "C  [nvgpucomp64.dll+0x1234]"
        var match = Regex.Match(frame, @"\[(\S+?)\+");
        if (match.Success)
            return match.Groups[1].Value;

        // e.g. "j  com.example.Foo.bar()V+4"
        // just return the whole frame identifier
        var parts = frame.TrimStart().Split(' ');
        return parts.Length > 1 ? parts[1] : frame.Trim();
    }

    public static List<JavaCrashLog> FindAndParseAll(IEnumerable<string> searchFolders, Action<string>? progress = null)
    {
        var results = new List<JavaCrashLog>();
        foreach (var folder in searchFolders)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var files = Directory.GetFiles(folder, "hs_err_pid*.log", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    progress?.Invoke($"Parsing {Path.GetFileName(f)}...");
                    var log = ParseFile(f);
                    if (log != null) results.Add(log);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                progress?.Invoke($"Error searching {folder}: {ex.Message}");
            }
        }
        return results;
    }
}
