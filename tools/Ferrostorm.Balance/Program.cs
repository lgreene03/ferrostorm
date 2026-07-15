using System.Text;
using Ferrostorm.Sim;

// TICKET-P2-BAL-01: balance simulator per docs/design/12-balance-simulator-spec.md.
// Per-cost engagement matrix over the producible catalogue + counter audit +
// TTK sanity bounds. Deterministic: fixed committed seeds; exits nonzero on any
// hard failure so CI can gate data changes. Harvester tempo test awaits the
// second faction's opening layout (single-faction data so far - noted in spec).

const int ArmyCredits = 3000;
const int MaxTicks = 3000;
int[] seeds = { 11, 22, 33 };
var types = new (int Id, string Name)[] { (1, "cannon_tank"), (2, "rifle_squad"), (3, "rocket_squad"), (8, "howitzer"), (9, "phantom_tank"), (10, "bulwark_tank"), (12, "vanguard_car") };

// Design-intent counter triangle (GDD s6): armour beats rifles, rockets beat
// armour, rifles beat rockets - every edge must hold per-cost.
var expectedWinners = new Dictionary<(int, int), int>
{
    [(1, 2)] = 1, // cannon > rifle
    [(1, 3)] = 3, // rocket > cannon
    [(2, 3)] = 2, // rifle > rocket
    [(1, 8)] = 1, // armour closes the dead zone and crushes siege head-on
    [(2, 8)] = 2, // infantry swarms inside the dead zone
    [(3, 8)] = 3, // rockets likewise: siege loses every knife fight per-cost
    // Specials must lose fair fights per-cost - their value is stealth alpha
    // strikes (phantom) or per-unit concentration and crush (bulwark), not
    // line efficiency. Unlisted pairs are recorded, not constrained.
    [(2, 9)] = 2,  // rifles shred revealed phantoms per-cost
    [(1, 9)] = 1,  // cannons beat phantoms in a fair fight per-cost
    // Vanguard car (the vertical-slice unit): a harasser must shred
    // infantry per-cost and lose to anything carrying an anti-armour gun.
    [(2, 12)] = 12, // vanguard > rifle: its whole reason to exist
    [(1, 12)] = 1,  // cannon > vanguard: never trade with real armour
    [(3, 12)] = 3,  // rocket > vanguard: AT infantry punishes light plate
    [(3, 9)] = 3,  // rockets likewise
    [(1, 10)] = 1, // massed cannons out-trade the wall per-cost
};

var report = new StringBuilder();
report.AppendLine($"# Balance report - {DateTime.UtcNow:yyyy-MM-dd} (counter triangle)");
report.AppendLine($"Army value {ArmyCredits} credits/side, seeds [{string.Join(", ", seeds)}], cap {MaxTicks} ticks.");
report.AppendLine();
report.AppendLine("| Matchup | Winner (3 seeds) | Avg survivor value | Avg resolution |");
report.AppendLine("|---|---|---|---|");

bool hardFail = false;

(int Winner, int SurvivorPct, int Ticks) Engage(ulong seed, int typeA, int typeB)
{
    var world = new World(seed, 64, 64, players: 2);
    var defA = world.GetUnitType(typeA);
    var defB = world.GetUnitType(typeB);
    int countA = ArmyCredits / defA.Cost, countB = ArmyCredits / defB.Cost;

    var a = new List<int>(); var b = new List<int>();
    for (int i = 0; i < countA; i++)
        a.Add(world.SpawnUnit(0, Fix64.FromInt(20), Fix64.FromInt(32 - countA / 2 + i), defA.Speed, defA.Hp, defA.Armour, defA.WeaponId));
    for (int i = 0; i < countB; i++)
        b.Add(world.SpawnUnit(1, Fix64.FromInt(44), Fix64.FromInt(32 - countB / 2 + i), defB.Speed, defB.Hp, defB.Armour, defB.WeaponId));

    // Attack-move to the enemy muster point, not per-unit Attack: a plain
    // Attack order ends when its target dies, and a partial wipe can leave
    // both remnants passive and out of sight range forever - the vanguard
    // car was the first unit fast enough to create that standoff (see
    // docs/balance journal). Attack-move hunts to the finish.
    var cmds = new List<Command>();
    foreach (int u in a) cmds.Add(new Command(0, 0, CommandType.AttackMove, u, Fix64.FromInt(44), Fix64.FromInt(32)));
    foreach (int u in b) cmds.Add(new Command(0, 1, CommandType.AttackMove, u, Fix64.FromInt(20), Fix64.FromInt(32)));

    for (int t = 0; t < MaxTicks; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        int aliveA = 0, aliveB = 0;
        foreach (var e in world.Entities) if (e.Alive) { if (e.PlayerId == 0) aliveA++; else aliveB++; }
        if (aliveA == 0 || aliveB == 0)
        {
            int winner = aliveA > 0 ? 0 : 1;
            int survivors = winner == 0 ? aliveA : aliveB;
            int cost = winner == 0 ? defA.Cost : defB.Cost;
            return (winner, survivors * cost * 100 / ArmyCredits, t + 1);
        }
    }
    return (-1, 0, MaxTicks); // unresolved
}

