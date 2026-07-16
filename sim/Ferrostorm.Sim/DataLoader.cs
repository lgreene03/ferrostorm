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
        IReadOnlyList<string> Prerequisites, string ProducedAt, bool VeterancyEnabled, string Notes);

    /// <summary>TICKET-P5-BD-06: a placeable structure as authored in /data/buildings, validated against schema.structure.json.</summary>
    public sealed record StructureData(
        string Id, string Name, string Faction, int Cost, int BuildTimeTicks,
        int Hp, int PowerSupply, int PowerDraw, int SightRange, int Footprint,
        IReadOnlyList<string> WeaponIds, IReadOnlyList<string> Prerequisites, string Notes);

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
            // Required (TICKET-P5-PROD-03): the structure id whose queue
            // builds this unit. Role cannot carry it - "economy" is already
            // overloaded to mean EntityKind.Harvester - so it is its own key.
            ProducedAt: ReqStr(m, "produced_at"),
            VeterancyEnabled: OptBool(m, "veterancy_enabled", true),
            Notes: m.TryGetValue("notes", out var n) ? n : "");
    }

    public static UnitData LoadUnitFile(string path) => ParseUnit(File.ReadAllText(path));

    /// <summary>
    /// TICKET-P5-BD-06. Deliberately the same shape as ParseUnit, reusing the
    /// same flat-YAML and inline-list primitives: integer maths only, culture
    /// invariant, run at match setup and never per tick. build_time_ticks 0 and
    /// sight_range 0 are legal here where they are not for units (the
    /// MCV-deployed yard and the barrier both depend on the former; FogSystem's
    /// zero-sight skip depends on the latter), so neither is defaulted away.
    /// </summary>
    public static StructureData ParseStructure(string yamlText)
    {
        var m = ParseFlatYaml(yamlText);
        string id = ReqStr(m, "id");
        if (!(id.StartsWith("dir_") || id.StartsWith("sod_") || id.StartsWith("com_")))
            throw new FormatException($"id '{id}' violates the dir_/sod_/com_ prefix convention (CLAUDE.md)");

        string faction = ReqStr(m, "faction");
        if (faction is not ("directorate" or "sodality" or "common"))
            throw new FormatException($"unknown faction '{faction}'");

        int footprint = ReqInt(m, "footprint");
        if (footprint < 1) throw new FormatException("footprint must be at least 1 cell per side");

        return new StructureData(
            Id: id,
            Name: ReqStr(m, "name"),
            Faction: faction,
            Cost: ReqInt(m, "cost"),
            BuildTimeTicks: ReqInt(m, "build_time_ticks"),
            Hp: ReqInt(m, "hp"),
            PowerSupply: ReqInt(m, "power_supply"),
            PowerDraw: ReqInt(m, "power_draw"),
            SightRange: ReqInt(m, "sight_range"),
            Footprint: footprint,
            WeaponIds: m.TryGetValue("weapon_ids", out var w) ? ParseInlineList(w) : new List<string>(),
            Prerequisites: m.TryGetValue("prerequisites", out var p) ? ParseInlineList(p) : new List<string>(),
            Notes: m.TryGetValue("notes", out var n) ? n : "");
    }

    public static StructureData LoadStructureFile(string path) => ParseStructure(File.ReadAllText(path));
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
        // 4 is the turret's gun: no unit carries it, but /data/buildings does
        // (TICKET-P5-BD-06), and one weapon-name map is the point of this switch.
        "wpn_turret_gun" => 4,
        "wpn_howitzer" => 5,
        "wpn_bulwark_cannon" => 6,
        // As authored in dir_vanguard_car.yaml (TICKET-P4-SLICE-01) - note the
        // missing wpn_ prefix, which is the file's inconsistency, not this
        // map's. Unmapped until TICKET-P5-PROD-02 made the unit round-trip a
        // directory walk: the one unit file the selftest never loaded was
        // hiding the one weapon name the map never learned (PROD-D9's point).
        "vanguard_autocannon" => 7,
        _ => throw new FormatException($"unknown weapon id '{name}'"),
    };

    /// <summary>Producible unit type ids: the file names the thing, this map
    /// names the number, and neither is free to drift alone - the selftest
    /// directory walk (TICKET-P5-PROD-02) proves every file against its
    /// compiled def through this map. Mirrors StructureCatalogue.TypeIdOf;
    /// throws on an unknown id rather than defaulting. Unit types are dense
    /// 1 through 12; struct type numbering is a DIFFERENT namespace (unit 11
    /// is the engineer, struct 11 is the barracks).</summary>
    public static int TypeIdOf(string id) => id switch
    {
        "dir_cannon_tank" => 1,
        "com_rifle_squad" => 2,
        "com_rocket_squad" => 3,
        "com_harvester" => 4,
        "sod_shade_raider" => 5,
        "dir_sentinel_scout" => 6,
        "com_mcv" => 7,
        "dir_howitzer" => 8,
        "sod_phantom_tank" => 9,
        "dir_bulwark_tank" => 10,
        "com_engineer" => 11,
        "dir_vanguard_car" => 12,
        _ => throw new FormatException($"unknown unit id '{id}'"),
    };

    public static World.UnitTypeDef ToTypeDef(DataLoader.UnitData u)
        => new(u.Cost, u.BuildTimeTicks, u.Hp, u.Armour,
               u.WeaponIds.Count > 0 ? WeaponIdOf(u.WeaponIds[0]) : 0, u.Speed,
               u.Role == "economy" ? EntityKind.Harvester : EntityKind.Unit,
               u.Stealth, u.Detector, u.VeterancyEnabled, u.SightRange,
               u.Faction == "directorate" ? World.FactionDirectorate
                   : u.Faction == "sodality" ? World.FactionSodality
                   : World.FactionCommon,
               // Carried, not read (TICKET-P5-PROD-03): prerequisites and the
               // producer link ride into the def so the tech-tree tickets gate
               // on values that already round-trip; nothing branches on them yet.
               StructureCatalogue.PrereqIds(u.Prerequisites),
               StructureCatalogue.TypeIdOf(u.ProducedAt));
}

