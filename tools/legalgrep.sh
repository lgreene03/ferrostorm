#!/usr/bin/env bash
# The legal check, `legalgrep` (P8-2, criterion F14; decision D31 in
# docs/tickets/P8-formidable-tracker.md, which closed Q011).
#
# WHY THIS EXISTS. CLAUDE.md's Legal rule was absolute and nothing enforced it.
# Ten announcer lines in the genre classics' own phrasing were voiced, shipped
# and wired to the alerts, the GDD, which outranks every document but the TDD,
# carried protected names in its pillars and its audio line, and the pipeline
# comments named the very material they told the reader to avoid. A rule that
# depends on someone noticing is a rule that has already been broken, so it is
# a check now, and both halves FAIL on any hit:
#
#   names    the protected names over every tree that ships (game/, art/,
#            data/, sim/, the README) and over docs/, minus the documents in
#            tools/legal-allowlist.txt. The allowlist is the rule's single
#            exception: a mark cannot be cleared without being named.
#   phrases  the ten retired announcer lines wherever a player hears or reads
#            them: the VO generator art/audio/make_vo.sh and data/, searched
#            whole, and game/scripts, searched for STRING LITERALS on lines
#            that are not comments. An identifier such as vo_unit_lost or
#            ConstructionComplete speaks to no player and cannot match (the
#            patterns need a space between words), and a comment explaining
#            what an alert is for is filtered, because a guard that forbids
#            naming the thing gets the explanation deleted instead of the
#            defect (the seat check in determinism.yml reasons the same way).
#
# The patterns live in tools/legalgrep-patterns.txt and never here, so the
# check cannot match its own source; tools/ is not scanned. It reads files
# through git grep, so it sees tracked files plus untracked ones that are not
# ignored (a new file is checked before anyone adds it) and never the
# gitignored import cache or build output.
#
# Exits 0 when clean, 1 on any hit or on any failure to run, because a check
# that cannot read its patterns would otherwise pass everything.
set -uo pipefail
cd "$(dirname "$0")/.."

PATTERNS=tools/legalgrep-patterns.txt
ALLOWLIST=tools/legal-allowlist.txt
# Everything that ships or is a working document. tools/ is absent on purpose:
# it holds the patterns file, which has to spell what it forbids.
NAME_SCOPE=(game art data sim docs README.md)
# What a player hears or reads, searched whole.
PHRASE_SCOPE=(art/audio/make_vo.sh data)
# Searched for string literals on non-comment lines only (see above).
LITERAL_SCOPE=(game/scripts)

report=""   # problems with the check itself: config, patterns, git
hits=""     # file:line:text, one per hit, merged across patterns at the end
add() { report="$report$1"$'\n'; }
hit() { hits="$hits$1"$'\n'; }

# git grep exits 0 on a match, 1 on none and above 1 when it cannot run. The
# last must never read as "no match", so it is reported as a failure.
OUT=""
grab() {
  OUT=$(git grep -n -I --untracked -E "$@" 2>&1)
  local rc=$?
  if [ "$rc" -gt 1 ]; then
    add "git grep could not run (exit $rc): $OUT"
    OUT=""
  elif [ "$rc" -eq 1 ]; then
    OUT=""
  fi
  return 0
}

[ -f "$PATTERNS" ] || add "the patterns file $PATTERNS is missing"
[ -f "$ALLOWLIST" ] || add "the allowlist $ALLOWLIST is missing"

# --- the allowlist ------------------------------------------------------------
allowed=()
excludes=()
if [ -f "$ALLOWLIST" ]; then
  while read -r path _; do
    case "$path" in ''|\#*) continue ;; esac
    case "$path" in
      docs/*) ;;
      *) add "allowlist: $path is outside docs/. The exception is for documents that discuss the marks, never for anything that ships (D31)"
         continue ;;
    esac
    if [ ! -f "$path" ]; then
      add "allowlist: $path does not exist; remove the entry"
      continue
    fi
    allowed+=("$path")
    excludes+=(":(exclude)$path")
  done < "$ALLOWLIST"
fi

# --- the patterns -------------------------------------------------------------
names=0
phrases=0
named_allowed=""   # allowlisted files that really do carry a protected name
if [ -f "$PATTERNS" ]; then
  while read -r kind flags pat; do
    case "$kind" in ''|\#*) continue ;; esac
    opts=()
    case "$flags" in *i*) opts+=(-i) ;; esac
    case "$kind" in
      name)
        names=$((names + 1))
        case "$flags" in *w*) opts+=(-w) ;; esac
        grab ${opts[@]+"${opts[@]}"} -e "$pat" -- "${NAME_SCOPE[@]}" ${excludes[@]+"${excludes[@]}"}
        [ -n "$OUT" ] && hit "$OUT"
        if [ "${#allowed[@]}" -gt 0 ]; then
          grab ${opts[@]+"${opts[@]}"} -l -e "$pat" -- "${allowed[@]}"
          [ -n "$OUT" ] && named_allowed="$named_allowed$OUT"$'\n'
        fi
        ;;
      phrase)
        phrases=$((phrases + 1))
        grab ${opts[@]+"${opts[@]}"} -e "(^|[^[:alnum:]_])$pat(\$|[^[:alnum:]_])" -- "${PHRASE_SCOPE[@]}"
        [ -n "$OUT" ] && hit "$OUT"
        grab ${opts[@]+"${opts[@]}"} -e "\"([^\"]*[^[:alnum:]_\"])?$pat([^[:alnum:]_\"][^\"]*)?\"" -- "${LITERAL_SCOPE[@]}"
        if [ -n "$OUT" ]; then
          OUT=$(printf '%s\n' "$OUT" | grep -vE '^[^:]+:[0-9]+:[[:space:]]*(//|/\*|\*)' || true)
          [ -n "$OUT" ] && hit "$OUT"
        fi
        ;;
      *)
        add "patterns: unknown kind '$kind' (expected name or phrase)"
        ;;
    esac
  done < "$PATTERNS"
fi

# A check with no patterns passes everything, so an empty half is a failure.
[ "$names" -gt 0 ] || add "patterns: no name patterns were read"
[ "$phrases" -gt 0 ] || add "patterns: no phrase patterns were read"

# An allowlisted document that names nothing no longer needs the exception.
for path in ${allowed[@]+"${allowed[@]}"}; do
  case "$named_allowed" in
    *"$path"*) ;;
    *) add "allowlist: $path carries no protected name; remove the entry" ;;
  esac
done

if [ -n "$report" ] || [ -n "$hits" ]; then
  printf '%s' "$report"
  # One line per hit however many patterns found it, cut to a readable width.
  [ -n "$hits" ] && printf '%s' "$hits" | sort -u | cut -c1-240
  echo "legalgrep: FAILED. Reword the hit (\"the genre's classics\", \"inspired by the classic RTS games of the 90s\"); allowlist a document only if its purpose is clearance, legal analysis or competitor research (D31)."
  exit 1
fi
echo "legalgrep: clean ($names name patterns, $phrases phrase patterns, ${#allowed[@]} allowlisted documents)"
exit 0
