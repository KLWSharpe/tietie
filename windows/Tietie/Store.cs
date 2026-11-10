using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Tietie;

sealed class ClipItem
{
    public long Id;
    public string Kind = "text";
    public string Text = "";
    public string ImageFile = "";
    public string Ocr = "";
    public string SourceApp = "";
    public bool Pinned;
    public double Created;
    public double LastUsed;
    public long Bytes;
    public int Width;
    public int Height;
    public string? ImagePath => string.IsNullOrEmpty(ImageFile) ? null : Path.Combine(Paths.Images, ImageFile);
}

/// 歷史記錄：SQLite 資料庫加上圖片資料夾。
sealed class Store : IDisposable
{
    readonly SqliteConnection db;
    readonly object gate = new();

    public Store()
    {
        db = Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA secure_delete=ON;");
        Exec(@"CREATE TABLE IF NOT EXISTS items(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            kind TEXT NOT NULL,
            text TEXT NOT NULL DEFAULT '',
            image_file TEXT NOT NULL DEFAULT '',
            ocr TEXT NOT NULL DEFAULT '',
            source_app TEXT NOT NULL DEFAULT '',
            hash TEXT NOT NULL UNIQUE,
            pinned INTEGER NOT NULL DEFAULT 0,
            created REAL NOT NULL,
            last_used REAL NOT NULL,
            bytes INTEGER NOT NULL DEFAULT 0,
            width INTEGER NOT NULL DEFAULT 0,
            height INTEGER NOT NULL DEFAULT 0);");
        Exec("CREATE INDEX IF NOT EXISTS items_order ON items(pinned, last_used);");
    }

    public void Dispose() => db.Dispose();

