#!/usr/bin/env python3
"""Generate data/missions/mission-04.fmap - "Ironhaul", 96x72, Directorate.

The first mission that is not about destroying something. Missions 01 to 03 are
a strike, a theft and a siege, and all three are decided by what is left
standing; this one is decided by what ARRIVES. A column has to cross the
Ironhaul from the western workings to the extraction field in the east, and the
mission ends the moment enough of it is standing on that ground.

That makes it the mission the Carrier exists for (P7-3). Five riflemen walking
the road are five targets for four thousand ticks; five riflemen in a hull are
one target that moves faster and can be answered by armour rather than by
prayer. Nothing FORCES the transport - a player who wants to walk it may - and
that is the point: it is the first mission where the roster offers a way of
travelling rather than a way of fighting.

The enemy does not have a base here. It has three blocking positions on the
road and a garrison that wakes when the column is seen, so the pressure is a
gauntlet rather than an economy, and the mission cannot be won by out-building
anyone.

WHY THIS MAP IS GENERATED AND NOT TYPED. Doc 26 section 4 requires every map to
come from a committed script, and states the reason as the fairness invariant:
180-degree rotation symmetry, proved cell by cell. A campaign mission has no
fairness invariant - it is asymmetric ON PURPOSE, and a mirrored mission would
be a skirmish with dialogue. So this uses mapgen's asymmetric mode, which drops
symmetry and the start-separation guard (there is no second start to be
separated from) and keeps every check that still means something: aprons open,
density in band, and reachability - including the mission-specific one, that the
player can actually walk to the objective. A mission whose extraction field sits
behind a sealed ridge is unwinnable, and it would only be discovered by playing
it.

Regenerate with:
    python3 tools/gen_mission_04.py data/missions/mission-04.fmap
"""
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mapgen import Canvas, report

W, H = 96, 72
START = (10, 36)
# The extraction field. Far east, on the far bank, and deliberately the single
# most distant open ground from the start: the mission is the journey.
EXTRACT = (86, 36)

c = Canvas(W, H, {0: START}, apron=4, symmetric=False)

# ---- The Ironhaul itself: a river down the middle with two crossings. Two
# rather than three, because the decision this mission wants is "north gate or
# south gate", and a third crossing turns a decision into a detour.
c.river(lambda t: 48 + int(3 * ((t % 24) - 12) / 12), lambda t: 2.0, vertical=True)
c.bridges([(16, 22), (50, 56)])

# ---- Blocking terrain. Laid so the road narrows twice on the way in and once
# on the way out, which is what gives the garrison somewhere worth standing.
MASSIFS = [
    (22, 10, 9, 6, 'h'), (22, 56, 9, 6, 'h'),        # the western narrows
    (34, 27, 5, 5, 'r'), (34, 40, 5, 5, 'r'),        # the approach to the crossings
    (58, 8, 8, 6, 'h'), (58, 58, 8, 6, 'h'),         # the eastern shoulder
    (68, 28, 5, 6, 'r'), (68, 38, 5, 6, 'r'),        # the last narrows before the field
    (14, 20, 4, 4, 'f'), (14, 48, 4, 4, 'f'),
    (80, 14, 4, 4, 'h'), (80, 54, 4, 4, 'h'),
]
for (x, y, dx, dy, ch) in MASSIFS:
    c.stamp(x, y, dx, dy, ch)

# ---- Ferrite. Only on the WESTERN bank, and only enough to field the column:
# an economy on the far side would turn an extraction into a second base, which
# is the mission this one is trying not to be.
PATCH = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
SMALL = [(0, 0), (1, 0), (0, 1), (1, 1)]
c.cluster(16, 32, PATCH)
c.cluster(16, 38, PATCH)
c.cluster(26, 22, SMALL)
c.cluster(26, 46, SMALL)

fields, blocked, density = c.validate(
    expected_fields=20, density_range=(0.08, 0.10), objectives=[EXTRACT])

# ---- Decoration (doc 26 s6): passable, drawn, outside the density budget.
# The haul road is the legibility of this map. A player who can see the route
# can decide about it; a player who cannot is just walking east.
c.decor(6, 35, 84, 2, '=')            # the haul road, west to east
c.decor(46, 16, 4, 6, '=')            # the northern bridge approach
c.decor(46, 50, 4, 6, '=')
c.decor(44, 8, 8, 56, '~', fill=3)    # the wet margin along both banks
c.decor(8, 8, 30, 22, ',', fill=4)    # scrub on the western workings
c.decor(8, 44, 30, 22, ',', fill=4)
c.decor(62, 20, 28, 32, ':', fill=3)  # gravel across the eastern flats
c.decor(78, 30, 14, 12, ':', fill=2)  # and thickening at the extraction field

