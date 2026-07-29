#!/usr/bin/env python3
"""Generate data/maps/skirmish-06.fmap - "Sable Crossroads", 96x64, two players.

TICKET-P6-MAP-06, doc 27 register row DR-18, the sixth map that row asked for
alongside the fifth. skirmish-05 closed the destroyable-bridge half of the
complaint. This closes the other half, and it does it by changing what an
outpost IS rather than by placing more of them.

On every map that carries outposts today they are SIDE income: a forward node
worth an engineer, sitting off the route between the bases, taken by whichever
commander happens to pass. Nothing makes a player go there. Sable Crossroads
makes them the ground itself.

Character: a sable ridge crosses the theatre corner to corner in both axes,
cutting it into four quadrants that touch only at four gaps in the arms. The
two bases sit in opposite quadrants, so the OTHER two - north-east and
south-west - belong to nobody, and the four neutral outposts stand in them.
Every route from one base to the other runs through one of those neutral
quadrants, because there is no direct road: to attack at all you must cross the
ground the outposts are standing on. Holding an outpost and holding your line
of approach become the same decision, which is what the feature was for.

There is deliberately no water here. skirmish-04 and skirmish-05 are the river
maps; this one is terrain and choice, so the pool reads as four distinct shapes
rather than three variations on a channel.

180-degree rotation symmetry about the map centre is the fairness invariant,
proved in tools/mapgen.py, and it is what makes the two neutral quadrants exact
mirrors: neither commander is nearer to the contested economy. Regenerate with:
    python3 tools/gen_skirmish_06.py data/maps/skirmish-06.fmap
"""
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 96, 64
# Opposite quadrants, corner to corner. Separation is 77 in Chebyshev, in line
# with the rest of the small-map pool - skirmish-05's first draft taught that
# lesson at 47, where the shorter run turned the map into an accidental rush.
STARTS = {0: (9, 12), 1: (86, 51)}
c = Canvas(W, H, STARTS, apron=4)

# ---- The Sable: two ridges, one vertical and one horizontal, crossing at the
# centre. Each arm is broken by ONE gap, and the gaps are a rotation pair, so
# the four quadrants form a ring: north-west touches north-east, which touches
# south-east, which touches south-west, which touches north-west again. No
# quadrant touches its diagonal opposite, which is the whole design - the two
# bases are diagonal, so neither can reach the other without entering neutral
# ground.
#
# Only player 0's half is described; every stamp writes its rotation image.
VBAR_X, VBAR_W = 46, 4           # the vertical ridge
HBAR_Y, HBAR_H = 30, 4           # the horizontal ridge
VGAP = (7, 15)                   # the northern gap in the vertical ridge
HGAP = (11, 19)                  # the western gap in the horizontal ridge

# Vertical ridge: the tip above the northern gap, then the long run from the gap
# down through the centre. Rotation completes the southern half and opens the
# matching southern gap at y=50..55.
c.stamp(VBAR_X, 0, VBAR_W, VGAP[0], 'h')
c.stamp(VBAR_X, VGAP[1], VBAR_W, 36 - VGAP[1] + 14, 'h')

# Horizontal ridge: the stub west of the western gap, then the long run east
# through the centre. Rotation completes it and opens the eastern gap at
# x=78..83.
c.stamp(0, HBAR_Y, HGAP[0], HBAR_H, 'h')
c.stamp(HGAP[1], HBAR_Y, 52 - HGAP[1] + 18, HBAR_H, 'h')

# ---- The four gaps are the only ways between quadrants, so they are the
# load-bearing crossings: validate() proves that sealing all four leaves the two
# starts in separate components. Marked rather than assumed, because a ridge
# with a leak somewhere is a ridge that decorates instead of deciding.
c.mark_pass([(x, y) for x in range(VBAR_X, VBAR_X + VBAR_W) for y in range(*VGAP)])
c.mark_pass([(x, y) for y in range(HBAR_Y, HBAR_Y + HBAR_H) for x in range(*HGAP)])

# ---- Cover in the quadrants: rubble at the gap mouths so a crossing is fought
# through something, and a knoll in the neutral ground worth standing on.
c.stamp(38, 22, 5, 2, 'r')       # rubble inside the north-west quadrant
c.stamp(56, 8, 4, 2, 'r')        # rubble at the northern gap's far mouth
c.stamp(66, 24, 4, 2, 'h')       # knoll in the neutral north-east quadrant
c.stamp(18, 40, 6, 2, 'f')       # fence line in the neutral south-west quadrant
c.stamp(4, 26, 5, 2, 'f')        # fence short of the western gap

# ---- Ferrite: 20 cells = 240,000 credits, the small-map convention (doc 26).
# A safe 4-cell patch behind each base, and a 6-cell patch in each NEUTRAL
# quadrant, so the contested economy and the contested outposts stand on the
# same ground and a player cannot take one while ignoring the other.
SAFE = [(0, 0), (1, 0), (0, 1), (1, 1)]
CONTEST = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
c.cluster(16, 20, SAFE)          # safe patch behind base 0, north-west
c.cluster(58, 16, CONTEST)       # contested patch in the neutral north-east

# ---- Neutral outposts (ADR-021), the point of the map: FOUR of them, two in
# each neutral quadrant, so the ring route a commander must take to attack is
# also the route that pays. Two calls, each placing a rotation pair.
c.outpost(62, 6)
c.outpost(74, 26)

# ---- Decoration: drawn, walked over, and outside every invariant. Until this
# layer existed every drawable character was an obstacle, so the 8-to-10 per
# cent density cap was also a cap on how much of the map could be SEEN, and the
# result was bare ground with two big masses on it. None of the below changes
# where a unit can go by a single cell.
#
# The roads are the point. A made track along each ridge and through each gap
# tells a player where the routes are before they have learned the map, which
# is a legibility gain as much as a visual one (GDD pillar 1).
c.decor(0, HBAR_Y - 1, 96, 1, '=')        # the road that runs the ridge line
c.decor(VBAR_X - 1, 0, 1, 64, '=')        # and its north-south counterpart
c.decor(HGAP[0], HBAR_Y - 3, HGAP[1] - HGAP[0], 6, ':')   # gravel worn through the gap
c.decor(VBAR_X - 2, VGAP[0], 4, VGAP[1] - VGAP[0], ':')   # and through the other

# Scrub, thinned so it reads as scatter rather than a lawn. Placed across the
# open quadrants, which are precisely the areas that read as empty.
c.decor(6, 16, 30, 10, ',', fill=3)       # north-west quadrant
c.decor(56, 4, 34, 20, ',', fill=4)       # north-east, the neutral ground
c.decor(20, 2, 20, 8, ',', fill=5)        # the northern shoulder
c.decor(4, 36, 30, 12, ',', fill=4)       # spilling south of the ridge

fields, blocked, density = c.validate(expected_fields=20, density_range=(0.08, 0.10))

path = sys.argv[1] if len(sys.argv) > 1 else "skirmish-06.fmap"
c.emit(path, [
    "# Sable Crossroads. Two ridges cut the theatre into four quadrants joined",
    "# only at four gaps. The bases sit diagonally opposite, so every route to",
    "# the enemy runs through neutral ground - and the four outposts stand in",
    "# it. Generated by tools/gen_skirmish_06.py; edit that script and",
    "# regenerate rather than editing this file by hand - the 180-degree",
    "# rotation symmetry is the fairness invariant and it is checked there",
    "# (tools/mapgen.py).",
])
report("skirmish-06 (Sable Crossroads)", c, fields, blocked, density, path, "4 gaps")
