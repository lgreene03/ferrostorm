#!/usr/bin/env python3
"""Generate data/maps/skirmish-09.fmap - "Kilnmoor Quarters", 160x120, FOUR players.

The pool's first four-player map (P7-8b). Every other map in data/maps declares
exactly two starts, because until now tools/mapgen.py could only express a
180-degree rotation PAIR and a pair is two seats. mapgen now carries named
symmetry GROUPS, and this map is built on "mirror2": the identity, the two
centre-line mirrors and the 180-degree rotation, an orbit of four. Every feature
below is authored once in the north-western quarter and completed into the other
three by that group, so the four quarters are provably identical.

SIZE. 160x120 is 19,200 cells, chosen to sit between the mid tier and the big
theatre: the pool runs 96x64 (6,144, the duel), 128x96 (12,288, the mid tier),
192x128 (24,576, the big theatre) and 256x192 (49,152, the epic). What matters
on a four-player map is not total area but area per commander, and 19,200 over
four seats is 4,800 cells each, between a duel map's 3,072 per seat and the mid
tier's 6,144. So this map feels like a slightly roomy duel map from inside a
base, while the whole board is larger than anything below the big theatre. It is
deliberately a RECTANGLE and not a square, which is also why the symmetry is
mirror2 rather than a quarter turn: a 90-degree rotation carries a w-by-h
rectangle onto an h-by-w one and is a symmetry of squares only, so mapgen does
not implement it and this map could not have used it.

CHARACTER: the radial map. Every map in the pool so far is LINEAR - a river, a
ridge or a sound lying between two lands, and the game on it is which crossing
you commit to. A four-seat map cannot be linear, so this one is built around a
centre instead of across a line. Two spoil dykes run the full width and the full
height of the moor and meet at the Kiln, a dead firing-house of ruined brick at
the exact centre of the map. The dykes cut the moor into four quarters, one
commander to each, and there are eight ways through: a pass part way along each
dyke arm, joining each quarter to its two neighbours, and four kiln gates where
the arms stop short of the Kiln itself.

The four gates matter more than their number suggests, because together they
form a RING of open ground round the Kiln. The ring is the only direct route
between diagonal quarters and it is the shortest route between any two, so it is
the ground worth holding; and being a ring, holding it means holding two gates
rather than one, which is what stops a single commander sitting on the centre.
The mid-dyke passes are the turning moves: longer, away from the Kiln, and the
way an army arrives somewhere the defender is not.

TWO PLAYERS. The lobby still expresses two seats and MainMenu.cs globs
skirmish-*.fmap, so this map will be offered and, today, will be played by two.
That is intended and it is safe for one reason: seats 0 and 1 are a 180-degree
ROTATION pair. rot180 is a member of the mirror2 group, so a two-player game
seated at 0 and 1 is fair in exactly the sense every existing map in the pool is
fair, and mapgen asserts that ordering rather than trusting it. Seats 2 and 3
are the other rotation pair and simply go unused. Played by two the map reads as
a long diagonal with a contested centre and two empty quarters to manoeuvre
through, which is a different game from the four-way and a good one.

The one honest cost of a mirror group: seats related by a MIRROR see a handed
copy of the map rather than an identical one, so seat 0's back wall lies to its
north-west while seat 2's lies to its north-east. Measured fairness is
untouched - the distance profiles to ferrite and to outposts are identical for
all four seats and mapgen proves it - but the experience of seats 0 and 2 is a
reflection rather than a repetition. Within each rotation pair, which is the
pairing a two-player game uses, there is no handedness at all. The other cost is
orientation: four identical quarters look alike, and the decorative layer cannot
distinguish them because decoration is placed as an orbit too. The Kiln is the
one landmark that tells a player where they are.

Regenerate with:
    python3 tools/gen_skirmish_09.py data/maps/skirmish-09.fmap
"""
import math
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 160, 120

