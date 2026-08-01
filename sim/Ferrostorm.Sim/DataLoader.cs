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
        IReadOnlyList<string> Prerequisites, string ProducedAt, bool VeterancyEnabled, string Notes,
        bool Air = false,      // ADR-028
        int MaxAlive = 0);     // P7-11b: 0 means unlimited, which is every unit but the two heroes

    /// <summary>TICKET-P5-BD-06: a placeable structure as authored in /data/buildings, validated against schema.structure.json.</summary>
    public sealed record StructureData(
        string Id, string Name, string Faction, int Cost, int BuildTimeTicks,
        int Hp, int PowerSupply, int PowerDraw, int SightRange, int Footprint,
        IReadOnlyList<string> WeaponIds, IReadOnlyList<string> Prerequisites, string Notes,
        int MaxAlive = 0,          // P7-11c: 0 means unlimited, which is every building but the mine
        // Which build tab offers this building: "buildings", "defence" or
        // "none". REQUIRED by the schema on every file, so a new building
        // cannot arrive without an answer; "none" is the three map-placed
        // buildings and is checked against the sim's own queueability rule in
        // StructureCatalogue.ToTypeDef rather than trusted.
        string BuildTab = "none");

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

    /// <summary>The player-facing name a /data id derives to: the faction prefix
    /// cut, underscores to spaces, upper-cased. So com_flak_track reads FLAK
    /// TRACK and sod_veil_projector reads VEIL PROJECTOR.
    ///
    /// ONE derivation for both catalogues. It served units first (see
    /// UnitCatalogue.DisplayNameOf, and the seven unbuildable units whose
    /// hand-written label table taught it), and the structure catalogue reads
    /// it too: all fifteen labels the sidebar's two hand-kept building arrays
    /// carried are EXACTLY what this produces, with zero mismatches, which is
    /// what makes deriving those lists inert.</summary>
    public static string DisplayNameFromId(string id)
    {
        int cut = id.IndexOf('_');
        return (cut >= 0 ? id[(cut + 1)..] : id).Replace('_', ' ').ToUpperInvariant();
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
            Air: OptBool(m, "air", false),   // ADR-028: absent means ground, so every existing file is unchanged
            Detector: OptBool(m, "detector", false),
            Prerequisites: m.TryGetValue("prerequisites", out var p) ? ParseInlineList(p) : new List<string>(),
            // Required (TICKET-P5-PROD-03): the structure id whose queue
            // builds this unit. Role cannot carry it - "economy" is already
            // overloaded to mean EntityKind.Harvester - so it is its own key.
            ProducedAt: ReqStr(m, "produced_at"),
            VeterancyEnabled: OptBool(m, "veterancy_enabled", true),
            // P7-11b: the per-player cap on LIVING units of this type. Absent
            // means 0 and 0 means unlimited, so every file written before the
            // heroes is unchanged and the enforcement is a no-op for it.
            MaxAlive: m.TryGetValue("max_alive", out _) ? ReqInt(m, "max_alive") : 0,
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

        // Required, and validated here rather than defaulted, for the reason the
        // key exists at all: a building whose tab could be omitted is a building
        // that can arrive with no button, which is precisely what the two
        // hand-kept sidebar arrays kept doing.
        string buildTab = ReqStr(m, "build_tab");
        if (buildTab is not ("buildings" or "defence" or "none"))
            throw new FormatException($"unknown build_tab '{buildTab}'");

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
            // P7-11c: the per-player cap on LIVING buildings of this type, the
            // unit loader's own key above. Absent means 0 and 0 means
            // unlimited, so every file written before the mine is unchanged and
            // the enforcement is a no-op for it.
            MaxAlive: m.TryGetValue("max_alive", out _) ? ReqInt(m, "max_alive") : 0,
            BuildTab: buildTab,
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

    /// <summary>A weapon as authored in /data/weapons, validated against
    /// schema.weapon.json. Ranges arrive as integer hundredths of a cell (the
    /// speed convention) and leave as Fix64, so 150 is exactly 1.5 cells and no
    /// fractional value has to be spelled as a division the author cannot
    /// see.</summary>
    public sealed record WeaponData(
        string Id, string Name, Fix64 Range, int Damage, Warhead Warhead, int CooldownTicks,
        Fix64 MinRange, Fix64 SplashRadius, bool AntiAir, string Notes);

    /// <summary>
    /// The same flat-YAML and integer-only primitives as ParseUnit and
    /// ParseStructure, run at match setup and never per tick. Weapon ids carry
    /// the `wpn_` prefix rather than the faction prefixes: a weapon is not
    /// owned by a side, it is carried by whatever unit or building the
    /// catalogue arms with it, and two of the nine are already shared across
    /// factions. min_range and splash_radius default to 0, which is what every
    /// weapon but the howitzer wants, and cooldown_ticks must be at least 1 so
    /// that a rate of fire is a rate rather than every tick forever.
    /// </summary>
    public static WeaponData ParseWeapon(string yamlText)
    {
        var m = ParseFlatYaml(yamlText);
        string id = ReqStr(m, "id");
        if (!id.StartsWith("wpn_"))
            throw new FormatException($"id '{id}' violates the wpn_ prefix convention for weapons");

        var warhead = ReqStr(m, "warhead") switch
        {
            "anti_infantry" => Warhead.AntiInfantry,
            "anti_armour" => Warhead.AntiArmour,
            "anti_building" => Warhead.AntiBuilding,
            "omni" => Warhead.Omni,
            var wh => throw new FormatException($"unknown warhead '{wh}'"),
        };

        int cooldown = ReqInt(m, "cooldown_ticks");
        if (cooldown < 1) throw new FormatException("cooldown_ticks must be at least 1");

        // Hundredths of a cell, integer-encoded, exactly as `speed` is for a
        // unit: 400 is 4 cells and 150 is 1.5, both exact in Fix64.
        Fix64 OptCells(string key)
            => m.ContainsKey(key) ? Fix64.FromFraction(ReqInt(m, key), 100) : Fix64.Zero;

        return new WeaponData(
            Id: id,
            Name: ReqStr(m, "name"),
            Range: Fix64.FromFraction(ReqInt(m, "range"), 100),
            Damage: ReqInt(m, "damage"),
            Warhead: warhead,
            CooldownTicks: cooldown,
            MinRange: OptCells("min_range"),
            SplashRadius: OptCells("splash_radius"),
            AntiAir: OptBool(m, "anti_air", false),
            Notes: m.TryGetValue("notes", out var n) ? n : "");
    }

    public static WeaponData LoadWeaponFile(string path) => ParseWeapon(File.ReadAllText(path));

    /// <summary>A skirmish-commander tuning row as authored in /data/ai,
    /// validated against schema.ai.json. One record serves both families and
    /// only the effect differs; see <see cref="AiTuningDef"/> for which family
    /// owns which field.</summary>
    public sealed record AiTuningData(
        string Id, string Name, AiTuningKind Kind,
        int ActEvery, int WaveSize,
        int BeatNumerator, int BeatDenominator,
        int HarvestersPerRefinery, int StartingCreditHandicap, string Notes);

    /// <summary>The keys each family OWNS. Stated as data rather than as a run
    /// of ContainsKey tests so that a key added to one family is automatically
    /// forbidden in the other: a rule that named the keys one at a time would be
    /// missed by whoever adds the next one, which is this codebase's recurring
    /// defect shape.</summary>
    private static readonly string[] PersonalityKeys = { "act_every_ticks", "wave_size" };
    private static readonly string[] RungKeys =
        { "beat_numerator", "beat_denominator", "harvesters_per_refinery", "starting_credit_handicap" };

    /// <summary>
    /// The same flat-YAML and integer-only primitives as ParseUnit and
    /// ParseWeapon, run at match setup and never per tick. AI tuning ids carry
    /// the `ai_` prefix rather than a faction prefix: a commander's taste and a
    /// difficulty rung belong to no side.
    ///
    /// The `kind` discriminator decides which keys are REQUIRED and which are
    /// REFUSED, so a personality can never carry a credit handicap and a rung
    /// can never carry a wave size. That is DR-14's orthogonality enforced at
    /// the file rather than merely described in it: the review that produced the
    /// ladder found personality and difficulty conflated, and a schema that let
    /// one file carry both would invite the conflation straight back.
    /// </summary>
    public static AiTuningData ParseAiTuning(string yamlText)
    {
        var m = ParseFlatYaml(yamlText);
        string id = ReqStr(m, "id");
        if (!id.StartsWith("ai_"))
            throw new FormatException($"id '{id}' violates the ai_ prefix convention for AI tuning");

        var kind = ReqStr(m, "kind") switch
        {
            "personality" => AiTuningKind.Personality,
            "rung" => AiTuningKind.Rung,
            var k => throw new FormatException($"unknown AI tuning kind '{k}'"),
        };
        bool isPersonality = kind == AiTuningKind.Personality;
        foreach (string forbidden in isPersonality ? RungKeys : PersonalityKeys)
            if (m.ContainsKey(forbidden))
                throw new FormatException(
                    $"a {(isPersonality ? "personality" : "rung")} row must not author '{forbidden}': "
                    + "personality is a commander's taste and difficulty is its strength, and DR-14 keeps them orthogonal");

        int actEvery = 0, waveSize = 0, numerator = 1, denominator = 1, harvesters = 1, handicap = 0;
        if (isPersonality)
        {
            actEvery = ReqInt(m, "act_every_ticks");
            if (actEvery < 1) throw new FormatException("act_every_ticks must be at least 1; the commander takes Tick modulo it");
            waveSize = ReqInt(m, "wave_size");
            if (waveSize < 1) throw new FormatException("wave_size must be at least 1");
        }
        else
        {
            numerator = ReqInt(m, "beat_numerator");
            if (numerator < 1) throw new FormatException("beat_numerator must be at least 1");
            denominator = ReqInt(m, "beat_denominator");
            if (denominator < 1) throw new FormatException("beat_denominator must be at least 1; the commander divides by it");
            harvesters = ReqInt(m, "harvesters_per_refinery");
            if (harvesters < 1) throw new FormatException("harvesters_per_refinery must be at least 1");
            handicap = ReqInt(m, "starting_credit_handicap");
        }

        return new AiTuningData(
            Id: id,
            Name: ReqStr(m, "name"),
            Kind: kind,
            ActEvery: actEvery,
            WaveSize: waveSize,
            BeatNumerator: numerator,
            BeatDenominator: denominator,
            HarvestersPerRefinery: harvesters,
            StartingCreditHandicap: handicap,
            Notes: m.TryGetValue("notes", out var n) ? n : "");
    }

    public static AiTuningData LoadAiTuningFile(string path) => ParseAiTuning(File.ReadAllText(path));
}

