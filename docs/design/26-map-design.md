# Doc 26: Skirmish Map Design

Owner: game-designer + tools. Phase: 6. Serves GDD (doc 02) pillars 2 and 4 and
the TDD (doc 03) pathfinding and map-format sections. Authorised by ADR-013.

This document is the standard the skirmish maps are held to. It states the
design principles the redesign applied, the distinct intent of each map, and the
two hard constraints every map must satisfy: the fairness invariant and the
requirement that the AI can still play. It is written so that the next person to
author or edit a map has the reasoning, not just the result.

## 1. The problem this redesign answered

The first skirmish maps were legible but artificial. skirmish-01 was a straight
wall of blocked cells down the centre column with two gaps, otherwise open
ground and three ferrite cells. skirmish-02 was a scatter of straight
rectangular blocks with a single ferrite pile in the middle. skirmish-04 was a
ruler-straight vertical water column. A straight barrier reads as imposed rather
than grown, and it plays as a binary: the gap is passable or it is not, and
there is no interior terrain to fight over. Fully open ground plays worse still,
because two armies simply collide as one blob and the bigger blob wins with no
room to manoeuvre. The brief was to fix how the maps look and, in the same move,
to give players a reason to prefer one piece of ground over another.

## 2. Principles applied

These are drawn from the competitive map-design tradition of the classic RTS
games of the 90s and its descendants, calibrated to Ferrostorm's unit speeds and
its anti-turtle toolkit rather than copied as numbers.

**Winding water over straight barriers.** Landscape curves, because water flows
downhill and around obstacles; straight lines signal something imposed on the
land rather than carved by it. A meander also does design work a straight line
cannot: each bend stages a different approach angle, so no two crossings share
the same trivial geometry. Rivers in this lineage are bisecting features crossed
by a handful of bridges, and the crossings are where the fighting concentrates.

**Two to three crossings, not one and not a dozen.** The tested sweet spot for a
1v1 map is roughly three usable routes between the bases. Too few invites
camping; too many makes defence unmanageable. Routes should be near equal in
length or the long one goes unused. Each of the redesigned maps therefore offers
three crossings: hold two of the three and you choose where the war happens.

**Chokepoints carve lanes, but must stay crackable.** Ridgelines, cliffs and
impassable terrain turn an open field into approach corridors, and a defender at
a choke gets a concave and superior angles, which is what lets a smaller force
hold a crossing. The danger is a choke so deep and narrow that it out-ranges the
artillery meant to crack it, or a single walled approach with no flank, which is
the map only a turtle can win. Every crossing here is wide enough to be shelled
from ground the attacker can reach and hold, and every defensible position has a
second approach.

**Base sites: defensible home, contested expansion.** Each start sits in a hill
alcove with a back wall and a clear mouth, defensible without being sealed. The
home economy is a small safe ferrite patch, enough to open on but not to win on.
The larger patch sits forward in contested ground, so taking it is a decision
made under fire. This is the "where and when do I expand" question the GDD's
economy pillar is built on: a player floats on the home patch, then must reach
into the open for more, and denying that reach is a real form of attack.

**Rotational symmetry for fairness.** All four maps are symmetric under a
180-degree rotation about the map centre, the transform that maps each start
onto the other exactly. Rotation is preferred over reflection for 1v1 because it
gives both players an identical experience of every feature, with no handedness
bias. Its one pitfall, the "two hills" effect where each player sits on their own
copy of an off-centre feature and the game deadens, is avoided by putting the
contested economy and the crossings where both players must reach across the
centre for them.

**Anti-turtle by construction.** The GDD makes artillery beat static defence and
gives each faction a superweapon as the defence-buster of last resort. The map's
job is to make sure a turtle behind one crossing always leaves something else
uncovered: a second and third crossing, a contested expansion it cannot hold and
mine at once, and open flanks a raider can use. None of the four maps can be won
by walling a single approach.

## 3. The four maps

The set is deliberately varied so the four do not feel the same.

