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

## Delivery notes

Shipped in four logical commits: the honest draws (d18c8ab), the turret
gate with the walls amendment bound into the same commit as the ADR
demands (0facf2a), the Radar Uplink and the blackout (dccb8d9), and the
wave's one golden regeneration (52004ee), with the verification surface
and this paper trail riding last.

**Part 1, honest draws.** com_refinery 0 to 40, com_construction_yard 0
to 20, dir_superweapon 100 to 150, in the YAMLs and DefaultStructureType
in lockstep; the selftest and catrefuse both prove the twins agree (the
catalogue checksum moved to 0xA7FD50AAE2D64D78, which is ADR-006's
designed replay-compatibility break: pre-wave saves and replays refuse
against the new numbers by checksum rather than desyncing). The stale
notes went with the numbers. The zero-margin opening base (exactly 100
supply against 100 draw) ships as the ADR explicitly accepts, A11
co-sign recorded in the ADR's deciders line. A consequence stated
honestly: a fresh yard with no plant now builds its first plant at the
GDD s5 half-rate floor, because the yard itself draws 20 against a
supply of 0. That is the curve the ADR priced, not an accident.

**Part 2, the turret gate.** One guard line in CombatSystem, exactly the
ADR's insertion point (after the real guard, above the cooldown
decrement) reading the Wave 2 spans that fed nothing until now, so the
pre-combat snapshot semantic is what the staged code always documented:
a plant destroyed on tick N disables its turrets on tick N+1, while
ProductionSystem keeps its post-combat tally under the per-system rule.
Turret kind only. Inclusive boundary, proven at the exact edge in the
walls battery's new PHASE J: an in-range unpowered turret withheld fire
for 60 ticks with its cooldown untouched, supply 14 against draw 20
stayed dark, supply 15 fired ON THE NEXT TICK the tally saw it, and
selling back below the line froze the reload mid-cycle. PHASE G was
amended in the same commit: player 1's turret is now powered (100
against 20) and a new assertion pins that power, so the howitzer result
is proven against a turret that is alive, powered and out-ranged rather
than silently dead.

**The SpawnTurret re-read, the ADR's acceptance criterion, every site.**
- ScenarioStealth (Program.cs): player 0's turret, previously 0 supply
  against 20 draw for the first 100 ticks (rule 1 would have passed
  because the turret was dead). Now 100 against 20 from tick 0, recorded
  in the report string.
- ScenarioVeil: player 1's turret, previously 0 against 20 (the baseline
  assertion would have thrown, the good failure). Now 100 against 20,
  recorded in the report string.
- Walls PHASE G: player 1, previously 0 against 20 with the hash unable
  to move; now 100 against 20 and asserted.
- The bench rig (DefenceLoadGate): ten turrets and ten plants per
  player, 1000 supply against 200 draw for the full 1000 ticks; verified
  by re-derivation, not assumption, and the rig still holds its whole
  population (the gate polices that itself).
- StDebug: a debug entry point, not a golden; given the same plant as
  the stealth scenario it mirrors, so it stays a faithful repro rig.
