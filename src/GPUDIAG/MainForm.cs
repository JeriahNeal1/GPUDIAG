using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using GPUDIAG.Core.Analysis;
using GPUDIAG.Configuration;
using GPUDIAG.Collectors;
using GPUDIAG.Core.Engine;
using GPUDIAG.Core.Export;
using GPUDIAG.Core.Models;
using GPUDIAG.Core.Parsers;
using GPUDIAG.Dialogs;
using System.Reflection;
using System.Linq;

namespace GPUDIAG;

public partial class MainForm : Form
{
    private DiagnosticReport _report = new();
    private bool _scanning;
    private string? _customDumpFolder;
    private AppSettings _settings = AppSettingsManager.Load();

    // Tab controls
    private TabControl _tabs = null!;
    private TabPage _tabOverview = null!, _tabTimeline = null!, _tabDumps = null!,
                    _tabWhea = null!, _tabGraphics = null!, _tabMemory = null!,
                    _tabStorage = null!, _tabCrashLogs = null!, _tabDiagnosis = null!,
                    _tabServices = null!, _tabExport = null!;

    // Shared controls
    private Button _btnQuickScan = null!, _btnDeepScan = null!,
                   _btnSelectDumps = null!, _btnExport = null!,
                   _btnExportHtmlQuick = null!, _btnExportJsonQuick = null!,
                   _btnExportZipQuick = null!, _btnExportCsvQuick = null!, _btnSettings = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblStatus = null!;
    private RichTextBox _rtbLog = null!;
    private Label _lblAdminBanner = null!;
    private Label _lblScanBanner = null!;

    // Tab content controls
    private RichTextBox _rtbOverview = null!, _rtbDumps = null!,
                          _rtbWhea = null!, _rtbGraphics = null!, _rtbMemory = null!,
                          _rtbStorage = null!, _rtbCrashLogs = null!, _rtbDiagnosis = null!,
                          _rtbServices = null!;
    private RichTextBox _rtbExportStatus = null!;
    private DataGridView _timelineGrid = null!;
    private ComboBox _cmbTimelineCategory = null!;
    private DateTimePicker _dtTimelineFrom = null!, _dtTimelineTo = null!;
    private Label _lblTimelineMeta = null!;
    private CheckBox _chkTimelineServiceControl = null!, _chkTimelineKernelPower = null!,
                     _chkTimelineWhea = null!, _chkTimelineAppCrash = null!, _chkTimelineStorage = null!;
    private Chart? _diagnosisChart;
    private Label _lblChartWarning = null!;
    private List<TimelineEvent> _filteredTimelineItems = new();

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
        ApplyThemeFromSettings();

