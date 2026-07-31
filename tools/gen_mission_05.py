#!/usr/bin/env python3
"""Generate data/missions/mission-05.fmap - "Skyfall", 112x80, Directorate.

The mission that teaches the air layer (ADR-028), and the first in the campaign
where a whole CATEGORY of unit is not optional. Sodality strike flyers come over
the ridge on a timer. Nothing the player starts with can touch them: every
existing weapon in the game is ground-only by ADR-028 clause 3, which is exactly
what makes an aircraft an aircraft. The answer exists, it is the Flak Track, and
the mission is lost by the player who does not work that out.

The objective is the airfield that sends them. Not the enemy base - the
AIRFIELD, tagged, so the mission ends when the source is gone even though the
rest of the base still stands. That is the shape a strike mission should have
and it is the shape mission 01 could not have until Q012 was answered: with
elimination and the scripted objective BOTH counted as wins, a tagged objective
that is a strict subset of the enemy's holdings is now a meaningful thing to
write, because it can fire first.

Short game stays ON here, unlike missions 03 and 04. This enemy owns a real base
and a real economy, so the sim's own rules describe the situation correctly and
there is nothing for the mission to override. The one trigger it adds on defeat
is a MESSAGE, so a loss says why, the way mission 02's does.

Generated, not typed - see the note in tools/gen_mission_04.py for why a mission
map is asymmetric on purpose and what is checked in place of symmetry.

Regenerate with:
    python3 tools/gen_mission_05.py data/missions/mission-05.fmap
"""
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 112, 80
START = (12, 40)
AIRFIELD = (88, 40)     # the objective, on the far side of the ridge

c = Canvas(W, H, {0: START}, apron=4, symmetric=False)

