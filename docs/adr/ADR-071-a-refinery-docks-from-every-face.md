# ADR-071: a refinery docks from every face, and a walk onto a building reaches whichever face is open
- Status: Ratified, by the orchestrator under the owner's standing authority of 2026-10-02, citing decision D4 of docs/tickets/P8-formidable-tracker.md ("Not reversible: this is a defect")
- Date: 2026-10-02
- Deciders: orchestrator (owner's standing authority, D4) + implementer agent
- GDD/TDD feature served: GDD s4 line 38 (a harvester "auto-returns to nearest refinery"); TDD s3 line 37 (harvester pathing gets its own planner, and path results must be deterministic); P8-15; finding ML-01; criterion F5

## Context

A walk onto a building aimed at the building's centre POINT, so the flow field
was built toward the one cell that point falls in. For a 2x2 footprint the
centre is the anchor plus one, which lands in the bottom-right footprint cell.
`FlowField.Build` seeds that single cell and expands outward. But the seed is
blocked (it is part of the building), its west and north neighbours are
blocked too (the same building), and the no-corner-cutting rule then refuses
every diagonal that passes one of them. The only cells the field can ever
reach first are the three east, south and south-east of the bottom-right
cell. Wall those three and the building is unreachable from every side, with
its west and north faces standing open. `Docked()` would accept a harvester on
any of them.

Measured by ML-01 and reproduced here with the old seed restored:

- **The fixture.** A refinery with exactly those three cells walled. The
  harvester banked 0 credits in 3000 ticks and stood frozen at ToRefinery, not
  moving, carrying 700.
- **A real base.** In the skirmish-01 Directorate mirror, the commander at
  start 1 seals the east and south of both its refineries into a courtyard.
  Its harvesters stood frozen at ToRefinery for **11961 of 19708
  harvester-ticks (60 per cent)** over the first 9000 ticks. The other seat
  froze for 66 of 22143. Single player always seats the AI at start 1.
- **The goldens carried it.** The `skirmish` golden's seat 1 harvester was
  frozen for 7394 of its 13065 ticks (57 per cent) and banked 11200 credits
  to seat 0's 27550. The `expansion` golden's harvesters were frozen for 4727
  of 17290 ticks (27 per cent). Two goldens had been certifying a broken
  economy.
- **The second symptom.** Contact units (capture, theft, sabotage, demolition)
  walk onto a building by the same centre point. The client harness lane saw a
  saboteur stand still for 120 ticks beside an open face. `dockfacegate`
  stage 3 reproduces it: with the old seed, a saboteur ordered onto a plant
  whose centre approach is walled never left its starting cell (20,24).

## Decision

**1. A walk onto a building routes to its footprint, not its centre cell.**
`FlowField.BuildToFootprint` builds a multi-source field. Its sources are
every in-bounds, passable cell of the one-cell ring round the footprint,
faces and corners alike, each at distance 0 and marked as a goal cell. The
ring is walked row by row (y, then x), which is ascending cell index, so the
seed order is fixed and sorted. An ascending list is already a valid
min-heap. The relaxation is `Build`'s own code, moved unchanged into a shared
`Relax`, and the single-target field still answers exactly as before
(measured: that refactor alone moved no golden). The cache keys footprint
fields by anchor and size in their own dictionary. That dictionary is only
ever looked up, never enumerated, and it is cleared with every other cached
field whenever passability changes.

`StepToward` uses the footprint field when the target cell is blocked AND
`DestinationStructureOf` names a structure. Only two walks exist in the sim:

- a harvester in ToRefinery whose target is its refinery's position;
- a contact unit whose explicit target passes CaptureSystem's own
  `CanBeActedOn` predicate and whose target is that building's position.

On a goal cell the unit closes on the nearest point of the footprint. That
point lies on its own cell's boundary, so the step can never clip a blocked
neighbour. Every edge point of a 2x2 or smaller footprint is within 1.42
cells of its centre, so the walk always ends inside the 1.75-cell contact
reach and the 2.83-cell dock.

The choice is derived each tick from state that is already hashed (HState,
RefineryId, ExplicitTarget, TargetX/Y). There is no new Entity field, no save
change and no wire change.

There is no repair walk to route. The Repair command toggles a building's
repair in place, and the client's send-to-depot order is a PathMove to a
point it computes. A mine does not block its ground, so a walk onto one is a
walk onto a point, as before.

**2. Fallback refinery.** A harvester in ToRefinery that is not docked and
cannot reach any open face of its assigned refinery takes the nearest other
refinery its owner has that it CAN reach. Distance is squared distance to the
refinery centre, the `FindNearestRefinery` measure. The comparison is strict
less-than in entity index order, so a tie goes to the lower id. The
reassignment sticks. If no other refinery is reachable, the harvester keeps
its assignment rather than oscillating between sealed refineries.

**3. The commander's apron.** `SkirmishAI.KeepsApron` filters
`TryFindPlacement`'s candidates. Nothing the commander places may cover a cell
of an own living refinery's one-cell ring. A refinery it places needs every
in-bounds cell of its own ring unblocked. A ring cell off the map edge is not
counted, because nothing can stand in it or seal it. The scan's order and
shape are untouched. Reorienting them is D5's job (P8-21), and doing both
here would have made their golden moves unattributable.

## Alternatives rejected

**Seed the footprint cells themselves at distance 0.** This is the smallest
code change. But a blocked seed cannot expand diagonally under the
no-corner-cutting rule, so a corner cell is reached only through a face.
A building whose only open neighbour is a corner, a position `Docked()`
accepts, would stay unreachable, and every route would still end with an aim
into the building. The explicit ring is what D4 decided, and it has neither
problem.

**Pick the nearest open face cell and route to it as an ordinary single
target.** "Nearest" by straight line picks a face the unit may not be able to
reach, which is exactly ML-01's courtyard. Picking the nearest REACHABLE face
needs a search from the faces outward, and that search is the multi-source
field.

**Carry the destination building in a new hashed Entity field.** This would
be more explicit than deriving it. But it costs a save-format bump and a hash
fold for something the hashed state already determines, and it adds a field
that every other writer of TargetX/Y would have to keep consistent.

**Route attack pursuit of buildings through the footprint field as well.**
Attack pursuit shares the flaw only where every open face is beyond weapon
range. It is a combat route, not one of D4's walks onto a building, and
including it would move combat goldens inside this regeneration and make the
moves unattributable. It is left on the centre cell. A future row can take it
if it is ever measured to matter.

**Fall back to the nearest refinery regardless of reachability (the existing
`FindNearestRefinery`).** With two sealed refineries this oscillates between
them forever. The fallback counts only refineries the harvester can reach,
and keeps its assignment when there are none.

## The measurement, before and after

**`dockfacegate`** (new, rides `match`): four stages, all PASS.

```
dockface: stage 1, east/south/south-east walled: a load banked at t=234, docked from (29,29) on the north-west corner
dockface: stage 2, every face walled: assigned the sealed refinery 0, fell back to 13, a load banked at t=267 from (44,32)
dockface: stage 3, the saboteur reached an open face and acted at t=58 from (29,29)
dockface: stage 3, the engineer reached an open face and acted at t=58 from (29,29)
dockface: stage 4, skirmish-01 DD o0 to t=9000: harvester-ticks frozen at ToRefinery seat 0 74 of 24705 (0 per cent), seat 1 70 of 23296 (0 per cent); longest freeze 1/1 ticks; apron clear throughout
dockface: stage 4, skirmish-01 DD o1 to t=9000: harvester-ticks frozen at ToRefinery seat 0 70 of 23296 (0 per cent), seat 1 74 of 24705 (0 per cent); longest freeze 1/1 ticks; apron clear throughout
```

The 70 to 74 frozen ticks per seat are the one-tick handover on each trip.
Loading switches to ToRefinery with the harvester stopped, and the next
HarvestSystem pass moves it. The longest freeze is 1 tick. Before this ADR it
was 2446.

**`seatfairgate`** (F5, non-binding until P8-21): 32 Normal mirrors, starts
swapped.

| | before | after |
|---|---|---|
| income within 15 per cent | 13 of 32 | 22 of 32 |
| pooled decided, by seat | 8/8 of 16 | 8/8 of 16 |
| pooled decided, by start | **14/2** | **8/8** |
| Directorate mirrors, by start | 14/0 of 14 | 6/6 of 12 |
| Sodality mirrors, by start | 0/2 of 2 | 2/2 of 4 |

In the mirrors the gate measures, docking accounts for the whole start
split. The gate still WOULD-FAIL, now on income alone: 10 of 32 matches are
over 15 per cent. The worst is skirmish-04 SS at 50 per cent; skirmish-08 DD
is at 19, skirmish-07 DD at 16, and both skirmish-06 pairings sit just over
15.

**`ladderprobe`** (every rung pairing, both seat orders, 324 unique matches):

| | before | after |
|---|---|---|
| seat 0 share of decided unique matches | **141 of 189 (75 per cent)** | **106 of 164 (65 per cent)** |
| undecided | 135 | 160 |
| Directorate mirror, seat 0 to seat 1 | 46-0 | 22-6 |
| Sodality mirror | 14-14 | 16-8 |
| Directorate seat 0 against Sodality | 53-4 | 59-3 |
| Sodality seat 0 against Directorate | 28-30 | 9-41 |

The ladder's seat bias falls by ten points and does not vanish. What is left
is a 65 per cent seat-0 share, a Sodality mirror that now leans 16 to 8, and a
faction gap that docking does not touch: the Directorate beats the Sodality
from either seat (59-3 and 41-9). That residue is what P8-21 (placement
orientation) and P8-53 (Fix64 rounding) exist to measure.

**`dockprobe`** (ADR-051's refinery-bottleneck probe): the most harvesters
unloading at one refinery at once went from 2/3/5/6 to 2/4/5/8 for 2, 4, 6
and 8 harvesters, because harvesters now spread across faces. Six-harvester
income rose from 66500 to 67900 (one refinery) and from 67200 to 68600 (two).
Three-harvester income is unchanged at 35000. ADR-051's reading, that a
refinery is not a bottleneck, holds and is now stronger.

## Hash and format

**Seven goldens moved and seventeen are byte-identical.** The change was
built in four stages, with `golden 2026` measured after each, so every move
has one cause.

- **A:** the `Relax` refactor alone moved 0.
- **B:** the footprint routing moved all 7.
- **C:** the fallback moved none further. No golden harvester is ever left
  without a route once any face counts.
- **D:** the apron moved the 4 commander goldens a second time.

The commander causes were split with a routing-only build (apron off) and a
probe that replays each scenario exactly; on the baseline it reproduces each
scenario's own report line.

| scenario | before | after | cause |
|---|---|---|---|
| `economy` | `0xC5FB6687C7AA3535` | `0xE370F4976CB58BA0` | routing |
| `skirmish` | `0x2DC6B7CC141FC20A` | `0xEE4F52D597293114` | routing, then apron |
| `expansion` | `0xEECA2D1C61A23359` | `0x1C5EC2136B4DD7D2` | routing, then apron |
| `aisuper` | `0x6A39F0D6EFA0B8BC` | `0x6E4761560D843874` | routing, then apron |
| `mission` | `0x6D491D77B5C4FD6D` | `0x699787A2B822C29C` | routing, then apron |
| `capture` | `0xF0464E85D418196E` | `0x9F328C60ABC7D45D` | routing (contact walk) |
| `mission02` | `0xCCCBBA6A55514025` | `0x7A6D404998519EED` | routing (contact walk) |

The measured cause of each move:

- **`economy`.** Harvester 1, working the eastern field, now docks at cell
  (13,12) at t=230, coming in straight from the south-east. Before, it
  curved round to (13,11) and docked at t=231. Every later trip shifts by
  one tick. The 8 docks and the 4013 credits are unchanged.
- **`skirmish`.** Before, seat 1's harvester was frozen for 7394 of 13065
  harvester-ticks and banked 11200 to seat 0's 27550.
  - Routing alone: the same refinery anchors, 50 frozen ticks, banked
    28247/31752.
  - The apron then moves every refinery anchor: seat 0's (8,6) to (9,6) and
    (12,10) to (9,12); seat 1's (87,51) to (88,51) and (83,55) to (86,57).
    (8,6) is refused because its own apron cell (7,6) is blocked.
  - Final: 45/43 frozen ticks, banked 29623/29173. The report line goes from
    14 to 31 entities destroyed and from treasuries 9743/673 to 11383/11488.
- **`expansion`.** Before, harvesters were frozen for 4727 of 17290 ticks.
  - Routing alone: 22 frozen. The third and fourth refineries arrive at
    t=3241 and 3946 instead of 3946 and 4831. Banked 13869 becomes 14552.
  - The apron moves all four anchors, (7,27), (11,29), (11,33), (3,25), to
    (8,27), (5,32), (3,35), (6,24). Banked 14410.
  - The far field is still mined to 0.
- **`aisuper`.** Routing alone leaves the placements, the 12029 credits
  banked and the superweapon at t=1921 all identical, so its move is the
  harvester's path and dock position alone. The apron refuses the
  commander's refinery at (10,27): that footprint's cell (11,27) lies in the
  apron of the pre-placed refinery at (12,26). The refinery goes to (5,30)
  instead. The superweapon is still placed at t=1921, and the enemy refinery
  is still battered to 1280/2000.
- **`mission`.**
  - Routing alone: the same anchors, and victory at t=3321 instead of 3411.
  - The apron moves the Rusher's refineries from (5,19) and (3,23) to (6,19)
    and (6,25). Its first attack-move leaves at t=2220 instead of 2040, and
    victory lands at **t=4541**.
  - That is inside `campaignsave`'s 7000-tick horizon (ADR-047). See
    Consequences.
- **`capture`.** The engineer walks straight along row 31 to the factory's
  west face and captures at t=78 from (29,31). Before, it veered to row 32,
  heading for the bottom-right cell's south neighbour, and captured at t=85
  from (30,32).
- **`mission02`.** The engineer captures from the west face, (35,18), at
  t=146 instead of from the south face, (36,20), at t=153. The scripted
  victory moves from 153 to 146. The scenario's own comment says the raiders
  "escort the engineer to the west face"; it now uses that face.

**Why the other seventeen do not move.** None of them routes a harvester or a
contact unit onto a building. `mission03` runs a commander, but its
harvester (bought at the factory) never once enters ToRefinery in its 4200
ticks, and with no Construction Yard the Turtle places nothing. Neither
mechanism is ever exercised there.

**Format.** No Entity field, no save version, no wire change, no `/data`
change, so the catalogue checksum is untouched. `determinism 2026` passes the
double run. Changing these seven hashes is a replay-compatibility break,
ratified under D4 as stated above.

## Proved to bite

- **Restoring the single-cell seed** (the footprint cache returning
  `Build(map, ax + size / 2, ay + size / 2)`) fails stage 1 with ML-01's own
  figure:

  > `dockface: with the cells east, south and south-east of the refinery's
  > bottom-right cell walled, the harvester banked nothing in 3000 ticks
  > (state ToRefinery, moving False, carry 700) - the dock route is seeded at
  > one cell again, and the open west and north faces are unusable (ML-01)`

  Stage 3 fails the same way:

  > `dockface: a saboteur ordered onto a power plant whose centre approach is
  > walled never acted in 400 ticks (at cell 20,24, moving True)`

- **Disabling the fallback** fails stage 2:

  > `dockface: with every face of its refinery walled the harvester banked
  > nothing in 3000 ticks (assigned 0) - it must fall back to the open
  > refinery 13 (ADR-071 clause 2)`

- **Disabling the apron** fails stage 4:

  > `dockface: skirmish-01 DD o0: a commander's refinery must keep a one-cell
  > clear apron (ADR-071 clause 3): t=526 seat 0 refinery 32 at (8,6) has
  > apron cell (7,6) blocked`

- **Old seed and no apron together** reproduce ML-01 exactly: seat 1 frozen
  for 11961 of 19708 harvester-ticks, 60 per cent. Stage 4's freeze check
  fails on its own:

  > `dockface: skirmish-01 DD o0: a seat 1 harvester stood frozen at
  > ToRefinery for 2446 consecutive ticks`

One observation for honesty about what stage 4 can see. With the old seed
restored but the apron kept, stage 4 passes. On skirmish-01 the apron alone
keeps the commander's docks open, so stage 4 proves the composite. Stages 1
to 3 prove the routing and the fallback on their own.

## Consequences

**What gets better.** A building is reached from whichever face is open, a
sealed refinery no longer strands its harvesters while another refinery
stands, and the commander cannot seal its own dock. The start split that F5
measures in mirrors is gone (14/2 to 8/8). Seat 1 no longer starves on
skirmish-01, which matters most in single player, where the AI always sits at
start 1.

**What is not fixed.** The ladder keeps a 65 per cent seat-0 share and a
faction gap, and F5's income clause still fails in 10 of 32 matches. Both go
to P8-21 and P8-53, which now measure against this base.

**A side effect to watch.** Mission-01's scripted victory comes 1130 ticks
later (3411 to 4541), and the cause is the apron's placement shift: routing
alone made it slightly sooner. It still wins well inside its horizon. Whether
a rush commander should give up base compactness for an apron is the kind of
question the playtest owns.

**What we are committed to.** Any new walk onto a building must go through
`DestinationStructureOf`, or it will silently route to a centre cell again.
Attack pursuit of a building still uses the centre cell, by decision rather
than by oversight (see Alternatives rejected).

**Found beyond the row and recorded, not acted on.** The `expansion` golden's
report line says the second base "added its refinery". Measured in both
builds, all four of the commander's refineries stand at the home base:
`TryFindPlacement` scans oldest first (ADR-050's fix), so the home yard's
rings take every refinery. The scenario's assertion (more than one base's
worth of refineries across two yards) passes on the count alone.
