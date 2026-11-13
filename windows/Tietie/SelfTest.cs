using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;

namespace Tietie;

/// 內建檢查：一定要先設定 TIETIE_HOME 到暫存資料夾，避免動到正式記錄。
static class SelfTest
{
    static readonly StringBuilder log = new();
    static int failures;

    static void Check(string name, bool ok, string detail = "")
    {
        var line = (ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? $"  [{detail}]" : "");
        log.AppendLine(line);
        Console.WriteLine(line);
        if (!ok) failures++;
    }

    static void Note(string line)
    {
        log.AppendLine(line);
        Console.WriteLine(line);
    }

    static void Finish(string file)
    {
        Note(failures == 0 ? "\n全部通過" : $"\n有 {failures} 項沒過");
        File.WriteAllText(Path.Combine(Paths.Home, file), log.ToString(), Encoding.UTF8);
    }

    public static byte[] TextImage(string text, int width = 900, int height = 220)
    {
        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font(Theme.FontName, 40f);
            g.DrawString(text, font, Brushes.Black, 40, 70);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    static byte[] NoiseImage(int side)
    {
        using var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, bmp.PixelFormat);
        var bytes = new byte[data.Stride * side];
        Random.Shared.NextBytes(bytes);
        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        bmp.UnlockBits(data);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    static void SetClipboard(DataObject data)
    {
        for (int i = 0; i < 6; i++)
        {
            try { Clipboard.SetDataObject(data, true, 5, 100); return; }
            catch { Thread.Sleep(80); }
        }
    }

    public static int Run()
    {
        if (!Paths.TestMode) { Console.WriteLine("請先設定 TIETIE_HOME 到暫存資料夾"); return 2; }
        Note($"Tietie {Program.Version} 自我檢查，文字辨識語言：{Ocr.Language}");
        Settings.Update(s => { s.MaxItems = 1000; s.ImageCapMB = 2048; s.Paused = false; });
        using var store = new Store();
        store.ClearUnpinned();

        var a = store.AddText("第一段文字 hello", "text", "測試");
        Check("版本比較：新版", Updater.IsNewer("v0.4.0", "0.3.0") && Updater.IsNewer("0.10.0", "0.9.9") && Updater.IsNewer("1.0", "0.9.9"));
        Check("版本比較：同版或舊版不提示", !Updater.IsNewer("v0.3.0", "0.3.0") && !Updater.IsNewer("0.2.9", "0.3.0") && !Updater.IsNewer("0.3", "0.3.0"));
        Check("讀得懂 GitHub 的版本資訊", Updater.ReadTag("{\"tag_name\":\"v0.4.0\",\"name\":\"x\"}") == "v0.4.0");
        Check("新增文字", a != null && store.Count() == 1, $"count={store.Count()}");
        var b = store.AddText("第一段文字 hello", "text", "測試");
        Check("重複內容不會多一筆", a == b && store.Count() == 1, $"count={store.Count()}");
        store.AddText("100%_done", "text", "測試");
        Thread.Sleep(20);
        store.AddText("另一段完全無關的內容", "text", "測試");
        Check("搜尋文字", store.List("hello").Count == 1);
        Check("搜尋特殊符號不會亂抓", store.List("%_").Select(i => i.Text).SequenceEqual(new[] { "100%_done" }),
            string.Join(",", store.List("%_").Select(i => i.Text)));
        Check("最新的排最上面", store.List().FirstOrDefault()?.Text == "另一段完全無關的內容");

        var png = TextImage("Tietie 12345");
        var imageId = store.AddImage(png, 900, 220, "測試", out bool isNew);
        var imageItem = imageId is long iid ? store.Item(iid) : null;
        Check("新增圖片並存成檔案", isNew && imageItem?.ImagePath is string p0 && File.Exists(p0));
        var ocr = Task.Run(() => Ocr.RecognizeAsync(png)).GetAwaiter().GetResult();
        Check("讀出圖片裡的數字", ocr.Contains("12345"), ocr.Replace("\n", " / "));
        if (Ocr.Language.StartsWith("zh"))
        {
            var zh = TextImage("訂單編號 貼貼測試");
            var zhText = Task.Run(() => Ocr.RecognizeAsync(zh)).GetAwaiter().GetResult();
            Check("讀出圖片裡的中文", zhText.Contains("貼貼") || zhText.Contains("訂單"), zhText);
        }
        else Note("SKIP  讀出圖片裡的中文（這台電腦沒有裝中文文字辨識）");
        if (imageId is long id1) store.SetOcr(id1, ocr);
        Check("用圖片裡的字搜得到圖片", store.List("12345").FirstOrDefault()?.Kind == "image");

        if (a is long aid) store.SetPinned(aid, true);
        Check("釘選的排最上面", store.List().FirstOrDefault()?.Id == a);
        store.ClearUnpinned();
        Check("一鍵清除會保留釘選", store.Count() == 1 && store.List().FirstOrDefault()?.Id == a, $"count={store.Count()}");
        Check("清除時圖片檔一起刪掉", imageItem?.ImagePath is string p1 && !File.Exists(p1));

        store.AddText("剛剛複製的敏感內容", "text", "測試");
        store.ClearRecent(600);
        Check("清除最近 10 分鐘", store.List("敏感").Count == 0 && store.Count() == 1);
        if (store.AddText("昨天複製過的密碼", "text", "測試") is long oldId)
        {
            store.Backdate(oldId, 86400);
            store.AddText("昨天複製過的密碼", "text", "測試");
            store.ClearRecent(600);
            Check("昨天複製過、剛剛又複製一次的內容也會被清掉", store.List("昨天複製過").Count == 0);
        }

        Settings.SimulateLoadFailure(true);
        Settings.Update(s => s.MaxItems = 10);
        for (int i = 0; i < 15; i++) store.AddText($"設定壞掉時 {i}", "text", "測試");
        Check("設定檔讀不到時不會自動刪記錄", store.List("設定壞掉時").Count == 15);
        Settings.SimulateLoadFailure(false);
        Settings.Update(s => s.MaxItems = 1000);
        foreach (var item in store.List("設定壞掉時")) store.Delete(item.Id);

        Settings.Update(s => s.MaxItems = 0);
        for (int i = 0; i < 12; i++) store.AddText($"下限 {i}", "text", "測試");
        Check("上限被改成 0 也至少保留 10 筆", store.List("下限").Count == 10, $"{store.List("下限").Count} 筆");
        Settings.Update(s => s.MaxItems = 1000);
        foreach (var item in store.List("下限")) store.Delete(item.Id);

        Settings.Update(s => s.MaxItems = 10);
        for (int i = 0; i < 30; i++) store.AddText($"批次 {i}", "text", "測試");
        var list = store.List();
        Check("超過筆數上限會刪最舊的", store.Count() == 11, $"count={store.Count()}");
        Check("留下的是最新的", list.Any(i => i.Text == "批次 29") && !list.Any(i => i.Text == "批次 0"));

        Settings.Update(s => { s.MaxItems = 1000; s.ImageCapMB = 50; });
        long written = 0;
        for (int i = 0; i < 8; i++)
        {
            var noise = NoiseImage(1700);
            written += noise.Length;
            store.AddImage(noise, 1700, 1700, "測試", out _);
        }
        long total = store.ImageBytes();
        Check("圖片超過容量上限會刪最舊的", written > 50L * 1024 * 1024 && total <= 50L * 1024 * 1024,
            $"寫入 {written / 1024 / 1024} MB，留下 {total / 1024 / 1024} MB");
        var onDisk = Directory.GetFiles(Paths.Images).Length;
        var rows = store.List("", 1000).Count(i => i.Kind == "image");
        Check("資料庫和圖片檔數量一致", onDisk == rows, $"檔案 {onDisk}，記錄 {rows}");

        // 剪貼簿監看與貼上
        foreach (var item in store.List("", 1000).Where(i => i.Pinned)) store.SetPinned(item.Id, false);
        store.ClearUnpinned();
        Settings.Update(s => { s.RecordConcealed = true; s.Onboarded = false; s.Paused = false; s.ImageCapMB = 2048; });
        var watcher = new ClipboardWatcher(store, listen: false);

        var secret = new DataObject();
        secret.SetData(DataFormats.UnicodeText, "before-onboarding-secret");
        secret.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        SetClipboard(secret);
        watcher.Capture();
        Check("還沒做完第一次設定前，不記密碼管理器的內容", store.List("before-onboarding").Count == 0);
        Settings.Update(s => s.Onboarded = true);

        var rich = new DataObject();
        rich.SetData(DataFormats.UnicodeText, "監看測試文字");
        rich.SetData(DataFormats.Html, "Version:0.9\r\nStartHTML:0\r\n<b>監看測試文字</b>");
        rich.SetData(DataFormats.Rtf, @"{\rtf1 x}");
        SetClipboard(rich);
        watcher.Capture();
        var got = store.List("監看測試文字").FirstOrDefault();
        Check("複製帶格式的文字會被記錄", got?.Kind == "text" && got.Text == "監看測試文字");

        if (got != null)
        {
            watcher.IgnoreFor(1200);
            Paster.Write(got);
            var d = Clipboard.GetDataObject();
            var formats = d?.GetFormats() ?? Array.Empty<string>();
            bool hasRich = formats.Any(f => f.Contains("HTML", StringComparison.OrdinalIgnoreCase) || f.Contains("Rich", StringComparison.OrdinalIgnoreCase) || f.Contains("Rtf", StringComparison.OrdinalIgnoreCase));
            Check("貼出來只有純文字，沒有格式", !hasRich && Clipboard.GetText() == "監看測試文字", string.Join(",", formats));
            long before = store.Count();
            watcher.Capture();
            Check("貼貼自己放回剪貼簿的不會重複記", store.Count() == before);
            watcher.IgnoreFor(0);
        }

        var pw = new DataObject();
        pw.SetData(DataFormats.UnicodeText, "hunter2-secret");
        pw.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        SetClipboard(pw);
        watcher.Capture();
        Check("選擇記密碼時，密碼管理器的內容會記", store.List("hunter2").Count == 1);

        Settings.Update(s => s.RecordConcealed = false);
        var pw2 = new DataObject();
        pw2.SetData(DataFormats.UnicodeText, "another-secret-9");
        pw2.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        SetClipboard(pw2);
        watcher.Capture();
        Check("選擇不記密碼時，密碼管理器的內容不會記", store.List("another-secret").Count == 0);
        var history = new DataObject();
        history.SetData(DataFormats.UnicodeText, "no-history-secret");
        history.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
        SetClipboard(history);
        watcher.Capture();
        Check("標記不要進剪貼簿歷程的內容也不記", store.List("no-history-secret").Count == 0);
        Settings.Update(s => s.RecordConcealed = true);

        Settings.Update(s => s.Paused = true);
        SetClipboard(new DataObject(DataFormats.UnicodeText, "暫停期間複製的"));
        watcher.Capture();
        Check("暫停時不記錄", store.List("暫停期間").Count == 0);
        Settings.Update(s => s.Paused = false);

        var picture = new DataObject();
        var pngBytes = TextImage("PNG 9876");
        picture.SetData("PNG", false, new MemoryStream(pngBytes));
        using (var bmp = (Bitmap)Image.FromStream(new MemoryStream(pngBytes))) picture.SetData(DataFormats.Bitmap, true, new Bitmap(bmp));
        SetClipboard(picture);
        watcher.Capture();
        var img = store.List().FirstOrDefault();
        Check("複製圖片會被記錄", img?.Kind == "image" && img.Width == 900, $"{img?.Kind} {img?.Width}");
        if (img != null)
        {
            watcher.IgnoreFor(1200);
            Paster.Write(img);
            var d = Clipboard.GetDataObject();
            Check("圖片貼得回去", d != null && d.GetDataPresent("PNG") && Clipboard.ContainsImage());
            watcher.IgnoreFor(0);
            if (img.ImagePath is string ip)
            {
                File.Delete(ip);
                SetClipboard(new DataObject(DataFormats.UnicodeText, "不能被清掉"));
                watcher.Capture();
                bool result = Paster.Write(img);
                Check("圖片檔不見時不會清空剪貼簿", !result && Clipboard.GetText() == "不能被清掉");
            }
        }

        watcher.IgnoreFor(1200);
        Paster.WriteText("圖片裡的字 9876");
        long beforeText = store.Count();
        watcher.Capture();
        Check("貼出圖片裡的文字是純文字，而且不會重複記", Clipboard.GetText() == "圖片裡的字 9876" && !Clipboard.ContainsImage() && store.Count() == beforeText);
        watcher.IgnoreFor(0);

        SetClipboard(new DataObject(DataFormats.UnicodeText, "   \r\n  "));
        long n = store.Count();
        watcher.Capture();
        Check("只有空白的內容不記", store.Count() == n);

        var tmp = Path.Combine(Paths.Home, "檔案測試.txt");
        File.WriteAllText(tmp, "x");
        var files = new DataObject();
        files.SetFileDropList(new StringCollection { tmp });
        SetClipboard(files);
        watcher.Capture();
        var f = store.List().FirstOrDefault();
        Check("複製檔案會被記錄", f?.Kind == "file" && f.Text == tmp, f?.Text ?? "");
        if (f != null)
        {
            watcher.IgnoreFor(1200);
            Paster.Write(f);
            var back = Clipboard.GetFileDropList();
            Check("檔案貼得回去", back.Count == 1 && back[0] == tmp);
            watcher.IgnoreFor(0);
        }
        watcher.Dispose();

        var big = new string('x', ClipboardWatcher.MaxTextChars + 10);
        var watcher2 = new ClipboardWatcher(store, listen: false);
        SetClipboard(new DataObject(DataFormats.UnicodeText, big));
        long beforeBig = store.Count();
        watcher2.Capture();
        Check("超過 5 MB 的文字不記", store.Count() == beforeBig);
        watcher2.Dispose();

        Check("工作列和空視窗不能當貼上目標", !Native.IsPasteTarget(IntPtr.Zero) && !Native.IsPasteTarget(Native.FindWindow("Shell_TrayWnd", null)));

        Check("判斷單一網址", ClipboardWatcher.LooksLikeSingleUrl("https://example.com/a.png") && !ClipboardWatcher.LooksLikeSingleUrl("看這個 https://example.com"));

        var hk = new HotKeyWindow();
        Check("全域快捷鍵註冊成功", hk.Register(HotKeyChoice.All[0]));
        hk.Dispose();

        Finish("selftest.txt");
        return failures == 0 ? 0 : 1;
    }

    // MARK: 版面截圖

    static void Pump(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    public static void Shoot(Rectangle area, string file)
    {
        try
        {
            using var bmp = new Bitmap(area.Width, area.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            bmp.Save(file, ImageFormat.Png);
            Note("OK  " + Path.GetFileName(file));
        }
        catch (Exception e) { Note($"FAIL  {Path.GetFileName(file)} {e.Message}"); failures++; }
    }

    public static void ShootScreen(string file) => Shoot(Screen.PrimaryScreen!.Bounds, file);

    static void Shoot(Form form, string file)
    {
        if (!form.Visible) form.Show();
        form.Activate();
        Pump(500);
        Shoot(Native.VisibleBounds(form.Handle, form.Bounds), file);
    }

    public static int RenderUi(string dir)
    {
        if (!Paths.TestMode) { Console.WriteLine("請先設定 TIETIE_HOME"); return 2; }
        Directory.CreateDirectory(dir);
        Settings.Update(s => { s.Onboarded = true; s.MaxItems = 1000; s.ImageCapMB = 2048; });
        using var store = new Store();
        store.ClearUnpinned();
        // 較早的記錄，讓清單有好幾頁可以看分頁效果
        foreach (var (text, app) in new[] {
            ("週五前把簡報寄給 Amy", "Slack"), ("Wi-Fi 密碼在冰箱旁邊", "LINE"), ("下週二 10:00 牙醫", "行事曆"),
            ("https://kueilan.com", "Google Chrome"), ("發票抬頭：貼貼工作室", "Notion"), ("會議室 B 已預約到 4 點", "Outlook"),
            ("新竹市東區光復路一段 100 號", "Google 地圖"), ("請幫我把這段翻成英文", "ChatGPT"), ("報名表下載連結已寄出", "Gmail"),
            ("AB-20261101 已出貨", "蝦皮"), ("記得帶身分證影本", "記事本"), ("簡報第 7 頁圖表要換", "PowerPoint"),
            ("今天晚餐吃咖哩", "LINE"), ("帳單 11/25 到期", "Gmail"), ("Zoom 會議 ID 123 456 7890", "Zoom"),
            ("把新版合約傳給律師", "Slack"), ("下載頁網址要放在最後一張", "Notion"), ("星期六早上去郵局", "記事本") })
        {
            store.AddText(text, "text", app);
            Thread.Sleep(10);
        }
        store.AddText("https://github.com/KLWSharpe/tietie", "text", "Google Chrome");
        Thread.Sleep(10);
        store.AddText("王小明　0912-345-678　台北市信義區松仁路 100 號", "text", "Notion");
        Thread.Sleep(10);
        var png = TextImage("發票號碼 AB-12345678");
        if (store.AddImage(png, 900, 220, "剪取工具", out _) is long imageId)
        {
            var ocr = Task.Run(() => Ocr.RecognizeAsync(png)).GetAwaiter().GetResult();
            store.SetOcr(imageId, ocr.Length > 0 ? ocr : "發票號碼 AB-12345678");
        }
        Thread.Sleep(10);
        store.AddText(string.Concat(Enumerable.Repeat("這是一段很長很長的文字，用來確認清單裡太長的內容會被截斷而不是把版面撐壞。", 6)), "text", "Word");
        Thread.Sleep(10);
        store.AddText(@"C:\Users\demo\Documents\報價單.pdf" + "\n" + @"C:\Users\demo\Documents\合約.pdf", "file", "檔案總管");
        Thread.Sleep(10);
        store.AddText("下午三點開會，記得帶筆電", "text", "LINE");
        Thread.Sleep(10);
        if (store.AddText("kueilan@example.com", "text", "Outlook") is long pinId) store.SetPinned(pinId, true);

        var panel = new HistoryForm(store);
        panel.ShowPanel();
        Pump(600);
        Shoot(Rectangle.Inflate(panel.Bounds, 12, 12), Path.Combine(dir, "1_清單.png"));
        panel.GoToPage(1);
        Pump(300);
        Shoot(Rectangle.Inflate(panel.Bounds, 12, 12), Path.Combine(dir, "1b_第二頁.png"));
        panel.GoToPage(0);
        Pump(300);
        // 右鍵選單（選到圖片那一列）
        int imageRow = panel.Items.ToList().FindIndex(i => i.Kind == "image");
        if (imageRow >= 0)
        {
            panel.ShowMenu(imageRow, new Point(160, 40 + imageRow * 46));
            Pump(500);
            Shoot(Rectangle.Inflate(panel.Bounds, 12, 12), Path.Combine(dir, "2_右鍵選單.png"));
            SendKeys.SendWait("{ESC}");
            Pump(300);
        }
        panel.ShowPanel();
        SendKeys.SendWait("12345678");
        Pump(500);
        Shoot(Rectangle.Inflate(panel.Bounds, 12, 12), Path.Combine(dir, "3_搜尋發票.png"));
        panel.HidePanel();

        Shoot(new OnboardingForm(), Path.Combine(dir, "4_首次設定.png"));
        Shoot(new SettingsForm(store), Path.Combine(dir, "5_設定.png"));
        Shoot(new AboutForm(), Path.Combine(dir, "6_關於.png"));
        // 工作列小圖示：放大給人看，深色和淺色工作列各一張
        using (var sheet = new Bitmap(360, 140))
        {
            using var g = Graphics.FromImage(sheet);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.Clear(Color.White);
            using (var dark = new SolidBrush(Color.FromArgb(32, 32, 32))) g.FillRectangle(dark, 0, 0, 180, 140);
            using (var normal = Icons.Tray(false)) g.DrawIcon(normal, new Rectangle(20, 20, 64, 64));
            using (var paused = Icons.Tray(true)) g.DrawIcon(paused, new Rectangle(100, 20, 64, 64));
            using (var real = Icons.Tray(false)) g.DrawIcon(real, new Rectangle(20, 100, 16, 16));
            using var p2 = new System.Drawing.Drawing2D.GraphicsPath();
            var bmp = new Bitmap(32, 32);
            using (var bg = Graphics.FromImage(bmp))
            {
                bg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Icons.DrawPapers(bg, new RectangleF(1, 1, 30, 30), Color.FromArgb(30, 30, 30), 2.6f, null);
            }
            g.DrawImage(bmp, new Rectangle(200, 20, 64, 64));
            g.DrawImage(bmp, new Rectangle(200, 100, 16, 16));
            sheet.Save(Path.Combine(dir, "8_工作列圖示.png"), ImageFormat.Png);
            Note("OK  8_工作列圖示.png");
        }
        Toast.Show("已複製，按 Ctrl+V 貼上", 5);
        Pump(500);
        ShootScreen(Path.Combine(dir, "7_整個螢幕.png"));
        Finish("render.txt");
        return failures == 0 ? 0 : 1;
    }
}

/// 實際操作測試：開記事本，模擬鍵盤操作貼貼，確認真的貼進記事本。
static class EndToEnd
{
    static readonly StringBuilder log = new();
    static int failures;

    static void Check(string name, bool ok, string detail = "")
    {
        var line = (ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? $"  [{detail}]" : "");
        log.AppendLine(line);
        Console.WriteLine(line);
        if (!ok) failures++;
    }

    static void Pump(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    static void Key(byte vk, bool up) => Native.keybd_event(vk, 0, up ? Native.KEYEVENTF_KEYUP : 0, UIntPtr.Zero);

    static void Press(params byte[] keys)
    {
        foreach (var k in keys) { Key(k, false); Thread.Sleep(20); }
        foreach (var k in keys.Reverse()) { Key(k, true); Thread.Sleep(20); }
    }

    static IntPtr FindNotepad(int pid)
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h)) return true;
            Native.GetWindowThreadProcessId(h, out uint p);
            var cls = Native.ClassOf(h);
            if (p == pid || cls == "Notepad") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static IntPtr FindEditor(IntPtr window)
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumChildWindows(window, (h, _) =>
        {
            var cls = Native.ClassOf(h);
            if (cls == "Edit" || cls.StartsWith("RichEdit")) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static int Run()
    {
        if (!Paths.TestMode) { Console.WriteLine("請先設定 TIETIE_HOME"); return 2; }
        var shots = Environment.GetEnvironmentVariable("TIETIE_SHOTS") ?? Path.Combine(Paths.Home, "shots");
        Directory.CreateDirectory(shots);
        Settings.Update(s => { s.Onboarded = true; s.RecordConcealed = true; s.Paused = false; s.HotKey = "ctrl+shift+v"; });
        var app = new TrayApp(noOnboarding: true);
        Check("快捷鍵 Ctrl+Shift+V 註冊成功", app.HotKeyOk);

        var notepadProcess = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
        IntPtr notepad = IntPtr.Zero;
        for (int i = 0; i < 60 && notepad == IntPtr.Zero; i++)
        {
            Pump(250);
            notepad = FindNotepad(notepadProcess?.Id ?? -1);
        }
        Check("打開記事本", notepad != IntPtr.Zero);
        var editor = notepad == IntPtr.Zero ? IntPtr.Zero : FindEditor(notepad);
        string NotepadText() => editor == IntPtr.Zero ? "" : Native.TextOf(editor);

        void Copy(DataObject d)
        {
            for (int i = 0; i < 6; i++)
            {
                try { Clipboard.SetDataObject(d, true, 5, 100); break; } catch { Thread.Sleep(80); }
            }
            Pump(700);
        }

        var one = new DataObject();
        one.SetData(DataFormats.UnicodeText, "第一筆 e2e-one");
        one.SetData(DataFormats.Html, "<b>第一筆 e2e-one</b>");
        Copy(one);
        Copy(new DataObject(DataFormats.UnicodeText, "第二筆 e2e-two"));
        Check("複製兩段文字，貼貼都有記到", app.Store.List("e2e").Count == 2, string.Join(" | ", app.Store.List("e2e").Select(i => i.Text)));

        void FocusNotepad()
        {
            Native.Focus(notepad);
            Pump(500);
        }

        FocusNotepad();
        Check("記事本在最前面", Native.GetForegroundWindow() == notepad);
        Press(0x11, 0x10, 0x56); // Ctrl+Shift+V
        Pump(800);
        Check("按 Ctrl+Shift+V 叫出清單", app.Panel.Visible && Native.GetForegroundWindow() == app.Panel.Handle,
            $"visible={app.Panel.Visible}");
        SelfTestShoot(Path.Combine(shots, "e2e_1_清單.png"));
        Press(0x28);             // 下
        Pump(200);
        Press(0x0D);             // Enter
        Pump(1500);
        Check("Enter 貼上：清單關掉、回到記事本", !app.Panel.Visible && Native.GetForegroundWindow() == notepad);
        Check("記事本裡出現第一筆文字", NotepadText().Contains("第一筆 e2e-one"), NotepadText());
        SelfTestShoot(Path.Combine(shots, "e2e_2_貼上之後.png"));

        Press(0x11, 0x10, 0x56);
        Pump(800);
        Press(0x11, 0x32);       // Ctrl+2
        Pump(1500);
        Check("Ctrl+2 快速貼上第二筆", NotepadText().Contains("第二筆 e2e-two"), NotepadText());

        Press(0x11, 0x10, 0x56);
        Pump(800);
        foreach (var k in new byte[] { 0x45, 0x32, 0x45, 0xBD, 0x54, 0x57, 0x4F }) { Press(k); Pump(60); }  // e2e-two
        Pump(400);
        Check("在清單打字搜尋，只剩符合的那一筆", app.Panel.SearchText == "e2e-two" && app.Panel.Items.Count == 1,
            $"搜尋框「{app.Panel.SearchText}」，{app.Panel.Items.Count} 筆");
        Press(0x1B);             // Esc
        Pump(500);
        Check("Esc 關掉清單", !app.Panel.Visible);

        // 換頁：多複製 12 筆，清單就有兩頁
        for (int i = 1; i <= 12; i++) Copy(new DataObject(DataFormats.UnicodeText, $"換頁測試 e2e-page-{i:00}"));
        FocusNotepad();
        Press(0x11, 0x10, 0x56);
        Pump(800);
        Check("記錄超過 10 筆時有兩頁", app.Panel.PageCount >= 2 && app.Panel.Page == 0 && app.Panel.Items.Count == 10,
            $"第 {app.Panel.Page + 1}／{app.Panel.PageCount} 頁，{app.Panel.Items.Count} 筆");
        Press(0x27);             // 右
        Pump(300);
        SelfTestShoot(Path.Combine(shots, "e2e_3_第二頁.png"));
        var secondPage = app.Panel.Items.Select(i => i.Text).ToList();
        Check("按右鍵換到第二頁（第 11 到 20 筆）", app.Panel.Page == 1 && secondPage.Count > 1, string.Join(" | ", secondPage));
        Press(0x25);             // 左
        Pump(300);
        Check("按左鍵回到第一頁", app.Panel.Page == 0);
        for (int i = 0; i < 9; i++) { Press(0x28); Pump(60); }
        Press(0x28);             // 第一頁最後一筆再按下
        Pump(300);
        Check("在第一頁最後一筆按下，跳到第二頁第一筆", app.Panel.Page == 1 && app.Panel.SelectedIndex == 0,
            $"第 {app.Panel.Page + 1} 頁第 {app.Panel.SelectedIndex + 1} 筆");
        Press(0x26);             // 上
        Pump(300);
        Check("在第二頁第一筆按上，回到第一頁最後一筆", app.Panel.Page == 0 && app.Panel.SelectedIndex == 9,
            $"第 {app.Panel.Page + 1} 頁第 {app.Panel.SelectedIndex + 1} 筆");
        Press(0x27);
        Pump(300);
        var wantSecond = secondPage.Count > 1 ? secondPage[1] : "?";
        Press(0x11, 0x32);       // 第二頁的 Ctrl+2 = 第 12 筆
        Pump(1500);
        Check("第二頁按 Ctrl+2 貼上第 12 筆", NotepadText().Contains(wantSecond), $"要的是「{wantSecond}」");
        Press(0x11, 0x10, 0x56);
        Pump(800);
        var wantTenth = app.Panel.Items.Count >= 10 ? app.Panel.Items[9].Text : "?";
        Press(0x11, 0x30);       // Ctrl+0 = 這頁第 10 筆
        Pump(1500);
        Check("Ctrl+0 貼上這頁第 10 筆", NotepadText().Contains(wantTenth), $"要的是「{wantTenth}」");

        // 直接按數字（不用按 Ctrl）就貼上這一頁的第幾筆
        Press(0x11, 0x10, 0x56);
        Pump(800);
        var wantThird = app.Panel.Items.Count >= 3 ? app.Panel.Items[2].Text : "?";
        Press(0x33);             // 3
        Pump(1500);
        Check("搜尋框是空的時候，直接按 3 貼上第 3 筆", !app.Panel.Visible && NotepadText().EndsWith(wantThird), $"要的是「{wantThird}」");
        Press(0x11, 0x10, 0x56);
        Pump(800);
        Press(0x45); Pump(60);   // e
        Press(0x32); Pump(300);  // 2
        Check("搜尋框有字時，按數字是打字", app.Panel.Visible && app.Panel.SearchText == "e2", $"搜尋框「{app.Panel.SearchText}」");
        Press(0x1B);
        Pump(500);

        // 從工作列打開清單時，沒有原本的視窗，不可以把內容按進別的地方
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        Native.Focus(tray);
        Pump(400);
        var beforeTray = NotepadText();
        app.Panel.ShowPanel();
        Pump(500);
        Press(0x0D);
        Pump(1500);
        Check("從工作列打開時，不會把內容按進其他視窗", NotepadText() == beforeTray && app.Panel.PreviousWindow == IntPtr.Zero,
            $"previous={app.Panel.PreviousWindow}");

        var png = SelfTest.TextImage("INVOICE 4821");
        var picture = new DataObject();
        picture.SetData("PNG", false, new MemoryStream(png));
        using (var bmp = (Bitmap)Image.FromStream(new MemoryStream(png))) picture.SetData(DataFormats.Bitmap, true, new Bitmap(bmp));
        Copy(picture);
        for (int i = 0; i < 40 && app.Store.List("4821").Count == 0; i++) Pump(250);
        Check("複製圖片後讀出圖片裡的字", app.Store.List("4821").FirstOrDefault()?.Kind == "image");

        FocusNotepad();
        Press(0x11, 0x10, 0x56);
        Pump(800);
        SelfTestShoot(Path.Combine(shots, "e2e_3_圖片在清單.png"));
        Press(0x12, 0x0D);       // Alt+Enter
        Pump(1800);
        Check("Alt+Enter 把圖片裡的字貼進記事本", NotepadText().Contains("4821"), NotepadText().Replace("\r\n", " / "));
        SelfTestShoot(Path.Combine(shots, "e2e_4_貼出圖片文字.png"));

        // 工作列圖示的選單
        var area = Screen.PrimaryScreen!.WorkingArea;
        app.ShowTrayMenu(new Point(area.Right - 320, area.Bottom - 340));
        Pump(600);
        SelfTestShoot(Path.Combine(shots, "e2e_5_工作列選單.png"));
        Press(0x1B);
        Pump(300);

        try { notepadProcess?.Kill(); } catch { }
        foreach (var p in Process.GetProcessesByName("notepad")) { try { p.Kill(); } catch { } }
        app.Quit();
        log.AppendLine(failures == 0 ? "\n全部通過" : $"\n有 {failures} 項沒過");
        File.WriteAllText(Path.Combine(Paths.Home, "e2e.txt"), log.ToString(), Encoding.UTF8);
        return failures == 0 ? 0 : 1;
    }

    static void SelfTestShoot(string file) => SelfTest.ShootScreen(file);
}