/// <summary>Bridges loaded /data unit definitions into the sim's producible catalogue (TICKET-P2-DATA-02).</summary>
public static class UnitCatalogue
{
    /// <summary>Weapon id registry. The /data weapon files have landed, so this
    /// is now exactly what TypeIdOf is for units and structures: the file names
    /// the thing and this map names the number, which is the sim's wire and save
    /// identity and therefore stays code rather than data.</summary>
    public static int WeaponIdOf(string name) => name switch
    {
        // DR-17: these three shipped as wpn_test_cannon/rifle/rocket, prototype
        // names that outlived the prototype and were the only "test" left in a
        // catalogue a player's save file names. Renaming them is checksum-safe
        // BY CONSTRUCTION, not by luck: CatalogueChecksum hashes the
        // canonicalised defs and never the file bytes, and what it takes from a
        // weapon is the resolved integer WeaponId, which this map still returns
        // unchanged. Every existing save and replay therefore still loads.
        "wpn_tank_cannon" => 1,
        "wpn_service_rifle" => 2,
        "wpn_rocket_tube" => 3,
        // 4 is the turret's gun: no unit carries it, but /data/buildings does
        // (TICKET-P5-BD-06), and one weapon-name map is the point of this switch.
        "wpn_turret_gun" => 4,
        "wpn_howitzer" => 5,
        "wpn_bulwark_cannon" => 6,
        // The missing wpn_ prefix this entry used to carry and apologise for is
        // gone: dir_vanguard_car.yaml now authors wpn_vanguard_autocannon like
        // every sibling, so all seven weapon names share one shape and the
        // comment no longer has to explain an exception. Found during DR-17 and
        // filed rather than folded into it, because a naming ticket quietly
        // growing a seventh rename is how a small change stops being reviewable.
        // Checksum-safe on DR-17's argument: the checksum takes the resolved
        // integer this map returns, never the string it matched on.
        "wpn_vanguard_autocannon" => 7,
        "wpn_emplacement_gun" => 8,   // P7-2
        "wpn_flak_gun" => 9,          // ADR-028: the only AntiAir weapon in the game
        "wpn_commando_rifle" => 10,   // P7-11b: carried by both heroes, which are one unit with one property changed
        _ => throw new FormatException($"unknown weapon id '{name}'"),
    };