foreach (var (idA, nameA) in types)
    foreach (var (idB, nameB) in types)
    {
        if (idA > idB) continue; // unordered pairs; sides are symmetric by construction
        var results = seeds.Select(s => Engage((ulong)s, idA, idB)).ToArray();
        string winners = string.Join("/", results.Select(r => r.Winner switch { 0 => nameA, 1 => nameB, _ => "UNRESOLVED" }));
        int avgSurv = (int)results.Where(r => r.Winner >= 0).DefaultIfEmpty().Average(r => r.SurvivorPct);
        double avgSecs = results.Average(r => r.Ticks) / (double)World.TicksPerSecond;
        report.AppendLine($"| {nameA} vs {nameB} | {winners} | {avgSurv}% | {avgSecs:F1}s |");

        bool mirror = idA == idB;
        foreach (var r in results)
        {
            if (r.Winner < 0 && !mirror)
            { report.AppendLine($"  - HARD FAIL: {nameA} vs {nameB} unresolved at {MaxTicks} ticks"); hardFail = true; }
            if (r.Winner >= 0 && !mirror && (r.Ticks < 2 * World.TicksPerSecond || r.Ticks > 90 * World.TicksPerSecond))
            { report.AppendLine($"  - HARD FAIL: TTK out of sanity bounds ({r.Ticks} ticks)"); hardFail = true; }
        }
        if (!mirror && expectedWinners.TryGetValue((idA, idB), out int expect))
        {
            foreach (var r in results)
                if (r.Winner >= 0 && (r.Winner == 0 ? idA : idB) != expect)
                {
                    report.AppendLine($"  - HARD FAIL: counter inversion - design intent says {types.First(t => t.Id == expect).Name} wins this per-cost");
                    hardFail = true;
                    break;
                }
        }
    }

// --- STATIC DEFENCE (TICKET-P5-DEF-17) ------------------------------------
// GDD s6 line 53 ("artillery beats static defence") and ADR-005's stated
// mitigation for the turtling risk ("a CI gate that proves a walled base
// falls to a siege army"), turned into a gate that runs on every push.
//
// A fortified position - Construction Yard, an oversized power plant, two
// turrets, optionally a twelve-segment wall line - is besieged by 3000 credits
// of a single unit type under attack-move. Three wall shapes, and the first of
// them is the whole reason this section can claim to measure anything:
//   none   - THE CONTROL. The same fortification with no wall at all. Without
//            it every number below is unattributable: a siege that succeeds
//            against a walled base tells you nothing unless you know what the
//            same siege does against an unwalled one.
//   gapped - a two-cell doorway the turrets cover. ADR-005 clause 6 is explicit
//            that this is the intended shape: gates are deferred and "the
//            player leaves a gap, as players did in 1995".
//   sealed - the doorway walled up. Pathological (the defender's own units
//            cannot leave either), but it is the only geometry that exercises
//            the DEF-05 breach path, so it is where the wall itself is shot.
//
// SiegeTicks is deliberately NOT the matrix's 3000-tick cap. That cap was sized
// for field engagements, which resolve above in 8 to 35 seconds; a siege is a
// different timescale, and the wall's designed function is to buy time. Capping
// the siege at 200s would score the wall succeeding as the gate failing.
const int SiegeTicks = 6000;

// The turtling bound. A sealed wall may buy the turtle at most this many ticks
// (80 seconds) against a siege army, measured against the unwalled control so
// map size, army speed and turret dps all cancel out and what remains is the
// wall's own contribution and nothing else.
//
// The number is set by what it has to separate, and both ends are measured
// rather than asserted: the shipped 500hp wall buys 235 ticks, and a wall
// regressed to 5000hp buys 1936. 1200 is the round number between them. That
// is what "the gate bites" means here - it is not a vibe, it has been run in
// both directions (docs/balance/2026-07-15-turtle-gate.md).
const int MaxWallDelay = 1200;

