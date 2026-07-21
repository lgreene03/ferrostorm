# P6 Wave B2 delivery notes: rally into the sim, then spawn occupancy and the refund

TICKET-P5-SPAWN-03 + TICKET-P5-SPAWN-04 and the SPAWN-D1 exit-move half of the
spawn work (doc 23 Wave 4), implementing ADR-007 as ratified, in the order the
ADR declares load-bearing: rally FIRST, occupancy second. One golden
regeneration for the whole wave, authorised by ADR-007's hash-impact clause.
Plan comment first (CLAUDE.md workflow rule 2), delivery notes and the standard
footer at the end.

## Plan

labels: persona:commander gdd:s5-45 phase:6 owner:sim-engineer + architect

Approach, in ADR-007's own order of commitments, with the two measured traps
(doc 23 section 7 items 1 and 2) driving the structure.

**Wire format.** `CommandType.SetRally = 16`, appended after LaunchSuper. The
unused hole at 1 is not reused. Replays are safe by construction: the command
type rides as a byte in both the .frep format and the LAN wire, so no format
version moves.

**State.** `Entity` gains `Fix64 RallyX, RallyY; bool HasRally; bool
Departing;` appended after FieldCloaked, never inserted. Unset is
HasRally = false with RallyX/RallyY zero; a clear (AuxId == -1) restores
exactly that canonical state so a rallied-then-cleared structure serialises
identically to one never rallied. `ApplyCommandCore` case SetRally accepts
only a producing structure the commanding player owns, through a named
predicate (`IsRallyable`: Factory or Construction Yard today) that ADR-009's
IsProducer will widen in place when the barracks lands, and clamps X and Y
exactly as the Move case does.

**Exit move.** In ProductionSystem, after a successful spawn, the new unit's
TargetX/TargetY/Moving/UseFlow are set directly (the sim issues no commands of
its own; ADR-007 rejects inventing an internal command channel). Destination:
the producer's rally when HasRally; otherwise a deterministic default two
further steps out along the chosen spawn offset (cell centre of
`(scx + 2*dx, scy + 2*dy)`, clamped in bounds), the same offset logic every
time, so the eleven-cell ring can never saturate in the no-rally default game.
Harvesters honour the rally and the default exit equally, matching the
client's shipped auto-harvest precedent.

**The close-rally trap (SPAWN-D3) and the plan's stated choice.** The
crowd-arrival shortcut declares any flow move complete within 4 cells of its
target, so a rally 2 cells out would be a silent no-op. The fix is ADR-007's
`Departing` flag, and this plan sets it on EVERY production exit move, not
only rally spawns: the default no-rally exit lands 2 to 3 cells out and would
be swallowed by the same shortcut, resurrecting the ring-saturation trap the
exit move exists to kill. Departing is set at spawn (units only; the shortcut
is kind-gated so harvesters never consult it), joins the shortcut guard as
`&& !e.Departing`, and clears in MovementSystem the tick the unit's cell
differs from its cell at the start of that tick (it starts in the spawn cell,
so the first boundary crossing is exactly "left the spawn cell") or the tick
the walk ends. Chosen over nudging the exit target past the arrival radius
because it keeps the player's actual rally point as the unit's destination -
a nudged target would park units somewhere the player did not point - and
over an arrival radius scaled to distance because one boolean appended to
state is a smaller, ADR-ratified surface than a new radius formula in the
hot movement path.

**Hash and save.** The four fields append to ComputeStateHash after
FieldCloaked in declaration order. Save format v4 (SaveMagicV4): WriteEntity
and ReadEntity append the same four fields in hash order; v1/v2/v3 saves all
still load with rally unset and Departing false, which is what those saves
meant. DEVIATION FROM THE ADR AS WRITTEN, recorded in a dated note on the ADR
itself: ADR-007 names "v3 magic", but Wave B1 (ADR-006) has since taken v3
for the catalogue checksum, so this wave's bump is v4 building on v3. The
catrefuse gate's v2 byte surgery is updated for the wider entity record, and
the new gate proves v3 and v2 downgrades still load.

