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

Shipped in four logical commits in the planned order, which is the order that
keeps the battery green at every boundary: the barracks hash-neutrally
(eaa90ba), the AI taught ahead of the gate with regeneration ONE (23e071f),
the gate itself with regeneration TWO (5d2c92f), and the tabbed sidebar,
client only and hash-neutral (2fbfcc0), with this paper trail riding last.

**Part 1, the barracks ships (ADR-009 clause 5).** SpawnBarracks in
SpawnFactory's shape, the PlaceStructure arm, the MapLoader arm (mandatory:
the Service Depot shipping without one is PROD-D7's proof this step gets
skipped), and IsStructure membership for Barracks AND Airfield, the Airfield
ahead of existing because membership is free and the omission is the failure
mode. `IsProducer` landed at three of its four sites - Produce's kind test,
ProductionSystem's producer test, CancelProduce - with the fourth, the queue
hash, deliberately held for the regeneration the ADR binds it to. The
Construction Yard was guarded out of Produce explicitly in the interim so its
STRUCTURE queue could never take a unit order in the window between commits;
the gate then subsumed that guard, because a yard's StructType is 4 and no
unit names it. `IsRallyable` became `IsProducer` in place, exactly as B2's
delivery note said it would, so the wire format never changed twice.
Acceptance was the empty diff: all 24 goldens byte-identical.

**Part 2, the AI learns the tree, and the finding that justifies the whole
ticket.** ADR-009 clause 7's change list, sited against today's code: the
barracks tracked in the entity scan, inserted into the ladder between the
refinery and the factory per doc 23 s4.3, the produce command routed at the
COMMAND site by the chosen type's own ProducedAt (leaving the selection switch,
which expresses doctrine, untouched), the queue-depth guard generalised to the
routed producer, and Barracks, RadarUplink and Airfield added to the
wave-target kinds. `expansionDesired` keeps its factory gate deliberately,
because com_mcv.yaml still authors `[com_factory]` and gating on the data is
what stops the AI saving 3500 credits for an MCV it cannot buy; a comment
binds that line to Q006 so the two move together.

THE FINDING, measured with a scratch probe against the real map rather than
reasoned about. Inserting the barracks rung broke mission-01 outright: the
ambush never sprang and the mission never won. The barracks costs 500 credits
the opening never used to spend, and mission-01 grants 5000 plus a timed 2000.
The commander bought plant, refinery, barracks, factory, a plant top-up and a
turret, arrived at tick 1261 holding 16 credits with ZERO harvesters, and
could never again afford the 1400-credit harvester that pays for all of it. No
income, no army, no wave, permanently. The yard ladder outbids the harvester
because it queues cheaper items first, and the pre-wave budget was only ever
hiding that by luck. The fix is a stated rule rather than a restored
coincidence: once the factory stands, want nothing more from the yard until
something is mining. It reads as economy before defence, which is what every
build order in the genre does anyway, and it covers the mid-game case honestly
too, because a commander who loses every harvester now rebuys the economy
before it rebuys a turret. Mission-01 wins again at tick 3574 against 3440
before, with the barracks in the ladder and both scripted messages firing.
This is scope beyond the ADR's enumerated list and is recorded here rather
than slipped in: it is forced BY the ADR's own ladder change, and the contract
puts the AI's build order in scope explicitly.

The AI's repair half of doc 23's PROD-06 (a depot rung and Repair commands) is
NOT in this wave. ADR-009 clause 7 does not order it, no ADR-009 mechanism
needs it, and bundling unrelated AI behaviour into this regeneration is what
the ADR forbids. Recorded as owed below.