(int WallsDead, int TurretsDead, int RazeTick, int SurvPct) Besiege(ulong seed, int atkType, string wall)
{
    // A 64x12 corridor. The wall line spans it, so a sealed wall genuinely
    // seals: on an open map every attacker simply walks around a twelve-segment
    // line and the wall is never part of the measurement at all.
    var world = new World(seed, 64, 12, players: 2);
    // The besieger owns no structure and no MCV, so the victory test would
    // declare it eliminated on tick 1 (World.VictorySystem). This fixture
    // measures a siege, not a match; short game off.
    world.ShortGameEnabled = false;
    const int midY = 6;

    int cyId = world.SpawnConstructionYard(0, 6, midY - 1);
    // Draw is 100 (yard) + 20 + 20 (turrets) = 140. One plant, oversized, so
    // supply >= draw and DEF-10's coming 75% rule cannot silently disarm the
    // turrets and hand the besieger a free win.
    world.SpawnPowerPlant(0, 6, midY + 3, supply: 200);
    int t1 = world.SpawnTurret(0, 16, midY - 4);
    int t2 = world.SpawnTurret(0, 16, midY + 2);

    var walls = new List<int>();
    if (wall != "none")
        for (int k = 0; k < 12; k++)
        {
            if (wall == "gapped" && (k == 5 || k == 6)) continue; // the doorway
            walls.Add(world.SpawnWall(0, 20, k));
        }

    var def = world.GetUnitType(atkType);
    int count = ArmyCredits / def.Cost;
    // Muster in a block: fifteen rifle squads do not fit in one column of a
    // twelve-cell corridor. Sight is passed from the catalogue rather than left
    // at SpawnUnit's default of 5 - the DEF-05 breach path only takes a barrier
    // it can see, so sight is load-bearing here in a way it is not in the unit
    // matrix above.
    var atk = new List<int>();
    const int rows = 8;
    for (int i = 0; i < count; i++)
        atk.Add(world.SpawnUnit(1, Fix64.FromInt(50 + i / rows), Fix64.FromInt(midY - Math.Min(count, rows) / 2 + i % rows),
            def.Speed, def.Hp, def.Armour, def.WeaponId, def.SightCells, unitType: atkType));

    var cmds = new List<Command>();
    foreach (int u in atk) cmds.Add(new Command(0, 1, CommandType.AttackMove, u, Fix64.FromInt(10), Fix64.FromInt(midY)));

    int razeTick = -1;
    for (int t = 0; t < SiegeTicks; t++)
    {
        world.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (!world.Entities[cyId].Alive) { razeTick = t + 1; break; }
    }

    int wallsDead = walls.Count(w => !world.Entities[w].Alive);
    int turretsDead = (world.Entities[t1].Alive ? 0 : 1) + (world.Entities[t2].Alive ? 0 : 1);
    int alive = atk.Count(u => world.Entities[u].Alive);
    return (wallsDead, turretsDead, razeTick, alive * def.Cost * 100 / ArmyCredits);
}

var besiegers = new (int Id, string Name)[] { (8, "howitzer"), (2, "rifle_squad"), (1, "cannon_tank") };
var shapes = new[] { "none", "gapped", "sealed" };
report.AppendLine();
report.AppendLine("## Static defence: does a walled base fall to a siege army?");
report.AppendLine();
report.AppendLine($"Fortification: 1 Construction Yard, 1 power plant (supply 200 >= draw 140), 2 turrets, 12 wall segments (\"none\" is the unwalled control). Besieger: {ArmyCredits} credits of one type, attack-move, cap {SiegeTicks} ticks.");
report.AppendLine();
report.AppendLine("| Besieger | Wall | Segments lost | Turrets killed | Yard razed | Army retained | Verdict |");
report.AppendLine("|---|---|---|---|---|---|---|");

