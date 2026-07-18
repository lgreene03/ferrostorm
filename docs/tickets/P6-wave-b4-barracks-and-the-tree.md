# P6 Wave B4 delivery notes: the barracks split, the tabbed sidebar and the tech tree

TICKET-P5-PROD-04 + TICKET-P5-PROD-05 + TICKET-P5-PROD-06's tree half (doc 23
Wave 6), implementing ADR-009 as ratified: produced_at and prerequisites stop
being carried-and-ignored and are enforced in Produce and BuildStructure, the
Barracks becomes buildable and produces the infantry, the sidebar grows GDD
line 86's tabs, and the AI learns the tree. TWO golden regenerations per doc
23 section 6 and the ADR's own hash-impact clause, which explicitly REJECTS
bundling them ("bundled, every AI-involving golden row moves in the same diff
as the tree gating and the regeneration becomes unreviewable"). Plan comment
first (CLAUDE.md workflow rule 2), delivery notes and the standard footer at
the end.

## Plan

labels: persona:commander gdd:s5-45 gdd:s5-47 gdd:s7-86 phase:6 owner:sim-engineer + client-engineer + ai-engineer + game-designer + balance

Approach, ordered so the battery is green at every commit boundary. The ADR
demands separate regenerations for PROD-04 and PROD-06, but the split without
the AI is a broken opponent (the total-failure finding) and the AI cannot
route to a barracks that is not spawnable. The order that satisfies both:

**Commit 1, the barracks ships, hash-neutrally (ADR-009 clause 5).**
SpawnBarracks copying SpawnFactory's shape (struct type 11, compiled and
authored since Wave 2); the PlaceStructure arm; the MapLoader arm (mandatory:
the Service Depot shipping without one is PROD-D7's proof this step gets
skipped); IsStructure gains Barracks and Airfield (RadarUplink joined in B3),
so the barracks blocks, sells for half, repairs, counts for the victory test
and is capturable, while IsBarrier is untouched; `IsProducer(EntityKind)`
(Factory, ConstructionYard, Barracks; the Airfield joins when it exists) is
introduced and used at THREE of its four sites now: Produce's kind test,
ProductionSystem's producer test (miss it and the barracks queue never
advances with no error) and CancelProduce (miss it and barracks orders are
uncancellable). The FOURTH site, the queue hash, deliberately waits for
commit 3: widening the hash input is regeneration material and the ADR binds
it to PROD-04's regeneration. The sim's IsRallyable becomes IsProducer in
place, exactly as B2's note said it would, so SetRally accepts a barracks.
No scenario builds a barracks yet: all 24 goldens must stay byte-identical,
and that empty diff is this commit's acceptance.

**Commit 2, the AI learns the tree (PROD-06's ADR-009 half), regeneration
ONE.** The ADR clause 7 change list, sited against today's line numbers:
barracks tracked in the entity scan; the ladder becomes plant, refinery,
barracks, factory, plant top-up, refinery-per-base, turret, radar,
superweapon (doc 23 s4.3's order; the barracks rung sits before the factory
because 500 credits of infantry production is the cheap early opening the
price was signed for); the produce command at the routing site (NOT the
type-selection switch) routes by the chosen type's ProducedAt (infantry to
the barracks, vehicles to the factory); the queue-depth guard for unit
production reads the ROUTED producer's queue, or rifles routed to a barracks
while the guard reads the factory queue means an unbounded barracks queue
and a runaway pay-as-you-build drain; Barracks, RadarUplink and Airfield
join the wave-target kinds so the AI does not walk straight past the
building this wave exists to add. `expansionDesired` keeps its factory gate
UNCHANGED with a comment: the MCV's prerequisite stays `[com_factory]` this
wave (Q006 is open, see below), so the factory gate is exactly the data, and
the radar gate joins in the same change that moves the MCV behind the radar.
The AI's repair half of doc 23's PROD-06 (depot rung, Repair commands) is
NOT in this wave: ADR-009's clause 7 does not order it, no ADR-009 mechanism
needs it, and bundling unrelated AI behaviour into the same regeneration is
what the ADR forbids; recorded as owed below. At this commit the gate does
not exist yet, so the AI's barracks routing works against an ungated
Produce; every AI-involving golden moves (skirmish, expansion, aisuper,
mission, mission03) and nothing else may move. Neutralisation proof: with
the SkirmishAI edits reverted on a scratch build, `golden 2026` must
reproduce the pre-wave file byte-exactly.

