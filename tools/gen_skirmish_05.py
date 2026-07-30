#!/usr/bin/env python3
"""Generate data/maps/skirmish-05.fmap - "Ashford Reach", 96x64, two players.

TICKET-P6-MAP-05, doc 27 register row DR-18. The register's complaint was that
the map pool underuses its own vocabulary: destroyable bridges appear on exactly
one map and neutral outposts on two, so two features the sim fully supports are
close to invisible in play. This map exists to carry both, and to carry them at
a size where they are unavoidable.

Character: the river map at CLOSE quarters. The only existing river is the
Tarnwater on skirmish-04, which is the 192x128 map: at that size a crossing is a
journey and severing one merely lengthens a march. Ashford Reach puts the same
idea on the small board, where the far bank is always in reach and every
crossing decision lands inside a minute.

The Ashford runs east to west across the middle of the theatre and is crossed
three times. The CENTRE ford is permanent ('B'): it is the contested ground, it
cannot be taken away, and it is where a stand-up fight happens. The two FLANK
bridges are destroyable ('b', ADR-025), which is the inversion of skirmish-04 -
there the centre is the span that can be cut and the flanks are permanent. Here
felling a flank does not cut the map in half; it closes a turning move and
forces the war back through the middle. That is a decision a player can make
early and live with, rather than a way to lock an opponent out, and validate()
proves it: the map still connects with BOTH destroyable spans rubbled at once.

Neutral outposts (ADR-021) sit one on each bank, forward of the bases and clear
of every crossing, so taking one means holding ground on the river rather than
tucking it behind a wall.

180-degree rotation symmetry about the map centre is the fairness invariant,
proved in tools/mapgen.py. Regenerate with:
    python3 tools/gen_skirmish_05.py data/maps/skirmish-05.fmap
"""
import math
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 96, 64
# North and south banks, but at OPPOSITE ENDS of the reach rather than facing
# each other across the middle. The first draft put them at (24,9) and (71,54),
# which is a Chebyshev separation of 47 where every other small map sits near
# 75, and the shorter run turned the map into an accidental rush: measured
# AI-vs-AI, whichever commander crossed first arrived before the other had an
# army, and from then on the defender's garrison never released. The probe read
# 37 against 2 on approach distance and 8 against 17 on peak army, where the
# other four maps read 13/4, 7/29, 10/7 and 3/4. Doc 26's remedy for a map an
# army cannot leave is to widen it, so the starts moved to the corners and the
# separation is now 75, in line with the pool.
STARTS = {0: (10, 8), 1: (85, 55)}     # a 180-rotation pair about (47.5, 31.5)
c = Canvas(W, H, STARTS, apron=4)

# ---- The Ashford: an east-west river down the middle of the theatre. The
# period divides the span exactly (95/47.5 = 2), so the meander is already its
# own rotation image and the closure in river() has nothing to correct rather
# than quietly widening one bank to fix a rounding.
PERIOD = 47.5
AMPLITUDE = 5.0


def centre(x):
    return 31.5 + AMPLITUDE * math.sin(2.0 * math.pi * x / PERIOD)


def halfwidth(x):
    # Narrower at the shoulders, wider mid-reach: a uniform channel reads as a
    # drawn line, and the wide stretches are what make the flanks feel remote.
    return 2.0 + 0.8 * math.sin(math.pi * x / (W - 1))


c.river(centre, halfwidth, vertical=False)

# ---- Three crossings. Bands are along-axis ranges, which is x for an east-west
# river. The outer pair are rotation images of each other (14..20 turns into
# 75..81) and the centre band straddles the middle, so all three are stated
# explicitly and the closure in bridges() confirms rather than invents them.
WEST, CENTRE, EAST = (14, 21), (43, 53), (75, 82)
spans = c.bridges([WEST, CENTRE, EAST])

