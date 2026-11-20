namespace Tietie;

/// 常駐在工作列右下角的貼貼。
sealed class TrayApp : ApplicationContext
{
    public readonly Store Store = new();
    public readonly ClipboardWatcher Watcher;
    public HistoryForm Panel { get; private set; }
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
        tray = new NotifyIcon { Text = L.T("app.name"), ContextMenuStrip = trayMenu, Visible = true };
        UpdateIcon();
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Panel.TogglePanel(); };

        RegisterHotKey();

        if (!Settings.Data.Onboarded && !noOnboarding) new OnboardingForm().Show();
        else AutoStart.Apply(Settings.Data.LaunchAtLogin);
        if (showPanel) Panel.ShowPanel();
        // 測試時（指定 TIETIE_HOME）不自動檢查，除非另外指定了假的更新網址
        if (!Paths.TestMode || Environment.GetEnvironmentVariable("TIETIE_UPDATE_URL") != null) Updater.StartAutomaticChecks();
    }

    void RegisterHotKey()
    {
        var choice = HotKeyChoice.Find(Settings.Data.HotKey);
        HotKeyOk = hotKey.Register(choice);
        if (!HotKeyOk) Toast.Show(L.T("toast.hotkeyTaken", choice.Label), 4);
    }

    void UpdateIcon()
    {
        var old = tray.Icon;
        tray.Icon = Icons.Tray(Settings.Data.Paused);
        old?.Dispose();
        tray.Text = Settings.Data.Paused ? L.T("app.tooltipPaused") : L.T("app.name");
    }

    // MARK: 貼上

    void Paste(ClipItem item, bool asText)
    {
        bool ok;
        Watcher.IgnoreFor(1200);
        if (asText)
        {
            var text = item.Ocr.Trim();
            if (text.Length == 0) { Toast.Show(L.T("toast.noTextInImage")); return; }
            ok = Paster.WriteText(text);
        }
        else
        {
            ok = Paster.Write(item);
            if (!ok && item.Kind == "image") { Toast.Show(L.T("toast.imageMissing")); return; }
        }
        if (!ok) { Toast.Show(L.T("win.toast.clipboardBusy")); return; }
        try { Store.Touch(item.Id); } catch { }
        var target = Panel.PreviousWindow;
        Paster.AfterModifiersReleased(() =>
        {
            Panel.HidePanel();
            if (Paster.AutoPaste) Paster.PasteInto(target);
            else Toast.Show(L.T("win.toast.copied"));
        });
    }

    // MARK: 選單

    void BuildMenu()
    {
        trayMenu.Items.Clear();
        var d = Settings.Data;
        var choice = HotKeyChoice.Find(d.HotKey);
        var open = trayMenu.Items.Add(L.T("win.menu.open", choice.Label), null, (_, _) => Panel.ShowPanel());
        open.Font = Theme.UiFont(9.75f, FontStyle.Bold);
        if (!HotKeyOk)
        {
            var warn = trayMenu.Items.Add(L.T("menu.hotkeyTaken", choice.Label));
            warn.Enabled = false;
        }
        trayMenu.Items.Add(new ToolStripSeparator());
        var pause = new ToolStripMenuItem(L.T("menu.pause")) { Checked = d.Paused };
        pause.Click += (_, _) =>
        {
            Settings.Update(s => s.Paused = !s.Paused);
            UpdateIcon();
            Toast.Show(Settings.Data.Paused ? L.T("toast.paused") : L.T("toast.resumed"));
        };
        trayMenu.Items.Add(pause);
        trayMenu.Items.Add(L.T("menu.clearRecent"), null, (_, _) =>
        {
            Store.ClearRecent(600);
            Panel.RefreshIfVisible();
            Toast.Show(L.T("toast.clearedRecent"));
        });
        trayMenu.Items.Add(L.T("menu.clearAll"), null, (_, _) =>
        {
            Store.ClearUnpinned();
            Panel.RefreshIfVisible();
            Toast.Show(L.T("toast.clearedAll"));
        });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(L.T("menu.settings"), null, (_, _) => ShowSettings());
        trayMenu.Items.Add(LanguageMenu());
        trayMenu.Items.Add(L.T("menu.about"), null, (_, _) => ShowAbout());
        trayMenu.Items.Add(L.T("menu.checkUpdate"), null, (_, _) => _ = Updater.Check(true));
        trayMenu.Items.Add(new ToolStripSeparator());
        long bytes = Store.ImageBytes();
        string size = bytes < 1024 * 1024 ? $"{bytes / 1024} KB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";
        var info = trayMenu.Items.Add(L.T("menu.stats", Store.Count(), size));
        info.Enabled = false;
        trayMenu.Items.Add(L.T("menu.quit"), null, (_, _) => Quit());
    }

    /// 選單裡的「語言」：跟隨系統，或指定一種語言。改了馬上生效。
    ToolStripMenuItem LanguageMenu()
    {
        var setting = Settings.Data.Language;
        // 不是英文時在後面加上 Language，看不懂目前語言的人也找得到
        var title = Lang.Current == "en" ? L.T("menu.language") : L.T("menu.language") + "  Language";
        var item = new ToolStripMenuItem(title);
        var system = new ToolStripMenuItem(L.T("menu.languageSystem", Lang.Names[Lang.SystemLanguage()]))
        {
            Checked = !Lang.Supported.Contains(setting),
        };
        system.Click += (_, _) => ChooseLanguage("system");
        item.DropDownItems.Add(system);
        item.DropDownItems.Add(new ToolStripSeparator());
        foreach (var code in Lang.Supported)
        {
            var entry = new ToolStripMenuItem(Lang.Names[code]) { Checked = setting == code };
            entry.Click += (_, _) => ChooseLanguage(code);
            item.DropDownItems.Add(entry);
        }
        return item;
    }

    public void ChooseLanguage(string code)
    {
        ApplyLanguage(code);
        Toast.Show(L.T("toast.languageChanged", Lang.Names[Lang.Current]));
    }

    /// 換語言：存起來，然後把清單、設定、關於視窗用新語言重新做一份。工作列選單每次打開都會重畫。
    public void ApplyLanguage(string code)
    {
        Settings.Update(s => s.Language = code);
        Lang.Reload();
        bool settingsOpen = settings != null && !settings.IsDisposed && settings.Visible;
        if (settings != null && !settings.IsDisposed) settings.Close();
        settings = null;
        if (about != null && !about.IsDisposed) about.Close();
        about = null;
        ReplacePanel();
        trayMenu.Font = Theme.UiFont(9.75f);
        UpdateIcon();
        if (settingsOpen) ShowSettings();
    }

    void ReplacePanel()
    {
        var old = Panel;
        old.HidePanel();
        old.Picked -= Paste;
        Watcher.Changed -= old.RefreshIfVisible;
        Watcher.ShowRequested -= old.ShowPanel;
        hotKey.Pressed -= old.TogglePanel;
        Panel = new HistoryForm(Store);
        Panel.Picked += Paste;
        Watcher.Changed += Panel.RefreshIfVisible;
        Watcher.ShowRequested += Panel.ShowPanel;
        hotKey.Pressed += Panel.TogglePanel;
        old.Dispose();
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

    /// 測試用：在指定位置打開工作列圖示的選單。
    public void ShowTrayMenu(Point at) => trayMenu.Show(at);

    /// 測試用：打開選單裡的「語言」子選單。
    public void OpenLanguageSubmenu()
    {
        foreach (ToolStripItem item in trayMenu.Items)
            if (item is ToolStripMenuItem m && m.HasDropDownItems) { m.ShowDropDown(); return; }
    }

    /// 測試用：選單目前每一項的文字（子選單用 > 接在後面，打勾的前面加 ✓）。
    public List<string> MenuTexts()
    {
        BuildMenu();
        var list = new List<string>();
        foreach (ToolStripItem item in trayMenu.Items)
        {
            if (item is ToolStripSeparator) continue;
            list.Add(item.Text ?? "");
            if (item is ToolStripMenuItem m)
                foreach (ToolStripItem sub in m.DropDownItems)
                    if (sub is ToolStripMenuItem sm) list.Add($"{item.Text} > {(sm.Checked ? "✓ " : "")}{sm.Text}");
        }
        return list;
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
