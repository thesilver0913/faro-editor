#!/bin/bash
# Debian package from a linux-x64 publish: deb.sh VERSION PUBLISH_DIR OUT_DIR → OUT_DIR/Faro-VERSION-linux-x64.deb
# Installs to /usr/lib/faro with a `faro` command and a menu entry. Faro needs the .NET 10 SDK: when none is installed,
# the package downloads it into /usr/lib/faro/dotnet (Microsoft's dotnet-install script), where Faro's launcher looks first.
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
Depends: curl
Section: devel
Priority: optional
Homepage: https://github.com/thesilver0913/faro-editor
Description: Faro UI/UX visual editor
 Design screens, bind them to your code and generate code with AI. Downloads the .NET 10 SDK if it isn't installed.
CONTROL
cat > "$root/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
# The .NET 10 SDK next to Faro, unless one is already installed.
sdk10() { "$1" --list-sdks 2>/dev/null | grep -q '^10\.'; }
if ! sdk10 dotnet && ! sdk10 /usr/lib/faro/dotnet/dotnet; then
  echo "Faro: downloading the .NET 10 SDK into /usr/lib/faro/dotnet..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/faro-dotnet-install.sh \
    && bash /tmp/faro-dotnet-install.sh --channel 10.0 --install-dir /usr/lib/faro/dotnet \
    || echo "Faro: couldn't install .NET 10. Install it yourself: https://dotnet.microsoft.com/download/dotnet/10.0"
  rm -f /tmp/faro-dotnet-install.sh
fi
exit 0
POSTINST
cat > "$root/DEBIAN/postrm" <<'POSTRM'
#!/bin/sh
[ "$1" = remove ] || [ "$1" = purge ] && rm -rf /usr/lib/faro/dotnet
exit 0
POSTRM
chmod 755 "$root/DEBIAN/postinst" "$root/DEBIAN/postrm"
mkdir -p "$out"
dpkg-deb --root-owner-group --build "$root" "$out/Faro-$version-linux-x64.deb"
rm -rf "$root"