    /// <summary>Bridges a /data/weapons file into the sim's WeaponDef, mirroring
    /// ToTypeDef for units and structures. Every field crosses: a weapon whose
    /// authored number was parsed and then dropped would be the P7-1 defect in a
    /// third place.</summary>
    public static WeaponDef ToWeaponDef(DataLoader.WeaponData w)
        => new(w.Range, w.Damage, w.Warhead, w.CooldownTicks, w.MinRange, w.SplashRadius, w.AntiAir);

    /// <summary>Producible unit type ids: the file names the thing, this table
    /// names the number, and neither is free to drift alone - the selftest
    /// directory walk (TICKET-P5-PROD-02) proves every file against its
    /// compiled def through it, and RegisterUnits refuses a compiled type that
    /// no file provides, so the table is complete over the dense range by two
    /// gates rather than by hope. Unit types are dense from 1; struct type
    /// numbering is a DIFFERENT namespace (unit 11 is the engineer, struct 11
    /// is the barracks).
    ///
    /// ONE table read in BOTH directions. It used to be a switch expression,
    /// which can only be read forwards, and so the client could not ask what a
    /// type is CALLED: the build sidebar kept its own list of units and labels
    /// instead, and that list fell seven units behind the catalogue, which is
    /// how the transport, the flak track and both heroes came to exist in the
    /// sim with no way for a player to build them.</summary>
    private static readonly string[] Ids =
    {
        "",                           // index 0: no unit type, so the array is indexed BY type id
        "dir_cannon_tank",            // 1
        "com_rifle_squad",            // 2
        "com_rocket_squad",           // 3
        "com_harvester",              // 4
        "sod_shade_raider",           // 5
        "dir_sentinel_scout",         // 6
        "com_mcv",                    // 7
        "dir_howitzer",               // 8
        "sod_phantom_tank",           // 9
        "dir_bulwark_tank",           // 10
        "com_engineer",               // 11
        "dir_vanguard_car",           // 12
        "com_repair_vehicle",         // 13: ADR-019 (P6 Wave C2)
        "com_carrier",                // 14: P7-3, the transport
        "com_strike_flyer",           // 15: ADR-028 (P7-4)
        "com_flak_track",             // 16: ADR-028 clause 4, the answer
        "sod_infiltrator",            // 17: P7-7, GDD s7's "Infiltrator (steals intel/credits)"
        "sod_saboteur",               // 18: P7-11a, GDD s7's "Saboteur (disables buildings)"
        // P7-11b: GDD s7's two heroes, lines 62 and 64. Adjacent ids because
        // they are one unit authored twice, differing by faction and by stealth
        // alone - the P7-2b Bastion / Shroud Nest precedent.
        "dir_commando",               // 19
        "sod_shadow_commando",        // 20
    };

