import AppKit

/// 內建自我檢查：`TIETIE_HOME=/某個暫存資料夾 Tietie --selftest`
/// 一定要指定 TIETIE_HOME，避免動到正式的歷史記錄。
enum SelfTest {
    static func run() -> Int32 {
        guard ProcessInfo.processInfo.environment["TIETIE_HOME"] != nil else {
            print("請先設定 TIETIE_HOME 到暫存資料夾再跑自我檢查")
            return 2
        }
        var failures = 0
        func check(_ name: String, _ ok: Bool, _ detail: String = "") {
            print((ok ? "PASS  " : "FAIL  ") + name + (detail.isEmpty ? "" : "  [\(detail)]"))
            if !ok { failures += 1 }
        }

        Settings.shared.update { $0.maxItems = 1000; $0.imageCapMB = 2048 }
        let store = Store()
        store.clearUnpinned()

        // 文字
        let a = store.addText("第一段文字 hello", sourceApp: "測試")
        check("版本比較：新版", Updater.isNewer("v0.4.0", than: "0.3.0") && Updater.isNewer("0.10.0", than: "0.9.9") && Updater.isNewer("1.0", than: "0.9.9"))
        check("版本比較：同版或舊版不提示", !Updater.isNewer("v0.3.0", than: "0.3.0") && !Updater.isNewer("0.2.9", than: "0.3.0") && !Updater.isNewer("0.3", than: "0.3.0"))
        check("新增文字", a != nil && store.count() == 1, "count=\(store.count())")
        let b = store.addText("第一段文字 hello", sourceApp: "測試")
        check("重複內容不會多一筆", a == b && store.count() == 1, "count=\(store.count())")
        store.addText("100%_done", sourceApp: "測試")
        store.addText("另一段完全無關的內容", sourceApp: "測試")
        check("搜尋文字", store.list(search: "hello").count == 1)
        check("搜尋特殊符號不會亂抓", store.list(search: "%_").map { $0.text } == ["100%_done"],
              "\(store.list(search: "%_").map { $0.text })")
        check("最新的排最上面", store.list(search: "").first?.text == "另一段完全無關的內容")

        // 圖片與圖片文字辨識
        let png = textImage("Tietie 12345 貼貼測試")
        let rep = NSBitmapImageRep(data: png)
        let imageID = store.addImage(png: png, width: rep?.pixelsWide ?? 0, height: rep?.pixelsHigh ?? 0, sourceApp: "測試")
        let imageItem = imageID.flatMap { store.item($0) }
        let fileExists = imageItem?.imageURL.map { FileManager.default.fileExists(atPath: $0.path) } ?? false
        check("新增圖片並存成檔案", imageID != nil && fileExists)
        let ocr = ClipboardMonitor.ocr(png: png)
        check("讀出圖片裡的數字", ocr.contains("12345"), ocr.replacingOccurrences(of: "\n", with: " / "))
        check("讀出圖片裡的中文", ocr.contains("貼貼") || ocr.contains("測試"), ocr.replacingOccurrences(of: "\n", with: " / "))
        if let id = imageID { store.setOCR(id: id, text: ocr) }
        check("用圖片裡的字搜得到圖片", store.list(search: "12345").first?.kind == "image")

        // 釘選與清除
        if let id = a { store.setPinned(id, true) }
        check("釘選的排最上面", store.list(search: "").first?.id == a)
        store.clearUnpinned()
        check("一鍵清除會保留釘選", store.count() == 1 && store.list(search: "").first?.id == a, "count=\(store.count())")
        let fileGone = imageItem?.imageURL.map { !FileManager.default.fileExists(atPath: $0.path) } ?? false
        check("清除時圖片檔一起刪掉", fileGone)

        // 清除最近 10 分鐘
        store.addText("剛剛複製的敏感內容", sourceApp: "測試")
        store.clearRecent(seconds: 600)
        check("清除最近 10 分鐘", store.list(search: "敏感").isEmpty && store.count() == 1)

        // 筆數上限
        Settings.shared.update { $0.maxItems = 10 }
        for i in 0..<30 { store.addText("批次 \(i)", sourceApp: "測試") }
        let list = store.list(search: "")
        check("超過筆數上限會刪最舊的", store.count() == 11, "count=\(store.count())")
        check("留下的是最新的", list.contains { $0.text == "批次 29" } && !list.contains { $0.text == "批次 0" })

        // 圖片容量上限（用雜訊圖模擬大圖）
        Settings.shared.update { $0.maxItems = 1000; $0.imageCapMB = 50 }
        var noiseBytes = 0
        for _ in 0..<8 {
            let noise = noiseImage(side: 1700)
            noiseBytes += noise.count
            store.addImage(png: noise, width: 1700, height: 1700, sourceApp: "測試")
        }
        let total = store.imageBytes()
        check("圖片超過容量上限會刪最舊的", noiseBytes > 50 * 1024 * 1024 && total <= 50 * 1024 * 1024,
              "寫入 \(noiseBytes / 1024 / 1024) MB，留下 \(total / 1024 / 1024) MB")
        let onDisk = (try? FileManager.default.contentsOfDirectory(atPath: Paths.images.path).count) ?? -1
        let rows = store.list(search: "", limit: 1000).filter { $0.kind == "image" }.count
        check("資料庫和圖片檔數量一致", onDisk == rows, "檔案 \(onDisk)，記錄 \(rows)")

        // 剪貼簿監看與貼上（只在指定了私有剪貼簿時才跑，不動使用者的剪貼簿）
        if ProcessInfo.processInfo.environment["TIETIE_PASTEBOARD"] != nil {
            let pb = Clip.board
            let monitor = ClipboardMonitor(store: store)
            for item in store.list(search: "", limit: 1000) where item.pinned { store.setPinned(item.id, false) }
            store.clearUnpinned()
            Settings.shared.update { $0.recordConcealed = true; $0.onboarded = false; $0.paused = false; $0.imageCapMB = 2048 }

            pb.clearContents()
            pb.setString("before-onboarding-secret", forType: .string)
            pb.setString("", forType: NSPasteboard.PasteboardType("org.nspasteboard.ConcealedType"))
            monitor.tick()
            check("還沒做完第一次設定前，不記密碼管理器的內容", store.list(search: "before-onboarding").isEmpty)
            Settings.shared.update { $0.onboarded = true }

            pb.clearContents()
            pb.setString("監看測試文字", forType: .string)
            pb.setString("<b>監看測試文字</b>", forType: .html)
            pb.setData(Data("{\\rtf1 x}".utf8), forType: .rtf)
            monitor.tick()
            let got = store.list(search: "監看測試文字").first
            check("複製帶格式的文字會被記錄", got?.kind == "text" && got?.text == "監看測試文字")

            if let item = got {
                monitor.ignoreCount = Paster.write(item) ?? -1
                let types = (pb.types ?? []).map { $0.rawValue }
                let rich = types.filter { $0.contains("html") || $0.contains("rtf") || $0.contains("HTML") || $0.contains("RTF") }
                check("貼出來只有純文字，沒有格式", rich.isEmpty && pb.data(forType: .html) == nil && pb.data(forType: .rtf) == nil && pb.string(forType: .string) == "監看測試文字",
                      types.joined(separator: ","))
                let before = store.count()
                monitor.tick()
                check("貼貼自己放回剪貼簿的不會重複記", store.count() == before)
            }

            pb.clearContents()
            pb.setString("hunter2-secret", forType: .string)
            pb.setString("", forType: NSPasteboard.PasteboardType("org.nspasteboard.ConcealedType"))
            monitor.tick()
            check("選擇記密碼時，密碼管理器的內容會記", store.list(search: "hunter2").count == 1)

            Settings.shared.update { $0.recordConcealed = false }
            pb.clearContents()
            pb.setString("another-secret-9", forType: .string)
            pb.setString("", forType: NSPasteboard.PasteboardType("org.nspasteboard.ConcealedType"))
            monitor.tick()
            check("選擇不記密碼時，密碼管理器的內容不會記", store.list(search: "another-secret").isEmpty)
            Settings.shared.update { $0.recordConcealed = true }

            Settings.shared.update { $0.paused = true }
            pb.clearContents()
            pb.setString("暫停期間複製的", forType: .string)
            monitor.tick()
            check("暫停時不記錄", store.list(search: "暫停期間").isEmpty)
            Settings.shared.update { $0.paused = false }

            pb.clearContents()
            pb.setData(textImage("PNG 9876"), forType: .png)
            monitor.tick()
            let img = store.list(search: "").first
            check("複製圖片會被記錄", img?.kind == "image" && (img?.width ?? 0) == 900)
            if let item = img {
                _ = Paster.write(item)
                check("圖片貼得回去", pb.data(forType: .png) != nil && pb.data(forType: .tiff) != nil)
                if let url = item.imageURL {
                    try? FileManager.default.removeItem(at: url)
                    pb.clearContents()
                    pb.setString("不能被清掉", forType: .string)
                    monitor.tick()
                    let result = Paster.write(item)
                    check("圖片檔不見時不會清空剪貼簿", result == nil && pb.string(forType: .string) == "不能被清掉")
                }
            }

            monitor.ignoreCount = Paster.writeText("圖片裡的字 9876")
            let beforeText = store.count()
            monitor.tick()
            check("貼出圖片裡的文字是純文字，而且不會重複記", pb.string(forType: .string) == "圖片裡的字 9876" && pb.data(forType: .png) == nil && store.count() == beforeText)

            pb.clearContents()
            pb.setString("   \n  ", forType: .string)
            let n = store.count()
            monitor.tick()
            check("只有空白的內容不記", store.count() == n)

            let tmp = Paths.home.appendingPathComponent("檔案測試.txt")
            try? Data("x".utf8).write(to: tmp)
            pb.clearContents()
            pb.writeObjects([tmp as NSURL])
            monitor.tick()
            let f = store.list(search: "").first
            check("複製檔案會被記錄", f?.kind == "file" && f?.text == tmp.path)
            if let item = f {
                _ = Paster.write(item)
                let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]
                check("檔案貼得回去", urls?.first?.path == tmp.path)
            }
            pb.releaseGlobally()
        } else {
            print("SKIP  剪貼簿監看與貼上（沒有指定 TIETIE_PASTEBOARD）")
        }

