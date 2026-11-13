import AppKit

/// 檢查 GitHub 上有沒有新版。只送出一個查詢最新版本號的請求，不會傳出任何剪貼簿記錄。
enum Updater {
    static let repo = "KLWSharpe/tietie"
    /// 測試時可以用 TIETIE_UPDATE_URL 指到本機的假資料。
    static var latestURL: URL {
        if let custom = ProcessInfo.processInfo.environment["TIETIE_UPDATE_URL"], let url = URL(string: custom) { return url }
        return URL(string: "https://api.github.com/repos/\(repo)/releases/latest")!
    }
    static let installCommand = "curl -fsSL https://raw.githubusercontent.com/\(repo)/main/install.sh | bash"
    private static let skippedKey = "skippedUpdateVersion"
    private static var timer: Timer?
    private static var checking = false

    /// 把「v0.4.0」「0.4」這類版本號轉成數字陣列，讀不懂的部分當 0。
    static func parts(_ version: String) -> [Int] {
        let trimmed = version.trimmingCharacters(in: .whitespaces).trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
        return trimmed.split(separator: ".").map { Int($0.prefix { $0.isNumber }) ?? 0 }
    }

    static func isNewer(_ candidate: String, than current: String) -> Bool {
        let a = parts(candidate), b = parts(current)
        for i in 0..<max(a.count, b.count) {
            let x = i < a.count ? a[i] : 0, y = i < b.count ? b[i] : 0
            if x != y { return x > y }
        }
        return false
    }

    /// 開啟後過一會兒檢查一次，之後每天檢查一次。
    static func startAutomaticChecks() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 15) { check(manual: false) }
        timer = Timer.scheduledTimer(withTimeInterval: 24 * 60 * 60, repeats: true) { _ in check(manual: false) }
    }

    static func check(manual: Bool) {
        if checking { return }
        checking = true
        var request = URLRequest(url: latestURL, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 15)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("Tietie/\(appVersion)", forHTTPHeaderField: "User-Agent")
        URLSession.shared.dataTask(with: request) { data, _, _ in
            let tag = data.flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] }?["tag_name"] as? String
            DispatchQueue.main.async {
                checking = false
                handle(tag: tag, manual: manual)
            }
        }.resume()
    }

    private static func handle(tag: String?, manual: Bool) {
        guard let tag else {
            if manual { info("暫時查不到新版資訊", "可能是網路沒有連上，請稍後再試一次。") }
            return
        }
        let latest = tag.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
        guard isNewer(latest, than: appVersion) else {
            if manual { info("已經是最新版", "你現在用的貼貼 \(appVersion) 就是最新版本。") }
            return
        }
        if !manual && UserDefaults.standard.string(forKey: skippedKey) == latest { return }
        prompt(latest: latest)
    }

    private static func prompt(latest: String) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "貼貼有新版本 \(latest)"
        alert.informativeText = "你現在用的是 \(appVersion)。按「立即更新」，幾秒鐘就裝好，原本的記錄都會留著。\n\n更新完成後，請從選單列的貼貼圖示按「開啟自動貼上權限」，重新打開一次。"
        alert.addButton(withTitle: "立即更新")
        alert.addButton(withTitle: "稍後再說")
        alert.addButton(withTitle: "略過這一版")
        switch alert.runModal() {
        case .alertFirstButtonReturn: install()
        case .alertThirdButtonReturn: UserDefaults.standard.set(latest, forKey: skippedKey)
        default: break
        }
    }

    /// 執行跟安裝教學同一行的安裝指令。它會關掉目前的貼貼、換成新版，再自動打開。
    private static func install() {
        if ProcessInfo.processInfo.environment["TIETIE_UPDATE_DRYRUN"] != nil {
            Toast.show("（測試）會執行更新指令", seconds: 2)
            return
        }
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/bash")
        process.arguments = ["-c", installCommand]
        var env = ProcessInfo.processInfo.environment
        // 裝回原本的位置（應用程式資料夾或個人的應用程式資料夾）
        env["TIETIE_INSTALL_DIR"] = Bundle.main.bundleURL.deletingLastPathComponent().path
        process.environment = env
        do {
            try process.run()
            Toast.show("正在下載新版，貼貼會自動重新打開", seconds: 4)
        } catch {
            info("沒辦法自動更新", "請打開「終端機」，貼上下面這一行再按 Enter：\n\n\(installCommand)")
        }
    }

    private static func info(_ title: String, _ text: String) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = text
        alert.addButton(withTitle: "好")
        alert.runModal()
    }
}