**Part 3, the gate (clauses 2, 3 and 4).** Produce gains two lines after the
faction check, using `break` so the shared writeback epilogue still runs:
`e.StructType != def.ProducedAt` (the barracks split, and it really is one
line) and `!HasPrereqs(...)`. BuildStructure gains the same prerequisite test
against the structure tree, authored across all eleven /data/buildings files
and mirrored in the compiled defaults in lockstep so the selftest round-trip
still passes. HasPrereqs is an entity-index scan: deterministic by
construction, O(entities) per command rather than per tick. The queue hash
widened to every producer, shutting PROD-D5, and that hole was sharper than
the audits described - the genuinely invisible case is not a slow divergence
but a permanently silent one, because the factory and the refinery are both
2000 credits and 300 build ticks, so two yards queueing one each had
bit-identical BuildProgress and BuildPaid every tick until ReadyStructure was
written. Clause 4's semantic is pinned as behaviour, not prose: the gate is on
QUEUEING, so killing a prerequisite mid-build does not cancel what is already
queued. Campaign edits rode the same commit: mission-01 gains struct 11,
mission-03 gains struct 11, and the header stops omitting the yard and the
wall, documents the barracks and the radar, and says out loud that struct type
11 is the barracks while unit type 11 is the engineer.

**Scenario surgery, decided per scenario.** production: rifles are barracks
units now, so it gains a barracks sited clear of its phase 2 raider, keeping
it a measurement of production rather than combat; every timing number
survives. capture: the case ADR-009 named as the sharpest in the wave, "the
prize produced a rifle squad" at a captured FACTORY, refused outright under
the split. Rewritten to capture-appropriate production, a cannon tank, with
the budget stretched from 200 to 400 ticks because nobody in that scenario
owns a power plant and a 150-tick build at the half-rate floor takes 300.
construction: phase I queues a radar whose prerequisite is a factory it never
had, so it gains one spawned DIRECTLY, which is the honest fix - the gate is
on queueing, so a scenario that spawns its prerequisites never runs the tree
check and the phase keeps testing the radar rather than the tree. spawngate:
every stage that produces a rifle now produces it at a barracks, which is the
gate exercised end to end rather than worked around, and stage 1 additionally
proves the barracks accepts SetRally.

**The new prodgate mode**, additive on the catrefuse and spawngate pattern so
the golden list stays 24 lines: the split refusing both ways and costing
nothing (a factory refusing rifle, rocket and engineer; a barracks refusing
cannon, harvester and MCV; a yard refusing units outright), the split
accepting both ways with units really arriving on the ring, unit prerequisites
binding to the OWNER's own structures (an enemy refinery buys the harvester
nothing), clause 4's queueing-time semantic as behaviour, the whole structure
tree walked rung by rung, the veil's faction gate proven ORTHOGONAL to its new
prerequisite in both directions, the barracks end to end at 500 credits and
100 ticks building all three infantry types and walking them to its rally, and
PROD-D5 shut.

**Part 4, the tabs (clause 6), client only.** Four tabs, not five: AIRCRAFT is
absent because clause 10 keeps the airfield out of this wave and a tab over an
empty list would advertise a building the game cannot build. It ships with the
air ADR. NOTE, because it is a deliberate divergence from this ticket's
instruction text: the instruction suggested infantry buttons might be "greyed
with a reason", and the ratified ADR's clause 6 chose ABSENT instead ("the
campaign allow-list, the live prerequisite check, and, for units, a living
producer" all gate VISIBILITY, "matching the shipped philosophy that
disallowed items are absent, not greyed"). The instruction defers to the ADR
on exactly this point, so absence is what shipped, with an empty-tab note
carrying the honesty the greying was meant to provide. Membership derives from
the live catalogue's produced_at rather than a second client table; QueueUnit
routes the same way; FindOwnStructure generalises to FindOwnStructureByType,
picking the shortest queue and fixing PROD-D6's inert second factory as a side
effect. Ralliable gains the Barracks and NOT the Construction Yard, which
settles B2's deferred affordance question in the direction ADR-009 clause 5
names.

**Two findings from the offscreen run**, both caught in its own screenshots.
The panel was too narrow for its own tab bar: at 190 pixels the four titles
overflowed and Godot's TabBar silently collapsed INFANTRY and VEHICLES behind
scroll arrows, a sidebar hiding half the game, invisible to every assertion
because the tabs still existed and still answered. The panel widens to 250 and
the toast's offsets now follow Sidebar.PanelWidth instead of a copied literal.
And the screenshots themselves were lying: the checks drive hundreds of sim
ticks inside one frame, so an immediate capture shows a stale image of an
earlier moment and the evidence stops matching the assertion it is filed
under. Yielding real frames before each capture fixed it.

