#!/bin/bash
# SPDX-License-Identifier: MIT
# Prüft eine selbst gebaute zxing-cpp-Bibliothek (packaging/zxing/build.sh):
#   check.sh <rid> <datei>
# - lädt auf den unterstützten Systemen (packaging/check-natives.sh: glibc, macOS-Mindestversion, keine VC++-Laufzeit)
# - hat die C-Schnittstelle, die Lupe über das NuGet-Paket ZXingCpp braucht
# - versions.env passt zur Version von ZXingCpp in src/Lupe.Core/Lupe.Core.csproj und zu LSMinimumSystemVersion
set -euo pipefail
rid=$1; file=$2
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
# shellcheck source=versions.env
. "$here/versions.env"
fail=0
bad() { echo "FEHLER: $*" >&2; fail=1; }

nuget=$(sed -n 's/.*PackageReference Include="ZXingCpp" Version="\([^"]*\)".*/\1/p' "$repo/src/Lupe.Core/Lupe.Core.csproj")
[ "$nuget" = "$ZXINGCPP_NUGET" ] || bad "NuGet ZXingCpp ist $nuget, versions.env nennt $ZXINGCPP_NUGET – Tag und Commit in versions.env anpassen"
plist=$(sed -n 's:.*<key>LSMinimumSystemVersion</key><string>\([^<]*\)</string>.*:\1:p' "$repo/packaging/macos/Info.plist")
[ "$plist" = "$MACOS_MIN" ] || bad "LSMinimumSystemVersion ist $plist, versions.env nennt $MACOS_MIN"

[ -f "$file" ] || { echo "FEHLER: $file fehlt" >&2; exit 1; }
"$repo/packaging/check-natives.sh" "$rid" "$file" || fail=1

case "$rid" in
    linux-*)
        exports=$(objdump -T "$file" | awk '$NF ~ /^ZXing_/ { print $NF }')
        for n in $(readelf -d "$file" | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p'); do
            case "$n" in
                libc.so.6|libm.so.6|libstdc++.so.6|libgcc_s.so.1|libpthread.so.0|libdl.so.2|librt.so.1|ld-linux*) ;;
                *) bad "unerwartete Abhängigkeit $n" ;;
            esac
        done
        ;;
    osx-*)
        exports=$(nm -gU "$file" | awk '{ print $NF }' | sed -n 's/^_\(ZXing_.*\)/\1/p')
        for lib in $(otool -L "$file" | tail -n +2 | awk '{ print $1 }'); do
            case "$lib" in
                @rpath/libZXing*|/usr/lib/libc++.1.dylib|/usr/lib/libSystem.B.dylib) ;;
                *) bad "unerwartete Abhängigkeit $lib" ;;
            esac
        done
        ;;
    win-*)
        if command -v dumpbin >/dev/null 2>&1; then
            imports=$(dumpbin -nologo -dependents "$file" | tr -d '\r' | sed -n 's/^ *\([^ ]*\.[dD][lL][lL]\)$/\1/p')
            exports=$(dumpbin -nologo -exports "$file" | tr -d '\r' | awk '{ print $NF }' | grep '^ZXing_' || true)
        else
            imports=$(llvm-readobj --coff-imports "$file" | tr -d '\r' | sed -n 's/.*Name: \(.*\.[dD][lL][lL]\)$/\1/p')
            exports=$(llvm-readobj --coff-exports "$file" | tr -d '\r' | sed -n 's/.*Name: \(ZXing_.*\)/\1/p')
        fi
        # Statisch gebaut (/MT): auch die Universal CRT (api-ms-win-crt-*, ucrtbase) steckt in der DLL
        crt=$(echo "$imports" | grep -iE '^(ucrtbase|api-ms-win-crt-)' | tr '\n' ' ' || true)
        [ -z "$crt" ] || bad "nicht statisch gebaut, importiert $crt"
        ;;
    *) echo "Unbekannte Plattform: $rid" >&2; exit 2 ;;
esac

# Die C-Schnittstelle, die Lupe über ZXingCpp braucht (Lesen; Erzeugen für Demo-Daten und Tests). Vollständig prüft
# Lupe selbst beim ersten Lesen (ZXingLibrary: jede DllImport-Funktion von ZXingCpp) – der Rauchtest deckt das ab.
for f in ZXing_ReadBarcodes ZXing_ReaderOptions_new ZXing_ReaderOptions_setTextMode ZXing_ImageView_new ZXing_Barcode_bytes \
         ZXing_Barcode_extra ZXing_Barcode_position ZXing_CreatorOptions_new ZXing_WriteBarcodeToImage ZXing_Version; do
    echo "$exports" | grep -qx "$f" || bad "Export $f fehlt"
done
echo "$(echo "$exports" | grep -c . || true) Exporte ZXing_*"

[ $fail -eq 0 ] && echo "Geprüft: $file ($rid, zxing-cpp $ZXING_TAG)" || { echo "Prüfung fehlgeschlagen: $file ($rid)" >&2; exit 1; }
