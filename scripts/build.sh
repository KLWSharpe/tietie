#!/bin/bash
# 把貼貼組裝成 build/Tietie.app，並壓成 build/Tietie.zip。
# 用法：./scripts/build.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
VERSION="$(sed -n 's/^let appVersion = "\(.*\)"/\1/p' Sources/Tietie/AppDelegate.swift)"
APP="$ROOT/build/Tietie.app"
STAGE="$(mktemp -d)"

echo "==> 編譯（Apple 晶片）"
swift build -c release --arch arm64
ARM="$ROOT/.build/arm64-apple-macosx/release/Tietie"

BIN="$ARM"
echo "==> 編譯（Intel）"
if swift build -c release --arch x86_64 >"$STAGE/x86.log" 2>&1; then
    lipo -create "$ARM" "$ROOT/.build/x86_64-apple-macosx/release/Tietie" -output "$STAGE/Tietie"
    BIN="$STAGE/Tietie"
    echo "    兩種晶片都支援"
else
    echo "    Intel 版編譯失敗，只產出 Apple 晶片版（記錄在 $STAGE/x86.log）"
fi

echo "==> 圖示"
swift scripts/make_icon.swift "$STAGE/icon_1024.png" >/dev/null
ICONSET="$STAGE/Tietie.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
    sips -z $size $size "$STAGE/icon_1024.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z $double $double "$STAGE/icon_1024.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$STAGE/Tietie.icns"

echo "==> 組裝"
mkdir -p "$ROOT/build"
NEW="$STAGE/Tietie.app"
mkdir -p "$NEW/Contents/MacOS" "$NEW/Contents/Resources"
cp "$BIN" "$NEW/Contents/MacOS/Tietie"
cp "$STAGE/Tietie.icns" "$NEW/Contents/Resources/Tietie.icns"
cat > "$NEW/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIdentifier</key><string>app.tietie.Tietie</string>
    <key>CFBundleName</key><string>Tietie</string>
    <key>CFBundleDisplayName</key><string>貼貼 Tietie</string>
    <key>CFBundleExecutable</key><string>Tietie</string>
    <key>CFBundleIconFile</key><string>Tietie</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>${VERSION}</string>
    <key>CFBundleVersion</key><string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>LSUIElement</key><true/>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSHumanReadableCopyright</key><string>MIT License</string>
</dict>
</plist>
EOF
codesign --force --sign - --identifier app.tietie.Tietie "$NEW" >/dev/null 2>&1

# 換掉舊的成品
if [ -d "$APP" ]; then
    mv "$APP" "$STAGE/old.app"
fi
mv "$NEW" "$APP"
(cd "$ROOT/build" && ditto -c -k --keepParent Tietie.app Tietie.zip)

echo "==> 完成：$APP（版本 $VERSION）"
lipo -archs "$APP/Contents/MacOS/Tietie"