# ---- ADR-025: the FLANKS are the destroyable ones, which is the inversion this
# map exists for. Cutting both leaves the centre ford standing, so the theatre
# never severs; validate() proves exactly that by flooding the map with every
# span cell rubbled at once. A player who fells a flank has closed a turning
# move and committed the war to the middle, which is a decision rather than a
# lockout.
c.destroyable([(x, y) for (x, y) in spans
               if WEST[0] <= x < WEST[1] or EAST[0] <= x < EAST[1]])

# ---- Bank terrain. Each base gets a hill shoulder for a back wall and a clear
# mouth; rubble at the bridgeheads means a crossing is fought through cover
# instead of over bare ground. Every stamp writes its rotation image, so only
# player 0's half is described here.
c.stamp(2, 2, 3, 6, 'h')         # spur west of base 0: a back wall against the edge
c.stamp(19, 3, 6, 3, 'h')        # shoulder east of base 0, leaving the mouth open
c.stamp(26, 14, 5, 2, 'r')       # rubble mid-bank, cover on the run to the centre
c.stamp(44, 12, 5, 3, 'r')       # rubble above the centre ford: cover for the fight
c.stamp(60, 6, 4, 2, 'h')        # knoll on the northern bank, a forward position
c.stamp(70, 16, 5, 2, 'f')       # fence line short of the eastern bridgehead
c.stamp(36, 20, 4, 2, 'f')       # fence line short of the channel

# ---- Ferrite: 20 cells = 240,000 credits, the small-map convention (doc 26).
# A safe 4-cell patch behind each base, and a 6-cell patch forward on each bank
# within reach of the centre ford, which is the economy worth fighting over.
SAFE = [(0, 0), (1, 0), (0, 1), (1, 1)]
FORWARD = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
c.cluster(14, 13, SAFE)          # safe patch behind base 0
c.cluster(46, 17, FORWARD)       # forward patch on the northern bank

# ---- Neutral outposts (ADR-021): one per bank, forward of the base apron and
# clear of all three crossings, so holding one means holding river ground.
c.outpost(30, 10)

# ---- Decoration (doc 26 s6): drawn, passable, outside the density budget.
# The shallows matter more here than on any other map. This map's hook is that
# the two FLANK crossings can be felled and the centre ford cannot, so a player
# has to read the channel and tell one kind of span from another; a wet margin
# along the bank is what makes the channel read as a channel rather than as a
# blue stripe. The design review raised the related question of whether a
# destroyable deck and a permanent one are actually distinguishable at default
# zoom, which decoration cannot answer and which is filed rather than papered
# over.
c.decor(0, 21, 96, 2, '~')           # the northern margin of the Ashford
c.decor(0, 41, 96, 2, '~')           # and the southern

# Roads to the three crossings, so the routes read before they are learned.
c.decor(10, 14, 16, 2, '=')          # to the western flank bridge
c.decor(40, 16, 14, 2, '=')          # to the centre ford
c.decor(4, 6, 2, 9, '=')             # the link back to base 0

c.decor(6, 2, 32, 9, ',', fill=4)    # scrub on the northern shoulder
c.decor(58, 4, 32, 12, ',', fill=5)  # and across the north-eastern bank
c.decor(22, 45, 36, 14, ':', fill=3) # gravel flats on the southern bank

fields, blocked, density = c.validate(expected_fields=20, density_range=(0.08, 0.10))

path = sys.argv[1] if len(sys.argv) > 1 else "skirmish-05.fmap"
c.emit(path, [
    "# Ashford Reach. The Ashford runs the width of the theatre and is crossed",
    "# three times: two destroyable flank bridges and one permanent centre ford.",
    "# Felling a flank closes a turning move; it can never cut the map in half.",
    "# Generated by tools/gen_skirmish_05.py; edit that script and regenerate",
    "# rather than editing this file by hand - the 180-degree rotation symmetry",
    "# is the fairness invariant and it is checked there (tools/mapgen.py).",
])
report("skirmish-05 (Ashford Reach)", c, fields, blocked, density, path, "3 crossings")
