using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace NetSpeedTray;

sealed class TrayApp : ApplicationContext
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static readonly (string name, FontStyle style) FontSel = PickFont();

    readonly Settings st = Settings.Load();
    readonly History hist = History.Load();
    readonly NotifyIcon ni = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly Dictionary<string, (long rx, long tx)> last = new();
    NetworkInterface[] nics = Array.Empty<NetworkInterface>();
    DateTime lastNicRefresh = DateTime.MinValue, lastTick = DateTime.UtcNow, lastSave = DateTime.UtcNow;
    double up, down;
    string lastKey = "";
    Icon curIcon;
    HistoryForm form;
    AppTracker tracker;
    bool trackingOn;
    readonly bool admin = IsAdmin();

    static (string, FontStyle) PickFont()
    {
        foreach (var n in new[] { "Bahnschrift SemiBold Condensed", "Arial Narrow" })
        {
            try { using var f = new FontFamily(n); return (n, n.StartsWith("Bahn") ? FontStyle.Regular : FontStyle.Bold); }
            catch { }
        }
        return ("Segoe UI", FontStyle.Bold);
    }

    static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public TrayApp()
    {
        if (admin)
        {
            tracker = new AppTracker(hist);
            trackingOn = tracker.Start();
        }

        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => BuildMenu(menu);
        ni.ContextMenuStrip = menu;
        ni.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowHistory(); };
        ni.Text = "Network speed";
        ni.Visible = true;

        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
    }

    string TrackStatus() =>
        trackingOn ? $"Per-app tracking is ON (method: {tracker.Mode}). {tracker.Events} network events captured so far. Counts start from when tracking was first enabled."
        : admin ? "Per-app tracking FAILED to start. Right-click the tray icon > \"Per-app tracking failed - show details\"."
        : "Per-app tracking is OFF. Right-click the tray icon > \"Enable per-app tracking\" (needs admin).";

    void Tick()
    {
        var now = DateTime.UtcNow;
        double dt = (now - lastTick).TotalSeconds;
        lastTick = now;
        if (dt < 0.1) return;

        if ((now - lastNicRefresh).TotalSeconds > 10)
        {
            try
            {
                nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel).ToArray();
            }
            catch { }
            lastNicRefresh = now;
        }

        long dRx = 0, dTx = 0;
        foreach (var n in nics)
        {
            try
            {
                var s = n.GetIPv4Statistics();
                long rx = s.BytesReceived, tx = s.BytesSent;
                if (last.TryGetValue(n.Id, out var p))
                {
                    if (rx >= p.rx) dRx += rx - p.rx;
                    if (tx >= p.tx) dTx += tx - p.tx;
                }
                last[n.Id] = (rx, tx);
            }
            catch { }
        }
        down = dRx / dt; up = dTx / dt;
        hist.Add(History.TotalKey, dRx, dTx);
        UpdateIcon();

        if ((now - lastSave).TotalSeconds > 60) { hist.Save(); lastSave = now; }
    }

    void UpdateIcon()
    {
        string u = Fmt.Short(up, st), d = Fmt.Short(down, st);
        string key = u + "|" + d;
        if (key == lastKey) return;
        lastKey = key;

        var old = curIcon;
        curIcon = Render(u, d);
        ni.Icon = curIcon;
        old?.Dispose();
        ni.Text = $"Upload \u2191 {Fmt.Long(up, st)}\nDownload \u2193 {Fmt.Long(down, st)}";
    }

    Icon Render(string upText, string downText)
    {
        const int Arrow = 7, Band = 16;
        int textRight = 32 - Arrow - 1;
        var lines = st.UploadOnTop
            ? new[] { (upText, st.UploadColorArgb, true), (downText, st.DownloadColorArgb, false) }
            : new[] { (downText, st.DownloadColorArgb, false), (upText, st.UploadColorArgb, true) };

        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var sf = StringFormat.GenericTypographic;

            Font f = null;
            if (st.FontPx > 0)
                f = new Font(FontSel.name, st.FontPx, FontSel.style, GraphicsUnit.Pixel);
            else
            {
                for (float size = 18; size >= 7; size -= 0.5f)
                {
                    f?.Dispose();
                    f = new Font(FontSel.name, size, FontSel.style, GraphicsUnit.Pixel);
                    float w = Math.Max(g.MeasureString(lines[0].Item1, f, 100, sf).Width,
                                       g.MeasureString(lines[1].Item1, f, 100, sf).Width);
                    if (w <= textRight) break;
                }
            }

            for (int i = 0; i < 2; i++)
            {
                var (text, argb, isUp) = lines[i];
                using var brush = new SolidBrush(Color.FromArgb(argb));
                var sz = g.MeasureString(text, f, 100, sf);
                float y0 = i * Band;
                g.DrawString(text, f, brush, textRight - sz.Width, y0 + (Band - sz.Height) / 2f, sf);

                // arrow drawn as a triangle: always visible, up = upload, down = download
                float cx = 32 - Arrow / 2f, cy = y0 + Band / 2f;
                PointF[] tri = isUp
                    ? new[] { new PointF(cx, cy - 3.5f), new PointF(cx - 3.5f, cy + 3f), new PointF(cx + 3.5f, cy + 3f) }
                    : new[] { new PointF(cx, cy + 3.5f), new PointF(cx - 3.5f, cy - 3f), new PointF(cx + 3.5f, cy - 3f) };
                using var arrowBrush = new SolidBrush(Color.FromArgb(isUp ? st.UploadArrowColorArgb : st.DownloadArrowColorArgb));
                g.FillPolygon(arrowBrush, tri);
            }
            f.Dispose();
        }
        IntPtr h = bmp.GetHicon();
        try { using var tmp = Icon.FromHandle(h); return (Icon)tmp.Clone(); }
        finally { DestroyIcon(h); }
    }

    void BuildMenu(ContextMenuStrip m)
    {
        m.Items.Clear();
        m.Items.Add("Open history", null, (_, _) => ShowHistory());
        m.Items.Add("Upload text color...", null, (_, _) => PickColor(0));
        m.Items.Add("Download text color...", null, (_, _) => PickColor(1));
        m.Items.Add("Upload arrow color...", null, (_, _) => PickColor(2));
        m.Items.Add("Download arrow color...", null, (_, _) => PickColor(3));

        var size = new ToolStripMenuItem("Text size");
        void AddSize(string text, int px) =>
            size.DropDownItems.Add(new ToolStripMenuItem(text, null, (_, _) => { st.FontPx = px; Changed(); }) { Checked = st.FontPx == px });
        AddSize("Auto-fit (largest that fits)", 0);
        foreach (int px in new[] { 10, 11, 12, 13, 14, 15, 16, 18, 20 }) AddSize(px + " px", px);
        m.Items.Add(size);

        var unit = new ToolStripMenuItem("Unit");
        void AddUnit(string text, string val) =>
            unit.DropDownItems.Add(new ToolStripMenuItem(text, null, (_, _) => { st.Unit = val; Changed(); }) { Checked = st.Unit == val });
        AddUnit("Auto (KB \u2194 MB)", "Auto");
        AddUnit("Always KB / kbps", "K");
        AddUnit("Always MB / Mbps", "M");
        m.Items.Add(unit);

        m.Items.Add(new ToolStripMenuItem("Use bits (kbps / Mbps)", null, (_, _) => { st.Bits = !st.Bits; Changed(); }) { Checked = st.Bits });
        m.Items.Add(new ToolStripMenuItem("Upload on top (off = download on top)", null, (_, _) => { st.UploadOnTop = !st.UploadOnTop; Changed(); }) { Checked = st.UploadOnTop });
        m.Items.Add(new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutostart()) { Checked = IsAutostart() });
        m.Items.Add(new ToolStripSeparator());

        if (trackingOn) m.Items.Add(new ToolStripMenuItem("Per-app tracking: ON (" + tracker.Mode + ")") { Enabled = false });
        else if (admin) m.Items.Add("Per-app tracking failed - show details", null, (_, _) => ShowTrackError());
        else m.Items.Add("Enable per-app tracking (restart as admin)", null, (_, _) => RestartAsAdmin());

        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Exit", null, (_, _) => Quit());
    }

    void ShowTrackError()
    {
        string e = tracker?.Error ?? "Unknown error";
        if (e.Length > 1800) e = e.Substring(0, 1800) + "...";
        MessageBox.Show("Full log: " + Path.Combine(Settings.Dir, "tracking-error.log") + "\n\n" + e,
            "Per-app tracking error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void Changed() { st.Save(); lastKey = ""; UpdateIcon(); }

    void PickColor(int which)
    {
        int cur = which switch { 0 => st.UploadColorArgb, 1 => st.DownloadColorArgb, 2 => st.UploadArrowColorArgb, _ => st.DownloadArrowColorArgb };
        using var dlg = new ColorDialog { Color = Color.FromArgb(cur), FullOpen = true };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        int c = dlg.Color.ToArgb();
        switch (which)
        {
            case 0: st.UploadColorArgb = c; break;
            case 1: st.DownloadColorArgb = c; break;
            case 2: st.UploadArrowColorArgb = c; break;
            default: st.DownloadArrowColorArgb = c; break;
        }
        Changed();
    }

    void ShowHistory()
    {
        if (form != null && !form.IsDisposed) { form.Activate(); return; }
        form = new HistoryForm(hist, TrackStatus);
        form.FormClosed += (_, _) => form = null;
        form.Show();
    }

    static bool IsAutostart()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("NetSpeedTray") != null; }
        catch { return false; }
    }

    static void ToggleAutostart()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (k.GetValue("NetSpeedTray") != null) k.DeleteValue("NetSpeedTray");
            else k.SetValue("NetSpeedTray", "\"" + Environment.ProcessPath + "\"");
        }
        catch { }
    }

    void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath)
            { UseShellExecute = true, Verb = "runas", Arguments = "--elevated" });
            Quit();
        }
        catch (Win32Exception) { /* user cancelled the UAC prompt */ }
    }

    void Quit()
    {
        timer.Stop();
        tracker?.Dispose();
        hist.Save();
        st.Save();
        ni.Visible = false;
        ni.Dispose();
        curIcon?.Dispose();
        ExitThread();
    }
}
