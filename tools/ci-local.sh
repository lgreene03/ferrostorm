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
fail=0
step() { printf '%-46s' "$1"; }
ok()   { echo "ok"; }
bad()  { echo "FAIL"; fail=1; }

step "sim purity (no float/double/Random/Godot)"
if grep -rnE '\b(float|double|System\.Random|Godot)\b' sim/Ferrostorm.Sim/ >/tmp/ci-purity.txt 2>&1; then
  bad; cat /tmp/ci-purity.txt
else ok; fi

step "portability (no engine ref outside /game)"
if grep -rln --include='*.cs' 'using Godot' sim/ tools/ data/ >/tmp/ci-port.txt 2>&1; then
  bad; cat /tmp/ci-port.txt
else ok; fi

step "hardcoded seat in the battle scene"
HITS=$(grep -nE 'PlayerId (==|!=) [0-9]+\b|_world\.Credits\([0-9]+\)|UpdateFrom\(_world, [0-9]+\)|IsVisible\([0-9]+,|IsExplored\([0-9]+,|ValidPlacement\([0-9]+,|_winner (==|!=) [0-9]+\b' game/scripts/SkirmishLive.cs | grep -vE '^[0-9]+:[[:space:]]*//' || true)
if [ -n "$HITS" ]; then bad; echo "$HITS"; else ok; fi

step "seat inverted by a ternary"
HITS=$(grep -rnE '(==|!=)[[:space:]]*0[[:space:]]*\?[[:space:]]*1[[:space:]]*:[[:space:]]*0\b|(==|!=)[[:space:]]*1[[:space:]]*\?[[:space:]]*0[[:space:]]*:[[:space:]]*1\b' game/scripts/ | grep -vE ':[[:space:]]*//' || true)
if [ -n "$HITS" ]; then bad; echo "$HITS"; else ok; fi

step "team colour keyed on the viewer's seat"
HITS=$(grep -rnE '(LocalPlayerId|EnemyPlayerId).*\?.*(DirectorateMark|SodalityMark)' game/scripts/ | grep -vE ':[[:space:]]*//' || true)
if [ -n "$HITS" ]; then bad; echo "$HITS"; else ok; fi

step "hardcoded player-0 faction gate in sidebar"
if grep -nE 'FactionOf\(0\)' game/scripts/Sidebar.cs >/tmp/ci-sb.txt 2>&1; then
  bad; cat /tmp/ci-sb.txt
else ok; fi

step "build"
if dotnet build sim/Ferrostorm.Sim.Runner -c Release >/tmp/ci-build.txt 2>&1; then ok; else bad; tail -20 /tmp/ci-build.txt; fi

run_mode() {
  step "$1"
  if dotnet run --project sim/Ferrostorm.Sim.Runner -c Release --no-build -- $2 >"/tmp/ci-$1.txt" 2>&1; then ok
  else bad; tail -6 "/tmp/ci-$1.txt"; fi
}
run_mode selftest      "selftest"
run_mode determinism   "determinism 2026"

step "golden (ORDERED diff, as CI does it)"
dotnet run --project sim/Ferrostorm.Sim.Runner -c Release --no-build -- golden 2026 >/tmp/ci-got.txt 2>&1
grep -v '^#' sim/golden-hashes.txt >/tmp/ci-want.txt
if diff /tmp/ci-got.txt /tmp/ci-want.txt >/tmp/ci-golden.txt 2>&1; then ok; else bad; cat /tmp/ci-golden.txt; fi

run_mode match         "match 2026"
run_mode lan           "lan 5"
run_mode lanchaos      "lanchaos 1 60 30"
run_mode spectate      "spectate"
run_mode replay        "replay"
run_mode saveload      "saveload"
run_mode campaignsave  "campaignsave"

step "balance gate"
if dotnet build tools/Ferrostorm.Balance -c Release >/tmp/ci-bb.txt 2>&1 \
   && dotnet run --project tools/Ferrostorm.Balance -c Release --no-build >/tmp/ci-balance.txt 2>&1; then ok
else bad; tail -6 /tmp/ci-balance.txt; fi

echo
if [ "$fail" -eq 0 ]; then
  echo "ci-local: every gate CI runs on this machine is green."
  echo "NOT covered here: Windows determinism, and the client harness"
  echo "(tools/verify-client.sh - run it separately, it needs the Godot editor)."
else
  echo "ci-local: FAILED. Do not push."
fi
exit "$fail"