    /// 打開資料庫。檔案壞掉時改名保留，再建一個新的，貼貼才不會開不起來。
    static SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={Paths.Db}");
        try
        {
            conn.Open();
            using var check = conn.CreateCommand();
            check.CommandText = "PRAGMA quick_check;";
            var result = check.ExecuteScalar() as string;
            if (result == "ok") return conn;
        }
        catch { }
        conn.Dispose();
        SqliteConnection.ClearAllPools();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var f = Paths.Db + ext;
            try { if (File.Exists(f)) File.Move(f, $"{Paths.Db}.broken-{stamp}{ext}"); } catch { }
        }
        conn = new SqliteConnection($"Data Source={Paths.Db}");
        conn.Open();
        return conn;
    }

    static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    static string Hash(string kind, byte[] data)
    {
        var prefix = System.Text.Encoding.UTF8.GetBytes(kind + ":");
        return Convert.ToHexString(SHA256.HashData(prefix.Concat(data).ToArray())).ToLowerInvariant();
    }

    SqliteCommand Cmd(string sql, params (string, object?)[] args)
    {
        var c = db.CreateCommand();
        c.CommandText = sql;
        foreach (var (name, value) in args) c.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return c;
    }

    void Exec(string sql, params (string, object?)[] args)
    {
        using var c = Cmd(sql, args);
        c.ExecuteNonQuery();
    }

    long Scalar(string sql, params (string, object?)[] args)
    {
        using var c = Cmd(sql, args);
        var v = c.ExecuteScalar();
        return v == null || v is DBNull ? 0 : Convert.ToInt64(v);
    }

    long? FindHash(string hash)
    {
        using var c = Cmd("SELECT id FROM items WHERE hash=$h", ("$h", hash));
        var v = c.ExecuteScalar();
        return v == null || v is DBNull ? null : Convert.ToInt64(v);
    }

    public long? AddText(string text, string kind = "text", string source = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        lock (gate)
        {
            var hash = Hash(kind, System.Text.Encoding.UTF8.GetBytes(text));
            if (FindHash(hash) is long existing)
            {
                Exec("UPDATE items SET last_used=$t, source_app=$s WHERE id=$id", ("$t", Now), ("$s", source), ("$id", existing));
                return existing;
            }
            Exec("INSERT INTO items(kind,text,source_app,hash,created,last_used,bytes) VALUES($k,$x,$s,$h,$t,$t,$b)",
                ("$k", kind), ("$x", text), ("$s", source), ("$h", hash), ("$t", Now), ("$b", (long)text.Length * 2));
            var id = Scalar("SELECT last_insert_rowid()");
            Prune();
            return id;
        }
    }

    public long? AddImage(byte[] png, int width, int height, string source, out bool isNew)
    {
        isNew = false;
        lock (gate)
        {
            var hash = Hash("image", png);
            if (FindHash(hash) is long existing)
            {
                Exec("UPDATE items SET last_used=$t, source_app=$s WHERE id=$id", ("$t", Now), ("$s", source), ("$id", existing));
                return existing;
            }
            var file = hash + ".png";
            try { File.WriteAllBytes(Path.Combine(Paths.Images, file), png); } catch { return null; }
            Exec("INSERT INTO items(kind,image_file,source_app,hash,created,last_used,bytes,width,height) VALUES('image',$f,$s,$h,$t,$t,$b,$w,$hh)",
                ("$f", file), ("$s", source), ("$h", hash), ("$t", Now), ("$b", (long)png.Length), ("$w", width), ("$hh", height));
            var id = Scalar("SELECT last_insert_rowid()");
            isNew = true;
            Prune();
            return id;
        }
    }

    public void SetOcr(long id, string text)
    {
        lock (gate) Exec("UPDATE items SET ocr=$o WHERE id=$id", ("$o", text), ("$id", id));
    }

    public void Touch(long id)
    {
        lock (gate) Exec("UPDATE items SET last_used=$t WHERE id=$id", ("$t", Now), ("$id", id));
    }

    public void SetPinned(long id, bool pinned)
    {
        lock (gate) Exec("UPDATE items SET pinned=$p WHERE id=$id", ("$p", pinned ? 1 : 0), ("$id", id));
    }

    public void Delete(long id)
    {
        lock (gate)
        {
            var item = Item(id);
            Exec("DELETE FROM items WHERE id=$id", ("$id", id));
            if (item != null) DeleteFile(item.ImageFile);
            Checkpoint();
        }
    }

    public void ClearUnpinned()
    {
        lock (gate)
        {
            foreach (var item in Query("SELECT * FROM items WHERE pinned=0")) DeleteFile(item.ImageFile);
            Exec("DELETE FROM items WHERE pinned=0");
            Checkpoint();
        }
    }

    public void ClearRecent(double seconds)
    {
        lock (gate)
        {
            var since = Now - seconds;
            foreach (var item in Query("SELECT * FROM items WHERE pinned=0 AND (created>=$t OR last_used>=$t)", ("$t", since))) DeleteFile(item.ImageFile);
            Exec("DELETE FROM items WHERE pinned=0 AND (created>=$t OR last_used>=$t)", ("$t", since));
            Checkpoint();
        }
    }

    /// 刪除後把暫存的寫入記錄清掉，被刪的內容才不會留在資料庫旁邊的暫存檔裡。
    void Checkpoint()
    {
        try { Exec("PRAGMA wal_checkpoint(TRUNCATE);"); } catch { }
    }

    /// 測試用：把一筆記錄改成很久以前建立、很久以前用過。
    public void Backdate(long id, double seconds)
    {
        lock (gate) Exec("UPDATE items SET created=$t, last_used=$t WHERE id=$id", ("$t", Now - seconds), ("$id", id));
    }

    public long Count() { lock (gate) return Scalar("SELECT COUNT(*) FROM items"); }
    public long ImageBytes() { lock (gate) return Scalar("SELECT COALESCE(SUM(bytes),0) FROM items WHERE kind='image'"); }

    public ClipItem? Item(long id)
    {
        lock (gate) return Query("SELECT * FROM items WHERE id=$id", ("$id", id)).FirstOrDefault();
    }

    public List<ClipItem> List(string search = "", int limit = 300)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(search))
                return Query("SELECT * FROM items ORDER BY pinned DESC, last_used DESC LIMIT $l", ("$l", limit));
            var pattern = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            return Query(@"SELECT * FROM items WHERE text LIKE $q ESCAPE '\' OR ocr LIKE $q ESCAPE '\'
                           ORDER BY pinned DESC, last_used DESC LIMIT $l", ("$q", pattern), ("$l", limit));
        }
    }

    List<ClipItem> Query(string sql, params (string, object?)[] args)
    {
        var list = new List<ClipItem>();
        using var c = Cmd(sql, args);
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ClipItem
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                Kind = r.GetString(r.GetOrdinal("kind")),
                Text = r.GetString(r.GetOrdinal("text")),
                ImageFile = r.GetString(r.GetOrdinal("image_file")),
                Ocr = r.GetString(r.GetOrdinal("ocr")),
                SourceApp = r.GetString(r.GetOrdinal("source_app")),
                Pinned = r.GetInt64(r.GetOrdinal("pinned")) != 0,
                Created = r.GetDouble(r.GetOrdinal("created")),
                LastUsed = r.GetDouble(r.GetOrdinal("last_used")),
                Bytes = r.GetInt64(r.GetOrdinal("bytes")),
                Width = (int)r.GetInt64(r.GetOrdinal("width")),
                Height = (int)r.GetInt64(r.GetOrdinal("height")),
            });
        }
        return list;
    }

    /// 超過筆數上限或圖片容量上限時，刪掉最舊的（釘選的不刪）。
    void Prune()
    {
        // 設定檔讀不到時不要刪任何東西，避免回到預設值後把使用者的記錄大量刪掉
        if (Settings.LoadFailed) return;
        var excess = Scalar("SELECT COUNT(*) FROM items WHERE pinned=0") - Math.Max(10, Settings.Data.MaxItems);
        if (excess > 0)
        {
            foreach (var item in Query("SELECT * FROM items WHERE pinned=0 ORDER BY last_used ASC LIMIT $n", ("$n", excess)))
            {
                Exec("DELETE FROM items WHERE id=$id", ("$id", item.Id));
                DeleteFile(item.ImageFile);
            }
        }
        long cap = (long)Math.Max(50, Settings.Data.ImageCapMB) * 1024 * 1024;
        long total = Scalar("SELECT COALESCE(SUM(bytes),0) FROM items WHERE kind='image' AND pinned=0");
        if (total > cap)
        {
            foreach (var item in Query("SELECT * FROM items WHERE kind='image' AND pinned=0 ORDER BY last_used ASC"))
            {
                if (total <= cap) break;
                Exec("DELETE FROM items WHERE id=$id", ("$id", item.Id));
                DeleteFile(item.ImageFile);
                total -= item.Bytes;
            }
        }
    }

    static void DeleteFile(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.EndsWith(".png") || name.Contains('/') || name.Contains('\\') || name.Contains("..")) return;
        try { File.Delete(Path.Combine(Paths.Images, name)); } catch { }
    }
}
