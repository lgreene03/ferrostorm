namespace Ferrostorm.Sim;

/// <summary>Player/AI intent enters the sim exclusively as commands scheduled for a tick (TDD s3).</summary>
public enum CommandType : byte
{
    None = 0,
    Move = 2,      // direct point move (no pathfinding)
    Stop = 3,
    PathMove = 4,  // flow-field pathed move to X/Y (TICKET-P1-04)
    Harvest = 5,   // EntityId (harvester) gathers from AuxId (ferrite field)
    Attack = 6,    // EntityId attacks AuxId explicitly (else auto-acquire)
    Produce = 7,   // EntityId (factory) queues unit type AuxId (TICKET-P2-SIM-03)
    AttackMove = 8,      // move to X/Y engaging anything met en route (TICKET-P2-UX-01)
    CancelProduce = 9,   // EntityId (factory) cancels queue index AuxId; head refunds paid credits
    PlaceStructure = 10, // player places structure type AuxId with footprint anchor at cell (X, Y)
    SellStructure = 11,  // EntityId (own structure) sold for half cost; footprint unblocks
    BuildStructure = 12, // EntityId (Construction Yard) queues structure type AuxId (TICKET-P2-SIM-05)
    Repair = 13,         // EntityId (own structure) toggles repair: 2 hp/tick for 1 credit/tick
    Deploy = 14,         // EntityId (MCV) unpacks into a Construction Yard on its own cell
    LaunchSuper = 15,    // EntityId (charged superweapon) fires at map position X/Y (TICKET-P2-SIM-15)
    SetRally = 16,       // EntityId (own producing structure) rally point at X/Y; AuxId == -1 clears (ADR-007)
}

public readonly struct Command
{
    public readonly int Tick;
    public readonly int PlayerId;
    public readonly CommandType Type;
    public readonly int EntityId;
    public readonly int AuxId;
    public readonly Fix64 X;
    public readonly Fix64 Y;
    /// <summary>Shift-queued (TICKET-P2-SIM-19): appended to the entity's order queue instead of replacing its current order.</summary>
    public readonly bool Queued;

    public Command(int tick, int playerId, CommandType type, int entityId, Fix64 x, Fix64 y, int auxId = -1, bool queued = false)
    { Tick = tick; PlayerId = playerId; Type = type; EntityId = entityId; X = x; Y = y; AuxId = auxId; Queued = queued; }
}

// APPEND ONLY. The state hash stores (int)e.Kind and the save format writes
// (byte)e.Kind, so appending a value is invisible to both for every existing
// kind; renumbering one silently rewrites every golden hash and every replay.
// RadarUplink (struct type 12) is spawnable since ADR-008; Barracks (struct
// type 11) is spawnable and produces the infantry since ADR-009; Airfield,
// Emplacement, Bastion and Outpost are reservations only (doc 23 s4.1),
// taken because reserving is free and a later collision with a saved byte is
// silent and fatal.
public enum EntityKind : byte { Unit = 0, Harvester = 1, Refinery = 2, FerriteField = 3, PowerPlant = 4, Factory = 5, ConstructionYard = 6, Turret = 7, Superweapon = 8, VeilProjector = 9, ServiceDepot = 10, Wall = 11, Barracks = 12, RadarUplink = 13, Airfield = 14, Emplacement = 15, Bastion = 16, Outpost = 17 }
public enum HarvestState : byte { Idle = 0, ToField = 1, Loading = 2, ToRefinery = 3, Unloading = 4 }

/// <summary>
/// Entity state as plain structs in a list with fixed iteration order (TDD s3).
/// One struct carries all component fields at Phase 1 scale; a proper SoA/ECS
/// split is queued for when entity variety grows (Architect note in ADR queue).
/// </summary>
public struct Entity
{
    public int Id;
    public bool Alive;
    public int PlayerId;         // -1 = neutral (ferrite fields)
    public EntityKind Kind;
    public Fix64 X, Y;

    // Movement
    public Fix64 TargetX, TargetY;
    public bool Moving;
    public bool UseFlow;
    public Fix64 Speed;          // cells per tick

    // Combat
    public int Hp;
    public ArmourClass Armour;
    public int WeaponId;         // 0 = unarmed
    public int Cooldown;
    public int ExplicitTarget;   // -1 = auto-acquire
    public Fix64 Sight;          // cells

    // Economy
    public HarvestState HState;
    public int Carry;            // credits worth of ferrite aboard
    public int StateTicks;       // countdown within Loading/Unloading
    public int FieldId;          // assigned ferrite field
    public int RefineryId;       // assigned refinery
    public int FerriteAmount;    // for FerriteField entities

    // Power and production (TICKET-P2-SIM-02/03)
    public int PowerSupply;      // structures only
    public int PowerDraw;
    public int BuildProgress;    // percent-ticks toward current queue head

    // Stall-arrival tracking (TICKET-P2-SIM-01b): position at tick start and
    // consecutive ticks of negligible progress while pathing.
    public Fix64 PrevX, PrevY;
    public int StallTicks;

    // Pay-as-you-build: credits already drained toward the current queue head.
    public int BuildPaid;

    // Attack-move: engage anything met en route, resume toward the ordered
    // point when the guns fall silent (TICKET-P2-UX-01). The ordered
    // destination lives in AMoveX/Y because TargetX/Y doubles as the working
    // waypoint during sight-range pursuit.
    public bool AMove;
    public Fix64 AMoveX, AMoveY;

    // Structure catalogue id (for sell-back refunds); 0 for non-structures.
    public int StructType;

    // Repair (TICKET-P2-SIM-08): heals 2 hp per tick for 1 credit per tick.
    public int MaxHp;
    public bool Repairing;

    // Sidebar flow (TICKET-P2-SIM-05): a Construction Yard that has finished
    // building holds the completed structure type here until it is placed.
    public int ReadyStructure;

    // Stealth and detection (TICKET-P2-SIM-09). A stealthed entity can only
    // be targeted by players whose bit is set in DetectedMask (recomputed
    // each tick from detector coverage) or while RevealTicks > 0 (firing
    // breaks stealth for everyone).
    public bool Stealth;
    public bool Detector;
    public int RevealTicks;
    public byte DetectedMask;

    // Veterancy (TICKET-P2-SIM-10): kills promote; veterans hit for 5/4,
    // elites for 6/4 and self-repair. Kill credit goes to the first attacker
    // in deterministic processing order on the killing tick.
    public int Kills;
    public int Rank;
    public bool VetEnabled;

    // Producible catalogue id (0 = scenario-spawned); the MCV deploy check
    // and future per-type logic key off this.
    public int UnitType;

    // Superweapon (TICKET-P2-SIM-15): countdown to ready (pauses on low
    // power), and the scheduled strike once launched.
    public int ChargeTicks;
    public int StrikeTicks;
    public Fix64 StrikeX, StrikeY;

    // Area cloak (TICKET-P2-SIM-18): recomputed each tick from powered veil
    // projector coverage; combined with Stealth for targetability.
    public bool FieldCloaked;

    // Rally in the sim (ADR-007), appended after FieldCloaked, never inserted:
    // hash order and save order are this declaration order. Unset is
    // HasRally false with RallyX/RallyY zero (SetRally's clear restores
    // exactly that state, so cleared and never-rallied serialise identically).
    // Departing marks a production exit move in flight: it suppresses the
    // crowd-arrival shortcut so a rally (or the default exit) within 4 cells
    // of the spawn cell still clears the factory mouth, and it lifts the tick
    // the unit leaves its spawn cell or the walk ends. Units only; the
    // shortcut is kind-gated, so harvesters never consult it.
    public Fix64 RallyX, RallyY;
    public bool HasRally;
    public bool Departing;
}

/// <summary>
/// The deterministic world. Fixed-timestep at 15 Hz nominal; the tick is the
/// only unit of time. Identical seed + identical command stream => identical
/// state hash on every platform (TDD s1). Systems run in the fixed TDD s3
/// order: Movement -> Combat -> Harvesting -> Fog.
/// </summary>
/// <summary>
/// TICKET-P2-SIM-13: deterministic gameplay events emitted during a tick.
/// The presentation layer consumes these for sound and effects instead of
/// diffing snapshots; they are derived from hashed state and are therefore
/// identical on every client, but are not themselves part of the hash.
/// A = primary entity id, B = context (target id, rank, player, type),
/// C = the producing structure's entity index on ProductionComplete and -1
/// everywhere else (TICKET-P5-BD-14). Before C existed the client had to guess
/// which factory made a unit by position proximity, which cross-wired two
/// factories parked within four cells of each other.
/// </summary>
public enum GameEventType : byte
{
    Fired = 1,              // A attacker, B target
    Died = 2,               // A entity
    StructurePlaced = 3,    // A new structure
    ProductionComplete = 4, // A new unit (or B=struct type held ready at CY A)
    Promoted = 5,           // A entity, B new rank
    Deployed = 6,           // A consumed MCV, B new Construction Yard
    PlayerEliminated = 7,   // B player
    SuperweaponReady = 8,   // A superweapon structure
    SuperweaponLaunched = 9,// A superweapon, impact at (X, Y) after the warning
    SuperweaponImpact = 10, // detonation at (X, Y)
    Captured = 11,          // A structure, B new owner
}
public readonly record struct GameEvent(GameEventType Type, int A, int B, Fix64 X = default, Fix64 Y = default, int C = -1);

public sealed partial class World
{
    public const int TicksPerSecond = 15;

    /// <summary>Winner player id once decided, -1 while the match is live (TICKET-P2-SIM-12). The sim keeps stepping after; presentation decides what "game over" means.</summary>
    public int Winner { get; private set; } = -1;

    // Faction identity (TICKET-P3-FAC-01): 0 = Directorate, 1 = Sodality.
    // Production of faction-locked units and structures is refused for the
    // wrong side - asymmetry is enforced, not advisory.
    public const int FactionDirectorate = 0;
    public const int FactionSodality = 1;
    private readonly byte[] _playerFaction;
    public void SetFaction(int player, int faction) => _playerFaction[player] = (byte)faction;
    public int FactionOf(int player) => _playerFaction[player];

    private readonly List<GameEvent> _events = new();
    /// <summary>Events emitted by the tick that just ran; cleared at the start of every Step.</summary>
    public IReadOnlyList<GameEvent> Events => _events;
    private readonly bool[] _eliminatedAnnounced;

    // Economy constants (GDD s4). /data wiring is a Phase 2 ticket.
    public const int HarvesterCapacity = 700;
    public const int LoadPerTick = 10;                       // 70 ticks to fill
    public const int UnloadTicks = 8 * TicksPerSecond;       // refinery processes a load in 8s

    private readonly List<Entity> _entities = new();
    private readonly DeterministicRandom _rng;
    private readonly FlowFieldCache _flow = new();
    public readonly Map Map;
    public int Tick { get; private set; } // set by Step and by Load

    private readonly int _players;
    private readonly long[] _credits;
    private readonly ulong[][] _visible; // per player, bitset over cells (this tick)
    private readonly ulong[][] _explored; // per player, shroud-lifted cells (ever seen)

    public World(ulong seed, int mapWidth = 64, int mapHeight = 64, int players = 2)
    {
        _rng = new DeterministicRandom(seed);
        Map = new Map(mapWidth, mapHeight);
        _players = players;
        _credits = new long[players];
        int words = (mapWidth * mapHeight + 63) / 64;
        _eliminatedAnnounced = new bool[players];
        _playerFaction = new byte[players]; // everyone Directorate until told otherwise
        _visible = new ulong[players][];
        _explored = new ulong[players][];
        for (int p = 0; p < players; p++) { _visible[p] = new ulong[words]; _explored[p] = new ulong[words]; }
    }

    public int EntityCount => _entities.Count;
    public IReadOnlyList<Entity> Entities => _entities;
    public DeterministicRandom Rng => _rng;
    public long Credits(int player) => _credits[player];
    public bool IsVisible(int player, int cx, int cy)
    { int c = Map.CellIndex(cx, cy); return (_visible[player][c >> 6] & (1UL << (c & 63))) != 0; }
    public bool IsExplored(int player, int cx, int cy)
    { int c = Map.CellIndex(cx, cy); return (_explored[player][c >> 6] & (1UL << (c & 63))) != 0; }

    private int Add(in Entity e) { _entities.Add(e); return e.Id; }

