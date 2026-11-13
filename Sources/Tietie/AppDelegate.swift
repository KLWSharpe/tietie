import AppKit
import Carbon.HIToolbox

let appVersion = "0.4.0"

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let store = Store()
    private lazy var monitor = ClipboardMonitor(store: store)
    private lazy var panel = HistoryPanelController(store: store)
    private lazy var settingsWindow = SettingsController(store: store)
    private let onboarding = OnboardingController()
    private let aboutWindow = AboutController()
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

        panel.onPick = { [weak self] item, asText in self?.paste(item, asText: asText) }
        settingsWindow.onHotKeyChange = { [weak self] in self?.registerHotKey() }
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
            Toast.show("快捷鍵 \(choice.label) 被別的程式佔用了，請到貼貼的設定換一組", seconds: 4)
        }
    }

    private func updateIcon() {
        let paused = Settings.shared.data.paused
        // 素色線條圖示，跟著系統深淺色自動變黑或白；暫停時換成暫停符號
        let image = NSImage(systemSymbolName: paused ? "pause.circle" : "doc.on.clipboard", accessibilityDescription: "貼貼")
        image?.isTemplate = true
        statusItem.button?.image = image
        statusItem.button?.toolTip = Settings.shared.data.paused ? "貼貼（已暫停記錄）" : "貼貼"
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
        let edit = NSMenu(title: "編輯")
        edit.addItem(withTitle: "還原", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "剪下", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "拷貝", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "貼上", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "全選", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
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
                Toast.show("這張圖片裡沒有讀到文字")
                return
            }
            count = Paster.writeText(text)
        } else {
            guard let written = Paster.write(item) else {
                Toast.show("這張圖片的檔案不見了，沒辦法貼上")
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
            Toast.show("已複製，按 ⌘V 貼上。想要自動貼上，請到右上角貼貼選單開啟權限", seconds: 3.5)
        }
    }

    // MARK: - 選單

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let data = Settings.shared.data
        let choice = HotKeyChoice.find(data.hotKey)

        let open = NSMenuItem(title: "開啟貼貼清單", action: #selector(openPanel), keyEquivalent: "v")
        open.keyEquivalentModifierMask = modifierMask(for: choice)
        open.target = self
        menu.addItem(open)
        if !hotKeyOK {
            let warn = NSMenuItem(title: "快捷鍵 \(choice.label) 被佔用，請到設定更換", action: nil, keyEquivalent: "")
            warn.isEnabled = false
            menu.addItem(warn)
        }

        menu.addItem(.separator())
        let pause = NSMenuItem(title: "暫停記錄", action: #selector(togglePause), keyEquivalent: "")
        pause.state = data.paused ? .on : .off
        pause.target = self
        menu.addItem(pause)

        let recent = NSMenuItem(title: "清除最近 10 分鐘的記錄", action: #selector(clearRecent), keyEquivalent: "")
        recent.target = self
        menu.addItem(recent)
        let all = NSMenuItem(title: "清除全部歷史記錄（保留釘選）", action: #selector(clearAll), keyEquivalent: "")
        all.target = self
        menu.addItem(all)

        menu.addItem(.separator())
        if !Paster.trusted {
            let ax = NSMenuItem(title: "開啟自動貼上權限…", action: #selector(openAX), keyEquivalent: "")
            ax.target = self
            menu.addItem(ax)
        }
        if AppDelegate.pasteboardBlocked {
            let deny = NSMenuItem(title: "系統沒有允許貼貼讀取剪貼簿，點此到系統設定允許…", action: #selector(openPasteSettings), keyEquivalent: "")
            deny.target = self
            menu.addItem(deny)
        }
        let settings = NSMenuItem(title: "設定…", action: #selector(openSettings), keyEquivalent: ",")
        settings.target = self
        menu.addItem(settings)
        let about = NSMenuItem(title: "關於貼貼", action: #selector(showAbout), keyEquivalent: "")
        about.target = self
        menu.addItem(about)
        let update = NSMenuItem(title: "檢查更新…", action: #selector(checkUpdate), keyEquivalent: "")
        update.target = self
        menu.addItem(update)

        menu.addItem(.separator())
        let size = ByteCountFormatter.string(fromByteCount: store.imageBytes(), countStyle: .file)
        let info = NSMenuItem(title: "已記錄 \(store.count()) 筆 · 圖片 \(size)", action: nil, keyEquivalent: "")
        info.isEnabled = false
        menu.addItem(info)
        let quit = NSMenuItem(title: "結束貼貼", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)
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
        Toast.show(Settings.shared.data.paused ? "已暫停記錄" : "恢復記錄")
    }

    @objc private func clearRecent() {
        store.clearRecent(seconds: 600)
        panel.refreshIfVisible()
        Toast.show("已清除最近 10 分鐘的記錄")
    }

    @objc private func clearAll() {
        store.clearUnpinned()
        panel.refreshIfVisible()
        Toast.show("已清除歷史記錄")
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
