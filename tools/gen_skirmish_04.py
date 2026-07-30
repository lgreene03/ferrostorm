#!/usr/bin/env python3
"""Generate data/maps/skirmish-04.fmap - "Tarnwater Crossing", 192x128, two players.

TICKET-P6-MAP-01 (originally MAP-04 in doc 22), redesign under ADR-013. The map
was first authored with a RULER-STRAIGHT river: four water columns at x=94..97
down the whole theatre. A straight river reads as an imposed channel and gives
every crossing the same trivial geometry. This is its answer: the Tarnwater now
MEANDERS, x=83..108, and the three bridges sit where the bends stage an attack
rather than at three identical points on a line.

Everything else the MAP-04 ticket fixed is preserved: 192x128, starts (12,20)
and (179,107), a 9x9 open apron per start, sixty ferrite cells (720,000 credits)
laid as twelve clusters of five, terrain density inside 8 to 10 percent, and
exactly three bridges whose removal disconnects the two starts. The generated
file is the single source of terrain, fields and starts.

180-degree rotation symmetry about the map centre (191-x, 127-y) is the fairness
invariant and it is proved in tools/mapgen.py, not trusted. Regenerate with:
    python3 tools/gen_skirmish_04.py data/maps/skirmish-04.fmap
"""
import math
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 192, 128
STARTS = {0: (12, 20), 1: (179, 107)}   # a 180-rotation pair about (95.5, 63.5)
MIDX = (W - 1) / 2                       # 95.5

c = Canvas(W, H, STARTS, apron=4)

# ---- The Tarnwater: a winding north-south river. Amplitude 12 wanders the
# channel x=83..108, well clear of both bases; the width breathes 4..6 cells.
def centre(y):
    return MIDX + 12.0 * math.sin(2 * math.pi * (y - (H - 1) / 2) / 70.0)


def halfwidth(y):
    return 2.1 + 0.7 * math.cos(2 * math.pi * (y - (H - 1) / 2) / 34.0)


c.river(centre, halfwidth, vertical=True)

# ---- Three crossings on a 128-tall map is the tempo lever the MAP-04 ticket
# signed for: attack routes are committed, and whoever holds two of the three
# chooses where the war happens. Each deck is six rows deep so a column crosses
# in formation. Bands are symmetric about the centre row; mapgen closes them
# under rotation regardless.
spans = c.bridges([(20, 26), (60, 68), (102, 108)])

# ---- ADR-025: the CENTRAL ford is destroyable; the two flanking ones are not.
# A felled span blocks its cell, so cutting the middle crossing is a real
# tactical act: it forces the enemy's push out to a flank and buys time. The
# band is self-symmetric under the 180-degree rotation (y and 127-y both fall
# inside 60..67), so both players can cut exactly the same crossing. Leaving the
# flanks permanent is what makes severing impossible: validate() re-proves the
# map connects with every destroyable span rubbled at once.
c.destroyable([(x, y) for (x, y) in spans if 60 <= y < 68])

# ---- Terrain. Bluffs line the banks and frame the bases; ruins litter the
# midfield; fences give light cover. Everything is a rotation pair, so the
# description of player 0's northern ground is player 1's southern ground turned
# 180. Density is tuned to sit inside 8 to 10 percent (the ticket target).
# Base 0 frame (top-left):
c.stamp(6, 8, 26, 4, 'h')        # ridge arc north of base 0
c.stamp(32, 14, 5, 16, 'h')      # east cheek of base 0, funnelling south
c.stamp(2, 34, 14, 5, 'h')       # south flank of base 0

# Bank bluffs: high ground overlooking the fords, the ground a defender wants.
c.stamp(74, 30, 5, 20, 'h')      # west-bank bluff, north reach
c.stamp(74, 84, 5, 20, 'h')      # west-bank bluff, south reach
c.stamp(50, 60, 6, 14, 'h')      # western massif between the west lanes

# Midfield ruins: cover on the approaches to the central ford.
c.stamp(44, 30, 12, 8, 'r')
c.stamp(60, 52, 9, 10, 'r')
c.stamp(36, 80, 12, 7, 'r')
c.stamp(54, 12, 10, 6, 'r')