**Commit 3, the gate (PROD-04), regeneration TWO.** In Produce, after the
faction check, with `break` not `return` so the shared epilogue still runs:
`if (e.StructType != def.ProducedAt) break;` (the single line that is the
barracks split) then `if (!HasPrereqs(c.PlayerId, def.Prereqs)) break;`.
HasPrereqs scans `_entities` in index order for a living entity of the
commanding player with each required StructType: deterministic, O(n) per
command, negligible against the TDD s6 budget. BuildStructure gains the same
HasPrereqs test against StructureTypeDef.Prereqs after its faction gate. The
queue hash widens from Factory to IsProducer, closing PROD-D5 inside this
regeneration as the ADR orders. The structure tree is authored across all
eleven building files per doc 23 s4.3 AND mirrored in DefaultStructureType
in lockstep (ADR-006: the compiled twin must match or the selftest refuses):
power plant, construction yard and wall `[]`; refinery, barracks, turret and
veil projector `[com_power_plant]`; factory `[com_refinery]`; service depot
and radar uplink `[com_factory]`; superweapon `[com_radar_uplink]`. The
catalogue checksum moves: ADR-006's designed replay-compatibility break,
exactly as B3's draw changes moved it; old saves and replays refuse by
checksum rather than desyncing, and that is designed, not fixed. Campaign
edits ride the same commit or missions break: mission-01's structure column
becomes `1,3,2,5,11`, mission-03's `1,3,2,5,8,11`, and the header comment
gains 11 barracks and 12 radar uplink, documents the two ids it already
omits (4 construction yard, 9 wall), and says out loud that struct type 11
and unit type 11 are different namespaces. Queueing-time semantics per ADR
clause 4: killing a prerequisite mid-build does NOT cancel an already-queued
item; the gate is on queueing, and the new prodgate proves it.

Scenario surgery, enumerated per the ADR ("decide per scenario, diff, and
explain each; a blanket regeneration is a rejection"):
- ScenarioProduction produced rifles at a FACTORY. It gains a barracks;
  rifles (phases 1 and 2) queue there, and phase 3's cannon stays at the
  factory, its true producer. Every timing number survives: the rifle is 75
  ticks at full power either way, and supply 100 covers draw 60.
- ScenarioCapture is the ADR's named sharpest case: "the prize produced a
  rifle squad under its new flag" at a captured FACTORY, refused under the
  split. Rewritten to capture-appropriate production: the prize produces a
  CANNON TANK (type 1, Directorate, produced_at the factory, no
  prerequisites) under its new flag. The loop budget stretches from 200 to
  400 ticks because a 150-tick cannon at the 50 per cent no-power floor is
  300 ticks where the rifle was 150.
- ScenarioConstruction queues the radar (phase I) at a yard with NO factory
  standing, refused under the tree. It gains a direct-spawned factory at
  (4,12) from tick 0: direct spawns never run the gate (the gate is on
  queueing), the early phases stay at full power (supply 100 against draw
  60), and phase I's crossings all survive with the factory's 40 counted
  (dark at 100 against 180, relit at 200, dead with the uplink).
- The spawngate's producers become barracks wherever rifles are produced
  (stages 2 to 7): the rifle's producer IS the barracks now, the maths (75
  ticks, 200 credits, 2x2 footprint, same spawn ring) is unchanged, and the
  gate now exercises the new producer end to end, rally included. Stage 1
  keeps its factory and CY validation and ADDS the barracks acceptance.
