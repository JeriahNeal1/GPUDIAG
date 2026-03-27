using GPUDIAG.Core.Models;

namespace GPUDIAG.Dialogs;

public sealed class EventDetailsDialog : Form
{
    public EventDetailsDialog(TimelineEvent timelineEvent, EventLogItem? anchor)
    {
        Text = "Event Details";
        StartPosition = FormStartPosition.CenterParent;
        Width = 980;
        Height = 700;
        BackColor = Color.FromArgb(22, 33, 62);
        ForeColor = Color.FromArgb(224, 224, 224);
        Font = new Font("Consolas", 9f);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 270,
            BackColor = Color.FromArgb(15, 52, 96)
        };

        var top = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(18, 27, 50),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f)
        };

        var details = new List<string>
        {
            $"Timestamp: {timelineEvent.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
            $"Severity: {timelineEvent.Severity}",
            $"Source: {timelineEvent.Source}",
            $"Summary: {timelineEvent.Summary}",
            string.Empty
        };

        if (anchor != null)
        {
            details.Add($"Category: {anchor.Category}");
            details.Add($"Provider: {anchor.ProviderName}");
            details.Add($"Event ID: {anchor.EventId}");
            details.Add($"Level: {anchor.LevelName}");
            details.Add(string.Empty);
            details.Add("Message:");
            details.Add(anchor.Message);
            if (!string.IsNullOrWhiteSpace(anchor.Xml))
            {
                details.Add(string.Empty);
                details.Add("Raw XML:");
                details.Add(anchor.Xml);
            }
        }

        top.Text = string.Join(Environment.NewLine, details.Where(x => x != null));
        split.Panel1.Controls.Add(top);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Color.FromArgb(18, 27, 50),
            BorderStyle = BorderStyle.None,
            GridColor = Color.FromArgb(45, 62, 94),
            ForeColor = Color.White,
            RowHeadersVisible = false
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(15, 52, 96);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0, 212, 255);
        grid.EnableHeadersVisualStyles = false;

        grid.Columns.Add("colTime", "Date/Time");
        grid.Columns.Add("colSeverity", "Severity");
        grid.Columns.Add("colCategory", "Category");
        grid.Columns.Add("colProvider", "Provider");
        grid.Columns.Add("colEventId", "Event ID");
        grid.Columns.Add("colSummary", "Summary");

        foreach (var correlated in timelineEvent.CorrelatedEvents.OrderByDescending(e => e.TimeCreated))
        {
            var summary = correlated.Message?.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
            if (summary.Length > 200)
                summary = summary[..200] + "…";

            grid.Rows.Add(
                correlated.TimeCreated.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                correlated.LevelName,
                correlated.Category.ToString(),
                correlated.ProviderName,
                correlated.EventId,
                summary);
        }

        split.Panel2.Controls.Add(grid);
        Controls.Add(split);

        var btnClose = new Button
        {
            Text = "Close",
            Dock = DockStyle.Bottom,
            Height = 34,
            BackColor = Color.FromArgb(80, 80, 120),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);
    }
}
