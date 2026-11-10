import AppKit

final class KeyPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}

/// 選取列永遠用醒目的顏色（清單本身不是輸入焦點，預設會變灰）。
final class EmphasizedRowView: NSTableRowView {
    override var isEmphasized: Bool {
        get { true }
        set {}
    }
}

final class ItemCell: NSTableCellView {
    let icon = NSImageView()
    let title = NSTextField(labelWithString: "")
    let sub = NSTextField(labelWithString: "")
    let badge = NSTextField(labelWithString: "")
    let pin = NSImageView()
    private var iconWidth: NSLayoutConstraint!
    private var iconHeight: NSLayoutConstraint!

    override init(frame: NSRect) {
        super.init(frame: frame)
        icon.imageScaling = .scaleProportionallyUpOrDown
        icon.wantsLayer = true
        icon.layer?.cornerRadius = 5
        icon.layer?.masksToBounds = true

        title.font = .systemFont(ofSize: 13)
        title.lineBreakMode = .byTruncatingTail
        title.maximumNumberOfLines = 1
        title.cell?.truncatesLastVisibleLine = true

        sub.font = .systemFont(ofSize: 10.5)
        sub.textColor = .secondaryLabelColor
        sub.lineBreakMode = .byTruncatingTail

        badge.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular)
        badge.textColor = .secondaryLabelColor
        badge.alignment = .right

        pin.image = NSImage(systemSymbolName: "pin.fill", accessibilityDescription: "已釘選")
        pin.contentTintColor = .systemOrange
        pin.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 10, weight: .regular)

        for v in [icon, title, sub, badge, pin] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            addSubview(v)
        }
        iconWidth = icon.widthAnchor.constraint(equalToConstant: 26)
        iconHeight = icon.heightAnchor.constraint(equalToConstant: 26)
        title.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        sub.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        badge.setContentHuggingPriority(.required, for: .horizontal)
        badge.setContentCompressionResistancePriority(.required, for: .horizontal)

        NSLayoutConstraint.activate([
            icon.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 6),
            icon.centerYAnchor.constraint(equalTo: centerYAnchor),
            iconWidth,
            iconHeight,

            title.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 9),
            title.trailingAnchor.constraint(lessThanOrEqualTo: pin.leadingAnchor, constant: -6),
            title.bottomAnchor.constraint(equalTo: centerYAnchor, constant: 1),

            sub.leadingAnchor.constraint(equalTo: title.leadingAnchor),
            sub.trailingAnchor.constraint(lessThanOrEqualTo: pin.leadingAnchor, constant: -6),
            sub.topAnchor.constraint(equalTo: centerYAnchor, constant: 2),

            badge.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8),
            badge.centerYAnchor.constraint(equalTo: centerYAnchor),
            badge.widthAnchor.constraint(greaterThanOrEqualToConstant: 24),

            pin.trailingAnchor.constraint(equalTo: badge.leadingAnchor, constant: -4),
            pin.centerYAnchor.constraint(equalTo: centerYAnchor),
            pin.widthAnchor.constraint(equalToConstant: 12),
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    func setIconSize(_ width: CGFloat, _ height: CGFloat? = nil) {
        iconWidth.constant = width
        iconHeight.constant = height ?? width
    }
}

/// 按快捷鍵跳出來的歷史清單。
final class HistoryPanelController: NSObject, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate, NSWindowDelegate, NSMenuDelegate {
    private let store: Store
    private let panel: KeyPanel
    private let search = NSSearchField()
    private let table = NSTableView()
    private let scroll = NSScrollView()
    private let emptyLabel = NSTextField(labelWithString: "")
    private let hint = NSTextField(labelWithString: "")
    private let preview = PreviewPanel()
    private var items: [ClipItem] = []
    private var keyMonitor: Any?
    private let thumbs = NSCache<NSString, NSImage>()
    private let timeFormatter: RelativeDateTimeFormatter = {
        let f = RelativeDateTimeFormatter()
        f.locale = Locale(identifier: "zh_TW")
        f.unitsStyle = .short
        return f
    }()