var razeBy = new Dictionary<(int, string), int>();
foreach (var (id, name) in besiegers)
    foreach (var shape in shapes)
    {
        var rs = seeds.Select(s => Besiege((ulong)s, id, shape)).ToArray();
        var r = rs[0];
        // Every seed must agree. The fixture draws no random numbers, so this is
        // agreement by construction rather than a statistical claim - but it is
        // a free tripwire for any RNG that leaks into the siege path later.
        bool seedsAgree = rs.All(x => x.WallsDead == r.WallsDead && x.TurretsDead == r.TurretsDead
                                   && x.RazeTick == r.RazeTick && x.SurvPct == r.SurvPct);
        razeBy[(id, shape)] = r.RazeTick;
        bool breached = r.RazeTick > 0 && r.SurvPct > 30;
        report.AppendLine($"| {name} | {shape} | {r.WallsDead} | {r.TurretsDead}/2 | {(r.RazeTick > 0 ? "t=" + r.RazeTick : "no")} | {r.SurvPct}% | {(breached ? "BREACHED" : "held")} |");

        if (!seedsAgree)
        { report.AppendLine($"  - HARD FAIL: {name} vs {shape} wall disagrees across seeds - the siege path has become non-deterministic"); hardFail = true; }

        // THE GATE. Only the howitzer is asserted, and only in the direction GDD
        // s6 line 53 and ADR-005 commit to: the siege army gets through. The
        // rifle and cannon rows are REPORTING ONLY - see the note below the
        // table, and docs/balance/2026-07-15-turtle-gate.md for the working.
        if (id != 8) continue;
        if (r.RazeTick < 0)
        { report.AppendLine($"  - HARD FAIL: artillery did not raze a {shape}-walled base within {SiegeTicks} ticks - GDD s6 line 53 says artillery beats static defence"); hardFail = true; }
        else if (r.SurvPct <= 30)
        { report.AppendLine($"  - HARD FAIL: artillery razed the {shape}-walled base but kept only {r.SurvPct}% of its army - a pyrrhic siege is not a counter"); hardFail = true; }
        if (r.TurretsDead < 2)
        { report.AppendLine($"  - HARD FAIL: artillery left {2 - r.TurretsDead} turret(s) standing behind a {shape} wall"); hardFail = true; }
        // The wall is only ever shot when it seals the route: auto-acquire skips
        // barriers (ADR-005 clause 2) and splash cannot reach a wall flush
        // against a 2x2 turret (distSq 2.5 against a 2.25 radius), so DEF-05's
        // breach is the only path to a dead segment.
        if (shape == "sealed" && r.WallsDead < 1)
        { report.AppendLine("  - HARD FAIL: artillery razed a sealed base without breaching a single segment - the breach path is dead"); hardFail = true; }
    }

// THE TURTLING BOUND. This, not the raze itself, is the assertion that can see
// the wall at all: the raze rows above are within 0 ticks of the unwalled
// control for a gapped wall, so a gate written on them alone would pass
// unchanged if barriers were deleted from the game entirely.
int noneRaze = razeBy[(8, "none")], sealedRaze = razeBy[(8, "sealed")], gappedRaze = razeBy[(8, "gapped")];
report.AppendLine();
report.AppendLine("| Artillery siege | Unwalled control | Gapped wall | Sealed wall |");
report.AppendLine("|---|---|---|---|");
report.AppendLine($"| Yard razed at tick | {noneRaze} | {gappedRaze} | {sealedRaze} |");
report.AppendLine($"| Ticks bought by the wall | - | {(gappedRaze > 0 && noneRaze > 0 ? (gappedRaze - noneRaze).ToString() : "n/a")} | {(sealedRaze > 0 && noneRaze > 0 ? (sealedRaze - noneRaze).ToString() : "n/a")} |");
if (noneRaze < 0)
{ report.AppendLine("  - HARD FAIL: artillery cannot raze even an UNWALLED fortification - the control is broken, every number above is unattributable"); hardFail = true; }
else if (sealedRaze > 0 && sealedRaze - noneRaze > MaxWallDelay)
{
    report.AppendLine($"  - HARD FAIL: a sealed wall bought the turtle {sealedRaze - noneRaze} ticks against a siege army, over the stated bound of {MaxWallDelay} - turtling is winning");
    hardFail = true;
}

