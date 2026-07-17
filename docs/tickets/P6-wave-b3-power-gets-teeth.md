# P6 Wave B3 delivery notes: power gets teeth

TICKET-P5-PWR-03 + TICKET-P5-PWR-04 + TICKET-P5-PWR-05 (doc 23 Wave 5),
implementing ADR-008 as ratified: the turret gate on CombatSystem's own
pre-combat tally, the honest draws, and the radar blackout tied to a now
buildable Radar Uplink, with ALERT-02's last clause (radar goes dark). One
golden regeneration for the whole wave, authorised by ADR-008's hash-impact
clause. Plan comment first (CLAUDE.md workflow rule 2), delivery notes and
the standard footer at the end.

## Plan

labels: persona:commander gdd:s5-48 gdd:s7-85 gdd:s7-86 phase:6 owner:sim-engineer + client-engineer + balance + game-designer

Approach, in the ADR's own clause order, all three parts in one wave because
the ADR forbids splitting them.

**Honest draws (clause 3).** com_construction_yard 0 to 20, com_refinery 0
to 40, dir_superweapon 100 to 150, edited in the /data YAMLs AND
`DefaultStructureType` in lockstep: the compiled table is the round-trip
reference the selftest asserts the files reproduce (ADR-006 kept /data the
runtime truth; the compiled twin must match or the selftest and ApplyData
both refuse). The stale notes go with it: com_refinery and
com_construction_yard stop documenting zero draws as deliberate, and
dir_turret's "buys nothing back today" stops being true the moment the gate
lands. The A11 co-sign is recorded by the ADR itself (deciders line plus
clause 3), including the explicitly accepted zero-margin opening base at
exactly 100 supply against 100 draw.

**The turret gate (clauses 1 and 2).** One guard line in CombatSystem,
immediately after the real guard `if (!e.Alive || e.WeaponId == 0) continue;`
and ABOVE the cooldown decrement (a dead turret does not reload), reading the
spans Wave 2 staged at the top of CombatSystem, which today feed nothing:

    if (e.Kind == EntityKind.Turret && !AtLeast75(combatSupply[e.PlayerId], combatDraw[e.PlayerId]))
    { _entities[i] = e; continue; }

Turret kind only (the GDD says defensive turrets; emplacement and bastion
join by kind when they land). Divisionless AtLeast75, inclusive boundary:
15 against 20 FIRES, 14 does not. Snapshot semantics exactly as the ADR
pinned them: PRE-combat power, because the CombatSystem tally runs before the
damage pass, so a plant destroyed on tick N disables its turrets on tick N+1,
while ProductionSystem keeps its own post-combat tally under the per-system
rule. No new tally, no hoisting, no restructure.

**Scenario surgery (clauses 5, 6, 7), enumerated before regenerating.**

- `walls` PHASE G, the trap the ADR exists to put in writing, amended in the
  SAME commit as the gate: player 1 gains a plant in worldG (supply 100
  against draw 20) so the howitzer-takes-nothing-back assertion is re-proven
  against a LIVE, POWERED turret, with a new assertion pinning that power;
  and a new PHASE J in the same battery proves the other truth separately:
  an in-range unpowered turret withholds fire with its cooldown frozen at
  zero, supply 14 against draw 20 stays dark, supply 15 fires ON THE NEXT
  TICK the tally sees it, and selling the topping-up plant freezes the
  reload mid-cycle.
- ScenarioStealth: a plant for player 0 ABOVE the turret spawn, so rule 1
  passes because stealth holds rather than because the turret is dead; the
  line-586 plant keeps its rule-2 geometry role.
- ScenarioVeil: a plant for player 1, whose turret owns the baseline
  assertion; without it the scenario throws (the good failure).
- ScenarioSuperweapon: draw is 170 under the new numbers (super 150 + yard
  20) and plants supply 100, so the boundary equality is unreachable by
  whole plants; a third plant with the nullable `supply: 70` override lands
  the post-sale total at exactly 170 and the inclusive-boundary assertion
  survives unloosened. No assertion is deleted or loosened anywhere in this
  wave.
- ScenarioConstruction: the yard itself now draws 20 and a bare yard builds
  at the half-rate floor, which would slide every timing assertion; a
  spawned plant keeps the scenario testing the sidebar flow at full power.
  Also gains PHASE I: the Radar Uplink queued at the yard, placed, standing
  with draw 80, the radar-live predicate (living uplink AND supply covering
  draw) crossing down when plants sell, recovering when one is rebuilt, and
  going dark for good when the uplink itself sells for exactly 450
  (IsStructure membership proven by behaviour).
- ScenarioProduction and ScenarioDepot are provably immune and their golden
  rows must be byte-identical; a diff that moves them is a defect.
- Every SpawnTurret site re-read by a human with the owner's supply against
  draw recorded (the acceptance the ADR demands; results in the delivery
  notes below, including the bench rig, StDebug and the MapLoader path,
  where the re-read found real rot the ADR's own parenthetical missed:
  the three MISSION .fmap files DO carry structures, and mission-02's camp
  turret has no plant).

