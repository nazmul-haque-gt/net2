using System.Diagnostics;
using System.Net;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace NetSpeedTray;

// Per-app byte counting via Windows ETW. Needs admin.
// Method 1: "Microsoft-Windows-Kernel-Network" provider in its own session (preferred).
// Method 2: classic "NT Kernel Logger" session (fallback).
sealed class AppTracker : IDisposable
{
    readonly History hist;
    readonly Dictionary<int, string> names = new();
    TraceEventSession session;
    Thread thread;

    public string Mode = "";
    public string Error = "";
    public long Events;

    public AppTracker(History h) { hist = h; }

    public bool Start()
    {
        var errors = new List<string>();
        if (Try(StartProvider, "provider", errors)) return true;
        if (Try(StartKernel, "kernel", errors)) return true;
        Error = string.Join("\n\n", errors);
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(Path.Combine(Settings.Dir, "tracking-error.log"), Error);
        }
        catch { }
        return false;
    }

    bool Try(Action starter, string label, List<string> errors)
    {
        try { starter(); Mode = label; return true; }
        catch (Exception ex)
        {
            errors.Add($"[{label}] {ex}");
            Cleanup();
            return false;
        }
    }

    void StartProvider()
    {
        session = new TraceEventSession("NetSpeedTray-Net");
        session.EnableProvider(new Guid("7DD42A49-5329-4832-8DFD-43D979153A88")); // Microsoft-Windows-Kernel-Network
        session.Source.Dynamic.All += OnProviderEvent;
        Run();
    }

    void StartKernel()
    {
        session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
        session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
        var k = session.Source.Kernel;
        k.TcpIpRecv += d => Add(d.ProcessID, d.size, 0, d.daddr);
        k.TcpIpSend += d => Add(d.ProcessID, 0, d.size, d.daddr);
        k.UdpIpRecv += d => Add(d.ProcessID, d.size, 0, d.daddr);
        k.UdpIpSend += d => Add(d.ProcessID, 0, d.size, d.daddr);
        Run();
    }

    void Run()
    {
        var s = session;
        thread = new Thread(() =>
        {
            try { s.Source.Process(); }
            catch (Exception ex) { Error = "Processing stopped: " + ex.Message; }
        }) { IsBackground = true, Name = "EtwNet" };
        thread.Start();
    }

    void OnProviderEvent(TraceEvent e)
    {
        int id = (int)e.ID;
        string en = (e.EventName ?? "").ToLowerInvariant();
        bool send = id == 10 || id == 26 || id == 42 || id == 58 || en.Contains("datasent");   // data sent (TCP/UDP, v4/v6)
        bool recv = id == 11 || id == 27 || id == 43 || id == 59 || en.Contains("datarecv") || en.Contains("datareceived");
        if (!send && !recv) return;
        try
        {
            object po = Field(e, "PID", "ProcessId"), so = Field(e, "size", "bytes");
            if (po == null || so == null) return;
            if (IsLoopback(Field(e, "daddr"))) return;
            int pid = Convert.ToInt32(po);
            long size = Convert.ToInt64(so);
            if (pid <= 0 || size <= 0) return;
            Events++;
            hist.Add(Name(pid), recv ? size : 0, send ? size : 0);
        }
        catch { }
    }

    // case-insensitive payload lookup
    static object Field(TraceEvent e, params string[] names)
    {
        var pn = e.PayloadNames;
        for (int i = 0; i < pn.Length; i++)
            foreach (var n in names)
                if (string.Equals(pn[i], n, StringComparison.OrdinalIgnoreCase))
                    return e.PayloadValue(i);
        return null;
    }

    static bool IsLoopback(object o) =>
        o is IPAddress ip ? IPAddress.IsLoopback(ip) : o is uint u && (u & 0xFF) == 127;

    void Add(int pid, long down, long up, IPAddress remote)
    {
        if (remote != null && IPAddress.IsLoopback(remote)) return;
        Events++;
        hist.Add(Name(pid), down, up);
    }

    string Name(int pid)
    {
        if (names.TryGetValue(pid, out var n)) return n;
        try { n = Process.GetProcessById(pid).ProcessName; }
        catch { n = pid == 4 ? "System" : $"PID {pid} (exited)"; }
        if (names.Count > 5000) names.Clear();
        names[pid] = n;
        return n;
    }

    void Cleanup()
    {
        try { session?.Dispose(); } catch { }
        session = null;
    }

    public void Dispose() => Cleanup();
}
