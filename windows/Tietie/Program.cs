namespace Tietie;

static class Program
{
    public const string Version = "0.3.0";

    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // 這三行必須在建立任何視窗之前設定
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Log(ex); };
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

    static void Log(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(Paths.Home, "error.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\r\n\r\n"); } catch { }
    }

    /// 出錯時不要跳出嚇人的對話框，記到 error.log 並小小提示。
    static void Report(Exception ex)
    {
        Log(ex);
        try { Toast.Show("貼貼遇到一個小問題，已記錄下來。可以再試一次"); } catch { }
    }
}
