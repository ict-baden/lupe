#!/bin/sh
# SPDX-License-Identifier: MIT
# Lupe für den aktuellen Benutzer einrichten (ohne root): Programm nach ~/.local/opt/lupe,
# Befehl „lupe“ in ~/.local/bin, Eintrag im Anwendungsmenü. Entfernen: ./install.sh --entfernen
set -e
src=$(cd "$(dirname "$0")" && pwd)
dest="${XDG_DATA_HOME:-$HOME/.local/share}"
opt="$HOME/.local/opt/lupe"
bin="$HOME/.local/bin"

if [ "${1:-}" = "--entfernen" ]; then
    rm -rf "$opt" "$bin/lupe" "$dest/applications/lupe.desktop" "$dest/icons/hicolor/256x256/apps/lupe.png"
    echo "Lupe entfernt. Einstellungen und Verlauf bleiben in ~/.config/lupe und ~/.local/share/lupe."
    exit 0
fi

mkdir -p "$HOME/.local/opt" "$bin" "$dest/applications" "$dest/icons/hicolor/256x256/apps"
if [ "$src" != "$opt" ]; then
    rm -rf "$opt"
    cp -R "$src" "$opt"
fi
chmod +x "$opt/Lupe" "$opt/install.sh"
ln -sf "$opt/Lupe" "$bin/lupe"
cp "$opt/lupe.png" "$dest/icons/hicolor/256x256/apps/lupe.png"
sed "s|^Exec=.*|Exec=\"$opt/Lupe\"|" "$opt/lupe.desktop" > "$dest/applications/lupe.desktop"
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database "$dest/applications" >/dev/null 2>&1 || true; fi
echo "Lupe ist eingerichtet: im Anwendungsmenü oder mit dem Befehl „lupe“ (liegt in $bin)."