# One start per quarter. The ORDER is the requirement: 0 and 1 are the
# 180-degree rotation pair (north-west against south-east) and 2 and 3 are the
# other one, so the two seats a two-player game fills are a rotation pair and
# the map is as fair for two as any map in the pool. mapgen asserts both that
# the four starts are exactly the orbit of start 0 under mirror2 and that they
# are ordered in rotation pairs.
STARTS = {0: (14, 14), 1: (145, 105), 2: (145, 14), 3: (14, 105)}
c = Canvas(W, H, STARTS, apron=4, symmetry="mirror2")

# ---- The dykes. Both lie ON a mirror axis, which is the one real constraint
# mirror2 puts on a shape: a feature sitting on an axis cannot WANDER across it,
# because a centre line that drifted left would be reflected into a second dyke
# drifting right. So the dykes wander in WIDTH instead of in position, swelling
# and narrowing along their length, which reads as banked spoil rather than as a
# ruled wall and costs the pathing nothing.
def swell(t, period):
    """Half-width of a dyke at distance t along it: 2 or 3 cells, so the dyke is
    4 or 6 cells across. Solid and axis-aligned at every point, so there is no
    staircase for a four-connected path to leak through however thin it gets."""
    return 2 + int(round(0.5 + 0.5 * math.sin(2 * math.pi * t / 29.0)))


VERT_ARM = 48          # rows 0..47: the northern arm, mirrored into the southern
VERT_GATE = range(48, 56)      # the kiln gate: open, and load-bearing
HORZ_ARM = 68          # cols 0..67: the western arm, mirrored into the eastern
HORZ_GATE = range(68, 76)
VERT_PASS = set(range(22, 30))   # the mid-dyke pass through the northern arm
HORZ_PASS = set(range(30, 38))   # and through the western arm

# The northern arm. Written row by row; each row's mirror image completes the
# southern arm, so the two are the same dyke turned over rather than two dykes
# that happen to look alike.
for y in range(VERT_ARM):
    lo, hi = 80 - swell(y, 29), 79 + swell(y, 29)
    if y in VERT_PASS:
        # Left open, but RECORDED as a load-bearing crossing so validate() can
        # prove that sealing every crossing leaves no quarter able to reach any
        # other. The corridor is exactly the width the dyke would have been, or
        # plugging it would not plug the dyke.
        c.mark_pass([(x, y) for x in range(lo, hi + 1)])
    else:
        c.stamp(lo, y, hi - lo + 1, 1, 'h')

# The northern kiln gate: the arm stops short of the Kiln and the gap between
# them is open ground. Recorded as a crossing for the same reason.
for y in VERT_GATE:
    lo, hi = 80 - swell(y, 29), 79 + swell(y, 29)
    c.mark_pass([(x, y) for x in range(lo, hi + 1)])

# The western arm and the western kiln gate, the same construction turned
# through a right angle.
for x in range(HORZ_ARM):
    lo, hi = 60 - swell(x, 29), 59 + swell(x, 29)
    if x in HORZ_PASS:
        c.mark_pass([(x, y) for y in range(lo, hi + 1)])
    else:
        c.stamp(x, lo, 1, hi - lo + 1, 'h')
for x in HORZ_GATE:
    lo, hi = 60 - swell(x, 29), 59 + swell(x, 29)
    c.mark_pass([(x, y) for y in range(lo, hi + 1)])

# ---- The Kiln. Eight by eight of ruined brick on the exact centre of the map,
# so it lies on BOTH mirror axes and its whole orbit is itself: one authored
# block, one block on the map. It is what the four gates are gates through and
# the only asymmetric-looking landmark on a map whose four quarters are
# otherwise identical, which is what a player navigates by.
c.stamp(76, 56, 8, 8, 'r')

# ---- Quarter terrain. Authored once in the north-western quarter; the group
# lays the same features into the other three. Each base gets a back wall and a
# clear mouth, the midfield gets something to break an attack against, and the
# lane towards the Kiln gets cover so the ring is fought for rather than walked
# into.
for (x, y, dx, dy, ch) in [
    (3, 3, 12, 4, 'h'),      # back wall north-west of base 0
    (3, 22, 4, 9, 'h'),      # spur down the western edge: the alcove's far jamb
    (38, 28, 8, 5, 'h'),     # midfield knoll, on the line between base and Kiln
    (56, 12, 6, 5, 'r'),     # broken brick on the northern lane to the mid pass
    (26, 44, 6, 2, 'f'),     # fence line on the approach to the mid-dyke pass
]:
    c.stamp(x, y, dx, dy, ch)

