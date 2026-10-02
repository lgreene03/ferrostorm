using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Ferrostorm.Sim;

// TICKET-P2-BAL-01: the balance simulator per docs/design/12-balance-simulator-spec.md.
//
// REBUILT BY P8-14 ("the balance tool tells the truth"), because the tool this
// replaced was the only evidence behind several design decisions and it did
// not measure the game that ships. Each defect below was verified, and each is
// named here beside the line that now prevents it, so a later edit cannot
// quietly reintroduce one:
//
//   1. The faction war was ONE deterministic match counted six times. The sim
//      never draws its RNG, so "six seeds" were six identical runs. It was then
//      adjudicated on banked credits at 7000 ticks, which rewards the AI's own
//      spending defect. Now: one run per (map, seat) cell, both orientations,
//      three maps, played to a result or 27000 ticks, never adjudicated. The
//      RNG is CHECKED, every run, rather than assumed (see NoteRng).
//   2. "Equal-credit" armies were unequal by integer division: 3000 credits
//      bought one 1600-credit bulwark against fifteen 200-credit rifle squads.
//      Now: equal credits FIELDED, within one unit's cost, and the unspent
//      remainder printed on every row (see Field).
//   3. Spawns at x=20.0 and x=44.0 sat on cell BOUNDARIES that floor into cells
//      20 and 44, which are not mirror images (20 mirrors to 43 on a 64-cell
//      map), so every mirror match was credited to seat 0. Now: cell centres,
//      mirrored across the diagonal (the arena note below says why a
//      left-right mirror cannot be exact), and every uncloaked mirror is
//      ASSERTED to end in mutual annihilation.
//   4. It played compiled defaults with no opening force. Now: every world is
//      registered from /data before tick 0, through the runner's and the
//      client's own CatalogueFiles.RegisterAll, and the faction war uses
//      MapData.PlaceSkirmishStart, the shipped opening hand.
//   5. It measured one budget (3000), so anything that changes with scale (the
//      claim that massed howitzers dominate, for one) was invisible to it. Now:
//      four budgets, 1800, 4800, 9000 and 12600.
//   6. `args[0]` was written as a report path unchecked, so a report could land
//      in the repository. Now: any path inside this repository, or inside the
//      main work tree that holds this one, is refused before anything runs.
//
// MODES. `full` (the default, and what CI runs, because CI invokes the tool
// with no arguments) and `quick`. They differ ONLY in the faction war's map
// list: full plays skirmish-01, -02 and -04 as F11 is written; quick drops
// skirmish-04, the 192x128 map that costs most of the war's runtime. Every
// other section is identical in both. Measured at P8-14 (2026-10-02, on a
// machine at load average 36): quick 9.7 s, full 17.6 s. CI can afford the
// criterion as written, so quick exists for fast local iteration only.
//
// EXIT CODES. 0: every self-check and every binding gate passed. 1: a
// self-check or a binding gate failed. 2: the tool could not run (bad
// arguments, a refused report path, /data missing). F10 and F11 are measured
// and printed every run and do NOT affect the exit code until the two switches
// below are flipped by the rows that own them.

// ---------------------------------------------------------------------------
// THE TWO SWITCHES. P8's criteria F10 (no unit is dominated) and F11 (the
// faction war is a contest) are reported by this tool from P8-14 onwards and
// become BINDING when their row lands: F10 at P8-32, after the unit balance
// pass, and F11 at P8-33, after the faction war is re-measured. Two flags
// rather than one because the two rows land separately; each row flips its own
// and nothing else. Flipping one makes a failed criterion exit 1.
// ---------------------------------------------------------------------------
const bool F10Binding = false;   // P8-32 flips this
const bool F11Binding = false;   // P8-33 flips this
const int F11MinSharePct = 40;   // F11: each faction wins at least 40 per cent of cells

// The four budgets F10 names. They are chosen so that most unit costs divide
// them, which keeps the fielded remainders small, and so that 12600 is large
// enough for a massed ball to show what it does at scale.
int[] budgets = { 1800, 4800, 9000, 12600 };

// One seed, deliberately. The sim draws no random numbers today (World._rng is
// never consumed, tracker D8), so a second seed is the same match counted
// twice, which is exactly defect 1. This is not assumed: every run below checks
// that the RNG state is still the seed when it ends, and the report says so.
const ulong Seed = 2026;

// Engagements stop at a decision, at this cap, or after StalemateTicks with no
// hit point lost on either side (two cloaked armies that cannot see each other
// walk past and stop; running them to the cap measures nothing).
const int EngageCapTicks = 6000;
const int StalemateTicks = 1500;
// PURSUIT. An attack-move ends at its ordered point, and each side is ordered
// to the other's STARTING front, so after the clash the two remnants walk away
// from each other, finish their orders far apart and park: measured, a dozen
// rows "ended" with both sides alive and nobody shooting (the vanguard car
// standoff the old tool recorded, reborn). A player would send the survivors
// at what is left, so every PursueEvery ticks any unit whose attack-move has
// completed is re-ordered at the nearest living enemy. It steers and never
// targets: the sim's own cloak rule still decides who can shoot, so a cloaked
// defender is walked towards and still fires first. Nearest is by DistSq with
// ties to the lower id, which is symmetric under the diagonal mirror.
const int PursueEvery = 15;

// THE ARENA, and why it is a DIAGONAL mirror. The obvious arena reflects seat
// 0 left to right (x -> 64 - x), and it CANNOT be exact in this sim: Fix64
// multiplication is an arithmetic shift, which floors towards negative
// infinity, so (-a)*b and -(a*b) differ by one unit in the last place. The
// seat marching west multiplies negative direction components and the seat
// marching east positive ones, so any diagonal step differs by one bit from
// tick 2, and in a large crowd fight that bit is amplified until it changes a
// target pick and the winner (measured: rifle mirrors at 4800 and up, rocket
// and vanguard mirrors at 9000 and up, a different seat winning at different
// budgets). Reflecting across the diagonal y = x instead NEGATES NOTHING: seat
// 1's x is computed from exactly the numbers that give seat 0's y, so every
// product is identical and the two armies stay bit-exact transposes (measured
// to the last bit through 63 against 63). Each army is a line along (1,1) with
// its ranks stepping back along (-1,1), so the two fronts face each other
// square-on across the diagonal; seat 1 is the transpose of seat 0.
const int LineLength = 16;              // units per rank
const int LineX0 = 13, LineY0 = 36;     // seat 0's rank 0 starts here; the line's centre is (20, 43)
const int FrontX = 20, FrontY = 43;     // seat 0's front centre; seat 1's is (43, 20)
//
// THE GEOMETRY CHECK. Every asymmetric pair is ALSO fought in the centred
// left-right arena (16-row blocks at cell centres, fronts at cells 19 and 44,
// the pre-P8-14 layout with its boundary defect removed), from both seats. It
// exists because a verdict can depend on the ANGLE of approach rather than on
// the units, and the first corrected run proved it: the howitzer beat massed
// cannons from both seats approaching along an axis and lost to them with
// nothing killed approaching along the diagonal, at every formation density
// tried. A row whose winner differs between the arenas is GEOMETRY-SENSITIVE
// and counts for neither unit, for the same reason a seat-decided row does.
// The left-right arena is not exact (the Fix64 note above), so its mirrors are
// never run and a seat disagreement inside it reads as sensitivity, not as a
// failure.
const int LrFront = 19;                 // left-right arena: seat 0's front column; seat 1's is 63 - LrFront

// The faction war: GDD pillar 2's 30-minute ceiling, at 15 ticks a second.
const int WarCapTicks = 27000;
const long WarStartCredits = 8000;   // the shipped skirmish default (MatchSetup.StartCredits)
string[] warMapsFull = { "skirmish-01", "skirmish-02", "skirmish-04" };
// Quick drops skirmish-04 only: it is four times the cells of the other two and
// dominates the war's runtime (both figures are in the P8-14 tracker row).
// Quick still plays both orientations on two maps, so its seat share and
// faction share are real measurements, never a shortened horizon. A cut
// horizon would leave cells undecided that a full run decides, and an
// undecided cell is not a smaller sample of the same truth. F11 can only be
// judged in full mode, and once it binds, quick mode fails it by design.
string[] warMapsQuick = { "skirmish-01", "skirmish-02" };

// ---------------------------------------------------------------------------
// Arguments and the repository guard (defect 6). Both are settled BEFORE any
// simulation runs, so a refused path costs nothing and writes nothing.
// ---------------------------------------------------------------------------
bool full = true;
string? reportPath = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "quick": full = false; break;
        case "full": full = true; break;
        case "--report":
            if (i + 1 >= args.Length) return Usage("--report needs a path");
            reportPath = args[++i];
            break;
        default: return Usage($"unknown argument '{args[i]}'");
    }
}

