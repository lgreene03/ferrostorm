#!/usr/bin/env bash
# Package Ferrostorm into a runnable build (macOS).
#
# There are two halves and the second is the one people forget. Godot exports
# everything under res:// into the .app, but /data lives OUTSIDE the Godot
# project (it is the sim's runtime source under ADR-006, and the sim's loaders
# take real OS paths, which files inside an exported .pck are not). So the data
# folder is copied BESIDE the .app, which is the layout GameFiles.RepoRoot
# searches for. Skip that copy and the game exports cleanly and then refuses
# every match with "catalogue" errors.
#
# Usage:  tools/package.sh [--debug]
# Output: build/macos/Ferrostorm.app  plus  build/macos/data
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="${GODOT:-$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot}"
OUT="$ROOT/build/macos"
APP="$OUT/Ferrostorm.app"
PRESET="macOS"
MODE="--export-release"
[ "${1:-}" = "--debug" ] && MODE="--export-debug"

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

echo "==> exporting $PRESET"
rm -rf "$APP"
mkdir -p "$OUT"
# Godot resolves the preset's export_path relative to the project directory.
"$GODOT" --headless --path "$ROOT/game" $MODE "$PRESET" "$APP"

[ -d "$APP" ] || { echo "export produced no app bundle at $APP" >&2; exit 1; }

echo "==> re-signing without the hardened runtime"
# Godot's macOS export signs ad-hoc AND enables the hardened runtime
# (codesign flags 0x10002 = adhoc,runtime). macOS refuses to launch that
# combination: the process is SIGKILLed at exec with no output at all, which
# reads like a corrupt build rather than a signing policy. Re-signing plain
# ad-hoc clears the runtime flag and the bundle launches.
#
# This is the LOCAL/TEST signature. Shipping to anyone else needs a real
# Developer ID signature and notarisation, at which point the hardened runtime
# comes back and stays (it is required for notarisation).
codesign --force --deep --sign - "$APP"

echo "==> staging /data beside the app"
# The game reads these at runtime, so they ship loose rather than baked in.
# rsync with --delete so a re-package never leaves a stale map or unit file
# behind, which would make the packaged build disagree with the repo.
rsync -a --delete "$ROOT/data/" "$OUT/data/"

echo "==> verifying"
for f in data/units data/buildings data/maps data/campaign data/fields; do
  [ -d "$OUT/$f" ] || { echo "missing $f beside the app" >&2; exit 1; }
done
MAPS=$(find "$OUT/data/maps" -name '*.fmap' | wc -l | tr -d ' ')
[ "$MAPS" -gt 0 ] || { echo "no maps staged" >&2; exit 1; }

echo
echo "packaged: $APP"
echo "data:     $OUT/data ($MAPS maps)"
echo
echo "The .app and the data folder must stay TOGETHER; moving the .app on its"
echo "own gives a game that starts and then refuses every match."
