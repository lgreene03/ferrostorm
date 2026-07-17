using System.Globalization;

namespace Ferrostorm.Sim;

/// <summary>
/// TICKET-P2-DATA-03: text map format. Same philosophy as the data loader -
/// a strict format that fails loudly, zero dependencies, parsed once at match
/// setup. Grid characters: '.' open ground, '#' impassable, 'F' ferrite
/// field (standard 12000 deposit at that cell).
/// Visual terrain classes (TICKET-P4-TER-01): to the SIM these alias exactly
/// to open or blocked - passability and hashes are untouched - but the
/// client reads MapData.Visual to render them as what they are:
///   'w' water (blocked)   'h' hill (blocked)    'r' ruin (blocked)
///   'f' fence (blocked)   'B' bridge (OPEN: the pathable river crossing)
///   ferrostorm-map v1
///   size W H
///   start PLAYER CX CY        (one per player; the CY footprint anchor)
///   grid:
///   [H rows of exactly W characters]
/// </summary>
public readonly record struct MapUnit(int Player, int UnitType, int Cx, int Cy, string Tag);
public readonly record struct MapStructure(int Player, int StructType, int Ax, int Ay, string Tag);

/// <summary>One mission trigger: a condition and an action, fired once (TICKET-P2-SIM-20).
/// Conditions: elapsed T | destroyed TAG | credits P AMOUNT | entered P CX CY R.
/// Actions: grant P AMOUNT | spawn P UNITTYPE CX CY COUNT | win P | message ID.</summary>
public readonly record struct MapTrigger(string[] When, string[] Do);

public sealed class MapData
{
    public int Width { get; init; }
    public int Height { get; init; }
    public IReadOnlyList<(int Cx, int Cy)> Blocked { get; init; } = Array.Empty<(int, int)>();
    public IReadOnlyList<(int Cx, int Cy)> Fields { get; init; } = Array.Empty<(int, int)>();
    public IReadOnlyDictionary<int, (int Cx, int Cy)> Starts { get; init; } = new Dictionary<int, (int, int)>();
    public IReadOnlyDictionary<int, int> Factions { get; init; } = new Dictionary<int, int>();
    public bool ShortGame { get; init; } = true;
    public IReadOnlyList<MapUnit> Units { get; init; } = Array.Empty<MapUnit>();
    public IReadOnlyList<MapStructure> Structures { get; init; } = Array.Empty<MapStructure>();
    public IReadOnlyList<MapTrigger> Triggers { get; init; } = Array.Empty<MapTrigger>();
    /// <summary>Client-only terrain dressing per cell; the sim never reads this.</summary>
    public IReadOnlyDictionary<(int Cx, int Cy), char> Visual { get; init; } = new Dictionary<(int, int), char>();
    public const int StandardFieldAmount = 12000;

    public static MapData Load(string path) => Parse(File.ReadAllText(path));

