using System.Globalization;
namespace NetSpeedTray;

static class Fmt
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    static (double v, int level) Scale(double bytesPerSec, Settings s)
    {
        double v = s.Bits ? bytesPerSec * 8 : bytesPerSec;
        double k = s.Bits ? 1000 : 1024;
        int lv;
        if (s.Unit == "M") lv = 2;
        else if (s.Unit == "K") lv = 1;
        else lv = v >= 1000 * k * k ? 3 : v >= 1000 * k ? 2 : 1;   // auto K -> M -> G
        return (v / Math.Pow(k, lv), lv);
    }

    // Short text for the tray icon, e.g. "4.5M"
    public static string Short(double bytesPerSec, Settings s)
    {
        var (v, lv) = Scale(bytesPerSec, s);
        string num = v < 10 ? v.ToString("0.0", C) : v.ToString("0", C);
        string unit = lv == 1 ? (s.Bits ? "k" : "K") : lv == 2 ? "M" : "G";
        return num + unit;
    }

    // Long text for the tooltip, e.g. "4.52 MB/s"
    public static string Long(double bytesPerSec, Settings s)
    {
        var (v, lv) = Scale(bytesPerSec, s);
        string num = v < 10 ? v.ToString("0.00", C) : v < 100 ? v.ToString("0.0", C) : v.ToString("0", C);
        string unit = s.Bits ? (lv == 1 ? "kbps" : lv == 2 ? "Mbps" : "Gbps")
                             : (lv == 1 ? "KB/s" : lv == 2 ? "MB/s" : "GB/s");
        return num + " " + unit;
    }

    public static string Bytes(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return (i == 0 ? v.ToString("0", C) : v.ToString("0.00", C)) + " " + u[i];
    }
}
