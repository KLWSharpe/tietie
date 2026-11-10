import Foundation
import SQLite3
import CryptoKit

private let SQLITE_TRANSIENT = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

struct ClipItem {
    let id: Int64
    let kind: String        // text / image / file
    let text: String        // 文字內容；file 是路徑（一行一個）；image 是空字串
    let imageFile: String?  // images/ 底下的檔名
    let ocr: String         // 圖片裡辨識出來的字
    let sourceApp: String
    let pinned: Bool
    let created: Double
    let lastUsed: Double
    let bytes: Int
    let width: Int
    let height: Int

    var imageURL: URL? { imageFile.map { Paths.images.appendingPathComponent($0) } }
}

/// 歷史記錄的資料庫。全部存在使用者自己的電腦，不連網。
final class Store {
    private var db: OpaquePointer?

    init() {
        Paths.prepare()
        if sqlite3_open(Paths.db.path, &db) != SQLITE_OK {
            NSLog("Tietie: 無法開啟資料庫")
        }
        exec("PRAGMA journal_mode=WAL")
        exec("PRAGMA secure_delete=ON")   // 刪掉的內容真的從檔案裡抹掉
        exec("""
        CREATE TABLE IF NOT EXISTS items(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            kind TEXT NOT NULL,
            text TEXT NOT NULL DEFAULT '',
            hash TEXT NOT NULL UNIQUE,
            image_file TEXT,
            ocr TEXT NOT NULL DEFAULT '',
            source_app TEXT NOT NULL DEFAULT '',
            pinned INTEGER NOT NULL DEFAULT 0,
            created REAL NOT NULL,
            last_used REAL NOT NULL,
            bytes INTEGER NOT NULL DEFAULT 0,
            width INTEGER NOT NULL DEFAULT 0,
            height INTEGER NOT NULL DEFAULT 0
        )
        """)
        exec("CREATE INDEX IF NOT EXISTS idx_items_order ON items(pinned, last_used)")
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: Paths.db.path)
    }

    deinit { sqlite3_close(db) }

    static func sha(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    // MARK: - 寫入

    /// 回傳這一筆的 id。同樣內容已經存在的話，只把它移到最上面。
    @discardableResult
    func addText(_ text: String, kind: String = "text", sourceApp: String) -> Int64? {
        let hash = Store.sha(Data((kind + ":" + text).utf8))
        if let existing = idForHash(hash) {
            touch(existing)
            return existing
        }
        let now = Date().timeIntervalSince1970
        let ok = run("INSERT INTO items(kind,text,hash,source_app,created,last_used,bytes) VALUES(?,?,?,?,?,?,?)",
                     [kind, text, hash, sourceApp, now, now, text.utf8.count])
        guard ok else { return nil }
        let id = sqlite3_last_insert_rowid(db)
        prune()
        return id
    }

    @discardableResult
    func addImage(png: Data, width: Int, height: Int, sourceApp: String) -> Int64? {
        let hash = Store.sha(png)
        if let existing = idForHash(hash) {
            touch(existing)
            return existing
        }
        let file = hash + ".png"
        let url = Paths.images.appendingPathComponent(file)
        do {
            try png.write(to: url, options: .atomic)
            try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
        } catch {
            NSLog("Tietie: 圖片寫入失敗 \(error.localizedDescription)")
            return nil
        }
        let now = Date().timeIntervalSince1970
        let ok = run("INSERT INTO items(kind,hash,image_file,source_app,created,last_used,bytes,width,height) VALUES('image',?,?,?,?,?,?,?,?)",
                     [hash, file, sourceApp, now, now, png.count, width, height])
        guard ok else { return nil }
        let id = sqlite3_last_insert_rowid(db)
        prune(keeping: id)
        return id
    }

    func setOCR(id: Int64, text: String) {
        run("UPDATE items SET ocr=? WHERE id=?", [text, id])
    }

    func touch(_ id: Int64) {
        run("UPDATE items SET last_used=? WHERE id=?", [Date().timeIntervalSince1970, id])
    }

    func setPinned(_ id: Int64, _ pinned: Bool) {
        run("UPDATE items SET pinned=? WHERE id=?", [pinned ? 1 : 0, id])
    }

    func delete(_ id: Int64) {
        deleteWhere("id=?", [id])
    }

    /// 清掉全部沒釘選的記錄。
    func clearUnpinned() {
        deleteWhere("pinned=0", [])
    }

    /// 清掉最近幾秒內用過或複製的、沒釘選的記錄。
    func clearRecent(seconds: Double) {
        let since = Date().timeIntervalSince1970 - seconds
        deleteWhere("pinned=0 AND (last_used>=? OR created>=?)", [since, since])
    }

    /// 超過上限就刪最舊、沒釘選的。
    func prune(keeping newest: Int64? = nil) {
        let maxItems = max(10, Settings.shared.data.maxItems)
        deleteWhere("""
        pinned=0 AND id NOT IN (
            SELECT id FROM items WHERE pinned=0 ORDER BY last_used DESC, id DESC LIMIT ?
        )
        """, [maxItems])

        let cap = Int64(max(50, Settings.shared.data.imageCapMB)) * 1024 * 1024
        var total = imageBytes()
        if total <= cap { return }
        // 從最舊的沒釘選圖片開始刪，刪到低於上限
        let victims = query("SELECT id, bytes FROM items WHERE kind='image' AND pinned=0 ORDER BY last_used ASC, id ASC", []) { stmt in
            (sqlite3_column_int64(stmt, 0), sqlite3_column_int64(stmt, 1))
        }
        for (id, bytes) in victims where total > cap && id != newest {
            delete(id)
            total -= bytes
        }
    }

    // MARK: - 讀取

    func list(search: String, limit: Int = 300) -> [ClipItem] {
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines)
        let columns = "id,kind,text,image_file,ocr,source_app,pinned,created,last_used,bytes,width,height"
        if q.isEmpty {
            return query("SELECT \(columns) FROM items ORDER BY pinned DESC, last_used DESC, id DESC LIMIT ?", [limit], map: Store.row)
        }
        let escaped = q.replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "%", with: "\\%")
            .replacingOccurrences(of: "_", with: "\\_")
        let like = "%" + escaped + "%"
        return query("""
        SELECT \(columns) FROM items
        WHERE text LIKE ? ESCAPE '\\' OR ocr LIKE ? ESCAPE '\\'
        ORDER BY pinned DESC, last_used DESC, id DESC LIMIT ?
        """, [like, like, limit], map: Store.row)
    }

    func item(_ id: Int64) -> ClipItem? {
        query("SELECT id,kind,text,image_file,ocr,source_app,pinned,created,last_used,bytes,width,height FROM items WHERE id=?", [id], map: Store.row).first
    }

    func count() -> Int {
        Int(query("SELECT COUNT(*) FROM items", []) { sqlite3_column_int64($0, 0) }.first ?? 0)
    }

    func imageBytes() -> Int64 {
        query("SELECT COALESCE(SUM(bytes),0) FROM items WHERE kind='image'", []) { sqlite3_column_int64($0, 0) }.first ?? 0
    }

    // MARK: - 內部

    private static func row(_ s: OpaquePointer) -> ClipItem {
        func str(_ i: Int32) -> String {
            guard let c = sqlite3_column_text(s, i) else { return "" }
            return String(cString: c)
        }
        let file = sqlite3_column_type(s, 3) == SQLITE_NULL ? nil : str(3)
        return ClipItem(
            id: sqlite3_column_int64(s, 0), kind: str(1), text: str(2), imageFile: file,
            ocr: str(4), sourceApp: str(5), pinned: sqlite3_column_int(s, 6) != 0,
            created: sqlite3_column_double(s, 7), lastUsed: sqlite3_column_double(s, 8),
            bytes: Int(sqlite3_column_int64(s, 9)),
            width: Int(sqlite3_column_int64(s, 10)), height: Int(sqlite3_column_int64(s, 11)))
    }

    private func idForHash(_ hash: String) -> Int64? {
        query("SELECT id FROM items WHERE hash=?", [hash]) { sqlite3_column_int64($0, 0) }.first
    }

    /// 刪除資料列，同時把對應的圖片檔刪掉。
    private func deleteWhere(_ condition: String, _ args: [Any]) {
        let files = query("SELECT image_file FROM items WHERE image_file IS NOT NULL AND (\(condition))", args) { stmt -> String in
            String(cString: sqlite3_column_text(stmt, 0))
        }
        run("DELETE FROM items WHERE \(condition)", args)
        for file in files where file.hasSuffix(".png") && !file.contains("/") && !file.contains("..") {
            try? FileManager.default.removeItem(at: Paths.images.appendingPathComponent(file))
        }
    }

    private func exec(_ sql: String) {
        if sqlite3_exec(db, sql, nil, nil, nil) != SQLITE_OK {
            NSLog("Tietie SQL 錯誤: \(String(cString: sqlite3_errmsg(db)))")
        }
    }

    private func bind(_ stmt: OpaquePointer?, _ args: [Any]) {
        for (i, arg) in args.enumerated() {
            let idx = Int32(i + 1)
            switch arg {
            case let v as String: sqlite3_bind_text(stmt, idx, v, -1, SQLITE_TRANSIENT)
            case let v as Int: sqlite3_bind_int64(stmt, idx, Int64(v))
            case let v as Int64: sqlite3_bind_int64(stmt, idx, v)
            case let v as Double: sqlite3_bind_double(stmt, idx, v)
            default: sqlite3_bind_null(stmt, idx)
            }
        }
    }

    @discardableResult
    private func run(_ sql: String, _ args: [Any]) -> Bool {
        var stmt: OpaquePointer?
        guard sqlite3_prepare_v2(db, sql, -1, &stmt, nil) == SQLITE_OK else {
            NSLog("Tietie SQL 錯誤: \(String(cString: sqlite3_errmsg(db)))")
            return false
        }
        defer { sqlite3_finalize(stmt) }
        bind(stmt, args)
        return sqlite3_step(stmt) == SQLITE_DONE
    }

    private func query<T>(_ sql: String, _ args: [Any], map: (OpaquePointer) -> T) -> [T] {
        var stmt: OpaquePointer?
        guard sqlite3_prepare_v2(db, sql, -1, &stmt, nil) == SQLITE_OK, let s = stmt else {
            NSLog("Tietie SQL 錯誤: \(String(cString: sqlite3_errmsg(db)))")
            return []
        }
        defer { sqlite3_finalize(s) }
        bind(s, args)
        var out: [T] = []
        while sqlite3_step(s) == SQLITE_ROW { out.append(map(s)) }
        return out
    }
}
