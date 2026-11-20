using System.Globalization;
using System.Resources;

namespace Tietie;

/// 介面語言。文字放在 Strings*.resx（由 i18n/*.json 產生，見 scripts/i18n.py）。
/// 設定是「system」時跟著 Windows 的顯示語言走，沒有支援就用英文。
static class Lang
{
    public static readonly string[] Supported = { "zh-Hant", "en", "zh-Hans", "ja", "ko", "pt", "es" };
    /// 每個語言用自己的文字寫自己的名字，看不懂目前語言的人也找得到自己的語言
    public static readonly Dictionary<string, string> Names = new()
    {
        ["zh-Hant"] = "繁體中文", ["en"] = "English", ["zh-Hans"] = "简体中文",
        ["ja"] = "日本語", ["ko"] = "한국어", ["pt"] = "Português", ["es"] = "Español",
    };
    public const string Fallback = "en";

    static readonly ResourceManager Resources = new("Tietie.Strings", typeof(Lang).Assembly);
    /// 程式一開始的 Windows 顯示語言（之後不會被貼貼改掉）
    public static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    public static string Current { get; private set; } = Resolve(Settings.Data.Language, SystemCulture.Name);
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Current);

    /// 把 Windows 的語言代碼（例如 zh-TW、zh-HK、zh-CN、pt-BR、ja-JP）對應到支援的語言，沒有就回傳 null。
    public static string? Match(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var parts = name.Replace('_', '-').ToLowerInvariant().Split('-');
        switch (parts[0])
        {
            case "zh":
                return parts.Contains("hant") || parts.Contains("tw") || parts.Contains("hk") || parts.Contains("mo") ? "zh-Hant" : "zh-Hans";
            case "en": case "ja": case "ko": case "pt": case "es":
                return parts[0];
            default:
                return null;
        }
    }

    public static string SystemLanguage(string? systemName = null) => Match(systemName ?? SystemCulture.Name) ?? Fallback;

    public static string Resolve(string setting, string? systemName) =>
        Supported.Contains(setting) ? setting : SystemLanguage(systemName);

    /// 設定改了之後重新決定語言。
    public static void Reload() => Current = Resolve(Settings.Data.Language, SystemCulture.Name);

    /// 取出一句介面文字。{0}、{1} 依序換成後面的參數。目前語言沒有這一句就用英文。
    public static string Text(string key, string? language, params object[] args)
    {
        var culture = CultureInfo.GetCultureInfo(language ?? Current);
        var value = Resources.GetString(key, culture) ?? Resources.GetString(key, CultureInfo.InvariantCulture) ?? key;
        return args.Length == 0 ? value : string.Format(culture, value, args);
    }

    /// 介面字型：繁中用正黑體、簡中用雅黑、日文用 Yu Gothic、韓文用 Malgun Gothic，其他用 Segoe UI。
    public static string FontName
    {
        get
        {
            string[] wanted = Current switch
            {
                "zh-Hant" => new[] { "Microsoft JhengHei UI", "Microsoft JhengHei" },
                "zh-Hans" => new[] { "Microsoft YaHei UI", "Microsoft YaHei" },
                "ja" => new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo" },
                "ko" => new[] { "Malgun Gothic" },
                _ => new[] { "Segoe UI" },
            };
            foreach (var name in wanted)
                if (Installed.Contains(name)) return name;
            return "Microsoft JhengHei UI";
        }
    }

    static HashSet<string>? installed;
    static HashSet<string> Installed => installed ??= new HashSet<string>(
        new System.Drawing.Text.InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
}

/// 簡寫：L.T("menu.quit")、L.T("menu.stats", 12, "3 MB")
static class L
{
    public static string T(string key, params object[] args) => Lang.Text(key, null, args);
}