        // 單一網址判斷
        check("判斷單一網址", ClipboardMonitor.looksLikeSingleURL("https://example.com/a.png")
              && !ClipboardMonitor.looksLikeSingleURL("看這個 https://example.com"))

        print(failures == 0 ? "\n全部通過" : "\n有 \(failures) 項沒過")
        return failures == 0 ? 0 : 1
    }

    static func textImage(_ text: String) -> Data {
        let size = NSSize(width: 900, height: 220)
        let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size.width), pixelsHigh: Int(size.height),
                                   bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                   colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        NSColor.white.setFill()
        NSRect(origin: .zero, size: size).fill()
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 64), .foregroundColor: NSColor.black]
        (text as NSString).draw(at: NSPoint(x: 40, y: 70), withAttributes: attrs)
        NSGraphicsContext.restoreGraphicsState()
        return rep.representation(using: .png, properties: [:])!
    }

    static func noiseImage(side: Int) -> Data {
        let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: side, pixelsHigh: side,
                                   bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                   colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        if let data = rep.bitmapData {
            let count = rep.bytesPerRow * side
            var generator = SystemRandomNumberGenerator()
            var i = 0
            while i + 8 <= count {
                var value = generator.next()
                withUnsafeBytes(of: &value) { src in
                    for k in 0..<8 { data[i + k] = src[k] }
                }
                i += 8
            }
            // alpha 設為不透明
            var p = 3
            while p < count { data[p] = 255; p += 4 }
        }
        return rep.representation(using: .png, properties: [:])!
    }
}
