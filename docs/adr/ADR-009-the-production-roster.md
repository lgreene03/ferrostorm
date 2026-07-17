# ADR-009: The production roster: the barracks split and the tech tree

- Status: Ratified (Architect authored 2026-07-17; ratified by Luke 2026-07-17 under the directive "design out and build all these", covering this ADR as drafted)
- Date: 2026-07-17
- Deciders: Architect agent + Luke (plus Game Designer sign-off on the
  prerequisite table and Balance co-sign on the barracks price, recorded
  below)
- GDD/TDD feature served: GDD s5 line 45 (unit queues per
  production-structure type, per-structure rally); GDD s5 line 47 (the MCV
  behind a tech structure); GDD s7 line 86 (the five sidebar tabs); doc 00
  line 25's pillar, "Tech tree via structures"

## Context

The entire `Produce` handler checks three things (World.cs:955-967): that the
producer is a Factory (:957), that the unit costs more than nothing (:958),
and that it belongs to your faction (:959-960). So one factory builds a rifle
squad, an engineer, three tanks, a howitzer, a harvester and a 3000-credit
MCV, at tick one, with nothing else standing. This is Luke's play-test
question restated as a defect (doc 23 PROD-D1).

Wave 2 (commit 147f03b) put everything this decision needs on the shelf,
hash-neutrally, and it is verified at HEAD. `produced_at` is a required key
on the unit schema (schema.unit.json:5, :23), all twelve unit YAMLs author
it, DataLoader parses it (DataLoader.cs:133) and resolves it to a struct type
(DataLoader.cs:240), and the compiled defs mirror both fields verbatim
(World.cs:339-352, with `ProducedAt` defaulting to 2, the factory, at
World.cs:302). The Barracks is compiled (struct type 11, World.cs:455: cost
500, build 100 ticks, hp 800, draw 20, prereq power plant) and authored
(data/buildings/com_barracks.yaml). And none of it is read by anything that
decides: produced_at and prerequisites are carried and ignored, by design,
so that Wave 2 could land without an ADR. This ADR is where they stop being
ignored.

The supporting facts, verified at HEAD:

- The sim already runs N independent queues keyed per producer id, so a
  barracks costs an arm in a switch, not new machinery.
- The queue hash covers Factory queues only (World.cs:1977-1978), doc 23's
  PROD-D5: a Construction Yard divergence between two same-cost, same-ticks
  structure types is invisible until completion, and any new producer kind
  inherits the hole.
- Structure-side prerequisites are NEW authoring, not an edit: only
  com_barracks.yaml:16 and com_radar_uplink.yaml:17 carry the key; the other
  nine building files author none.
- Four units list com_factory as both producer and prerequisite
  (com_mcv.yaml:17 and :21, dir_howitzer.yaml:17-18,
  dir_bulwark_tank.yaml:16-17, sod_phantom_tank.yaml:17-18), a tautology
  that says nothing once produced_at gates production.
- The sidebar is two flat arrays under two headers (Sidebar.cs:18-37, eight
  entries); GDD line 86 wants five tabs. The client can also only reach one
  factory (doc 23 PROD-D6): a second factory is inert scenery despite the
  sim supporting N lines.
- **The AI finding, which inverts the intuitive risk.** The sceptic pass on
  doc 23's audits proved that an AI which does not know about produced_at
  does not "still build tanks and look almost normal". It builds nothing.
  `lineHolds = army >= _waveSize` (SkirmishAI.cs:177), and both faction
  branches produce only rifles and rockets until it holds (:179-193). The
  moment produced_at gates Produce, types 2 and 3 are refused at the factory,
  `army` stays 0 forever, lineHolds is never true, and the AI produces only
  refused infantry in perpetuity and never attacks (:327). The failure is
  total. The AI change is therefore IN SCOPE of this decision, not a
  follow-up.

Honesty about the cited authority, carried over from doc 23: GDD line 45
promises two building queues and two unit queues per production-structure
type. This ADR delivers one queue per producer, which is what already ships,
now with more producers. After everything below lands, line 45's four-queue
promise is still unimplemented. That gap is stated here rather than buried.

## Decision

1. **A producer notion.** `IsProducer(EntityKind)` covering Factory,
   ConstructionYard and Barracks (Airfield joins when it exists). It is used
   in FOUR places, three of which fail silently if missed: `Produce`
   (World.cs:957); ProductionSystem's producer test (World.cs:1799), miss it
   and the barracks queue never advances with no error; `CancelProduce`
   (World.cs:824), miss it and barracks orders are uncancellable; and the
   queue hash (World.cs:1977), which widens to all producer queues, closing
   PROD-D5 in the same regeneration this ADR already pays for.
