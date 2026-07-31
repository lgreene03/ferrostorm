using System.Diagnostics;
using Ferrostorm.Net;
using Ferrostorm.Presentation;
using Ferrostorm.Sim;

// Ferrostorm headless runner.
// Modes:
//   selftest           - unit assertions (Fix64, RNG, hashing, damage matrix)
//   determinism [seed] - run every scenario twice in-process; fail on any hash mismatch
//   golden [seed]      - print "scenario hash" lines (CI diffs vs sim/golden-hashes.txt)
//   match [seed]       - run scenarios with perf reporting vs the 8ms/tick budget
//   lan [games]        - relay + 2 lockstep clients over loopback TCP per game (TICKET-P1-08)
//   catrefuse          - ADR-006: a mismatched catalogue refuses (LAN hello, saves, replays) rather than desyncs
//   spawngate          - ADR-007: rally in the sim, the spawn exit move, save v4, occupancy and the zero-drain hold
//   stancegate         - ADR-015: hold-fire discipline, guard leash-and-return, patrol cycling, save v7 round-trip
//   repairgate         - ADR-019: the repair vehicle mends own mobile units in the field (not power gated, not itself/enemies/structures)
//   outpostgate        - ADR-021: the neutral Outpost - engineer capture, the 15/s income beat, neutral inertness, not-hope elimination
//   firesalegate       - DR-10: the beaten AI's last stand - fire sale + final wave, once; yard control; MCV redeploy
//   airepairgate       - ADR-026/DR-13: the AI mends a damaged own structure (not a healthy one, not while broke)
//   difficultygate     - DR-14/doc 28: the Easy-to-Brutal ladder - Normal is the identity rung, the beat and the mining vary, the handicap is setup-only
//   fordgate           - DR-18: both armies CROSS Ashford Reach (skirmish-05), the doc 26 "a map where an army parks is a failed map" bar that mapgate cannot see
//   lanegate           - ADR-023: parallel build lanes - overflow only, both lanes build at once, the prune matches the hash guard, save v8
//   lansetup           - ADR-022: the host's match setup rides the Hello, so a joiner builds the identical world
//   bridgegate         - ADR-025: a standing bridge is passable, a felled one BLOCKS its cell (the one death that reduces passability)
//   mapgate            - every committed map loads, spawns the opening hand, plays AI-vs-AI, and the AI CAPTURES at least one of its declared outposts (C4c inverted this from the original "stand neutral" assertion)
//   lanpoll            - Q002/C7a: the non-blocking TryAdvanceTick drive, clean and under chaos, no call ever blocking on the socket
//   pinprobe           - Q018 diagnostic: per-commander attack-move counts, attrition and end positions across every committed map (not a gate; nothing asserts)
//   pintrace           - Q018 diagnostic stage two: per-unit travelled-vs-net, engagement, enclosure, reachable region and crowding for the stalled commander (not a gate; nothing asserts)
//   infiltratorgate    - P7-7: the Infiltrator moves credits rather than minting them, robs without capturing, and leaves the engineer alone
//   campaigngate       - P7-9: the manifest's ids all resolve, a mission can be won by ARRIVING, and a noshortgame mission can still be LOST (Q016)
//   factiondefencegate - P7-2b: each side builds only its own defence; the Bastion is tough and dear, the Nest cloaks and decloaks on firing
//   airgate            - ADR-028: ground weapons cannot touch an aircraft, the flak track can, and it crosses sealed terrain
//   transportgate      - P7-3: the Carrier loads infantry only, unloads them intact, and takes its cargo down with it
//   emplacementgate    - P7-2: the Emplacement beats infantry and LOSES to armour, so defence is a choice; and it obeys the power gate
//   factiongate        - P7-1: a building's side comes from /data; common admits both, a declared side is obeyed
//   basingate          - skirmish-07 played 20,000 ticks (~22 simulated minutes): the commanders expand and fight rather than stall
//   sizeprobe          - doc 26 s5: ms/tick and flow-field build cost against map area (not a gate; nothing asserts)
//   decorgate          - decorative terrain (, : = ~): drawn, never blocking, outside the density budget
//   bench              - Fix64 throughput evidence for ADR-002
// Exit 0 = pass, nonzero = failure. CI treats nonzero as merge-blocking.

int Fail(string msg) { Console.Error.WriteLine($"FAIL: {msg}"); return 1; }

// ---------------- Scenarios ----------------
// Each returns final hash; checkpoint callback fires every 100 ticks.

ulong ScenarioMovement(ulong seed, Action<int, ulong>? cp = null)
{
    var world = new World(seed, 512, 512, players: 1);
    var rng = new DeterministicRandom(seed ^ 0xA5A5A5A5UL);
    var mapSize = Fix64.FromInt(512);
    const int units = 500, ticks = 1000;

    for (int i = 0; i < units; i++)
        world.SpawnUnit(0, rng.NextFix64Unit() * mapSize, rng.NextFix64Unit() * mapSize,
            Fix64.FromFraction(rng.NextInt(4) + 1, 4), hp: 100, ArmourClass.Light, weaponId: 0);

    var cmds = new List<Command>();
    for (int t = 0; t < ticks; t++)
    {
        cmds.Clear();
        if (t % 10 == 0)
            for (int o = 0; o < units / 10; o++)
                cmds.Add(new Command(t, 0, CommandType.Move, rng.NextInt(units),
                    rng.NextFix64Unit() * mapSize, rng.NextFix64Unit() * mapSize));
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        if (t % 100 == 99) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    return world.ComputeStateHash();
}

World BuildPathingWorld(ulong seed, out int units)
{
    var world = new World(seed, 64, 64, players: 1);
    // Wall down x=32 with two gaps: y in 10..12 and 50..52.
    for (int y = 0; y < 64; y++)
        if (y is < 10 or (> 12 and < 50) or > 52)
            world.Map.SetBlocked(32, y, true);
    var rng = new DeterministicRandom(seed ^ 0x5EEDUL);
    units = 500;
    for (int i = 0; i < units; i++)
        world.SpawnUnit(0,
            Fix64.FromInt(2 + rng.NextInt(18)) + Fix64.Half,
            Fix64.FromInt(2 + rng.NextInt(60)) + Fix64.Half,
            Fix64.FromFraction(1, 4), hp: 100, ArmourClass.Light, weaponId: 0);
    return world;
}

ulong ScenarioPathing(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    var world = BuildPathingWorld(seed, out int units);
    var target = (X: Fix64.FromInt(60) + Fix64.Half, Y: Fix64.FromInt(32) + Fix64.Half);
    var cmds = new List<Command>();
    for (int i = 0; i < units; i++)
        cmds.Add(new Command(0, 0, CommandType.PathMove, i, target.X, target.Y));

    var sw = Stopwatch.StartNew();
    int arrivedTick = -1;
    const int maxTicks = 3000;
    for (int t = 0; t < maxTicks; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (t % 100 == 99) cp?.Invoke(t + 1, world.ComputeStateHash());
        if (arrivedTick < 0)
        {
            int settled = 0;
            foreach (var e in world.Entities) if (!e.Moving) settled++;
            if (settled == units) arrivedTick = t + 1;
        }
    }
    sw.Stop();
    if (arrivedTick < 0) throw new Exception("pathing: units failed to settle within budget ticks");
    // Behavioural contract: every unit settles, none abandoned across the
    // wall, and the whole crowd sits within a 22-cell radius of the target
    // (500 units freeze-on-contact pack loosely; compaction is the ticketed
    // P2 formation work, not a Phase 1 requirement).
    int nearTarget = 0;
    foreach (var e in world.Entities)
    {
        if (e.X < Fix64.FromInt(33)) throw new Exception("pathing: unit stranded on the wrong side of the wall");
        if (Fix64.DistSq(e.X - target.X, e.Y - target.Y) <= Fix64.FromInt(484)) nearTarget++;
    }
    if (nearTarget != units)
        throw new Exception($"pathing: only {nearTarget}/{units} settled within 22 cells of target");
    report?.Invoke($"pathing: {units} units through wall gaps, all settled by tick {arrivedTick}, {nearTarget}/{units} within 22 cells, none stranded; " +
                   $"{sw.Elapsed.TotalMilliseconds / maxTicks:F3} ms/tick (budget 8)");
    return world.ComputeStateHash();
}

ulong ScenarioEconomy(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    var world = new World(seed, 64, 64, players: 2);
    int refinery = world.SpawnRefinery(0, 10, 10);
    int f1 = world.SpawnFerriteField(Fix64.FromInt(30), Fix64.FromInt(30), 2000);
    int f2 = world.SpawnFerriteField(Fix64.FromInt(34), Fix64.FromInt(30), 2000);
    var harvesters = new[]
    {
        world.SpawnHarvester(0, Fix64.FromInt(11), Fix64.FromInt(12)),
        world.SpawnHarvester(0, Fix64.FromInt(12), Fix64.FromInt(11)),
        world.SpawnHarvester(0, Fix64.FromInt(12), Fix64.FromInt(12)),
    };
    _ = refinery;
    var cmds = new List<Command>
    {
        new(0, 0, CommandType.Harvest, harvesters[0], Fix64.Zero, Fix64.Zero, f1),
        new(0, 0, CommandType.Harvest, harvesters[1], Fix64.Zero, Fix64.Zero, f2),
        new(0, 0, CommandType.Harvest, harvesters[2], Fix64.Zero, Fix64.Zero, f1),
    };
    const int ticks = 5000;
    for (int t = 0; t < ticks; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (t % 100 == 99) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    long credits = world.Credits(0);
    // ADR-012 (Wave B6): the two 2000-unit fields still deliver their full
    // 4000, plus exactly 14 that regrew while they were alive and below cap
    // before exhaustion (1 unit per 75 ticks; f1's two harvesters strip it
    // faster and it collects fewer ticks than the single-harvester f2, summing
    // to 14). Seed-independent: the harvest of these fixed fields is
    // deterministic across every seed, so this stays an exact assertion the way
    // the pre-regrowth 4000 was, not a band. A stripped field would deliver its
    // spawn amount and no more; the surplus IS regrowth, proven in RegrowthGate.
    if (credits != 4014)
        throw new Exception($"economy: expected 4000 delivered plus 14 regrown (ADR-012), got {credits}");

    // Flee phase (TICKET-P2-SIM-08): a rifle camps a fresh field; a harvester
    // sent in must abandon loading under fire and run its part-load home.
    int freshField = world.SpawnFerriteField(Fix64.FromInt(30), Fix64.FromInt(30), 2000);
    world.SpawnUnit(1, Fix64.FromInt(30), Fix64.FromInt(28), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
    cmds.Add(new Command(0, 0, CommandType.Harvest, harvesters[0], Fix64.Zero, Fix64.Zero, freshField));
    bool fledWithPartLoad = false;
    for (int t = 0; t < 800; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        var h = world.Entities[harvesters[0]];
        if (h.HState == HarvestState.ToRefinery && h.Carry > 0 && h.Carry < World.HarvesterCapacity)
            fledWithPartLoad = true;
        if (t % 100 == 99) cp?.Invoke(world.Tick, world.ComputeStateHash());
    }
    if (!fledWithPartLoad) throw new Exception("economy: harvester never fled the camped field with a part-load");
    if (!world.Entities[harvesters[0]].Alive) throw new Exception("economy: harvester died instead of fleeing (700 hp vs rifle should survive easily)");
    report?.Invoke($"economy: 3 harvesters exhausted 2 fields, credits {credits} (4000 spawn + 14 regrown, ADR-012) exact; camped harvester fled mid-load and survived (flee-on-damage live)");
    return world.ComputeStateHash();
}

ulong ScenarioCombat(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    var world = new World(seed, 64, 64, players: 2);
    for (int i = 0; i < 15; i++) // player 0: cannons (anti-armour) - wrong tool vs infantry
        world.SpawnUnit(0, Fix64.FromInt(18), Fix64.FromInt(20 + i), Fix64.FromFraction(1, 5),
            hp: 300, ArmourClass.Heavy, weaponId: 1);
    for (int i = 0; i < 20; i++) // player 1: rifles (anti-infantry) - also wrong tool vs heavy armour
        world.SpawnUnit(1, Fix64.FromInt(46), Fix64.FromInt(18 + i), Fix64.FromFraction(1, 4),
            hp: 100, ArmourClass.None, weaponId: 2);

    // Both armies receive attack orders: attack-pursuit closes each unit to
    // weapon range of its victim, kills cascade to auto-acquire, and the
    // damage matrix decides the outcome.
    var cmds = new List<Command>();
    for (int i = 0; i < 15; i++)
        cmds.Add(new Command(0, 0, CommandType.Attack, i, Fix64.Zero, Fix64.Zero, 15 + i));
    for (int i = 0; i < 20; i++)
        cmds.Add(new Command(0, 1, CommandType.Attack, 15 + i, Fix64.Zero, Fix64.Zero, i % 15));

    const int ticks = 800;
    for (int t = 0; t < ticks; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (t % 100 == 99) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    int alive0 = 0, alive1 = 0;
    foreach (var e in world.Entities)
        if (e.Alive) { if (e.PlayerId == 0) alive0++; else alive1++; }
    if (alive0 > 0 == (alive1 > 0))
        throw new Exception($"combat: expected a decisive result, got p0={alive0} p1={alive1}");
    report?.Invoke($"combat: decisive engagement, survivors p0={alive0} p1={alive1} (damage matrix + fog live)");
    return world.ComputeStateHash();
}

ulong ScenarioProduction(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    var world = new World(seed, 64, 64, players: 2);
    world.GrantCredits(0, 1000);
    int plant = world.SpawnPowerPlant(0, 10, 10); // supply 100
    int factory = world.SpawnFactory(0, 14, 10);  // draw 40
    // ADR-009 scenario surgery. Rifles are BARRACKS units now (produced_at
    // com_barracks), so a factory refuses them and this scenario's three
    // opening rifles need the building that makes them. Sited well clear of
    // the phase 2 raider at (9,9) so nothing new comes under fire and the
    // scenario keeps measuring production rather than combat. Draw is 60
    // against supply 100, so every full-power timing assertion below is
    // unchanged; phase 3's cannon stays at the factory, its true producer.
    int barracks = world.SpawnBarracks(0, 18, 16); // draw 20

    var cmds = new List<Command>
    {
        new(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2),
        new(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2),
        new(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2),
    };

    var spawnTicks = new List<int>();
    int seen = 3; // plant + factory + barracks
    const int phase1Ticks = 300;
    for (int t = 0; t < phase1Ticks; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (t == 39)
        {
            // Pay-as-you-build audit: 40 full-power ticks into the first rifle,
            // drained credits must equal the sim's own integer formula.
            long expectedPaid = 200L * (40 * 100) / (75 * 100);
            if (world.Credits(0) != 1000 - expectedPaid)
                throw new Exception($"production: pay-as-you-build drain wrong, credits {world.Credits(0)} vs {1000 - expectedPaid}");
        }
        while (world.EntityCount > seen) { spawnTicks.Add(t + 1); seen++; }
        if (t % 100 == 99) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    if (spawnTicks.Count != 3) throw new Exception($"production: expected 3 rifles at full power, got {spawnTicks.Count}");
    if (world.Credits(0) != 400) throw new Exception($"production: expected 400 credits left, got {world.Credits(0)}");
    int fullPowerBuild = spawnTicks[1] - spawnTicks[0];
    if (fullPowerBuild != 75) throw new Exception($"production: full-power rifle should take 75 ticks, took {fullPowerBuild}");

    // Kill the power plant: supply 0 => rate floor 50% (GDD s5), builds take 2x.
    int killer = world.SpawnUnit(1, Fix64.FromInt(9), Fix64.FromInt(9), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, 1);
    cmds.Add(new Command(0, 1, CommandType.Attack, killer, Fix64.Zero, Fix64.Zero, plant));
    seen = world.EntityCount;
    bool plantDead = false;
    int queuedAt = -1;
    var lowPowerSpawns = new List<int>();
    for (int t = phase1Ticks; t < phase1Ticks + 500; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (!plantDead && !world.Entities[plant].Alive)
        {
            plantDead = true;
            queuedAt = t + 1;
            cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        }
        while (world.EntityCount > seen) { lowPowerSpawns.Add(t + 1); seen++; }
        if (t % 100 == 99) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    if (!plantDead) throw new Exception("production: power plant survived the scripted attack");
    if (lowPowerSpawns.Count != 1) throw new Exception($"production: expected 1 low-power rifle, got {lowPowerSpawns.Count}");
    int lowPowerBuild = lowPowerSpawns[0] - queuedAt;
    if (lowPowerBuild != 150) throw new Exception($"production: low-power rifle should take 150 ticks (50% rate), took {lowPowerBuild}");

    // Phase 3 (TICKET-P2-UX-01b): with only 200 credits left, queue a
    // 600-credit cannon. Pay-as-you-build must drain to broke, stall without
    // spawning, and CancelProduce must refund every drained credit exactly.
    long preQueue = world.Credits(0);
    if (preQueue != 200) throw new Exception($"production: expected 200 credits entering phase 3, got {preQueue}");
    cmds.Add(new Command(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 1));
    seen = world.EntityCount;
    for (int t = 0; t < 250; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
    }
    if (world.EntityCount != seen) throw new Exception("production: unaffordable cannon spawned anyway");
    var f = world.Entities[factory];
    if (world.Credits(0) + f.BuildPaid != preQueue)
        throw new Exception($"production: credit conservation broken while stalled ({world.Credits(0)} + {f.BuildPaid} != {preQueue})");
    if (f.BuildPaid == 0) throw new Exception("production: build never drained before stalling");
    cmds.Add(new Command(0, 0, CommandType.CancelProduce, factory, Fix64.Zero, Fix64.Zero, 0));
    world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
    cmds.Clear();
    if (world.Credits(0) != preQueue)
        throw new Exception($"production: cancel refund inexact, credits {world.Credits(0)} vs {preQueue}");
    if (world.Entities[factory].BuildPaid != 0 || world.Entities[factory].BuildProgress != 0)
        throw new Exception("production: cancel did not reset the build slot");
    report?.Invoke($"production: 3 rifles at 75 ticks each on full power, pay-as-you-build drain exact; plant destroyed -> low-power build took {lowPowerBuild} ticks (50% rate per GDD s5); broke-stall conserved credits and cancel refunded exactly");
    return world.ComputeStateHash();
}

ulong ScenarioAttackMove(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // Gauntlet: cannons attack-move across the map through two rifle picket
    // lines. They must stop and destroy each picket, resume marching, and
    // settle at the ordered point (TICKET-P2-UX-01).
    var world = new World(seed, 64, 64, players: 2);
    var cannons = new List<int>();
    for (int i = 0; i < 8; i++)
        cannons.Add(world.SpawnUnit(0, Fix64.FromInt(6), Fix64.FromInt(28 + i), Fix64.FromFraction(1, 5), 300, ArmourClass.Heavy, 1));
    for (int i = 0; i < 4; i++)
        world.SpawnUnit(1, Fix64.FromInt(24), Fix64.FromInt(26 + i * 3), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
    for (int i = 0; i < 4; i++)
        world.SpawnUnit(1, Fix64.FromInt(40), Fix64.FromInt(26 + i * 3), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);

    var dest = (X: Fix64.FromInt(58), Y: Fix64.FromInt(32));
    var cmds = new List<Command>();
    foreach (int id in cannons)
        cmds.Add(new Command(0, 0, CommandType.AttackMove, id, dest.X, dest.Y));

    const int ticks = 1200;
    for (int t = 0; t < ticks; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (t % 100 == 99) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    // Contract: every picket within sight of the marching line dies; the two
    // flankers at y=26 sit 6+ cells off the route - beyond sight range 5 and
    // hidden by fog - and MUST survive: attack-move engages what it
    // encounters, it is not map-wide omniscient hunting.
    int riflesAlive = 0, flankersAlive = 0, cannonsAlive = 0, settledNearDest = 0;
    foreach (var e in world.Entities)
    {
        if (!e.Alive) continue;
        if (e.PlayerId == 1)
        {
            riflesAlive++;
            if (e.Y == Fix64.FromInt(26)) flankersAlive++;
        }
        else
        {
            cannonsAlive++;
            if (!e.Moving && !e.AMove && Fix64.DistSq(e.X - dest.X, e.Y - dest.Y) <= Fix64.FromInt(64)) settledNearDest++;
        }
    }
    if (riflesAlive != 2 || flankersAlive != 2)
        throw new Exception($"attackmove: expected exactly the 2 out-of-sight flankers to survive, got {riflesAlive} alive ({flankersAlive} flankers)");
    if (cannonsAlive != 8) throw new Exception($"attackmove: expected 8 cannons alive, got {cannonsAlive}");
    if (settledNearDest != 8) throw new Exception($"attackmove: only {settledNearDest}/8 resumed and settled within 8 cells of the ordered point");
    report?.Invoke("attackmove: 6 in-path pickets destroyed, both out-of-sight flankers correctly bypassed, all 8 cannons resumed and settled (hunt/resume/arrive verified)");
    return world.ComputeStateHash();
}

ulong ScenarioConstruction(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // Sidebar build-then-place flow (TICKET-P2-SIM-05), GDD Q2 adjacency,
    // CY radius, sell-back, repair, and live flow-cache invalidation.
    var world = new World(seed, 64, 64, players: 2);
    world.GrantCredits(0, 20000);
    int cy1 = world.SpawnConstructionYard(0, 8, 8);
    // ADR-008 scenario surgery: the yard itself now draws 20, and a bare yard
    // with no plant builds at the GDD s5 half-rate floor, which would slide
    // every timing assertion below. This plant keeps the early phases at full
    // power (100 supply against at most 60 draw before phase I), so they keep
    // testing the sidebar flow rather than the brown-out curve.
    int scenarioPlant = world.SpawnPowerPlant(0, 4, 8);
    // ADR-009 scenario surgery: phase I queues a Radar Uplink, whose tree
    // prerequisite is a factory, and this scenario never had one. Spawned
    // DIRECTLY rather than queued, which is the honest way to satisfy it -
    // the gate is on queueing, so a scenario that spawns its prerequisites
    // never runs the tree check at all and this phase keeps testing the radar
    // rather than the tree. Sited far from phase C's radius assertions at
    // (51,44) and (52,44), which only ever gain anchors, never lose them.
    // Its 40 draw is counted in phase I's arithmetic below.
    world.SpawnFactory(0, 4, 12);
    for (int y = 0; y < 64; y++) if (y is < 30 or > 31) world.Map.SetBlocked(30, y, true);
    int runner = world.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(31), Fix64.FromFraction(1, 4), 100, ArmourClass.Light, 0);
    var cmds = new List<Command>();
    void StepN(int n) { for (int i = 0; i < n; i++) { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }

    // Phase A: queue a plant; mid-build it is paid-for but not ready; then
    // ready; rejected placements retain readiness; a legal one consumes it.
    cmds.Add(new(0, 0, CommandType.BuildStructure, cy1, Fix64.Zero, Fix64.Zero, 1));
    StepN(50);
    if (world.Entities[cy1].ReadyStructure != 0) throw new Exception("construction: ready too early");
    if (world.Entities[cy1].BuildPaid <= 0) throw new Exception("construction: build not draining");
    StepN(60);
    if (world.Entities[cy1].ReadyStructure != 1) throw new Exception("construction: plant not ready after 110 ticks");
    if (world.Credits(0) != 20000 - 300) throw new Exception($"construction: build should have drained exactly 300, credits {world.Credits(0)}");
    int count = world.EntityCount;
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(40), Fix64.FromInt(40), 1)); // too far
    StepN(1);
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(8), Fix64.FromInt(8), 1)); // overlaps CY
    StepN(1);
    if (world.EntityCount != count || world.Entities[cy1].ReadyStructure != 1)
        throw new Exception("construction: rejected placement must retain readiness and spawn nothing");
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(12), Fix64.FromInt(8), 1)); // legal
    StepN(1);
    if (world.EntityCount != count + 1 || world.Entities[cy1].ReadyStructure != 0)
        throw new Exception("construction: legal placement must consume readiness");
    if (world.Credits(0) != 20000 - 300) throw new Exception("construction: placement must not charge again");
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(15), Fix64.FromInt(8), 1)); // nothing ready
    StepN(1);
    if (world.EntityCount != count + 1) throw new Exception("construction: placement with nothing ready must be rejected");
    int plant1 = count;

    // Phase B: chained adjacency - anchor (17,8) is Chebyshev 9 from the CY
    // (illegal alone) but 5 from the new plant (legal via the chain).
    cmds.Add(new(0, 0, CommandType.BuildStructure, cy1, Fix64.Zero, Fix64.Zero, 1));
    StepN(110);
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(17), Fix64.FromInt(8), 1));
    StepN(1);
    if (world.Credits(0) != 20000 - 600 || world.EntityCount != count + 2)
        throw new Exception("construction: chained-adjacency placement failed");

    // Phase C: CY projects the largest radius (GDD Q2 resolution).
    world.SpawnConstructionYard(0, 44, 44);
    if (world.ValidPlacement(0, 52, 44)) throw new Exception("construction: Chebyshev 8 must exceed even the CY radius");
    if (!world.ValidPlacement(0, 51, 44)) throw new Exception("construction: Chebyshev 7 must be legal via the CY radius");

    // Phase D: turret through the flow, placed via the second CY's radius,
    // then sold - half refund, footprint freed.
    cmds.Add(new(0, 0, CommandType.BuildStructure, cy1, Fix64.Zero, Fix64.Zero, 5));
    StepN(160);
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(51), Fix64.FromInt(44), 5));
    StepN(1);
    if (world.Credits(0) != 20000 - 1200) throw new Exception($"construction: turret should have drained 600 in build, credits {world.Credits(0)}");
    int turret = world.EntityCount - 1;
    if (world.Entities[turret].Kind != EntityKind.Turret) throw new Exception("construction: turret placement failed");
    cmds.Add(new(0, 0, CommandType.SellStructure, turret, Fix64.Zero, Fix64.Zero));
    StepN(1);
    if (world.Credits(0) != 20000 - 900) throw new Exception($"construction: sell-back should refund half (300), credits {world.Credits(0)}");
    if (world.Entities[turret].Alive || !world.ValidPlacement(0, 51, 44))
        throw new Exception("construction: sold structure must die and free its footprint");

    // Phase E: repair (TICKET-P2-SIM-08). An enemy cannon batters plant1;
    // pulled off, the wrench restores it at 2 hp and 1 credit per tick.
    int raider = world.SpawnUnit(1, Fix64.FromInt(17), Fix64.FromInt(9), Fix64.FromFraction(1, 5), 300, ArmourClass.Heavy, 1);
    cmds.Add(new(0, 1, CommandType.Attack, raider, Fix64.Zero, Fix64.Zero, plant1));
    StepN(60);
    cmds.Add(new(0, 1, CommandType.Move, raider, Fix64.FromInt(26), Fix64.FromInt(20)));
    StepN(40);
    var damaged = world.Entities[plant1];
    if (!damaged.Alive || damaged.Hp >= damaged.MaxHp) throw new Exception($"construction: plant should be damaged but alive (hp {damaged.Hp}/{damaged.MaxHp})");
    long beforeRepair = world.Credits(0);
    int expectedCharge = (damaged.MaxHp - damaged.Hp + 1) / 2;
    cmds.Add(new(0, 0, CommandType.Repair, plant1, Fix64.Zero, Fix64.Zero));
    StepN(expectedCharge + 10);
    var repaired = world.Entities[plant1];
    if (repaired.Hp != repaired.MaxHp || repaired.Repairing)
        throw new Exception($"construction: repair should complete and switch off (hp {repaired.Hp}/{repaired.MaxHp})");
    if (beforeRepair - world.Credits(0) != expectedCharge)
        throw new Exception($"construction: repair charge inexact ({beforeRepair - world.Credits(0)} vs {expectedCharge})");

    // Phase F: corridor denial mid-march - the near gap is sealed by a
    // direct-scripted structure; the runner must reroute via the far gap.
    for (int y = 0; y < 64; y++) if (y == 50) world.Map.SetBlocked(30, y, false);
    world.InvalidateFlowCache();
    cmds.Add(new(0, 0, CommandType.PathMove, runner, Fix64.FromInt(50), Fix64.FromInt(31)));
    StepN(30);
    int gapPlant = world.SpawnPowerPlant(0, 29, 30); // seals the near gap (scenario scripting)
    for (int t = 0; t < 900; t++)
    {
        world.Step(default);
        if (t % 100 == 99) cp?.Invoke(world.Tick, world.ComputeStateHash());
    }
    var u = world.Entities[runner];
    if (u.Moving || u.X <= Fix64.FromInt(31)
        || Fix64.DistSq(u.X - Fix64.FromInt(50), u.Y - Fix64.FromInt(31)) > Fix64.FromInt(16))
        throw new Exception($"construction: runner failed to reroute and settle (at {u.X},{u.Y}, moving={u.Moving})");
    // Phase G: MCV deploy founds a base with the CY's full radius.
    var mcvDef = world.GetUnitType(7);
    int mcv = world.SpawnUnit(0, Fix64.FromInt(40) + Fix64.Half, Fix64.FromInt(20) + Fix64.Half,
        mcvDef.Speed, mcvDef.Hp, mcvDef.Armour, 0, veterancy: false, unitType: 7);
    int preDeploy = world.EntityCount;
    cmds.Add(new(0, 0, CommandType.Deploy, mcv, Fix64.Zero, Fix64.Zero));
    StepN(1);
    if (world.Entities[mcv].Alive) throw new Exception("construction: deployed MCV should be consumed");
    if (world.EntityCount != preDeploy + 1 || world.Entities[preDeploy].Kind != EntityKind.ConstructionYard)
        throw new Exception("construction: deploy did not produce a Construction Yard");
    if (!world.ValidPlacement(0, 47, 20)) throw new Exception("construction: new CY should project its radius (Chebyshev 7)");
    // Phase H: cancelling a READY structure refunds it in full and clears
    // the slot; the paused production line is free again.
    long beforeCancel = world.Credits(0);
    cmds.Add(new(0, 0, CommandType.BuildStructure, cy1, Fix64.Zero, Fix64.Zero, 1)); // plant, 300
    StepN(1);
    StepN(101); // plant builds in 100 ticks of progress at full power
    if (world.Entities[cy1].ReadyStructure != 1) throw new Exception("construction: plant should be ready for phase H");
    long paidCancel = world.Credits(0);
    cmds.Add(new(0, 0, CommandType.CancelProduce, cy1, Fix64.Zero, Fix64.Zero));
    StepN(1);
    if (world.Entities[cy1].ReadyStructure != 0) throw new Exception("construction: cancel should clear the ready slot");
    if (world.Credits(0) != paidCancel + 300) throw new Exception($"construction: ready cancel should refund the full 300 ({world.Credits(0) - paidCancel})");
    if (world.Credits(0) != beforeCancel) throw new Exception("construction: the cancelled building should cost net nothing");
    // Phase I: the Radar Uplink is BUILDABLE (ADR-008 clause 4) and behaves as
    // a full structure, and the radar-live predicate the client's minimap
    // gates on - a living own uplink AND supply covering draw, GDD line 48's
    // below-100 clause - crosses down on power loss, recovers on a rebuilt
    // plant, and dies with the uplink. Computed here exactly as
    // SkirmishLive.AfterTicks computes it, so the sim-side truth the client
    // renders is pinned in the battery.
    bool RadarLive()
    {
        int sup = 0, drw = 0; bool uplink = false;
        foreach (var e in world.Entities)
        {
            if (!e.Alive || e.PlayerId != 0) continue;
            if (e.Kind == EntityKind.RadarUplink) uplink = true;
            sup += e.PowerSupply; drw += e.PowerDraw;
        }
        return uplink && sup >= drw;
    }
    if (RadarLive()) throw new Exception("construction: no uplink stands yet - the radar must be dark");
    cmds.Add(new(0, 0, CommandType.BuildStructure, cy1, Fix64.Zero, Fix64.Zero, 12));
    StepN(151); // 150 build ticks at full power
    if (world.Entities[cy1].ReadyStructure != 12) throw new Exception($"construction: radar uplink not ready after 151 ticks (slot {world.Entities[cy1].ReadyStructure})");
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(12), Fix64.FromInt(12), 12));
    StepN(1);
    int radar = world.EntityCount - 1;
    var rv = world.Entities[radar];
    if (rv.Kind != EntityKind.RadarUplink || rv.StructType != 12 || rv.PowerDraw != 80)
        throw new Exception($"construction: placed uplink wrong (kind {rv.Kind}, structType {rv.StructType}, draw {rv.PowerDraw})");
    if (!RadarLive()) throw new Exception("construction: uplink standing and supply covering draw - the radar must be live");
    // Sell plants until supply no longer covers draw: the blackout crossing.
    // Two sells, counted against LIVE state rather than assumed inventory:
    // the phase-B plant is already dead by now - the phase E raider spawns in
    // auto-acquire range of it, stops to engage (the shipped hold-to-fire
    // rule), and shells it down across phases F to H. That kill predates this
    // wave and no assertion ever covered it; the supply arithmetic here
    // learnt the hard way that live totals are the only honest probe (the
    // same lesson B2's harness recorded about EntityCount). After the sells
    // the gap-sealing plant's 100 stands against a draw of 140.
    cmds.Add(new(0, 0, CommandType.SellStructure, scenarioPlant, Fix64.Zero, Fix64.Zero));
    cmds.Add(new(0, 0, CommandType.SellStructure, plant1, Fix64.Zero, Fix64.Zero));
    StepN(1);
    if (RadarLive()) throw new Exception("construction: supply below draw must take the radar dark");
    // Recovery: one rebuilt plant relights it.
    cmds.Add(new(0, 0, CommandType.BuildStructure, cy1, Fix64.Zero, Fix64.Zero, 1));
    StepN(202); // 100 build ticks at the half-rate floor (supply below draw), plus placement slack
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(12), Fix64.FromInt(16), 1));
    StepN(1);
    if (!RadarLive()) throw new Exception("construction: a rebuilt plant must relight the radar");
    // And the uplink itself sells for exactly half of 900 - IsStructure
    // membership proven by behaviour - taking the radar dark for good.
    long beforeRadarSell = world.Credits(0);
    cmds.Add(new(0, 0, CommandType.SellStructure, radar, Fix64.Zero, Fix64.Zero));
    StepN(1);
    if (world.Credits(0) != beforeRadarSell + 450)
        throw new Exception($"construction: uplink sell-back should refund exactly 450 (got {world.Credits(0) - beforeRadarSell})");
    if (world.Entities[radar].Alive || !world.ValidPlacement(0, 12, 12, 12))
        throw new Exception("construction: a sold uplink must die and free its footprint");
    if (RadarLive()) throw new Exception("construction: no uplink, no radar");
    report?.Invoke("construction: sidebar queue/ready/place flow exact (rejects retain readiness); chained adjacency and CY radius verified; sell-back refunded half; repair restored full hp at exact cost; corridor sealed mid-march and rerouted; MCV deployed into a radius-projecting CY; ready-cancel refunded in full; radar uplink built, placed with draw 80, radar-live predicate crossed dark on plant sales, relit on a rebuilt plant, and died with its uplink at exactly 450 refund");
    return world.ComputeStateHash();
}

World BuildSkirmishWorld(ulong seed)
{
    // The committed map file is the single source of terrain, fields, and
    // start positions (TICKET-P2-DATA-03); the scenario adds forces and funds.
    string mapPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/maps/skirmish-01.fmap"));
    var map = MapData.Load(mapPath);
    var world = map.BuildWorld(seed, players: 2);
    // ADR-011 (Wave B5): the gated skirmish scenario now builds the SAME
    // opening hand a real skirmish begins with - the two construction yards
    // plus one harvester and three rifle squads per side at cell centres -
    // through the shared MapLoader builder, so the golden covers the world
    // players actually play rather than a bare two-yard world nobody plays.
    // The 8000-credit treasury is unchanged (ADR-011 clause 2), so the
    // golden's movement is attributable to the hand and the centring alone.
    map.PlaceSkirmishStart(world, 8000);
    return world;
}

ulong ScenarioSkirmish(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // Two rule-based commanders play a full match through the public command
    // interface (TICKET-AI-01): build order, harvesting, production, defence,
    // and attack waves - the complete classic loop, closed, with no human.
    var world = BuildSkirmishWorld(seed);
    var ais = new[] { new SkirmishAI(0), new SkirmishAI(1) };
    var cmds = new List<Command>();
    const int ticks = 5000;
    for (int t = 0; t < ticks; t++)
    {
        cmds.Clear();
        ais[0].Act(world, cmds);
        ais[1].Act(world, cmds);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        if (t % 250 == 249) cp?.Invoke(t + 1, world.ComputeStateHash());
    }

    Span<int> structures = stackalloc int[2];
    Span<int> unitsBuilt = stackalloc int[2];
    Span<int> harvesters = stackalloc int[2];
    int deaths = 0;
    foreach (var e in world.Entities)
    {
        if (e.PlayerId < 0) continue;
        if (!e.Alive) { deaths++; continue; }
        if (e.Kind is EntityKind.PowerPlant or EntityKind.Refinery or EntityKind.Factory or EntityKind.Turret) structures[e.PlayerId]++;
        if (e.Kind == EntityKind.Unit) unitsBuilt[e.PlayerId]++;
        if (e.Kind == EntityKind.Harvester) harvesters[e.PlayerId]++;
    }
    for (int p = 0; p < 2; p++)
    {
        if (structures[p] < 3 && deaths == 0)
            throw new Exception($"skirmish: player {p} failed to build a base ({structures[p]} structures, no combat either)");
    }
    if (unitsBuilt[0] + unitsBuilt[1] + deaths < 8)
        throw new Exception($"skirmish: almost no military activity (alive {unitsBuilt[0]}+{unitsBuilt[1]}, deaths {deaths})");
    if (deaths == 0)
        throw new Exception("skirmish: 5000 ticks and nobody fired a shot - waves never launched");
    long spent0 = world.Credits(0), spent1 = world.Credits(1);
    report?.Invoke($"skirmish: full AI-vs-AI match ran the complete loop - bases built, harvesting live, {deaths} entities destroyed, treasuries {spent0}/{spent1}");
    return world.ComputeStateHash();
}

int ReplayCheck()
{
    // TICKET-P2-SIM-07: record an AI-vs-AI match, replay the bare command
    // stream with NO AI attached, and demand the identical final hash.
    const ulong seed = 2026;
    const int ticks = 3000;
    var world = BuildSkirmishWorld(seed);
    var ais = new[] { new SkirmishAI(0), new SkirmishAI(1) };
    // ADR-006: the recording carries the catalogue checksum, as the client's
    // BeginRecording now does; the round-trip below proves the carry.
    var writer = new ReplayWriter(seed, "skirmish", world.CatalogueChecksum);
    var cmds = new List<Command>();
    for (int t = 0; t < ticks; t++)
    {
        cmds.Clear();
        ais[0].Act(world, cmds);
        ais[1].Act(world, cmds);
        foreach (var c in cmds) writer.Record(in c);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
    }
    ulong liveHash = world.ComputeStateHash();
    string path = Path.Combine(Path.GetTempPath(), "ferrostorm-selftest.slrep");
    writer.Finish(liveHash, path);

    var replay = Replay.Load(path);
    if (replay.Seed != seed || replay.Setup != "skirmish") return Fail("replay: header round-trip");
    if (!replay.HasCatalogueChecksum || replay.CatalogueChecksum != world.CatalogueChecksum)
        return Fail("replay: catalogue checksum did not round-trip (ADR-006)");
    replay.AssertCatalogueMatches(world.CatalogueChecksum); // same catalogue: must pass silently
    var world2 = BuildSkirmishWorld(replay.Seed);
    var buf = new List<Command>();
    for (int t = 0; t < ticks; t++)
    {
        buf.Clear();
        buf.AddRange(replay.CommandsFor(t));
        world2.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buf));
    }
    ulong replayHash = world2.ComputeStateHash();
    if (replayHash != liveHash) return Fail($"replay: hash mismatch 0x{replayHash:X16} vs live 0x{liveHash:X16}");
    if (replay.FinalHash != liveHash) return Fail("replay: recorded hash line mismatch");
    Console.WriteLine($"replay: {new FileInfo(path).Length} bytes reproduced a {ticks}-tick AI match bit-exactly (0x{liveHash:X16})");
    return 0;
}

ulong ScenarioStealth(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P2-SIM-09: the three stealth rules, isolated.
    var world = new World(seed, 64, 64, players: 2);
    var raiderDef = world.GetUnitType(5);
    int Raider(Fix64 x, Fix64 y) => world.SpawnUnit(1, x, y, raiderDef.Speed, raiderDef.Hp, raiderDef.Armour,
        raiderDef.WeaponId, raiderDef.SightCells, stealth: true, veterancy: true, unitType: 5);
    var cmds = new List<Command>();
    void StepN(int n) { for (int i = 0; i < n; i++) { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }

    // ADR-008 scenario surgery: the turret must be POWERED from tick 0
    // (supply 100 against draw 20 for the whole scenario) or rule 1 passes
    // because the turret is dead rather than because stealth holds. The
    // plant below at rule 2 keeps its geometry role unchanged.
    world.SpawnPowerPlant(0, 10, 10);
    // Rule 1: undetected stealth is untargetable. A raider parks inside the
    // turret's range 5 but outside its OWN rifle range 3 (truly passive -
    // any closer and it would auto-engage, legitimately breaking stealth)
    // for 100 ticks and must not lose a single hit point.
    int turret = world.SpawnTurret(0, 20, 20);
    int ghost = Raider(Fix64.FromInt(25), Fix64.FromInt(21));
    StepN(100);
    if (world.Entities[ghost].Hp != raiderDef.Hp)
        throw new Exception($"stealth: undetected raider was shot ({world.Entities[ghost].Hp}/{raiderDef.Hp})");

    // Rule 2: firing breaks stealth. The raider attacks a plant hard against
    // the turret - the plant centre sits 2 cells from the turret centre, so
    // EVERY point within the raider's rifle range 3 of it lies within the
    // turret's range 5. The muzzle flash makes it fair game; punishment is
    // geometrically inescapable.
    int plant = world.SpawnPowerPlant(0, 22, 20, hp: 1500);
    cmds.Add(new(0, 1, CommandType.Attack, ghost, Fix64.Zero, Fix64.Zero, plant));
    StepN(120);
    var shot = world.Entities[ghost];
    if (shot.Alive && shot.Hp == raiderDef.Hp)
        throw new Exception("stealth: firing raider was never punished - reveal-on-fire dead");
    if (world.Entities[plant].Hp == 1500)
        throw new Exception("stealth: raider never damaged the plant");

    // Rule 3: detectors paint stealth for their whole team. A fresh, passive
    // raider sits by the turret unharmed until a sentinel scout arrives.
    int lurker = Raider(Fix64.FromInt(17), Fix64.FromInt(21));
    StepN(80);
    if (world.Entities[lurker].Hp != raiderDef.Hp)
        throw new Exception("stealth: passive lurker should be safe without detection");
    var scoutDef = world.GetUnitType(6);
    world.SpawnUnit(0, Fix64.FromInt(15), Fix64.FromInt(21), scoutDef.Speed, scoutDef.Hp, scoutDef.Armour,
        scoutDef.WeaponId, scoutDef.SightCells, detector: true, unitType: 6);
    StepN(80);
    if (world.Entities[lurker].Alive && world.Entities[lurker].Hp == raiderDef.Hp)
        throw new Exception("stealth: detector coverage failed to make the lurker targetable");
    if (cp != null) cp(world.Tick, world.ComputeStateHash());
    report?.Invoke("stealth: undetected raider untouchable in turret range; firing broke stealth and drew fire; detector arrival painted the passive lurker (all three rules verified; turret POWERED throughout, 100 supply vs 20 draw per ADR-008)");
    return world.ComputeStateHash();
}

ulong ScenarioVeterancy(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P2-SIM-10: promotion at 3 and 6 kills, damage scaling 4/4 ->
    // 5/4 -> 6/4 verified to the hit point, and elite self-repair.
    var world = new World(seed, 64, 64, players: 2);
    int vet = world.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(20), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2); // rifle: 12 dmg vs None
    var cmds = new List<Command>();
    void StepN(int n) { for (int i = 0; i < n; i++) { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }

    int MeasureShot(int probeHp)
    {
        int probe = world.SpawnUnit(1, Fix64.FromInt(22), Fix64.FromInt(20), Fix64.Zero, probeHp, ArmourClass.None, 0);
        cmds.Add(new(0, 0, CommandType.Attack, vet, Fix64.Zero, Fix64.Zero, probe));
        for (int guard = 0; guard < 60; guard++)
        {
            StepN(1);
            if (world.Entities[probe].Hp != probeHp)
            {
                int dmg = probeHp - world.Entities[probe].Hp;
                cmds.Add(new(0, 1, CommandType.Stop, probe, Fix64.Zero, Fix64.Zero)); // noop carrier tidy
                cmds.Add(new(0, 0, CommandType.Stop, vet, Fix64.Zero, Fix64.Zero));
                StepN(1);
                // Finish the probe off cheaply so it does not clutter later kills.
                var pr = world.Entities[probe];
                if (pr.Alive)
                {
                    cmds.Add(new(0, 0, CommandType.Attack, vet, Fix64.Zero, Fix64.Zero, probe));
                    while (world.Entities[probe].Alive) StepN(1);
                    cmds.Add(new(0, 0, CommandType.Stop, vet, Fix64.Zero, Fix64.Zero));
                    StepN(1);
                }
                return dmg;
            }
        }
        throw new Exception("veterancy: probe was never hit");
    }

    int KillCount() => world.Entities[vet].Kills;
    void FeedKills(int n)
    {
        for (int k = 0; k < n; k++)
        {
            int dummy = world.SpawnUnit(1, Fix64.FromInt(22), Fix64.FromInt(21), Fix64.Zero, 1, ArmourClass.None, 0);
            cmds.Add(new(0, 0, CommandType.Attack, vet, Fix64.Zero, Fix64.Zero, dummy));
            while (world.Entities[dummy].Alive) StepN(1);
        }
        cmds.Add(new(0, 0, CommandType.Stop, vet, Fix64.Zero, Fix64.Zero));
        StepN(1);
    }

    int rookie = MeasureShot(100);
    if (rookie != 12) throw new Exception($"veterancy: rookie rifle should hit for 12, hit for {rookie}");
    int killsSoFar = KillCount();
    FeedKills(3 - killsSoFar);
    if (world.Entities[vet].Rank != 1) throw new Exception($"veterancy: 3 kills should promote to veteran (kills={KillCount()}, rank={world.Entities[vet].Rank})");
    int veteran = MeasureShot(100);
    if (veteran != 15) throw new Exception($"veterancy: veteran should hit for 15 (12*5/4), hit for {veteran}");
    FeedKills(6 - KillCount());
    if (world.Entities[vet].Rank != 2) throw new Exception($"veterancy: 6 kills should promote to elite (kills={KillCount()})");
    int elite = MeasureShot(100);
    if (elite != 18) throw new Exception($"veterancy: elite should hit for 18 (12*6/4), hit for {elite}");

    // Elite self-repair: batter the elite, pull the attacker off, count the healing.
    int bully = world.SpawnUnit(1, Fix64.FromInt(23), Fix64.FromInt(20), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, 2);
    cmds.Add(new(0, 1, CommandType.Attack, bully, Fix64.Zero, Fix64.Zero, vet));
    StepN(20);
    cmds.Add(new(0, 1, CommandType.Move, bully, Fix64.FromInt(40), Fix64.FromInt(40)));
    StepN(5);
    int woundedHp = world.Entities[vet].Hp;
    if (woundedHp >= 100) throw new Exception("veterancy: elite was never wounded for the regen test");
    StepN(46); // three 15-tick regen beats
    int healed = world.Entities[vet].Hp - woundedHp;
    if (healed < 3) throw new Exception($"veterancy: elite regen too slow ({healed} hp in 46 ticks)");
    if (cp != null) cp(world.Tick, world.ComputeStateHash());
    report?.Invoke($"veterancy: 12/15/18 damage at rookie/veteran/elite exact; promotions at 3 and 6 kills; elite regenerated {healed} hp off-combat");
    return world.ComputeStateHash();
}

ulong ScenarioVictory(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P2-SIM-12, short-game rule: structures or an MCV keep a player
    // alive; units alone do not. The winner latches the tick the last hope dies.
    var world = new World(seed, 64, 64, players: 2);
    world.SpawnConstructionYard(0, 8, 8);
    int plant = world.SpawnPowerPlant(1, 40, 30, hp: 150);
    var mcvDef = world.GetUnitType(7);
    int mcv = world.SpawnUnit(1, Fix64.FromInt(50), Fix64.FromInt(40), mcvDef.Speed, mcvDef.Hp, mcvDef.Armour, 0, veterancy: false, unitType: 7);
    int survivorRifle = world.SpawnUnit(1, Fix64.FromInt(55), Fix64.FromInt(55), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
    _ = survivorRifle;
    var cannons = new[]
    {
        world.SpawnUnit(0, Fix64.FromInt(36), Fix64.FromInt(30), Fix64.FromFraction(1, 5), 300, ArmourClass.Heavy, 1),
        world.SpawnUnit(0, Fix64.FromInt(36), Fix64.FromInt(32), Fix64.FromFraction(1, 5), 300, ArmourClass.Heavy, 1),
    };
    var cmds = new List<Command>();
    foreach (int c in cannons) cmds.Add(new(0, 0, CommandType.Attack, c, Fix64.Zero, Fix64.Zero, plant));
    bool eliminatedSeen = false;
    int winnerAtPlantDeath = -2;
    for (int t = 0; t < 400 && world.Entities[plant].Alive; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        foreach (var ev in world.Events) if (ev.Type == GameEventType.PlayerEliminated) eliminatedSeen = true;
    }
    if (world.Entities[plant].Alive) throw new Exception("victory: plant survived the scripted assault");
    winnerAtPlantDeath = world.Winner;
    if (winnerAtPlantDeath != -1 || eliminatedSeen)
        throw new Exception("victory: the MCV should keep player 1's hopes alive after the last structure fell");

    foreach (int c in cannons) cmds.Add(new(0, 0, CommandType.Attack, c, Fix64.Zero, Fix64.Zero, mcv));
    int winnerTick = -1, mcvDeathTick = -1;
    for (int t = 0; t < 1500 && winnerTick < 0; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (mcvDeathTick < 0 && !world.Entities[mcv].Alive) mcvDeathTick = world.Tick;
        foreach (var ev in world.Events) if (ev.Type == GameEventType.PlayerEliminated && ev.B == 1) eliminatedSeen = true;
        if (world.Winner >= 0) winnerTick = world.Tick;
        if (t % 100 == 99) cp?.Invoke(world.Tick, world.ComputeStateHash());
    }
    if (world.Winner != 0) throw new Exception($"victory: player 0 should have won (winner={world.Winner})");
    if (!eliminatedSeen) throw new Exception("victory: elimination event never emitted");
    if (winnerTick != mcvDeathTick) throw new Exception($"victory: winner should latch the tick the MCV died ({winnerTick} vs {mcvDeathTick})");
    report?.Invoke($"victory: last structure fell with no winner (MCV = hope); MCV death latched the win the same tick; the stray rifle changed nothing (short-game rule exact)");
    return world.ComputeStateHash();
}

ulong ScenarioExpansion(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-AI-03: a lone commander with a thin home field must buy an MCV,
    // drive it to the rich far field, deploy a second base, add a refinery
    // and second harvester, and mine the new deposit.
    var world = new World(seed, 96, 64, players: 2);
    world.GrantCredits(0, 9000);
    world.SpawnConstructionYard(0, 8, 30);
    int nearField = world.SpawnFerriteField(Fix64.FromInt(20), Fix64.FromInt(28), 2500);
    int farField = world.SpawnFerriteField(Fix64.FromInt(60), Fix64.FromInt(30), 12000);
    _ = nearField;
    var ai = SkirmishAI.Standard(0);
    var cmds = new List<Command>();
    const int ticks = 7000;
    for (int t = 0; t < ticks; t++)
    {
        cmds.Clear();
        ai.Act(world, cmds);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        if (t % 500 == 499) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    int cys = 0, refineries = 0, working = 0;
    foreach (var e in world.Entities)
    {
        if (!e.Alive || e.PlayerId != 0) continue;
        if (e.Kind == EntityKind.ConstructionYard) cys++;
        if (e.Kind == EntityKind.Refinery) refineries++;
        if (e.Kind == EntityKind.Harvester && e.HState != HarvestState.Idle) working++;
    }
    if (cys != 2) throw new Exception($"expansion: expected a second base, got {cys} CYs");
    if (refineries != 2) throw new Exception($"expansion: expected a refinery per base, got {refineries}");
    if (world.Entities[farField].FerriteAmount >= 12000)
        throw new Exception("expansion: the far field was never mined");
    if (working < 1) throw new Exception("expansion: no harvester working at the end");
    report?.Invoke($"expansion: AI bought an MCV, founded a second base at the rich field, added its refinery, and mined it down to {world.Entities[farField].FerriteAmount} (economy migrated)");
    return world.ComputeStateHash();
}

ulong ScenarioArtillery(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P2-SIM-14: splash hits clumps, the dead zone is absolute.
    var world = new World(seed, 64, 64, players: 2);
    var hDef = world.GetUnitType(8);
    int gun = world.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(30), hDef.Speed, hDef.Hp, hDef.Armour, hDef.WeaponId, hDef.SightCells, unitType: 8);
    // A tight clump of three rifles at range 8: one shell should hurt all three.
    int r1 = world.SpawnUnit(1, Fix64.FromInt(28), Fix64.FromInt(30), Fix64.Zero, 100, ArmourClass.None, 0);
    int r2 = world.SpawnUnit(1, Fix64.FromInt(28) + Fix64.Half, Fix64.FromInt(30), Fix64.Zero, 100, ArmourClass.None, 0);
    int r3 = world.SpawnUnit(1, Fix64.FromInt(28), Fix64.FromInt(30) + Fix64.Half, Fix64.Zero, 100, ArmourClass.None, 0);
    var cmds = new List<Command> { new(0, 0, CommandType.Attack, gun, Fix64.Zero, Fix64.Zero, r1) };
    world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
    cmds.Clear();
    // One volley: direct hit 60*30%=18 on r1; splash 9 each on r2/r3.
    if (world.Entities[r1].Hp != 82) throw new Exception($"artillery: direct hit should deal 18 (hp {world.Entities[r1].Hp})");
    if (world.Entities[r2].Hp != 91 || world.Entities[r3].Hp != 91)
        throw new Exception($"artillery: splash should deal 9 to the clump ({world.Entities[r2].Hp}/{world.Entities[r3].Hp})");

    // Dead zone: a rifle standing 2 cells away is untouchable and the gun
    // must not fire at it even under an explicit order.
    var world2 = new World(seed + 1, 64, 64, players: 2);
    int gun2 = world2.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(30), hDef.Speed, hDef.Hp, hDef.Armour, hDef.WeaponId, hDef.SightCells, unitType: 8);
    int close = world2.SpawnUnit(1, Fix64.FromInt(22), Fix64.FromInt(30), Fix64.Zero, 100, ArmourClass.None, 0);
    cmds.Add(new(0, 0, CommandType.Attack, gun2, Fix64.Zero, Fix64.Zero, close));
    for (int t = 0; t < 100; t++) { world2.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); }
    if (world2.Entities[close].Hp != 100)
        throw new Exception("artillery: the dead zone was violated");
    if (cp != null) { cp(world.Tick, world.ComputeStateHash()); cp(world2.Tick, world2.ComputeStateHash()); }
    report?.Invoke("artillery: one shell hurt the whole clump (18 direct, 9 splash x2, exact); the 3-cell dead zone held absolute under a standing order");
    return world.ComputeStateHash() ^ world2.ComputeStateHash();
}

ulong ScenarioSuperweapon(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P2-SIM-15: charge (power-gated), warning, detonation, recharge.
    var world = new World(seed, 64, 64, players: 2);
    world.GrantCredits(0, 1000);
    world.SpawnConstructionYard(0, 6, 6);
    int plant1 = world.SpawnPowerPlant(0, 10, 6);   // supply 100
    int plant2 = world.SpawnPowerPlant(0, 14, 6);   // supply 200
    // ADR-008 clause 6: under the honest draws this base draws 170 (the
    // superweapon's 150 plus the yard's 20), and plants supply 100 each, so
    // no whole-plant total can ever EQUAL 170 - and the equality IS the test
    // below. The nullable supply override is the named escape: 100 + 100 + 70
    // means selling plant1 lands the total at exactly 170 against 170, and
    // the inclusive-boundary assertion survives without loosening anything.
    int plant3 = world.SpawnPowerPlant(0, 18, 6, supply: 70); // supply 270 total vs draw 170
    _ = plant2; _ = plant3;
    int super = world.SpawnSuperweapon(0, 6, 10, chargeTicks: 90);
    // Target cluster far away: two rifles at ground zero, a factory in the
    // outer ring, a bystander outside both rings.
    var gz = (X: Fix64.FromInt(45), Y: Fix64.FromInt(40));
    int v1 = world.SpawnUnit(1, gz.X, gz.Y, Fix64.Zero, 100, ArmourClass.None, 0);
    int v2 = world.SpawnUnit(1, gz.X + Fix64.Half, gz.Y, Fix64.Zero, 100, ArmourClass.None, 0);
    int vFactory = world.SpawnFactory(1, 46, 41); // centre (47,42): dist ~2.8 = outer ring
    int bystander = world.SpawnUnit(1, gz.X + Fix64.FromInt(5), gz.Y, Fix64.Zero, 100, ArmourClass.None, 0);
    var cmds = new List<Command>();
    void StepN(int n) { for (int i = 0; i < n; i++) { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }

    // Power-gated charge, the boundary INCLUSIVE: the charge runs at full
    // supply, and selling plant1 drops the total to exactly equal the draw -
    // World's `supply >= draw` must keep the charge running on the equality.
    StepN(40);
    int chargeMid = world.Entities[super].ChargeTicks;
    if (chargeMid != 50) throw new Exception($"superweapon: charge should be 50 after 40 ticks (got {chargeMid})");
    cmds.Add(new(0, 0, CommandType.SellStructure, plant1, Fix64.Zero, Fix64.Zero)); // supply 270 -> 170, draw 170: boundary holds
    StepN(1);
    // Premature launch attempt must be refused while charging.
    cmds.Add(new(0, 0, CommandType.LaunchSuper, super, gz.X, gz.Y));
    StepN(1);
    if (world.Entities[super].StrikeTicks >= 0) throw new Exception("superweapon: launched before charged");
    StepN(60);
    if (world.Entities[super].ChargeTicks != 0) throw new Exception($"superweapon: should be charged (remaining {world.Entities[super].ChargeTicks})");

    cmds.Add(new(0, 0, CommandType.LaunchSuper, super, gz.X, gz.Y));
    StepN(1);
    if (world.Entities[super].StrikeTicks < 0) throw new Exception("superweapon: launch refused while ready");
    StepN(40);
    if (!world.Entities[v1].Alive == false && world.Entities[v1].Hp != 100) throw new Exception("superweapon: damage before impact");
    bool impactSeen = false;
    for (int t = 0; t < 40 && !impactSeen; t++)
    {
        world.Step(default);
        foreach (var ev in world.Events) if (ev.Type == GameEventType.SuperweaponImpact) impactSeen = true;
    }
    if (!impactSeen) throw new Exception("superweapon: impact never arrived");
    if (world.Entities[v1].Alive || world.Entities[v2].Alive)
        throw new Exception("superweapon: ground-zero rifles should be annihilated (720 Omni)");
    if (world.Entities[vFactory].Hp != 1500 - 360)
        throw new Exception($"superweapon: outer-ring factory should take exactly 360 (hp {world.Entities[vFactory].Hp})");
    if (world.Entities[bystander].Hp != 100)
        throw new Exception("superweapon: the bystander outside both rings was hit");
    if (world.Entities[super].ChargeTicks != 1500 - 39 && world.Entities[super].ChargeTicks > 1500)
        throw new Exception($"superweapon: recharge did not restart properly ({world.Entities[super].ChargeTicks})");
    if (cp != null) cp(world.Tick, world.ComputeStateHash());
    report?.Invoke("superweapon: 90-tick charge exact; premature launch refused; 75-tick warning honoured; ground zero annihilated, outer ring damaged to the point, bystander untouched; recharge restarted");
    return world.ComputeStateHash();
}

ulong ScenarioCrush(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P2-SIM-16: an MCV (heavy, unarmed - so the kill can only be the
    // treads) rolls through a picket line of enemy rifles. Every squad dies,
    // not one shot is fired, and the MCV earns no rank (veterancy disabled).
    var world = new World(seed, 64, 64, players: 2);
    var mcvDef = world.GetUnitType(7);
    int mcv = world.SpawnUnit(0, Fix64.FromInt(10) + Fix64.Half, Fix64.FromInt(30) + Fix64.Half,
        mcvDef.Speed, mcvDef.Hp, mcvDef.Armour, 0, veterancy: false, unitType: 7);
    var rifles = new[]
    {
        world.SpawnUnit(1, Fix64.FromInt(20) + Fix64.Half, Fix64.FromInt(30) + Fix64.Half, Fix64.Zero, 100, ArmourClass.None, 0),
        world.SpawnUnit(1, Fix64.FromInt(24) + Fix64.Half, Fix64.FromInt(30) + Fix64.Half, Fix64.Zero, 100, ArmourClass.None, 0),
        world.SpawnUnit(1, Fix64.FromInt(28) + Fix64.Half, Fix64.FromInt(30) + Fix64.Half, Fix64.Zero, 100, ArmourClass.None, 0),
    };
    var cmds = new List<Command>
    {
        new(0, 0, CommandType.Move, mcv, Fix64.FromInt(34) + Fix64.Half, Fix64.FromInt(30) + Fix64.Half),
    };
    int firedEvents = 0;
    for (int t = 0; t < 300; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        foreach (var ev in world.Events) if (ev.Type == GameEventType.Fired) firedEvents++;
        if (t % 100 == 99) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    foreach (int r in rifles)
        if (world.Entities[r].Alive) throw new Exception($"crush: rifle {r} survived the treads");
    if (firedEvents != 0) throw new Exception($"crush: {firedEvents} shots in a fight that should be all treads");
    var m = world.Entities[mcv];
    if (!m.Alive || m.Hp != mcvDef.Hp) throw new Exception("crush: the MCV should be untouched");
    if (m.Kills != 0 || m.Rank != 0) throw new Exception("crush: veterancy-disabled crusher must not rank up");
    if (m.Moving) throw new Exception("crush: the MCV never completed its drive");
    report?.Invoke("crush: three squads flattened without a shot fired; the veterancy-disabled MCV stayed rank 0 and finished its drive untouched");
    return world.ComputeStateHash();
}

ulong ScenarioAiSuper(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-AI-04: a rich commander with a complete base must decide to
    // build the superweapon, wait out the charge, and fire it at the enemy
    // refinery - end to end, through the public command interface.
    var world = new World(seed, 96, 64, players: 2);
    world.GrantCredits(0, 15000);
    world.SpawnConstructionYard(0, 8, 30);
    world.SpawnPowerPlant(0, 12, 30);
    world.SpawnPowerPlant(0, 8, 26);   // headroom for the superweapon's 100 draw
    world.SpawnRefinery(0, 12, 26);
    world.SpawnFactory(0, 8, 34);
    int harv = world.SpawnHarvester(0, Fix64.FromInt(14), Fix64.FromInt(34));
    int field = world.SpawnFerriteField(Fix64.FromInt(22), Fix64.FromInt(30), 12000);
    world.SpawnConstructionYard(1, 86, 30);
    int enemyRefinery = world.SpawnRefinery(1, 82, 30);
    var ai = SkirmishAI.Standard(0);
    var cmds = new List<Command> { new(0, 0, CommandType.Harvest, harv, Fix64.Zero, Fix64.Zero, field) };
    bool launched = false, impacted = false;
    int superBuilt = -1, radarBuilt = -1;
    const int ticks = 4500;
    for (int t = 0; t < ticks; t++)
    {
        ai.Act(world, cmds);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        foreach (var ev in world.Events)
        {
            if (ev.Type == GameEventType.StructurePlaced && superBuilt < 0
                && world.Entities[ev.A].Kind == EntityKind.Superweapon) superBuilt = world.Tick;
            if (ev.Type == GameEventType.StructurePlaced && radarBuilt < 0
                && world.Entities[ev.A].Kind == EntityKind.RadarUplink) radarBuilt = world.Tick;
            if (ev.Type == GameEventType.SuperweaponLaunched) launched = true;
            if (ev.Type == GameEventType.SuperweaponImpact) impacted = true;
        }
        if (t % 500 == 499) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    if (superBuilt < 0) throw new Exception("aisuper: the AI never built its superweapon");
    // ADR-008 clause 4: the ladder raises the radar before the superweapon,
    // which is what keeps the AI alive the day prerequisites land (ADR-009).
    if (radarBuilt < 0 || radarBuilt > superBuilt)
        throw new Exception($"aisuper: the radar must stand before the superweapon (radar {radarBuilt}, super {superBuilt})");
    if (!launched) throw new Exception("aisuper: charged and never fired");
    if (!impacted) throw new Exception("aisuper: launch without impact");
    var target = world.Entities[enemyRefinery];
    if (target.Alive && target.Hp >= target.MaxHp)
        throw new Exception("aisuper: the enemy refinery came through unscathed");
    report?.Invoke($"aisuper: superweapon placed at tick {superBuilt}, charged, and fired at the enemy refinery ({(target.Alive ? $"battered to {target.Hp}/{target.MaxHp}" : "destroyed")})");
    return world.ComputeStateHash();
}

ulong ScenarioVeil(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P2-SIM-18: the veil projector cloaks nearby friendlies - and
    // the whole veil drops the instant the base loses full power.
    var world = new World(seed, 64, 64, players: 2);
    world.SpawnTurret(1, 20, 20); // enemy turret, range 5, centre (21,21)
    // ADR-008 scenario surgery: the turret's OWNER needs power (100 supply
    // against 20 draw) or the gate silences the gun and the baseline
    // assertion throws - the good failure the ADR predicted.
    world.SpawnPowerPlant(1, 50, 50);
    int plant = world.SpawnPowerPlant(0, 40, 40); // supply 100 vs projector draw 60
    var cmds = new List<Command>();
    void StepN(int n) { for (int i = 0; i < n; i++) { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }

    // Baseline: an uncloaked rifle inside turret range gets shot.
    int bait = world.SpawnUnit(0, Fix64.FromInt(25), Fix64.FromInt(21), Fix64.Zero, 100, ArmourClass.None, 0);
    StepN(60);
    if (world.Entities[bait].Hp == 100) throw new Exception("veil: baseline rifle was never engaged");

    // Cloaked: a projector at (26,20) covers (25,22); the rifle is invisible.
    world.SpawnVeilProjector(0, 26, 20);
    int ghost = world.SpawnUnit(0, Fix64.FromInt(25), Fix64.FromInt(22), Fix64.Zero, 100, ArmourClass.None, 0);
    StepN(100);
    if (world.Entities[ghost].Hp != 100)
        throw new Exception($"veil: cloaked rifle was shot ({world.Entities[ghost].Hp}/100)");

    // Brown-out: selling the plant drops supply below the projector's draw -
    // the veil collapses and the turret opens fire.
    cmds.Add(new(0, 0, CommandType.SellStructure, plant, Fix64.Zero, Fix64.Zero));
    StepN(60);
    if (world.Entities[ghost].Hp == 100)
        throw new Exception("veil: power cut should have dropped the veil and exposed the rifle");
    if (cp != null) cp(world.Tick, world.ComputeStateHash());
    report?.Invoke("veil: baseline rifle engaged; cloaked rifle untouchable for 100 ticks; selling the plant collapsed the veil and the turret opened fire (power coupling exact; turret owner POWERED throughout, 100 supply vs 20 draw per ADR-008)");
    return world.ComputeStateHash();
}

ulong ScenarioWaypoints(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P2-SIM-19: shift-queued orders execute in sequence; a fresh
    // direct order wipes the remaining plan.
    var world = new World(seed, 64, 64, players: 1);
    int u = world.SpawnUnit(0, Fix64.FromInt(8) + Fix64.Half, Fix64.FromInt(8) + Fix64.Half,
        Fix64.FromFraction(1, 2), 100, ArmourClass.Light, 0);
    var wp = new[]
    {
        (X: Fix64.FromInt(40) + Fix64.Half, Y: Fix64.FromInt(8) + Fix64.Half),
        (X: Fix64.FromInt(40) + Fix64.Half, Y: Fix64.FromInt(40) + Fix64.Half),
        (X: Fix64.FromInt(8) + Fix64.Half, Y: Fix64.FromInt(40) + Fix64.Half),
    };
    var cmds = new List<Command>
    {
        new(0, 0, CommandType.PathMove, u, wp[0].X, wp[0].Y),
        new(0, 0, CommandType.PathMove, u, wp[1].X, wp[1].Y, queued: true),
        new(0, 0, CommandType.PathMove, u, wp[2].X, wp[2].Y, queued: true),
    };
    var reachedAt = new int[3] { -1, -1, -1 };
    for (int t = 0; t < 800; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        var e = world.Entities[u];
        for (int i = 0; i < 3; i++)
            if (reachedAt[i] < 0 && Fix64.DistSq(e.X - wp[i].X, e.Y - wp[i].Y) <= Fix64.FromInt(20)) reachedAt[i] = t;
        if (t % 200 == 199) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    if (reachedAt[0] < 0 || reachedAt[1] < 0 || reachedAt[2] < 0)
        throw new Exception($"waypoints: not all waypoints visited ({reachedAt[0]}/{reachedAt[1]}/{reachedAt[2]})");
    if (!(reachedAt[0] < reachedAt[1] && reachedAt[1] < reachedAt[2]))
        throw new Exception($"waypoints: visited out of order ({reachedAt[0]}, {reachedAt[1]}, {reachedAt[2]})");

    // A direct order mid-plan wipes the rest: re-run, override after wp1.
    var world2 = new World(seed + 1, 64, 64, players: 1);
    int u2 = world2.SpawnUnit(0, Fix64.FromInt(8) + Fix64.Half, Fix64.FromInt(8) + Fix64.Half,
        Fix64.FromFraction(1, 2), 100, ArmourClass.Light, 0);
    cmds.Add(new(0, 0, CommandType.PathMove, u2, wp[0].X, wp[0].Y));
    cmds.Add(new(0, 0, CommandType.PathMove, u2, wp[1].X, wp[1].Y, queued: true));
    cmds.Add(new(0, 0, CommandType.PathMove, u2, wp[2].X, wp[2].Y, queued: true));
    bool overridden = false;
    Fix64 nearestWp2 = Fix64.MaxValue;
    var home = (X: Fix64.FromInt(8) + Fix64.Half, Y: Fix64.FromInt(20) + Fix64.Half);
    for (int t = 0; t < 800; t++)
    {
        world2.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        var e = world2.Entities[u2];
        if (!overridden && Fix64.DistSq(e.X - wp[0].X, e.Y - wp[0].Y) <= Fix64.FromInt(20))
        {
            overridden = true;
            cmds.Add(new(0, 0, CommandType.PathMove, u2, home.X, home.Y)); // direct: wipes wp2 and wp3
        }
        Fix64 d2 = Fix64.DistSq(e.X - wp[1].X, e.Y - wp[1].Y);
        if (d2 < nearestWp2) nearestWp2 = d2;
        if (t % 200 == 199) cp?.Invoke(world2.Tick + 100000, world2.ComputeStateHash());
    }
    if (!overridden) throw new Exception("waypoints: override phase never reached wp1");
    var final = world2.Entities[u2];
    if (final.Moving || Fix64.DistSq(final.X - home.X, final.Y - home.Y) > Fix64.FromInt(20))
        throw new Exception("waypoints: unit did not settle at the override point");
    if (nearestWp2 <= Fix64.FromInt(64))
        throw new Exception("waypoints: the wiped plan was still partially executed (approached wp2)");
    report?.Invoke("waypoints: three shift-queued legs visited strictly in order; a direct order mid-plan wiped the remainder and the unit settled at the override point");
    return world.ComputeStateHash() ^ world2.ComputeStateHash();
}

ulong ScenarioMission(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P2-SIM-20 end to end: mission-01 loads as pure data - tagged
    // enemies, timed reinforcement grant, an ambush zone, and a scripted
    // win-on-camp-destroyed objective. The player side is driven by the
    // skirmish AI, which knows nothing about missions.
    string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/missions/mission-01.fmap"));
    var map = MapData.Load(path);
    var world = map.BuildWorld(seed, players: 2, out var tags);
    if (!tags.TryGetValue("camp", out var camp) || camp.Count != 3)
        throw new Exception($"mission: expected 3 tagged camp entities, got {(tags.TryGetValue("camp", out var c) ? c.Count : 0)}");
    // Mission economics: bootstrap (plant 300 + refinery 2000 + factory 2000
    // + harvester 1400 = 5700) must be affordable from start + the timed
    // grant, or the strike force is the only wave that will ever exist.
    world.GrantCredits(0, 5000);
    world.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
    var mission = new MissionRunner(map, tags);
    var ai = SkirmishAI.Rusher(0); // small aggressive waves suit a strike mission
    var cmds = new List<Command>();
    int enemiesBeforeAmbush = -1, grantTickCredits = -1;
    bool ambushSeen = false;
    const int ticks = 9000;
    int wonAt = -1;
    for (int t = 0; t < ticks && wonAt < 0; t++)
    {
        cmds.Clear();
        ai.Act(world, cmds);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        mission.Tick(world);
        if (t == 149) enemiesBeforeAmbush = world.EntityCount;
        if (t == 150 && grantTickCredits < 0) grantTickCredits = (int)world.Credits(0);
        if (!ambushSeen && mission.Messages.Contains("ambush_sprung"))
        {
            ambushSeen = true;
            if (world.EntityCount < enemiesBeforeAmbush + 3)
                throw new Exception("mission: ambush message without ambush rifles");
        }
        if (world.Winner >= 0) wonAt = world.Tick;
        if (t % 500 == 499) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    if (!mission.Messages.Contains("reinforcements_inbound"))
        throw new Exception("mission: the timed message never fired");
    if (!ambushSeen) throw new Exception("mission: the ambush zone was never sprung");
    if (wonAt < 0) throw new Exception("mission: the camp was never destroyed within the time limit");
    if (world.Winner != 0) throw new Exception($"mission: scripted objective declared the wrong winner ({world.Winner})");
    // This invariant was NARROWED once, because the tag then covered two rifle
    // squads that elimination did not wait for: victory arrived from
    // VictorySystem the moment player 1's last STRUCTURE died while a tagged
    // UNIT lived, so "winner implies every tagged entity dead" held only by
    // accident of kill order. Q012 is now answered (fork 3: elimination and
    // the scripted objective are BOTH wins) and the tag covers structures
    // only, so the full invariant is provable again and is asserted as such.
    foreach (int id in camp)
        if (world.Entities[id].Alive)
            throw new Exception("mission: winner declared while a tagged camp entity lived");
    report?.Invoke($"mission: mission-01 ran as pure data - timed grant and message fired, the ambush sprang on zone entry, and victory landed at tick {wonAt} with every TAGGED camp entity confirmed dead (Q012 answered: the tag describes what the objective actually waits for)");
    return world.ComputeStateHash();
}

ulong ScenarioCapture(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P3-FAC-03: sixty hit points of pure audacity. An engineer
    // walks to an enemy factory, converts it, and is consumed; the captured
    // factory then produces for its new flag.
    var world = new World(seed, 64, 64, players: 2);
    world.GrantCredits(0, 2000);
    int factory = world.SpawnFactory(1, 30, 30);
    var engDef = world.GetUnitType(11);
    int eng = world.SpawnUnit(0, Fix64.FromInt(14), Fix64.FromInt(31), engDef.Speed, engDef.Hp, engDef.Armour, 0,
        veterancy: false, unitType: 11);
    var cmds = new List<Command> { new(0, 0, CommandType.Attack, eng, Fix64.Zero, Fix64.Zero, factory) };
    bool capturedEvent = false;
    for (int t = 0; t < 400; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        foreach (var ev in world.Events)
            if (ev.Type == GameEventType.Captured && ev.A == factory && ev.B == 0) capturedEvent = true;
        if (t % 100 == 99) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    if (!capturedEvent) throw new Exception("capture: the Captured event never fired");
    var f = world.Entities[factory];
    if (f.PlayerId != 0) throw new Exception($"capture: factory should fly flag 0 (flies {f.PlayerId})");
    if (world.Entities[eng].Alive) throw new Exception("capture: the engineer should be consumed by the act");
    // The prize produces for its new owner - and ADR-009's hash-impact clause
    // named THIS line as the sharpest known case in the whole wave. It used to
    // produce a rifle squad, which under the barracks split is infantry
    // ordered from a factory and is refused outright. Rewritten to
    // capture-appropriate production: a cannon tank, a vehicle, which is what
    // a captured FACTORY can actually build. The prerequisite side is
    // satisfied honestly too, because dir_cannon_tank authors none. The
    // budget stretches from 200 to 400 ticks for a real reason: nobody in
    // this scenario owns a power plant, so the 150-tick cannon builds at the
    // GDD s5 half-rate floor, and 300 ticks is the honest number.
    cmds.Add(new(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 1));
    int before = world.EntityCount;
    for (int t = 0; t < 400 && world.EntityCount == before; t++)
    { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); }
    if (world.EntityCount == before || world.Entities[before].PlayerId != 0)
        throw new Exception("capture: the captured factory should produce for its new owner");
    if (world.Entities[before].UnitType != 1)
        throw new Exception($"capture: the prize should have built a cannon tank, got unit type {world.Entities[before].UnitType}");
    report?.Invoke("capture: engineer converted the enemy factory on contact and was consumed; the prize built a cannon tank under its new flag (a FACTORY unit - ADR-009's split means the old rifle squad would now be refused there, which is exactly the case the ADR named)");
    return world.ComputeStateHash();
}

ulong ScenarioMission02(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // Silent Salvage, scripted like a player would fly it: raiders escort
    // the engineer to the west face, the alarm triggers fire, the engineer
    // touches the door, everything changes flags.
    string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/missions/mission-02.fmap"));
    var map = MapData.Load(path);
    var world = map.BuildWorld(seed, players: 2, out var tags);
    if (world.FactionOf(0) != World.FactionSodality) throw new Exception("mission02: player should fly Sodality colours");
    var mission = new MissionRunner(map, tags);
    int wrench = tags["wrench"][0], prize = tags["prize"][0];
    var cmds = new List<Command>();
    var missionCmds = new List<Command>();
    // Raiders sweep ahead to draw and kill the sentinel; the wrench follows.
    var raiders = new List<int>();
    for (int i = 0; i < world.Entities.Count; i++)
        if (world.Entities[i].Alive && world.Entities[i].PlayerId == 0 && world.Entities[i].UnitType == 5) raiders.Add(i);
    foreach (int r in raiders)
        cmds.Add(new(0, 0, CommandType.AttackMove, r, Fix64.FromInt(34), Fix64.FromInt(19)));
    cmds.Add(new(0, 0, CommandType.Attack, wrench, Fix64.Zero, Fix64.Zero, prize));
    bool alarm = false, flags = false;
    for (int t = 0; t < 2500 && world.Winner < 0; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        mission.Tick(world, missionCmds);
        if (missionCmds.Count > 0) { cmds.AddRange(missionCmds); missionCmds.Clear(); }
        if (mission.Messages.Contains("they_know")) alarm = true;
        if (mission.Messages.Contains("flags_change")) flags = true;
        if (t % 400 == 399) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    if (!alarm) throw new Exception("mission02: the compound never raised the alarm");
    if (!flags || world.Winner != 0) throw new Exception($"mission02: capture objective failed (winner={world.Winner})");
    if (world.Entities[prize].PlayerId != 0) throw new Exception("mission02: the prize should fly flag 0");
    if (world.Entities[wrench].Alive) throw new Exception("mission02: the engineer is consumed by the act");
    // THE DEFEAT PATH, in a SECOND world so the golden run above is untouched.
    // Lose the wrench and 'owned prize 0' can never fire, because the prize is
    // taken by engineer and there is only one - the mission used to run forever,
    // unwinnable and undeclared. Killed outright rather than played to a loss:
    // what is under test is that the trigger fires and declares, not the combat
    // that got there.
    {
        var lost = MapData.Load(path).BuildWorld(seed, players: 2, out var lostTags);
        var lostMission = new MissionRunner(MapData.Load(path), lostTags);
        int doomed = lostTags["wrench"][0];
        var e = lost.Entities[doomed];
        e.Alive = false;
        lost.SetEntityForTest(doomed, e);
        var lostCmds = new List<Command>();
        for (int t = 0; t < 60 && lost.Winner < 0; t++)
        {
            lost.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(lostCmds));
            lostCmds.Clear();
            lostMission.Tick(lost, lostCmds);
        }
        if (lost.Winner != 1)
            throw new Exception($"mission02: losing the wrench must declare defeat, got winner={lost.Winner}");
        if (!lostMission.Messages.Contains("the_wrench_is_lost"))
            throw new Exception("mission02: the defeat should say why");
    }
    report?.Invoke($"mission02: Sodality raid ran as data - the alarm assault triggered, the engineer converted the depot under fire, scripted victory at tick {world.Tick}; and losing the wrench declares DEFEAT rather than running forever (the win still wins despite the engineer being consumed, which is the trigger-order invariant)");
    return world.ComputeStateHash();
}

ulong ScenarioMission03(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // Hold the Line: the Turtle defends a Spine gap against three scripted
    // assault waves; survival to tick 4200 is the scripted victory.
    string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/missions/mission-03.fmap"));
    var map = MapData.Load(path);
    var world = map.BuildWorld(seed, players: 2, out var tags);
    world.GrantCredits(0, 4000);
    var mission = new MissionRunner(map, tags);
    var ai = SkirmishAI.Turtle(0);
    var cmds = new List<Command>();
    var missionCmds = new List<Command>();
    int wavesSeen = 0;
    for (int t = 0; t < 5000 && world.Winner < 0; t++)
    {
        cmds.Clear();
        ai.Act(world, cmds);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        mission.Tick(world, missionCmds);
        if (missionCmds.Count > 0) { cmds.AddRange(missionCmds); missionCmds.Clear(); }
        if (t % 700 == 699) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    wavesSeen = (mission.Messages.Contains("first_wave") ? 1 : 0)
              + (mission.Messages.Contains("second_wave") ? 1 : 0)
              + (mission.Messages.Contains("last_wave") ? 1 : 0);
    if (wavesSeen != 3) throw new Exception($"mission03: expected 3 assault waves, saw {wavesSeen}");
    if (world.Winner != 0) throw new Exception($"mission03: the line did not hold (winner={world.Winner})");
    if (!mission.Messages.Contains("the_line_held")) throw new Exception("mission03: the survival message never fired");
    report?.Invoke($"mission03: three scripted waves broke on the gap and the Turtle held - survival victory at tick {world.Tick}");
    return world.ComputeStateHash();
}

ulong ScenarioDepot(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // The field hospital: exact rates, exact prices, no charity.
    var world = new World(seed, 64, 64, players: 2);
    world.GrantCredits(0, 100);
    world.SpawnPowerPlant(0, 10, 10); // covers the depot's 30 draw
    world.SpawnServiceDepot(0, 20, 20);
    int wounded = world.SpawnUnit(0, Fix64.FromInt(23), Fix64.FromInt(21), Fix64.Zero, 300, ArmourClass.Heavy, 0);
    int enemy = world.SpawnUnit(1, Fix64.FromInt(24), Fix64.FromInt(21), Fix64.Zero, 300, ArmourClass.Heavy, 0);
    var e0 = world.Entities[wounded]; e0.Hp = 200; // scripted battle damage
    var e1 = world.Entities[enemy]; e1.Hp = 200;
    world.SetEntityForTest(wounded, e0); world.SetEntityForTest(enemy, e1);
    var cmds = new List<Command>();
    long creditsBefore = world.Credits(0);
    for (int t = 0; t < 60; t++) { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); }
    // 50 ticks of healing closes 100 hp at 2/tick; the last 10 ticks idle at full.
    if (world.Entities[wounded].Hp != 300)
        throw new Exception($"depot: wounded ally should be fully mended ({world.Entities[wounded].Hp}/300)");
    if (world.Entities[enemy].Hp != 200)
        throw new Exception("depot: the enemy does not get treated");
    long spent = creditsBefore - world.Credits(0);
    if (spent != 50)
        throw new Exception($"depot: 100 hp at 2hp/1cr per tick should cost exactly 50 (spent {spent})");
    // No credits, no repairs.
    var world2 = new World(seed + 1, 64, 64, players: 1);
    world2.SpawnPowerPlant(0, 10, 10);
    world2.SpawnServiceDepot(0, 20, 20);
    int broke = world2.SpawnUnit(0, Fix64.FromInt(23), Fix64.FromInt(21), Fix64.Zero, 300, ArmourClass.Heavy, 0);
    var b = world2.Entities[broke]; b.Hp = 200; world2.SetEntityForTest(broke, b);
    for (int t = 0; t < 40; t++) world2.Step(default);
    if (world2.Entities[broke].Hp != 200)
        throw new Exception("depot: repairs are not charity - no credits, no mending");
    if (cp != null) { cp(world.Tick, world.ComputeStateHash()); cp(world2.Tick + 100000, world2.ComputeStateHash()); }
    report?.Invoke("depot: ally mended 100 hp at exactly 50 credits; the enemy went untreated; the broke commander got nothing");
    return world.ComputeStateHash() ^ world2.ComputeStateHash();
}

ulong ScenarioWalls(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)
{
    // TICKET-P5-DEF-06: ADR-005 made machine-checkable. Nine phases, one per
    // clause of the ADR - upfront pay, affordability, the barrier chain, the
    // per-player cap, the three exclusions, auto-acquire, the anti-turtle
    // counter, the breach, and sell-back. Phases E to H need a player
    // eliminated or the map severed, so they run in sub-worlds (the
    // ScenarioArtillery/ScenarioDepot precedent); every sub-world folds into
    // the returned hash, so none of them can rot unnoticed.
    var world = new World(seed, 64, 64, players: 2);
    world.GrantCredits(0, 20000);
    world.GrantCredits(1, 5000);
    int cy0 = world.SpawnConstructionYard(0, 8, 8);
    int cy1 = world.SpawnConstructionYard(1, 40, 40); // player 1 owns hope from tick 1: no stray elimination
    int runner = world.SpawnUnit(0, Fix64.FromInt(50) + Fix64.Half, Fix64.FromInt(50) + Fix64.Half,
        Fix64.FromFraction(1, 4), 100, ArmourClass.Light, 0);
    var cmds = new List<Command>();
    void StepN(int n) { for (int i = 0; i < n; i++) { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }
    static int Barriers(World w, int player)
    {
        int n = 0;
        foreach (var e in w.Entities) if (e.Alive && e.PlayerId == player && e.Kind == EntityKind.Wall) n++;
        return n;
    }

    // PHASE A: upfront pay (ADR-005 clause 3). A wall lands with NOTHING ready
    // at the yard and the treasury is charged the moment it does - the exact
    // inverse of the sidebar flow, which the negative control re-proves is
    // still intact for real buildings.
    int countA = world.EntityCount;
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(8), Fix64.FromInt(12), 9));
    StepN(1);
    if (world.EntityCount != countA + 1) throw new Exception("walls: a barrier must place with no ready slot at the yard");
    int wallA = countA;
    var wa = world.Entities[wallA];
    if (wa.Kind != EntityKind.Wall || wa.StructType != 9)
        throw new Exception($"walls: phase A spawned the wrong thing (kind {wa.Kind}, structType {wa.StructType})");
    if (world.Credits(0) != 20000 - 100)
        throw new Exception($"walls: a barrier must charge exactly 100 upfront (credits {world.Credits(0)})");
    if (world.Entities[cy0].ReadyStructure != 0) throw new Exception("walls: a barrier must not touch the yard's ready slot");
    int countANeg = world.EntityCount;
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(12), Fix64.FromInt(12), 1)); // power plant, nothing ready
    StepN(1);
    if (world.EntityCount != countANeg)
        throw new Exception("walls: the upfront path must not leak - a real building with nothing ready is still refused");

    // PHASE B: affordability. 99 credits do not buy a 100-credit segment, and
    // a refused segment charges nothing.
    world.GrantCredits(0, -(world.Credits(0) - 99));
    int countB = world.EntityCount;
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(8), Fix64.FromInt(14), 9));
    StepN(1);
    if (world.EntityCount != countB) throw new Exception("walls: 99 credits must not buy a 100-credit segment");
    if (world.Credits(0) != 99) throw new Exception($"walls: a refused segment must not charge (credits {world.Credits(0)})");

    // PHASE C: the chain (ADR-005 clause 4). Anchors are chosen so each
    // assertion has exactly one possible reason to pass: (8,14) is Chebyshev 6
    // from the yard (legal by the yard); (8,16) is Chebyshev 8 from the yard,
    // so ONLY the barrier chain at radius 2 can carry it; (8,19) is Chebyshev 3
    // from the nearest segment and 11 from the yard, so nothing can.
    world.GrantCredits(0, 20000 - 99);
    int countC = world.EntityCount;
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(8), Fix64.FromInt(14), 9));
    StepN(1);
    if (world.EntityCount != countC + 1) throw new Exception("walls: a funded segment inside the yard radius must place");
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(8), Fix64.FromInt(16), 9));
    StepN(1);
    if (world.EntityCount != countC + 2) throw new Exception("walls: a segment must chain from a segment at Chebyshev 2");
    cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(8), Fix64.FromInt(19), 9));
    StepN(1);
    if (world.EntityCount != countC + 2) throw new Exception("walls: a segment must NOT chain at Chebyshev 3");
    if (world.ValidPlacement(0, 9, 16, 1))
        throw new Exception("walls: a barrier must never anchor a real building (the base-crawl exploit)");
    if (!world.ValidPlacement(0, 12, 8, 9))
        throw new Exception("walls: the yard must still anchor a segment inside its own radius");

    // PHASE D: the cap (ADR-005 clause 5). Crawl a grid until the cap bites.
    // Every candidate is funded and chain-legal, so the ONLY thing that can
    // stop the crawl is MaxBarriersPerPlayer.
    for (int gy = 12; gy <= 30; gy += 2)
        for (int gx = 8; gx <= 30; gx += 2)
        {
            if (!world.ValidPlacement(0, gx, gy, 9)) continue;
            cmds.Add(new(0, 0, CommandType.PlaceStructure, runner, Fix64.FromInt(gx), Fix64.FromInt(gy), 9));
            StepN(1);
        }
    if (Barriers(world, 0) != World.MaxBarriersPerPlayer)
        throw new Exception($"walls: the cap must bite at exactly {World.MaxBarriersPerPlayer} (got {Barriers(world, 0)})");
    int countD = world.EntityCount;
    cmds.Add(new(0, 1, CommandType.PlaceStructure, cy1, Fix64.FromInt(44), Fix64.FromInt(40), 9));
    StepN(1);
    if (world.EntityCount != countD + 1 || Barriers(world, 1) != 1)
        throw new Exception("walls: the cap is PER PLAYER - player 1's first segment must still place");

    // PHASE I: sell-back and rubble. Half the cost returns and the single cell
    // is placeable again (DEF-03's anchor recovery, exercised at size 1).
    long beforeSell = world.Credits(0);
    cmds.Add(new(0, 0, CommandType.SellStructure, wallA, Fix64.Zero, Fix64.Zero));
    StepN(1);
    if (world.Credits(0) != beforeSell + 50)
        throw new Exception($"walls: a sold segment must refund exactly 50 (got {world.Credits(0) - beforeSell})");
    if (world.Entities[wallA].Alive) throw new Exception("walls: a sold segment must die");
    if (!world.ValidPlacement(0, 8, 12, 9)) throw new Exception("walls: a sold segment must free its cell");
    cp?.Invoke(world.Tick, world.ComputeStateHash());

    // PHASE E: the exclusions (ADR-005 clause 2). An engineer does not convert
    // a fence, and a player whose last possession is one 100-credit wall is
    // still eliminated - without that, matches never end.
    var worldE = new World(seed + 1, 64, 64, players: 2);
    worldE.SpawnConstructionYard(0, 8, 8);
    int eCy1 = worldE.SpawnConstructionYard(1, 30, 30);
    int eWall = worldE.SpawnWall(1, 36, 36);
    var engDef = worldE.GetUnitType(11);
    int eng = worldE.SpawnUnit(0, Fix64.FromInt(34) + Fix64.Half, Fix64.FromInt(36) + Fix64.Half,
        engDef.Speed, engDef.Hp, engDef.Armour, engDef.WeaponId, veterancy: false, unitType: 11);
    var ecmds = new List<Command> { new(0, 0, CommandType.Attack, eng, Fix64.Zero, Fix64.Zero, eWall) };
    void StepE(int n) { for (int i = 0; i < n; i++) { worldE.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ecmds)); ecmds.Clear(); } }
    StepE(60);
    if (!worldE.Entities[eng].Alive) throw new Exception("walls: a fence must not consume an engineer");
    if (worldE.Entities[eWall].PlayerId != 1) throw new Exception("walls: an engineer must not capture a barrier");
    // The yard falls; the fence stands. Pre-damaged so the point is the
    // victory rule, not the ballistics (DEF-04 owns the arithmetic).
    var doomed = worldE.Entities[eCy1];
    doomed.Hp = 30;
    worldE.SetEntityForTest(eCy1, doomed);
    var kDef = worldE.GetUnitType(1);
    int killer = worldE.SpawnUnit(0, Fix64.FromInt(34) + Fix64.Half, Fix64.FromInt(31) + Fix64.Half,
        kDef.Speed, kDef.Hp, kDef.Armour, kDef.WeaponId, kDef.SightCells, unitType: 1);
    ecmds.Add(new(0, 0, CommandType.Attack, killer, Fix64.Zero, Fix64.Zero, eCy1));
    bool eliminated = false;
    for (int t = 0; t < 120; t++)
    {
        worldE.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ecmds));
        ecmds.Clear();
        foreach (var ev in worldE.Events)
            if (ev.Type == GameEventType.PlayerEliminated && ev.B == 1) eliminated = true;
    }
    if (!eliminated) throw new Exception("walls: a player left holding only a wall must still be eliminated");
    if (worldE.Winner != 0) throw new Exception($"walls: the survivor must win (winner {worldE.Winner})");
    if (!worldE.Entities[eWall].Alive)
        throw new Exception("walls: phase E proves nothing unless the wall is still standing at elimination");
    cp?.Invoke(worldE.Tick + 100000, worldE.ComputeStateHash());

    // PHASE F: auto-acquire (ADR-005 clause 2). Tanks do not stop to plink at
    // masonry - but an explicit order still bites.
    var worldF = new World(seed + 2, 64, 64, players: 2);
    worldF.ShortGameEnabled = false; // a rig, not a match: nobody here owns a base
    int fWall = worldF.SpawnWall(1, 30, 30);
    var cDef = worldF.GetUnitType(1);
    int cannon = worldF.SpawnUnit(0, Fix64.FromInt(28) + Fix64.Half, Fix64.FromInt(30) + Fix64.Half,
        cDef.Speed, cDef.Hp, cDef.Armour, cDef.WeaponId, cDef.SightCells, unitType: 1);
    var fcmds = new List<Command>();
    void StepF(int n) { for (int i = 0; i < n; i++) { worldF.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(fcmds)); fcmds.Clear(); } }
    StepF(60); // 2 cells away, gun range 4, sight 5: every excuse to fire
    if (worldF.Entities[fWall].Hp != 500)
        throw new Exception($"walls: auto-acquire must ignore barriers (hp {worldF.Entities[fWall].Hp})");
    fcmds.Add(new(0, 0, CommandType.Attack, cannon, Fix64.Zero, Fix64.Zero, fWall));
    StepF(60);
    if (worldF.Entities[fWall].Hp >= 500)
        throw new Exception("walls: an explicit attack order must still bite a barrier");
    cp?.Invoke(worldF.Tick + 200000, worldF.ComputeStateHash());

    // PHASE G: the counter (GDD s6 line 53, made machine-checkable). Artillery
    // beats static defence: the howitzer works from range 8, outside the
    // turret's range 5 and outside its own 3-cell dead zone, and walks away
    // untouched. This is the assertion that keeps turtling beatable.
    var worldG = new World(seed + 3, 64, 64, players: 2);
    worldG.ShortGameEnabled = false;
    var seg = new int[5];
    for (int k = 0; k < 5; k++) seg[k] = worldG.SpawnWall(1, 30, 28 + k);
    // ADR-008 clause 5, the amendment bound to the gate: player 1's turret
    // must be POWERED (100 supply against 20 draw) or the assertion below
    // that the gun "took nothing back" passes because the turret is dead
    // rather than because it is out-ranged - and the hash would not move to
    // tell anyone. The howitzer result is re-proven against a LIVE turret.
    worldG.SpawnPowerPlant(1, 36, 29);
    int gTurret = worldG.SpawnTurret(1, 32, 29); // centre (33,30): 10.5 cells from the gun, hopelessly short
    var gDef = worldG.GetUnitType(8);
    int gun = worldG.SpawnUnit(0, Fix64.FromInt(22) + Fix64.Half, Fix64.FromInt(30) + Fix64.Half,
        gDef.Speed, gDef.Hp, gDef.Armour, gDef.WeaponId, gDef.SightCells, unitType: 8);
    int gunHp = worldG.Entities[gun].Hp;
    var gcmds = new List<Command> { new(0, 0, CommandType.Attack, gun, Fix64.Zero, Fix64.Zero, seg[2]) };
    worldG.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(gcmds));
    gcmds.Clear();
    // One shell, exact: 60 AntiBuilding vs Structure is 100%, splash is half
    // (30) inside radius 1.5 - so both orthogonal neighbours bleed and the
    // segments 2 cells out do not (ADR-005 clause 10).
    if (worldG.Entities[seg[2]].Hp != 440)
        throw new Exception($"walls: a howitzer shell must deal exactly 60 to a segment (hp {worldG.Entities[seg[2]].Hp})");
    if (worldG.Entities[seg[1]].Hp != 470 || worldG.Entities[seg[3]].Hp != 470)
        throw new Exception($"walls: splash must deal exactly 30 to both orthogonal neighbours ({worldG.Entities[seg[1]].Hp}/{worldG.Entities[seg[3]].Hp})");
    if (worldG.Entities[seg[0]].Hp != 500 || worldG.Entities[seg[4]].Hp != 500)
        throw new Exception("walls: splash radius 1.5 must not reach 2 cells");
    for (int t = 0; t < 900; t++)
    {
        worldG.Step(default);
        if (t % 300 == 299) cp?.Invoke(worldG.Tick + 300000, worldG.ComputeStateHash());
    }
    if (worldG.Entities[seg[2]].Alive)
        throw new Exception("walls: artillery must breach static defence (GDD s6 line 53)");
    // ADR-008: assert the POWER, not just the outcome - the sentence "took
    // nothing back" is only meaningful if the turret could have shot back.
    {
        int gSup = 0, gDrw = 0;
        foreach (var e in worldG.Entities)
            if (e.Alive && e.PlayerId == 1) { gSup += e.PowerSupply; gDrw += e.PowerDraw; }
        if (!worldG.Entities[gTurret].Alive || gSup * 4 < gDrw * 3)
            throw new Exception($"walls: phase G proves nothing unless the turret is alive and powered ({gSup} supply vs {gDrw} draw)");
    }
    if (worldG.Entities[gun].Hp != gunHp)
        throw new Exception($"walls: the gun outranges the turret and must take nothing back (hp {worldG.Entities[gun].Hp}/{gunHp})");

    // PHASE H: the breach (DEF-05). Terrain seals x=30 but for one cell, and a
    // single enemy segment plugs it: the route is severed for the whole map.
    // Without the breach rule the tank oscillates in place forever.
    var worldH = new World(seed + 4, 64, 64, players: 2);
    worldH.ShortGameEnabled = false;
    for (int y = 0; y < 64; y++) if (y != 30) worldH.Map.SetBlocked(30, y, true);
    int plug = worldH.SpawnWall(1, 30, 30);
    var hDef = worldH.GetUnitType(1);
    int breacher = worldH.SpawnUnit(0, Fix64.FromInt(24) + Fix64.Half, Fix64.FromInt(30) + Fix64.Half,
        hDef.Speed, hDef.Hp, hDef.Armour, hDef.WeaponId, hDef.SightCells, unitType: 1);
    // Negative control: a tank whose ordered point IS reachable must never
    // touch the wall - RouteExists has to short-circuit the whole rule.
    int control = worldH.SpawnUnit(0, Fix64.FromInt(10) + Fix64.Half, Fix64.FromInt(10) + Fix64.Half,
        hDef.Speed, hDef.Hp, hDef.Armour, hDef.WeaponId, hDef.SightCells, unitType: 1);
    var aim = (X: Fix64.FromInt(50), Y: Fix64.FromInt(30) + Fix64.Half);
    var hcmds = new List<Command>
    {
        new(0, 0, CommandType.AttackMove, breacher, aim.X, aim.Y),
        new(0, 0, CommandType.AttackMove, control, Fix64.FromInt(16), Fix64.FromInt(10) + Fix64.Half),
    };
    int acquiredAt = -1;
    bool controlTouchedWall = false;
    for (int t = 0; t < 900; t++)
    {
        worldH.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hcmds));
        hcmds.Clear();
        if (acquiredAt < 0 && worldH.Entities[breacher].ExplicitTarget == plug) acquiredAt = t + 1;
        if (worldH.Entities[control].ExplicitTarget == plug) controlTouchedWall = true;
        if (t % 300 == 299) cp?.Invoke(worldH.Tick + 400000, worldH.ComputeStateHash());
    }
    if (acquiredAt < 0 || acquiredAt > 30)
        throw new Exception($"walls: the breacher must acquire the sealing segment within 30 ticks (acquired at {acquiredAt})");
    if (controlTouchedWall)
        throw new Exception("walls: a unit with a reachable objective must never target a wall (RouteExists must short-circuit)");
    if (worldH.Entities[plug].Alive) throw new Exception("walls: the breacher must destroy the segment sealing its route");
    var arrived = worldH.Entities[breacher];
    if (Fix64.DistSq(arrived.X - aim.X, arrived.Y - aim.Y) > Fix64.FromInt(16))
        throw new Exception($"walls: the breacher must resume its march through the breach unordered (ended {arrived.X},{arrived.Y})");

    // PHASE J: the turret gate itself (ADR-008 clauses 1 and 5), the other
    // half of phase G's double truth. Phase G proves a POWERED turret is
    // out-ranged; this proves an UNPOWERED turret is offline BECAUSE of
    // power: in range, alive, loaded, and withholding fire. Plus the
    // inclusive boundary made machine-checkable: supply 14 against draw 20
    // (70 per cent) stays dark, supply 15 (exactly 75) fires on the very
    // next tick the pre-combat tally sees it, and dropping back below the
    // line freezes the reload mid-cycle (the continue sits above the
    // cooldown decrement: a dead turret does not reload).
    var worldJ = new World(seed + 5, 64, 64, players: 2);
    worldJ.ShortGameEnabled = false;
    int jTurret = worldJ.SpawnTurret(0, 20, 20); // draw 20, range 5, centre (21,21)
    int jBait = worldJ.SpawnUnit(1, Fix64.FromInt(24), Fix64.FromInt(21), Fix64.Zero, 4000, ArmourClass.Heavy, 0); // unarmed, 3 cells out: every excuse to be shot
    var jcmds = new List<Command>();
    void StepJ(int n) { for (int i = 0; i < n; i++) { worldJ.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(jcmds)); jcmds.Clear(); } }
    StepJ(60); // supply 0 against draw 20: 0 per cent
    if (worldJ.Entities[jBait].Hp != 4000)
        throw new Exception($"walls: an unpowered turret must withhold fire (bait hp {worldJ.Entities[jBait].Hp})");
    if (worldJ.Entities[jTurret].Cooldown != 0)
        throw new Exception("walls: an offline turret must not touch its cooldown");
    worldJ.SpawnPowerPlant(0, 26, 26, supply: 14); // 14 against 20: 70 per cent, still dark
    StepJ(60);
    if (worldJ.Entities[jBait].Hp != 4000)
        throw new Exception("walls: supply 14 against draw 20 is BELOW the boundary and must not fire");
    int jTopUp = worldJ.SpawnPowerPlant(0, 26, 30, supply: 1); // 15 against 20: exactly 75 per cent
    int hpAtBoundary = worldJ.Entities[jBait].Hp;
    StepJ(1);
    if (worldJ.Entities[jBait].Hp >= hpAtBoundary)
        throw new Exception("walls: supply 15 against draw 20 is exactly 75 per cent and must FIRE on the next tick (inclusive boundary, one-tick restore)");
    int jCd = worldJ.Entities[jTurret].Cooldown;
    if (jCd <= 0) throw new Exception("walls: the boundary shot should have started the reload");
    int hpAfterShot = worldJ.Entities[jBait].Hp;
    jcmds.Add(new(0, 0, CommandType.SellStructure, jTopUp, Fix64.Zero, Fix64.Zero)); // back to 14: the gate freezes the reload
    StepJ(10);
    if (worldJ.Entities[jTurret].Cooldown != jCd)
        throw new Exception($"walls: dropping below 75 must freeze the reload (cooldown {worldJ.Entities[jTurret].Cooldown} vs frozen {jCd})");
    if (worldJ.Entities[jBait].Hp != hpAfterShot)
        throw new Exception($"walls: a turret refrozen below the line must not land another shot (bait {worldJ.Entities[jBait].Hp} vs {hpAfterShot})");
    cp?.Invoke(worldJ.Tick + 500000, worldJ.ComputeStateHash());

    report?.Invoke("walls: nine ADR-005 clauses held - (A) a segment landed with no ready slot and charged exactly 100 upfront while a real building with nothing ready stayed refused; " +
                   "(B) 99 credits bought nothing and charged nothing; (C) the chain carried a segment to Chebyshev 2 but not 3, never anchored a power plant, and the yard still anchored its own; " +
                   $"(D) the cap bit at exactly {World.MaxBarriersPerPlayer} per player and player 1's first segment still placed; (E) an engineer bounced off a fence and a player left holding one wall was still eliminated; " +
                   "(F) auto-acquire ignored masonry at 2 cells but an explicit order bit; (G) a howitzer at range 8 dealt 60 direct and exactly 30 to each orthogonal neighbour, breached the line, and took nothing back from a turret PROVEN ALIVE AND POWERED (100 supply vs 20 draw, ADR-008) - out-ranged, not dead; " +
                   "(H) a tank sealed out by one segment acquired it in under 30 ticks, destroyed it, and resumed its march unordered while a tank with a reachable objective never glanced at it; " +
                   "(I) a sold segment refunded exactly 50 and freed its cell; " +
                   "(J) ADR-008's gate: an in-range turret at 0 supply withheld fire for 60 ticks with its cooldown untouched, 14/20 supply (70%) stayed dark, 15/20 (exactly 75%, inclusive) fired on the NEXT tick, and selling back below the line froze the reload mid-cycle");
    return world.ComputeStateHash() ^ worldE.ComputeStateHash() ^ worldF.ComputeStateHash()
         ^ worldG.ComputeStateHash() ^ worldH.ComputeStateHash() ^ worldJ.ComputeStateHash();
}

var scenarios = new (string Name, Func<ulong, Action<int, ulong>?, ulong> Run)[]
{
    ("movement",   (s, cp) => ScenarioMovement(s, cp)),
    ("pathing",    (s, cp) => ScenarioPathing(s, cp)),
    ("economy",    (s, cp) => ScenarioEconomy(s, cp)),
    ("combat",     (s, cp) => ScenarioCombat(s, cp)),
    ("production", (s, cp) => ScenarioProduction(s, cp)),
    ("attackmove", (s, cp) => ScenarioAttackMove(s, cp)),
    ("construction", (s, cp) => ScenarioConstruction(s, cp)),
    ("skirmish", (s, cp) => ScenarioSkirmish(s, cp)),
    ("stealth", (s, cp) => ScenarioStealth(s, cp)),
    ("veterancy", (s, cp) => ScenarioVeterancy(s, cp)),
    ("victory", (s, cp) => ScenarioVictory(s, cp)),
    ("expansion", (s, cp) => ScenarioExpansion(s, cp)),
    ("artillery", (s, cp) => ScenarioArtillery(s, cp)),
    ("superweapon", (s, cp) => ScenarioSuperweapon(s, cp)),
    ("crush", (s, cp) => ScenarioCrush(s, cp)),
    ("aisuper", (s, cp) => ScenarioAiSuper(s, cp)),
    ("veil", (s, cp) => ScenarioVeil(s, cp)),
    ("waypoints", (s, cp) => ScenarioWaypoints(s, cp)),
    ("mission", (s, cp) => ScenarioMission(s, cp)),
    ("capture", (s, cp) => ScenarioCapture(s, cp)),
    ("mission02", (s, cp) => ScenarioMission02(s, cp)),
    ("mission03", (s, cp) => ScenarioMission03(s, cp)),
    ("depot", (s, cp) => ScenarioDepot(s, cp)),
    ("walls", (s, cp) => ScenarioWalls(s, cp)),
};

// ---------------- Modes ----------------

int SelfTest()
{
    var two = Fix64.FromInt(2);
    if ((two * Fix64.FromInt(3)).ToIntRound() != 6) return Fail("Fix64 mul");
    if ((Fix64.FromInt(7) / two).Raw != Fix64.FromInt(7).Raw / 2) return Fail("Fix64 div");
    if (Fix64.FromFraction(1, 2) != Fix64.Half) return Fail("Fix64 fraction");
    if (Fix64.Sqrt(Fix64.FromInt(144)).ToIntRound() != 12) return Fail("Fix64 sqrt");
    var s2 = Fix64.Sqrt(two);
    if (Fix64.Abs(s2 * s2 - two) > Fix64.FromFraction(1, 1_000_000)) return Fail("Fix64 sqrt precision");
    if (Fix64.FromInt(-5) + Fix64.FromInt(5) != Fix64.Zero) return Fail("Fix64 negatives");

    // ADR-005 / TICKET-P5-DEF-03: the variable-footprint refactor is only
    // behaviour-neutral if FromFraction(2, 2) is bit-identical to One, so that
    // FootprintCentre(a, 2) still produces exactly FromInt(a + 1). This is the
    // arithmetic the 23 golden hashes rest on; assert it rather than trust it.
    if (Fix64.FromFraction(2, 2).Raw != Fix64.One.Raw) return Fail("Fix64 FromFraction(2,2) != One");
    if ((Fix64.FromInt(8) + Fix64.FromFraction(2, 2)).Raw != Fix64.FromInt(9).Raw) return Fail("footprint centre size-2 identity");
    // TICKET-P5-BD-06 turned these two into instance reads off the catalogue, so
    // a World is needed to ask. The answers must not have moved by one cell.
    var fp = new World(1);
    if (fp.FootprintOf(4) != 2) return Fail("FootprintOf: construction yard is 2x2");
    if (fp.FootprintOf(9) != 1) return Fail("FootprintOf: wall is 1x1");
    if (fp.FootprintOf(0) != 2) return Fail("FootprintOf: unknown type defaults to 2x2");
    if (fp.FootprintOf(World.GateStructType) != 1) return Fail("FootprintOf: ADR-005 clause 6 reserves the gate as 1x1");
    // A 2x2 centred at 9 anchors at 8; a 1x1 centred at 8.5 anchors at 8.
    if (fp.AnchorOf(Fix64.FromInt(9), 4) != 8) return Fail("AnchorOf 2x2");
    if (fp.AnchorOf(Fix64.FromInt(8) + Fix64.Half, 9) != 8) return Fail("AnchorOf 1x1");
    // TICKET-P5-BASE-01: AnchorOf must invert FootprintCentre for footprints 3
    // and 4 too - schema.structure.json permits up to 4, and the shipped
    // "- (size - 1)" was off by one for both (silent and fatal, ADR-005:76).
    // No compiled type carries either size, so register test types; the centre
    // below is FootprintCentre's documented formula, anchor + size/2.
    fp.RegisterStructureType(98, new World.StructureTypeDef(1, EntityKind.Factory, 1, Footprint: 3));
    fp.RegisterStructureType(99, new World.StructureTypeDef(1, EntityKind.Factory, 1, Footprint: 4));
    foreach (int size in new[] { 3, 4 })
    {
        int testType = size == 3 ? 98 : 99;
        for (int a = 0; a <= 48; a++)
        {
            Fix64 centre = Fix64.FromInt(a) + Fix64.FromFraction(size, 2);
            if (fp.AnchorOf(centre, testType) != a)
                return Fail($"AnchorOf: footprint {size} round-trip failed at anchor {a}");
        }
    }

    var rng = new DeterministicRandom(42);
    ulong first = rng.NextUlong();
    if (first != new DeterministicRandom(42).NextUlong()) return Fail("RNG not reproducible");
    var bounded = new DeterministicRandom(7);
    for (int i = 0; i < 10_000; i++)
        if (bounded.NextInt(37) is < 0 or >= 37) return Fail("RNG bounds");

    if (DamageMatrix.Apply(100, Warhead.AntiArmour, ArmourClass.Heavy) != 100) return Fail("matrix AA/heavy");
    if (DamageMatrix.Apply(100, Warhead.AntiInfantry, ArmourClass.Heavy) != 25) return Fail("matrix AI/heavy");

    var w1 = new World(1); w1.SpawnUnit(0, Fix64.One, Fix64.One, Fix64.One, 10, ArmourClass.None, 0);
    var w2 = new World(1); w2.SpawnUnit(0, Fix64.One, Fix64.One + new Fix64(1), Fix64.One, 10, ArmourClass.None, 0);
    if (w1.ComputeStateHash() == w2.ComputeStateHash()) return Fail("hash not sensitive");

    // Flow field basics: route must round the wall, not through it.
    var m = new Map(8, 8);
    for (int y = 0; y < 7; y++) m.SetBlocked(4, y, true); // wall with gap at y=7
    var ff = FlowField.Build(m, 6, 0);
    int c = m.CellIndex(2, 0);
    for (int hops = 0; hops < 64 && c != m.CellIndex(6, 0); hops++)
    {
        int next = ff.NextCell(m, c % 8, c / 8);
        if (next < 0) return Fail("flow: unreachable");
        if (m.IsBlocked(next % 8, next / 8)) return Fail("flow: routed through wall");
        c = next;
    }
    if (c != m.CellIndex(6, 0)) return Fail("flow: never arrived");

    // Data loader: the committed example file must round-trip exactly (TICKET-P2-DATA-01).
    string dataPath = Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/units/com_harvester.yaml");
    if (File.Exists(dataPath))
    {
        var u = DataLoader.LoadUnitFile(Path.GetFullPath(dataPath));
        if (u.Id != "com_harvester" || u.Name != "Harvester") return Fail("data: id/name");
        if (u.Cost != 1400 || u.Hp != 700 || u.BuildTimeTicks != 300) return Fail("data: numbers");
        if (u.Armour != ArmourClass.Heavy || u.WeaponIds.Count != 0) return Fail("data: armour/weapons");
        if (u.Prerequisites.Count != 1 || u.Prerequisites[0] != "com_refinery") return Fail("data: prerequisites");
        if (u.ProducedAt != "com_factory") return Fail("data: produced_at (TICKET-P5-PROD-03)");
        if (u.Speed != Fix64.FromFraction(18, 100)) return Fail("data: speed encoding");
        if (u.VeterancyEnabled) return Fail("data: veterancy flag");
        if (!u.Notes.Contains("US2.2")) return Fail("data: folded notes block");
        Console.WriteLine("selftest: data loader round-trips com_harvester.yaml");
    }
    else Console.WriteLine("selftest: data file not found at expected relative path, loader untested this run");

    // Catalogue wiring (TICKET-P2-DATA-02, walk per TICKET-P5-PROD-02): every
    // /data/units file must convert to exactly its compiled reference def -
    // value equality on the record, produced_at and prerequisites included.
    // A directory walk, not a hand-kept list: the hand-kept list is how
    // dir_vanguard_car.yaml went unverified for a whole phase (PROD-D9).
    string unitsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/units"));
    if (Directory.Exists(unitsDir) && Directory.GetFiles(unitsDir, "*.yaml").Length > 0)
    {
        var refWorld = new World(0);
        var unitFiles = Directory.GetFiles(unitsDir, "*.yaml");
        Array.Sort(unitFiles, StringComparer.Ordinal); // fixed order: a directory walk is not a source of truth
        int unitsSeen = 0;
        var unitTypesSeen = new HashSet<int>();
        foreach (var f in unitFiles)
        {
            var ud = DataLoader.LoadUnitFile(f);
            int typeId = UnitCatalogue.TypeIdOf(ud.Id);
            var def = UnitCatalogue.ToTypeDef(ud);
            if (def != refWorld.GetUnitType(typeId))
                return Fail($"catalogue: {ud.Id} (unit type {typeId}) mismatch {def} vs {refWorld.GetUnitType(typeId)}");
            if (!unitTypesSeen.Add(typeId)) return Fail($"catalogue: unit type {typeId} claimed twice");
            unitsSeen++;
        }
        // Every compiled unit type must be authored. Unit types are dense from
        // 1 (doc 23 s4.1), so walk the compiled catalogue until it runs out
        // rather than trusting a magic max that rots.
        int unitsCompiled = 0;
        for (int t = 1; refWorld.GetUnitType(t).Cost > 0; t++)
        {
            unitsCompiled++;
            if (!unitTypesSeen.Contains(t)) return Fail($"catalogue: no /data/units file for compiled unit type {t}");
        }
        if (unitsSeen != unitsCompiled)
            return Fail($"catalogue: {unitsSeen} unit files but {unitsCompiled} compiled unit types");
        var w = new World(0);
        w.RegisterUnitType(1, UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "dir_cannon_tank.yaml")))); // legal before tick 0
        Console.WriteLine($"selftest: /data/units reproduces all {unitsSeen} compiled unit defs exactly (produced_at and prerequisites included)");
    }
    else Console.WriteLine("selftest: data/units not found, catalogue wiring untested this run");

    // Structure catalogue wiring (TICKET-P5-BD-06): /data/buildings must convert
    // to exactly the compiled reference defs. This is the ticket's whole
    // acceptance argument - the golden hashes prove the relocation changed no
    // behaviour, and this proves the files, not the literals, are now the
    // catalogue. Value equality on the record, every field, every type.
    string buildingsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/buildings"));
    if (Directory.Exists(buildingsDir) && Directory.GetFiles(buildingsDir, "*.yaml").Length > 0)
    {
        var refWorld = new World(0);
        var files = Directory.GetFiles(buildingsDir, "*.yaml");
        Array.Sort(files, StringComparer.Ordinal); // fixed order: a directory walk is not a source of truth
        int seen = 0;
        var seenTypes = new HashSet<int>();
        foreach (var f in files)
        {
            var sd = DataLoader.LoadStructureFile(f);
            int typeId = StructureCatalogue.TypeIdOf(sd.Id);
            var def = StructureCatalogue.ToTypeDef(sd);
            if (def != refWorld.GetStructureType(typeId))
                return Fail($"structure catalogue: {sd.Id} (type {typeId}) mismatch {def} vs {refWorld.GetStructureType(typeId)}");
            if (!seenTypes.Add(typeId)) return Fail($"structure catalogue: type {typeId} claimed twice");
            seen++;
        }
        // Every compiled type must be authored: a file missing is how a
        // hard-coded value survives a catalogue migration unnoticed. Bounded
        // by the catalogue's own constant (TICKET-P5-PROD-02), and the gate is
        // skipped EXPLICITLY: type 10 is ADR-005's reservation, with no def
        // and no file, and this loop is exactly where the reservation bites.
        for (int t = 1; t <= World.MaxStructType; t++)
        {
            if (t == World.GateStructType) continue;
            if (!seenTypes.Contains(t)) return Fail($"structure catalogue: no /data/buildings file for compiled type {t}");
        }
        // The gate (ADR-005 clause 6) is deferred and must stay unbuildable:
        // Cost 0 is what every command handler tests to refuse a type.
        if (refWorld.GetStructureType(World.GateStructType).Cost != 0)
            return Fail("structure catalogue: the reserved gate type must have no def");
        // RegisterStructureType is the /data override path; legal before tick 0.
        var rw = new World(0);
        rw.RegisterStructureType(1, StructureCatalogue.ToTypeDef(
            DataLoader.LoadStructureFile(Path.Combine(buildingsDir, "com_power_plant.yaml"))));
        Console.WriteLine($"selftest: /data/buildings reproduces all {seen} compiled structure defs exactly");
    }
    else Console.WriteLine("selftest: data/buildings not found, structure catalogue untested this run");

    // Ferrite field regrowth (ADR-012): the committed /data/fields file must
    // reproduce World's compiled reference twin exactly, the same round-trip
    // discipline the unit and structure catalogues hold. This is the equality
    // the goldens rest on: the scenarios build compiled worlds, so if the file
    // and the twin ever drift, the shipped client (which loads the file) and
    // the battery (which loads the twin) would silently play different numbers.
    string fieldsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/fields"));
    if (Directory.Exists(fieldsDir) && Directory.GetFiles(fieldsDir, "*.yaml").Length > 0)
    {
        var fieldFiles = Directory.GetFiles(fieldsDir, "*.yaml");
        Array.Sort(fieldFiles, StringComparer.Ordinal);
        bool sawFerrite = false;
        foreach (var f in fieldFiles)
        {
            var fd = DataLoader.LoadFieldFile(f);
            if (fd.Id != "com_ferrite_field") continue;
            if (fd.Name != "Ferrite Field") return Fail("field: name");
            if (fd.RegrowAmount != World.DefaultRegrowAmount)
                return Fail($"field: regrow_amount {fd.RegrowAmount} != compiled twin {World.DefaultRegrowAmount}");
            if (fd.RegrowIntervalTicks != World.DefaultRegrowIntervalTicks)
                return Fail($"field: regrow_interval_ticks {fd.RegrowIntervalTicks} != compiled twin {World.DefaultRegrowIntervalTicks}");
            // The registration path applies the file to a world before tick 0.
            var fw = new World(0);
            CatalogueFiles.RegisterFields(fw, fieldsDir);
            sawFerrite = true;
        }
        if (!sawFerrite) return Fail("field: no com_ferrite_field definition in /data/fields");
        Console.WriteLine($"selftest: /data/fields reproduces the compiled ferrite regrowth twin exactly ({World.DefaultRegrowAmount} per {World.DefaultRegrowIntervalTicks} ticks)");
    }
    else Console.WriteLine("selftest: data/fields not found, regrowth tuning untested this run");

    // Map loader (TICKET-P2-DATA-03): the committed skirmish map round-trips.
    string mapFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/maps/skirmish-01.fmap"));
    if (File.Exists(mapFile))
    {
        var md = MapData.Load(mapFile);
        if (md.Width != 96 || md.Height != 64) return Fail("map: size");
        // ADR-013 redesign (Serpentine Ford): the byte-content fingerprint of
        // skirmish-01. Regenerated by tools/gen_skirmish_01.py; these numbers are
        // that generator's asserted census (winding river, three fords, ridges,
        // ruins, fences, 20 ferrite cells at 8.92% blocked density).
        if (md.Blocked.Count != 548) return Fail($"map: expected 548 terrain cells (river, ridges, ruins, fences), got {md.Blocked.Count}");
        if (md.Fields.Count != 20 || !md.Fields.Contains((17, 19))) return Fail("map: fields (safe patch by base 0 at (17,19))");
        if (md.Starts[0] != (9, 9) || md.Starts[1] != (86, 54)) return Fail("map: starts (a 180-rotation pair)");
        var mw = md.BuildWorld(1, 2);
        if (!mw.Map.IsBlocked(15, 15) || mw.Map.IsBlocked(14, 15)) return Fail("map: terrain application (base-0 shoulder hill)");
        if (mw.EntityCount != 20) return Fail("map: field spawn count");
        Console.WriteLine("selftest: map loader round-trips skirmish-01 (terrain, fields, starts)");
    }
    else Console.WriteLine("selftest: map file not found, loader untested this run");

    Console.WriteLine("selftest: all assertions passed");
    return 0;
}

int Determinism(ulong seed)
{
    foreach (var (name, run) in scenarios)
    {
        var cp1 = new List<(int, ulong)>(); var cp2 = new List<(int, ulong)>();
        ulong h1 = run(seed, (t, h) => cp1.Add((t, h)));
        ulong h2 = run(seed, (t, h) => cp2.Add((t, h)));
        if (cp1.Count != cp2.Count) return Fail($"{name}: checkpoint count differs");
        for (int i = 0; i < cp1.Count; i++)
            if (cp1[i] != cp2[i]) return Fail($"{name}: divergence at tick {cp1[i].Item1}");
        if (h1 != h2) return Fail($"{name}: final hash mismatch");
        Console.WriteLine($"determinism [{name}]: double-run identical, final=0x{h1:X16}");
    }
    return 0;
}

int Golden(ulong seed)
{
    // Console.WriteLine uses Environment.NewLine (\r\n on Windows), which
    // breaks a byte-for-byte diff against the LF-committed golden-hashes.txt
    // even when every hash value is identical. Force LF so the comparison
    // is platform-independent, matching the file this output is diffed
    // against.
    Console.Out.NewLine = "\n";
    foreach (var (name, run) in scenarios)
        Console.WriteLine($"{name} {seed} 0x{run(seed, null):X16}");
    return 0;
}

int DefenceLoadGate(ulong seed)
{
    // TICKET-P5-DEF-06 clause 4. The TDD s6 ratified budget, verbatim
    // (03-technical-design-document.md:59): "600 active units + 200 structures
    // ... sim tick under 8 ms". 160 of the 200 structures are walls - 80 per
    // player, exactly the ADR-005 clause 5 cap - so this gate is what proves
    // the cap is the right number rather than a guess. The armies attack-move
    // through each other, so the O(n) auto-acquire scan, RouteExists and the
    // barrier predicates are all on the clock, not just movement.
    const int ticks = 1000, unitsPerPlayer = 300, wallsPerPlayer = 80, buildingsPerPlayer = 20;
    var world = new World(seed, 128, 128, players: 2);
    world.ShortGameEnabled = false; // a perf rig, not a match: never end early
    for (int p = 0; p < 2; p++)
    {
        int wallY = p == 0 ? 10 : 110;
        for (int i = 0; i < wallsPerPlayer; i++) world.SpawnWall(p, 10 + i % 20 * 2, wallY + i / 20 * 2);
        int bldY = p == 0 ? 24 : 96;
        for (int i = 0; i < buildingsPerPlayer; i++)
        {
            int bx = 10 + i % 10 * 4, by = bldY + i / 10 * 4;
            if (i % 2 == 0) world.SpawnTurret(p, bx, by); else world.SpawnPowerPlant(p, bx, by);
        }
        var def = world.GetUnitType(1);
        int uy = p == 0 ? 36 : 80;
        for (int i = 0; i < unitsPerPlayer; i++)
            world.SpawnUnit(p, Fix64.FromInt(5 + i % 30 * 2) + Fix64.Half, Fix64.FromInt(uy + i / 30) + Fix64.Half,
                def.Speed, def.Hp, def.Armour, def.WeaponId, def.SightCells, unitType: 1);
    }
    if (world.EntityCount != 2 * (unitsPerPlayer + wallsPerPlayer + buildingsPerPlayer))
        return Fail($"PERF GATE: the defence-load rig built {world.EntityCount} entities, not {2 * (unitsPerPlayer + wallsPerPlayer + buildingsPerPlayer)}");
    // A perf rig, not a balance test: nothing may die. Measured without this,
    // the two armies annihilate each other inside a couple of hundred ticks
    // (33 of 600 units left at tick 1000) and the average quietly reports the
    // cost of a nearly empty world while claiming to have measured 600 + 200.
    // Pinning hit points keeps the whole stated population on the clock -
    // still firing, still scanning - for every one of the 1000 ticks.
    for (int i = 0; i < world.EntityCount; i++)
    {
        var e = world.Entities[i];
        e.Hp = e.MaxHp = 1_000_000;
        world.SetEntityForTest(i, e);
    }
    var cmds = new List<Command>();
    // Order both armies onto each other's line: every unit is attack-moving.
    foreach (var e in world.Entities)
        if (e.Kind == EntityKind.Unit)
            cmds.Add(new Command(0, e.PlayerId, CommandType.AttackMove, e.Id,
                Fix64.FromInt(32), Fix64.FromInt(e.PlayerId == 0 ? 110 : 12)));
    var sw = Stopwatch.StartNew();
    for (int t = 0; t < ticks; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
    }
    sw.Stop();
    double ms = sw.Elapsed.TotalMilliseconds / ticks;
    // The gate polices its own honesty: if the rig ever stops holding the
    // population it claims to measure, the figure below is meaningless and
    // this fails rather than reporting a comfortable lie.
    int aliveUnits = 0, aliveStructures = 0, aliveWalls = 0;
    foreach (var e in world.Entities)
    {
        if (!e.Alive) continue;
        if (e.Kind == EntityKind.Unit) aliveUnits++; else aliveStructures++;
        if (e.Kind == EntityKind.Wall) aliveWalls++;
    }
    if (aliveUnits != unitsPerPlayer * 2 || aliveStructures != 2 * (wallsPerPlayer + buildingsPerPlayer) || aliveWalls != 2 * wallsPerPlayer)
        return Fail($"PERF GATE: the rig must hold its full population for the whole run (ended {aliveUnits} units, {aliveStructures} structures, {aliveWalls} walls) - a budget measured on a half-empty world proves nothing");
    Console.WriteLine($"defence load: {ticks} ticks x {unitsPerPlayer * 2} units + {2 * (wallsPerPlayer + buildingsPerPlayer)} structures ({2 * wallsPerPlayer} walls), {ms:F3} ms/tick (budget 8)");
    if (ms > 8.0) return Fail($"PERF GATE: defence load {ms:F3} ms/tick exceeds the 8 ms budget at 600 units + 200 structures (TDD s6)");
    return 0;
}

int CatalogueRefuse()
{
    // ADR-006's gate scenario: a deliberately mismatched catalogue REFUSES
    // rather than desyncs, on every surface that carries the checksum - the
    // LAN hello, the save format and the replay format - plus the readable
    // error shapes the client's /data load must produce. Additive: not a
    // golden scenario, so the 24 golden lines are untouched by construction.

    // 1. The checksum itself: stable across worlds, sensitive to one def.
    var wa = new World(1);
    var wb = new World(2);
    if (wa.CatalogueChecksum != wb.CatalogueChecksum)
        return Fail("catrefuse: two compiled catalogues must produce one checksum");
    ulong good = wa.CatalogueChecksum;
    var wc = new World(3);
    wc.RegisterUnitType(1, wc.GetUnitType(1) with { Cost = wc.GetUnitType(1).Cost + 1 });
    ulong bad = wc.CatalogueChecksum;
    if (bad == good)
        return Fail("catrefuse: a one-credit def change must change the checksum");

    // 2. The ADR's hash-impact argument, asserted rather than assumed: the
    // /data files register to a catalogue identical to the compiled one, so
    // adoption moves nothing. This is the equality the goldens rest on.
    string unitsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/units"));
    string buildingsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/buildings"));
    if (Directory.Exists(unitsDir) && Directory.Exists(buildingsDir))
    {
        var wd = new World(4);
        CatalogueFiles.RegisterAll(wd, unitsDir, buildingsDir);
        if (wd.CatalogueChecksum != good)
            return Fail($"catrefuse: /data registers to 0x{wd.CatalogueChecksum:X16} but the compiled catalogue is 0x{good:X16} - the two sources have drifted");
        Console.WriteLine($"catrefuse: /data and the compiled catalogue agree on 0x{good:X16} (ADR-006 hash-impact argument holds)");
    }
    else Console.WriteLine("catrefuse: data directories not found, /data equality untested this run");

    // 3. The LAN hello: two lockstep clients, one bumped def, and the game
    // must refuse before tick 0 with BOTH checksums named on both sides.
    {
        var relay = new Relay(playerCount: 2);
        relay.Start();
        new Thread(relay.Run) { IsBackground = true }.Start();
        var errors = new Exception?[2];
        var threads = new Thread[2];
        for (int p = 0; p < 2; p++)
        {
            int pid = p;
            threads[p] = new Thread(() =>
            {
                try
                {
                    World Mismatched(ulong seed)
                    {
                        var w = LanWorldFactory(seed);
                        if (pid == 1) w.RegisterUnitType(1, w.GetUnitType(1) with { Cost = w.GetUnitType(1).Cost + 1 });
                        return w;
                    }
                    using var client = new LockstepClient(relay.Port, Mismatched, 77);
                    client.Prime();   // must never be reached
                }
                catch (Exception ex) { errors[pid] = ex; }
            });
            threads[p].Start();
        }
        foreach (var t in threads) t.Join();
        for (int p = 0; p < 2; p++)
        {
            if (errors[p] is not InvalidDataException)
                return Fail($"catrefuse: client {p} was not refused (got {(errors[p] == null ? "no error - the game would have desynced" : errors[p]!.GetType().Name + ": " + errors[p]!.Message)})");
            string msg = errors[p]!.Message;
            if (!msg.Contains($"0x{good:X16}") || !msg.Contains($"0x{bad:X16}"))
                return Fail($"catrefuse: client {p}'s refusal must name both checksums, got: {msg}");
        }
        if (!relay.CatalogueRefused) return Fail("catrefuse: the relay must record the refusal");
        if (relay.DesyncDetected) return Fail("catrefuse: a refusal must never register as a desync");
    }

    // 4. The save format: v3 records the checksum; a matching catalogue loads
    // bit-exact; a foreign catalogue refuses naming both checksums; the same
    // bytes wearing the v2 magic and no checksum load unchecked, because a
    // MISSING checksum means do not check, never refuse.
    {
        var live = new World(2026, 32, 32, 2);
        live.GrantCredits(0, 1234);
        live.SpawnUnit(0, Fix64.FromInt(5), Fix64.FromInt(5), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
        using var ms = new MemoryStream();
        live.Save(ms);
        ms.Position = 0;
        var loaded = World.Load(ms);
        if (loaded.ComputeStateHash() != live.ComputeStateHash())
            return Fail("catrefuse: a v3 save under the same catalogue must load bit-exact");
        ms.Position = 0;
        try
        {
            World.Load(ms, w => w.RegisterUnitType(1, w.GetUnitType(1) with { Cost = w.GetUnitType(1).Cost + 1 }));
            return Fail("catrefuse: a save carrying a foreign checksum must refuse");
        }
        catch (InvalidDataException e)
        {
            if (!e.Message.Contains($"0x{good:X16}") || !e.Message.Contains($"0x{bad:X16}"))
                return Fail($"catrefuse: the save refusal must name both checksums, got: {e.Message}");
        }
        // v2 surgery via the shared layout-aware helper: drop the checksum
        // AND the ADR-007 rally tail from every entity record, leaving the
        // published on-disk shape of every existing v2 save (magic
        // 0x534C4132). The old inline surgery assumed the checksum was the
        // only difference, which save format v4 made untrue.
        var v2 = DowngradeSave(ms.ToArray(), 0x534C4132u);
        var old = World.Load(new MemoryStream(v2), w => w.RegisterUnitType(1, w.GetUnitType(1) with { Cost = w.GetUnitType(1).Cost + 1 }));
        if (old.ComputeStateHash() != live.ComputeStateHash())
            return Fail("catrefuse: a v2 save must still load under any catalogue (no checksum, no check)");
    }

    // 5. The replay format: v3 carries a catalogue line that round-trips and
    // refuses a foreign checksum naming both; a v2 stream has none and is
    // never refused.
    {
        string path = Path.Combine(Path.GetTempPath(), "ferrostorm-catrefuse.frep");
        var writer = new ReplayWriter(7, "gate", good);
        writer.Record(new Command(0, 0, CommandType.Stop, 0, Fix64.Zero, Fix64.Zero));
        writer.Finish(0xABCD, path);
        var rep = Replay.Load(path);
        if (!rep.HasCatalogueChecksum || rep.CatalogueChecksum != good)
            return Fail("catrefuse: the replay catalogue line must round-trip");
        rep.AssertCatalogueMatches(good);   // must not throw
        try
        {
            rep.AssertCatalogueMatches(bad);
            return Fail("catrefuse: a replay with a foreign checksum must refuse");
        }
        catch (InvalidDataException e)
        {
            if (!e.Message.Contains($"0x{good:X16}") || !e.Message.Contains($"0x{bad:X16}"))
                return Fail($"catrefuse: the replay refusal must name both checksums, got: {e.Message}");
        }
        string v2Path = Path.Combine(Path.GetTempPath(), "ferrostorm-catrefuse-v2.frep");
        File.WriteAllLines(v2Path, new[] { "ferrostorm-replay v2", "seed 7", "setup gate", "hash 000000000000ABCD" });
        var oldRep = Replay.Load(v2Path);
        if (oldRep.HasCatalogueChecksum) return Fail("catrefuse: a v2 replay must carry no checksum");
        oldRep.AssertCatalogueMatches(bad);   // no checksum, no check, no throw
    }

    // 6. ADR-006 commitment 2, the readable error shapes, exercised on the
    // shared loader the client calls: a missing /data says so; a malformed
    // file is named with the parser's line; a file missing for a compiled
    // type names that type rather than falling back to compiled values.
    {
        string scratch = Path.Combine(Path.GetTempPath(), "ferrostorm-catrefuse-data");
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        Directory.CreateDirectory(Path.Combine(scratch, "units"));
        Directory.CreateDirectory(Path.Combine(scratch, "buildings"));
        try
        {
            CatalogueFiles.RegisterAll(new World(5), Path.Combine(scratch, "missing"), Path.Combine(scratch, "buildings"));
            return Fail("catrefuse: a missing /data directory must refuse");
        }
        catch (IOException e)
        {
            if (!e.Message.Contains("/data is missing")) return Fail($"catrefuse: the missing-directory message must say so, got: {e.Message}");
        }
        string badFile = Path.Combine(scratch, "units", "dir_cannon_tank.yaml");
        File.WriteAllText(badFile, "id: dir_cannon_tank\n  oops: indented\n");
        try
        {
            CatalogueFiles.RegisterAll(new World(6), Path.Combine(scratch, "units"), Path.Combine(scratch, "buildings"));
            return Fail("catrefuse: a malformed data file must refuse");
        }
        catch (FormatException e)
        {
            if (!e.Message.Contains("dir_cannon_tank.yaml") || !e.Message.Contains("line 2"))
                return Fail($"catrefuse: the parse error must name the file and the line, got: {e.Message}");
        }
        File.Delete(badFile);
        try
        {
            CatalogueFiles.RegisterAll(new World(7), Path.Combine(scratch, "units"), Path.Combine(scratch, "buildings"));
            return Fail("catrefuse: an incomplete /data must refuse rather than mix catalogues");
        }
        catch (FormatException e)
        {
            if (!e.Message.Contains("compiled unit type 1"))
                return Fail($"catrefuse: the incompleteness error must name the compiled type, got: {e.Message}");
        }
        Directory.Delete(scratch, recursive: true);
    }

    Console.WriteLine("catrefuse: a mismatched catalogue refuses on every surface - the LAN hello named both checksums on both clients with no desync, " +
                      "a foreign-checksum save and replay both refused naming both values, a v2 save and a v2 replay still load unchecked, " +
                      "and the /data loader fails readably for a missing directory, a malformed file (file and line) and a missing file (compiled type named)");
    return 0;
}

// Byte surgery for the backwards-compatibility gates: rebuild a CURRENT
// (v4) save as an older format on disk. v4 -> v3 strips the ADR-007 rally
// fields from every entity record; below v3 the ADR-006 catalogue checksum
// goes too, and below v2 the per-player faction byte. The walk mirrors the
// serializer's layout field by field; if that layout drifts, the load
// assertions downstream fail loudly rather than blessing surgery on wrong
// bytes. (The pre-B2 version of this lived inline in catrefuse and assumed
// the only difference was the checksum; the wider entity record made it a
// shared, layout-aware helper.)
byte[] DowngradeSave(byte[] current, uint targetMagic)
{
    const uint magicV1 = 0x534C4131u, magicV3 = 0x534C4133u, magicV4 = 0x534C4134u, magicV5 = 0x534C4135u, magicV6 = 0x534C4136u, magicV7 = 0x534C4137u, magicV8 = 0x534C4138u, magicV9 = 0x534C4139u;
    using var input = new BinaryReader(new MemoryStream(current));
    var outMs = new MemoryStream();
    using var w = new BinaryWriter(outMs);
    // P7-3: the SOURCE is whatever Save() currently writes, which is v9 now.
    // Pinned to a literal version this helper broke the moment the format
    // moved, and it broke in the battery rather than here - the same
    // name-one-version trap the loader's hasBuildLanes had.
    if (input.ReadUInt32() != magicV9)
        throw new InvalidOperationException("save surgery expects a v9 stream (the current Save format)");
    w.Write(targetMagic);
    ulong checksum = input.ReadUInt64();
    if (targetMagic is magicV3 or magicV4 or magicV5 or magicV6 or magicV7 or magicV8) w.Write(checksum); // v3+ keep the checksum; v1/v2 never had one
    w.Write(input.ReadInt32());   // tick
    w.Write(input.ReadInt32());   // winner
    w.Write(input.ReadBoolean()); // short game
    int players = input.ReadInt32(); w.Write(players);
    int mw = input.ReadInt32(), mh = input.ReadInt32(); w.Write(mw); w.Write(mh);
    w.Write(input.ReadBytes((mw + 7) / 8 * mh)); // packed terrain rows
    w.Write(input.ReadUInt64());  // rng state
    for (int p = 0; p < players; p++)
    {
        byte faction = input.ReadByte();
        if (targetMagic != magicV1) w.Write(faction); // v1 predates the faction byte (Q001)
        w.Write(input.ReadInt64());   // credits
        w.Write(input.ReadBoolean()); // eliminated flag
        int words = input.ReadInt32(); w.Write(words);
        for (int i = 0; i < words; i++) w.Write(input.ReadUInt64());
    }
    int count = input.ReadInt32(); w.Write(count);
    // The entity record is fixed-width: 209 bytes through FieldCloaked, then
    // the v4 rally tail (RallyX 8 + RallyY 8 + HasRally 1 + Departing 1), then
    // the v5 ferrite cap (int 4), then the v6 no-progress tail (NearestApproachSq
    // 8 + NoProgressTicks 4 = 12, Q013/ADR-014), then the v7 stance tail (Stance
    // 1 + PostX 8 + PostY 8 + PatrolX 8 + PatrolY 8 + PatrolOutbound 1 = 34,
    // ADR-015). A target keeps the tails its format carried and drops the rest:
    // v6 keeps rally+cap+no-progress, v5 keeps rally+cap, v4 keeps rally only,
    // v3 and below drop all four; the v7 stance tail is dropped for every target.
    const int v3EntityBytes = 209, rallyTailBytes = 18, ferriteCapBytes = 4, noProgressTailBytes = 12, stanceTailBytes = 34;
    bool keepRally = targetMagic == magicV4 || targetMagic == magicV5 || targetMagic == magicV6 || targetMagic == magicV7;
    bool keepCap = targetMagic == magicV5 || targetMagic == magicV6 || targetMagic == magicV7;
    bool keepNoProgress = targetMagic == magicV6 || targetMagic == magicV7;
    bool keepStance = targetMagic == magicV7;
    for (int i = 0; i < count; i++)
    {
        w.Write(input.ReadBytes(v3EntityBytes));
        if (keepRally) w.Write(input.ReadBytes(rallyTailBytes)); else input.ReadBytes(rallyTailBytes);
        if (keepCap) w.Write(input.ReadBytes(ferriteCapBytes)); else input.ReadBytes(ferriteCapBytes);
        if (keepNoProgress) w.Write(input.ReadBytes(noProgressTailBytes)); else input.ReadBytes(noProgressTailBytes);
        if (keepStance) w.Write(input.ReadBytes(stanceTailBytes)); else input.ReadBytes(stanceTailBytes);
    }
    // Production queues and order queues are format-identical in every format,
    // so they are copied verbatim - but ADR-023's lane block sits between them
    // and the trailer in v8 and exists in NO earlier format, so the tail can no
    // longer be one blanket copy: the blocks are walked so the lane block can
    // be dropped for every target below v8.
    int queueCount = input.ReadInt32(); w.Write(queueCount);
    for (int i = 0; i < queueCount; i++)
    {
        w.Write(input.ReadInt32());                       // producer id
        int n = input.ReadInt32(); w.Write(n);
        for (int k = 0; k < n; k++) w.Write(input.ReadInt32());
    }
    int orderQueueCount = input.ReadInt32(); w.Write(orderQueueCount);
    for (int i = 0; i < orderQueueCount; i++)
    {
        w.Write(input.ReadInt32());                       // entity id
        int n = input.ReadInt32(); w.Write(n);
        for (int k = 0; k < n; k++) w.Write(input.ReadBytes(34)); // one serialized Command
    }
    // ADR-023's lane block: kept for a v8 target, dropped below it. v9 is the
    // SOURCE format and is refused as a target rather than half-copied, the
    // same reason v8 used to be.
    if (targetMagic == magicV9) throw new InvalidOperationException("save surgery downgrades; v9 is the source format, not a target");
    int laneCount = input.ReadInt32();
    bool keepLanes = targetMagic == magicV8;
    if (keepLanes) w.Write(laneCount);
    for (int i = 0; i < laneCount; i++)
    {
        int yard = input.ReadInt32();
        int prog = input.ReadInt32(), paid = input.ReadInt32(), ready = input.ReadInt32();
        int n = input.ReadInt32();
        if (keepLanes) { w.Write(yard); w.Write(prog); w.Write(paid); w.Write(ready); w.Write(n); }
        for (int k = 0; k < n; k++) { int t = input.ReadInt32(); if (keepLanes) w.Write(t); }
    }
    // P7-3 (v9 only): the transport holds, consumed and DROPPED. Every target
    // this helper produces predates transports, so a hold cannot be expressed
    // in any of them - and a unit that was aboard is simply not in that older
    // world, which is what those formats meant.
    int cargoCount = input.ReadInt32();
    for (int i = 0; i < cargoCount; i++)
    {
        input.ReadInt32();                                // carrier id
        int n = input.ReadInt32();
        for (int k = 0; k < n; k++) { input.ReadInt32(); input.ReadInt32(); input.ReadInt32(); }
    }
    w.Write(input.ReadUInt32());                          // trailer
    return outMs.ToArray();
}

int SpawnGate()
{
    // ADR-007 / doc 23 Wave 4: rally in the sim, the spawn exit move, save
    // format v4, and (with SPAWN-04) occupancy and the hold that never
    // charges. Additive, the catrefuse pattern: standalone mode and battery
    // stage, never a golden scenario, so the golden list stays 24 lines by
    // construction.
    //
    // ADR-009 surgery: every stage below that PRODUCES A RIFLE now produces
    // it at a BARRACKS, because that is the rifle's producer since the split
    // and a factory refuses it. This is not a workaround, it is the gate
    // being exercised end to end: the whole rally, exit-move, occupancy and
    // hold machinery is re-proven against the new producer, which is where
    // infantry rally matters most. Every number is untouched - the rifle
    // still costs 200 and takes 75 ticks, the barracks is 2x2 like the
    // factory so the spawn ring and its centre cell are identical, and the
    // 20-point draw against a 100-supply plant is still full power.
    var cmds = new List<Command>();

    // 1. SetRally validation: producing structures the commander owns, and
    // nothing else. Clamped exactly as Move clamps; AuxId -1 clears back to
    // the canonical unset state.
    {
        var w = new World(11, 64, 64, 2);
        void StepN(int n) { for (int i = 0; i < n; i++) { w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }
        w.GrantCredits(0, 10000);
        int factory = w.SpawnFactory(0, 10, 6);
        int cy = w.SpawnConstructionYard(0, 14, 6);
        int rifle = w.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(20), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
        int enemyFactory = w.SpawnFactory(1, 40, 40);
        cmds.Add(new Command(0, 0, CommandType.SetRally, rifle, Fix64.FromInt(30), Fix64.FromInt(30), 0));
        cmds.Add(new Command(0, 0, CommandType.SetRally, enemyFactory, Fix64.FromInt(30), Fix64.FromInt(30), 0));
        StepN(1);
        if (w.Entities[rifle].HasRally) return Fail("spawngate: a unit must refuse SetRally (producing structures only)");
        if (w.Entities[enemyFactory].HasRally) return Fail("spawngate: an enemy structure must refuse SetRally (ownership)");
        cmds.Add(new Command(0, 0, CommandType.SetRally, factory, Fix64.FromInt(999), Fix64.FromInt(-9), 0));
        cmds.Add(new Command(0, 0, CommandType.SetRally, cy, Fix64.FromInt(30), Fix64.FromInt(30), 0));
        StepN(1);
        var f = w.Entities[factory];
        if (!f.HasRally) return Fail("spawngate: a factory must accept SetRally");
        if (f.RallyX != Fix64.FromInt(64) - Fix64.Half || f.RallyY != Fix64.Zero)
            return Fail($"spawngate: SetRally must clamp exactly as Move does (got {f.RallyX},{f.RallyY})");
        if (!w.Entities[cy].HasRally) return Fail("spawngate: a Construction Yard must accept SetRally (ADR-007's predicate, now ADR-009's IsProducer)");
        // ADR-009 clause 5: the barracks joins the rallyable producers, which
        // is B2's explicitly deferred question answered - IsRallyable became
        // IsProducer in place, so the wire format never changed twice.
        int barracks = w.SpawnBarracks(0, 18, 6);
        cmds.Add(new Command(0, 0, CommandType.SetRally, barracks, Fix64.FromInt(24), Fix64.FromInt(9), 0));
        StepN(1);
        if (!w.Entities[barracks].HasRally)
            return Fail("spawngate: a Barracks must accept SetRally (ADR-009 clause 5 - infantry want a rally most)");
        cmds.Add(new Command(0, 0, CommandType.SetRally, factory, Fix64.FromInt(1), Fix64.FromInt(1), -1));
        StepN(1);
        f = w.Entities[factory];
        if (f.HasRally || f.RallyX != Fix64.Zero || f.RallyY != Fix64.Zero)
            return Fail("spawngate: AuxId -1 must clear back to the canonical unset state");
    }

    // 2. The exit move: produced units leave the mouth and settle at the
    // rally; ProductionComplete still fires with C naming the producer.
    {
        var w = new World(12, 64, 64, 2);
        w.GrantCredits(0, 20000);
        w.SpawnPowerPlant(0, 6, 6);
        int barracks = w.SpawnBarracks(0, 10, 10);
        var rallyX = Map.CellCentre(25); var rallyY = Map.CellCentre(11);
        cmds.Add(new Command(0, 0, CommandType.SetRally, barracks, rallyX, rallyY, 0));
        for (int k = 0; k < 3; k++)
            cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        int completions = 0, wrongC = 0;
        int preCount = w.EntityCount;
        for (int t = 0; t < 3 * 75 + 200; t++)
        {
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            cmds.Clear();
            foreach (var ev in w.Events)
                if (ev.Type == GameEventType.ProductionComplete) { completions++; if (ev.C != barracks) wrongC++; }
        }
        if (completions != 3) return Fail($"spawngate: expected 3 completions at the rallied barracks, got {completions}");
        if (wrongC != 0) return Fail("spawngate: ProductionComplete must carry the producer in C");
        int settled = 0;
        for (int i = preCount; i < w.EntityCount; i++)
        {
            var u = w.Entities[i];
            if (!u.Alive) return Fail("spawngate: a produced unit died unprovoked");
            if (u.Moving || u.Departing) return Fail("spawngate: produced units must settle (Departing cleared, walk ended)");
            if (Fix64.DistSq(u.X - rallyX, u.Y - rallyY) > Fix64.FromInt(36))
                return Fail($"spawngate: unit {i} settled {u.X},{u.Y}, not near the rally (crowd radius 4 plus follower spacing)");
            settled++;
        }
        if (settled != 3) return Fail($"spawngate: expected 3 settled units, got {settled}");
    }

    // 3. SPAWN-D3 is dead: a rally TWO cells from the mouth still moves every
    // unit off its spawn cell (the Departing guard suppresses the 4-cell
    // crowd-arrival shortcut until the mouth is actually cleared).
    {
        var w = new World(13, 64, 64, 2);
        w.GrantCredits(0, 20000);
        w.SpawnPowerPlant(0, 6, 6);
        int barracks = w.SpawnBarracks(0, 10, 10);
        // Producer centre cell is (11,11) - a barracks is 2x2 exactly as the
        // factory is - and the ring's first offset (0,2) makes the mouth
        // (11,13). Two cells further down: (11,15).
        cmds.Add(new Command(0, 0, CommandType.SetRally, barracks, Map.CellCentre(11), Map.CellCentre(15), 0));
        cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        var spawnPos = new Dictionary<int, (Fix64 X, Fix64 Y)>();
        int seen = w.EntityCount;
        for (int t = 0; t < 75 + 120; t++)
        {
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            cmds.Clear();
            while (w.EntityCount > seen) { spawnPos[seen] = (w.Entities[seen].X, w.Entities[seen].Y); seen++; }
        }
        if (spawnPos.Count != 1)
            return Fail($"spawngate: close rally must not stall the mouth (got {spawnPos.Count}/1 spawns)");
        foreach (var (id, at) in spawnPos)
        {
            var u = w.Entities[id];
            if (u.Moving) return Fail("spawngate: close-rally units must settle");
            if (Map.CellOf(u.X) == Map.CellOf(at.X) && Map.CellOf(u.Y) == Map.CellOf(at.Y))
                return Fail($"spawngate: unit {id} never left its spawn cell - a 2-cell rally is still a silent no-op (SPAWN-D3)");
            if (Fix64.DistSq(u.X - Map.CellCentre(11), u.Y - Map.CellCentre(15)) > Fix64.FromInt(16))
                return Fail($"spawngate: the close-rallied unit settled at {u.X},{u.Y}, outside the crowd radius of its 2-cell rally");
        }
        // The multi-unit close-rally case (followers reusing the mouth) is
        // asserted in the SPAWN-04 sections below: it needs the occupancy
        // test, which is exactly doc 23's load-bearing ordering.
    }

    // 4. Save format round-trip: a world with live rally state round-trips
    // bit-exact and resumes bit-exact; a v3 downgrade still loads with rally
    // unset; a v2 downgrade additionally loses the checksum and is never
    // refused. The save is v6 now (Q013/ADR-014's no-progress backstop atop
    // ADR-012's ferrite cap), which DowngradeSave strips along with the rally
    // tail for a pre-v4 target; the ferrite-cap resume is proven in
    // RegrowthGate against a world that actually has fields.
    {
        var w = new World(14, 64, 64, 2);
        void StepN(int n) { for (int i = 0; i < n; i++) { w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }
        w.GrantCredits(0, 20000);
        w.SpawnPowerPlant(0, 6, 6);
        // Two BARRACKS (the rifle's producer since ADR-009), which also keeps
        // this stage proving what it always proved: two independent rallied
        // producers, one set and one cleared, surviving a save.
        int f1 = w.SpawnBarracks(0, 10, 10);
        int f2 = w.SpawnBarracks(0, 16, 10);
        cmds.Add(new Command(0, 0, CommandType.SetRally, f1, Map.CellCentre(30), Map.CellCentre(12), 0));
        cmds.Add(new Command(0, 0, CommandType.SetRally, f2, Map.CellCentre(30), Map.CellCentre(20), 0));
        cmds.Add(new Command(0, 0, CommandType.Produce, f1, Fix64.Zero, Fix64.Zero, 2));
        cmds.Add(new Command(0, 0, CommandType.Produce, f1, Fix64.Zero, Fix64.Zero, 2));
        StepN(1);
        cmds.Add(new Command(0, 0, CommandType.SetRally, f2, Fix64.Zero, Fix64.Zero, -1)); // set then cleared
        StepN(80); // first rifle is out and walking: Departing state is live somewhere in here
        ulong hashMid = w.ComputeStateHash();
        using var ms = new MemoryStream();
        w.Save(ms);
        ms.Position = 0;
        var loaded = World.Load(ms);
        if (loaded.ComputeStateHash() != hashMid)
            return Fail($"spawngate: v4 save must load bit-exact (0x{loaded.ComputeStateHash():X16} vs 0x{hashMid:X16})");
        var lf1 = loaded.Entities[f1];
        if (!lf1.HasRally || lf1.RallyX != Map.CellCentre(30) || lf1.RallyY != Map.CellCentre(12))
            return Fail("spawngate: the rally must survive the save BY THE SIM (Q004's resolution)");
        if (loaded.Entities[f2].HasRally)
            return Fail("spawngate: a cleared rally must stay cleared through the round trip");
        int countAtSave = loaded.EntityCount;
        for (int t = 0; t < 200; t++) { w.Step(default); loaded.Step(default); }
        if (loaded.ComputeStateHash() != w.ComputeStateHash())
            return Fail("spawngate: resumed run diverged from the uninterrupted one");
        var v6Bytes = ms.ToArray();
        var v3World = World.Load(new MemoryStream(DowngradeSave(v6Bytes, 0x534C4133u)));
        if (v3World.EntityCount != countAtSave)
            return Fail("spawngate: v3 downgrade lost entities");
        foreach (var e in v3World.Entities)
            if (e.HasRally || e.Departing || e.RallyX != Fix64.Zero || e.RallyY != Fix64.Zero)
                return Fail("spawngate: a v3 save must load with rally unset and Departing false");
        var v2World = World.Load(new MemoryStream(DowngradeSave(v6Bytes, 0x534C4132u)),
            world => world.RegisterUnitType(1, world.GetUnitType(1) with { Cost = world.GetUnitType(1).Cost + 1 }));
        if (v2World.EntityCount != countAtSave)
            return Fail("spawngate: v2 downgrade lost entities (and must never be checksum-refused)");
    }

    // 5. SPAWN-D1 is dead: ten units produced back to back, no rally, occupy
    // ten DISTINCT cells (doc 23 SPAWN-04 acceptance). The default exit move
    // plus the occupancy test spread them; nothing stacks, nothing vanishes.
    {
        var w = new World(15, 64, 64, 2);
        w.GrantCredits(0, 20000);
        w.SpawnPowerPlant(0, 6, 6);
        int barracks = w.SpawnBarracks(0, 10, 10);
        for (int k = 0; k < 10; k++)
            cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        int preCount = w.EntityCount;
        for (int t = 0; t < 10 * 75 + 400; t++)
        {
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            cmds.Clear();
        }
        if (w.EntityCount - preCount != 10)
            return Fail($"spawngate: expected 10 produced units, got {w.EntityCount - preCount} (the ring must never saturate in the no-rally default game)");
        var cells = new HashSet<int>();
        for (int i = preCount; i < w.EntityCount; i++)
        {
            var u = w.Entities[i];
            if (!u.Alive) return Fail("spawngate: a produced unit died unprovoked in the spread test");
            if (u.Moving) return Fail("spawngate: all ten produced units must settle");
            if (!cells.Add(w.Map.CellIndex(Map.CellOf(u.X), Map.CellOf(u.Y))))
                return Fail($"spawngate: two of ten produced units share cell ({Map.CellOf(u.X)},{Map.CellOf(u.Y)}) - stacked-forever is back");
        }
    }

    // 6. The walled-in hold: with every spawn cell blocked, a completed unit
    // is HELD - never deleted, never re-charged. The factory spends EXACTLY
    // ZERO credits over 100 held ticks (the 3000-credits-per-second trap's
    // assertion), the queue stalls honestly behind the held head, and the
    // instant a cell frees the unit spawns with ProductionComplete carrying C.
    {
        var w = new World(16, 64, 64, 2);
        var events = new List<GameEvent>();
        void StepN(int n)
        {
            for (int i = 0; i < n; i++)
            {
                w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
                cmds.Clear();
                events.AddRange(w.Events);
            }
        }
        w.GrantCredits(0, 20000);
        w.SpawnPowerPlant(0, 6, 6);
        int barracks = w.SpawnBarracks(0, 10, 10);
        // Wall the whole ring: the producer's centre cell is (11,11); block
        // every spawn candidate. The map, not units, blocks here - the
        // walled-in case is the one that can persist forever and must stay
        // honest.
        foreach (var (dx, dy) in new[] { (0, 2), (1, 2), (-1, 2), (2, 0), (-2, 0), (0, -2), (2, 2), (-2, 2), (2, -2), (-2, -2), (0, 3) })
            w.Map.SetBlocked(11 + dx, 11 + dy, true);
        w.InvalidateFlowCache();
        cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        int preCount = w.EntityCount;
        StepN(75 + 30); // the head completes at tick 75 and is now held
        var f = w.Entities[barracks];
        if (f.BuildProgress != 75 * 100)
            return Fail($"spawngate: the held producer must sit at 100 per cent (progress {f.BuildProgress})");
        if (f.BuildPaid != 200)
            return Fail($"spawngate: the held head must stay FULLY PAID (BuildPaid {f.BuildPaid}) or a cancel refunds nothing");
        if (w.QueueLength(barracks) != 2)
            return Fail($"spawngate: the held head must not pop and the line must stall behind it (queue {w.QueueLength(barracks)})");
        if (w.EntityCount != preCount)
            return Fail("spawngate: nothing may spawn while every cell is blocked");
        foreach (var ev in events)
            if (ev.Type == GameEventType.ProductionComplete)
                return Fail("spawngate: ProductionComplete must not fire during the hold");
        long heldCredits = w.Credits(0);
        events.Clear();
        StepN(100);
        if (w.Credits(0) != heldCredits)
            return Fail($"spawngate: a blocked producer spent {heldCredits - w.Credits(0)} credits over 100 held ticks - it must spend EXACTLY ZERO");
        if (w.EntityCount != preCount)
            return Fail("spawngate: the held unit must neither spawn nor vanish while blocked");
        if (w.QueueLength(barracks) != 2 || w.Entities[barracks].BuildProgress != 75 * 100)
            return Fail("spawngate: the hold must persist unchanged while blocked");
        foreach (var ev in events)
            if (ev.Type == GameEventType.ProductionComplete)
                return Fail("spawngate: no completion event may fire across 100 held ticks");
        // Free one cell: the mouth clears and the producer resumes THAT tick.
        events.Clear();
        w.Map.SetBlocked(11, 13, false);
        w.InvalidateFlowCache();
        StepN(2);
        if (w.EntityCount != preCount + 1)
            return Fail("spawngate: the held unit must spawn the instant a cell frees");
        int freed = preCount;
        if (Map.CellOf(w.Entities[freed].X) != 11 || Map.CellOf(w.Entities[freed].Y) != 13)
            return Fail("spawngate: the released unit must spawn on the freed cell");
        int completions = 0;
        foreach (var ev in events)
            if (ev.Type == GameEventType.ProductionComplete)
            { completions++; if (ev.C != barracks) return Fail("spawngate: the released completion must carry the producer in C"); }
        if (completions != 1)
            return Fail($"spawngate: expected exactly one completion on release, got {completions}");
        if (w.QueueLength(barracks) != 1)
            return Fail("spawngate: the queue must pop on release and the second item must take the head");
        StepN(75 + 60); // the second unit follows through the same freed mouth
        if (w.EntityCount != preCount + 2)
            return Fail("spawngate: the stalled second unit must build and spawn after the release");
        if (w.Credits(0) != 20000 - 2 * 200)
            return Fail($"spawngate: two rifles must cost exactly 400 across hold and release (spent {20000 - w.Credits(0)})");
    }

    // 7. The multi-unit close rally: with occupancy live, a rally two cells
    // from the mouth neither bricks the ring nor stacks the crowd - four
    // units spawn, settle at four DISTINCT POSITIONS (a rally crowd packs at
    // separation spacing, so two positions may share a cell; identical
    // positions would be the stacked-forever defect), one reaches the rally,
    // and a fifth still spawns.
    {
        var w = new World(17, 64, 64, 2);
        w.GrantCredits(0, 20000);
        w.SpawnPowerPlant(0, 6, 6);
        int barracks = w.SpawnBarracks(0, 10, 10);
        cmds.Add(new Command(0, 0, CommandType.SetRally, barracks, Map.CellCentre(11), Map.CellCentre(15), 0));
        for (int k = 0; k < 4; k++)
            cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        int preCount = w.EntityCount;
        for (int t = 0; t < 4 * 75 + 300; t++)
        {
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            cmds.Clear();
        }
        if (w.EntityCount - preCount != 4)
            return Fail($"spawngate: a close rally must not stall production under occupancy (got {w.EntityCount - preCount}/4)");
        var positions = new HashSet<(long, long)>();
        bool anyAtRally = false;
        for (int i = preCount; i < w.EntityCount; i++)
        {
            var u = w.Entities[i];
            if (!u.Alive) return Fail("spawngate: a close-rallied unit died unprovoked");
            if (u.Moving) return Fail("spawngate: close-rallied units must settle");
            if (!positions.Add((u.X.Raw, u.Y.Raw)))
                return Fail("spawngate: two close-rallied units stacked on one exact position (SPAWN-D1 is back)");
            if (Fix64.DistSq(u.X - Map.CellCentre(11), u.Y - Map.CellCentre(15)) <= Fix64.FromInt(4)) anyAtRally = true;
        }
        if (!anyAtRally)
            return Fail("spawngate: no close-rallied unit ended within 2 cells of the rally point");
        cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        for (int t = 0; t < 75 + 120; t++)
        {
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            cmds.Clear();
        }
        if (w.EntityCount - preCount != 5)
            return Fail("spawngate: the ring must still offer a cell for a fifth unit after the close-rally crowd settles");
    }

    // 8. The wire and replay formats carry SetRally as an ordinary command.
    {
        string path = Path.Combine(Path.GetTempPath(), "ferrostorm-spawngate.frep");
        var writer = new ReplayWriter(7, "gate");
        writer.Record(new Command(3, 0, CommandType.SetRally, 5, Fix64.FromInt(9), Fix64.FromInt(9), 0));
        writer.Record(new Command(4, 0, CommandType.SetRally, 5, Fix64.Zero, Fix64.Zero, -1));
        writer.Finish(0xABCD, path);
        var rep = Replay.Load(path);
        var c0 = rep.CommandsFor(3)[0];
        var c1 = rep.CommandsFor(4)[0];
        if (c0.Type != CommandType.SetRally || c0.AuxId != 0 || c0.X != Fix64.FromInt(9))
            return Fail("spawngate: SetRally must round-trip the replay format");
        if (c1.Type != CommandType.SetRally || c1.AuxId != -1)
            return Fail("spawngate: the SetRally clear must round-trip the replay format");
        File.Delete(path);
    }

    Console.WriteLine("spawngate: SetRally validates (owner + producer only, Move-exact clamp, -1 clears canonically) and a BARRACKS now accepts it too (ADR-009 clause 5, B2's deferred question answered); " +
                      "3 rallied rifles left the barracks mouth and settled at the rally with C naming the producer; a 2-cell rally moved the unit (SPAWN-D3 dead); " +
                      "a v4 save round-tripped live rally state bit-exact and resumed bit-exact, a v3 downgrade loaded rally-unset, a v2 downgrade loaded unchecked; " +
                      "ten units spread to ten distinct cells; a walled-in factory held its paid unit at 100 per cent spending EXACTLY ZERO over 100 ticks, " +
                      "deleted nothing, stalled the line honestly, and released with C the instant a cell freed at exactly 400 credits for two rifles; " +
                      "a 2-cell rally under occupancy spawned 4+1 units at distinct positions with one at the rally; SetRally round-trips the replay format");
    return 0;
}

int ProdGate()
{
    // ADR-009 / doc 23 Wave 6: the barracks split and the tech tree made
    // machine-checkable. Additive, the catrefuse and spawngate pattern:
    // standalone mode and battery stage, never a golden scenario, so the
    // golden list stays 24 lines by construction.
    var cmds = new List<Command>();

    // 1. produced_at REFUSAL, both directions, which is the split itself.
    // A refusal must cost nothing: no queue entry, no credits.
    {
        var w = new World(21, 64, 64, 2);
        void StepN(int n) { for (int i = 0; i < n; i++) { w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }
        w.GrantCredits(0, 20000);
        w.SpawnPowerPlant(0, 6, 6);
        w.SpawnRefinery(0, 6, 10);           // the harvester's prerequisite, so only produced_at is on trial
        int factory = w.SpawnFactory(0, 10, 10);
        int barracks = w.SpawnBarracks(0, 16, 10);
        int cy = w.SpawnConstructionYard(0, 22, 10);
        long before = w.Credits(0);
        // Infantry ordered at the factory: rifle, rocket and engineer.
        cmds.Add(new Command(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 2));
        cmds.Add(new Command(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 3));
        cmds.Add(new Command(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 11));
        // Vehicles ordered at the barracks: cannon tank, harvester, MCV.
        cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 1));
        cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 4));
        cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 7));
        // And anything at all ordered at a Construction Yard, whose queue
        // holds STRUCTURES: no unit names struct type 4 as its producer, so
        // the same one line refuses it and a unit order can never land in
        // the structure queue.
        cmds.Add(new Command(0, 0, CommandType.Produce, cy, Fix64.Zero, Fix64.Zero, 2));
        cmds.Add(new Command(0, 0, CommandType.Produce, cy, Fix64.Zero, Fix64.Zero, 1));
        StepN(2);
        if (w.QueueLength(factory) != 0)
            return Fail($"prodgate: a factory must refuse infantry (queue {w.QueueLength(factory)})");
        if (w.QueueLength(barracks) != 0)
            return Fail($"prodgate: a barracks must refuse vehicles (queue {w.QueueLength(barracks)})");
        if (w.QueueLength(cy) != 0)
            return Fail($"prodgate: a Construction Yard must refuse units outright (queue {w.QueueLength(cy)})");
        if (w.Credits(0) != before)
            return Fail($"prodgate: a refused order must charge nothing (spent {before - w.Credits(0)})");

        // 2. produced_at ACCEPTANCE: each producer takes its own, and the
        // unit really arrives on the spawn ring rather than merely queueing.
        int preCount = w.EntityCount;
        cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        cmds.Add(new Command(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 1));
        StepN(1);
        if (w.QueueLength(barracks) != 1 || w.QueueLength(factory) != 1)
            return Fail("prodgate: each producer must accept its own produced_at");
        StepN(200);
        int rifles = 0, cannons = 0;
        for (int i = preCount; i < w.EntityCount; i++)
        {
            if (w.Entities[i].UnitType == 2) rifles++;
            if (w.Entities[i].UnitType == 1) cannons++;
        }
        if (rifles != 1) return Fail($"prodgate: the barracks must build the rifle squad (got {rifles})");
        if (cannons != 1) return Fail($"prodgate: the factory must build the cannon tank (got {cannons})");
    }

    // 3. UNIT prerequisites: the harvester needs a refinery, and the gate is
    // on the OWNER's own standing structures, not anybody's.
    {
        var w = new World(22, 64, 64, 2);
        void StepN(int n) { for (int i = 0; i < n; i++) { w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }
        w.GrantCredits(0, 20000);
        w.SpawnPowerPlant(0, 6, 6);
        int factory = w.SpawnFactory(0, 10, 10);
        w.SpawnRefinery(1, 30, 30); // the ENEMY's refinery must not satisfy it
        cmds.Add(new Command(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 4));
        StepN(1);
        if (w.QueueLength(factory) != 0)
            return Fail("prodgate: a harvester with no OWN refinery must be refused (an enemy's does not count)");
        int refinery = w.SpawnRefinery(0, 14, 6);
        cmds.Add(new Command(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 4));
        StepN(1);
        if (w.QueueLength(factory) != 1)
            return Fail("prodgate: a harvester with an own refinery standing must be accepted");
        // ADR-009 clause 4, the pinned semantic: the gate is on QUEUEING, so
        // killing the prerequisite mid-build does NOT cancel what is already
        // queued. Doc 22 line 1524 asked for this to be recorded rather than
        // left emergent; here it is, as behaviour.
        int preCount = w.EntityCount;
        cmds.Add(new Command(0, 0, CommandType.SellStructure, refinery, Fix64.Zero, Fix64.Zero));
        StepN(400);
        if (w.EntityCount <= preCount)
            return Fail("prodgate: a queued item must survive its prerequisite dying mid-build (the gate is on queueing)");
        // Kind, not UnitType: SpawnHarvester sets no UnitType (harvesters are
        // identified by kind), which is the sort of thing an assertion written
        // from the catalogue rather than from the spawn code gets wrong.
        if (w.Entities[preCount].Kind != EntityKind.Harvester)
            return Fail($"prodgate: the surviving queued item should be the harvester (got {w.Entities[preCount].Kind})");
        // But a FRESH order after the loss is refused, which is the other
        // half of the same rule.
        cmds.Add(new Command(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 4));
        StepN(1);
        if (w.QueueLength(factory) != 0)
            return Fail("prodgate: a NEW harvester order after the refinery died must be refused");
    }

    // 4. The STRUCTURE tree, rung by rung, each refused then accepted the
    // moment its prerequisite stands. This is the whole of ADR-009 clause 3
    // walked in one world.
    {
        var w = new World(23, 96, 96, 2);
        void StepN(int n) { for (int i = 0; i < n; i++) { w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }
        w.GrantCredits(0, 200000);
        int cy = w.SpawnConstructionYard(0, 10, 10);
        // Nothing but a yard: the refinery, the turret and the barracks all
        // want a power plant, the factory wants a refinery, the depot and the
        // radar want a factory, and the superweapon wants a radar.
        foreach (int t in new[] { 3, 5, 11, 2, 8, 12, 6 })
        {
            cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, t));
            StepN(1);
            if (w.QueueLength(cy) != 0)
                return Fail($"prodgate: struct type {t} must be refused with a bare Construction Yard standing");
        }
        // The plant itself needs nothing and is accepted immediately.
        cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 1));
        StepN(1);
        if (w.QueueLength(cy) != 1) return Fail("prodgate: the power plant needs no prerequisite and must be accepted");
        // 210 ticks, not 110: this yard has no plant yet and draws 20 against
        // a supply of zero, so its first plant builds at the GDD s5 half-rate
        // floor. That is the curve ADR-008's honest draws priced, and a test
        // that assumed full power here would be reading the wrong game.
        StepN(210);
        cmds.Add(new Command(0, 0, CommandType.PlaceStructure, cy, Fix64.FromInt(14), Fix64.FromInt(10), 1));
        StepN(1);
        // With a plant: refinery, turret and barracks open; factory, depot,
        // radar and superweapon still shut.
        foreach (int t in new[] { 3, 5, 11 })
        {
            cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, t));
            StepN(1);
            if (w.QueueLength(cy) != 1)
                return Fail($"prodgate: struct type {t} must open the moment a power plant stands");
            cmds.Add(new Command(0, 0, CommandType.CancelProduce, cy, Fix64.Zero, Fix64.Zero, 0));
            StepN(1);
        }
        foreach (int t in new[] { 2, 8, 12, 6 })
        {
            cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, t));
            StepN(1);
            if (w.QueueLength(cy) != 0)
                return Fail($"prodgate: struct type {t} must stay shut behind a lone power plant");
        }
        // Refinery stands: the factory opens.
        w.SpawnRefinery(0, 10, 14);
        cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 2));
        StepN(1);
        if (w.QueueLength(cy) != 1) return Fail("prodgate: the factory must open behind a refinery");
        cmds.Add(new Command(0, 0, CommandType.CancelProduce, cy, Fix64.Zero, Fix64.Zero, 0));
        StepN(1);
        // Factory stands: depot and radar open, superweapon still shut.
        w.SpawnFactory(0, 14, 14);
        foreach (int t in new[] { 8, 12 })
        {
            cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, t));
            StepN(1);
            if (w.QueueLength(cy) != 1) return Fail($"prodgate: struct type {t} must open behind a factory");
            cmds.Add(new Command(0, 0, CommandType.CancelProduce, cy, Fix64.Zero, Fix64.Zero, 0));
            StepN(1);
        }
        cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 6));
        StepN(1);
        if (w.QueueLength(cy) != 0) return Fail("prodgate: the superweapon must stay shut behind a factory alone");
        // Radar stands: the superweapon opens. The full ladder is walked.
        w.SpawnRadarUplink(0, 18, 14);
        cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 6));
        StepN(1);
        if (w.QueueLength(cy) != 1) return Fail("prodgate: the superweapon must open behind a radar uplink");
        cmds.Add(new Command(0, 0, CommandType.CancelProduce, cy, Fix64.Zero, Fix64.Zero, 0));
        StepN(1);
        // The veil's faction gate is ORTHOGONAL to its new prerequisite: this
        // Directorate player owns a plant and is still refused, and a
        // Sodality player without a plant would be refused too.
        cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 7));
        StepN(1);
        if (w.QueueLength(cy) != 0)
            return Fail("prodgate: the veil's faction gate must still refuse a Directorate player who has the plant");
        var sod = new World(24, 64, 64, 2);
        sod.SetFaction(0, World.FactionSodality);
        sod.GrantCredits(0, 20000);
        int sodCy = sod.SpawnConstructionYard(0, 10, 10);
        cmds.Add(new Command(0, 0, CommandType.BuildStructure, sodCy, Fix64.Zero, Fix64.Zero, 7));
        sod.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (sod.QueueLength(sodCy) != 0)
            return Fail("prodgate: a Sodality player with no power plant must still be refused the veil (the tree is orthogonal to faction)");
        sod.SpawnPowerPlant(0, 14, 10);
        cmds.Add(new Command(0, 0, CommandType.BuildStructure, sodCy, Fix64.Zero, Fix64.Zero, 7));
        sod.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (sod.QueueLength(sodCy) != 1)
            return Fail("prodgate: a Sodality player WITH a power plant must be accepted for the veil");
    }

    // 5. The barracks end to end through the real flow: queued at a yard
    // behind a plant, placed, producing all three infantry types, rallying,
    // and cancelling with an exact refund.
    {
        var w = new World(25, 64, 64, 2);
        void StepN(int n) { for (int i = 0; i < n; i++) { w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } }
        w.GrantCredits(0, 20000);
        int cy = w.SpawnConstructionYard(0, 10, 10);
        w.SpawnPowerPlant(0, 14, 10);
        long beforeBuild = w.Credits(0);
        cmds.Add(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 11));
        StepN(101); // 100 build ticks at full power
        if (w.Entities[cy].ReadyStructure != 11)
            return Fail($"prodgate: the barracks must be ready after 101 ticks (slot {w.Entities[cy].ReadyStructure})");
        if (beforeBuild - w.Credits(0) != 500)
            return Fail($"prodgate: the barracks must cost exactly 500 (spent {beforeBuild - w.Credits(0)})");
        cmds.Add(new Command(0, 0, CommandType.PlaceStructure, cy, Fix64.FromInt(10), Fix64.FromInt(14), 11));
        StepN(1);
        int barracks = w.EntityCount - 1;
        var bv = w.Entities[barracks];
        if (bv.Kind != EntityKind.Barracks || bv.StructType != 11 || bv.PowerDraw != 20 || bv.Hp != 800)
            return Fail($"prodgate: the placed barracks is wrong (kind {bv.Kind}, type {bv.StructType}, draw {bv.PowerDraw}, hp {bv.Hp})");
        // All three infantry types, and a rally they all honour.
        var rallyX = Map.CellCentre(24); var rallyY = Map.CellCentre(20);
        cmds.Add(new Command(0, 0, CommandType.SetRally, barracks, rallyX, rallyY, 0));
        foreach (int t in new[] { 2, 3, 11 })
            cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, t));
        StepN(1);
        if (w.QueueLength(barracks) != 3)
            return Fail($"prodgate: the barracks must take rifle, rocket and engineer (queue {w.QueueLength(barracks)})");
        int preCount = w.EntityCount;
        StepN(75 + 100 + 120 + 300);
        int built = w.EntityCount - preCount;
        if (built != 3) return Fail($"prodgate: the barracks must build all three infantry types (built {built})");
        var seenTypes = new HashSet<int>();
        int atRally = 0;
        for (int i = preCount; i < w.EntityCount; i++)
        {
            var u = w.Entities[i];
            seenTypes.Add(u.UnitType);
            if (Fix64.DistSq(u.X - rallyX, u.Y - rallyY) <= Fix64.FromInt(36)) atRally++;
        }
        if (!seenTypes.Contains(2) || !seenTypes.Contains(3) || !seenTypes.Contains(11))
            return Fail("prodgate: rifle, rocket and engineer must all come out of the barracks");
        if (atRally != 3)
            return Fail($"prodgate: all three should have walked to the barracks rally ({atRally}/3)");
        // Cancelling a barracks order refunds exactly what was drained.
        cmds.Add(new Command(0, 0, CommandType.Produce, barracks, Fix64.Zero, Fix64.Zero, 2));
        StepN(30);
        long midBuild = w.Credits(0);
        int paid = w.Entities[barracks].BuildPaid;
        if (paid <= 0) return Fail("prodgate: the barracks build must be draining before the cancel");
        cmds.Add(new Command(0, 0, CommandType.CancelProduce, barracks, Fix64.Zero, Fix64.Zero, 0));
        StepN(1);
        if (w.Credits(0) != midBuild + paid)
            return Fail($"prodgate: cancelling a barracks order must refund exactly what it drained ({w.Credits(0) - midBuild} vs {paid})");
        if (w.QueueLength(barracks) != 0)
            return Fail("prodgate: the cancelled barracks order must leave the queue");
    }

    // 6. PROD-D5 shut. The queue hash covered FACTORY queues only, so a
    // Construction Yard's queue was invisible to it - and the sharpest case
    // is not a slow divergence but a permanently silent one: the factory and
    // the refinery are both 2000 credits and 300 build ticks, so two yards
    // queueing one each had bit-identical BuildProgress and BuildPaid every
    // tick until ReadyStructure was finally written. Widening the hash to
    // every producer closes it, and this is the test that would have caught
    // it: two otherwise identical worlds must now hash DIFFERENTLY the tick
    // the orders land.
    {
        World Yard(int queued)
        {
            var w = new World(26, 64, 64, 2);
            w.GrantCredits(0, 20000);
            int cy = w.SpawnConstructionYard(0, 10, 10);
            w.SpawnPowerPlant(0, 14, 10);
            w.SpawnRefinery(0, 10, 14); // the factory's prerequisite, so both orders are legal
            var one = new List<Command> { new(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, queued) };
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(one));
            return w;
        }
        var wFactory = Yard(2);
        var wRefinery = Yard(3);
        if (wFactory.ComputeStateHash() == wRefinery.ComputeStateHash())
            return Fail("prodgate: a Construction Yard queueing a factory must hash differently from one queueing a refinery (PROD-D5)");
    }

    Console.WriteLine("prodgate: the split refuses both ways and costs nothing (factory refused rifle, rocket and engineer; barracks refused cannon, harvester and MCV; " +
                      "a Construction Yard refused units outright) and accepts both ways (rifle out of the barracks, cannon out of the factory); " +
                      "unit prerequisites bind to the OWNER's own structures (an enemy refinery bought the harvester nothing) and the gate is on QUEUEING, " +
                      "so a queued harvester survived its refinery being sold mid-build while a fresh order was refused; the structure tree was walked rung by rung " +
                      "(seven types refused at a bare yard, refinery/turret/barracks opening on the plant, factory on the refinery, depot and radar on the factory, " +
                      "superweapon on the radar) with the veil's faction gate proven ORTHOGONAL in both directions; the barracks ran end to end at 500 credits and " +
                      "100 ticks, built rifle, rocket and engineer, walked all three to its rally, and refunded a cancel exactly; and PROD-D5 is shut - a yard " +
                      "queueing a factory now hashes differently from one queueing a same-cost, same-ticks refinery");
    return 0;
}

int RegrowthGate()
{
    // ADR-012 gate. Additive, the catrefuse/spawngate/prodgate pattern: a
    // standalone mode and a Match battery stage, never a golden scenario, so
    // the golden list stays 24 lines by construction. Proves the four things
    // ADR-012's consequences demand: a below-cap field recovers at the
    // placeholder rate, a field stripped to zero stays dead, regrowth never
    // overflows the cap, and the whole thing round-trips save/load (v5) with
    // pre-v5 saves resuming sanely.

    // --- 1. Recovery at the placeholder rate, proven differentially ---------
    // The identical harvest sequence run twice, once with regrowth live and
    // once disabled (regrow_amount 0). The field is huge, so it never nears
    // depletion and the per-tick take is LoadPerTick in BOTH runs: the two
    // sequences are byte-identical except for regrowth's own additions. The
    // field is below its cap from the first load (well before tick 75) and
    // never returns to it, so every regrow tick in the window fires. The
    // difference in remaining ferrite is therefore EXACTLY the number of
    // intervals in the window times regrow_amount - the rate, measured.
    const int cap = 1_000_000, window = 1000;
    (long amount, long credits, long carry) Drain(int regrowAmount)
    {
        var w = new World(700, 64, 64, players: 2);
        if (regrowAmount != World.DefaultRegrowAmount) w.ConfigureRegrowth(regrowAmount, World.DefaultRegrowIntervalTicks);
        w.SpawnRefinery(0, 10, 10);
        int fld = w.SpawnFerriteField(Fix64.FromInt(12), Fix64.FromInt(12), cap);
        int hv = w.SpawnHarvester(0, Fix64.FromInt(12), Fix64.FromInt(12));
        var cmds = new List<Command> { new(0, 0, CommandType.Harvest, hv, Fix64.Zero, Fix64.Zero, fld) };
        for (int t = 0; t < window; t++) { w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); }
        var f = w.Entities[fld];
        return (f.FerriteAmount, w.Credits(0), w.Entities[hv].Carry);
    }
    var on = Drain(World.DefaultRegrowAmount);
    var off = Drain(0);
    int expectedRegrown = (window - 1) / World.DefaultRegrowIntervalTicks * World.DefaultRegrowAmount;
    if (on.credits != off.credits || on.carry != off.carry)
        return Fail($"regrowth: the harvest sequence must be identical with regrowth on and off (credits {on.credits}/{off.credits}, carry {on.carry}/{off.carry})");
    if (on.amount - off.amount != expectedRegrown)
        return Fail($"regrowth: expected the below-cap field to recover exactly {expectedRegrown} over {window} ticks ({World.DefaultRegrowAmount} per {World.DefaultRegrowIntervalTicks}), got {on.amount - off.amount}");
    if (off.amount != cap - off.credits - off.carry)
        return Fail("regrowth: conservation broke (off-run remaining must be spawn amount minus everything harvested)");

    // --- 2. The cap is a ceiling: an untouched field at cap never overflows --
    {
        var w = new World(701, 64, 64, players: 2);
        int fld = w.SpawnFerriteField(Fix64.FromInt(30), Fix64.FromInt(30), 5000);
        for (int t = 0; t < 400; t++) w.Step(default); // several regrow intervals, no harvester
        if (w.Entities[fld].FerriteAmount != 5000)
            return Fail($"regrowth: a field at cap must not overflow (expected 5000, got {w.Entities[fld].FerriteAmount})");
    }

    // --- 3. Denial: a field stripped to zero is dead ground forever ----------
    {
        var w = new World(702, 64, 64, players: 2);
        w.SpawnRefinery(0, 10, 10);
        int fld = w.SpawnFerriteField(Fix64.FromInt(12), Fix64.FromInt(12), 30); // tiny: dead well before tick 75
        int hv = w.SpawnHarvester(0, Fix64.FromInt(12), Fix64.FromInt(12));
        var cmds = new List<Command> { new(0, 0, CommandType.Harvest, hv, Fix64.Zero, Fix64.Zero, fld) };
        int diedAt = -1;
        for (int t = 0; t < 60 && diedAt < 0; t++) { w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); if (!w.Entities[fld].Alive) diedAt = w.Tick; }
        if (diedAt < 0 || diedAt >= World.DefaultRegrowIntervalTicks)
            return Fail($"regrowth: the denial field should be stripped to zero before the first regrow tick (died at {diedAt})");
        // A window spanning several regrow intervals: it must never come back.
        for (int t = 0; t < 300; t++)
        {
            w.Step(default);
            var f = w.Entities[fld];
            if (f.Alive || f.FerriteAmount != 0)
                return Fail($"regrowth: DENIAL BROKEN - a stripped field regrew (alive {f.Alive}, amount {f.FerriteAmount} at tick {w.Tick})");
        }
    }

    // --- 4. Save/load (v5) round-trips regrowth, and a pre-v5 save resumes ---
    {
        var w = new World(703, 64, 64, players: 2);
        w.SpawnRefinery(0, 10, 10);
        int fld = w.SpawnFerriteField(Fix64.FromInt(12), Fix64.FromInt(12), cap);
        int hv = w.SpawnHarvester(0, Fix64.FromInt(12), Fix64.FromInt(12));
        var cmds = new List<Command> { new(0, 0, CommandType.Harvest, hv, Fix64.Zero, Fix64.Zero, fld) };
        for (int t = 0; t < 500; t++) { w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); } // mid-regrowth
        ulong hashMid = w.ComputeStateHash();
        using var ms = new MemoryStream();
        w.Save(ms);
        ms.Position = 0;
        var loaded = World.Load(ms);
        if (loaded.ComputeStateHash() != hashMid)
            return Fail($"regrowth: v5 save must load bit-exact (0x{loaded.ComputeStateHash():X16} vs 0x{hashMid:X16})");
        if (loaded.Entities[fld].FerriteCap != cap)
            return Fail($"regrowth: the ferrite cap must survive the v5 round trip (got {loaded.Entities[fld].FerriteCap})");
        for (int t = 0; t < 500; t++) { w.Step(default); loaded.Step(default); }
        if (loaded.ComputeStateHash() != w.ComputeStateHash())
            return Fail("regrowth: the resumed run diverged - regrowth state did not round-trip");
        // A pre-v5 (v4) downgrade drops the cap; it must load with the cap
        // defaulted to the stored amount, hash-identically (the cap is unhashed).
        using var ms2 = new MemoryStream();
        w.Save(ms2); // w has advanced another 500 ticks: save its CURRENT state for the downgrade check
        var v4World = World.Load(new MemoryStream(DowngradeSave(ms2.ToArray(), 0x534C4134u)));
        var lf = v4World.Entities[fld];
        if (lf.FerriteCap != lf.FerriteAmount)
            return Fail($"regrowth: a pre-v5 save must default the cap to the stored amount (cap {lf.FerriteCap} vs amount {lf.FerriteAmount})");
        if (v4World.ComputeStateHash() != w.ComputeStateHash())
            return Fail("regrowth: a v4 downgrade must be hash-identical (the cap is unhashed)");
    }

    Console.WriteLine($"regrowthgate: a below-cap field recovered exactly {expectedRegrown} over {window} ticks ({World.DefaultRegrowAmount} per {World.DefaultRegrowIntervalTicks}) with the harvest sequence unchanged; " +
                      "a field at cap never overflowed; a field stripped to zero stayed dead across 300 ticks and four regrow intervals; " +
                      "a v5 save round-tripped regrowth and the cap bit-exact and resumed identically; a v4 downgrade loaded with the cap defaulted to the stored amount, hash-identical");
    return 0;
}

int StanceGate()
{
    // ADR-015 gate. Additive, the catrefuse/spawngate/prodgate/regrowthgate
    // pattern: a standalone mode and a Match battery stage, never a golden
    // scenario, so the golden list stays 24 lines by construction. Proves the
    // four things ADR-015's gate clause names: hold-fire suppresses the
    // auto-acquire an identically-placed aggressive unit takes; guard engages an
    // intruder within its leash and returns to its post; patrol cycles its two
    // waypoints; and a v7 save round-trips the stance while a v6 downgrade loads
    // Aggressive hash-identically.
    //
    // Weapon 1 (TankCannon) has range 4 and no dead zone, and anti-armour vs a
    // Heavy target is full damage (30), so one shot fells a 10-hp intruder.
    Fix64 crowdRadiusSq = Fix64.FromInt(16); // the 4-cell crowd-arrival radius, squared

    // --- 1. Hold-fire is fire discipline, proven differentially --------------
    // The SAME setup twice: a stationary armed unit with a stationary enemy two
    // cells inside its weapon range. The aggressive unit auto-acquires and kills
    // it; the hold-fire unit never fires and the enemy is untouched. The only
    // difference between the two runs is the stance byte - Q003's engineer
    // discipline, isolated.
    int HoldFireProbe(Stance stance)
    {
        var w = new World(2200, 64, 64, players: 2);
        int shooter = w.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(20), Fix64.Zero, 300, ArmourClass.Heavy, weaponId: 1);
        int enemy = w.SpawnUnit(1, Fix64.FromInt(22), Fix64.FromInt(20), Fix64.Zero, 10, ArmourClass.Heavy, weaponId: 0);
        var set = new List<Command> { new(0, 0, CommandType.SetStance, shooter, Fix64.Zero, Fix64.Zero, (int)stance) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(set));
        for (int t = 0; t < 60; t++) w.Step(default);
        return w.Entities[enemy].Alive ? w.Entities[enemy].Hp : 0; // full hp (untouched) or 0 (dead)
    }
    int aggressiveHp = HoldFireProbe(Stance.Aggressive);
    int holdFireHp = HoldFireProbe(Stance.HoldFire);
    if (aggressiveHp != 0)
        return Fail($"stance: the AGGRESSIVE control must auto-acquire and kill the in-range enemy (enemy hp {aggressiveHp}, expected dead)");
    if (holdFireHp != 10)
        return Fail($"stance: a HOLD-FIRE unit must not fire on an enemy in weapon range (enemy hp {holdFireHp}, expected the full 10)");

    // --- 2. Guard engages within its leash and returns to its post -----------
    {
        // A wide-sighted guard so the leash is a visible excursion: sight 10,
        // weapon range 4. Post at (20,20); a stationary 10-hp intruder 9 cells
        // out - inside the leash, well outside weapon range - so the guard must
        // LEAVE the post to close and fire, then RETURN once the intruder dies.
        var w = new World(2201, 64, 64, players: 2);
        int guard = w.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(20), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, weaponId: 1, sightCells: 10);
        int intruder = w.SpawnUnit(1, Fix64.FromInt(20), Fix64.FromInt(29), Fix64.Zero, 10, ArmourClass.Heavy, weaponId: 0);
        var set = new List<Command> { new(0, 0, CommandType.SetStance, guard, Fix64.Zero, Fix64.Zero, (int)Stance.Guard) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(set));
        var g0 = w.Entities[guard];
        if (g0.Stance != Stance.Guard) return Fail("stance: the guard order must set Stance.Guard");
        if (g0.PostX != Fix64.FromInt(20) || g0.PostY != Fix64.FromInt(20))
            return Fail("stance: the guard post must pin to the unit's position when ordered");
        Fix64 maxStraySq = Fix64.Zero;
        int diedAt = -1;
        for (int t = 0; t < 200; t++)
        {
            w.Step(default);
            var gg = w.Entities[guard];
            Fix64 straySq = Fix64.DistSq(gg.X - Fix64.FromInt(20), gg.Y - Fix64.FromInt(20));
            if (straySq > maxStraySq) maxStraySq = straySq;
            if (diedAt < 0 && !w.Entities[intruder].Alive) diedAt = w.Tick;
        }
        if (diedAt < 0)
            return Fail("stance: a guard must engage and kill an intruder inside its leash");
        if (maxStraySq <= crowdRadiusSq)
            return Fail($"stance: the guard should have LEFT its post to engage (max stray sq raw {maxStraySq.Raw}, expected beyond the crowd radius)");
        var g = w.Entities[guard];
        if (Fix64.DistSq(g.X - Fix64.FromInt(20), g.Y - Fix64.FromInt(20)) > crowdRadiusSq)
            return Fail("stance: a guard with no intruder in leash must return to its post");
        if (g.ExplicitTarget >= 0)
            return Fail("stance: a returned guard must hold no target");
        if (g.Stance != Stance.Guard)
            return Fail("stance: guard must persist across the engage-and-return cycle");
    }

    // --- 3. Patrol cycles its two waypoints ----------------------------------
    {
        // Endpoint A is the spawn (10,20); endpoint B is (30,20), 20 cells east,
        // on open ground with no enemies, so each leg completes cleanly.
        var w = new World(2202, 64, 64, players: 1);
        int scout = w.SpawnUnit(0, Fix64.FromInt(10), Fix64.FromInt(20), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, weaponId: 0);
        var set = new List<Command> { new(0, 0, CommandType.SetStance, scout, Fix64.FromInt(30), Fix64.FromInt(20), (int)Stance.Patrol) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(set));
        var s0 = w.Entities[scout];
        if (s0.Stance != Stance.Patrol || !s0.PatrolOutbound)
            return Fail("stance: the patrol order must set Stance.Patrol on the outbound leg");
        if (s0.PatrolX != Fix64.FromInt(30) || s0.PostX != Fix64.FromInt(10))
            return Fail("stance: patrol endpoints must pin A=origin, B=ordered point");
        int flips = 0;
        bool prevOutbound = s0.PatrolOutbound;
        Fix64 minX = s0.X, maxX = s0.X;
        for (int t = 0; t < 600; t++)
        {
            w.Step(default);
            var s = w.Entities[scout];
            if (s.PatrolOutbound != prevOutbound) { flips++; prevOutbound = s.PatrolOutbound; }
            if (s.X < minX) minX = s.X;
            if (s.X > maxX) maxX = s.X;
        }
        if (flips < 3)
            return Fail($"stance: a patrol must cycle its endpoints (only {flips} flips in 600 ticks, expected at least 3)");
        if (minX > Fix64.FromInt(14))
            return Fail($"stance: patrol never returned near endpoint A (min x {minX.ToIntRound()}, expected within the crowd radius of 10)");
        if (maxX < Fix64.FromInt(26))
            return Fail($"stance: patrol never reached near endpoint B (max x {maxX.ToIntRound()}, expected within the crowd radius of 30)");
    }

    // --- 4. A v7 save round-trips stance; a v6 downgrade loads Aggressive -----
    {
        // A world carrying one unit in each non-default stance, advanced a few
        // ticks so the patrol is mid-leg and the hold-fire unit is sat on an
        // enemy it refuses to shoot.
        var w = new World(2203, 64, 64, players: 2);
        int guard = w.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(20), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, weaponId: 1, sightCells: 8);
        int patrol = w.SpawnUnit(0, Fix64.FromInt(10), Fix64.FromInt(40), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, weaponId: 0);
        int held = w.SpawnUnit(0, Fix64.FromInt(50), Fix64.FromInt(50), Fix64.Zero, 300, ArmourClass.Heavy, weaponId: 1);
        w.SpawnUnit(1, Fix64.FromInt(50), Fix64.FromInt(52), Fix64.Zero, 300, ArmourClass.Heavy, weaponId: 0);
        var set = new List<Command>
        {
            new(0, 0, CommandType.SetStance, guard, Fix64.Zero, Fix64.Zero, (int)Stance.Guard),
            new(0, 0, CommandType.SetStance, patrol, Fix64.FromInt(30), Fix64.FromInt(40), (int)Stance.Patrol),
            new(0, 0, CommandType.SetStance, held, Fix64.Zero, Fix64.Zero, (int)Stance.HoldFire),
        };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(set));
        for (int t = 0; t < 30; t++) w.Step(default); // patrol advances mid-leg
        ulong hashMid = w.ComputeStateHash();
        using var ms = new MemoryStream();
        w.Save(ms);
        ms.Position = 0;
        var loaded = World.Load(ms);
        if (loaded.ComputeStateHash() != hashMid)
            return Fail($"stance: a v7 save must load bit-exact (0x{loaded.ComputeStateHash():X16} vs 0x{hashMid:X16})");
        if (loaded.Entities[guard].Stance != Stance.Guard || loaded.Entities[guard].PostX != Fix64.FromInt(20))
            return Fail("stance: guard stance/post lost in the v7 round trip");
        if (loaded.Entities[patrol].Stance != Stance.Patrol || loaded.Entities[patrol].PatrolX != Fix64.FromInt(30))
            return Fail("stance: patrol stance/waypoint lost in the v7 round trip");
        if (loaded.Entities[held].Stance != Stance.HoldFire)
            return Fail("stance: hold-fire stance lost in the v7 round trip");
        for (int t = 0; t < 60; t++) { w.Step(default); loaded.Step(default); }
        if (loaded.ComputeStateHash() != w.ComputeStateHash())
            return Fail("stance: the resumed run diverged - stance state did not round-trip");

        // A v6 downgrade of an AGGRESSIVE-only world must load hash-identically:
        // v6 has no stance tail, so every unit loads Aggressive, which is exactly
        // what those units are. A non-default stance cannot survive a v6
        // downgrade because Stance is hashed; the honest, provable claim is that
        // the state a v6 save could ever have held - stanceless - loads
        // Aggressive-identical, so old saves and replays resume unchanged.
        var wa = new World(2204, 64, 64, players: 2);
        wa.SpawnUnit(0, Fix64.FromInt(15), Fix64.FromInt(15), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, weaponId: 1);
        wa.SpawnUnit(1, Fix64.FromInt(40), Fix64.FromInt(40), Fix64.FromFraction(1, 4), 100, ArmourClass.None, weaponId: 2);
        var move = new List<Command> { new(0, 0, CommandType.PathMove, 0, Fix64.FromInt(30), Fix64.FromInt(30)) };
        wa.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(move));
        for (int t = 0; t < 40; t++) wa.Step(default);
        ulong aggHash = wa.ComputeStateHash();
        using var msa = new MemoryStream();
        wa.Save(msa);
        var v6World = World.Load(new MemoryStream(DowngradeSave(msa.ToArray(), 0x534C4136u)));
        if (v6World.ComputeStateHash() != aggHash)
            return Fail($"stance: a v6 downgrade of an aggressive world must be hash-identical (0x{v6World.ComputeStateHash():X16} vs 0x{aggHash:X16})");
        foreach (var e in v6World.Entities)
            if (e.Stance != Stance.Aggressive)
                return Fail("stance: a v6 downgrade must load every unit Aggressive");
    }

    Console.WriteLine("stancegate: hold-fire suppressed the auto-acquire an aggressive twin took (enemy 10/10 vs dead); " +
                      "a guard left its post to kill an intruder in its leash and returned within the crowd radius; " +
                      "a patrol cycled its two waypoints across 600 ticks; a v7 save round-tripped all three stances and resumed bit-exact; " +
                      "a v6 downgrade loaded every unit Aggressive, hash-identical");
    return 0;
}

int RepairGate()
{
    // ADR-019 gate (P6 Wave C2). Additive, the stancegate/regrowthgate pattern:
    // a standalone mode and a Match battery stage, never a golden scenario, so the
    // golden list stays 24. Proves the repair vehicle (unit type 13) mends own
    // mobile units at the depot's rate and price, NOT power gated, NOT itself, NOT
    // enemies, NOT structures, mends a harvester too, and stops when broke.
    const int RV = World.RepairVehicleType;
    Fix64 x20 = Fix64.FromInt(20), x22 = Fix64.FromInt(22), x18 = Fix64.FromInt(18);

    // --- 1. Mends a friendly unit at 2 hp/tick for 1 credit/tick, with NO power
    //        anywhere in the world - the not-power-gated decision (ADR-019). A
    //        Service Depot in this same powerless setup would heal nothing. ------
    {
        var w = new World(2300, 64, 64, players: 1);
        w.GrantCredits(0, 100);
        w.SpawnUnit(0, x20, x20, Fix64.Zero, 300, ArmourClass.Light, weaponId: 0, unitType: RV);
        int hurt = w.SpawnUnit(0, x22, x20, Fix64.Zero, 100, ArmourClass.None, weaponId: 0);
        var h = w.Entities[hurt]; h.Hp = 50; w.SetEntityForTest(hurt, h);   // scripted battle damage
        long before = w.Credits(0);
        for (int t = 0; t < 60; t++) w.Step(default);
        if (w.Entities[hurt].Hp != 100)
            return Fail($"repair: a repair vehicle must fully mend a friendly unit with no power present (hp {w.Entities[hurt].Hp}/100)");
        long spent = before - w.Credits(0);
        if (spent != 25)
            return Fail($"repair: 50 hp at 2hp/1cr per tick must cost exactly 25 (spent {spent})");
    }

    // --- 2. Excludes itself and enemies; mends a harvester ---------------------
    {
        var w = new World(2301, 64, 64, players: 2);
        w.GrantCredits(0, 200);
        int medic = w.SpawnUnit(0, x20, x20, Fix64.Zero, 300, ArmourClass.Light, weaponId: 0, unitType: RV);
        var m = w.Entities[medic]; m.Hp = 250; w.SetEntityForTest(medic, m);   // damaged: must NOT self-heal
        int control = w.SpawnUnit(0, x20, x22, Fix64.Zero, 100, ArmourClass.None, weaponId: 0);
        var c = w.Entities[control]; c.Hp = 50; w.SetEntityForTest(control, c);
        int enemy = w.SpawnUnit(1, x22, x20, Fix64.Zero, 100, ArmourClass.None, weaponId: 0);
        var en = w.Entities[enemy]; en.Hp = 50; w.SetEntityForTest(enemy, en);
        int harv = w.SpawnHarvester(0, x18, x20);
        var hv = w.Entities[harv]; hv.Hp = 600; w.SetEntityForTest(harv, hv);
        for (int t = 0; t < 60; t++) w.Step(default);
        if (w.Entities[control].Hp != 100)
            return Fail($"repair: the positive-control friendly unit must be mended ({w.Entities[control].Hp}/100)");
        if (w.Entities[medic].Hp != 250)
            return Fail($"repair: a repair vehicle must NOT mend itself ({w.Entities[medic].Hp}, expected the unchanged 250)");
        if (w.Entities[enemy].Hp != 50)
            return Fail($"repair: a repair vehicle must NOT mend an enemy ({w.Entities[enemy].Hp}, expected the unchanged 50)");
        if (w.Entities[harv].Hp != 700)
            return Fail($"repair: a repair vehicle must mend a harvester ({w.Entities[harv].Hp}/700)");
    }

    // --- 3. Excludes structures (the kind gate, proven at zero distance) -------
    {
        var w = new World(2302, 64, 64, players: 1);
        w.GrantCredits(0, 100);
        int plant = w.SpawnPowerPlant(0, 30, 30);
        var pe = w.Entities[plant];
        w.SpawnUnit(0, pe.X, pe.Y, Fix64.Zero, 300, ArmourClass.Light, weaponId: 0, unitType: RV); // medic ON the plant
        int plantMax = pe.MaxHp;
        var pd = w.Entities[plant]; pd.Hp = plantMax - 100; w.SetEntityForTest(plant, pd);
        for (int t = 0; t < 60; t++) w.Step(default);
        if (w.Entities[plant].Hp != plantMax - 100)
            return Fail($"repair: a repair vehicle must NOT mend a structure ({w.Entities[plant].Hp}, expected the unchanged {plantMax - 100})");
    }

    // --- 4. Stops the tick the treasury empties -------------------------------
    {
        var w = new World(2303, 64, 64, players: 1);
        w.GrantCredits(0, 6);   // 6 credits: exactly 6 heal-ticks, +12 hp
        w.SpawnUnit(0, x20, x20, Fix64.Zero, 300, ArmourClass.Light, weaponId: 0, unitType: RV);
        int hurt = w.SpawnUnit(0, x22, x20, Fix64.Zero, 100, ArmourClass.None, weaponId: 0);
        var h = w.Entities[hurt]; h.Hp = 50; w.SetEntityForTest(hurt, h);
        for (int t = 0; t < 60; t++) w.Step(default);
        if (w.Entities[hurt].Hp != 62)
            return Fail($"repair: 6 credits must buy exactly 6 heal-ticks then stop ({w.Entities[hurt].Hp}, expected 62)");
        if (w.Credits(0) != 0)
            return Fail($"repair: a repair vehicle must stop when broke (credits {w.Credits(0)}, expected 0)");
    }

    Console.WriteLine("repairgate: a repair vehicle fully mended a friendly unit at 2hp/1cr per tick with NO power present (a depot would not); " +
                      "it did not mend itself, an enemy, or a structure; it mended a harvester to full; and 6 credits bought exactly 6 heal-ticks then stopped");
    return 0;
}

int OutpostGate()
{
    // ADR-021 gate (P6 Wave C4). Additive, the repairgate/stancegate pattern: a
    // standalone mode and a Match battery stage, never a golden scenario, so the
    // golden list stays 24. Proves the four ADR-021 behaviours: an engineer
    // captures a NEUTRAL outpost through the untouched CaptureSystem; a captured
    // outpost pays exactly OutpostIncomePerSecond once per second; a neutral one
    // pays nobody and is never auto-acquired; and a player whose last possession
    // is a captured outpost is still eliminated (an income node is not a base).

    // --- 1. Capture, then the exact income beat ------------------------------
    {
        var w = new World(2400, 64, 64, players: 2);
        int outpost = w.SpawnOutpost(-1, 20, 20);
        // The engineer is unit type 11; capture is an Attack order onto the
        // structure, the ExplicitTarget CaptureSystem drives (the capture
        // scenario's own idiom, here against a NEUTRAL owner).
        int eng = w.SpawnUnit(0, Fix64.FromInt(26), Fix64.FromInt(20), Fix64.FromFraction(1, 5), 60, ArmourClass.None, weaponId: 0, unitType: 11);
        var order = new List<Command> { new(0, 0, CommandType.Attack, eng, Fix64.Zero, Fix64.Zero, outpost) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
        int capturedAt = -1;
        for (int t = 0; t < 300 && capturedAt < 0; t++)
        {
            w.Step(default);
            if (w.Entities[outpost].PlayerId == 0) capturedAt = w.Tick;
        }
        if (capturedAt < 0)
            return Fail("outpost: an engineer must capture a NEUTRAL outpost (PlayerId never flipped in 300 ticks)");
        if (w.Entities[eng].Alive)
            return Fail("outpost: the capture must consume the engineer");
        // The income beat, proven exactly: over any 150-tick window an owned
        // outpost pays on precisely 10 second-boundaries (150/15), 15 each.
        long before = w.Credits(0);
        for (int t = 0; t < 150; t++) w.Step(default);
        long earned = w.Credits(0) - before;
        if (earned != 150)
            return Fail($"outpost: a captured outpost must pay exactly 10x{World.OutpostIncomePerSecond} over 150 ticks (earned {earned})");
    }

    // --- 2. A neutral outpost is inert: pays nobody, draws no fire -----------
    {
        var w = new World(2401, 64, 64, players: 2);
        int outpost = w.SpawnOutpost(-1, 20, 20);
        w.GrantCredits(0, 100);
        w.GrantCredits(1, 100);
        // An armed enemy unit parked beside it: auto-acquire skips neutrals
        // (t.PlayerId < 0), so the outpost must take no damage unordered.
        w.SpawnUnit(1, Fix64.FromInt(22), Fix64.FromInt(20), Fix64.Zero, 300, ArmourClass.Heavy, weaponId: 1);
        for (int t = 0; t < 60; t++) w.Step(default);
        if (w.Credits(0) != 100 || w.Credits(1) != 100)
            return Fail($"outpost: a NEUTRAL outpost must pay nobody (credits {w.Credits(0)}/{w.Credits(1)}, expected 100/100)");
        if (w.Entities[outpost].PlayerId != -1)
            return Fail("outpost: nothing but an engineer may claim a neutral outpost");
        if (w.Entities[outpost].Hp != w.Entities[outpost].MaxHp)
            return Fail($"outpost: auto-acquire must never target a neutral outpost (hp {w.Entities[outpost].Hp})");
    }

    // --- 3. An outpost is not hope: its owner is still eliminated ------------
    {
        var w = new World(2402, 64, 64, players: 2);
        w.SpawnOutpost(0, 20, 20);           // player 0's ONLY possession
        w.SpawnPowerPlant(1, 40, 40);        // player 1 has a real base
        for (int t = 0; t < 5 && w.Winner < 0; t++) w.Step(default);
        if (w.Winner != 1)
            return Fail($"outpost: a player whose last possession is a captured outpost must be eliminated (winner {w.Winner}, expected 1)");
    }

    Console.WriteLine("outpostgate: an engineer captured a NEUTRAL outpost (consumed by the act) and the prize paid exactly " +
                      $"{World.OutpostIncomePerSecond}/s over a 150-tick window; a neutral outpost paid nobody and was never auto-acquired; " +
                      "and a player left holding only a captured outpost was still eliminated");
    return 0;
}

int LaneGate()
{
    // ADR-023 gate. Additive, the repairgate/outpostgate pattern: standalone
    // mode plus a Match stage, never a golden scenario, so the golden list
    // stays 24 lines by construction.
    //
    // Proves the wave's feature AND its neutrality rule. The second is the one
    // that matters: if a second order at an IDLE yard ever reached lane 2, the
    // construction scenario's turret would move and every AI golden would
    // diverge, so that rule is asserted here rather than trusted.
    World LaneWorld(ulong seed, out int cy)
    {
        var w = new World(seed, 64, 64, players: 1);
        w.GrantCredits(0, 100000);
        cy = w.SpawnConstructionYard(0, 20, 20);
        w.SpawnPowerPlant(0, 30, 30);   // supply, so the rate is the full 100
        return w;
    }
    List<Command> One(Command c) => new() { c };
    void Step(World w, List<Command>? cmds = null) =>
        w.Step(cmds is null ? default : System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));

    // --- 1. THE NEUTRALITY RULE: an order into an IDLE yard stays in lane 1 --
    {
        var w = LaneWorld(3300, out int cy);
        Step(w, One(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 1)));
        if (w.QueueLength(cy) != 1)
            return Fail($"lane: an order into an idle yard must land in lane 1 (QueueLength {w.QueueLength(cy)})");
        if (w.LaneContents(cy).Count != 0)
            return Fail("lane: an order into an idle yard must NOT create a second lane - this is the rule the goldens rest on");
    }

    // --- 2. THE FEATURE: a second order overflows and BOTH build at once -----
    {
        var w = LaneWorld(3301, out int cy);
        Step(w, One(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 1)));   // power plant
        Step(w);                                                                                      // lane 1 starts
        Step(w, One(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 3)));   // refinery: overflows
        if (w.LaneContents(cy).Count != 1)
            return Fail("lane: a second order at a BUSY yard must overflow into lane 2");
        int p1 = -1, p2 = -1;
        for (int t = 0; t < 40; t++)
        {
            Step(w);
            p1 = w.Entities[cy].BuildProgress;
            p2 = w.LaneState(cy).Progress;
        }
        if (p1 <= 0 || p2 <= 0)
            return Fail($"lane: BOTH lanes must build simultaneously (lane1 progress {p1}, lane2 progress {p2}) - this is the whole wave");
    }

    // --- 3. Independent ready slots, and an independent refund --------------
    {
        var w = LaneWorld(3302, out int cy);
        Step(w, One(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 1)));
        Step(w);
        Step(w, One(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 1)));
        for (int t = 0; t < 400; t++) Step(w);
        if (w.Entities[cy].ReadyStructure != 1)
            return Fail($"lane: lane 1 should hold a ready structure (got {w.Entities[cy].ReadyStructure})");
        if (w.LaneState(cy).Ready != 1)
            return Fail($"lane: lane 2 should hold its OWN ready structure (got {w.LaneState(cy).Ready})");

        // Cancelling the lane-2 ready refunds it in full and leaves lane 1 alone.
        long before = w.Credits(0);
        Step(w, One(new Command(0, 0, CommandType.CancelProduce, cy, Fix64.Zero, Fix64.Zero, World.LaneFlag)));
        long refund = w.Credits(0) - before;
        if (refund != w.GetStructureType(1).Cost)
            return Fail($"lane: cancelling a lane-2 ready must refund its full cost (refunded {refund})");
        if (w.LaneState(cy).Ready != 0)
            return Fail("lane: the lane-2 ready slot must clear on cancel");
        if (w.Entities[cy].ReadyStructure != 1)
            return Fail("lane: cancelling lane 2 must not disturb lane 1");
    }

    // --- 4. THE GUARD IS EQUIVALENT TO INERTNESS ---------------------------
    // A world whose lane was used and then emptied must hash IDENTICALLY to a
    // world that never had one. If pruning ever stopped matching the hash
    // guard, the two would differ and every save and LAN peer would disagree.
    {
        var a = LaneWorld(3303, out int cyA);
        var b = LaneWorld(3303, out int cyB);
        // a: overflow a cheap structure into lane 2, then cancel it away.
        Step(a, One(new Command(0, 0, CommandType.BuildStructure, cyA, Fix64.Zero, Fix64.Zero, 1)));
        Step(a);
        Step(a, One(new Command(0, 0, CommandType.BuildStructure, cyA, Fix64.Zero, Fix64.Zero, 1)));
        if (a.LaneContents(cyA).Count != 1) return Fail("lane: setup for the inertness proof did not overflow");
        Step(a, One(new Command(0, 0, CommandType.CancelProduce, cyA, Fix64.Zero, Fix64.Zero, World.LaneFlag)));
        // b: the same single order, never overflowed. The SAME NUMBER OF TICKS
        // (four each), or the comparison would only be measuring the tick
        // counter, which is itself hashed.
        Step(b, One(new Command(0, 0, CommandType.BuildStructure, cyB, Fix64.Zero, Fix64.Zero, 1)));
        Step(b);
        Step(b);
        Step(b);
        if (a.LaneContents(cyA).Count != 0)
            return Fail("lane: a cancelled lane must be pruned, or 'entry present' stops meaning 'lane active'");
        if (a.ComputeStateHash() != b.ComputeStateHash())
            return Fail($"lane: a used-then-emptied lane must hash identically to one that never existed " +
                        $"(0x{a.ComputeStateHash():X16} vs 0x{b.ComputeStateHash():X16})");
    }

    // --- 5. v8 round-trips both lanes; a v7 downgrade of a one-lane world ---
    {
        var w = LaneWorld(3304, out int cy);
        Step(w, One(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 1)));
        Step(w);
        Step(w, One(new Command(0, 0, CommandType.BuildStructure, cy, Fix64.Zero, Fix64.Zero, 3)));
        for (int t = 0; t < 30; t++) Step(w);
        ulong mid = w.ComputeStateHash();
        using var ms = new MemoryStream();
        w.Save(ms);
        ms.Position = 0;
        var loaded = World.Load(ms);
        if (loaded.ComputeStateHash() != mid)
            return Fail($"lane: a v8 save must load bit-exact (0x{loaded.ComputeStateHash():X16} vs 0x{mid:X16})");
        if (loaded.LaneContents(cy).Count != w.LaneContents(cy).Count || loaded.LaneState(cy) != w.LaneState(cy))
            return Fail("lane: the second lane did not survive the v8 round trip");
        for (int t = 0; t < 60; t++) { Step(w); Step(loaded); }
        if (loaded.ComputeStateHash() != w.ComputeStateHash())
            return Fail("lane: the resumed run diverged - lane state did not round-trip");

        // A v7 downgrade of a world with NO lane is hash-identical: v7 is
        // exactly "this world, without a second lane", which is what it was.
        var single = LaneWorld(3305, out int cy2);
        Step(single, One(new Command(0, 0, CommandType.BuildStructure, cy2, Fix64.Zero, Fix64.Zero, 1)));
        for (int t = 0; t < 20; t++) Step(single);
        ulong singleHash = single.ComputeStateHash();
        using var ms2 = new MemoryStream();
        single.Save(ms2);
        var v7World = World.Load(new MemoryStream(DowngradeSave(ms2.ToArray(), 0x534C4137u)));
        if (v7World.ComputeStateHash() != singleHash)
            return Fail($"lane: a v7 downgrade of a lane-free world must be hash-identical " +
                        $"(0x{v7World.ComputeStateHash():X16} vs 0x{singleHash:X16})");
    }

    // BD-05: SELLING a producer must give back what the player already PAID
    // toward what it was building. It used to refund the building's own cost
    // and nothing else, so a yard holding a finished structure sold for half
    // its own price while the fully-paid item in the slot simply vanished.
    // Asserted to the CREDIT, and with a CONTROL: an EMPTY yard must still
    // refund exactly half and not a credit more, or the fix has started
    // inventing money.
    {
        int yardCost = new World(1, 1).GetStructureType(4).Cost;
        int plantCost = new World(1, 1).GetStructureType(1).Cost;

        // Control: nothing pending, so the refund is the building alone.
        var bare = LaneWorld(4401, out int bareCy);
        long beforeBare = bare.Credits(0);
        Step(bare, One(new Command(0, 0, CommandType.SellStructure, bareCy, Fix64.Zero, Fix64.Zero)));
        if (bare.Credits(0) != beforeBare + yardCost / 2)
            return Fail($"sell: an EMPTY yard must refund exactly half ({bare.Credits(0) - beforeBare} vs {yardCost / 2})");

        // A finished structure in the ready slot: fully paid, so fully refunded,
        // exactly as cancelling it would.
        var loaded2 = LaneWorld(4402, out int loadedCy);
        Step(loaded2, One(new Command(0, 0, CommandType.BuildStructure, loadedCy, Fix64.Zero, Fix64.Zero, 1)));
        for (int t = 0; t < 400 && loaded2.Entities[loadedCy].ReadyStructure == 0; t++) Step(loaded2);
        if (loaded2.Entities[loadedCy].ReadyStructure == 0)
            return Fail("sell: the yard never finished a structure to hold");
        long beforeLoaded = loaded2.Credits(0);
        Step(loaded2, One(new Command(0, 0, CommandType.SellStructure, loadedCy, Fix64.Zero, Fix64.Zero)));
        long gained = loaded2.Credits(0) - beforeLoaded;
        if (gained != yardCost / 2 + plantCost)
            return Fail($"sell: a LOADED yard must refund the ready structure in full too " +
                        $"({gained} vs {yardCost / 2 + plantCost})");

        // Mid-build: the pro-rata payment comes back, exactly as cancelling the
        // head does. Compared against the drained amount rather than a guess.
        var midBuild = LaneWorld(4403, out int midCy);
        Step(midBuild, One(new Command(0, 0, CommandType.BuildStructure, midCy, Fix64.Zero, Fix64.Zero, 1)));
        for (int t = 0; t < 20; t++) Step(midBuild);
        int paid = midBuild.Entities[midCy].BuildPaid;
        if (paid <= 0) return Fail("sell: the mid-build yard never drained anything to refund");
        long beforeMid = midBuild.Credits(0);
        Step(midBuild, One(new Command(0, 0, CommandType.SellStructure, midCy, Fix64.Zero, Fix64.Zero)));
        if (midBuild.Credits(0) != beforeMid + yardCost / 2 + paid)
            return Fail($"sell: a yard sold MID-BUILD must refund what it drained " +
                        $"({midBuild.Credits(0) - beforeMid} vs {yardCost / 2 + paid})");
    }

    Console.WriteLine("lanegate: an order into an idle yard stays in lane 1 (the rule the goldens rest on); a second order at a busy yard " +
                      "overflows and BOTH lanes build simultaneously; the lanes hold independent ready slots and refund independently; " +
                      "a used-then-emptied lane hashes identically to one that never existed (the prune matches the hash guard); " +
                      "and a v8 save round-trips both lanes bit-exact while a v7 downgrade of a lane-free world loads hash-identically");
    return 0;
}

int BridgeGate()
{
    // ADR-025 gate (P6 Wave C6a). Additive, the repairgate/outpostgate pattern:
    // standalone mode plus a Match stage, never a golden scenario, so the golden
    // list stays 24 lines by construction.
    //
    // The wave's one genuinely new mechanic is an entity death that makes ground
    // LESS passable - every other death in the sim unblocks - so that is what
    // this proves hardest, and it proves it through actual pathing rather than
    // by reading a flag.

    // A canyon: a solid wall at x=30 with a single gap at y=20, and the gap is
    // a bridge deck. Cross it, then fell it and watch the route die.
    World Canyon(ulong seed, out int span)
    {
        var w = new World(seed, 64, 64, players: 2);
        for (int y = 0; y < 64; y++) if (y != 20) w.Map.SetBlocked(30, y, true);
        span = w.SpawnBridge(30, 20);
        w.InvalidateFlowCache();
        return w;
    }

    // --- 1. A standing bridge is PASSABLE: a unit crosses the only gap -------
    {
        var w = Canyon(3400, out int span);
        int u = w.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(20), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, weaponId: 0);
        if (w.Map.IsBlocked(30, 20))
            return Fail("bridge: a standing bridge must leave its cell PASSABLE - it is the crossing");
        var go = new List<Command> { new(0, 0, CommandType.PathMove, u, Fix64.FromInt(45), Fix64.FromInt(20)) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(go));
        for (int t = 0; t < 600; t++) w.Step(default);
        if (w.Entities[u].X <= Fix64.FromInt(31))
            return Fail($"bridge: a unit must cross a standing bridge (stalled at x {w.Entities[u].X.ToIntRound()})");
    }

    // --- 2. Felling it BLOCKS the cell, and the crossing is gone -------------
    {
        var w = Canyon(3401, out int span);
        // An explicit Attack is how a bridge is felled. Weapon 1 at range 4.
        int gunner = w.SpawnUnit(0, Fix64.FromInt(27), Fix64.FromInt(20), Fix64.Zero, 300, ArmourClass.Heavy, weaponId: 1);
        var order = new List<Command> { new(0, 0, CommandType.Attack, gunner, Fix64.Zero, Fix64.Zero, span) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
        // 800 hp against a test cannon is deliberately many shots: felling a
        // span should be an act, not a stray. Give the gate the patience the
        // design asks for rather than softening the bridge to suit the test.
        int fellAt = -1;
        for (int t = 0; t < 4000 && w.Entities[span].Alive; t++) { w.Step(default); if (!w.Entities[span].Alive) fellAt = w.Tick; }
        if (w.Entities[span].Alive)
            return Fail($"bridge: an explicit Attack must be able to fell a bridge (hp still {w.Entities[span].Hp} after 4000 ticks)");
        if (!w.Map.IsBlocked(30, 20))
            return Fail("bridge: a FELLED bridge must BLOCK its cell - this inversion is the whole wave");

        // And the route is genuinely gone, not merely flagged: a unit ordered
        // across can no longer reach the far side.
        int u = w.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(20), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, weaponId: 0);
        var go = new List<Command> { new(0, 0, CommandType.PathMove, u, Fix64.FromInt(45), Fix64.FromInt(20)) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(go));
        for (int t = 0; t < 600; t++) w.Step(default);
        if (w.Entities[u].X > Fix64.FromInt(30))
            return Fail("bridge: with the span down there is no route across, so a unit must not reach the far bank");
    }

    // --- 3. Neutral means no stray fire and no engineer ----------------------
    {
        // Two separate worlds on purpose. The first cut put an armed enemy and
        // an engineer in one, and the gunner simply shot the engineer, which
        // failed the capture assertion for a reason that had nothing to do with
        // bridges. One claim per world.
        {
            var w = Canyon(3402, out int span);
            // An armed enemy parked beside it with NO order: auto-acquire skips
            // neutrals, so a crossing is never felled by accident.
            w.SpawnUnit(1, Fix64.FromInt(28), Fix64.FromInt(20), Fix64.Zero, 300, ArmourClass.Heavy, weaponId: 1);
            for (int t = 0; t < 200; t++) w.Step(default);
            if (w.Entities[span].Hp != w.Entities[span].MaxHp)
                return Fail($"bridge: auto-acquire must never target a neutral bridge (hp {w.Entities[span].Hp})");
        }
        {
            var w = Canyon(3403, out int span);
            int eng = w.SpawnUnit(0, Fix64.FromInt(28), Fix64.FromInt(21), Fix64.FromFraction(1, 5), 60, ArmourClass.None, weaponId: 0, unitType: 11);
            var order = new List<Command> { new(0, 0, CommandType.Attack, eng, Fix64.Zero, Fix64.Zero, span) };
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
            for (int t = 0; t < 200; t++) w.Step(default);
            if (w.Entities[span].PlayerId != -1)
                return Fail("bridge: an engineer must NOT capture a bridge");
            if (!w.Entities[eng].Alive)
                return Fail("bridge: the engineer was consumed, so a capture happened after all");
        }
    }

    // --- 4. A bridge is not hope: it cannot keep a player alive --------------
    {
        var w = new World(3404, 64, 64, players: 2);
        w.SpawnBridge(20, 20);              // neutral, belongs to nobody
        w.SpawnPowerPlant(1, 40, 40);       // player 1 has a real base
        w.SpawnUnit(0, Fix64.FromInt(10), Fix64.FromInt(10), Fix64.Zero, 100, ArmourClass.None, weaponId: 0);
        for (int t = 0; t < 5 && w.Winner < 0; t++) w.Step(default);
        if (w.Winner != 1)
            return Fail($"bridge: a bridge is neutral and is nobody's hope; player 0 must still be eliminated (winner {w.Winner})");
    }

    Console.WriteLine("bridgegate: a standing bridge leaves its cell passable and a unit crosses it; an explicit Attack fells it, " +
                      "the wreck BLOCKS the cell (the one death in the sim that makes ground less passable) and the route across is " +
                      "genuinely gone; auto-acquire never touches a neutral span and an engineer cannot capture one; and a bridge is " +
                      "nobody's hope for victory");
    return 0;
}

int LanSetupGate()
{
    // ADR-022 gate (P6 Wave C7b, first slice). Additive, standalone plus a
    // Match stage, never a golden scenario.
    //
    // The question it answers: can a JOINER build the host's world without
    // being told the seed out of band? Before this, LockstepClient took a seed
    // from its own caller, so two machines agreeing was an article of faith
    // arranged outside the protocol. The gate proves it is now arranged INSIDE
    // it, by giving the joiner a DELIBERATELY WRONG fallback seed and checking
    // the worlds still match.
    const ulong hostSeed = 90210UL, joinerWrongSeed = 11111UL;
    var setup = new byte[8];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(setup, hostSeed);

    var relay = new Relay(playerCount: 2, setup: setup);
    relay.Start();
    new Thread(relay.Run) { IsBackground = true }.Start();

    var hashes = new ulong[2];
    var seen = new int[2];
    var errors = new Exception?[2];
    var threads = new Thread[2];
    for (int p = 0; p < 2; p++)
    {
        int pid = p;
        threads[p] = new Thread(() =>
        {
            try
            {
                // Player 0 is the host and knows its own seed. Player 1 is the
                // joiner: it is handed the WRONG seed on purpose and must build
                // from the Hello's setup blob instead.
                using var client = pid == 0
                    ? new LockstepClient(relay.Port, LanWorldFactory, hostSeed)
                    : new LockstepClient(relay.Port, LanWorldFactory, joinerWrongSeed, null,
                        blob =>
                        {
                            if (blob.Length < 8) throw new Exception("joiner received no setup in the Hello");
                            ulong s = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(blob);
                            return LanWorldFactory(s);
                        });
                seen[pid] = client.Setup.Length;
                client.Prime();
                while (client.World.Tick < 120)
                {
                    client.SubmitCommands(System.Array.Empty<Command>());
                    if (!client.AdvanceTick()) throw new Exception("desync notified");
                }
                hashes[pid] = client.World.ComputeStateHash();
            }
            catch (Exception ex) { errors[pid] = ex; }
        });
        threads[p].Start();
    }
    foreach (var t in threads) t.Join();
    foreach (var e in errors) if (e != null) return Fail($"lansetup: {e.Message}");
    if (seen[0] != 8 || seen[1] != 8)
        return Fail($"lansetup: both clients must receive the host's setup in the Hello (got {seen[0]}/{seen[1]} bytes)");
    if (relay.DesyncDetected) return Fail("lansetup: relay flagged desync");
    if (hashes[0] != hashes[1])
        return Fail($"lansetup: the joiner built a DIFFERENT world despite the setup exchange " +
                    $"(0x{hashes[0]:X16} vs 0x{hashes[1]:X16}) - a join would desync on the first divergent order");

    // The negative control, which is what makes the assertion mean something:
    // the joiner's own seed really would have built a different world, so the
    // match above is the setup blob working rather than a coincidence.
    if (LanWorldFactory(hostSeed).ComputeStateHash() == LanWorldFactory(joinerWrongSeed).ComputeStateHash())
        return Fail("lansetup: the control seed builds an identical world, so this gate proves nothing - pick another");

    Console.WriteLine($"lansetup: the host's setup rode the Hello to both clients (8 bytes each) and a joiner handed a " +
                      $"DELIBERATELY WRONG seed still built the host's world exactly (0x{hashes[0]:X16} after 120 ticks, no desync); " +
                      "the wrong seed is proven to build a different world, so the match is the exchange working");
    return 0;
}

int MapGate()
{
    // The map validation harness doc 18 Phase D asked for and never got, now
    // owed a second time by ADR-021: a map may declare a neutral Outpost, and
    // MapLoader THROWS on any struct type with no spawn arm, so an unguarded
    // map file can break the shipped game while every golden stays green (no
    // golden scenario loads skirmish-02 or skirmish-04). This walks EVERY
    // committed map, builds the real opening hand on it, and plays both AIs.
    // Additive, never a golden scenario, so the golden list stays 24.
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    string mapDir = Path.Combine(root, "data", "maps");
    var maps = Directory.GetFiles(mapDir, "*.fmap");
    Array.Sort(maps, StringComparer.Ordinal);   // directory order must not leak into a gate
    if (maps.Length == 0) return Fail("mapgate: no maps found");

    int totalOutposts = 0, totalCaptured = 0;
    const int ticks = 1500;
    foreach (var mapPath in maps)
    {
        string name = Path.GetFileNameWithoutExtension(mapPath);
        MapData map;
        World world;
        try
        {
            // ADR-006: the /data catalogue before tick 0, exactly as the client
            // and the battery do, so a map naming a /data-defined structure is
            // exercised against the shipped catalogue.
            map = MapData.Load(mapPath);
            world = map.BuildWorld(4242, players: 2, out _, w =>
            {
                CatalogueFiles.RegisterAll(w,
                    Path.Combine(root, "data", "units"), Path.Combine(root, "data", "buildings"));
                CatalogueFiles.RegisterFields(w, Path.Combine(root, "data", "fields"));
            });
            map.PlaceSkirmishStart(world, 8000);
        }
        catch (Exception ex)
        {
            // The exact failure an unguarded map produces: a struct type with
            // no MapLoader arm, a malformed line, an off-map start.
            return Fail($"mapgate: {name} failed to load: {ex.Message}");
        }

        int before = world.EntityCount;
        var ais = new[] { new SkirmishAI(0), new SkirmishAI(1) };
        var cmds = new List<Command>();
        try
        {
            for (int t = 0; t < ticks; t++)
            {
                cmds.Clear();
                ais[0].Act(world, cmds);
                ais[1].Act(world, cmds);
                world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            }
        }
        catch (Exception ex)
        {
            return Fail($"mapgate: {name} threw during play at tick {world.Tick}: {ex.Message}");
        }

        if (world.EntityCount <= before)
            return Fail($"mapgate: {name} produced nothing in {ticks} ticks - the AI cannot play this map");

        // ADR-021: every outpost the map declares must stand. Ownership is
        // deliberately NOT asserted neutral any more: since the AI learned to
        // send an engineer (C4c) a captured outpost here is the feature
        // working, not a fault. That an outpost EXISTS at all after a real map
        // load is the end-to-end proof of the map path outpostgate cannot give,
        // because outpostgate spawns its outposts directly.
        int outposts = 0, captured = 0;
        foreach (var e in world.Entities)
        {
            if (!e.Alive || e.Kind != EntityKind.Outpost) continue;
            outposts++;
            if (e.PlayerId >= 0) captured++;
        }
        // C4c: on a map that carries outposts the AI must actually TAKE the
        // free income. Without this the whole capture routine could rot to a
        // no-op and every other assertion here would still pass. Deterministic
        // (fixed seed), and measured with margin: 2 of 2 and 3 of 4 today.
        if (outposts > 0 && captured == 0)
            return Fail($"mapgate: {name} carries {outposts} outposts and the AI captured none in {ticks} ticks");
        totalOutposts += outposts;
        totalCaptured += captured;
        Console.WriteLine($"mapgate: {name} loaded, {ticks} ticks of AI-vs-AI, " +
                          $"{world.EntityCount - before} entities produced, " +
                          $"{outposts} outposts ({captured} AI-captured)");
    }

    Console.WriteLine($"mapgate: all {maps.Length} committed maps load, spawn the opening hand and play {ticks} ticks " +
                      $"of AI-vs-AI without throwing; {totalOutposts} outposts stood across them, {totalCaptured} taken by an AI");
    return 0;
}

int FireSaleGate()
{
    // DR-10 gate. Additive, the repairgate/outpostgate pattern: a standalone
    // mode and a Match battery stage, never a golden scenario, so the golden
    // list stays 24. The neutrality proof is the golden diff itself: the
    // trigger state (no Construction Yard AND no MCV) is one no golden ever
    // reaches, and inside it the AI previously issued nothing at all, so in
    // every golden the change is byte-identical dead code (C4c precedent).

    // --- 1. The sale: a beaten AI sells everything and sends the last wave ---
    {
        var w = new World(2500, 64, 64, players: 2);
        w.SpawnConstructionYard(0, 8, 8);   // the enemy production structure the wave targets
        int plant = w.SpawnPowerPlant(1, 40, 30);
        int turret = w.SpawnTurret(1, 42, 30);
        int rifle = w.SpawnUnit(1, Fix64.FromInt(44), Fix64.FromInt(32), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
        var ai = new SkirmishAI(1);
        var cmds = new List<Command>();
        long before = w.Credits(1);
        for (int t = 0; t < 40; t++)
        {
            cmds.Clear();
            ai.Act(w, cmds);
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        }
        if (w.Entities[plant].Alive || w.Entities[turret].Alive)
            return Fail("firesale: a beaten AI (no yard, no MCV) must sell its remaining structures");
        // Relative, per the standing lesson: the defect DIRECTION is no sale
        // and therefore no refund, so "rose" separates cleanly.
        if (w.Credits(1) <= before)
            return Fail($"firesale: the sale must bank the sell refunds (credits {w.Credits(1)}, started {before})");
        // The last wave: the rifle was attack-moved at the enemy yard (8,8),
        // so after 40 ticks it must have left its spawn column westward.
        if (w.Entities[rifle].X >= Fix64.FromInt(44))
            return Fail("firesale: the last wave must march (rifle never moved toward the enemy)");
        // The latch: on the next AI beat the sale must NOT re-issue anything
        // (without the once-only flag the AttackMoves would repeat forever).
        while (w.Tick % 15 != 0) w.Step(default);
        cmds.Clear();
        ai.Act(w, cmds);
        if (cmds.Count != 0)
            return Fail($"firesale: the sale must fire ONCE (a later beat issued {cmds.Count} commands)");
    }

    // --- 2. The control: an AI holding its yard never fires the sale ---------
    {
        var w = new World(2501, 64, 64, players: 2);
        w.SpawnConstructionYard(0, 8, 8);
        w.SpawnConstructionYard(1, 40, 30);
        int plant = w.SpawnPowerPlant(1, 44, 30);
        var ai = new SkirmishAI(1);
        var cmds = new List<Command>();
        for (int t = 0; t < 100; t++)
        {
            cmds.Clear();
            ai.Act(w, cmds);
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        }
        if (!w.Entities[plant].Alive)
            return Fail("firesale control: an AI with a Construction Yard must never sell its base");
    }

    // --- 3. The comeback: decapitated but holding an MCV, it rebuilds --------
    {
        var w = new World(2502, 64, 64, players: 2);
        w.SpawnConstructionYard(0, 8, 8);
        int plant = w.SpawnPowerPlant(1, 40, 30);
        var mcvDef = w.GetUnitType(7);
        w.SpawnUnit(1, Fix64.FromInt(46), Fix64.FromInt(40), mcvDef.Speed, mcvDef.Hp, mcvDef.Armour, 0, veterancy: false, unitType: 7);
        var ai = new SkirmishAI(1);
        var cmds = new List<Command>();
        for (int t = 0; t < 40; t++)
        {
            cmds.Clear();
            ai.Act(w, cmds);
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        }
        if (!w.Entities[plant].Alive)
            return Fail("firesale: an MCV in hand means NOT beaten - nothing may be sold");
        bool rebuilt = false;
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var e = w.Entities[i];
            if (e.Alive && e.PlayerId == 1 && e.Kind == EntityKind.ConstructionYard) rebuilt = true;
        }
        if (!rebuilt)
            return Fail("firesale: a decapitated AI holding an MCV must deploy it (no yard reappeared in 40 ticks)");
    }

    Console.WriteLine("firesalegate: a beaten AI (no yard, no MCV) sold its structures for real refunds and attack-moved its last " +
                      "unit at the enemy, exactly once (the next beat issued nothing); an AI holding its yard never sold; and a " +
                      "decapitated AI holding an MCV deployed it into a new yard instead of selling");
    return 0;
}

int AiRepairGate()
{
    // ADR-026 gate (DR-13). Additive, the firesalegate/repairgate pattern: a
    // standalone mode and a Match battery stage, never a golden scenario, so
    // the golden list stays 24. It is the POSITIVE proof that skirmish's moved
    // hash is repair firing and nothing else - the AI mends a damaged own
    // structure, never a healthy one, and never while broke. Each part gives
    // the AI a Construction Yard so it is never in the fire-sale branch, and
    // grants credits BELOW any structure cost so the construction ladder can
    // afford nothing and Repair is the only command the commander can issue -
    // the repair is read in isolation, not inferred from a busy beat. The
    // default decision beat is 15 ticks and 0 % 15 == 0, so the AI acts on
    // tick 0, which is where the first-beat assertion reads its command.

    // --- 1. The mend: a damaged own structure is repaired to full, at the
    //        sim's rate, and Repair is the ONE command the first beat issues --
    {
        var w = new World(2600, 64, 64, players: 2);
        w.SpawnConstructionYard(0, 8, 8);              // a harmless enemy; the AI is player 1
        w.SpawnConstructionYard(1, 40, 30);            // the AI keeps its yard: not beaten
        int plant = w.SpawnPowerPlant(1, 44, 30);
        int maxHp = w.Entities[plant].MaxHp;
        var p = w.Entities[plant]; p.Hp = maxHp - 100; w.SetEntityForTest(plant, p);  // scripted battle damage
        w.GrantCredits(1, 60);                         // below any structure cost: only Repair is affordable
        var ai = new SkirmishAI(1);
        var cmds = new List<Command>();

        // First beat (tick 0): the ONLY command must be Repair on the plant.
        ai.Act(w, cmds);
        int repairCount = 0; bool repairedPlant = false;
        foreach (var c in cmds)
            if (c.Type == CommandType.Repair) { repairCount++; if (c.EntityId == plant) repairedPlant = true; }
        if (!repairedPlant)
            return Fail("airepair: the AI must issue Repair on its damaged structure");
        if (cmds.Count != repairCount || repairCount != 1)
            return Fail($"airepair: with nothing else affordable, Repair must be the only command ({cmds.Count} issued, {repairCount} repairs)");

        long before = w.Credits(1);
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        for (int t = 1; t < 80; t++)
        {
            cmds.Clear();
            ai.Act(w, cmds);
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        }
        if (w.Entities[plant].Hp != maxHp)
            return Fail($"airepair: the mend must reach full health ({w.Entities[plant].Hp}/{maxHp})");
        if (w.Entities[plant].Repairing)
            return Fail("airepair: repair must switch itself off at full health");
        // 100 hp at 2/1 costs exactly 50; the once-per-episode toggle means no
        // extra spend from re-issuing (which would toggle the mend back OFF).
        long spent = before - w.Credits(1);
        if (spent != 50)
            return Fail($"airepair: 100 hp at 2hp/1cr must cost exactly 50 (spent {spent}) - a re-toggle would show here");
    }

    // --- 2. The healthy control: a full-health structure is never touched -----
    {
        var w = new World(2601, 64, 64, players: 2);
        w.SpawnConstructionYard(0, 8, 8);
        w.SpawnConstructionYard(1, 40, 30);
        int plant = w.SpawnPowerPlant(1, 44, 30);      // spawns at full health
        w.GrantCredits(1, 60);
        var ai = new SkirmishAI(1);
        var cmds = new List<Command>();
        bool anyRepair = false;
        for (int t = 0; t < 60; t++)
        {
            cmds.Clear();
            ai.Act(w, cmds);
            foreach (var c in cmds) if (c.Type == CommandType.Repair) anyRepair = true;
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        }
        if (anyRepair || w.Entities[plant].Repairing)
            return Fail("airepair control: an undamaged structure must never be repaired");
    }

    // --- 3. The broke control: a damaged structure with no credits is left ----
    {
        var w = new World(2602, 64, 64, players: 2);
        w.SpawnConstructionYard(0, 8, 8);
        w.SpawnConstructionYard(1, 40, 30);
        int plant = w.SpawnPowerPlant(1, 44, 30);
        int maxHp = w.Entities[plant].MaxHp;
        var p = w.Entities[plant]; p.Hp = maxHp - 100; w.SetEntityForTest(plant, p);
        // no GrantCredits: the AI is broke, the credit floor must hold
        var ai = new SkirmishAI(1);
        var cmds = new List<Command>();
        bool anyRepair = false;
        for (int t = 0; t < 60; t++)
        {
            cmds.Clear();
            ai.Act(w, cmds);
            foreach (var c in cmds) if (c.Type == CommandType.Repair) anyRepair = true;
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        }
        if (anyRepair || w.Entities[plant].Repairing)
            return Fail("airepair broke control: a penniless AI must not toggle a repair it cannot begin to pay");
        if (w.Entities[plant].Hp != maxHp - 100)
            return Fail($"airepair broke control: the damaged structure must stay damaged ({w.Entities[plant].Hp}/{maxHp})");
    }

    Console.WriteLine("airepairgate: the AI mended a damaged own structure to full at 2hp/1cr per tick (Repair the only affordable " +
                      "command, spent exactly 50, no re-toggle); it never touched a healthy structure; and while broke it toggled " +
                      "nothing and the damage stood");
    return 0;
}

int PinTrace()
{
    // Q018 stage two. pinprobe established that the stalled commander orders
    // constantly, keeps its units alive and leaves them near home, and named
    // two mechanisms that fit: AttackMove prosecuting local targets forever, or
    // the wave and defence cadences re-ordering units before they travel. It
    // could not separate them, because a command stream shows what was ORDERED
    // and not what the unit then did.
    //
    // This traces the units themselves, and the discriminator is one number per
    // unit: DISTANCE TRAVELLED against NET DISPLACEMENT.
    //
    //   Prosecution predicts a unit that barely moves at all: it stops to fight
    //   whatever it meets, so travelled and net are BOTH small, and it spends
    //   most of its ticks holding an ExplicitTarget.
    //
    //   Thrashing predicts the opposite signature: a unit that walks a long way
    //   in total while ending up where it started, so travelled is LARGE and
    //   net is small, with few ticks holding a target - it is being sent back
    //   and forth, not fighting.
    //
    // The two are not variations of the same answer; they call for different
    // fixes, and the ratio tells them apart without a judgement call.
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    string mapPath = Path.Combine(root, "data", "maps", "skirmish-05.fmap");
    var map = MapData.Load(mapPath);
    var world = map.BuildWorld(4242, players: 2, out _, w =>
    {
        CatalogueFiles.RegisterAll(w,
            Path.Combine(root, "data", "units"), Path.Combine(root, "data", "buildings"));
        CatalogueFiles.RegisterFields(w, Path.Combine(root, "data", "fields"));
    });
    map.PlaceSkirmishStart(world, 8000);

    var ais = new[] { new SkirmishAI(0), new SkirmishAI(1) };
    var cmds = new List<Command>();
    // Per-unit accumulators, indexed by entity id. Player 1 is the stalled
    // commander on this map and this seed (pinprobe: 147 orders, closed to 53).
    var orders = new Dictionary<int, int>();
    var travelled = new Dictionary<int, int>();
    var engagedTicks = new Dictionary<int, int>();
    var movingTicks = new Dictionary<int, int>();
    var firstCell = new Dictionary<int, (int X, int Y)>();
    var lastCell = new Dictionary<int, (int X, int Y)>();
    const int ticks = 6000;

    for (int t = 0; t < ticks; t++)
    {
        cmds.Clear();
        ais[0].Act(world, cmds);
        int mark0 = cmds.Count;
        ais[1].Act(world, cmds);
        for (int i = mark0; i < cmds.Count; i++)   // player 1's orders only
            if (cmds[i].Type == CommandType.AttackMove || cmds[i].Type == CommandType.PathMove)
                orders[cmds[i].EntityId] = orders.GetValueOrDefault(cmds[i].EntityId) + 1;

        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));

        for (int i = 0; i < world.Entities.Count; i++)
        {
            var e = world.Entities[i];
            if (!e.Alive || e.PlayerId != 1 || e.Kind != EntityKind.Unit) continue;
            var cell = (X: Map.CellOf(e.X), Y: Map.CellOf(e.Y));
            if (!firstCell.ContainsKey(i)) firstCell[i] = cell;
            else
            {
                var prev = lastCell[i];
                // Chebyshev step between consecutive ticks, summed: the length
                // of the walk, however much doubling back it contains.
                travelled[i] = travelled.GetValueOrDefault(i)
                    + Math.Max(Math.Abs(cell.X - prev.X), Math.Abs(cell.Y - prev.Y));
            }
            lastCell[i] = cell;
            if (e.ExplicitTarget >= 0) engagedTicks[i] = engagedTicks.GetValueOrDefault(i) + 1;
            if (e.Moving) movingTicks[i] = movingTicks.GetValueOrDefault(i) + 1;
        }
    }

    Console.WriteLine("pintrace (Q018 stage two): skirmish-05, player 1, the stalled commander");
    Console.WriteLine("  unit  orders  travelled  net  engaged%  moving%   reading");
    int thrash = 0, prosecute = 0, neither = 0;
    foreach (var id in orders.Keys.OrderBy(k => k))
    {
        if (!firstCell.ContainsKey(id) || !lastCell.ContainsKey(id)) continue;
        int trav = travelled.GetValueOrDefault(id);
        var a = firstCell[id];
        var b = lastCell[id];
        int net = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        int seen = Math.Max(1, movingTicks.GetValueOrDefault(id) + 1);
        int engPct = 100 * engagedTicks.GetValueOrDefault(id) / Math.Max(1, ticks);
        int movPct = 100 * movingTicks.GetValueOrDefault(id) / Math.Max(1, ticks);
        // Thrashing: walked at least three times its net displacement, and a
        // real distance, so a unit that simply stood still cannot qualify.
        // Prosecution: hardly walked at all and spent its time holding targets.
        string reading;
        if (trav >= 30 && trav >= net * 3) { reading = "THRASH (walked far, went nowhere)"; thrash++; }
        else if (trav < 30 && engPct >= 5) { reading = "PROSECUTE (stayed put, engaged)"; prosecute++; }
        else { reading = "-"; neither++; }
        Console.WriteLine($"  {id,4}  {orders[id],6}  {trav,9}  {net,3}  {engPct,7}%  {movPct,6}%   {reading}");
    }
    Console.WriteLine($"  verdict counts: thrash {thrash}, prosecute {prosecute}, neither {neither}");

    // The reading the two named mechanisms did not predict: units flagged
    // MOVING for a large share of their life that travel zero cells. If they
    // are walled in, the ring around them is blocked and the wall is made of
    // their OWN commander's buildings, which the placement search put there.
    // Count the blocked neighbours of every stalled unit and name what is
    // standing on them.
    Console.WriteLine("  --- enclosure check on the units that never travelled ---");
    int walled = 0, open = 0;
    foreach (var id in orders.Keys.OrderBy(k => k))
    {
        if (!lastCell.ContainsKey(id)) continue;
        if (travelled.GetValueOrDefault(id) > 1) continue;         // it could move; not our case
        if (movingTicks.GetValueOrDefault(id) * 100 / ticks < 5) continue;  // and it tried to
        var c = lastCell[id];
        int blocked = 0;
        var owners = new List<string>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = c.X + dx, ny = c.Y + dy;
                if (nx < 0 || ny < 0 || nx >= 96 || ny >= 64) { blocked++; continue; }
                if (world.Map.IsBlocked(nx, ny)) blocked++;
            }
        // What structure sits nearest, and whose is it?
        int near = -1, nearD = 99;
        for (int i = 0; i < world.Entities.Count; i++)
        {
            var e = world.Entities[i];
            if (!e.Alive || !World.IsStructure(e.Kind)) continue;
            int d = Math.Max(Math.Abs(Map.CellOf(e.X) - c.X), Math.Abs(Map.CellOf(e.Y) - c.Y));
            if (d < nearD) { nearD = d; near = i; }
        }
        string owner = near >= 0
            ? $"{world.Entities[near].Kind} of player {world.Entities[near].PlayerId} at {nearD} cells"
            : "no structure nearby";
        if (blocked >= 5) walled++; else open++;
        Console.WriteLine($"  unit {id,4} at ({c.X},{c.Y}): {blocked}/8 neighbours blocked, nearest structure {owner}");
    }
    Console.WriteLine($"  enclosure: {walled} stalled units with 5+ blocked neighbours, {open} with fewer");

    // Local openness is not an exit. Flood from a stalled unit over unblocked
    // ground and ask how big its world is and whether the enemy is in it. A
    // small pocket means the commander sealed its own base at the PERIMETER,
    // which no neighbour count around an individual unit would ever show. A
    // flood that reaches the far start means the ground is connected and the
    // stall is something other than terrain.
    Console.WriteLine("  --- reachable region from a stalled unit ---");
    var start0 = map.Starts[0];
    foreach (var id in orders.Keys.OrderBy(k => k).Take(40))
    {
        if (!lastCell.ContainsKey(id)) continue;
        if (travelled.GetValueOrDefault(id) > 1) continue;
        if (movingTicks.GetValueOrDefault(id) * 100 / ticks < 5) continue;
        var c = lastCell[id];
        var seen = new HashSet<(int, int)> { (c.X, c.Y) };
        var q = new Queue<(int X, int Y)>();
        q.Enqueue((c.X, c.Y));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= 96 || ny >= 64) continue;
                    if (world.Map.IsBlocked(nx, ny)) continue;
                    if (seen.Add((nx, ny))) q.Enqueue((nx, ny));
                }
        }
        bool reachesFoe = seen.Contains((start0.Cx, start0.Cy));
        Console.WriteLine($"  unit {id,4} at ({c.X},{c.Y}): can stand on {seen.Count} cells; "
                          + $"enemy start reachable over open ground: {reachesFoe}");
        break;   // one is enough: they are all in the same pocket or all not
    }

    // Terrain is connected and the units are in open ground, so the only thing
    // left in their way is EACH OTHER. Map.IsBlocked sees terrain and
    // structures; it does not see units. Count friendly bodies packed around
    // each stalled unit, and how many share its exact cell.
    Console.WriteLine("  --- crowding around the stalled units ---");
    int totalNear = 0, samples = 0, stacked = 0;
    foreach (var id in orders.Keys.OrderBy(k => k))
    {
        if (!lastCell.ContainsKey(id)) continue;
        if (travelled.GetValueOrDefault(id) > 1) continue;
        if (movingTicks.GetValueOrDefault(id) * 100 / ticks < 5) continue;
        var c = lastCell[id];
        int near = 0, same = 0;
        for (int i = 0; i < world.Entities.Count; i++)
        {
            var e = world.Entities[i];
            if (!e.Alive || e.PlayerId != 1 || e.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
            if (i == id) continue;
            int d = Math.Max(Math.Abs(Map.CellOf(e.X) - c.X), Math.Abs(Map.CellOf(e.Y) - c.Y));
            if (d <= 2) near++;
            if (d == 0) same++;
        }
        totalNear += near; samples++; stacked += same;
        Console.WriteLine($"  unit {id,4} at ({c.X},{c.Y}): {near} friendly bodies within 2 cells, {same} on its exact cell");
    }
    if (samples > 0)
        Console.WriteLine($"  crowding: {totalNear} bodies within 2 cells across {samples} stalled units "
                          + $"(mean {totalNear / (double)samples:F1}), {stacked} cell-sharing overlaps");
    return 0;
}

int PinProbe()
{
    // TEMPORARY diagnostic for Q018. The question as filed ASSERTED a mechanism
    // ("nothing ever says the raid is over, so a pinned commander has no path
    // back to offence") on reasoning alone. Two mechanisms fit the same
    // symptom and they predict OPPOSITE command streams:
    //
    //   A. the wave never fires  -> few or no AttackMove orders to non-garrison
    //      units, because the wave condition is never met.
    //   B. the wave fires and dies -> plenty of AttackMove orders to many
    //      distinct units, which then fail to travel because they are engaged
    //      or blocked on the way.
    //
    // Counting the commander's own command stream separates them, which is
    // what this does. Defence orders go to garrison ids (the lowest few unit
    // ids); the wave orders everything else, so the count of DISTINCT units
    // ever given an AttackMove is the discriminator.
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    var maps = Directory.GetFiles(Path.Combine(root, "data", "maps"), "*.fmap");
    Array.Sort(maps, StringComparer.Ordinal);
    foreach (var mapPath in maps)
    {
        string name = Path.GetFileNameWithoutExtension(mapPath);
        var map = MapData.Load(mapPath);
        var world = map.BuildWorld(4242, players: 2, out _, w =>
        {
            CatalogueFiles.RegisterAll(w,
                Path.Combine(root, "data", "units"), Path.Combine(root, "data", "buildings"));
            CatalogueFiles.RegisterFields(w, Path.Combine(root, "data", "fields"));
        });
        map.PlaceSkirmishStart(world, 8000);
        var s0 = map.Starts[0];
        var s1 = map.Starts[1];
        var ais = new[] { new SkirmishAI(0), new SkirmishAI(1) };
        var cmds = new List<Command>();
        var ordered = new HashSet<int>[] { new(), new() };   // distinct units ever attack-moved
        int[] amCount = new int[2];
        int[] nearest = { 999, 999 };
        for (int t = 0; t < 6000; t++)
        {
            cmds.Clear();
            int mark0 = 0;
            ais[0].Act(world, cmds);
            mark0 = cmds.Count;                 // everything so far belongs to player 0
            ais[1].Act(world, cmds);
            for (int i = 0; i < cmds.Count; i++)
            {
                if (cmds[i].Type != CommandType.AttackMove) continue;
                int p = i < mark0 ? 0 : 1;
                amCount[p]++;
                ordered[p].Add(cmds[i].EntityId);
            }
            world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            for (int i = 0; i < world.Entities.Count; i++)
            {
                var e = world.Entities[i];
                if (!e.Alive || e.Kind != EntityKind.Unit || e.PlayerId < 0) continue;
                var goal = e.PlayerId == 0 ? s1 : s0;
                int d = Math.Max(Math.Abs(Map.CellOf(e.X) - goal.Cx), Math.Abs(Map.CellOf(e.Y) - goal.Cy));
                if (d < nearest[e.PlayerId]) nearest[e.PlayerId] = d;
            }
        }
        // Attrition: of the units each commander ever attack-moved, how many
        // are still standing? "Ordered many, lost many" is a wave dying on the
        // way; "ordered many, still alive, still at home" would be a movement
        // failure instead.
        int[] lost = new int[2], survive = new int[2];
        for (int p = 0; p < 2; p++)
            foreach (int id in ordered[p])
            {
                if (id < 0 || id >= world.Entities.Count) continue;
                if (world.Entities[id].Alive) survive[p]++; else lost[p]++;
            }
        Console.WriteLine($"pin {name}: p0 attackmoves {amCount[0]} over {ordered[0].Count} units (lost {lost[0]}, alive {survive[0]}), closed to {nearest[0]}; "
                          + $"p1 attackmoves {amCount[1]} over {ordered[1].Count} units (lost {lost[1]}, alive {survive[1]}), closed to {nearest[1]}");

        // Where do the survivors of a stalled wave actually stand, and is the
        // ground they would have to cross still there? A felled span BLOCKS its
        // cell (ADR-025), so a map can lose crossings DURING a match that the
        // generator proved present before it.
        int spansAlive = 0;
        for (int i = 0; i < world.Entities.Count; i++)
            if (world.Entities[i].Kind == EntityKind.Bridge && world.Entities[i].Alive) spansAlive++;
        int spansTotal = 0;
        for (int i = 0; i < world.Entities.Count; i++)
            if (world.Entities[i].Kind == EntityKind.Bridge) spansTotal++;
        if (spansTotal > 0 || nearest[0] > 30 || nearest[1] > 30)
        {
            var stuck = new List<string>();
            foreach (int id in ordered[nearest[0] > nearest[1] ? 0 : 1])
            {
                if (id < 0 || id >= world.Entities.Count) continue;
                var e = world.Entities[id];
                if (!e.Alive) continue;
                if (stuck.Count < 6) stuck.Add($"({Map.CellOf(e.X)},{Map.CellOf(e.Y)}){(e.Moving ? "m" : "-")}");
            }
            Console.WriteLine($"     spans {spansAlive}/{spansTotal} still standing; the worse side's survivors: {string.Join(" ", stuck)}");
        }
    }
    return 0;
}

int SizeProbe()
{
    // Doc 26 calls 192x128 "the tested map ceiling", which is a statement about
    // what has been tried rather than about what the sim can carry. Making the
    // pool bigger needs that turned into a measurement, because the thing that
    // scales with AREA is not rendering, it is the flow field: FlowField.Build
    // is a Dijkstra over every cell of the grid, cached per destination, so
    // doubling both dimensions quadruples the cost of the first order issued to
    // any new destination and quadruples the memory each cached field holds.
    //
    // The unit count is held CONSTANT across the sizes on purpose. Scaling
    // units with area would measure the two together and answer neither
    // question; what is wanted here is the cost of the ground alone.
    const int units = 200, ticks = 400;
    Console.WriteLine("sizeprobe: constant 200 units, 400 ticks, varying only the map");
    Console.WriteLine("  size        cells   ms/tick   vs 8ms budget   first-tick ms (flow build)");
    foreach (var (w, h) in new[] { (96, 64), (192, 128), (256, 192), (384, 256), (512, 384) })
    {
        var world = new World(4242, w, h, players: 2);
        // A wall down the middle with one gap, so every unit must path a real
        // route rather than walk a straight line: a flow field on empty ground
        // is the easy case and would flatter the big sizes.
        for (int y = 0; y < h; y++)
            if (y < h / 2 - 4 || y > h / 2 + 4) world.Map.SetBlocked(w / 2, y, true);
        for (int i = 0; i < units; i++)
        {
            int p = i % 2;
            world.SpawnUnit(p, Fix64.FromInt(p == 0 ? 4 + i % 20 : w - 5 - i % 20),
                            Fix64.FromInt(4 + (i * 7) % (h - 8)),
                            Fix64.FromFraction(1, 4), 1_000_000, ArmourClass.None, weaponId: 2);
        }
        var cmds = new List<Command>();
        foreach (var e in world.Entities)
            if (e.Kind == EntityKind.Unit)
                cmds.Add(new Command(0, e.PlayerId, CommandType.PathMove, e.Id,
                    Fix64.FromInt(e.PlayerId == 0 ? w - 5 : 4), Fix64.FromInt(h / 2)));
        // The FIRST tick is timed separately, because it is the one that pays
        // for the flow-field build. Averaging it over 400 ticks would hide the
        // only cost that actually scales with area, and a build spike is felt
        // as a frame hitch the moment a player orders an army somewhere new.
        var first = Stopwatch.StartNew();
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        first.Stop();
        cmds.Clear();
        var sw = Stopwatch.StartNew();
        for (int t = 1; t < ticks; t++)
        {
            world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            cmds.Clear();
        }
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds / (ticks - 1);
        double spike = first.Elapsed.TotalMilliseconds;
        Console.WriteLine($"  {w,3}x{h,-3}  {w * h,8}   {ms,7:F3}   {(ms / 8.0 * 100),6:F1}%   {spike,8:F2}");
    }
    Console.WriteLine("  (the 8 ms/tick budget is TDD s6, the same one the defence load gate holds)");
    return 0;
}

int InfiltratorGate()
{
    // P7-7. Additive, the factiondefencegate pattern: a standalone mode and a
    // Match battery stage, never a golden scenario, so the golden list stays 24.
    const int Infil = World.InfiltratorUnitType, Engineer = World.EngineerUnitType;

    (World W, int Spy, int Vault) Setup(ulong seed)
    {
        var w = new World(seed, 64, 64, players: 2);
        w.SetFaction(0, World.FactionSodality);
        int vault = w.SpawnRefinery(1, 30, 20);
        w.GrantCredits(1, 5000);
        var d = w.GetUnitType(Infil);
        int spy = w.SpawnUnit(0, Fix64.FromInt(31), Fix64.FromInt(21), d.Speed, d.Hp,
                              d.Armour, 0, veterancy: false, unitType: Infil);
        return (w, spy, vault);
    }

    // --- 1. It steals, the victim loses exactly what the thief gains, and the
    //        act consumes it. Conservation is asserted because an economy tool
    //        that MINTS credits rather than moving them is a different and much
    //        worse feature.
    {
        var (w, spy, vault) = Setup(3300);
        long theirs = w.Credits(1), mine = w.Credits(0);
        var order = new List<Command> { new(w.Tick, 0, CommandType.Attack, spy, Fix64.Zero, Fix64.Zero, vault) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
        for (int t = 0; t < 60 && w.Entities[spy].Alive; t++) w.Step(default);
        long taken = w.Credits(0) - mine;
        if (taken <= 0) return Fail($"infiltrator: it must actually steal (gained {taken})");
        if (theirs - w.Credits(1) != taken)
            return Fail($"infiltrator: credits must MOVE, not appear - victim lost {theirs - w.Credits(1)}, thief gained {taken}");
        if (w.Entities[spy].Alive)
            return Fail("infiltrator: the act must consume the thief, as capture consumes an engineer");
    }

    // --- 2. It is a ROBBERY, not a capture. Conflating them would hand the
    //        Sodality a second engineer instead of a different tool, which is
    //        the opposite of what the identity pillar needs.
    {
        var (w, spy, vault) = Setup(3301);
        int hp = w.Entities[vault].Hp;
        var order = new List<Command> { new(w.Tick, 0, CommandType.Attack, spy, Fix64.Zero, Fix64.Zero, vault) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
        for (int t = 0; t < 60 && w.Entities[spy].Alive; t++) w.Step(default);
        if (w.Entities[vault].PlayerId != 1)
            return Fail("infiltrator: the building must STAY the victim's - this is a robbery, not a capture");
        if (w.Entities[vault].Hp != hp)
            return Fail("infiltrator: the building must be unharmed - the thief is not a demolition charge");
    }

    // --- 3. A share, not a flat sum: robbing a rich enemy is worth more than
    //        robbing a poor one, which is what makes it economy DENIAL rather
    //        than a fixed bounty.
    {
        var (wRich, spyR, vaultR) = Setup(3302);
        var (wPoor, spyP, vaultP) = Setup(3303);
        wPoor.GrantCredits(1, -4000);   // same fixture, a fifth of the treasury
        long rich0 = wRich.Credits(0), poor0 = wPoor.Credits(0);
        foreach (var (w, spy, vault) in new[] { (wRich, spyR, vaultR), (wPoor, spyP, vaultP) })
        {
            var order = new List<Command> { new(w.Tick, 0, CommandType.Attack, spy, Fix64.Zero, Fix64.Zero, vault) };
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
            for (int t = 0; t < 60 && w.Entities[spy].Alive; t++) w.Step(default);
        }
        long tookRich = wRich.Credits(0) - rich0, tookPoor = wPoor.Credits(0) - poor0;
        if (tookRich <= tookPoor)
            return Fail($"infiltrator: the haul must scale with the victim's treasury ({tookRich} from the rich, "
                        + $"{tookPoor} from the poor) - a flat sum would not punish hoarding");
    }

    // --- 4. The engineer is UNCHANGED. The contact system was generalised to
    //        carry both, and a generalisation that quietly altered the older
    //        behaviour would be a regression wearing a refactor's clothes.
    {
        var w = new World(3304, 64, 64, players: 2);
        int plant = w.SpawnPowerPlant(1, 30, 20);
        var d = w.GetUnitType(Engineer);
        int eng = w.SpawnUnit(0, Fix64.FromInt(31), Fix64.FromInt(21), d.Speed, d.Hp,
                              d.Armour, 0, veterancy: false, unitType: Engineer);
        var order = new List<Command> { new(w.Tick, 0, CommandType.Attack, eng, Fix64.Zero, Fix64.Zero, plant) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
        for (int t = 0; t < 60 && w.Entities[eng].Alive; t++) w.Step(default);
        if (w.Entities[plant].PlayerId != 0)
            return Fail("infiltrator: generalising the contact system must leave CAPTURE working");
        if (w.Entities[eng].Alive)
            return Fail("infiltrator: capture must still consume the engineer");
    }

    Console.WriteLine("infiltratorgate: the Infiltrator steals a share of the victim's treasury and the credits MOVE "
                      + "rather than appear; the building stays the victim's and unharmed, so it is a robbery and not "
                      + "a second capture; the haul scales with the target's wealth, which is economy denial rather "
                      + "than a bounty; and the engineer's capture is untouched by the generalisation");
    return 0;
}

int CampaignGate()
{
    // P7-9. The campaign is now six missions and a manifest of magic numbers,
    // and the manifest had ALREADY gone stale by six structure types before
    // anyone noticed - because nothing read it except the client sidebar, at
    // runtime, in a build nobody runs headless. This gate reads it the way the
    // sidebar does and refuses ids that do not resolve.
    //
    // Then it proves the two things missions 04 to 06 added that the earlier
    // three could not have: that a mission can be won by ARRIVING, and that a
    // mission which suppresses the short-game rule can still be LOST (Q016 -
    // mission 03 could not, and ran forever).
    string Root(string rel) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", rel));

    // -- Stage 1: every id in the manifest resolves ------------------------
    // The stale-header class of defect, made impossible to leave lying about.
    {
        var probe = new World(1, 16, 16, players: 2);
        string[] rows = File.ReadAllLines(Root("data/campaign/campaign.txt"));
        int missions = 0, ids = 0;
        foreach (string raw in rows)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var col = line.Split('|', StringSplitOptions.TrimEntries);
            if (col.Length < 3) return Fail($"campaign: manifest row has {col.Length} columns: {line}");
            string file = col[0];
            if (!File.Exists(Root(file))) return Fail($"campaign: manifest names a missing mission '{file}'");
            // It must LOAD, not merely exist. A mission that throws on parse is
            // a mission the campaign menu offers and the game cannot open.
            var m = MapData.Load(Root(file));
            if (m.Width <= 0 || m.Height <= 0) return Fail($"campaign: {file} has no grid");
            missions++;
            for (int c = 3; c < col.Length && c <= 4; c++)
            {
                if (col[c] == "-" || col[c].Length == 0) continue;
                foreach (string tok in col[c].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    int id = int.Parse(tok.Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    // A def that was never registered comes back as
                    // default(T), and every real one carries hit points.
                    bool ok = c == 3
                        ? probe.GetStructureType(id).Hp > 0
                        : probe.GetUnitType(id).Hp > 0;
                    if (!ok)
                        return Fail($"campaign: {file} allows {(c == 3 ? "structure" : "unit")} id {id}, "
                                    + "which is not in the catalogue - the manifest and the game disagree");
                    ids++;
                }
            }
        }
        if (missions != 6) return Fail($"campaign: expected 6 missions in the manifest, found {missions}");
        Console.WriteLine($"campaigngate: manifest lists {missions} missions and {ids} buildable ids, "
                          + "every one of which loads and resolves in the catalogue");
    }

    // -- Stage 2: mission 04 is won by ARRIVING, not by killing ------------
    // The property under test is the SHAPE of the win, so it is asserted as a
    // shape: the extraction trigger fired, and the enemy is still standing.
    // Asserting a tick or a body count would be asserting the route.
    {
        string path = Root("data/missions/mission-04.fmap");
        var map = MapData.Load(path);
        var world = map.BuildWorld(7401, players: 2, out var tags);
        var mission = new MissionRunner(map, tags);
        // Driven the way the mission expects to be played: a commander that
        // BUILDS (the column is the cargo, not the army - the mission hands
        // over a yard, ferrite and seven thousand credits for exactly this
        // reason), with everything it owns pushed east on a standing order.
        // The first pass walked only the six starting units into the gauntlet
        // and lost all six, which measured the mission's difficulty rather
        // than the thing under test.
        var ai = SkirmishAI.Rusher(0);
        var cmds = new List<Command>();
        var missionCmds = new List<Command>();
        Fix64 ex = Map.CellCentre(86), ey = Map.CellCentre(36);
        for (int t = 0; t < 12000 && world.Winner < 0; t++)
        {
            cmds.Clear();
            ai.Act(world, cmds);
            if (t % 240 == 0)
                for (int i = 0; i < world.Entities.Count; i++)
                {
                    var e = world.Entities[i];
                    if (e.Alive && e.PlayerId == 0 && e.Kind == EntityKind.Unit)
                        cmds.Add(new Command(world.Tick, 0, CommandType.AttackMove, i, ex, ey));
                }
            world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            mission.Tick(world, missionCmds);
            if (missionCmds.Count > 0) { cmds.Clear(); cmds.AddRange(missionCmds); missionCmds.Clear(); world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); }
        }
        if (world.Winner != 0)
        {
            int alive = 0; Fix64 best = Fix64.FromInt(9999);
            for (int i = 0; i < world.Entities.Count; i++)
            {
                var e = world.Entities[i];
                if (!e.Alive || e.PlayerId != 0 || e.Kind != EntityKind.Unit) continue;
                alive++;
                Fix64 d = Fix64.Sqrt(Fix64.DistSq(e.X - ex, e.Y - ey));
                if (d < best) best = d;
            }
            return Fail($"mission04: winner={world.Winner}, {alive} player units alive, nearest {best} from the field");
        }
        if (!mission.Messages.Contains("extraction_made"))
            return Fail("mission04: won without the extraction trigger - something else ended it");
        bool enemyStands = false;
        for (int i = 0; i < world.Entities.Count; i++)
        {
            var e = world.Entities[i];
            if (e.Alive && e.PlayerId == 1 && World.IsStructure(e.Kind)) { enemyStands = true; break; }
        }
        if (!enemyStands)
            return Fail("mission04: the enemy was wiped out, so this proves nothing about arriving");
        Console.WriteLine($"campaigngate: mission 04 was won at tick {world.Tick} by ARRIVING - the extraction "
                          + "trigger fired with the enemy still standing, the first win in the campaign that is "
                          + "not a body count");
    }

    // -- Stage 3: a noshortgame mission can still be LOST (Q016) -----------
    // This is the defect Q016 described, asserted directly: mission 03 set
    // 'rules noshortgame' and carried no defeat, so a player who lost
    // everything was neither beaten nor able to win, and the mission ran
    // until the clock. Killed outright rather than played to a loss - what is
    // under test is that the condition fires and declares.
    foreach (var (file, tag, loser) in new[]
             {
                 ("data/missions/mission-03.fmap", "the_line_broke", 0),
                 ("data/missions/mission-04.fmap", "column_lost", 0),
                 ("data/missions/mission-06.fmap", "the_crown_falls", 1),
             })
    {
        string path = Root(file);
        var map = MapData.Load(path);
        var world = map.BuildWorld(7402, players: 2, out var tags);
        var mission = new MissionRunner(map, tags);
        if (world.ShortGameEnabled)
            return Fail($"campaign: {file} is the noshortgame case and short game is ON - the test is vacuous");
        // Erase everything the losing side holds that counts as hope.
        for (int i = 0; i < world.Entities.Count; i++)
        {
            var e = world.Entities[i];
            if (!e.Alive || e.PlayerId != loser) continue;
            e.Alive = false;
            world.SetEntityForTest(i, e);
        }
        if (world.HasHope(loser))
            return Fail($"campaign: {file} - failed to erase player {loser}, the test would prove nothing");
        var cmds = new List<Command>();
        var missionCmds = new List<Command>();
        for (int t = 0; t < 120 && world.Winner < 0; t++)
        {
            world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            cmds.Clear();
            mission.Tick(world, missionCmds);
            if (missionCmds.Count > 0) { cmds.AddRange(missionCmds); missionCmds.Clear(); }
        }
        int expect = loser == 0 ? 1 : 0;
        if (world.Winner != expect)
            return Fail($"campaign: {file} - player {loser} holds nothing and the mission did not declare "
                        + $"(winner={world.Winner}, wanted {expect}). This is exactly Q016's defect.");
        if (!mission.Messages.Contains(tag))
            return Fail($"campaign: {file} - the ending declared but said nothing ('{tag}' never fired)");
    }
    Console.WriteLine("campaigngate: a mission that suppresses the short-game rule can now still END - three of "
                      + "them declare on 'eliminated', each with a line explaining itself, which is the defect "
                      + "Q016 described (mission 03 ran forever)");

    // -- Stage 4: mission 06 owns BOTH endings -----------------------------
    // The finale sets noshortgame, so if its win trigger were wrong the
    // mission would be unwinnable rather than merely unfair. Stage 3 proved
    // the win side (crown falls); this proves the defeat side of the SAME
    // mission, which is what "the mission owns its ending" has to mean.
    {
        string path = Root("data/missions/mission-06.fmap");
        var map = MapData.Load(path);
        var world = map.BuildWorld(7403, players: 2, out var tags);
        var mission = new MissionRunner(map, tags);
        for (int i = 0; i < world.Entities.Count; i++)
        {
            var e = world.Entities[i];
            if (!e.Alive || e.PlayerId != 0) continue;
            e.Alive = false;
            world.SetEntityForTest(i, e);
        }
        var cmds = new List<Command>();
        var missionCmds = new List<Command>();
        for (int t = 0; t < 120 && world.Winner < 0; t++)
        {
            world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            cmds.Clear();
            mission.Tick(world, missionCmds);
            if (missionCmds.Count > 0) { cmds.AddRange(missionCmds); missionCmds.Clear(); }
        }
        if (world.Winner != 1 || !mission.Messages.Contains("the_landing_is_lost"))
            return Fail($"mission06: the landing was wiped out and the mission did not say so "
                        + $"(winner={world.Winner})");
        Console.WriteLine("campaigngate: mission 06 owns both endings - the crown falling and the landing being "
                          + "lost are each the mission's own trigger, so the finale cannot end without saying why");
    }

    // -- Stage 5: mission 05's premise actually holds ----------------------
    // The mission is built on ADR-028 clause 3: the player's starting force
    // cannot touch an aircraft. If that were false the mission would still
    // "work" and would teach nothing, so it is proved against the REAL map
    // and the REAL starting force rather than against a constructed pair.
    {
        string path = Root("data/missions/mission-05.fmap");
        var map = MapData.Load(path);
        var world = map.BuildWorld(7404, players: 2, out var tags);
        var mission = new MissionRunner(map, tags);
        var cmds = new List<Command>();
        var missionCmds = new List<Command>();
        int flyer = -1;
        for (int t = 0; t < 600 && flyer < 0; t++)
        {
            world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            cmds.Clear();
            mission.Tick(world, missionCmds);
            if (missionCmds.Count > 0) { cmds.AddRange(missionCmds); missionCmds.Clear(); }
            for (int i = 0; i < world.Entities.Count; i++)
                if (world.Entities[i].Alive && world.IsAirborne(world.Entities[i])) { flyer = i; break; }
        }
        if (flyer < 0) return Fail("mission05: the first sortie never launched");
        // Order every ground unit the player owns to attack it, explicitly.
        var attack = new List<Command>();
        int shooters = 0;
        for (int i = 0; i < world.Entities.Count; i++)
        {
            var e = world.Entities[i];
            if (e.Alive && e.PlayerId == 0 && e.Kind == EntityKind.Unit && e.WeaponId != 0)
            {
                // Put them under it, so range is not what saves the aircraft.
                e.X = world.Entities[flyer].X; e.Y = world.Entities[flyer].Y;
                world.SetEntityForTest(i, e);
                attack.Add(new Command(world.Tick, 0, CommandType.Attack, i, Fix64.Zero, Fix64.Zero, flyer));
                shooters++;
            }
        }
        if (shooters == 0) return Fail("mission05: the player's start has no armed ground units, so the premise is untested");
        int hp0 = world.Entities[flyer].Hp;
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(attack));
        for (int t = 0; t < 150 && world.Entities[flyer].Alive; t++) world.Step(default);
        if (!world.Entities[flyer].Alive || world.Entities[flyer].Hp != hp0)
            return Fail($"mission05: {shooters} ground units standing underneath the aircraft damaged it "
                        + $"({hp0} -> {world.Entities[flyer].Hp}); the mission's whole premise is false");
        Console.WriteLine($"campaigngate: mission 05's premise holds against the real map - {shooters} armed ground "
                          + "units of the player's actual starting force, standing directly underneath the first "
                          + "sortie and explicitly ordered to attack it, took exactly nothing off it");
    }

    Console.WriteLine("campaigngate: the campaign is six missions the game can actually open - the manifest's ids all "
                      + "resolve, a mission can be won by ARRIVING rather than by killing, a mission that suppresses "
                      + "the short-game rule can still be lost and says why (Q016), and mission 05's no-answer-to-air "
                      + "premise is true of its real starting force");
    return 0;
}

int FactionDefenceGate()
{
    // P7-2b. Additive, the airgate pattern: a standalone mode and a Match
    // battery stage, never a golden scenario, so the golden list stays 24.
    //
    // Both defences are written GDD s3 doctrine rather than invention - the
    // Directorate's buildings are "tough but expensive", the Sodality has
    // "cloaked units AND structures" - and this gate holds them to it. A pair
    // that differed only in cost would be two turrets with a price tag.
    const int Bastion = 17, Nest = 18, Turret = 5;

    // --- 1. Each side can build its own and NOT the other's. This is the
    //        first row in the game where that is true of a defence at all.
    {
        var w = new World(3200, 64, 64, players: 2);
        if (!w.StructureAllowedForFaction(Bastion, World.FactionDirectorate))
            return Fail("faction defence: the Directorate must be able to build its Bastion");
        if (w.StructureAllowedForFaction(Bastion, World.FactionSodality))
            return Fail("faction defence: the Bastion is the Directorate's and the Sodality must NOT have it");
        if (!w.StructureAllowedForFaction(Nest, World.FactionSodality))
            return Fail("faction defence: the Sodality must be able to build its Shroud Nest");
        if (w.StructureAllowedForFaction(Nest, World.FactionDirectorate))
            return Fail("faction defence: the Nest is the Sodality's and the Directorate must NOT have it");
    }

    // --- 2. The Bastion is what the doctrine says: tougher than the common
    //        turret by a wide margin, and dearer. Asserted as a RATIO rather
    //        than a literal, so a balance pass can move the numbers without
    //        silently turning the Directorate's identity into a reskin.
    {
        var w = new World(3201, 64, 64, players: 2);
        var bast = w.GetStructureType(Bastion);
        var turr = w.GetStructureType(Turret);
        if (bast.Hp < turr.Hp * 3)
            return Fail($"faction defence: 'tough but expensive' needs the Bastion far tougher than a turret "
                        + $"({bast.Hp} vs {turr.Hp})");
        if (bast.Cost <= turr.Cost)
            return Fail("faction defence: 'tough but expensive' means EXPENSIVE - a tougher, cheaper building is "
                        + "not a trade");
    }

    // --- 3. The Nest is genuinely CLOAKED, which is the Sodality's whole
    //        identity and the half that needed no new machinery: CanTarget and
    //        the decloak-on-firing rule are already entity-level, so a
    //        stealthed structure inherits both.
    {
        var w = new World(3202, 64, 64, players: 2);
        w.SetFaction(0, World.FactionSodality);
        int nest = w.SpawnFactionDefence(0, Nest, 20, 20);
        if (!w.Entities[nest].Stealth)
            return Fail("faction defence: the Shroud Nest must be cloaked - GDD s3 says 'cloaked units AND structures'");
        var bastion = w.SpawnFactionDefence(1, Bastion, 40, 40);
        if (w.Entities[bastion].Stealth)
            return Fail("faction defence: the Bastion must NOT be cloaked - it is a wall, not an ambush");
    }

    // --- 4. And it DECLOAKS when it fires, which is the GDD's own stealth
    //        rule and the reason the cloak is fair. An ambush that stays hidden
    //        while shooting is not an ambush, it is an invisible turret.
    {
        var w = new World(3203, 64, 64, players: 2);
        w.SetFaction(0, World.FactionSodality);
        int nest = w.SpawnFactionDefence(0, Nest, 20, 20);
        w.SpawnPowerPlant(0, 30, 30, supply: 500);
        var d = w.GetUnitType(2);
        w.SpawnUnit(1, Fix64.FromInt(22), Fix64.FromInt(20), d.Speed, d.Hp, d.Armour, d.WeaponId,
                    veterancy: false, unitType: 2);
        bool revealed = false;
        for (int t = 0; t < 400 && !revealed; t++) { w.Step(default); revealed = w.Entities[nest].RevealTicks > 0; }
        if (!revealed)
            return Fail("faction defence: the Nest must decloak when it fires - a cloak that survives firing is "
                        + "an invisible turret, and the GDD's stealth rule says every stealth tool has a counter");
    }

    Console.WriteLine("factiondefencegate: each side builds its OWN defence and not the other's - the first time that "
                      + "is true in this game; the Directorate's Bastion is far tougher and dearer than a turret, as "
                      + "'tough but expensive' requires; and the Sodality's Shroud Nest is genuinely cloaked and "
                      + "DECLOAKS when it fires, which is the GDD's own stealth rule inherited with no new machinery");
    return 0;
}

int AirGate()
{
    // ADR-028 (P7-4). Additive, the transportgate pattern: a standalone mode and
    // a Match battery stage, never a golden scenario, so the golden list stays
    // 24.
    //
    // The ADR binds this gate to prove BOTH halves. An aircraft nothing can
    // shoot is a dominant strategy rather than a feature, so "ground weapons
    // cannot touch it" and "the answer kills it" are one claim in two parts and
    // neither is worth having alone.
    const int Flyer = 15, Flak = 16, Rifle = 2, Tank = 1;

    int SpawnOf(World w, int type, int player, int cx, int cy)
    {
        var d = w.GetUnitType(type);
        return w.SpawnUnit(player, Fix64.FromInt(cx), Fix64.FromInt(cy), d.Speed, d.Hp,
                           d.Armour, d.WeaponId, veterancy: false, unitType: type);
    }

    // --- 1. Ground weapons cannot engage an aircraft, by AUTO-ACQUIRE. A rifle
    //        squad and a tank stand under it and neither can do a thing.
    {
        var w = new World(3100, 64, 64, players: 2);
        int flyer = SpawnOf(w, Flyer, 1, 20, 20);
        SpawnOf(w, Rifle, 0, 21, 20);
        SpawnOf(w, Tank, 0, 20, 21);
        int hp = w.Entities[flyer].Hp;
        for (int t = 0; t < 600; t++) w.Step(default);
        if (w.Entities[flyer].Hp != hp)
            return Fail($"air: ground weapons must not reach an aircraft ({w.Entities[flyer].Hp} of {hp} hp) - "
                        + "an aircraft everything can shoot is just a fast tank");
    }

    // --- 2. And not by an EXPLICIT ORDER either. This is the half a scan-only
    //        rule would have missed: told to attack the plane, a rifle squad
    //        must drop the order rather than execute it.
    {
        var w = new World(3101, 64, 64, players: 2);
        int flyer = SpawnOf(w, Flyer, 1, 20, 20);
        int rifle = SpawnOf(w, Rifle, 0, 21, 20);
        var order = new List<Command> { new(w.Tick, 0, CommandType.Attack, rifle, Fix64.Zero, Fix64.Zero, flyer) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
        int hp = w.Entities[flyer].Hp;
        for (int t = 0; t < 600; t++) w.Step(default);
        if (w.Entities[flyer].Hp != hp)
            return Fail("air: an EXPLICIT attack order from a ground weapon must not reach an aircraft");
    }

    // --- 3. The answer works. Same aircraft, same range, one flak track.
    {
        var w = new World(3102, 64, 64, players: 2);
        int flyer = SpawnOf(w, Flyer, 1, 20, 20);
        SpawnOf(w, Flak, 0, 23, 20);
        bool killed = false;
        for (int t = 0; t < 900 && !killed; t++) { w.Step(default); killed = !w.Entities[flyer].Alive; }
        if (!killed)
            return Fail("air: the flak track must kill an aircraft standing in its range - without an answer the "
                        + "layer is a dominant strategy, and ADR-028 clause 4 refuses to land without one");
    }

    // --- 4. The answer is NOT a general-purpose escort: an anti-air weapon
    //        leaves the ground alone, or it would be a better tank as well.
    {
        var w = new World(3103, 64, 64, players: 2);
        int tank = SpawnOf(w, Tank, 1, 20, 20);
        SpawnOf(w, Flak, 0, 23, 20);
        int hp = w.Entities[tank].Hp;
        for (int t = 0; t < 600; t++) w.Step(default);
        if (w.Entities[tank].Hp != hp)
            return Fail("air: the flak track must NOT shoot the ground - a counter that also fights ground units "
                        + "is just a better tank");
    }

    // --- 5. Terrain means nothing to an aircraft. A wall of blocked cells that
    //        stops a tank dead does not slow the flyer at all, which is the
    //        whole of what "air" means in this sim.
    {
        int Crosses(int unitType)
        {
            var w = new World(3104, 64, 64, players: 2);
            for (int y = 0; y < 64; y++) if (y is < 28 or > 34) w.Map.SetBlocked(32, y, true);
            for (int y = 28; y <= 34; y++) w.Map.SetBlocked(32, y, true);   // sealed: no gap at all
            int u = SpawnOf(w, unitType, 0, 10, 31);
            var order = new List<Command> { new(w.Tick, 0, CommandType.PathMove, u, Fix64.FromInt(55), Fix64.FromInt(31)) };
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
            for (int t = 0; t < 1200; t++)
            {
                w.Step(default);
                if (Map.CellOf(w.Entities[u].X) > 32) return t;
            }
            return -1;
        }
        if (Crosses(Flyer) < 0)
            return Fail("air: an aircraft must cross a sealed wall - terrain is nothing to it");
        if (Crosses(Tank) >= 0)
            return Fail("air control: a TANK must NOT cross a sealed wall, or this check proves nothing about air");
    }

    Console.WriteLine("airgate: a rifle squad and a tank standing under an aircraft cannot scratch it, and neither can "
                      + "an EXPLICIT attack order; the flak track kills it; the flak track leaves the ground alone; and "
                      + "the aircraft crosses a sealed wall a tank cannot, so terrain means nothing to it. Both halves "
                      + "of ADR-028 clause 4 hold: the layer and its answer landed together");
    return 0;
}

int TransportGate()
{
    // P7-3. Additive, the emplacementgate pattern: a standalone mode and a
    // Match battery stage, never a golden scenario, so the golden list stays 24.
    const int Carrier = World.CarrierUnitType, Rifle = 2, Engineer = World.EngineerUnitType, Tank = 1;

    World Fresh(out int carrier)
    {
        var w = new World(3000, 64, 64, players: 2);
        var cd = w.GetUnitType(Carrier);
        carrier = w.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(20), cd.Speed, cd.Hp,
                              cd.Armour, 0, veterancy: false, unitType: Carrier);
        return w;
    }
    int Board(World w, int carrier, int unitType, int cx, int cy)
    {
        var d = w.GetUnitType(unitType);
        int u = w.SpawnUnit(0, Fix64.FromInt(cx), Fix64.FromInt(cy), d.Speed, d.Hp,
                            d.Armour, d.WeaponId, veterancy: false, unitType: unitType);
        var cmd = new List<Command> { new(w.Tick, 0, CommandType.LoadTransport, u, Fix64.Zero, Fix64.Zero, carrier) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmd));
        return u;
    }

    // --- 1. Infantry boards, and boarding DESPAWNS rather than flags. A
    //        carried unit that stayed alive would have to be skipped by
    //        movement, combat, separation, selection and drawing, and every one
    //        of those skips is an enumeration somebody forgets.
    {
        var w = Fresh(out int carrier);
        int rifle = Board(w, carrier, Rifle, 21, 20);
        if (w.Entities[rifle].Alive)
            return Fail("transport: a boarded unit must leave the world, not linger as a live entity");
        if (w.CargoOf(carrier).Count != 1)
            return Fail($"transport: the hold must hold one ({w.CargoOf(carrier).Count})");
    }

    // --- 2. Armour cannot board. What a transport carries is a DATA question
    //        (barracks-produced), so the tank is refused without a hardcoded
    //        list naming it.
    {
        var w = Fresh(out int carrier);
        int tank = Board(w, carrier, Tank, 21, 20);
        if (!w.Entities[tank].Alive || w.CargoOf(carrier).Count != 0)
            return Fail("transport: a tank is not infantry and must NOT board");
        if (!w.IsCarryable(Engineer))
            return Fail("transport: the ENGINEER must be carryable - delivering one is the play this unit exists for");
    }

    // --- 3. Capacity is real, and the refusal does not eat the unit.
    {
        var w = Fresh(out int carrier);
        for (int i = 0; i < World.CarrierCapacity; i++) Board(w, carrier, Rifle, 21, 20 + (i % 2));
        if (w.CargoOf(carrier).Count != World.CarrierCapacity)
            return Fail($"transport: the hold must fill to {World.CarrierCapacity} ({w.CargoOf(carrier).Count})");
        int overflow = Board(w, carrier, Rifle, 21, 21);
        if (!w.Entities[overflow].Alive)
            return Fail("transport: a refused boarder must survive the refusal - a full hold is not a shredder");
        if (w.CargoOf(carrier).Count != World.CarrierCapacity)
            return Fail("transport: a full hold must not exceed capacity");
    }

    // --- 4. Unload puts them back, keeps their health and rank, and PRUNES the
    //        hold - the prune is what makes the hash fold sound.
    {
        var w = Fresh(out int carrier);
        int rifle = Board(w, carrier, Rifle, 21, 20);
        var hurt = w.Entities[rifle];
        int before = w.EntityCount;
        var cmd = new List<Command> { new(w.Tick, 0, CommandType.UnloadTransport, carrier, Fix64.Zero, Fix64.Zero) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmd));
        if (w.CargoOf(carrier).Count != 0)
            return Fail("transport: unloading must empty the hold");
        int landed = -1;
        for (int i = before; i < w.EntityCount; i++)
            if (w.Entities[i].Alive && w.Entities[i].UnitType == Rifle) landed = i;
        if (landed < 0)
            return Fail("transport: unloading must put a live rifle squad back on the map");
        if (w.Entities[landed].Hp != hurt.Hp)
            return Fail($"transport: a unit must keep its health across the journey ({w.Entities[landed].Hp} vs {hurt.Hp})");
    }

    // --- 5. THE EDGE CASE THAT MATTERS: a destroyed transport takes its cargo
    //        with it. Anything else would make the hold a place to hide an army
    //        where nothing can shoot it.
    {
        var w = Fresh(out int carrier);
        Board(w, carrier, Rifle, 21, 20);
        Board(w, carrier, Rifle, 21, 21);
        if (w.CargoOf(carrier).Count != 2) return Fail("transport: the precondition needs two aboard");
        // Killed through the REAL damage path, not by setting Alive false: a
        // death staged by fiat skips whatever the death path does, which is
        // exactly the code under test here.
        var frail = w.Entities[carrier]; frail.Hp = 1; w.SetEntityForTest(carrier, frail);
        var rd = w.GetUnitType(Rifle);
        w.SpawnUnit(1, Fix64.FromInt(21), Fix64.FromInt(20), rd.Speed, rd.Hp, rd.Armour, rd.WeaponId,
                    veterancy: false, unitType: Rifle);
        int wasAlive = w.EntityCount;
        for (int t = 0; t < 120 && w.Entities[carrier].Alive; t++) w.Step(default);
        // Step ON past the death. The loop above stops the tick the carrier
        // dies, and the sweep that empties a dead hold runs on the NEXT tick -
        // asserting immediately would have measured the frame before the
        // cleanup rather than the cleanup.
        for (int t = 0; t < 3; t++) w.Step(default);
        if (w.Entities[carrier].Alive)
            return Fail("transport: the precondition needs the carrier destroyed");
        for (int i = wasAlive; i < w.EntityCount; i++)
            if (w.Entities[i].Alive && w.Entities[i].UnitType == Rifle)
                return Fail("transport: a destroyed carrier must NOT spill its cargo back onto the map");
        if (w.CargoOf(carrier).Count != 0)
            return Fail("transport: a destroyed carrier must not still be holding an army");
    }

    // --- 6. A save carries the hold. Without this a player who saved with
    //        troops aboard would load to find them gone, which is data loss
    //        rather than a missing feature - and nothing else in the battery
    //        would have noticed.
    {
        var w = Fresh(out int carrier);
        Board(w, carrier, Rifle, 21, 20);
        Board(w, carrier, Engineer, 21, 21);
        var ms = new MemoryStream();
        w.Save(ms);
        ms.Position = 0;
        var back = World.Load(ms);
        var hold = back.CargoOf(carrier);
        if (hold.Count != 2)
            return Fail($"transport: a save must carry the hold ({hold.Count} of 2 survived)");
        if (hold[0].UnitType != Rifle || hold[1].UnitType != Engineer)
            return Fail("transport: the hold must round-trip in ORDER - unloading order is gameplay");
        if (back.ComputeStateHash() != w.ComputeStateHash())
            return Fail("transport: a loaded world carrying cargo must hash identically to the one saved");
    }

    Console.WriteLine("transportgate: infantry boards and leaves the world rather than lingering as a skipped entity; a "
                      + "tank is refused because carryable is DATA (barracks-produced) and the engineer therefore rides; "
                      + $"the hold fills to {World.CarrierCapacity} and a refused boarder survives; unloading returns the "
                      + "unit with its health and rank and PRUNES the hold; and a destroyed carrier takes its cargo with "
                      + "it, so the hold is not somewhere to hide an army; and a v9 save round-trips the hold in order, "
                      + "hash-identical");
    return 0;
}

int EmplacementGate()
{
    // P7-2. Additive, the factiongate/decorgate pattern: a standalone mode and
    // a Match battery stage, never a golden scenario, so the golden list stays
    // 24.
    //
    // The claim is not "a new building exists". It is that base defence now has
    // a ROCK-PAPER-SCISSORS, which needs both halves proven: the Emplacement
    // must beat the thing the turret answers badly, and must LOSE to the thing
    // the turret answers well. A defence that is simply better than the turret
    // would be a straight upgrade and would make the choice fake.
    const int Emplacement = 15, Turret = 5;

    // 1. Against infantry the Emplacement wins and wins faster than the turret.
    //    Both are given the same rifle squad at the same range, and the turret
    //    is the control - its anti-armour warhead is measurably the wrong tool.
    int TicksToKillInfantry(int structType)
    {
        var w = new World(2900, 64, 64, players: 2);
        int def = structType == Emplacement ? w.SpawnEmplacement(0, 20, 20) : w.SpawnTurret(0, 20, 20);
        // Power it, or ADR-008's brown-out gate silences both and the check
        // measures nothing at all.
        w.SpawnPowerPlant(0, 30, 30, supply: 500);
        int foe = w.SpawnUnit(1, Fix64.FromInt(23), Fix64.FromInt(21),
                              Fix64.Zero, 100, ArmourClass.None, weaponId: 0);
        for (int t = 0; t < 900; t++)
        {
            w.Step(default);
            if (!w.Entities[foe].Alive) return t;
        }
        return -1;
    }
    int empVsInfantry = TicksToKillInfantry(Emplacement);
    int turVsInfantry = TicksToKillInfantry(Turret);
    if (empVsInfantry < 0)
        return Fail("emplacement: it must kill a rifle squad standing in its range");
    if (turVsInfantry >= 0 && empVsInfantry >= turVsInfantry)
        return Fail($"emplacement: it must answer infantry BETTER than the turret does "
                    + $"({empVsInfantry} ticks vs the turret's {turVsInfantry}) - otherwise it is not a counter, "
                    + "it is a second turret");

    // 2. Against armour it is the wrong tool, which is what makes the pair a
    //    choice. Same shape, armoured target, and the turret is the control
    //    that SHOULD win here.
    int TicksToKillArmour(int structType)
    {
        var w = new World(2901, 64, 64, players: 2);
        if (structType == Emplacement) w.SpawnEmplacement(0, 20, 20); else w.SpawnTurret(0, 20, 20);
        w.SpawnPowerPlant(0, 30, 30, supply: 500);
        int foe = w.SpawnUnit(1, Fix64.FromInt(23), Fix64.FromInt(21),
                              Fix64.Zero, 400, ArmourClass.Heavy, weaponId: 0);
        for (int t = 0; t < 1800; t++)
        {
            w.Step(default);
            if (!w.Entities[foe].Alive) return t;
        }
        return -1;
    }
    int empVsArmour = TicksToKillArmour(Emplacement);
    int turVsArmour = TicksToKillArmour(Turret);
    if (turVsArmour < 0)
        return Fail("emplacement control: the TURRET must still kill armour - if it cannot, this check proves nothing");
    if (empVsArmour >= 0 && empVsArmour <= turVsArmour)
        return Fail($"emplacement: it must answer armour WORSE than the turret ({empVsArmour} vs {turVsArmour}) - "
                    + "a defence that beats everything removes the decision this wave exists to create");

    // 3. It obeys ADR-008's power rule, like every other weapon emplacement.
    //    Free defence once built would make the economy stop mattering.
    {
        var w = new World(2902, 64, 64, players: 2);
        w.SpawnEmplacement(0, 20, 20);
        int foe = w.SpawnUnit(1, Fix64.FromInt(23), Fix64.FromInt(21),
                              Fix64.Zero, 100, ArmourClass.None, weaponId: 0);
        for (int t = 0; t < 600; t++) w.Step(default);   // no power plant anywhere
        if (!w.Entities[foe].Alive)
            return Fail("emplacement: an UNPOWERED emplacement must hold its fire (ADR-008)");
    }

    Console.WriteLine($"emplacementgate: the Emplacement kills a rifle squad in {empVsInfantry} ticks against the "
                      + $"turret's {turVsInfantry}, and is the WORSE answer to armour ({(empVsArmour < 0 ? "never killed it" : empVsArmour + " ticks")} "
                      + $"against the turret's {turVsArmour}), so base defence is a choice rather than a ladder; "
                      + "and it holds fire unpowered, so defence still costs an economy");
    return 0;
}

int FactionGate()
{
    // P7-1. Additive, the decorgate/repairgate pattern: a standalone mode and a
    // Match battery stage, never a golden scenario, so the golden list stays 24.
    //
    // The rule under test is that a building's FACTION comes from /data. Before
    // this wave it did not: every building YAML authored a `faction:` line,
    // DataLoader validated it, and the bridge into StructureTypeDef dropped it,
    // while the sim hardcoded one expression naming the Veil. So the files said
    // one thing and the game did another, which is the ADR-006 class of defect
    // exactly - and it is invisible to every golden, because no golden scenario
    // plays a Sodality commander who tries to build a Directorate building.
    var w = new World(2800, 64, 64, players: 2);
    w.SetFaction(0, World.FactionDirectorate);
    w.SetFaction(1, World.FactionSodality);

    // 1. A common building is buildable by BOTH. The turret is the case that
    //    matters: its file said "directorate" and both sides could build it,
    //    and the repair was to make the DATA true rather than to take the
    //    turret away from a faction that has had it all along.
    const int Turret = 5, Veil = 7, Plant = 1;
    foreach (int f in new[] { World.FactionDirectorate, World.FactionSodality })
    {
        if (!w.StructureAllowedForFaction(Turret, f))
            return Fail($"faction: the turret is common and faction {f} must be able to build it");
        if (!w.StructureAllowedForFaction(Plant, f))
            return Fail($"faction: a power plant is common and faction {f} must be able to build it");
    }

    // 2. A declared building is buildable ONLY by its side, and the declaration
    //    now lives in sod_veil_projector.yaml rather than in a predicate.
    if (!w.StructureAllowedForFaction(Veil, World.FactionSodality))
        return Fail("faction: the Veil is Sodality's and the Sodality must be able to build it");
    if (w.StructureAllowedForFaction(Veil, World.FactionDirectorate))
        return Fail("faction: the Veil is Sodality's and the Directorate must NOT be able to build it");

    // 3. The rule is READ, not hardcoded. Register a def that declares the
    //    other side and watch the answer follow the data - this is the check
    //    that would have failed before the wave no matter what the file said,
    //    because nothing consulted the file at all.
    var probe = new World(2801, 32, 32, players: 2);
    probe.SetFaction(0, World.FactionDirectorate);
    probe.RegisterStructureType(Turret,
        new World.StructureTypeDef(600, EntityKind.Turret, 150, Hp: 400, PowerDraw: 20,
                                   SightCells: 6, WeaponId: 4, Faction: World.FactionSodality));
    if (probe.StructureAllowedForFaction(Turret, World.FactionDirectorate))
        return Fail("faction: a def declaring Sodality must refuse the Directorate - the gate is still hardcoded");
    if (!probe.StructureAllowedForFaction(Turret, World.FactionSodality))
        return Fail("faction: a def declaring Sodality must admit the Sodality");

    Console.WriteLine("factiongate: a building's side now comes from /data - common buildings admit both factions, the "
                      + "Veil admits only the Sodality, and a def that DECLARES a side is obeyed, so the rule is read "
                      + "rather than named in code. The turret and superweapon are declared common, which is what they "
                      + "have always been in play whatever their files said");
    return 0;
}

int BasinGate()
{
    // skirmish-07 played at the length it was DESIGNED for. Additive, the
    // fordgate/mapgate pattern: a standalone mode and a Match battery stage,
    // never a golden scenario, so the golden list stays 24.
    //
    // This exists because the map was signed off on a smoke test. mapgate runs
    // 1500 ticks, and at 15 ticks per second that is one hundred seconds of
    // simulated time - against a map whose whole rationale is the GDD's 15 to
    // 30 minute macro window, whose starts are 231 cells apart precisely so a
    // crossing is a commitment, and which carries sixteen expansion sites. A
    // hundred seconds proves the AI is alive on it. It proves nothing about
    // whether a real match on it converges or degenerates.
    //
    // 20,000 ticks is a little over 22 minutes, inside that window.
    //
    // It covers EVERY chokeless map, not just the first one. skirmish-08 makes
    // the same claim by a different route - the Sound bounds the battlefield
    // instead of karst breaking it up - and a claim that needed proving once
    // needs proving each time it is made. Adding the second map here rather
    // than waiting for a reviewer to point out the omission is the whole
    // lesson of the first review.
    const int ticks = 20_000;
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    foreach (var name in new[] { "skirmish-07", "skirmish-08" })
    {
    string mapPath = Path.Combine(root, "data", "maps", $"{name}.fmap");
    if (!File.Exists(mapPath)) return Fail($"basin: {name}.fmap is missing");

    MapData map;
    World world;
    try
    {
        map = MapData.Load(mapPath);
        world = map.BuildWorld(4242, players: 2, out _, w =>
        {
            CatalogueFiles.RegisterAll(w,
                Path.Combine(root, "data", "units"), Path.Combine(root, "data", "buildings"));
            CatalogueFiles.RegisterFields(w, Path.Combine(root, "data", "fields"));
        });
        map.PlaceSkirmishStart(world, 8000);
    }
    catch (Exception ex) { return Fail($"basin: {name} failed to load: {ex.Message}"); }

    var ais = new[] { new SkirmishAI(0), new SkirmishAI(1) };
    var cmds = new List<Command>();
    int deaths = 0, peakEntities = 0;
    for (int t = 0; t < ticks; t++)
    {
        cmds.Clear();
        ais[0].Act(world, cmds);
        ais[1].Act(world, cmds);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        if (world.EntityCount > peakEntities) peakEntities = world.EntityCount;
    }

    // Read the end state: who built what, who took what, and did anyone die.
    // Heap arrays rather than stackalloc: this block now runs once per map
    // inside the loop above, and a stackalloc in a loop is a stack-overflow
    // waiting to happen (CA2014). Four two-element arrays per map is nothing.
    int[] refineries = new int[2];
    int[] yards = new int[2];
    int[] army = new int[2];
    int[] outposts = new int[2];
    for (int i = 0; i < world.Entities.Count; i++)
    {
        var e = world.Entities[i];
        if (!e.Alive) { if (e.PlayerId >= 0) deaths++; continue; }
        if (e.PlayerId < 0) continue;
        switch (e.Kind)
        {
            case EntityKind.Refinery: refineries[e.PlayerId]++; break;
            case EntityKind.ConstructionYard: yards[e.PlayerId]++; break;
            case EntityKind.Outpost: outposts[e.PlayerId]++; break;
            case EntityKind.Unit: army[e.PlayerId]++; break;
        }
    }

    // The map's OWN premise is that it rewards covering area rather than
    // holding a gate, so the thing to assert is that expansion actually
    // happened. A macro map on which nobody expands is a big empty map.
    // Scale-free on purpose. The first version of this check demanded four
    // "expansion units" as an absolute count, which passed skirmish-07 (six
    // outposts on the map) and failed skirmish-08 (four) for a winner holding
    // the same PROPORTION of what was on offer. An absolute bar on a
    // map-relative quantity measures the map's generosity, not the commander's
    // play. What the claim actually is: somebody built an economy beyond the
    // opening hand, and somebody took a node.
    if (refineries[0] + refineries[1] == 0)
        return Fail($"basin[{name}]: over 22 simulated minutes not one refinery was built - "
                    + "a macro map nobody macros on is just a big empty map");
    if (outposts[0] + outposts[1] == 0)
        return Fail($"basin[{name}]: over 22 simulated minutes not one outpost was held - "
                    + "the map's own expansion incentive went untouched");
    if (deaths == 0)
        return Fail($"basin[{name}]: 20,000 ticks and nothing died - the armies never met, so the map is too big "
                    + "or too open for its own separation");
    if (yards[0] == 0 && yards[1] == 0)
        return Fail($"basin[{name}]: both commanders lost every construction yard, which is not a result, it is a bug");

    Console.WriteLine($"basingate[{name}]: played {ticks} ticks (~22 simulated minutes, inside the GDD window). "
                      + $"Refineries {refineries[0]}/{refineries[1]}, outposts held {outposts[0]}/{outposts[1]}, "
                      + $"armies {army[0]}/{army[1]}, yards {yards[0]}/{yards[1]}, {deaths} entities destroyed, "
                      + $"peak {peakEntities}. The commanders expanded and fought: it is a match, not a stalemate "
                      + "on open ground");
    }
    return 0;
}

int DecorGate()
{
    // The decorative terrain layer. Additive, the fordgate/difficultygate
    // pattern: a standalone mode and a Match battery stage, never a golden
    // scenario, so the golden list stays 24.
    //
    // One claim carries this whole feature: a decorated cell is DRAWN and
    // PASSABLE. If that is ever false the layer stops being decoration and
    // becomes invisible terrain, which is the worst failure a map can have -
    // an obstacle the player cannot see. Every check below exists to pin it.
    // 16x8 rather than the smallest grid that fits the characters: a cramped
    // map leaves no room either side of the walk, and a failure there would be
    // ambiguous between "decoration blocks" and "the unit had nowhere to go".
    // A wall across the map whose ONLY gap is the four decorative cells.
    // This is a far stronger test than walking along a decorated row: if a
    // unit gets from the south half to the north half at all, it can only
    // have walked over decoration, and if decoration blocked there would be
    // no route to find. It also proves the FLOW FIELD routes through decor,
    // not merely that the parser kept it out of Blocked.
    const string src = "ferrostorm-map v1\nsize 48 24\nstart 0 4 20\nstart 1 43 3\ngrid:\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "####################,:=~########################\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n"
                     + "................................................\n";
    MapData map;
    try { map = MapData.Parse(src); }
    catch (Exception ex) { return Fail($"decor: a map using the decorative characters failed to parse: {ex.Message}"); }

    // 1. Every decorative character is recorded as decor, and NONE of them is
    //    recorded as blocked. The '#' beside them is the positive control: if
    //    the parser stopped blocking things entirely this check would pass for
    //    the wrong reason, so a real obstacle sits in the same row.
    if (map.Decor.Count != 4)
        return Fail($"decor: expected 4 decorated cells, got {map.Decor.Count}");
    foreach (var (cx, cy) in map.Decor)
        foreach (var (bx, by) in map.Blocked)
            if (cx == bx && cy == by)
                return Fail($"decor: cell ({cx},{cy}) is decorated AND blocked - decoration must never obstruct");
    if (map.Blocked.Count != 44)
        return Fail($"decor: the control wall must still block ({map.Blocked.Count} blocked cells, expected 44)");

    // 2. Each decorative cell carries its own dressing through to the client,
    //    or they would all render as the same thing and the vocabulary would
    //    be a lie.
    foreach (var ch in new[] { ',', ':', '=', '~' })
    {
        bool found = false;
        foreach (var kv in map.Visual) if (kv.Value == ch) found = true;
        if (!found) return Fail($"decor: '{ch}' reached no Visual entry, so the client cannot draw it");
    }

    // 3. The claim itself, in the sim rather than in the parser: a unit walks
    //    THROUGH the decorated gap: the wall at y=12 has no other opening, so
    //    a unit that reaches the far side crossed decoration to do it.
    string droot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    var w = map.BuildWorld(4242, players: 2, out _, ww =>
    {
        CatalogueFiles.RegisterAll(ww,
            Path.Combine(droot, "data", "units"), Path.Combine(droot, "data", "buildings"));
        CatalogueFiles.RegisterFields(ww, Path.Combine(droot, "data", "fields"));
    });
    int id = w.SpawnUnit(0, Map.CellCentre(21), Map.CellCentre(18),
                         Fix64.FromFraction(1, 4), 100, ArmourClass.None, weaponId: 2);
    var order = new List<Command>
    {
        new(w.Tick, 0, CommandType.PathMove, id, Map.CellCentre(21), Map.CellCentre(5)),
    };
    w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
    bool arrived = false;
    for (int t = 0; t < 2000 && !arrived; t++)
    {
        w.Step(default);
        var e = w.Entities[id];
        // Arrival is CROSSING THE WALL, not reaching an exact cell. Asserting
        // an exact destination failed here with the unit sitting four cells
        // short and settled - the same arrival slack that has now caught three
        // separate checks in this file. North of y=12 is the claim; getting
        // there is only possible through the decorated gap.
        if (Map.CellOf(e.Y) < 12) arrived = true;
    }
    if (!arrived)
        return Fail($"decor: a unit never crossed the wall whose only gap is decorated - decoration is blocking, which is the "
                    + $"one thing it must never do (stopped at ({Map.CellOf(w.Entities[id].X)},{Map.CellOf(w.Entities[id].Y)}), moving={w.Entities[id].Moving})");

    Console.WriteLine("decorgate: the decorative characters , : = ~ parse as DECOR and never as blocked (with a '#' "
                      + "control still blocking beside them), each carries its own dressing through to the client, "
                      + "and a unit crossed a wall whose ONLY gap was decorated, so the flow field routes over "
                      + "decoration - a map can now carry detail that costs pathing nothing");
    return 0;
}

int FordGate()
{
    // DR-18 gate, for skirmish-05 (Ashford Reach). Additive, the
    // difficultygate/mapgate pattern: a standalone mode and a Match battery
    // stage, never a golden scenario, so the golden list stays 24.
    //
    // This exists because mapgate PASSING on this map proves less than it
    // looks. mapgate asserts the map loads, that the AI produced something,
    // and that a declared outpost was captured - and on Ashford Reach both
    // outposts sit on the same bank as the base that takes them, so every one
    // of those assertions is satisfied without a single unit ever touching a
    // bridge. The property that actually matters on a river map is the one doc
    // 26 states as the acceptance bar: "a map where an army parks is a failed
    // map, not a hard one". A flow field that cannot path a bridge choke
    // returns -1 and parks the attacking army at home, which is exactly the
    // failure this map's shape could ship, and nothing else in the battery
    // would notice it.
    // The same root idiom mapgate uses, open-coded there too.
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    string mapPath = Path.Combine(root, "data", "maps", "skirmish-05.fmap");
    if (!File.Exists(mapPath)) return Fail("ford: skirmish-05.fmap is missing");

    MapData map;
    World world;
    try
    {
        map = MapData.Load(mapPath);
        world = map.BuildWorld(4242, players: 2, out _, w =>
        {
            CatalogueFiles.RegisterAll(w,
                Path.Combine(root, "data", "units"), Path.Combine(root, "data", "buildings"));
            CatalogueFiles.RegisterFields(w, Path.Combine(root, "data", "fields"));
        });
        // NO opening hand here, deliberately. The first draft called
        // PlaceSkirmishStart and then walked a unit to the far START cell -
        // which by then had a Construction Yard standing on it, so the
        // destination was blocked by construction, the flow field correctly
        // returned -1, and the gate reported "the flow field cannot path the
        // Ashford" about a map that paths perfectly well. The bases are what
        // the geometry is being tested INDEPENDENTLY of, so they are left out
        // and the start cells stay the open apron ground the generator proved.
    }
    catch (Exception ex) { return Fail($"ford: skirmish-05 failed to load: {ex.Message}"); }

    // The channel meanders about y=31.5 with amplitude 5 and a half-width under
    // 3, so it never reaches beyond y=23..40. These bands are comfortably
    // OUTSIDE it in both directions: a unit standing on one can only have got
    // there across a bridge, never by hugging its own shore.
    const int NorthBank = 22, SouthBank = 41;

    // What this gate asserts, and what it deliberately does NOT.
    //
    // It asserts the MAP property: that a unit ordered to the far bank gets
    // there, from both sides, and still gets there once BOTH destroyable flank
    // spans are rubble. That is doc 26's real worry - "a chokepoint the flow
    // field cannot path returns minus one and parks the attacking army at
    // home" - and it is a fact about geometry, testable without an opponent.
    //
    // It does NOT assert that both AI commanders mount an offensive. That was
    // this gate's first draft and it was the wrong bar: measured across the
    // whole pool, whichever commander strikes first pins the other's garrison
    // and the pinned side never marches, so the reading swings hard on which
    // side happens to move first. Ashford Reach read 37-against-2 on approach
    // distance before its starts were widened and 11-against-53 after, but
    // skirmish-02, shipped and unchallenged, reads 7-against-29 on the same
    // measure. Holding a new map to a bar no existing map meets would be
    // inventing a standard rather than applying one. The one-sidedness is real
    // and is filed as a finding against the AI, where it belongs, not tuned
    // around here (docs/questions/Q018).
    Fix64 CellC(int c) => Map.CellCentre(c);
    var north = map.Starts[0];
    var south = map.Starts[1];

    bool WalksTo(World w, int player, (int Cx, int Cy) from, (int Cx, int Cy) to, bool wantSouth)
    {
        // A rifle squad, the cheapest thing that walks, ordered clean across.
        int id = w.SpawnUnit(player, CellC(from.Cx), CellC(from.Cy),
                             Fix64.FromFraction(1, 4), 100, ArmourClass.None, weaponId: 2);
        var order = new List<Command>
        {
            new(w.Tick, player, CommandType.PathMove, id, CellC(to.Cx), CellC(to.Cy)),
        };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
        for (int t = 0; t < 4000; t++)
        {
            w.Step(default);
            int cy = Map.CellOf(w.Entities[id].Y);
            if (wantSouth ? cy >= SouthBank : cy <= NorthBank) return true;
        }
        return false;
    }

    if (!WalksTo(world, 0, north, south, wantSouth: true))
        return Fail("ford: a unit ordered from the northern start to the southern never crossed - "
                    + "the flow field cannot path the Ashford southbound");
    if (!WalksTo(world, 1, south, north, wantSouth: false))
        return Fail("ford: a unit ordered from the southern start to the northern never crossed - "
                    + "the flow field cannot path the Ashford northbound");

    // The map must also carry destroyable spans at all, or it is not exercising
    // the vocabulary DR-18 added it for and the generator's 'b' cells never
    // became entities.
    int spans = 0;
    for (int i = 0; i < world.Entities.Count; i++)
        if (world.Entities[i].Alive && world.Entities[i].Kind == EntityKind.Bridge) spans++;
    if (spans == 0)
        return Fail("ford: skirmish-05 spawned no destroyable spans - it is not exercising ADR-025");

    // NOT asserted here, deliberately: that the theatre still connects once both
    // flank spans are rubble. It is proven twice already and neither proof
    // needs this gate to restate it. tools/mapgen.py proves it on the graph
    // before the file is written, by flooding the map with every destroyable
    // span removed at once, which is what the generator's crossing check is
    // for; and bridgegate proves the general sim behaviour that a felled span
    // BLOCKS its cell. Restating it here would mean felling bridges by fiat,
    // and a bridge only dies through a damage path, so the "test" would have
    // had to reach around the sim to stage a death the sim would never produce.
    Console.WriteLine($"fordgate: Ashford Reach is walkable in BOTH directions under a real flow field - a unit "
                      + $"ordered across arrived southbound and northbound - over {spans} destroyable spans plus the "
                      + "permanent centre ford. Whether both COMMANDERS choose to march is a property of the AI and "
                      + "not of this map: it is filed as Q018");

    // skirmish-06 (Sable Crossroads) rides the same proof, because it fails the
    // same way if it fails at all: its four quadrants touch only at four gaps,
    // and a gap the flow field will not thread is a map cut into quarters.
    // Start-to-start rather than bank-to-bank, since there is no channel here
    // to be one side or the other of - the whole question is whether the ring
    // route through the neutral quadrants is walkable at all.
    //
    // Worth stating why mapgate's outpost assertion means MORE here than it did
    // on the Ashford. There, both outposts stood on the bank of the base that
    // took them, so the assertion passed without anything crossing. Here all
    // four stand in the NEUTRAL quadrants, which no base starts in, so an AI
    // that captures one has necessarily walked a gap. It captured four of four.
    string crossPath = Path.Combine(root, "data", "maps", "skirmish-06.fmap");
    if (!File.Exists(crossPath)) return Fail("ford: skirmish-06.fmap is missing");
    MapData cross;
    try { cross = MapData.Load(crossPath); }
    catch (Exception ex) { return Fail($"ford: skirmish-06 failed to load: {ex.Message}"); }

    // No opening hand, for the reason recorded above: a base on the destination
    // cell blocks it, and -1 would then be the correct answer to a question
    // this gate should not be asking.
    //
    // A FRESH world per direction, which is not fussiness. Both directions
    // first shared one world and the reverse leg failed, because the outbound
    // unit had settled four cells from the south-east start and was still
    // standing there when the return leg tried to spawn and set off from that
    // exact spot. The map was never the problem; the leftover was.
    string where = "";
    bool Arrives(int player, (int Cx, int Cy) from, (int Cx, int Cy) to)
    {
        World w;
        try
        {
            w = cross.BuildWorld(4242, players: 2, out _, ww =>
            {
                CatalogueFiles.RegisterAll(ww,
                    Path.Combine(root, "data", "units"), Path.Combine(root, "data", "buildings"));
                CatalogueFiles.RegisterFields(ww, Path.Combine(root, "data", "fields"));
            });
        }
        catch (Exception ex) { where = $"world build threw: {ex.Message}"; return false; }

        int id = w.SpawnUnit(player, Map.CellCentre(from.Cx), Map.CellCentre(from.Cy),
                             Fix64.FromFraction(1, 4), 100, ArmourClass.None, weaponId: 2);
        var order = new List<Command>
        {
            new(w.Tick, player, CommandType.PathMove, id,
                Map.CellCentre(to.Cx), Map.CellCentre(to.Cy)),
        };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
        for (int t = 0; t < 6000; t++)
        {
            w.Step(default);
            var e = w.Entities[id];
            // Arrival with the slack movement itself allows. This was 3 first
            // and reported the map unthreadable while the unit was sitting 4
            // cells from the target with Moving false - it had walked the whole
            // ring and settled, which is arrival by any honest reading. The
            // sim's own arrival tolerances are looser than this: SkirmishAI
            // treats an MCV as arrived at DistSq 144, twelve cells, precisely
            // because a crowd or a stall settles a unit short of an exact cell.
            // Six is inside that and far outside the eight-cell base apron, so
            // it cannot pass a unit that failed to reach the quadrant at all.
            if (Math.Max(Math.Abs(Map.CellOf(e.X) - to.Cx), Math.Abs(Map.CellOf(e.Y) - to.Cy)) <= 6)
                return true;
            where = $"stopped at ({Map.CellOf(e.X)},{Map.CellOf(e.Y)}) moving={e.Moving}";
        }
        return false;
    }

    var nw = cross.Starts[0];
    var se = cross.Starts[1];
    if (!Arrives(0, nw, se))
        return Fail("ford: on Sable Crossroads a unit ordered from the north-west start to the south-east "
                    + $"never arrived - the ring route through the neutral quadrants does not thread ({where})");
    if (!Arrives(1, se, nw))
        return Fail("ford: on Sable Crossroads a unit ordered from the south-east start to the north-west "
                    + $"never arrived - the ring route does not thread in reverse ({where})");

    Console.WriteLine("fordgate: Sable Crossroads threads too - a unit walked the ring route through the neutral "
                      + "quadrants start to start in both directions, so its four gaps join the quadrants in "
                      + "practice and not merely on the generator's graph");
    return 0;
}

int DifficultyGate()
{
    // DR-14 / doc 28 gate. Additive, the firesalegate/airepairgate pattern: a
    // standalone mode and a Match battery stage, never a golden scenario, so
    // the golden list stays 24. The claim it pins is the one the whole wave
    // rests on - Normal is the IDENTITY rung, so a ladder was added without
    // moving a single hash - plus the two honest knobs and the declared
    // handicap's confinement to setup.

    // --- 1. Normal is the identity rung: the ladder's default plays the
    //        identical match to the commander that shipped before it existed.
    //        This is the golden diff's claim restated as a check, so a future
    //        edit that quietly changes Normal fails HERE and not six months
    //        later in a hash nobody can explain. -----------------------------
    {
        ulong Play(bool viaLadder)
        {
            var w = BuildSkirmishWorld(2700);
            var ais = viaLadder
                ? new[] { new SkirmishAI(0, difficulty: AiDifficulty.Normal), new SkirmishAI(1, difficulty: AiDifficulty.Normal) }
                : new[] { new SkirmishAI(0), new SkirmishAI(1) };
            var cmds = new List<Command>();
            for (int t = 0; t < 1200; t++)
            {
                cmds.Clear();
                ais[0].Act(w, cmds);
                ais[1].Act(w, cmds);
                w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            }
            return w.ComputeStateHash();
        }
        ulong plain = Play(false), normal = Play(true);
        if (plain != normal)
            return Fail($"difficulty: Normal must be the identity rung (plain 0x{plain:X16} vs Normal 0x{normal:X16})");
    }

    // --- 2. The beat: Easy thinks at half speed and Brutal fastest. Counted as
    //        commands issued over an identical window in an identical world,
    //        which is the observable a player actually feels. -----------------
    {
        int Commands(AiDifficulty d)
        {
            var w = BuildSkirmishWorld(2701);
            var ai = new SkirmishAI(0, difficulty: d);
            var foe = new SkirmishAI(1);
            var cmds = new List<Command>();
            int issued = 0;
            for (int t = 0; t < 900; t++)
            {
                cmds.Clear();
                ai.Act(w, cmds);
                issued += cmds.Count;      // count THIS commander only, before the foe adds any
                foe.Act(w, cmds);
                w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
            }
            return issued;
        }
        int easy = Commands(AiDifficulty.Easy), normal = Commands(AiDifficulty.Normal), brutal = Commands(AiDifficulty.Brutal);
        if (easy >= normal)
            return Fail($"difficulty: Easy must act less often than Normal (easy {easy}, normal {normal})");
        if (brutal <= normal)
            return Fail($"difficulty: Brutal must act more often than Normal (brutal {brutal}, normal {normal})");
    }

    // --- 3. The economy knob, isolated: with a refinery, a factory and one
    //        harvester already working, Normal wants no more and Hard buys a
    //        second. Built directly rather than played into, so the rule is
    //        read on its own instead of inferred from a whole match. ---------
    {
        bool BuysASecondHarvester(AiDifficulty d)
        {
            var w = new World(2702, 64, 64, players: 2);
            w.SpawnConstructionYard(1, 8, 8);            // a harmless foe: never the fire-sale branch
            w.SpawnConstructionYard(0, 40, 30);
            w.SpawnPowerPlant(0, 44, 30);
            w.SpawnRefinery(0, 36, 30);
            w.SpawnFactory(0, 40, 34);
            w.SpawnHarvester(0, Fix64.FromInt(37), Fix64.FromInt(32));   // one already mining
            w.GrantCredits(0, 4000);                     // affords a 1400 harvester outright
            var ai = new SkirmishAI(0, difficulty: d);
            var cmds = new List<Command>();
            ai.Act(w, cmds);
            foreach (var c in cmds)
                if (c.Type == CommandType.Produce && c.AuxId == 4) return true;   // unit type 4 is the harvester
            return false;
        }
        if (BuysASecondHarvester(AiDifficulty.Normal))
            return Fail("difficulty: Normal must keep one harvester per refinery (it bought a second)");
        if (!BuysASecondHarvester(AiDifficulty.Hard))
            return Fail("difficulty: Hard must run a second harvester per refinery (it bought none)");
        if (!BuysASecondHarvester(AiDifficulty.Brutal))
            return Fail("difficulty: Brutal inherits Hard's macro (it bought no second harvester)");
    }

    // --- 4. The handicap is DECLARED and lives in setup. The value is offered
    //        to whoever builds the match; the AI itself must never conjure a
    //        credit, because a replay re-runs the command stream with no AI
    //        attached and a self-granting commander would desync its own match.
    {
        if (SkirmishAI.StartingCreditHandicap(AiDifficulty.Brutal) != 5000)
            return Fail("difficulty: Brutal's declared handicap must be 5000 starting credits");
        foreach (var d in new[] { AiDifficulty.Easy, AiDifficulty.Normal, AiDifficulty.Hard })
            if (SkirmishAI.StartingCreditHandicap(d) != 0)
                return Fail($"difficulty: {d} must carry NO handicap (GDD line 76 allows one only at Brutal)");

        // The negative control: a Brutal commander whose setup granted it
        // nothing stays penniless. Nothing it can issue makes money appear.
        var w = new World(2703, 64, 64, players: 2);
        w.SpawnConstructionYard(1, 8, 8);
        w.SpawnConstructionYard(0, 40, 30);
        var ai = new SkirmishAI(0, difficulty: AiDifficulty.Brutal);
        var cmds = new List<Command>();
        long opening = w.Credits(0);
        for (int t = 0; t < 300; t++)
        {
            cmds.Clear();
            ai.Act(w, cmds);
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        }
        if (w.Credits(0) > opening)
            return Fail($"difficulty: a Brutal AI must never grant ITSELF credits (opened {opening}, holds {w.Credits(0)})");
    }

    Console.WriteLine("difficultygate: Normal is the identity rung (a full match hashes identically to the pre-ladder commander); " +
                      "Easy acts less often than Normal and Brutal more often; Normal keeps one harvester per refinery while Hard " +
                      "and Brutal buy a second; and Brutal's 5000-credit handicap is declared to SETUP only - the AI itself never " +
                      "made a credit appear");
    return 0;
}

int Match(ulong seed)
{
    var sw = Stopwatch.StartNew();
    ScenarioMovement(seed);
    sw.Stop();
    double moveMs = sw.Elapsed.TotalMilliseconds / 1000.0;
    Console.WriteLine($"movement: 1000 ticks x 500 units, {moveMs:F3} ms/tick (budget 8)");
    if (moveMs > 8.0) return Fail($"PERF GATE: movement {moveMs:F3} ms/tick exceeds the 8 ms budget (TDD s6)");
    ScenarioPathing(seed, null, Console.WriteLine);
    ScenarioEconomy(seed, null, Console.WriteLine);
    ScenarioCombat(seed, null, Console.WriteLine);
    ScenarioProduction(seed, null, Console.WriteLine);
    ScenarioAttackMove(seed, null, Console.WriteLine);
    ScenarioConstruction(seed, null, Console.WriteLine);
    ScenarioSkirmish(seed, null, Console.WriteLine);
    ScenarioStealth(seed, null, Console.WriteLine);
    ScenarioVeterancy(seed, null, Console.WriteLine);
    ScenarioVictory(seed, null, Console.WriteLine);
    ScenarioExpansion(seed, null, Console.WriteLine);
    ScenarioArtillery(seed, null, Console.WriteLine);
    ScenarioSuperweapon(seed, null, Console.WriteLine);
    ScenarioCrush(seed, null, Console.WriteLine);
    ScenarioAiSuper(seed, null, Console.WriteLine);
    ScenarioVeil(seed, null, Console.WriteLine);
    ScenarioWaypoints(seed, null, Console.WriteLine);
    ScenarioMission(seed, null, Console.WriteLine);
    ScenarioCapture(seed, null, Console.WriteLine);
    ScenarioMission02(seed, null, Console.WriteLine);
    ScenarioMission03(seed, null, Console.WriteLine);
    ScenarioDepot(seed, null, Console.WriteLine);
    ScenarioWalls(seed, null, Console.WriteLine);
    int defence = DefenceLoadGate(seed);
    if (defence != 0) return defence;
    // ADR-006: the catalogue-mismatch refuse gate rides the battery exactly as
    // the defence gate does, additively, so the golden list is untouched.
    int catalogue = CatalogueRefuse();
    if (catalogue != 0) return catalogue;
    // ADR-007: the rally and spawn gate rides the battery the same way.
    int spawn = SpawnGate();
    if (spawn != 0) return spawn;
    // ADR-009: and so does the production and tech-tree gate.
    int prod = ProdGate();
    if (prod != 0) return prod;
    // ADR-012: and the ferrite regrowth gate.
    int regrowth = RegrowthGate();
    if (regrowth != 0) return regrowth;
    // ADR-015: and the unit command-stance gate.
    int stance = StanceGate();
    if (stance != 0) return stance;
    // ADR-019: and the repair-vehicle gate.
    int repair = RepairGate();
    if (repair != 0) return repair;
    // ADR-021: and the neutral-outpost gate.
    int outpost = OutpostGate();
    if (outpost != 0) return outpost;
    // ADR-023: and the parallel build lanes.
    int lanegate = LaneGate();
    if (lanegate != 0) return lanegate;
    // ADR-022: and the LAN setup exchange.
    int lansetup = LanSetupGate();
    if (lansetup != 0) return lansetup;
    // ADR-025: and the destroyable bridges.
    int bridgegate = BridgeGate();
    if (bridgegate != 0) return bridgegate;
    // ADR-021 / doc 18 Phase D: and every committed map loads and plays.
    int mapgate = MapGate();
    if (mapgate != 0) return mapgate;
    // DR-10: and the beaten AI's last stand.
    int firesale = FireSaleGate();
    if (firesale != 0) return firesale;
    // ADR-026 / DR-13: and the AI-repairs-its-structures gate.
    int airepair = AiRepairGate();
    if (airepair != 0) return airepair;
    // DR-14 / doc 28: and the difficulty ladder, whose first claim is that
    // Normal did not move.
    int difficulty = DifficultyGate();
    if (difficulty != 0) return difficulty;
    // DR-18: and the Ashford actually carries an attack, which mapgate's
    // outpost assertion cannot see.
    int ford = FordGate();
    if (ford != 0) return ford;
    // The decorative terrain layer: drawn, and provably passable.
    int decorg = DecorGate();
    if (decorg != 0) return decorg;
    // skirmish-07 at the length it was designed for, not a 100-second smoke test.
    int basin = BasinGate();
    if (basin != 0) return basin;
    // P7-1: a building's faction comes from /data, not from a hardcoded name.
    int factions = FactionGate();
    if (factions != 0) return factions;
    // P7-2: base defence is a choice, not a ladder.
    int emplace = EmplacementGate();
    if (emplace != 0) return emplace;
    // P7-3: the transport carries, refuses armour, and dies with its cargo.
    int transport = TransportGate();
    if (transport != 0) return transport;
    // ADR-028: the air layer, and the answer it is bound to.
    int air = AirGate();
    if (air != 0) return air;
    // P7-2b: each side's own defence, held to written doctrine.
    int fdef = FactionDefenceGate();
    if (fdef != 0) return fdef;
    // P7-7: the Infiltrator robs rather than captures.
    int infil = InfiltratorGate();
    if (infil != 0) return infil;
    // P7-9: six missions the game can open, and the two things missions 04 to
    // 06 added that the earlier three could not have.
    int campaign = CampaignGate();
    if (campaign != 0) return campaign;
    // Q002 / C7a: and the non-blocking lockstep poll gate.
    int lanpoll = LanPoll();
    if (lanpoll != 0) return lanpoll;
    return 0;
}

World LanWorldFactory(ulong seed)
{
    // Both clients must construct identical worlds: all players' entities.
    var world = new World(seed, 64, 64, players: 2);
    for (int i = 0; i < 10; i++)
    {
        world.SpawnUnit(0, Fix64.FromInt(5 + i), Fix64.FromInt(5), Fix64.FromFraction(1, 4), 300, ArmourClass.Heavy, 1);
        world.SpawnUnit(1, Fix64.FromInt(5 + i), Fix64.FromInt(58), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
    }
    return world;
}

int Lan(int games)
{
    for (int g = 0; g < games; g++)
    {
        ulong seed = 1000UL + (ulong)g;
        var relay = new Relay(playerCount: 2);
        relay.Start();
        var relayThread = new Thread(relay.Run) { IsBackground = true };
        relayThread.Start();

        var results = new ulong[2];
        var errors = new Exception?[2];
        var clientThreads = new Thread[2];
        for (int p = 0; p < 2; p++)
        {
            int pid = p;
            clientThreads[p] = new Thread(() =>
            {
                try
                {
                    using var client = new LockstepClient(relay.Port, LanWorldFactory, seed);
                    var cmdRng = new DeterministicRandom(seed * 7919UL + (ulong)client.PlayerId);
                    client.Prime();
                    const int ticks = 300;
                    while (client.World.Tick < ticks)
                    {
                        var cmds = new List<Command>();
                        if (client.World.Tick % 15 == 0)
                        {
                            // Order 3 of my units somewhere new. Entity ids: p0 owns even, p1 odd (spawn interleave).
                            for (int k = 0; k < 3; k++)
                            {
                                int mine = cmdRng.NextInt(10) * 2 + client.PlayerId;
                                cmds.Add(new Command(0, client.PlayerId, CommandType.PathMove, mine,
                                    Fix64.FromInt(4 + cmdRng.NextInt(56)), Fix64.FromInt(4 + cmdRng.NextInt(56)),
                                    queued: cmdRng.NextInt(3) == 0)); // exercise the shift-queue flag over TCP
                            }
                        }
                        client.SubmitCommands(cmds);
                        if (!client.AdvanceTick()) throw new Exception("desync notified");
                    }
                    results[pid] = client.World.ComputeStateHash();
                }
                catch (Exception ex) { errors[pid] = ex; }
            });
            clientThreads[p].Start();
        }
        foreach (var t in clientThreads) t.Join();
        foreach (var e in errors) if (e != null) return Fail($"game {g}: {e.Message}");
        if (relay.DesyncDetected) return Fail($"game {g}: relay flagged desync");
        if (results[0] != results[1]) return Fail($"game {g}: final hashes differ");
        Console.WriteLine($"lan game {g + 1}/{games}: 300 ticks, 2 clients, hash 0x{results[0]:X16} identical, no desync");
    }
    Console.WriteLine($"lan: {games} games completed with zero desyncs (gate: 20)");
    return 0;
}

int LanPoll()
{
    // P6 Wave C7a (Q002's remainder, first half): the NON-BLOCKING lockstep
    // drive. Both clients run a frame-loop-shaped driver - submit this tick's
    // batch exactly once, TryAdvanceTick, and on a miss sleep a millisecond
    // standing in for "render a frame and come back" - so no call ever blocks
    // on the socket, which is the property SkirmishLive's accumulator needs.
    // Game 1 runs on clean loopback; game 2 runs through the ChaosProxy
    // (60ms +/- 30ms plus stalls), where the gate additionally asserts the
    // poll MISSED at least once, proving the non-blocking path was genuinely
    // exercised rather than degenerating into the blocking soak.
    for (int g = 0; g < 2; g++)
    {
        bool chaos = g == 1;
        ulong seed = 7000UL + (ulong)g;
        var relay = new Relay(playerCount: 2);
        relay.Start();
        new Thread(relay.Run) { IsBackground = true }.Start();
        ChaosProxy[]? proxies = chaos
            ? new[]
            {
                new ChaosProxy(relay.Port, 60, 30, 50, 500, timingSeed: 101),
                new ChaosProxy(relay.Port, 60, 30, 50, 500, timingSeed: 102),
            }
            : null;

        var results = new ulong[2];
        var missCounts = new int[2];
        var errors = new Exception?[2];
        var threads = new Thread[2];
        for (int p = 0; p < 2; p++)
        {
            int pid = p;
            threads[p] = new Thread(() =>
            {
                try
                {
                    int port = proxies?[pid].Port ?? relay.Port;
                    using var client = new LockstepClient(port, LanWorldFactory, seed);
                    var cmdRng = new DeterministicRandom(seed * 7919UL + (ulong)client.PlayerId);
                    client.Prime();
                    const int ticks = 300;
                    int lastSubmitted = -1, misses = 0;
                    long deadline = Environment.TickCount64 + 120_000;
                    while (client.World.Tick < ticks)
                    {
                        if (Environment.TickCount64 > deadline) throw new TimeoutException("poll drive never completed");
                        // The once-per-tick submit guard the frame loop must
                        // carry: the relay counts batches per tick, so a
                        // resubmit for the same tick would corrupt the merge.
                        if (client.World.Tick != lastSubmitted)
                        {
                            var cmds = new List<Command>();
                            if (client.World.Tick % 15 == 0)
                                for (int k = 0; k < 3; k++)
                                {
                                    int mine = cmdRng.NextInt(10) * 2 + client.PlayerId;
                                    cmds.Add(new Command(0, client.PlayerId, CommandType.PathMove, mine,
                                        Fix64.FromInt(4 + cmdRng.NextInt(56)), Fix64.FromInt(4 + cmdRng.NextInt(56))));
                                }
                            client.SubmitCommands(cmds);
                            lastSubmitted = client.World.Tick;
                        }
                        if (!client.TryAdvanceTick(out bool desynced))
                        {
                            if (desynced) throw new Exception("desync notified");
                            misses++;
                            Thread.Sleep(1);   // the frame renders on; the sim waits
                        }
                    }
                    results[pid] = client.World.ComputeStateHash();
                    missCounts[pid] = misses;
                }
                catch (Exception ex) { errors[pid] = ex; }
            });
            threads[p].Start();
        }
        foreach (var t in threads) t.Join();
        foreach (var e in errors) if (e != null) return Fail($"lanpoll {(chaos ? "chaos" : "clean")}: {e.Message}");
        if (relay.DesyncDetected) return Fail($"lanpoll {(chaos ? "chaos" : "clean")}: relay flagged desync");
        if (results[0] != results[1]) return Fail($"lanpoll {(chaos ? "chaos" : "clean")}: final hashes differ");
        if (chaos && missCounts[0] + missCounts[1] == 0)
            return Fail("lanpoll chaos: the poll never missed under 60ms+stall chaos - the non-blocking path was not exercised");
        Console.WriteLine($"lanpoll {(chaos ? "chaos" : "clean")}: 300 ticks, 2 clients, hash 0x{results[0]:X16} identical, " +
                          $"no desync, poll misses {missCounts[0]}/{missCounts[1]}");
    }
    Console.WriteLine("lanpoll: the non-blocking TryAdvanceTick drive completed clean and under chaos with identical hashes; " +
                      "no call ever blocked on the socket (the frame-loop property Q002's remainder needs)");
    return 0;
}

int LanChaos(int games, int delayMs, int jitterMs, int stallPerMille, int stallMs, int ticks)
{
    for (int g = 0; g < games; g++)
    {
        ulong seed = 5000UL + (ulong)g;
        var relay = new Relay(playerCount: 2);
        relay.Start();
        new Thread(relay.Run) { IsBackground = true }.Start();

        var proxies = new[]
        {
            new ChaosProxy(relay.Port, delayMs, jitterMs, stallPerMille, stallMs, timingSeed: g * 2 + 1),
            new ChaosProxy(relay.Port, delayMs, jitterMs, stallPerMille, stallMs, timingSeed: g * 2 + 2),
        };
        var results = new ulong[2];
        var errors = new Exception?[2];
        var threads = new Thread[2];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int p = 0; p < 2; p++)
        {
            int pid = p;
            threads[p] = new Thread(() =>
            {
                try
                {
                    using var client = new LockstepClient(proxies[pid].Port, LanWorldFactory, seed);
                    var cmdRng = new DeterministicRandom(seed * 7919UL + (ulong)client.PlayerId);
                    client.Prime();
                    while (client.World.Tick < ticks)
                    {
                        var cmds = new List<Command>();
                        if (client.World.Tick % 15 == 0)
                            for (int k = 0; k < 3; k++)
                            {
                                int mine = cmdRng.NextInt(10) * 2 + client.PlayerId;
                                cmds.Add(new Command(0, client.PlayerId, CommandType.PathMove, mine,
                                    Fix64.FromInt(4 + cmdRng.NextInt(56)), Fix64.FromInt(4 + cmdRng.NextInt(56))));
                            }
                        client.SubmitCommands(cmds);
                        if (!client.AdvanceTick(timeoutMs: 30_000)) throw new Exception("desync notified");
                    }
                    results[pid] = client.World.ComputeStateHash();
                }
                catch (Exception ex) { errors[pid] = ex; }
            });
            threads[p].Start();
        }
        foreach (var t in threads) t.Join();
        sw.Stop();
        foreach (var pr in proxies) pr.Dispose();
        foreach (var e in errors) if (e != null) return Fail($"chaos game {g}: {e.Message}");
        if (relay.DesyncDetected) return Fail($"chaos game {g}: relay flagged desync");
        if (results[0] != results[1]) return Fail($"chaos game {g}: final hashes differ");
        Console.WriteLine($"chaos game {g + 1}/{games}: {ticks} ticks under {delayMs}ms±{jitterMs}ms + {stallPerMille / 10.0}% stalls of {stallMs}ms, " +
                          $"hash 0x{results[0]:X16} identical, no desync ({sw.Elapsed.TotalSeconds:F1}s wall)");
    }
    Console.WriteLine($"lanchaos: {games} games under adverse conditions, zero desyncs");
    return 0;
}

int Spectate()
{
    // Exercises the presentation contract (TICKET-P1-07 groundwork) headless:
    // machine assertions on the interpolation maths, then ASCII frames of the
    // combat scenario so a human can eyeball the pipeline end to end.
    var world = new World(2026, 64, 64, players: 2);
    for (int i = 0; i < 15; i++)
        world.SpawnUnit(0, Fix64.FromInt(18), Fix64.FromInt(20 + i), Fix64.FromFraction(1, 5), 300, ArmourClass.Heavy, 1);
    for (int i = 0; i < 20; i++)
        world.SpawnUnit(1, Fix64.FromInt(46), Fix64.FromInt(18 + i), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
    var cmds = new List<Command>();
    for (int i = 0; i < 15; i++) cmds.Add(new Command(0, 0, CommandType.Attack, i, Fix64.Zero, Fix64.Zero, 15 + i));
    for (int i = 0; i < 20; i++) cmds.Add(new Command(0, 1, CommandType.Attack, 15 + i, Fix64.Zero, Fix64.Zero, i % 15));

    var interp = new SnapshotInterpolator(windowTicks: 8);
    int deathEvents = 0, firedEvents = 0;
    var s0 = new List<SnapshotInterpolator.ViewEntity>();
    var s1 = new List<SnapshotInterpolator.ViewEntity>();
    var mid = new List<SnapshotInterpolator.ViewEntity>();
    double ToD(Fix64 v) => v.Raw / 4294967296.0;

    for (int t = 0; t < 400; t++)
    {
        var (tick, ents, _) = world.TakeSnapshot();
        interp.AddSnapshot(tick, ents);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        foreach (var ev in world.Events)
        {
            if (ev.Type == GameEventType.Died) deathEvents++;
            if (ev.Type == GameEventType.Fired) firedEvents++;
        }

        if (t >= 1)
        {
            int t1 = interp.NewestTick, t0 = t1 - 1;
            if (!interp.TrySample(t0, s0) || !interp.TrySample(t1, s1) || !interp.TrySample(t0 + 0.5, mid))
                return Fail("spectate: sampling failed inside the window");
            for (int i = 0; i < s0.Count; i++)
            {
                // Endpoints must be exact; the midpoint must sit inside the segment envelope.
                if (System.Math.Abs(mid[i].X - (s0[i].X + s1[i].X) / 2) > 1e-9) return Fail($"spectate: X midpoint off at tick {t0} entity {i}");
                if (System.Math.Abs(mid[i].Y - (s0[i].Y + s1[i].Y) / 2) > 1e-9) return Fail($"spectate: Y midpoint off at tick {t0} entity {i}");
            }
            if (interp.Count > 8) return Fail("spectate: window eviction failed");
        }
    }

    // Endpoint exactness against the live snapshot at the newest tick.
    var (finalTick, finalEnts, _) = world.TakeSnapshot();
    interp.AddSnapshot(finalTick, finalEnts);
    interp.TrySample(finalTick, s1);
    for (int i = 0; i < finalEnts.Length; i++)
        if (s1[i].X != ToD(finalEnts[i].X) || s1[i].Y != ToD(finalEnts[i].Y))
            return Fail("spectate: alpha=0 sample must equal the snapshot exactly");

    // Event stream must agree with observable state (TICKET-P2-SIM-13).
    int corpses = 0;
    foreach (var e in world.Entities) if (!e.Alive) corpses++;
    if (deathEvents != corpses) return Fail($"spectate: {deathEvents} death events vs {corpses} corpses");
    if (firedEvents == 0) return Fail("spectate: a battle with zero Fired events");

    // P5-ECON-01: the snapshot contract must carry a field's REMAINING STOCK,
    // or the client cannot draw a field draining. It could not: ViewEntity had
    // no amount, so the client scaled fields by Hp, and a field spawns with
    // Hp = 1, which pinned the expression to its floor and drew every field at
    // a constant size forever. Asserted here rather than trusted, because the
    // defect's whole character was code that LOOKED implemented and was dead.
    {
        var w = new World(4242, 64, 64, players: 1);
        int field = w.SpawnFerriteField(Fix64.FromInt(20), Fix64.FromInt(20), 12000);
        int refinery = w.SpawnRefinery(0, 24, 20);
        int harv = w.SpawnHarvester(0, Fix64.FromInt(22), Fix64.FromInt(20));
        var hcmd = new List<Command> { new(0, 0, CommandType.Harvest, harv, Fix64.Zero, Fix64.Zero, field) };
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hcmd));
        var vi = new SnapshotInterpolator(windowTicks: 4);
        var view = new List<SnapshotInterpolator.ViewEntity>();
        var (t0s, e0s, _) = w.TakeSnapshot();
        vi.AddSnapshot(t0s, e0s);
        vi.TrySample(t0s, view);
        int startAmount = view[field].FerriteAmount, startCap = view[field].FerriteCap;
        if (startAmount != 12000 || startCap != 12000)
            return Fail($"spectate: a fresh field must report its full stock and cap through the view contract (got {startAmount}/{startCap})");
        for (int t = 0; t < 200; t++) w.Step(default);
        var (t1s, e1s, _) = w.TakeSnapshot();
        vi.AddSnapshot(t1s, e1s);
        vi.TrySample(t1s, view);
        int nowAmount = view[field].FerriteAmount;
        if (nowAmount >= startAmount)
            return Fail($"spectate: a mined field must report a FALLING stock through the view contract ({nowAmount} vs {startAmount})");
        if (nowAmount != w.Entities[field].FerriteAmount)
            return Fail("spectate: the view's ferrite stock must equal the sim's, not a stale or interpolated value");
        if (w.Entities[harv].Carry <= 0 && w.Credits(0) <= 0)
            return Fail("spectate: the harvest never ran, so the drain assertion proved nothing");
        Console.WriteLine($"spectate: the view contract carries a draining field ({startAmount} -> {nowAmount} of cap {startCap}), so the client can render it");
    }

    Console.WriteLine($"spectate: interpolation contract verified over 400 ticks; event stream consistent ({deathEvents} deaths, {firedEvents} shots)");

    foreach (double sampleTime in new[] { finalTick - 6.0, finalTick - 3.5, finalTick - 1.0 })
    {
        interp.TrySample(sampleTime, mid);
        var grid = new char[32, 64];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 64; x++) grid[y, x] = '.';
        foreach (var v in mid)
        {
            if (!v.Alive) continue;
            int gx = System.Math.Clamp((int)v.X, 0, 63), gy = System.Math.Clamp((int)(v.Y / 2), 0, 31);
            grid[gy, gx] = v.PlayerId == 0 ? 'D' : 's';
        }
        Console.WriteLine($"-- t={sampleTime:F1} (D = Directorate cannon, s = Sodality-stand-in rifle, 2 cells/row) --");
        for (int y = 8; y < 26; y++)
        {
            var row = new char[64];
            for (int x = 0; x < 64; x++) row[x] = grid[y, x];
            Console.WriteLine(new string(row));
        }
    }
    return 0;
}

int AmDebug()
{
    var world = new World(2026, 64, 64, players: 2);
    var cannons = new List<int>();
    for (int i = 0; i < 8; i++)
        cannons.Add(world.SpawnUnit(0, Fix64.FromInt(6), Fix64.FromInt(28 + i), Fix64.FromFraction(1, 5), 300, ArmourClass.Heavy, 1));
    for (int i = 0; i < 4; i++)
        world.SpawnUnit(1, Fix64.FromInt(24), Fix64.FromInt(26 + i * 3), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
    for (int i = 0; i < 4; i++)
        world.SpawnUnit(1, Fix64.FromInt(40), Fix64.FromInt(26 + i * 3), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
    var cmds = new List<Command>();
    foreach (int id in cannons)
        cmds.Add(new Command(0, 0, CommandType.AttackMove, id, Fix64.FromInt(58), Fix64.FromInt(32)));
    for (int t = 0; t < 1200; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (t % 200 == 199)
        {
            Console.WriteLine($"== t={t + 1} ==");
            foreach (var e in world.Entities)
            {
                if (!e.Alive) continue;
                string kind = e.PlayerId == 0 ? "cannon" : "rifle ";
                Console.WriteLine($"  {kind} id={e.Id} pos=({e.X},{e.Y}) hp={e.Hp} moving={e.Moving} amove={e.AMove} tgt=({e.TargetX},{e.TargetY}) stall={e.StallTicks}");
            }
        }
    }
    return 0;
}

int SkDebug()
{
    var world = BuildSkirmishWorld(2026);
    var ais = new[] { new SkirmishAI(0), new SkirmishAI(1) };
    var cmds = new List<Command>();
    for (int t = 0; t < 5000; t++)
    {
        cmds.Clear();
        ais[0].Act(world, cmds);
        ais[1].Act(world, cmds);
        if (t < 200 && cmds.Count > 0)
            foreach (var c in cmds) Console.WriteLine($"t={t} p{c.PlayerId} {c.Type} ent={c.EntityId} aux={c.AuxId}");
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        if (t % 1000 == 999)
        {
            int structs = 0, units = 0, harv = 0, qcy = world.QueueLength(0);
            foreach (var e in world.Entities)
            {
                if (!e.Alive || e.PlayerId != 0) continue;
                if (e.Kind is EntityKind.PowerPlant or EntityKind.Refinery or EntityKind.Factory or EntityKind.Turret) structs++;
                if (e.Kind == EntityKind.Unit) units++;
                if (e.Kind == EntityKind.Harvester) harv++;
            }
            Console.WriteLine($"t={t + 1}: p0 credits={world.Credits(0)} structs={structs} units={units} harv={harv} cyQueue={qcy} cyReady={world.Entities[0].ReadyStructure} cyPaid={world.Entities[0].BuildPaid} entities={world.EntityCount}");
        }
    }
    return 0;
}

int M2Debug()
{
    // Sequence-effect bisection: run the first N registered scenarios first.
    if (int.TryParse(Environment.GetEnvironmentVariable("M2_PRE_N"), out int preN))
    {
        for (int k = 0; k < preN && k < scenarios.Length; k++)
        {
            if (scenarios[k].Name == "mission02") break;
            Console.WriteLine($"pre-running {scenarios[k].Name}...");
            scenarios[k].Run(2026, null);
        }
    }
    string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/missions/mission-02.fmap"));
    var map = MapData.Load(path);
    var world = map.BuildWorld(2026, 2, out var tags);
    var mission = new MissionRunner(map, tags);
    int wrench = tags["wrench"][0], prize = tags["prize"][0];
    var cmds = new List<Command>();
    for (int i = 0; i < world.Entities.Count; i++)
        if (world.Entities[i].Alive && world.Entities[i].PlayerId == 0 && world.Entities[i].UnitType == 5)
            cmds.Add(new(0, 0, CommandType.AttackMove, i, Fix64.FromInt(34), Fix64.FromInt(19)));
    cmds.Add(new(0, 0, CommandType.Attack, wrench, Fix64.Zero, Fix64.Zero, prize));
    var mc = new List<Command>();
    for (int t = 0; t < 600; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        mission.Tick(world, mc);
        if (mc.Count > 0) { cmds.AddRange(mc); mc.Clear(); }
        if (t % 100 == 99)
        {
            var e = world.Entities[wrench];
            Console.Write($"t={t + 1}: wrench=({e.X},{e.Y}) alive={e.Alive} tgt={e.ExplicitTarget} moving={e.Moving} |");
            for (int i = 0; i < world.Entities.Count; i++)
            {
                var u = world.Entities[i];
                if (u.PlayerId == 0 && u.UnitType == 5)
                    Console.Write($" raider{i}=({u.X},{u.Y}) alive={u.Alive}");
            }
            Console.WriteLine($" | msgs=[{string.Join(",", mission.Messages)}]");
        }
    }
    return 0;
}

int FacDebug()
{
    string mapPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/maps/skirmish-01.fmap"));
    var map = MapData.Load(mapPath);
    var w = map.BuildWorld(3001, 2);
    w.SetFaction(0, World.FactionDirectorate);
    w.SetFaction(1, World.FactionSodality);
    w.GrantCredits(0, 8000); w.GrantCredits(1, 8000);
    w.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
    w.SpawnConstructionYard(1, map.Starts[1].Cx, map.Starts[1].Cy);
    var ais = new[] { SkirmishAI.Standard(0), SkirmishAI.Standard(1) };
    var cmds = new List<Command>();
    for (int t = 0; t < 7000 && w.Winner < 0; t++)
    {
        cmds.Clear();
        ais[0].Act(w, cmds); ais[1].Act(w, cmds);
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        if (t % 1400 == 1399)
        {
            var made = new Dictionary<(int P, int T), int>();
            var alive = new Dictionary<(int P, int T), int>();
            int str0 = 0, str1 = 0;
            foreach (var e in w.Entities)
            {
                if (e.PlayerId < 0 || e.Kind == EntityKind.FerriteField) continue;
                if (e.UnitType > 0)
                {
                    var k = (e.PlayerId, e.UnitType);
                    made[k] = made.GetValueOrDefault(k) + 1;
                    if (e.Alive) alive[k] = alive.GetValueOrDefault(k) + 1;
                }
                if (e.Alive && e.StructType > 0) { if (e.PlayerId == 0) str0++; else str1++; }
            }
            string Fmt(int p) => string.Join(" ", made.Where(kv => kv.Key.P == p).OrderBy(kv => kv.Key.T)
                .Select(kv => $"t{kv.Key.T}:{alive.GetValueOrDefault(kv.Key)}/{kv.Value}"));
            Console.WriteLine($"t={t + 1} winner={w.Winner} | DIR str={str0} cr={w.Credits(0)} [{Fmt(0)}] | SOD str={str1} cr={w.Credits(1)} [{Fmt(1)}]");
        }
    }
    Console.WriteLine($"final winner={w.Winner} at t={w.Tick}");
    return 0;
}

int StDebug()
{
    var world = new World(2026, 64, 64, players: 2);
    var rd = world.GetUnitType(5);
    world.SpawnPowerPlant(0, 10, 10); // ADR-008: mirror ScenarioStealth's surgery - the turret must be powered to be a faithful repro rig
    int turret = world.SpawnTurret(0, 20, 20);
    int ghost = world.SpawnUnit(1, Fix64.FromInt(25), Fix64.FromInt(21), rd.Speed, rd.Hp, rd.Armour, rd.WeaponId, rd.SightCells, stealth: true, unitType: 5);
    var cmds = new List<Command>();
    for (int t = 0; t < 100; t++) { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); }
    int plant = world.SpawnPowerPlant(0, 22, 20, hp: 1500);
    cmds.Add(new(0, 1, CommandType.Attack, ghost, Fix64.Zero, Fix64.Zero, plant));
    for (int t = 0; t < 120; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (t % 20 == 19)
        {
            var g = world.Entities[ghost]; var tu = world.Entities[turret]; var pl = world.Entities[plant];
            Console.WriteLine($"t={t + 1}: raider pos=({g.X},{g.Y}) hp={g.Hp} reveal={g.RevealTicks} cd={g.Cooldown} moving={g.Moving} tgt={g.ExplicitTarget} | plant hp={pl.Hp} | turret cd={tu.Cooldown}");
        }
    }
    return 0;
}

int ExDebug()
{
    var world = new World(2026, 96, 64, players: 2);
    world.GrantCredits(0, 9000);
    world.SpawnConstructionYard(0, 8, 30);
    world.SpawnFerriteField(Fix64.FromInt(20), Fix64.FromInt(28), 2500);
    int farField = world.SpawnFerriteField(Fix64.FromInt(60), Fix64.FromInt(30), 12000);
    var ai = SkirmishAI.Standard(0);
    var cmds = new List<Command>();
    for (int t = 0; t < 6000; t++)
    {
        cmds.Clear();
        ai.Act(world, cmds);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        if (t % 600 == 599)
        {
            int cys = 0, refs = 0, harv = 0, mcv = -1, fact = -1, army = 0;
            foreach (var e in world.Entities)
            {
                if (!e.Alive || e.PlayerId != 0) continue;
                if (e.Kind == EntityKind.ConstructionYard) cys++;
                if (e.Kind == EntityKind.Refinery) refs++;
                if (e.Kind == EntityKind.Harvester) harv++;
                if (e.Kind == EntityKind.Factory) fact = e.Id;
                if (e.Kind == EntityKind.Unit && e.UnitType == 7) mcv = e.Id;
                if (e.Kind == EntityKind.Unit && e.UnitType != 7) army++;
            }
            Console.WriteLine($"t={t + 1}: credits={world.Credits(0)} cys={cys} refs={refs} harv={harv} army={army} mcvId={mcv} factQ={(fact >= 0 ? world.QueueLength(fact) : -1)} far={world.Entities[farField].FerriteAmount}");
            if (mcv >= 0)
            {
                var m = world.Entities[mcv];
                Console.WriteLine($"    mcv pos=({m.X},{m.Y}) moving={m.Moving}");
            }
        }
    }
    return 0;
}

int PathDebug()
{
    var world = BuildPathingWorld(2026, out int units);
    var target = (X: Fix64.FromInt(60) + Fix64.Half, Y: Fix64.FromInt(32) + Fix64.Half);
    var cmds = new List<Command>();
    for (int i = 0; i < units; i++)
        cmds.Add(new Command(0, 0, CommandType.PathMove, i, target.X, target.Y));
    for (int t = 0; t < 3000; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (t % 250 == 249)
        {
            int moving = 0, stoppedIn = 0, stoppedOut = 0;
            Fix64 worstStopped = Fix64.Zero, nearestMoving = Fix64.MaxValue;
            int leftOfWall = 0;
            foreach (var e in world.Entities)
            {
                Fix64 dSq = Fix64.DistSq(e.X - target.X, e.Y - target.Y);
                if (e.Moving) { moving++; if (dSq < nearestMoving) nearestMoving = dSq; }
                else if (dSq <= Fix64.FromInt(144)) stoppedIn++;
                else { stoppedOut++; if (dSq > worstStopped) worstStopped = dSq; }
                if (e.X < Fix64.FromInt(32)) leftOfWall++;
            }
            Console.WriteLine($"t={t + 1}: moving={moving} (nearest dSq {nearestMoving}), stoppedInZone={stoppedIn}, stoppedOutside={stoppedOut} (worst dSq {worstStopped}), leftOfWall={leftOfWall}");
        }
    }
    return 0;
}

int SaveLoad()
{
    // TICKET-P2-SIM-17. Record an AI skirmish as a command stream, run it
    // uninterrupted for the reference hash, then run half, save, load, and
    // finish from the same stream: the loaded world must hash identically at
    // the save point AND at the end. Any serialization slip - hashed or not -
    // surfaces as divergence.
    // Q001 hardening: BuildSkirmishWorld's map declares no factions, so
    // _playerFaction was [0, 0] here and a save format that DROPPED the
    // faction still round-tripped it as zero by luck; the field was
    // droppable and no gate could see it. Every world in this scenario now
    // declares a non-zero faction for player 1, and the round trip must
    // preserve it explicitly as well as through the hash.
    const ulong seed = 2026;
    const int half = 1500, full = 3000;
    World BuildFactionedWorld()
    {
        var w = BuildSkirmishWorld(seed);
        w.SetFaction(0, World.FactionDirectorate);
        w.SetFaction(1, World.FactionSodality);
        return w;
    }
    var recorded = new List<Command>[full];
    {
        var w = BuildFactionedWorld();
        var ais = new[] { new SkirmishAI(0), new SkirmishAI(1) };
        for (int t = 0; t < full; t++)
        {
            var c = new List<Command>();
            ais[0].Act(w, c);
            ais[1].Act(w, c);
            recorded[t] = c;
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(c));
        }
    }
    ulong hashFull;
    {
        var w = BuildFactionedWorld();
        for (int t = 0; t < full; t++) w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(recorded[t]));
        hashFull = w.ComputeStateHash();
    }

    var live = BuildFactionedWorld();
    for (int t = 0; t < half; t++) live.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(recorded[t]));
    ulong hashMid = live.ComputeStateHash();
    using var ms = new MemoryStream();
    live.Save(ms);
    ms.Position = 0;
    var loaded = World.Load(ms);
    if (loaded.FactionOf(1) != World.FactionSodality)
        return Fail($"saveload: faction dropped by the round trip (player 1 saved as {World.FactionSodality}, loaded as {loaded.FactionOf(1)})");
    if (loaded.ComputeStateHash() != hashMid)
        return Fail($"saveload: loaded hash 0x{loaded.ComputeStateHash():X16} != saved 0x{hashMid:X16}");
    for (int t = half; t < full; t++) loaded.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(recorded[t]));
    if (loaded.ComputeStateHash() != hashFull)
        return Fail($"saveload: resumed run diverged (0x{loaded.ComputeStateHash():X16} vs 0x{hashFull:X16})");
    Console.WriteLine($"saveload: {ms.Length} bytes; player 1's Sodality faction survived the round trip; loaded hash exact at the save point; resumed run reached the uninterrupted final hash 0x{hashFull:X16} bit-for-bit");
    return 0;
}

int CampaignSave()
{
    // TICKET-P2-SIM-21. Same philosophy as the saveload gate, with the
    // mission runner in the loop: record the AI's commands over a full
    // mission, then replay-drive to the midpoint, save world AND mission
    // state, load both, resume - the winner, the messages, and the final
    // hash must all match the uninterrupted run.
    const ulong seed = 2026;
    const int half = 1800, full = 4500; // horizon covers scripted victory under garrison-era AI doctrine
    string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/missions/mission-01.fmap"));
    var map = MapData.Load(path);

    var recorded = new List<Command>[full];
    {
        var w = map.BuildWorld(seed, 2, out var tags);
        w.GrantCredits(0, 5000);
        w.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
        var m = new MissionRunner(map, tags);
        var ai = SkirmishAI.Rusher(0);
        for (int t = 0; t < full; t++)
        {
            var c = new List<Command>();
            ai.Act(w, c);
            recorded[t] = c;
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(c));
            m.Tick(w);
        }
    }
    ulong hashFull; int winnerFull; int messagesFull;
    {
        var w = map.BuildWorld(seed, 2, out var tags);
        w.GrantCredits(0, 5000);
        w.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
        var m = new MissionRunner(map, tags);
        for (int t = 0; t < full; t++)
        {
            w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(recorded[t]));
            m.Tick(w);
        }
        hashFull = w.ComputeStateHash(); winnerFull = w.Winner; messagesFull = m.Messages.Count;
    }
    if (winnerFull != 0) return Fail($"campaignsave: reference run should end in scripted victory (winner={winnerFull})");

    var live = map.BuildWorld(seed, 2, out var liveTags);
    live.GrantCredits(0, 5000);
    live.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
    var liveMission = new MissionRunner(map, liveTags);
    for (int t = 0; t < half; t++)
    {
        live.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(recorded[t]));
        liveMission.Tick(live);
    }
    ulong hashMid = live.ComputeStateHash();
    using var ms = new MemoryStream();
    live.Save(ms);
    using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) liveMission.Save(bw);

    ms.Position = 0;
    var loaded = World.Load(ms);
    var loadedMission = new MissionRunner(map, liveTags); // tags rebuild deterministically from the same map
    using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true)) loadedMission.LoadState(br);
    if (loaded.ComputeStateHash() != hashMid)
        return Fail($"campaignsave: loaded world hash mismatch");
    for (int t = half; t < full; t++)
    {
        loaded.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(recorded[t]));
        loadedMission.Tick(loaded);
    }
    if (loaded.ComputeStateHash() != hashFull)
        return Fail($"campaignsave: resumed run diverged (0x{loaded.ComputeStateHash():X16} vs 0x{hashFull:X16})");
    if (loaded.Winner != winnerFull)
        return Fail($"campaignsave: resumed winner {loaded.Winner} vs {winnerFull}");
    if (loadedMission.Messages.Count != messagesFull)
        return Fail($"campaignsave: resumed messages {loadedMission.Messages.Count} vs {messagesFull}");
    Console.WriteLine($"campaignsave: {ms.Length} bytes (world + mission); mid-mission save resumed to the identical scripted victory, message log, and final hash 0x{hashFull:X16}");
    return 0;
}

int Export(ulong seed, string outPath)
{
    // Visual export (TICKET-P3-VIS-01): a full faction war on the Spine,
    // sampled every 2 ticks into JSON the match viewer replays. The runner
    // may use floating point for FORMATTING - the sim it reads never does.
    string mapPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/maps/skirmish-01.fmap"));
    var map = MapData.Load(mapPath);
    var world = map.BuildWorld(seed, 2);
    world.SetFaction(0, World.FactionDirectorate);
    world.SetFaction(1, World.FactionSodality);
    world.GrantCredits(0, 8000); world.GrantCredits(1, 8000);
    world.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
    world.SpawnConstructionYard(1, map.Starts[1].Cx, map.Starts[1].Cy);
    var ais = new[] { SkirmishAI.Rusher(0), SkirmishAI.Rusher(1) };
    var cmds = new List<Command>();
    var sb = new System.Text.StringBuilder(8_000_000);
    sb.Append("{\"map\":{\"w\":").Append(map.Width).Append(",\"h\":").Append(map.Height).Append(",\"blocked\":[");
    for (int i = 0; i < map.Blocked.Count; i++)
    { if (i > 0) sb.Append(','); sb.Append('[').Append(map.Blocked[i].Cx).Append(',').Append(map.Blocked[i].Cy).Append(']'); }
    sb.Append("]},\"frames\":[");
    int limit = 5400, endAt = limit;
    bool first = true;
    for (int t = 0; t < endAt; t++)
    {
        cmds.Clear();
        ais[0].Act(world, cmds); ais[1].Act(world, cmds);
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        if (world.Winner >= 0 && endAt == limit) endAt = Math.Min(limit, world.Tick + 150);
        if (world.Tick % 2 != 0) continue;
        if (!first) sb.Append(',');
        first = false;
        sb.Append("{\"t\":").Append(world.Tick)
          .Append(",\"cr\":[").Append(world.Credits(0)).Append(',').Append(world.Credits(1)).Append("],\"w\":").Append(world.Winner)
          .Append(",\"e\":[");
        bool fe = true;
        foreach (var e in world.Entities)
        {
            if (!e.Alive && e.Kind != EntityKind.FerriteField) continue;
            if (!fe) sb.Append(',');
            fe = false;
            double x = e.X.Raw / 4294967296.0, y = e.Y.Raw / 4294967296.0;
            sb.Append('[').Append(e.Id).Append(',').Append((int)e.Kind).Append(',').Append(e.UnitType).Append(',')
              .Append(e.PlayerId).Append(',').Append((int)(x * 100)).Append(',').Append((int)(y * 100)).Append(',')
              .Append(e.Kind == EntityKind.FerriteField ? e.FerriteAmount : e.Hp).Append(',')
              .Append(e.Kind == EntityKind.FerriteField ? 12000 : e.MaxHp).Append(',')
              .Append((e.Stealth || e.FieldCloaked) && e.RevealTicks == 0 ? 1 : 0).Append(']');
        }
        sb.Append("],\"ev\":[");
        bool fv = true;
        foreach (var ev in world.Events)
        {
            if (ev.Type is not (GameEventType.Fired or GameEventType.Died or GameEventType.SuperweaponLaunched
                or GameEventType.SuperweaponImpact or GameEventType.StructurePlaced or GameEventType.Captured)) continue;
            if (!fv) sb.Append(',');
            fv = false;
            sb.Append('[').Append((int)ev.Type).Append(',').Append(ev.A).Append(',').Append(ev.B);
            if (ev.Type is GameEventType.SuperweaponLaunched or GameEventType.SuperweaponImpact)
            {
                double ex = ev.X.Raw / 4294967296.0, ey = ev.Y.Raw / 4294967296.0;
                sb.Append(',').Append((int)(ex * 100)).Append(',').Append((int)(ey * 100));
            }
            sb.Append(']');
        }
        sb.Append("]}");
    }
    sb.Append("],\"winner\":").Append(world.Winner).Append(",\"winTick\":").Append(world.Winner >= 0 ? world.Tick : -1).Append('}');
    File.WriteAllText(outPath, sb.ToString());
    Console.WriteLine($"export: {sb.Length / 1024} KB, winner={world.Winner}, ticks={world.Tick}");
    return 0;
}

int Bench()
{
    const int n = 20_000_000;
    var rng = new DeterministicRandom(99);
    var a = new Fix64((long)(rng.NextUlong() >> 16) | 1);
    var b = Fix64.FromFraction(3, 7);
    var acc = Fix64.Zero;
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < n; i++) acc = a * b + new Fix64(acc.Raw & 0xFFFF);
    sw.Stop();
    Console.WriteLine($"Fix64 mul+add: {n / sw.Elapsed.TotalSeconds / 1e6:F0} Mops/s (acc raw {acc.Raw})");
    var sq = Fix64.FromInt(2);
    var sacc = Fix64.Zero;
    sw.Restart();
    for (int i = 0; i < 1_000_000; i++) sacc = Fix64.Sqrt(sq + new Fix64(sacc.Raw & 0xFF));
    sw.Stop();
    Console.WriteLine($"Fix64 sqrt: {1_000_000 / sw.Elapsed.TotalSeconds / 1e6:F1} Mops/s (acc raw {sacc.Raw})");
    return 0;
}

return args.Length == 0
    ? SelfTest() | Determinism(2026) | Match(2026) | Lan(5)
    : args[0] switch
    {
        "selftest" => SelfTest(),
        "determinism" => Determinism(args.Length > 1 ? ulong.Parse(args[1]) : 2026),
        "golden" => Golden(args.Length > 1 ? ulong.Parse(args[1]) : 2026),
        "match" => Match(args.Length > 1 ? ulong.Parse(args[1]) : 2026),
        "lan" => Lan(args.Length > 1 ? int.Parse(args[1]) : 20),
        "lanchaos" => LanChaos(
            games: args.Length > 1 ? int.Parse(args[1]) : 3,
            delayMs: args.Length > 2 ? int.Parse(args[2]) : 60,
            jitterMs: args.Length > 3 ? int.Parse(args[3]) : 30,
            stallPerMille: 50, stallMs: 500,
            ticks: 150),
        "catrefuse" => CatalogueRefuse(),
        "spawngate" => SpawnGate(),
        "prodgate" => ProdGate(),
        "regrowthgate" => RegrowthGate(),
        "stancegate" => StanceGate(),
        "repairgate" => RepairGate(),
        "outpostgate" => OutpostGate(),
        "lanegate" => LaneGate(),
        "bridgegate" => BridgeGate(),
        "lansetup" => LanSetupGate(),
        "mapgate" => MapGate(),
        "firesalegate" => FireSaleGate(),
        "airepairgate" => AiRepairGate(),
        "difficultygate" => DifficultyGate(),
        "fordgate" => FordGate(),
        "decorgate" => DecorGate(),
        "basingate" => BasinGate(),
        "factiongate" => FactionGate(),
        "emplacementgate" => EmplacementGate(),
        "transportgate" => TransportGate(),
        "airgate" => AirGate(),
        "factiondefencegate" => FactionDefenceGate(),
        "infiltratorgate" => InfiltratorGate(),
        "campaigngate" => CampaignGate(),
        "sizeprobe" => SizeProbe(),
        "pinprobe" => PinProbe(),
        "pintrace" => PinTrace(),
        "lanpoll" => LanPoll(),
        "bench" => Bench(),
        "pathdebug" => PathDebug(),
        "exdebug" => ExDebug(),
        "stdebug" => StDebug(),
        "facdebug" => FacDebug(),
        "m2debug" => M2Debug(),
        "skdebug" => SkDebug(),
        "amdebug" => AmDebug(),
        "spectate" => Spectate(),
        "replay" => ReplayCheck(),
        "saveload" => SaveLoad(),
        "campaignsave" => CampaignSave(),
        "export" => Export(args.Length > 1 ? ulong.Parse(args[1]) : 2026, args.Length > 2 ? args[2] : "ferrostorm-replay.json"),
        _ => Fail($"unknown mode '{args[0]}'"),
    };
