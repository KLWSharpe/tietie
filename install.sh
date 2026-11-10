#!/bin/bash
# 貼貼 Tietie 安裝指令。
# 做的事：下載最新版 → 放進「應用程式」資料夾 → 打開。不需要管理員密碼。
set -euo pipefail

REPO="KLWSharpe/tietie"
URL="${TIETIE_ZIP_URL:-https://github.com/$REPO/releases/latest/download/Tietie.zip}"
DEST="${TIETIE_INSTALL_DIR:-/Applications}"
TMP="$(mktemp -d)"

echo "==> 下載貼貼"
curl -fsSL "$URL" -o "$TMP/Tietie.zip"
ditto -x -k "$TMP/Tietie.zip" "$TMP/new"
if [ ! -x "$TMP/new/Tietie.app/Contents/MacOS/Tietie" ]; then
    echo "下載的檔案不完整，請再試一次。"
    exit 1
fi

# 「應用程式」資料夾不能寫入的話，改放到個人的應用程式資料夾
mkdir -p "$DEST" 2>/dev/null || true
if [ ! -w "$DEST" ]; then
    DEST="$HOME/Applications"
    mkdir -p "$DEST"
fi

# 關掉正在執行的舊版，並把舊版移到暫存區（不直接刪除）
pkill -x Tietie >/dev/null 2>&1 || true
for _ in $(seq 1 25); do
    pgrep -x Tietie >/dev/null 2>&1 || break
    sleep 0.2
done
if [ -d "$DEST/Tietie.app" ]; then
    mv "$DEST/Tietie.app" "$TMP/old-Tietie.app"
    # 更新後 macOS 會把新版當成另一個程式，舊的「輔助使用」開關會失效。
    # 先把舊開關清掉，貼貼打開後會再請你開一次。
    tccutil reset Accessibility app.tietie.Tietie >/dev/null 2>&1 || true
    UPDATED=1
fi

echo "==> 安裝到 $DEST"
mv "$TMP/new/Tietie.app" "$DEST/Tietie.app"
xattr -dr com.apple.quarantine "$DEST/Tietie.app" 2>/dev/null || true

if [ -z "${TIETIE_NO_OPEN:-}" ]; then
    open "$DEST/Tietie.app"
fi
echo "==> 完成。貼貼在螢幕右上角的選單列，按 ⇧⌘V 叫出清單。"
if [ -n "${UPDATED:-}" ]; then
    echo "    這次是更新：請從貼貼選單按「開啟自動貼上權限」，重新打開一次。"
fi
