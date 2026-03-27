# GPUDIAG

A Windows-native read-only diagnostic utility for investigating recurring graphics-related system instability. Built with C# on .NET 8 using WinForms.

## Purpose

GPUDIAG collects, correlates, and ranks the most likely root causes of recurring crashes on Windows systems — specifically targeting:

1. NVIDIA dGPU / VRAM / graphics driver path instability
2. PCIe / root complex / motherboard / power delivery instability
3. RAM / IMC / CPU-side memory instability
4. Storage / filesystem corruption (primary vs. secondary)
5. Third-party kernel driver interference

## Requirements

- **Windows 11** (or Windows 10 build 1903+)
- **.NET 8 Runtime** – [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Administrator privileges** recommended for full access to WHEA logs, minidumps, and WMI data
- Optional: **Windows Debugging Tools** (WinDbg/CDB) for full crash dump analysis

## Build Instructions

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022+ or the `dotnet` CLI

### Build

```powershell
git clone <repo>
cd GPUDIAG
dotnet build GPUDIAG.slnx
```

Or build release:

```powershell
dotnet build GPUDIAG.slnx -c Release
```

### Run

```powershell
dotnet run --project src/GPUDIAG/GPUDIAG.csproj
```

Or build and run the executable:

```powershell
dotnet publish src/GPUDIAG/GPUDIAG.csproj -c Release -r win-x64 --self-contained
.\src\GPUDIAG\bin\Release\net8.0-windows\win-x64\publish\GPUDIAG.exe
```

### Run Tests

```powershell
dotnet test tests/GPUDIAG.Tests/GPUDIAG.Tests.csproj
```

## Project Structure

```
GPUDIAG/
├── GPUDIAG.slnx                          # Solution file
├── README.md
├── src/
│   ├── GPUDIAG.Core/                     # Cross-platform business logic (net8.0)
│   │   ├── Models/                       # Data models
│   │   │   ├── SystemInfo.cs
│   │   │   ├── EventLogItem.cs
│   │   │   ├── WheaEvent.cs
│   │   │   ├── CrashDump.cs
│   │   │   ├── JavaCrashLog.cs
│   │   │   ├── StorageDevice.cs
│   │   │   ├── MemoryInfo.cs
│   │   │   ├── DiagnosisResult.cs
│   │   │   └── DiagnosticReport.cs
│   │   ├── Parsers/
│   │   │   ├── JavaCrashLogParser.cs     # Parses JVM hs_err_pid*.log files
│   │   │   └── WheaDecoder.cs            # Classifies WHEA hardware error events
│   │   ├── Engine/
│   │   │   └── DiagnosisEngine.cs        # Heuristic scoring engine
│   │   └── Export/
│   │       ├── HtmlReportGenerator.cs    # HTML report
│   │       ├── JsonReportGenerator.cs    # JSON report
│   │       └── EvidenceBundleExporter.cs # ZIP bundle exporter
│   └── GPUDIAG/                          # WinForms UI app (net8.0-windows)
│       ├── Program.cs
│       ├── MainForm.cs                   # Main UI with 10 tabs
│       └── Collectors/
│           ├── SystemInfoCollector.cs    # OS, BIOS, CPU, GPU, RAM, displays
│           ├── EventLogCollector.cs      # Windows event logs + WHEA
│           ├── WerCollector.cs           # WER/crash reports
│           ├── MinidumpAnalyzer.cs       # Minidump analysis (WinDbg if available)
│           ├── StorageCollector.cs       # SMART, disk health, partitions
│           └── MemoryCollector.cs        # RAM diagnostics
└── tests/
    └── GPUDIAG.Tests/                    # xUnit tests (net8.0, runs on Linux/Mac/Windows)
        ├── JavaCrashLogParserTests.cs
        ├── WheaDecoderTests.cs
        └── DiagnosisEngineTests.cs
```

## Features

### Scan Modes

**Quick Scan:**
- System inventory (OS, BIOS, CPU, GPU, RAM, displays, power plan)
- Windows Event Log collection (last 30 days)
- WHEA hardware error events
- WER / Windows Error Reporting crash entries
- Java `hs_err_pid*.log` detection
- Diagnosis summary

**Deep Scan:**
- Everything in Quick Scan
- Crash dump analysis (WinDbg if installed, metadata fallback)
- Storage health (SMART/NVMe, dirty filesystem check)
- Deeper event correlation

### UI Tabs

| Tab | Content |
|-----|---------|
| Overview | System inventory |
| Event Timeline | Anchor events with ±10 min correlation window |
| Dumps | Crash dump analysis and bugcheck decoding |
| WHEA | Hardware error events classified by subsystem |
| Graphics | TDR, NVIDIA driver, GPU-related events |
| Memory | RAM evidence and diagnostics results |
| Storage | SMART health, disk events, primary vs. secondary assessment |
| Crash Logs | WER entries and Java crash logs |
| Diagnosis | Ranked hypotheses with confidence scores |
| Drivers/Services | Repeated service crash/restart loop summary tied to hypotheses |
| Export | Export HTML, JSON, or ZIP bundle |

### New UX & Settings Enhancements

- **Interactive Event Timeline** now uses a sortable grid with category/date-range filters, quick category toggles (ServiceControl, KernelPower, WHEA, AppCrash, Storage), and a double-click details dialog showing correlated events.
- Timeline category selection now persists across runs, and filtered timeline rows can be exported directly to CSV.
- **Diagnosis tab chart** now displays normalized confidence bars that sum to 100%, including evidence counts (`+supporting`, `-counter`) per hypothesis.
- Chart initialization/rendering is now guarded with error handling. If the chart dependency fails to load, GPUDIAG shows a red warning banner and logs the exception instead of crashing.
- **Scan summary banner** appears after each scan with key totals and elapsed time, with distinct colors for Quick vs Deep scans.
- **Admin warning banner** appears when not elevated and explains which data sources can be incomplete.
- **Toolbar quick export** buttons for HTML, JSON, ZIP, and Timeline CSV are available directly from the top toolbar.
- **Settings dialog** (`⚙ Settings`) allows configuring:
  - Event log look-back period (days)
  - Enable/disable WER collector
  - Enable/disable Java crash log collector
  - Enable/disable WMI storage collector
  - Theme mode (dark/light), accent color, and reset-to-defaults
  - Settings persist to `%APPDATA%/GPUDIAG/config.json` and apply on next run.

### Diagnosis Engine

The heuristic engine scores and ranks hypotheses with weighted priors:

1. **NVIDIA dGPU / VRAM** — weighted by TDR events, NVIDIA driver errors, GPU crash dumps, LiveKernelEvents, Java nvgpucomp64.dll crashes, 0x19C WIN32K_POWER_WATCHDOG_TIMEOUT
2. **PCIe / Motherboard / Power** — weighted by WHEA PCIe events, Kernel-Power events, 0x19C dumps
3. **CPU / Cache / Internal Bus** — weighted by CPU/cache WHEA patterns and machine-check style crashes
4. **RAM / IMC / CPU** — weighted by memory diagnostic errors, WHEA memory events, random crash patterns
5. **Storage / Filesystem** — weighted by SMART critical warnings, disk events, dirty filesystem (SFC failure alone is NOT sufficient)
6. **Third-Party Driver** — weighted by repeated non-system faulting modules in dumps and recurrent service failures

Additional weighting highlights:
- Repeated **Service Control Manager** crash loops (for example `NVIDIA LocalSystem Container`) now boost GPU and third-party-driver hypotheses.
- AppCrash module/process matching now supports GPU signals (e.g., `nvgpucomp64.dll`) and security-stack interference hints (e.g., `MsMpEng.exe`).

### Export

- **HTML Report**: Human-readable, styled report with all findings, timeline table, service failure summary, scan timestamp, and app version
- **JSON Report**: Machine-readable structured data
- **ZIP Bundle**: HTML + JSON + event summaries + timeline.csv + dump analysis outputs + Java crash headers

## What Requires Admin

| Feature | Admin Required |
|---------|---------------|
| WHEA event log | Yes |
| C:\Windows\Minidump | Usually yes |
| WMI hardware queries | Some (SMART, BIOS) |
| Windows Event Log (System/Application) | No |
| WER ProgramData folder | No |

The app runs without admin but clearly notes which sources are limited.

## What the App Does NOT Do

- Does not run SFC, DISM, CHKDSK, or any repair tools
- Does not modify the registry
- Does not run any destructive operations
- Does not assume storage is the root cause based only on SFC failure
- Does not require internet access

## Sample Diagnosis Output

```
1. NVIDIA dGPU / VRAM / Graphics Driver Path
   Confidence: High (82%) [████████████████░░░░]
   Supporting:
     + 7 TDR/display reset event(s) found in event log.
     + 2 crash dump(s) implicate graphics modules.
     + 1 dump(s) show 0x19C WIN32K_POWER_WATCHDOG_TIMEOUT
     + 1 Java crash log(s) with native GPU code (nvgpucomp64.dll)
   → Next test: Run GPU stress test (FurMark) and monitor for TDR. Use DDU to
     clean-install the latest NVIDIA driver. Run VRAM test with GPU-Z.
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `System.Data.SqlClient` | 4.8.6 | Required runtime dependency for WinForms DataVisualization Chart |
| `System.Management` | 8.0.0 | WMI queries (GPU, RAM, disk info) |
| `System.Text.Json` | 8.0.5 | JSON serialization |
| `xunit` | 2.9.2 | Unit testing (tests project only) |

## Contributing / Building on Linux/macOS

The `GPUDIAG.Core` library and `GPUDIAG.Tests` build and run on Linux/macOS for development and CI purposes. The `GPUDIAG` WinForms project requires Windows to run but compiles cross-platform with `EnableWindowsTargeting=true`.
