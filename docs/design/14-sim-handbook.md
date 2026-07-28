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

`World.Step(commands)` runs at 15 Hz, in THIRTEEN systems:

1. ApplyCommands - incoming commands (queue-aware; direct orders wipe plans)
2. OrderDispatch - pop next shift-queued order for idle entities (sorted ids)
3. **Stance** - hold-fire, guard leash, patrol legs (ADR-015)
4. Movement - direct steps and flow-field pathing, arrival rules
5. Separation - crowd pushing, arrival contagion, crush
6. Detection - reveal decay, veil projector cloaking, detector painting, elite regen
7. **Capture** - the engineer taking a structure or a neutral outpost (ADR-021)
8. Combat - targeting, min range, damage matrix, splash, deaths, veterancy
9. Harvest - the Idle/ToField/Loading/ToRefinery/Unloading state machine
10. **Regrowth** - ferrite fields recovering toward their cap (ADR-012)
11. Production - factories/yards, build lanes, repair, superweapon, outpost income
12. Fog - per-player visible/explored bitsets
13. Victory - short-game rule (no structures and no MCV = eliminated)

Reordering these systems is a determinism-schema change: goldens regenerate
and a design note is required.

**The authority on this order is `World.Step` itself, not this list.** Three
systems (Stance, Capture, Regrowth) were added to the sim without being added
here, and a list that claims to be contractual while silently omitting a third
of it is worse than no list. Check the method before relying on this.

## Commands (the only way players touch the world)

**The numbers below are the wire format**: they are serialised into replays and
into every lockstep batch, so they are not free to renumber. Read them from
`CommandType` in `World.cs` if there is any doubt - this table was wrong in
seven places for several waves.

Move(2) Stop(3) PathMove(4) Harvest(5) Attack(6) Produce(7) AttackMove(8)
CancelProduce(9) PlaceStructure(10) SellStructure(11) BuildStructure(12)
Repair(13) Deploy(14) LaunchSuper(15) **SetRally(16)** **SetStance(17)**.
(0 is None; there is no command 1.) Every command carries an
optional Queued flag (shift-queue): queued orders append to the entity's
plan; a direct order wipes it. Movement orders clear standing attack orders
(order priority). Deploy is MCV-only and founds a Construction Yard with no
adjacency requirement. LaunchSuper is refused until charged.

## Catalogue (compiled defaults; /data YAML must match, selftest enforces)

**/data is the runtime source since ADR-006, so the compiled table below is the
FALLBACK and the authored YAML is what a match actually charges.** Read
`data/units/` and `data/buildings/` for live numbers; selftest enforces that the
two agree.

Units (13): 1 dir_cannon_tank 600cr/300hp/Heavy, 2 com_rifle_squad 200/100/None,
3 com_rocket_squad 300/80/None, 4 com_harvester 1400/700/Heavy (no vet),
5 sod_shade_raider 500/150/Light (stealth), 6 dir_sentinel_scout 400/90
(detector, sight 7), 7 com_mcv 3000/600/Heavy (no vet, deploys),
8 dir_howitzer 900/160/Light (range 9, dead zone 3, splash 1.5),
9 sod_phantom_tank 900/200/Light (stealth), 10 dir_bulwark_tank 1600/550/Heavy,
11 com_engineer 500/60/None (captures on contact, consumed by the act),
12 dir_vanguard_car 450/150/Light (sight 6), 13 com_repair_vehicle 700/300/Light
(no vet, unarmed, mobile heal aura; ADR-019).

Structures (type ids to 14): 1 plant 300 (+100 power), 2 factory 2000 (draw 40),
3 refinery 2000, 4 construction_yard 3000 (MCV-only), 5 turret 600 (draw 20),
6 superweapon 4000 (draw 100), 7 veil_projector 1500 (draw 60, cloak r6),
8 service_depot, 9 wall segment (ADR-005; **10 is RESERVED for the deferred
gate**), 11 barracks (ADR-009), 12 radar_uplink (ADR-008), 13 outpost
(map-placed, neutral, capturable, pays 15 cr/s; ADR-021), 14 bridge
(map-placed, neutral, felling it BLOCKS the cell; ADR-025).

Weapons: 1 TankCannon r4/30/AA/15t, 2 ServiceRifle r3/12/AI/8t,
3 RocketTube r4/40/AA/20t, 4 TurretGun r5/35/AA/12t,
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
  at 2 hp and 1 credit per tick - no power or no credits, no mending. The
  repair VEHICLE (unit 13, ADR-019) runs the identical loop as a mobile
  aura, and is deliberately NOT power-gated. The two stack, which is the
  widened premise of the open Q009.

## File formats (all versioned, all fail loudly)

- Units: `data/units/*.yaml` - strict subset parser in DataLoader.
- Maps: `ferrostorm-map v2` - size, starts, character grid (`.` `#` `F`,
  plus `b` for a destroyable bridge deck, ADR-025),
  then optional `unit`/`structure` lines (with tags) and
  `trigger WHEN... -> DO...` lines. Conditions: elapsed/destroyed/credits/
  entered, owned TAG P (capture objectives). Actions: grant/spawn/win/
  message/assault P CX CY (every unit of P attack-moves - issued as
  commands via the Tick output list; the mission is a player, not a god).
  MissionRunner fires each once. Map headers: faction P F, rules
  noshortgame. The campaign manifest and briefings live in data/campaign/.
- Replays: `ferrostorm-replay v2` - seed, setup, per-tick command lines
  including the queued flag.
- Saves: binary `SLA8...SDNE` (v7 added unit stances per ADR-015, v8 the
  second build-lane block per ADR-023; every earlier magic still loads) - every field of world state; the saveload
  gate proves load-hash equality AND that a resumed run reaches the
  uninterrupted final hash (protects even unhashed fields).

## Runner modes and gates

`dotnet run --project sim/Ferrostorm.Sim.Runner -c Release -- <mode>`:
selftest, determinism [seed], golden [seed], match [seed] (24 scenarios +
hard 8 ms/tick perf gate), lan N, lanchaos N delay jitter, spectate, replay,
saveload, campaignsave, bench, plus *debug diagnostics. Feature gates added
since: catrefuse, spawngate, prodgate, regrowthgate, stancegate, repairgate,
outpostgate, lanegate, bridgegate, mapgate, lanpoll, lansetup. **The header of
`Program.cs` is the authoritative list**; this one has lagged before.

The balance tool (`tools/Ferrostorm.Balance`) is a 4x4 matchup matrix plus a
tempo baseline with hard expectations.

CI (.github/workflows/determinism.yml) is THREE jobs and any of them red blocks
the merge: `banned-tokens` (purity grep, ADR-004 portability grep, the
hardcoded-seat guard and the team-colour guard), `determinism` on Windows AND
Linux (selftest, double-run determinism, golden diff, match, lan 5, lanchaos,
spectate, replay, saveload, campaignsave, balance), and `client-harness` on
Linux, which installs the Godot mono editor and drives the REAL battle scene
headless through `tools/verify-client.sh`. nightly-soak.yml runs 5 seeds x 2 OS
plus lan 20.

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