- ScenarioSuperweapon spawns its superweapon directly and never runs the
  gate; immune. The sixteen no-production rows (movement, pathing, economy,
  combat, attackmove, stealth, veterancy, victory, superweapon, artillery,
  crush, veil, waypoints, mission02, depot, walls) must be BYTE-IDENTICAL
  through BOTH regenerations; any of them moving is the stop condition.
- AI rows at this commit: no AI command is ever refused (the commit-2 ladder
  already builds every prerequisite before its dependant), so their movement
  here is the queue-hash widening alone where a producer queue is non-empty
  at the final tick. The neutralisation proof demonstrates exactly that.

A new additive `prodgate` mode (the catrefuse/spawngate pattern: standalone
and in the battery, never a golden): produced_at refusal (factory refuses
rifle and engineer, barracks refuses cannon and harvester, CY refuses
units, queue empty and credits untouched in every case); produced_at
acceptance (rifle at barracks spawns on the ring, cannon at factory);
unit-prerequisite refusal and acceptance (harvester refused without a
refinery, accepted with one; the MCV's authored `[com_factory]` accepted at
a factory, the tautology enforced honestly); structure-tree refusal and
acceptance at the yard (turret without plant, factory without refinery,
radar without factory, superweapon without radar, each refused then
accepted once the prerequisite stands); the veil's faction gate proven
orthogonal to its new plant prerequisite; the queueing-time semantic (a
queued harvester survives its refinery's death mid-build); the barracks
full flow (yard queues it behind a plant, places it, it produces rifle,
rocket and engineer, honours a rally, and its orders cancel with exact
refund); and the PROD-D5 hole shut (two yards queueing same-cost,
same-ticks factory-versus-refinery now hash differently the tick they
queue).

**Commit 4, the five tabs and producer routing (PROD-05, client-only,
hash-neutral).** Sidebar.cs's two flat arrays and two headers become a
styled TabContainer over four visible tabs: BUILDINGS (1 power plant, 3
refinery, 11 barracks, 2 factory, 8 service depot, 12 radar uplink),
DEFENCE (5 turret, 9 wall, 6 superweapon, 7 veil projector), INFANTRY
(units whose produced_at is the barracks), VEHICLES (produced_at the
factory). AIRCRAFT is ABSENT until aircraft exist: that is the ADR's
ratified UX choice, matching the shipped philosophy that disallowed items
are absent, not greyed ("progression should read as the tree growing"), so
the honest empty-infantry-tab state is an empty tab, and the tab itself
joins with the air ADR. Membership is derived from the live catalogue's
ProducedAt (a new delegate), not a second table. Per-item visibility
becomes the AND of three things where today it is one: the campaign
allow-list and Wave A's faction gating (the Init-time static gate,
unchanged, intact per tab), the live prerequisite check against the
player's standing structure types (recomputed each frame from sim reads),
and, for units, a living producer of the right produced_at, which is what
makes an empty INFANTRY tab teach the player to build a barracks.
MakeButton, cost and seconds labels, icons, the pulsing PLACE button, the
power bar and the queue badges are untouched; the queue readouts move with
their tabs (BUILDINGS and DEFENCE both read the yard's queue, INFANTRY the
barracks queue, VEHICLES the factory queue, and the Q counters sit on the
tab titles). SkirmishLive generalises FindOwnStructure(EntityKind) to
FindOwnStructureByType(int) choosing the live producer with the SHORTEST
queue (ties to the lower id), which fixes PROD-D6 (the inert second
factory) as a side effect and is the seam a later primary-building ticket
hangs off; QueueUnit routes by the unit's ProducedAt; the client's
Ralliable gains Barracks (the Airfield joins when it exists); the hint bar
and selection readout offer rally on both producers; CheckExitBlocked
watches every unit producer, not just factories. The CY rally affordance is
settled per the ADR: clause 5 names Barracks as the gain and nothing else,
so the client still offers no rally click on a Construction Yard (its
products are placed, not spawned; the sim keeps accepting the command as
inert state per ADR-007). ModelLibrary maps kind 12 to com_service_depot
as the interim mesh (the established B3 pattern; visually distinct from
the factory so a player can tell the producers apart) and the bespoke
model and icon are owed to art-pipeline. StructNames already carries
BARRACKS at index 11.

