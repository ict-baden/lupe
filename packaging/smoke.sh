#!/bin/sh
# SPDX-License-Identifier: MIT
# Rauchtest eines fertigen Pakets: Lupe liest den QR-Code in smoke-qr.png (neben diesem Skript).
#   smoke.sh <lupe-programm>
# Läuft auch in schlanken Containern (nur sh). Schlägt fehl, wenn der Code nicht gelesen wird – z. B. weil die native
# zxing-cpp-Bibliothek auf dem System nicht lädt (LUP-0001, Exitcode 3) oder ein Paket eine Abhängigkeit vergisst.
set -eu
lupe=$1
here=$(cd "$(dirname "$0")" && pwd)
expected=$(printf 'QR_CODE\thttps://github.com/ict-baden/lupe')
code=0
out=$("$lupe" lesen "$here/smoke-qr.png") || code=$?
if [ "$code" -ne 0 ] || [ "$out" != "$expected" ]; then
    echo "Rauchtest fehlgeschlagen (Exitcode $code): $out" >&2
    exit 1
fi
echo "Rauchtest bestanden: $out"
