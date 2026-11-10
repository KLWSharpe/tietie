import AppKit
import ServiceManagement

// MARK: - 登入時自動啟動

enum LoginItem {
    private static let label = "app.tietie.Tietie"
    private static var agentURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents/\(label).plist")
    }

    /// 只有從 .app 啟動、而且不是測試模式時才真的去動系統設定。
    static var available: Bool {
        Bundle.main.bundlePath.hasSuffix(".app") && ProcessInfo.processInfo.environment["TIETIE_HOME"] == nil
    }

    static func apply(_ on: Bool) {
        guard available else { return }
        do {
            if on {
                if SMAppService.mainApp.status != .enabled { try SMAppService.mainApp.register() }
            } else {
                if SMAppService.mainApp.status == .enabled { try SMAppService.mainApp.unregister() }
            }
            try? FileManager.default.removeItem(at: agentURL)
            return
        } catch {
            NSLog("Tietie: 系統登入項目設定失敗，改用備用方式 \(error.localizedDescription)")
        }
        // 備用方式：自己放一個啟動設定檔
        if on, let exe = Bundle.main.executablePath {
            let plist: [String: Any] = ["Label": label, "ProgramArguments": [exe], "RunAtLoad": true]
            try? FileManager.default.createDirectory(at: agentURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            if let data = try? PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0) {
                try? data.write(to: agentURL, options: .atomic)
            }
        } else {
            try? FileManager.default.removeItem(at: agentURL)
        }
    }
}

// MARK: - 小提示

enum Toast {
    private static var window: NSPanel?

    static func show(_ message: String, seconds: Double = 2.2) {
        window?.orderOut(nil)
        let label = NSTextField(labelWithString: message)
        label.font = .systemFont(ofSize: 13, weight: .medium)
        label.textColor = .white
        label.alignment = .center
        label.sizeToFit()
        let size = NSSize(width: label.frame.width + 36, height: 38)
        let panel = NSPanel(contentRect: NSRect(origin: .zero, size: size),
                            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.level = .statusBar
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .transient]
        let box = NSView(frame: NSRect(origin: .zero, size: size))
        box.wantsLayer = true
        box.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.82).cgColor
        box.layer?.cornerRadius = 10
        label.frame = NSRect(x: 18, y: (size.height - label.frame.height) / 2, width: label.frame.width, height: label.frame.height)
        box.addSubview(label)
        panel.contentView = box
        if let visible = NSScreen.main?.visibleFrame {
            panel.setFrameOrigin(NSPoint(x: visible.midX - size.width / 2, y: visible.minY + 90))
        }
        panel.orderFrontRegardless()
        window = panel
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds) {
            if window === panel { panel.orderOut(nil); window = nil }
        }
    }
}

// MARK: - 共用的小工具

private func label(_ text: String, size: CGFloat = 13, weight: NSFont.Weight = .regular, color: NSColor = .labelColor) -> NSTextField {
    let field = NSTextField(wrappingLabelWithString: text)
    field.font = .systemFont(ofSize: size, weight: weight)
    field.textColor = color
    field.isSelectable = false
    return field
}

private func makeWindow(title: String, width: CGFloat, content: NSView) -> NSWindow {
    let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: width, height: 100),
                          styleMask: [.titled, .closable], backing: .buffered, defer: false)
    window.title = title
    window.isReleasedWhenClosed = false
    let holder = NSView()
    content.translatesAutoresizingMaskIntoConstraints = false
    holder.addSubview(content)
    NSLayoutConstraint.activate([
        content.topAnchor.constraint(equalTo: holder.topAnchor, constant: 22),
        content.bottomAnchor.constraint(equalTo: holder.bottomAnchor, constant: -22),
        content.leadingAnchor.constraint(equalTo: holder.leadingAnchor, constant: 26),
        content.trailingAnchor.constraint(equalTo: holder.trailingAnchor, constant: -26),
        holder.widthAnchor.constraint(equalToConstant: width),
    ])
    window.contentView = holder
    window.setContentSize(holder.fittingSize)
    window.center()
    return window
}

// MARK: - 第一次開啟的設定