- MapLoader.cs, THE FINDING: the ADR's clause 7 parenthetical says "the
  shipped .fmap files carry no structures today", and that is true of
  the four skirmish maps but FALSE of the three mission files, which the
  human re-read caught. mission-01: player 1's camp is plant + factory +
  turret, 100 supply against 60 draw, turret LIVE, no change needed.
  mission-03: player 0 starts at 100 supply against 120 draw (the
  refinery's new 40), which is 83 per cent: turrets LIVE, production
  slowed until the Turtle's supply-ahead rung tops up, which it does
  immediately. mission-02: the camp turret drew 20 against a supply of
  ZERO, the exact walls-trap shape - the gate would have silenced the
  compound's only gun for the whole mission with no assertion to notice.
  Fixed in the same change: mission-02.fmap gains a plant for the camp
  (100 against 60), restoring the pre-wave fight exactly.
- One more pre-existing find from phase I's arithmetic: in
  ScenarioConstruction, the phase E raider spawns in auto-acquire range
  of the phase-B plant, stops to engage under the shipped hold-to-fire
  rule, and kills it quietly mid-scenario. No assertion ever covered it
  and no assertion covers it now; it is recorded in a comment at the
  phase I sells, which count supply from live state instead.

**Part 3, the Radar Uplink and the blackout.** The sim side is exactly
the enumerated surgery: SpawnRadarUplink, the PlaceStructure arm, the
MapLoader arm, and IsStructure membership (sell, repair, capture,
rubble-unblock, adjacency and the victory test all proven by phase I's
sell at exactly 450 and the freed footprint). The sidebar entry is
common-faction; the AI raises the radar between the turret and the
superweapon behind a hasRadar flag at BD-09's 1500-credit threshold,
and aisuper asserts radar-before-superweapon. The blackout is
client-side at the minimap refresh, the documented rule precisely: lit
only while a living own uplink stands AND supply covers draw (the
below-100 clause, not the turret's 75). Dark renders the cinder panel
and centred bone RADAR OFFLINE per BD-09's sketch, pings exempt
deliberately; clicks are swallowed while dark (small-decision call,
noted in the plan). ALERT-02's last clause is edge-triggered on the
loss crossing from a flag that starts false, so the radarless opening
stays silent: toast, the new alert_radar cue (synthesised in the
pipeline: a carrier fracturing into band-passed static, 715 ms, RMS
-12.4 dBFS, peak -3 dBFS like the rest of the set), the placeholder
vo_radar_offline line (one regenerable `gen` entry in make_vo.sh, same
legal caveat as the whole VO set), a gold ping at the uplink and the
jump-to-event record. Turret feedback: per-owner brown-out drives an
unshaded near-black MaterialOverlay wash on turret meshes (both sides'
turrets, deliberately: seeing the enemy's guns go dark is the
plant-snipe pillar's payoff) and the single-selection readout appends
OFFLINE - LOW POWER. No bespoke radar model exists: kind 13 renders the
sod_veil_projector interim doc 23 prescribes (the dish spins under
ScanRig); the bespoke model is owed to art-pipeline
(TICKET-P6-ART-01).

**Q008, handled in the open.** ADR-008 gates the blackout on Q008. The
P6 tracker's authority (Luke's directive of 2026-07-17) lists the radar
blackout in this wave's scope and today's instruction assigns ALERT-02's
radar clause here explicitly, so the mechanics ship with the day-one
blackout as the ADR wrote it: every shipped map and mission opens
radarless. The curation half of Q008 - whether missions grant or script
radar coverage - stays open with the Game Designer inside its decide-by
date, recorded on Q008 itself. Note for that decision: mission-02 now
carries a camp plant for the TURRET gate, which is unrelated to the
player's radar; all three missions still play radarless for the human.

**The regeneration, explained row by row.** One regeneration for the
wave. Thirteen of 24 rows moved; eleven are byte-identical, production
and depot among them exactly as clause 7 demands. The neutralisation
proof (B2's technique): with the gate line, the three compiled draws and
the AI radar rung temporarily reverted on a scratch build, EIGHTEEN rows
reverted byte-exactly to the pre-wave goldens (movement, pathing,
economy, combat, production, attackmove, skirmish, veterancy, victory,
expansion, artillery, crush, aisuper, waypoints, mission, capture,
mission03, depot), proving the wave's behavioural surface is their only
mover; the six still moved are exactly the six amended-world scenarios,
and three of those (stealth, veil, mission02) hash IDENTICALLY in
scratch and final, proving their powering plant is their entire
movement and that the gate never bites a powered turret. Mechanisms in
the final diff: economy and victory move on the hashed PowerDraw
literal alone (no system reads the changed draws in either world);
skirmish, expansion, aisuper, mission and mission03 move behaviourally
(the AI banks for and raises a radar - aisuper's superweapon lands at
tick 1051 against 766 before - and the supply-ahead rung fires earlier
under the honest draws); superweapon moves by the third plant plus the
draw literals with the boundary intact at 170 against 170; construction
by its plant, the draws and phase I; walls by phase G's plant plus
phase J's whole sub-world. The walls row moving at all is the amendment
working: unamended, it would not have moved and phase G would have
rotted silently. Nothing moved unexplained; nothing explained failed to
move. No turretless, power-rich scenario moved, so the stop condition
never fired.

### Verification evidence

Full battery exit 0 (selftest with both catalogue round-trips,
determinism 24/24 double-run identical, match with every scenario
assertion including walls A-J and construction A-I, defence load 1.54
ms/tick against the 8 ms budget with the gate's guard in the hot loop,
catrefuse, spawngate, lan 5/5). Green standalone: saveload, replay,
campaignsave, spectate, lanchaos, and the balance gate (PASS verdict
unchanged). Purity and portability greps clean. Goldens byte-identical
across two fresh runs, LF verified. Both client builds (Debug and
ExportRelease) at zero warnings; project.godot byte-identical at
commit.

Offscreen client run: a temporary autoload (removed before commit, the
established pattern) drove the REAL menu start path
(StartSkirmishForTest) into a live skirmish and verified 13/13 checks
through the shipped verification surface, engine exit 0, screenshots
captured: the minimap DARK at the radarless open with no alert (the
edge starts false); still dark with 200 supply and no uplink; a Radar
Uplink queued through the real sidebar path, placed, and the minimap
LIVE with no alert on gaining it; both plants sold and the minimap
blacked out with the RADAR OFFLINE toast, the cue, the gold ping and
the alert counter at exactly one; a rebuilt plant relighting it; a
turret placed and verified clean while powered, then the last plant
sold and the turret wearing the offline wash with the readout reading
"TURRET 400/400 OFFLINE - LOW POWER R repair X sell" and the second
dark crossing alerting too. The harness recording was deleted; the
user's 50 pre-existing replays and both save slots are untouched
(listings diffed before and after).

## Changed / Assumed / Needed next

**Changed:** sim/Ferrostorm.Sim (World.cs: the turret gate, the three
draws, SpawnRadarUplink, PlaceStructure arm, IsStructure, comment
truth; MapLoader.cs: the RadarUplink arm; SkirmishAI.cs: hasRadar and
the radar rung), sim/Ferrostorm.Sim.Runner/Program.cs (stealth, veil,
superweapon, construction surgery; walls phase G amendment and phase J;
construction phase I; aisuper radar assertion; StDebug mirror),
data/buildings (three draws plus three notes; com_radar_uplink note),
data/missions/mission-02.fmap (the camp plant), game/scripts
(Sidebar.cs entry; ModelLibrary.cs interim mapping; Minimap.cs blackout
render and input swallow; SkirmishLive.cs radar gate, alert, turret
wash, readout, StructNames, verification surface), art/audio (synth.py
alert_radar; make_vo.sh vo_radar_offline) plus the two generated wavs,
sim/golden-hashes.txt (the wave's ONE regeneration, every row explained
above), docs (ADR-008 dated notes, Q008 note, ALERT-02 closure note,
tracker, ledger, TICKET-P6-ART-01, this file).

**Assumed:** the AI's radar affordability threshold stays BD-09's 1500
credits (small-decision call; one constant). The radar button sits
between SERVICE DEPOT and VEIL PROJECTOR in rough tech order. Enemy
turrets dim too, stated in the plan and reversible in one predicate.
Clicks on a dark minimap are swallowed rather than passed through. The
blackout binds only the human, exactly as ADR-008 records.

**Needed next (from whom):** the Game Designer answers Q008's curation
half (missions granting or scripting radar) inside the decide-by date;
art-pipeline owes the bespoke com_radar_uplink model and sidebar icon
(TICKET-P6-ART-01); the Game Designer still owns Q010 (the invented
full-power cliffs, now sharpened by the honest draws) and BAL-01/02
stand unchanged; ADR-009's wave (B4) inherits IsProducer, the
prerequisite enforcement that makes the radar's carried Prereqs real,
and the tabbed sidebar.
