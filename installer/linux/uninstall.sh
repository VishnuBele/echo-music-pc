#!/bin/sh
set -eu
INSTALL_DIR=${ECHOMUSIC_INSTALL_DIR:-"$HOME/.local/opt/echo-music"}
APPLICATIONS_DIR=${XDG_DATA_HOME:-"$HOME/.local/share"}/applications
rm -f "$APPLICATIONS_DIR/echo-music.desktop"
rm -rf "$INSTALL_DIR"
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi
printf 'Echo Music was removed from %s\n' "$INSTALL_DIR"
