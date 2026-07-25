#!/usr/bin/env bash
# Package Ferrostorm into a runnable build.
#
# There are two halves and the second is the one people forget. Godot exports
# everything under res:// into the package, but /data lives OUTSIDE the Godot
# project (it is the sim's runtime source under ADR-006, and the sim's loaders
# take real OS paths, which files inside an exported .pck are not). So the data
# folder is copied BESIDE the executable, which is the layout
# GameFiles.RepoRoot searches for. Skip that copy and the game exports cleanly
# and then refuses every match with catalogue errors.
#
# Usage:  tools/package.sh [macos|linux|windows|all] [--debug]
# Output: build/<platform>/<the game>  plus  build/<platform>/data
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="${GODOT:-$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot}"

TARGET="${1:-macos}"
case "$TARGET" in macos|linux|windows|all) ;; --debug) TARGET=macos ;; *) echo "unknown target '$TARGET' (macos|linux|windows|all)" >&2; exit 1 ;; esac
MODE="--export-release"
for a in "$@"; do [ "$a" = "--debug" ] && MODE="--export-debug"; done

[ -x "$GODOT" ] || { echo "godot not found at $GODOT (set GODOT=...)" >&2; exit 1; }

# export_presets.cfg is gitignored by long-standing convention (it is where a
# real signing identity would end up), so a fresh clone has none and the export
# would fail with an unhelpful "no preset" error. The committed template is the
# working default; copy it into place once and it is then the developer's own
# file to edit, never overwritten.
if [ ! -f "$ROOT/game/export_presets.cfg" ]; then
  echo "==> no export_presets.cfg, seeding it from the committed template"
  cp "$ROOT/game/export_presets.template.cfg" "$ROOT/game/export_presets.cfg"
fi

echo "==> building the C# assemblies (ExportRelease)"
dotnet build "$ROOT/game/Ferrostorm.Game.csproj" -c ExportRelease

# Stage /data beside a built game. rsync with --delete so a re-package never
# leaves a stale map or unit file behind, which would make the packaged build
# disagree with the repo.
stage_data() {
  local out="$1"
  echo "==> staging /data beside the build in $out"
  rsync -a --delete "$ROOT/data/" "$out/data/"
  for f in data/units data/buildings data/maps data/campaign data/fields; do
    [ -d "$out/$f" ] || { echo "missing $f beside the build" >&2; exit 1; }
  done
  local maps
  maps=$(find "$out/data/maps" -name '*.fmap' | wc -l | tr -d ' ')
  [ "$maps" -gt 0 ] || { echo "no maps staged" >&2; exit 1; }
  echo "    $maps maps staged"
}

package_macos() {
  local out="$ROOT/build/macos" app
  app="$out/Ferrostorm.app"
  echo "==> exporting macOS"
  rm -rf "$app"; mkdir -p "$out"
  "$GODOT" --headless --path "$ROOT/game" $MODE "macOS" "$app"
  [ -d "$app" ] || { echo "export produced no app bundle at $app" >&2; exit 1; }

  echo "==> re-signing without the hardened runtime"
  # Godot's macOS export signs ad-hoc AND enables the hardened runtime
  # (codesign flags 0x10002 = adhoc,runtime). macOS refuses to launch that
  # combination: the process is SIGKILLed at exec with no output at all, which
  # reads like a corrupt build rather than a signing policy. Re-signing plain
  # ad-hoc clears the runtime flag and the bundle launches.
  #
  # This is the LOCAL/TEST signature. Shipping to anyone else needs a real
  # Developer ID signature and notarisation, at which point the hardened
  # runtime comes back and stays (it is required for notarisation) and this
  # step must be dropped rather than kept.
  codesign --force --deep --sign - "$app"
  stage_data "$out"
  echo "packaged: $app"
}

package_linux() {
  local out="$ROOT/build/linux" bin
  bin="$out/Ferrostorm.x86_64"
  echo "==> exporting Linux"
  rm -rf "$out"; mkdir -p "$out"
  "$GODOT" --headless --path "$ROOT/game" $MODE "Linux" "$bin"
  [ -f "$bin" ] || { echo "export produced no linux binary at $bin" >&2; exit 1; }
  chmod +x "$bin"
  stage_data "$out"
  echo "packaged: $bin"
}

package_windows() {
  local out="$ROOT/build/windows" bin
  bin="$out/Ferrostorm.exe"
  echo "==> exporting Windows"
  rm -rf "$out"; mkdir -p "$out"
  "$GODOT" --headless --path "$ROOT/game" $MODE "Windows Desktop" "$bin"
  [ -f "$bin" ] || { echo "export produced no windows binary at $bin" >&2; exit 1; }
  stage_data "$out"
  echo "packaged: $bin"
}

case "$TARGET" in
  macos)   package_macos ;;
  linux)   package_linux ;;
  windows) package_windows ;;
  all)     package_macos; package_linux; package_windows ;;
esac

echo
echo "The game and its data folder must stay TOGETHER; moving the executable on"
echo "its own gives a game that starts and then refuses every match."
