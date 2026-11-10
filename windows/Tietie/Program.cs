namespace Tietie;

static class Program
{
    public const string Version = "0.2.0";

    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        if (args.Contains("--version"))
        {
            Console.WriteLine("Tietie " + Version);
            return 0;
        }
        if (args.Contains("--selftest")) return SelfTest.Run();
        int r = Array.IndexOf(args, "--render-ui");
        if (r >= 0 && r + 1 < args.Length) return SelfTest.RenderUi(args[r + 1]);
        if (args.Contains("--e2e")) return EndToEnd.Run();

        // 已經有一個貼貼在跑，就請它打開清單
        using var mutex = new Mutex(true, "Local\\TietieClipboardManager", out bool first);
        if (!first)
        {
            var existing = Native.FindWindow(null, ClipboardWatcher.WindowTitle);
            if (existing != IntPtr.Zero) Native.PostMessage(existing, ClipboardWatcher.WM_SHOW, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }
        Application.Run(new TrayApp(showPanel: args.Contains("--show"), noOnboarding: args.Contains("--no-onboarding")));
        return 0;
    }
}
