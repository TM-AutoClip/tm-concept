#!/usr/bin/env sh
# Adds TM Concept to the application menu of the current user (no root). Run it from the unpacked tm-concept folder.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
icons="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
mkdir -p "$apps" "$icons"
cp "$here/tm-concept.png" "$icons/tm-concept.png"
sed "s#^Exec=.*#Exec=\"$here/tm-concept\"#" "$here/tm-concept.desktop" > "$apps/tm-concept.desktop"
chmod 644 "$apps/tm-concept.desktop"
echo "TM Concept was added to the application menu."
