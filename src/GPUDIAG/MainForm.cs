using System.Windows.Forms;
using GPUDIAG.Collectors;
using GPUDIAG.Core.Engine;
using GPUDIAG.Core.Export;
using GPUDIAG.Core.Models;
using GPUDIAG.Core.Parsers;

namespace GPUDIAG;

public partial class MainForm : Form
{
    private DiagnosticReport _report = new();
    private bool _scanning;
    private string? _customDumpFolder;

    // Tab controls
    private TabControl _tabs = null!;
    private TabPage _tabOverview = null!, _tabTimeline = null!, _tabDumps = null!,
                    _tabWhea = null!, _tabGraphics = null!, _tabMemory = null!,
                    _tabStorage = null!, _tabCrashLogs = null!, _tabDiagnosis = null!,
                    _tabExport = null!;

    // Shared controls
    private Button _btnQuickScan = null!, _btnDeepScan = null!,
                   _btnSelectDumps = null!, _btnExport = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblStatus = null!;
    private RichTextBox _rtbLog = null!;

    // Tab content controls
    private RichTextBox _rtbOverview = null!, _rtbTimeline = null!, _rtbDumps = null!,
                        _rtbWhea = null!, _rtbGraphics = null!, _rtbMemory = null!,
                        _rtbStorage = null!, _rtbCrashLogs = null!, _rtbDiagnosis = null!;
    private RichTextBox _rtbExportStatus = null!;

    private bool _isAdmin;

    public MainForm()
    {
        InitializeComponent();
        _isAdmin = SystemInfoCollector.IsRunningAsAdmin();
        UpdateAdminStatus();
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        Text = "GPUDIAG — GPU System Diagnostic Tool";
        MinimumSize = new System.Drawing.Size(1024, 720);
        Size = new System.Drawing.Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = System.Drawing.Color.FromArgb(26, 26, 46);
        ForeColor = System.Drawing.Color.FromArgb(224, 224, 224);
        Font = new System.Drawing.Font("Consolas", 9f);

        BuildToolbar();
        BuildTabs();
        BuildStatusBar();

        ResumeLayout();
    }

    private void BuildToolbar()
    {
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = System.Drawing.Color.FromArgb(15, 52, 96),
            Padding = new Padding(5)
        };

