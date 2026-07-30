#!/usr/bin/env python3
"""Generate data/maps/skirmish-08.fmap - "Tidewrack Sound", 128x96, two players.

The mid tier. The pool ran 96x64 five times, then 192x128 once, then 256x192
once, with nothing between the fast duel and the big theatre - a 5:1:1 skew the
design review called a real hole in the offering. 12,288 cells sits squarely in
the gap: twice a duel map, half the old big one.

Character: the map where water is a BOUNDARY rather than a barrier. Every other
map in the pool that carries water uses it as something to cross - the
Serpentine, the Tarnwater and the Ashford are all rivers with bridges, and the
game on them is which crossing you commit to. There is not one bridge on this
map and there is nothing to cross. The Sound intrudes from two opposite corners
and simply removes them, leaving a broad diagonal of land running corner to
corner between the bases.

What that changes is the shape of a fight rather than its route. With the sea on
one flank and the sea on the other, neither commander can be turned: there is no
long way round, no flank to protect and no gate to hold, so an attack arrives on
a broad front and is answered on one. It is the only map in the pool where
position is decided by depth rather than by width, and the only one where losing
ground cannot be recovered by going around.

The shoreline is stepped rather than ruled, for the reason doc 26 section 1
gives about the old maps: a straight edge reads as an imposed boundary. Shallows
are laid along it, which is the first real use of that decorative character -
the wet margin is what makes a coast read as a coast rather than as a blue
region that starts abruptly.

180-degree rotation symmetry about the map centre is the fairness invariant,
proved in tools/mapgen.py, and it is what puts one sea on each commander's left.
Regenerate with:
    python3 tools/gen_skirmish_08.py data/maps/skirmish-08.fmap
"""
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 128, 96
# The land runs corner to corner between the seas, so the starts sit on that
# diagonal. 103 apart in Chebyshev against the 89 the separation guard demands
# at this size.
STARTS = {0: (12, 80), 1: (115, 15)}
c = Canvas(W, H, STARTS, apron=4)

# ---- The Sound. A stepped coast rather than a ruled one: each row of water is
# one cell narrower than the row above, so the shore descends in a staircase and
# reads as a coastline instead of a drawn triangle. Written on the north-eastern
# corner and completed by rotation into the south-western, which is what puts
# the sea on the same side of each commander.
COAST_ROWS = 18
for y in range(COAST_ROWS):
    width = COAST_ROWS + 2 - y
    if width > 0:
        c.stamp(W - width, y, width, 1, 'w')

# ---- Terrain on the land bridge. Scattered rather than laid in a line: there
# is no crossing to guard here, so terrain exists to give an attack something to
# break against and a defender somewhere to stand, not to make a gate.
MASSIFS = [
    (28, 60, 7, 5, 'h'), (46, 44, 8, 6, 'h'), (18, 38, 6, 7, 'h'),
    (62, 66, 6, 5, 'h'), (36, 22, 8, 5, 'h'), (8, 58, 5, 6, 'h'),
    (54, 28, 6, 5, 'r'), (24, 74, 8, 5, 'r'), (70, 48, 5, 6, 'r'),
    (44, 78, 6, 4, 'r'),
    (40, 56, 6, 3, 'f'), (60, 20, 3, 6, 'f'), (16, 26, 6, 3, 'f'),
]
for (x, y, dx, dy, ch) in MASSIFS:
    c.stamp(x, y, dx, dy, ch)

# ---- Ferrite: 40 cells for a mid-tier map, between the small maps' 20 and the
# big theatre's 60. A safe patch behind each base, one contested patch on each
# half of the land bridge, and a small pair on the centre line.
SAFE = [(0, 0), (1, 0), (0, 1), (1, 1)]
SITE = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
c.cluster(20, 70, SAFE)          # behind base 0
c.cluster(34, 48, SITE)          # forward on player 0's half
c.cluster(52, 74, SITE)          # and out toward the southern shore
c.cluster(58, 40, SAFE)          # the centre-line pair, the closest thing to a prize

# ---- Neutral outposts (ADR-021): a pair on the land bridge, where an army
# going past has to decide whether to stop for them.
c.outpost(44, 36)
c.outpost(30, 58)

fields, blocked, density = c.validate(expected_fields=40, density_range=(0.08, 0.10))

# ---- Decoration (doc 26 s6). The shallows are the point here: a coast that
# starts abruptly reads as a painted region, and the wet margin is what makes it
# read as water meeting land. This is the first map in the pool to use that
# character for what it is for.
for y in range(COAST_ROWS + 3):
    width = COAST_ROWS + 5 - y
    if width > 0:
        c.decor(W - width, y, 3, 1, '~')

# A road down the land bridge. On a map with no crossings the track IS the
# through-route, and saying so is the whole of this map's legibility.
c.decor(6, 84, 116, 2, '=')
c.decor(20, 30, 2, 46, '=')

c.decor(10, 40, 40, 30, ',', fill=4)     # scrub across player 0's half
c.decor(64, 8, 44, 28, ',', fill=5)      # and the northern shoulder
c.decor(30, 4, 34, 16, ':', fill=3)      # gravel on the northern approach

path = sys.argv[1] if len(sys.argv) > 1 else "skirmish-08.fmap"
c.emit(path, [
    "# Tidewrack Sound, the mid tier. 128x96, twice a duel map and half the old",
    "# big one. The sea intrudes from two opposite corners and leaves a broad",
    "# diagonal of land between the bases: there is no bridge and nothing to",
    "# cross, so neither commander has a flank to turn and an attack arrives on",
    "# a broad front. Generated by tools/gen_skirmish_08.py; edit that script",
    "# and regenerate rather than editing this file by hand - the 180-degree",
    "# rotation symmetry is the fairness invariant and it is checked there",
    "# (tools/mapgen.py).",
])
report("skirmish-08 (Tidewrack Sound)", c, fields, blocked, density, path, "no crossings: a land bridge between two seas")
