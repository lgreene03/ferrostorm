#!/usr/bin/env python3
"""Generate data/missions/mission-06.fmap - "Ashen Crown", 128x96, Sodality.

The finale, and the only mission in the campaign that is a whole game. Missions
01 to 05 each remove something - 01 has no base to build, 02 has nothing
buildable at all, 03 cannot attack, 04 has no economy to fight over, 05 has one
building that matters. This one removes nothing. The player flies Sodality
colours against a fortified Directorate command base and has to beat it with
everything the game has.

What makes it a finale rather than a long skirmish is that the enemy is built to
show what P7 added. The base carries a Bastion and Emplacements rather than a
row of identical turrets, so the defence is a shape to read rather than a wall
to grind (P7-2/P7-2b); it holds an airfield, so the sky is contested (P7-4); and
the player's side gets the Infiltrator, so the enemy's treasury is a target as
well as its buildings (P7-7). None of that is forced. All of it is available,
which is the difference between a finale and a tutorial.

Victory and defeat are both the mission's own, stated with the condition Q016
added, and 'noshortgame' is set so that they are the ONLY ones. That is not
redundancy with the sim's rule: it is the mission taking ownership of its own
ending so that both outcomes carry a line of text, the way mission 02's defeat
does, instead of the match simply stopping.

Generated, not typed - see the note in tools/gen_mission_04.py for why a mission
map is asymmetric on purpose and what is checked in place of symmetry.

Regenerate with:
    python3 tools/gen_mission_06.py data/missions/mission-06.fmap
"""
import math
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 128, 96
START = (14, 80)
CROWN = (104, 16)       # the Directorate command yard, on the high ground

c = Canvas(W, H, {0: START}, apron=4, symmetric=False)

# ---- The Ashen Reach: a river across the map's waist, four crossings. Four
# rather than two, because a finale should be won by choosing where to commit
# rather than by queueing at the only door.
#
# A SINE and not a sawtooth. The first draft wound with `(t % 40) - 20`, which
# looks like a wave and is not one: it snaps back fifteen cells at the period
# boundary, and a river that teleports leaves a corridor straight through
# itself. It cost nothing to find because the load-bearing-crossings proof
# refused the map - which is exactly the argument doc 26 makes for generating
# maps rather than typing them.
c.river(lambda t: 52 + 8 * math.sin(t * 2 * math.pi / 128), lambda t: 2.0, vertical=False)
c.bridges([(18, 24), (48, 54), (78, 84), (106, 112)])

# ---- Terrain. Open ground on the player's southern half so a base can grow,
# a broken shoulder mid-map where the fighting will happen, and a ring of high
# ground around the command base that has to be entered rather than swept.
MASSIFS = [
    (30, 74, 7, 5, 'r'), (52, 82, 8, 5, 'r'), (78, 76, 7, 5, 'h'),
    (20, 62, 6, 6, 'f'), (44, 66, 7, 5, 'f'), (98, 66, 7, 5, 'h'),
    (14, 44, 7, 5, 'h'), (36, 40, 6, 5, 'h'), (60, 34, 7, 5, 'r'),
    (86, 40, 7, 5, 'h'), (110, 44, 6, 5, 'r'),
    (24, 22, 7, 5, 'f'), (46, 16, 7, 5, 'r'),
    (68, 8, 7, 6, 'h'), (68, 22, 7, 6, 'h'),
    (92, 4, 6, 5, 'h'), (92, 30, 6, 5, 'h'),
    (116, 12, 6, 6, 'h'),
]
for (x, y, dx, dy, ch) in MASSIFS:
    c.stamp(x, y, dx, dy, ch)

# ---- Ferrite. A full economy for both sides: this is the mission where the
# game is played normally.
PATCH = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
BIG = PATCH + [(0, 2), (1, 2), (2, 2)]
SMALL = [(0, 0), (1, 0), (0, 1), (1, 1)]
c.cluster(24, 68, BIG)             # behind the player's base, clear of the landing
c.cluster(30, 86, PATCH)
c.cluster(56, 74, PATCH)           # contested, south of the river
c.cluster(88, 84, PATCH)
c.cluster(50, 50, SMALL)           # the crossing prizes, north bank
c.cluster(108, 52, SMALL)
c.cluster(100, 26, BIG)            # the enemy's, inside the ring
c.cluster(84, 14, PATCH)
c.cluster(30, 10, PATCH)           # and a far patch worth raiding for

# ---- Neutral outposts (ADR-021). Two, both south of the river, so the early
# game has somewhere to go that is not the enemy.
c.outpost(48, 78)
c.outpost(74, 62)

fields, blocked, density = c.validate(
    expected_fields=56, density_range=(0.08, 0.10), objectives=[CROWN])

# ---- Decoration.
c.decor(10, 88, 110, 2, '=')            # the southern road
c.decor(20, 26, 2, 60, '=')             # the western trunk road north
c.decor(60, 12, 56, 2, '=')             # the approach to the command base
c.decor(6, 46, 116, 12, '~', fill=3)    # the wet margin along the Reach
c.decor(8, 60, 46, 26, ',', fill=4)     # scrub across the player's half
c.decor(60, 62, 50, 24, ',', fill=4)
c.decor(88, 6, 34, 32, ':', fill=2)     # the hardstanding of the command base
c.decor(24, 4, 40, 26, ':', fill=4)
c.decor(66, 30, 22, 14, ':', fill=3)