# ---- The ridge. One long north-south wall with two passes, because the
# ground story of this mission is that the enemy's aircraft do not care about
# any of it and the player's army does. The ridge is what makes the air layer
# legible: the flyers arrive over ground the player has to walk around.
RIDGE_X = 56
for y in range(0, 80):
    if 22 <= y < 28 or 52 <= y < 58:
        continue                      # the two passes
    c.stamp(RIDGE_X + (y % 6) // 3, y, 5, 1, 'h')
c.mark_pass([(RIDGE_X + 2, y) for y in range(22, 28)] +
            [(RIDGE_X + 2, y) for y in range(52, 58)])

# ---- Terrain either side. Sparse on the player's half so a base can be laid
# out, heavier on the enemy's so the approach to the airfield has to be chosen.
MASSIFS = [
    (26, 12, 7, 5, 'r'), (26, 62, 7, 5, 'r'),
    (36, 32, 5, 5, 'f'), (36, 44, 5, 5, 'f'),
    (24, 34, 4, 4, 'f'),
    (70, 10, 8, 6, 'h'), (70, 64, 8, 6, 'h'),
    (74, 28, 6, 5, 'r'), (74, 48, 6, 5, 'r'),
    (96, 20, 6, 6, 'h'), (96, 56, 6, 6, 'h'),
    (86, 6, 5, 5, 'f'), (86, 70, 5, 5, 'f'),
]
for (x, y, dx, dy, ch) in MASSIFS:
    c.stamp(x, y, dx, dy, ch)

# ---- Ferrite. A real economy on the player's side, because this mission
# genuinely requires production: flak tracks are not in the starting force and
# cannot be, or the lesson would not land.
PATCH = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
SMALL = [(0, 0), (1, 0), (0, 1), (1, 1)]
c.cluster(18, 34, PATCH)
c.cluster(18, 44, PATCH)
c.cluster(30, 22, SMALL)
c.cluster(30, 54, SMALL)
c.cluster(44, 38, SMALL)          # the forward patch, on the wrong side of safe
c.cluster(80, 34, PATCH)          # the enemy's, behind the ridge
c.cluster(80, 44, PATCH)

fields, blocked, density = c.validate(
    expected_fields=36, density_range=(0.08, 0.10), objectives=[AIRFIELD])

# ---- Decoration.
c.decor(8, 39, 46, 2, '=')             # the player's road east, to the passes
c.decor(54, 24, 6, 2, '=')             # through the northern pass
c.decor(54, 54, 6, 2, '=')             # and the southern
c.decor(62, 39, 44, 2, '=')            # and on to the airfield
c.decor(84, 30, 16, 20, ':', fill=2)   # the hardstanding around the airfield
c.decor(10, 10, 40, 24, ',', fill=4)   # scrub across the player's half
c.decor(10, 50, 40, 24, ',', fill=4)
c.decor(64, 16, 40, 12, ':', fill=3)
c.decor(64, 56, 40, 16, ':', fill=3)

# ---- The mission proper -------------------------------------------------
# Struct: 1 power, 2 factory, 3 refinery, 5 turret, 11 barracks, 12 radar,
#         15 emplacement, 16 airfield.
# Unit:   1 cannon, 2 rifle, 3 rocket, 4 harvester, 15 strike flyer,
#         16 flak track.
MISSION = [
    "",
    "# --- Mission 05: Skyfall - take the airfield before it takes you ---",
    "# The player's start. A working force and nothing that can shoot upward:",
    "# every weapon here is ground-only (ADR-028 clause 3), which is the whole",
    "# premise. The flak track has to be BUILT - so the yard and the credits to",
    "# reach a factory are declared here, in the mission, rather than in the",
    "# client's per-mission switch (see the note in mission 04).",
    f"structure 0 4 {START[0]} {START[1]}",
    "trigger elapsed 0 -> grant 0 6000",
    f"unit 0 1 {START[0] + 4} {START[1] - 2}",
    f"unit 0 1 {START[0] + 4} {START[1] + 2}",
    f"unit 0 2 {START[0] + 2} {START[1] - 3}",
    f"unit 0 2 {START[0] + 2} {START[1] + 3}",
    f"unit 0 3 {START[0] + 5} {START[1]}",
    f"unit 0 4 {START[0] - 2} {START[1] + 2}",
    "",
    "# The Sodality airbase. The AIRFIELD is the objective and it is tagged;",
    "# everything else here exists to make getting to it a decision.",
    f"structure 1 16 {AIRFIELD[0]} {AIRFIELD[1]} airfield",
    "structure 1 1 92 34",
    "structure 1 1 92 46",
    "structure 1 3 82 52",
    "structure 1 2 82 28",
    "structure 1 5 74 36",
    "structure 1 5 74 44",
    "structure 1 15 66 24",
    "structure 1 15 66 54",
    "unit 1 2 70 40",
    "unit 1 1 78 40",
    "unit 1 4 80 50",
    "",
    "# The air. Three sorties, escalating, from the airfield's own apron. The",
    "# first is a warning that costs a harvester; the third is a real strike.",
    "trigger elapsed 400 -> message first_sortie",
    "trigger elapsed 400 -> spawn 1 15 86 38 1",
    "trigger elapsed 400 -> assault 1 20 40",
    "trigger elapsed 1600 -> message second_sortie",
    "trigger elapsed 1600 -> spawn 1 15 86 36 2",
    "trigger elapsed 1600 -> assault 1 16 40",
    "trigger elapsed 3000 -> message the_full_wing",
    "trigger elapsed 3000 -> spawn 1 15 86 42 3",
    "trigger elapsed 3000 -> spawn 1 1 72 40 2",
    "trigger elapsed 3000 -> assault 1 14 40",
    "",
    "# The objective. A strict subset of the enemy's holdings, which is a thing",
    "# worth writing only because Q012 answered that a scripted objective and",
    "# elimination are BOTH wins: this one can fire while the base still",
    "# stands, and that is the mission.",
    "trigger destroyed airfield -> message the_sky_is_ours",
    "trigger destroyed airfield -> win 0",
    "",
    "# Short game is left ON: this enemy owns a base and an economy, so the",
    "# sim's own rules already describe defeat correctly and the mission has",
    "# nothing to override. All this adds is a reason.",
    "trigger eliminated 0 -> message the_sky_was_theirs",
]

placed = c.check_entities(MISSION)
path = sys.argv[1] if len(sys.argv) > 1 else "mission-05.fmap"
c.emit(path, [
    "# Skyfall, campaign mission 05. The air layer taught as a mission: Sodality",
    "# strike flyers come over the ridge on a timer and nothing the player starts",
    "# with can touch them, because every existing weapon is ground-only. The",
    "# answer is the Flak Track and it has to be built. The objective is the",
    "# AIRFIELD, tagged, so the mission ends when the source is gone rather than",
    "# when the base is. Generated by tools/gen_mission_05.py; edit that script",
    "# and regenerate rather than editing this file by hand.",
], version=2, pre_grid=[
    "faction 0 0",
    "faction 1 1",
], post_grid=MISSION)
report("mission-05 (Skyfall)", c, fields, blocked, density, path,
       "two passes through the ridge, and an enemy that does not need them")