        _btnQuickScan = CreateButton("⚡ Quick Scan", System.Drawing.Color.FromArgb(0, 120, 215));
        _btnDeepScan = CreateButton("🔍 Deep Scan", System.Drawing.Color.FromArgb(0, 80, 160));
        _btnSelectDumps = CreateButton("📂 Select Dump Folder", System.Drawing.Color.FromArgb(80, 80, 120));
        _btnExport = CreateButton("💾 Export Report", System.Drawing.Color.FromArgb(0, 100, 80));
        _btnExport.Enabled = false;

        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            Width = 200,
            Height = 22,
            Visible = false,
            MarqueeAnimationSpeed = 30
        };

        var lblTitle = new Label
        {
            Text = "GPUDIAG v1.0",
            ForeColor = System.Drawing.Color.FromArgb(0, 212, 255),
            Font = new System.Drawing.Font("Consolas", 12f, System.Drawing.FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(5, 8, 20, 0)
        };

        toolbar.Controls.AddRange(new Control[]
        {
            lblTitle, _btnQuickScan, _btnDeepScan, _btnSelectDumps, _btnExport, _progressBar
        });

        foreach (Control c in toolbar.Controls)
        {
            if (c is Button) { c.Top = 10; }
            c.Left = toolbar.Controls.OfType<Control>()
                .Where(x => x != c)
                .Sum(x => x.Width + 5) + 5;
        }

        // Manually position with FlowLayout
        toolbar.Controls.Clear();
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(3)
        };
        flow.Controls.AddRange(new Control[]
        {
            lblTitle, _btnQuickScan, _btnDeepScan, _btnSelectDumps, _btnExport, _progressBar
        });

        // Align progress bar vertically
        _progressBar.Margin = new Padding(5, 14, 0, 0);
        toolbar.Controls.Add(flow);

        Controls.Add(toolbar);

        _btnQuickScan.Click += (_, _) => RunScanAsync("Quick");
        _btnDeepScan.Click += (_, _) => RunScanAsync("Deep");
        _btnSelectDumps.Click += SelectDumpFolder;
        _btnExport.Click += ExportReport;
    }

    private void BuildTabs()
    {
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new System.Drawing.Point(8, 4)
        };

        _tabOverview = CreateTab("Overview");
        _tabTimeline = CreateTab("Event Timeline");
        _tabDumps = CreateTab("Dumps");
        _tabWhea = CreateTab("WHEA");
        _tabGraphics = CreateTab("Graphics");
        _tabMemory = CreateTab("Memory");
        _tabStorage = CreateTab("Storage");
        _tabCrashLogs = CreateTab("Crash Logs");
        _tabDiagnosis = CreateTab("Diagnosis");
        _tabExport = CreateTab("Export");

        _rtbOverview = AddRtb(_tabOverview);
        _rtbTimeline = AddRtb(_tabTimeline);
        _rtbDumps = AddRtb(_tabDumps);
        _rtbWhea = AddRtb(_tabWhea);
        _rtbGraphics = AddRtb(_tabGraphics);
        _rtbMemory = AddRtb(_tabMemory);
        _rtbStorage = AddRtb(_tabStorage);
        _rtbCrashLogs = AddRtb(_tabCrashLogs);

        // Diagnosis tab — styled differently
        _rtbDiagnosis = AddRtb(_tabDiagnosis);

        // Export tab
        var exportPanel = new Panel { Dock = DockStyle.Fill };
        var btnExportHtml = CreateButton("Export HTML Report", System.Drawing.Color.FromArgb(0, 100, 80));
        btnExportHtml.Width = 180;
        var btnExportJson = CreateButton("Export JSON Report", System.Drawing.Color.FromArgb(0, 80, 100));
        btnExportJson.Width = 180;
        var btnExportZip = CreateButton("Export ZIP Bundle", System.Drawing.Color.FromArgb(100, 60, 0));
        btnExportZip.Width = 180;
        var exportFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(5)
        };
        exportFlow.Controls.AddRange(new Control[] { btnExportHtml, btnExportJson, btnExportZip });
        _rtbExportStatus = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(22, 33, 62),
            ForeColor = System.Drawing.Color.FromArgb(200, 200, 200),
            Font = new System.Drawing.Font("Consolas", 9f),
            ReadOnly = true
        };
        exportPanel.Controls.Add(_rtbExportStatus);
        exportPanel.Controls.Add(exportFlow);
        _tabExport.Controls.Add(exportPanel);

        btnExportHtml.Click += (_, _) => ExportFile("html");
        btnExportJson.Click += (_, _) => ExportFile("json");
        btnExportZip.Click += (_, _) => ExportFile("zip");

        _tabs.TabPages.AddRange(new[]
        {
            _tabOverview, _tabTimeline, _tabDumps, _tabWhea,
            _tabGraphics, _tabMemory, _tabStorage, _tabCrashLogs,
            _tabDiagnosis, _tabExport
        });

        Controls.Add(_tabs);

        SetInitialText();
    }

    private void BuildStatusBar()
    {
        var statusPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 120,
            BackColor = System.Drawing.Color.FromArgb(10, 10, 20)
        };

        var lblLog = new Label
        {
            Text = "Status / Errors:",
            ForeColor = System.Drawing.Color.Gray,
            AutoSize = true,
            Location = new System.Drawing.Point(5, 2)
        };

        _lblStatus = new Label
        {
            Text = "Ready",
            ForeColor = System.Drawing.Color.FromArgb(0, 212, 255),
            AutoSize = true,
            Location = new System.Drawing.Point(100, 2)
        };

        _rtbLog = new RichTextBox
        {
            Dock = DockStyle.Bottom,
            Height = 100,
            BackColor = System.Drawing.Color.FromArgb(10, 10, 20),
            ForeColor = System.Drawing.Color.Gray,
            Font = new System.Drawing.Font("Consolas", 8f),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };

        statusPanel.Controls.Add(_rtbLog);
        statusPanel.Controls.Add(_lblStatus);
        statusPanel.Controls.Add(lblLog);
        Controls.Add(statusPanel);
    }

    private void SetInitialText()
    {
        _rtbOverview.Text = "Click 'Quick Scan' or 'Deep Scan' to begin diagnosis.\r\n\r\n" +
                            $"Running as: {(_isAdmin ? "Administrator" : "Standard User (some data sources limited)")}\r\n";
    }

    private void UpdateAdminStatus()
    {
        if (!_isAdmin)
        {
            AppendLog("⚠ Running without elevation. Some collectors (WHEA, some WMI, minidumps) may return limited data. " +
                      "Right-click GPUDIAG and 'Run as administrator' for full results.", System.Drawing.Color.Yellow);
        }
    }

    // ─── Scan logic ───────────────────────────────────────────────────────────

    private async void RunScanAsync(string scanType)
    {
        if (_scanning) return;
        _scanning = true;
        _btnQuickScan.Enabled = false;
        _btnDeepScan.Enabled = false;
        _progressBar.Visible = true;
        _btnExport.Enabled = false;

        AppendLog($"Starting {scanType} scan...", System.Drawing.Color.Cyan);
        SetStatus($"Running {scanType} scan...");

        _report = new DiagnosticReport { ScanType = scanType };

        try
        {
            void Log(string msg) => AppendLog(msg, System.Drawing.Color.LightGray);

            // System inventory
            SetStatus("Collecting system inventory...");
            var sysCollector = new SystemInfoCollector(msg => { _report.CollectionErrors.Add(msg); Log(msg); });
            _report.System = await sysCollector.CollectAsync();
            UpdateOverviewTab();

            // Event logs
            SetStatus("Collecting event logs...");
            var eventCollector = new EventLogCollector(msg => { _report.CollectionErrors.Add(msg); Log(msg); }, 30);
            _report.Events = await eventCollector.CollectAsync();
            _report.WheaEvents = eventCollector.ExtractWheaEvents(_report.Events);
            _report.Timeline = eventCollector.BuildTimeline(_report.Events);
            UpdateEventTabs();

            // WER / crash reports
            SetStatus("Collecting WER / crash reports...");
            var werCollector = new WerCollector(msg => { _report.CollectionErrors.Add(msg); Log(msg); });
            _report.WerEntries = await werCollector.CollectAsync();
            UpdateCrashLogsTab();

            // Java crash logs
            SetStatus("Searching for Java crash logs...");
            var javaFolders = GetJavaSearchFolders();
            _report.JavaCrashLogs = JavaCrashLogParser.FindAndParseAll(javaFolders,
                msg => { Log(msg); });
            UpdateCrashLogsTab();

            if (scanType == "Deep")
            {
                // Dump analysis
                SetStatus("Analyzing crash dumps...");
                var dumpAnalyzer = new MinidumpAnalyzer(msg => { _report.CollectionErrors.Add(msg); Log(msg); });
                _report.CrashDumps = await dumpAnalyzer.AnalyzeAsync(_customDumpFolder);
                UpdateDumpsTab();

                // Storage
                SetStatus("Collecting storage evidence...");
                var storageCollector = new StorageCollector(msg => { _report.CollectionErrors.Add(msg); Log(msg); });
                _report.Storage = await storageCollector.CollectAsync(_report.Events);
                UpdateStorageTab();
            }

            // Memory
            SetStatus("Collecting memory evidence...");
            var memCollector = new MemoryCollector(msg => { _report.CollectionErrors.Add(msg); Log(msg); });
            _report.Memory = await memCollector.CollectAsync(_report.Events);
            UpdateMemoryTab();

            // Run diagnosis engine
            SetStatus("Running diagnosis engine...");
            var engine = new DiagnosisEngine(_report);
            _report.Diagnosis = engine.Analyze();
            UpdateDiagnosisTab();

            _btnExport.Enabled = true;
            SetStatus($"{scanType} scan complete. {_report.Events.Count} events, {_report.WheaEvents.Count} WHEA, {_report.CrashDumps.Count} dumps analyzed.");
            AppendLog($"✓ {scanType} scan complete.", System.Drawing.Color.LimeGreen);

            // Switch to Diagnosis tab
            _tabs.SelectedTab = _tabDiagnosis;
        }
        catch (Exception ex)
        {
            _report.CollectionErrors.Add($"Scan failed: {ex}");
            SetStatus($"Scan error: {ex.Message}");
            AppendLog($"❌ Scan error: {ex.Message}", System.Drawing.Color.Red);
        }
        finally
        {
            _scanning = false;
            _btnQuickScan.Enabled = true;
            _btnDeepScan.Enabled = true;
            _progressBar.Visible = false;
        }
    }

    // ─── Tab updaters ─────────────────────────────────────────────────────────

    private void UpdateOverviewTab()
    {
        InvokeIfNeeded(() =>
        {
            var sys = _report.System;
            if (sys == null) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══ SYSTEM OVERVIEW ═══");
            sb.AppendLine($"OS:           {sys.OsVersion}");
            sb.AppendLine($"Build:        {sys.OsBuild}");
            sb.AppendLine($"Uptime:       {sys.Uptime:d\\.hh\\:mm\\:ss}");
            sb.AppendLine($"BIOS:         {sys.BiosVersion} ({sys.BiosDate})");
            sb.AppendLine($"Motherboard:  {sys.Motherboard}");
            sb.AppendLine($"CPU:          {sys.CpuModel}");
            sb.AppendLine($"RAM:          {sys.TotalRamBytes / (1024 * 1024 * 1024.0):F1} GB");
            sb.AppendLine($"Power Plan:   {sys.PowerPlan}");
            sb.AppendLine($"Fast Startup: {(sys.FastStartupEnabled ? "Enabled" : "Disabled")}");
            sb.AppendLine($"Hibernate:    {(sys.HibernateEnabled ? "Enabled" : "Disabled")}");
            sb.AppendLine($"Elevation:    {(sys.IsElevated ? "Admin" : "Standard User")}");
            sb.AppendLine();

            if (sys.Gpus.Any())
            {
                sb.AppendLine("═══ GPU(s) ═══");
                foreach (var gpu in sys.Gpus)
                {
                    sb.AppendLine($"  {gpu.Name}");
                    sb.AppendLine($"    Driver: {gpu.DriverVersion} ({gpu.DriverDate})");
                    sb.AppendLine($"    PCI:    {gpu.PciLocation}");
                    sb.AppendLine($"    VRAM:   {gpu.DedicatedVramBytes / (1024 * 1024 * 1024.0):F1} GB");
                    sb.AppendLine($"    Status: {gpu.Status}");
                }
                sb.AppendLine();
            }

            if (sys.Displays.Any())
            {
                sb.AppendLine("═══ DISPLAYS ═══");
                foreach (var d in sys.Displays)
                    sb.AppendLine($"  {d.Name} [{(d.IsInternal ? "INTERNAL" : "EXTERNAL")}] Active={d.Active}");
                sb.AppendLine($"  Only External Display: {sys.OnlyExternalDisplay}");
                sb.AppendLine();
            }

            if (sys.DimmSlots.Any())
            {
                sb.AppendLine("═══ RAM DIMMS ═══");
                foreach (var d in sys.DimmSlots)
                    sb.AppendLine($"  [{d.Slot}] {d.Manufacturer} {d.PartNumber} {d.CapacityBytes / (1024 * 1024 * 1024.0):F1}GB @ {d.SpeedMhz}MHz");
                sb.AppendLine();
            }

            _rtbOverview.Text = sb.ToString();
        });
    }

    private void UpdateEventTabs()
    {
        InvokeIfNeeded(() =>
        {
            // Timeline tab
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"═══ EVENT TIMELINE ({_report.Timeline.Count} anchor events, {_report.Events.Count} total events) ═══");
            sb.AppendLine("Window: ±10 minutes around bugchecks, TDRs, WHEA, app crashes");
            sb.AppendLine();

            foreach (var t in _report.Timeline.OrderByDescending(x => x.Timestamp).Take(100))
            {
                sb.AppendLine($"▶ {t.Timestamp:yyyy-MM-dd HH:mm:ss} [{t.Severity}]");
                sb.AppendLine($"  {t.Summary}");
                if (t.CorrelatedEvents.Any())
                {
                    sb.AppendLine($"  ↳ {t.CorrelatedEvents.Count} correlated events:");
                    foreach (var ce in t.CorrelatedEvents.Take(5))
                        sb.AppendLine($"    {ce.TimeCreated:HH:mm:ss} [{ce.Category}] {ce.ProviderName} ID={ce.EventId}");
                }
                sb.AppendLine();
            }

            if (!_report.Timeline.Any())
                sb.AppendLine("No critical anchor events found in the last 30 days.");

            _rtbTimeline.Text = sb.ToString();

            // WHEA tab
            var wSb = new System.Text.StringBuilder();
            wSb.AppendLine($"═══ WHEA HARDWARE ERRORS ({_report.WheaEvents.Count} events) ═══");
            wSb.AppendLine();

            if (!_report.WheaEvents.Any())
            {
                wSb.AppendLine("No WHEA events found in the last 30 days.");
                wSb.AppendLine("Note: WHEA log may require elevation or may be empty if no hardware errors occurred.");
            }
            else
            {
                foreach (var w in _report.WheaEvents.OrderByDescending(x => x.TimeCreated))
                {
                    wSb.AppendLine($"  {w.TimeCreated:yyyy-MM-dd HH:mm:ss}  ID={w.EventId}  [{w.Severity}]");
                    wSb.AppendLine($"  Subsystem: {WheaDecoder.GetSubsystemDisplayName(w.ClassifiedSubsystem)}" +
                                   (w.ClassificationUncertain ? " (?uncertain)" : ""));
                    wSb.AppendLine($"  Reason:    {w.ClassificationReason}");
                    wSb.AppendLine($"  Message:   {w.Message?.Split('\n').FirstOrDefault()?.Trim()}");
                    wSb.AppendLine();
                }
            }

            _rtbWhea.Text = wSb.ToString();

            // Graphics tab
            var gSb = new System.Text.StringBuilder();
            gSb.AppendLine("═══ GRAPHICS / GPU EVIDENCE ═══");
            gSb.AppendLine();

            var tdrEvts = _report.Events.Where(e => e.Category == EventCategory.TdrDisplay).ToList();
            var nvEvts = _report.Events.Where(e => e.Category == EventCategory.NvidiaDriver).ToList();

            gSb.AppendLine($"TDR / Display Reset events: {tdrEvts.Count}");
            gSb.AppendLine($"NVIDIA driver events:        {nvEvts.Count}");
            gSb.AppendLine($"WHEA PCIe/GPU events:        {_report.WheaPcieCount}");
            gSb.AppendLine();

            foreach (var e in tdrEvts.Concat(nvEvts).OrderByDescending(x => x.TimeCreated).Take(30))
            {
                gSb.AppendLine($"  {e.TimeCreated:yyyy-MM-dd HH:mm:ss}  [{e.Category}]  ID={e.EventId}");
                gSb.AppendLine($"  {e.Message?.Split('\n').FirstOrDefault()?.Trim()}");
                gSb.AppendLine();
            }

            _rtbGraphics.Text = gSb.ToString();
        });
    }

    private void UpdateDumpsTab()
    {
        InvokeIfNeeded(() =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"═══ CRASH DUMPS ({_report.CrashDumps.Count} found) ═══");
            sb.AppendLine();

            if (!_report.CrashDumps.Any())
            {
                sb.AppendLine("No crash dumps found in C:\\Windows\\Minidump");
                sb.AppendLine("Use 'Select Dump Folder' to specify an alternate location.");
            }

            foreach (var d in _report.CrashDumps)
            {
                sb.AppendLine($"━━━ {d.FileName} ({d.FileSizeBytes / 1024} KB) [{d.LastModified:yyyy-MM-dd HH:mm:ss}]");
                sb.AppendLine($"  Bugcheck:       {d.BugCheckCode}");
                if (d.BugCheckParameters.Any())
                    sb.AppendLine($"  Parameters:     {string.Join(", ", d.BugCheckParameters)}");
                sb.AppendLine($"  Faulting Mod:   {d.FaultingModule}");
                sb.AppendLine($"  Probable Cause: {d.ProbableCause}");

                var flags = new List<string>();
                if (d.IsKernelSecurityCheckFailure) flags.Add("⚠ KERNEL_SECURITY_CHECK_FAILURE (0x139)");
                if (d.IsWin32kPowerWatchdogTimeout) flags.Add("⚠ WIN32K_POWER_WATCHDOG_TIMEOUT (0x19C) — graphics/display power path");
                if (d.IsGraphicsRelated) flags.Add("⚠ GRAPHICS RELATED");
                foreach (var f in flags) sb.AppendLine($"  {f}");

                if (!string.IsNullOrEmpty(d.AnalysisError))
                    sb.AppendLine($"  Analysis note:  {d.AnalysisError}");

                if (!string.IsNullOrEmpty(d.StackSummary))
                {
                    sb.AppendLine("  Stack summary:");
                    foreach (var line in d.StackSummary.Split('\n').Take(10))
                        sb.AppendLine($"    {line}");
                }
                sb.AppendLine();
            }

            _rtbDumps.Text = sb.ToString();
        });
    }

    private void UpdateStorageTab()
    {
        InvokeIfNeeded(() =>
        {
            var storage = _report.Storage;
            if (storage == null) { _rtbStorage.Text = "Run Deep Scan to collect storage evidence."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══ STORAGE EVIDENCE ═══");
            sb.AppendLine();
            sb.AppendLine($"Assessment: {storage.PrimaryVsSecondaryAssessment}");
            sb.AppendLine();

            foreach (var dev in storage.Devices)
            {
                sb.AppendLine($"Drive: {dev.Model} ({dev.InterfaceType}) [{dev.SizeBytes / (1024 * 1024 * 1024.0):F0} GB]");
                sb.AppendLine($"  Status: {dev.Status}");
                if (dev.SmartHealth.Available)
                {
                    sb.AppendLine($"  SMART Status: {dev.SmartHealth.OverallStatus}");
                    sb.AppendLine($"  Critical Warning: {(dev.SmartHealth.CriticalWarning ? "YES ⚠" : "No")}");
                }
                foreach (var p in dev.Partitions)
                    sb.AppendLine($"  {p.DriveLetter} [{p.FileSystem}] {p.FreeBytes / (1024 * 1024 * 1024.0):F0}/{p.TotalBytes / (1024 * 1024 * 1024.0):F0} GB free" +
                                  (p.IsDirty ? " ⚠ DIRTY" : ""));
                sb.AppendLine();
            }

            if (storage.DiskEvents.Any())
            {
                sb.AppendLine($"Disk/Filesystem events ({storage.DiskEvents.Count}):");
                foreach (var e in storage.DiskEvents.OrderByDescending(x => x.TimeCreated).Take(20))
                    sb.AppendLine($"  {e.TimeCreated:yyyy-MM-dd HH:mm:ss} [{e.Category}] {e.ProviderName} ID={e.EventId}: {e.Message?.Split('\n').FirstOrDefault()?.Trim()}");
            }

            _rtbStorage.Text = sb.ToString();
        });
    }

    private void UpdateMemoryTab()
    {
        InvokeIfNeeded(() =>
        {
            var mem = _report.Memory;
            if (mem == null) { _rtbMemory.Text = "Memory data not collected."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══ MEMORY / RAM EVIDENCE ═══");
            sb.AppendLine();
            sb.AppendLine($"Total RAM:    {mem.TotalPhysicalBytes / (1024 * 1024 * 1024.0):F1} GB");
            sb.AppendLine($"Available:    {mem.AvailableBytes / (1024 * 1024 * 1024.0):F1} GB");
            if (!string.IsNullOrEmpty(mem.PageFilePath))
                sb.AppendLine($"Page File:    {mem.PageFilePath} ({mem.PageFileSizeBytes / (1024 * 1024.0):F0} MB)");
            sb.AppendLine();

            sb.AppendLine("Windows Memory Diagnostic:");
            if (mem.LastDiagResult != null)
            {
                sb.AppendLine($"  Last run:    {mem.LastDiagResult.RunTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"  Result:      {mem.LastDiagResult.ResultText}");
                sb.AppendLine($"  Errors found: {(mem.LastDiagResult.ErrorsFound ? "YES ⚠" : "No")}");
            }
            else
                sb.AppendLine("  No recent Windows Memory Diagnostic results found.");

            sb.AppendLine();
            sb.AppendLine("MemTest86: " + (mem.MemtestResultsAvailable ? "Results available" :
                "Not available — CANNOT rule out RAM without extended testing (MemTest86, minimum 2 passes)"));

            sb.AppendLine();
            sb.AppendLine($"Crash pattern analysis:");
            sb.AppendLine($"  GPU/graphics path pattern: {(mem.GraphicsPathPatternDetected ? "YES" : "No")}");
            sb.AppendLine($"  Random module crash pattern: {(mem.RandomCrashPatternAcrossModules ? "YES" : "No")}");

            _rtbMemory.Text = sb.ToString();
        });
    }

    private void UpdateCrashLogsTab()
    {
        InvokeIfNeeded(() =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"═══ CRASH LOGS (WER: {_report.WerEntries.Count}, Java: {_report.JavaCrashLogs.Count}) ═══");
            sb.AppendLine();

            if (_report.WerEntries.Any())
            {
                sb.AppendLine("─── WER / Windows Error Reporting ───");
                var lke = _report.WerEntries.Where(w => w.IsLiveKernelEvent).ToList();
                var bsod = _report.WerEntries.Where(w => w.IsBlueScreen).ToList();
                var app = _report.WerEntries.Where(w => w.IsAppCrash).ToList();

                if (lke.Any()) sb.AppendLine($"LiveKernelEvents: {lke.Count} (graphics TDR indicators)");
                if (bsod.Any()) sb.AppendLine($"BlueScreens: {bsod.Count}");
                if (app.Any()) sb.AppendLine($"AppCrashes: {app.Count}");
                sb.AppendLine();

                foreach (var w in _report.WerEntries.OrderByDescending(x => x.TimeCreated).Take(30))
                {
                    var marker = w.IsGraphicsRelated ? "⚠ " : "  ";
                    sb.AppendLine($"{marker}{w.TimeCreated:yyyy-MM-dd HH:mm:ss}  {w.EventName}");
                    sb.AppendLine($"    Process: {w.FaultingProcess}  Module: {w.FaultingModule}  Exception: {w.ExceptionCode}");
                }
                sb.AppendLine();
            }

            if (_report.JavaCrashLogs.Any())
            {
                sb.AppendLine("─── Java / JVM Crash Logs ───");
                foreach (var j in _report.JavaCrashLogs)
                {
                    var marker = j.InNativeGraphicsCode ? "⚠ " : "  ";
                    sb.AppendLine($"{marker}{j.FileName}");
                    sb.AppendLine($"    Exception:   {j.ExceptionCode}");
                    sb.AppendLine($"    Frame:       {j.ProblematicFrame}");
                    sb.AppendLine($"    Module:      {j.ProblematicModule}");
                    if (j.HasNvgpucomp) sb.AppendLine("    ⚠ nvgpucomp64.dll DETECTED");
                    sb.AppendLine();
                }
            }

            _rtbCrashLogs.Text = sb.ToString();
        });
    }

    private void UpdateDiagnosisTab()
    {
        InvokeIfNeeded(() =>
        {
            var diag = _report.Diagnosis;
            if (diag == null) { _rtbDiagnosis.Text = "Run scan to generate diagnosis."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║             GPUDIAG DIAGNOSIS — ROOT CAUSE RANKING          ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();
            sb.AppendLine(diag.ExecutiveSummary);
            sb.AppendLine();

            sb.AppendLine("═══ TOP 5 HYPOTHESES ═══");
            for (int i = 0; i < diag.TopHypotheses.Count; i++)
            {
                var h = diag.TopHypotheses[i];
                var bar = GetConfidenceBar(h.ConfidenceScore);
                sb.AppendLine($"{i + 1}. {h.Name}");
                sb.AppendLine($"   Confidence: {h.ConfidenceLabel} ({h.ConfidenceScore:P0}) {bar}");

                if (h.SupportingEvidence.Any())
                {
                    sb.AppendLine("   Supporting:");
                    foreach (var e in h.SupportingEvidence.Take(5))
                        sb.AppendLine($"     + {e}");
                }
                if (h.CounterEvidence.Any())
                {
                    sb.AppendLine("   Counter:");
                    foreach (var e in h.CounterEvidence.Take(3))
                        sb.AppendLine($"     - {e}");
                }
                sb.AppendLine($"   → Next test: {h.RecommendedTest}");
                sb.AppendLine();
            }

            if (diag.KeyFindings.Any())
            {
                sb.AppendLine("═══ KEY FINDINGS ═══");
                foreach (var f in diag.KeyFindings)
                    sb.AppendLine($"  • {f}");
                sb.AppendLine();
            }

            sb.AppendLine("═══ RECOMMENDED NEXT ACTION ═══");
            sb.AppendLine(diag.RecommendedNextAction);

            _rtbDiagnosis.Text = sb.ToString();
        });
    }

    private static string GetConfidenceBar(double score)
    {
        int bars = (int)(score * 20);
        return "[" + new string('█', bars) + new string('░', 20 - bars) + "]";
    }

    // ─── Export ───────────────────────────────────────────────────────────────

    private void ExportReport(object? sender, EventArgs e)
    {
        _tabs.SelectedTab = _tabExport;
    }

    private void ExportFile(string format)
    {
        if (_report.Diagnosis == null)
        {
            MessageBox.Show("Please run a scan first.", "No data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dlg = new FolderBrowserDialog
        {
            Description = "Select output folder for GPUDIAG report",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        var folder = dlg.SelectedPath;
        var ts = _report.GeneratedAt.ToString("yyyyMMdd_HHmmss");

        try
        {
            string path;
            switch (format)
            {
                case "html":
                    path = Path.Combine(folder, $"GPUDIAG_Report_{ts}.html");
                    File.WriteAllText(path, HtmlReportGenerator.Generate(_report), System.Text.Encoding.UTF8);
                    break;
                case "json":
                    path = Path.Combine(folder, $"GPUDIAG_Report_{ts}.json");
                    File.WriteAllText(path, JsonReportGenerator.Generate(_report), System.Text.Encoding.UTF8);
                    break;
                default:
                    path = EvidenceBundleExporter.Export(_report, folder);
                    break;
            }

            AppendLog($"✓ Report exported: {path}", System.Drawing.Color.LimeGreen);
            _rtbExportStatus.AppendText($"Exported: {path}\r\n");

            if (MessageBox.Show($"Report saved to:\r\n{path}\r\n\r\nOpen now?", "Export Complete",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SelectDumpFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder containing .dmp files",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _customDumpFolder = dlg.SelectedPath;
            AppendLog($"Custom dump folder: {_customDumpFolder}", System.Drawing.Color.Cyan);
        }
    }

    private static IEnumerable<string> GetJavaSearchFolders()
    {
        var folders = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            @"C:\Users"
        };

        try
        {
            foreach (var profile in Directory.GetDirectories(@"C:\Users"))
                folders.Add(profile);
        }
        catch { }

        return folders.Where(Directory.Exists).Distinct();
    }

    private void AppendLog(string message, System.Drawing.Color color)
    {
        InvokeIfNeeded(() =>
        {
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            _rtbLog.SelectionColor = _rtbLog.ForeColor;
            _rtbLog.ScrollToCaret();
        });
    }

    private void SetStatus(string msg)
    {
        InvokeIfNeeded(() => _lblStatus.Text = msg);
    }

    private void InvokeIfNeeded(Action action)
    {
        if (InvokeRequired) Invoke(action);
        else action();
    }

    private static TabPage CreateTab(string title)
    {
        return new TabPage(title)
        {
            BackColor = System.Drawing.Color.FromArgb(22, 33, 62),
            ForeColor = System.Drawing.Color.FromArgb(224, 224, 224)
        };
    }

    private static RichTextBox AddRtb(TabPage tab)
    {
        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(22, 33, 62),
            ForeColor = System.Drawing.Color.FromArgb(200, 200, 200),
            Font = new System.Drawing.Font("Consolas", 9f),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false
        };
        tab.Controls.Add(rtb);
        return rtb;
    }

    private static Button CreateButton(string text, System.Drawing.Color backColor)
    {
        return new Button
        {
            Text = text,
            BackColor = backColor,
            ForeColor = System.Drawing.Color.White,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 2),
            Margin = new Padding(3, 8, 3, 8),
            Font = new System.Drawing.Font("Consolas", 9f)
        };
    }
}