/// <summary>
/// Bridges /data/buildings definitions into the sim's placeable catalogue
/// (TICKET-P5-BD-06). The structure type id is the sim's wire and save
/// identity, so it stays a compiled map keyed by the /data id, exactly as
/// WeaponIdOf is for weapons: the file names the thing, the map names the
/// number, and neither is free to drift alone.
/// </summary>
public static class StructureCatalogue
{
    /// <summary>Structure type ids as ratified in ADR-005 (9 is the wall; 10 is the deferred gate and has no file; new types number from 11 upward per doc 23 s4.1). Throws on an unknown id rather than defaulting, matching WeaponIdOf.</summary>
    public static int TypeIdOf(string id) => id switch
    {
        "com_power_plant" => 1,
        "com_factory" => 2,
        "com_refinery" => 3,
        "com_construction_yard" => 4,
        "dir_turret" => 5,
        "dir_superweapon" => 6,
        "sod_veil_projector" => 7,
        "com_service_depot" => 8,
        "com_wall" => 9,
        "com_barracks" => 11,
        "com_radar_uplink" => 12,
        _ => throw new FormatException($"unknown structure id '{id}'"),
    };

    /// <summary>The EntityKind each structure type spawns as. Kind is a save-format value (World writes (byte)e.Kind), so like the type id it is code, not data.</summary>
    public static EntityKind KindOf(string id) => TypeIdOf(id) switch
    {
        1 => EntityKind.PowerPlant,
        2 => EntityKind.Factory,
        3 => EntityKind.Refinery,
        4 => EntityKind.ConstructionYard,
        5 => EntityKind.Turret,
        6 => EntityKind.Superweapon,
        7 => EntityKind.VeilProjector,
        8 => EntityKind.ServiceDepot,
        9 => EntityKind.Wall,
        11 => EntityKind.Barracks,
        12 => EntityKind.RadarUplink,
        _ => throw new FormatException($"no EntityKind for structure id '{id}'"),
    };

    /// <summary>Prerequisite ids resolved to structure type numbers - a unit's
    /// and a structure's prerequisites are both structure ids, so one resolver
    /// serves both catalogues. Empty becomes null: both mean "none" and the
    /// def equality treats them identically.</summary>
    public static int[]? PrereqIds(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return null;
        var r = new int[ids.Count];
        for (int i = 0; i < ids.Count; i++) r[i] = TypeIdOf(ids[i]);
        return r;
    }

    /// <summary>Prerequisites now ride into the def (TICKET-P5-PROD-03) instead
    /// of being parsed and dropped; nothing reads them until the tech-tree
    /// tickets land, which keeps this wave hash-neutral while ending the era
    /// of the loader silently discarding authored data.</summary>
    public static World.StructureTypeDef ToTypeDef(DataLoader.StructureData s)
        => new(s.Cost, KindOf(s.Id), s.BuildTimeTicks, s.Hp, s.PowerSupply, s.PowerDraw,
               s.SightRange, s.Footprint,
               s.WeaponIds.Count > 0 ? UnitCatalogue.WeaponIdOf(s.WeaponIds[0]) : 0,
               PrereqIds(s.Prerequisites));
}
