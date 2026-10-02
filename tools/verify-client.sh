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
# An OLD checkout fails the same way for the assets added since its last
# import. Testing for an empty .godot/imported caught only the fresh case, so a
# branch that added six music tracks failed every score check locally with
# "missing" while CI, importing from scratch, would have passed; the same stale
# cache had been playing the announcer lines P8-2 regenerated in their old
# form. The import pass is incremental (it re-imports only new or changed
# files, about two seconds when nothing has changed), so it simply runs every
# time and its chatter goes to a log unless it fails.
IMPORT_LOG=$(mktemp)
echo "verify: import pass (incremental; a fresh checkout imports everything)"
if ! "$GODOT" --headless --audio-driver Dummy --path "$ROOT/game" --import > "$IMPORT_LOG" 2>&1; then
  cat "$IMPORT_LOG"
  echo "verify: the import pass failed" >&2
  exit 1
fi
rm -f "$IMPORT_LOG"

LOG=$(mktemp)
set +e
"$GODOT" --headless --audio-driver Dummy --path "$ROOT/game" res://scenes/Verify.tscn > "$LOG" 2>&1
rc=$?
set -e
# KNOWN-MISSING and EXCEPTION are the inputgate's table of verbs a player
# cannot yet issue (P8-1): shown, never counted as failures, so the gap stays
# visible while the harness stays green.
grep -E '^verify:|^  ok |^  FAIL |^  KNOWN-MISSING |^  EXCEPTION ' "$LOG" || true

# Godot has been known to exit 0 after a Quit(1) on some platforms, so the
# verdict LINE is the authority and the exit code is corroboration. Trusting
# only the exit code is how a red harness silently goes green.
if grep -q '^verify: PASS' "$LOG" && [ "$rc" -eq 0 ]; then
  rm -f "$LOG"; exit 0
fi
echo "client verification FAILED (exit $rc)" >&2
exit 1
