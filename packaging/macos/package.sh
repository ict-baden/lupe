#!/bin/bash
# Lupe.app aus einer Veröffentlichung (dotnet publish -r osx-<arch> --self-contained), gepackt als ZIP:
#   package.sh <publish-ordner> <arch: arm64|x64> <version> <ausgabe-ordner> [<dateisuffix>]
# Nur ad hoc signiert, nicht notarisiert – Gatekeeper verlangt beim ersten Start Rechtsklick → Öffnen.
set -euo pipefail
pub=$1; arch=$2; version=$3; out=$4; suffix=${5:-}
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
mkdir -p "$out"
out=$(cd "$out" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

app="$work/Lupe.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$pub"/. "$app/Contents/MacOS/"
chmod 755 "$app/Contents/MacOS/Lupe"
short=${version%%-*}
sed -e "s/@SHORTVERSION@/$short/" -e "s/@VERSION@/$version/" "$here/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"
printf 'APPL????' > "$app/Contents/PkgInfo"

# Symbol: lupe.png (256 px) → lupe.icns
iconset="$work/lupe.iconset"
mkdir -p "$iconset"
png="$repo/src/Lupe/Assets/lupe.png"
for s in 16 32 128 256; do
    sips -z $s $s "$png" --out "$iconset/icon_${s}x${s}.png" >/dev/null
    d=$((s * 2))
    if [ $d -le 256 ]; then sips -z $d $d "$png" --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null; fi
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/lupe.icns"

# Ad-hoc-Signatur über das ganze Bündel (Apple Silicon startet unsignierten Code nicht)
codesign --force --deep --sign - "$app"
codesign --verify --deep "$app"
ditto -c -k --keepParent "$app" "$out/Lupe$suffix-macos-$arch.zip"
echo "Gebaut: $out/Lupe$suffix-macos-$arch.zip"
