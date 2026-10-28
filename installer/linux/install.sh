#!/bin/sh
set -eu
HERE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
INSTALL_DIR=${ECHOMUSIC_INSTALL_DIR:-"$HOME/.local/opt/echo-music"}
APPLICATIONS_DIR=${XDG_DATA_HOME:-"$HOME/.local/share"}/applications
DESKTOP_FILE="$APPLICATIONS_DIR/echo-music.desktop"

if [ ! -f "$HERE/EchoMusic" ]; then
  echo "EchoMusic executable is missing. Extract the complete Linux archive first." >&2
  exit 1
fi
mkdir -p "$INSTALL_DIR" "$APPLICATIONS_DIR"
cp -a "$HERE/." "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/EchoMusic" "$INSTALL_DIR/install.sh" "$INSTALL_DIR/uninstall.sh"
cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=Echo Music
Comment=Native desktop music player
Exec="$INSTALL_DIR/EchoMusic" %U
Icon=$INSTALL_DIR/Assets/echo-logo.png
Terminal=false
Categories=AudioVideo;Audio;Player;
StartupWMClass=EchoMusic
EOF
chmod 644 "$DESKTOP_FILE"
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi
printf 'Echo Music installed to %s\n' "$INSTALL_DIR"
printf 'Launch it from your applications menu or run: "%s/EchoMusic"\n' "$INSTALL_DIR"
printf 'To uninstall, run: "%s/uninstall.sh"\n' "$INSTALL_DIR"
