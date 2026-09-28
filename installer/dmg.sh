#!/bin/bash
# macOS disk image from an osx-* publish (run on macOS): dmg.sh VERSION RID PUBLISH_DIR OUT_DIR → OUT_DIR/Faro-VERSION-RID.dmg
# Ad-hoc signed only (no Apple Developer ID, not notarized): the first launch needs
# System Settings › Privacy & Security › Open Anyway (or `xattr -dr com.apple.quarantine /Applications/Faro.app`).
set -euo pipefail
version=$1 rid=$2 publish=$3 out=$4
stage=$(mktemp -d)
app="$stage/Faro.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -r "$publish"/. "$app/Contents/MacOS/"
sips -z 512 512 "$(dirname "$0")/../assets/faro-icon.png" --out "$stage/icon.png" > /dev/null
sips -s format icns "$stage/icon.png" --out "$app/Contents/Resources/faro.icns" > /dev/null
rm "$stage/icon.png"
cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Faro</string>
  <key>CFBundleDisplayName</key><string>Faro</string>
  <key>CFBundleIdentifier</key><string>io.github.thesilver0913.faro</string>
  <key>CFBundleExecutable</key><string>Faro.Editor</string>
  <key>CFBundleIconFile</key><string>faro.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${version%%-*}</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
codesign --force --deep --sign - "$app"
ln -s /Applications "$stage/Applications"
mkdir -p "$out"
hdiutil create -volname Faro -srcfolder "$stage" -ov -format UDZO "$out/Faro-$version-$rid.dmg"
rm -rf "$stage"