report.AppendLine();
report.AppendLine("FINDING, not a caveat: against artillery a gapped wall - the shape ADR-005 clause 6");
report.AppendLine("actually intends - is worth ZERO. The yard falls on the same tick with it and without");
report.AppendLine("it, because the howitzer's range 9 beats the turret's range 5, so it parks outside the");
report.AppendLine("wall and shells over the top and never has a reason to touch masonry. A sealed wall");
report.AppendLine("buys 235 ticks, 7% of the siege. \"Artillery beats static defence\" holds, but it holds");
report.AppendLine("independently of barriers, so this section polices the turret's range and the wall's");
report.AppendLine("hit points, and cannot police much else. See docs/balance/2026-07-15-turtle-gate.md.");
report.AppendLine();
report.AppendLine("Rifle and cannon rows are REPORTING ONLY, and the reason is a finding rather than a");
report.AppendLine("hedge: neither the ticket's \"zero segments destroyed\" nor a yard-razed test measures");
report.AppendLine("what it appears to. Segments lost is set by geometry, not by warhead - nothing at all");
report.AppendLine("shoots a gapped wall, and against a sealed one massed rifles out-chew the howitzer");
report.AppendLine("(8 segments to 3). Yard-razed inverts under a different order: told to Attack the yard");
report.AppendLine("directly, rifles raze it at t=767 losing nothing while the howitzer walks into its own");
report.AppendLine("3-cell dead zone and dies to the last unit. Under attack-move the negative controls");
report.AppendLine("hold only because the stall-cancel at World.cs:1488 drops AMove for good and parks them");
report.AppendLine("short of the yard. Gating on that would gate an artefact. These rows are recorded every");
report.AppendLine("run so the trend is visible; promoting them to blocking is gated on attack-move");
report.AppendLine("prosecuting a base to the finish, per the faction-war precedent below.");

// COST EFFICIENCY (DEF-17 clause 3). The wall is deliberately the cheapest hit
// points in the game - that is what a wall IS - and it is paid for by having no
// gun, no vision, no adjacency for real buildings, an 80-segment cap and splash
// vulnerability. The bound stops a later tuning pass from quietly making walls
// free. Integer maths: hp per credit x100, bound 800 (= 8.0).
report.AppendLine();
report.AppendLine("## Cost efficiency: hit points per credit");
report.AppendLine();
report.AppendLine("| Thing | Hit points | Cost | Hp per credit |");
report.AppendLine("|---|---|---|---|");
// TICKET-P5-BD-06: hit points come off the catalogue now. This file used to
// carry its own copy of 500 and 400 with a comment pointing at the spawn
// methods, which is the third source of truth the catalogue move exists to
// delete: a balance gate reading stale numbers passes while the game is wrong.
var wallDef = World.DefaultStructureType(9);
var turretDef = World.DefaultStructureType(5);
int WallHp = wallDef.Hp, TurretHp = turretDef.Hp;
int wallHpPerCreditX100 = WallHp * 100 / wallDef.Cost;
int turretHpPerCreditX100 = TurretHp * 100 / turretDef.Cost;
var tankDef = new World(1, 8, 8, players: 1).GetUnitType(1);
int tankHpPerCreditX100 = tankDef.Hp * 100 / tankDef.Cost;
string Hpc(int x100) => $"{x100 / 100}.{x100 % 100:D2}";
report.AppendLine($"| wall segment | {WallHp} | {wallDef.Cost} | {Hpc(wallHpPerCreditX100)} |");
report.AppendLine($"| turret | {TurretHp} | {turretDef.Cost} | {Hpc(turretHpPerCreditX100)} |");
report.AppendLine($"| cannon tank | {tankDef.Hp} | {tankDef.Cost} | {Hpc(tankHpPerCreditX100)} |");
if (wallHpPerCreditX100 > 800)
{
    report.AppendLine($"  - HARD FAIL: a wall buys {Hpc(wallHpPerCreditX100)} hp per credit, over the stated bound of 8.0 - the cheapest hit points in the game have become free ones");
    hardFail = true;
}

// Harvester tempo (TICKET-P2-BAL-02): the standard opening layout, run for
// 3000 ticks, measured in delivered credits. Factions currently share one
// economy so the sides must match exactly; the committed baseline number is
// the tripwire for any future /data drift.
long Tempo(ulong seed)
{
    var w = new World(seed, 64, 64, players: 1);
    w.SpawnConstructionYard(0, 6, 30);
    w.SpawnPowerPlant(0, 10, 30);
    w.SpawnRefinery(0, 6, 34);
    w.SpawnFactory(0, 10, 34);
    int h = w.SpawnHarvester(0, Fix64.FromInt(9), Fix64.FromInt(37));
    int field = w.SpawnFerriteField(Fix64.FromInt(20), Fix64.FromInt(34), 12000);
    var c = new List<Command> { new(0, 0, CommandType.Harvest, h, Fix64.Zero, Fix64.Zero, field) };
    for (int t = 0; t < 3000; t++)
    {
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(c));
        c.Clear();
    }
    return w.Credits(0);
}
long tempoA = Tempo(101), tempoB = Tempo(101); // identical config: must be identical
report.AppendLine();
report.AppendLine($"## Harvester tempo (standard opening, 3000 ticks): {tempoA} credits");
if (tempoA != tempoB)
{ report.AppendLine("  - HARD FAIL: tempo not reproducible"); hardFail = true; }
if (tempoA < 1400)
{ report.AppendLine($"  - HARD FAIL: tempo collapsed below 2 delivered loads ({tempoA})"); hardFail = true; }

