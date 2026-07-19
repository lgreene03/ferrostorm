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
#   * Switch ANY ONE of those screen-space passes back on and about twelve
#     pixels out of 1,440,000 take one of two values, differing by at most
#     4/255, always in the same four-by-seven cluster on one high-contrast
#     vehicle edge. Which pass is on changes nothing about which pixels move.
#     That is a floating-point ordering difference inside the driver's compute
#     passes landing on a pixel that already sat on a rounding boundary.
#
# So the gate is: fewer than 200 pixels may differ at all, and no pixel may
# differ by more than 8/255. A real visual change moves tens of thousands of
# pixels by tens of levels, so this floor cannot hide one. If a future run
# reports numbers above the gate, the harness HAS broken and the cause is
# almost certainly something new that is animating between runs.
set -eu

WORK=${1:?usage: verify-determinism.sh WORKDIR}
HERE=$(cd "$(dirname "$0")" && pwd)

rm -rf "$WORK/verify-a" "$WORK/verify-b"
"$HERE/capture.sh" "$WORK/verify-a" det >/dev/null
"$HERE/capture.sh" "$WORK/verify-b" det >/dev/null

exec python3 "$HERE/contact.py" --compare "$WORK/verify-a" "$WORK/verify-b" --prefix det