    /// 使用者選了一筆。第二個參數是 true 時，圖片改貼「圖片裡辨識出來的文字」。
    var onPick: ((ClipItem, Bool) -> Void)?

    var isVisible: Bool { panel.isVisible }

    init(store: Store) {
        self.store = store
        panel = KeyPanel(contentRect: NSRect(x: 0, y: 0, width: 460, height: 520),
                         styleMask: [.titled, .fullSizeContentView, .nonactivatingPanel],
                         backing: .buffered, defer: false)
        super.init()
        buildUI()
    }

    private func buildUI() {
        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        panel.isMovableByWindowBackground = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        panel.delegate = self
        for button in [NSWindow.ButtonType.closeButton, .miniaturizeButton, .zoomButton] {
            panel.standardWindowButton(button)?.isHidden = true
        }

        let effect = NSVisualEffectView()
        effect.material = .popover
        effect.state = .active
        effect.blendingMode = .behindWindow
        panel.contentView = effect

        search.placeholderString = "打字搜尋，圖片裡的字也找得到"
        search.font = .systemFont(ofSize: 15)
        search.delegate = self
        search.focusRingType = .none
        search.sendsSearchStringImmediately = true

        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("main"))
        column.resizingMask = .autoresizingMask
        table.addTableColumn(column)
        table.headerView = nil
        table.dataSource = self
        table.delegate = self
        table.backgroundColor = .clear
        table.style = .inset
        table.intercellSpacing = NSSize(width: 0, height: 2)
        table.allowsEmptySelection = true
        table.allowsMultipleSelection = false
        table.refusesFirstResponder = true
        table.target = self
        table.action = #selector(rowClicked)
        let rowMenu = NSMenu()
        rowMenu.delegate = self
        table.menu = rowMenu

        scroll.documentView = table
        scroll.hasVerticalScroller = true
        scroll.drawsBackground = false
        scroll.automaticallyAdjustsContentInsets = false

        emptyLabel.alignment = .center
        emptyLabel.textColor = .secondaryLabelColor
        emptyLabel.font = .systemFont(ofSize: 13)
        emptyLabel.maximumNumberOfLines = 3

        hint.stringValue = "↩ 貼上　⌥↩ 貼圖片裡的字　⌘1–9 快速貼　空白鍵預覽　⌥P 釘選　右鍵看更多"
        hint.font = .systemFont(ofSize: 10.5)
        hint.textColor = .secondaryLabelColor
        hint.alignment = .center
        hint.lineBreakMode = .byTruncatingTail

