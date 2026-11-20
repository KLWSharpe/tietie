import AppKit

if CommandLine.arguments.contains("--version") {
    print("Tietie \(appVersion)")
    exit(0)
}

if CommandLine.arguments.contains("--selftest") {
    exit(SelfTest.run())
}

// 檢查版面用：把每個語言的畫面畫成圖片存到指定資料夾，不會在螢幕上跳出任何視窗。
if let i = CommandLine.arguments.firstIndex(of: "--render-ui"), i + 1 < CommandLine.arguments.count {
    guard ProcessInfo.processInfo.environment["TIETIE_HOME"] != nil else {
        print("請先設定 TIETIE_HOME")
        exit(2)
    }
    let root = URL(fileURLWithPath: CommandLine.arguments[i + 1], isDirectory: true)
    _ = NSApplication.shared
    // 加 --light 用淺色外觀出圖（深色模式下出的圖背景是透明的，不好看清楚）
    if CommandLine.arguments.contains("--light") { NSApp.appearance = NSAppearance(named: .aqua) }
    let store = Store()
    // 每個語言各出一組畫面到「資料夾/語言代碼」；最後一組 system 是「跟隨系統」，
    // 可以加上 -AppleLanguages "(ja)" 這類參數模擬別的系統語言。
    var failures = 0
    for code in Lang.supported + ["system"] {
        Settings.shared.update { $0.language = code }
        Lang.reload()
        let dir = root.appendingPathComponent(code, isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        print("== \(code)（實際使用 \(Lang.current)）")
        fillDemo(store, chinese: Lang.current.hasPrefix("zh"))
        let panel = HistoryPanelController(store: store)
        let shots: [(String, Data?)] = [
            ("1_清單.png", panel.renderSnapshot()),
            ("1b_第二頁.png", panel.renderSnapshot(page: 1)),
            ("2_搜尋.png", panel.renderSnapshot(searching: "12345678")),
            ("3_找不到.png", panel.renderSnapshot(searching: "zzz-none")),
            ("4_首次設定.png", OnboardingController().renderSnapshot()),
            ("5_設定.png", SettingsController(store: store).renderSnapshot()),
            ("6_關於.png", AboutController().renderSnapshot()),
        ]
        for (name, data) in shots {
            if let data { try? data.write(to: dir.appendingPathComponent(name)); print("OK  \(code)/\(name)") }
            else { print("FAIL  \(code)/\(name)"); failures += 1 }
        }
    }
    Settings.shared.update { $0.language = "system" }
    exit(failures == 0 ? 0 : 1)
}

/// 放一批示範記錄，讓清單有好幾頁可以看分頁效果。中文介面用中文內容，其他語言用英文內容。
func fillDemo(_ store: Store, chinese: Bool) {
    store.clearUnpinned()
    for item in store.list(search: "", limit: 1000) where item.pinned { store.setPinned(item.id, false) }
    store.clearUnpinned()
    let older: [(String, String)] = chinese ? [
        ("週五前把簡報寄給 Amy", "Slack"), ("Wi-Fi 密碼在冰箱旁邊", "LINE"), ("下週二 10:00 牙醫", "行事曆"),
        ("https://kueilan.com", "Chrome"), ("發票抬頭：貼貼工作室", "Notion"), ("會議室 B 已預約到 4 點", "Outlook"),
        ("新竹市東區光復路一段 100 號", "Google 地圖"), ("請幫我把這段翻成英文", "ChatGPT"), ("報名表下載連結已寄出", "Gmail"),
        ("AB-20261101 已出貨", "蝦皮"), ("記得帶身分證影本", "備忘錄"), ("簡報第 7 頁圖表要換", "Keynote"),
        ("今天晚餐吃咖哩", "LINE"), ("帳單 11/25 到期", "Gmail"), ("Zoom 會議 ID 123 456 7890", "Zoom"),
        ("把新版合約傳給律師", "Slack"), ("下載頁網址要放在最後一張", "Notion"), ("星期六早上去郵局", "備忘錄"),
    ] : [
        ("Send the deck to Amy before Friday", "Slack"), ("Wi-Fi password is on the fridge", "Messages"), ("Dentist next Tuesday 10:00", "Calendar"),
        ("https://kueilan.com", "Chrome"), ("Invoice title: Tietie Studio", "Notion"), ("Room B is booked until 4 pm", "Outlook"),
        ("100 Main Street, Springfield", "Maps"), ("Please translate this paragraph", "ChatGPT"), ("Sign-up form link sent", "Gmail"),
        ("Order AB-20261101 shipped", "Amazon"), ("Bring a copy of your ID", "Notes"), ("Replace the chart on slide 7", "Keynote"),
        ("Curry for dinner tonight", "Messages"), ("Bill due 11/25", "Gmail"), ("Zoom meeting ID 123 456 7890", "Zoom"),
        ("Send the new contract to the lawyer", "Slack"), ("Put the download link on the last slide", "Notion"), ("Post office on Saturday morning", "Notes"),
    ]
    for (text, app) in older { store.addText(text, sourceApp: app) }
    store.addText("https://github.com/KLWSharpe/tietie", sourceApp: "Chrome")
    store.addText(chinese ? "王小明　0912-345-678　台北市信義區松仁路 100 號" : "Alex Chen · +1 555 0123 · 42 Market St, San Francisco", sourceApp: "Notion")
    let png = SelfTest.textImage(chinese ? "訂單編號 AB-12345678" : "Order No. AB-12345678")
    if let id = store.addImage(png: png, width: 900, height: 220, sourceApp: chinese ? "截圖" : "Screenshot") {
        store.setOCR(id: id, text: ClipboardMonitor.ocr(png: png))
    }
    store.addText(String(repeating: chinese ? "客戶回信：報價單收到了，下週一前會確認數量，合約草稿麻煩先準備好，有問題我再打給你。"
                                             : "Client reply: we got the quote and will confirm quantities by Monday. Please prepare the draft contract. ", count: 4), sourceApp: "Gmail")
    store.addText(chinese ? "/Users/demo/Documents/報價單.pdf\n/Users/demo/Documents/合約.pdf" : "/Users/demo/Documents/Quote.pdf\n/Users/demo/Documents/Contract.pdf", kind: "file", sourceApp: "Finder")
    store.addText(chinese ? "下午三點開會，記得帶筆電" : "Meeting at 3 pm, bring the laptop", sourceApp: chinese ? "LINE" : "Messages")
    if let id = store.addText("kueilan@example.com", sourceApp: "Mail") { store.setPinned(id, true) }
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
