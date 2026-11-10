namespace Tietie;

/// 常駐在工作列右下角的貼貼。
sealed class TrayApp : ApplicationContext
{
    public readonly Store Store = new();
    public readonly ClipboardWatcher Watcher;
    public readonly HistoryForm Panel;
    readonly HotKeyWindow hotKey = new();
    readonly NotifyIcon tray;
    readonly ContextMenuStrip trayMenu = new();
    SettingsForm? settings;
    AboutForm? about;
    public bool HotKeyOk { get; private set; }

    public TrayApp(bool showPanel = false, bool noOnboarding = false)
    {
        Watcher = new ClipboardWatcher(Store);
        Panel = new HistoryForm(Store);
        Panel.Picked += Paste;
        Watcher.Changed += Panel.RefreshIfVisible;
        Watcher.ShowRequested += Panel.ShowPanel;
        hotKey.Pressed += Panel.TogglePanel;

        trayMenu.Font = Theme.UiFont(9.75f);
        trayMenu.Opening += (_, _) => BuildMenu();
        BuildMenu();
        tray = new NotifyIcon { Text = "貼貼", ContextMenuStrip = trayMenu, Visible = true };
        UpdateIcon();
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Panel.TogglePanel(); };

        RegisterHotKey();

        if (!Settings.Data.Onboarded && !noOnboarding) new OnboardingForm().Show();
        else AutoStart.Apply(Settings.Data.LaunchAtLogin);
        if (showPanel) Panel.ShowPanel();
    }

    void RegisterHotKey()
    {
        var choice = HotKeyChoice.Find(Settings.Data.HotKey);
        HotKeyOk = hotKey.Register(choice);
        if (!HotKeyOk) Toast.Show($"快捷鍵 {choice.Label} 被別的程式佔用了，請到貼貼的設定換一組", 4);
    }

    void UpdateIcon()
    {
        var old = tray.Icon;
        tray.Icon = Icons.Tray(Settings.Data.Paused);
        old?.Dispose();
        tray.Text = Settings.Data.Paused ? "貼貼（已暫停記錄）" : "貼貼";
    }

    // MARK: 貼上

    void Paste(ClipItem item, bool asText)
    {
        bool ok;
        Watcher.IgnoreFor(1200);
        if (asText)
        {
            var text = item.Ocr.Trim();
            if (text.Length == 0) { Toast.Show("這張圖片裡沒有讀到文字"); return; }
            ok = Paster.WriteText(text);
        }
        else
        {
            ok = Paster.Write(item);
            if (!ok && item.Kind == "image") { Toast.Show("這張圖片的檔案不見了，沒辦法貼上"); return; }
        }
        if (!ok) { Toast.Show("剪貼簿被別的程式佔用，請再試一次"); return; }
        Store.Touch(item.Id);
        var target = Panel.PreviousWindow;
        Paster.AfterModifiersReleased(() =>
        {
            Panel.HidePanel();
            if (Paster.AutoPaste) Paster.PasteInto(target);
            else Toast.Show("已複製，按 Ctrl+V 貼上");
        });
    }

    // MARK: 選單

    void BuildMenu()
    {
        trayMenu.Items.Clear();
        var d = Settings.Data;
        var choice = HotKeyChoice.Find(d.HotKey);
        var open = trayMenu.Items.Add($"開啟貼貼清單（{choice.Label}）", null, (_, _) => Panel.ShowPanel());
        open.Font = Theme.UiFont(9.75f, FontStyle.Bold);
        if (!HotKeyOk)
        {
            var warn = trayMenu.Items.Add($"快捷鍵 {choice.Label} 被佔用，請到設定更換");
            warn.Enabled = false;
        }
        trayMenu.Items.Add(new ToolStripSeparator());
        var pause = new ToolStripMenuItem("暫停記錄") { Checked = d.Paused };
        pause.Click += (_, _) =>
        {
            Settings.Update(s => s.Paused = !s.Paused);
            UpdateIcon();
            Toast.Show(Settings.Data.Paused ? "已暫停記錄" : "恢復記錄");
        };
        trayMenu.Items.Add(pause);
        trayMenu.Items.Add("清除最近 10 分鐘的記錄", null, (_, _) =>
        {
            Store.ClearRecent(600);
            Panel.RefreshIfVisible();
            Toast.Show("已清除最近 10 分鐘的記錄");
        });
        trayMenu.Items.Add("清除全部歷史記錄（保留釘選）", null, (_, _) =>
        {
            Store.ClearUnpinned();
            Panel.RefreshIfVisible();
            Toast.Show("已清除歷史記錄");
        });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("設定…", null, (_, _) => ShowSettings());
        trayMenu.Items.Add("關於貼貼", null, (_, _) => ShowAbout());
        trayMenu.Items.Add(new ToolStripSeparator());
        long bytes = Store.ImageBytes();
        string size = bytes < 1024 * 1024 ? $"{bytes / 1024} KB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";
        var info = trayMenu.Items.Add($"已記錄 {Store.Count()} 筆 · 圖片 {size}");
        info.Enabled = false;
        trayMenu.Items.Add("結束貼貼", null, (_, _) => Quit());
    }

    void ShowSettings()
    {
        if (settings == null || settings.IsDisposed)
        {
            settings = new SettingsForm(Store);
            settings.HotKeyChanged += RegisterHotKey;
        }
        settings.RefreshUsage();
        settings.Show();
        settings.Activate();
    }

    void ShowAbout()
    {
        if (about == null || about.IsDisposed) about = new AboutForm();
        about.Show();
        about.Activate();
    }

    public void Quit()
    {
        tray.Visible = false;
        tray.Dispose();
        hotKey.Dispose();
        Watcher.Dispose();
        Store.Dispose();
        ExitThread();
    }
}