**skirmish-01, Serpentine Ford (96x64), the river-crossing map.** A river winds
the length of the theatre and is forded three times, north, centre and south.
The bases sit in opposite corners on opposite banks. Hills give each base a back
wall and split the near bank into a central lane and a wider southern flank;
ruins and fences give cover on the ford approaches. The safe ferrite patch sits
thirteen cells from each base; the larger contested patch sits beside the
central ford, in ground the enemy can reach. This is the gated `skirmish`
scenario map and the client's default, so it is the map most players meet first.

**skirmish-02, Ironback Ridge (96x64), the ridge-and-passes map.** No water. One
ridgeline runs corner to corner, along the map's main diagonal, dividing the
theatre into two lands so completely that the only ground routes are its three
passes: a left flank, a central saddle and a right flank. The central saddle is
the direct route and the widest, so it cannot be walled shut. Which pass an army
commits to, and which the defender chooses to hold, is the whole game. The
larger ferrite patch sits at the saddle, contested by design.

**skirmish-03, the resource-contest map (96x64), unchanged.** skirmish-03 was
already authored to this standard, with winding water, hills, ruins, fences and
bridges, and it is the frozen reference map the look-dev camera constants and
the committed reference save are tuned to. Redesigning it would break that
harness for no design gain, so it is preserved as the resource-contest member of
the set. It carries the whole terrain vocabulary and the most contested central
economy of the four.

**skirmish-04, Tarnwater Crossing (192x128), the big theatre.** The tested map
ceiling. The Tarnwater meanders down the theatre, bridged three times, with bank
bluffs that overlook the fords, ruins in the midfield and a sixty-cell economy
laid as twelve clusters of five: one safe by each base, two near, two mid and
one contested beside the central ford. The extra area is room for two economies
to grow apart and for a wave to be seen coming and answered.

## 4. The two hard constraints

**The fairness invariant, proved not trusted.** Every map is generated by a
committed Python script in `tools/`, never hand-typed, because the invariant is
mechanical: every feature must be placed as a 180-degree rotation-symmetric pair
about the map centre. The shared library `tools/mapgen.py` writes each feature
together with its rotation image, then proves, cell by cell, that blocked cells,
ferrite and bridges are all symmetric; that every apron is open so the starting
Construction Yard and MCV fit; that both starts can reach every ferrite patch,
the far start and every apron over the passable cells with bridges open; that
closing every crossing disconnects the two starts, so the crossings are
load-bearing rather than decorative; that the ferrite distance profile from each
start is identical; and that terrain density sits inside 8 to 10 percent, below
which the map reads as empty and above which pathing and the draw-call budget
suffer. A bad edit fails in the generator, not in a match.

**The AI must still play.** This is the constraint that makes a hard map
different from a broken one. Units move by flow field, and a chokepoint the flow
field cannot path returns minus one and parks the attacking army at home. The
generator's reachability proof is the conservative model of the sim's own
passability (four-connected, bridges open), so a route it proves exists is a
route a unit can walk. Every redesigned map was then put through a full
AI-vs-AI match in both faction matchups as the acceptance test: both commanders
must build a base, keep a harvester working, produce, path across the crossings
and fight to a result. A map where an army parks is a failed map, not a hard
one, and is widened until it flows. None of the three needed widening.

## Changed / Assumed / Needed next

**Changed.** New standard document. skirmish-01, skirmish-02 and skirmish-04
redesigned to it; skirmish-03 preserved with reasons stated.

**Assumed.** The brief's directive that 180-degree rotation symmetry is the
fairness invariant on every map takes precedence over doc 22's earlier MAP-02
note that skirmish-01's fair axis was the mirror x to 94-x; the redesign moves
skirmish-01's starts to a rotation pair, which the ADR authorises. Ferrite
budgets match the existing convention (20 cells on the small maps, 60 on the big
one). Terrain density target 8 to 10 percent matches doc 22 MAP-04.

**Needed next, and from whom.** A human taste pass on the running client
(art-pipeline, client-engineer): the top-down previews prove layout, not
lighting. If the four are wanted to share more visual identity, skirmish-03 could
be brought under the same generator in a later ticket, which would require
regenerating the look-dev reference save and re-taking the reference captures.