**The Radar Uplink becomes buildable (clause 4).** SpawnRadarUplink (the
SpawnServiceDepot shape, struct type 12, compiled and authored since Wave
2), the PlaceStructure arm, the MapLoader arm, `EntityKind.RadarUplink`
added to IsStructure (the silent killer: sell, repair, capture,
rubble-unblock, adjacency and the victory test all hang off it), and the
sidebar entry (common faction, per the com_ prefix and the data file; the
sim gates only the veil projector on faction). No bespoke model exists:
KindModel maps kind 13 to sod_veil_projector.glb, the interim doc 23
prescribes (its `dish` child already spins under ScanRig); the real model is
owed to art-pipeline. The AI ladder gains the radar before the superweapon
with a hasRadar flag, at the BD-09 affordability threshold (credits 1500).

**The blackout and the alert (clause 4 + ALERT-02's last clause).**
Client-side, at the minimap refresh, which has no guard today: the minimap
is lit only while a living own Radar Uplink stands AND supply covers draw
(GDD line 48's below-100 clause; deliberately NOT the 75 per cent turret
threshold). While dark, per doc 22 BD-09's sketch: the cinder panel plus
centred bone RADAR OFFLINE text; no terrain, no fog, no dots, no frustum;
pings still render (blanking the base-under-attack ping would stealth-nerf
the alert system); clicks stop navigating (small-decision call: a map you
cannot see should not silently order the camera; the click is swallowed
rather than passed through). The alert is edge-triggered on the LOSS
crossing only, mirroring the low-power alert's `_wasBrownOut` pattern with
the flag starting false, so the radarless opening fires no alert: RADAR
OFFLINE toast, a new distinct `alert_radar` cue from the synth pipeline (a
carrier tone fracturing into static: signal loss, not a siren), the
placeholder VO clip `vo_radar_offline` (one `gen` line in make_vo.sh),
a gold minimap ping at the uplink, and the position recorded for
jump-to-event. Recovery relights the minimap the frame supply covers draw
again. The blackout binds only the human (SkirmishAI reads Entities with no
visibility filter), exactly as the ADR records.

**Turret-offline client feedback.** Per-owner brown-out computed each frame
with the sidebar's own divisionless test; an offline turret's meshes get an
unshaded near-black MaterialOverlay wash (dark and inert, no new palette
token: black at partial alpha shades the existing materials), applied to
BOTH sides' turrets deliberately - watching the enemy's guns go dark is the
payoff of the plant-snipe pillar - and the single-selection readout says
OFFLINE - LOW POWER, the REP-04 readout idiom.

**Q008 and the opening experience.** ADR-008 gates the blackout on Q008.
Luke's directive of 2026-07-17 ("design out and build all these", the P6
tracker's authority line) lists the radar blackout in this wave's scope and
today's instruction assigns ALERT-02's radar clause here explicitly, so the
blackout SHIPS with the ADR's rule as written: every shipped map and mission
opens radarless (the day-one blackout, option 1 behaviour). What stays open
on Q008 for the Game Designer, inside its decide-by date, is the CURATION
half: whether missions grant or script radar coverage. Recorded on Q008
itself, not silently resolved.

**Gates and regeneration.** Full battery exit 0. ONE regeneration via
`golden 2026` after all sim work lands, on both platforms via CI. The diff
explained scenario by scenario with B2's neutralisation technique: with the
behavioural surface temporarily reverted (gate line, the three compiled
draws, the AI radar rung) on a scratch build, rows that revert to the
pre-wave goldens are proven pure hash-input movers (PowerDraw is a hashed
field, so a draw change moves a hash with zero behavioural change); rows
that stay moved are the amended scenarios and must each carry a mechanism.
Production and depot byte-identical in the FINAL run or stop. The walls row
WILL move this wave (its phase G is amended and phase J added); the ADR's
"walls will not move" describes the unamended counterfactual that made the
trap invisible, which is exactly why the amendment is bound to the gate.

Assumptions: the AI radar affordability threshold (credits 1500) is BD-09's
number, kept as a small-decision call; the radar sidebar entry sits between
SERVICE DEPOT and VEIL PROJECTOR in tech-cost order; enemy turrets dim too
(stated above, reversible in one predicate).

Interfaces touched: World.cs (guard, draws, SpawnRadarUplink, PlaceStructure
arm, IsStructure, comment truth), MapLoader.cs (RadarUplink arm),
SkirmishAI.cs (hasRadar + ladder rung), Runner Program.cs (five scenario
surgeries + walls phase J + aisuper radar assertion + construction phase I +
StDebug mirror), Sidebar.cs (entry), ModelLibrary.cs (interim mapping),
SkirmishLive.cs (radar gate, alert, turret dim, readout, StructNames),
Minimap.cs (blackout render + input swallow), synth.py + make_vo.sh (+ the
two generated assets), data/buildings (3 draw edits + 2 note fixes + radar
note), data/missions/mission-02.fmap (a plant for the camp),
sim/golden-hashes.txt (the wave's one regeneration), docs (ADR-008 dated
notes, Q008 note, tracker, ledger, this file).
