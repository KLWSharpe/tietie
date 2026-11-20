import Foundation

/// 介面語言。文字放在 i18n/<語言>.json，組裝時會放進 Tietie.app/Contents/Resources/i18n/。
/// 設定是「system」時跟著系統語言走，系統語言沒有支援就用英文。
enum Lang {
    static let supported = ["zh-Hant", "en", "zh-Hans", "ja", "ko", "pt", "es"]
    /// 每個語言用自己的文字寫自己的名字，看不懂目前語言的人也找得到自己的語言
    static let names: [String: String] = [
        "zh-Hant": "繁體中文", "en": "English", "zh-Hans": "简体中文",
        "ja": "日本語", "ko": "한국어", "pt": "Português", "es": "Español",
    ]
    static let fallback = "en"

    /// 把系統給的語言代碼（例如 zh-Hant-TW、zh-HK、pt-BR、ja-JP）對應到支援的語言，沒有就回傳 nil。
    static func match(_ identifier: String) -> String? {
        let id = identifier.replacingOccurrences(of: "_", with: "-").lowercased()
        let parts = id.split(separator: "-").map(String.init)
        guard let primary = parts.first else { return nil }
        switch primary {
        case "zh":
            if parts.contains("hant") || parts.contains("tw") || parts.contains("hk") || parts.contains("mo") { return "zh-Hant" }
            return "zh-Hans"
        case "en", "ja", "ko", "pt", "es":
            return primary
        default:
            return nil
        }
    }

    /// 依照設定和系統的偏好語言順序決定要用哪個語言。
    static func resolve(setting: String, preferred: [String]) -> String {
        if supported.contains(setting) { return setting }
        return systemLanguage(preferred)
    }

    /// 照系統偏好語言的順序找第一個支援的語言（跟 macOS 一樣的挑法），都沒有就用英文。
    static func systemLanguage(_ preferred: [String] = Locale.preferredLanguages) -> String {
        for id in preferred {
            if let code = match(id) { return code }
        }
        return fallback
    }

    private(set) static var current = resolve(setting: Settings.shared.data.language, preferred: Locale.preferredLanguages)
    private static var tables: [String: [String: String]] = [:]

    /// 設定改了之後重新決定語言。
    static func reload() {
        current = resolve(setting: Settings.shared.data.language, preferred: Locale.preferredLanguages)
    }

    static var locale: Locale { Locale(identifier: current) }

    /// 語言檔所在的資料夾：.app 裡的 Resources/i18n；直接跑 swift build 出來的執行檔時，找專案裡的 i18n。
    static var directory: URL {
        if let res = Bundle.main.resourceURL?.appendingPathComponent("i18n", isDirectory: true),
           FileManager.default.fileExists(atPath: res.appendingPathComponent("en.json").path) {
            return res
        }
        // 從執行檔的位置往上找（例如 .build/arm64-apple-macosx/release/Tietie 往上找到專案裡的 i18n）
        var dir = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath().deletingLastPathComponent()
        for _ in 0..<6 {
            let candidate = dir.appendingPathComponent("i18n", isDirectory: true)
            if FileManager.default.fileExists(atPath: candidate.appendingPathComponent("en.json").path) { return candidate }
            dir = dir.deletingLastPathComponent()
        }
        return dir.appendingPathComponent("i18n", isDirectory: true)
    }

    static func table(_ code: String) -> [String: String] {
        if let cached = tables[code] { return cached }
        let url = directory.appendingPathComponent("\(code).json")
        let loaded = (try? Data(contentsOf: url))
            .flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: String] } ?? [:]
        if !loaded.isEmpty { tables[code] = loaded }
        return loaded
    }

    /// 取出一句介面文字。{0}、{1} 依序換成後面的參數。目前語言沒有這一句就用英文。
    static func text(_ key: String, _ args: [CustomStringConvertible] = [], language: String? = nil) -> String {
        let code = language ?? current
        var value = table(code)[key] ?? table(fallback)[key] ?? key
        for (i, arg) in args.enumerated() {
            value = value.replacingOccurrences(of: "{\(i)}", with: arg.description)
        }
        return value
    }
}

/// 簡寫：L("menu.quit")、L("menu.stats", 12, "3 MB")
func L(_ key: String, _ args: CustomStringConvertible...) -> String {
    Lang.text(key, args)
}

/// 檔案大小（例如 3.2 MB）。用數字寫，不會出現「Zero KB」這種只有英文的字。
func sizeText(_ bytes: Int64) -> String {
    let f = ByteCountFormatter()
    f.countStyle = .file
    f.allowsNonnumericFormatting = false
    return f.string(fromByteCount: bytes)
}