    /// <summary>The number for a name. Mirrors StructureCatalogue.TypeIdOf;
    /// throws on an unknown id rather than defaulting. A scan of a score of
    /// entries, run once per /data file at load, which is cheaper than a second
    /// lookup structure that can disagree with the table it was built from.</summary>
    public static int TypeIdOf(string id)
    {
        for (int t = 1; t < Ids.Length; t++) if (Ids[t] == id) return t;
        throw new FormatException($"unknown unit id '{id}'");
    }

    /// <summary>The name for a number, the same table read the other way. Throws
    /// on a type the table does not name, matching TypeIdOf: a blank name would
    /// reach a player as a blank button, which is the silent failure this whole
    /// table exists to stop.</summary>
    /// <summary>The player-facing name for a unit type, derived from its /data
    /// id: the faction prefix cut, underscores to spaces, upper-cased. So
    /// com_flak_track reads FLAK TRACK.
    ///
    /// ONE derivation, here, because there were two hand-maintained name tables
    /// in the client and BOTH had fallen behind the catalogue: the sidebar's
    /// stopped at thirteen units of twenty, so seven were unbuildable, and
    /// SkirmishLive's stopped at the same thirteen, so every unit above it read
    /// as "UNIT" in the readout and in toasts. The second table's own comment
    /// records this happening once before and being fixed by adding entries,
    /// which treats the symptom: a hand-maintained list of a thing the
    /// catalogue already knows will fall behind again.
    ///
    /// Verified inert when it replaced the sidebar's table: all thirteen
    /// hand-written labels are EXACTLY what this produces, with zero
    /// mismatches.
    ///
    /// The derivation itself now lives in DataLoader.DisplayNameFromId, shared
    /// with the structure catalogue, which had the same defect one namespace
    /// over: the sidebar's two BUILDING arrays were hand-kept as well, and the
    /// mine had to be added to one of them by hand. A third copy of a
    /// four-line string transform would have been the same mistake in the
    /// small.</summary>
    public static string DisplayNameOf(int typeId) => DataLoader.DisplayNameFromId(IdOf(typeId));

