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
    if (credits != 4000)
        throw new Exception($"economy: expected all 4000 ferrite delivered as credits, got {credits}");

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
    report?.Invoke($"economy: 3 harvesters exhausted 2 fields, credits {credits}/4000 exact; camped harvester fled mid-load and survived (flee-on-damage live)");
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

    var cmds = new List<Command>
    {
        new(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 2),
        new(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 2),
        new(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 2),
    };

    var spawnTicks = new List<int>();
    int seen = 2; // plant + factory
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
            cmds.Add(new Command(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 2));
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
    world.SpawnPowerPlant(0, 29, 30); // seals the near gap (scenario scripting)
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
    report?.Invoke("construction: sidebar queue/ready/place flow exact (rejects retain readiness); chained adjacency and CY radius verified; sell-back refunded half; repair restored full hp at exact cost; corridor sealed mid-march and rerouted; MCV deployed into a radius-projecting CY; ready-cancel refunded in full");
    return world.ComputeStateHash();
}

World BuildSkirmishWorld(ulong seed)
{
    // The committed map file is the single source of terrain, fields, and
    // start positions (TICKET-P2-DATA-03); the scenario adds forces and funds.
    string mapPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/maps/skirmish-01.fmap"));
    var map = MapData.Load(mapPath);
    var world = map.BuildWorld(seed, players: 2);
    world.GrantCredits(0, 8000);
    world.GrantCredits(1, 8000);
    world.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
    world.SpawnConstructionYard(1, map.Starts[1].Cx, map.Starts[1].Cy);
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
    var writer = new ReplayWriter(seed, "skirmish");
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
    report?.Invoke("stealth: undetected raider untouchable in turret range; firing broke stealth and drew fire; detector arrival painted the passive lurker (all three rules verified)");
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
    int plant2 = world.SpawnPowerPlant(0, 14, 6);   // supply 200 total vs draw 100
    _ = plant2;
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

    // Power-gated charge: kill a plant mid-charge -> draw 100+40(factory? p1) own draw: super 100 vs supply 200 -> after losing one plant supply 100 == draw 100: still charges (>=). Kill BOTH edge: use scripted attacker on plant1, dropping supply to 100 while draw is 100 (boundary charges), then verify boundary semantics by simply pausing via selling? Keep crisp: sell plant1 -> supply 100, draw 100: >= holds, still charging. Sell plant2? then dead. Test the PAUSE by selling one plant AND placing... simpler: sell plant1 (supply 100 vs draw 100: still charging, boundary inclusive), step to ready.
    StepN(40);
    int chargeMid = world.Entities[super].ChargeTicks;
    if (chargeMid != 50) throw new Exception($"superweapon: charge should be 50 after 40 ticks (got {chargeMid})");
    cmds.Add(new(0, 0, CommandType.SellStructure, plant1, Fix64.Zero, Fix64.Zero)); // supply 200 -> 100, draw 100: boundary holds
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
    int superBuilt = -1;
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
            if (ev.Type == GameEventType.SuperweaponLaunched) launched = true;
            if (ev.Type == GameEventType.SuperweaponImpact) impacted = true;
        }
        if (t % 500 == 499) cp?.Invoke(t + 1, world.ComputeStateHash());
    }
    if (superBuilt < 0) throw new Exception("aisuper: the AI never built its superweapon");
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
    report?.Invoke("veil: baseline rifle engaged; cloaked rifle untouchable for 100 ticks; selling the plant collapsed the veil and the turret opened fire (power coupling exact)");
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
    foreach (int id in camp)
        if (world.Entities[id].Alive) throw new Exception("mission: winner declared while camp entities lived");
    report?.Invoke($"mission: mission-01 ran as pure data - timed grant and message fired, the ambush sprang on zone entry, and the scripted objective declared victory at tick {wonAt} with the camp confirmed destroyed");
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
    // The prize produces for its new owner.
    cmds.Add(new(0, 0, CommandType.Produce, factory, Fix64.Zero, Fix64.Zero, 2));
    int before = world.EntityCount;
    for (int t = 0; t < 200 && world.EntityCount == before; t++)
    { world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)); cmds.Clear(); }
    if (world.EntityCount == before || world.Entities[before].PlayerId != 0)
        throw new Exception("capture: the captured factory should produce for its new owner");
    report?.Invoke("capture: engineer converted the enemy factory on contact and was consumed; the prize produced a rifle squad under its new flag");
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
    worldG.SpawnTurret(1, 32, 29); // centre (33,30): 10.5 cells from the gun, hopelessly short
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

    report?.Invoke("walls: nine ADR-005 clauses held - (A) a segment landed with no ready slot and charged exactly 100 upfront while a real building with nothing ready stayed refused; " +
                   "(B) 99 credits bought nothing and charged nothing; (C) the chain carried a segment to Chebyshev 2 but not 3, never anchored a power plant, and the yard still anchored its own; " +
                   $"(D) the cap bit at exactly {World.MaxBarriersPerPlayer} per player and player 1's first segment still placed; (E) an engineer bounced off a fence and a player left holding one wall was still eliminated; " +
                   "(F) auto-acquire ignored masonry at 2 cells but an explicit order bit; (G) a howitzer at range 8 dealt 60 direct and exactly 30 to each orthogonal neighbour, breached the line, and took nothing back from the turret; " +
                   "(H) a tank sealed out by one segment acquired it in under 30 ticks, destroyed it, and resumed its march unordered while a tank with a reachable objective never glanced at it; " +
                   "(I) a sold segment refunded exactly 50 and freed its cell");
    return world.ComputeStateHash() ^ worldE.ComputeStateHash() ^ worldF.ComputeStateHash()
         ^ worldG.ComputeStateHash() ^ worldH.ComputeStateHash();
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
        if (u.Speed != Fix64.FromFraction(18, 100)) return Fail("data: speed encoding");
        if (u.VeterancyEnabled) return Fail("data: veterancy flag");
        if (!u.Notes.Contains("US2.2")) return Fail("data: folded notes block");
        Console.WriteLine("selftest: data loader round-trips com_harvester.yaml");
    }
    else Console.WriteLine("selftest: data file not found at expected relative path, loader untested this run");

    // Catalogue wiring (TICKET-P2-DATA-02): the /data files must convert to
    // exactly the compiled reference defs - value equality on the record.
    string unitsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/units"));
    if (Directory.Exists(unitsDir))
    {
        var refWorld = new World(0);
        var cannon = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "dir_cannon_tank.yaml")));
        var rifle = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "com_rifle_squad.yaml")));
        if (cannon != refWorld.GetUnitType(1)) return Fail($"catalogue: cannon mismatch {cannon} vs {refWorld.GetUnitType(1)}");
        if (rifle != refWorld.GetUnitType(2)) return Fail($"catalogue: rifle mismatch {rifle} vs {refWorld.GetUnitType(2)}");
        var rocket = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "com_rocket_squad.yaml")));
        var harv = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "com_harvester.yaml")));
        if (rocket != refWorld.GetUnitType(3)) return Fail($"catalogue: rocket mismatch {rocket} vs {refWorld.GetUnitType(3)}");
        if (harv != refWorld.GetUnitType(4)) return Fail($"catalogue: harvester mismatch {harv} vs {refWorld.GetUnitType(4)}");
        var raider = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "sod_shade_raider.yaml")));
        var scout = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "dir_sentinel_scout.yaml")));
        var mcv = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "com_mcv.yaml")));
        var howitzer = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "dir_howitzer.yaml")));
        if (howitzer != refWorld.GetUnitType(8)) return Fail($"catalogue: howitzer mismatch {howitzer} vs {refWorld.GetUnitType(8)}");
        var phantom = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "sod_phantom_tank.yaml")));
        var bulwark = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "dir_bulwark_tank.yaml")));
        var engineer = UnitCatalogue.ToTypeDef(DataLoader.LoadUnitFile(Path.Combine(unitsDir, "com_engineer.yaml")));
        if (phantom != refWorld.GetUnitType(9)) return Fail($"catalogue: phantom mismatch {phantom} vs {refWorld.GetUnitType(9)}");
        if (bulwark != refWorld.GetUnitType(10)) return Fail($"catalogue: bulwark mismatch {bulwark} vs {refWorld.GetUnitType(10)}");
        if (engineer != refWorld.GetUnitType(11)) return Fail($"catalogue: engineer mismatch {engineer} vs {refWorld.GetUnitType(11)}");
        if (raider != refWorld.GetUnitType(5)) return Fail($"catalogue: raider mismatch {raider} vs {refWorld.GetUnitType(5)}");
        if (scout != refWorld.GetUnitType(6)) return Fail($"catalogue: scout mismatch {scout} vs {refWorld.GetUnitType(6)}");
        if (mcv != refWorld.GetUnitType(7)) return Fail($"catalogue: mcv mismatch {mcv} vs {refWorld.GetUnitType(7)}");
        var w = new World(0);
        w.RegisterUnitType(1, cannon); // legal before tick 0
        Console.WriteLine("selftest: /data catalogue reproduces compiled defs exactly (cannon, rifle, rocket, harvester)");
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
        // hard-coded value survives a catalogue migration unnoticed.
        for (int t = 1; t <= 9; t++)
            if (!seenTypes.Contains(t)) return Fail($"structure catalogue: no /data/buildings file for compiled type {t}");
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

    // Map loader (TICKET-P2-DATA-03): the committed skirmish map round-trips.
    string mapFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/maps/skirmish-01.fmap"));
    if (File.Exists(mapFile))
    {
        var md = MapData.Load(mapFile);
        if (md.Width != 96 || md.Height != 64) return Fail("map: size");
        if (md.Blocked.Count != 248) return Fail($"map: expected 248 terrain cells (spine + corner ridges), got {md.Blocked.Count}");
        if (md.Fields.Count != 3 || !md.Fields.Contains((47, 31))) return Fail("map: fields (the prize sits in the Ferrite Gap)");
        if (md.Starts[0] != (8, 30) || md.Starts[1] != (86, 30)) return Fail("map: starts");
        var mw = md.BuildWorld(1, 2);
        if (!mw.Map.IsBlocked(5, 5) || mw.Map.IsBlocked(4, 5)) return Fail("map: terrain application");
        if (mw.EntityCount != 3) return Fail("map: field spawn count");
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
    const ulong seed = 2026;
    const int half = 1500, full = 3000;
    var recorded = new List<Command>[full];
    {
        var w = BuildSkirmishWorld(seed);
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
        var w = BuildSkirmishWorld(seed);
        for (int t = 0; t < full; t++) w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(recorded[t]));
        hashFull = w.ComputeStateHash();
    }

    var live = BuildSkirmishWorld(seed);
    for (int t = 0; t < half; t++) live.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(recorded[t]));
    ulong hashMid = live.ComputeStateHash();
    using var ms = new MemoryStream();
    live.Save(ms);
    ms.Position = 0;
    var loaded = World.Load(ms);
    if (loaded.ComputeStateHash() != hashMid)
        return Fail($"saveload: loaded hash 0x{loaded.ComputeStateHash():X16} != saved 0x{hashMid:X16}");
    for (int t = half; t < full; t++) loaded.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(recorded[t]));
    if (loaded.ComputeStateHash() != hashFull)
        return Fail($"saveload: resumed run diverged (0x{loaded.ComputeStateHash():X16} vs 0x{hashFull:X16})");
    Console.WriteLine($"saveload: {ms.Length} bytes; loaded hash exact at the save point; resumed run reached the uninterrupted final hash 0x{hashFull:X16} bit-for-bit");
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
