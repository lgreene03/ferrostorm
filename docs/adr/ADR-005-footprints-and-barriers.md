# ADR-005: Variable structure footprints and barrier placement rules

Status: Proposed (Architect authored 2026-07-15; awaiting Luke's ratification)
Closes: the ADR-005 slot reserved in docs/adr/ADR-open-queue.md ("Tile size,
grid resolution, footprint rules")
Gates: TICKET-P5-DEF-03, DEF-04 and DEF-05. None of them may merge before
this document is ratified.

## Context

The simulation assumes every structure occupies exactly 2x2 cells. The
assumption is not a constant in one place; it is spread across the code as
an idiom. `World.FootprintSize` is a const 2 (World.cs:354). `FootprintCentre`
derives the entity position from an anchor (World.cs:376). Five call sites
recover the anchor by subtracting one from the cell of the centre
(World.cs:699, 768, 1081, 1500 and SkirmishAI.cs:385). Every one of those
sites is silently wrong for any structure that is not 2x2, and the wrongness
is not a crash: it is an off-by-one in placement validation and footprint
blocking, which is the worst kind of defect this project can ship because
the golden hashes would happily bless it.

The classic base-building idiom this game is inspired by requires cheap
chained barrier segments at 1x1. Ferrostorm has no barrier entity of any
kind today: turrets exist and fire through the standard combat system, but
there is nothing to build a perimeter from. The design documents already
commit to the surrounding decisions. The GDD resolves Q2 as strict classic
adjacency. GDD s6 line 53 commits to artillery beating static defence, which
is the counter that keeps barriers from making turtling unbeatable. GDD s7
line 86 already lists a Defence sidebar tab that has nothing to put in it.

The prize is a system that adds a real gameplay pillar while regenerating no
golden hash, because the footprint generalisation is arithmetically identical
for the 2x2 case and every new branch is unreachable until a barrier exists.

## Decision

1. **Footprint becomes a per-type property.** `World.FootprintOf(int
   structType)` returns 2 for types 1 to 8 and 1 for barrier types. The
   entity position remains the footprint centre. The anchor is recovered as
   `Map.CellOf(X) - (size - 1)`, which for size 2 is exactly the existing
   `Map.CellOf(X) - 1`. The const `FootprintSize = 2` is retained as the
   default so no existing call site changes meaning.
2. **Barriers are structures for some purposes and not others.** They block,
   sell, repair and take damage like any structure. They are EXCLUDED from
   the victory test (World.cs:1553), from engineer capture (World.cs:921),
   and from combat auto-acquisition (World.cs:1000). A player whose last
   possession is a wall has lost; an engineer cannot convert a fence; and no
   unit wastes its cooldown chewing masonry it was not ordered to attack.
3. **Barriers are bought per segment at placement, with no ready slot and no
   build time.** BuildTicks 0, which World.cs:661 already refuses to queue,
   so the sidebar flow cannot accidentally admit them. This revives the
   upfront-cost model that World.cs:339 documents as the pre-SIM-05
   behaviour: the cost leaves the treasury the moment the segment lands.
4. **Barriers project a build radius of 2, for other barriers only.** A
   player may crawl a wall outward two cells and one hundred credits at a
   step. A barrier never anchors a non-barrier structure, so nobody crawls a
   factory across the map to the enemy base. This is the clause that keeps
   clause 3's cheapness from becoming an exploit.
5. **Per-player barrier cap of 80 segments.** Justified against the ratified
   TDD s6 budget of 600 active units and 200 structures
   (03-technical-design-document.md:59): two players at 80 barriers plus
   roughly 16 real buildings each is about 192 structures, inside budget.
   The cap is a performance guarantee, not a design flourish.
6. **Gates are DEFERRED, and the blocker is recorded rather than waved at.**
   Passability is one global grid (Map.cs:7) and FlowFieldCache's only
   invalidation is Clear() (Map.cs:140). A gate that is passable to its owner
   and solid to the enemy therefore requires either per-player flow fields or
   an incremental flow repair. Neither exists, and inventing one to ship a
   gate would be the tail wagging the dog. Walls without gates are a complete
   feature; the player leaves a gap, as players did in 1995.

Struct type numbering is reserved here so that later tickets cannot silently
disagree: **type 9 is the wall segment, type 10 is the gate** (reserved,
unimplemented per clause 6). DEF-12's emplacement and DEF-13b's bastion must
number around that reservation, and `FootprintOf` must agree with whatever
they choose. A footprint mismatch is silent and fatal.

## Consequences

The golden-hash position is the whole point and is stated explicitly:
clauses 1 to 5 are constructed to be behaviour-neutral in the absence of
barriers. EntityKind values are appended, never renumbered. No Entity field
is added. `Fix64.FromFraction(2, 2)` is bit-identical to `Fix64.One`, so all
size-2 arithmetic produces the same bits it produces today. Therefore **all
23 existing golden hashes must remain byte-identical after DEF-03, DEF-04 and
DEF-05**, and that is their acceptance criterion rather than an aspiration.
If a hash moves, the change is wrong; it does not get regenerated.

DEF-04 and DEF-05 must merge in the same batch. DEF-05 is the attack-move
breach behaviour, and without it a walled base makes the flow field return
-1 for attackers across the whole map: the AI's waves halt at their own base
and attack-move silently stops working for the human too. Shipping the wall
without the breach is shipping a bug, not shipping half a feature.

The balance risk is turtling. It is mitigated by three existing systems
rather than by hope: the howitzer's anti-building warhead, the superweapon,
and GDD s6's ratified commitment that artillery beats static defence.
TICKET-P5-DEF-17 turns that commitment into a CI gate that proves a walled
base falls to a siege army, so the mitigation is measured on every push.

Deferring gates costs the player a convenience and costs the project
nothing structural: if per-player flow fields are ever built for another
reason, clause 6 is revisited with the blocker already gone.