    public static string IdOf(int typeId)
        => typeId > 0 && typeId < Ids.Length
            ? Ids[typeId]
            : throw new FormatException($"unknown unit type {typeId}");

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
               StructureCatalogue.TypeIdOf(u.ProducedAt),
               u.Air,          // ADR-028
               u.MaxAlive);    // P7-11b
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
    /// <summary>
    /// Structure type ids as ratified in ADR-005 (9 is the wall; 10 is the
    /// deferred gate and has no file; new types number from 11 upward per doc 23
    /// s4.1). Indexed BY type id, with an empty string where a number is not a
    /// structure.
    ///
    /// ONE table read in BOTH directions, the shape UnitCatalogue.Ids already
    /// has and for the identical reason. It used to be a switch expression,
    /// which can only be read forwards, and so nothing could ask what a
    /// BUILDING is called - reachabilitygate's own comment records the gap
    /// ("/data has no id-to-name map for buildings the way UnitCatalogue.IdOf is
    /// one for units") and named its buildings by EntityKind instead, while the
    /// sidebar kept two hand-written arrays of labels that fell behind the
    /// catalogue exactly as the unit array had.
    /// </summary>
    private static readonly string[] Ids =
    {
        "",                       // index 0: no structure type
        "com_power_plant",        // 1
        "com_factory",            // 2
        "com_refinery",           // 3
        "com_construction_yard",  // 4
        "dir_turret",             // 5
        "dir_superweapon",        // 6
        "sod_veil_projector",     // 7
        "com_service_depot",      // 8
        "com_wall",               // 9
        // 10 is GateStructType, reserved by ADR-005 clause 6 for the deferred
        // wall gates: no def, no file and no name. The hole is why this table is
        // indexed rather than dense, and why P7-2 numbered the Emplacement from
        // 15 rather than taking the gap.
        "",                       // 10
        "com_barracks",           // 11
        "com_radar_uplink",       // 12
        "com_outpost",            // 13: ADR-021 (P6 Wave C4), map-placed, never built
        "com_bridge",             // 14: ADR-025 (P6 Wave C6a), map-placed, never built
        "com_emplacement",        // 15: P7-2
        "com_airfield",           // 16: ADR-028
        "dir_bastion",            // 17: P7-2b, the Directorate's defence
        "sod_shroud_nest",        // 18: P7-2b, the Sodality's
        "com_mine",               // 19: P7-11c, built and placed like any other building
    };

    /// <summary>The number for a name. Throws on an unknown id rather than
    /// defaulting, matching WeaponIdOf and UnitCatalogue.TypeIdOf.</summary>
    public static int TypeIdOf(string id)
    {
        for (int t = 1; t < Ids.Length; t++) if (Ids[t].Length > 0 && Ids[t] == id) return t;
        throw new FormatException($"unknown structure id '{id}'");
    }

    /// <summary>The name for a number, the same table read the other way.
    /// Throws on a type the table does not name - including the reserved gate -
    /// for UnitCatalogue.IdOf's reason: a blank name would reach a player as a
    /// blank button.</summary>
    public static string IdOf(int typeId)
        => typeId > 0 && typeId < Ids.Length && Ids[typeId].Length > 0
            ? Ids[typeId]
            : throw new FormatException($"unknown structure type {typeId}");

    /// <summary>The player-facing name for a structure type, derived from its
    /// /data id by the one derivation the unit catalogue uses. The build
    /// sidebar's two hand-kept label arrays are what this replaces.</summary>
    public static string DisplayNameOf(int typeId) => DataLoader.DisplayNameFromId(IdOf(typeId));

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
        15 => EntityKind.Emplacement,   // P7-2
        16 => EntityKind.Airfield,      // ADR-028
        17 => EntityKind.Bastion,       // P7-2b
        18 => EntityKind.Emplacement,   // P7-2b: a nest is an emplacement that hides
        11 => EntityKind.Barracks,
        12 => EntityKind.RadarUplink,
        13 => EntityKind.Outpost,   // ADR-021
        14 => EntityKind.Bridge,    // ADR-025
        19 => EntityKind.Mine,      // P7-11c
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
    {
        var kind = KindOf(s.Id);
        var tab = s.BuildTab switch
        {
            "buildings" => BuildTab.Buildings,
            "defence" => BuildTab.Defence,
            "none" => BuildTab.None,
            var t => throw new FormatException($"unknown build_tab '{t}' on '{s.Id}'"),
        };
        // THE TAB AND THE SIM MUST AGREE ABOUT WHO CAN HAVE THIS BUILDING, and
        // the agreement is checked rather than restated. "No tab" is a second
        // way of saying "no player may order it", and the first way is the sim's
        // own refusal in BuildStructure - BuildTicks <= 0 - with the Wall kind as
        // its single exception, because a barrier is bought through the placement
        // path instead. reachabilitygate holds the same equivalence from the
        // other side, naming the three map-placed kinds and failing if /data ever
        // makes one buildable. Left unchecked, "which buildings are offered"
        // would become a second list free to drift from "which buildings a
        // player can order", and a button that orders nothing is worse than an
        // absent one.
        bool queueable = s.BuildTimeTicks > 0 || kind == EntityKind.Wall;
        if (queueable && tab == BuildTab.None)
            throw new FormatException(
                $"'{s.Id}' authors build_tab: none and yet a Construction Yard will queue it, so it would be "
                + "buildable with no button anywhere. Give it a tab, or take away its build time.");
        if (!queueable && tab != BuildTab.None)
            throw new FormatException(
                $"'{s.Id}' authors build_tab: {s.BuildTab} and has no build time, so no Construction Yard will "
                + "ever queue it and the button would order nothing. Map-placed buildings carry build_tab: none.");
        return new(s.Cost, kind, s.BuildTimeTicks, s.Hp, s.PowerSupply, s.PowerDraw,
               s.SightRange, s.Footprint,
               s.WeaponIds.Count > 0 ? UnitCatalogue.WeaponIdOf(s.WeaponIds[0]) : 0,
               PrereqIds(s.Prerequisites),
               // P7-1: carry the faction the file already declares. It was
               // parsed and validated here and then dropped, which is what made
               // every building's `faction:` line decoration.
               s.Faction switch
               {
                   "directorate" => World.FactionDirectorate,
                   "sodality" => World.FactionSodality,
                   _ => World.FactionCommon,
               },
               // P7-11c: and the cap crosses too. A max_alive parsed and then
               // dropped would be this project's most-repeated defect in a new
               // place, and a loud one: the file would advertise a limit the
               // sim never applied.
               s.MaxAlive,
               // ...and the tab, which is the only field on the def the sim
               // never reads. It crosses for the same reason all the same: a
               // key authored, validated and dropped is the P7-1 defect, and
               // here it would leave the sidebar guessing again.
               tab);
    }
}

