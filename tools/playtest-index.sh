#!/usr/bin/env bash
# P7-28: which documents are waiting on a playtest, DERIVED rather than
# remembered - and which of them the current brief forgets to mention.
#
# This exists because of what P7-27 found: doc 24 claimed three things were
# missing that had shipped waves earlier, and the project's response to that
# class of problem had been a WARNING in the tracker header ("several of the
# design docs lag by whole waves") rather than a CHECK. A warning only helps a
# reader who already suspects. A playtest brief is exactly the kind of document
# that goes stale the same way: it is written once, the ADRs keep accumulating,
# and nobody notices the list stopped matching.
#
# It ASSERTS NOTHING about the game. It is a report, by ADR-061's gate-versus-
# probe rule: which ADRs want a human verdict is a fact about the documents, and
# whether the brief covers them is a judgement a reader makes in one glance.
#
# Exit code is 0 unless the brief itself is missing, so this is safe to run
# anywhere and is deliberately NOT wired into the CI battery.
set -uo pipefail
cd "$(dirname "$0")/.."

BRIEF="docs/tickets/P7-playtest-brief-2026-08-03.md"

printf '\n== documents naming a playtest ==\n\n'

# The search is over the whole of docs/, so a new ADR, ticket or question is
# picked up the day it is written. Nothing here is a hand-kept list.
# Read with a while-loop rather than `mapfile`, which is bash 4 and this project
# is developed on macOS where /bin/bash is 3.2. Found by running it.
WANTING=()
while IFS= read -r line; do
  [ -n "$line" ] && WANTING+=("$line")
done < <(grep -ril "playtest" docs/ 2>/dev/null | grep -v "playtest-brief" | sort)

if [ "${#WANTING[@]}" -eq 0 ]; then
  echo "  none - nothing in docs/ mentions a playtest."
  exit 0
fi

for f in "${WANTING[@]}"; do
  printf '  %s\n' "$f"
done
printf '\n  %d document(s).\n' "${#WANTING[@]}"

if [ ! -f "$BRIEF" ]; then
  printf '\nNO BRIEF at %s - there is nothing telling anyone what to play.\n' "$BRIEF"
  exit 1
fi

printf '\n== which of them the current brief does NOT mention ==\n\n'
printf '   Split in two, because the first run of this script buried the signal:\n'
printf '   21 documents were "not cited" and most were design docs using the word\n'
printf '   in passing. What matters is a DECISION waiting on a human verdict - an\n'
printf '   ADR overturning clause, a balance ticket, an open question - so those\n'
printf '   are listed first and separately. Nothing is hidden; it is sorted.\n\n'

# Matched by the document's IDENTIFIER (ADR-nnn, Q0nn, or the file stem), which
# is what a brief would cite, rather than by prose - prose comparison would be a
# guess dressed as a check.
missing=0
other=0
printf '  DECISIONS waiting on a verdict and not cited by the brief:\n'
for f in "${WANTING[@]}"; do
  base=$(basename "$f" .md)
  id=$(printf '%s' "$base" | grep -oE '^(ADR-[0-9]{3}|Q[0-9]{3}|BALANCE-[a-z-]+)' || true)
  if [ -z "$id" ]; then continue; fi
  if ! grep -q -- "$id" "$BRIEF"; then
    printf '    %-14s %s\n' "$id" "$f"
    missing=$((missing + 1))
  fi
done
[ "$missing" -eq 0 ] && printf '    none - every ADR, question and balance ticket naming a playtest is cited.\n'

printf '\n  Other documents mentioning a playtest (prose, not a pending decision):\n'
for f in "${WANTING[@]}"; do
  base=$(basename "$f" .md)
  if printf '%s' "$base" | grep -qE '^(ADR-[0-9]{3}|Q[0-9]{3}|BALANCE-[a-z-]+)'; then continue; fi
  printf '    %s\n' "$f"
  other=$((other + 1))
done
[ "$other" -eq 0 ] && printf '    none.\n'

if [ "$missing" -gt 0 ]; then
  printf '\n  %d decision(s) not cited. Not automatically wrong - an ADR may mention a\n' "$missing"
  printf '  playtest only in passing, or its question may already be answered. It is a\n'
  printf '  list to READ, not a failure - but a brief that omits a live question is how\n'
  printf '  the last stale document got that way.\n'
fi

printf '\nBrief: %s\n\n' "$BRIEF"