# Fence lines: light cover, not a wall.
c.stamp(22, 62, 18, 2, 'f')
c.stamp(66, 96, 16, 2, 'f')

# ---- Ferrite: 12 clusters of 5 = 60 cells = 720,000 credits, laid as six
# clusters on player 0's ground and their six rotation images. Per player: one
# safe patch by the base, two near, two mid, and one contested patch beside the
# central ford. The clusters clear the winding channel; cluster() proves it.
CLUSTER = [(0, 0), (1, 0), (0, 1), (2, 1), (1, 2)]   # 5 cells
CLUSTERS = [
    (20, 28),    # safe patch by base 0
    (34, 44),    # near 0
    (8, 56),     # near 0
    (58, 74),    # mid 0
    (66, 22),    # mid 0
    (86, 60),    # contested patch beside the central ford
]
for cx, cy in CLUSTERS:
    c.cluster(cx, cy, CLUSTER)

# ---- Neutral Outposts (ADR-021): two pairs on a theatre this size, laid on
# player 0's ground and completed by rotation. One sits on the western approach
# to the central ford, so holding the ford and holding its income become the
# same decision; the other sits back in the southern lane as a quieter node an
# expansion can pick up. Both clear the channel, the bridge decks, the base
# aprons and every stamp - an outpost blocks its own 2x2 when it spawns, so
# validate() re-proves reachability with them standing.
c.outpost(66, 40)                # western approach to the central ford
c.outpost(30, 70)                # southern lane, behind the fence line

# ---- Decoration (doc 26 s6): drawn, passable, outside the density budget.
# The design review named this map the biggest missed opportunity in the pool
# purely by area - 24,576 cells, none of them dressed, with whole quadrants away
# from the river carrying no terrain, no ferrite and no reason to walk through
# them. That reads as nothing, and on the largest canvas it is the most visible
# fault in the set. None of the below moves a unit by a single cell.
#
# Shallows first, because they do real legibility work: a wet margin along the
# channel says "this is the bank" before a player has learned where the fords
# are, and the bank is the ground doc 26 says a defender wants.
c.decor(78, 0, 5, 128, '~')          # the western margin, the length of the reach
c.decor(112, 0, 5, 128, '~')         # and the eastern

# Roads to the fords. A made track running to a crossing telegraphs the route
# without conceding anything tactically: the crossings are already visible, and
# what a new player lacks is knowing which ground leads to them.
c.decor(0, 22, 74, 2, '=')           # the northern approach
c.decor(0, 62, 74, 2, '=')           # the central, to the ford the map is named for
c.decor(20, 24, 2, 38, '=')          # the link between them on player 0's bank

# Gravel where traffic concentrates: bridgeheads and the base approach.
c.decor(66, 18, 12, 8, ':')
c.decor(66, 58, 12, 8, ':')
c.decor(14, 14, 12, 6, ':')          # worn ground outside base 0

# Scrub across the empty quadrants, thinned so it scatters rather than carpets.
# These are precisely the regions the review called dead space.
c.decor(4, 32, 56, 40, ',', fill=4)      # the western interior
c.decor(120, 6, 56, 34, ',', fill=5)     # the north-eastern quarter
c.decor(34, 86, 54, 32, ',', fill=4)     # the southern lane

fields, blocked, density = c.validate(expected_fields=60, density_range=(0.08, 0.10))

path = sys.argv[1] if len(sys.argv) > 1 else "skirmish-04.fmap"
c.emit(path, [
    "# Tarnwater Crossing. The Tarnwater winds the length of the theatre and is",
    "# bridged three times: north, centre and south. Whoever holds two of the",
    "# three chooses where the war happens. Generated by tools/gen_skirmish_04.py;",
    "# edit that script and regenerate rather than editing this file by hand - the",
    "# 180-degree rotation symmetry is the fairness invariant and it is checked",
    "# there (tools/mapgen.py).",
])
report("skirmish-04 (Tarnwater Crossing)", c, fields, blocked, density, path, "3 fords")