final class OnboardingController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private let recordAll = NSButton(radioButtonWithTitle: "全部都記，包含從密碼管理器複製的帳號密碼", target: nil, action: nil)
    private let skipSecret = NSButton(radioButtonWithTitle: "密碼管理器複製的內容不要記", target: nil, action: nil)
    private let loginCheck = NSButton(checkboxWithTitle: "開機後自動啟動貼貼", target: nil, action: nil)
    private let axStatus = NSTextField(labelWithString: "")
    private var timer: Timer?
    var onFinish: (() -> Void)?

    func show() {
        if window == nil { build() }
        refreshStatus()
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in self?.refreshStatus() }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func renderSnapshot() -> Data? {
        if window == nil { build() }
        refreshStatus()
        return window.flatMap { snapshot(of: $0) }
    }

    private func build() {
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10

        stack.addArrangedSubview(label("歡迎使用貼貼 Tietie", size: 20, weight: .semibold))
        stack.addArrangedSubview(label("貼貼會記住你複製過的文字、圖片和檔案。按 \(HotKeyChoice.find(Settings.shared.data.hotKey).label) 叫出清單，選一筆就貼上，貼出來的文字不帶格式。\n所有記錄只存在這台電腦，不會上傳到任何地方。", color: .secondaryLabelColor))
        stack.setCustomSpacing(20, after: stack.arrangedSubviews.last!)

        stack.addArrangedSubview(label("1. 要不要記錄密碼？", weight: .semibold))
        recordAll.target = self; recordAll.action = #selector(radioChanged)
        skipSecret.target = self; skipSecret.action = #selector(radioChanged)
        recordAll.state = Settings.shared.data.recordConcealed ? .on : .off
        skipSecret.state = Settings.shared.data.recordConcealed ? .off : .on
        stack.addArrangedSubview(recordAll)
        stack.addArrangedSubview(skipSecret)
        stack.addArrangedSubview(label("記錄會留在這台電腦上，能打開這台電腦的人就看得到。如果電腦會借人或多人共用，建議選第二個。貼完敏感內容後，也可以從右上角選單一鍵清除。之後可以在「設定」裡更改。", size: 11.5, color: .secondaryLabelColor))
        stack.setCustomSpacing(20, after: stack.arrangedSubviews.last!)

        stack.addArrangedSubview(label("2. 開啟自動貼上", weight: .semibold))
        stack.addArrangedSubview(label("要讓貼貼在你選好之後直接幫你貼上，需要在系統設定的「輔助使用」把貼貼打開。沒有開也能用，只是選好之後要自己按一次 ⌘V。", size: 11.5, color: .secondaryLabelColor))
        let axButton = NSButton(title: "開啟權限設定…", target: self, action: #selector(openAX))
        let axRow = NSStackView(views: [axButton, axStatus])
        axRow.spacing = 10
        axStatus.font = .systemFont(ofSize: 12)
        stack.addArrangedSubview(axRow)
        stack.setCustomSpacing(20, after: axRow)

        stack.addArrangedSubview(label("3. 自動啟動", weight: .semibold))
        loginCheck.state = Settings.shared.data.launchAtLogin ? .on : .off
        stack.addArrangedSubview(loginCheck)
        stack.setCustomSpacing(24, after: loginCheck)

        let done = NSButton(title: "開始使用", target: self, action: #selector(finish))
        done.keyEquivalent = "\r"
        done.bezelStyle = .rounded
        done.controlSize = .large
        stack.addArrangedSubview(done)

        let w = makeWindow(title: "貼貼 Tietie", width: 500, content: stack)
        w.delegate = self
        window = w
    }

    @objc private func radioChanged(_ sender: NSButton) {
        // 兩個單選鈕不在同一個容器裡時要自己互斥
        recordAll.state = sender === recordAll ? .on : .off
        skipSecret.state = sender === skipSecret ? .on : .off
    }

    @objc private func openAX() {
        Paster.requestTrust()
        Paster.openAccessibilitySettings()
    }

    private func refreshStatus() {
        if Paster.trusted {
            axStatus.stringValue = "已開啟"
            axStatus.textColor = .systemGreen
        } else {
            axStatus.stringValue = "尚未開啟"
            axStatus.textColor = .secondaryLabelColor
        }
    }

    @objc private func finish() {
        save()
        window?.close()
    }

    private func save() {
        let record = recordAll.state == .on
        let login = loginCheck.state == .on
        Settings.shared.update {
            $0.recordConcealed = record
            $0.launchAtLogin = login
            $0.onboarded = true
        }
        LoginItem.apply(login)
    }

    func windowWillClose(_ notification: Notification) {
        timer?.invalidate()
        timer = nil
        if !Settings.shared.data.onboarded { save() }
        onFinish?()
        NSApp.hide(nil)
    }
}

// MARK: - 設定

final class SettingsController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private let hotKeyPopup = NSPopUpButton()
    private let maxPopup = NSPopUpButton()
    private let capPopup = NSPopUpButton()
    private let concealedCheck = NSButton(checkboxWithTitle: "記錄密碼管理器複製的帳號密碼", target: nil, action: nil)
    private let loginCheck = NSButton(checkboxWithTitle: "開機後自動啟動貼貼", target: nil, action: nil)
    private let axStatus = NSTextField(labelWithString: "")
    private let usage = NSTextField(labelWithString: "")
    private var timer: Timer?
    private let store: Store
    var onHotKeyChange: (() -> Void)?

    private let maxChoices = [200, 500, 1000, 3000, 10000]
    private let capChoices = [(500, "500 MB"), (1024, "1 GB"), (2048, "2 GB"), (5120, "5 GB"), (10240, "10 GB")]

    init(store: Store) {
        self.store = store
    }

    func show() {
        if window == nil { build() }
        sync()
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in self?.refreshStatus() }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func renderSnapshot() -> Data? {
        if window == nil { build() }
        sync()
        return window.flatMap { snapshot(of: $0) }
    }

    private func build() {
        for choice in HotKeyChoice.all { hotKeyPopup.addItem(withTitle: choice.label) }
        for n in maxChoices { maxPopup.addItem(withTitle: "\(n) 筆") }
        for (_, title) in capChoices { capPopup.addItem(withTitle: title) }
        for control in [hotKeyPopup, maxPopup, capPopup, concealedCheck, loginCheck] as [NSControl] {
            control.target = self
            control.action = #selector(changed)
        }

        let grid = NSGridView(views: [
            [label("叫出清單的快捷鍵"), hotKeyPopup],
            [label("最多保留"), maxPopup],
            [label("圖片最多佔用"), capPopup],
        ])
        grid.rowSpacing = 10
        grid.columnSpacing = 14
        grid.column(at: 0).xPlacement = .trailing
        grid.rowAlignment = .firstBaseline

        let axButton = NSButton(title: "開啟權限設定…", target: self, action: #selector(openAX))
        let axRow = NSStackView(views: [label("自動貼上權限"), axStatus, axButton])
        axRow.spacing = 10
        axStatus.font = .systemFont(ofSize: 13)

        usage.font = .systemFont(ofSize: 11.5)
        usage.textColor = .secondaryLabelColor

        let stack = NSStackView(views: [
            grid,
            label("超過上限時，會自動刪掉最舊的記錄。釘選的不會被刪。", size: 11.5, color: .secondaryLabelColor),
            concealedCheck,
            label("記錄只存在這台電腦，不會上傳。電腦會借人或多人共用時，建議取消勾選。", size: 11.5, color: .secondaryLabelColor),
            loginCheck,
            axRow,
            usage,
        ])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 12
        stack.setCustomSpacing(4, after: grid)
        stack.setCustomSpacing(4, after: concealedCheck)

        let w = makeWindow(title: "貼貼設定", width: 440, content: stack)
        w.delegate = self
        window = w
    }

    private func sync() {
        let data = Settings.shared.data
        hotKeyPopup.selectItem(at: HotKeyChoice.all.firstIndex { $0.id == data.hotKey } ?? 0)
        maxPopup.selectItem(at: maxChoices.firstIndex(of: data.maxItems) ?? 2)
        capPopup.selectItem(at: capChoices.firstIndex { $0.0 == data.imageCapMB } ?? 2)
        concealedCheck.state = data.recordConcealed ? .on : .off
        loginCheck.state = data.launchAtLogin ? .on : .off
        refreshStatus()
    }

    private func refreshStatus() {
        axStatus.stringValue = Paster.trusted ? "已開啟" : "尚未開啟"
        axStatus.textColor = Paster.trusted ? .systemGreen : .systemOrange
        let size = ByteCountFormatter.string(fromByteCount: store.imageBytes(), countStyle: .file)
        usage.stringValue = "目前共 \(store.count()) 筆記錄，圖片佔用 \(size)。"
    }

    @objc private func changed() {
        let oldHotKey = Settings.shared.data.hotKey
        let oldLogin = Settings.shared.data.launchAtLogin
        // 調低上限會刪掉舊記錄，先問一聲
        let newMax = maxChoices[max(0, maxPopup.indexOfSelectedItem)]
        let newCap = Int64(capChoices[max(0, capPopup.indexOfSelectedItem)].0) * 1024 * 1024
        let overCount = newMax < Settings.shared.data.maxItems && store.count() > newMax
        let overCap = newCap < Int64(Settings.shared.data.imageCapMB) * 1024 * 1024 && store.imageBytes() > newCap
        if overCount || overCap {
            let alert = NSAlert()
            alert.messageText = "調低上限會刪掉最舊的記錄"
            alert.informativeText = "超過新上限的舊記錄會馬上刪除，沒辦法復原。釘選的不會被刪。"
            alert.addButton(withTitle: "取消")
            alert.addButton(withTitle: "調低並刪除")
            if alert.runModal() == .alertFirstButtonReturn {
                sync()
                return
            }
        }
        Settings.shared.update {
            $0.hotKey = HotKeyChoice.all[max(0, hotKeyPopup.indexOfSelectedItem)].id
            $0.maxItems = maxChoices[max(0, maxPopup.indexOfSelectedItem)]
            $0.imageCapMB = capChoices[max(0, capPopup.indexOfSelectedItem)].0
            $0.recordConcealed = concealedCheck.state == .on
            $0.launchAtLogin = loginCheck.state == .on
        }
        store.prune()
        if Settings.shared.data.hotKey != oldHotKey { onHotKeyChange?() }
        if Settings.shared.data.launchAtLogin != oldLogin { LoginItem.apply(Settings.shared.data.launchAtLogin) }
        refreshStatus()
    }

    @objc private func openAX() {
        Paster.requestTrust()
        Paster.openAccessibilitySettings()
    }

    func windowWillClose(_ notification: Notification) {
        timer?.invalidate()
        timer = nil
        NSApp.hide(nil)
    }
}
