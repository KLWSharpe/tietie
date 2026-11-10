import AppKit

/// 「關於貼貼」視窗。
final class AboutController: NSObject {
    private var window: NSWindow?

    func show() {
        if window == nil { build() }
        NSApp.activate(ignoringOtherApps: true)
        window?.center()
        window?.makeKeyAndOrderFront(nil)
    }

    func renderSnapshot() -> Data? {
        if window == nil { build() }
        return window.flatMap { snapshot(of: $0) }
    }

    private func text(_ string: String, size: CGFloat, weight: NSFont.Weight = .regular, color: NSColor = .labelColor) -> NSTextField {
        let field = NSTextField(wrappingLabelWithString: string)
        field.font = .systemFont(ofSize: size, weight: weight)
        field.textColor = color
        field.alignment = .center
        field.isSelectable = false
        return field
    }

    private func link(_ title: String, action: Selector) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.isBordered = false
        button.attributedTitle = NSAttributedString(string: title, attributes: [
            .font: NSFont.systemFont(ofSize: 13),
            .foregroundColor: NSColor.linkColor,
        ])
        return button
    }

    private func build() {
        let icon = NSImageView()
        icon.image = NSApp.applicationIconImage
        icon.imageScaling = .scaleProportionallyUpOrDown
        icon.widthAnchor.constraint(equalToConstant: 96).isActive = true
        icon.heightAnchor.constraint(equalToConstant: 96).isActive = true

        let name = text("貼貼 Tietie", size: 22, weight: .semibold)
        let version = text("版本 \(appVersion)", size: 11, color: .secondaryLabelColor)
        let tagline = text("免費、開源的 Mac 剪貼簿小工具", size: 13)
        let detail = text("記住你複製過的文字、圖片和檔案。\n所有記錄只存在這台電腦，不會上傳。", size: 12, color: .secondaryLabelColor)

        let line = NSBox()
        line.boxType = .separator
        line.widthAnchor.constraint(equalToConstant: 240).isActive = true

        let author = text("製作：奎嵐 KueiLan", size: 13, weight: .medium)
        // 主要按鈕：直接打開奎嵐的網站
        let site = NSButton(title: "前往奎嵐的網站 kueilan.com", target: self, action: #selector(openSite))
        site.bezelStyle = .rounded
        site.controlSize = .large
        site.keyEquivalent = "\r"
        let repo = link("到 GitHub 看原始碼、給回饋", action: #selector(openRepo))
        let links = NSStackView(views: [site, repo])
        links.orientation = .vertical
        links.spacing = 10

        let feedback = text("歡迎給回饋與建議，之後會幫大家更新。", size: 11, color: .secondaryLabelColor)

        let stack = NSStackView(views: [icon, name, version, tagline, detail, line, author, links, feedback])
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.spacing = 6
        stack.setCustomSpacing(12, after: icon)
        stack.setCustomSpacing(2, after: name)
        stack.setCustomSpacing(16, after: version)
        stack.setCustomSpacing(18, after: detail)
        stack.setCustomSpacing(18, after: line)
        stack.setCustomSpacing(12, after: author)
        stack.setCustomSpacing(14, after: links)
        stack.edgeInsets = NSEdgeInsets(top: 12, left: 32, bottom: 28, right: 32)
        stack.translatesAutoresizingMaskIntoConstraints = false

        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 380, height: 400),
                         styleMask: [.titled, .closable, .fullSizeContentView], backing: .buffered, defer: false)
        w.title = "關於貼貼"
        w.titleVisibility = .hidden
        w.titlebarAppearsTransparent = true
        w.isMovableByWindowBackground = true
        w.isReleasedWhenClosed = false
        let content = NSView()
        content.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 28),
            stack.bottomAnchor.constraint(equalTo: content.bottomAnchor),
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            content.widthAnchor.constraint(equalToConstant: 380),
        ])
        w.contentView = content
        w.setContentSize(content.fittingSize)
        window = w
    }

    @objc private func openSite() {
        if let url = URL(string: "https://kueilan.com") { NSWorkspace.shared.open(url) }
    }

    @objc private func openRepo() {
        if let url = URL(string: "https://github.com/KLWSharpe/tietie") { NSWorkspace.shared.open(url) }
    }
}
