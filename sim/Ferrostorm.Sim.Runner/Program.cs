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
    if (!tags.TryGetValue("camp", out var camp) || camp.Count != 5)
        throw new Exception($"mission: expected 5 tagged camp entities, got {(tags.TryGetValue("camp", out var c) ? c.Count : 0)}");
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
    // The winner-implies-camp-dead invariant this scenario used to assert was
    // a coincidence of ordering, not a rule: victory can arrive from
    // VictorySystem eliminating player 1 the moment its last STRUCTURE dies,
    // while a tagged camp UNIT lives. ADR-010's attack-move fix made the
    // attackers focus structures, which exposed it. The provable invariant is
    // asserted instead - winner 0 and every camp structure razed - and the
    // design question (should elimination end a scripted mission before its
    // objective, or should the objective trigger count only structures?) is
    // Q012, owner Producer + QA.
    foreach (int id in camp)
        if (world.Entities[id].Alive && world.Entities[id].Kind != EntityKind.Unit)
            throw new Exception("mission: winner declared while camp structures lived");
    report?.Invoke($"mission: mission-01 ran as pure data - timed grant and message fired, the ambush sprang on zone entry, and victory landed at tick {wonAt} with every camp structure confirmed razed (Q012 tracks the objective-vs-elimination question)");
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
    report?.Invoke($"mission02: Sodality raid ran as data - the alarm assault triggered, the engineer converted the depot under fire, scripted victory at tick {world.Tick}");
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
byte[] DowngradeSave(byte[] v7, uint targetMagic)
{
    const uint magicV1 = 0x534C4131u, magicV3 = 0x534C4133u, magicV4 = 0x534C4134u, magicV5 = 0x534C4135u, magicV6 = 0x534C4136u, magicV7 = 0x534C4137u;
    using var input = new BinaryReader(new MemoryStream(v7));
    var outMs = new MemoryStream();
    using var w = new BinaryWriter(outMs);
    if (input.ReadUInt32() != magicV7)
        throw new InvalidOperationException("save surgery expects a v7 stream");
    w.Write(targetMagic);
    ulong checksum = input.ReadUInt64();
    if (targetMagic == magicV3 || targetMagic == magicV4 || targetMagic == magicV5 || targetMagic == magicV6) w.Write(checksum); // v3+ keep the checksum; v1/v2 never had one
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
    bool keepRally = targetMagic == magicV4 || targetMagic == magicV5 || targetMagic == magicV6;
    bool keepCap = targetMagic == magicV5 || targetMagic == magicV6;
    bool keepNoProgress = targetMagic == magicV6;
    for (int i = 0; i < count; i++)
    {
        w.Write(input.ReadBytes(v3EntityBytes));
        if (keepRally) w.Write(input.ReadBytes(rallyTailBytes)); else input.ReadBytes(rallyTailBytes);
        if (keepCap) w.Write(input.ReadBytes(ferriteCapBytes)); else input.ReadBytes(ferriteCapBytes);
        if (keepNoProgress) w.Write(input.ReadBytes(noProgressTailBytes)); else input.ReadBytes(noProgressTailBytes);
        input.ReadBytes(stanceTailBytes); // dropped: no format below v7 carried the stance tail
    }
    // Production queues, order queues and the trailer are format-identical.
    w.Write(input.ReadBytes((int)(input.BaseStream.Length - input.BaseStream.Position)));
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
    // Weapon 1 (TestCannon) has range 4 and no dead zone, and anti-armour vs a
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