**Occupancy and the hold (SPAWN-04), after rally lands.** The spawn loop
gains an occupancy test over live entities at each candidate offset, using
ValidPlacement's own predicate (`!o.Alive || IsStructure(o.Kind)` skips;
anything else standing in the cell blocks it) rather than a hand-rolled kind
list, per doc 23. A direct entity scan per completion attempt is chosen over
reading SeparationSystem's `_buckets` because the buckets hold only
Unit/Harvester kinds and are built before ProductionSystem runs, so they miss
both the broader predicate and any unit another factory spawned earlier in
the same tick; completions are rare and the scan is O(entities), negligible
against the TDD s6 budget. When every offset is blocked the unit is HELD:
the queue head is not popped, BuildProgress stays at total, and - THE
MEASURED TRAP - BuildPaid stays intact, because the zeroing of
BuildProgress/BuildPaid moves from the top of the completion block to after
a successful spawn (and into the CY branch). With BuildPaid == full cost and
progress pinned at total, the pay-as-you-build formula owes exactly zero
every held tick, so a held factory spends nothing by construction, the queue
behind the head stalls honestly, and a cancel while held still refunds in
full. The silent-deletion path (SPAWN-D2) dies: the only way a completed
unit leaves the queue is by spawning, and ProductionComplete (with C set)
fires on the spawn tick. The client shows EXIT BLOCKED through the existing
W3-19 toast path from a post-Step read of the held state (progress at total
with a non-empty queue); no new event, no hash surface.

**Client thin mirror.** The `_rally` dictionary is deleted; the IssueOrder
rally branch sends CommandType.SetRally through the ordinary command path
(closing SPAWN-D9); `_rallyMarkers` stays as a node-lifetime cache whose
positions are written every frame from the sim's own RallyX/RallyY, so the
marker can never drift from the truth; the ProductionComplete rally
application is deleted (the sim moves the unit itself). The one behavioural
carry-over: a harvester produced by a rallied factory still enrols in
`_manuallyStopped` (rally beats auto-harvest, P5-ECON-07 clause 2a), now
keyed off the sim's HasRally on the producer rather than the dead dictionary.
Rally persists across saves BY THE SIM, which resolves Q004's rally half.

**Gates.** A new additive `spawngate` mode (the catrefuse pattern: standalone
and inside the match battery, not a golden scenario, so the golden list stays
24 lines): SetRally validation and clear; rally exit and arrival; a 2-cell
close rally still clearing the mouth; ten units back to back settling on ten
distinct cells; the walled-in hold spending EXACTLY ZERO credits over 100
held ticks with nothing deleted and the resume firing ProductionComplete
with C set the tick a cell frees; save v4 round-trip with live rally state;
v3/v2 downgrade surgery still loading with rally unset; SetRally
round-tripping the .frep format.

**Regeneration.** ONE, after both parts land, under ADR-007's ratified
sign-off. Every scenario's hash moves by construction (four new fields joined
the hash input for every entity); which scenarios ALSO moved behaviourally is
established by experiment, not assertion: with the four h.Add lines
temporarily neutralised, scenarios whose hashes revert to the old goldens are
proven hash-input-only movers, and the rest are the behavioural movers the
diff must explain. LF endings via the existing Console.Out.NewLine guard.

Assumptions: the client's Ralliable affordance stays Factory-only (the sim
accepts a Construction Yard per ADR-007, but a CY's products are placed, not
spawned, so a CY rally is inert state; the affordance question belongs to
PROD-05's tab work). The offscreen client harness is a temporary autoload
removed before commit, per the Wave A/B1 pattern.

