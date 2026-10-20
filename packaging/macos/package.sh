#!/bin/bash
# SPDX-License-Identifier: MIT
# Lupe.app aus einer Veröffentlichung (dotnet publish -r osx-<arch> --self-contained), gepackt als ZIP:
#   package.sh <publish-ordner> <arch: arm64|x64> <version> <ausgabe-ordner> [<dateisuffix>]
# Signatur: ohne MACOS_SIGN_IDENTITY ad hoc (Gatekeeper verlangt beim ersten Start „Dennoch öffnen“, siehe
# docs/installation.md). Mit MACOS_SIGN_IDENTITY ("Developer ID Application: …", Zertifikat im Schlüsselbund) jede Datei
# mit Hardened Runtime und Zeitstempel; mit MACOS_NOTARY_APPLE_ID, MACOS_NOTARY_TEAM_ID und MACOS_NOTARY_PASSWORD
# (app-spezifisches Passwort) zusätzlich notarisiert und das Ticket angeheftet. CI setzt sie nur, wenn die Zugangsdaten
# hinterlegt sind (.github/workflows/build.yml).
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

identity=${MACOS_SIGN_IDENTITY:-}
if [ -z "$identity" ]; then
    # Ad-hoc-Signatur über das ganze Bündel (Apple Silicon startet unsignierten Code nicht)
    codesign --force --deep --sign - "$app"
else
    # Developer ID: jede Datei in Contents/MacOS einzeln (auch die .NET-Assemblys), dann das Bündel – mit Hardened
    # Runtime, Zeitstempel und den Berechtigungen für JIT und Kamera
    find "$app/Contents/MacOS" -type f | while IFS= read -r f; do
        codesign --force --timestamp --options runtime --entitlements "$here/Lupe.entitlements" --sign "$identity" "$f"
    done
    codesign --force --timestamp --options runtime --entitlements "$here/Lupe.entitlements" --sign "$identity" "$app"
fi
codesign --verify --deep --strict "$app"

if [ -n "$identity" ] && [ -n "${MACOS_NOTARY_PASSWORD:-}" ]; then
    ditto -c -k --keepParent "$app" "$work/notarisieren.zip"
    xcrun notarytool submit "$work/notarisieren.zip" --apple-id "$MACOS_NOTARY_APPLE_ID" --team-id "$MACOS_NOTARY_TEAM_ID" \
        --password "$MACOS_NOTARY_PASSWORD" --wait --timeout 30m
    xcrun stapler staple "$app"
fi
ditto -c -k --keepParent "$app" "$out/Lupe$suffix-macos-$arch.zip"
echo "Gebaut: $out/Lupe$suffix-macos-$arch.zip"
