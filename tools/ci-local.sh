#!/usr/bin/env bash
# Run every gate CI runs, locally, in CI's own form.
#
# WHY THIS EXISTS. A bare `dotnet run` is ONE of CI's eleven sim steps, and
# three merges went to main red because "the battery is green" was read as "CI
# will pass". They are not the same claim and never were:
#
#   - `golden`, `campaignsave`, `saveload`, `replay`, `spectate`, `lanchaos` and
#     the balance tool are all separate runner modes.
#   - The banned-token guards are shell greps that run no code at all, so a
#     perfect battery says nothing about them. The one that actually bit was the
#     word "double" written in a COMMENT in /sim, which CLAUDE.md forbids with
#     no exemption for comments and which no test could ever catch.
#   - The golden check in CI is an ORDERED diff. A sorted comparison passes
#     where CI fails.
#
# Run this before every push. It is not a substitute for CI, which also runs on
# Windows and drives the real client; it is the part that can be known early.
set -uo pipefail
cd "$(dirname "$0")/.."
# Each run gets its OWN log directory. Fixed /tmp paths meant two worktrees
# running this gate at once overwrote each other's logs, so a step's evidence
# could be another lane's output (verdicts were unaffected: they come from
# exit codes). The directory is kept so a failure stays inspectable.
L=$(mktemp -d "${TMPDIR:-/tmp}/ci-local.XXXXXX")
fail=0
step() { printf '%-46s' "$1"; }
ok()   { echo "ok"; }
bad()  { echo "FAIL"; fail=1; }

step "sim purity (no float/double/Random/Godot)"
if grep -rnE '\b(float|double|System\.Random|Godot)\b' sim/Ferrostorm.Sim/ >$L/purity.txt 2>&1; then
  bad; cat $L/purity.txt
else ok; fi

step "portability (no engine ref outside /game)"
if grep -rln --include='*.cs' 'using Godot' sim/ tools/ data/ >$L/port.txt 2>&1; then
  bad; cat $L/port.txt
else ok; fi

step "hardcoded seat in the battle scene"
# The command CONSTRUCTOR was missing from this list, which is the one place
# C7b's ninety-three-site plumbing most had to hold: a Command's second
# argument IS the seat, and a literal there is refused by World.ApplyCommand
# at any seat but zero, silently and only in LAN. Measured: all 27 call sites
# pass LocalPlayerId or c.PlayerId, so this costs nothing and catches the
# regression the rest of this grep walked straight past.
HITS=$(grep -nE 'PlayerId (==|!=) [0-9]+\b|_world\.Credits\([0-9]+\)|UpdateFrom\(_world, [0-9]+\)|IsVisible\([0-9]+,|IsExplored\([0-9]+,|ValidPlacement\([0-9]+,|_winner (==|!=) [0-9]+\b|new Command\([^,]+,[[:space:]]*[0-9]+,' game/scripts/SkirmishLive.cs | grep -vE '^[0-9]+:[[:space:]]*//' || true)
if [ -n "$HITS" ]; then bad; echo "$HITS"; else ok; fi

step "seat inverted by a ternary"
HITS=$(grep -rnE '(==|!=)[[:space:]]*0[[:space:]]*\?[[:space:]]*1[[:space:]]*:[[:space:]]*0\b|(==|!=)[[:space:]]*1[[:space:]]*\?[[:space:]]*0[[:space:]]*:[[:space:]]*1\b' game/scripts/ | grep -vE ':[[:space:]]*//' || true)
if [ -n "$HITS" ]; then bad; echo "$HITS"; else ok; fi

step "team colour keyed on the viewer's seat"
HITS=$(grep -rnE '(LocalPlayerId|EnemyPlayerId).*\?.*(DirectorateMark|SodalityMark)' game/scripts/ | grep -vE ':[[:space:]]*//' || true)
if [ -n "$HITS" ]; then bad; echo "$HITS"; else ok; fi

step "hardcoded player-0 faction gate in sidebar"
if grep -nE 'FactionOf\(0\)' game/scripts/Sidebar.cs >$L/sb.txt 2>&1; then
  bad; cat $L/sb.txt
else ok; fi

# P8-2 (D31): CLAUDE.md's Legal rule, as CI runs it. Patterns, allowlist and
# scope live in tools/legalgrep-patterns.txt, tools/legal-allowlist.txt and
# the script's own header.
step "legal (protected names, retired VO phrasing)"
if bash tools/legalgrep.sh >$L/legal.txt 2>&1; then ok; else bad; cat $L/legal.txt; fi

step "build"
if dotnet build sim/Ferrostorm.Sim.Runner -c Release >$L/build.txt 2>&1; then ok; else bad; tail -20 $L/build.txt; fi

run_mode() {
  step "$1"
  if dotnet run --project sim/Ferrostorm.Sim.Runner -c Release --no-build -- $2 >"$L/$1.txt" 2>&1; then ok
  else bad; tail -6 "$L/$1.txt"; fi
}
run_mode selftest      "selftest"
run_mode determinism   "determinism 2026"

step "golden (ORDERED diff, as CI does it)"
dotnet run --project sim/Ferrostorm.Sim.Runner -c Release --no-build -- golden 2026 >$L/got.txt 2>&1
grep -v '^#' sim/golden-hashes.txt >$L/want.txt
if diff $L/got.txt $L/want.txt >$L/golden.txt 2>&1; then ok; else bad; cat $L/golden.txt; fi

run_mode match         "match 2026"
run_mode lan           "lan 5"
run_mode lanchaos      "lanchaos 1 60 30"
run_mode spectate      "spectate"
run_mode replay        "replay"
run_mode saveload      "saveload"
run_mode campaignsave  "campaignsave"

step "balance gate"
if dotnet build tools/Ferrostorm.Balance -c Release >$L/bb.txt 2>&1 \
   && dotnet run --project tools/Ferrostorm.Balance -c Release --no-build >$L/balance.txt 2>&1; then ok
else bad; tail -6 $L/balance.txt; fi

echo
if [ "$fail" -eq 0 ]; then
  echo "ci-local: every gate CI runs on this machine is green."
  echo "NOT covered here: Windows determinism, and the client harness"
  echo "(tools/verify-client.sh - run it separately, it needs the Godot editor)."
else
  echo "ci-local: FAILED. Do not push."
fi
echo "logs: $L"
exit "$fail"