# ---- Ferrite: 16 authored cells become 64, which is 16 per seat. The pool
# gives 10 cells per seat on a duel map and 20 on the mid tier, and this map's
# quarter sits between those two in area, so its economy sits between them too.
# Split the way doc 26 asks: a safe patch that cannot win a game, a forward
# patch in the quarter's own contested ground, and the richest ground of all
# against the Kiln wall, where every commander can reach it and nobody can mine
# it quietly.
SAFE = [(0, 0), (1, 0), (0, 1), (1, 1)]
SITE = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
c.cluster(8, 24, SAFE)           # safe, behind base 0
c.cluster(34, 18, SITE)          # forward in the north-western quarter
c.cluster(70, 50, SITE)          # on the ring, in the north-western gate corner

# ---- Neutral outposts (ADR-021): two orbits, so eight in total and two per
# commander, the same provision per seat that skirmish-06 and skirmish-07 make.
# One sits mid-quarter on the way to the Kiln and one out on the flank, so
# taking the direct route and taking the income are not the same decision.
# Their image anchors come from mapgen's _block_anchor, which derives the
# top-left corner of each image from the block's own corners: a mirror carries
# the anchor along the axis it flips, and getting that wrong by one cell is
# exactly what the outpost distance-profile check in validate() is there to
# catch.
c.outpost(48, 36)                # mid-quarter, on the line to the Kiln
c.outpost(20, 40)                # out on the western flank

fields, blocked, density = c.validate(
    expected_fields=64, density_range=(0.08, 0.10),
    # The default floor scales with the LONG side, which is right for two starts
    # on a diagonal and wrong here: on a four-quarter map the nearest pair of
    # bases faces across the SHORT axis, and no four-quadrant layout on a
    # 4:3 rectangle can separate them by 0.7 of the long side. The floor is
    # therefore stated against the short side, 0.7 x 120 = 84, and the closest
    # pair on this map is 91 apart. The diagonal pair a two-player game uses is
    # 131 apart, comfortably above the 103 of the mid tier.
    min_separation=84)

# ---- Decoration (doc 26 s6): drawn, passable, outside the density budget.
# The kiln yard first, because the ring is the ground this map is about and a
# player needs to read it as a place rather than as a gap between blocks.
c.decor(66, 46, 12, 12, ':', fill=2)

# Haul roads. The moor was worked, so the tracks run to the Kiln and along the
# dykes, which also says out loud where the routes are (GDD pillar 1: readable
# in one glance).
c.decor(72, 0, 3, 48, '=')       # up the flank of the northern arm
c.decor(0, 65, 68, 2, '=')       # along the southern face of the western arm
c.decor(40, 20, 2, 30, '=')      # a spur from the midfield towards the gate

c.decor(6, 30, 34, 16, ',', fill=3)      # heather across the quarter's interior
c.decor(42, 2, 22, 14, ':', fill=4)      # burnt ground on the northern lane
c.decor(4, 46, 30, 10, ',', fill=4)      # and down towards the western pass

path = sys.argv[1] if len(sys.argv) > 1 else "skirmish-09.fmap"
c.emit(path, [
    "# Kilnmoor Quarters, 160x120, the pool's first FOUR-player map. Two spoil",
    "# dykes cut the moor into four quarters and meet at the Kiln at the centre;",
    "# eight ways through, four of them gates onto the ring of open ground round",
    "# the Kiln itself. Seats 0 and 1 are a 180-degree rotation pair and seats 2",
    "# and 3 are the other one, so a two-player game on this map is as fair as a",
    "# game on any two-start map in the pool. Generated by",
    "# tools/gen_skirmish_09.py; edit that script and regenerate rather than",
    "# editing this file by hand - the mirror2 symmetry is the fairness",
    "# invariant and it is checked there (tools/mapgen.py).",
])
report("skirmish-09 (Kilnmoor Quarters)", c, fields, blocked, density, path,
       "8: four kiln gates onto the centre ring, and one mid-dyke pass on each arm")