string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
string dataRoot = Path.Combine(repoRoot, "data");
if (!Directory.Exists(Path.Combine(dataRoot, "units")))
{
    Console.Error.WriteLine($"balance: no /data catalogue at {dataRoot}; the tool measures /data and has no fallback");
    return 2;
}
if (reportPath != null)
{
    string? why = InsideRepository(reportPath, repoRoot);
    if (why != null)
    {
        Console.Error.WriteLine($"balance: REFUSED report path '{reportPath}': {why}. "
            + "A report is a measurement of one commit and belongs outside the tree that produced it; pass a path outside the repository.");
        return 2;
    }
}

var clock = Stopwatch.StartNew();
var report = new StringBuilder();
var selfFailures = new List<string>();   // the tool's own correctness: always exit 1
var gateFailures = new List<string>();   // binding design gates: exit 1
int runs = 0, rngDrawn = 0;               // the seed-invariance tripwire, every run counted
var rngDrawnWhere = new SortedSet<string>(StringComparer.Ordinal);

void Line(string s = "") => report.AppendLine(s);
void SelfFail(string s) { selfFailures.Add(s); Line($"  - SELF-CHECK FAILED: {s}"); }
void GateFail(string s) { gateFailures.Add(s); Line($"  - HARD FAIL: {s}"); }

// Every world this tool builds goes through here or through MapData.BuildWorld
// with the same registrar, so no measurement can fall back to compiled numbers.
World Catalogued(int width, int height, int players)
{
    var w = new World(Seed, width, height, players);
    CatalogueFiles.RegisterAll(w, dataRoot);
    return w;
}

void NoteRng(World w, string section)
{
    runs++;
    if (w.Rng.State != Seed) { rngDrawn++; rngDrawnWhere.Add(section); }
}

// ---------------------------------------------------------------------------
// The catalogue, read back from a registered world.
// ---------------------------------------------------------------------------
var cat = Catalogued(8, 8, 2);
ulong checksumData = cat.CatalogueChecksum;
ulong checksumCompiled = new World(Seed, 8, 8, 2).CatalogueChecksum;
var roster = cat.UnitTypeIds()
    .Select(id => { var d = cat.GetUnitType(id); return new Unit(id, UnitCatalogue.IdOf(id), d, cat.GetWeaponType(d.WeaponId)); })
    .ToList();

Line($"# Balance report ({(full ? "full" : "quick")} mode)");
Line();
Line($"Catalogue: /data, registered through CatalogueFiles.RegisterAll, checksum 0x{checksumData:X16} "
    + $"({(checksumData == checksumCompiled ? "identical to" : "DIFFERS from")} the compiled reference 0x{checksumCompiled:X16}). "
    + $"{roster.Count} unit types. Seed {Seed}, one run per cell (see the seed-invariance line at the end).");
Line();

// ===========================================================================
// 1. THE ENGAGEMENT MATRIX (F10)
// ===========================================================================
// A matchup is a pair in which EACH side can engage the other, decided from
// the catalogue by ADR-028 clause 3 (a weapon engages air if and only if it is
// anti-air) and never from a hand list. That rule is what pairs the strike
// flyer with the flak track and with nothing else, and it is why the rule is
// written once: air against a ground unit that cannot shoot it is a free kill,
// not a matchup, and a flak track cannot shoot the ground at all.
var combatants = roster.Where(u => u.Armed).ToList();
var nonCombatants = roster.Where(u => !u.Armed).ToList();
var pairs = new List<(Unit A, Unit B)>();
for (int i = 0; i < combatants.Count; i++)
    for (int j = i; j < combatants.Count; j++)
        if (combatants[i].CanEngage(combatants[j]) && combatants[j].CanEngage(combatants[i]))
            pairs.Add((combatants[i], combatants[j]));

Line("## 1. Engagement matrix: equal credits fielded, centred mirrored spawns");
Line();
Line($"Budgets {string.Join(", ", budgets)}. Arena 64x64, open ground, mirrored across the diagonal y = x (a left-right mirror cannot be exact in Fix64; see the source). "
    + $"Each side musters at cell centres in ranks of {LineLength} along the diagonal, fronts centred on ({FrontX},{FrontY}) and ({FrontY},{FrontX}), and attack-moves to the other's front. "
    + $"Cap {EngageCapTicks} ticks; a fight with no hit point lost for {StalemateTicks} ticks is stopped and called a stalemate, or NO CONTACT if nothing was ever hit.");
Line();
Line("Fielding: each side buys whole units. One side fields its maximum; the other fields the count closest to that value without exceeding the budget; "
    + "the closer of the two anchorings is taken. So the two sides' fielded credits differ by less than one unit's cost, and each side's unspent remainder is printed. "
    + "A unit with a max_alive cap (the heroes) fields at most that many, and its opponent is matched to the hero's value, not to the budget.");
Line();
Line($"Matchups: {pairs.Count(p => p.A.Id != p.B.Id)} pairs plus {pairs.Count(p => p.A.Id == p.B.Id)} mirrors. Every pair is fought from BOTH seats in the diagonal arena "
    + "and again from both seats in a centred left-right arena (the geometry check); the table shows the diagonal seat-0 run, and a row the seat or the angle decided says so.");
Line($"Not a matchup, listed rather than dropped: {string.Join(", ", nonCombatants.Select(u => u.Name + " (no weapon)"))}; "
    + string.Join(", ", combatants.Where(u => !u.CanEngage(u)).Select(u => u.Name + $" (no mirror: {(u.Def.Air ? "air cannot shoot air" : "anti-air cannot shoot the ground")})")) + ".");
Line();

