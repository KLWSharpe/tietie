using System.Runtime.InteropServices;
using System.Text;

namespace Tietie;

/// Windows 系統函式。
static class Native
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_USER = 0x0400;
    public const int WM_GETTEXT = 0x000D;
    public const int WM_GETTEXTLENGTH = 0x000E;

    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    public const uint KEYEVENTF_KEYUP = 0x2;

    [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("imm32.dll")] public static extern uint ImmGetVirtualKey(IntPtr hWnd);
    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr himc);
    [DllImport("imm32.dll")] static extern bool ImmNotifyIME(IntPtr himc, uint action, uint index, uint value);

    /// 取消輸入法正在組的字（數字鍵被拿去快速貼上時，不要留下注音符號）
    public static void CancelComposition(IntPtr hWnd)
    {
        var himc = ImmGetContext(hWnd);
        if (himc == IntPtr.Zero) return;
        ImmNotifyIME(himc, 0x15 /* NI_COMPOSITIONSTR */, 0x4 /* CPS_CANCEL */, 0);
        ImmReleaseContext(hWnd, himc);
    }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

    /// 視窗實際看得到的範圍（不含 Windows 11 看不見的邊框）。
    public static Rectangle VisibleBounds(IntPtr hwnd, Rectangle fallback)
    {
        try
        {
            if (DwmGetWindowAttribute(hwnd, 9, out RECT r, Marshal.SizeOf<RECT>()) == 0)
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
        catch { }
        return fallback;
    }

    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int ret);

    static readonly string[] ShellClasses =
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "Windows.UI.Core.CoreWindow", "Progman", "WorkerW", "XamlExplorerHostIslandWindow",
    };

    /// 這個視窗能不能當成貼上的目標（排除工作列、開始功能表、桌面和貼貼自己）。
    public static bool IsPasteTarget(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;
        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0 || pid == Environment.ProcessId) return false;
        return !ShellClasses.Contains(ClassOf(GetAncestor(hWnd, 2)));
    }

    public static uint ProcessOf(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        return pid;
    }

    static bool? selfElevated;

    /// 這個行程是不是用系統管理員權限在跑。查不到時回傳 null。
    public static bool? IsElevated(uint pid)
    {
        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, 0x0008, out var token)) return null;
            try { return GetTokenInformation(token, 20, out int elevated, 4, out _) ? elevated != 0 : null; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    /// 目標視窗權限比貼貼高時，Windows 會擋掉模擬按鍵。
    public static bool BlocksKeys(IntPtr hWnd)
    {
        selfElevated ??= IsElevated((uint)Environment.ProcessId) ?? false;
        if (selfElevated == true) return false;
        return IsElevated(ProcessOf(hWnd)) == true;
    }

    public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// 把某個視窗叫到最前面（系統限制比較多，所以先借用目前前景視窗的輸入狀態）。
    public static void Focus(IntPtr target)
    {
        if (target == IntPtr.Zero || !IsWindow(target)) return;
        if (IsIconic(target)) ShowWindow(target, 9);
        var fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        bool attached = fg != IntPtr.Zero && fgThread != me && AttachThreadInput(me, fgThread, true);
        SetForegroundWindow(target);
        BringWindowToTop(target);
        if (attached) AttachThreadInput(me, fgThread, false);
    }

    public static string ClassOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string TextOf(IntPtr hWnd)
    {
        int len = (int)SendMessage(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        var sb = new StringBuilder(Math.Max(len + 1, 1));
        SendMessage(hWnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb);
        return sb.ToString();
    }

    /// 圓角視窗（Windows 11 才有效果）。
    public static void RoundCorners(IntPtr hwnd)
    {
        try
        {
            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        }
        catch { }
    }
}
