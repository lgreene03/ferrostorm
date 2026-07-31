# Doc 26: Skirmish Map Design

**STATUS, 2026-07-25:** this standard predates two features now in the map
format, and a map authored strictly from it would omit both. (1) Neutral
OUTPOSTS (ADR-021), placed in rotationally symmetric pairs on skirmish-02 (one
pair) and skirmish-04 (two pairs): the fairness invariants below must cover
outpost pairs as they already cover ferrite. (2) The destroyable BRIDGE deck
character `b` (ADR-025), passable until felled and BLOCKING its cell afterwards,
so a reachability proof that assumes bridges stay open is proving the wrong
thing. tools/mapgen.py implements both and validates them; read it alongside
this document.

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

**skirmish-03, the frozen look-dev reference (96x64), unchanged, and the
weakest map in the pool.** The paragraph that stood here claimed skirmish-03
"was already authored to this standard" and "carries the whole terrain
vocabulary and the most contested central economy of the four". A review of the
grid does not support any of that, and the claim is withdrawn rather than left
to mislead whoever reads this next.

What the file actually contains: a dead straight horizontal water band across
the full width (grid rows 36 to 39) broken by exactly TWO three-wide bridge
gaps, which is the "ruler-straight, imposed rather than grown" pattern section 1
of this document disowns, and two crossings rather than the two-to-three this
document calls the tested sweet spot; two literal straight blocked bars at rows
22 and 53, the "scatter of straight rectangular blocks" named here as old
skirmish-02's flaw; a hollow fenced rectangle at rows 14 to 20 containing
nothing, marking nothing and doing no design work; and two ferrite clusters both
at roughly equal distance from each start, with none of the safe-near versus
contested-forward split every other map in the pool now authors deliberately.

It is preserved for one reason only, and it is not a design one: the look-dev
camera constants and the committed reference save are tuned to it, so
regenerating it means re-taking the reference captures. That is a real cost and
a real reason to leave it alone, but it is a harness reason. It should not be
described as an exemplar of this standard, because it is the clearest violation
of it in the set.

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

**A MISSION map is generated too, and proves different things** (ADR-029). The
rule above is that a map comes from a committed script; the reason given for it,
rotation symmetry, is a SKIRMISH reason. A campaign mission is asymmetric on
purpose - the player and the scripted enemy are not meant to be evenly matched,
and a mirrored mission would be a skirmish with dialogue. So `symmetric=False`
drops the checks that are about fairness between two starts (rotation symmetry,
the ferrite and outpost distance profiles, the start-separation guard, none of
which have a second start to speak about) and keeps every check that is about
the map being playable: aprons open, density in band, reachability. It adds two
that a skirmish map does not need. Every cell the script sends the player to
must be walkable from the start, because a mission whose objective sits behind a
sealed ridge is unwinnable and would only be found by playing it. And every
authored `unit` and `structure` line is parsed back and proved to stand on open
ground, which is the thing a hand-typed mission gets wrong. Where a mission
marks crossings, the load-bearing proof is restated as "close them and the
OBJECTIVE becomes unreachable" rather than "the two starts fall apart".

Both additions caught a defect on their first run: a tank standing on a ferrite
patch, and a river that was not a river, its centre function snapping back
fifteen cells at the period boundary and leaving a corridor straight through
itself. That is the argument of this section, made once more and not by
assertion. Missions 01 to 03 predate all of this and are still hand-typed 64x48
grids; bringing them under the generator would move two golden hashes, so it is
a row of its own rather than a rider.

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

**skirmish-08, Tidewrack Sound (128x96), the mid tier.** The pool ran five maps
at 96x64, then one at 192x128, then one at 256x192, with nothing between the
fast duel and the big theatre. 12,288 cells fills that gap: twice a duel map,
half the old big one.

Its idea is water as a BOUNDARY rather than a barrier. Every other map here that
carries water uses it as something to cross - the Serpentine, the Tarnwater and
the Ashford are rivers with bridges, and the game on them is which crossing you
commit to. This map has no bridge and nothing to cross. The Sound intrudes from
two opposite corners and removes them, leaving a broad diagonal of land between
the bases.

That changes the shape of a fight rather than its route. With sea on one flank
and sea on the other, neither commander can be turned: no long way round, no
flank to protect, no gate to hold, so an attack arrives on a broad front and is
answered on one. It is the only map in the pool where position is decided by
depth rather than by width. Measured over a full match it produced the most
combat of any map - 186 entities destroyed against the basin's 133 - which is
what a map with nowhere to hide should do.

