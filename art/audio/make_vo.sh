#!/bin/sh
# TICKET-P6-VO-01: the battlefield voice, regenerable in one command.
#
# One `say` line per clip, macOS system text-to-speech (voice Daniel, en_GB,
# matching the project's British voice), converted with afconvert to the
# project wav format: 16-bit 44.1 kHz mono WAVE, the same shape every
# synthesised asset in game/audio carries (verified against ui_confirm.wav
# with afinfo).
#
# LEGAL CAVEAT, stated rather than buried (doc 24): system text-to-speech
# output is a PLACEHOLDER. Redistribution licensing for Apple voices must be
# cleared by legal-review before any public release build ships these clips;
# this script is the mitigation, because replacing the voice is one command.
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
gen vo_construction_complete  "Construction complete."
gen vo_unit_ready             "Unit ready."
gen vo_unit_lost              "Unit lost."
gen vo_base_under_attack      "Our base is under attack."
gen vo_harvester_under_attack "Harvester under attack."
gen vo_low_power              "Base power low."
gen vo_radar_offline          "Radar offline."
gen vo_superweapon_launch     "Superweapon launch detected."
gen vo_mission_accomplished   "Mission accomplished."
gen vo_mission_failed         "Mission failed."

echo "done: $(ls "$OUT" | grep -c '\.wav$') clips in $OUT"
