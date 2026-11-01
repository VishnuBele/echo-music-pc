#!/bin/sh
set -eu
INSTALL_DIR=${ECHOMUSIC_INSTALL_DIR:-"$HOME/.local/opt/echo-music"}
DATA_HOME=${XDG_DATA_HOME:-"$HOME/.local/share"}
APPLICATIONS_DIR="$DATA_HOME/applications"
ICON_DIR="$DATA_HOME/icons/hicolor/256x256/apps"
rm -f "$APPLICATIONS_DIR/echo-music.desktop"
rm -f "$ICON_DIR/echo-music.png"
rm -rf "$INSTALL_DIR"
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi
printf 'Echo Music was removed from %s\n' "$INSTALL_DIR"
