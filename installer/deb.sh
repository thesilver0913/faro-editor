#!/bin/bash
# Debian package from a linux-x64 publish: deb.sh VERSION PUBLISH_DIR OUT_DIR [Canary] → OUT_DIR/Faro-VERSION-linux-x64.deb
# Installs to /usr/lib/faro with a `faro` command and a menu entry (Faro Canary: faro-canary, its own package, beside it). Faro needs the .NET 10 SDK: when none is installed,
# the package downloads it into /usr/lib/faro/dotnet (Microsoft's dotnet-install script), where Faro's launcher looks first.
set -euo pipefail
version=$1 publish=$2 out=$3
if [[ "${4:-}" == Canary ]]; then pkg=faro-canary name="Faro Canary" icon=faro-canary-icon.png file=FaroCanary
else pkg=faro name=Faro icon=faro-icon.png file=Faro; fi
root=$(mktemp -d)
mkdir -p "$root/DEBIAN" "$root/usr/lib/$pkg" "$root/usr/bin" "$root/usr/share/applications" "$root/usr/share/pixmaps"
cp -r "$publish"/. "$root/usr/lib/$pkg/"
ln -s "../lib/$pkg/Faro.Editor" "$root/usr/bin/$pkg"
cp "$(dirname "$0")/../assets/$icon" "$root/usr/share/pixmaps/$pkg.png"
cat > "$root/usr/share/applications/$pkg.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=$name
Comment=UI/UX visual editor
Exec=$pkg %f
Icon=$pkg
Categories=Development;IDE;
DESKTOP
cat > "$root/DEBIAN/control" <<CONTROL
Package: $pkg
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
sed -i "s#/usr/lib/faro#/usr/lib/$pkg#g" "$root/DEBIAN/postinst" "$root/DEBIAN/postrm" # .NET next to this package's Faro
chmod 755 "$root/DEBIAN/postinst" "$root/DEBIAN/postrm"
mkdir -p "$out"
dpkg-deb --root-owner-group --build "$root" "$out/$file-$version-linux-x64.deb"
rm -rf "$root"
