using System.Text.Json;
using Microsoft.Win32;

namespace Tietie;

static class Paths
{
    public static readonly string Home = Environment.GetEnvironmentVariable("TIETIE_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tietie");
    public static string Db => Path.Combine(Home, "history.db");
    public static string Images => Path.Combine(Home, "images");
    public static string SettingsFile => Path.Combine(Home, "settings.json");

    /// 測試模式：指定了 TIETIE_HOME 時不碰登錄檔。
    public static bool TestMode => Environment.GetEnvironmentVariable("TIETIE_HOME") != null;

    static Paths()
    {
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Images);
    }
}

sealed class SettingsData
{
    public bool Onboarded { get; set; }
    public bool RecordConcealed { get; set; }
    public int MaxItems { get; set; } = 1000;
    public int ImageCapMB { get; set; } = 2048;
    public bool Paused { get; set; }
    public bool LaunchAtLogin { get; set; } = true;
    public string HotKey { get; set; } = "ctrl+shift+v";
}

static class Settings
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static SettingsData Data { get; private set; } = Load();

    static SettingsData Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(Paths.SettingsFile), Options) ?? new SettingsData();
        }
        catch { }
        return new SettingsData();
    }

    public static void Update(Action<SettingsData> change)
    {
        change(Data);
        try { File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(Data, Options)); } catch { }
    }
}

sealed record HotKeyChoice(string Id, string Label, uint Modifiers)
{
    public static readonly HotKeyChoice[] All =
    {
        new("ctrl+shift+v", "Ctrl + Shift + V", Native.MOD_CONTROL | Native.MOD_SHIFT),
        new("ctrl+alt+v", "Ctrl + Alt + V", Native.MOD_CONTROL | Native.MOD_ALT),
        new("alt+shift+v", "Alt + Shift + V", Native.MOD_ALT | Native.MOD_SHIFT),
    };
    public static HotKeyChoice Find(string id) => All.FirstOrDefault(c => c.Id == id) ?? All[0];
}

/// 開機自動啟動（寫在目前使用者的登錄檔，不需要管理員權限）。
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool on)
    {
        if (Paths.TestMode) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue("Tietie", $"\"{Application.ExecutablePath}\" --background");
            else key.DeleteValue("Tietie", false);
        }
        catch { }
    }
}
