#!/bin/bash
# SPDX-License-Identifier: MIT
# Prüft alle nativen Bibliotheken und Programme einer Veröffentlichung darauf, dass sie auf den unterstützten Systemen laden:
#   check-natives.sh <rid> <datei-oder-ordner>...
#   Linux:   höchstens glibc $GLIBC_MAX und libstdc++ $GLIBCXX_MAX (packaging/limits.env)
#   macOS:   minos höchstens LSMinimumSystemVersion (packaging/macos/Info.plist), richtige Architektur
#   Windows: keine VC++-Laufzeit (VCRUNTIME*, MSVCP*) unter den Importen – kein Lupe-Paket bringt sie mit
# Linux und macOS mit Bordmitteln (objdump/readelf bzw. otool/lipo), Windows (Git Bash) mit dumpbin oder llvm-readobj.
set -euo pipefail
rid=$1; shift
here=$(cd "$(dirname "$0")" && pwd)
# shellcheck source=limits.env
. "$here/limits.env"
fail=0
bad() { echo "FEHLER: $*" >&2; fail=1; }

# Größte Versionsnummer aus einer Liste (eine je Zeile), ohne sort -V (fehlt unter macOS teils)
maxver() { awk -F. 'NF { printf "%05d%05d%05d %s\n", $1, $2, $3, $0 }' | sort | tail -n 1 | cut -d' ' -f2; }
# a <= b für Versionsnummern
le() { [ "$(printf '%s\n%s\n' "$1" "$2" | maxver)" = "$2" ]; }

files=()
for p in "$@"; do
    if [ -d "$p" ]; then
        while IFS= read -r f; do files+=("$f"); done < <(find "$p" -type f | sort)
    else
        files+=("$p")
    fi
done

macmin=$(sed -n 's:.*<key>LSMinimumSystemVersion</key><string>\([^<]*\)</string>.*:\1:p' "$here/macos/Info.plist")
checked=0
for f in "${files[@]}"; do
    case "$rid" in
        linux-*)
            head -c 4 "$f" | grep -q 'ELF' || continue
            checked=$((checked + 1))
            glibc=$(objdump -T "$f" 2>/dev/null | grep -o 'GLIBC_[0-9.]*' | sed 's/GLIBC_//' | maxver || true)
            cxx=$(objdump -T "$f" 2>/dev/null | grep -o 'GLIBCXX_[0-9.]*' | sed 's/GLIBCXX_//' | maxver || true)
            [ -z "$glibc" ] || le "$glibc" "$GLIBC_MAX" || bad "$(basename "$f") braucht glibc $glibc (erlaubt $GLIBC_MAX)"
            [ -z "$cxx" ] || le "$cxx" "$GLIBCXX_MAX" || bad "$(basename "$f") braucht GLIBCXX_$cxx (erlaubt $GLIBCXX_MAX)"
            arch=$(readelf -h "$f" | sed -n 's/.*Machine: *//p')
            case "$rid:$arch" in
                linux-x64:*X86-64*|linux-arm64:*AArch64*) ;;
                *) bad "$(basename "$f"): Architektur $arch passt nicht zu $rid" ;;
            esac
            echo "  glibc ${glibc:--} libstdc++ ${cxx:--}  $(basename "$f")"
            ;;
        osx-*)
            file -b "$f" | grep -q 'Mach-O' || continue
            checked=$((checked + 1))
            minos=$(otool -l "$f" | awk '/LC_BUILD_VERSION/ { b = 1 } b && $1 == "minos" { print $2; exit }')
            [ -n "$minos" ] || minos=$(otool -l "$f" | awk '/LC_VERSION_MIN_MACOSX/ { b = 1 } b && $1 == "version" { print $2; exit }')
            le "$minos" "$macmin" || bad "$(basename "$f"): minos $minos liegt über LSMinimumSystemVersion $macmin"
            want=arm64; [ "$rid" = osx-x64 ] && want=x86_64
            archs=$(lipo -archs "$f")
            echo " $archs " | grep -q " $want " || bad "$(basename "$f"): Architektur $archs, erwartet $want"
            echo "  minos $minos  $archs  $(basename "$f")"
            ;;
        win-*)
            case "$f" in *.dll|*.DLL|*.exe|*.EXE) ;; *) continue ;; esac
            checked=$((checked + 1))
            if command -v dumpbin >/dev/null 2>&1; then
                imports=$(dumpbin -nologo -dependents "$f" | tr -d '\r' | sed -n 's/^ *\([^ ]*\.[dD][lL][lL]\)$/\1/p')
            else
                imports=$(llvm-readobj --coff-imports "$f" | tr -d '\r' | sed -n 's/.*Name: \(.*\.[dD][lL][lL]\)$/\1/p')
            fi
            vc=$(echo "$imports" | grep -iE '^(vcruntime|msvcp|concrt)' | tr '\n' ' ' || true)
            [ -z "$vc" ] || bad "$(basename "$f") braucht die VC++-Laufzeit: $vc"
            echo "  $(basename "$f"): $(echo $imports)"
            ;;
        *) echo "Unbekannte Plattform: $rid" >&2; exit 2 ;;
    esac
done
[ "$checked" -gt 0 ] || bad "keine nativen Dateien gefunden in: $*"
[ $fail -eq 0 ] && echo "Geprüft: $checked native Dateien ($rid)" || { echo "Prüfung fehlgeschlagen ($rid)" >&2; exit 1; }
