#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BUILD_ROOT="${BUILD_ROOT:-${TMPDIR:-/tmp}/echomusic-build}"
PUBLISH_DIR="${PUBLISH_DIR:-$BUILD_ROOT/publish-win-x64}"
SETUP_UI_DIR="$BUILD_ROOT/publish-custom-setup"
CORE_SETUP="$BUILD_ROOT/EchoMusic-CoreSetup-v2.0.0.exe"
OUT_EXE="${OUT_EXE:-$HOME/EchoMusic-Setup-v2.0.0-x64.exe}"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
SIGNTOOL_BIN="${SIGNTOOL_BIN:-signtool}"

rm -rf "$PUBLISH_DIR" "$SETUP_UI_DIR" "$BUILD_ROOT/obj" "$BUILD_ROOT/bin" \
  "$BUILD_ROOT/setup-ui-obj" "$BUILD_ROOT/setup-ui-bin" "$CORE_SETUP"

"$DOTNET_BIN" publish "$PROJECT_DIR/EchoMusic.csproj" \
  -c Release \
  -r win-x64 \
  -p:BuildWindows=true \
  -p:BaseIntermediateOutputPath="$BUILD_ROOT/obj/" \
  -p:MSBuildProjectExtensionsPath="$BUILD_ROOT/obj/" \
  -p:BaseOutputPath="$BUILD_ROOT/bin/" \
  --self-contained true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$PUBLISH_DIR"

# Generate an explicit per-file payload list so the custom setup UI can show real byte-based progress.
PAYLOAD_INCLUDE="$BUILD_ROOT/payload-files.nsh"
PAYLOAD_BYTES_FILE="$BUILD_ROOT/payload-bytes.txt"
python3 - "$PUBLISH_DIR" "$PROJECT_DIR" "$PAYLOAD_INCLUDE" "$PAYLOAD_BYTES_FILE" <<'PY'
from pathlib import Path
import sys
publish, project, include_path, total_path = map(Path, sys.argv[1:])
files=[]
for source in sorted(publish.rglob('*')):
    if not source.is_file():
        continue
    rel=source.relative_to(publish)
    if rel.as_posix().lower() == 'echo-emerald-v20.ico':
        continue
    files.append((rel, source, source.stat().st_size))
files.extend([
    (Path('echo-emerald-v20.ico'), project/'Assets/echo.ico', (project/'Assets/echo.ico').stat().st_size),
    (Path('LICENSE.txt'), project/'LICENSE.txt', (project/'LICENSE.txt').stat().st_size),
])
lines=['!macro InstallPayload']
total=0
for rel, source, size in files:
    dest_dir=rel.parent.as_posix()
    nsis_dir='$INSTDIR' if dest_dir == '.' else '$INSTDIR\\' + dest_dir.replace('/', '\\')
    source_text=source.resolve().as_posix()
    if '"' in source_text or '"' in rel.name or '$' in source_text:
        raise SystemExit(f'Unexpected NSIS-special character in path: {source_text}')
    lines.append(f'  SetOutPath "{nsis_dir}"')
    lines.append(f'  File /oname={rel.name} "{source_text}"')
    lines.append(f'  IntOp $R1 $R1 + {size}')
    lines.append('  FileOpen $R2 "$INSTDIR\\.echo-install-progress" w')
    lines.append('  FileWrite $R2 "$R1"')
    lines.append('  FileClose $R2')
    total += size
lines.append('!macroend')
Path(include_path).write_text('\n'.join(lines)+'\n', encoding='utf-8')
Path(total_path).write_text(str(total), encoding='ascii')
print(f'Generated payload manifest: {len(files)} files, {total:,} bytes')
PY

# Build the full offline installation engine. The user-facing Avalonia setup launches this silently.
makensis -DPUBLISH_DIR="$PUBLISH_DIR" -DPAYLOAD_INCLUDE="$PAYLOAD_INCLUDE" -DOUT_FILE="$CORE_SETUP" "$SCRIPT_DIR/echo-setup.nsi"

sign_binary() {
  local target="$1"
  local args=(sign /fd SHA256 /f "$SIGN_CERT_FILE")
  if [[ -n "${SIGN_CERT_PASSWORD:-}" ]]; then args+=(/p "$SIGN_CERT_PASSWORD"); fi
  args+=(/tr "${SIGN_TIMESTAMP_URL:-http://timestamp.digicert.com}" /td SHA256 "$target")
  "$SIGNTOOL_BIN" "${args[@]}"
}

if [[ -n "${SIGN_CERT_FILE:-}" ]]; then
  [[ -f "$SIGN_CERT_FILE" ]] || { echo "SIGN_CERT_FILE does not exist: $SIGN_CERT_FILE" >&2; exit 2; }
  for binary in "$PUBLISH_DIR"/*.exe; do [[ -e "$binary" ]] && sign_binary "$binary"; done
  sign_binary "$CORE_SETUP"
fi

"$DOTNET_BIN" publish "$SCRIPT_DIR/ui/EchoMusicSetup.csproj" \
  -c Release \
  -r win-x64 \
  -p:BaseIntermediateOutputPath="$BUILD_ROOT/setup-ui-obj/" \
  -p:MSBuildProjectExtensionsPath="$BUILD_ROOT/setup-ui-obj/" \
  -p:BaseOutputPath="$BUILD_ROOT/setup-ui-bin/" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$SETUP_UI_DIR"

[[ -f "$SETUP_UI_DIR/EchoMusicSetup.exe" ]] || { echo "Custom installer UI was not produced: $SETUP_UI_DIR/EchoMusicSetup.exe" >&2; exit 3; }
cp "$PAYLOAD_BYTES_FILE" "$SETUP_UI_DIR/install-payload-bytes.txt"
mkdir -p "$(dirname "$OUT_EXE")"
makensis -DCUSTOM_SETUP_DIR="$SETUP_UI_DIR" -DCORE_SETUP="$CORE_SETUP" -DOUT_FILE="$OUT_EXE" "$SCRIPT_DIR/outer-setup.nsi"
if [[ -n "${SIGN_CERT_FILE:-}" ]]; then sign_binary "$OUT_EXE"; fi
if [[ -z "${SIGN_CERT_FILE:-}" ]]; then
  echo "No trusted Authenticode certificate was configured. The custom installer UI is still unsigned; SmartScreen or antivirus reputation warnings may occur."
fi
echo "Built custom Avalonia Windows installer: $OUT_EXE"
ls -lh "$OUT_EXE"
