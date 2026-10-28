#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BUILD_ROOT="${BUILD_ROOT:-${TMPDIR:-/tmp}/echomusic-linux-package}"
PUBLISH_DIR="$BUILD_ROOT/publish-linux-x64"
BUNDLE_DIR="$BUILD_ROOT/EchoMusic-Linux-v2.0.1-x64"
OUT_ARCHIVE="${OUT_ARCHIVE:-$HOME/EchoMusic-Linux-v2.0.1-x64.tar.gz}"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"

rm -rf "$PUBLISH_DIR" "$BUNDLE_DIR"
"$DOTNET_BIN" publish "$PROJECT_DIR/EchoMusic.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false \
  -p:BaseIntermediateOutputPath="$BUILD_ROOT/obj/" \
  -p:MSBuildProjectExtensionsPath="$BUILD_ROOT/obj/" \
  -p:BaseOutputPath="$BUILD_ROOT/bin/" \
  -o "$PUBLISH_DIR"
mkdir -p "$BUNDLE_DIR"
cp -a "$PUBLISH_DIR/." "$BUNDLE_DIR/"
cp "$SCRIPT_DIR/linux/install.sh" "$SCRIPT_DIR/linux/uninstall.sh" "$PROJECT_DIR/README-LINUX.md" "$BUNDLE_DIR/"
chmod +x "$BUNDLE_DIR/EchoMusic" "$BUNDLE_DIR/install.sh" "$BUNDLE_DIR/uninstall.sh"
find "$BUNDLE_DIR" -type f \( -name '*.pdb' -o -name '*.xml' \) -delete
mkdir -p "$(dirname "$OUT_ARCHIVE")"
tar -czf "$OUT_ARCHIVE" -C "$BUILD_ROOT" "$(basename "$BUNDLE_DIR")"
echo "Built self-contained Linux x64 package: $OUT_ARCHIVE"
ls -lh "$OUT_ARCHIVE"
