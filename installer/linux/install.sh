#!/bin/sh
set -eu
HERE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
INSTALL_DIR=${ECHOMUSIC_INSTALL_DIR:-"$HOME/.local/opt/echo-music"}
DATA_HOME=${XDG_DATA_HOME:-"$HOME/.local/share"}
APPLICATIONS_DIR="$DATA_HOME/applications"
ICON_DIR="$DATA_HOME/icons/hicolor/256x256/apps"
DESKTOP_FILE="$APPLICATIONS_DIR/echo-music.desktop"

if [ ! -f "$HERE/EchoMusic" ]; then
  echo "EchoMusic executable is missing. Extract the complete Linux archive first." >&2
  exit 1
fi
if [ ! -f "$HERE/.self-contained" ]; then
  if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft.NETCore.App 8\.'; then
    echo "Echo Music Linux requires the .NET 8 runtime. Install Microsoft.NETCore.App 8.0, then run this installer again." >&2
    exit 1
  fi
fi
mkdir -p "$INSTALL_DIR" "$APPLICATIONS_DIR" "$ICON_DIR"
cp -a "$HERE/." "$INSTALL_DIR/"
if [ -f "$HERE/Assets/echo-logo.png" ]; then
  cp "$HERE/Assets/echo-logo.png" "$ICON_DIR/echo-music.png"
fi
chmod +x "$INSTALL_DIR/EchoMusic" "$INSTALL_DIR/install.sh" "$INSTALL_DIR/uninstall.sh"
cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=Echo Music
Comment=Native desktop music player
Exec="$INSTALL_DIR/EchoMusic" %U
Icon=$ICON_DIR/echo-music.png
Terminal=false
Categories=AudioVideo;Audio;Player;
StartupWMClass=EchoMusic
StartupNotify=true
EOF
chmod 644 "$DESKTOP_FILE"
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi
printf 'Echo Music installed to %s\n' "$INSTALL_DIR"
printf 'Launch it from your applications menu or run: "%s/EchoMusic"\n' "$INSTALL_DIR"
printf 'To uninstall, run: "%s/uninstall.sh"\n' "$INSTALL_DIR"
