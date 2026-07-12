namespace Ferrostorm.Sim;

/// <summary>
/// TICKET-P2-DATA-01: loader for the /data YAML dialect. Deliberately a STRICT
/// SUBSET, not full YAML: flat mappings of scalars, inline lists [a, b], and
/// folded blocks (>). That is everything the schema permits, it keeps the sim
/// dependency-free (NuGet-less builds, ADR-002 spirit), and unsupported
/// constructs fail loudly with a line number rather than parsing wrongly.
/// Parsing is culture-invariant integer maths only; runs at match setup, never
/// per-tick.
/// </summary>
public static class DataLoader
{
    public sealed record UnitData(
        string Id, string Name, string Faction, int Tier, int Cost, int BuildTimeTicks,
        int Hp, ArmourClass Armour, Fix64 Speed, string Role,
        IReadOnlyList<string> WeaponIds, int SightRange, bool Stealth, bool Detector,
        IReadOnlyList<string> Prerequisites, bool VeterancyEnabled, string Notes);

    public static Dictionary<string, string> ParseFlatYaml(string text)
    {
        var map = new Dictionary<string, string>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimEnd();
            if (trimmed.Length == 0 || trimmed.TrimStart().StartsWith('#')) continue;
            if (line.StartsWith(' ') || line.StartsWith('\t'))
                throw new FormatException($"line {i + 1}: unexpected indentation outside a folded block");

            int colon = trimmed.IndexOf(':');
            if (colon <= 0) throw new FormatException($"line {i + 1}: expected 'key: value'");
            string key = trimmed[..colon].Trim();
            string value = trimmed[(colon + 1)..].Trim();

            if (value == ">")
            {
                // Folded block: consume following more-indented lines, join with spaces.
                var parts = new List<string>();
                while (i + 1 < lines.Length && (lines[i + 1].StartsWith("  ") || lines[i + 1].Trim().Length == 0))
                {
                    i++;
                    string p = lines[i].Trim();
                    if (p.Length > 0) parts.Add(p);
                }
                value = string.Join(' ', parts);
            }

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            if (!map.TryAdd(key, value))
                throw new FormatException($"line {i + 1}: duplicate key '{key}'");
        }
        return map;
    }

    public static List<string> ParseInlineList(string value)
    {
        value = value.Trim();
        if (value is "[]" or "") return new List<string>();
        if (value.Length < 2 || value[0] != '[' || value[^1] != ']')
            throw new FormatException($"expected inline list [a, b], got '{value}'");
        var items = new List<string>();
        foreach (var raw in value[1..^1].Split(','))
        {
            string item = raw.Trim();
            if (item.Length >= 2 &&
                ((item[0] == '"' && item[^1] == '"') || (item[0] == '\'' && item[^1] == '\'')))
                item = item[1..^1];
            if (item.Length > 0) items.Add(item);
        }
        return items;
    }

    private static int ReqInt(Dictionary<string, string> m, string key)
        => m.TryGetValue(key, out var v) && int.TryParse(v, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int r)
            ? r : throw new FormatException($"missing or non-integer field '{key}'");

    private static string ReqStr(Dictionary<string, string> m, string key)
        => m.TryGetValue(key, out var v) ? v : throw new FormatException($"missing field '{key}'");

    private static bool OptBool(Dictionary<string, string> m, string key, bool dflt)
        => m.TryGetValue(key, out var v) ? v == "true" || (v != "false" ? throw new FormatException($"'{key}' must be true/false") : false) : dflt;

    public static UnitData ParseUnit(string yamlText)
    {
        var m = ParseFlatYaml(yamlText);
        string id = ReqStr(m, "id");
        if (!(id.StartsWith("dir_") || id.StartsWith("sod_") || id.StartsWith("com_")))
            throw new FormatException($"id '{id}' violates the dir_/sod_/com_ prefix convention (CLAUDE.md)");

        var armour = ReqStr(m, "armour_class") switch
        {
            "none" => ArmourClass.None,
            "light" => ArmourClass.Light,
            "heavy" => ArmourClass.Heavy,
            "structure" => ArmourClass.Structure,
            var a => throw new FormatException($"unknown armour_class '{a}'"),
        };

        // Schema: speed is a plain integer, interpreted as hundredths of a cell
        // per tick (integer-encoded fixed point; e.g. 18 => 0.18 cells/tick).
        Fix64 speed = Fix64.FromFraction(ReqInt(m, "speed"), 100);

        return new UnitData(
            Id: id,
            Name: ReqStr(m, "name"),
            Faction: ReqStr(m, "faction"),
            Tier: ReqInt(m, "tier"),
            Cost: ReqInt(m, "cost"),
            BuildTimeTicks: ReqInt(m, "build_time_ticks"),
            Hp: ReqInt(m, "hp"),
            Armour: armour,
            Speed: speed,
            Role: ReqStr(m, "role"),
            WeaponIds: m.TryGetValue("weapon_ids", out var w) ? ParseInlineList(w) : new List<string>(),
            SightRange: m.TryGetValue("sight_range", out var sr) ? ReqInt(m, "sight_range") : 0,
            Stealth: OptBool(m, "stealth", false),
            Detector: OptBool(m, "detector", false),
            Prerequisites: m.TryGetValue("prerequisites", out var p) ? ParseInlineList(p) : new List<string>(),
            VeterancyEnabled: OptBool(m, "veterancy_enabled", true),
            Notes: m.TryGetValue("notes", out var n) ? n : "");
    }

    public static UnitData LoadUnitFile(string path) => ParseUnit(File.ReadAllText(path));
}

/// <summary>Bridges loaded /data unit definitions into the sim's producible catalogue (TICKET-P2-DATA-02).</summary>
public static class UnitCatalogue
{
    /// <summary>Weapon id registry; /data weapon files land in Phase 2, so the name map is compiled for now.</summary>
    public static int WeaponIdOf(string name) => name switch
    {
        "wpn_test_cannon" => 1,
        "wpn_test_rifle" => 2,
        "wpn_test_rocket" => 3,
        "wpn_howitzer" => 5,
        "wpn_bulwark_cannon" => 6,
        _ => throw new FormatException($"unknown weapon id '{name}'"),
    };

    public static World.UnitTypeDef ToTypeDef(DataLoader.UnitData u)
        => new(u.Cost, u.BuildTimeTicks, u.Hp, u.Armour,
               u.WeaponIds.Count > 0 ? WeaponIdOf(u.WeaponIds[0]) : 0, u.Speed,
               u.Role == "economy" ? EntityKind.Harvester : EntityKind.Unit,
               u.Stealth, u.Detector, u.VeterancyEnabled, u.SightRange,
               u.Faction == "directorate" ? World.FactionDirectorate
                   : u.Faction == "sodality" ? World.FactionSodality
                   : World.FactionCommon);
}