/// <summary>
/// Bridges /data/ai definitions into the sim's AI tuning table, in the shape
/// UnitCatalogue and StructureCatalogue are for their kinds: the file names the
/// thing, the compiled map names the number, and neither is free to drift alone.
/// The tuning id is the value the catalogue checksum carries, so it stays code
/// rather than data for the same reason a weapon id does.
/// </summary>
public static class AiCatalogue
{
    /// <summary>Tuning ids, resolved to the constants beside the compiled table
    /// so the numbers are stated once. Throws on an unknown id rather than
    /// defaulting, matching WeaponIdOf and StructureCatalogue.TypeIdOf.</summary>
    public static int TuningIdOf(string id) => id switch
    {
        "ai_standard" => AiTuning.StandardId,
        "ai_rusher" => AiTuning.RusherId,
        "ai_turtle" => AiTuning.TurtleId,
        "ai_easy" => AiTuning.EasyId,
        "ai_normal" => AiTuning.NormalId,
        "ai_hard" => AiTuning.HardId,
        "ai_brutal" => AiTuning.BrutalId,
        _ => throw new FormatException($"unknown AI tuning id '{id}'"),
    };

    /// <summary>Every field crosses: a tuning number that was parsed and then
    /// dropped would be the P7-1 defect in a fourth place, and here it would be
    /// worse than decoration - a file a player edits, a checksum that moves, and
    /// a commander that plays the compiled numbers regardless.</summary>
    public static AiTuningDef ToTuningDef(DataLoader.AiTuningData a)
        => new(a.Kind, a.ActEvery, a.WaveSize, a.BeatNumerator, a.BeatDenominator,
               a.HarvestersPerRefinery, a.StartingCreditHandicap);
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
    /// <summary>
    /// The ONE description of what a /data subdirectory is: either a catalogue
    /// kind, carrying the step that loads it, or a directory known to hold
    /// something that is not defs at all. Both the registration walk in
    /// <see cref="RegisterAll(World, string)"/> and the unrecognised-directory
    /// guard beside it read this table, and that is the point of it. Fields and
    /// then weapons each shipped as an EXTRA call every caller had to remember
    /// beside RegisterAll, so a caller that forgot one got a partial catalogue
    /// and no error at all, silently playing compiled numbers. Two lists would
    /// be that same defect wearing a second coat, so there is exactly one.
    ///
    /// Array order is REGISTRATION order: units, structures, fields, weapons,
    /// AI tuning, which is the order the per-kind call sites used before they collapsed
    /// into one call. The checksum walks ids in sorted order and so cannot see
    /// this, but the order still decides which failure a broken /data reports
    /// first, so it is stated here rather than left to chance.
    /// </summary>
    private static readonly (string Dir, Action<World, string>? Register)[] DataDirs =
    {
        ("units",     RegisterUnits),
        ("buildings", RegisterStructures),
        ("fields",    RegisterFields),
        ("weapons",   RegisterWeapons),
        // Promoted from a null row the day the skirmish commander's tuning moved
        // out of SkirmishAI.cs. It was listed as "reserved for authored AI
        // tuning" and held nothing, which is exactly the state this table exists
        // to make impossible for longer than one wave.
        ("ai",        RegisterAiTuning),
        // Known, and deliberately NOT a catalogue kind. Listed rather than
        // silently skipped, so the guard can tell a directory that holds no
        // defs from one nobody has ever heard of.
        ("maps",      null),   // .fmap terrain, loaded per match by MapData
        ("missions",  null),   // campaign mission files, loaded by the mission runner
        ("campaign",  null),   // the campaign manifest and its briefings
    };

