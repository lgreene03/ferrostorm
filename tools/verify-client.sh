#!/usr/bin/env bash
# Drive the real battle scene headless and assert on what it does.
#
# The sim has twenty-four golden hashes and a dozen gates; the client has had
# nothing but "it compiles". That gap is why the same defect shape shipped four
# times, each time code that looked implemented and was dead. This closes it.
#
# Exits nonzero if any check fails, so CI can fail on it.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="${GODOT:-$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot}"
[ -x "$GODOT" ] || { echo "godot not found at $GODOT (set GODOT=...)" >&2; exit 1; }

dotnet build "$ROOT/game/Ferrostorm.Game.csproj" -c Debug

# A FRESH CHECKOUT has no .godot/ (it is gitignored), and a project whose assets
# have never been imported does not fail loudly: the models simply are not there,
# the actor loop builds nothing, and every check that reads the view fails with
# an empty-looking scene. Ten of them did, which is how this was found.
#
# The test is on .godot/IMPORTED, not on .godot itself, and that is the whole
# trick: the dotnet build above writes .godot/mono, so a directory test on
# .godot is ALWAYS true by the time it runs and the import is never triggered.
if [ -z "$(ls -A "$ROOT/game/.godot/imported" 2>/dev/null)" ]; then
  echo "verify: no imported assets, running one import pass first (fresh checkout)"
  "$GODOT" --headless --audio-driver Dummy --path "$ROOT/game" --import
fi

LOG=$(mktemp)
set +e
"$GODOT" --headless --audio-driver Dummy --path "$ROOT/game" res://scenes/Verify.tscn > "$LOG" 2>&1
rc=$?
set -e
grep -E '^verify:|^  ok |^  FAIL ' "$LOG" || true

# Godot has been known to exit 0 after a Quit(1) on some platforms, so the
# verdict LINE is the authority and the exit code is corroboration. Trusting
# only the exit code is how a red harness silently goes green.
if grep -q '^verify: PASS' "$LOG" && [ "$rc" -eq 0 ]; then
  rm -f "$LOG"; exit 0
fi
echo "client verification FAILED (exit $rc)" >&2
exit 1
