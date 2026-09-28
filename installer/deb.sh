#!/bin/bash
# Debian package from a linux-x64 publish: deb.sh VERSION PUBLISH_DIR OUT_DIR → OUT_DIR/Faro-VERSION-linux-x64.deb
# Installs to /usr/lib/faro with a `faro` command and a menu entry. Needs the .NET 10 SDK (recommended, not required:
# Microsoft's own packages or a manual install both work).
set -euo pipefail
version=$1 publish=$2 out=$3
root=$(mktemp -d)
mkdir -p "$root/DEBIAN" "$root/usr/lib/faro" "$root/usr/bin" "$root/usr/share/applications" "$root/usr/share/pixmaps"
cp -r "$publish"/. "$root/usr/lib/faro/"
ln -s ../lib/faro/Faro.Editor "$root/usr/bin/faro"
cp "$(dirname "$0")/../assets/faro-icon.png" "$root/usr/share/pixmaps/faro.png"
cat > "$root/usr/share/applications/faro.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Faro
Comment=UI/UX visual editor
Exec=faro %f
Icon=faro
Categories=Development;IDE;
DESKTOP
cat > "$root/DEBIAN/control" <<CONTROL
Package: faro
Version: ${version/-/\~}
Architecture: amd64
Maintainer: Faro <https://github.com/thesilver0913/faro-editor>
Recommends: dotnet-sdk-10.0
Section: devel
Priority: optional
Homepage: https://github.com/thesilver0913/faro-editor
Description: Faro UI/UX visual editor
 Design screens, bind them to your code and generate code with AI. Needs the .NET 10 SDK.
CONTROL
mkdir -p "$out"
dpkg-deb --root-owner-group --build "$root" "$out/Faro-$version-linux-x64.deb"
rm -rf "$root"
