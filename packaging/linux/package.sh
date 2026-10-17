#!/bin/bash
# Linux-Pakete aus einer Veröffentlichung (dotnet publish -r linux-<arch> --self-contained):
#   package.sh <publish-ordner> <arch: x64|arm64> <version> <ausgabe-ordner> [<dateisuffix>]
# Ergebnis: Lupe<suffix>-linux-<arch>.tar.gz, für x64 zusätzlich das .deb (dpkg-deb):
#   ohne Suffix (main) lupe_amd64.deb – fester Link auf „latest“, mit Suffix (Tag) lupe_<version>_amd64.deb
set -euo pipefail
pub=$1; arch=$2; version=$3; out=$4; suffix=${5:-}
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
mkdir -p "$out"
out=$(cd "$out" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# tar.gz: Ordner lupe/ mit Programm, Menüeintrag, Symbol und Einrichtung
mkdir -p "$work/tar/lupe"
cp -R "$pub"/. "$work/tar/lupe/"
cp "$here/lupe.desktop" "$here/install.sh" "$here/LIESMICH.txt" "$work/tar/lupe/"
cp "$repo/src/Lupe/Assets/lupe.png" "$work/tar/lupe/lupe.png"
chmod 755 "$work/tar/lupe/Lupe" "$work/tar/lupe/install.sh"
tar -C "$work/tar" --owner=0 --group=0 -czf "$out/Lupe$suffix-linux-$arch.tar.gz" lupe
echo "Gebaut: $out/Lupe$suffix-linux-$arch.tar.gz"

[ "$arch" = "x64" ] || exit 0

# .deb: /opt/lupe, /usr/bin/lupe, Menüeintrag und Symbol
deb="$work/deb"
mkdir -p "$deb/DEBIAN" "$deb/opt/lupe" "$deb/usr/bin" "$deb/usr/share/applications" \
         "$deb/usr/share/icons/hicolor/256x256/apps" "$deb/usr/share/doc/lupe"
cp -R "$pub"/. "$deb/opt/lupe/"
chmod -R u=rwX,go=rX "$deb/opt/lupe"
chmod 755 "$deb/opt/lupe/Lupe"
ln -s /opt/lupe/Lupe "$deb/usr/bin/lupe"
install -m 644 "$here/lupe.desktop" "$deb/usr/share/applications/lupe.desktop"
install -m 644 "$repo/src/Lupe/Assets/lupe.png" "$deb/usr/share/icons/hicolor/256x256/apps/lupe.png"
install -m 644 "$repo/THIRD-PARTY-NOTICES.txt" "$deb/usr/share/doc/lupe/THIRD-PARTY-NOTICES.txt"
# „0.1.0-ci.12“: der Bindestrich wäre eine Debian-Revision – „0.1.0~ci.12“ sortiert vor 0.1.0
debversion=${version//-/\~}
size=$(du -sk "$deb" | cut -f1)
{
    echo "Package: lupe"
    echo "Version: $debversion"
    echo "Architecture: amd64"
    echo "Maintainer: ICT-Baden <noreply@github.com>"
    echo "Installed-Size: $size"
    echo "Depends: libx11-6, libxtst6, libxrandr2, libxi6, libice6, libsm6, libfontconfig1, libicu78 | libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67"
    echo "Section: utils"
    echo "Priority: optional"
    echo "Homepage: https://github.com/ict-baden/lupe"
    echo "Description: QR- und Barcodes lesen"
    echo " Liest QR- und Barcodes per Webcam, Bildschirmausschnitt, Datei oder"
    echo " Zwischenablage und kopiert, tippt oder öffnet den Inhalt."
} > "$deb/DEBIAN/control"
find "$deb" -type d -exec chmod 755 {} +
debname=lupe_amd64.deb
[ -n "$suffix" ] && debname="lupe_${version}_amd64.deb"
dpkg-deb --root-owner-group -Zxz --build "$deb" "$out/$debname" >/dev/null
echo "Gebaut: $out/$debname"