    /// <summary>
    /// The single honest entry point: register EVERY catalogue kind in /data
    /// into a world before tick 0, from the /data root itself. One call, all of
    /// it, so a new kind joins every caller at once instead of being an extra
    /// line each of them has to remember.
    ///
    /// It also refuses a /data holding a directory this table does not know,
    /// naming it. The day somebody adds data/newkind/ full of yaml, the build
    /// fails and asks to be wired in, rather than parsing, validating and then
    /// silently ignoring the lot, which is the P7-1 defect shape.
    ///
    /// A missing directory behaves exactly as it did when each kind was called
    /// by hand: the per-kind step throws its own readable IOException naming
    /// what it wanted. A world nobody calls this on keeps the compiled numbers
    /// it was seeded with, which is what keeps the bare-World harness scenarios
    /// green.
    /// </summary>
    public static void RegisterAll(World w, string dataRoot)
    {
        // Guarded, because a dataRoot that is not there at all is the missing
        // /data case: it must reach the per-kind step below and produce that
        // step's message, not a directory-enumeration failure from the guard.
        if (Directory.Exists(dataRoot))
        {
            var subs = Directory.GetDirectories(dataRoot);
            Array.Sort(subs, StringComparer.Ordinal);
            foreach (var sub in subs)
            {
                string name = Path.GetFileName(sub);
                bool known = false;
                foreach (var (dir, _) in DataDirs)
                    if (string.Equals(dir, name, StringComparison.Ordinal)) known = true;
                if (!known)
                    throw new FormatException(
                        $"{sub}: unrecognised /data directory '{name}'. " +
                        "Every directory under /data is either a catalogue kind this loader registers or one recorded " +
                        "as holding no defs; an unknown one would be authored, validated and then silently ignored. " +
                        "Add it to CatalogueFiles.DataDirs, with a registration step if it holds defs and null if it does not.");
            }
        }

        foreach (var (dir, register) in DataDirs)
            register?.Invoke(w, Path.Combine(dataRoot, dir));
    }

    /// <summary>
    /// Units and structures only, from two explicit directories. Named for what
    /// it does after RegisterAll took its old name: it registered two of the
    /// four kinds while calling itself "all", which is exactly how callers came
    /// to be missing fields and weapons. Kept for the error-path assertions
    /// that feed it scratch directories to prove bad input is refused.
    /// </summary>
    public static void RegisterUnitsAndStructures(World w, string unitsDir, string buildingsDir)
    {
        if (!Directory.Exists(unitsDir) || !Directory.Exists(buildingsDir))
            throw new IOException(
                $"/data is missing: expected {unitsDir} and {buildingsDir}. " +
                "Gameplay numbers live in /data (ADR-006) and a battle cannot start without them. " +
                "Restore the data directory beside the game and try again.");

        RegisterUnits(w, unitsDir);
        RegisterStructures(w, buildingsDir);
    }

