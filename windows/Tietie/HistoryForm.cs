using System.Drawing.Drawing2D;

namespace Tietie;

/// 按快捷鍵跳出來的歷史清單。
sealed class HistoryForm : Form
{
    readonly Store store;
    readonly SearchBox search = new();
    readonly ListBox list = new();
    readonly Label empty = new();
    readonly Label hint = new();
    readonly PreviewForm preview = new();
    readonly ContextMenuStrip menu = new();
    readonly Dictionary<string, Image> thumbs = new();
    List<ClipItem> items = new();
    bool menuOpen;

    /// 打開清單之前在用的視窗，貼上時要回到這裡。
    public IntPtr PreviousWindow { get; private set; }

    /// 選了一筆。第二個參數是 true 時，圖片改貼圖片裡的文字。
    public event Action<ClipItem, bool>? Picked;

    public IReadOnlyList<ClipItem> Items => items;
    public string SearchText => search.Text;
    public int SelectedIndex => list.SelectedIndex;
    public bool PreviewVisible => preview.Visible;

    float Dpi => DeviceDpi / 96f;

    public HistoryForm(Store store)
    {
        this.store = store;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(480, 560);
        BackColor = Theme.Back;
        Font = Theme.UiFont(10f);
        Padding = new Padding(1);
        Text = "貼貼";

        var top = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(14, 12, 14, 8), BackColor = Theme.Back };
        var field = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Field, Padding = new Padding(10, 7, 10, 4) };
        field.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Border);
            using var path = Icons.Rounded(new RectangleF(0, 0, field.Width - 1, field.Height - 1), 6 * Dpi);
            e.Graphics.DrawPath(pen, path);
        };
        search.BorderStyle = BorderStyle.None;
        search.Dock = DockStyle.Fill;
        search.Font = Theme.UiFont(12f);
        search.BackColor = Theme.Field;
        search.ForeColor = Theme.Fore;
        search.Hint = "打字搜尋，圖片裡的字也找得到";
        search.TextChanged += (_, _) => { search.Invalidate(); Reload(true); };
        field.Controls.Add(search);
        top.Controls.Add(field);

        hint.Dock = DockStyle.Bottom;
        hint.Height = 30;
        hint.TextAlign = ContentAlignment.MiddleCenter;
        hint.ForeColor = Theme.Sub;
        hint.Font = Theme.UiFont(8.25f);
        hint.Text = "Enter 貼上　Alt+Enter 貼圖片裡的字　Ctrl+1–9 快速貼　空白鍵預覽　Alt+P 釘選　右鍵看更多";

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, Padding = new Padding(6, 0, 6, 0) };
        list.Dock = DockStyle.Fill;
        list.BorderStyle = BorderStyle.None;
        list.BackColor = Theme.Back;
        list.ForeColor = Theme.Fore;
        list.DrawMode = DrawMode.OwnerDrawVariable;
        list.IntegralHeight = false;
        list.TabStop = false;
        list.MeasureItem += MeasureItem;
        list.DrawItem += DrawItem;
        list.MouseClick += ListClicked;
        list.MouseUp += ListMouseUp;
        list.SelectedIndexChanged += (_, _) => UpdatePreview();
        empty.Dock = DockStyle.Fill;
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.ForeColor = Theme.Sub;
        empty.Font = Theme.UiFont(10f);
        empty.Visible = false;
        host.Controls.Add(list);
        host.Controls.Add(empty);

        Controls.Add(host);
        Controls.Add(hint);
        Controls.Add(top);

        menu.Font = Theme.UiFont(9.75f);
        menu.Closed += (_, _) => menuOpen = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;         // 不出現在 Alt+Tab
            cp.ClassStyle |= 0x20000;   // 陰影
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RoundCorners(Handle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    // MARK: 顯示與隱藏

    public void TogglePanel()
    {
        if (Visible) HidePanel(); else ShowPanel();
    }

    public void ShowPanel()
    {
        var fg = Native.GetForegroundWindow();
        if (fg != Handle && fg != preview.Handle)
        {
            Native.GetWindowThreadProcessId(fg, out uint pid);
            if (pid != Environment.ProcessId) PreviousWindow = fg;
        }
        search.Text = "";
        Reload(true);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (int)((area.Height - Height) * 0.38));
        Show();
        Activate();
        Native.Focus(Handle);
        search.Focus();
    }

    public void HidePanel()
    {
        preview.Hide();
        Hide();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!menuOpen) BeginInvoke(() => { if (!menuOpen && !ContainsFocus) HidePanel(); });
    }

    public void RefreshIfVisible()
    {
        if (!Visible) return;
        var selected = Selected?.Id;
        Reload(false);
        var row = items.FindIndex(i => i.Id == selected);
        Select(row >= 0 ? row : 0);
    }

    void Reload(bool selectFirst)
    {
        items = store.List(search.Text);
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var item in items) list.Items.Add(item);
        list.EndUpdate();
        bool none = items.Count == 0;
        empty.Text = search.Text.Length == 0 ? "還沒有記錄。\n複製任何文字或圖片，就會出現在這裡。" : $"找不到「{search.Text}」";
        empty.Visible = none;
        list.Visible = !none;
        if (selectFirst && !none) Select(0);
        UpdatePreview();
    }

    void Select(int row)
    {
        if (row < 0 || row >= items.Count) return;
        list.SelectedIndex = row;
    }

    ClipItem? Selected => list.SelectedIndex >= 0 && list.SelectedIndex < items.Count ? items[list.SelectedIndex] : null;

    // MARK: 鍵盤

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Down: Select(Math.Min(list.SelectedIndex + 1, items.Count - 1)); return true;
            case Keys.Up: Select(Math.Max(list.SelectedIndex - 1, 0)); return true;
            case Keys.PageDown: Select(Math.Min(list.SelectedIndex + 8, items.Count - 1)); return true;
            case Keys.PageUp: Select(Math.Max(list.SelectedIndex - 8, 0)); return true;
            case Keys.Enter: Pick(Selected, false); return true;
            case Keys.Alt | Keys.Enter: Pick(Selected, true); return true;
            case Keys.Escape:
                if (preview.Visible) preview.Hide(); else HidePanel();
                return true;
            case Keys.Alt | Keys.P: TogglePin(); return true;
            case Keys.Control | Keys.Delete: DeleteSelected(); return true;
            case Keys.Control | Keys.Y: TogglePreview(); return true;
            case Keys.Space:
                if (search.Text.Length == 0) { TogglePreview(); return true; }
                break;
        }
        var key = keyData & Keys.KeyCode;
        if ((keyData & Keys.Modifiers) == Keys.Control)
        {
            int index = key >= Keys.D1 && key <= Keys.D9 ? key - Keys.D1 : key >= Keys.NumPad1 && key <= Keys.NumPad9 ? key - Keys.NumPad1 : -1;
            if (index >= 0)
            {
                if (index < items.Count) Pick(items[index], false);
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void Pick(ClipItem? item, bool asText)
    {
        if (item == null) return;
        Picked?.Invoke(item, asText && item.Kind == "image");
    }

    void TogglePin()
    {
        if (Selected is not ClipItem item) return;
        store.SetPinned(item.Id, !item.Pinned);
        Reload(false);
        Select(items.FindIndex(i => i.Id == item.Id));
    }

    void DeleteSelected()
    {
        if (Selected is not ClipItem item) return;
        int row = list.SelectedIndex;
        store.Delete(item.Id);
        Reload(false);
        Select(Math.Min(row, items.Count - 1));
    }

    void TogglePreview()
    {
        if (preview.Visible) preview.Hide();
        else if (Selected is ClipItem item) preview.ShowItem(item, this);
    }

    void UpdatePreview()
    {
        if (!preview.Visible) return;
        if (Selected is ClipItem item) preview.ShowItem(item, this); else preview.Hide();
    }

    // MARK: 滑鼠

    void ListClicked(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int row = list.IndexFromPoint(e.Location);
        if (row < 0 || row >= items.Count) return;
        Pick(items[row], (ModifierKeys & Keys.Alt) == Keys.Alt);
    }

    void ListMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        int row = list.IndexFromPoint(e.Location);
        if (row < 0 || row >= items.Count) return;
        Select(row);
        ShowMenu(row, e.Location);
    }

    public void ShowMenu(int row, Point at)
    {
        var item = items[row];
        menu.Items.Clear();
        if (item.Kind == "image")
        {
            menu.Items.Add("貼上圖片", null, (_, _) => Pick(item, false));
            bool hasText = item.Ocr.Trim().Length > 0;
            var t = menu.Items.Add(hasText ? "貼上圖片裡的文字" : "貼上圖片裡的文字（這張沒有讀到字）", null, (_, _) => Pick(item, true));
            t.Enabled = hasText;
        }
        else
        {
            menu.Items.Add("貼上", null, (_, _) => Pick(item, false));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("放大預覽", null, (_, _) => preview.ShowItem(item, this));
        menu.Items.Add(item.Pinned ? "取消釘選" : "釘選", null, (_, _) => TogglePin());
        menu.Items.Add("刪除這一筆", null, (_, _) => DeleteSelected());
        menuOpen = true;
        menu.Show(list, at);
    }

    // MARK: 畫每一列

    void MeasureItem(object? sender, MeasureItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= items.Count) return;
        e.ItemHeight = (int)((items[e.Index].Kind == "image" ? 66 : 46) * Dpi);
    }

    static string OneLine(string text)
    {
        var head = text.Length > 300 ? text[..300] : text;
        return string.Join(" ", head.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    string Age(double lastUsed)
    {
        var seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastUsed;
        if (seconds < 60) return "剛剛";
        if (seconds < 3600) return $"{(int)(seconds / 60)} 分鐘前";
        if (seconds < 86400) return $"{(int)(seconds / 3600)} 小時前";
        return $"{(int)(seconds / 86400)} 天前";
    }

    static string SizeText(long bytes) => bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";

    void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= items.Count) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var item = items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        float s = Dpi;
        var r = e.Bounds;

        using (var back = new SolidBrush(Theme.Back)) g.FillRectangle(back, r);
        if (selected)
        {
            using var path = Icons.Rounded(new RectangleF(r.X + 4 * s, r.Y + 2 * s, r.Width - 8 * s, r.Height - 4 * s), 7 * s);
            using var brush = new SolidBrush(Theme.Accent);
            g.FillPath(brush, path);
        }
        var fore = selected ? Color.White : Theme.Fore;
        var sub = selected ? Color.FromArgb(225, 235, 255) : Theme.Sub;

        int x = r.X + (int)(14 * s);
        var meta = new List<string>();
        string title;
        int iconW;
        switch (item.Kind)
        {
            case "image":
                iconW = (int)(76 * s);
                var ih = (int)(50 * s);
                var iconRect = new Rectangle(x, r.Y + (r.Height - ih) / 2, iconW, ih);
                if (Thumb(item) is Image thumb)
                {
                    using var clip = Icons.Rounded(iconRect, 5 * s);
                    var st = g.Save();
                    g.SetClip(clip);
                    using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, iconRect);
                    float k = Math.Min((float)iconRect.Width / thumb.Width, (float)iconRect.Height / thumb.Height);
                    var size = new SizeF(thumb.Width * k, thumb.Height * k);
                    g.DrawImage(thumb, iconRect.X + (iconRect.Width - size.Width) / 2, iconRect.Y + (iconRect.Height - size.Height) / 2, size.Width, size.Height);
                    g.Restore(st);
                }
                var firstLine = item.Ocr.Split('\n').FirstOrDefault() ?? "";
                title = firstLine.Trim().Length == 0 ? "圖片" : "圖片：" + OneLine(item.Ocr);
                meta.Add($"{item.Width}×{item.Height}");
                break;
            case "file":
                iconW = (int)(24 * s);
                var paths = item.Text.Split('\n');
                try
                {
                    if (File.Exists(paths[0]) && Icon.ExtractAssociatedIcon(paths[0]) is Icon fileIcon)
                        using (fileIcon) g.DrawIcon(fileIcon, new Rectangle(x, r.Y + (r.Height - iconW) / 2, iconW, iconW));
                    else DrawTextGlyph(g, new Rectangle(x, r.Y + (r.Height - iconW) / 2, iconW, iconW), sub, true);
                }
                catch { }
                var names = paths.Select(Path.GetFileName).ToArray();
                title = names.Length > 1 ? $"{names[0]} 等 {names.Length} 個檔案" : names.FirstOrDefault() ?? "檔案";
                meta.Add("檔案");
                break;
            default:
                iconW = (int)(20 * s);
                DrawTextGlyph(g, new Rectangle(x + 2, r.Y + (r.Height - iconW) / 2, iconW, iconW), sub, false);
                title = OneLine(item.Text);
                break;
        }
        if (item.SourceApp.Length > 0) meta.Add(item.SourceApp);
        meta.Add(Age(item.LastUsed));
        if (item.Kind == "image") meta.Add(SizeText(item.Bytes));
        if (item.Kind == "text" && item.Text.Length > 80) meta.Add($"{item.Text.Length} 字");

        int right = r.Right - (int)(12 * s);
        if (e.Index < 9)
        {
            var badge = $"Ctrl+{e.Index + 1}";
            using var badgeFont = Theme.UiFont(8.25f);
            var bs = TextRenderer.MeasureText(badge, badgeFont);
            TextRenderer.DrawText(g, badge, badgeFont, new Rectangle(right - bs.Width, r.Y, bs.Width, r.Height), sub,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            right -= bs.Width + (int)(6 * s);
        }
        if (item.Pinned)
        {
            var pinColor = selected ? Color.White : Theme.Pin;
            using var glyph = new Font("Segoe MDL2 Assets", 10f);
            if (glyph.Name == "Segoe MDL2 Assets")
            {
                var gs = TextRenderer.MeasureText("\uE718", glyph);
                TextRenderer.DrawText(g, "\uE718", glyph, new Rectangle(right - gs.Width, r.Y, gs.Width, r.Height), pinColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                right -= gs.Width + (int)(4 * s);
            }
            else
            {
                using var pinBrush = new SolidBrush(pinColor);
                g.FillEllipse(pinBrush, new RectangleF(right - 10 * s, r.Y + r.Height / 2f - 5 * s, 10 * s, 10 * s));
                right -= (int)(18 * s);
            }
        }

        int textX = x + iconW + (int)(10 * s);
        int textW = Math.Max(10, right - textX);
        using var titleFont = Theme.UiFont(10f);
        using var subFont = Theme.UiFont(8.25f);
        int mid = r.Y + r.Height / 2;
        TextRenderer.DrawText(g, title, titleFont, new Rectangle(textX, mid - (int)(21 * s), textW, (int)(21 * s)), fore,
            TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, string.Join(" · ", meta), subFont, new Rectangle(textX, mid + (int)(1 * s), textW, (int)(18 * s)), sub,
            TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    static void DrawTextGlyph(Graphics g, Rectangle r, Color color, bool page)
    {
        using var pen = new Pen(color, Math.Max(1.4f, r.Width / 12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (page)
        {
            using var path = Icons.Rounded(new RectangleF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.08f, r.Width * 0.64f, r.Height * 0.84f), r.Width * 0.1f);
            g.DrawPath(pen, path);
            return;
        }
        float[] widths = { 0.9f, 0.6f, 0.8f, 0.5f };
        for (int i = 0; i < widths.Length; i++)
        {
            float y = r.Y + r.Height * (0.2f + i * 0.2f);
            g.DrawLine(pen, r.X, y, r.X + r.Width * widths[i], y);
        }
    }

    Image? Thumb(ClipItem item)
    {
        var path = item.ImagePath;
        if (path == null) return null;
        if (thumbs.TryGetValue(item.ImageFile, out var cached)) return cached;
        try
        {
            using var full = Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
            float k = Math.Min(1f, 320f / Math.Max(full.Width, full.Height));
            var thumb = new Bitmap(full, Math.Max(1, (int)(full.Width * k)), Math.Max(1, (int)(full.Height * k)));
            thumbs[item.ImageFile] = thumb;
            return thumb;
        }
        catch { return null; }
    }
}

/// 搜尋框：游標在裡面時也看得到灰色提示字。
sealed class SearchBox : TextBox
{
    public string Hint = "";

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x000F && Text.Length == 0 && Hint.Length > 0)
        {
            using var g = Graphics.FromHwnd(Handle);
            TextRenderer.DrawText(g, Hint, Font, new Rectangle(1, 0, ClientSize.Width, ClientSize.Height), Theme.Sub,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }
}

/// 空白鍵叫出來的大預覽。
sealed class PreviewForm : Form
{
    readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    readonly TextBox text = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };

    public PreviewForm()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Field;
        Padding = new Padding(12);
        text.BackColor = Theme.Field;
        text.ForeColor = Theme.Fore;
        text.Font = Theme.UiFont(10f);
        Controls.Add(picture);
        Controls.Add(text);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x08000000;
            cp.ClassStyle |= 0x20000;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RoundCorners(Handle);
    }

    public void ShowItem(ClipItem item, Form anchor)
    {
        float s = DeviceDpi / 96f;
        var size = new Size((int)(520 * s), anchor.Height);
        if (item.Kind == "image" && item.ImagePath is string path && File.Exists(path))
        {
            picture.Image?.Dispose();
            using (var img = Image.FromStream(new MemoryStream(File.ReadAllBytes(path)))) picture.Image = new Bitmap(img);
            picture.Visible = true;
            text.Visible = false;
            float max = 640 * s;
            float k = Math.Min(1f, Math.Min(max / Math.Max(1, item.Width), max / Math.Max(1, item.Height)));
            size = new Size((int)Math.Max(item.Width * k + 24 * s, 200 * s), (int)Math.Max(item.Height * k + 24 * s, 160 * s));
        }
        else
        {
            picture.Visible = false;
            text.Visible = true;
            text.Text = (item.Text.Length > 20000 ? item.Text[..20000] : item.Text).Replace("\r\n", "\n").Replace("\n", "\r\n");
            text.SelectionLength = 0;
        }
        var area = Screen.FromControl(anchor).WorkingArea;
        int x = anchor.Right + (int)(10 * s);
        if (x + size.Width > area.Right) x = anchor.Left - size.Width - (int)(10 * s);
        if (x < area.Left) x = area.Left + 10;
        int y = Math.Max(area.Top + 10, Math.Min(anchor.Top, area.Bottom - size.Height - 10));
        Bounds = new Rectangle(x, y, size.Width, size.Height);
        if (!Visible) Show(anchor);
        Invalidate(true);
    }
}
