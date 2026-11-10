import AppKit

/// 資料位置。測試時可用環境變數 TIETIE_HOME 指到別的資料夾，不會碰到正式資料。
enum Paths {
    static let home: URL = {
        if let custom = ProcessInfo.processInfo.environment["TIETIE_HOME"], !custom.isEmpty {
            return URL(fileURLWithPath: custom, isDirectory: true)
        }
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("Tietie", isDirectory: true)
    }()
    static var db: URL { home.appendingPathComponent("history.db") }
    static var images: URL { home.appendingPathComponent("images", isDirectory: true) }
    static var settings: URL { home.appendingPathComponent("settings.json") }

    static func prepare() {
        try? FileManager.default.createDirectory(at: images, withIntermediateDirectories: true)
        // 只有自己讀得到
        try? FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: home.path)
    }
}

/// 貼貼使用的剪貼簿。測試時可用 TIETIE_PASTEBOARD 指到一個私有的剪貼簿，不會動到使用者正在用的那一個。
enum Clip {
    static let board: NSPasteboard = {
        if let name = ProcessInfo.processInfo.environment["TIETIE_PASTEBOARD"], !name.isEmpty {
            return NSPasteboard(name: NSPasteboard.Name(name))
        }
        return NSPasteboard.general
    }()
}

struct HotKeyChoice {
    let id: String
    let label: String
    let modifiers: UInt32   // Carbon modifiers
    static let all: [HotKeyChoice] = [
        HotKeyChoice(id: "shift-cmd-v", label: "⇧⌘V", modifiers: UInt32(0x0100 | 0x0200)),   // cmdKey | shiftKey
        HotKeyChoice(id: "opt-cmd-v", label: "⌥⌘V", modifiers: UInt32(0x0100 | 0x0800)),     // cmdKey | optionKey
        HotKeyChoice(id: "ctrl-cmd-v", label: "⌃⌘V", modifiers: UInt32(0x0100 | 0x1000)),    // cmdKey | controlKey
    ]
    static func find(_ id: String) -> HotKeyChoice { all.first { $0.id == id } ?? all[0] }
}

struct SettingsData: Codable {
    var onboarded = false
    var recordConcealed = false     // 是否記錄密碼管理器標記為機密的內容（第一次開啟時由使用者選）
    var maxItems = 1000             // 沒釘選的最多留幾筆
    var imageCapMB = 2048           // 圖片總容量上限
    var paused = false
    var launchAtLogin = true
    var hotKey = "shift-cmd-v"

    init() {}
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        onboarded = (try? c.decode(Bool.self, forKey: .onboarded)) ?? false
        recordConcealed = (try? c.decode(Bool.self, forKey: .recordConcealed)) ?? false
        maxItems = (try? c.decode(Int.self, forKey: .maxItems)) ?? 1000
        imageCapMB = (try? c.decode(Int.self, forKey: .imageCapMB)) ?? 2048
        paused = (try? c.decode(Bool.self, forKey: .paused)) ?? false
        launchAtLogin = (try? c.decode(Bool.self, forKey: .launchAtLogin)) ?? true
        hotKey = (try? c.decode(String.self, forKey: .hotKey)) ?? "shift-cmd-v"
    }
}

final class Settings {
    static let shared = Settings()
    private(set) var data = SettingsData()

    private init() {
        Paths.prepare()
        if let raw = try? Data(contentsOf: Paths.settings),
           let decoded = try? JSONDecoder().decode(SettingsData.self, from: raw) {
            data = decoded
        }
    }

    func update(_ change: (inout SettingsData) -> Void) {
        change(&data)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let raw = try? encoder.encode(data) {
            try? raw.write(to: Paths.settings, options: .atomic)
        }
    }
}
