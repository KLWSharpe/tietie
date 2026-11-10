using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Tietie;

/// 監看剪貼簿：Windows 每次剪貼簿有變化都會通知這個隱藏視窗。
sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    public const string WindowTitle = "TietieMessageWindow";
    public const int WM_SHOW = Native.WM_USER + 1;

    // 密碼管理器複製時會加上的標記
    static readonly string[] ConcealedFormats = { "ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore" };
    // 密碼管理器的程式名稱（有些瀏覽器擴充或舊版不會加標記，用來源程式再擋一層）
    static readonly string[] PasswordApps = { "keepass", "bitwarden", "1password", "lastpass", "dashlane", "enpass", "nordpass", "proton pass", "roboform", "keeper" };
    public const int MaxTextChars = 5 * 1024 * 1024 / 2;

    readonly Store store;
    readonly bool listening;
    readonly SynchronizationContext? ui = SynchronizationContext.Current;
    long ignoreUntil;

    public event Action? Changed;
    public event Action? ShowRequested;

    public ClipboardWatcher(Store store, bool listen = true)
    {
        this.store = store;
        CreateHandle(new CreateParams { Caption = WindowTitle });
        if (listen) listening = Native.AddClipboardFormatListener(Handle);
    }

    public void Dispose()
    {
        if (listening) Native.RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }

    /// 貼貼自己放進剪貼簿的內容，在這段時間內不要再記一次。
    public void IgnoreFor(int ms) => ignoreUntil = Environment.TickCount64 + ms;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_CLIPBOARDUPDATE)
        {
            try { Capture(); } catch { }
        }
        else if (m.Msg == WM_SHOW)
        {
            ShowRequested?.Invoke();
        }
        base.WndProc(ref m);
    }

    static IDataObject? Read()
    {
        for (int i = 0; i < 6; i++)
        {
            try { return Clipboard.GetDataObject(); }
            catch (ExternalException) { Thread.Sleep(60); }
        }
        return null;
    }

    public static bool IsConcealed(IDataObject data)
    {
        if (ConcealedFormats.Any(data.GetDataPresent)) return true;
        try
        {
            if (data.GetDataPresent("CanIncludeInClipboardHistory") && data.GetData("CanIncludeInClipboardHistory") is MemoryStream ms
                && ms.Length >= 4)
            {
                var bytes = ms.ToArray();
                return BitConverter.ToInt32(bytes, 0) == 0;
            }
        }
        catch { return true; }
        return false;
    }

    /// 讀目前的剪貼簿並記錄。回傳有沒有記下東西。
    public bool Capture()
    {
        if (Environment.TickCount64 < ignoreUntil) return false;
        var s = Settings.Data;
        if (s.Paused) return false;
        var data = Read();
        if (data == null) return false;
        var source = ForegroundApp();
        bool concealed = IsConcealed(data) || PasswordApps.Any(p => source.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (concealed && !(s.Onboarded && s.RecordConcealed)) return false;
        bool recorded = false;

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            recorded = store.AddText(string.Join("\n", files), "file", source) != null;
        }
        else
        {
            string text = "";
            try { if (data.GetDataPresent(DataFormats.UnicodeText)) text = data.GetData(DataFormats.UnicodeText) as string ?? ""; } catch { }
            bool hasImage = data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib);
            if (hasImage && (string.IsNullOrWhiteSpace(text) || LooksLikeSingleUrl(text)))
            {
                var png = ReadPng(data, out int w, out int h);
                if (png != null)
                {
                    var id = store.AddImage(png, w, h, source, out bool isNew);
                    recorded = id != null;
                    if (id is long newId && isNew) RecognizeLater(newId, png);
                }
            }
            else if (!string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextChars)
            {
                recorded = store.AddText(text, "text", source) != null;
            }
        }
        if (recorded) Changed?.Invoke();
        return recorded;
    }

    void RecognizeLater(long id, byte[] png)
    {
        Task.Run(() => Ocr.RecognizeAsync(png)).ContinueWith(t =>
        {
            var text = t.IsCompletedSuccessfully ? t.Result : "";
            if (text.Length == 0) return;
            void Apply()
            {
                store.SetOcr(id, text);
                Changed?.Invoke();
            }
            if (ui != null) ui.Post(_ => Apply(), null); else Apply();
        });
    }

    static byte[]? ReadPng(IDataObject data, out int width, out int height)
    {
        width = height = 0;
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream pngStream)
            {
                var bytes = pngStream.ToArray();
                using var probe = Image.FromStream(new MemoryStream(bytes));
                width = probe.Width;
                height = probe.Height;
                return bytes;
            }
        }
        catch { }
        try
        {
            var image = data.GetData(DataFormats.Bitmap) as Image ?? Clipboard.GetImage();
            if (image == null) return null;
            using (image)
            {
                width = image.Width;
                height = image.Height;
                using var ms = new MemoryStream();
                image.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
        catch { return null; }
    }

    public static bool LooksLikeSingleUrl(string text)
    {
        var t = text.Trim();
        return !t.Contains(' ') && !t.Contains('\n') && Uri.TryCreate(t, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
    }

    static string ForegroundApp()
    {
        try
        {
            var h = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0) return "";
            if (pid == Environment.ProcessId) return "貼貼";
            using var p = Process.GetProcessById((int)pid);
            var name = p.ProcessName;
            try
            {
                var desc = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim();
            }
            catch { }
            return name;
        }
        catch { return ""; }
    }
}

