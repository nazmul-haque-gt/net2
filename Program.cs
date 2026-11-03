namespace NetSpeedTray;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--elevated")) Thread.Sleep(1500); // let the old instance exit
        using var mutex = new Mutex(true, "NetSpeedTray_SingleInstance", out bool created);
        if (!created) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
