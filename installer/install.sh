#!/bin/bash
# Installs Faro on Linux or macOS from GitHub Releases, the newest release by default:
#   curl -fsSL https://github.com/thesilver0913/faro-editor/releases/latest/download/install.sh | bash
# It asks first; "o" picks another version (betas too) from versions.txt, which release.yml keeps on the latest release.
# Options (piped: "| bash -s -- --yes"):
#   --version X  install X        --yes     the newest, without asking    --list  print the versions
#   --canary     Faro Canary (canary.txt: its builds)                     --user  Linux: into ~/.local, no sudo
# Linux: the .deb through apt where there is apt (and no --user), else the tar.gz into ~/.local/share/faro.
# macOS: the .pkg for this Mac (arm64 / x64) through `installer`.
set -euo pipefail
base=https://github.com/${FARO_REPO:-thesilver0913/faro-editor}/releases
version='' list='' yes='' canary='' user=''
while [[ $# -gt 0 ]]; do
  case $1 in
    --version) version=$2; shift ;;
    --list) list=1 ;; --yes) yes=1 ;; --canary) canary=1 ;; --user) user=1 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done
say() { if [[ ${LC_ALL:-${LANG:-}} == ja* ]]; then echo "$2"; else echo "$1"; fi; }
if [[ -n $canary ]]; then name="Faro Canary" file=FaroCanary cmd=faro-canary list_file=canary.txt
else name=Faro file=Faro cmd=faro list_file=versions.txt; fi

if [[ -z $version || -n $list ]]; then
  versions=$(curl -fsSL "$base/latest/download/$list_file") || { say "Couldn't get the list of $name versions." "$name のバージョンの一覧を取得できませんでした。"; exit 1; }
  if [[ -n $list ]]; then echo "$versions"; exit; fi
  # the newest without "-" (a full release); Faro Canary's are all builds, so its newest
  version=$(grep -v -- - <<< "$versions" | head -n 1 || true)
  [[ -n $version ]] || version=$(head -n 1 <<< "$versions")
  if [[ -z $yes ]]; then
    { exec < /dev/tty; } 2> /dev/null || { say "No terminal to ask in: add --yes or --version X." "確認できる端末がありません。--yes か --version X を付けてください。"; exit 2; }
    while :; do
      read -rp "$(say "$name $version will be installed. [Enter] install  [o] other versions  [q] quit: " "$name $version をインストールします。[Enter] インストール  [o] 他のバージョン  [q] やめる: ")" answer
      case $answer in
        '') break ;;
        q*) exit 1 ;;
        o*)
          n=0; while read -r v; do n=$((n + 1)); echo "  $n) $v"; done <<< "$versions"
          read -rp "$(say "Number: " "番号: ")" n
          [[ $n =~ ^[0-9]+$ ]] && v=$(sed -n "${n}p" <<< "$versions") && [[ -n $v ]] && version=$v ;;
      esac
    done
  fi
fi

[[ -n $canary ]] && tag=canary-${version##*-build} || tag=v$version
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
get() { curl -fL --progress-bar "$base/download/$tag/$1" -o "$tmp/$1"; }
case $(uname -s) in
  Darwin)
    [[ $(uname -m) == arm64 ]] && rid=osx-arm64 || rid=osx-x64
    f=$file-$version-$rid.pkg
    get "$f"
    sudo rm -rf "/Applications/$name.app" # a .pkg "upgrades" over the app, leaving a newer version's files behind when going back
    sudo installer -pkg "$tmp/$f" -target /
    say "Installed: /Applications/$name.app" "インストールしました: /Applications/$name.app" ;;
  Linux)
    [[ $(uname -m) == x86_64 ]] || { say "Faro for Linux is x64 only." "Linux 版の Faro は x64 だけです。"; exit 1; }
    if [[ -z $user ]] && command -v apt-get > /dev/null; then
      f=$file-$version-linux-x64.deb
      get "$f"
      chmod 644 "$tmp/$f" && chmod 755 "$tmp" # apt reads it as its own user
      sudo apt-get install -y "$tmp/$f"
    else
      f=$file-$version-linux-x64.tar.gz
      get "$f"
      dir=${XDG_DATA_HOME:-$HOME/.local/share}/$cmd
      mkdir -p "$dir" "$HOME/.local/bin" "${XDG_DATA_HOME:-$HOME/.local/share}/applications"
      find "$dir" -mindepth 1 -maxdepth 1 ! -name dotnet -exec rm -rf {} + # the old version, but not its .NET
      tar -xzf "$tmp/$f" -C "$dir"
      sdk10() { "$1" --list-sdks 2> /dev/null | grep -q '^10\.'; }
      sdk10 dotnet || sdk10 "$dir/dotnet/dotnet" || sh "$dir/get-dotnet.sh"
      ln -sf "$dir/Faro.Editor" "$HOME/.local/bin/$cmd"
      cat > "${XDG_DATA_HOME:-$HOME/.local/share}/applications/$cmd.desktop" << DESKTOP
[Desktop Entry]
Type=Application
Name=$name
Comment=UI/UX visual editor
Exec=$dir/Faro.Editor %f
Icon=$dir/icon.png
Categories=Development;IDE;
DESKTOP
      say "Installed: $dir (command: ~/.local/bin/$cmd)" "インストールしました: $dir(コマンド: ~/.local/bin/$cmd)"
    fi ;;
  *) say "Use Faro-Setup.exe on Windows." "Windows では Faro-Setup.exe を使ってください。"; exit 1 ;;
esac
