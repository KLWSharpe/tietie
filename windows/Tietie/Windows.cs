using System.Diagnostics;

namespace Tietie;

/// 第一次打開時的設定。
sealed class OnboardingForm : Form
{
    readonly RadioButton recordAll = new() { Text = L.T("onb.recordAll"), AutoSize = true };
    readonly RadioButton skipSecret = new() { Text = L.T("onb.skipSecret"), AutoSize = true };
    readonly CheckBox login = new() { Text = L.T("launchAtLogin"), AutoSize = true };

    public OnboardingForm()
    {
        Ui.PrepareDialog(this, L.T("app.fullName"));
        var col = Ui.Column(new Padding(28, 24, 28, 24));
        int w = 460;
        var hotKey = HotKeyChoice.Find(Settings.Data.HotKey).Label;
        col.Controls.Add(Ui.Text(L.T("onb.title"), 15f, FontStyle.Bold));
        col.Controls.Add(Ui.Text(L.T("onb.intro", hotKey),
            9.75f, FontStyle.Regular, Color.DimGray, w));

        var s1 = Ui.Text(L.T("onb.passwordQuestion"), 10.5f, FontStyle.Bold);
        s1.Margin = new Padding(0, 16, 0, 6);
        col.Controls.Add(s1);
        recordAll.Checked = Settings.Data.RecordConcealed;
        skipSecret.Checked = !Settings.Data.RecordConcealed;
        col.Controls.Add(recordAll);
        col.Controls.Add(skipSecret);
        col.Controls.Add(Ui.Text(L.T("win.onb.passwordNote"),
            9f, FontStyle.Regular, Color.DimGray, w));

        var s2 = Ui.Text(L.T("win.onb.startupTitle"), 10.5f, FontStyle.Bold);
        s2.Margin = new Padding(0, 16, 0, 6);
        col.Controls.Add(s2);
        login.Checked = Settings.Data.LaunchAtLogin;
        col.Controls.Add(login);
        col.Controls.Add(Ui.Text(L.T("win.onb.trayNote"), 9f, FontStyle.Regular, Color.DimGray, w));

        var done = new Button { Text = L.T("onb.start"), AutoSize = true, Padding = new Padding(14, 4, 14, 4), Margin = new Padding(0, 18, 0, 0) };
        done.Click += (_, _) => Close();
        AcceptButton = done;
        col.Controls.Add(done);
        Controls.Add(col);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Settings.Update(s =>
        {
            s.Onboarded = true;
            s.RecordConcealed = recordAll.Checked;
            s.LaunchAtLogin = login.Checked;
        });
        AutoStart.Apply(login.Checked);
        base.OnFormClosed(e);
    }
}

/// 設定。
sealed class SettingsForm : Form
{
    readonly Store store;
    readonly ComboBox hotKey = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    readonly ComboBox max = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    readonly ComboBox cap = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    readonly CheckBox concealed = new() { Text = L.T("settings.recordConcealed"), AutoSize = true };
    readonly CheckBox login = new() { Text = L.T("launchAtLogin"), AutoSize = true };
    readonly Label usage = new() { AutoSize = true, ForeColor = Color.DimGray };
    readonly int[] maxChoices = { 200, 500, 1000, 3000, 10000 };
    readonly (int mb, string label)[] capChoices = { (500, "500 MB"), (1024, "1 GB"), (2048, "2 GB"), (5120, "5 GB"), (10240, "10 GB") };
    bool syncing;
    public event Action? HotKeyChanged;

