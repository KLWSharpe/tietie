using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Tietie;

/// 顏色、字型與圖示。
static class Theme
{
    static int ReadDword(string name, int fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue(name) is int v ? v : fallback;
        }
        catch { return fallback; }
    }

    public static bool Dark => ReadDword("AppsUseLightTheme", 1) == 0;
    public static bool TaskbarLight => ReadDword("SystemUsesLightTheme", 0) == 1;

    public static Color Back => Dark ? Color.FromArgb(36, 36, 36) : Color.FromArgb(250, 250, 249);
    public static Color Field => Dark ? Color.FromArgb(52, 52, 52) : Color.White;
    public static Color Fore => Dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(30, 32, 34);
    public static Color Sub => Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(112, 112, 112);
    public static Color Border => Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(222, 222, 220);
    public static readonly Color Accent = Color.FromArgb(64, 120, 230);
    public static readonly Color Pin = Color.FromArgb(232, 128, 30);

    public const string FontName = "Microsoft JhengHei UI";
    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) => new(FontName, size, style);
}

static class Icons
{
    static Image? appImage;

    /// 貼貼的圖示（素色線條，米白底）。
    public static Image AppImage
    {
        get
        {
            if (appImage != null) return appImage;
            try
            {
                using var s = typeof(Icons).Assembly.GetManifestResourceStream("Tietie.icon.png");
                if (s != null) return appImage = Image.FromStream(s);
            }
            catch { }
            var bmp = new Bitmap(256, 256);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawPapers(g, new RectangleF(28, 28, 200, 200), Color.FromArgb(33, 36, 38), 10f, Color.FromArgb(250, 249, 245));
            }
            return appImage = bmp;
        }
    }

    public static Icon AppIcon
    {
        get
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
            catch { return SystemIcons.Application; }
        }
    }

    /// 工作列右下角的小圖示：素色線條，跟著工作列深淺色變黑或白。
    public static Icon Tray(bool paused)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var color = Theme.TaskbarLight ? Color.FromArgb(30, 30, 30) : Color.White;
            if (paused)
            {
                using var pen = new Pen(color, 2.6f);
                g.DrawEllipse(pen, 4, 4, 24, 24);
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, 12, 10, 3, 12);
                g.FillRectangle(brush, 17, 10, 3, 12);
            }
            else
            {
                DrawPapers(g, new RectangleF(1, 1, 30, 30), color, 2.6f, null);
            }
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        Native.DestroyIcon(handle);
        return icon;
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// 畫兩張疊在一起的紙（跟 Mac 版的圖示同一個設計）。
    public static void DrawPapers(Graphics g, RectangleF box, Color ink, float stroke, Color? paper)
    {
        // 設計稿的範圍（1024 格）
        const float dx = 230, dy = 190, dw = 560, dh = 640;
        float scale = Math.Min(box.Width / dw, box.Height / dh);
        float ox = box.X + (box.Width - dw * scale) / 2, oy = box.Y + (box.Height - dh * scale) / 2;
        RectangleF Map(float x, float y, float w, float h) => new(ox + (x - dx) * scale, oy + (y - dy) * scale, w * scale, h * scale);

        var state = g.Save();
        using var pen = new Pen(ink, stroke) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };

        void Sheet(RectangleF r, float angle, bool lines)
        {
            var c = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            using var m = new Matrix();
            m.RotateAt(angle, c);
            using var path = Rounded(r, 54 * scale);
            path.Transform(m);
            if (paper is Color fill)
            {
                using var b = new SolidBrush(fill);
                g.FillPath(b, path);
            }
            else
            {
                // 透明底：把後面那張紙被擋住的線清掉
                var mode = g.CompositingMode;
                g.CompositingMode = CompositingMode.SourceCopy;
                using var clear = new SolidBrush(Color.Transparent);
                g.FillPath(clear, path);
                g.CompositingMode = mode;
            }
            g.DrawPath(pen, path);
            if (!lines) return;
            float[] widths = { 0.74f, 0.52f, 0.66f };
            for (int i = 0; i < widths.Length; i++)
            {
                float y = r.Y + (118 + i * 104) * scale;
                var pts = new[] { new PointF(r.X + 78 * scale, y), new PointF(r.X + (78 + (380 - 156) * widths[i]) * scale, y) };
                m.TransformPoints(pts);
                g.DrawLine(pen, pts[0], pts[1]);
            }
        }

        Sheet(Map(290, 234, 380, 450), -9, false);
        Sheet(Map(350, 334, 380, 450), 6, true);
        g.Restore(state);
    }
}

/// 畫面角落跳出來、幾秒後自己消失的提示。
sealed class Toast : Form
{
    static Toast? current;
    readonly System.Windows.Forms.Timer timer = new();

    Toast(string message, double seconds)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(40, 40, 40);
        Padding = new Padding(18, 12, 18, 12);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var label = new Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = Color.White,
            Font = Theme.UiFont(10f),
        };
        Controls.Add(label);
        timer.Interval = (int)(seconds * 1000);
        timer.Tick += (_, _) => Close();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x08000000; // 工具視窗、不搶焦點
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RoundCorners(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 60);
        timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Dispose();
        if (current == this) current = null;
        base.OnFormClosed(e);
    }

    public static void Show(string message, double seconds = 2.2)
    {
        current?.Close();
        current = new Toast(message, seconds);
        current.Show();
    }
}

static class Ui
{
    public static Label Text(string text, float size = 9.75f, FontStyle style = FontStyle.Regular, Color? color = null, int maxWidth = 0)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = Theme.UiFont(size, style),
            ForeColor = color ?? SystemColors.ControlText,
            Margin = new Padding(0, 0, 0, 6),
        };
        if (maxWidth > 0) label.MaximumSize = new Size(maxWidth, 0);
        return label;
    }

    public static FlowLayoutPanel Column(Padding padding)
    {
        return new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = padding,
            Dock = DockStyle.Fill,
        };
    }

    public static void PrepareDialog(Form form, string title)
    {
        form.Text = title;
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.AutoSize = true;
        form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        form.Font = Theme.UiFont(9.75f);
        form.BackColor = Color.White;
        form.Icon = Icons.AppIcon;
        form.ShowInTaskbar = true;
    }
}
