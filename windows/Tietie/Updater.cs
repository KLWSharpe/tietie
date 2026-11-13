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
            if (manual) Info("暫時查不到新版資訊", "可能是網路沒有連上，請稍後再試一次。");
            return;
        }
        var latest = tag.Trim().TrimStart('v', 'V');
        if (!IsNewer(latest, Program.Version))
        {
            if (manual) Info("已經是最新版", $"你現在用的貼貼 {Program.Version} 就是最新版本。");
            return;
        }
        if (!manual && Settings.Data.SkippedUpdateVersion == latest) return;
        Prompt(latest);
    }

    static void Prompt(string latest)
    {
        var update = new TaskDialogButton("立即更新");
        var later = new TaskDialogButton("稍後再說");
        var skip = new TaskDialogButton("略過這一版");
        var page = new TaskDialogPage
        {
            Caption = "貼貼",
            Heading = $"貼貼有新版本 {latest}",
            Text = $"你現在用的是 {Program.Version}。按「立即更新」，幾秒鐘就裝好，原本的記錄都會留著。",
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
            Info("沒辦法自動更新", "請按 Windows 鍵，打 PowerShell，按 Enter，貼上下面這一行再按 Enter：\n\n" + InstallCommand);
        }
    }

    static void Info(string title, string text) =>
        MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
}
