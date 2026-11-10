using System.Diagnostics;

namespace Tietie;

/// 第一次打開時的設定。
sealed class OnboardingForm : Form
{
    readonly RadioButton recordAll = new() { Text = "全部都記，包含從密碼管理器複製的帳號密碼", AutoSize = true };
    readonly RadioButton skipSecret = new() { Text = "密碼管理器複製的內容不要記", AutoSize = true };
    readonly CheckBox login = new() { Text = "開機後自動啟動貼貼", AutoSize = true };

    public OnboardingForm()
    {
        Ui.PrepareDialog(this, "貼貼 Tietie");
        var col = Ui.Column(new Padding(28, 24, 28, 24));
        int w = 460;
        var hotKey = HotKeyChoice.Find(Settings.Data.HotKey).Label;
        col.Controls.Add(Ui.Text("歡迎使用貼貼 Tietie", 15f, FontStyle.Bold));
        col.Controls.Add(Ui.Text($"貼貼會記住你複製過的文字、圖片和檔案。按 {hotKey} 叫出清單，選一筆就貼上，貼出來的文字不帶格式。\n所有記錄只存在這台電腦，不會上傳到任何地方。",
            9.75f, FontStyle.Regular, Color.DimGray, w));

        var s1 = Ui.Text("1. 要不要記錄密碼？", 10.5f, FontStyle.Bold);
        s1.Margin = new Padding(0, 16, 0, 6);
        col.Controls.Add(s1);
        recordAll.Checked = Settings.Data.RecordConcealed;
        skipSecret.Checked = !Settings.Data.RecordConcealed;
        col.Controls.Add(recordAll);
        col.Controls.Add(skipSecret);
        col.Controls.Add(Ui.Text("記錄會留在這台電腦上，能打開這台電腦的人就看得到。如果電腦會借人或多人共用，建議選第二個。貼完敏感內容後，也可以從工作列的貼貼圖示一鍵清除。之後可以在「設定」裡更改。",
            9f, FontStyle.Regular, Color.DimGray, w));

        var s2 = Ui.Text("2. 自動啟動", 10.5f, FontStyle.Bold);
        s2.Margin = new Padding(0, 16, 0, 6);
        col.Controls.Add(s2);
        login.Checked = Settings.Data.LaunchAtLogin;
        col.Controls.Add(login);
        col.Controls.Add(Ui.Text("貼貼會待在螢幕右下角的工作列。如果看不到，點工作列上的「^」箭頭就能找到。", 9f, FontStyle.Regular, Color.DimGray, w));

        var done = new Button { Text = "開始使用", AutoSize = true, Padding = new Padding(14, 4, 14, 4), Margin = new Padding(0, 18, 0, 0) };
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
    readonly CheckBox concealed = new() { Text = "記錄密碼管理器複製的帳號密碼", AutoSize = true };
    readonly CheckBox login = new() { Text = "開機後自動啟動貼貼", AutoSize = true };
    readonly Label usage = new() { AutoSize = true, ForeColor = Color.DimGray };
    readonly int[] maxChoices = { 200, 500, 1000, 3000, 10000 };
    readonly (int mb, string label)[] capChoices = { (500, "500 MB"), (1024, "1 GB"), (2048, "2 GB"), (5120, "5 GB"), (10240, "10 GB") };
    bool syncing;
    public event Action? HotKeyChanged;

    public SettingsForm(Store store)
    {
        this.store = store;
        Ui.PrepareDialog(this, "貼貼設定");
        foreach (var c in HotKeyChoice.All) hotKey.Items.Add(c.Label);
        foreach (var n in maxChoices) max.Items.Add($"{n} 筆");
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
        Row("叫出清單的快捷鍵", hotKey);
        Row("最多保留", max);
        Row("圖片最多佔用", cap);
        Row("密碼", concealed);
        Row("啟動", login);
        Row("目前用量", usage);

        var col = Ui.Column(new Padding(24, 0, 24, 22));
        col.Controls.Add(Ui.Text("超過上限時，會自動刪掉最舊的記錄。釘選的不會被刪。", 9f, FontStyle.Regular, Color.DimGray, 420));

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
        usage.Text = $"{store.Count()} 筆 · 圖片 {size}";
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
                var answer = MessageBox.Show(this, "調低上限後，超過的舊記錄會在下次複製時被刪掉（釘選的不會）。確定要調低嗎？", "貼貼",
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
        Ui.PrepareDialog(this, "關於貼貼");
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
        Center(Ui.Text("貼貼 Tietie", 17f, FontStyle.Bold), 0, 0);
        Center(Ui.Text($"版本 {Program.Version}", 8.25f, FontStyle.Regular, Color.Gray), 0, 14);
        Center(Ui.Text("免費、開源的剪貼簿小工具", 10f), 0, 4);
        Center(Ui.Text("記住你複製過的文字、圖片和檔案。\n所有記錄只存在這台電腦，不會上傳。", 9f, FontStyle.Regular, Color.DimGray, 320), 0, 14);
        Center(new Label { BorderStyle = BorderStyle.Fixed3D, Height = 2, Width = 240, AutoSize = false }, 0, 14);
        Center(Ui.Text("製作：奎嵐 KueiLan", 10f, FontStyle.Bold), 0, 10);

        var site = new Button { Text = "前往奎嵐的網站 kueilan.com", AutoSize = true, Padding = new Padding(14, 5, 14, 5) };
        site.Click += (_, _) => Open("https://kueilan.com");
        Center(site, 0, 8);
        AcceptButton = site;

        var repo = new LinkLabel { Text = "到 GitHub 看原始碼、給回饋", AutoSize = true, Font = Theme.UiFont(9.5f), LinkColor = Theme.Accent };
        repo.LinkClicked += (_, _) => Open("https://github.com/KLWSharpe/tietie");
        Center(repo, 0, 12);
        Center(Ui.Text("歡迎給回饋與建議，之後會幫大家更新。", 8.25f, FontStyle.Regular, Color.Gray), 0, 0);
        Controls.Add(col);
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
