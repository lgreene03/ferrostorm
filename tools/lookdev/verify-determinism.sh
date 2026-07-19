#!/bin/sh
# LOOK-01's acceptance criterion, made runnable: capture the three reference
# frames twice on an unchanged tree and compare them pixel for pixel.
#
#   tools/lookdev/verify-determinism.sh WORKDIR
#
# What "pass" means here, and why it is not simply `cmp`. On this machine
# (Apple M4, Metal, Godot 4.7) the shipped environment does NOT render
# bit-identically twice, and the harness is not the reason. Evidence, measured:
#
#   * With SSAO, SSIL, SSR, the glow pass, the volumetric fog, the depth of
#     field and every particle system disabled, six consecutive runs produce
#     six byte-identical PNGs. That is the proof that the scene, the saved
#     world, the camera, the animation state, the particle seeds and the frame
#     counts are all genuinely frozen: everything the harness is responsible
#     for is deterministic.
#   * Switch ANY ONE of those screen-space passes back on and a handful of
#     pixels out of 1,440,000 take one of two values, always in the same tight
#     cluster on one high-contrast vehicle edge. Which pass is on changes
#     nothing about which pixels move. That is a floating-point ordering
#     difference inside the driver's compute passes landing on a pixel that
#     already sat on a rounding boundary.
#   * The SIZE of that difference in bytes tracks the grading, not the harness:
#     before Wave V1 it was at most 4/255, and after V1 raised the tonemap
#     exposure the same pixels differ by up to 35/255. Nothing about the
#     capture changed. That is why the gate below counts pixels and takes a
#     whole-frame mean rather than capping a single pixel.
#
# So the gate is: fewer than 200 pixels of 1,440,000 may differ at all, and the
# mean absolute difference over the whole frame must stay below 0.01 of one
# level. The smallest ticket in Wave V1 moved 385,000 pixels, so this floor
# cannot hide a real change. If a future run reports numbers above the gate,
# the harness HAS broken, and the cause is almost certainly something new that
# is animating between runs.
set -eu

WORK=${1:?usage: verify-determinism.sh WORKDIR}
HERE=$(cd "$(dirname "$0")" && pwd)

rm -rf "$WORK/verify-a" "$WORK/verify-b"
"$HERE/capture.sh" "$WORK/verify-a" det >/dev/null
"$HERE/capture.sh" "$WORK/verify-b" det >/dev/null

exec python3 "$HERE/contact.py" --compare "$WORK/verify-a" "$WORK/verify-b" --prefix det