**Q006 and Q007, handled in the open (the ADR's Gates clause).** Both are
open with the Game Designer until 2026-07-24, and the ADR says the final
unit prerequisite table "additionally waits on Q006 and Q007", while its
clause 9 conditions the tier-2 move on a Balance call whose co-sign the
deciders line records only for the barracks price. So this wave implements
the mechanically-enforceable part and defers the curation, exactly as B3
did with Q008: the engineer's authored `produced_at: com_barracks` is now
ENFORCED (the mechanically-enforceable part; one YAML line reverses it
under Q007, now at regeneration cost, which Q007's own text predicted);
the four `[com_factory]` tautologies (MCV, howitzer, bulwark, phantom)
stay AS AUTHORED and are enforced honestly (trivially satisfied at a live
factory, which is what a tautology means; no behaviour hides behind them);
and the tier-2-behind-the-depot and MCV-behind-the-radar rewrites land
with the Q006/Q007 resolutions as a data-plus-AI follow-up under ADR-009's
already-ratified regeneration sign-off. Dated status notes go on both
question files.

**Verification.** Full battery exit 0 at every commit boundary; goldens
byte-identical after commit 1, regenerated with per-row explanation and
neutralisation proof at commits 2 and 3; determinism double-run 24/24; the
standalone gates (saveload, replay, campaignsave, spectate, lanchaos,
balance) green at the wave's head; purity and portability greps clean; both
client builds at zero warnings; project.godot byte-identical at commit. An
offscreen client run through a temporary autoload (removed before commit,
the established pattern) drives the REAL menu start path into a live
skirmish and proves: the tabbed sidebar navigates; the INFANTRY tab is
empty with no barracks standing (the ADR's absent-not-greyed rule) while
BUILDINGS offers the barracks; a barracks queued and placed through the
real sidebar path makes the infantry buttons appear, and a rifle queues
from the INFANTRY tab only, producing from the barracks; a barracks rally
set through the real right-click path is honoured by the spawned squad; a
Sodality run shows Sodality tab contents (Wave A gating intact per tab);
screenshots captured. User saves and replays untouched (listings diffed).

Assumptions: no tab hotkeys (GDD line 86 names the tabs, neither the GDD
line nor the ADR assigns keys; small-decision call, noted for UX); the
interim barracks mesh is the service depot rather than a second factory
silhouette (one-line reversal); the AI barracks rung sits before the
factory per doc 23 s4.3's ladder order; tab Q-counters replace the two
section-header counters (BD-10's counters, carried onto the tabs that now
own those queues).

Interfaces touched: sim/Ferrostorm.Sim (World.cs: IsProducer, HasPrereqs,
Produce, BuildStructure, SpawnBarracks, PlaceStructure arm, IsStructure,
IsRallyable, queue hash, structure-tree compiled defaults, comment truth;
MapLoader.cs: the Barracks arm; SkirmishAI.cs: scan, ladder, routing,
guards, wave targets), sim/Ferrostorm.Sim.Runner/Program.cs (production,
capture and construction surgery; spawngate producer surgery; prodgate mode
+ battery wiring + usage), data/buildings/*.yaml (the prerequisite tree +
stale-note truth), data/campaign/campaign.txt (columns + header),
game/scripts (Sidebar.cs tabs; SkirmishLive.cs routing, rally affordance,
readouts, exit-blocked, verification surface; ModelLibrary.cs interim
mapping), sim/golden-hashes.txt (TWO regenerations, each explained), docs
(ADR-009 dated implementation notes, Q006/Q007 status notes, tracker,
ledger, this file).

## Delivery notes

(appended on completion)
