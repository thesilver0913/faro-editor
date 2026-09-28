#!/bin/bash
# macOS installer package from an osx-* publish (run on macOS): pkg.sh VERSION RID PUBLISH_DIR OUT_DIR → OUT_DIR/Faro-VERSION-RID.pkg
# Installs Faro.app into /Applications; when no .NET 10 SDK is installed, downloads it into /usr/local/share/dotnet (the
# standard location, which Faro's launcher searches) with Microsoft's dotnet-install script.
# Ad-hoc signed only (no Apple Developer ID, not notarized): opening the .pkg and Faro the first time needs
# System Settings › Privacy & Security › Open Anyway.
set -euo pipefail
version=$1 rid=$2 publish=$3 out=$4
stage=$(mktemp -d)
app="$stage/root/Applications/Faro.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$stage/scripts"
cp -r "$publish"/. "$app/Contents/MacOS/"
sips -z 512 512 "$(dirname "$0")/../assets/faro-icon.png" --out "$stage/icon.png" > /dev/null
sips -s format icns "$stage/icon.png" --out "$app/Contents/Resources/faro.icns" > /dev/null
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
cat > "$stage/scripts/postinstall" <<'POSTINSTALL'
#!/bin/bash
# The .NET 10 SDK, unless one is already installed.
dotnet=/usr/local/share/dotnet/dotnet
if ! "$dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/faro-dotnet-install.sh \
    && bash /tmp/faro-dotnet-install.sh --channel 10.0 --install-dir /usr/local/share/dotnet \
    && mkdir -p /usr/local/bin && ln -sf "$dotnet" /usr/local/bin/dotnet
  rm -f /tmp/faro-dotnet-install.sh
fi
exit 0
POSTINSTALL
chmod 755 "$stage/scripts/postinstall"
mkdir -p "$out"
pkgbuild --root "$stage/root" --scripts "$stage/scripts" --identifier io.github.thesilver0913.faro --version "${version%%-*}" \
  --install-location / "$out/Faro-$version-$rid.pkg"
rm -rf "$stage"
