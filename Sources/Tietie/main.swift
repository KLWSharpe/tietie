import AppKit

if CommandLine.arguments.contains("--version") {
    print("Tietie \(appVersion)")
    exit(0)
}

if CommandLine.arguments.contains("--selftest") {
    exit(SelfTest.run())
}

// 檢查版面用：把三個畫面畫成圖片存到指定資料夾，不會在螢幕上跳出任何視窗。
if let i = CommandLine.arguments.firstIndex(of: "--render-ui"), i + 1 < CommandLine.arguments.count {
    guard ProcessInfo.processInfo.environment["TIETIE_HOME"] != nil else {
        print("請先設定 TIETIE_HOME")
        exit(2)
    }
    let dir = URL(fileURLWithPath: CommandLine.arguments[i + 1], isDirectory: true)
    try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    _ = NSApplication.shared
    let store = Store()
    store.clearUnpinned()
    store.addText("https://github.com/KLWSharpe/tietie", sourceApp: "Chrome")
    store.addText("王小明　0912-345-678　台北市信義區松仁路 100 號", sourceApp: "Notion")
    let png = SelfTest.textImage("發票號碼 AB-12345678")
    if let id = store.addImage(png: png, width: 900, height: 220, sourceApp: "截圖") {
        store.setOCR(id: id, text: ClipboardMonitor.ocr(png: png))
    }
    store.addText(String(repeating: "這是一段很長很長的文字，用來確認清單裡太長的內容會被截斷而不是把版面撐壞。", count: 6), sourceApp: "Pages")
    store.addText("/Users/demo/Documents/報價單.pdf\n/Users/demo/Documents/合約.pdf", kind: "file", sourceApp: "Finder")
    store.addText("下午三點開會，記得帶筆電", sourceApp: "LINE")
    if let id = store.addText("kueilan@example.com", sourceApp: "Mail") { store.setPinned(id, true) }
    let panel = HistoryPanelController(store: store)
    let shots: [(String, Data?)] = [
        ("1_清單.png", panel.renderSnapshot()),
        ("2_搜尋發票.png", panel.renderSnapshot(searching: "12345678")),
        ("3_找不到.png", panel.renderSnapshot(searching: "不存在的字")),
        ("4_首次設定.png", OnboardingController().renderSnapshot()),
        ("5_設定.png", SettingsController(store: store).renderSnapshot()),
    ]
    for (name, data) in shots {
        if let data { try? data.write(to: dir.appendingPathComponent(name)); print("OK  \(name)") } else { print("FAIL  \(name)") }
    }
    exit(0)
}

// 已經有一個貼貼在跑就不要再開第二個
if let bundleID = Bundle.main.bundleIdentifier,
   NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).count > 1 {
    exit(0)
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