The shore is stepped rather than ruled, for the reason section 1 gives about the
old maps, and shallows are laid along it: the wet margin is what makes a coast
read as a coast instead of as a blue region that starts abruptly.

**skirmish-07, Karsthollow Basin (256x192), the epic theatre.** The first map
built to the measured ceiling of section 5 rather than to the assumed one:
49,152 cells, eight times a small map and twice skirmish-04. It also breaks this
document's own "two to three crossings" guidance ON PURPOSE, and the deviation
is stated rather than smuggled.

Every other map in the pool is a crossing map, and the game on them is which
choke you commit to. That formula is good and it is now used six times. This map
has no river, no ridge and no load-bearing crossing at all; the basin is broken
by scattered karst that leaves many routes rather than few. mapgen's crossing
proof only runs on a map that DECLARES chokes, so a map with none is a shape
chosen deliberately, not an invariant dodged.

The point of the shape is what it rewards. With no gate to hold, holding ground
becomes a question of area rather than of a chokepoint, which is the macro game
the GDD's 15-to-30 minute window allows and which nothing in the pool asked for.
The economy is laid to match: a four-cell safe patch behind each base that
cannot win a game, sixteen expansion sites spread across the basin, and the
richest ground dead centre beside a pair of outposts - a prize that is central,
worth taking, and impossible to hold quietly because everything can reach it.
Six outposts stand in total, the most of any map.

It is also the first map proved at the length it was designed for. mapgate runs
1,500 ticks, which at fifteen ticks per second is a hundred seconds - enough to
show the AI is alive on a map and nothing more, and plainly not enough for a map
whose whole rationale is the fifteen-to-thirty minute window. basingate plays it and skirmish-08
for 20,000 ticks each, a little over twenty-two simulated minutes, and asserts that
the commanders actually expanded and actually fought rather than sitting on a
large empty board. Measured on the committed map: one side finished holding
three outposts, a refinery and 47 units against an opponent reduced to nothing,
with 133 entities destroyed and 324 alive at peak. The basin converges to a
result; it does not degenerate into a stand-off on open ground. The margin is
lopsided, but that is the first-striker asymmetry filed as Q018 and is a
property of the commander rather than of the terrain.

## 5. Size: the ceiling, measured

"The tested map ceiling" earlier in this document was a statement about what had
been tried, not about what the sim can carry, and the pool inherited it as if it
were a limit. It has now been measured (the runner's `sizeprobe` mode, 200 units
held constant, 400 ticks, a walled map so every unit paths a real route).

| Size | Cells | ms/tick | Flow-field build |
|------|-------|---------|------------------|
| 192x128 | 24,576 | 0.46 | 4.1 ms |
| 256x192 | 49,152 | 0.25 | 7.0 ms |
| 384x256 | 98,304 | 0.25 | 14.4 ms |
| 512x384 | 196,608 | 0.24 | 29.7 ms |

Two findings, and the second decides the ceiling.

**Steady-state cost FALLS as a map grows.** It is not area that costs, it is unit
DENSITY: two hundred units on a small map jam together and the separation pass
dominates, while the same two hundred spread over a large one barely interact.
Every size above sits under 6 per cent of the 8 ms budget, and the smallest map
is the most expensive of them.

**The flow field is the real constraint, and it is a spike rather than a load.**
FlowField.Build is a Dijkstra over every cell, so its cost is linear in area, and
it is paid whenever an order names a destination cell not already cached. At
192x128 that is 4 ms, which ships today and is the honest baseline. 256x192
costs 7 ms, still inside a single 60 fps frame. 384x256 costs 14 ms, about one
frame, and would be felt as a hitch on a busy order. 512x384 costs 30 ms and
would be felt plainly.

**The ceiling is therefore 256x192 for a map meant to ship**, twice the area of
the current big theatre and eight times a small one, at under one frame per new
destination. 384x256 becomes viable only if flow-field building is made cheaper
or amortised, which is not a map-design question and is not attempted here.

## 6. Detail: decoration is not terrain

Every drawable character in the format used to block movement, so the 8-to-10
per-cent density band was also a cap on how much of a map could be SEEN, and
maps read as bare ground carrying two or three large masses. The decorative
characters (',' scrub, ':' gravel, '=' road, '~' shallows) are drawn, passable,
and outside the density budget entirely, so detail and difficulty are no longer
the same dial. The band above continues to govern BLOCKING terrain and is
unchanged; decoration is reported separately by the generator and has no target,
because it costs pathing nothing.

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
