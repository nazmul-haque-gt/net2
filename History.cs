using System.Text.Json;
namespace NetSpeedTray;

public class History
{
    public const string TotalKey = "*TOTAL*";
    public Dictionary<string, Dictionary<string, long[]>> Days { get; set; } = new();

    readonly object gate = new();
    string dayKey = DateTime.Now.ToString("yyyy-MM-dd");
    long dayCheck = Environment.TickCount64;

    static string FilePath => Path.Combine(Settings.Dir, "history.json");

    public static History Load()
    {
        try { return JsonSerializer.Deserialize<History>(File.ReadAllText(FilePath)) ?? new History(); }
        catch { return new History(); }
    }

    public void Add(string app, long down, long up)
    {
        if (down == 0 && up == 0) return;
        lock (gate)
        {
            if (Environment.TickCount64 - dayCheck > 1000)
            {
                dayKey = DateTime.Now.ToString("yyyy-MM-dd");
                dayCheck = Environment.TickCount64;
            }
            if (!Days.TryGetValue(dayKey, out var day)) Days[dayKey] = day = new();
            if (!day.TryGetValue(app, out var v)) day[app] = v = new long[2];
            v[0] += down; v[1] += up;
        }
    }

    public Dictionary<string, long[]> Snapshot(int days)
    {
        var r = new Dictionary<string, long[]>();
        string cut = days == 0 ? "0000" : DateTime.Today.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
        lock (gate)
        {
            foreach (var d in Days)
            {
                if (string.CompareOrdinal(d.Key, cut) < 0) continue;
                foreach (var a in d.Value)
                {
                    if (!r.TryGetValue(a.Key, out var t)) r[a.Key] = t = new long[2];
                    t[0] += a.Value[0]; t[1] += a.Value[1];
                }
            }
        }
        return r;
    }

    public void Save()
    {
        try
        {
            string json;
            lock (gate)
            {
                string cut = DateTime.Today.AddDays(-90).ToString("yyyy-MM-dd");
                foreach (var k in Days.Keys.Where(k => string.CompareOrdinal(k, cut) < 0).ToList()) Days.Remove(k);
                json = JsonSerializer.Serialize(this);
            }
            Directory.CreateDirectory(Settings.Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }
}