/// 把選好的內容放回剪貼簿，再替使用者按 Ctrl+V。
static class Paster
{
    public static bool AutoPaste => Environment.GetEnvironmentVariable("TIETIE_NO_AUTOPASTE") == null;

    static bool Put(DataObject data)
    {
        for (int i = 0; i < 6; i++)
        {
            try { Clipboard.SetDataObject(data, true, 5, 100); return true; }
            catch (ExternalException) { Thread.Sleep(80); }
        }
        return false;
    }

    /// 只放純文字，不帶任何格式。
    public static bool WriteText(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, false, text);
        return Put(data);
    }

    /// 圖片檔不見時回傳 false，而且不動剪貼簿。
    public static bool Write(ClipItem item)
    {
        switch (item.Kind)
        {
            case "image":
                var path = item.ImagePath;
                if (path == null || !File.Exists(path)) return false;
                byte[] png;
                Bitmap bitmap;
                try
                {
                    png = File.ReadAllBytes(path);
                    using var image = Image.FromStream(new MemoryStream(png));
                    bitmap = new Bitmap(image);
                }
                catch { return false; }
                using (bitmap)
                {
                    var imageData = new DataObject();
                    imageData.SetData(DataFormats.Bitmap, true, bitmap);
                    imageData.SetData("PNG", false, new MemoryStream(png));
                    return Put(imageData);
                }
            case "file":
                var existing = item.Text.Split('\n').Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
                if (existing.Length == 0) return WriteText(item.Text);
                var list = new StringCollection();
                list.AddRange(existing);
                var fileData = new DataObject();
                fileData.SetFileDropList(list);
                return Put(fileData);
            default:
                return WriteText(item.Text);
        }
    }

    /// 等使用者放開 Shift、Alt、Windows 鍵（最多 1.5 秒），再執行下一步。
    public static void AfterModifiersReleased(Action next)
    {
        var start = Environment.TickCount64;
        var timer = new System.Windows.Forms.Timer { Interval = 25 };
        timer.Tick += (_, _) =>
        {
            bool held = Native.KeyDown(0x10) || Native.KeyDown(0x12) || Native.KeyDown(0x5B) || Native.KeyDown(0x5C);
            if (held && Environment.TickCount64 - start < 1500) return;
            timer.Stop();
            timer.Dispose();
            next();
        };
        timer.Start();
    }

    /// 回到原本的視窗並按 Ctrl+V。確定焦點真的回去了才按，否則只提示使用者自己貼。
    public static void PasteInto(IntPtr target)
    {
        if (!Native.IsPasteTarget(target))
        {
            Toast.Show("已複製，按 Ctrl+V 貼上");
            return;
        }
        if (Native.BlocksKeys(target))
        {
            Native.Focus(target);
            Toast.Show("已複製。這個視窗是用系統管理員權限開的，請自己按 Ctrl+V 貼上", 3.5);
            return;
        }
        Native.Focus(target);
        var timer = new System.Windows.Forms.Timer { Interval = 70 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            var fg = Native.GetForegroundWindow();
            if (Native.ProcessOf(fg) == Native.ProcessOf(target)) SendCtrlV();
            else Toast.Show("已複製，按 Ctrl+V 貼上");
        };
        timer.Start();
    }

    public static void SendCtrlV()
    {
        foreach (byte vk in new byte[] { 0xA0, 0xA1, 0xA4, 0xA5, 0x5B, 0x5C })
            if (Native.KeyDown(vk)) Native.keybd_event(vk, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Native.keybd_event(0x11, 0, 0, UIntPtr.Zero);
        Native.keybd_event(0x56, 0, 0, UIntPtr.Zero);
        Native.keybd_event(0x56, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Native.keybd_event(0x11, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}

/// 全域快捷鍵。
sealed class HotKeyWindow : NativeWindow, IDisposable
{
    const int Id = 0x7417;
    bool registered;
    public event Action? Pressed;

    public HotKeyWindow() => CreateHandle(new CreateParams());

    public bool Register(HotKeyChoice choice)
    {
        if (registered) Native.UnregisterHotKey(Handle, Id);
        registered = Native.RegisterHotKey(Handle, Id, choice.Modifiers | Native.MOD_NOREPEAT, 0x56);
        return registered;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && (int)m.WParam == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (registered) Native.UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }
}

/// 用 Windows 內建的文字辨識讀出圖片裡的字。
static class Ocr
{
    static OcrEngine? engine;
    static bool tried;

    static OcrEngine? Engine()
    {
        if (tried) return engine;
        tried = true;
        foreach (var tag in new[] { "zh-Hant-TW", "zh-TW", "zh-Hant" })
        {
            try
            {
                var lang = new Windows.Globalization.Language(tag);
                if (OcrEngine.IsLanguageSupported(lang) && OcrEngine.TryCreateFromLanguage(lang) is OcrEngine e) return engine = e;
            }
            catch { }
        }
        try { if (OcrEngine.TryCreateFromUserProfileLanguages() is OcrEngine e) return engine = e; } catch { }
        try
        {
            var en = new Windows.Globalization.Language("en-US");
            if (OcrEngine.IsLanguageSupported(en)) engine = OcrEngine.TryCreateFromLanguage(en);
        }
        catch { }
        return engine;
    }

    public static string Language => Engine()?.RecognizerLanguage.LanguageTag ?? "(無)";

    static readonly Regex CjkGap = new(@"(?<=[　-〿一-鿿＀-￯])\s+(?=[　-〿一-鿿＀-￯])");

    public static async Task<string> RecognizeAsync(byte[] png)
    {
        try
        {
            var eng = Engine();
            if (eng == null) return "";
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync().AsTask().ConfigureAwait(false);
                await writer.FlushAsync().AsTask().ConfigureAwait(false);
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
            uint max = OcrEngine.MaxImageDimension;
            SoftwareBitmap bitmap;
            if (decoder.PixelWidth > max || decoder.PixelHeight > max)
            {
                double scale = Math.Min((double)max / decoder.PixelWidth, (double)max / decoder.PixelHeight);
                var transform = new BitmapTransform
                {
                    ScaledWidth = (uint)(decoder.PixelWidth * scale),
                    ScaledHeight = (uint)(decoder.PixelHeight * scale),
                };
                bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);
            }
            else
            {
                bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask().ConfigureAwait(false);
            }
            using (bitmap)
            {
                var result = await eng.RecognizeAsync(bitmap).AsTask().ConfigureAwait(false);
                var lines = result.Lines.Select(l => CjkGap.Replace(l.Text, "").Trim()).Where(l => l.Length > 0);
                return string.Join("\n", lines);
            }
        }
        catch { return ""; }
    }
}
