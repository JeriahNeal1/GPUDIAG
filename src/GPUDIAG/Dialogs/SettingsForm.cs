using GPUDIAG.Configuration;

namespace GPUDIAG.Dialogs;

public sealed class SettingsForm : Form
{
    private readonly NumericUpDown _numLookback;
    private readonly CheckBox _chkWer;
    private readonly CheckBox _chkJava;
    private readonly CheckBox _chkStorage;

    public AppSettings Settings { get; }

    public SettingsForm(AppSettings current)
    {
        Settings = current.Clone();

        Text = "GPUDIAG Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 460;
        Height = 300;
        BackColor = Color.FromArgb(22, 33, 62);
        ForeColor = Color.FromArgb(224, 224, 224);
        Font = new Font("Consolas", 9f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
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

        layout.Controls.Add(buttons, 0, 4);
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
        DialogResult = DialogResult.OK;
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
