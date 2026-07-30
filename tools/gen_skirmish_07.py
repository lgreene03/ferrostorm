#!/usr/bin/env python3
"""Generate data/maps/skirmish-07.fmap - "Karsthollow Basin", 256x192, two players.

The epic theatre. 49,152 cells, EIGHT TIMES a small map and twice the previous
big one, sized to the ceiling doc 26 section 5 measured rather than to the one
it used to assume.

This map exists to answer three separate gaps the design review found in the
pool as a set, and it answers them with one map because they are the same gap
seen from three sides.

FIRST, SIZE. Five of the seven maps are 96x64 and the pool inherited "192x128
is the tested ceiling" as though it were a limit. It never was: measured, the
steady-state cost of a match FALLS as the map grows, because what costs is unit
density and not area, and a large map spreads the same army out. What does scale
is the flow-field build, and at 256x192 that is 7 ms, inside a single frame.

SECOND, ARCHETYPE. Every other map in the pool is a crossing map: two or three
chokes, and the game is which one you commit to. That is a good formula and it
is now used six times. This map deliberately does NOT use it. There is no river,
no ridge line and no load-bearing crossing anywhere; the basin is broken up by
scattered karst massifs that leave many routes rather than few. Doc 26's "two to
three crossings" guidance is a rule for the duel maps and this map is not one,
which is stated here rather than quietly ignored - mapgen's crossing proof only
runs on a map that declares chokes, so a map with none is a deliberate shape and
not an unproven one.

THIRD, WHAT THE MAP REWARDS. With no choke to hold, holding ground stops being
about a gate and starts being about area, which is the macro game the GDD's
15-to-30 minute window has room for and no map currently asks for. The economy
is laid to suit: a small safe patch behind each base that will not win a game,
EIGHT expansion sites spread across the basin, and the richest ground in the
middle beside a pair of outposts - a prize that is genuinely central, genuinely
worth taking, and genuinely impossible to hold quietly, because everything can
reach it.

180-degree rotation symmetry about the map centre is the fairness invariant,
proved in tools/mapgen.py. Regenerate with:
    python3 tools/gen_skirmish_07.py data/maps/skirmish-07.fmap
"""
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 256, 192
# Opposite corners, 231 apart in Chebyshev against the 179 the new separation
# guard demands at this size. A macro map wants a long run: the whole point is
# that an army crossing the basin is a commitment you can see coming and answer.
STARTS = {0: (14, 168), 1: (241, 23)}
c = Canvas(W, H, STARTS, apron=5)

# ---- The karst: scattered massifs rather than a barrier. Each is placed on
# player 0's half and completed by rotation, so the basin is symmetric without
# any single feature dividing it. The sizes vary deliberately - a field of
# identical blocks reads as tiling, which is the fault the old skirmish-02 had.
MASSIFS = [
    (30, 130, 14, 10, 'h'), (58, 148, 11, 7, 'h'), (20, 96, 10, 13, 'h'),
    (52, 108, 16, 8, 'h'), (86, 132, 13, 10, 'h'), (34, 62, 12, 9, 'h'),
    (74, 78, 14, 10, 'h'), (104, 100, 11, 11, 'h'), (10, 44, 10, 8, 'h'),
    (62, 30, 13, 9, 'h'), (96, 54, 10, 10, 'h'), (118, 148, 10, 8, 'h'),
    (44, 172, 13, 6, 'r'), (92, 166, 11, 7, 'r'), (24, 12, 12, 7, 'r'),
    (126, 74, 9, 12, 'r'), (140, 118, 10, 9, 'r'), (8, 118, 8, 10, 'r'),
    (112, 24, 11, 8, 'r'), (70, 100, 7, 7, 'r'),
    (46, 88, 10, 3, 'f'), (100, 140, 11, 3, 'f'), (28, 40, 10, 3, 'f'),
    (130, 96, 3, 11, 'f'), (84, 116, 3, 10, 'f'),
]
for (x, y, dx, dy, ch) in MASSIFS:
    c.stamp(x, y, dx, dy, ch)

# ---- The economy, laid for expansion rather than for a choke.
SAFE = [(0, 0), (1, 0), (0, 1), (1, 1)]
SITE = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]

# A small safe patch behind each base. Four cells is an opening, not a game:
# a player who never leaves it loses to one who does, which is the whole
# premise of a macro map.
c.cluster(22, 158, SAFE)

# Eight expansion sites across player 0's half, each completed by rotation, so
# sixteen sites stand in the basin. They are spread rather than clustered so
# that taking a second and a third means covering real ground.
SITES = [
    (40, 118), (66, 140), (18, 78), (88, 156),
    (56, 46), (100, 84), (124, 128), (40, 34),
]
for (sx, sy) in SITES:
    c.cluster(sx, sy, SITE)

# The prize: the richest ground on the map, dead centre, beside the outposts.
# Placed off the exact centre so its rotation image is a distinct second patch -
# a single patch ON the centre would be its own mirror and pay one player first.
c.cluster(100, 80, SITE)
c.cluster(114, 80, SITE)

# ---- Neutral outposts (ADR-021). A central pair as the standing prize, and two
# mid-field pairs so an expanding player has something to take on the way.
c.outpost(120, 88)               # the centre, next to the rich ground
c.outpost(64, 120)               # mid-field, player 0's side of the basin
c.outpost(96, 40)                # and out on the northern flank

fields, blocked, density = c.validate(expected_fields=128, density_range=(0.08, 0.10))

# ---- Decoration (doc 26 s6): drawn, passable, outside the density budget. On
# a canvas this size it is doing real work rather than dressing: 49,152 cells of
# bare ground would read as emptiness, which is exactly what the review said
# about the corners of skirmish-04.
#
# Roads first, and they carry the map's legibility. On a basin with no chokes a
# player has no landmark to navigate by, so the tracks ARE the mental map: a
# ring road around the basin and two spurs into the centre say where the ground
# goes without telling anyone where to fight.
c.decor(0, 96, 256, 2, '=')          # the east-west trunk through the middle
c.decor(126, 0, 2, 192, '=')         # and the north-south
c.decor(30, 150, 90, 2, '=')         # the southern ring
c.decor(20, 60, 2, 60, '=')          # the western spur

# Gravel where an army will actually walk: the approaches to the centre prize.
c.decor(96, 78, 34, 30, ':', fill=2)
c.decor(40, 110, 26, 20, ':', fill=3)

# Scrub across the open basin floor, thinned so it scatters. These are the
# stretches between massifs that would otherwise be nothing at all.
c.decor(4, 100, 110, 80, ',', fill=4)
c.decor(140, 8, 110, 80, ',', fill=4)
c.decor(150, 100, 100, 84, ',', fill=6)
c.decor(6, 6, 100, 84, ',', fill=6)

path = sys.argv[1] if len(sys.argv) > 1 else "skirmish-07.fmap"
c.emit(path, [
    "# Karsthollow Basin, the epic theatre. 256x192, eight times a small map.",
    "# No river, no ridge and no chokepoint: the basin is broken by scattered",
    "# karst so there are many routes rather than two or three, and holding",
    "# ground means covering area instead of holding a gate. Sixteen expansion",
    "# sites and a rich centre beside the outposts. Generated by",
    "# tools/gen_skirmish_07.py; edit that script and regenerate rather than",
    "# editing this file by hand - the 180-degree rotation symmetry is the",
    "# fairness invariant and it is checked there (tools/mapgen.py).",
])
report("skirmish-07 (Karsthollow Basin)", c, fields, blocked, density, path, "open basin, no chokes")
