# Ferrostorm Sim Handbook

Reference manual for `sim/Ferrostorm.Sim` - the deterministic core every other
part of the game sits on. Written for a future contributor (human or agent)
who needs the ground truth without reading eleven sessions of history.

## The one rule

The simulation is bit-deterministic. Same seed, same commands, same final
state hash on any machine, any OS, any latency. Everything else in this
document exists in service of that rule. The library has zero NuGet
dependencies, no float/double, no System.Random, no Godot types (CI greps
for all four), and all arithmetic is Fix64 (Q32.32 fixed point) or integer.

## Tick anatomy (system order is contractual)

`World.Step(commands)` runs at 15 Hz:

1. ApplyCommands - incoming commands (queue-aware; direct orders wipe plans)
2. OrderDispatch - pop next shift-queued order for idle entities (sorted ids)
3. Movement - direct steps and flow-field pathing, arrival rules
4. Separation - crowd pushing, arrival contagion, crush
5. Detection - reveal decay, veil projector cloaking, detector painting, elite regen
6. Combat - targeting, min range, damage matrix, splash, deaths, veterancy
7. Harvest - the Idle/ToField/Loading/ToRefinery/Unloading state machine
8. Production - factories/yards, repair, superweapon charge and strikes
9. Fog - per-player visible/explored bitsets
10. Victory - short-game rule (no structures and no MCV = eliminated)

Reordering these systems is a determinism-schema change: goldens regenerate
and a design note is required.

## Commands (the only way players touch the world)

Move(1) PathMove(2) Stop(3) Attack(4) Harvest(5) Produce(6) CancelProduce(7)
AttackMove(8) BuildStructure(9) PlaceStructure(10) SellStructure(11)
Rally(12) Repair(13) Deploy(14) LaunchSuper(15). Every command carries an
optional Queued flag (shift-queue): queued orders append to the entity's
plan; a direct order wipes it. Movement orders clear standing attack orders
(order priority). Deploy is MCV-only and founds a Construction Yard with no
adjacency requirement. LaunchSuper is refused until charged.

## Catalogue (compiled defaults; /data YAML must match, selftest enforces)

Units: 1 cannon_tank 600cr/300hp/Heavy, 2 rifle_squad 200/100/None,
3 sod_rocket_squad 300/80/None, 4 com_harvester 1400/700/Heavy (no vet),
5 sod_shade_raider 500/150/Light (stealth), 6 dir_sentinel_scout 400/90
(detector, sight 7), 7 com_mcv 3000/600/Heavy (no vet, deploys),
8 dir_howitzer 900/160/Light (range 9, dead zone 3, splash 1.5).

Structures: 1 plant 300 (+100 power), 2 factory 2000 (draw 40),
3 refinery 2000, 4 construction_yard 3000 (MCV-only), 5 turret 600 (draw 20),
6 superweapon 4000 (draw 100), 7 veil_projector 1500 (draw 60, cloak r6).

Weapons: 1 TestCannon r4/30/AA/15t, 2 TestRifle r3/12/AI/8t,
3 TestRocket r4/40/AA/20t, 4 TurretGun r5/35/AA/12t,
5 Howitzer r9/60/AB/45t min3 splash1.5 (splash is half damage, friend or
foe, shooter spared).

## Factions (TICKET-P3-FAC)

Players carry a hashed faction (0 Directorate, 1 Sodality; map header
`faction P F`). Faction-locked hardware is refused at the factory and yard.
Design law: the counter-triangle (rifles, rockets) is COMMON; identity
lives in the specials. Engineers (common) capture enemy structures on
contact via a plain Attack order - the engineer is consumed, queues clear,
a Captured event fires. AI doctrine is faction-aware: garrison squads
(waveSize/2 lowest-id fighters) answer all threats including harvester
distress while the field army ignores bait; Sodality phantoms wage the
shadow war on enemy harvesting; Directorate sentinels escort harvesters
and waves strike the enemy refinery first.

## Mechanics quick truths

- Economy: harvesters carry 700, load 10/tick, unload over 120 ticks;
  auto-retarget when fields die; flee only while Loading under fire.
- Construction: build-then-place from the yard sidebar; strict Q2 adjacency
  (radius 5, CY radius 7); pay-as-you-build with exact cancel refunds;
  sell-back 50%; destroyed structures unblock to rubble.
- Power: supply < draw halts production, superweapon charge, and veils.
- Stealth: per-player DetectedMask; firing reveals for 45 ticks; detectors
  and the veil interact exactly as classic (detector strips both).
- Veterancy: 3/6 kills; damage x5/4 and x6/4; elites regen 1hp/15t; crush
  kills credit the driver; superweapons credit nobody.
- Crush: heavy vehicles flatten enemy None-armour infantry at deep contact;
  crush-eligible pairs exert no separation push ("treads do not yield").
- Victory: short-game rule, or a mission trigger's scripted `win`. The
  short-game rule is a SKIRMISH rule: maps declare `rules noshortgame` for
  commando and defence missions (a baseless strike force is not a defeated
  player); the flag is hashed and saved.
- Service depot (structure 8): powered depots mend own units in radius 4
  at 2 hp and 1 credit per tick - no power or no credits, no mending.

## File formats (all versioned, all fail loudly)

- Units: `data/units/*.yaml` - strict subset parser in DataLoader.
- Maps: `ferrostorm-map v2` - size, starts, character grid (`.` `#` `F`),
  then optional `unit`/`structure` lines (with tags) and
  `trigger WHEN... -> DO...` lines. Conditions: elapsed/destroyed/credits/
  entered, owned TAG P (capture objectives). Actions: grant/spawn/win/
  message/assault P CX CY (every unit of P attack-moves - issued as
  commands via the Tick output list; the mission is a player, not a god).
  MissionRunner fires each once. Map headers: faction P F, rules
  noshortgame. The campaign manifest and briefings live in data/campaign/.
- Replays: `ferrostorm-replay v2` - seed, setup, per-tick command lines
  including the queued flag.
- Saves: binary `SLA6...SDNE` - every field of world state; the saveload
  gate proves load-hash equality AND that a resumed run reaches the
  uninterrupted final hash (protects even unhashed fields).

## Runner modes and gates

`dotnet run --project sim/Ferrostorm.Sim.Runner -c Release -- <mode>`:
selftest, determinism [seed], golden [seed], match [seed] (24 scenarios +
hard 8 ms/tick perf gate), lan N, lanchaos N delay jitter, spectate, replay,
saveload, bench, plus *debug diagnostics. The balance tool
(`tools/Ferrostorm.Balance`) is a 4x4 matchup matrix plus a tempo baseline
with hard expectations. CI (determinism.yml) runs purity grep, double-run
determinism, golden diff, match, lan 5, lanchaos, spectate, replay, and
saveload on every push; nightly-soak.yml runs 5 seeds x 2 OS + lan 20.

## Golden hashes

`sim/golden-hashes.txt` pins 24 scenario finals. During pre-production a
schema change regenerates them freely (three-line header explains the
policy); from first public build, changing one is a replay-compatibility
break requiring an ADR.

## What is deliberately NOT in the sim

Rendering, sound, input, camera (Godot's job, fed by snapshots + the
GameEvent stream); AI decision state (SkirmishAI is a sim-adjacent driver
using only public commands); mission trigger state (MissionRunner likewise -
campaign save is world save + mission state, TICKET-P2-SIM-21).