    public SettingsForm(Store store)
    {
        this.store = store;
        Ui.PrepareDialog(this, L.T("settings.title"));
        foreach (var c in HotKeyChoice.All) hotKey.Items.Add(c.Label);
        foreach (var n in maxChoices) max.Items.Add(L.T("settings.items", n));
        foreach (var c in capChoices) cap.Items.Add(c.label);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(24, 22, 24, 8), Dock = DockStyle.Top };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Row(string label, Control control)
        {
            var l = Ui.Text(label);
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 6, 16, 6);
            control.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(l);
            grid.Controls.Add(control);
        }
        Row(L.T("settings.hotkey"), hotKey);
        Row(L.T("settings.keep"), max);
        Row(L.T("settings.imageCap"), cap);
        Row(L.T("win.settings.passwords"), concealed);
        Row(L.T("win.settings.startup"), login);
        Row(L.T("win.settings.usageLabel"), usage);

        var col = Ui.Column(new Padding(24, 0, 24, 22));
        col.Controls.Add(Ui.Text(L.T("settings.limitNote"), 9f, FontStyle.Regular, Color.DimGray, 420));

        var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
        outer.Controls.Add(grid);
        outer.Controls.Add(col);
        Controls.Add(outer);

        Sync();
        foreach (var c in new Control[] { hotKey, max, cap }) ((ComboBox)c).SelectedIndexChanged += (_, _) => Changed(c);
        concealed.CheckedChanged += (_, _) => Changed(concealed);
        login.CheckedChanged += (_, _) => Changed(login);
    }

    void Sync()
    {
        syncing = true;
        var d = Settings.Data;
        hotKey.SelectedIndex = Math.Max(0, Array.FindIndex(HotKeyChoice.All, c => c.Id == d.HotKey));
        max.SelectedIndex = Math.Max(0, Array.IndexOf(maxChoices, d.MaxItems));
        cap.SelectedIndex = Math.Max(0, Array.FindIndex(capChoices, c => c.mb == d.ImageCapMB));
        concealed.Checked = d.RecordConcealed;
        login.Checked = d.LaunchAtLogin;
        RefreshUsage();
        syncing = false;
    }

    public void RefreshUsage()
    {
        long bytes = store.ImageBytes();
        string size = bytes < 1024 * 1024 ? $"{bytes / 1024} KB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";
        usage.Text = L.T("win.settings.usage", store.Count(), size);
    }

    void Changed(Control sender)
    {
        if (syncing) return;
        var d = Settings.Data;
        if (sender == hotKey)
        {
            Settings.Update(s => s.HotKey = HotKeyChoice.All[hotKey.SelectedIndex].Id);
            HotKeyChanged?.Invoke();
        }
        else if (sender == max || sender == cap)
        {
            int newMax = maxChoices[max.SelectedIndex];
            int newCap = capChoices[cap.SelectedIndex].mb;
            bool lower = newMax < d.MaxItems || newCap < d.ImageCapMB;
            if (lower)
            {
                var answer = MessageBox.Show(this, L.T("win.settings.lowerConfirm"), L.T("app.fullName"),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (answer != DialogResult.OK) { Sync(); return; }
            }
            Settings.Update(s => { s.MaxItems = newMax; s.ImageCapMB = newCap; });
        }
        else if (sender == concealed)
        {
            Settings.Update(s => s.RecordConcealed = concealed.Checked);
        }
        else if (sender == login)
        {
            Settings.Update(s => s.LaunchAtLogin = login.Checked);
            AutoStart.Apply(login.Checked);
        }
    }
}

/// 關於貼貼。
sealed class AboutForm : Form
{
    public AboutForm()
    {
        Ui.PrepareDialog(this, L.T("menu.about"));
        var col = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(32, 26, 32, 26), Dock = DockStyle.Fill };
        col.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

        Control Center(Control c, int top = 0, int bottom = 4)
        {
            c.Anchor = AnchorStyles.None;
            c.Margin = new Padding(0, top, 0, bottom);
            if (c is Label l) { l.TextAlign = ContentAlignment.MiddleCenter; }
            col.Controls.Add(c);
            return c;
        }

        Center(new PictureBox { Image = Icons.AppImage, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(96, 96) }, 0, 12);
        Center(Ui.Text(L.T("app.fullName"), 17f, FontStyle.Bold), 0, 0);
        Center(Ui.Text(L.T("about.version", Program.Version), 8.25f, FontStyle.Regular, Color.Gray), 0, 14);
        Center(Ui.Text(L.T("win.about.tagline"), 10f, FontStyle.Regular, null, 320), 0, 4);
        Center(Ui.Text(L.T("about.detail"), 9f, FontStyle.Regular, Color.DimGray, 320), 0, 14);
        Center(new Label { BorderStyle = BorderStyle.Fixed3D, Height = 2, Width = 240, AutoSize = false }, 0, 14);
        Center(Ui.Text(L.T("about.author"), 10f, FontStyle.Bold), 0, 10);

        var site = new Button { Text = L.T("about.site"), AutoSize = true, Padding = new Padding(14, 5, 14, 5) };
        site.Click += (_, _) => Open("https://kueilan.com");
        Center(site, 0, 8);
        AcceptButton = site;

        var repo = new LinkLabel { Text = L.T("about.repo"), AutoSize = true, Font = Theme.UiFont(9.5f), LinkColor = Theme.Accent };
        repo.LinkClicked += (_, _) => Open("https://github.com/KLWSharpe/tietie");
        Center(repo, 0, 12);
        Center(Ui.Text(L.T("about.feedback"), 8.25f, FontStyle.Regular, Color.Gray, 320), 0, 0);
        Controls.Add(col);
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