    public static MapData Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || (lines[0] != "ferrostorm-map v1" && lines[0] != "ferrostorm-map v2"))
            throw new FormatException("not a ferrostorm map (v1/v2)");
        int w = 0, h = 0, gridAt = -1;
        var starts = new Dictionary<int, (int, int)>();
        var factions = new Dictionary<int, int>();
        bool shortGame = true;
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#') && !line.All(c => c is '#' or '.')) continue;
            if (line == "grid:") { gridAt = i + 1; break; }
            var p = line.Split(' ');
            switch (p[0])
            {
                case "size":
                    w = int.Parse(p[1], CultureInfo.InvariantCulture);
                    h = int.Parse(p[2], CultureInfo.InvariantCulture);
                    break;
                case "start":
                    starts[int.Parse(p[1], CultureInfo.InvariantCulture)] =
                        (int.Parse(p[2], CultureInfo.InvariantCulture), int.Parse(p[3], CultureInfo.InvariantCulture));
                    break;
                case "faction":
                    factions[int.Parse(p[1], CultureInfo.InvariantCulture)] = int.Parse(p[2], CultureInfo.InvariantCulture);
                    break;
                case "rules":
                    if (p[1] == "noshortgame") shortGame = false;
                    else throw new FormatException($"line {i + 1}: unknown rule '{p[1]}'");
                    break;
                default: throw new FormatException($"line {i + 1}: unknown header '{p[0]}'");
            }
        }
        if (w <= 0 || h <= 0 || gridAt < 0) throw new FormatException("missing size or grid");
        if (lines.Length < gridAt + h) throw new FormatException($"grid truncated: need {h} rows");
        var blocked = new List<(int, int)>();
        var visual = new Dictionary<(int, int), char>();
        var fields = new List<(int, int)>();
        for (int y = 0; y < h; y++)
        {
            string row = lines[gridAt + y];
            if (row.Length != w) throw new FormatException($"grid row {y}: expected {w} chars, got {row.Length}");
            for (int x = 0; x < w; x++)
                switch (row[x])
                {
                    case '.': break;
                    case '#': blocked.Add((x, y)); break;
                    case 'F': fields.Add((x, y)); break;
                    case 'w': case 'h': case 'r': case 'f':
                        blocked.Add((x, y)); visual[(x, y)] = row[x]; break;
                    case 'B':
                        visual[(x, y)] = 'B'; break;   // bridge: open to the sim
                    default: throw new FormatException($"grid ({x},{y}): unknown character '{row[x]}'");
                }
        }

        // v2 mission sections after the grid: tagged entities and triggers.
        var units = new List<MapUnit>();
        var structures = new List<MapStructure>();
        var triggers = new List<MapTrigger>();
        for (int i = gridAt + h; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "unit":
                    units.Add(new MapUnit(
                        int.Parse(p[1], CultureInfo.InvariantCulture), int.Parse(p[2], CultureInfo.InvariantCulture),
                        int.Parse(p[3], CultureInfo.InvariantCulture), int.Parse(p[4], CultureInfo.InvariantCulture),
                        p.Length > 5 ? p[5] : ""));
                    break;
                case "structure":
                    structures.Add(new MapStructure(
                        int.Parse(p[1], CultureInfo.InvariantCulture), int.Parse(p[2], CultureInfo.InvariantCulture),
                        int.Parse(p[3], CultureInfo.InvariantCulture), int.Parse(p[4], CultureInfo.InvariantCulture),
                        p.Length > 5 ? p[5] : ""));
                    break;
                case "trigger":
                {
                    int arrow = Array.IndexOf(p, "->");
                    if (arrow < 2 || arrow >= p.Length - 1)
                        throw new FormatException($"line {i + 1}: trigger needs 'trigger WHEN... -> DO...'");
                    triggers.Add(new MapTrigger(p[1..arrow], p[(arrow + 1)..]));
                    break;
                }
                default: throw new FormatException($"line {i + 1}: unknown section '{p[0]}'");
            }
        }
        return new MapData
        {
            Width = w, Height = h, Blocked = blocked, Fields = fields, Starts = starts,
            Factions = factions, ShortGame = shortGame, Units = units, Structures = structures, Triggers = triggers, Visual = visual,
        };
    }

    /// <summary>Construct a world from this map: terrain applied, fields spawned; the scenario places starting forces at Starts.</summary>
    public World BuildWorld(ulong seed, int players) => BuildWorld(seed, players, out _);

    /// <summary>Full mission build: terrain, fields, tagged units and structures. Tags map to spawned entity ids for the trigger engine.
    /// configure (ADR-006) runs after the World is constructed and BEFORE any
    /// spawn, because the tagged units and structures below read the catalogue
    /// at spawn time: registering /data after BuildWorld would hand mission
    /// content compiled stats under an edited /data. No shipped caller that
    /// omits it changes behaviour.</summary>
    public World BuildWorld(ulong seed, int players, out Dictionary<string, List<int>> tags, Action<World>? configure = null)
    {
        var world = new World(seed, Width, Height, players) { ShortGameEnabled = ShortGame };
        configure?.Invoke(world);
        foreach (var (p, f) in Factions) world.SetFaction(p, f);
        var tagMap = new Dictionary<string, List<int>>();
        foreach (var (cx, cy) in Blocked) world.Map.SetBlocked(cx, cy, true);
        foreach (var (cx, cy) in Fields) world.SpawnFerriteField(Map.CellCentre(cx), Map.CellCentre(cy), StandardFieldAmount);
        void Tag(string tag, int id)
        {
            if (tag.Length == 0) return;
            if (!tagMap.TryGetValue(tag, out var list)) tagMap[tag] = list = new List<int>();
            list.Add(id);
        }
        foreach (var st in Structures)
        {
            int id = world.GetStructureType(st.StructType).Kind switch
            {
                EntityKind.PowerPlant => world.SpawnPowerPlant(st.Player, st.Ax, st.Ay),
                EntityKind.Factory => world.SpawnFactory(st.Player, st.Ax, st.Ay),
                EntityKind.Refinery => world.SpawnRefinery(st.Player, st.Ax, st.Ay),
                EntityKind.ConstructionYard => world.SpawnConstructionYard(st.Player, st.Ax, st.Ay),
                EntityKind.Turret => world.SpawnTurret(st.Player, st.Ax, st.Ay),
                EntityKind.Superweapon => world.SpawnSuperweapon(st.Player, st.Ax, st.Ay),
                EntityKind.VeilProjector => world.SpawnVeilProjector(st.Player, st.Ax, st.Ay),
                // Shipped missing (PROD-D7, fixed by TICKET-P5-PROD-02): every
                // spawnable kind needs an arm or a map carrying it throws here.
                EntityKind.ServiceDepot => world.SpawnServiceDepot(st.Player, st.Ax, st.Ay),
                EntityKind.Wall => world.SpawnWall(st.Player, st.Ax, st.Ay),
                _ => throw new FormatException($"map structure: unknown struct type {st.StructType}"),
            };
            Tag(st.Tag, id);
        }
        foreach (var u in Units)
        {
            var def = world.GetUnitType(u.UnitType);
            if (def.Cost <= 0) throw new FormatException($"map unit: unknown unit type {u.UnitType}");
            int id = def.Kind == EntityKind.Harvester
                ? world.SpawnHarvester(u.Player, Map.CellCentre(u.Cx), Map.CellCentre(u.Cy))
                : world.SpawnUnit(u.Player, Map.CellCentre(u.Cx), Map.CellCentre(u.Cy), def.Speed, def.Hp,
                    def.Armour, def.WeaponId, def.SightCells, def.Stealth, def.Detector, def.Veterancy, u.UnitType);
            Tag(u.Tag, id);
        }
        tags = tagMap;
        return world;
    }
}