2. **Produce branches on the carried data.** After the faction check, using
   `break` not `return` so the shared epilogue at World.cs:969 still runs:
   `if (!IsProducer(e.Kind)) break;` then
   `if (e.StructType != def.ProducedAt) break;` (this single line is the
   barracks split) then `if (!HasPrereqs(c.PlayerId, def.Prereqs)) break;`.
   `HasPrereqs` scans `_entities` in index order for a living entity of the
   commanding player with the required StructType: deterministic, O(n) per
   command, negligible against the TDD s6 budget.
3. **BuildStructure branches too.** The same HasPrereqs test against
   `StructureTypeDef.Prereqs` in the BuildStructure handler (World.cs:910),
   and the structure tree is authored across all building files per doc 23
   s4.3: power plant and construction yard and wall `[]` (the wall is never
   queued, ADR-005 clause 3; the yard is MCV-deployed); refinery, barracks,
   turret and veil projector `[com_power_plant]`; factory `[com_refinery]`;
   service depot and radar uplink `[com_factory]`; superweapon and the
   future airfield `[com_radar_uplink]`. The veil's hard faction gate stays;
   it is orthogonal.
4. **Queue semantics pinned rather than emergent:** killing a prerequisite
   mid-build does NOT cancel an already-queued item. The gate is on
   queueing. Doc 22 line 1524 demanded this be recorded; it now is.
5. **The barracks ships.** SpawnBarracks copying SpawnFactory's shape; the
   PlaceStructure arm; the MapLoader arm, mandatory because the Service
   Depot shipping without one (PROD-D7) is the proof this step gets skipped;
   IsStructure (World.cs:972-975) gains Barracks, RadarUplink and Airfield,
   real buildings that block, sell for half, repair, count for the victory
   test and are capturable, while IsBarrier (World.cs:982) is untouched. The
   client's `Ralliable` (SkirmishLive.cs:200) gains Barracks: rally is sim
   state by ADR-007 and infantry want it most. Infantry move to the
   barracks: com_rifle_squad, com_rocket_squad and com_engineer author
   `produced_at: com_barracks` (the first two are settled; the engineer's
   placement is Q007's decision, and the data already carries the
   genre-idiom candidate at com_engineer.yaml:19, reversible by one YAML
   line while the question is open).
6. **The sidebar grows GDD line 86's tabs.** Five tabs replace the two flat
   arrays, membership per doc 23 s4.3, AIRCRAFT absent until aircraft exist,
   matching the shipped philosophy that disallowed items are absent, not
   greyed (Sidebar.cs:139: "progression should read as the tree growing").
   Per-item visibility becomes the AND of three things where today it is
   one: the campaign allow-list (unchanged, an intersection with the tree,
   not a rival to it), the live prerequisite check, and, for units, a living
   producer of the right produced_at, which is what makes an empty INFANTRY
   tab teach the player to build a barracks. `FindOwnStructure(EntityKind)`
   generalises to `FindOwnStructureByType(int)`, which fixes PROD-D6 (the
   inert second factory) as a side effect and is the seam a later
   primary-building ticket hangs off.
7. **The AI learns the tree, as its own ticket in the same release.** The
   change list, sited precisely because the audit's version pointed at the
   wrong lines: insert the barracks and the radar into the build ladder
   (SkirmishAI.cs:94-101; without the radar the AI queues a superweapon it
   can never build and stalls forever); track the barracks in the entity
   scan (:38-90); route the produce command at :196, NOT the type-selection
   switch at :179-193; generalise the per-producer queue-depth guards at
   :145 and :164-165, or rifles routed to a barracks while the guard reads
   the factory queue means an unbounded barracks queue and a runaway
   pay-as-you-build treasury drain; add Barracks, RadarUplink and Airfield
   to the wave-target kinds (:77-88) or the AI walks straight past the
   building this ADR exists to add; and gate `expansionDesired` (:127) on
   the radar once the MCV moves behind it, or the AI saves 3500 credits for
   an MCV it cannot buy and stops spending on its army, the subtlest failure
   in the whole change.
8. **Campaign edits are mandatory in the same change or missions break.**
   mission-01's structure column `1,3,2,5` becomes `1,3,2,5,11`
   (data/campaign/campaign.txt:9), or the extended tutorial teaches the
   player to build infantry from a building that cannot build infantry;
   mission-03's becomes `1,3,2,5,8,11` (campaign.txt:11); mission-02 is
   `- | -` and unaffected. The header (campaign.txt:6-8) gains 11 barracks
   and 12 radar uplink, the two ids it already omits (4 construction yard,
   9 wall, doc 23 PROD-D11), and a note that struct type 11 and unit type 11
   (the engineer) are different namespaces, exactly the kind of thing that
   gets misread.