        for v in [search, scroll, emptyLabel, hint] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            effect.addSubview(v)
        }
        NSLayoutConstraint.activate([
            search.topAnchor.constraint(equalTo: effect.topAnchor, constant: 12),
            search.leadingAnchor.constraint(equalTo: effect.leadingAnchor, constant: 12),
            search.trailingAnchor.constraint(equalTo: effect.trailingAnchor, constant: -12),
            search.heightAnchor.constraint(equalToConstant: 30),

            scroll.topAnchor.constraint(equalTo: search.bottomAnchor, constant: 8),
            scroll.leadingAnchor.constraint(equalTo: effect.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: effect.trailingAnchor),
            scroll.bottomAnchor.constraint(equalTo: hint.topAnchor, constant: -6),

            emptyLabel.centerXAnchor.constraint(equalTo: scroll.centerXAnchor),
            emptyLabel.centerYAnchor.constraint(equalTo: scroll.centerYAnchor),
            emptyLabel.widthAnchor.constraint(lessThanOrEqualTo: scroll.widthAnchor, constant: -40),

            hint.leadingAnchor.constraint(equalTo: effect.leadingAnchor, constant: 10),
            hint.trailingAnchor.constraint(equalTo: effect.trailingAnchor, constant: -10),
            hint.bottomAnchor.constraint(equalTo: effect.bottomAnchor, constant: -8),
        ])
    }

    /// 不顯示在螢幕上，直接把清單畫成圖片（檢查版面用）。
    func renderSnapshot(searching query: String = "") -> Data? {
        search.stringValue = query
        reload(selectFirst: true)
        return snapshot(of: panel)
    }

    func debugState() -> [String: Any] {
        // 每一列中心點的螢幕座標（左上角為原點），測試時用來模擬滑鼠點擊
        let top = NSScreen.screens.first?.frame.height ?? 0
        let centers: [[Double]] = (0..<min(items.count, 6)).map { row in
            let inWindow = table.convert(table.rect(ofRow: row), to: nil)
            let onScreen = panel.convertToScreen(inWindow)
            return [Double(onScreen.midX), Double(top - onScreen.midY)]
        }
        return [
            "rowCenters": centers,
            "visible": panel.isVisible,
            "isKey": panel.isKeyWindow,
            "search": search.stringValue,
            "selectedRow": table.selectedRow,
            "count": items.count,
            "titles": items.prefix(6).map { ($0.pinned ? "[釘] " : "") + $0.kind + ": " + String(($0.kind == "image" ? $0.ocr : $0.text).prefix(40)) },
            "previewVisible": preview.isVisible,
            "firstResponder": String(describing: type(of: panel.firstResponder as Any)),
        ]
    }

    func debugSnapshot() -> Data? { snapshot(of: panel) }

    // MARK: - 顯示與隱藏

    func toggle() {
        if panel.isVisible { hide() } else { show() }
    }

    func show() {
        search.stringValue = ""
        reload(selectFirst: true)

        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
        if let visible = screen?.visibleFrame {
            let size = panel.frame.size
            let origin = NSPoint(x: visible.midX - size.width / 2,
                                 y: visible.minY + (visible.height - size.height) * 0.62)
            panel.setFrameOrigin(origin)
        }
        panel.makeKeyAndOrderFront(nil)
        panel.makeFirstResponder(search)

        if keyMonitor == nil {
            keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
                guard let self, self.panel.isKeyWindow else { return event }
                return self.handleKey(event) ? nil : event
            }
        }
    }

    func hide() {
        preview.orderOut(nil)
        panel.orderOut(nil)
        if let monitor = keyMonitor {
            NSEvent.removeMonitor(monitor)
            keyMonitor = nil
        }
    }

    func windowDidResignKey(_ notification: Notification) {
        hide()
    }

    /// 清單開著的時候有新東西進來，就更新畫面。
    func refreshIfVisible() {
        guard panel.isVisible else { return }
        let selectedID = selectedItem?.id
        reload(selectFirst: false)
        if let id = selectedID, let row = items.firstIndex(where: { $0.id == id }) {
            select(row)
        } else if !items.isEmpty {
            select(0)
        }
    }

    private func reload(selectFirst: Bool) {
        items = store.list(search: search.stringValue)
        table.reloadData()
        if items.isEmpty {
            emptyLabel.stringValue = search.stringValue.isEmpty
                ? "還沒有記錄。\n複製任何文字或圖片，就會出現在這裡。"
                : "找不到「\(search.stringValue)」"
            emptyLabel.isHidden = false
        } else {
            emptyLabel.isHidden = true
        }
        if selectFirst, !items.isEmpty { select(0) }
        updatePreviewIfShown()
    }

    private func select(_ row: Int) {
        guard row >= 0, row < items.count else { return }
        table.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
        table.scrollRowToVisible(row)
    }

    private var selectedItem: ClipItem? {
        let row = table.selectedRow
        return row >= 0 && row < items.count ? items[row] : nil
    }

    // MARK: - 鍵盤

    private func handleKey(_ event: NSEvent) -> Bool {
        // 輸入法還在選字的時候，按鍵全部交給輸入法
        if let editor = panel.firstResponder as? NSTextView, editor.hasMarkedText() { return false }

        let flags = event.modifierFlags.intersection([.command, .option, .control, .shift])
        switch Int(event.keyCode) {
        case 125: // 下
            select(min(table.selectedRow + 1, items.count - 1)); updatePreviewIfShown(); return true
        case 126: // 上
            select(max(table.selectedRow - 1, 0)); updatePreviewIfShown(); return true
        case 36, 76: // return / enter
            // ⌥↩：選到圖片時，貼出圖片裡辨識出來的文字
            if let item = selectedItem { pick(item, asText: flags == .option) }
            return true
        case 53: // esc
            if preview.isVisible { preview.orderOut(nil) } else { hide() }
            return true
        case 49: // 空白鍵：搜尋框是空的時候才當預覽
            if search.stringValue.isEmpty && flags.isEmpty {
                togglePreview()
                return true
            }
            return false
        case 51: // delete
            if flags == .command, let item = selectedItem {
                let row = table.selectedRow
                store.delete(item.id)
                reload(selectFirst: false)
                select(min(row, items.count - 1))
                updatePreviewIfShown()
                return true
            }
            return false
        case 16: // ⌘Y：隨時都能預覽（搜尋框有字時空白鍵是打字）
            if flags == .command {
                togglePreview()
                return true
            }
            return false
        case 35: // P
            if flags == .option, let item = selectedItem {
                store.setPinned(item.id, !item.pinned)
                reload(selectFirst: false)
                if let row = items.firstIndex(where: { $0.id == item.id }) { select(row) }
                return true
            }
            return false
        default:
            break
        }
        // 數字鍵 1 到 9 的實體鍵位，不受輸入法或鍵盤配置影響
        let digitKeys = [18, 19, 20, 21, 23, 22, 26, 28, 25]
        if flags == .command, let index = digitKeys.firstIndex(of: Int(event.keyCode)) {
            if index < items.count { pick(items[index]) }
            return true
        }
        return false
    }

    @objc private func rowClicked() {
        let row = table.clickedRow
        guard row >= 0, row < items.count else { return }
        // 按住 ⌥ 點圖片：貼出圖片裡的文字
        let option = NSApp.currentEvent?.modifierFlags.contains(.option) ?? false
        pick(items[row], asText: option)
    }

    private func pick(_ item: ClipItem, asText: Bool = false) {
        hide()
        onPick?(item, asText && item.kind == "image")
    }

    // MARK: - 右鍵選單

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let row = table.clickedRow
        guard row >= 0, row < items.count else { return }
        select(row)
        let item = items[row]
        func add(_ title: String, _ action: Selector, enabled: Bool = true) {
            let entry = NSMenuItem(title: title, action: enabled ? action : nil, keyEquivalent: "")
            entry.target = self
            entry.isEnabled = enabled
            menu.addItem(entry)
        }
        menu.autoenablesItems = false
        if item.kind == "image" {
            add("貼上圖片", #selector(menuPaste))
            let hasText = !item.ocr.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            add(hasText ? "貼上圖片裡的文字" : "貼上圖片裡的文字（這張沒有讀到字）", #selector(menuPasteText), enabled: hasText)
        } else {
            add("貼上", #selector(menuPaste))
        }
        menu.addItem(.separator())
        add("放大預覽", #selector(menuPreview))
        add(item.pinned ? "取消釘選" : "釘選", #selector(menuPin))
        add("刪除這一筆", #selector(menuDelete))
    }

    @objc private func menuPaste() { if let item = selectedItem { pick(item) } }
    @objc private func menuPasteText() { if let item = selectedItem { pick(item, asText: true) } }
    @objc private func menuPreview() {
        if let item = selectedItem { preview.show(item: item, beside: panel) }
    }
    @objc private func menuPin() {
        guard let item = selectedItem else { return }
        store.setPinned(item.id, !item.pinned)
        reload(selectFirst: false)
        if let row = items.firstIndex(where: { $0.id == item.id }) { select(row) }
    }
    @objc private func menuDelete() {
        guard let item = selectedItem else { return }
        let row = table.selectedRow
        store.delete(item.id)
        reload(selectFirst: false)
        select(min(row, items.count - 1))
    }

    func controlTextDidChange(_ obj: Notification) {
        reload(selectFirst: true)
    }

    // MARK: - 預覽

    private func togglePreview() {
        if preview.isVisible {
            preview.orderOut(nil)
        } else if let item = selectedItem {
            preview.show(item: item, beside: panel)
        }
    }

    private func updatePreviewIfShown() {
        guard preview.isVisible else { return }
        if let item = selectedItem {
            preview.show(item: item, beside: panel)
        } else {
            preview.orderOut(nil)
        }
    }

    // MARK: - 清單內容

    func numberOfRows(in tableView: NSTableView) -> Int { items.count }

    func tableView(_ tableView: NSTableView, heightOfRow row: Int) -> CGFloat {
        items[row].kind == "image" ? 62 : 42
    }

    func tableView(_ tableView: NSTableView, rowViewForRow row: Int) -> NSTableRowView? {
        EmphasizedRowView()
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let id = NSUserInterfaceItemIdentifier("cell")
        let cell = (tableView.makeView(withIdentifier: id, owner: nil) as? ItemCell) ?? {
            let c = ItemCell(frame: .zero)
            c.identifier = id
            return c
        }()
        let item = items[row]
        cell.badge.stringValue = row < 9 ? "⌘\(row + 1)" : ""
        cell.pin.isHidden = !item.pinned

        var meta: [String] = []
        if !item.sourceApp.isEmpty { meta.append(item.sourceApp) }
        let age = Date().timeIntervalSince1970 - item.lastUsed
        meta.append(age < 60 ? "剛剛" : timeFormatter.localizedString(for: Date(timeIntervalSince1970: item.lastUsed), relativeTo: Date()))

        switch item.kind {
        case "image":
            cell.setIconSize(76, 50)
            cell.icon.image = thumbnail(for: item)
            cell.icon.contentTintColor = nil
            let firstLine = item.ocr.split(separator: "\n").first.map(String.init) ?? ""
            cell.title.stringValue = firstLine.isEmpty ? "圖片" : "圖片：" + oneLine(item.ocr)
            meta.insert("\(item.width)×\(item.height)", at: 0)
            meta.append(ByteCountFormatter.string(fromByteCount: Int64(item.bytes), countStyle: .file))
        case "file":
            cell.setIconSize(26)
            let paths = item.text.split(separator: "\n").map(String.init)
            cell.icon.image = paths.first.map { NSWorkspace.shared.icon(forFile: $0) }
            cell.icon.contentTintColor = nil
            let names = paths.map { ($0 as NSString).lastPathComponent }
            cell.title.stringValue = names.count > 1 ? "\(names[0]) 等 \(names.count) 個檔案" : (names.first ?? "檔案")
            meta.insert("檔案", at: 0)
        default:
            cell.setIconSize(20)
            cell.icon.image = NSImage(systemSymbolName: "text.alignleft", accessibilityDescription: "文字")
            cell.icon.contentTintColor = .secondaryLabelColor
            cell.title.stringValue = oneLine(item.text)
            let count = item.text.count
            if count > 80 { meta.append("\(count) 字") }
        }
        cell.sub.stringValue = meta.joined(separator: " · ")
        return cell
    }

    private func oneLine(_ text: String) -> String {
        let head = text.prefix(300)
        let parts = head.split(whereSeparator: { $0.isWhitespace || $0.isNewline })
        return parts.joined(separator: " ")
    }

    private func thumbnail(for item: ClipItem) -> NSImage? {
        guard let url = item.imageURL else { return nil }
        let key = url.lastPathComponent as NSString
        if let cached = thumbs.object(forKey: key) { return cached }
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceThumbnailMaxPixelSize: 320,
            kCGImageSourceCreateThumbnailWithTransform: true,
        ]
        guard let cg = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else { return nil }
        let image = NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
        thumbs.setObject(image, forKey: key)
        return image
    }
}

/// 空白鍵叫出來的大預覽。
final class PreviewPanel: NSPanel {
    private let imageView = NSImageView()
    private let textScroll = NSScrollView()
    private let textView = NSTextView()

    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 520, height: 520),
                   styleMask: [.titled, .fullSizeContentView, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        titleVisibility = .hidden
        titlebarAppearsTransparent = true
        level = .floating
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        for button in [NSWindow.ButtonType.closeButton, .miniaturizeButton, .zoomButton] {
            standardWindowButton(button)?.isHidden = true
        }
        let effect = NSVisualEffectView()
        effect.material = .popover
        effect.state = .active
        contentView = effect

        imageView.imageScaling = .scaleProportionallyUpOrDown
        textView.isEditable = false
        textView.isSelectable = false
        textView.drawsBackground = false
        textView.font = .systemFont(ofSize: 13)
        textView.textContainerInset = NSSize(width: 10, height: 10)
        textView.isVerticallyResizable = true
        textView.autoresizingMask = [.width]
        textView.textContainer?.widthTracksTextView = true
        textScroll.documentView = textView
        textScroll.hasVerticalScroller = true
        textScroll.drawsBackground = false

        for v in [imageView, textScroll] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            effect.addSubview(v)
            NSLayoutConstraint.activate([
                v.topAnchor.constraint(equalTo: effect.topAnchor, constant: 10),
                v.bottomAnchor.constraint(equalTo: effect.bottomAnchor, constant: -10),
                v.leadingAnchor.constraint(equalTo: effect.leadingAnchor, constant: 10),
                v.trailingAnchor.constraint(equalTo: effect.trailingAnchor, constant: -10),
            ])
        }
    }

    override var canBecomeKey: Bool { false }

    func show(item: ClipItem, beside anchor: NSWindow) {
        var size = NSSize(width: 520, height: anchor.frame.height)
        if item.kind == "image", let url = item.imageURL, let image = NSImage(contentsOf: url) {
            imageView.image = image
            imageView.isHidden = false
            textScroll.isHidden = true
            let w = CGFloat(max(item.width, 1)), h = CGFloat(max(item.height, 1))
            let scale = min(640 / w, 640 / h, 1)
            size = NSSize(width: max(w * scale + 20, 200), height: max(h * scale + 20, 160))
        } else {
            imageView.isHidden = true
            textScroll.isHidden = false
            let body = item.kind == "file" ? item.text : String(item.text.prefix(20000))
            textView.string = body
            textView.textColor = .labelColor
        }
        let screen = anchor.screen?.visibleFrame ?? NSScreen.main?.visibleFrame ?? .zero
        var x = anchor.frame.maxX + 10
        if x + size.width > screen.maxX { x = anchor.frame.minX - size.width - 10 }
        if x < screen.minX { x = screen.minX + 10 }
        var y = anchor.frame.maxY - size.height
        if y < screen.minY { y = screen.minY + 10 }
        setFrame(NSRect(origin: NSPoint(x: x, y: y), size: size), display: true)
        orderFront(nil)
    }
}

/// 把視窗內容畫成 PNG。
func snapshot(of window: NSWindow) -> Data? {
    guard let view = window.contentView else { return nil }
    window.layoutIfNeeded()
    view.layoutSubtreeIfNeeded()
    guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return nil }
    view.cacheDisplay(in: view.bounds, to: rep)
    let canvas = NSImage(size: view.bounds.size)
    canvas.lockFocus()
    NSColor.windowBackgroundColor.setFill()
    view.bounds.fill()
    rep.draw(in: view.bounds)
    canvas.unlockFocus()
    guard let tiff = canvas.tiffRepresentation, let out = NSBitmapImageRep(data: tiff) else { return nil }
    return out.representation(using: .png, properties: [:])
}
