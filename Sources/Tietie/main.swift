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
    // 較早的記錄，讓清單有好幾頁可以看分頁效果
    let older: [(String, String)] = [
        ("週五前把簡報寄給 Amy", "Slack"), ("Wi-Fi 密碼在冰箱旁邊", "LINE"), ("下週二 10:00 牙醫", "行事曆"),
        ("https://kueilan.com", "Chrome"), ("發票抬頭：貼貼工作室", "Notion"), ("會議室 B 已預約到 4 點", "Outlook"),
        ("新竹市東區光復路一段 100 號", "Google 地圖"), ("請幫我把這段翻成英文", "ChatGPT"), ("報名表下載連結已寄出", "Gmail"),
        ("AB-20261101 已出貨", "蝦皮"), ("記得帶身分證影本", "備忘錄"), ("簡報第 7 頁圖表要換", "Keynote"),
        ("今天晚餐吃咖哩", "LINE"), ("帳單 11/25 到期", "Gmail"), ("Zoom 會議 ID 123 456 7890", "Zoom"),
        ("把新版合約傳給律師", "Slack"), ("下載頁網址要放在最後一張", "Notion"), ("星期六早上去郵局", "備忘錄"),
    ]
    for (text, app) in older { store.addText(text, sourceApp: app) }
    store.addText("https://github.com/KLWSharpe/tietie", sourceApp: "Chrome")
    store.addText("王小明　0912-345-678　台北市信義區松仁路 100 號", sourceApp: "Notion")
    let png = SelfTest.textImage("訂單編號 AB-12345678")
    if let id = store.addImage(png: png, width: 900, height: 220, sourceApp: "截圖") {
        store.setOCR(id: id, text: ClipboardMonitor.ocr(png: png))
    }
    store.addText(String(repeating: "客戶回信：報價單收到了，下週一前會確認數量，合約草稿麻煩先準備好，有問題我再打給你。", count: 4), sourceApp: "Gmail")
    store.addText("/Users/demo/Documents/報價單.pdf\n/Users/demo/Documents/合約.pdf", kind: "file", sourceApp: "Finder")
    store.addText("下午三點開會，記得帶筆電", sourceApp: "LINE")
    if let id = store.addText("kueilan@example.com", sourceApp: "Mail") { store.setPinned(id, true) }
    let panel = HistoryPanelController(store: store)
    let shots: [(String, Data?)] = [
        ("1_清單.png", panel.renderSnapshot()),
        ("1b_第二頁.png", panel.renderSnapshot(page: 1)),
        ("2_搜尋發票.png", panel.renderSnapshot(searching: "12345678")),
        ("3_找不到.png", panel.renderSnapshot(searching: "不存在的字")),
        ("4_首次設定.png", OnboardingController().renderSnapshot()),
        ("5_設定.png", SettingsController(store: store).renderSnapshot()),
        ("6_關於.png", AboutController().renderSnapshot()),
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
