#!/bin/sh
# LOOK-01: regenerate tools/lookdev/reference-state.fsav.
#
# You should almost never need this. The save is COMMITTED so that every
# capture, on every machine, on every day, loads a byte-identical world; the
# entire before-and-after method collapses if the battlefield differs between
# the two halves of a comparison. Regenerate only if the save format changes
# or the sim's own behaviour changes, and when you do, say so in the delivery
# notes and re-take every "before" capture you intend to compare against.
#
# This one CAN run --headless: it only steps the simulation and writes a file,
# and it never asks the GPU for anything.
set -eu

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
GODOT=${GODOT:-$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot}

exec "$GODOT" --headless --path "$ROOT/game" res://scenes/LookDev.tscn \
    --audio-driver Dummy --lookdev --lookdev-make-save