Interfaces touched: World (CommandType, Entity, ApplyCommandCore,
MovementSystem/StepToward, ProductionSystem, ComputeStateHash),
World.Serialization (v4), Runner Program.cs (spawngate + catrefuse surgery +
battery wiring), SkirmishLive (SetRally click path, marker sync, held toast,
verification surface), docs (ADR-007 note, Q004 resolution, tracker, ledger,
this file), sim/golden-hashes.txt (the wave's one regeneration).

## Delivery notes

Shipped in three commits in the ADR's load-bearing order: rally first
(4cba971), occupancy and the hold second (dc49054), the campaign's first
golden regeneration third (c5b2f90), docs and verification riding last.

**Part 1, rally in the sim.** CommandType.SetRally = 16 appended after
LaunchSuper; Entity gains RallyX/RallyY/HasRally/Departing after
FieldCloaked, hashed and serialised in declaration order. The SetRally case
accepts only an owned producing structure through the named IsRallyable
predicate (Factory or Construction Yard, per the ADR, ahead of ADR-009's
IsProducer), clamps exactly as Move does, and AuxId -1 clears back to the
canonical unset state so cleared and never-rallied serialise identically.
A produced unit leaves the mouth with movement fields written directly
(no internal command channel, per the ADR): towards the rally when set,
otherwise towards a deterministic default two further steps out along the
chosen spawn offset, so the eleven-cell ring cannot saturate in the
no-rally default game. Departing suppresses the crowd-arrival shortcut
until the unit leaves its spawn cell (or the walk ends), which kills
SPAWN-D3: a 2-cell rally moves the unit. Deliberate reading, recorded on
the ADR: Departing is set on EVERY production exit move, not only rally
spawns, because the default exit lands inside the same 4-cell shortcut
radius and would otherwise resurrect the inverted-order trap. Save format
v4 (the ADR's dated note covers the v3-to-v4 shift after B1 took v3);
v1/v2/v3 all load with rally unset, proven by downgrade surgery in the
gate. Replays and the LAN wire carry SetRally as an ordinary byte-typed
command; no format version moved.

**Part 2, occupancy and the refund.** The spawn loop tests standing
entities as well as terrain, with ValidPlacement's own predicate
(CellOccupied: dead and structure entities skip, anything else standing in
the cell blocks it) rather than a hand-rolled kind list. A direct
entity-index scan is used instead of SeparationSystem's buckets, and the
choice is stated in the code: the buckets hold only Unit/Harvester kinds
and are built before ProductionSystem runs, so they would miss both the
broader predicate and same-tick spawns from an earlier factory;
completions are rare, so the scan is negligible. When every offset is
blocked the unit is HELD: queue head unpopped (a paid unit can never
vanish; SPAWN-D2 dead), BuildProgress pinned at total, and BuildPaid kept
intact by moving the zeroing below the hold decision, so the
pay-as-you-build formula owes exactly zero every held tick - the measured
3000-credits-per-second trap is structurally impossible rather than merely
avoided. The line behind the held head stalls honestly; ProductionComplete
(with C naming the producer) fires the tick a cell frees; a cancel while
held refunds in full through the untouched BuildPaid. The client raises
EXIT BLOCKED through the existing W3-19 toast path from a post-Step read
of the held state (no new event, no hash surface).

**Client thin mirror.** The `_rally` dictionary is deleted. The right-click
issues CommandType.SetRally on the ordinary path (closing SPAWN-D9);
markers are written every frame from the sim's own RallyX/RallyY and
reaped when the producer dies or clears, so a marker can neither drift
from the truth nor outlive it; the ProductionComplete rally application is
gone (the sim moves the unit itself). A harvester produced by a rallied
factory still enrols as parked (rally beats auto-harvest, P5-ECON-07
clause 2a), now keyed off the sim's HasRally. Rally persists across saves
BY THE SIM, resolving Q004's rally half (resolution appended to Q004; the
auto-resume half stays open there by ADR-007's own scoping).

**The regeneration, explained row by row.** All 24 rows move BY
CONSTRUCTION: four fields joined the per-entity hash input. Which rows
also moved behaviourally was established by experiment: with the four Add
lines temporarily neutralised, EIGHTEEN scenarios reverted exactly to the
old goldens - movement, pathing, economy, combat, attackmove,
construction, stealth, veterancy, victory, artillery, superweapon, crush,
veil, waypoints, mission02, mission03, depot, walls - proving their
behaviour bit-identical (they complete no factory production, so no exit
move and no occupancy test ever fires; mission03's Turtle never funds a
factory inside its scripted-survival horizon, which is why an AI mission
sits in this group; mission02's raid is over in 153 ticks). The SIX that
stayed moved are exactly the production scenarios, each with a mechanism:
production (three rifles plus the low-power rifle now exit and spread),
skirmish (both AIs produce armies all match), expansion (the AI's economy
and army production), aisuper (production en route to the superweapon),
mission (the Rusher's waves), capture (the captured factory's rifle squad
walks its exit move). Nothing moved that cannot be explained; nothing
explained failed to move.

### Verification evidence

Full battery exit 0 (selftest, determinism 24/24 double-run identical,
match with all 24 scenario assertions plus defence load, catrefuse and the
new spawngate, lan 5/5). Also green standalone: saveload, replay,
campaignsave, spectate, lanchaos, and the balance gate. Golden file
regenerated ONCE, byte-identical against a fresh `golden 2026` run, LF
endings verified byte-level (the Console.Out.NewLine guard). Purity and
portability greps clean. Both client builds (Debug and ExportRelease) at
zero warnings; project.godot byte-identical at commit.

The spawngate (additive, standalone and in the battery): SetRally
validation (unit refused, enemy structure refused, Move-exact clamping,
CY accepted, -1 clearing canonically); three rallied rifles exiting and
settling at a 14-cell rally with C naming the producer; the 2-cell rally
moving the unit off its spawn cell and inside the crowd radius
(SPAWN-D3); a v4 save round-tripping live rally state bit-exact at the
save point and bit-exact after 200 resumed ticks, with v3 and v2
downgrade surgeries loading rally-unset and unchecked respectively; ten
units back to back occupying ten DISTINCT cells; the walled-in factory
held at 100 per cent for 100 ticks spending EXACTLY ZERO credits with
nothing spawned and nothing deleted and no completion event, then
releasing THE TICK a cell freed, completion carrying C, queue popping to
the second item, and two rifles costing exactly 400 credits across hold
and release; the multi-unit close rally spawning 4+1 units at distinct
positions with at least one at the rally; SetRally and its clear
round-tripping the .frep format.

Offscreen client run: a temporary autoload (removed before commit, the
established pattern; project.godot byte-identical at commit) drove the
REAL paths headless, 12/12 checks PASS, engine exit 0. A menu-configured
skirmish-01 battle scripted in a factory; the REAL right-click path
(IssueOrder through the camera's own unprojection) became
CommandType.SetRally in the sim at the clicked point (20.49, 37.50,
within the click's float tolerance of the aimed 20.5/37.5); the marker
rendered from the sim's RallyX/RallyY, showed for the selected producer
and hid on deselect while the sim kept the rally; two rifles queued
through the real sidebar path left the factory and settled at the marker;
with the ring walled in, the held factory raised the real W3-19 toast
("EXIT BLOCKED - RIFLE SQUAD WAITING"), spent EXACTLY ZERO credits over
30 held ticks with nothing spawned and nothing deleted, and released the
paid unit the tick a cell freed; the battle saved to an empty slot,
quit to the menu, resumed through the menu's own flow, and the rally came
back FROM THE SIM at exact coordinates with the marker rebuilt purely
from sim state; a rifle produced AFTER the resume settled at the restored
rally. The `_rally` dictionary no longer exists to have carried any of
it: the client compiles without it and every HasRally answer is a sim
read. Slot 4 was confirmed empty before use and cleaned after; harness
recordings were deleted; the user's own saves (slots 1 and 2) and all 50
pre-existing replay files were untouched.

Two harness-side findings from getting there, neither a product defect:
the scripted test base had no power plant, so the held rifle built at the
GDD s5 50 per cent floor and the harness had to wait for completion
rather than assume 75 ticks; and raw EntityCount is the wrong stability
probe across long windows because the enemy AI's own placements and
production move it, so the hold assertions count player-0 rifles.

## Changed / Assumed / Needed next

**Changed:** sim/Ferrostorm.Sim (World.cs: SetRally, Entity rally fields,
Departing guard and lifecycle, exit move, CellOccupied, the hold
restructure with the zeroing moved below the hold decision, hash;
World.Serialization.cs: save v4 with tolerant v1/v2/v3 loads),
sim/Ferrostorm.Sim.Runner/Program.cs (spawngate mode + battery stage,
DowngradeSave shared surgery, catrefuse's v2 surgery rebased onto it),
game/scripts/SkirmishLive.cs (SetRally click path, sim-driven markers,
EXIT BLOCKED toast, HasRally surface reads sim, `_rally` deleted),
sim/golden-hashes.txt (the wave's ONE regeneration, every row explained
above), docs (ADR-007 dated deviation note, Q004 rally resolution,
campaign tracker, ledger, this file).

**Assumed:** the client's rally affordance stays Factory-only while the
sim accepts a CY per the ADR (a CY's products are placed, not spawned; a
CY rally is inert state until ADR-009's producer split makes the
affordance question real, and PROD-05 owns it). Departing on the default
exit move is within ADR-007's intent (recorded on the ADR itself). The
close-rally crowd packs at separation spacing, so distinct POSITIONS (not
cells) is the honest stacking bar there; the distinct-CELLS bar holds for
the no-rally spread, per doc 23's own acceptance.

**Needed next (from whom):** ADR-009/PROD-05 (client-engineer) extends
Ralliable and the sim's IsRallyable into the shared IsProducer when the
barracks lands, and decides the CY rally affordance; the Q004 auto-resume
half still wants its Architect ruling with the doc 22 economy batch;
netcode (netcode agent) inherits SetRally in the wire format with nothing
to retrofit.