9. **The tautology cleanup rides the implementing ticket, with Game Designer
   sign-off.** The four `[com_factory]` prerequisites say nothing under
   produced_at and are rewritten with the rest of the unit prerequisite
   table per doc 23 s4.3's proposal: tier-2 vehicles (howitzer, bulwark,
   phantom) behind the Service Depot, the cheapest way to give the depot's
   1200 credits a build-order reason to exist, and the MCV behind the radar
   uplink pending Q006. Neither lands without the sign-off: the tier-2 move
   is a Balance and Game Designer call, and the MCV's gate is exactly what
   Q006 exists to decide. The barracks price (500, cheap and early, because
   that is what makes an infantry rush a strategy rather than a factory
   afterthought) carries the same co-sign.
10. **The airfield is OUT of this ADR.** Struct type 13 and
    `EntityKind.Airfield = 14` remain reservations (recorded, with the loop
    bounds, in ADR-008's amendment note to ADR-005). An airfield without
    aircraft is a 1000-credit building that produces nothing, occupies four
    cells and can only be sold; there is no partial version. It ships in the
    same drop as the first aircraft and never before, behind the air-layer
    ADR that P4-PORT-02 already requires, which also owns the slot-model
    Entity field and its hash cost (doc 23 s4.4 prices it for that ADR).
11. **Supersessions.** This ADR supersedes doc 22's stale numbering at its
    lines 1435, 1471 and 2062 (written before the ratified wall took struct
    type 9) and extends ADR-005's reservation exactly as recorded in
    ADR-008's amendment note, without touching the ratified text.

## Alternatives rejected

**Overload `role` to carry the producer.** The shipped roles are twelve
values for twelve units and `ToTypeDef` already overloads "economy" to mean
EntityKind.Harvester; a second overload makes role a second source of truth
with two meanings nobody can grep for. A dedicated required key is one line
per file and the schema enforces it.

**A hard-coded producer switch in code (doc 22 DEF-13's shape).** CLAUDE.md
forbids a second source of truth beside /data, and doc 22's own BD-17 already
superseded DEF-13 on exactly those grounds. The compiled defs mirror the
files as a round-trip reference; they do not get an independent opinion.

**Ship the split without the AI change, or bundle the AI into the same
regeneration.** Without it the AI builds nothing at all, the total-failure
finding in the Context, so shipping the split alone ships a broken opponent.
Bundled, every AI-involving golden row moves in the same diff as the tree
gating and the regeneration becomes unreviewable. Same release, separate
tickets, separate regenerations (doc 23 section 6: PROD-04 and PROD-06 each
their own).

**Take the airfield now because the barracks makes it look like twenty
lines.** It is twenty lines that ship a dead building and forecloses none of
the real work (the slot lease, the altitude plane, anti-air), all of which
waits on the air ADR regardless. Reserving the numbers costs nothing;
shipping the shell costs a regeneration and a refund magnet.

## Consequences

Easier: the tech tree exists and is authored in /data where CLAUDE.md says
gameplay numbers live; the barracks split makes infantry a doctrine rather
than a factory by-product; the sidebar teaches the tree by growing; a second
factory finally works; the CY queue hash hole closes; the veil, barracks and
radar all become reachable through one generalised routing seam.

Harder: Produce and BuildStructure gain failure modes the client must
surface honestly (a refused queue with no toast is REP-D1's sin again); the
campaign allow-lists become an intersection two documents must agree on; the
AI's build ladder gets longer and its failure modes subtler, which is why
its ticket carries its own regeneration and its own 6000-tick acceptance
run.

Committed to: the four IsProducer sites as a checklist; the queueing-time
prerequisite semantic; the campaign edits in the same change; the Q006 and
Q007 decisions before the final prerequisite table merges; the A11 co-signs
on the barracks price and the tier-2 move; the capture-scenario rewrite
below.

Hash impact: MOVES golden hashes, in two regenerations per doc 23 section 6.
PROD-04 (the split and the tree): gating changes behaviour wherever a
factory produced infantry, and the sharpest known case is named so nobody
trips over it: the capture scenario produces unit type 2 at a captured
FACTORY and prints "the prize produced a rifle squad under its new flag"
(Program.cs:1134); under the split that command is refused and the scenario
fails until it is rewritten to capture-appropriate production. Read it
before touching anything. A scenario that spawns structures directly never
runs the gate and its golden is safe; one that goes through Produce or
BuildStructure either gains its prerequisites or accepts the regeneration.
Decide per scenario, diff, and explain each in the commit; a blanket
regeneration is a rejection. The queue-hash widening (clause 1) lands inside
PROD-04's regeneration, not its own. PROD-06 (the AI): every AI-involving
row moves, the widest blast radius in the plan for about forty lines of
code, and it must NOT be bundled or the diff becomes unreadable. Changing a
golden hash requires an ADR plus Architect sign-off (CLAUDE.md); this ADR is
that sign-off, for both regenerations, once ratified.

Gates: TICKET-P5-PROD-04 implements the split and the tree; PROD-05 (five
tabs and producer routing, client-only, hash-neutral) depends on it;
PROD-06 (the AI) ships in the same release under its own regeneration. None
may start before ratification, and the final prerequisite table additionally
waits on Q006 and Q007.