    private static void RegisterUnits(World w, string unitsDir)
    {
        if (!Directory.Exists(unitsDir))
            throw new IOException(
                $"/data is missing: expected {unitsDir}. " +
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
    }

    private static void RegisterStructures(World w, string buildingsDir)
    {
        if (!Directory.Exists(buildingsDir))
            throw new IOException(
                $"/data is missing: expected {buildingsDir}. " +
                "Gameplay numbers live in /data (ADR-006) and a battle cannot start without them. " +
                "Restore the data directory beside the game and try again.");

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
    /// into a world before tick 0. It is a row in DataDirs like every other
    /// kind, so RegisterAll loads it; it stays public only for the selftest,
    /// which registers this one kind on its own to prove the file reproduces
    /// the compiled twin. Keeping it separate from RegisterAll's signature so
    /// that existing call sites were untouched is what left every caller
    /// carrying an extra line, and is exactly what the table ended. A world
    /// nobody registers fields into runs the compiled placeholder
    /// (World.DefaultRegrowAmount / DefaultRegrowIntervalTicks), which the
    /// selftest proves this file reproduces exactly. A sorted ordinal walk (a
    /// directory listing is not a source of truth) that demands the compiled
    /// ferrite field id, because a missing file must fail loudly rather than
    /// silently leave the placeholder standing for edited data.
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

    /// <summary>
    /// Register the authored weapon table from /data/weapons into a world
    /// before tick 0, the same opt-in load step RegisterFields is for ferrite
    /// regrowth and on the same terms: a sorted ordinal walk (a directory
    /// listing is not a source of truth), registration through the WeaponIdOf
    /// map the gate proves, a duplicate id refused, and EVERY compiled weapon
    /// id demanded, because a partial /data silently mixing authored and
    /// compiled guns is the two-catalogue ambiguity ADR-006 exists to end.
    ///
    /// This is the step that makes the numbers real. World seeds its weapon
    /// table from the compiled reference so a bare harness world still plays,
    /// but a MATCH registers these files over the top and CombatSystem reads
    /// the world's table, so editing a range here changes the game.
    /// </summary>
    public static void RegisterWeapons(World w, string weaponsDir)
    {
        if (!Directory.Exists(weaponsDir))
            throw new IOException(
                $"/data is missing: expected {weaponsDir}. " +
                "Weapon numbers live in /data (ADR-006) and a battle cannot start without them. " +
                "Restore the data directory beside the game and try again.");

        var seen = new HashSet<int>();
        var files = Directory.GetFiles(weaponsDir, "*.yaml");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var f in files)
        {
            try
            {
                var wd = DataLoader.LoadWeaponFile(f);
                int id = UnitCatalogue.WeaponIdOf(wd.Id);
                if (!seen.Add(id)) throw new FormatException($"weapon id {id} is claimed twice");
                w.RegisterWeaponType(id, UnitCatalogue.ToWeaponDef(wd));
            }
            catch (FormatException e)
            {
                throw new FormatException($"{f}: {e.Message}", e);
            }
        }
        // Weapon ids are dense from 1 to the compiled bound; 0 is None and is
        // not a weapon, so it is not demanded.
        for (int id = 1; id <= Weapons.MaxWeaponId; id++)
            if (!seen.Contains(id))
                throw new FormatException(
                    $"{weaponsDir}: no weapon file provides compiled weapon id {id}. " +
                    "The compiled table is not a fallback (ADR-006), so the battle is refused rather than played on mixed numbers.");
    }

    /// <summary>
    /// Register the authored skirmish-commander tuning from /data/ai into a
    /// world before tick 0, on exactly the terms RegisterWeapons is on: a sorted
    /// ordinal walk (a directory listing is not a source of truth), registration
    /// through the AiCatalogue.TuningIdOf map the gate proves, a duplicate id
    /// refused, and EVERY compiled id demanded, because a partial /data silently
    /// mixing authored and compiled commanders is the two-catalogue ambiguity
    /// ADR-006 exists to end.
    ///
    /// This kind carries a safety argument the others do not. The commander's
    /// numbers were compiled, so two LAN peers agreed on them BY CONSTRUCTION;
    /// authoring them creates a desync vector where peers with different files
    /// issue different AI commands. World folds the registered table into
    /// CatalogueChecksum, which the LAN hello, saves and replays already compare
    /// and refuse a mismatch on, so the new vector is closed by the mechanism
    /// that was already there rather than by trust.
    /// </summary>
    public static void RegisterAiTuning(World w, string aiDir)
    {
        if (!Directory.Exists(aiDir))
            throw new IOException(
                $"/data is missing: expected {aiDir}. " +
                "The skirmish commander's tuning lives in /data (ADR-006) and a battle cannot start without it. " +
                "Restore the data directory beside the game and try again.");

        var seen = new HashSet<int>();
        var files = Directory.GetFiles(aiDir, "*.yaml");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var f in files)
        {
            try
            {
                var a = DataLoader.LoadAiTuningFile(f);
                int id = AiCatalogue.TuningIdOf(a.Id);
                if (!seen.Add(id)) throw new FormatException($"AI tuning id {id} is claimed twice");
                w.RegisterAiTuning(id, AiCatalogue.ToTuningDef(a));
            }
            catch (FormatException e)
            {
                throw new FormatException($"{f}: {e.Message}", e);
            }
        }
        // Tuning ids are dense from 1 to the compiled bound; there is no id 0,
        // because there is no such thing as an unspecified commander.
        for (int id = 1; id <= AiTuning.MaxTuningId; id++)
            if (!seen.Contains(id))
                throw new FormatException(
                    $"{aiDir}: no AI file provides compiled tuning id {id}. " +
                    "The compiled table is not a fallback (ADR-006), so the battle is refused rather than played on mixed numbers.");
    }
}
