namespace NetSpeedTray;

sealed class HistoryForm : Form
{
    readonly History hist;
    readonly Func<string> status;
    readonly DataGridView grid = new();
    readonly ComboBox range = new();
    readonly Label note = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };

    public HistoryForm(History h, Func<string> status)
    {
        hist = h; this.status = status;
        Text = "Network usage history";
        Width = 640; Height = 480;
        StartPosition = FormStartPosition.CenterScreen;

        range.DropDownStyle = ComboBoxStyle.DropDownList;
        range.Items.AddRange(new object[] { "Today", "Last 7 days", "Last 30 days", "All time" });
        range.SelectedIndex = 0;
        range.Dock = DockStyle.Top;
        range.SelectedIndexChanged += (_, _) => Reload();

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor = SystemColors.Window;
        grid.Columns.Add("app", "Application");
        grid.Columns.Add("down", "Downloaded");
        grid.Columns.Add("up", "Uploaded");
        grid.Columns.Add("tot", "Total");
        grid.Columns[0].FillWeight = 160;

        note.Dock = DockStyle.Bottom;
        note.Height = 40;
        note.Padding = new Padding(6, 4, 6, 4);

        Controls.Add(grid);
        Controls.Add(note);
        Controls.Add(range);

        timer.Tick += (_, _) => Reload();
        timer.Start();
        FormClosed += (_, _) => timer.Dispose();
        Reload();
    }

    void Reload()
    {
        int days = range.SelectedIndex switch { 0 => 1, 1 => 7, 2 => 30, _ => 0 };
        var snap = hist.Snapshot(days);
        int first = grid.FirstDisplayedScrollingRowIndex;
        grid.Rows.Clear();
        if (snap.TryGetValue(History.TotalKey, out var t))
            grid.Rows.Add("All traffic (total)", Fmt.Bytes(t[0]), Fmt.Bytes(t[1]), Fmt.Bytes(t[0] + t[1]));
        foreach (var kv in snap.Where(k => k.Key != History.TotalKey)
                               .OrderByDescending(k => k.Value[0] + k.Value[1]).Take(300))
            grid.Rows.Add(kv.Key, Fmt.Bytes(kv.Value[0]), Fmt.Bytes(kv.Value[1]),
                          Fmt.Bytes(kv.Value[0] + kv.Value[1]));
        if (first >= 0 && first < grid.RowCount) grid.FirstDisplayedScrollingRowIndex = first;
        note.Text = status();
    }
}
