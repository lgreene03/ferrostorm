#!/bin/sh
# TICKET-P6-VO-01: the battlefield voice, regenerable in one command.
#
# One `say` line per clip, macOS system text-to-speech (voice Daniel, en_GB,
# matching the project's British voice), converted with afconvert to the
# project wav format: 16-bit 44.1 kHz mono WAVE, the same shape every
# synthesised asset in game/audio carries (verified against ui_confirm.wav
# with afinfo).
#
# LEGAL CAVEAT, stated rather than buried (doc 24). It has two halves, and for
# a long time it had only the first.
#
# The VOICE: system text-to-speech output is a PLACEHOLDER. Redistribution
# licensing for Apple voices must be cleared by legal-review before any public
# release build ships these clips; this script is the mitigation, because
# replacing the voice is one command.
#
# The WORDS: every line below is original phrasing in the game's own
# vocabulary (ferrite, the uplink, brown-out, the field), written for P8-2
# under decision D26. The first version of this list voiced the genre
# classics' own announcer lines word for word, which CLAUDE.md's Legal rule
# and the audio charter both forbid, and this caveat said nothing about it
# because it covered only the voice. A line must do its job without echoing a
# famous announcer or unit line from ANY well-known RTS: never restore a
# retired line, and check a new one against the genre before adding it.
# tools/legalgrep.sh fails CI on the ten retired phrasings here, in data/ and
# in string literals in game/scripts, but it cannot recognise a new echo; that
# check is a human one. (vo_unit_ready's first draft ended on a stock unit
# acknowledgement from the classics and was replaced before it shipped.)
set -e

VOICE="Daniel"
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../../game/audio/vo"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$OUT"

gen() {
  say -v "$VOICE" -o "$TMP/$1.aiff" "$2"
  afconvert -f WAVE -d LEI16@44100 -c 1 "$TMP/$1.aiff" "$OUT/$1.wav"
  echo "$1.wav  \"$2\""
}

# The clip list per doc 24. Silos needed is omitted on purpose: no silo
# system exists to warn about.
# The file names are the client's keys (SkirmishLive.PlayVo) and stay as they
# were; only the words changed.
gen vo_construction_complete  "Structure ready for placement."
gen vo_unit_ready             "Fresh unit on the field."
gen vo_unit_lost              "Casualty reported."
gen vo_base_under_attack      "Hostiles are hitting the base."
gen vo_harvester_under_attack "Harvester is taking fire."
gen vo_low_power              "Brown-out. Defences going dark."
gen vo_radar_offline          "Uplink lost. We are blind."
gen vo_superweapon_launch     "Enemy strike inbound. Brace."
gen vo_mission_accomplished   "The field is ours."
gen vo_mission_failed         "The field is lost. Fall back."
# P8-10: the critical events that used to happen in silence. Same rule as
# every line above: the game's own words, no echo of any classic announcer
# (whose superweapon lines are "<weapon> ready", "<weapon> charging" and
# "<weapon> detected"; none of these takes that shape).
gen vo_superweapon_ready       "Our strike is charged. Choose the ground."
gen vo_enemy_superweapon       "Enemy strike platform sighted. The clock is running."
gen vo_enemy_superweapon_ready "Enemy strike platform charged. Get clear."
gen vo_radar_jammed            "Uplink jammed. Static on every band."
gen vo_sabotaged               "Sabotage. A structure has gone dark."

echo "done: $(ls "$OUT" | grep -c '\.wav$') clips in $OUT"
