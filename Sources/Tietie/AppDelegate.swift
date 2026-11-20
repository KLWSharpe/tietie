import AppKit
import Carbon.HIToolbox

let appVersion = "0.5.1"

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let store = Store()
    private lazy var monitor = ClipboardMonitor(store: store)
    private var panel: HistoryPanelController!
    private var settingsWindow: SettingsController!
    private let onboarding = OnboardingController()
    private var aboutWindow = AboutController()
    private let hotKey = HotKey()
    private var statusItem: NSStatusItem!
    private var hotKeyOK = true

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
        updateIcon()

        monitor.onChange = { [weak self] in self?.panel.refreshIfVisible() }
        monitor.start()

        buildWindows()
        registerHotKey()
        installEditMenu()
        writeStatus()

        let args = CommandLine.arguments
        if !Settings.shared.data.onboarded && !args.contains("--no-onboarding") {
            onboarding.show()
        } else {
            LoginItem.apply(Settings.shared.data.launchAtLogin)
        }
        if args.contains("--debug") {
            // 測試用：收到通知就把清單目前的狀態和畫面存到資料夾的 debug 子資料夾
            DistributedNotificationCenter.default().addObserver(forName: Notification.Name("app.tietie.debug.dump"), object: nil, queue: .main) { [weak self] _ in
                guard let self else { return }
                let dir = Paths.home.appendingPathComponent("debug", isDirectory: true)
                try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                var state = self.panel.debugState()
                state["accessibility"] = Paster.trusted
                state["appActive"] = NSApp.isActive
                state["frontmost"] = NSWorkspace.shared.frontmostApplication?.localizedName ?? ""
                if let json = try? JSONSerialization.data(withJSONObject: state, options: [.prettyPrinted, .sortedKeys]) {
                    try? json.write(to: dir.appendingPathComponent("state.json"), options: .atomic)
                }
                if let png = self.panel.debugSnapshot() {
                    try? png.write(to: dir.appendingPathComponent("panel.png"), options: .atomic)
                }
                // 選單列選單目前的樣子（含「語言」子選單和打勾的位置）
                let menu = NSMenu()
                self.menuNeedsUpdate(menu)
                func titles(_ m: NSMenu) -> [Any] {
                    m.items.map { item -> Any in
                        if item.isSeparatorItem { return "---" }
                        let mark = item.state == .on ? "✓ " : ""
                        if let sub = item.submenu { return [mark + item.title: titles(sub)] }
                        return mark + item.title
                    }
                }
                let info: [String: Any] = ["language": Lang.current, "setting": Settings.shared.data.language, "menu": titles(menu)]
                if let json = try? JSONSerialization.data(withJSONObject: info, options: [.prettyPrinted, .sortedKeys]) {
                    try? json.write(to: dir.appendingPathComponent("menu.json"), options: .atomic)
                }
                if let png = self.settingsWindow.renderSnapshot() {
                    try? png.write(to: dir.appendingPathComponent("settings.png"), options: .atomic)
                }
            }
            // 測試用：模擬在「語言」子選單選了某一項（object 是語言代碼或 system）
            DistributedNotificationCenter.default().addObserver(forName: Notification.Name("app.tietie.debug.language"), object: nil, queue: .main) { [weak self] note in
                guard let code = note.object as? String else { return }
                let entry = NSMenuItem()
                entry.representedObject = code
                self?.chooseLanguage(entry)
            }
        }
        if args.contains("--debug") {
            // 測試用：收到通知就打開清單
            DistributedNotificationCenter.default().addObserver(forName: Notification.Name("app.tietie.debug.show"), object: nil, queue: .main) { [weak self] _ in
                self?.panel.show()
            }
        }
        // 測試時（指定 TIETIE_HOME）不自動檢查，除非另外指定了假的更新網址
        if ProcessInfo.processInfo.environment["TIETIE_HOME"] == nil || ProcessInfo.processInfo.environment["TIETIE_UPDATE_URL"] != nil {
            Updater.startAutomaticChecks()
        }
        if args.contains("--show") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in self?.panel.show() }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private func registerHotKey() {
        let choice = HotKeyChoice.find(Settings.shared.data.hotKey)
        hotKeyOK = hotKey.register(keyCode: UInt32(kVK_ANSI_V), modifiers: choice.modifiers) { [weak self] in
            self?.panel.toggle()
        }
        if !hotKeyOK {
            Toast.show(L("toast.hotkeyTaken", choice.label), seconds: 4)
        }
    }

    private func updateIcon() {
        let paused = Settings.shared.data.paused
        // 素色線條圖示，跟著系統深淺色自動變黑或白；暫停時換成暫停符號
        let image = NSImage(systemSymbolName: paused ? "pause.circle" : "doc.on.clipboard", accessibilityDescription: L("app.name"))
        image?.isTemplate = true
        statusItem.button?.image = image
        statusItem.button?.toolTip = Settings.shared.data.paused ? L("app.tooltipPaused") : L("app.name")
    }

    /// 新版 macOS 會管制程式讀取剪貼簿。沒有設成「允許」時要提醒使用者。
    static var pasteboardBlocked: Bool {
        if #available(macOS 15.4, *) {
            let behavior = NSPasteboard.general.accessBehavior
            return behavior == .alwaysDeny || behavior == .ask
        }
        return false
    }

    /// 把目前的狀態寫成一個小檔案，出問題時方便查。
    private func writeStatus() {
        var lines = ["version=\(appVersion)", "accessibility=\(Paster.trusted)", "hotkey_ok=\(hotKeyOK)"]
        if #available(macOS 15.4, *) {
            lines.append("pasteboard_access=\(NSPasteboard.general.accessBehavior.rawValue)  (0 預設 1 每次詢問 2 允許 3 拒絕)")
        }
        try? lines.joined(separator: "\n").write(to: Paths.home.appendingPathComponent("status.txt"), atomically: true, encoding: .utf8)
    }

    /// 讓搜尋框裡的 ⌘V、⌘A、⌘C、⌘X、⌘Z 可以用。
    private func installEditMenu() {
        let main = NSMenu()
        let editItem = NSMenuItem()
        let edit = NSMenu(title: L("mac.edit.title"))
        edit.addItem(withTitle: L("mac.edit.undo"), action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: L("mac.edit.cut"), action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: L("mac.edit.copy"), action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: L("mac.edit.paste"), action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: L("mac.edit.selectAll"), action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)
        NSApp.mainMenu = main
    }

    // MARK: - 貼上

    private func paste(_ item: ClipItem, asText: Bool = false) {
        let count: Int
        if asText {
            let text = item.ocr.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !text.isEmpty else {
                Toast.show(L("toast.noTextInImage"))
                return
            }
            count = Paster.writeText(text)
        } else {
            guard let written = Paster.write(item) else {
                Toast.show(L("toast.imageMissing"))
                return
            }
            count = written
        }
        monitor.ignoreCount = count
        store.touch(item.id)
        // 貼貼自己的視窗（設定、關於）在最前面時，先退到後面，讓原本的視窗接手
        var delay = 0.12
        if NSWorkspace.shared.frontmostApplication?.processIdentifier == ProcessInfo.processInfo.processIdentifier {
            NSApp.hide(nil)
            delay = 0.35
        }
        // TIETIE_NO_AUTOPASTE：測試用，只放進剪貼簿、不送出 ⌘V，避免打到使用者正在用的視窗
        let autoPaste = Paster.trusted && ProcessInfo.processInfo.environment["TIETIE_NO_AUTOPASTE"] == nil
        if autoPaste {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) {
                Paster.sendCommandV()
            }
        } else {
            Toast.show(L("mac.toast.copiedNoAX"), seconds: 3.5)
        }
    }

    // MARK: - 選單

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let data = Settings.shared.data
        let choice = HotKeyChoice.find(data.hotKey)

        let open = NSMenuItem(title: L("menu.open"), action: #selector(openPanel), keyEquivalent: "v")
        open.keyEquivalentModifierMask = modifierMask(for: choice)
        open.target = self
        menu.addItem(open)
        if !hotKeyOK {
            let warn = NSMenuItem(title: L("menu.hotkeyTaken", choice.label), action: nil, keyEquivalent: "")
            warn.isEnabled = false
            menu.addItem(warn)
        }

        menu.addItem(.separator())
        let pause = NSMenuItem(title: L("menu.pause"), action: #selector(togglePause), keyEquivalent: "")
        pause.state = data.paused ? .on : .off
        pause.target = self
        menu.addItem(pause)

        let recent = NSMenuItem(title: L("menu.clearRecent"), action: #selector(clearRecent), keyEquivalent: "")
        recent.target = self
        menu.addItem(recent)
        let all = NSMenuItem(title: L("menu.clearAll"), action: #selector(clearAll), keyEquivalent: "")
        all.target = self
        menu.addItem(all)

        menu.addItem(.separator())
        if !Paster.trusted {
            let ax = NSMenuItem(title: L("mac.menu.enableAX"), action: #selector(openAX), keyEquivalent: "")
            ax.target = self
            menu.addItem(ax)
        }
        if AppDelegate.pasteboardBlocked {
            let deny = NSMenuItem(title: L("mac.menu.pasteboardBlocked"), action: #selector(openPasteSettings), keyEquivalent: "")
            deny.target = self
            menu.addItem(deny)
        }
        let settings = NSMenuItem(title: L("menu.settings"), action: #selector(openSettings), keyEquivalent: ",")
        settings.target = self
        menu.addItem(settings)
        menu.addItem(languageMenuItem())
        let about = NSMenuItem(title: L("menu.about"), action: #selector(showAbout), keyEquivalent: "")
        about.target = self
        menu.addItem(about)
        let update = NSMenuItem(title: L("menu.checkUpdate"), action: #selector(checkUpdate), keyEquivalent: "")
        update.target = self
        menu.addItem(update)

        menu.addItem(.separator())
        let size = sizeText(store.imageBytes())
        let info = NSMenuItem(title: L("menu.stats", store.count(), size), action: nil, keyEquivalent: "")
        info.isEnabled = false
        menu.addItem(info)
        let quit = NSMenuItem(title: L("menu.quit"), action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)
    }

    /// 選單裡的「語言」：跟隨系統，或指定一種語言。改了馬上生效。
    private func languageMenuItem() -> NSMenuItem {
        let setting = Settings.shared.data.language
        // 不是英文時在後面加上 Language，看不懂目前語言的人也找得到
        let title = Lang.current == "en" ? L("menu.language") : "\(L("menu.language"))  Language"
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        let sub = NSMenu()
        let system = NSMenuItem(title: L("menu.languageSystem", Lang.names[Lang.systemLanguage()] ?? ""), action: #selector(chooseLanguage(_:)), keyEquivalent: "")
        system.representedObject = "system"
        system.state = Lang.supported.contains(setting) ? .off : .on
        system.target = self
        sub.addItem(system)
        sub.addItem(.separator())
        for code in Lang.supported {
            let entry = NSMenuItem(title: Lang.names[code] ?? code, action: #selector(chooseLanguage(_:)), keyEquivalent: "")
            entry.representedObject = code
            entry.state = setting == code ? .on : .off
            entry.target = self
            sub.addItem(entry)
        }
        item.submenu = sub
        return item
    }

    @objc private func chooseLanguage(_ sender: NSMenuItem) {
        guard let code = sender.representedObject as? String else { return }
        applyLanguage(code)
        Toast.show(L("toast.languageChanged", Lang.names[Lang.current] ?? Lang.current))
    }

    /// 換語言：存起來，然後把清單、設定、關於視窗用新語言重新做一份。選單每次打開都會重畫，不用另外處理。
    func applyLanguage(_ code: String) {
        Settings.shared.update { $0.language = code }
        Lang.reload()
        let settingsWasOpen = settingsWindow.isVisible
        panel.hide()
        settingsWindow.close()
        buildWindows()
        aboutWindow.close()
        aboutWindow = AboutController()
        installEditMenu()
        updateIcon()
        if settingsWasOpen { settingsWindow.show() }
    }

    private func buildWindows() {
        panel = HistoryPanelController(store: store)
        panel.onPick = { [weak self] item, asText in self?.paste(item, asText: asText) }
        settingsWindow = SettingsController(store: store)
        settingsWindow.onHotKeyChange = { [weak self] in self?.registerHotKey() }
    }

    private func modifierMask(for choice: HotKeyChoice) -> NSEvent.ModifierFlags {
        var mask: NSEvent.ModifierFlags = []
        if choice.modifiers & UInt32(cmdKey) != 0 { mask.insert(.command) }
        if choice.modifiers & UInt32(shiftKey) != 0 { mask.insert(.shift) }
        if choice.modifiers & UInt32(optionKey) != 0 { mask.insert(.option) }
        if choice.modifiers & UInt32(controlKey) != 0 { mask.insert(.control) }
        return mask
    }

    @objc private func openPanel() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { [weak self] in self?.panel.show() }
    }

    @objc private func togglePause() {
        Settings.shared.update { $0.paused.toggle() }
        updateIcon()
        Toast.show(Settings.shared.data.paused ? L("toast.paused") : L("toast.resumed"))
    }

    @objc private func clearRecent() {
        store.clearRecent(seconds: 600)
        panel.refreshIfVisible()
        Toast.show(L("toast.clearedRecent"))
    }

    @objc private func clearAll() {
        store.clearUnpinned()
        panel.refreshIfVisible()
        Toast.show(L("toast.clearedAll"))
    }

    @objc private func openAX() {
        Paster.requestTrust()
        Paster.openAccessibilitySettings()
    }

    @objc private func openPasteSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Pasteboard") {
            NSWorkspace.shared.open(url)
        }
    }

    @objc private func checkUpdate() {
        Updater.check(manual: true)
    }

    @objc private func openSettings() {
        settingsWindow.show()
    }

    @objc private func showAbout() {
        aboutWindow.show()
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }
}
