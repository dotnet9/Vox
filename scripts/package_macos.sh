#!/usr/bin/env bash
# 将同一份 NativeAOT 产物封装为带图标的 .app、DMG 和便携 ZIP。
set -euo pipefail
RID="${1:?usage: package_macos.sh osx-x64|osx-arm64}"
case "$RID" in osx-x64|osx-arm64) ;; *) echo "unsupported rid: $RID" >&2; exit 1 ;; esac
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR="${PUBLISH_DIR:-$ROOT_DIR/artifacts/publish/Vox.App-$RID}"
OUT_DIR="${OUT_DIR:-$ROOT_DIR/artifacts/release}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT_DIR/Directory.Build.props" | head -n 1)"
[[ -n "$VERSION" ]] || { echo 'Unable to resolve project version.' >&2; exit 1; }
MACOS_VERSION="${VERSION%%[-+]*}"
WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/vox-macos-$RID.XXXXXX")"
trap 'rm -rf "$WORK_DIR"' EXIT
APP_DIR="$WORK_DIR/dmg/Vox.app"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources" "$OUT_DIR"
pwsh -NoProfile -File "$ROOT_DIR/scripts/verify_native_publish.ps1" -Rid "$RID" -SourceDirectory "$PUBLISH_DIR"
# 保留应用现有的 AppContext.BaseDirectory 资源解析约定。
for entry in "$PUBLISH_DIR"/*; do
  case "${entry##*/}" in *.pdb|*.dbg|*.dSYM) continue ;; esac
  ditto "$entry" "$APP_DIR/Contents/MacOS/${entry##*/}"
done
chmod +x "$APP_DIR/Contents/MacOS/Vox"
ICONSET="$WORK_DIR/Vox.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$ROOT_DIR/logo.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$ROOT_DIR/logo.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP_DIR/Contents/Resources/Vox.icns"
printf 'APPL????' > "$APP_DIR/Contents/PkgInfo"
cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Vox</string>
  <key>CFBundleDisplayName</key><string>Vox</string>
  <key>CFBundleIdentifier</key><string>com.codewf.vox</string>
  <key>CFBundleExecutable</key><string>Vox</string>
  <key>CFBundleIconFile</key><string>Vox</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$MACOS_VERSION</string>
  <key>CFBundleVersion</key><string>$MACOS_VERSION</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>CFBundleDocumentTypes</key><array><dict>
    <key>CFBundleTypeName</key><string>Markdown</string>
    <key>CFBundleTypeExtensions</key><array><string>md</string><string>markdown</string><string>mdown</string></array>
    <key>CFBundleTypeRole</key><string>Editor</string>
    <key>LSHandlerRank</key><string>Alternate</string>
  </dict></array>
</dict></plist>
PLIST
plutil -lint "$APP_DIR/Contents/Info.plist"
# 签名失败必须阻止发布；ad-hoc 不代表 Developer ID 签名或公证。
codesign --force --deep --sign - "$APP_DIR"
codesign --verify --deep --strict "$APP_DIR"
ZIP="$OUT_DIR/Vox-v$VERSION-$RID.zip"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ZIP"
ln -s /Applications "$WORK_DIR/dmg/Applications"
DMG="$OUT_DIR/Vox-v$VERSION-$RID.dmg"
for attempt in 1 2 3 4 5; do
  if hdiutil create -volname Vox -srcfolder "$WORK_DIR/dmg" -ov -format UDZO "$DMG"; then break; fi
  [[ "$attempt" -lt 5 ]] || { echo 'DMG creation failed.' >&2; exit 1; }
  sleep 5
done
for file in "$DMG" "$ZIP"; do
  hash="$(shasum -a 256 "$file" | awk '{print $1}')"
  printf '%s  %s\n' "$hash" "${file##*/}" > "$file.sha256"
done
echo "Created DMG and portable .app ZIP in $OUT_DIR"