**Q006 and Q007, handled in the open** exactly as B3 handled Q008. The
mechanically enforceable half ships: the engineer's authored `produced_at:
com_barracks` is now ENFORCED, and the four `[com_factory]` unit prerequisites
that are tautologies under produced_at (MCV, howitzer, bulwark, phantom) are
left AS AUTHORED and enforced honestly, because they are trivially satisfied
at the factory the unit already comes out of and no behaviour hides behind
them. The curation half - tier-2 behind the depot, the MCV behind the radar -
waits for the Game Designer inside the decide-by date, as ADR-009's gates
clause requires. Dated status notes are on both question files. Q007's own
warning has now come true and is recorded there: the cheap window has closed,
and reversing the engineer's building is a behaviour change inside a
regeneration rather than a free YAML edit.

**The regenerations, explained row by row.**

REGENERATION ONE (the AI, 23e071f). FOUR rows moved: skirmish, expansion,
aisuper and mission, which are exactly the four scenarios that run a commander
with a Construction Yard to command. TWENTY are byte-identical, which is the
load-bearing half: no scenario without an AI may move in an AI-only commit.
mission03 is the interesting non-mover and deserves naming, because a careless
reading would call it a missed AI row - its map places no Construction Yard,
so Act returns decapitated on its first line and the Turtle's defence is
entirely pre-placed (B3's notes had mission03 moving on the honest draws,
which was a hashed PowerDraw literal, not the commander). Neutralisation proof
in B2's technique: with SkirmishAI.cs alone reverted on a scratch build and
every other change left standing, all 24 rows returned byte-exactly to the
pre-wave goldens.

REGENERATION TWO (the gate, 5d2c92f). SEVEN rows moved: production,
construction, capture, skirmish, expansion, aisuper and mission. SEVENTEEN are
byte-identical, so the stop condition never fired - every scenario that runs no
production at all is untouched. The four AI rows are the ones that matter,
because the obvious reading is that the gate started refusing AI commands, and
that reading is WRONG. Neutralisation proof: with the queue-hash widening
ALONE reverted and the gate and the tree left fully live, skirmish, expansion,
aisuper and mission returned EXACTLY to their regeneration-one values. So the
gate refuses the commander nothing, and those four move purely because their
yards' queues are now hash input. An independent 6000-tick
Directorate-against-Sodality probe agrees and says it directly: zero refused
Produce commands and zero refused BuildStructure commands on both sides.
capture's scratch value equals its final value, confirming its movement is
entirely the rewritten production; production and construction move on both
their amended worlds and the widened hash, which is why neither reverts.
Nothing moved unexplained; nothing explained failed to move.

The catalogue checksum moved to 0x5BF90D9D2F839D68. That is ADR-006's designed
replay-compatibility break, exactly as B3's honest draws moved it: saves and
replays from before this wave refuse by checksum rather than desyncing quietly
on numbers that no longer mean what they meant. Designed, not a regression.

### Verification evidence

Full battery exit 0 (selftest with both catalogue round-trips, determinism
24/24 double-run identical, every scenario assertion, defence load 1.575
ms/tick against the 8 ms budget with HasPrereqs in the command path, catrefuse,
spawngate and the new prodgate). Green standalone: saveload, replay,
campaignsave, spectate, lanchaos, and the balance gate. Purity and portability
greps clean. Goldens regenerated twice identically at each regeneration, LF
preserved, and byte-identical across the client commit. Both client builds
(Debug and ExportRelease) at zero warnings; project.godot byte-identical to
HEAD at commit.

AI acceptance under the gate, a 6000-tick Directorate against Sodality run on
the shipped skirmish map: both factions raised a barracks at tick 646; the
Directorate produced 20 units out of a barracks and 6 out of a factory, the
Sodality 21 and 4, so both fielded genuinely MIXED armies; both launched waves
(66 and 47 attack-move orders, biggest single wave 6 and 5); and neither had a
single Produce or BuildStructure command refused. The Sodality's zero surviving
units at tick 6000 is a battle outcome, not a production failure, which is why
the probe counts units at BIRTH by producer rather than reading a final
snapshot.

Offscreen client run: a temporary autoload (removed before commit, the
established pattern) drove the REAL menu start path into two live skirmishes,
51 checks, 0 failures, engine exit 0, seven screenshots. Four tabs exist and
navigate; membership by produced_at proven for six units and three structures;
the opening base offers ONLY the power plant, with barracks, refinery, factory,
turret and superweapon all correctly absent behind their prerequisites; the
tree OPENS as a real plant is built and placed through the sidebar; the
barracks follows by the same path; the INFANTRY tab fills with rifle, rocket
and engineer while VEHICLES still says what it needs; a rifle order lands in
the BARRACKS queue and not the factory; a barracks rally set through the real
right-click path is honoured by the squad that comes out; and Wave A's faction
gating is intact per tab both ways, including a Sodality player's veil
projector absent at the plantless open by the TREE rather than the faction and
appearing once its plant stands. The 24 harness recordings were deleted; the
user's 50 replays and both save slots are untouched, diffed before and after.

## Changed / Assumed / Needed next

**Changed:** sim/Ferrostorm.Sim (World.cs: IsProducer, HasPrereqs, the Produce
split and prerequisite gate, the BuildStructure gate, SpawnBarracks, the
PlaceStructure arm, IsStructure, IsRallyable widened, the queue hash widened,
the compiled structure tree, the FactoryStructType and BarracksStructType
constants, comment truth; MapLoader.cs: the Barracks arm; SkirmishAI.cs: the
entity scan, the ladder with its barracks and economy-first rungs, the routing
by ProducedAt, the per-producer queue guard, the wave-target kinds),
sim/Ferrostorm.Sim.Runner/Program.cs (production, capture, construction and
spawngate surgery; the new prodgate mode, its battery stage and its usage
line), data/buildings (the prerequisite tree across all eleven files plus note
truth), data/campaign/campaign.txt (both structure columns and the header),
game/scripts (Sidebar.cs: the TabContainer, the two structure arrays, the
three-way visibility AND, per-producer queue readouts, the empty-tab notes,
the widened panel and the tab verification surface; SkirmishLive.cs:
FindOwnStructureByType, QueueUnit routing, OwnsStructType, Ralliable,
CheckExitBlocked over producers, the readout, the toast offsets, the ADR-009
verification surface; ModelLibrary.cs: the interim barracks mesh),
sim/golden-hashes.txt (TWO regenerations, every row explained above), docs
(ADR-009 dated implementation notes, Q006 and Q007 status notes, the campaign
tracker, the ledger, TICKET-P6-ART-02, this file).

**Assumed:** no tab hotkeys, because GDD line 86 names the tabs and neither it
nor the ADR assigns keys (small-decision call, noted for UX). The AI's
barracks rung sits before the factory per doc 23 s4.3's ladder order. The
economy-first rung is stated above as a finding rather than assumed quietly.
The interim barracks mesh is the service depot rather than a second factory
silhouette, so the two producers read apart (one-line reversal). The tab
queue counters replace BD-10's two section-header counters, carried onto the
tabs that now own those queues. The panel width moved from 190 to 250 to fit
the tab bar.

**Needed next (from whom):** the Game Designer answers Q006 (the MCV's tier
gate) and Q007 (where the engineer is built) inside their decide-by date, and
both now cost a regeneration rather than a YAML edit, which Q007's own text
predicted; Balance and the Game Designer co-sign the tier-2-behind-the-depot
move before the final unit prerequisite table merges (ADR-009 clause 9);
art-pipeline owes the bespoke com_barracks model and sidebar icon
(TICKET-P6-ART-02) alongside the radar's (TICKET-P6-ART-01); the ai-engineer
owns doc 23 PROD-06's repair half (a depot rung and Repair commands),
deliberately not bundled into this wave's regeneration; UX owns whether the
tabs want hotkeys; and C3 (the four-queue sidebar, GDD line 45 in full) now
has the per-producer seam it needs, while C5's air ADR owns the AIRCRAFT tab
and the airfield together.