        ResumeLayout();
    }

    private void BuildToolbar()
    {
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 98,
            BackColor = System.Drawing.Color.FromArgb(15, 52, 96),
            Padding = new Padding(5)
        };

        _btnQuickScan = CreateButton("⚡ Quick Scan", System.Drawing.Color.FromArgb(0, 120, 215));
        _btnDeepScan = CreateButton("🔍 Deep Scan", System.Drawing.Color.FromArgb(0, 80, 160));
        _btnSelectDumps = CreateButton("📂 Select Dump Folder", System.Drawing.Color.FromArgb(80, 80, 120));
        _btnSettings = CreateButton("⚙ Settings", System.Drawing.Color.FromArgb(58, 88, 118));
        _btnExport = CreateButton("💾 Export Report", System.Drawing.Color.FromArgb(0, 100, 80));
        _btnExportHtmlQuick = CreateButton("HTML", System.Drawing.Color.FromArgb(0, 100, 80));
        _btnExportJsonQuick = CreateButton("JSON", System.Drawing.Color.FromArgb(0, 90, 110));
        _btnExportZipQuick = CreateButton("ZIP", System.Drawing.Color.FromArgb(100, 60, 0));
        _btnExportCsvQuick = CreateButton("CSV", System.Drawing.Color.FromArgb(0, 90, 60));
        _btnExport.Enabled = false;
        _btnExportHtmlQuick.Enabled = false;
        _btnExportJsonQuick.Enabled = false;
        _btnExportZipQuick.Enabled = false;
        _btnExportCsvQuick.Enabled = false;
        _btnExportCsvQuick.Enabled = false;

        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            Width = 130,
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

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(3)
        };

        flow.Controls.AddRange(new Control[]
        {
            lblTitle, _btnQuickScan, _btnDeepScan, _btnSelectDumps, _btnSettings, _btnExport,
            _btnExportHtmlQuick, _btnExportJsonQuick, _btnExportZipQuick, _btnExportCsvQuick, _progressBar
        });

        // Align progress bar vertically
        _progressBar.Margin = new Padding(5, 14, 0, 0);

        _lblScanBanner = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false,
            BackColor = System.Drawing.Color.FromArgb(0, 90, 75),
            ForeColor = Color.White,
            Padding = new Padding(8, 0, 0, 0),
            Font = new Font("Consolas", 9f, FontStyle.Bold)
        };

        _lblAdminBanner = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false,
            BackColor = System.Drawing.Color.FromArgb(120, 72, 0),
            ForeColor = Color.FromArgb(255, 245, 220),
            Padding = new Padding(8, 0, 0, 0),
            Font = new Font("Consolas", 9f, FontStyle.Bold)
        };

        toolbar.Controls.Add(_lblAdminBanner);
        toolbar.Controls.Add(_lblScanBanner);
        toolbar.Controls.Add(flow);

        Controls.Add(toolbar);

        _btnQuickScan.Click += (_, _) => RunScanAsync("Quick");
        _btnDeepScan.Click += (_, _) => RunScanAsync("Deep");
        _btnSelectDumps.Click += SelectDumpFolder;
        _btnSettings.Click += OpenSettings;
        _btnExport.Click += ExportReport;
        _btnExportHtmlQuick.Click += (_, _) => ExportFile("html");
        _btnExportJsonQuick.Click += (_, _) => ExportFile("json");
        _btnExportZipQuick.Click += (_, _) => ExportFile("zip");
        _btnExportCsvQuick.Click += (_, _) => ExportFile("csv");
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
        _tabServices = CreateTab("Drivers/Services");
        _tabExport = CreateTab("Export");

        _rtbOverview = AddRtb(_tabOverview);
        _rtbDumps = AddRtb(_tabDumps);
        _rtbWhea = AddRtb(_tabWhea);
        _rtbGraphics = AddRtb(_tabGraphics);
        _rtbMemory = AddRtb(_tabMemory);
        _rtbStorage = AddRtb(_tabStorage);
        _rtbCrashLogs = AddRtb(_tabCrashLogs);
        _rtbServices = AddRtb(_tabServices);

        BuildTimelineTab();
        BuildDiagnosisTab();

        // Export tab
        var exportPanel = new Panel { Dock = DockStyle.Fill };
        var btnExportHtml = CreateButton("Export HTML Report", System.Drawing.Color.FromArgb(0, 100, 80));
        btnExportHtml.Width = 180;
        var btnExportJson = CreateButton("Export JSON Report", System.Drawing.Color.FromArgb(0, 80, 100));
        btnExportJson.Width = 180;
        var btnExportZip = CreateButton("Export ZIP Bundle", System.Drawing.Color.FromArgb(100, 60, 0));
        btnExportZip.Width = 180;
        var btnExportCsv = CreateButton("Export Timeline CSV", System.Drawing.Color.FromArgb(0, 90, 60));
        btnExportCsv.Width = 180;
        var exportFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(5)
        };
        exportFlow.Controls.AddRange(new Control[] { btnExportHtml, btnExportJson, btnExportZip, btnExportCsv });
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
        btnExportCsv.Click += (_, _) => ExportFile("csv");

        _tabs.TabPages.AddRange(new[]
        {
            _tabOverview, _tabTimeline, _tabDumps, _tabWhea,
            _tabGraphics, _tabMemory, _tabStorage, _tabCrashLogs,
            _tabDiagnosis, _tabServices, _tabExport
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

    private void BuildTimelineTab()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = System.Drawing.Color.FromArgb(22, 33, 62)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(8, 8, 8, 4),
            BackColor = System.Drawing.Color.FromArgb(18, 27, 50)
        };

        filterPanel.Controls.Add(CreateFilterLabel("Category"));
        _cmbTimelineCategory = new ComboBox
        {
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = System.Drawing.Color.FromArgb(15, 52, 96),
            ForeColor = Color.White
        };
        _cmbTimelineCategory.Items.AddRange(new object[]
        {
            "All", "Kernel-Power", "WHEA", "TDR/Display", "Graphics driver", "Disk/FileSystem", "Crash", "Other"
        });
        var selectedCategory = _settings.TimelineCategory;
        if (string.IsNullOrWhiteSpace(selectedCategory) ||
            !_cmbTimelineCategory.Items.Cast<object>().Any(x => string.Equals(x.ToString(), selectedCategory, StringComparison.OrdinalIgnoreCase)))
        {
            selectedCategory = "All";
        }
        _cmbTimelineCategory.SelectedItem = selectedCategory;
        filterPanel.Controls.Add(_cmbTimelineCategory);

        filterPanel.Controls.Add(CreateFilterLabel("From"));
        _dtTimelineFrom = new DateTimePicker
        {
            Width = 170,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm",
            Value = DateTime.Now.AddDays(-_settings.EventLookbackDays),
            BackColor = System.Drawing.Color.FromArgb(15, 52, 96),
            ForeColor = Color.White
        };
        filterPanel.Controls.Add(_dtTimelineFrom);

        filterPanel.Controls.Add(CreateFilterLabel("To"));
        _dtTimelineTo = new DateTimePicker
        {
            Width = 170,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm",
            Value = DateTime.Now,
            BackColor = System.Drawing.Color.FromArgb(15, 52, 96),
            ForeColor = Color.White
        };
        filterPanel.Controls.Add(_dtTimelineTo);

        _chkTimelineServiceControl = new CheckBox
        {
            Text = "ServiceControl",
            Checked = true,
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Margin = new Padding(16, 8, 0, 0)
        };
        _chkTimelineKernelPower = new CheckBox
        {
            Text = "KernelPower",
            Checked = true,
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Margin = new Padding(8, 8, 0, 0)
        };
        _chkTimelineWhea = new CheckBox
        {
            Text = "WHEA",
            Checked = true,
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Margin = new Padding(8, 8, 0, 0)
        };
        _chkTimelineAppCrash = new CheckBox
        {
            Text = "AppCrash",
            Checked = true,
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Margin = new Padding(8, 8, 0, 0)
        };
        _chkTimelineStorage = new CheckBox
        {
            Text = "Storage",
            Checked = true,
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Margin = new Padding(8, 8, 0, 0)
        };
        filterPanel.Controls.Add(_chkTimelineServiceControl);
        filterPanel.Controls.Add(_chkTimelineKernelPower);
        filterPanel.Controls.Add(_chkTimelineWhea);
        filterPanel.Controls.Add(_chkTimelineAppCrash);
        filterPanel.Controls.Add(_chkTimelineStorage);

        _lblTimelineMeta = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(0, 212, 255),
            Padding = new Padding(12, 6, 0, 0),
            Text = "No events loaded."
        };
        filterPanel.Controls.Add(_lblTimelineMeta);

        _timelineGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.FromArgb(22, 33, 62),
            BorderStyle = BorderStyle.None,
            GridColor = Color.FromArgb(42, 58, 92),
            RowHeadersVisible = false
        };
        _timelineGrid.EnableHeadersVisualStyles = false;
        _timelineGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(15, 52, 96);
        _timelineGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0, 212, 255);
        _timelineGrid.DefaultCellStyle.BackColor = Color.FromArgb(22, 33, 62);
        _timelineGrid.DefaultCellStyle.ForeColor = Color.FromArgb(220, 220, 220);
        _timelineGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(32, 68, 110);
        _timelineGrid.DefaultCellStyle.SelectionForeColor = Color.White;

        _timelineGrid.Columns.Add("colTime", "Date/Time");
        _timelineGrid.Columns.Add("colSeverity", "Severity");
        _timelineGrid.Columns.Add("colCategory", "Category");
        _timelineGrid.Columns.Add("colProvider", "Provider");
        _timelineGrid.Columns.Add("colEventId", "Event ID");
        _timelineGrid.Columns.Add("colSummary", "Summary");

        _cmbTimelineCategory.SelectedIndexChanged += (_, _) => RefreshTimelineGrid();
        _dtTimelineFrom.ValueChanged += (_, _) => RefreshTimelineGrid();
        _dtTimelineTo.ValueChanged += (_, _) => RefreshTimelineGrid();
        _chkTimelineServiceControl.CheckedChanged += (_, _) => RefreshTimelineGrid();
        _chkTimelineKernelPower.CheckedChanged += (_, _) => RefreshTimelineGrid();
        _chkTimelineWhea.CheckedChanged += (_, _) => RefreshTimelineGrid();
        _chkTimelineAppCrash.CheckedChanged += (_, _) => RefreshTimelineGrid();
        _chkTimelineStorage.CheckedChanged += (_, _) => RefreshTimelineGrid();
        _timelineGrid.CellDoubleClick += TimelineGridOnCellDoubleClick;

        root.Controls.Add(filterPanel, 0, 0);
        root.Controls.Add(_timelineGrid, 0, 1);
        _tabTimeline.Controls.Add(root);
    }

    private static Label CreateFilterLabel(string text) => new()
    {
        Text = text + ":",
        ForeColor = Color.FromArgb(200, 200, 200),
        AutoSize = true,
        Padding = new Padding(6, 6, 2, 0)
    };

    private void BuildDiagnosisTab()
    {
        var root = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 260,
            BackColor = Color.FromArgb(22, 33, 62)
        };

        _lblChartWarning = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Visible = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(140, 35, 35),
            ForeColor = Color.FromArgb(255, 230, 230),
            Padding = new Padding(10, 0, 0, 0),
            Font = new Font("Consolas", 9f, FontStyle.Bold)
        };

        try
        {
            _diagnosisChart = new Chart
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 33, 62)
            };
            var area = new ChartArea("DiagnosisArea")
            {
                BackColor = Color.FromArgb(18, 27, 50)
            };
            area.AxisX.MajorGrid.Enabled = false;
            area.AxisX.LabelStyle.ForeColor = Color.FromArgb(220, 220, 220);
            area.AxisX.Interval = 1;
            area.AxisY.Maximum = 100;
            area.AxisY.Minimum = 0;
            area.AxisY.Title = "Confidence (%)";
            area.AxisY.TitleForeColor = Color.FromArgb(180, 210, 245);
            area.AxisY.LabelStyle.ForeColor = Color.FromArgb(220, 220, 220);
            area.AxisY.MajorGrid.LineColor = Color.FromArgb(45, 62, 94);
            _diagnosisChart.ChartAreas.Add(area);

            var series = new Series("Hypotheses")
            {
                ChartType = SeriesChartType.Bar,
                IsValueShownAsLabel = true,
                LabelForeColor = Color.White,
                Font = new Font("Consolas", 8.5f, FontStyle.Bold)
            };
            _diagnosisChart.Series.Add(series);
            _diagnosisChart.Legends.Clear();
        }
        catch (Exception ex)
        {
            _lblChartWarning.Text = "⚠ Chart could not be loaded. Please install System.Data.SqlClient or switch to an alternate chart library.";
            _lblChartWarning.Visible = true;
            if (_rtbLog != null)
                AppendLog($"Chart initialization failed: {ex}", Color.Red);
            else
                System.Diagnostics.Debug.WriteLine($"Chart initialization failed: {ex}");
        }

        _rtbDiagnosis = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 33, 62),
            ForeColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Consolas", 9f),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false
        };

        if (_diagnosisChart != null)
            root.Panel1.Controls.Add(_diagnosisChart);
        root.Panel1.Controls.Add(_lblChartWarning);
        root.Panel2.Controls.Add(_rtbDiagnosis);
        _tabDiagnosis.Controls.Add(root);
    }

    private void SetInitialText()
    {
        _rtbOverview.Text = "Click 'Quick Scan' or 'Deep Scan' to begin diagnosis.\r\n\r\n" +
                            $"Running as: {(_isAdmin ? "Administrator" : "Standard User (some data sources limited)")}\r\n" +
                            $"Event look-back: {_settings.EventLookbackDays} day(s)\r\n";
        _rtbDiagnosis.Text = "Run scan to generate diagnosis.";
    }

    private void UpdateAdminStatus()
    {
        if (!_isAdmin)
        {
            _lblAdminBanner.Text = "⚠ Not running as administrator. WHEA log, minidumps, and parts of WMI may be incomplete. Use 'Run as administrator' for full diagnostics.";
            _lblAdminBanner.Visible = true;
            AppendLog("⚠ Running without elevation. Some collectors (WHEA, some WMI, minidumps) may return limited data. " +
                      "Right-click GPUDIAG and 'Run as administrator' for full results.", System.Drawing.Color.Yellow);
        }
        else if (_lblAdminBanner != null)
        {
            _lblAdminBanner.Visible = false;
        }
    }

    // ─── Scan logic ───────────────────────────────────────────────────────────

    private async void RunScanAsync(string scanType)
    {
        if (_scanning) return;
        var started = DateTime.UtcNow;
        _scanning = true;
        _btnQuickScan.Enabled = false;
        _btnDeepScan.Enabled = false;
        _progressBar.Visible = true;
        _btnExport.Enabled = false;
        _btnExportHtmlQuick.Enabled = false;
        _btnExportJsonQuick.Enabled = false;
        _btnExportZipQuick.Enabled = false;

        AppendLog($"Starting {scanType} scan...", System.Drawing.Color.Cyan);
        SetStatus($"Running {scanType} scan...");
        if (_lblScanBanner != null) _lblScanBanner.Visible = false;

        _report = new DiagnosticReport
        {
            ScanType = scanType,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"
        };

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
            var eventCollector = new EventLogCollector(msg => { _report.CollectionErrors.Add(msg); Log(msg); }, _settings.EventLookbackDays);
            _report.Events = await eventCollector.CollectAsync();
            _report.WheaEvents = eventCollector.ExtractWheaEvents(_report.Events);
            _report.Timeline = eventCollector.BuildTimeline(_report.Events);
            UpdateEventTabs();

            // WER / crash reports
            if (_settings.EnableWerCollector)
            {
                SetStatus("Collecting WER / crash reports...");
                var werCollector = new WerCollector(msg => { _report.CollectionErrors.Add(msg); Log(msg); });
                _report.WerEntries = await werCollector.CollectAsync();
            }
            else
            {
                Log("WER collector disabled in settings.");
                _report.WerEntries = new List<WerEntry>();
            }
            UpdateCrashLogsTab();

            // Java crash logs
            if (_settings.EnableJavaCollector)
            {
                SetStatus("Searching for Java crash logs...");
                var javaFolders = GetJavaSearchFolders();
                _report.JavaCrashLogs = JavaCrashLogParser.FindAndParseAll(javaFolders,
                    msg => { Log(msg); });
            }
            else
            {
                Log("Java crash log collector disabled in settings.");
                _report.JavaCrashLogs = new List<JavaCrashLog>();
            }
            UpdateCrashLogsTab();

            if (scanType == "Deep")
            {
                // Dump analysis
                SetStatus("Analyzing crash dumps...");
                var dumpAnalyzer = new MinidumpAnalyzer(msg => { _report.CollectionErrors.Add(msg); Log(msg); });
                _report.CrashDumps = await dumpAnalyzer.AnalyzeAsync(_customDumpFolder);
                UpdateDumpsTab();

                // Storage
            if (_settings.EnableWmiStorageCollector)
            {
                    SetStatus("Collecting storage evidence...");
                    var storageCollector = new StorageCollector(msg => { _report.CollectionErrors.Add(msg); Log(msg); });
                    _report.Storage = await storageCollector.CollectAsync(_report.Events);
                }
                else
                {
                    Log("WMI storage collector disabled in settings.");
                    _report.Storage = new StorageEvidence
                    {
                        PrimaryVsSecondaryAssessment = "Storage collector disabled in settings."
                    };
                }
                UpdateStorageTab();
            }
            else
            {
                _report.CrashDumps = new List<CrashDump>();
                _report.Storage = null;
                UpdateDumpsTab();
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
            UpdateOverviewTab();
            UpdateWarningBadges();

            _btnExport.Enabled = true;
            _btnExportHtmlQuick.Enabled = true;
            _btnExportJsonQuick.Enabled = true;
            _btnExportZipQuick.Enabled = true;
            _btnExportCsvQuick.Enabled = true;

            var elapsed = DateTime.UtcNow - started;
            var statusSummary = $"{scanType} scan complete. {_report.Events.Count} events, {_report.WheaEvents.Count} WHEA, {_report.CrashDumps.Count} dumps analyzed.";
            SetStatus(statusSummary);
            if (_lblScanBanner != null)
            {
                _lblScanBanner.Text = $"Scan complete: {_report.Events.Count} events, {_report.WheaEvents.Count} WHEA errors, {_report.CrashDumps.Count} dumps analyzed in {elapsed:hh\\:mm\\:ss}";
                _lblScanBanner.Text += $" | GPUDIAG v{_report.AppVersion} | {_report.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
                _lblScanBanner.BackColor = scanType.Equals("Deep", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(0, 68, 110)
                    : Color.FromArgb(0, 100, 75);
                _lblScanBanner.Visible = true;
            }
            AppendLog($"✓ {scanType} scan complete.", System.Drawing.Color.LimeGreen);
            _settings.TimelineCategory = _cmbTimelineCategory?.SelectedItem?.ToString() ?? "All";
            AppSettingsManager.Save(_settings);

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

            if (_report.CollectionErrors.Any())
            {
                sb.AppendLine("═══ ERRORS ENCOUNTERED DURING COLLECTION ═══");
                foreach (var err in _report.CollectionErrors.Distinct().Take(15))
                    sb.AppendLine($"  ⚠ {err}");
                if (_report.CollectionErrors.Count > 15)
                    sb.AppendLine($"  ...and {_report.CollectionErrors.Count - 15} more.");
                sb.AppendLine();
            }

            _rtbOverview.Text = sb.ToString();
        });
    }

    private void UpdateEventTabs()
    {
        InvokeIfNeeded(() =>
        {
            RefreshTimelineGrid();

            // WHEA tab
            var wSb = new System.Text.StringBuilder();
            wSb.AppendLine($"═══ WHEA HARDWARE ERRORS ({_report.WheaEvents.Count} events) ═══");
            wSb.AppendLine();

            if (!_report.WheaEvents.Any())
            {
                wSb.AppendLine($"No WHEA events found in the last {_settings.EventLookbackDays} days.");
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
            var serviceSummaries = ServiceFailureAnalyzer.Summarize(_report.Events, minimumCount: 1);
            var gpuServiceSummaries = serviceSummaries.Where(x => x.IsGpuRelated).ToList();

            gSb.AppendLine($"TDR / Display Reset events: {tdrEvts.Count}");
            gSb.AppendLine($"NVIDIA driver events:        {nvEvts.Count}");
            gSb.AppendLine($"WHEA PCIe/GPU events:        {_report.WheaPcieCount}");
            gSb.AppendLine();

            if (gpuServiceSummaries.Any())
            {
                gSb.AppendLine("Driver/Service failures (GPU-related):");
                foreach (var svc in gpuServiceSummaries.Take(8))
                {
                    gSb.AppendLine($"  ⚠ {svc.ServiceName} terminated/restarted {svc.Count} time(s) " +
                                   $"({svc.FirstOccurrenceUtc.ToLocalTime():MM-dd HH:mm} → {svc.LastOccurrenceUtc.ToLocalTime():MM-dd HH:mm})");
                }
                gSb.AppendLine();
            }
            else
            {
                gSb.AppendLine("No repeated GPU-related service failures detected.");
                gSb.AppendLine();
            }

            foreach (var e in tdrEvts.Concat(nvEvts).OrderByDescending(x => x.TimeCreated).Take(30))
            {
                gSb.AppendLine($"  {e.TimeCreated:yyyy-MM-dd HH:mm:ss}  [{e.Category}]  ID={e.EventId}");
                gSb.AppendLine($"  {e.Message?.Split('\n').FirstOrDefault()?.Trim()}");
                gSb.AppendLine();
            }

            _rtbGraphics.Text = gSb.ToString();

            // Drivers/services tab
            var sSb = new System.Text.StringBuilder();
            sSb.AppendLine("═══ DRIVER / SERVICE FAILURE SUMMARY ═══");
            sSb.AppendLine();
            var repeatedFailures = ServiceFailureAnalyzer.Summarize(_report.Events, minimumCount: 3);
            if (!repeatedFailures.Any())
            {
                sSb.AppendLine("No services exceeded failure threshold (>3) in selected look-back window.");
            }
            else
            {
                sSb.AppendLine("Services with frequent failures/restarts:");
                sSb.AppendLine();
                foreach (var svc in repeatedFailures)
                {
                    sSb.AppendLine($"{(svc.IsGpuRelated ? "⚠ " : "  ")}{svc.ServiceName}");
                    sSb.AppendLine($"    Count: {svc.Count}");
                    sSb.AppendLine($"    First: {svc.FirstOccurrenceUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                    sSb.AppendLine($"    Last:  {svc.LastOccurrenceUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                    if (svc.IsGpuRelated)
                        sSb.AppendLine("    Link: Supports NVIDIA dGPU/VRAM hypothesis.");
                    sSb.AppendLine();
                }
            }
            _rtbServices.Text = sSb.ToString();
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
                sb.AppendLine();

                var dumpInitFailures = storage.DiskEvents.Count(e => e.EventId == 46 && e.ProviderName.Contains("volmgr", StringComparison.OrdinalIgnoreCase));
                var dumpInitSuccess = storage.DiskEvents.Count(e => e.EventId == 162 && e.ProviderName.Contains("volmgr", StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"Crash dump initialization failures (Event 46): {dumpInitFailures}");
                sb.AppendLine($"Crash dump initialization successes (Event 162): {dumpInitSuccess}");
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
                sb.AppendLine($"   Confidence: {h.ConfidenceLabel} ({h.ConfidenceScore:P0}) {bar}   (+{h.SupportingEvidence.Count}, -{h.CounterEvidence.Count})");

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
            UpdateDiagnosisChart(diag);
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
        if (_report.Diagnosis == null)
        {
            MessageBox.Show("Please run a scan first.", "No data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var menu = new ContextMenuStrip();
        menu.Items.Add("Export HTML", null, (_, _) => ExportFile("html"));
        menu.Items.Add("Export JSON", null, (_, _) => ExportFile("json"));
        menu.Items.Add("Export ZIP Bundle", null, (_, _) => ExportFile("zip"));
        menu.Items.Add("Export Timeline CSV", null, (_, _) => ExportFile("csv"));
        menu.Show(_btnExport, new Point(0, _btnExport.Height));
    }

    private void ExportFile(string format)
    {
        if (_report.Diagnosis == null && !string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Please run a scan first.", "No data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            using var saveDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"GPUDIAG_Timeline_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export filtered timeline to CSV"
            };
            if (saveDialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                File.WriteAllText(saveDialog.FileName, BuildTimelineCsv(), System.Text.Encoding.UTF8);
                AppendLog($"✓ Timeline CSV exported: {saveDialog.FileName}", System.Drawing.Color.LimeGreen);
                _rtbExportStatus.AppendText($"Exported: {saveDialog.FileName}\r\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

            if (MessageBox.Show($"Report saved to:\r\n{path}\r\n\r\nOpen destination folder?", "Export Complete",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                var destinationFolder = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(destinationFolder))
                    destinationFolder = folder;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(destinationFolder) { UseShellExecute = true });
            }
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

    private void OpenSettings(object? sender, EventArgs e)
    {
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        _settings = dlg.Settings.Clone();
        _settings.TimelineCategory = _cmbTimelineCategory?.SelectedItem?.ToString() ?? _settings.TimelineCategory;
        AppSettingsManager.Save(_settings);
        if (_dtTimelineFrom != null && _dtTimelineTo != null)
        {
            _dtTimelineFrom.Value = DateTime.Now.AddDays(-_settings.EventLookbackDays);
            _dtTimelineTo.Value = DateTime.Now;
        }
        ApplyThemeFromSettings();
        AppendLog($"Settings saved. Look-back={_settings.EventLookbackDays} days, WER={_settings.EnableWerCollector}, Java={_settings.EnableJavaCollector}, WMI storage={_settings.EnableWmiStorageCollector}.", Color.Cyan);
        UpdateOverviewTab();
        RefreshTimelineGrid();
    }

    private void ApplyThemeFromSettings()
    {
        var isLight = string.Equals(_settings.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);
        var back = isLight ? Color.FromArgb(243, 246, 252) : Color.FromArgb(26, 26, 46);
        var panelBack = isLight ? Color.FromArgb(225, 233, 246) : Color.FromArgb(22, 33, 62);
        var text = isLight ? Color.FromArgb(25, 32, 45) : Color.FromArgb(224, 224, 224);
        var accent = ParseAccentColor(_settings.AccentColorHex);

        BackColor = back;
        ForeColor = text;
        if (_tabs != null)
            _tabs.BackColor = panelBack;

        foreach (TabPage tab in _tabs.TabPages)
        {
            tab.BackColor = panelBack;
            tab.ForeColor = text;
        }

        ApplyThemeToControlTree(this, isLight, back, panelBack, text, accent);
        if (_lblScanBanner != null)
            _lblScanBanner.ForeColor = Color.White;
    }

    private static void ApplyThemeToControlTree(Control root, bool isLight, Color back, Color panelBack, Color text, Color accent)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case RichTextBox rtb:
                    rtb.BackColor = panelBack;
                    rtb.ForeColor = text;
                    break;
                case DataGridView dgv:
                    dgv.BackgroundColor = panelBack;
                    dgv.DefaultCellStyle.BackColor = panelBack;
                    dgv.DefaultCellStyle.ForeColor = text;
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = isLight ? Color.FromArgb(196, 214, 240) : Color.FromArgb(15, 52, 96);
                    dgv.ColumnHeadersDefaultCellStyle.ForeColor = accent;
                    dgv.GridColor = isLight ? Color.FromArgb(190, 200, 220) : Color.FromArgb(42, 58, 92);
                    break;
                case Button btn:
                    btn.ForeColor = Color.White;
                    break;
                case ComboBox cb:
                    cb.BackColor = isLight ? Color.White : Color.FromArgb(15, 52, 96);
                    cb.ForeColor = isLight ? Color.Black : Color.White;
                    break;
                case DateTimePicker dt:
                    dt.BackColor = isLight ? Color.White : Color.FromArgb(15, 52, 96);
                    dt.ForeColor = isLight ? Color.Black : Color.White;
                    break;
                case Label lbl:
                    if (!lbl.Visible || lbl.BackColor == Color.Transparent)
                        lbl.ForeColor = text;
                    break;
                case Panel p:
                    p.BackColor = p.Dock == DockStyle.Top
                        ? (isLight ? Color.FromArgb(196, 214, 240) : Color.FromArgb(15, 52, 96))
                        : panelBack;
                    break;
                case TableLayoutPanel tlp:
                    tlp.BackColor = panelBack;
                    break;
                case FlowLayoutPanel flp:
                    flp.BackColor = panelBack;
                    break;
            }

            if (c.HasChildren)
                ApplyThemeToControlTree(c, isLight, back, panelBack, text, accent);
        }
    }

    private static Color ParseAccentColor(string? hex)
    {
        try
        {
            return ColorTranslator.FromHtml(string.IsNullOrWhiteSpace(hex) ? "#00D4FF" : hex);
        }
        catch
        {
            return ColorTranslator.FromHtml("#00D4FF");
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

    private void RefreshTimelineGrid()
    {
        if (_timelineGrid == null || _cmbTimelineCategory == null || _dtTimelineFrom == null || _dtTimelineTo == null)
            return;

        _timelineGrid.Rows.Clear();
        if (!_report.Timeline.Any())
        {
            if (_lblTimelineMeta != null)
                _lblTimelineMeta.Text = "No timeline anchor events available. Run a scan.";
            return;
        }

        var fromUtc = _dtTimelineFrom.Value.ToUniversalTime();
        var toUtc = _dtTimelineTo.Value.ToUniversalTime();
        if (toUtc < fromUtc)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }

        var selectedCategory = _cmbTimelineCategory.SelectedItem?.ToString() ?? "All";
        // Requery from report events whenever filters change so the correlation window
        // reflects the currently selected look-back range.
        var sourceEvents = _report.Events
            .Where(e => e.TimeCreated >= fromUtc && e.TimeCreated <= toUtc)
            .ToList();
        var rebuiltTimeline = new EventLogCollector(_ => { }, _settings.EventLookbackDays).BuildTimeline(sourceEvents);

        var filtered = rebuiltTimeline
            .Where(t => MatchesTimelineCategory(t, selectedCategory))
            .Where(MatchesQuickCategoryToggles)
            .OrderByDescending(t => t.Timestamp)
            .ToList();
        _filteredTimelineItems = filtered;
        _settings.TimelineCategory = selectedCategory;
        AppSettingsManager.Save(_settings);

        foreach (var timelineItem in filtered)
        {
            var rowIndex = _timelineGrid.Rows.Add(
                timelineItem.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                timelineItem.Severity.ToString(),
                GetTimelineCategoryLabel(timelineItem.Category),
                timelineItem.Source,
                timelineItem.EventId,
                NormalizeSummary(timelineItem.Summary, 180));
            _timelineGrid.Rows[rowIndex].Tag = timelineItem;
        }

        if (_lblTimelineMeta != null)
            _lblTimelineMeta.Text = $"Showing {filtered.Count} / {rebuiltTimeline.Count} anchor events from {sourceEvents.Count} total events in selected window.";
    }

    private bool MatchesQuickCategoryToggles(TimelineEvent timeline)
    {
        if (_chkTimelineServiceControl != null &&
            !_chkTimelineServiceControl.Checked &&
            timeline.Category == EventCategory.ServiceControl)
            return false;

        if (_chkTimelineKernelPower != null &&
            !_chkTimelineKernelPower.Checked &&
            timeline.Category == EventCategory.KernelPower)
            return false;

        if (_chkTimelineWhea != null &&
            !_chkTimelineWhea.Checked &&
            timeline.Category == EventCategory.Whea)
            return false;

        if (_chkTimelineAppCrash != null &&
            !_chkTimelineAppCrash.Checked &&
            timeline.Category == EventCategory.AppCrash)
            return false;

        if (_chkTimelineStorage != null &&
            !_chkTimelineStorage.Checked &&
            timeline.Category is EventCategory.Disk or EventCategory.Filesystem or EventCategory.Storage)
            return false;

        return true;
    }

    private static string NormalizeSummary(string? text, int maxLength)
    {
        var cleaned = text?.Replace("\r", " ").Replace("\n", " ").Trim() ?? string.Empty;
        if (cleaned.Length <= maxLength)
            return cleaned;
        return cleaned[..maxLength] + "…";
    }

    private static string GetTimelineCategoryLabel(EventCategory category) => category switch
    {
        EventCategory.KernelPower => "Kernel-Power",
        EventCategory.Whea => "WHEA",
        EventCategory.TdrDisplay => "TDR/Display",
        EventCategory.NvidiaDriver => "Graphics driver",
        EventCategory.Disk or EventCategory.Filesystem or EventCategory.Storage => "Disk/FileSystem",
        EventCategory.BugCheck or EventCategory.AppCrash => "Crash",
        _ => "Other"
    };

    private static bool MatchesTimelineCategory(TimelineEvent timeline, string selectedCategory)
    {
        if (string.Equals(selectedCategory, "All", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(GetTimelineCategoryLabel(timeline.Category), selectedCategory, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildTimelineCsv()
    {
        if (_filteredTimelineItems.Count == 0)
            RefreshTimelineGrid();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DateTime,Provider,EventId,Severity,Category,Summary");
        foreach (var t in _filteredTimelineItems)
        {
            sb.AppendLine(string.Join(",",
                Csv(t.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(t.Source),
                Csv(t.EventId.ToString()),
                Csv(t.Severity.ToString()),
                Csv(GetTimelineCategoryLabel(t.Category)),
                Csv(NormalizeSummary(t.Summary, 400))));
        }
        return sb.ToString();
    }

    private static string Csv(string value)
    {
        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");
        return $"\"{value}\"";
    }

    private void UpdateWarningBadges()
    {
        var wheaWarn = _report.WheaEvents.Any();
        var serviceWarn = ServiceFailureAnalyzer.Summarize(_report.Events, minimumCount: 3).Any();
        var memoryWarn = _report.Memory?.LastDiagResult?.ErrorsFound == true;

        _tabWhea.Text = wheaWarn ? "⚠ WHEA" : "WHEA";
        _tabServices.Text = serviceWarn ? "⚠ Drivers/Services" : "Drivers/Services";
        _tabMemory.Text = memoryWarn ? "⚠ Memory" : "Memory";
    }

    private void TimelineGridOnCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _timelineGrid.Rows[e.RowIndex];
        if (row.Tag is not TimelineEvent timelineEvent) return;

        var anchor = timelineEvent.AnchorEvent ??
                     _report.Events.FirstOrDefault(x =>
                         x.EventId == timelineEvent.EventId &&
                         x.ProviderName.Equals(timelineEvent.Source, StringComparison.OrdinalIgnoreCase) &&
                         Math.Abs((x.TimeCreated - timelineEvent.Timestamp).TotalMinutes) <= 1);

        using var dlg = new EventDetailsDialog(timelineEvent, anchor);
        dlg.ShowDialog(this);
    }

    private void UpdateDiagnosisChart(DiagnosisResult diag)
    {
        try
        {
            if (_diagnosisChart == null || _diagnosisChart.Series.Count == 0)
                return;

            var series = _diagnosisChart.Series[0];
            series.Points.Clear();
            if (!diag.TopHypotheses.Any())
                return;

            foreach (var h in diag.TopHypotheses.Take(5))
            {
                var label = h.Name.Length > 28 ? h.Name[..28] + "…" : h.Name;
                var pointIndex = series.Points.AddXY(label, Math.Round(h.ConfidenceScore * 100, 1));
                var point = series.Points[pointIndex];
                point.Color = h.ConfidenceScore >= 0.5
                    ? Color.FromArgb(220, 68, 68)
                    : h.ConfidenceScore >= 0.25
                        ? Color.FromArgb(255, 170, 0)
                        : Color.FromArgb(68, 170, 68);
                point.Label = $"{h.ConfidenceScore:P0} (+{h.SupportingEvidence.Count}, -{h.CounterEvidence.Count})";
                point.AxisLabel = label;
            }
            if (_lblChartWarning != null)
                _lblChartWarning.Visible = false;
        }
        catch (Exception ex)
        {
            if (_lblChartWarning != null)
            {
                _lblChartWarning.Text = "⚠ Chart could not be loaded. Please install System.Data.SqlClient or switch to an alternate chart library.";
                _lblChartWarning.Visible = true;
            }
            AppendLog($"Chart render failed: {ex}", Color.Red);
        }
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
