#!/usr/bin/env bash
# 从已有发布产物打包 Vox macOS .app 与 dmg（MVP：普通 dmg + ad-hoc 签名；品牌化镜像/公证后续引入）。
# 用法：./scripts/package_macos.sh osx-x64|osx-arm64
set -euo pipefail

RID="${1:?usage: package_macos.sh osx-x64|osx-arm64}"
case "$RID" in
  osx-x64|osx-arm64) ;;
  *) echo "unsupported rid: $RID" >&2; exit 1 ;;
esac

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUNDLE_ID="com.codewf.vox"
PUBLISH_DIR="$ROOT_DIR/artifacts/publish/Vox.App-$RID"
OUT_DIR="$ROOT_DIR/artifacts/release"
WORK_DIR="$ROOT_DIR/artifacts/macos/work"

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT_DIR/Directory.Build.props" | head -n 1)"
[[ -n "$VERSION" ]] || { echo "Unable to resolve version from Directory.Build.props." >&2; exit 1; }

APP_DIR="$WORK_DIR/Vox.app"
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Vox</string>
  <key>CFBundleDisplayName</key><string>Vox</string>
  <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
  <key>CFBundleExecutable</key><string>Vox</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# 发布产物整体作为 .app 内容（可执行文件 Vox 位于 Contents/MacOS）。
cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/Vox"
# ad-hoc 签名：无 Developer ID 时保证本地可启动；正式分发再接签名+公证。
codesign --force --deep -s - "$APP_DIR" 2>/dev/null || true

mkdir -p "$OUT_DIR"
DMG="$OUT_DIR/Vox-v$VERSION-$RID.dmg"
rm -f "$DMG"
hdiutil create -volname "Vox" -srcfolder "$WORK_DIR" -ov -format UDZO "$DMG"
rm -rf "$WORK_DIR"
echo "dmg -> $DMG"
