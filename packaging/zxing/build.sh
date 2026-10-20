#!/bin/bash
# SPDX-License-Identifier: MIT
# Baut die native zxing-cpp-Bibliothek selbst, statt der aus dem NuGet-Paket ZXingCpp zu nehmen.
#   build.sh <rid> <ausgabe-ordner>     rid: linux-x64 | linux-arm64 | osx-arm64 | osx-x64 | win-x64
# Ergebnis: <ausgabe-ordner>/<rid>/libZXing.so | libZXing.dylib | ZXing.dll
#
# Warum: Die Bibliotheken im NuGet-Paket sind auf zu neuen Systemen gebaut. libZXing.so braucht glibc 2.38
# (Debian 12 und Ubuntu 22.04 lesen damit keinen einzigen Code), libZXing.dylib verlangt macOS 26 (arm64)
# bzw. 15 (x64), und ZXing.dll braucht die VC++-Laufzeit, die kein Lupe-Paket mitbringt. Hier gebaut:
#   Linux: im Container manylinux_2_28 (glibc 2.28), Aufruf über docker – siehe unten
#   macOS: mit MACOSX_DEPLOYMENT_TARGET 12.0 (wie LSMinimumSystemVersion in packaging/macos/Info.plist)
#   Windows: mit statischer C/C++-Laufzeit (/MT), keine VC++-Laufzeit nötig
# Prüfen: packaging/zxing/check.sh. Die Version muss zur C-Schnittstelle passen, die das verwendete NuGet-Paket
# ZXingCpp (src/Lupe.Core/Lupe.Core.csproj) aufruft – siehe versions.env.
set -euo pipefail
rid=$1; out=$2
here=$(cd "$(dirname "$0")" && pwd)
# shellcheck source=versions.env
. "$here/versions.env"
mkdir -p "$out/$rid"
out=$(cd "$out" && pwd)
work=${ZXING_WORK:-$(mktemp -d)}
# Git Bash unter Windows: Pfade mit / für cmake, git und die MSYS-Werkzeuge gleichermaßen
if command -v cygpath >/dev/null 2>&1; then work=$(cygpath -m "$work"); out=$(cygpath -m "$out"); fi
jobs=$( (nproc || sysctl -n hw.ncpu) 2>/dev/null || echo 4)

# Linux: im manylinux-Container bauen (das Skript ruft sich dort selbst mit LUPE_ZXING_IN_CONTAINER=1 auf)
case "$rid" in
    linux-*)
        if [ "${LUPE_ZXING_IN_CONTAINER:-}" != 1 ]; then
            case "$rid" in
                linux-x64) image=$MANYLINUX_X64; platform=linux/amd64 ;;
                linux-arm64) image=$MANYLINUX_ARM64; platform=linux/arm64 ;;
                *) echo "Unbekannte Plattform: $rid" >&2; exit 2 ;;
            esac
            docker run --rm --platform "$platform" -e LUPE_ZXING_IN_CONTAINER=1 -e ZXING_WORK=/work \
                -v "$here:/scripts:ro" -v "$out:/out" "$image" bash /scripts/build.sh "$rid" /out
            exit 0
        fi
        ;;
esac

src="$work/zxing-cpp"
# Im Container gehört der Ordner einem anderen Benutzer als git – dort ausdrücklich erlauben
[ "${LUPE_ZXING_IN_CONTAINER:-}" = 1 ] && git config --global --add safe.directory '*'
if [ ! -d "$src/.git" ]; then
    git clone --quiet --depth 1 --branch "$ZXING_TAG" "$ZXING_REPO" "$src"
fi
actual=$(git -C "$src" rev-parse HEAD)
if [ "$actual" != "$ZXING_COMMIT" ]; then
    echo "zxing-cpp $ZXING_TAG ist $actual, erwartet $ZXING_COMMIT (Commit im NuGet-Paket ZXingCpp $ZXINGCPP_NUGET)" >&2
    exit 1
fi
# zint (Erzeugen von Codes: Demo-Daten und Tests) ist ein Untermodul
git -C "$src" submodule update --quiet --init --depth 1

# Wie im Workflow publish-dotnet.yml von zxing-cpp, aber ohne Beispiele und ohne die C-Testanwendung
# (die lädt stb aus dem Netz; STB_IMAGE_INCLUDE_DIR auf einen vorhandenen Ordner verhindert das)
args=(-S "$src" -B "$work/build" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON
      -DZXING_READERS=ON -DZXING_WRITERS=ON -DZXING_C_API=ON -DZXING_EXAMPLES=OFF
      -DZXING_BLACKBOX_TESTS=OFF -DZXING_UNIT_TESTS=OFF -DSTB_IMAGE_INCLUDE_DIR="$src/core/src")
case "$rid" in
    linux-*)
        file=libZXing.so
        built=core/libZXing.so
        ;;
    osx-*)
        file=libZXing.dylib
        built=core/libZXing.dylib
        arch=arm64; [ "$rid" = osx-x64 ] && arch=x86_64
        args+=(-DCMAKE_OSX_ARCHITECTURES=$arch -DCMAKE_OSX_DEPLOYMENT_TARGET="$MACOS_MIN")
        ;;
    win-x64)
        file=ZXing.dll
        built=core/Release/ZXing.dll
        # Statische C/C++-Laufzeit: keine VCRUNTIME140.dll/MSVCP140.dll auf dem Zielrechner nötig
        args+=(-A x64 -DZXING_LINK_CPP_STATICALLY=ON -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded)
        ;;
    *) echo "Unbekannte Plattform: $rid" >&2; exit 2 ;;
esac

cmake "${args[@]}"
cmake --build "$work/build" --config Release --target ZXing --parallel "$jobs"
# cp folgt dem Symlink libZXing.so -> libZXing.so.4 -> libZXing.so.<version>
cp -L "$work/build/$built" "$out/$rid/$file"
case "$rid" in
    linux-*) strip --strip-unneeded "$out/$rid/$file" ;;
    osx-*) strip -x "$out/$rid/$file" ;;
esac
cp "$src/LICENSE" "$out/$rid/zxing-cpp-LICENSE.txt"
echo "Gebaut: $out/$rid/$file (zxing-cpp $ZXING_TAG)"
