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

    /// <summary>ADR-012: a ferrite field's regrowth tuning as authored in
    /// /data/fields, validated against schema.field.json. Global rate numbers
    /// only; the per-instance spawn amount (the regrowth cap) is map/scenario
    /// data carried on the entity, not on this definition.</summary>
    public sealed record FieldData(string Id, string Name, int RegrowAmount, int RegrowIntervalTicks, string Notes);

    /// <summary>
    /// ADR-012. Same flat-YAML and integer-only primitives as ParseUnit and
    /// ParseStructure, run at match setup and never per tick. regrow_amount 0 is
    /// legal and disables regrowth; regrow_interval_ticks must be at least 1 so
    /// the per-tick modulo the sim derives the schedule from never divides by
    /// zero.
    /// </summary>
    public static FieldData ParseField(string yamlText)
    {
        var m = ParseFlatYaml(yamlText);
        string id = ReqStr(m, "id");
        if (!(id.StartsWith("dir_") || id.StartsWith("sod_") || id.StartsWith("com_")))
            throw new FormatException($"id '{id}' violates the dir_/sod_/com_ prefix convention (CLAUDE.md)");

        int amount = ReqInt(m, "regrow_amount");
        int interval = ReqInt(m, "regrow_interval_ticks");
        if (interval < 1) throw new FormatException("regrow_interval_ticks must be at least 1");

        return new FieldData(
            Id: id,
            Name: ReqStr(m, "name"),
            RegrowAmount: amount,
            RegrowIntervalTicks: interval,
            Notes: m.TryGetValue("notes", out var n) ? n : "");
    }

    public static FieldData LoadFieldFile(string path) => ParseField(File.ReadAllText(path));
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

/// <summary>
/// ADR-006: register the whole /data catalogue into a world before tick 0.
/// This is the runner's own load path (Program.cs, the gate's reference
/// implementation) made callable, so the shipped client and the gate walk one
/// implementation instead of two that drift: a sorted ordinal walk (a
/// directory listing is not a source of truth), registration through the same
/// TypeIdOf and ToTypeDef maps the selftest proves, a duplicate claim refused,
/// and EVERY compiled type demanded, because a partial /data silently mixing
/// authored and compiled values is exactly the two-catalogue ambiguity the ADR
/// exists to end. The compiled catalogue is NOT a fallback here; it remains
/// the selftest's round-trip truth and the default for harness callers that
/// never touch disk.
/// Failures are messages, not crashes (ADR-006 commitment 2): a missing
/// directory says /data is missing and what was expected; a parse failure
/// names the file and carries the parser's own line number; a missing file
/// names the compiled type it was meant to provide.
/// </summary>
public static class CatalogueFiles
{
    public static void RegisterAll(World w, string unitsDir, string buildingsDir)
    {
        if (!Directory.Exists(unitsDir) || !Directory.Exists(buildingsDir))
            throw new IOException(
                $"/data is missing: expected {unitsDir} and {buildingsDir}. " +
                "Gameplay numbers live in /data (ADR-006) and a battle cannot start without them. " +
                "Restore the data directory beside the game and try again.");

        var seenUnits = new HashSet<int>();
        var unitFiles = Directory.GetFiles(unitsDir, "*.yaml");
        Array.Sort(unitFiles, StringComparer.Ordinal);
        foreach (var f in unitFiles)
        {
            try
            {
                var u = DataLoader.LoadUnitFile(f);
                int typeId = UnitCatalogue.TypeIdOf(u.Id);
                if (!seenUnits.Add(typeId)) throw new FormatException($"unit type {typeId} is claimed twice");
                w.RegisterUnitType(typeId, UnitCatalogue.ToTypeDef(u));
            }
            catch (FormatException e)
            {
                throw new FormatException($"{f}: {e.Message}", e);
            }
        }
        // Unit types are dense from 1 (doc 23 s4.1): walk the compiled
        // catalogue until it runs out, exactly as the selftest does.
        for (int t = 1; w.GetUnitType(t).Cost > 0; t++)
            if (!seenUnits.Contains(t))
                throw new FormatException(
                    $"{unitsDir}: no unit file provides compiled unit type {t}. " +
                    "The compiled catalogue is not a fallback (ADR-006), so the battle is refused rather than played on mixed numbers.");

        var seenStructs = new HashSet<int>();
        var structFiles = Directory.GetFiles(buildingsDir, "*.yaml");
        Array.Sort(structFiles, StringComparer.Ordinal);
        foreach (var f in structFiles)
        {
            try
            {
                var s = DataLoader.LoadStructureFile(f);
                int typeId = StructureCatalogue.TypeIdOf(s.Id);
                if (!seenStructs.Add(typeId)) throw new FormatException($"structure type {typeId} is claimed twice");
                w.RegisterStructureType(typeId, StructureCatalogue.ToTypeDef(s));
            }
            catch (FormatException e)
            {
                throw new FormatException($"{f}: {e.Message}", e);
            }
        }
        // Bounded by the catalogue's own constant, the gate skipped explicitly:
        // type 10 is ADR-005's reservation, with no def and no file.
        for (int t = 1; t <= World.MaxStructType; t++)
        {
            if (t == World.GateStructType) continue;
            if (!seenStructs.Contains(t))
                throw new FormatException(
                    $"{buildingsDir}: no building file provides compiled structure type {t}. " +
                    "The compiled catalogue is not a fallback (ADR-006), so the battle is refused rather than played on mixed numbers.");
        }
    }

    /// <summary>
    /// ADR-012: register the ferrite field regrowth tuning from /data/fields
    /// into a world before tick 0, the same opt-in load step RegisterAll is for
    /// the unit and structure catalogues. Kept separate rather than folded into
    /// RegisterAll's signature so the four existing RegisterAll call sites and
    /// their gates are untouched; a caller that never registers fields runs the
    /// compiled placeholder (World.DefaultRegrowAmount / DefaultRegrowIntervalTicks),
    /// which the selftest proves this file reproduces exactly. A sorted ordinal
    /// walk (a directory listing is not a source of truth) that demands the
    /// compiled ferrite field id, because a missing file must fail loudly rather
    /// than silently leave the placeholder standing for edited data.
    /// </summary>
    public static void RegisterFields(World w, string fieldsDir)
    {
        if (!Directory.Exists(fieldsDir))
            throw new IOException(
                $"/data is missing: expected {fieldsDir}. " +
                "Ferrite regrowth numbers live in /data (ADR-006/ADR-012) and a battle cannot start without them. " +
                "Restore the data directory beside the game and try again.");

        var files = Directory.GetFiles(fieldsDir, "*.yaml");
        Array.Sort(files, StringComparer.Ordinal);
        bool applied = false;
        foreach (var f in files)
        {
            try
            {
                var fd = DataLoader.LoadFieldFile(f);
                if (fd.Id != "com_ferrite_field") continue; // the only field type today
                w.ConfigureRegrowth(fd.RegrowAmount, fd.RegrowIntervalTicks);
                applied = true;
            }
            catch (FormatException e)
            {
                throw new FormatException($"{f}: {e.Message}", e);
            }
        }
        if (!applied)
            throw new FormatException(
                $"{fieldsDir}: no field file provides com_ferrite_field. " +
                "The compiled defaults are not a fallback (ADR-006), so the battle is refused rather than played on mixed numbers.");
    }
}