    public int SpawnUnit(int player, Fix64 x, Fix64 y, Fix64 speed, int hp, ArmourClass armour, int weaponId, int sightCells = 5,
        bool stealth = false, bool detector = false, bool veterancy = true, int unitType = 0)
        => Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Unit,
            X = x, Y = y, TargetX = x, TargetY = y, Speed = speed,
            Hp = hp, MaxHp = hp, Armour = armour, WeaponId = weaponId, ExplicitTarget = -1,
            Sight = Fix64.FromInt(sightCells), FieldId = -1, RefineryId = -1,
            Stealth = stealth, Detector = detector, VetEnabled = veterancy, UnitType = unitType,
        });

    public int SpawnHarvester(int player, Fix64 x, Fix64 y)
        => Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Harvester,
            X = x, Y = y, TargetX = x, TargetY = y, Speed = Fix64.FromFraction(1, 5),
            Hp = 700, MaxHp = 700, Armour = ArmourClass.Heavy, WeaponId = 0, ExplicitTarget = -1,
            Sight = Fix64.FromInt(5), FieldId = -1, RefineryId = -1,
        });

    public int SpawnRefinery(int player, int ax, int ay)
    {
        var def = GetStructureType(3);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Refinery,
            X = x, Y = y, TargetX = x, TargetY = y,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, WeaponId = def.WeaponId, ExplicitTarget = -1, StructType = 3,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    public int SpawnFerriteField(Fix64 x, Fix64 y, int amount)
        => Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = -1, Kind = EntityKind.FerriteField,
            X = x, Y = y, TargetX = x, TargetY = y,
            Hp = 1, MaxHp = 1, Armour = ArmourClass.Structure, WeaponId = 0, ExplicitTarget = -1,
            FerriteAmount = amount, FieldId = -1, RefineryId = -1,
        });

    // Producible unit types (TICKET-P2-SIM-03). Compiled defaults serve also as the
    // reference values for the /data loader round-trip test (TICKET-P2-DATA-02);
    // matches can overwrite or extend the catalogue before tick 0 via
    // RegisterUnitType. The catalogue is static config, like weapons, and is
    // therefore not part of the state hash.
    public const int FactionCommon = 2;
    /// <summary>
    /// Prereqs (structure type ids that must stand alive) and ProducedAt (the
    /// structure type whose queue builds this unit; 2 = the factory) are
    /// CARRIED, NOT READ this wave (TICKET-P5-PROD-03): no gate branches on
    /// either until the tech-tree tickets land. Trailing and defaulted so the
    /// twelve compiled entries keep compiling. Equality is declared by hand
    /// because the synthesized record comparison would test the Prereqs ARRAY
    /// REFERENCE, and the selftest round-trip (/data file == compiled def)
    /// compares defs built on both sides of that reference.
    /// </summary>
    public readonly record struct UnitTypeDef(int Cost, int BuildTicks, int Hp, ArmourClass Armour, int WeaponId, Fix64 Speed,
        EntityKind Kind = EntityKind.Unit, bool Stealth = false, bool Detector = false, bool Veterancy = true, int SightCells = 5,
        int Faction = FactionCommon, int[]? Prereqs = null, int ProducedAt = 2)
    {
        public bool Equals(UnitTypeDef other)
            => Cost == other.Cost && BuildTicks == other.BuildTicks && Hp == other.Hp
            && Armour == other.Armour && WeaponId == other.WeaponId && Speed == other.Speed
            && Kind == other.Kind && Stealth == other.Stealth && Detector == other.Detector
            && Veterancy == other.Veterancy && SightCells == other.SightCells && Faction == other.Faction
            && ProducedAt == other.ProducedAt && PrereqsEqual(Prereqs, other.Prereqs);
        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(Cost); h.Add(BuildTicks); h.Add(Hp); h.Add(Armour); h.Add(WeaponId); h.Add(Speed);
            h.Add(Kind); h.Add(Stealth); h.Add(Detector); h.Add(Veterancy); h.Add(SightCells);
            h.Add(Faction); h.Add(ProducedAt);
            if (Prereqs != null) foreach (int p in Prereqs) h.Add(p);
            return h.ToHashCode();
        }
    }

    /// <summary>Value comparison for a def's prerequisite list: null and empty
    /// both mean "no prerequisites" and compare equal; order is significant
    /// (the lists are authored, not computed).</summary>
    private static bool PrereqsEqual(int[]? a, int[]? b)
    {
        int an = a?.Length ?? 0, bn = b?.Length ?? 0;
        if (an != bn) return false;
        for (int i = 0; i < an; i++) if (a![i] != b![i]) return false;
        return true;
    }
    private readonly Dictionary<int, UnitTypeDef> _unitTypes = new()
    {
        // Prereqs/ProducedAt mirror the /data files verbatim (the round-trip
        // selftest proves it) and are carried unread this wave: the infantry
        // trio names the barracks (struct type 11) as producer, everything
        // else takes the factory default. The tech-tree cleanup of the
        // prerequisite lists is a Game Designer ticket, not this one.
        { 1, new UnitTypeDef(600, 150, 300, ArmourClass.Heavy, 1, Fix64.FromFraction(1, 5), Faction: FactionDirectorate) },   // dir_cannon_tank
        { 2, new UnitTypeDef(200, 75, 100, ArmourClass.None, 2, Fix64.FromFraction(1, 4), ProducedAt: 11) },     // com_rifle_squad (common)
        { 3, new UnitTypeDef(300, 100, 80, ArmourClass.None, 3, Fix64.FromFraction(11, 50), ProducedAt: 11) },   // com_rocket_squad (common: the counter-triangle is shared, identity lives in the specials)
        { 4, new UnitTypeDef(1400, 300, 700, ArmourClass.Heavy, 0, Fix64.FromFraction(9, 50), EntityKind.Harvester, Veterancy: false, Prereqs: new[] { 3 }) }, // com_harvester
        { 5, new UnitTypeDef(500, 100, 150, ArmourClass.Light, 2, Fix64.FromFraction(7, 25), Stealth: true, Faction: FactionSodality) },            // sod_shade_raider
        { 6, new UnitTypeDef(400, 75, 90, ArmourClass.None, 0, Fix64.FromFraction(3, 10), Detector: true, SightCells: 7, Faction: FactionDirectorate) }, // dir_sentinel_scout
        { 7, new UnitTypeDef(3000, 400, 600, ArmourClass.Heavy, 0, Fix64.FromFraction(3, 20), Veterancy: false, Prereqs: new[] { 2 }) },        // com_mcv
        { 8, new UnitTypeDef(900, 200, 160, ArmourClass.Light, 5, Fix64.FromFraction(3, 20), SightCells: 7, Faction: FactionDirectorate, Prereqs: new[] { 2 }) },            // dir_howitzer
        // Signature units (TICKET-P3-FAC-02): the personality pieces.
        // Phantom: the Sodality stealth tank - rockets from nowhere.
        { 9, new UnitTypeDef(900, 210, 200, ArmourClass.Light, 3, Fix64.FromFraction(6, 25), Stealth: true, Faction: FactionSodality, Prereqs: new[] { 2 }) },  // sod_phantom_tank
        // Bulwark: the Directorate wall that walks. Slow, vast, undeniable.
        { 10, new UnitTypeDef(1600, 350, 550, ArmourClass.Heavy, 6, Fix64.FromFraction(3, 25), Faction: FactionDirectorate, Prereqs: new[] { 2 }) },            // dir_bulwark_tank
        // Engineer: captures enemy structures on contact; consumed by the act.
        { 11, new UnitTypeDef(500, 120, 60, ArmourClass.None, 0, Fix64.FromFraction(1, 5), Veterancy: false, ProducedAt: 11) },                           // com_engineer
        // Vanguard: the Directorate harasser - the raider trade, armour for
        // stealth (TICKET-P4-SLICE-01, the full-pipeline vertical slice).
        { 12, new UnitTypeDef(450, 100, 150, ArmourClass.Light, 7, Fix64.FromFraction(8, 25), SightCells: 6, Faction: FactionDirectorate) }, // dir_vanguard_car
    };
    public UnitTypeDef GetUnitType(int typeId) => _unitTypes.TryGetValue(typeId, out var d) ? d : default;
    public void RegisterUnitType(int typeId, UnitTypeDef def)
    {
        if (Tick != 0) throw new InvalidOperationException("catalogue is fixed once the match starts");
        _unitTypes[typeId] = def;
    }

    private readonly Dictionary<int, List<int>> _queues = new(); // factory id -> queued type ids (keyed access only)

    /// <summary>Test and scenario scripting only: overwrite an entity wholesale (e.g. pre-damaging units for a repair test).</summary>
    public void SetEntityForTest(int id, in Entity e) => _entities[id] = e;

    /// <summary>Scenario/trigger credit grant (campaign scripting, starting funds).</summary>
    public void GrantCredits(int player, long amount) => _credits[player] += amount;

    /// <summary>Scripted mission objectives can decide the match ahead of the short-game rule (TICKET-P2-SIM-20). First declaration wins; hashed like any winner.</summary>
    public void DeclareWinner(int player)
    {
        if (Winner >= 0 || (uint)player >= (uint)_players) return;
        Winner = player;
    }

    /// <summary>For scenario/campaign terrain scripting that edits Map directly: cached routes must be discarded.</summary>
    public void InvalidateFlowCache() => _flow.Clear();

    /// <summary>Queued build count for a factory (UI and AI read this; 0 for anything else).</summary>
    public int QueueLength(int factoryId)
        => _queues.TryGetValue(factoryId, out var q) ? q.Count : 0;

    /// <summary>Read-only view of a producer's queued type ids for the sidebar
    /// (M3, doc 18). Pure accessor: no state is touched, nothing hashed.</summary>
    public IReadOnlyList<int> QueueContents(int factoryId)
        => _queues.TryGetValue(factoryId, out var q) ? q : System.Array.Empty<int>();

    // Placeable structures (TICKET-P2-SIM-04, footprints generalised by
    // TICKET-P5-DEF-03 per ADR-005). Footprint size is a per-type property:
    // types 1 to 8 occupy the 2x2 default, barriers occupy 1x1. The entity
    // position is always the footprint centre (anchor + size/2), so the anchor
    // is recoverable as CellOf(X) - size/2 for any size. The classic
    // build-in-sidebar-then-place flow is TICKET-P2-SIM-05; barriers keep the
    // upfront-cost model instead, deducting on placement with no ready slot
    // (ADR-005 clause 3).
    /// <summary>
    /// TICKET-P5-BD-06: every field here is authored in /data/buildings and the
    /// compiled defaults below exist to be proved equal to those files by the
    /// selftest round-trip, not to be edited (CLAUDE.md forbids hand-editing
    /// stats in code). Hp/PowerSupply/PowerDraw/SightCells were previously
    /// literals scattered across the eight Spawn methods; Footprint and WeaponId
    /// are carried so that placement and armament read the def rather than a
    /// second switch. Like the unit catalogue this is static config and is not
    /// part of the state hash.
    /// </summary>
    public readonly record struct StructureTypeDef(int Cost, EntityKind Kind, int BuildTicks,
        int Hp = 0, int PowerSupply = 0, int PowerDraw = 0, int SightCells = 0,
        int Footprint = FootprintSize, int WeaponId = 0, int[]? Prereqs = null)
    {
        // Hand-declared for the same reason as UnitTypeDef: the synthesized
        // comparison would test the Prereqs array reference and quietly fail
        // the /data round-trip on logically identical defs. Prereqs is carried
        // unread this wave (TICKET-P5-PROD-03); no build gate consults it yet.
        public bool Equals(StructureTypeDef other)
            => Cost == other.Cost && Kind == other.Kind && BuildTicks == other.BuildTicks
            && Hp == other.Hp && PowerSupply == other.PowerSupply && PowerDraw == other.PowerDraw
            && SightCells == other.SightCells && Footprint == other.Footprint
            && WeaponId == other.WeaponId && PrereqsEqual(Prereqs, other.Prereqs);
        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(Cost); h.Add(Kind); h.Add(BuildTicks); h.Add(Hp); h.Add(PowerSupply);
            h.Add(PowerDraw); h.Add(SightCells); h.Add(Footprint); h.Add(WeaponId);
            if (Prereqs != null) foreach (int p in Prereqs) h.Add(p);
            return h.ToHashCode();
        }
    }

    /// <summary>The compiled reference catalogue: the values a /data/buildings file must reproduce exactly. Static so that callers with no World (the Balance tool) can price a structure; a live match must read GetStructureType instead, which honours RegisterStructureType.</summary>
    public static StructureTypeDef DefaultStructureType(int typeId) => typeId switch
    {
        1 => new StructureTypeDef(300, EntityKind.PowerPlant, 100, Hp: 150, PowerSupply: 100, SightCells: 4),
        2 => new StructureTypeDef(2000, EntityKind.Factory, 300, Hp: 1500, PowerDraw: 40, SightCells: 5),
        // Honest draws (ADR-008 clause 3, BD-07 rebased; A11 co-sign recorded
        // in the ADR): the refinery 0 to 40, the yard 0 to 20, the superweapon
        // 100 to 150. The opening base is then EXACTLY 100 supply against 100
        // draw - the zero-margin boundary the ADR accepts explicitly.
        3 => new StructureTypeDef(2000, EntityKind.Refinery, 300, Hp: 2000, PowerDraw: 40, SightCells: 6),
        4 => new StructureTypeDef(3000, EntityKind.ConstructionYard, 0, Hp: 3000, PowerDraw: 20, SightCells: 6), // MCV-deployed, never queued
        5 => new StructureTypeDef(600, EntityKind.Turret, 150, Hp: 400, PowerDraw: 20, SightCells: 6, WeaponId: 4),
        6 => new StructureTypeDef(4000, EntityKind.Superweapon, 600, Hp: 1200, PowerDraw: 150, SightCells: 4),
        7 => new StructureTypeDef(1500, EntityKind.VeilProjector, 250, Hp: 900, PowerDraw: 60, SightCells: 6),
        8 => new StructureTypeDef(1200, EntityKind.ServiceDepot, 200, Hp: 1000, PowerDraw: 30, SightCells: 4),
        // Barrier segment (ADR-005). BuildTicks 0 keeps it out of the Construction
        // Yard queue by the existing guard in BuildStructure, exactly as type 4 is
        // kept out: barriers are bought upfront at placement instead. SightCells 0
        // keeps 80 segments per player out of the fog pass entirely.
        9 => new StructureTypeDef(100, EntityKind.Wall, 0, Hp: 500, SightCells: 0, Footprint: 1),
        // 10 is the gate: RESERVED by ADR-005 clause 6, deliberately no def.
        // Barracks (TICKET-P5-PROD-03, numbers from doc 23 s4.3): cheap and
        // early, because that is what makes an infantry rush a real strategy
        // rather than a factory afterthought. Struct type 11 is the barracks;
        // UNIT type 11 is the engineer - different namespaces, no clash.
        // Buildable since ADR-009 clause 5: it is the producer of every unit
        // whose ProducedAt names struct type 11 (the infantry).
        11 => new StructureTypeDef(500, EntityKind.Barracks, 100, Hp: 800, PowerDraw: 20, SightCells: 5, Prereqs: new[] { 1 }),
        // Radar uplink (doc 23 s4.2 numbers): buildable since ADR-008 clause 4.
        // The client's minimap is lit only while a living uplink stands with
        // supply covering draw; the sim itself gates nothing on it yet (the
        // prerequisite tree is ADR-009's wave - the Prereqs value is carried,
        // not read).
        12 => new StructureTypeDef(900, EntityKind.RadarUplink, 150, Hp: 1000, PowerDraw: 80, SightCells: 10, Prereqs: new[] { 2 }),
        _ => default,
    };

    /// <summary>
    /// The highest COMPILED structure type (TICKET-P5-PROD-02): the bound for
    /// every loop that enumerates the catalogue. EntityKind reservations above
    /// it (airfield, emplacement, bastion, outpost) have numbers but no defs
    /// and no /data files, so they must stay OUTSIDE this bound until
    /// implemented. Enumerating loops must also skip GateStructType: the gate
    /// is inside the bound but has no def and no file (ADR-005 clause 6).
    /// </summary>
    public const int MaxStructType = 12;

    private readonly Dictionary<int, StructureTypeDef> _structTypes = SeedStructureTypes();
    private static Dictionary<int, StructureTypeDef> SeedStructureTypes()
    {
        var d = new Dictionary<int, StructureTypeDef>();
        for (int t = 1; t <= MaxStructType; t++)
        {
            if (t == GateStructType) continue; // reserved: no def to seed (ADR-005)
            d[t] = DefaultStructureType(t);
        }
        return d;
    }

    /// <summary>The live catalogue. Unknown types return default, whose Cost 0 is what every command handler already tests to refuse them.</summary>
    public StructureTypeDef GetStructureType(int typeId) => _structTypes.TryGetValue(typeId, out var d) ? d : default;

    /// <summary>Match setup may overwrite or extend the catalogue before tick 0, mirroring RegisterUnitType. After tick 0 the catalogue is frozen: a mid-match change would be a silent replay divergence.</summary>
    public void RegisterStructureType(int typeId, StructureTypeDef def)
    {
        if (Tick != 0) throw new InvalidOperationException("catalogue is fixed once the match starts");
        _structTypes[typeId] = def;
    }

    /// <summary>
    /// ADR-006 commitment 1: the catalogue checksum. FNV-1a in the sim's own
    /// StateHash idiom over the CANONICALISED registered defs, never over file
    /// bytes: unit types first, then structure types, each walked in ascending
    /// type id (dictionary iteration order must never leak into an artefact),
    /// each def contributing every field in declaration order with the
    /// prerequisite list length-prefixed. Two worlds agreeing here are playing
    /// the same numbers; the LAN hello, saves and replays carry this value and
    /// refuse a mismatch before tick 0. Deliberately NOT part of
    /// ComputeStateHash: hashing the catalogue into state would move all 24
    /// goldens for zero behavioural change (the ADR's rejected alternative).
    /// </summary>
    public ulong CatalogueChecksum
    {
        get
        {
            var h = StateHash.Create();
            var unitIds = new List<int>(_unitTypes.Keys);
            unitIds.Sort();
            h.Add(unitIds.Count);
            foreach (int id in unitIds)
            {
                var d = _unitTypes[id];
                h.Add(id); h.Add(d.Cost); h.Add(d.BuildTicks); h.Add(d.Hp); h.Add((int)d.Armour);
                h.Add(d.WeaponId); h.Add(d.Speed); h.Add((int)d.Kind); h.Add(d.Stealth); h.Add(d.Detector);
                h.Add(d.Veterancy); h.Add(d.SightCells); h.Add(d.Faction); h.Add(d.ProducedAt);
                h.Add(d.Prereqs?.Length ?? 0);
                if (d.Prereqs != null) foreach (int p in d.Prereqs) h.Add(p);
            }
            var structIds = new List<int>(_structTypes.Keys);
            structIds.Sort();
            h.Add(structIds.Count);
            foreach (int id in structIds)
            {
                var d = _structTypes[id];
                h.Add(id); h.Add(d.Cost); h.Add((int)d.Kind); h.Add(d.BuildTicks); h.Add(d.Hp);
                h.Add(d.PowerSupply); h.Add(d.PowerDraw); h.Add(d.SightCells); h.Add(d.Footprint);
                h.Add(d.WeaponId);
                h.Add(d.Prereqs?.Length ?? 0);
                if (d.Prereqs != null) foreach (int p in d.Prereqs) h.Add(p);
            }
            return h.Value;
        }
    }

    /// <summary>The default footprint: every structure type except a barrier is 2x2.</summary>
    public const int FootprintSize = 2;

    /// <summary>ADR-005 clause 6: type 10 is the gate, reserved and deferred. It has no def and no file, so its 1x1 footprint cannot be read from the catalogue and is stated here instead of being silently lost.</summary>
    public const int GateStructType = 10;

    /// <summary>Cells per side of a structure type's square footprint (ADR-005), read from the def. Barriers are 1x1; everything else, including an unknown type (Footprint 0 on the default def), takes the 2x2 default that the placement path has always assumed.</summary>
    public int FootprintOf(int structType)
    {
        if (structType == GateStructType) return 1;
        int f = GetStructureType(structType).Footprint;
        return f > 0 ? f : FootprintSize;
    }

    /// <summary>
    /// Recover a structure's footprint anchor from its centre: CellOf(centre)
    /// minus size/2 in integer division (TICKET-P5-BASE-01). Exact for every
    /// size schema.structure.json permits (1 to 4): a 1x1 centre is anchor+0.5
    /// so CellOf gives anchor - 0; a 2x2 centre is anchor+1, minus 1; a 3x3
    /// centre is anchor+1.5 so CellOf gives anchor+1, minus 1; a 4x4 centre is
    /// anchor+2, minus 2. For sizes 1 and 2 - the only values any /data file
    /// uses - this is bit-identical to the previous "- (size - 1)" expression,
    /// which the byte-identical goldens prove; for 3 and 4 the old expression
    /// was wrong by one, which ADR-005 line 76 names as silent and fatal.
    /// </summary>
    public int AnchorOf(Fix64 centre, int structType) => Map.CellOf(centre) - FootprintOf(structType) / 2;

    // GDD Q2 strict adjacency: Chebyshev anchor distance to an own structure.
    // The Construction Yard projects the largest radius (Q2 resolution).
    public const int BuildRadius = 5;
    public const int CyBuildRadius = 7;
    /// <summary>A barrier anchors only other barriers, and only this far (ADR-005 clause 4): a wall crawls outward two cells and 100 credits at a step, but never carries a factory with it.</summary>
    public const int BarrierBuildRadius = 2;
    /// <summary>Per-player barrier cap (ADR-005 clause 5). Derived from the TDD s6 ratified budget of 200 structures (03-technical-design-document.md:59): 2 x 80 + ~32 real buildings = ~192, inside budget. A performance guarantee, not a design flourish.</summary>
    public const int MaxBarriersPerPlayer = 80;

    /// <summary>TICKET-P5-REP-07: the Service Depot's field-repair reach, in
    /// cells. A system constant, deliberately NOT sight_range - the depot sees
    /// 4 cells by coincidence and com_service_depot.yaml:12-15 warns about
    /// exactly that confusion; probes confirm radius 4 heals and radius 5 does
    /// not. The client draws the selected depot's aura ring from this same
    /// constant so the two cannot drift. Named from the existing literal in
    /// ProductionSystem's depot loop (a squared-distance compare against 16),
    /// so behaviour is bit-identical.</summary>
    public const int DepotRepairRadiusCells = 4;

    private void UnblockFootprint(int ax, int ay, int size)
    {
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
                Map.SetBlocked(ax + dx, ay + dy, false);
        _flow.Clear();
    }

    private void BlockFootprint(int ax, int ay, int size)
    {
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
                Map.SetBlocked(ax + dx, ay + dy, true);
        _flow.Clear(); // passability changed: every cached route is suspect
    }

    /// <summary>
    /// Centre of a size x size footprint anchored at 'anchor'. Bit-identical to
    /// the former FromInt(anchor + 1) for size 2: FromFraction(2, 2) computes
    /// ((Int128)2 &lt;&lt; 32) / 2 == 1L &lt;&lt; 32 == Fix64.One, and
    /// FromInt(a).Raw + One.Raw == (long)(a + 1) &lt;&lt; 32 == FromInt(a + 1).Raw.
    /// Do not substitute any other formula.
    /// </summary>
    private static Fix64 FootprintCentre(int anchor, int size) => Fix64.FromInt(anchor) + Fix64.FromFraction(size, 2);

    /// <summary>The supply and hp overrides survive BD-06 as nullable rather than literal defaults: passing nothing takes the catalogue value, so there is one place to edit, and the scenarios that pass an explicit number are bit-identical either way.</summary>
    public int SpawnPowerPlant(int player, int ax, int ay, int? supply = null, int? hp = null)
    {
        var def = GetStructureType(1);
        int sup = supply ?? def.PowerSupply, php = hp ?? def.Hp;
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.PowerPlant,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 1,
            Hp = php, MaxHp = php, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerSupply = sup,
            PowerDraw = def.PowerDraw,
        });
    }

    public int SpawnConstructionYard(int player, int ax, int ay)
    {
        var def = GetStructureType(4);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.ConstructionYard,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 4,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    /// <summary>Superweapon: charges over defaultCharge ticks (pausing while underpowered); a test may shorten the charge.</summary>
    public int SpawnSuperweapon(int player, int ax, int ay, int chargeTicks = 1500)
    {
        var def = GetStructureType(6);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Superweapon,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 6,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
            ChargeTicks = chargeTicks, StrikeTicks = -1,
        });
    }

    /// <summary>Veil projector (TICKET-P2-SIM-18): Sodality area cloak. Friendly
    /// mobile units within its Sight radius are field-cloaked - but only while
    /// the base has full power; a brown-out drops the whole veil (classic).</summary>
    public int SpawnVeilProjector(int player, int ax, int ay, int? hp = null)
    {
        var def = GetStructureType(7);
        int vhp = hp ?? def.Hp;
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.VeilProjector,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 7,
            Hp = vhp, MaxHp = vhp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    /// <summary>Radar Uplink (ADR-008 clause 4): the eye of the base. A living
    /// uplink with supply covering draw is what lights the client's minimap;
    /// inside the sim it is an ordinary structure - it sells, repairs,
    /// captures, blocks and counts for the victory test via IsStructure.</summary>
    public int SpawnRadarUplink(int player, int ax, int ay)
    {
        var def = GetStructureType(12);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.RadarUplink,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 12,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    /// <summary>Service depot: own units within radius 4 repair 2 hp/tick at 1 credit/tick each - the field hospital of the armoured column.</summary>
    public int SpawnServiceDepot(int player, int ax, int ay)
    {
        var def = GetStructureType(8);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.ServiceDepot,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 8,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    public int SpawnTurret(int player, int ax, int ay)
    {
        var def = GetStructureType(5);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Turret,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 5,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, WeaponId = def.WeaponId, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    /// <summary>
    /// Barrier segment (ADR-005): 1x1, bought upfront at placement, no ready
    /// slot, no build time. Sight = Fix64.Zero is deliberate and load-bearing:
    /// FogSystem skips zero-sight entities, so 80 walls per player cost nothing
    /// in the fog pass and grant no vision.
    /// </summary>
    public int SpawnWall(int player, int ax, int ay)
    {
        var def = GetStructureType(9);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Wall,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 9,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, WeaponId = def.WeaponId, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    /// <summary>Living barrier count for a player, enforcing MaxBarriersPerPlayer. Entity-index scan: deterministic.</summary>
    private int CountBarriers(int player)
    {
        int n = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Alive && e.PlayerId == player && IsBarrier(e.Kind)) n++;
        }
        return n;
    }

    public int SpawnFactory(int player, int ax, int ay, int? draw = null)
    {
        var def = GetStructureType(2);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Factory,
            X = x, Y = y, TargetX = x, TargetY = y,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1, StructType = 2,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = draw ?? def.PowerDraw,
        });
    }

    /// <summary>Barracks (ADR-009 clause 5): the infantry production building,
    /// SpawnFactory's shape for struct type 11. Cheap and early, because that
    /// is what makes an infantry rush a real strategy rather than a factory
    /// afterthought. Inside the sim it is an ordinary producer and an ordinary
    /// structure: it queues, rallies, sells for half, repairs, captures,
    /// blocks and counts for the victory test (all via IsProducer and
    /// IsStructure membership).</summary>
    public int SpawnBarracks(int player, int ax, int ay)
    {
        var def = GetStructureType(11);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Barracks,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 11,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    /// <summary>
    /// Advance one tick, consuming the commands scheduled for it. The net layer
    /// must present commands pre-ordered (by PlayerId, then submission order);
    /// the sim applies them in the order given, identically everywhere.
    /// </summary>
    public void Step(ReadOnlySpan<Command> commands)
    {
        _events.Clear();
        foreach (ref readonly var c in commands) ApplyCommand(in c);
        OrderDispatchSystem();
        MovementSystem();
        SeparationSystem();
        DetectionSystem();
        CaptureSystem();
        CombatSystem();
        HarvestSystem();
        ProductionSystem();
        FogSystem();
        VictorySystem();
        Tick++;
    }

    private bool ValidId(int id) => (uint)id < (uint)_entities.Count;

    // Shift-queue order queues (TICKET-P2-SIM-19), keyed by entity id.
    // Cross-tick state: hashed and serialized. Keyed access only in hot paths;
    // any ITERATION goes through sorted keys.
    private readonly Dictionary<int, List<Command>> _orderQueues = new();

    private static bool IsBusy(in Entity e)
        => e.Moving || e.AMove || e.ExplicitTarget >= 0 || e.HState != HarvestState.Idle;

    /// <summary>
    /// Pop the next queued order for every idle entity. Runs after incoming
    /// commands (which may clear queues) and before movement. Entities are
    /// visited in sorted-id order because dispatched commands can touch
    /// shared state (a queued Produce checks the treasury).
    /// </summary>
    private void OrderDispatchSystem()
    {
        if (_orderQueues.Count == 0) return;
        var ids = new List<int>(_orderQueues.Keys);
        ids.Sort();
        foreach (int id in ids)
        {
            if (!ValidId(id) || !_entities[id].Alive) { _orderQueues.Remove(id); continue; }
            var q = _orderQueues[id];
            if (q.Count == 0) { _orderQueues.Remove(id); continue; }
            if (IsBusy(_entities[id])) continue;
            var next = q[0];
            q.RemoveAt(0);
            ApplyCommandCore(in next);
        }
    }

    private void ApplyCommand(in Command c)
    {
        if (!ValidId(c.EntityId)) return;
        {
            var carrier = _entities[c.EntityId];
            if (!carrier.Alive || carrier.PlayerId != c.PlayerId) return;
            if (c.Queued)
            {
                // Shift-queue: order preservation demands appending whenever a
                // queue already exists, even if the entity is momentarily idle.
                if (IsBusy(in carrier) || (_orderQueues.TryGetValue(c.EntityId, out var eq) && eq.Count > 0))
                {
                    if (!_orderQueues.TryGetValue(c.EntityId, out var q)) _orderQueues[c.EntityId] = q = new List<Command>();
                    q.Add(c);
                    return;
                }
            }
            else
            {
                _orderQueues.Remove(c.EntityId); // a fresh direct order wipes the plan
            }
        }
        ApplyCommandCore(in c);
    }

    private void ApplyCommandCore(in Command c)
    {
        var e = _entities[c.EntityId];
        if (!e.Alive || e.PlayerId != c.PlayerId) return; // players command only their own entities

        switch (c.Type)
        {
            case CommandType.Move:
            case CommandType.PathMove:
            case CommandType.AttackMove:
                // A fresh movement order overrides any standing attack order
                // (classic priority) - without this, a retreating unit stops
                // to keep shooting and the Move is silently hijacked.
                e.ExplicitTarget = -1;
                e.TargetX = Fix64.Clamp(c.X, Fix64.Zero, Fix64.FromInt(Map.Width) - Fix64.Half);
                e.TargetY = Fix64.Clamp(c.Y, Fix64.Zero, Fix64.FromInt(Map.Height) - Fix64.Half);
                e.Moving = true;
                e.UseFlow = c.Type != CommandType.Move;
                e.AMove = c.Type == CommandType.AttackMove;
                e.AMoveX = e.TargetX; e.AMoveY = e.TargetY;
                e.HState = HarvestState.Idle;
                e.StallTicks = 0;
                break;
            case CommandType.Stop:
                e.Moving = false; e.HState = HarvestState.Idle; e.ExplicitTarget = -1; e.AMove = false;
                break;
            case CommandType.Harvest:
                if (e.Kind == EntityKind.Harvester && ValidId(c.AuxId)
                    && _entities[c.AuxId].Kind == EntityKind.FerriteField && _entities[c.AuxId].Alive)
                {
                    e.FieldId = c.AuxId;
                    e.RefineryId = FindNearestRefinery(e.PlayerId, e.X, e.Y);
                    if (e.RefineryId >= 0) e.HState = HarvestState.ToField;
                }
                break;
            case CommandType.Attack:
                if (ValidId(c.AuxId) && _entities[c.AuxId].Alive) e.ExplicitTarget = c.AuxId;
                break;
            case CommandType.CancelProduce:
            {
                // A finished structure waiting in the sidebar slot cancels
                // first: it was fully paid, so it refunds in full, and the
                // paused production line resumes on its own (the slot gates it).
                if (e.Kind == EntityKind.ConstructionYard && e.ReadyStructure != 0)
                {
                    _credits[e.PlayerId] += GetStructureType(e.ReadyStructure).Cost;
                    e.ReadyStructure = 0;
                    break;
                }
                // ADR-009 clause 1, CancelProduce site: miss this and
                // barracks orders are uncancellable, with no error to say so.
                if (!IsProducer(e.Kind)) break;
                if (!_queues.TryGetValue(e.Id, out var cq) || c.AuxId < 0 || c.AuxId >= cq.Count) break;
                if (c.AuxId == 0)
                {
                    // Head is in progress: refund everything drained so far -
                    // pay-as-you-build makes the pro-rata refund exact for free.
                    _credits[e.PlayerId] += e.BuildPaid;
                    e.BuildPaid = 0;
                    e.BuildProgress = 0;
                }
                cq.RemoveAt(c.AuxId);
                break;
            }
            case CommandType.PlaceStructure:
            {
                // Classic sidebar flow (TICKET-P2-SIM-05): the structure was
                // already built and paid for at a Construction Yard; placing
                // consumes that readiness and charges nothing. Issued on any
                // own entity as the sanctioned command carrier. A rejected
                // placement retains readiness for another attempt.
                var sd = GetStructureType(c.AuxId);
                if (sd.Cost <= 0) break;
                // Barriers (ADR-005 clause 3) revive the upfront-cost model:
                // they have no ready slot and no build time, so the treasury is
                // charged the moment the segment lands. Everything else keeps
                // the sidebar readiness flow untouched.
                bool barrier = IsBarrier(sd.Kind);
                int ax = Map.CellOf(c.X), ay = Map.CellOf(c.Y);
                int readyCy = -1;
                if (barrier)
                {
                    if (_credits[c.PlayerId] < sd.Cost) break;
                    if (CountBarriers(c.PlayerId) >= MaxBarriersPerPlayer) break;
                }
                else
                {
                    for (int i = 0; i < _entities.Count; i++)
                    {
                        var o = _entities[i];
                        if (o.Alive && o.PlayerId == c.PlayerId && o.Kind == EntityKind.ConstructionYard
                            && o.ReadyStructure == c.AuxId) { readyCy = i; break; }
                    }
                    if (readyCy < 0) break;
                }
                // Order matters: validate before charging, charge before spawning.
                if (!ValidPlacement(c.PlayerId, ax, ay, c.AuxId)) break;
                if (barrier)
                {
                    _credits[c.PlayerId] -= sd.Cost;
                }
                else
                {
                    var cyEnt = _entities[readyCy];
                    cyEnt.ReadyStructure = 0;
                    _entities[readyCy] = cyEnt;
                    // The carrier entity is often the ready CY itself; resync the
                    // local copy so the ApplyCommand epilogue writeback cannot
                    // resurrect the readiness we just consumed.
                    if (readyCy == c.EntityId) e = cyEnt;
                }
                _events.Add(new GameEvent(GameEventType.StructurePlaced, _entities.Count, c.AuxId));
                switch (sd.Kind)
                {
                    case EntityKind.PowerPlant: SpawnPowerPlant(c.PlayerId, ax, ay); break;
                    case EntityKind.Factory: SpawnFactory(c.PlayerId, ax, ay); break;
                    case EntityKind.Refinery: SpawnRefinery(c.PlayerId, ax, ay); break;
                    case EntityKind.ConstructionYard: SpawnConstructionYard(c.PlayerId, ax, ay); break;
                    case EntityKind.Turret: SpawnTurret(c.PlayerId, ax, ay); break;
                    case EntityKind.Superweapon: SpawnSuperweapon(c.PlayerId, ax, ay); break;
                    case EntityKind.VeilProjector: SpawnVeilProjector(c.PlayerId, ax, ay); break;
                    case EntityKind.ServiceDepot: SpawnServiceDepot(c.PlayerId, ax, ay); break;
                    case EntityKind.Wall: SpawnWall(c.PlayerId, ax, ay); break;
                    case EntityKind.Barracks: SpawnBarracks(c.PlayerId, ax, ay); break;
                    case EntityKind.RadarUplink: SpawnRadarUplink(c.PlayerId, ax, ay); break;
                }
                break;
            }
            case CommandType.BuildStructure:
            {
                if (e.Kind != EntityKind.ConstructionYard) break;
                var bd = GetStructureType(c.AuxId);
                if (bd.Cost <= 0 || bd.BuildTicks <= 0) break; // CYs are MCV-deployed, never queued
                // The veil projector is Sodality doctrine; all other structures are common (for now).
                if (c.AuxId == 7 && _playerFaction[c.PlayerId] != FactionSodality) break;
                if (!_queues.TryGetValue(e.Id, out var bq)) _queues[e.Id] = bq = new List<int>();
                bq.Add(c.AuxId);
                break;
            }
            case CommandType.Deploy:
            {
                if (e.Kind != EntityKind.Unit || e.UnitType != 7) break; // MCVs only
                int dax = Map.CellOf(e.X), day = Map.CellOf(e.Y);
                if (!ValidFoundation(dax, day, c.EntityId)) break;
                e.Alive = false; // the vehicle IS the building
                int newCy = SpawnConstructionYard(e.PlayerId, dax, day);
                _events.Add(new GameEvent(GameEventType.Deployed, c.EntityId, newCy));
                break;
            }
            case CommandType.Repair:
            {
                if (!IsStructure(e.Kind)) break;
                e.Repairing = !e.Repairing;
                break;
            }
            case CommandType.LaunchSuper:
            {
                if (e.Kind != EntityKind.Superweapon || e.ChargeTicks > 0 || e.StrikeTicks >= 0) break;
                e.StrikeTicks = 75; // five seconds of incoming warning
                e.StrikeX = Fix64.Clamp(c.X, Fix64.Zero, Fix64.FromInt(Map.Width));
                e.StrikeY = Fix64.Clamp(c.Y, Fix64.Zero, Fix64.FromInt(Map.Height));
                _events.Add(new GameEvent(GameEventType.SuperweaponLaunched, c.EntityId, -1, e.StrikeX, e.StrikeY));
                break;
            }
            case CommandType.SellStructure:
            {
                if (!IsStructure(e.Kind)) break;
                var sold = GetStructureType(e.StructType);
                _credits[e.PlayerId] += sold.Cost / 2;
                e.Alive = false;
                UnblockFootprint(AnchorOf(e.X, e.StructType), AnchorOf(e.Y, e.StructType), FootprintOf(e.StructType));
                break;
            }
            case CommandType.SetRally:
            {
                // ADR-007: only a producing structure the commanding player
                // owns (ownership is the guard at the top of this method).
                // AuxId == -1 clears, restoring the canonical unset state so a
                // cleared structure serialises identically to a never-rallied
                // one. X/Y clamp exactly as the Move case does.
                if (!IsRallyable(e.Kind)) break;
                if (c.AuxId == -1)
                {
                    e.HasRally = false;
                    e.RallyX = Fix64.Zero;
                    e.RallyY = Fix64.Zero;
                    break;
                }
                e.HasRally = true;
                e.RallyX = Fix64.Clamp(c.X, Fix64.Zero, Fix64.FromInt(Map.Width) - Fix64.Half);
                e.RallyY = Fix64.Clamp(c.Y, Fix64.Zero, Fix64.FromInt(Map.Height) - Fix64.Half);
                break;
            }
            case CommandType.Produce:
            {
                // ADR-009 clause 1: any producer may receive Produce. The CY
                // is guarded out explicitly until the produced_at gate lands
                // later in this same wave (its queue holds STRUCTURES, so a
                // unit order routed into it would build the wrong catalogue's
                // type); the gate replaces this line with the split proper.
                if (!IsProducer(e.Kind)) break;
                if (e.Kind == EntityKind.ConstructionYard) break;
                if (GetUnitType(c.AuxId).Cost <= 0) break;
                if (GetUnitType(c.AuxId).Faction != FactionCommon
                    && GetUnitType(c.AuxId).Faction != _playerFaction[c.PlayerId]) break; // not your side's hardware
                // Pay-as-you-build (GDD s5): credits drain as progress accrues,
                // and progress halts while the treasury cannot cover the next
                // slice - so queueing needs no upfront affordability check.
                if (!_queues.TryGetValue(e.Id, out var q)) _queues[e.Id] = q = new List<int>();
                q.Add(c.AuxId);
                break;
            }
        }
        _entities[c.EntityId] = e;
    }

    // RadarUplink joined with ADR-008, Barracks and Airfield with ADR-009
    // clause 5 (the Airfield ahead of existing: membership is free and the
    // omission is the failure mode): omitting a new building here is the
    // silent killer the ADRs name - sell, repair, capture, rubble-unblock,
    // placement adjacency and the VictorySystem short-game rule all hang off
    // this predicate with no compile error to catch the omission.
    private static bool IsStructure(EntityKind k)
        => k is EntityKind.Refinery or EntityKind.Factory or EntityKind.PowerPlant
             or EntityKind.ConstructionYard or EntityKind.Turret or EntityKind.Superweapon
             or EntityKind.VeilProjector or EntityKind.ServiceDepot or EntityKind.Wall
             or EntityKind.Barracks or EntityKind.RadarUplink or EntityKind.Airfield;

    /// <summary>
    /// A barrier is a structure for blocking, selling, repairing and damage, and
    /// is excluded from the victory test, engineer capture and combat
    /// auto-acquisition (ADR-005 clause 2). DEF-09 appends 'or EntityKind.Gate'.
    /// </summary>
    private static bool IsBarrier(EntityKind k) => k is EntityKind.Wall;

    /// <summary>
    /// ADR-009 clause 1: the producer notion. Factory, Construction Yard and
    /// Barracks; the Airfield joins when it exists (it is a slot-model
    /// producer and waits on the air-layer ADR). Used at FOUR sites, three of
    /// which fail silently if missed: Produce's kind test, ProductionSystem's
    /// producer test (miss it and the barracks queue never advances with no
    /// error), CancelProduce (miss it and barracks orders are uncancellable),
    /// and the queue hash (which widens to all producer queues, closing
    /// PROD-D5, inside PROD-04's regeneration).
    /// </summary>
    private static bool IsProducer(EntityKind k)
        => k is EntityKind.Factory or EntityKind.ConstructionYard or EntityKind.Barracks;

    /// <summary>
    /// ADR-007: the structures SetRally accepts - producing structures only.
    /// ADR-009 widened this predicate to IsProducer in place, exactly as B2's
    /// delivery note said it would, so the wire format never changed twice:
    /// the barracks rallies (infantry want it most) and the Construction Yard
    /// keeps accepting the command as inert state (its products are placed,
    /// not spawned; the client offers no affordance on it).
    /// </summary>
    private static bool IsRallyable(EntityKind k) => IsProducer(k);

    /// <summary>Footprint physically clear (bounds, terrain, standing entities), ignoring adjacency - MCV deployment founds a base from nothing.</summary>
    /// <remarks>Fixed at FootprintSize by design (ADR-005 clause 1): only MCV deploy calls this, and a Construction Yard is always 2x2.</remarks>
    public bool ValidFoundation(int ax, int ay, int ignoreEntity = -1)
    {
        for (int dy = 0; dy < FootprintSize; dy++)
            for (int dx = 0; dx < FootprintSize; dx++)
            {
                int cx = ax + dx, cy = ay + dy;
                if (!Map.InBounds(cx, cy) || Map.IsBlocked(cx, cy)) return false;
            }
        for (int i = 0; i < _entities.Count; i++)
        {
            if (i == ignoreEntity) continue;
            var o = _entities[i];
            if (!o.Alive || IsStructure(o.Kind)) continue;
            int ocx = Map.CellOf(o.X), ocy = Map.CellOf(o.Y);
            if (ocx >= ax && ocx < ax + FootprintSize && ocy >= ay && ocy < ay + FootprintSize) return false;
        }
        return true;
    }

    /// <summary>
    /// GDD Q2 strict adjacency: every cell of the candidate's footprint must be
    /// in bounds, unblocked, and free of standing entities, and the anchor must
    /// lie within BuildRadius (Chebyshev) of an own living structure's anchor.
    /// The structType argument selects the footprint size (ADR-005 clause 1);
    /// it defaults to 0, which FootprintOf maps to the 2x2 default, so every
    /// pre-existing caller keeps its exact meaning.
    /// </summary>
    public bool ValidPlacement(int player, int ax, int ay, int structType = 0)
    {
        int size = FootprintOf(structType);
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
            {
                int cx = ax + dx, cy = ay + dy;
                if (!Map.InBounds(cx, cy) || Map.IsBlocked(cx, cy)) return false;
            }
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (!o.Alive || IsStructure(o.Kind)) continue; // structure cells are already blocked
            int ocx = Map.CellOf(o.X), ocy = Map.CellOf(o.Y);
            if (ocx >= ax && ocx < ax + size && ocy >= ay && ocy < ay + size) return false;
        }
        // ADR-005 clause 4: a barrier anchors only other barriers, and at its
        // own shorter radius. With no barrier present this loop is identical to
        // the pre-DEF-04 rule.
        bool candidateIsBarrier = IsBarrier(GetStructureType(structType).Kind);
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (!o.Alive || o.PlayerId != player || !IsStructure(o.Kind)) continue;
            bool anchorIsBarrier = IsBarrier(o.Kind);
            if (anchorIsBarrier && !candidateIsBarrier) continue; // a wall never anchors a real building
            int oax = AnchorOf(o.X, o.StructType), oay = AnchorOf(o.Y, o.StructType);
            int radius = anchorIsBarrier ? BarrierBuildRadius
                : o.Kind == EntityKind.ConstructionYard ? CyBuildRadius : BuildRadius;
            if (Math.Max(Math.Abs(oax - ax), Math.Abs(oay - ay)) <= radius) return true;
        }
        return false;
    }

    private int FindNearestRefinery(int player, Fix64 x, Fix64 y)
    {
        int best = -1; Fix64 bestD = Fix64.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var r = _entities[i];
            if (!r.Alive || r.Kind != EntityKind.Refinery || r.PlayerId != player) continue;
            Fix64 d = Fix64.DistSq(r.X - x, r.Y - y);
            if (d < bestD || (d == bestD && i < best)) { bestD = d; best = i; }
        }
        return best;
    }

    // ---- Systems (fixed order) ----

    /// <summary>
    /// TICKET-P5-PWR-02: the one power tally. Called per SYSTEM, not once per
    /// Step, and that is load-bearing: CombatSystem can destroy a plant on the
    /// tick ProductionSystem then reads, and the shipped behaviour is that
    /// ProductionSystem sees the post-combat total. Collapsing the calls into
    /// a shared per-Step tally would move every golden hash. The PlayerId
    /// upper bound closes PWR-D6: a scenario-spawned entity with an
    /// out-of-range player would previously have indexed past the span.
    /// </summary>
    private void ComputePower(Span<int> supply, Span<int> draw)
    {
        supply.Clear();
        draw.Clear();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.PlayerId < 0 || e.PlayerId >= _players) continue;
            supply[e.PlayerId] += e.PowerSupply;
            draw[e.PlayerId] += e.PowerDraw;
        }
    }

    /// <summary>
    /// GDD s7 line 85's 75 per cent threshold in integer maths with no
    /// division - the same expression the client's power bar already uses
    /// (Sidebar.cs), so the sim's notion of a brown-out and the UI's cannot
    /// drift (PWR-D4). Read by CombatSystem's turret gate (ADR-008 clause 1)
    /// against the pre-combat tally; the boundary is inclusive.
    /// </summary>
    private static bool AtLeast75(int supply, int draw) => draw <= 0 || supply * 4 >= draw * 3;

    private void MovementSystem()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || !e.Moving || e.Speed == Fix64.Zero) continue;
            e.PrevX = e.X; e.PrevY = e.Y;
            StepToward(ref e);
            // ADR-007: Departing lifts the tick the unit's cell differs from
            // its spawn cell or the walk ends. PrevX/PrevY hold the position
            // at the top of this tick, and a departing unit starts in its
            // spawn cell, so the first boundary crossing observed here is
            // exactly "left the spawn cell".
            if (e.Departing && (!e.Moving
                || Map.CellOf(e.X) != Map.CellOf(e.PrevX) || Map.CellOf(e.Y) != Map.CellOf(e.PrevY)))
                e.Departing = false;
            _entities[i] = e;
        }
    }

    /// <summary>Move one tick toward (TargetX, TargetY), via flow field when UseFlow.</summary>
    private void StepToward(ref Entity e)
    {
        Fix64 aimX = e.TargetX, aimY = e.TargetY;

        if (e.UseFlow)
        {
            // Crowd arrival: combat units consider a PathMove complete within 4
            // cells of the destination, so massed armies settle instead of
            // fighting for one exact point. Never applies while executing an
            // attack order - attackers must close to weapon range, not to a
            // comfortable distance. Formation offsets are a P2 ticket.
            // !e.Departing (ADR-007): a production exit move must actually
            // leave the factory mouth - without the guard any rally (or
            // default exit) within 4 cells of the spawn cell is a silent
            // no-op and the spawn ring saturates (SPAWN-D3).
            if (e.Kind == EntityKind.Unit && e.ExplicitTarget < 0 && !e.AMove && !e.Departing
                && Fix64.DistSq(e.TargetX - e.X, e.TargetY - e.Y) <= Fix64.FromInt(16))
            { e.Moving = false; return; }

            int cx = Map.CellOf(e.X), cy = Map.CellOf(e.Y);
            int tcx = Map.CellOf(e.TargetX), tcy = Map.CellOf(e.TargetY);
            if (cx != tcx || cy != tcy)
            {
                var field = _flow.Get(Map, tcx, tcy);
                int next = field.NextCell(Map, cx, cy);
                if (next < 0) { e.Moving = false; return; } // unreachable
                aimX = Map.CellCentre(next % Map.Width);
                aimY = Map.CellCentre(next / Map.Width);
            }
        }

        Fix64 dx = aimX - e.X, dy = aimY - e.Y;
        Fix64 distSq = Fix64.DistSq(dx, dy);
        Fix64 stepSq = e.Speed * e.Speed;
        if (distSq <= stepSq)
        {
            e.X = aimX; e.Y = aimY;
            if (aimX == e.TargetX && aimY == e.TargetY)
            {
                // Exact arrival stops the walk. It only ends an attack-move
                // under the same rule as the completion branch in CombatSystem:
                // the point actually reached must be the ORDERED point and
                // nothing targetable may stand within sight of it. Without the
                // guard, landing exactly on the point (or, rarer, exactly on a
                // pursuit target's coordinates) released the stance regardless,
                // which is the same arrival-without-arrival hole in miniature.
                e.Moving = false;
                if (e.AMove && e.TargetX == e.AMoveX && e.TargetY == e.AMoveY && !EnemyNearAMovePoint(in e))
                    e.AMove = false;
            }
        }
        else
        {
            Fix64 dist = Fix64.Sqrt(distSq);
            e.X += dx * e.Speed / dist;
            e.Y += dy * e.Speed / dist;
        }
    }

    /// <summary>
    /// TICKET-P2-SIM-09: recompute per-player detection of stealthed entities
    /// from detector coverage, tick down firing-reveal windows, and (riding
    /// the same pass) run elite self-repair (TICKET-P2-SIM-10).
    /// </summary>
    private void DetectionSystem()
    {
        // Power tally factored out (TICKET-P5-PWR-02); the per-entity work
        // below stays in its own loop. The split is hash-neutral: the field
        // sets are disjoint, and the tally completed before the veil loop
        // read it in the fused version too.
        Span<int> supply = stackalloc int[_players];
        Span<int> draw = stackalloc int[_players];
        ComputePower(supply, draw);
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive) continue;
            bool dirty = false;
            if (e.RevealTicks > 0) { e.RevealTicks--; dirty = true; }
            if (e.DetectedMask != 0) { e.DetectedMask = 0; dirty = true; }
            if (e.FieldCloaked) { e.FieldCloaked = false; dirty = true; }
            if (e.Rank == 2 && e.Hp < e.MaxHp && Tick % 15 == 0) { e.Hp++; dirty = true; }
            if (dirty) _entities[i] = e;
        }
        // Veil projectors (TICKET-P2-SIM-18): powered projectors cloak nearby
        // friendly mobile units. Runs before detectors so detection can still
        // strip the veil.
        for (int v = 0; v < _entities.Count; v++)
        {
            var vp = _entities[v];
            if (!vp.Alive || vp.Kind != EntityKind.VeilProjector) continue;
            if (supply[vp.PlayerId] < draw[vp.PlayerId]) continue; // brown-out drops the veil
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (!e.Alive || e.PlayerId != vp.PlayerId) continue;
                if (e.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
                if (Fix64.DistSq(e.X - vp.X, e.Y - vp.Y) > vp.Sight * vp.Sight) continue;
                e.FieldCloaked = true;
                _entities[i] = e;
            }
        }
        for (int d = 0; d < _entities.Count; d++)
        {
            var det = _entities[d];
            if (!det.Alive || !det.Detector || det.PlayerId < 0) continue;
            byte bit = (byte)(1 << det.PlayerId);
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (!e.Alive || !(e.Stealth || e.FieldCloaked) || e.PlayerId == det.PlayerId) continue;
                if (Fix64.DistSq(e.X - det.X, e.Y - det.Y) > det.Sight * det.Sight) continue;
                e.DetectedMask |= bit;
                _entities[i] = e;
            }
        }
    }

    /// <summary>Can observer player target this entity? Stealth blocks targeting unless revealed by firing or detected by that player's detectors.</summary>
    private static bool CanTarget(int byPlayer, in Entity t)
        => !(t.Stealth || t.FieldCloaked) || t.RevealTicks > 0 || (t.DetectedMask & (1 << byPlayer)) != 0;

    /// <summary>
    /// TICKET-P3-FAC-03: engineers (unit type 11) ordered onto an enemy
    /// structure pursue it and convert it on contact. The engineer is
    /// consumed by the act; the captured structure keeps its hit points but
    /// loses its production queue and ready slot (the crews flee with the
    /// blueprints). The signature 90s-RTS personality mechanic - and the seed of
    /// the Reclaimers' salvage identity when the third faction arrives.
    /// </summary>
    private void CaptureSystem()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.UnitType != 11 || e.ExplicitTarget < 0) continue;
            if (!ValidId(e.ExplicitTarget)) { e.ExplicitTarget = -1; _entities[i] = e; continue; }
            var t = _entities[e.ExplicitTarget];
            // ADR-005 clause 2: engineers do not capture fences.
            if (!t.Alive || !IsStructure(t.Kind) || IsBarrier(t.Kind) || t.PlayerId == e.PlayerId)
            { e.ExplicitTarget = -1; _entities[i] = e; continue; }
            Fix64 d = Fix64.DistSq(t.X - e.X, t.Y - e.Y);
            if (d <= Fix64.FromFraction(49, 16)) // within 1.75 cells of the footprint centre: through the door
            {
                int captured = e.ExplicitTarget;
                t.PlayerId = e.PlayerId;
                t.Repairing = false;
                t.ReadyStructure = 0;
                _queues.Remove(captured);
                _orderQueues.Remove(captured);
                _entities[captured] = t;
                e.Alive = false; e.Moving = false; e.ExplicitTarget = -1; // the act consumes the engineer
                _events.Add(new GameEvent(GameEventType.Captured, captured, e.PlayerId));
            }
            else
            {
                e.TargetX = t.X; e.TargetY = t.Y;
                e.UseFlow = true; e.Moving = true;
            }
            _entities[i] = e;
        }
    }

    /// <summary>Is there any flow route from this entity's cell to its ordered attack-move point? Reads the deterministic flow cache; FlowField.Build is pure, so building here or in MovementSystem yields the same field.</summary>
    private bool RouteExists(in Entity e)
    {
        int cx = Map.CellOf(e.X), cy = Map.CellOf(e.Y);
        int tcx = Map.CellOf(e.AMoveX), tcy = Map.CellOf(e.AMoveY);
        if (cx == tcx && cy == tcy) return true;
        return _flow.Get(Map, tcx, tcy).NextCell(Map, cx, cy) >= 0;
    }

    /// <summary>Nearest living enemy barrier by squared distance; ties break to the lower id via strict less-than in entity index order (the FindNearestRefinery precedent).</summary>
    private int NearestEnemyBarrier(in Entity e)
    {
        int best = -1; Fix64 bestD = Fix64.MaxValue;
        for (int j = 0; j < _entities.Count; j++)
        {
            var t = _entities[j];
            if (!t.Alive || !IsBarrier(t.Kind) || t.PlayerId < 0 || t.PlayerId == e.PlayerId) continue;
            Fix64 d = Fix64.DistSq(t.X - e.X, t.Y - e.Y);
            if (d < bestD) { bestD = d; best = j; }
        }
        return best;
    }

    /// <summary>
    /// Does any targetable enemy stand within this unit's sight of its ordered
    /// attack-move point? This is the completion question: "all clear" must be
    /// judged from where the unit was SENT, not from where the crowd happened
    /// to stop, or the lead unit of a crowd halts up to the crowd radius short
    /// and declares victory over a base it cannot see. Evaluated lazily at the
    /// completion sites so the cost lands once per completion attempt rather
    /// than once per enemy per tick, and so the rule holds identically on the
    /// auto-acquire path, the explicit-target path (breach pursuit holds
    /// ExplicitTarget while AMove is up) and the exact-arrival path in MoveTo.
    /// Filters mirror auto-acquisition exactly: barriers and ferrite fields are
    /// not "something to fight" (ADR-005 clause 2) and stealth follows
    /// CanTarget - unseen is untargetable, so an unseen enemy cannot hold the
    /// stance open. Reads e.Sight, which is immutable after spawn and therefore
    /// deliberately unhashed; if sight ever becomes mutable at runtime it must
    /// enter ComputeStateHash or this predicate desyncs invisibly.
    /// </summary>
    private bool EnemyNearAMovePoint(in Entity e)
    {
        for (int j = 0; j < _entities.Count; j++)
        {
            var t = _entities[j];
            if (!t.Alive || t.PlayerId < 0 || t.PlayerId == e.PlayerId || t.Kind == EntityKind.FerriteField || IsBarrier(t.Kind)) continue;
            if (!CanTarget(e.PlayerId, in t)) continue;
            if (Fix64.DistSq(t.X - e.AMoveX, t.Y - e.AMoveY) <= e.Sight * e.Sight) return true;
        }
        return false;
    }

    private void CombatSystem()
    {
        // TICKET-P5-PWR-02 staged this third tally; ADR-008 clause 1 is what
        // reads it: the turret gate below. Snapshot semantics, pinned by the
        // ADR's clause 2: this tally runs BEFORE damage is applied, so the
        // gate sees PRE-combat power (a plant destroyed on tick N disables
        // its turrets on tick N+1), while ProductionSystem's own tally sees
        // the post-combat total the same tick. Both are deterministic; they
        // are different numbers, and the per-system rule (see ComputePower)
        // is what keeps each system's instant honest.
        Span<int> combatSupply = stackalloc int[_players];
        Span<int> combatDraw = stackalloc int[_players];
        ComputePower(combatSupply, combatDraw);

        // Acquire and fire. Damage applied immediately; deaths processed after
        // the scan so within-tick results don't depend on entity order.
        Span<int> pendingDamage = _entities.Count <= 4096 ? stackalloc int[_entities.Count] : new int[_entities.Count];
        Span<int> firstAttacker = _entities.Count <= 4096 ? stackalloc int[_entities.Count] : new int[_entities.Count];
        pendingDamage.Clear();
        firstAttacker.Fill(-1);

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.WeaponId == 0) continue;
            // GDD s5 line 48: a browned-out base cannot power its guns
            // (ADR-008 clause 1). Turret kind only - the GDD says defensive
            // turrets, and doc 22's emplacement and bastion join by kind when
            // they land. The continue sits ABOVE the cooldown decrement
            // deliberately: a dead turret does not reload. Inclusive boundary
            // via divisionless AtLeast75: supply 15 against draw 20 FIRES.
            if (e.Kind == EntityKind.Turret && !AtLeast75(combatSupply[e.PlayerId], combatDraw[e.PlayerId]))
            { _entities[i] = e; continue; }
            if (e.Cooldown > 0) { e.Cooldown--; _entities[i] = e; continue; }

            var w = Weapons.Get(e.WeaponId);
            int target = -1, sightTarget = -1;

            // A dead, invalid, or no-longer-targetable (stealthed) explicit
            // target is cleared so the unit falls back to auto-acquire next
            // tick instead of standing idle or chasing a ghost.
            if (e.ExplicitTarget >= 0 && (!ValidId(e.ExplicitTarget) || !_entities[e.ExplicitTarget].Alive
                || !CanTarget(e.PlayerId, _entities[e.ExplicitTarget])))
                e.ExplicitTarget = -1;

            if (e.ExplicitTarget >= 0)
            {
                var t = _entities[e.ExplicitTarget];
                Fix64 td = Fix64.DistSq(t.X - e.X, t.Y - e.Y);
                if (td < w.MinRange * w.MinRange)
                {
                    // Inside the dead zone: artillery stands helpless (classic
                    // weakness); it neither fires nor pursues further in.
                }
                else if (td <= w.Range * w.Range)
                {
                    target = e.ExplicitTarget;
                    if (e.Kind == EntityKind.Unit) e.Moving = false; // in range: hold and fire
                }
                else if (e.Kind == EntityKind.Unit && e.Speed != Fix64.Zero)
                {
                    // Attack-pursuit: close to weapon range via pathfinding.
                    e.TargetX = t.X; e.TargetY = t.Y;
                    e.Moving = true; e.UseFlow = true;
                }
            }
            else
            {
                // Auto-acquire nearest enemy in range; ties break to lower id.
                // Attack-movers additionally track the nearest enemy within
                // SIGHT so they can hunt flankers instead of marching past.
                Fix64 bestD = Fix64.MaxValue, bestSightD = Fix64.MaxValue;
                for (int j = 0; j < _entities.Count; j++)
                {
                    var t = _entities[j];
                    // Barriers are skipped exactly as ferrite fields are (ADR-005
                    // clause 2): without this your tanks stop to plink at a wall
                    // instead of the turret behind it, and this O(n) inner scan
                    // grows by 160 entities for every armed unit every tick. An
                    // explicit Attack order still targets a wall; only
                    // auto-acquisition declines to.
                    if (!t.Alive || t.PlayerId < 0 || t.PlayerId == e.PlayerId || t.Kind == EntityKind.FerriteField || IsBarrier(t.Kind)) continue;
                    if (!CanTarget(e.PlayerId, in t)) continue; // stealth: unseen is untargetable
                    Fix64 d = Fix64.DistSq(t.X - e.X, t.Y - e.Y);
                    if (d >= w.MinRange * w.MinRange && d <= w.Range * w.Range && d < bestD) { bestD = d; target = j; }
                    if (e.AMove && d <= e.Sight * e.Sight && d < bestSightD) { bestSightD = d; sightTarget = j; }
                }
            }

            if (target >= 0)
            {
                // Veterancy scales outgoing damage: 4/4, 5/4, 6/4 by rank.
                int dmg = DamageMatrix.Apply(w.Damage, w.Warhead, _entities[target].Armour) * (4 + e.Rank) / 4;
                pendingDamage[target] += dmg;
                if (firstAttacker[target] < 0) firstAttacker[target] = i;
                _events.Add(new GameEvent(GameEventType.Fired, i, target));
                if (w.SplashRadius > Fix64.Zero)
                {
                    // Splash: half damage (own matrix per victim) to everything
                    // else near the TARGET - friend or foe; only the shooter
                    // itself is spared (TICKET-P2-SIM-14).
                    var tp = _entities[target];
                    Fix64 rr = w.SplashRadius * w.SplashRadius;
                    for (int v = 0; v < _entities.Count; v++)
                    {
                        if (v == target || v == i) continue;
                        var vic = _entities[v];
                        if (!vic.Alive || vic.Kind == EntityKind.FerriteField) continue;
                        if (Fix64.DistSq(vic.X - tp.X, vic.Y - tp.Y) > rr) continue;
                        pendingDamage[v] += DamageMatrix.Apply(w.Damage, w.Warhead, vic.Armour) * (4 + e.Rank) / 8;
                        if (firstAttacker[v] < 0) firstAttacker[v] = i;
                    }
                }
                e.Cooldown = w.CooldownTicks;
                if (e.Stealth || e.FieldCloaked) e.RevealTicks = 45; // firing breaks any cloak for 3 seconds
                if (e.Kind == EntityKind.Unit) e.Moving = false; // stop to engage
            }
            else if (e.AMove && e.Kind == EntityKind.Unit)
            {
                if (sightTarget >= 0)
                {
                    // Hunt: enemy in sight but outside gun range - close on it.
                    var t = _entities[sightTarget];
                    e.TargetX = t.X; e.TargetY = t.Y;
                }
                else if (!RouteExists(in e) && NearestEnemyBarrier(in e) is int wid && wid >= 0)
                {
                    // BREACH (ADR-005 / DEF-05). The ordered point is unreachable
                    // and an enemy barrier exists: make a hole rather than
                    // oscillating in place forever. Two stages, deliberately:
                    // path toward the nearest barrier from anywhere on the map
                    // (a fully enclosed base severs the route for units nowhere
                    // near a wall, which would otherwise stand still), but only
                    // take it as a target once it is inside this unit's own
                    // Sight, so the unit never shoots a wall it cannot see.
                    // Self-healing needs no cleanup: when the wall dies the
                    // stale-target guard above clears ExplicitTarget next tick,
                    // AMove is still true so the march resumes, and the death
                    // path unblocks the footprint and clears the flow cache, so
                    // the next route query rebuilds against the breach.
                    //
                    // We do NOT halt here. This block also runs on the ticks
                    // when ExplicitTarget is already the wall but the wall is
                    // still outside weapon range (the pursuit branch above
                    // leaves target < 0), so halting on sight would stomp that
                    // pursuit and park the unit in the band between Sight and
                    // Range forever - the very freeze this ticket exists to
                    // kill. Closing is always right; the pursuit branch owns
                    // the stop, via its "in range: hold and fire".
                    var wall = _entities[wid];
                    if (Fix64.DistSq(wall.X - e.X, wall.Y - e.Y) <= e.Sight * e.Sight) e.ExplicitTarget = wid;
                    e.TargetX = wall.X; e.TargetY = wall.Y;
                }
                else if (Fix64.DistSq(e.AMoveX - e.X, e.AMoveY - e.Y) <= Fix64.FromInt(16) && !EnemyNearAMovePoint(in e))
                {
                    // All clear and inside the crowd radius of the ordered
                    // point: the attack-move is complete.
                    //
                    // "All clear" is judged from the ORDERED POINT, not from
                    // this unit's feet (EnemyNearAMovePoint). The crowd radius
                    // is 4 cells, so the lead unit halts up to 4 cells short;
                    // judging sight from there declares victory while the thing
                    // it was sent to kill sits 4 cells further out than it can
                    // see. Worse, halting here leaves TargetX/Y on the ordered
                    // point, which makes this unit a textbook arrival-contagion
                    // seed and strips AMove from the whole crowd behind it
                    // within a few ticks. The army parks intact outside an
                    // untouched base.
                    e.Moving = false; e.AMove = false;
                    _entities[i] = e;
                    continue;
                }
                else
                {
                    // All clear: march on to the ordered destination.
                    e.TargetX = e.AMoveX; e.TargetY = e.AMoveY;
                }
                e.Moving = true; e.UseFlow = true;
            }
            _entities[i] = e;
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            if (pendingDamage[i] == 0) continue;
            var t = _entities[i];
            t.Hp -= pendingDamage[i];
            if (t.Alive && t.Kind == EntityKind.Harvester && t.HState == HarvestState.Loading)
            {
                // Shot while loading: abandon the crystal face and run the
                // part-load home (TICKET-P2-SIM-08). Damage merely en route
                // does not deter a harvester - classic stubbornness; the
                // state machine handles pathing and re-tasking.
                t.HState = HarvestState.ToRefinery;
            }
            if (t.Hp <= 0)
            {
                _events.Add(new GameEvent(GameEventType.Died, i, -1));
                t.Alive = false; t.Moving = false; t.HState = HarvestState.Idle;
                // Destroyed structures leave passable rubble: the footprint
                // unblocks and cached routes are discarded.
                if (IsStructure(t.Kind)) UnblockFootprint(AnchorOf(t.X, t.StructType), AnchorOf(t.Y, t.StructType), FootprintOf(t.StructType));
                // Kill credit for veterancy: the first attacker this tick.
                int killer = firstAttacker[i];
                if (killer >= 0)
                {
                    var k = _entities[killer];
                    if (k.Alive && k.VetEnabled)
                    {
                        k.Kills++;
                        int newRank = k.Kills >= 6 ? 2 : k.Kills >= 3 ? 1 : 0;
                        if (newRank != k.Rank) _events.Add(new GameEvent(GameEventType.Promoted, killer, newRank));
                        k.Rank = newRank;
                        _entities[killer] = k;
                    }
                }
            }
            _entities[i] = t;
        }
    }

    private void HarvestSystem()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.Kind != EntityKind.Harvester || e.HState == HarvestState.Idle) continue;

            switch (e.HState)
            {
                case HarvestState.ToField:
                {
                    if (!ValidId(e.FieldId) || !_entities[e.FieldId].Alive || _entities[e.FieldId].FerriteAmount <= 0)
                    { RetargetField(ref e); break; }
                    var f = _entities[e.FieldId];
                    if (Arrived(in e, f.X, f.Y)) { e.HState = HarvestState.Loading; e.Moving = false; }
                    else MoveTo(ref e, f.X, f.Y);
                    break;
                }
                case HarvestState.Loading:
                {
                    if (!ValidId(e.FieldId)) { e.HState = HarvestState.Idle; break; }
                    var f = _entities[e.FieldId];
                    int take = Math.Min(LoadPerTick, Math.Min(HarvesterCapacity - e.Carry, f.FerriteAmount));
                    e.Carry += take;
                    f.FerriteAmount -= take;
                    if (f.FerriteAmount <= 0) f.Alive = false;
                    _entities[e.FieldId] = f;
                    if (e.Carry >= HarvesterCapacity || !f.Alive)
                        e.HState = HarvestState.ToRefinery;
                    break;
                }
                case HarvestState.ToRefinery:
                {
                    if (!ValidId(e.RefineryId) || !_entities[e.RefineryId].Alive)
                    {
                        e.RefineryId = FindNearestRefinery(e.PlayerId, e.X, e.Y);
                        if (e.RefineryId < 0) { e.HState = HarvestState.Idle; break; }
                    }
                    var r = _entities[e.RefineryId];
                    if (Docked(in e, r.X, r.Y)) { e.HState = HarvestState.Unloading; e.StateTicks = UnloadTicks; e.Moving = false; }
                    else MoveTo(ref e, r.X, r.Y);
                    break;
                }
                case HarvestState.Unloading:
                {
                    if (--e.StateTicks <= 0)
                    {
                        _credits[e.PlayerId] += e.Carry;
                        e.Carry = 0;
                        e.HState = HarvestState.ToField;
                        if (!ValidId(e.FieldId) || !_entities[e.FieldId].Alive) RetargetField(ref e);
                    }
                    break;
                }
            }
            _entities[i] = e;
        }
    }

    private void RetargetField(ref Entity e)
    {
        // Nearest live field with ferrite remaining; ties break to lower id (US2.2 auto-reassign).
        int best = -1; Fix64 bestD = Fix64.MaxValue;
        for (int j = 0; j < _entities.Count; j++)
        {
            var f = _entities[j];
            if (!f.Alive || f.Kind != EntityKind.FerriteField || f.FerriteAmount <= 0) continue;
            Fix64 d = Fix64.DistSq(f.X - e.X, f.Y - e.Y);
            if (d < bestD) { bestD = d; best = j; }
        }
        if (best < 0) { e.HState = HarvestState.Idle; e.Moving = false; }
        else { e.FieldId = best; e.HState = HarvestState.ToField; }
    }

    private static bool Arrived(in Entity e, Fix64 x, Fix64 y)
        => Fix64.DistSq(x - e.X, y - e.Y) <= Fix64.One; // within 1 cell (open ground: ferrite fields)

    // Structures block their own footprints, so vehicles dock from adjacent
    // cells: within ~2.83 cells of the footprint centre covers every
    // orthogonal and diagonal neighbour of a 2x2 building.
    private static bool Docked(in Entity e, Fix64 x, Fix64 y)
        => Fix64.DistSq(x - e.X, y - e.Y) <= Fix64.FromInt(8);

    private void MoveTo(ref Entity e, Fix64 x, Fix64 y)
    {
        e.TargetX = x; e.TargetY = y; e.Moving = true; e.UseFlow = true;
        StepToward(ref e);
    }

    /// <summary>
    /// TICKET-P2-SIM-01b: pairwise separation so marching units do not stack.
    /// Spatial bucket grid rebuilt per tick; only MOVING mobile entities are
    /// displaced (stationary units act as soft obstacles), processed in entity
    /// index order with immediate application, so results are order-fixed and
    /// deterministic. Exact-overlap fallback direction derives from ids.
    /// </summary>
    private void SeparationSystem()
    {
        _buckets.Clear();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
            int cell = Map.CellIndex(Map.CellOf(e.X), Map.CellOf(e.Y));
            if (!_buckets.TryGetValue(cell, out var list)) _buckets[cell] = list = new List<int>();
            list.Add(i);
        }

        Fix64 minDist = Fix64.FromFraction(3, 5);            // combined radius: 0.3 + 0.3 cells
        Fix64 minDistSq = minDist * minDist;

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || !e.Moving || e.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
            int cx = Map.CellOf(e.X), cy = Map.CellOf(e.Y);
            Fix64 pushX = Fix64.Zero, pushY = Fix64.Zero;
            bool pressedOnStationary = false;

            for (int by = cy - 1; by <= cy + 1; by++)
            {
                if ((uint)by >= (uint)Map.Height) continue;
                for (int bx = cx - 1; bx <= cx + 1; bx++)
                {
                    if ((uint)bx >= (uint)Map.Width) continue;
                    if (!_buckets.TryGetValue(Map.CellIndex(bx, by), out var list)) continue;
                    foreach (int j in list)
                    {
                        if (j == i) continue;
                        var o = _entities[j];
                        if (!o.Alive) continue;
                        Fix64 dx = e.X - o.X, dy = e.Y - o.Y;
                        Fix64 dSq = Fix64.DistSq(dx, dy);
                        if (dSq >= minDistSq) continue;

                        // Crush (TICKET-P2-SIM-16): a heavy vehicle rolling
                        // over enemy foot infantry flattens the squad. Deep
                        // contact only; kill credit follows the usual
                        // veterancy rules; stealth is no defence against
                        // thirty tonnes that cannot see you.
                        if (e.Kind == EntityKind.Unit && e.Armour == ArmourClass.Heavy
                            && o.Kind == EntityKind.Unit && o.Armour == ArmourClass.None
                            && o.PlayerId >= 0 && o.PlayerId != e.PlayerId)
                        {
                            // Treads do not yield: a crush-eligible squad
                            // exerts NO separation push on the vehicle, so
                            // contact deepens instead of steering around -
                            // which is the entire point of driving at them.
                            if (dSq < Fix64.FromFraction(4, 25)) // within 0.4 cells: flattened
                            {
                                var crushed = o;
                                crushed.Alive = false; crushed.Moving = false; crushed.HState = HarvestState.Idle;
                                _entities[j] = crushed;
                                _events.Add(new GameEvent(GameEventType.Died, j, -1));
                                if (e.VetEnabled)
                                {
                                    e.Kills++;
                                    int nr = e.Kills >= 6 ? 2 : e.Kills >= 3 ? 1 : 0;
                                    if (nr != e.Rank) _events.Add(new GameEvent(GameEventType.Promoted, i, nr));
                                    e.Rank = nr;
                                }
                            }
                            continue;
                        }

                        // Arrival contagion: pressing against a stopped unit
                        // that was heading to the same destination means the
                        // crowd has reached it - this unit has arrived too.
                        // Bounded to near the destination so a queue in a
                        // chokepoint cannot freeze by chain reaction.
                        //
                        // The neighbour must have ARRIVED, not merely be stopped.
                        // A unit that halts to FIRE keeps its attack-move stance
                        // and keeps TargetX/Y on its ordered point (the combat
                        // branch stops it without retargeting), so without the
                        // !o.AMove clause the first unit to pause and shoot
                        // becomes an arrival seed and cancels the attack-move of
                        // the entire crowd behind it, up to 20 cells from a
                        // destination none of them reached. A unit that has
                        // released the stance has genuinely finished its order,
                        // which is what makes it sound evidence that the crowd
                        // is there. Contagion is transitive by design - each
                        // newly settled ring seeds the next - and !o.AMove
                        // propagates along the chain, so the reach is unchanged.
                        // Plain moves never set AMove, so their settling
                        // behaviour is untouched.
                        if (e.Kind == EntityKind.Unit && o.Kind == EntityKind.Unit
                            && !o.Moving && !o.AMove && e.UseFlow
                            && (!e.AMove || (e.TargetX == e.AMoveX && e.TargetY == e.AMoveY))
                            && o.TargetX == e.TargetX && o.TargetY == e.TargetY
                            && Fix64.DistSq(e.TargetX - e.X, e.TargetY - e.Y) <= Fix64.FromInt(400))
                        {
                            e.Moving = false; e.AMove = false;
                            pushX = Fix64.Zero; pushY = Fix64.Zero;
                            goto settled;
                        }

                        if (dSq == Fix64.Zero)
                        {
                            // Deterministic tiebreak for perfect overlap.
                            dx = i < j ? Fix64.FromFraction(1, 100) : Fix64.FromFraction(-1, 100);
                            dSq = dx * dx;
                        }
                        Fix64 d = Fix64.Sqrt(dSq);
                        Fix64 overlap = minDist - d;
                        Fix64 scale = o.Moving ? Fix64.Half : Fix64.One; // full push off stationary blockers
                        if (!o.Moving) pressedOnStationary = true;
                        pushX += dx * overlap * scale / d;
                        pushY += dy * overlap * scale / d;
                    }
                }
            }
            settled:
            if (pushX != Fix64.Zero || pushY != Fix64.Zero)
            {
                Fix64 nx = Fix64.Clamp(e.X + pushX, Fix64.Half, Fix64.FromInt(Map.Width) - Fix64.Half);
                Fix64 ny = Fix64.Clamp(e.Y + pushY, Fix64.Half, Fix64.FromInt(Map.Height) - Fix64.Half);
                if (!Map.IsBlocked(Map.CellOf(nx), Map.CellOf(e.Y))) e.X = nx;
                if (!Map.IsBlocked(Map.CellOf(e.X), Map.CellOf(ny))) e.Y = ny;
            }

            // Stall arrival: a pathing combat unit that is no longer making
            // progress TOWARD its destination for 2 seconds is jammed against
            // (or orbiting the rim of) a settled crowd - treat it as arrived
            // where it stands. Progress is measured as reduction in distance
            // to target, not raw displacement, because blocked units slide
            // tangentially around the crowd at full speed while getting no
            // closer. Capacity-free, unlike any fixed contagion radius.
            // Harvesters are excluded: their controller owns retries.
            if (e.Moving && e.UseFlow && e.Kind == EntityKind.Unit
                && (!e.AMove || (e.TargetX == e.AMoveX && e.TargetY == e.AMoveY)))
            {
                Fix64 dPrev = Fix64.Sqrt(Fix64.DistSq(e.TargetX - e.PrevX, e.TargetY - e.PrevY));
                Fix64 dNow = Fix64.Sqrt(Fix64.DistSq(e.TargetX - e.X, e.TargetY - e.Y));
                // Leaky accumulator, gated on stationary contact: a unit
                // making no progress WHILE pressed against settled units is
                // rim-locked and should give up; a unit merely slowed inside
                // moving traffic is queueing and must keep trying. The leak
                // (+2 blocked / -1 progressing) rides out rim churn, where
                // single ticks of progress alternate with ejection.
                if (pressedOnStationary && dPrev - dNow < e.Speed / Fix64.FromInt(8))
                    e.StallTicks += 2;
                else if (e.StallTicks > 0)
                    e.StallTicks--;
                if (e.StallTicks >= 4 * TicksPerSecond)
                {
                    // Giving up on the WALK is always right - the unit is
                    // rim-locked and shoving achieves nothing. Giving up on the
                    // FIGHT is only right if this is the destination: a jam 19
                    // cells short of the objective is traffic, not arrival, and
                    // benching the unit there leaves it inert for the rest of
                    // the match while the crowd ahead of it moves on without
                    // it. Near the destination the two coincide and the
                    // heuristic keeps its original meaning.
                    if (e.AMove && Fix64.DistSq(e.TargetX - e.X, e.TargetY - e.Y) <= Fix64.FromInt(16)) e.AMove = false;
                    e.Moving = false; e.StallTicks = 0;
                }
            }

            _entities[i] = e; // persists arrival state even with zero push
        }
    }

    private readonly Dictionary<int, List<int>> _buckets = new(); // rebuilt per tick; keyed access only

    /// <summary>
    /// SPAWN-04's occupancy test: does any standing entity hold this cell?
    /// ValidPlacement's own predicate, not a hand-rolled kind list: dead
    /// entities and structures skip (structure cells are already blocked on
    /// the terrain grid), anything else standing in the cell blocks it - so a
    /// future EntityKind append is covered by construction. A direct
    /// entity-index scan rather than a read of SeparationSystem's _buckets:
    /// the buckets hold only Unit/Harvester kinds and were built before
    /// ProductionSystem ran, so they would miss both the broader predicate
    /// and anything an earlier factory spawned this same tick. Completions
    /// are rare, so O(entities) here is negligible against the TDD s6 budget.
    /// </summary>
    private bool CellOccupied(int cx, int cy)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (!o.Alive || IsStructure(o.Kind)) continue;
            if (Map.CellOf(o.X) == cx && Map.CellOf(o.Y) == cy) return true;
        }
        return false;
    }

    // Deterministic ring of spawn offsets for completed units.
    private static readonly (int Dx, int Dy)[] SpawnOffsets =
        { (0, 2), (1, 2), (-1, 2), (2, 0), (-2, 0), (0, -2), (2, 2), (-2, 2), (2, -2), (-2, -2), (0, 3) };

    /// <summary>
    /// TICKET-P2-SIM-02/03: power totals and factory queues. Per GDD s5, when
    /// supply falls below draw, production speed scales linearly down to 50%.
    /// Progress accrues in integer percent-ticks: 100/tick at full power.
    /// </summary>
    private void ProductionSystem()
    {
        // Freshly tallied here rather than shared from CombatSystem's call:
        // production must see the POST-combat total (TICKET-P5-PWR-02).
        Span<int> supply = stackalloc int[_players];
        Span<int> draw = stackalloc int[_players];
        ComputePower(supply, draw);

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive) continue;

            // Repair (TICKET-P2-SIM-08): 2 hp per tick for 1 credit per tick,
            // halting while broke and switching off when whole.
            if (e.Repairing && IsStructure(e.Kind))
            {
                if (e.Hp >= e.MaxHp) { e.Repairing = false; _entities[i] = e; }
                else if (_credits[e.PlayerId] >= 1)
                {
                    _credits[e.PlayerId] -= 1;
                    e.Hp = Math.Min(e.MaxHp, e.Hp + 2);
                    if (e.Hp >= e.MaxHp) e.Repairing = false;
                    _entities[i] = e;
                }
            }

            // Superweapon lifecycle (TICKET-P2-SIM-15): the charge advances
            // only at full power (classic); a scheduled strike counts down
            // regardless - the missile is already in the air.
            if (e.Kind == EntityKind.ServiceDepot)
            {
                // Field repairs: powered depots mend own damaged units in
                // radius 4 for a credit per unit per tick (2 hp/tick, same
                // rate and price as structure repair).
                if (supply[e.PlayerId] < draw[e.PlayerId]) { _entities[i] = e; continue; }
                for (int u = 0; u < _entities.Count; u++)
                {
                    var v = _entities[u];
                    if (!v.Alive || v.PlayerId != e.PlayerId || v.Hp >= v.MaxHp) continue;
                    if (v.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
                    // Squared distance, so the compare is radius squared: 16.
                    if (Fix64.DistSq(v.X - e.X, v.Y - e.Y)
                        > Fix64.FromInt(DepotRepairRadiusCells * DepotRepairRadiusCells)) continue;
                    if (_credits[e.PlayerId] < 1) break;
                    _credits[e.PlayerId] -= 1;
                    v.Hp = v.Hp + 2 > v.MaxHp ? v.MaxHp : v.Hp + 2;
                    _entities[u] = v;
                }
                _entities[i] = e;
                continue;
            }
            if (e.Kind == EntityKind.Superweapon)
            {
                int sp = e.PlayerId;
                if (e.ChargeTicks > 0 && supply[sp] >= draw[sp])
                {
                    if (--e.ChargeTicks == 0)
                        _events.Add(new GameEvent(GameEventType.SuperweaponReady, i, -1));
                }
                if (e.StrikeTicks > 0) e.StrikeTicks--;
                else if (e.StrikeTicks == 0)
                {
                    e.StrikeTicks = -1;
                    e.ChargeTicks = 1500; // the cycle begins again
                    _events.Add(new GameEvent(GameEventType.SuperweaponImpact, i, -1, e.StrikeX, e.StrikeY));
                    _entities[i] = e;
                    ApplyAreaDamage(e.StrikeX, e.StrikeY, 900);
                    e = _entities[i]; // the strike may have killed the launcher itself
                }
                _entities[i] = e;
                continue;
            }

            // ADR-009 clause 1, ProductionSystem site: miss this and the
            // barracks queue never advances, with no error to say so.
            if (!IsProducer(e.Kind)) continue;
            if (e.Kind == EntityKind.ConstructionYard && e.ReadyStructure != 0) continue; // placement pending pauses the line
            if (!_queues.TryGetValue(e.Id, out var q) || q.Count == 0) continue;

            int p = e.PlayerId;
            int rate = draw[p] <= 0 || supply[p] >= draw[p]
                ? 100
                : 50 + 50 * supply[p] / draw[p];

            bool isCy = e.Kind == EntityKind.ConstructionYard;
            int queuedType = q[0]; // captured once: the queue mutates on completion
            int defCost = isCy ? GetStructureType(queuedType).Cost : GetUnitType(queuedType).Cost;
            int defTicks = isCy ? GetStructureType(queuedType).BuildTicks : GetUnitType(queuedType).BuildTicks;
            var def = GetUnitType(queuedType);
            int total = defTicks * 100;
            int tentative = Math.Min(e.BuildProgress + rate, total);
            // Pay-as-you-build: cumulative amount owed at this progress point,
            // in integer maths. Progress halts when credits cannot cover the
            // slice; it resumes automatically when the treasury refills.
            int owed = (int)((long)defCost * tentative / total) - e.BuildPaid;
            if (_credits[p] < owed) { _entities[i] = e; continue; }
            _credits[p] -= owed;
            e.BuildPaid += owed;
            e.BuildProgress = tentative;
            if (e.BuildProgress >= total)
            {
                // TICKET-P5-SPAWN-04, THE MEASURED TRAP: the zeroing of
                // BuildProgress and BuildPaid lives BELOW the hold decision,
                // inside each success branch. At the top of this block - where
                // it used to sit - every held tick re-entered with BuildPaid 0
                // and the owed computation above recharged the FULL unit cost
                // each tick: measured at ~3000 credits/second. With BuildPaid
                // intact and progress pinned at total, owed is exactly zero
                // for every held tick by the formula itself.
                if (isCy)
                {
                    e.BuildProgress = 0;
                    e.BuildPaid = 0;
                    // Sidebar flow: the finished structure waits for placement.
                    // C is the producing yard, which is A here too; set it anyway
                    // so a consumer can read C without asking which case it is.
                    _events.Add(new GameEvent(GameEventType.ProductionComplete, i, queuedType, C: i));
                    e.ReadyStructure = queuedType;
                    q.RemoveAt(0);
                    _entities[i] = e;
                    continue;
                }
                // Spawn-cell occupancy (SPAWN-04): terrain AND standing
                // entities, via ValidPlacement's own predicate (CellOccupied).
                int scx = Map.CellOf(e.X), scy = Map.CellOf(e.Y);
                int sdx = 0, sdy = 0;
                bool found = false;
                foreach (var (dx, dy) in SpawnOffsets)
                {
                    int nx = scx + dx, ny = scy + dy;
                    if (!Map.InBounds(nx, ny) || Map.IsBlocked(nx, ny)) continue;
                    if (CellOccupied(nx, ny)) continue;
                    sdx = dx; sdy = dy; found = true;
                    break;
                }
                if (!found)
                {
                    // Every offset blocked: the unit is HELD at 100 per cent,
                    // fully paid, and retried next tick. The queue head is not
                    // popped, so a paid unit can no longer vanish (SPAWN-D2 is
                    // dead), the line behind the head stalls honestly, and a
                    // cancel while held still refunds in full via BuildPaid.
                    _entities[i] = e;
                    continue;
                }
                e.BuildProgress = 0;
                e.BuildPaid = 0;
                q.RemoveAt(0);
                int sx = scx + sdx, sy = scy + sdy;
                int spawned = def.Kind == EntityKind.Harvester
                    ? SpawnHarvester(p, Map.CellCentre(sx), Map.CellCentre(sy))
                    : SpawnUnit(p, Map.CellCentre(sx), Map.CellCentre(sy), def.Speed, def.Hp, def.Armour, def.WeaponId,
                        def.SightCells, def.Stealth, def.Detector, def.Veterancy, queuedType);
                SetExitMove(spawned, in e, scx, scy, sdx, sdy);
                // C = the factory that built it (TICKET-P5-BD-14): the rally
                // attribution the client cannot recover from position alone.
                _events.Add(new GameEvent(GameEventType.ProductionComplete, spawned, queuedType, C: i));
            }
            _entities[i] = e;
        }
    }

    /// <summary>
    /// ADR-007: a produced unit leaves the factory mouth. Toward the
    /// producer's rally when one is set; otherwise toward a deterministic
    /// default two further steps out along the chosen spawn offset (the same
    /// offset logic every time), so the eleven-cell ring can never saturate
    /// in the no-rally default game. Movement fields are written directly
    /// rather than synthesising a Command: the sim queues no commands of its
    /// own anywhere, and ADR-007 rejects inventing an internal command
    /// channel. Harvesters honour the rally and the default exit equally;
    /// Departing is set for units only (the crowd-arrival shortcut is
    /// kind-gated, so harvesters never consult it).
    /// </summary>
    private void SetExitMove(int spawned, in Entity producer, int scx, int scy, int dx, int dy)
    {
        var nu = _entities[spawned];
        Fix64 tx, ty;
        if (producer.HasRally)
        {
            tx = producer.RallyX;
            ty = producer.RallyY;
        }
        else
        {
            tx = Fix64.Clamp(Map.CellCentre(scx + 2 * dx), Fix64.Zero, Fix64.FromInt(Map.Width) - Fix64.Half);
            ty = Fix64.Clamp(Map.CellCentre(scy + 2 * dy), Fix64.Zero, Fix64.FromInt(Map.Height) - Fix64.Half);
        }
        nu.TargetX = tx; nu.TargetY = ty;
        nu.Moving = true; nu.UseFlow = true;
        if (nu.Kind == EntityKind.Unit) nu.Departing = true;
        _entities[spawned] = nu;
    }

    /// <summary>
    /// Detonation damage: full Omni damage within 1.5 cells of ground zero,
    /// half within 3. Deaths use the standard rules (rubble unblocks); nobody
    /// earns veterancy from a superweapon.
    /// </summary>
    private void ApplyAreaDamage(Fix64 x, Fix64 y, int baseDamage)
    {
        Fix64 innerSq = Fix64.FromFraction(9, 4); // 1.5^2
        Fix64 outerSq = Fix64.FromInt(9);         // 3^2
        for (int i = 0; i < _entities.Count; i++)
        {
            var t = _entities[i];
            if (!t.Alive || t.Kind == EntityKind.FerriteField) continue;
            Fix64 d = Fix64.DistSq(t.X - x, t.Y - y);
            if (d > outerSq) continue;
            int dmg = DamageMatrix.Apply(baseDamage, Warhead.Omni, t.Armour);
            if (d > innerSq) dmg /= 2;
            t.Hp -= dmg;
            if (t.Hp <= 0)
            {
                _events.Add(new GameEvent(GameEventType.Died, i, -1));
                t.Alive = false; t.Moving = false; t.HState = HarvestState.Idle;
                if (IsStructure(t.Kind)) UnblockFootprint(AnchorOf(t.X, t.StructType), AnchorOf(t.Y, t.StructType), FootprintOf(t.StructType));
            }
            _entities[i] = t;
        }
    }

    private void FogSystem()
    {
        for (int p = 0; p < _players; p++) Array.Clear(_visible[p]);
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.PlayerId < 0 || e.Sight == Fix64.Zero) continue;
            int cx = Map.CellOf(e.X), cy = Map.CellOf(e.Y);
            int r = e.Sight.ToIntFloor();
            int r2 = r * r;
            var vis = _visible[e.PlayerId];
            var exp = _explored[e.PlayerId];
            for (int dy = -r; dy <= r; dy++)
            {
                int ny = cy + dy;
                if ((uint)ny >= (uint)Map.Height) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int nx = cx + dx;
                    if ((uint)nx >= (uint)Map.Width || dx * dx + dy * dy > r2) continue;
                    int c = Map.CellIndex(nx, ny);
                    vis[c >> 6] |= 1UL << (c & 63);
                    exp[c >> 6] |= 1UL << (c & 63);
                }
            }
        }
    }

    /// <summary>
    /// TICKET-P2-SIM-12, classic short-game rule: a player with no living
    /// structures and no MCV is eliminated - units alone cannot rebuild.
    /// The last player standing wins; the sim keeps stepping afterwards.
    /// </summary>
    /// <summary>The classic short-game rule (no structures + no MCV = out)
    /// is a SKIRMISH rule. Commando and defence missions disable it via the
    /// map header 'rules noshortgame' - the mission script owns victory, and
    /// a baseless strike force is not a defeated player. Hashed.</summary>
    public bool ShortGameEnabled { get; set; } = true;

    private void VictorySystem()
    {
        if (!ShortGameEnabled || Winner >= 0 || _players < 2) return;
        Span<bool> hasHope = stackalloc bool[_players];
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.PlayerId < 0) continue;
            // ADR-005 clause 2: a barrier is not hope. Without this exclusion a
            // player whose last possession is one 100-credit wall is never
            // eliminated and the match never ends.
            if ((IsStructure(e.Kind) && !IsBarrier(e.Kind)) || e.UnitType == 7) hasHope[e.PlayerId] = true;
        }
        int living = 0, last = -1;
        for (int p = 0; p < _players; p++)
        {
            if (hasHope[p]) { living++; last = p; }
            else if (!_eliminatedAnnounced[p])
            {
                _eliminatedAnnounced[p] = true;
                _events.Add(new GameEvent(GameEventType.PlayerEliminated, -1, p));
            }
        }
        if (living == 1) Winner = last;
    }

    /// <summary>Hash of everything gameplay-relevant; exchanged between clients for desync detection (US1.2).</summary>
    public ulong ComputeStateHash()
    {
        var h = StateHash.Create();
        h.Add(Tick);
        h.Add(Winner);
        h.Add(ShortGameEnabled);
        h.Add(_rng.State);
        h.Add(_entities.Count);
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            h.Add(e.Id); h.Add(e.Alive); h.Add(e.PlayerId); h.Add((int)e.Kind);
            h.Add(e.X); h.Add(e.Y); h.Add(e.TargetX); h.Add(e.TargetY);
            h.Add(e.Moving); h.Add(e.UseFlow); h.Add(e.Speed);
            h.Add(e.Hp); h.Add((int)e.Armour); h.Add(e.WeaponId); h.Add(e.Cooldown); h.Add(e.ExplicitTarget);
            h.Add((int)e.HState); h.Add(e.Carry); h.Add(e.StateTicks);
            h.Add(e.FieldId); h.Add(e.RefineryId); h.Add(e.FerriteAmount);
            h.Add(e.PowerSupply); h.Add(e.PowerDraw); h.Add(e.BuildProgress);
            h.Add(e.PrevX); h.Add(e.PrevY); h.Add(e.StallTicks); h.Add(e.BuildPaid);
            h.Add(e.AMove); h.Add(e.AMoveX); h.Add(e.AMoveY); h.Add(e.StructType);
            h.Add(e.MaxHp); h.Add(e.Repairing); h.Add(e.ReadyStructure);
            h.Add(e.Stealth); h.Add(e.Detector); h.Add(e.RevealTicks); h.Add(e.DetectedMask);
            h.Add(e.Kills); h.Add(e.Rank); h.Add(e.VetEnabled); h.Add(e.UnitType);
            h.Add(e.ChargeTicks); h.Add(e.StrikeTicks); h.Add(e.StrikeX); h.Add(e.StrikeY);
            h.Add(e.FieldCloaked);
            // ADR-007: rally is sim state, so it is hashed state, appended
            // after FieldCloaked in declaration order (the save order too).
            h.Add(e.RallyX); h.Add(e.RallyY); h.Add(e.HasRally); h.Add(e.Departing);
            if (e.Kind == EntityKind.Factory && _queues.TryGetValue(e.Id, out var q))
            { h.Add(q.Count); foreach (int t in q) h.Add(t); }
        }
        if (_orderQueues.Count > 0)
        {
            var qids = new List<int>(_orderQueues.Keys);
            qids.Sort();
            foreach (int id in qids)
            {
                var q = _orderQueues[id];
                h.Add(id); h.Add(q.Count);
                foreach (var c in q)
                {
                    h.Add((int)c.Type); h.Add(c.EntityId); h.Add(c.AuxId);
                    h.Add(c.X); h.Add(c.Y); h.Add(c.PlayerId); h.Add(c.Queued);
                }
            }
        }
        for (int p = 0; p < _players; p++)
        {
            h.Add(_playerFaction[p]);
            h.Add(_credits[p]);
            var exp = _explored[p];
            for (int w = 0; w < exp.Length; w++) h.Add(exp[w]);
        }
        return h.Value;
    }

    /// <summary>Render snapshot for the presentation layer (feeds TICKET-P1-07). Read-only copy; the renderer never touches live state.</summary>
    public (int Tick, Entity[] Entities, long[] Credits) TakeSnapshot()
        => (Tick, _entities.ToArray(), (long[])_credits.Clone());
}