# ---- The mission proper -------------------------------------------------
# Struct types: 1 power, 2 factory, 3 refinery, 5 turret, 11 barracks.
# Unit types: 1 cannon, 2 rifle, 3 rocket, 4 harvester, 8 howitzer, 14 carrier.
MISSION = [
    "",
    "# --- Mission 04: Ironhaul - get the column to the extraction field ---",
    "# The player's column. Deliberately infantry-heavy: this is the cargo, and",
    "# what the mission asks is how it travels, not whether it can fight.",
    "#",
    "# The construction yard and the opening credits are declared HERE rather",
    "# than in a per-mission switch in the client. Missions 01 and 03 are set",
    "# up by a `switch (MissionIndex)` in SkirmishLive.cs, which is a rule keyed",
    "# on WHICH mission this is instead of on what the mission says - so every",
    "# new mission needs an edit in C# and a matching one in the gate, and a",
    "# mission whose author forgot is silently a mission with no base. It needs",
    "# no new machinery to say it here: a structure line and an elapsed-0",
    "# trigger already mean exactly this.",
    f"structure 0 4 {START[0]} {START[1]}",
    "trigger elapsed 0 -> grant 0 3000",
    f"unit 0 2 {START[0] + 2} {START[1] - 3}",
    f"unit 0 2 {START[0] + 2} {START[1] - 1}",
    f"unit 0 2 {START[0] + 2} {START[1] + 1}",
    f"unit 0 3 {START[0] + 4} {START[1] - 2}",
    f"unit 0 3 {START[0] + 4} {START[1] + 2}",
    f"unit 0 1 {START[0] + 5} {START[1]}",
    "",
    "# The gauntlet. Three blocking positions, no economy: this enemy cannot",
    "# grow, so the mission is a road and not a war.",
    "structure 1 5 40 30 gate_north",
    "structure 1 5 40 42 gate_south",
    "structure 1 1 42 36",
    "structure 1 5 76 34 last_gate",
    "structure 1 5 76 38 last_gate",
    "unit 1 2 44 20",
    "unit 1 2 44 52",
    "unit 1 1 62 36",
    "",
    "# Scripting. The garrison wakes when the column reaches the crossings,",
    "# then again on the eastern bank, so the pressure follows the journey",
    "# rather than arriving all at once at the start.",
    "trigger elapsed 200 -> message column_ready",
    "trigger elapsed 200 -> grant 0 4000",
    "trigger entered 0 38 36 10 -> message the_crossings",
    "trigger entered 0 38 36 10 -> spawn 1 2 52 24 3",
    "trigger entered 0 38 36 10 -> spawn 1 2 52 48 3",
    "trigger entered 0 38 36 10 -> assault 1 40 36",
    "trigger entered 0 62 36 10 -> message the_far_bank",
    "trigger entered 0 62 36 10 -> spawn 1 1 78 24 2",
    "trigger entered 0 62 36 10 -> spawn 1 1 78 48 2",
    "trigger entered 0 62 36 10 -> assault 1 70 36",
    "",
    "# The objective: BE THERE. The win is written above the defeat, so a",
    "# column that arrives on the same tick its last holding dies has arrived",
    "# (MissionRunner evaluates in file order and DeclareWinner latches).",
    f"trigger entered 0 {EXTRACT[0]} {EXTRACT[1]} 6 -> message extraction_made",
    f"trigger entered 0 {EXTRACT[0]} {EXTRACT[1]} 6 -> win 0",
    "",
    "# Q016's answer, applied: the mission states its own defeat, asking the",
    "# same predicate the skirmish short-game rule asks. 'noshortgame' is",
    "# required because this enemy owns blocking positions and no base worth",
    "# the name, and without it the sim would end the mission on its own terms.",
    "trigger eliminated 0 -> message column_lost",
    "trigger eliminated 0 -> win 1",
]

placed = c.check_entities(MISSION)
path = sys.argv[1] if len(sys.argv) > 1 else "mission-04.fmap"
c.emit(path, [
    "# Ironhaul, campaign mission 04. The first mission decided by what ARRIVES",
    "# rather than by what is left standing: a column crosses from the western",
    "# workings to the extraction field in the east, through a gauntlet with no",
    "# economy behind it. The mission the Carrier exists for, though nothing",
    "# forces it. Generated by tools/gen_mission_04.py; edit that script and",
    "# regenerate rather than editing this file by hand.",
], version=2, pre_grid=[
    "faction 0 0",
    "faction 1 1",
    "rules noshortgame",
], post_grid=MISSION)
report("mission-04 (Ironhaul)", c, fields, blocked, density, path,
       "two bridges over the Ironhaul: north gate or south gate")
