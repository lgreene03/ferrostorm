#!/usr/bin/env python3
"""Generate data/maps/skirmish-02.fmap - "Ironback Ridge", 96x64, two players.

TICKET-P6-MAP-01, MAP redesign under ADR-013. This map replaces the old
skirmish-02: a scatter of straight rectangular blocked blocks on otherwise open
ground, with no reason to move through any particular part of the field. This is
its answer: a single ridgeline runs corner to corner, dividing the theatre into
two lands, and it is pierced by exactly three passes. There is no water here;
the terrain itself is the barrier and the decision.

Character: the ridge-and-passes map. The Ironback separates the two bases so
completely that the only ground routes between them are its three passes: a left
flank, a central saddle, and a right flank. Which pass an army commits to, and
which the defender chooses to hold, is the map. The central saddle is the direct
route and the widest, so it cannot be walled shut and out-ranges no artillery;
the flanks are the turning moves. Each base sits in a hill alcove; the larger
ferrite patch sits forward at the central saddle, contested ground by design.

180-degree rotation symmetry about the map centre is the fairness invariant,
proved in tools/mapgen.py. The ridge is built on player 0's half and completed
by rotation, so the two lands are identical. Regenerate with:
    python3 tools/gen_skirmish_02.py data/maps/skirmish-02.fmap
"""
import math
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 96, 64
STARTS = {0: (10, 53), 1: (85, 10)}   # a 180-rotation pair, the opposite diagonal to skirmish-01
c = Canvas(W, H, STARTS, apron=4)

# ---- The Ironback: a ridge running from the top-left corner to the bottom-
# right, along the map's main diagonal, so the bottom-left base and the top-
# right base fall into separate lands. A gentle wobble keeps it from reading as
# a ruled line. Built one column at a time on player 0's half; each stamp writes
# its rotation image too, so player 1's half is the same ridge turned 180.
SLOPE = (H - 1) / (W - 1)             # 63/95


def ridge_y(x):
    return x * SLOPE + 3.5 * math.sin(2 * math.pi * x / 23.0)


THICK = 2                              # 5 cells thick: no 4-connected leak through the staircase
# Passes as column ranges on player 0's half. The left flank at x=19..24; the
# central saddle at x=44..47 here, completed by rotation to x=48..51, so the
# saddle straddles the centre and is eight columns wide. Rotation turns the left
# flank into the right flank at x=71..76.
LEFT_FLANK = set(range(19, 25))
CENTRAL = set(range(44, 48))
pass_cols = LEFT_FLANK | CENTRAL

for x in range(0, 48):
    yc = round(ridge_y(x))
    lo, hi = yc - THICK, yc + THICK
    if x in pass_cols:
        # Leave the corridor open, but record it as a load-bearing crossing so
        # validate() can prove that sealing every pass disconnects the bases.
        corridor = [(x, y) for y in range(lo, hi + 1) if 0 <= y < H]
        c.mark_pass(corridor)
    else:
        span = [y for y in range(lo, hi + 1) if 0 <= y < H]
        if span:
            c.stamp(x, span[0], 1, len(span), 'h')

# ---- Base alcoves: a short hill spur behind each base gives it a back wall and
# a single defensible mouth, without sealing it (reachability is proved).
c.stamp(3, 44, 4, 3, 'h')        # spur west of base 0
c.stamp(15, 58, 8, 3, 'h')       # spur south of base 0

# ---- Rubble at the pass mouths: cover so a pass is fought through, not over
# open ground, and a little broken ground that funnels without blocking.
c.stamp(26, 40, 5, 4, 'r')       # rubble at the left-flank mouth
c.stamp(34, 44, 5, 3, 'r')       # rubble on the saddle approach
c.stamp(29, 48, 4, 3, 'h')       # forward knoll: a defensible mid-position in base 0's land
c.stamp(14, 30, 6, 2, 'f')       # fence line across the western lane
c.stamp(30, 55, 7, 2, 'f')       # fence line on the southern flank

# ---- Ferrite: 20 cells = 240,000 credits. A safe 4-cell patch by each base and
# a larger 6-cell patch forward at the central saddle, contested by both.
SAFE = [(0, 0), (1, 0), (0, 1), (1, 1)]
CONTEST = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
c.cluster(18, 47, SAFE)          # safe patch, near base 0
c.cluster(37, 39, CONTEST)       # contested patch below the central saddle

# ---- Neutral Outposts (ADR-021): one in each land, a forward income node worth
# an engineer and worth raiding. Placed well clear of the base aprons and of the
# three passes, so an outpost never part-seals a crossing (its 2x2 footprint
# blocks when it spawns) and neither base starts owning one. The pair is a
# rotation image, so the Chebyshev profiles match and neither side is nearer.
c.outpost(20, 20)                # in base 0's land, forward on the western lane
# A SECOND pair (design review): one outpost pair was the thinnest provision of
# any outpost-carrying map in the pool - skirmish-04 has two pairs and
# skirmish-06 four - which left the mechanic barely present on the map whose
# whole subject is choosing a lane. This one sits behind the left-flank pass,
# so committing to that flank and taking its income are the same decision.
c.outpost(28, 33)

# ---- Decoration (doc 26 s6): drawn, passable, outside the density budget.
# This map has no water and its entire identity is "which side of the Ironback
# am I on", which makes it the map most exposed to the orientation problem a
# rotationally symmetric layout creates: the two lands are geometrically
# identical, so with no texture cue a player cannot tell their half from the
# enemy's at a glance. The two lands are therefore dressed DIFFERENTLY on
# purpose - gravel and made tracks on the western land, scrub on the eastern -
# which is the one place in this pool where breaking visual symmetry is the
# correct answer, because the fairness invariant governs terrain and ferrite,
# not ground cover.
c.decor(2, 2, 44, 26, ':', fill=3)       # the western land reads dry and worn
c.decor(52, 38, 42, 24, ',', fill=3)     # the eastern reads overgrown

# A road along the central saddle. Doc 26 already intends the saddle to be the
# wide, unwallable, default route; the road says so to a player who has not yet
# learned the map (GDD pillar 1, readable in one glance).
c.decor(40, 26, 16, 2, '=')
c.decor(18, 40, 14, 2, '=')              # and a track to the left-flank pass

fields, blocked, density = c.validate(expected_fields=20, density_range=(0.08, 0.10))

path = sys.argv[1] if len(sys.argv) > 1 else "skirmish-02.fmap"
c.emit(path, [
    "# Ironback Ridge. One ridgeline divides the theatre corner to corner and is",
    "# pierced by three passes: a left flank, a central saddle and a right flank.",
    "# Which pass you commit to is the game. Generated by tools/gen_skirmish_02.py;",
    "# edit that script and regenerate rather than editing this file by hand - the",
    "# 180-degree rotation symmetry is the fairness invariant and it is checked",
    "# there (tools/mapgen.py).",
])
report("skirmish-02 (Ironback Ridge)", c, fields, blocked, density, path, "3 passes")
