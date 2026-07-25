#!/bin/sh
# LOOK-01: take the three reference captures. This script IS the invocation
# contract: nothing about a capture may be typed by hand, because a flag typed
# differently on two days makes the two captures incomparable and the whole
# look-development loop rests on them being comparable.
#
#   tools/lookdev/capture.sh OUTDIR TAG [extra Godot flags...]
#
# Extra flags exist for doc 25 section 3's two day-one experiments:
#   --lookdev-fog0      volumetric fog density forced to 0
#   --lookdev-noshroud  the fog-of-war shroud plane hidden
#   --lookdev-hud       keep the HUD visible (it is hidden by default; see below)
#
# The HUD is hidden by default because the measurements in LOOK-02 are about
# the battlefield and a sidebar occupying a fifth of the frame would dominate
# every one of them. Pass --lookdev-hud when the question is about the HUD.
#
# NOT --headless. Godot's headless mode selects the dummy rasterizer, which
# renders nothing at all, so a "headless" capture would be a blank PNG. The
# audio driver is the part that genuinely has to be silenced offscreen
# (game/README-GODOT.md: CoreAudio init can hang forever waiting on a
# permission prompt with no window to show it in).
set -eu

OUT=${1:?usage: capture.sh OUTDIR TAG [flags...]}
TAG=${2:?usage: capture.sh OUTDIR TAG [flags...]}
shift 2

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
GODOT=${GODOT:-$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot}

# A LOCKED CONSOLE CANNOT CAPTURE, so refuse loudly rather than hang silently.
# Diagnosed 2026-07-26: with the macOS console locked, WindowServer suspends a
# GUI app's event loop entirely - Godot initialises Metal, then blocks forever
# in nextEventMatchingMask with the engine loop never iterating once. It is a
# full stall, not a slowdown: an unattended run sat 56 minutes on the first
# camera with zero output. Nothing an unattended session can set fixes it
# (caffeinate, --disable-vsync and NSAppSleepDisabled were each tried and
# disproven - the block is the session lock, not sleep, vsync or App Nap), and
# headless is no escape because this build pairs the headless display driver
# with the dummy renderer only. A human unlocking the machine is the fix, so
# say exactly that and exit.
if [ "$(uname)" = "Darwin" ] && ioreg -n Root -d1 -a | grep -A1 IOConsoleLocked | grep -q '<true/>'; then
    echo "capture.sh: the macOS console is LOCKED, so WindowServer will suspend" >&2
    echo "Godot's event loop and the capture will hang forever producing nothing." >&2
    echo "Unlock the machine (physically or via Screen Sharing) and rerun." >&2
    exit 2
fi

mkdir -p "$OUT"

# ONE PROCESS PER CAMERA, and this is not an efficiency choice. The volumetric
# fog runs temporal reprojection, so what a frame looks like depends on every
# frame the process drew before it. Shooting all three cameras in one process
# gives CAM-B a fog history containing CAM-A; returning to CAM-A afterwards and
# shooting it again produced sixteen thousand differing pixels, up to 64/255,
# with the scene frozen. Three cold starts, three identical histories, one
# acceptance criterion that holds.
#
# --fixed-fps 60 pins the delta the scene tree hands to tweens, particles and
# the water scroll, so the warm-up is the same length of simulated time on a
# fast machine and a slow one. It does not cap the loop, so the frame-time
# number the harness prints is still a real measurement.
for CAM in camA camB camC; do
    "$GODOT" --path "$ROOT/game" res://scenes/LookDev.tscn \
        --audio-driver Dummy \
        --fixed-fps 60 \
        --lookdev \
        "--lookdev-out=$OUT" \
        "--lookdev-tag=$TAG" \
        "--lookdev-cam=$CAM" \
        "$@"
done