EngageResult Engage(Arena arena, Unit a, int nA, Unit b, int nB)
{
    var w = Catalogued(64, 64, 2);
    var sideA = Muster(w, arena, 0, a, nA);
    var sideB = Muster(w, arena, 1, b, nB);
    var cmds = new List<Command>();
    // Attack-move rather than per-unit Attack: a plain Attack ends when its
    // target dies and can leave both remnants passive out of sight range
    // (the vanguard car standoff recorded in docs/balance). Attack-move hunts.
    // Each side marches on the other's front centre.
    var (ax, ay, bx, by) = arena == Arena.Diagonal
        ? (FrontY, FrontX, FrontX, FrontY)
        : (63 - LrFront, 32, LrFront, 32);
    foreach (int u in sideA) cmds.Add(new Command(0, 0, CommandType.AttackMove, u, Map.CellCentre(ax), Map.CellCentre(ay)));
    foreach (int u in sideB) cmds.Add(new Command(0, 1, CommandType.AttackMove, u, Map.CellCentre(bx), Map.CellCentre(by)));

    int hpA0 = SumHp(w, sideA), hpB0 = SumHp(w, sideB);
    int lastHp = hpA0 + hpB0, quietSince = 0;
    int t = 0;
    for (; t < EngageCapTicks; t++)
    {
        w.Step(CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        int aliveA = Alive(w, sideA), aliveB = Alive(w, sideB);
        if (aliveA == 0 || aliveB == 0) break;
        int hp = SumHp(w, sideA) + SumHp(w, sideB);
        if (hp != lastHp) { lastHp = hp; quietSince = t; }
        else if (t - quietSince >= StalemateTicks) break;
        if ((t + 1) % PursueEvery == 0) { Pursue(w, 0, sideA, sideB, cmds); Pursue(w, 1, sideB, sideA, cmds); }
    }
    NoteRng(w, "engagements");
    int fa = Alive(w, sideA), fb = Alive(w, sideB);
    int ha = SumHp(w, sideA), hb = SumHp(w, sideB);
    Outcome o = fa == 0 && fb == 0 ? Outcome.Annihilation
        : fb == 0 ? Outcome.SeatA
        : fa == 0 ? Outcome.SeatB
        : (ha == hpA0 && hb == hpB0) ? Outcome.NoContact
        : Outcome.Stalemate;
    return new EngageResult(o, Math.Min(t + 1, EngageCapTicks), fa, fb, ha, hb);
}

// cells[(budget, a, b)]: every run of one matchup, read through Read() below
// so the table, the headline and the counter triangle can never disagree.
var cells = new Dictionary<(int Budget, int A, int B), Cell>();
bool fieldingOk = true, mirrorOk = true;
int largestGap = 0, largestGapPct = -1; string largestGapAt = "";
int mirrorsAnnihilated = 0, mirrorsCloaked = 0;
var seatRows = new List<string>();
var geoRows = new List<string>();
// Doc 12's time-to-kill sanity bounds. Asserted by the old tool at one budget;
// REPORTED now, because a 12600-credit fight is allowed to take longer than a
// 3000-credit one and the bound was never re-derived for scale.
const int TtkMinSeconds = 2, TtkMaxSeconds = 90;
var ttkOutside = new List<string>();

foreach (int budget in budgets)
{
    Line($"### Budget {budget}");
    Line();
    Line("| Matchup | Fielded (count x cost, unspent) | Winner | Survivor value | Resolved |");
    Line("|---|---|---|---|---|");
    foreach (var (a, b) in pairs)
    {
        var (nA, nB) = Field(budget, a, b);
        int fA = nA * a.Def.Cost, fB = nB * b.Def.Cost;
        int gap = Math.Abs(fA - fB);
        // THE FIELDED-CREDIT CHECK. Both sides inside the budget, both field at
        // least one unit, and the gap under one unit's cost. Asserted on every
        // row rather than trusted, because "equal credits" is the claim the
        // old tool made and did not keep.
        if (nA < 1 || nB < 1 || fA > budget || fB > budget || gap >= Math.Max(a.Def.Cost, b.Def.Cost))
        {
            fieldingOk = false;
            SelfFail($"{a.Name} vs {b.Name} at {budget}: fielded {fA} against {fB}, gap {gap}, outside one unit's cost or the budget");
        }
        int gapPct = gap * 100 / Math.Max(fA, fB);
        if (gapPct > largestGapPct || (gapPct == largestGapPct && gap > largestGap))
        { largestGap = gap; largestGapPct = gapPct; largestGapAt = $"{a.Name} vs {b.Name} at {budget} ({fA} v {fB})"; }

        var r1 = Engage(Arena.Diagonal, a, nA, b, nB);
        bool mirror = a.Id == b.Id;
        string winner, surv;
        if (mirror)
        {
            // THE MIRROR SELF-CHECK. Identical armies on mirrored cells must end
            // identically: both destroyed on the same tick. Cloaked mirrors
            // cannot see each other, so they must instead end untouched and
            // still identical. Anything else is a seat effect in the arena,
            // which is precisely what contaminated every mirror before P8-14.
            bool cloaked = a.Def.Stealth && !a.Def.Detector;
            if (cloaked && r1.Outcome == Outcome.NoContact && r1.AliveA == r1.AliveB && r1.HpA == r1.HpB) mirrorsCloaked++;
            else if (!cloaked && r1.Outcome == Outcome.Annihilation) mirrorsAnnihilated++;
            else
            {
                mirrorOk = false;
                SelfFail($"mirror {a.Name} at {budget} ended {r1.Outcome} with {r1.AliveA} against {r1.AliveB} alive and {r1.HpA} against {r1.HpB} hp; a mirrored arena must end symmetric");
            }
            cells[(budget, a.Id, b.Id)] = new Cell(r1, r1, default, default, nA, nB);
            winner = r1.Outcome switch { Outcome.Annihilation => "mutual annihilation", Outcome.NoContact => "NO CONTACT (both cloaked)", _ => r1.Outcome.ToString() };
            surv = "-";
        }
        else
        {
            var r2 = Engage(Arena.Diagonal, b, nB, a, nA);    // the same fight from the other seats
            var l1 = Engage(Arena.LeftRight, a, nA, b, nB);   // and along an axis, from both seats
            var l2 = Engage(Arena.LeftRight, b, nB, a, nA);
            var cell = new Cell(r1, r2, l1, l2, nA, nB);
            cells[(budget, a.Id, b.Id)] = cell;
            switch (Read(cell, a, b, out _))
            {
                case Reading.SeatDecided:
                    seatRows.Add($"{a.Name} vs {b.Name} at {budget}");
                    winner = $"SEAT-DECIDED: {Describe(r1, a, b)} with {a.Name} in seat 0, {Describe(r2, b, a)} with {b.Name} in seat 0";
                    break;
                case Reading.GeometrySensitive:
                    geoRows.Add($"{a.Name} vs {b.Name} at {budget}");
                    string lr = WinnerUnit(l1, a, b) == WinnerUnit(l2, b, a) ? Describe(l1, a, b) : $"{Describe(l1, a, b)} or {Describe(l2, b, a)} by seat";
                    winner = $"GEOMETRY-SENSITIVE: {Describe(r1, a, b)} on the diagonal, {lr} left-right";
                    break;
                default:
                    winner = Describe(r1, a, b);
                    break;
            }
            surv = SurvivorPct(r1, a, nA, b, nB);
        }
        string resolved = r1.Outcome is Outcome.NoContact or Outcome.Stalemate ? $"stopped at {r1.Ticks / (double)World.TicksPerSecond:F1}s" : $"{r1.Ticks / (double)World.TicksPerSecond:F1}s";
        // Doc 12's time-to-kill bounds, over decided asymmetric fights.
        if (!mirror && r1.Outcome is Outcome.SeatA or Outcome.SeatB or Outcome.Annihilation
            && (r1.Ticks < TtkMinSeconds * World.TicksPerSecond || r1.Ticks > TtkMaxSeconds * World.TicksPerSecond))
            ttkOutside.Add($"{a.Name} vs {b.Name} at {budget} ({r1.Ticks / (double)World.TicksPerSecond:F1}s)");
        Line($"| {a.Name} vs {b.Name} | {nA}x{a.Def.Cost}={fA} ({budget - fA}) v {nB}x{b.Def.Cost}={fB} ({budget - fB}){Capped(a, nA, budget)}{Capped(b, nB, budget)} | {winner} | {surv} | {resolved} |");
    }
    Line();
}

// --- The headline: each unit's record at each budget, and F10 --------------
// A record counts a matchup once per budget, and only an outcome both seats
// AND both arenas agree on. A SEAT-DECIDED or GEOMETRY-SENSITIVE row is
// neither a win nor a loss for either unit, because the seat or the angle won
// it. NO CONTACT is neither a win nor a loss, and the mirror is not counted.
Line("### Matrix headline: each unit's record (wins-losses-draws; nc = no contact, sd = seat-decided, gs = geometry-sensitive) and F10");
Line();
Line($"| Unit | {string.Join(" | ", budgets)} | Loses every matchup at every budget? |");
Line($"|---|{string.Concat(budgets.Select(_ => "---|"))}---|");
var dominated = new List<string>();
foreach (var u in combatants)
{
    var opponents = pairs.Where(p => p.A.Id != p.B.Id && (p.A.Id == u.Id || p.B.Id == u.Id)).ToList();
    if (opponents.Count == 0) continue;
    var cellsText = new List<string>();
    bool losesEverywhere = true;
    foreach (int budget in budgets)
    {
        int win = 0, loss = 0, draw = 0, nc = 0, sd = 0, gs = 0;
        foreach (var (a, b) in opponents)
        {
            var c = cells[(budget, a.Id, b.Id)];
            var reading = Read(c, a, b, out var wu);
            if (reading == Reading.SeatDecided) sd++;
            else if (reading == Reading.GeometrySensitive) gs++;
            else if (c.First.Outcome is Outcome.NoContact) nc++;
            else if (wu == null) draw++;
            else if (wu.Id == u.Id) win++;
            else loss++;
        }
        cellsText.Add($"{win}-{loss}-{draw}{(nc > 0 ? $" nc{nc}" : "")}{(sd > 0 ? $" sd{sd}" : "")}{(gs > 0 ? $" gs{gs}" : "")}");
        if (loss != opponents.Count) losesEverywhere = false;
    }
    if (losesEverywhere) dominated.Add(u.Name);
    Line($"| {u.Name} | {string.Join(" | ", cellsText)} | {(losesEverywhere ? "**YES**" : "no")} |");
}
Line();
string f10 = dominated.Count == 0
    ? $"F10 MET: no unit loses every matchup at every budget ({combatants.Count} armed types measured; {nonCombatants.Count} unarmed types have no matchup and cannot be dominated in one)."
    : $"F10 NOT MET: {dominated.Count} unit(s) lose every matchup at every budget: {string.Join(", ", dominated)}.";
Line(f10 + (F10Binding ? " (binding)" : " (reporting until P8-32 flips F10Binding)"));
if (F10Binding && dominated.Count > 0) GateFail(f10);
Line();

Line($"Fielded-credit check: {(fieldingOk ? "PASS" : "FAILED")} over {pairs.Count * budgets.Length} rows; every pair within one unit's cost of the other and inside its budget. "
    + $"Largest gap: {largestGap} credits ({largestGapPct}% of the larger side), {largestGapAt}.");
Line($"Mirror self-check: {(mirrorOk ? "PASS" : "FAILED")}: {mirrorsAnnihilated} uncloaked mirror runs ended in mutual annihilation and {mirrorsCloaked} cloaked mirror runs made no contact and stayed identical, across {budgets.Length} budgets.");
// THE SEAT CHECK, reported rather than asserted, and the reason is a finding
// about the SIM rather than the arena. The arena is exact (the mirror check
// proves it); what is left is processing order inside a tick. The sim walks
// entities in id order and writes some state in place as it goes, so a later
// entity sees an earlier one's change on the SAME tick. The measured case is
// the cloak: a cloaked unit that fires sets its own RevealTicks during the
// combat pass, so a defender with a HIGHER id returns fire that same tick and
// a defender with a lower id waits a tick. Whoever holds the higher ids
// therefore decides an even cloaked duel. The tool cannot remove that and must
// not hide it: such a row is printed SEAT-DECIDED, counted for neither unit,
// and listed here so it stays visible until the sim's ordering is changed.
int asymRows = pairs.Count(p => p.A.Id != p.B.Id) * budgets.Length;
Line($"Seat check (reported): {asymRows} asymmetric rows run from both seats; {seatRows.Count} were SEAT-DECIDED"
    + (seatRows.Count == 0 ? "." : $": {string.Join("; ", seatRows)}. Cause: the sim resolves a tick in entity-id order and writes some state in place (a cloaked shooter's reveal is visible the same tick only to higher ids), so the seat holding the higher ids can decide an otherwise even fight."));
Line($"Geometry check (reported): the same {asymRows} rows fought again along an axis from both seats; {geoRows.Count} changed winner with the angle of approach"
    + (geoRows.Count == 0 ? "." : $": {string.Join("; ", geoRows)}. Read these as unsettled by this tool, not as wins or losses."));
Line($"Time-to-kill bounds (doc 12, {TtkMinSeconds}s to {TtkMaxSeconds}s, reported): {ttkOutside.Count} decided asymmetric rows outside"
    + (ttkOutside.Count == 0 ? "." : $": {string.Join("; ", ttkOutside)}."));
Line();

// --- The counter triangle, at every budget (reporting) ---------------------
// GDD s6's intended counters, per cost. Kept from the old tool's list, which
// asserted them at one budget with unequal armies. They are REPORTED at all
// four budgets now and do not gate: a balance verdict is P8-32's to act on,
// and gating it here would make this tooling row change the game's balance by
// turning CI red until somebody did.
var expectedWinners = new (string A, string B, string Winner, string Why)[]
{
    ("dir_cannon_tank", "com_rifle_squad", "dir_cannon_tank", "armour beats rifles"),
    ("dir_cannon_tank", "com_rocket_squad", "com_rocket_squad", "rockets beat armour"),
    ("com_rifle_squad", "com_rocket_squad", "com_rifle_squad", "rifles beat rockets"),
    ("dir_cannon_tank", "dir_howitzer", "dir_cannon_tank", "armour closes the dead zone"),
    ("com_rifle_squad", "dir_howitzer", "com_rifle_squad", "infantry swarms inside the dead zone"),
    ("com_rocket_squad", "dir_howitzer", "com_rocket_squad", "siege loses every knife fight"),
    ("com_rifle_squad", "sod_phantom_tank", "com_rifle_squad", "rifles shred revealed phantoms"),
    ("dir_cannon_tank", "sod_phantom_tank", "dir_cannon_tank", "cannons beat phantoms in a fair fight"),
    ("com_rocket_squad", "sod_phantom_tank", "com_rocket_squad", "rockets beat phantoms"),
    ("com_rifle_squad", "dir_vanguard_car", "dir_vanguard_car", "the harasser shreds infantry"),
    ("dir_cannon_tank", "dir_vanguard_car", "dir_cannon_tank", "never trade with real armour"),
    ("com_rocket_squad", "dir_vanguard_car", "com_rocket_squad", "AT infantry punishes light plate"),
    ("dir_cannon_tank", "dir_bulwark_tank", "dir_cannon_tank", "massed cannons out-trade the wall"),
};
Line("### Counter triangle (GDD s6) at every budget, reporting");
Line();
Line($"| Intended | Why | {string.Join(" | ", budgets)} |");
Line($"|---|---|{string.Concat(budgets.Select(_ => "---|"))}");
int inversions = 0;
foreach (var (an, bn, wn, why) in expectedWinners)
{
    var ua = roster.FirstOrDefault(u => u.Name == an); var ub = roster.FirstOrDefault(u => u.Name == bn);
    if (ua == null || ub == null) { Line($"| {wn} wins {an} vs {bn} | {why} | {string.Join(" | ", budgets.Select(_ => "type missing"))} |"); continue; }
    var (lo, hi) = ua.Id < ub.Id ? (ua, ub) : (ub, ua);
    var marks = new List<string>();
    foreach (int budget in budgets)
    {
        if (!cells.TryGetValue((budget, lo.Id, hi.Id), out var c)) { marks.Add("no matchup"); continue; }
        var reading = Read(c, lo, hi, out var wu);
        if (reading == Reading.SeatDecided) marks.Add("seat-decided");
        else if (reading == Reading.GeometrySensitive) marks.Add("geometry-sensitive");
        else if (c.First.Outcome == Outcome.NoContact) marks.Add("no contact");
        else if (wu == null) marks.Add("draw");
        else if (wu.Name == wn) marks.Add("holds");
        else { marks.Add("**INVERTED**"); inversions++; }
    }
    Line($"| {wn} wins {an} vs {bn} | {why} | {string.Join(" | ", marks)} |");
}
Line();
Line($"{inversions} inversion(s) across {expectedWinners.Length * budgets.Length} edge-budget cells. Reported, not gated: see the note in the source.");
Line();
Console.Error.WriteLine($"balance: matrix done at {clock.Elapsed.TotalSeconds:F1}s");

// ===========================================================================
// 2. STATIC DEFENCE (TICKET-P5-DEF-17), now on /data
// ===========================================================================
// GDD s6 line 53 ("artillery beats static defence") and ADR-005's stated
// mitigation for the turtling risk ("a CI gate that proves a walled base falls
// to a siege army"), a gate that runs on every push. Unchanged in what it
// asserts and in its fixture: 3000 credits of one besieger type against a
// yard, an oversized plant, two turrets and a twelve-segment wall line in
// three shapes (none is the CONTROL; gapped is ADR-005 clause 6's intended
// doorway; sealed exercises the DEF-05 breach). Two things changed at P8-14:
// the world is registered from /data like every other world here, and it runs
// one seed rather than three identical ones. It is NOT an equal-credit
// comparison and does not claim to be; section 3 is.
//
// SiegeTicks is deliberately not the matrix cap: a siege is a different
// timescale and the wall's designed function is to buy time. MaxWallDelay is
// the turtling bound, measured in both directions (the shipped 500 hp wall
// buys 229 ticks, a wall regressed to 5000 hp buys 1936; 1200 sits between;
// docs/balance/2026-07-15-turtle-gate.md).
const int SiegeArmyCredits = 3000;
const int SiegeTicks = 6000;
const int MaxWallDelay = 1200;

(int WallsDead, int TurretsDead, int RazeTick, int SurvPct) Besiege(Unit atk, string wall)
{
    // A 64x12 corridor the wall line spans, so a sealed wall genuinely seals.
    var world = Catalogued(64, 12, 2);
    // The besieger owns no structure and no MCV, so the short-game rule would
    // eliminate it on tick 1. This fixture measures a siege, not a match.
    world.ShortGameEnabled = false;
    const int midY = 6;
    int cyId = world.SpawnConstructionYard(0, 6, midY - 1);
    // Draw 100 + 20 + 20; one oversized plant so the turrets cannot brown out.
    world.SpawnPowerPlant(0, 6, midY + 3, supply: 200);
    int t1 = world.SpawnTurret(0, 16, midY - 4);
    int t2 = world.SpawnTurret(0, 16, midY + 2);
    var walls = new List<int>();
    if (wall != "none")
        for (int k = 0; k < 12; k++)
        {
            if (wall == "gapped" && (k == 5 || k == 6)) continue;   // the doorway
            walls.Add(world.SpawnWall(0, 20, k));
        }
    int count = SiegeArmyCredits / atk.Def.Cost;
    var army = new List<int>();
    const int rows = 8;
    for (int i = 0; i < count; i++)
        army.Add(SpawnOf(world, 1, atk, Fix64.FromInt(50 + i / rows), Fix64.FromInt(midY - Math.Min(count, rows) / 2 + i % rows)));
    var cmds = new List<Command>();
    foreach (int u in army) cmds.Add(new Command(0, 1, CommandType.AttackMove, u, Fix64.FromInt(10), Fix64.FromInt(midY)));
    int razeTick = -1;
    for (int t = 0; t < SiegeTicks; t++)
    {
        world.Step(CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (!world.Entities[cyId].Alive) { razeTick = t + 1; break; }
    }
    NoteRng(world, "DEF-17 siege");
    int wallsDead = walls.Count(w => !world.Entities[w].Alive);
    int turretsDead = (world.Entities[t1].Alive ? 0 : 1) + (world.Entities[t2].Alive ? 0 : 1);
    int alive = army.Count(u => world.Entities[u].Alive);
    return (wallsDead, turretsDead, razeTick, alive * atk.Def.Cost * 100 / SiegeArmyCredits);
}

Unit UnitNamed(string name) => roster.First(u => u.Name == name);
var besiegers = new[] { UnitNamed("dir_howitzer"), UnitNamed("com_rifle_squad"), UnitNamed("dir_cannon_tank") };
var shapes = new[] { "none", "gapped", "sealed" };
Line("## 2. Static defence: does a walled base fall to a siege army? (DEF-17 gate)");
Line();
Line($"Fortification: 1 Construction Yard, 1 power plant (supply 200 >= draw 140), 2 turrets, 12 wall segments (\"none\" is the unwalled control). "
    + $"Besieger: {SiegeArmyCredits} credits of one type, attack-move, cap {SiegeTicks} ticks. Not an equal-credit comparison: section 3 is.");
Line();
// P7-19: "Ticks bought" is derived per besieger so a negative (a wall that
// helped the attacker) is visible without subtracting non-adjacent rows.
Line("| Besieger | Wall | Segments lost | Turrets killed | Yard razed | Ticks bought | Army retained | Verdict |");
Line("|---|---|---|---|---|---|---|---|");
var razeBy = new Dictionary<(int, string), int>();
foreach (var atk in besiegers)
    foreach (var shape in shapes)
    {
        var r = Besiege(atk, shape);
        razeBy[(atk.Id, shape)] = r.RazeTick;
        bool breached = r.RazeTick > 0 && r.SurvPct > 30;
        string bought = "-";
        if (shape != "none" && razeBy.TryGetValue((atk.Id, "none"), out int ctrl) && ctrl > 0 && r.RazeTick > 0)
        {
            int delta = r.RazeTick - ctrl;
            bought = delta > 0 ? "+" + delta : delta.ToString();
        }
        Line($"| {atk.Name} | {shape} | {r.WallsDead} | {r.TurretsDead}/2 | {(r.RazeTick > 0 ? "t=" + r.RazeTick : "no")} | {bought} | {r.SurvPct}% | {(breached ? "BREACHED" : "held")} |");
        // THE GATE: only the howitzer, only in the direction GDD s6 line 53 and
        // ADR-005 commit to. Rifle and cannon rows are reporting only.
        if (atk.Name != "dir_howitzer") continue;
        if (r.RazeTick < 0) GateFail($"artillery did not raze a {shape}-walled base within {SiegeTicks} ticks; GDD s6 line 53 says artillery beats static defence");
        else if (r.SurvPct <= 30) GateFail($"artillery razed the {shape}-walled base but kept only {r.SurvPct}% of its army; a pyrrhic siege is not a counter");
        if (r.TurretsDead < 2) GateFail($"artillery left {2 - r.TurretsDead} turret(s) standing behind a {shape} wall");
        if (shape == "sealed" && r.WallsDead < 1) GateFail("artillery razed a sealed base without breaching a single segment; the breach path is dead");
    }
int howId = UnitNamed("dir_howitzer").Id;
int noneRaze = razeBy[(howId, "none")], sealedRaze = razeBy[(howId, "sealed")], gappedRaze = razeBy[(howId, "gapped")];
Line();
Line("| Artillery siege | Unwalled control | Gapped wall | Sealed wall |");
Line("|---|---|---|---|");
Line($"| Yard razed at tick | {noneRaze} | {gappedRaze} | {sealedRaze} |");
Line($"| Ticks bought by the wall | - | {(gappedRaze > 0 && noneRaze > 0 ? (gappedRaze - noneRaze).ToString() : "n/a")} | {(sealedRaze > 0 && noneRaze > 0 ? (sealedRaze - noneRaze).ToString() : "n/a")} |");
if (noneRaze < 0) GateFail("artillery cannot raze even an UNWALLED fortification; the control is broken and every number above is unattributable");
else if (sealedRaze > 0 && sealedRaze - noneRaze > MaxWallDelay)
    GateFail($"a sealed wall bought the turtle {sealedRaze - noneRaze} ticks against a siege army, over the stated bound of {MaxWallDelay}; turtling is winning");
Line();
Line("Rifle and cannon rows are REPORTING ONLY (ADR-010's consequences): massed rifles and cannons genuinely raze this fortification at 3000 credits, "
    + "so gating \"must not breach\" would gate a claim that is measurably false. Against artillery a gapped wall is worth zero ticks, because the howitzer's range 9 "
    + "beats the turret's 5 and it never touches masonry (docs/balance/2026-07-15-turtle-gate.md).");
Line();

// ===========================================================================
// 3. THE EQUAL-CREDIT SIEGE (tracker D19), reporting
// ===========================================================================
// D19: static defence is judged by an equal-credit siege row that includes
// each side's FACTION defence, because the DEF-17 fixture above sets 2400
// credits of defence (two turrets and twelve walls) against 3000 of attackers.
// Here each faction's own gun, alone, is bought to DefenceCredits, and every
// armed ground type besieges it with the same credits fielded. 2800 is the
// least common multiple of today's Bastion (1400) and Shroud Nest (400), so
// both factions field exactly 2800 today; if either price moves, the remainder
// is printed rather than hidden. Same yard and plant as the target in both.
// D19 reverses (the faction defences are re-tuned) if this row still loses to
// every non-artillery besieger; P8-33 records that verdict from this table.
const int DefenceCredits = 2800;
int bastionId = StructureCatalogue.TypeIdOf("dir_bastion");
int nestId = StructureCatalogue.TypeIdOf("sod_shroud_nest");

(int RazeTick, int DefencesDead, int Defences, int Fielded, int ArmyCount, int SurvPct, int DefenceFielded) EqualSiege(int defenceType, Unit atk)
{
    var world = Catalogued(64, 20, 2);
    world.ShortGameEnabled = false;
    const int midY = 10;
    var ddef = world.GetStructureType(defenceType);
    int n = DefenceCredits / ddef.Cost;
    int cyId = world.SpawnConstructionYard(0, 6, midY - 1);
    world.SpawnPowerPlant(0, 6, midY + 3, supply: 400);   // oversized: no brown-out can decide a siege
    var guns = new List<int>();
    int y0 = midY - n * ddef.Footprint / 2;
    for (int k = 0; k < n; k++) guns.Add(world.SpawnFactionDefence(0, defenceType, 16, y0 + k * ddef.Footprint));
    int defenceFielded = n * ddef.Cost;
    // The matrix's closest-count rule, bounded by the same DefenceCredits budget.
    int count = Closest(defenceFielded, atk.Def.Cost, Math.Max(1, Math.Min(DefenceCredits / atk.Def.Cost, Cap(atk))));
    var army = new List<int>();
    const int rows = 8;
    for (int i = 0; i < count; i++)
        army.Add(SpawnOf(world, 1, atk, Map.CellCentre(50 + i / rows), Map.CellCentre(midY - Math.Min(count, rows) / 2 + i % rows)));
    var cmds = new List<Command>();
    foreach (int u in army) cmds.Add(new Command(0, 1, CommandType.AttackMove, u, Map.CellCentre(7), Map.CellCentre(midY)));
    int razeTick = -1;
    for (int t = 0; t < SiegeTicks; t++)
    {
        world.Step(CollectionsMarshal.AsSpan(cmds));
        cmds.Clear();
        if (!world.Entities[cyId].Alive) { razeTick = t + 1; break; }
        if (army.All(u => !world.Entities[u].Alive)) break;
    }
    NoteRng(world, "equal-credit siege");
    int alive = army.Count(u => world.Entities[u].Alive);
    int fielded = count * atk.Def.Cost;
    return (razeTick, guns.Count(g => !world.Entities[g].Alive), n, fielded, count, fielded == 0 ? 0 : alive * atk.Def.Cost * 100 / fielded, defenceFielded);
}

Line("## 3. Equal-credit siege: each faction's own defence (D19), reporting");
Line();
Line($"Target: 1 Construction Yard and 1 oversized power plant. Defence: {DefenceCredits} credits of the faction's own gun only, stacked in one column in front of the yard "
    + $"(Directorate: {DefenceCredits / cat.GetStructureType(bastionId).Cost} dir_bastion; Sodality: {DefenceCredits / cat.GetStructureType(nestId).Cost} sod_shroud_nest, cloaked until it fires). "
    + $"Besieger: the same credits fielded of one armed ground type, attack-move, cap {SiegeTicks} ticks.");
Line();
Line("| Besieger | Fielded | v Directorate (Bastions): yard | guns lost | army kept | v Sodality (Shroud Nests): yard | guns lost | army kept |");
Line("|---|---|---|---|---|---|---|---|");
var groundGuns = combatants.Where(u => !u.Def.Air && !u.Weapon.AntiAir).ToList();
var held = new Dictionary<int, List<string>> { [bastionId] = new(), [nestId] = new() };
var fell = new Dictionary<int, List<string>> { [bastionId] = new(), [nestId] = new() };
foreach (var atk in groundGuns)
{
    var d = EqualSiege(bastionId, atk);
    var s = EqualSiege(nestId, atk);
    foreach (var (id, r) in new[] { (bastionId, d), (nestId, s) })
        (r.RazeTick > 0 ? fell : held)[id].Add(atk.Name);
    string Yard(int tick) => tick > 0 ? $"razed t={tick}" : "**held**";
    Line($"| {atk.Name} | {d.ArmyCount}x{atk.Def.Cost}={d.Fielded} v {d.DefenceFielded} | {Yard(d.RazeTick)} | {d.DefencesDead}/{d.Defences} | {d.SurvPct}% | {Yard(s.RazeTick)} | {s.DefencesDead}/{s.Defences} | {s.SurvPct}% |");
}
Line();
foreach (var (id, label) in new[] { (bastionId, "Directorate (Bastions)"), (nestId, "Sodality (Shroud Nests)") })
{
    var nonArtyHeld = held[id].Where(n => n != "dir_howitzer").ToList();
    Line($"{label}: held against {held[id].Count} of {groundGuns.Count} besiegers{(held[id].Count > 0 ? $" ({string.Join(", ", held[id])})" : "")}; "
        + $"{(nonArtyHeld.Count == 0 ? "fell to EVERY non-artillery besieger (D19's reversal condition)" : $"held against {nonArtyHeld.Count} non-artillery besieger(s)")}.");
}
Line();
Console.Error.WriteLine($"balance: sieges done at {clock.Elapsed.TotalSeconds:F1}s");

// ===========================================================================
// 4. COST EFFICIENCY (DEF-17 clause 3), now off the registered catalogue
// ===========================================================================
// The wall is deliberately the cheapest hit points in the game; the bound
// stops a tuning pass from quietly making walls free. Integer maths: hp per
// credit x100, bound 800 (8.0).
var wallDef = cat.GetStructureType(StructureCatalogue.TypeIdOf("com_wall"));
var turretDef = cat.GetStructureType(StructureCatalogue.TypeIdOf("dir_turret"));
var tankDef = UnitNamed("dir_cannon_tank").Def;
int wallX100 = wallDef.Hp * 100 / wallDef.Cost, turretX100 = turretDef.Hp * 100 / turretDef.Cost, tankX100 = tankDef.Hp * 100 / tankDef.Cost;
string Hpc(int x100) => $"{x100 / 100}.{x100 % 100:D2}";
Line("## 4. Cost efficiency: hit points per credit");
Line();
Line("| Thing | Hit points | Cost | Hp per credit |");
Line("|---|---|---|---|");
Line($"| wall segment | {wallDef.Hp} | {wallDef.Cost} | {Hpc(wallX100)} |");
Line($"| turret | {turretDef.Hp} | {turretDef.Cost} | {Hpc(turretX100)} |");
Line($"| cannon tank | {tankDef.Hp} | {tankDef.Cost} | {Hpc(tankX100)} |");
if (wallX100 > 800) GateFail($"a wall buys {Hpc(wallX100)} hp per credit, over the stated bound of 8.0; the cheapest hit points in the game have become free ones");
Line();

// ===========================================================================
// 5. HARVESTER TEMPO (TICKET-P2-BAL-02), now on /data
// ===========================================================================
long Tempo()
{
    var w = Catalogued(64, 64, 1);
    w.SpawnConstructionYard(0, 6, 30);
    w.SpawnPowerPlant(0, 10, 30);
    w.SpawnRefinery(0, 6, 34);
    w.SpawnFactory(0, 10, 34);
    int h = w.SpawnHarvester(0, Fix64.FromInt(9), Fix64.FromInt(37));
    int field = w.SpawnFerriteField(Fix64.FromInt(20), Fix64.FromInt(34), 12000);
    var c = new List<Command> { new(0, 0, CommandType.Harvest, h, Fix64.Zero, Fix64.Zero, field) };
    for (int t = 0; t < 3000; t++)
    {
        w.Step(CollectionsMarshal.AsSpan(c));
        c.Clear();
    }
    NoteRng(w, "tempo");
    return w.Credits(0);
}
long tempoA = Tempo(), tempoB = Tempo();
Line($"## 5. Harvester tempo (standard opening, 3000 ticks): {tempoA} credits");
if (tempoA != tempoB) SelfFail("tempo not reproducible between two identical runs");
if (tempoA < 1400) GateFail($"tempo collapsed below 2 delivered loads ({tempoA})");
Line();

// ===========================================================================
// 6. THE FACTION WAR (F11)
// ===========================================================================
// The shipped skirmish, exactly: the map's own terrain and fields, the /data
// catalogue registered inside BuildWorld's configure hook (SkirmishLive's
// RegisterCatalogue), factions set before the opening hand, then
// PlaceSkirmishStart with the menu's default 8000 credits, then one Standard
// commander per seat at Normal reading its tuning from that world. Each
// faction plays each seat on each map, so a seat advantage shows up as a SEAT
// share and cannot pose as a faction one. A cell is decided by the sim's own
// victory rule or not at all: there is no adjudication, and banked credits
// decide nothing, because rewarding a treasury rewards the AI's spending defect
// (AI-03) rather than the side that is winning.
//
// Stuck-harvester share (ML-01): a harvester-tick is STUCK when the harvester
// is neither loading nor unloading and its position did not change from the
// previous tick. Measured per seat over the whole match.
var warMaps = full ? warMapsFull : warMapsQuick;
WarResult War(string mapName, int dirSeat)
{
    var map = MapData.Load(Path.Combine(dataRoot, "maps", mapName + ".fmap"));
    var w = map.BuildWorld(Seed, players: 2, out _, x => CatalogueFiles.RegisterAll(x, dataRoot));
    int sodSeat = dirSeat == 0 ? 1 : 0;
    w.SetFaction(dirSeat, World.FactionDirectorate);
    w.SetFaction(sodSeat, World.FactionSodality);
    map.PlaceSkirmishStart(w, WarStartCredits);
    var ais = new[] { SkirmishAI.Standard(0, AiDifficulty.Normal, w), SkirmishAI.Standard(1, AiDifficulty.Normal, w) };
    var cmds = new List<Command>();
    var lastPos = new Dictionary<int, (Fix64 X, Fix64 Y)>();
    var harvTicks = new long[2];
    var stuckTicks = new long[2];
    while (w.Tick < WarCapTicks && w.Winner < 0)
    {
        cmds.Clear();
        ais[0].Act(w, cmds);
        ais[1].Act(w, cmds);
        w.Step(CollectionsMarshal.AsSpan(cmds));
        var es = w.Entities;
        for (int i = 0; i < es.Count; i++)
        {
            var e = es[i];
            if (e.Kind != EntityKind.Harvester || !e.Alive || (uint)e.PlayerId > 1) continue;
            harvTicks[e.PlayerId]++;
            if (lastPos.TryGetValue(e.Id, out var p) && p.X == e.X && p.Y == e.Y
                && e.HState != HarvestState.Loading && e.HState != HarvestState.Unloading)
                stuckTicks[e.PlayerId]++;
            lastPos[e.Id] = (e.X, e.Y);
        }
    }
    NoteRng(w, "faction war");
    return new WarResult(mapName, dirSeat, w.Winner, w.Winner >= 0 ? w.Tick : -1,
        Pct(stuckTicks[0], harvTicks[0]), Pct(stuckTicks[1], harvTicks[1]), w.Credits(0), w.Credits(1));
}

var war = new List<WarResult>();
foreach (var m in warMaps)
    for (int dirSeat = 0; dirSeat < 2; dirSeat++)
    {
        war.Add(War(m, dirSeat));
        Console.Error.WriteLine($"balance: faction war {m} (Directorate in seat {dirSeat}) done at {clock.Elapsed.TotalSeconds:F1}s");
    }

string FactionName(int f) => f == World.FactionDirectorate ? "Directorate" : "Sodality";
int WinnerFaction(WarResult r) => r.Winner < 0 ? -1 : r.Winner == r.DirSeat ? World.FactionDirectorate : World.FactionSodality;
Line($"## 6. Faction war: Directorate vs Sodality, each faction in each seat ({war.Count} cells on {string.Join(", ", warMaps)})");
Line();
Line($"One Normal Standard commander per seat, PlaceSkirmishStart({WarStartCredits}), /data catalogue, played to the sim's own victory or {WarCapTicks} ticks. "
    + "Undecided cells are reported undecided: nothing is adjudicated, and banked credits decide nothing (shown only so a banking stall is visible).");
Line();
Line("| Map | Seat 0 | Seat 1 | Winner by faction | Winner by seat | Decided at | Stuck harvester-ticks (seat 0 / seat 1) | Banked at end (not used) |");
Line("|---|---|---|---|---|---|---|---|");
foreach (var r in war)
{
    int wf = WinnerFaction(r);
    Line($"| {r.Map} | {FactionName(r.DirSeat == 0 ? World.FactionDirectorate : World.FactionSodality)} | {FactionName(r.DirSeat == 1 ? World.FactionDirectorate : World.FactionSodality)} "
        + $"| {(wf < 0 ? "undecided" : FactionName(wf))} | {(r.Winner < 0 ? "undecided" : "seat " + r.Winner)} "
        + $"| {(r.DecidedAt > 0 ? $"t={r.DecidedAt} ({r.DecidedAt / World.TicksPerSecond / 60}m{r.DecidedAt / World.TicksPerSecond % 60:D2}s)" : $"not by {WarCapTicks}")} "
        + $"| {r.StuckPct0}% / {r.StuckPct1}% | {r.Credits0} / {r.Credits1} |");
}
Line();
// The reading column says which effect a map's two cells show. Each faction
// plays each seat once per map, so one SEAT winning both cells is a seat
// effect (the faction changed and the winner did not) and one FACTION winning
// both is a faction effect; with an undecided cell the map says neither.
Line("| Scope | Directorate | Sodality | Undecided | Seat 0 | Seat 1 | Reading |");
Line("|---|---|---|---|---|---|---|");
foreach (var scope in warMaps.Select(m => (Name: m, Rows: war.Where(r => r.Map == m).ToList())).Append((Name: "**all cells**", Rows: war)))
{
    int n = scope.Rows.Count;
    int dir = scope.Rows.Count(r => WinnerFaction(r) == World.FactionDirectorate);
    int sod = scope.Rows.Count(r => WinnerFaction(r) == World.FactionSodality);
    int und = scope.Rows.Count(r => r.Winner < 0);
    int s0 = scope.Rows.Count(r => r.Winner == 0), s1 = scope.Rows.Count(r => r.Winner == 1);
    string reading = n != 2 ? "-"
        : und > 0 ? "unsettled (a cell undecided)"
        : s0 == 2 || s1 == 2 ? $"SEAT effect: seat {(s0 == 2 ? 0 : 1)} won with both factions"
        : $"FACTION effect: {(dir == 2 ? "Directorate" : "Sodality")} won from both seats";
    Line($"| {scope.Name} | {Share(dir, n)} | {Share(sod, n)} | {Share(und, n)} | {Share(s0, n)} | {Share(s1, n)} | {reading} |");
}
Line();
int dirCells = war.Count(r => WinnerFaction(r) == World.FactionDirectorate);
int sodCells = war.Count(r => WinnerFaction(r) == World.FactionSodality);
int seat0Cells = war.Count(r => r.Winner == 0), seat1Cells = war.Count(r => r.Winner == 1);
bool f11Met = dirCells * 100 >= F11MinSharePct * war.Count && sodCells * 100 >= F11MinSharePct * war.Count;
string f11 = (f11Met ? "F11 MET" : "F11 NOT MET")
    + $": Directorate {Share(dirCells, war.Count)}, Sodality {Share(sodCells, war.Count)} of cells; each needs at least {F11MinSharePct}%."
    + (full ? "" : " QUICK MODE: two of F11's three maps; `full` measures the criterion as written.");
Line(f11 + (F11Binding ? " (binding)" : " (reporting until P8-33 flips F11Binding)"));
Line($"Seat share: seat 0 {Share(seat0Cells, war.Count)}, seat 1 {Share(seat1Cells, war.Count)}. "
    + (Math.Abs(seat0Cells - seat1Cells) > Math.Abs(dirCells - sodCells)
        ? "The seat split is wider than the faction split: read this war as a SEAT effect first (D5, P8-21), not a faction verdict."
        : Math.Abs(seat0Cells - seat1Cells) == Math.Abs(dirCells - sodCells)
            ? "The seat split is as wide as the faction split, so the totals cannot tell the two apart: read the per-map Reading column."
            : "The faction split is wider than the seat split."));
if (F11Binding && (!f11Met || !full)) GateFail(full ? f11 : "F11 is binding and can only be judged in full mode; run `full`");
Line();

// ===========================================================================
// Seed invariance, verdict, report
// ===========================================================================
Line(rngDrawn == 0
    ? $"Seed invariance: the RNG was never drawn in any of {runs} runs, so seed {Seed} stands for every seed and one run per cell is the whole population."
    : $"Seed invariance: WARNING, the RNG was drawn in {rngDrawn} of {runs} runs ({string.Join(", ", rngDrawnWhere)}). Those cells are now SAMPLES of a distribution; "
      + "run several seeds per cell before reading a share off them.");
Line();
bool pass = selfFailures.Count == 0 && gateFailures.Count == 0;
Line(pass
    ? "## VERDICT: PASS - every self-check and every binding gate held. F10 and F11 above are measured truthfully and bind only when their switches flip."
    : $"## VERDICT: FAIL - {selfFailures.Count} self-check(s) and {gateFailures.Count} binding gate(s) failed (listed above).");

var text = report.ToString();
Console.Write(text);
Console.Error.WriteLine($"balance: {(full ? "full" : "quick")} mode, total {clock.Elapsed.TotalSeconds:F1}s");
if (reportPath != null) File.WriteAllText(reportPath, text);
return pass ? 0 : 1;

// ===========================================================================
// Helpers
// ===========================================================================

static int Usage(string why)
{
    Console.Error.WriteLine($"balance: {why}");
    Console.Error.WriteLine("usage: Ferrostorm.Balance [full|quick] [--report PATH-OUTSIDE-THE-REPOSITORY]   (full is the default and what CI runs)");
    return 2;
}

// Spawn one unit exactly as the factory delivers it (World's ProductionSystem):
// every catalogue field, including the ones the old tool dropped (stealth,
// detector, sight, veterancy, and the unit type that makes IsAirborne true).
static int SpawnOf(World w, int seat, Unit u, Fix64 x, Fix64 y)
    => w.SpawnUnit(seat, x, y, u.Def.Speed, u.Def.Hp, u.Def.Armour, u.Def.WeaponId,
        u.Def.SightCells, u.Def.Stealth, u.Def.Detector, u.Def.Veterancy, u.Id);

// The diagonal arena (see LineLength above for why diagonal). Seat 0's unit i
// stands at rank i / LineLength, file i % LineLength, a short line centred on
// the same spot whatever its length; seat 1's unit i stands on the transpose
// of that cell. Cell centres throughout, so the mirror holds in cell terms too.
// The left-right arena (the geometry check) stacks the same counts in columns
// of LineLength rows centred on row 32, seat 1 on the mirrored columns.
static List<int> Muster(World w, Arena arena, int seat, Unit u, int n)
{
    int len = Math.Min(n, LineLength), off = (LineLength - len) / 2;
    var ids = new List<int>(n);
    for (int i = 0; i < n; i++)
    {
        int file = i % LineLength, rank = i / LineLength;
        if (arena == Arena.LeftRight)
        {
            int cx = seat == 0 ? LrFront - rank : 63 - LrFront + rank, cy = 32 - len / 2 + file;
            ids.Add(SpawnOf(w, seat, u, Map.CellCentre(cx), Map.CellCentre(cy)));
            continue;
        }
        int x = LineX0 + off + file - rank, y = LineY0 + off + file + rank;
        ids.Add(seat == 0
            ? SpawnOf(w, 0, u, Map.CellCentre(x), Map.CellCentre(y))
            : SpawnOf(w, 1, u, Map.CellCentre(y), Map.CellCentre(x)));
    }
    return ids;
}

// See PursueEvery: re-order every idle survivor at the nearest living enemy.
static void Pursue(World w, int seat, List<int> own, List<int> enemy, List<Command> cmds)
{
    foreach (int u in own)
    {
        var e = w.Entities[u];
        if (!e.Alive || e.AMove) continue;   // still prosecuting its attack-move
        int best = -1; Fix64 bestD = Fix64.MaxValue;
        foreach (int v in enemy)
        {
            var t = w.Entities[v];
            if (!t.Alive) continue;
            Fix64 d = Fix64.DistSq(t.X - e.X, t.Y - e.Y);
            if (d < bestD) { bestD = d; best = v; }
        }
        if (best >= 0) cmds.Add(new Command(w.Tick, seat, CommandType.AttackMove, u, w.Entities[best].X, w.Entities[best].Y));
    }
}

static int Alive(World w, List<int> ids) { int n = 0; foreach (int i in ids) if (w.Entities[i].Alive) n++; return n; }
static int SumHp(World w, List<int> ids) { int s = 0; foreach (int i in ids) { var e = w.Entities[i]; if (e.Alive) s += e.Hp; } return s; }
static int Cap(Unit u) => u.Def.MaxAlive > 0 ? u.Def.MaxAlive : int.MaxValue;

// Equal credits fielded (defect 2). Try both anchorings: one side at its
// maximum, the other at the count whose value is closest to it inside the
// budget. Take the anchoring with the smaller gap; on a tie, the larger total.
static (int NA, int NB) Field(int budget, Unit a, Unit b)
{
    int maxA = Math.Min(budget / a.Def.Cost, Cap(a)), maxB = Math.Min(budget / b.Def.Cost, Cap(b));
    if (maxA < 1 || maxB < 1) return (maxA, maxB);
    int nb1 = Closest(maxA * a.Def.Cost, b.Def.Cost, maxB);
    int na2 = Closest(maxB * b.Def.Cost, a.Def.Cost, maxA);
    int gap1 = Math.Abs(maxA * a.Def.Cost - nb1 * b.Def.Cost), gap2 = Math.Abs(na2 * a.Def.Cost - maxB * b.Def.Cost);
    int tot1 = maxA * a.Def.Cost + nb1 * b.Def.Cost, tot2 = na2 * a.Def.Cost + maxB * b.Def.Cost;
    return gap1 < gap2 || (gap1 == gap2 && tot1 >= tot2) ? (maxA, nb1) : (na2, maxB);
}

// The count of a unit costing `cost` whose value is closest to `target`, in
// [1, max]; a tie goes to the count that does not exceed the target.
static int Closest(int target, int cost, int max)
{
    int lo = Math.Clamp(target / cost, 1, max), hi = Math.Clamp(lo + 1, 1, max);
    return Math.Abs(hi * cost - target) < Math.Abs(lo * cost - target) ? hi : lo;
}

static Unit? WinnerUnit(EngageResult r, Unit seat0, Unit seat1)
    => r.Outcome switch { Outcome.SeatA => seat0, Outcome.SeatB => seat1, _ => null };

// The ONE reading of an asymmetric row. `winner` is the diagonal seat-0 run's
// winner (null for a draw, a stalemate or no contact), meaningful only when
// the reading is Agreed.
static Reading Read(Cell c, Unit a, Unit b, out Unit? winner)
{
    winner = WinnerUnit(c.First, a, b);
    if (winner != WinnerUnit(c.Swapped, b, a)) return Reading.SeatDecided;
    if (winner != WinnerUnit(c.LrFirst, a, b) || winner != WinnerUnit(c.LrSwapped, b, a)) return Reading.GeometrySensitive;
    return Reading.Agreed;
}

static string Describe(EngageResult r, Unit seat0, Unit seat1) => r.Outcome switch
{
    Outcome.SeatA => seat0.Name,
    Outcome.SeatB => seat1.Name,
    Outcome.Annihilation => "mutual annihilation",
    Outcome.NoContact => "NO CONTACT (neither can see the other)",
    _ => $"stalemate ({r.AliveA} v {r.AliveB} alive)",
};

static string SurvivorPct(EngageResult r, Unit a, int nA, Unit b, int nB) => r.Outcome switch
{
    Outcome.SeatA => $"{r.AliveA * 100 / nA}%",
    Outcome.SeatB => $"{r.AliveB * 100 / nB}%",
    _ => "-",
};

static string Capped(Unit u, int n, int budget)
    => u.Def.MaxAlive > 0 && n == u.Def.MaxAlive && budget / u.Def.Cost > n ? $" [{u.Name} capped at max_alive {u.Def.MaxAlive}]" : "";

static int Pct(long part, long whole) => whole == 0 ? 0 : (int)(part * 100 / whole);
static string Share(int k, int n) => n == 0 ? "-" : $"{k}/{n} ({k * 100 / n}%)";

// THE REPOSITORY GUARD (defect 6). A path is refused when, with symbolic links
// resolved over the part of it that exists, it lies inside this checkout or
// inside the main work tree that owns it (a git worktree's .git file names the
// common directory, and the main work tree is its parent). Case is folded,
// because APFS and NTFS fold it by default: the guard refuses more, never less.
static string? InsideRepository(string path, string repoRoot)
{
    string target = RealPath(Path.GetFullPath(path));
    var roots = new List<string> { RealPath(repoRoot) };
    string dotGit = Path.Combine(repoRoot, ".git");
    if (File.Exists(dotGit))
    {
        string first = File.ReadLines(dotGit).FirstOrDefault() ?? "";
        if (first.StartsWith("gitdir:", StringComparison.Ordinal))
        {
            // <main>/.git/worktrees/<name> -> <main>
            var gitDir = new DirectoryInfo(Path.GetFullPath(first.Substring(7).Trim(), repoRoot));
            var main = gitDir.Parent?.Parent?.Parent;
            if (main != null) roots.Add(RealPath(main.FullName));
        }
    }
    foreach (var root in roots)
        if (IsUnder(target, root)) return $"it resolves to {target}, inside the repository at {root}";
    return null;
}

static bool IsUnder(string p, string root)
{
    root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return p.Equals(root, StringComparison.OrdinalIgnoreCase)
        || p.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

static string RealPath(string full)
{
    for (int depth = 0; depth < 32; depth++)
    {
        string root = Path.GetPathRoot(full)!;
        var parts = full.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        string cur = root;
        bool relinked = false;
        for (int i = 0; i < parts.Length; i++)
        {
            string next = Path.Combine(cur, parts[i]);
            FileSystemInfo? info = Directory.Exists(next) ? new DirectoryInfo(next) : File.Exists(next) ? new FileInfo(next) : null;
            if (info == null) return Path.Combine(new[] { cur }.Concat(parts.Skip(i)).ToArray());
            var link = info.LinkTarget;
            if (link != null)
            {
                // Re-walk from the top with the link replaced, so a target that
                // itself passes through a link is resolved too.
                string resolved = Path.GetFullPath(link, cur);
                full = Path.Combine(new[] { resolved }.Concat(parts.Skip(i + 1)).ToArray());
                relinked = true;
                break;
            }
            cur = next;
        }
        if (!relinked) return cur;
    }
    return full;
}

// ===========================================================================
// Types
// ===========================================================================

record Unit(int Id, string Name, World.UnitTypeDef Def, WeaponDef Weapon)
{
    public bool Armed => Def.WeaponId != 0 && Weapon.Damage > 0;
    /// <summary>ADR-028 clause 3: a weapon engages air if and only if it is anti-air.</summary>
    public bool CanEngage(Unit target) => Armed && Weapon.AntiAir == target.Def.Air;
}

enum Outcome { SeatA, SeatB, Annihilation, NoContact, Stalemate }

enum Arena { Diagonal, LeftRight }

/// <summary>How a matchup row reads once every run is in: a winner both seats
/// and both arenas agree on (or none: a draw or no contact), or a row the seat
/// or the angle of approach decided, which counts for neither unit.</summary>
enum Reading { Agreed, SeatDecided, GeometrySensitive }

record struct EngageResult(Outcome Outcome, int Ticks, int AliveA, int AliveB, int HpA, int HpB);

/// <summary>One matchup at one budget: the diagonal arena from both seats, and
/// the left-right arena from both seats (left default for a mirror, which only
/// the exact diagonal arena runs).</summary>
record struct Cell(EngageResult First, EngageResult Swapped, EngageResult LrFirst, EngageResult LrSwapped, int NA, int NB);

record struct WarResult(string Map, int DirSeat, int Winner, int DecidedAt, int StuckPct0, int StuckPct1, long Credits0, long Credits1);
