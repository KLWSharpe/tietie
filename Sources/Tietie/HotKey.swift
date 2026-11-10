import AppKit
import Carbon.HIToolbox

/// 全域快捷鍵。用系統內建的方式註冊，不需要額外權限。
final class HotKey {
    private var ref: EventHotKeyRef?
    private static var handlerInstalled = false
    private static var action: (() -> Void)?

    /// 註冊失敗（例如被別的程式佔用）回傳 false。
    @discardableResult
    func register(keyCode: UInt32, modifiers: UInt32, action: @escaping () -> Void) -> Bool {
        unregister()
        HotKey.action = action
        if !HotKey.handlerInstalled {
            var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            let status = InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
                DispatchQueue.main.async { HotKey.action?() }
                return noErr
            }, 1, &spec, nil, nil)
            guard status == noErr else { return false }
            HotKey.handlerInstalled = true
        }
        let id = EventHotKeyID(signature: OSType(0x5449_4554), id: 1) // "TIET"
        let status = RegisterEventHotKey(keyCode, modifiers, id, GetApplicationEventTarget(), 0, &ref)
        return status == noErr && ref != nil
    }

    func unregister() {
        if let ref { UnregisterEventHotKey(ref) }
        ref = nil
    }
}

/// 把選到的那一筆貼到原本在用的視窗。
enum Paster {
    static var trusted: Bool { AXIsProcessTrusted() }

    /// 跳出系統的權限提示，帶使用者去「輔助使用」打勾。
    static func requestTrust() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    static func openAccessibilitySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    /// 送出一次 ⌘V。
    static func sendCommandV() {
        let source = CGEventSource(stateID: .combinedSessionState)
        let vKey = CGKeyCode(kVK_ANSI_V)
        let down = CGEvent(keyboardEventSource: source, virtualKey: vKey, keyDown: true)
        let up = CGEvent(keyboardEventSource: source, virtualKey: vKey, keyDown: false)
        down?.flags = .maskCommand
        up?.flags = .maskCommand
        down?.post(tap: .cgAnnotatedSessionEventTap)
        up?.post(tap: .cgAnnotatedSessionEventTap)
    }

    /// 把一筆記錄放回剪貼簿。回傳放完之後的 changeCount，讓監看程式略過這一次。
    /// 圖片檔不見了的話回傳 nil，而且不會動到剪貼簿。
    /// 只放純文字（用在「貼上圖片裡的文字」）。
    static func writeText(_ text: String) -> Int {
        let pb = Clip.board
        pb.clearContents()
        pb.setString(text, forType: .string)
        return pb.changeCount
    }

    static func write(_ item: ClipItem) -> Int? {
        let pb = Clip.board
        var png: Data?
        if item.kind == "image" {
            guard let url = item.imageURL, let data = try? Data(contentsOf: url) else { return nil }
            png = data
        }
        pb.clearContents()
        switch item.kind {
        case "image":
            if let png {
                pb.setData(png, forType: .png)
                if let rep = NSBitmapImageRep(data: png), let tiff = rep.tiffRepresentation {
                    pb.setData(tiff, forType: .tiff)
                }
            }
        case "file":
            let urls = item.text.split(separator: "\n").map { URL(fileURLWithPath: String($0)) as NSURL }
            let existing = urls.filter { FileManager.default.fileExists(atPath: ($0 as URL).path) }
            if existing.isEmpty {
                pb.setString(item.text, forType: .string)
            } else {
                pb.writeObjects(existing)
            }
        default:
            pb.setString(item.text, forType: .string)
        }
        return pb.changeCount
    }
}
