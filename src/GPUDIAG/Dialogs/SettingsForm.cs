using GPUDIAG.Configuration;

namespace GPUDIAG.Dialogs;

public sealed class SettingsForm : Form
{
    private readonly NumericUpDown _numLookback;
    private readonly CheckBox _chkWer;
    private readonly CheckBox _chkJava;
    private readonly CheckBox _chkStorage;
    private readonly ComboBox _cmbTheme;
    private readonly Label _lblAccent;
    private Color _accentColor;

    public AppSettings Settings { get; }

    public SettingsForm(AppSettings current)
    {
        Settings = current.Clone();

        Text = "GPUDIAG Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 500;
        Height = 420;
        BackColor = Color.FromArgb(22, 33, 62);
        ForeColor = Color.FromArgb(224, 224, 224);
        Font = new Font("Consolas", 9f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        layout.Controls.Add(CreateLabel("Event log look-back period (days):"), 0, 0);
        _numLookback = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 365,
            Value = Math.Clamp(Settings.EventLookbackDays, 1, 365),
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 52, 96),
            ForeColor = Color.White
        };
        layout.Controls.Add(_numLookback, 1, 0);

        _chkWer = CreateCheck("Enable WER collector", Settings.EnableWerCollector);
        _chkJava = CreateCheck("Enable Java crash log collector", Settings.EnableJavaCollector);
        _chkStorage = CreateCheck("Enable WMI storage collector (Deep scan)", Settings.EnableWmiStorageCollector);

        layout.Controls.Add(_chkWer, 0, 1);
        layout.SetColumnSpan(_chkWer, 2);
        layout.Controls.Add(_chkJava, 0, 2);
        layout.SetColumnSpan(_chkJava, 2);
        layout.Controls.Add(_chkStorage, 0, 3);
        layout.SetColumnSpan(_chkStorage, 2);

        layout.Controls.Add(CreateLabel("Theme mode:"), 0, 4);
        _cmbTheme = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(15, 52, 96),
            ForeColor = Color.White
        };
        _cmbTheme.Items.AddRange(new object[] { "Dark", "Light" });
        _cmbTheme.SelectedItem = string.Equals(Settings.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
        layout.Controls.Add(_cmbTheme, 1, 4);

        layout.Controls.Add(CreateLabel("Accent color:"), 0, 5);
        _accentColor = ParseAccent(Settings.AccentColorHex);
        var btnAccent = CreateButton("Choose…", Color.FromArgb(58, 88, 118));
        btnAccent.Click += (_, _) => PickAccentColor();
        var accentPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _lblAccent = new Label
        {
            AutoSize = true,
            Text = ColorTranslator.ToHtml(_accentColor),
            Padding = new Padding(8, 8, 0, 0)
        };
        accentPanel.Controls.Add(btnAccent);
        accentPanel.Controls.Add(_lblAccent);
        layout.Controls.Add(accentPanel, 1, 5);

        var btnReset = CreateButton("Reset Settings", Color.FromArgb(120, 72, 0));
        btnReset.Click += (_, _) => ResetToDefaults();
        layout.Controls.Add(btnReset, 0, 6);
        layout.SetColumnSpan(btnReset, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var btnCancel = CreateButton("Cancel", Color.FromArgb(80, 80, 120));
        var btnSave = CreateButton("Save", Color.FromArgb(0, 120, 215));
        btnSave.Click += (_, _) => SaveAndClose();
        btnCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnSave);

        layout.Controls.Add(buttons, 0, 7);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private void SaveAndClose()
    {
        Settings.EventLookbackDays = (int)_numLookback.Value;
        Settings.EnableWerCollector = _chkWer.Checked;
        Settings.EnableJavaCollector = _chkJava.Checked;
        Settings.EnableWmiStorageCollector = _chkStorage.Checked;
        Settings.ThemeMode = _cmbTheme.SelectedItem?.ToString() ?? "Dark";
        Settings.AccentColorHex = ColorTranslator.ToHtml(_accentColor);
        DialogResult = DialogResult.OK;
    }

    private void PickAccentColor()
    {
        using var dlg = new ColorDialog { Color = _accentColor };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        _accentColor = dlg.Color;
        _lblAccent.Text = ColorTranslator.ToHtml(_accentColor);
    }

    private void ResetToDefaults()
    {
        _numLookback.Value = 30;
        _chkWer.Checked = true;
        _chkJava.Checked = true;
        _chkStorage.Checked = true;
        _cmbTheme.SelectedItem = "Dark";
        _accentColor = ColorTranslator.FromHtml("#00D4FF");
        _lblAccent.Text = "#00D4FF";
    }

    private static Color ParseAccent(string? hex)
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

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Padding = new Padding(0, 6, 0, 0)
    };

    private static CheckBox CreateCheck(string text, bool isChecked) => new()
    {
        Text = text,
        Checked = isChecked,
        AutoSize = true,
        Margin = new Padding(0, 8, 0, 4)
    };

    private static Button CreateButton(string text, Color backColor) => new()
    {
        Text = text,
        BackColor = backColor,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        AutoSize = true,
        Padding = new Padding(10, 4, 10, 4),
        Margin = new Padding(8, 8, 0, 0)
    };
}