# ---- The mission proper -------------------------------------------------
# Struct: 1 power, 2 factory, 3 refinery, 4 CY, 5 turret, 8 depot, 11 barracks,
#         12 radar, 15 emplacement, 16 airfield, 17 bastion.
# Unit:   1 cannon, 2 rifle, 3 rocket, 4 harvester, 5 raider, 9 phantom,
#         11 engineer, 14 carrier, 15 flyer, 16 flak, 17 infiltrator.
MISSION = [
    "",
    "# --- Mission 06: Ashen Crown - break the Directorate command base ---",
    "# The player's landing force. A real force, because this mission is a real",
    "# game. The yard and the opening credits are declared here rather than in",
    "# the client's per-mission switch (see the note in mission 04).",
    f"structure 0 4 {START[0]} {START[1]}",
    "trigger elapsed 0 -> grant 0 8000",
    f"unit 0 9 {START[0] + 4} {START[1] - 4}",
    f"unit 0 9 {START[0] + 6} {START[1] - 4}",
    f"unit 0 5 {START[0] + 4} {START[1] - 6}",
    f"unit 0 5 {START[0] + 6} {START[1] - 6}",
    f"unit 0 2 {START[0] + 2} {START[1] - 2}",
    f"unit 0 2 {START[0] + 8} {START[1] - 2}",
    f"unit 0 4 {START[0] - 2} {START[1] - 4}",
    f"unit 0 11 {START[0] + 2} {START[1] - 6}",
    "",
    "# The Directorate command base. Defence with a SHAPE: a Bastion holding",
    "# the road, Emplacements covering the infantry approaches, turrets on the",
    "# flanks. Grinding it down the same way at every point does not work, and",
    "# that is the whole of what P7-2 and P7-2b bought.",
    f"structure 1 4 {CROWN[0]} {CROWN[1]} crown",
    "structure 1 1 108 24",
    "structure 1 1 112 30",
    "structure 1 3 96 22",
    "structure 1 2 98 8",
    "structure 1 11 88 24",
    "structure 1 12 106 34",
    "structure 1 16 82 6",
    "structure 1 17 94 38",
    "structure 1 15 78 18",
    "structure 1 15 100 36",
    "structure 1 5 76 30",
    "structure 1 5 116 24",
    "structure 1 8 110 8",
    "unit 1 4 98 26",
    "unit 1 4 88 14",
    "unit 1 1 84 32",
    "unit 1 1 94 36",
    "unit 1 2 80 24",
    "unit 1 3 94 28",
    "",
    "# A forward picket south of the Reach, so the crossings are contested",
    "# before the base is.",
    "structure 1 5 58 44 picket",
    "structure 1 5 84 52 picket",
    "unit 1 2 60 50",
    "unit 1 1 86 52",
    "",
    "# Scripting. Counterattacks keyed to the river rather than to the clock,",
    "# so the mission answers what the player does instead of reciting a list.",
    "trigger elapsed 300 -> message the_crown_stands",
    "trigger elapsed 300 -> grant 0 3000",
    "trigger entered 0 66 52 14 -> message they_have_seen_you",
    "trigger entered 0 66 52 14 -> spawn 1 1 90 44 3",
    "trigger entered 0 66 52 14 -> assault 1 66 56",
    "trigger entered 0 96 34 16 -> message the_wing_scrambles",
    "trigger entered 0 96 34 16 -> spawn 1 15 84 8 3",
    "trigger entered 0 96 34 16 -> spawn 1 1 104 30 3",
    "trigger entered 0 96 34 16 -> assault 1 96 38",
    "trigger elapsed 4500 -> message the_reserve",
    "trigger elapsed 4500 -> spawn 1 1 110 18 4",
    "trigger elapsed 4500 -> assault 1 70 60",
    "",
    "# Both endings are the mission's own. 'noshortgame' is set above, so these",
    "# two triggers are the ONLY ways this mission can end, and each of them",
    "# says something. They ask World.HasHope, the same predicate the skirmish",
    "# short-game rule asks, so a campaign defeat cannot drift from a skirmish",
    "# one (Q016). The win is written first: on a tick where the last of both",
    "# sides falls together, the attacker has taken the crown.",
    "trigger eliminated 1 -> message the_crown_falls",
    "trigger eliminated 1 -> win 0",
    "trigger eliminated 0 -> message the_landing_is_lost",
    "trigger eliminated 0 -> win 1",
]

placed = c.check_entities(MISSION)
path = sys.argv[1] if len(sys.argv) > 1 else "mission-06.fmap"
c.emit(path, [
    "# Ashen Crown, campaign mission 06 and the finale. The only mission that is",
    "# a whole game: a Sodality landing in the south against a fortified",
    "# Directorate command base in the north, across the Ashen Reach and its four",
    "# crossings. Built to show what P7 added - defence with a shape, a contested",
    "# sky, and a treasury worth stealing - none of it forced. Generated by",
    "# tools/gen_mission_06.py; edit that script and regenerate rather than",
    "# editing this file by hand.",
], version=2, pre_grid=[
    "faction 0 1",
    "faction 1 0",
    "rules noshortgame",
], post_grid=MISSION)
report("mission-06 (Ashen Crown)", c, fields, blocked, density, path,
       "four crossings of the Ashen Reach, and a ring of high ground beyond")
