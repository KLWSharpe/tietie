using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Tietie;

/// 檢查 GitHub 上有沒有新版。只送出一個查詢最新版本號的請求，不會傳出任何剪貼簿記錄。
static class Updater
{
    const string Repo = "KLWSharpe/tietie";
    public const string InstallCommand = "irm https://raw.githubusercontent.com/" + Repo + "/main/install.ps1 | iex";
    static string LatestUrl => Environment.GetEnvironmentVariable("TIETIE_UPDATE_URL")
        ?? $"https://api.github.com/repos/{Repo}/releases/latest";
    static System.Windows.Forms.Timer? timer;
    static bool checking;

    /// 把「v0.4.0」「0.4」這類版本號轉成數字陣列，讀不懂的部分當 0。
    public static int[] Parts(string version) =>
        version.Trim().TrimStart('v', 'V').Split('.')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .ToArray();

    public static bool IsNewer(string candidate, string current)
    {
        var a = Parts(candidate);
        var b = Parts(current);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// 開啟後過一會兒檢查一次，之後每天檢查一次。
    public static void StartAutomaticChecks()
    {
        var first = new System.Windows.Forms.Timer { Interval = 15_000 };
        first.Tick += (_, _) => { first.Stop(); first.Dispose(); _ = Check(false); };
        first.Start();
        timer = new System.Windows.Forms.Timer { Interval = 24 * 60 * 60 * 1000 };
        timer.Tick += (_, _) => _ = Check(false);
        timer.Start();
    }

    public static async Task Check(bool manual)
    {
        if (checking) return;
        checking = true;
        string? tag = null;
        try
        {
            if (LatestUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                tag = ReadTag(await File.ReadAllTextAsync(new Uri(LatestUrl).LocalPath));
            }
            else
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Tietie/{Program.Version}");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                tag = ReadTag(await http.GetStringAsync(LatestUrl));
            }
        }
        catch { }
        finally { checking = false; }
        Handle(tag, manual);
    }

    internal static string? ReadTag(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
    }

    static void Handle(string? tag, bool manual)
    {
        if (tag == null)
        {
            if (manual) Info(L.T("update.unreachableTitle"), L.T("update.unreachableText"));
            return;
        }
        var latest = tag.Trim().TrimStart('v', 'V');
        if (!IsNewer(latest, Program.Version))
        {
            if (manual) Info(L.T("update.latestTitle"), L.T("update.latestText", Program.Version));
            return;
        }
        if (!manual && Settings.Data.SkippedUpdateVersion == latest) return;
        Prompt(latest);
    }

    static void Prompt(string latest)
    {
        var update = new TaskDialogButton(L.T("update.now"));
        var later = new TaskDialogButton(L.T("update.later"));
        var skip = new TaskDialogButton(L.T("update.skip"));
        var page = new TaskDialogPage
        {
            Caption = L.T("app.fullName"),
            Heading = L.T("update.heading", latest),
            Text = L.T("win.update.text", Program.Version),
            Buttons = { update, later, skip },
            DefaultButton = update,
            Icon = TaskDialogIcon.Information,
        };
        var result = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
        if (result == update) Install();
        else if (result == skip) Settings.Update(s => s.SkippedUpdateVersion = latest);
    }

    /// 執行跟安裝教學同一行的安裝指令。它會關掉目前的貼貼、換成新版，再自動打開。
    static void Install()
    {
        if (Environment.GetEnvironmentVariable("TIETIE_UPDATE_DRYRUN") != null) return;
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(InstallCommand);
            // 裝回原本的位置
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(dir)) psi.Environment["TIETIE_INSTALL_DIR"] = dir;
            Process.Start(psi);
        }
        catch
        {
            Info(L.T("update.failTitle"), L.T("win.update.failText", InstallCommand));
        }
    }

    static void Info(string title, string text) =>
        MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
}