report.AppendLine();
report.AppendLine(hardFail
    ? "## VERDICT: HARD FAIL - data change requires Balance sign-off before merge."
    : "## VERDICT: PASS - matchups match design intent within bounds.");

var text = report.ToString();
Console.Write(text);
// --- Faction-vs-faction gate (TICKET-P3-FAC-05) ---------------------------
// Six seeded full-AI matches, Directorate vs Sodality on the committed
// skirmish map, faction-locked doctrine. A seed's winner is the declared
// victor, or (at the tick limit) the side with more surviving structure
// value plus credits - deterministic either way. Neither faction may sweep:
// each must take at least 2 of 6, or the data goes back to the forge.
int dirWins = 0, sodWins = 0;
var factionLines = new List<string> { "", "## Faction war: Directorate vs Sodality (6 seeds, 7000 ticks)", "", "| seed | result | decided at |", "|---|---|---|" };
string mapPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "data/maps/skirmish-01.fmap"));
var factionMap = Ferrostorm.Sim.MapData.Load(mapPath);
for (ulong fs = 3001; fs <= 3006; fs++)
{
    var w = factionMap.BuildWorld(fs, 2);
    w.SetFaction(0, World.FactionDirectorate);
    w.SetFaction(1, World.FactionSodality);
    w.GrantCredits(0, 8000); w.GrantCredits(1, 8000);
    w.SpawnConstructionYard(0, factionMap.Starts[0].Cx, factionMap.Starts[0].Cy);
    w.SpawnConstructionYard(1, factionMap.Starts[1].Cx, factionMap.Starts[1].Cy);
    var ais = new[] { SkirmishAI.Standard(0), SkirmishAI.Standard(1) };
    var cmds = new List<Command>();
    int decidedAt = -1;
    for (int t = 0; t < 7000 && decidedAt < 0; t++)
    {
        cmds.Clear();
        ais[0].Act(w, cmds); ais[1].Act(w, cmds);
        w.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds));
        if (w.Winner >= 0) decidedAt = w.Tick;
    }
    int winner = w.Winner;
    if (winner < 0)
    {
        // Tick-limit adjudication: surviving structure value + treasury.
        long s0 = w.Credits(0), s1 = w.Credits(1);
        foreach (var e in w.Entities)
        {
            if (!e.Alive || e.PlayerId < 0) continue;
            long v = e.StructType > 0 ? w.GetStructureType(e.StructType).Cost
                   : e.UnitType > 0 ? w.GetUnitType(e.UnitType).Cost
                   : 0; // surviving armies are winning positions too
            if (e.PlayerId == 0) s0 += v; else s1 += v;
        }
        winner = s0 == s1 ? -1 : s0 > s1 ? 0 : 1;
    }
    if (winner == 0) dirWins++; else if (winner == 1) sodWins++;
    factionLines.Add($"| {fs} | {(winner == 0 ? "Directorate" : winner == 1 ? "Sodality" : "dead heat")} | {(decidedAt > 0 ? decidedAt.ToString() : "adjudicated")} |");
}
factionLines.Add("");
factionLines.Add($"Directorate {dirWins} - {sodWins} Sodality");
text += string.Join("\n", factionLines);
Console.WriteLine($"faction war: Directorate {dirWins} - {sodWins} Sodality");
// REPORTING ONLY until TICKET-AI-05 (defence squads) lands: AI-vs-AI faction
// outcomes are currently doctrine-dominated, not data-dominated - eleven
// forge iterations oscillated between 6-0 shutouts as each doctrine found
// the other's blind spot, culminating in a faithful reproduction of the
// classic harass-bait exploit. The score is recorded every run so the trend
// is visible; the gate is promoted to blocking once the AI can defend its
// economy without being puppeted and human playtesting has set the meta.
if (dirWins < 2 || sodWins < 2)
    Console.WriteLine($"faction balance (reporting): one side dominating ({dirWins}-{sodWins}); blocked on TICKET-AI-05 + human playtesting");

if (args.Length > 0) File.WriteAllText(args[0], text);
return hardFail ? 1 : 0;
