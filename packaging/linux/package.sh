#!/bin/bash
# SPDX-License-Identifier: MIT
# Linux-Pakete aus einer Veröffentlichung (dotnet publish -r linux-<arch> --self-contained):
#   package.sh <publish-ordner> <arch: x64|arm64> <version> <ausgabe-ordner> [<dateisuffix>]
# Ergebnis: Lupe<suffix>-linux-<arch>.tar.gz, für x64 zusätzlich das .deb (dpkg-deb):
#   ohne Suffix (main) lupe_amd64.deb – fester Link auf „latest“, mit Suffix (Tag) lupe_<version>_amd64.deb;
#   der Paketname darin ist ictbaden-lupe (wie alle ICT-Baden-Pakete), der Dateiname bleibt wegen der festen Links
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
cp "$here/lupe.desktop" "$here/install.sh" "$here/README.txt" "$here/LIESMICH.txt" "$work/tar/lupe/"
cp "$repo/src/Lupe/Assets/lupe.png" "$work/tar/lupe/lupe.png"
chmod 755 "$work/tar/lupe/Lupe" "$work/tar/lupe/install.sh"
tar -C "$work/tar" --owner=0 --group=0 -czf "$out/Lupe$suffix-linux-$arch.tar.gz" lupe
echo "Gebaut: $out/Lupe$suffix-linux-$arch.tar.gz"

[ "$arch" = "x64" ] || exit 0

# .deb: /opt/lupe, /usr/bin/lupe, Menüeintrag und Symbol
deb="$work/deb"
mkdir -p "$deb/DEBIAN" "$deb/opt/lupe" "$deb/usr/bin" "$deb/usr/share/applications" \
         "$deb/usr/share/icons/hicolor/256x256/apps" "$deb/usr/share/doc/ictbaden-lupe"
cp -R "$pub"/. "$deb/opt/lupe/"
chmod -R u=rwX,go=rX "$deb/opt/lupe"
chmod 755 "$deb/opt/lupe/Lupe"
ln -s /opt/lupe/Lupe "$deb/usr/bin/lupe"
install -m 644 "$here/lupe.desktop" "$deb/usr/share/applications/lupe.desktop"
install -m 644 "$repo/src/Lupe/Assets/lupe.png" "$deb/usr/share/icons/hicolor/256x256/apps/lupe.png"
install -m 644 "$repo/THIRD-PARTY-NOTICES.txt" "$deb/usr/share/doc/ictbaden-lupe/THIRD-PARTY-NOTICES.txt"
# copyright nach der Debian-Richtlinie (maschinenlesbares Format): Lupes MIT-Lizenz, Verweis auf die Drittanbieter
{
    echo "Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/"
    echo "Upstream-Name: Lupe"
    echo "Source: https://github.com/ict-baden/lupe"
    echo
    echo "Files: *"
    echo "Copyright: $(sed -n 's/^Copyright (c) //p' "$repo/LICENSE" | head -n 1)"
    echo "License: MIT"
    echo "Comment: The bundled third-party libraries and their licences are listed in"
    echo " /usr/share/doc/ictbaden-lupe/THIRD-PARTY-NOTICES.txt"
    echo
    echo "License: MIT"
    sed -e '1,/^Copyright/d' "$repo/LICENSE" | sed -e '/./,$!d' -e 's/^$/./' -e 's/^/ /'
} > "$deb/usr/share/doc/ictbaden-lupe/copyright"
chmod 644 "$deb/usr/share/doc/ictbaden-lupe/copyright"
# „0.1.0-ci.12“: der Bindestrich wäre eine Debian-Revision – „0.1.0~ci.12“ sortiert vor 0.1.0
debversion=${version//-/\~}
size=$(du -sk "$deb" | cut -f1)
# Mindest-glibc aus den mitgelieferten Bibliotheken (wie dpkg-shlibdeps): ein zu altes System lehnt das Paket ab,
# statt dass Lupe dort nicht startet oder nichts liest. libstdc++/libgcc braucht die .NET-Laufzeit und zxing-cpp.
glibc=$(find "$deb/opt/lupe" -type f \( -name '*.so' -o -name Lupe -o -name createdump \) -exec objdump -T {} \; 2>/dev/null \
    | grep -o 'GLIBC_[0-9.]*' | sed 's/GLIBC_//' | sort -t. -k1,1n -k2,2n -k3,3n | tail -n 1)
[ -n "$glibc" ] || { echo "glibc-Version der Bibliotheken nicht ermittelt (objdump fehlt?)" >&2; exit 1; }
{
    # Paketname ictbaden-<name>, einheitlich für alle ICT-Baden-Pakete; ersetzt das frühere Paket „lupe“ sauber
    echo "Package: ictbaden-lupe"
    echo "Version: $debversion"
    echo "Architecture: amd64"
    echo "Provides: lupe"
    echo "Conflicts: lupe"
    echo "Replaces: lupe"
    # Eine eigene Adresse statt der von GitHub; eine echte Kontaktadresse für Pakete steht noch aus
    echo "Maintainer: ICT Baden GmbH <noreply@ict-baden.de>"
    echo "Installed-Size: $size"
    echo "Depends: libc6 (>= $glibc), libstdc++6, libgcc-s1 | libgcc1, libx11-6, libxtst6, libxrandr2, libxi6, libice6, libsm6, libfontconfig1, libicu78 | libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67 | libicu66"
    echo "Section: utils"
    echo "Priority: optional"
    echo "Homepage: https://ict-baden.de/produkte/lupe/"
    echo "Description: QR and barcode reader"
    echo " Reads QR codes and barcodes from the webcam, a screen region, image files"
    echo " or the clipboard and copies, types or opens the content."
} > "$deb/DEBIAN/control"
find "$deb" -type d -exec chmod 755 {} +
debname=lupe_amd64.deb
[ -n "$suffix" ] && debname="lupe_${version}_amd64.deb"
dpkg-deb --root-owner-group -Zxz --build "$deb" "$out/$debname" >/dev/null
echo "Gebaut: $out/$debname"
