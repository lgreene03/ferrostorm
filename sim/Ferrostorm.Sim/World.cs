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
    SetStance = 17,      // EntityId (own unit) stance = (Stance)AuxId; Patrol reads X/Y as the far waypoint (ADR-015)
    LoadTransport = 18,  // P7-3: EntityId (own infantry) boards transport AuxId; walks to it if out of reach
    UnloadTransport = 19,// P7-3: EntityId (own transport) sets its whole cargo down around itself
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
// Emplacement and Bastion are reservations only (doc 23 s4.1), taken because
// reserving is free and a later collision with a saved byte is silent and
// fatal. Outpost (17) graduated under ADR-021 and Bridge (18) under ADR-025.
// Gate (20) graduated under P7-10, the second barrier ADR-005 reserved STRUCT
// type 10 for. The kind number is appended at the end of this enum rather than
// borrowed from that reservation, because the two numbering spaces are
// different and ADR-005 line 76 records that confusing them is silent and fatal.
// APPEND ONLY: the byte is written into saves.
public enum EntityKind : byte { Unit = 0, Harvester = 1, Refinery = 2, FerriteField = 3, PowerPlant = 4, Factory = 5, ConstructionYard = 6, Turret = 7, Superweapon = 8, VeilProjector = 9, ServiceDepot = 10, Wall = 11, Barracks = 12, RadarUplink = 13, Airfield = 14, Emplacement = 15, Bastion = 16, Outpost = 17, Bridge = 18, Mine = 19, Gate = 20, WatchPost = 21 }
public enum HarvestState : byte { Idle = 0, ToField = 1, Loading = 2, ToRefinery = 3, Unloading = 4 }

/// <summary>
/// Which tab of the build sidebar offers a structure, authored per building in
/// /data. PRESENTATION, and the sim reads it nowhere: it cannot change which
/// commands are accepted, so it is deliberately NOT folded into
/// <see cref="World.CatalogueChecksum"/> - two peers with different tabs would
/// play the same match with the same army from the same command stream, which
/// is the exact test ADR-032 sets for what must ride the checksum. Keeping it
/// out also means adding it moves no save and refuses no replay.
///
/// It exists because the split is EDITORIAL and nothing derivable stands in for
/// it. The client kept two hand-written arrays instead, and they fell behind the
/// catalogue the way the unit array did before them: the mine had to be added to
/// one by hand in the wave that shipped it.
///
/// <see cref="None"/> is no button at all, which the three map-placed buildings
/// carry (the MCV-deployed yard, the outpost and the bridge). That is the same
/// set reachabilitygate excludes, and StructureCatalogue.ToTypeDef refuses any
/// file where the two disagree rather than letting a second list grow here.
/// </summary>
public enum BuildTab : byte { None = 0, Buildings = 1, Defence = 2 }

// APPEND ONLY (like EntityKind): the hash stores (int)e.Stance and the save
// writes (byte)e.Stance, so a new value is invisible to both for every existing
// stance; renumbering one rewrites every golden and every replay. Aggressive is
// 0, which is the struct default and the neutral serialised value, so an unset
// stance and a pre-v7 save both read Aggressive - today's behaviour (ADR-015).
public enum Stance : byte { Aggressive = 0, HoldFire = 1, Guard = 2, Patrol = 3 }

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

    // Ferrite regrowth cap (ADR-012), appended after Departing, never inserted.
    // A FerriteField's spawn amount, the ceiling regrowth restores towards. Set
    // once at spawn and never mutated, so like Sight it is SERIALIZED (save v5)
    // but deliberately NOT part of ComputeStateHash: it is immutable spawn-time
    // provenance, identical on every client, and hashing it would move every
    // golden (including the no-ferrite scenarios ADR-012 requires stay still)
    // for zero behavioural change. Zero on every non-field entity.
    public int FerriteCap;

    // No-progress settle backstop (Q013, ADR-014), appended after FerriteCap,
    // never inserted. NearestApproachSq is the closest squared distance to the
    // current flow destination this walk has achieved; NoProgressTicks counts
    // ticks since it last improved. A flow-pathing unit that fails to better its
    // nearest approach for NoProgressDeadline ticks is orbiting a crowd rim and
    // is benched where it stands. Both are mutable per-tick sim state that gates
    // when a unit stops moving, so - unlike the immutable FerriteCap above -
    // they ARE hashed and serialized (save v6), following the rally-fields
    // precedent for mutable movement state. A Raw-zero NearestApproachSq is the
    // "unseeded" sentinel: the first eligible tick after a fresh order seeds it
    // from the live distance, so a re-order re-arms simply by zeroing it.
    public Fix64 NearestApproachSq;
    public int NoProgressTicks;

    // Unit command stances (ADR-015), appended after the ADR-014 backstop tail,
    // never inserted. Hash order and save order are this declaration order. All
    // six are HASHED and SERIALIZED (save v7), following the ADR-007 rally
    // precedent for mutable per-entity command state: a stance changes what a
    // unit does, so two clients must agree on it exactly. Aggressive (0) is the
    // default and every field is zero on a fresh spawn, which is today's
    // behaviour, so nothing moves until a SetStance sets a non-default value.
    // Stance carries the whole state; the four stances are mutually exclusive by
    // construction. PostX/PostY is the guard POST or the patrol ORIGIN (endpoint
    // A) - the unit's position when the order was given. PatrolX/PatrolY is the
    // patrol FAR point (endpoint B), zero for non-patrol. PatrolOutbound is the
    // patrol heading: true toward (PatrolX,PatrolY), false toward (PostX,PostY).
    public Stance Stance;
    public Fix64 PostX, PostY;
    public Fix64 PatrolX, PatrolY;
    public bool PatrolOutbound;
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
    // P7-11a: A structure, B the saboteur's player, C the tick it comes back.
    // NOT Captured, though the infiltrator's theft borrows that type: the
    // client's DR-20 alert reads Captured as an ownership change and caches the
    // new owner from it, so raising it for a building that never changed hands
    // would announce "you lost it" about a building the player still owns. The
    // whole distinction this unit exists to make is sabotage against capture,
    // and it has to survive as far as the alert.
    Sabotaged = 12,
    // P7-7a: A structure, B the thief's player, C the credits taken. The
    // Infiltrator raised Captured for this from P7-7 until now, and the comment
    // above was written describing that as a thing to avoid while it was
    // already happening. It is the same argument: a robbery is not an
    // ownership change, and the building's flag never moves.
    Robbed = 13,
}
public readonly record struct GameEvent(GameEventType Type, int A, int B, Fix64 X = default, Fix64 Y = default, int C = -1);

public sealed partial class World
{
    public const int TicksPerSecond = 15;

    // Q013 / ADR-014: the no-progress settle backstop deadline. A flow-pathing
    // unit that has not bettered its nearest approach to the destination for
    // this many ticks is benched where it stands. 210 ticks (14s) is 3.5x the
    // 60-tick (4s) StallTicks net and sits comfortably above the 132-tick
    // worst-case legitimate queue plateau measured across the five soak seeds,
    // so it only ever catches a genuine limit-cycle orbit, never a unit that is
    // still filtering through a chokepoint. Measurement recorded on Q013.
    public const int NoProgressDeadline = 14 * TicksPerSecond;

    /// <summary>Winner player id once decided, -1 while the match is live (TICKET-P2-SIM-12). The sim keeps stepping after; presentation decides what "game over" means.</summary>
    public int Winner { get; private set; } = -1;

    // Faction identity (TICKET-P3-FAC-01): 0 = Directorate, 1 = Sodality.
    // Production of faction-locked units and structures is refused for the
    // wrong side - asymmetry is enforced, not advisory.
    public const int FactionDirectorate = 0;
    public const int FactionSodality = 1;
    private readonly byte[] _playerFaction;
    /// <summary>The player's side, fixed before tick 0.
    ///
    /// This was a bare array write with NO GUARD, found while writing SetTeam's
    /// and noted then rather than fixed. The faction is HASHED state, so a call
    /// after tick 0 would change the state hash mid-match: every replay of that
    /// match would then diverge at the tick it happened, and in LAN the two
    /// peers would part company the instant one of them made the call. Nothing
    /// does today - both call sites run before the first Step - which is why it
    /// has cost nothing and why it is a trap rather than a bug.
    ///
    /// Guarded on the same terms as SetTeam and the catalogue registrars, so
    /// the three things that are "match setup, frozen at the start" now all say
    /// so in the same way instead of two saying it and one meaning it.</summary>
    public void SetFaction(int player, int faction)
    {
        if (Tick != 0) throw new InvalidOperationException("factions are fixed once the match starts");
        if ((uint)player >= (uint)_players)
            throw new ArgumentOutOfRangeException(nameof(player), $"no seat {player} in a {_players}-player world");
        _playerFaction[player] = (byte)faction;
    }
    public int FactionOf(int player) => _playerFaction[player];

    // Teams and alliances (P7-8c). GDD s9 promises "custom lobbies up to 4v4"
    // and there was no notion of a side larger than a seat. This is it: ONE
    // TEAM ID PER PLAYER, DEFAULTING TO THE PLAYER'S OWN ID.
    //
    // The default is the whole design rather than a convenience. Every player
    // starts on a team of one, so a free-for-all is unchanged BY CONSTRUCTION:
    // every expression teams touch reduces to the comparison that stood there
    // before, and all 24 goldens stay byte-identical without any scenario
    // knowing teams exist. Nothing here is a special case for "no teams set",
    // because there is no such state - the identity map IS the free-for-all.
    //
    // A team id is a player id, so the range is the seat range. That keeps
    // TeamOf's answer indexable (VictorySystem counts living teams in a span of
    // _players) and means a lobby names a team by naming a seat.
    private readonly int[] _playerTeam;

    /// <summary>
    /// P7-8c: put a player on a team. Settable before tick 0 and frozen after,
    /// mirroring RegisterUnitType and ConfigureRegrowth and refusing the same
    /// way: a mid-match alliance change would be a silent replay divergence,
    /// because the team map is hashed state that no command stream carries.
    ///
    /// Both ids are range-checked. A team id outside the seat range would index
    /// VictorySystem's living-team span out of bounds, and a team of -1 would
    /// collide with TeamOf's neutral answer.
    /// </summary>
    public void SetTeam(int player, int team)
    {
        if (Tick != 0) throw new InvalidOperationException("teams are fixed once the match starts");
        if ((uint)player >= (uint)_players)
            throw new ArgumentOutOfRangeException(nameof(player), $"no seat {player} in a {_players}-player world");
        if ((uint)team >= (uint)_players)
            throw new ArgumentOutOfRangeException(nameof(team), $"a team id is a seat id, so it must be 0..{_players - 1}, not {team}");
        _playerTeam[player] = team;
    }

    /// <summary>
    /// P7-8c: which team a player fights for. Its own id unless a lobby said
    /// otherwise.
    ///
    /// A player id outside the seat range answers with itself, which is what
    /// keeps a NEUTRAL (-1) hostile to everybody exactly as it was before teams
    /// existed: -1 is not a legal team id, so it equals nobody's team.
    /// </summary>
    public int TeamOf(int player) => (uint)player < (uint)_players ? _playerTeam[player] : player;

    /// <summary>
    /// P7-8c: "is this entity on player P's side?" - which INCLUDES P's own,
    /// because an ally and yourself are the same answer to every question that
    /// asks this. The complement of <see cref="IsEnemyOf"/> over owned things.
    ///
    /// A neutral is nobody's ally, from the PlayerId >= 0 clause, which is what
    /// leaves ADR-021's neutral outpost capturable: the contact rule asks "not
    /// allied", and a rock is not allied to anyone.
    /// </summary>
    public bool IsAlliedTo(in Entity e, int player)
        => e.PlayerId >= 0 && TeamOf(e.PlayerId) == TeamOf(player);

    private readonly List<GameEvent> _events = new();
    /// <summary>Events emitted by the tick that just ran; cleared at the start of every Step.</summary>
    public IReadOnlyList<GameEvent> Events => _events;
    private readonly bool[] _eliminatedAnnounced;

    // Economy constants (GDD s4). /data wiring is a Phase 2 ticket.
    public const int HarvesterCapacity = 700;
    public const int LoadPerTick = 10;                       // 70 ticks to fill
    public const int UnloadTicks = 8 * TicksPerSecond;       // refinery processes a load in 8s

    // Ferrite regrowth (ADR-012). The compiled reference twin of the /data
    // field definition (data/fields/com_ferrite_field.yaml): 1 unit every 75
    // ticks (5 seconds at 15Hz), a deliberate trickle against harvest rates an
    // order of magnitude higher. Every new World starts on these; a caller that
    // loads /data overrides them via ConfigureRegrowth, and the selftest proves
    // the file reproduces these exactly. Not hashed and not serialized: they are
    // pre-tick-0 config, re-supplied on load exactly as the catalogue is.
    public const int DefaultRegrowAmount = 1;
    public const int DefaultRegrowIntervalTicks = 75;
    private int _regrowAmount = DefaultRegrowAmount;
    private int _regrowIntervalTicks = DefaultRegrowIntervalTicks;

    /// <summary>ADR-012 / ADR-006: Balance's regrowth numbers from /data, applied
    /// before tick 0, mirroring RegisterUnitType. After tick 0 the numbers are
    /// frozen: a mid-match change would be a silent replay divergence. An interval
    /// below 1 is refused so the per-tick schedule modulo never divides by zero.</summary>
    public void ConfigureRegrowth(int amount, int intervalTicks)
    {
        if (Tick != 0) throw new InvalidOperationException("regrowth numbers are fixed once the match starts");
        if (intervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(intervalTicks), "regrow interval must be at least 1 tick");
        _regrowAmount = amount;
        _regrowIntervalTicks = intervalTicks;
    }

    private readonly List<Entity> _entities = new();
    private readonly DeterministicRandom _rng;
    private readonly FlowFieldCache _flow = new();
    public readonly Map Map;
    public int Tick { get; private set; } // set by Step and by Load

    private readonly int _players;
    /// <summary>How many seats this world was built with (P7-8a). Read-only and
    /// stateless, so it hashes nothing and moves no golden. It exists because
    /// every generalisation away from "exactly two players" needs to ask the
    /// WORLD how many there are rather than assume: MapLoader's opening hand is
    /// the first caller, and VictorySystem below has always used _players
    /// directly for the same reason.</summary>
    public int PlayerCount => _players;
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
        // P7-8c: everyone on their own team until a lobby says otherwise, which
        // is what makes a free-for-all the default by construction rather than
        // by a special case anywhere downstream.
        _playerTeam = new int[players];
        for (int p = 0; p < players; p++) _playerTeam[p] = p;
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

    /// <summary>The harvester, read from the catalogue like every other unit.
    ///
    /// This is the OLDEST spawner in the file and it predates the catalogue
    /// entirely: it hardcoded hit points, armour, sight and speed, and it never
    /// stamped a UnitType at all. Two consequences, and the second is the one
    /// that had been costing something the whole time.
    ///
    /// With no UnitType, every harvester in the game stood as type 0, so its
    /// authored def could not be read back off the entity. `AtMaxAlive`,
    /// `IsAirborne` and the client's name and model lookups were all blind to
    /// it, and one runner check had already been written around the gap rather
    /// than against it.
    ///
    /// And the hardcoded speed DIVERGED. `Fix64.FromFraction(1, 5)` is 0.20;
    /// `com_harvester.yaml` authors `speed: 18`, which is 0.18. Every harvester
    /// in the game moved eleven per cent faster than the file that is supposed
    /// to define it, so every economy measurement this project has ever taken
    /// was taken against a number nobody wrote down. That is P7-1's defect -
    /// authored data that does not drive the runtime - in the place it has sat
    /// longest.
    ///
    /// The other three values happened to match, which is exactly why this
    /// survived: a mostly-correct copy is harder to notice than a wrong one.
    /// </summary>
    public int SpawnHarvester(int player, Fix64 x, Fix64 y)
    {
        var def = GetUnitType(HarvesterUnitType);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Harvester,
            X = x, Y = y, TargetX = x, TargetY = y, Speed = def.Speed,
            Hp = def.Hp, MaxHp = def.Hp, Armour = def.Armour, WeaponId = def.WeaponId,
            ExplicitTarget = -1, Sight = Fix64.FromInt(def.SightCells),
            FieldId = -1, RefineryId = -1, UnitType = HarvesterUnitType,
        });
    }

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
            FerriteAmount = amount, FerriteCap = amount, FieldId = -1, RefineryId = -1,
        });

    // Producible unit types (TICKET-P2-SIM-03). Compiled defaults serve also as the
    // reference values for the /data loader round-trip test (TICKET-P2-DATA-02);
    // matches can overwrite or extend the catalogue before tick 0 via
    // RegisterUnitType. The catalogue is static config and is therefore not
    // part of the state hash; it carries its own CatalogueChecksum instead.
    // This comment used to say "like weapons", meaning the compiled table in
    // Combat.cs that no registration path could reach. Weapons are a REGISTERED
    // catalogue themselves now (see _weaponTypes), so the contrast it drew is
    // gone and the rule simply covers all three.
    public const int FactionCommon = 2;
    /// <summary>
    /// Prereqs (structure type ids that must stand alive) and ProducedAt (the
    /// structure type whose queue builds this unit; 2 = the factory) are READ
    /// since ADR-009: ProducedAt IS the barracks split and Prereqs gates the
    /// queue. They were carried unread from Wave 2 so that the data could
    /// round-trip before the decision that reads it. Trailing and defaulted so
    /// the twelve compiled entries keep compiling. Equality is declared by hand
    /// because the synthesized record comparison would test the Prereqs ARRAY
    /// REFERENCE, and the selftest round-trip (/data file == compiled def)
    /// compares defs built on both sides of that reference.
    /// </summary>
    public readonly record struct UnitTypeDef(int Cost, int BuildTicks, int Hp, ArmourClass Armour, int WeaponId, Fix64 Speed,
        EntityKind Kind = EntityKind.Unit, bool Stealth = false, bool Detector = false, bool Veterancy = true, int SightCells = 5,
        int Faction = FactionCommon, int[]? Prereqs = null, int ProducedAt = 2, bool Air = false,
        int MaxAlive = 0)
    {
        public bool Equals(UnitTypeDef other)
            => Cost == other.Cost && BuildTicks == other.BuildTicks && Hp == other.Hp
            && Armour == other.Armour && WeaponId == other.WeaponId && Speed == other.Speed
            && Kind == other.Kind && Stealth == other.Stealth && Detector == other.Detector
            && Veterancy == other.Veterancy && SightCells == other.SightCells && Faction == other.Faction
            && ProducedAt == other.ProducedAt && MaxAlive == other.MaxAlive
            // Air was missing from this comparison and from the checksum fold
            // below, from ADR-028 until now. Both omissions were silent and the
            // second was the dangerous one: a drifting `air:` key was invisible
            // to the /data round-trip selftest AND to the LAN desync guard, so
            // two peers could disagree about which units FLY while every unit,
            // building and gun still matched. That is precisely the failure
            // ADR-032 clause 2 names, in a field that predates the rule.
            && Air == other.Air
            && PrereqsEqual(Prereqs, other.Prereqs);
        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(Cost); h.Add(BuildTicks); h.Add(Hp); h.Add(Armour); h.Add(WeaponId); h.Add(Speed);
            h.Add(Kind); h.Add(Stealth); h.Add(Detector); h.Add(Veterancy); h.Add(SightCells);
            h.Add(Faction); h.Add(ProducedAt); h.Add(MaxAlive); h.Add(Air);
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
        // selftest proves it) and are ENFORCED since ADR-009: the infantry
        // trio names the barracks (struct type 11) as producer, everything
        // else takes the factory default. The four [com_factory] entries that
        // are tautologies under produced_at (mcv, howitzer, bulwark, phantom)
        // are left AS AUTHORED and enforced honestly - they are trivially
        // satisfied at the factory the unit already comes out of, so no
        // behaviour hides behind them - because rewriting them is the Q006
        // and Q007 curation ADR-009's gates clause holds for the Game
        // Designer.
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
        // Repair vehicle (ADR-019, P6 Wave C2): common, unarmed, mends own units
        // in the field. Prereq the Service Depot (struct type 8), so field repair
        // unlocks behind the repair building it extends; produced at the factory
        // (the ProducedAt default 2). Veterancy off, like the MCV and harvester.
        // The heal behaviour is hardcoded in ProductionSystem, not a stat.
        { 13, new UnitTypeDef(700, 120, 300, ArmourClass.Light, 0, Fix64.FromFraction(1, 5), Veterancy: false, Prereqs: new[] { 8 }) }, // com_repair_vehicle
        // Carrier (P7-3): the transport, and the first unit that exists to move
        // OTHER units. Unarmed on purpose - a transport that fights is a light
        // tank that happens to carry, and deciding whether to escort it is the
        // whole point. Prereq the barracks (struct type 11), because what it
        // carries is what the barracks makes.
        { 14, new UnitTypeDef(600, 140, 350, ArmourClass.Light, 0, Fix64.FromFraction(22, 100), Veterancy: false, SightCells: 6, Prereqs: new[] { 11 }) }, // com_carrier
        // ADR-028: the strike aircraft. Air true is what takes it off the flow
        // field and out of reach of every ground weapon; produced at the
        // airfield (struct type 16), which is its own prerequisite.
        { 15, new UnitTypeDef(1100, 200, 180, ArmourClass.Light, 1, Fix64.FromFraction(45, 100), SightCells: 8, Prereqs: new[] { 16 }, ProducedAt: 16, Air: true) }, // com_strike_flyer
        // ADR-028 clause 4: the answer, and it ships in the same wave by the
        // ADR's own binding. Mobile, because what air threatens most is the
        // army in the field rather than the base.
        { 16, new UnitTypeDef(550, 130, 260, ArmourClass.Light, 9, Fix64.FromFraction(26, 100), SightCells: 7, Prereqs: new[] { 12 }) }, // com_flak_track
        // P7-7: the Infiltrator. Cloaked, unarmed, barracks-built, and consumed
        // by the theft exactly as the engineer is consumed by capture.
        { 17, new UnitTypeDef(700, 150, 90, ArmourClass.None, 0, Fix64.FromFraction(1, 5), Stealth: true, Veterancy: false, SightCells: 5, Faction: FactionSodality, Prereqs: new[] { 12 }, ProducedAt: 11) }, // sod_infiltrator
        // P7-11a: the Saboteur, the Infiltrator's twin down to the fixture.
        // Cloaked, unarmed, barracks-built and consumed by the act; it differs
        // only in what the act DOES, which is to switch a building off rather
        // than to rob it. Cheaper and frailer than the thief because a brown-out
        // is a window rather than a payday.
        { 18, new UnitTypeDef(600, 140, 80, ArmourClass.None, 0, Fix64.FromFraction(1, 5), Stealth: true, Veterancy: false, SightCells: 5, Faction: FactionSodality, Prereqs: new[] { 12 }, ProducedAt: 11) }, // sod_saboteur
        // P7-11b: GDD s7's two heroes. ONE unit authored twice, differing by
        // faction and by Stealth alone - the P7-2b Bastion / Shroud Nest
        // precedent - so the two lines below are deliberately identical
        // everywhere else and must be edited together. MaxAlive 1 is the GDD's
        // own "one at a time", and it is the only non-zero MaxAlive in the
        // catalogue, which is why the cap enforcement is inert for every other
        // unit and the goldens do not move.
        { 19, new UnitTypeDef(1500, 300, 200, ArmourClass.None, 10, Fix64.FromFraction(1, 5), Veterancy: false, SightCells: 6, Faction: FactionDirectorate, Prereqs: new[] { 12 }, ProducedAt: 11, MaxAlive: 1) }, // dir_commando
        { 20, new UnitTypeDef(1500, 300, 200, ArmourClass.None, 10, Fix64.FromFraction(1, 5), Stealth: true, Veterancy: false, SightCells: 6, Faction: FactionSodality, Prereqs: new[] { 12 }, ProducedAt: 11, MaxAlive: 1) }, // sod_shadow_commando
    };
    public UnitTypeDef GetUnitType(int typeId) => _unitTypes.TryGetValue(typeId, out var d) ? d : default;

    /// <summary>
    /// Every unit type id this world has registered, ASCENDING. A read, and only
    /// a read: a fresh list each call, so a caller may keep it and the catalogue
    /// is never handed a mutable view of itself.
    ///
    /// Sorted for the reason CatalogueChecksum sorts the same keys: dictionary
    /// iteration order must never leak into anything a player or a peer can see,
    /// and a sidebar whose buttons came out in a different order on two machines
    /// would make the grid hotkeys mean different things at each seat.
    ///
    /// It exists because the client had no way to ASK what the catalogue holds,
    /// so the build sidebar kept a hand-written list of units beside it that had
    /// fallen seven behind - a rule keyed on an instance where it should key on
    /// the property, which is this project's most-repeated defect.
    /// </summary>
    public IReadOnlyList<int> UnitTypeIds()
    {
        var ids = new List<int>(_unitTypes.Keys);
        ids.Sort();
        return ids;
    }

    public void RegisterUnitType(int typeId, UnitTypeDef def)
    {
        if (Tick != 0) throw new InvalidOperationException("catalogue is fixed once the match starts");
        _unitTypes[typeId] = def;
    }

    private readonly Dictionary<int, List<int>> _queues = new(); // factory id -> queued type ids (keyed access only)

    /// <summary>
    /// ADR-023: the Construction Yard's SECOND build lane. A lane carries its own
    /// queue, head progress, paid-so-far and ready slot, so a structure and a
    /// second structure build simultaneously (GDD line 45).
    ///
    /// It lives here rather than as four more Entity fields deliberately: an
    /// Entity tail append is hashed for EVERY entity in the game and would move
    /// all 24 goldens mechanically (the ADR-014/015 pattern), to give a second
    /// line to the one kind that can use it.
    /// </summary>
    public sealed class BuildLane
    {
        public readonly List<int> Queue = new();
        public int Progress;
        public int Paid;
        public int Ready;
        /// <summary>Nothing queued, nothing building, nothing waiting to place.
        /// The PRUNE predicate and the HASH GUARD are the same expression on
        /// purpose: "an entry exists" must mean "this lane is doing something",
        /// or the guarded fold would skip state that gates behaviour, which is a
        /// silent desync rather than an optimisation.</summary>
        public bool Inert => Queue.Count == 0 && Progress == 0 && Paid == 0 && Ready == 0;
    }
    private readonly Dictionary<int, BuildLane> _lanes = new(); // yard id -> its second lane, ONLY while active

    /// <summary>P7-3: what a transport is carrying, keyed by transport id and
    /// held ONLY while it carries something. A carried unit is despawned - it
    /// is not a live entity that every system has to remember to skip, which
    /// is the enumeration trap this phase has already been bitten by three
    /// times - and what is kept is exactly enough to put it back: its type, its
    /// health and its rank.
    ///
    /// A pruned side collection, the ADR-023 lane pattern, and pruned for the
    /// same reason: the entry is removed the instant the hold empties, so "no
    /// entry" provably means "no state that could gate behaviour", which is
    /// what makes the guarded hash fold sound rather than merely convenient.
    /// A world with no transport carries no entry and hashes identically.</summary>
    public readonly record struct CargoUnit(int UnitType, int Hp, int Rank);
    private readonly Dictionary<int, List<CargoUnit>> _cargo = new();

    /// <summary>P7-11a: which structures are switched off, keyed by entity id
    /// and holding the tick each one comes back. A pruned side collection on
    /// exactly the _cargo terms and for exactly its reason: the entry is removed
    /// the tick the sabotage lapses, so "no entry" provably means "no state that
    /// could gate behaviour", which is what makes the guarded hash fold sound
    /// rather than merely convenient. A world with no saboteur in it carries no
    /// entry and hashes byte-identically to one compiled before saboteurs
    /// existed, which is why all 24 goldens stand.
    ///
    /// A tick STAMP rather than a countdown, the ADR-012 regrowth idiom: a
    /// stamp resumes correctly across save and load with no schedule to rewind,
    /// and it makes extending an existing sabotage a max rather than a sum.</summary>
    private readonly Dictionary<int, int> _disabledUntil = new();   // entityId -> tick it comes back

    /// <summary>P7-11a: how long one saboteur switches a building off for. Thirty
    /// seconds at 15 Hz. The duration is the only part of GDD s7 line 64 that is
    /// not written down, so it is recorded as my call: long enough that a
    /// brown-out is a window to attack through, short enough that losing a plant
    /// to one infantryman is a setback rather than a loss. Balance owns the
    /// number under charter A11.</summary>
    public const int SabotageDurationTicks = 450;

    /// <summary>P7-11a: is this building switched off? The ONE place the rule is
    /// stated, read by the power tally, the structure-weapon gate and the
    /// production loop, so those three cannot drift from each other or from the
    /// prune in Step. An absent entry and a lapsed entry answer the same, which
    /// is what lets the prune run on a schedule rather than at an exact tick.</summary>
    public bool IsDisabled(int entityId)
        => _disabledUntil.TryGetValue(entityId, out int until) && until > Tick;

    /// <summary>P7-3: how many a transport holds. Five is the benchmark figure
    /// and it is the number that makes an engineer plus an escort fit.</summary>
    public const int CarrierCapacity = 5;
    public const int CarrierUnitType = 14;

    /// <summary>P7-3: what a transport is carrying, for the client and the
    /// gates. Empty for anything that is not carrying, including anything that
    /// is not a transport.</summary>
    public IReadOnlyList<CargoUnit> CargoOf(int transportId)
        => _cargo.TryGetValue(transportId, out var c) ? c : System.Array.Empty<CargoUnit>();

    /// <summary>P7-3: infantry is what a transport carries, and it is a DATA
    /// question, not a hardcoded list - anything the barracks produces fits.
    /// That admits the engineer, which is the point: delivering one under fire
    /// is the play this unit exists to make possible.</summary>
    /// <summary>ADR-028: does this entity fly? Answered from the CATALOGUE by
    /// unit type, never from a hashed per-entity flag - a flag would move all
    /// 24 goldens for no behavioural change, which is the cost ADR-012 refused
    /// for FerriteCap. Structures never fly, whatever their type id happens to
    /// collide with in the unit table.</summary>
    public bool IsAirborne(in Entity e)
        => e.Kind == EntityKind.Unit && GetUnitType(e.UnitType).Air;

    /// <summary>ADR-028 clause 3, stated ONCE: a weapon engages air if and only
    /// if it is an anti-air weapon. Read the equality both ways, because both
    /// directions are load-bearing - a ground gun cannot reach a plane, AND a
    /// dedicated anti-air gun cannot shoot the ground. The first draft wrote
    /// this as `AntiAir || !IsAirborne`, meaning "can ALSO hit air", and the
    /// gate caught the consequence immediately: the flak track was a better
    /// tank as well as the answer to aircraft, which would have made the
    /// counter a straight upgrade.
    ///
    /// All THREE target-selection paths ask this - the explicit order, the main
    /// auto-acquire scan and the guard stance's leash scan - rather than each
    /// testing the flag itself. A first pass guarded two of the three and the
    /// gate shot a plane down with a rifle.</summary>
    public bool WeaponCanEngage(in WeaponDef w, in Entity target)
        => WeaponCanEngage(w.AntiAir, in target);

    /// <summary>P7-8g: clause 3 taking the FLAG rather than the weapon, so a
    /// shooter that has no WeaponDef can still ask it. The mine is one: it
    /// carries WeaponId 0 and a blast radius, and it is not anti-air.</summary>
    public bool WeaponCanEngage(bool antiAir, in Entity target)
        => antiAir == IsAirborne(target);

    /// <summary>
    /// P7-8g: "is this entity player P's OWN?" One of the TWO questions the sim
    /// answered everywhere with the same hand-written comparison, and the reason
    /// for pulling them apart is that teams (P7-8c) is exactly what separates
    /// them: a teammate's unit is neither yours nor an enemy.
    ///
    /// Ownership is what commanding, loading a transport, repairing, selling,
    /// placing and every "count my own" census ask. It is NOT friendliness. When
    /// alliances land this predicate must NOT change: an ally's tank is still not
    /// yours to order about.
    ///
    /// A neutral (PlayerId -1) is nobody's own, which falls out of the compare
    /// rather than needing a clause of its own.
    /// </summary>
    public static bool IsOwnedBy(in Entity e, int player) => e.PlayerId == player;

    /// <summary>
    /// P7-8g: "is this entity an ENEMY of player P?" The other question, asked by
    /// target acquisition, the mine trigger, the crush rule, the attack-move
    /// completion test and every scan the AI runs.
    ///
    /// Today an enemy is anything owned by somebody else. The PlayerId >= 0
    /// clause is what keeps the neutrals out - ferrite fields, uncaptured
    /// outposts and bridges - which is why a wave does not march on a rock and a
    /// minefield does not go off under a bridge.
    ///
    /// P7-8c CHANGED EXACTLY THIS EXPRESSION and nothing else in the sim needed
    /// editing, which is the whole reason for naming it. The body is now "owned
    /// by somebody AND not allied to P". The air layer was added by editing each
    /// site that needed it by hand, and ADR-028 records that the first pass
    /// guarded two of three paths and shot an aircraft down with a rifle, while
    /// the mine - a fourth path - was missed again in its own first draft. A
    /// question spelled out by hand at every site is a question somebody forgets.
    ///
    /// It is an INSTANCE method now, because the answer depends on the world's
    /// team map. With the default map (everyone on their own team) it reduces to
    /// the `e.PlayerId != player` it replaced, term for term, which is why the
    /// goldens do not move.
    /// </summary>
    public bool IsEnemyOf(in Entity e, int player) => e.PlayerId >= 0 && !IsAlliedTo(in e, player);

    /// <summary>
    /// P7-8g: the ONE entry point for a path that PICKS SOMETHING TO SHOOT. It
    /// asks the two questions such a path can never skip - is the candidate an
    /// enemy, and can the gun doing the picking reach it - so a new scan cannot
    /// acquire a target while leaving either unasked. The anti-air flag is a
    /// required argument for precisely that reason: there is no way to call this
    /// and quietly skip the air question, which is the failure ADR-028 and the
    /// mine each paid for once.
    ///
    /// EVERY path that chooses a victim, and what each of them asks:
    ///   1. CombatSystem's auto-acquire scan - here, plus the stealth test and
    ///      the ADR-005 clause 2 skip of barriers and ferrite fields.
    ///   2. StanceSystem's Guard leash scan - here, plus those same two.
    ///   3. MineSystem's proximity trigger - here, with antiAir false, because a
    ///      buried charge cannot reach a plane; plus its Unit-or-Harvester
    ///      filter. It deliberately does NOT ask CanTarget: a cloaked scout still
    ///      treads on a mine.
    ///   4. CombatSystem's explicit-target branch VALIDATES a target it was
    ///      handed rather than picking one, so it asks CanTarget and
    ///      WeaponCanEngage directly and asks no hostility question at all - an
    ///      ordered Attack may legitimately be aimed at a wall or a bridge.
    ///   5. NearestEnemyBarrier, the breach pick, asks hostility alone. What it
    ///      returns becomes an ExplicitTarget, so path 4 is where the air rule
    ///      lands on it and an anti-air unit's breach target is dropped there.
    ///   6. EnemyNearAMovePoint is NOT target selection. It asks whether the
    ///      ordered ground is clear, so it must count enemies this unit cannot
    ///      personally shoot; routing it through here would end an attack-move
    ///      that still had work to do.
    /// </summary>
    private bool CanBeEngagedBy(int byPlayer, bool antiAir, in Entity t)
        => t.Alive && IsEnemyOf(in t, byPlayer) && WeaponCanEngage(antiAir, in t);

    public bool IsCarryable(int unitType)
        => unitType != CarrierUnitType && GetUnitType(unitType).ProducedAt == BarracksStructType;

    /// <summary>ADR-023 clause 6: the bit that marks a CancelProduce AuxId as
    /// addressing the SECOND lane. High enough that no real queue index or
    /// structure type can collide with it, and absent from every command
    /// written before this ADR, so old replays decode unchanged.</summary>
    public const int LaneFlag = 1 << 20;

    /// <summary>ADR-023: the second lane of a yard, or null. Unlike _queues,
    /// whose entries are sticky, a lane entry exists only while the lane is
    /// active; see BuildLane.Inert.</summary>
    private BuildLane? LaneOf(int yardId) => _lanes.TryGetValue(yardId, out var l) ? l : null;

    /// <summary>Drop a lane the moment it goes inert, so "present" means
    /// "active" by construction (ADR-023 clause 3).</summary>
    private void PruneLane(int yardId)
    {
        if (_lanes.TryGetValue(yardId, out var l) && l.Inert) _lanes.Remove(yardId);
    }

    /// <summary>ADR-023: read-only view of a yard's second-lane queue, the
    /// QueueContents twin the sidebar needs to show both lines.</summary>
    public IReadOnlyList<int> LaneContents(int yardId)
        => _lanes.TryGetValue(yardId, out var l) ? l.Queue : System.Array.Empty<int>();

    /// <summary>ADR-023: the second lane's head progress in percent-ticks, its
    /// paid-so-far and its ready structure type (0 = none), for the sidebar.</summary>
    public (int Progress, int Paid, int Ready) LaneState(int yardId)
        => _lanes.TryGetValue(yardId, out var l) ? (l.Progress, l.Paid, l.Ready) : (0, 0, 0);

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
    /// P7-1: Faction joins this def, matching UnitTypeDef which has carried one
    /// since the roster existed. It was missing, and the consequence was a lie
    /// in the data: every building YAML authors a `faction:` line, DataLoader
    /// parses it and VALIDATES it against directorate/sodality/common, and then
    /// the bridge into this def dropped it on the floor. The field was
    /// authored, checked, and enforced by nothing. In its place the sim
    /// hardcoded a single exception naming one structure, so dir_turret.yaml
    /// and dir_superweapon.yaml both said "directorate" while BOTH sides could
    /// build them. That is the ADR-006 class of defect the /data runtime wave
    /// exists to prevent: authored numbers that do not drive the runtime.
    /// Defaults to FactionCommon so every compiled def and every caller that
    /// omits it keeps exactly today's meaning.
    public readonly record struct StructureTypeDef(int Cost, EntityKind Kind, int BuildTicks,
        int Hp = 0, int PowerSupply = 0, int PowerDraw = 0, int SightCells = 0,
        int Footprint = FootprintSize, int WeaponId = 0, int[]? Prereqs = null,
        int Faction = FactionCommon,
        // P7-11c: the per-player cap on LIVING structures of this type, the
        // UnitTypeDef column of the same name for the building catalogue. 0
        // means unlimited, so every building authored before the mine is
        // untouched and the enforcement is a no-op for all of them.
        int MaxAlive = 0,
        // P7-5b: this building reveals stealth within its SightCells, the
        // UnitTypeDef column of the same name for the building catalogue.
        // Until now only a unit could detect, because Entity.Detector was
        // written in exactly one place (SpawnUnit), so the Directorate's mobile
        // Sentinel Scout was the only counter to cloak in the game and the
        // Sodality had none at all.
        bool Detector = false,
        // P7-5e: this building's strike DESTROYS RESOURCE FIELDS, which GDD s8
        // gives to the Sodality seismic charge alone as its economic-warfare
        // identity.
        //
        // Authored rather than derived, and asked rather than named. P7-5c
        // selected that effect with `StructType == SeismicChargeStructType`, a
        // literal - the instance-not-property defect this phase has corrected
        // about fifteen times, and that one was mine from one wave ago. It also
        // left the AI no question it could ask, so a commander holding a
        // field-destroying weapon aimed it at buildings.
        bool DestroysFields = false,
        // Which build tab offers this building, authored in /data. The ONE
        // column on this def the sim never reads: it decides nothing about what
        // a command does, only about where a player finds the button. It is
        // carried here because the catalogue is the only channel from /data to
        // the client, and it is authored rather than derived because the split
        // is EDITORIAL and nothing else on this def implies it - the Airfield
        // is a producer that belongs beside the defences, and the wall, the
        // veil, the superweapon and the mine carry no weapon. See the enum for
        // why it is deliberately absent from CatalogueChecksum.
        BuildTab Tab = BuildTab.None)
    {
        // Hand-declared for the same reason as UnitTypeDef: the synthesized
        // comparison would test the Prereqs array reference and quietly fail
        // the /data round-trip on logically identical defs. Prereqs is READ
        // since ADR-009: BuildStructure refuses a structure whose tree the
        // commanding player has not built.
        public bool Equals(StructureTypeDef other)
            => Cost == other.Cost && Kind == other.Kind && BuildTicks == other.BuildTicks
            && Hp == other.Hp && PowerSupply == other.PowerSupply && PowerDraw == other.PowerDraw
            && SightCells == other.SightCells && Footprint == other.Footprint
            && WeaponId == other.WeaponId && MaxAlive == other.MaxAlive
            // P7-5b: FACTION, which this hand-written comparison has been
            // missing since P7-1 put the field on the def - and the omission
            // matters more than it looks. This Equals is what the /data
            // round-trip selftest uses, so a building whose yaml said one side
            // and whose compiled reference said the other would round-trip
            // CLEAN. P7-5a moved com_power_plant to directorate in both places
            // and would have passed having moved it in either.
            && Faction == other.Faction
            // And Detector with it, so the same hole is not opened by the same
            // field arriving a second time.
            && Detector == other.Detector
            && DestroysFields == other.DestroysFields
            // The tab joins the comparison even though it joins no checksum: it
            // is what makes the /data round-trip in selftest prove the authored
            // key against the compiled reference, which is the only thing that
            // stops a bare World (no /data at all) offering a different sidebar
            // from a loaded one.
            && Tab == other.Tab
            && PrereqsEqual(Prereqs, other.Prereqs);
        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(Cost); h.Add(Kind); h.Add(BuildTicks); h.Add(Hp); h.Add(PowerSupply);
            h.Add(PowerDraw); h.Add(SightCells); h.Add(Footprint); h.Add(WeaponId);
            h.Add(MaxAlive); h.Add(Tab); h.Add(Faction); h.Add(Detector); h.Add(DestroysFields);
            if (Prereqs != null) foreach (int p in Prereqs) h.Add(p);
            return h.ToHashCode();
        }
    }

    /// <summary>The compiled reference catalogue: the values a /data/buildings file must reproduce exactly. Static so that callers with no World (the Balance tool) can price a structure; a live match must read GetStructureType instead, which honours RegisterStructureType.</summary>
    public static StructureTypeDef DefaultStructureType(int typeId) => typeId switch
    {
        // ADR-009 clause 3: the structure tech tree. Every Prereqs list here
        // mirrors its /data/buildings file verbatim, and the selftest
        // round-trip is what proves it: the files are the catalogue and these
        // literals exist to be proved equal to them. The tree is the one doc
        // 23 s4.3 sets out - the yard, the plant and the wall stand alone
        // (the yard is MCV-deployed and the wall is never queued), everything
        // cheap hangs off the plant, the factory waits on an economy, the
        // tier-2 support buildings wait on the factory, and the superweapon
        // waits on the radar.
        //
        // Every Tab here mirrors its /data/buildings file verbatim too, and the
        // same round-trip proves it. The values are the two hand-kept arrays the
        // sidebar used to carry, transcribed once: nothing on this def implies
        // the split, which is why it is authored (see the BuildTab enum).
        // P7-5 (DR-02): the Directorate's plant, and the numbers are UNCHANGED.
        // GDD s3 gives this side "fewer, bigger power plants (= juicier
        // targets)", and that is what 100 supply behind 150 hp already was: the
        // whole base on one fragile building. The row did not need a rebalance,
        // it needed the OTHER side to stop sharing this.
        1 => new StructureTypeDef(300, EntityKind.PowerPlant, 100, Hp: 150, SightCells: 4,
                                  PowerSupply: 100, Faction: FactionDirectorate,
                                  Tab: BuildTab.Buildings),
        2 => new StructureTypeDef(2000, EntityKind.Factory, 300, Hp: 1500, PowerDraw: 40, SightCells: 5, Prereqs: new[] { 3 },
                                  Tab: BuildTab.Buildings),
        // Honest draws (ADR-008 clause 3, BD-07 rebased; A11 co-sign recorded
        // in the ADR): the refinery 0 to 40, the yard 0 to 20, the superweapon
        // 100 to 150. The opening base is then EXACTLY 100 supply against 100
        // draw - the zero-margin boundary the ADR accepts explicitly.
        3 => new StructureTypeDef(2000, EntityKind.Refinery, 300, Hp: 2000, PowerDraw: 40, SightCells: 6, Prereqs: new[] { 1 },
                                  Tab: BuildTab.Buildings),
        // MCV-deployed, never queued - and so no tab, which the loader checks
        // against the same BuildTicks 0 that keeps it out of the yard's queue.
        4 => new StructureTypeDef(3000, EntityKind.ConstructionYard, 0, Hp: 3000, PowerDraw: 20, SightCells: 6,
                                  Tab: BuildTab.None),
        5 => new StructureTypeDef(600, EntityKind.Turret, 150, Hp: 400, PowerDraw: 20, SightCells: 6, WeaponId: 4, Prereqs: new[] { 1 },
                                  Tab: BuildTab.Defence),
        // P7-5c (DR-04): the Directorate's ORBITAL CANNON, and like its power
        // plant the numbers are UNCHANGED. GDD s8 gives this side "huge
        // single-point damage", and 900 Omni inside a 1.5-cell core is exactly
        // that. The row needed the other side to stop sharing it.
        6 => new StructureTypeDef(4000, EntityKind.Superweapon, 600, Hp: 1200, PowerDraw: 150, SightCells: 4, Prereqs: new[] { 12 },
                                  Faction: FactionDirectorate,
                                  Tab: BuildTab.Defence),
        // P7-1: the Veil declares its side here as data rather than being
        // named in a hardcoded predicate. The compiled default must agree
        // with sod_veil_projector.yaml or the /data round-trip fails loudly,
        // which is exactly the check that now protects the rule.
        // P7-2: the Emplacement. Cheap, short-lived under armour, and the only
        // thing in the game that answers massed infantry from a fixed position.
        15 => new StructureTypeDef(350, EntityKind.Emplacement, 90, Hp: 300, PowerDraw: 10, SightCells: 5, WeaponId: 8, Prereqs: new[] { 1 },
                                   Tab: BuildTab.Defence),
        // ADR-028: the Airfield, behind the radar uplink (struct type 12). It
        // is the one entry where the tab CANNOT be guessed from the rest of the
        // def: a producer with no weapon, filed under DEFENCE because that is
        // where the tech buildings sit.
        16 => new StructureTypeDef(1800, EntityKind.Airfield, 260, Hp: 1100, PowerDraw: 50, SightCells: 6, Prereqs: new[] { 12 },
                                   Tab: BuildTab.Defence),
        // P7-2b: the faction defences, both from written GDD s3 doctrine - the
        // Directorate's buildings are "tough but expensive", the Sodality has
        // "cloaked units AND structures".
        17 => new StructureTypeDef(1400, EntityKind.Bastion, 300, Hp: 1600, PowerDraw: 40, SightCells: 7, WeaponId: 4, Prereqs: new[] { 12 }, Faction: FactionDirectorate,
                                   Tab: BuildTab.Defence),
        18 => new StructureTypeDef(400, EntityKind.Emplacement, 110, Hp: 260, PowerDraw: 15, SightCells: 6, WeaponId: 8, Prereqs: new[] { 1 }, Faction: FactionSodality,
                                   Tab: BuildTab.Defence),
        7 => new StructureTypeDef(1500, EntityKind.VeilProjector, 250, Hp: 900, PowerDraw: 60, SightCells: 6, Prereqs: new[] { 1 }, Faction: FactionSodality,
                                  Tab: BuildTab.Defence),
        8 => new StructureTypeDef(1200, EntityKind.ServiceDepot, 200, Hp: 1000, PowerDraw: 30, SightCells: 4, Prereqs: new[] { 2 },
                                  Tab: BuildTab.Buildings),
        // Barrier segment (ADR-005). BuildTicks 0 keeps it out of the Construction
        // Yard queue by the existing guard in BuildStructure, exactly as type 4 is
        // kept out: barriers are bought upfront at placement instead. SightCells 0
        // keeps 80 segments per player out of the fog pass entirely.
        // The one buildable type with no build time: DEFENCE all the same,
        // because the Wall kind is the sim's own exception to "BuildTicks 0
        // means no player may have it" (the placement path buys it outright).
        9 => new StructureTypeDef(100, EntityKind.Wall, 0, Hp: 500, SightCells: 0, Footprint: 1,
                                  Tab: BuildTab.Defence),
        // P7-10: the Gate, and struct type 10 is the number ADR-005 reserved for
        // it, so nothing is renumbered to make room. See the ADR's P7-10
        // amendment for why clause 6's blocker does not reach this design: the
        // deferral is scoped to a gate that is passable to its OWNER and solid
        // to the enemy at the same moment, which is what would need a per-player
        // flow field. This gate has ONE GLOBAL state, so an open gate is passable
        // to everybody and a closed one is solid to everybody, and the single
        // global passability grid says exactly that with no new machinery.
        //
        // Everything else here follows the wall, because a gate IS a barrier:
        // Footprint 1, BuildTicks 0 (bought upfront as the segment lands, no
        // ready slot, the ADR-005 clause 3 model), SightCells 0 so a run of them
        // stays out of the fog pass, DEFENCE for the wall's reason, and no
        // prerequisites because a barrier is never queued at a yard and so never
        // reaches the tree check.
        //
        // The two numbers that are NOT the wall's are Luke's and are recorded as
        // his: cost 200, twice a wall segment, because a gate is the one place in
        // a perimeter that lets your own army through and it should not be the
        // cheap way to build a wall; and Hp 500, which IS the wall's, so a
        // besieger breaches the gate and the wall beside it at the same rate and
        // the gate is not a soft spot to aim at.
        10 => new StructureTypeDef(200, EntityKind.Gate, 0, Hp: 500, SightCells: 0, Footprint: 1,
                                   Tab: BuildTab.Defence),
        // Barracks (TICKET-P5-PROD-03, numbers from doc 23 s4.3): cheap and
        // early, because that is what makes an infantry rush a real strategy
        // rather than a factory afterthought. Struct type 11 is the barracks;
        // UNIT type 11 is the engineer - different namespaces, no clash.
        // Buildable since ADR-009 clause 5: it is the producer of every unit
        // whose ProducedAt names struct type 11 (the infantry).
        11 => new StructureTypeDef(500, EntityKind.Barracks, 100, Hp: 800, PowerDraw: 20, SightCells: 5, Prereqs: new[] { 1 },
                                   Tab: BuildTab.Buildings),
        // Radar uplink (doc 23 s4.2 numbers): buildable since ADR-008 clause 4.
        // The client's minimap is lit only while a living uplink stands with
        // supply covering draw. Its Prereqs are READ since ADR-009: the radar
        // waits on a factory, and the superweapon waits on the radar.
        12 => new StructureTypeDef(900, EntityKind.RadarUplink, 150, Hp: 1000, PowerDraw: 80, SightCells: 10, Prereqs: new[] { 2 },
                                   Tab: BuildTab.Buildings),
        // Neutral Outpost (ADR-021, P6 Wave C4): the capturable income structure
        // of GDD line 41. MAP-PLACED ONLY, never player-built: BuildTicks 0 keeps
        // it out of every Construction Yard queue by the existing guard (the yard
        // and barrier precedent), and no sidebar item names it. Cost 500 exists
        // for the schema and for the classic sell-a-captured-building half-refund;
        // it is never charged. PowerDraw 0 so a captured income building never
        // browns out the grid it funds. Unarmed; a modest Sight gives its owner
        // map-control vision beside the income.
        13 => new StructureTypeDef(500, EntityKind.Outpost, 0, Hp: 1000, SightCells: 5,
                                   Tab: BuildTab.None),
        // Destroyable bridge (ADR-025, P6 Wave C6a). MAP-PLACED ONLY, like the
        // outpost: BuildTicks 0 keeps it out of every yard queue and no sidebar
        // item names it. Footprint 1, the wall's shape, because a bridge deck is
        // a single cell wide. SightCells 0 for the wall's reason: a long deck is
        // many entities and none of them should enter the fog pass. Hp 800 makes
        // felling one a deliberate act rather than incidental splash.
        14 => new StructureTypeDef(400, EntityKind.Bridge, 0, Hp: 800, SightCells: 0, Footprint: 1,
                                   Tab: BuildTab.None),
        // P7-11c: the Mine. A STRUCTURE rather than a new kind of thing, which
        // is what lets it inherit the BuildStructure/PlaceStructure path,
        // ownership, cost, the tech tree, the catalogue and the sidebar whole.
        //
        // Footprint 1, the wall's and the bridge's shape: a mine is one cell.
        // SightCells 0 for the wall's reason and one of its own - a buried
        // charge that lit the fog would be a free scout that also explodes.
        // BuildTicks 60 (four seconds) keeps it in the yard queue like every
        // other building, which is what stage 1 of minegate asserts; a 0 here
        // would make it a barrier-shaped upfront purchase and would put it in
        // reachabilitygate's map-placed exclusion list by accident.
        // Hp 100 makes a revealed mine cheap to clear, which is the other half
        // of GDD line 56's "every stealth tool has a public counter".
        // Prereqs the radar uplink (type 12), the tier the superweapon and the
        // airfield already wait behind.
        // MaxAlive 20 is the cap, and it is set where it is for two reasons.
        // 20 x 400 credits is 8000, EXACTLY the ceiling MaxBarriersPerPlayer
        // already sets on the other tool that can carpet a map (80 walls at
        // 100), so the two cost the same to max out and neither is the cheap
        // way to tile the ground. And it bounds MineSystem's per-tick scan at
        // 20 mines per player rather than at whatever a treasury can afford.
        19 => new StructureTypeDef(400, EntityKind.Mine, 60, Hp: 100, SightCells: 0, Footprint: 1,
                                   Prereqs: new[] { 12 }, MaxAlive: MinesPerPlayer,
                                   Tab: BuildTab.Defence),
        // P7-5 (DR-02): the Sodality's generator, the other half of GDD s3's
        // "decentralised power (many small generators)". Every number is set
        // against the Directorate plant above rather than chosen on its own,
        // because the identity IS the comparison and a generator priced in
        // isolation would just be a cheaper building.
        //
        // Three generators are the unit of comparison, being what it takes to
        // beat one plant's 100 supply:
        //   supply     120 for 390 credits, against 100 for 300. The Sodality
        //              pays 3.25 credits per power to the Directorate's 3.00,
        //              so CENTRALISED IS MORE EFFICIENT - that is the upside
        //              the GDD's "bigger" has to buy, or the trade is one-way.
        //   hp         210 across three buildings against 150 in one. Tougher
        //              in total and, decisively, no single kill takes more than
        //              a THIRD of the supply. That is the whole of
        //              "decentralised", and it is what the gate measures.
        //   build      135 ticks against 100, and three placements against one.
        //              The sprawl costs time and attention, not just credits.
        //   footprint  1 against 2, so the sprawl fits a base rather than
        //              needing three times the room.
        // Sight 3 (against 4) keeps a cheap generator from being a cheap
        // watchtower, which is the one way this could have been strictly better.
        20 => new StructureTypeDef(130, EntityKind.PowerPlant, 45, Hp: 70, SightCells: 3,
                                   PowerSupply: 40, Footprint: 1, Faction: FactionSodality,
                                   Tab: BuildTab.Buildings),
        // P7-5b (DR-03): the Sodality's Watch Post, and the answer to GDD line
        // 56's "every stealth tool has a public counter" for the side that had
        // none. Set against dir_sentinel_scout (unit 6, 400 credits, 90 hp,
        // sight 7, mobile, unarmed) rather than priced alone, because the two
        // are the same answer given in two shapes and the SHAPE is the identity:
        //   the Directorate SWEEPS - a scout car drives where it suspects
        //   the Sodality WAITS   - a post is planted where it predicts
        // 350 against 400 and 260 hit points against 90, so it is cheaper and
        // far harder to pick off, paid for by never moving. Sight 8 against 7 is
        // the one number where it wins outright, and it has to: a detector that
        // cannot move must cover more ground to be worth planting at all.
        // Unarmed on purpose - GDD line 56 says detectors are "visible and
        // killable", and a detector that shoots is a turret with a bonus.
        21 => new StructureTypeDef(350, EntityKind.WatchPost, 80, Hp: 260, SightCells: 8,
                                   PowerDraw: 15, Footprint: 1, Prereqs: new[] { 1 },
                                   Faction: FactionSodality, Detector: true,
                                   Tab: BuildTab.Defence),
        // P7-5c (DR-04): the Sodality's SEISMIC CHARGE. GDD s8 says "one
        // superweapon per faction", so every number it shares with the orbital
        // cannon is shared ON PURPOSE - same 4000 credits, same 600 build
        // ticks, same 150 draw, same radar prerequisite, same charge. The two
        // are meant to be the same DECISION with different consequences, and a
        // cheaper or faster one would make the choice about price instead of
        // about what it does.
        //
        // It keeps EntityKind.Superweapon deliberately: charge, the ready
        // event, the launch command and the five-second warning are all keyed
        // on that kind and all of it applies unchanged. Only the impact differs,
        // and the impact branches on StructType.
        22 => new StructureTypeDef(4000, EntityKind.Superweapon, 600, Hp: 1200, PowerDraw: 150, SightCells: 4,
                                   Prereqs: new[] { 12 }, Faction: FactionSodality, DestroysFields: true,
                                   Tab: BuildTab.Defence),
        _ => default,
    };

    /// <summary>
    /// The highest COMPILED structure type (TICKET-P5-PROD-02): the bound for
    /// every loop that enumerates the catalogue. EntityKind reservations above
    /// it (airfield, emplacement, bastion) have numbers but no defs and no
    /// /data files, so they must stay OUTSIDE this bound until implemented
    /// (the outpost graduated to struct type 13 under ADR-021).
    ///
    /// P7-10 removed the exception this paragraph used to carry. Every
    /// enumerating loop had to skip GateStructType by hand, because 10 was
    /// inside the bound with no def and no file; the gate now HAS both, so the
    /// range is dense again and each of those skips is gone rather than kept as
    /// a no-op waiting to be wrong.
    /// </summary>
    // P7-2 raised this from 14 to 15 for the Emplacement.
    public const int MaxStructType = 22;   // P7-5c raised it for the Sodality seismic charge

    /// <summary>P7-5b: the Sodality's detector, named for the sites that spawn
    /// or assert it rather than written as a literal.</summary>
    public const int WatchPostStructType = 21;

    /// <summary>
    /// P7-5c (DR-04): the two superweapons. Both are EntityKind.Superweapon, so
    /// everything about charging, launching and warning is shared; these names
    /// exist for the one place that must tell them apart, which is the impact.
    /// </summary>
    public const int OrbitalCannonStructType = 6;
    public const int SeismicChargeStructType = 22;

    /// <summary>
    /// P7-5 (DR-02): the two power plants, named rather than written as literals
    /// at the sites that choose between them. GDD s3 gives each side its own
    /// grid, so "the power plant" is no longer a thing that exists.
    /// </summary>
    public const int DirectoratePlantStructType = 1;
    public const int SodalityGeneratorStructType = 20;

    /// <summary>The plant a given side actually builds. One place, so the AI,
    /// the gates and any future opening all ask the same question.</summary>
    public static int PlantTypeForFaction(int faction)
        => faction == FactionSodality ? SodalityGeneratorStructType : DirectoratePlantStructType;

    private readonly Dictionary<int, StructureTypeDef> _structTypes = SeedStructureTypes();
    private static Dictionary<int, StructureTypeDef> SeedStructureTypes()
    {
        var d = new Dictionary<int, StructureTypeDef>();
        // P7-10: dense again. This loop used to skip GateStructType, which had no
        // def to seed while ADR-005 clause 6 held; the gate has one now.
        for (int t = 1; t <= MaxStructType; t++) d[t] = DefaultStructureType(t);
        return d;
    }

    /// <summary>The live catalogue. Unknown types return default, whose Cost 0 is what every command handler already tests to refuse them.</summary>
    public StructureTypeDef GetStructureType(int typeId) => _structTypes.TryGetValue(typeId, out var d) ? d : default;

    /// <summary>
    /// Every structure type id this world has registered, ASCENDING: the twin of
    /// <see cref="UnitTypeIds"/>, down to the fresh list and the sort. A read,
    /// and only a read, so a caller may keep the list and the catalogue is never
    /// handed a mutable view of itself.
    ///
    /// Sorted for the reason UnitTypeIds sorts: dictionary iteration order must
    /// never leak into anything a player or a peer can see.
    ///
    /// It exists because nothing could ASK what the BUILDING catalogue holds.
    /// Every caller that wanted the set walked 1 to MaxStructType and remembered
    /// to skip GateStructType itself - a bound and an exception restated at each
    /// site, which is the hand-kept-list shape that left seven units with no
    /// sidebar button. Asking the registry makes a registered type a member by
    /// construction, which is how P7-10's gate acquired its sidebar button, its
    /// reachabilitygate order and its client-harness check without any of the
    /// three being told about it.
    /// </summary>
    public IReadOnlyList<int> StructureTypeIds()
    {
        var ids = new List<int>(_structTypes.Keys);
        ids.Sort();
        return ids;
    }

    /// <summary>Match setup may overwrite or extend the catalogue before tick 0, mirroring RegisterUnitType. After tick 0 the catalogue is frozen: a mid-match change would be a silent replay divergence.</summary>
    public void RegisterStructureType(int typeId, StructureTypeDef def)
    {
        if (Tick != 0) throw new InvalidOperationException("catalogue is fixed once the match starts");
        _structTypes[typeId] = def;
    }

    /// <summary>
    /// The live WEAPON catalogue, the third leg of ADR-006 and the one that was
    /// missing: every weapon number used to be compiled in Combat.cs and the sim
    /// read the static table directly, so data/weapons could have held anything
    /// at all and the game would not have noticed. That is the P7-1 defect shape
    /// exactly, authored data that does not drive the runtime, and it is why the
    /// two target-selection call sites now read THIS rather than Weapons.Get.
    ///
    /// Seeded from the compiled reference table so a World built with no /data
    /// behind it plays today's numbers unchanged, which is what keeps every
    /// harness that constructs a bare World green. Unknown ids return
    /// Weapons.None, matching Weapons.Get's own default: weapon 0 means unarmed
    /// and every caller already tests for it.
    /// </summary>
    private readonly Dictionary<int, WeaponDef> _weaponTypes = SeedWeaponTypes();
    private static Dictionary<int, WeaponDef> SeedWeaponTypes()
    {
        var d = new Dictionary<int, WeaponDef>();
        for (int id = 1; id <= Weapons.MaxWeaponId; id++) d[id] = Weapons.Get(id);
        return d;
    }

    /// <summary>The weapon a live match fires. Read this, never Weapons.Get, anywhere a world is in scope.</summary>
    public WeaponDef GetWeaponType(int weaponId) => _weaponTypes.TryGetValue(weaponId, out var d) ? d : Weapons.None;

    /// <summary>Match setup may overwrite or extend the weapon table before tick 0, mirroring RegisterUnitType and RegisterStructureType. After tick 0 it is frozen: a mid-match change would be a silent replay divergence.</summary>
    public void RegisterWeaponType(int weaponId, WeaponDef def)
    {
        if (Tick != 0) throw new InvalidOperationException("catalogue is fixed once the match starts");
        _weaponTypes[weaponId] = def;
    }

    /// <summary>
    /// The live AI TUNING catalogue, the fourth leg of ADR-006. The skirmish
    /// commander's numbers used to be compiled literals in SkirmishAI.cs, which
    /// made two LAN peers agree on them by construction; moving them into
    /// /data/ai creates a desync vector that nothing else in the game has, since
    /// peers holding different files would issue different AI COMMANDS while
    /// every def they compare stayed equal. That is why this table is registered
    /// on the World and folded into <see cref="CatalogueChecksum"/> rather than
    /// living in a process-global beside the commander: the LAN hello, saves and
    /// replays already compare the checksum and refuse a mismatch before tick 0,
    /// so the desync becomes a refusal.
    ///
    /// Seeded from the compiled reference table so a World built with no /data
    /// behind it plays today's commander unchanged, which is what keeps every
    /// harness that constructs a bare World green.
    /// </summary>
    private readonly Dictionary<int, AiTuningDef> _aiTuning = SeedAiTuning();
    private static Dictionary<int, AiTuningDef> SeedAiTuning()
    {
        var d = new Dictionary<int, AiTuningDef>();
        for (int id = 1; id <= AiTuning.MaxTuningId; id++) d[id] = AiTuning.Get(id);
        return d;
    }

    /// <summary>The tuning a live match's commander is built from. Read this,
    /// never AiTuning.Get, anywhere a world is in scope. An id outside the table
    /// falls through to the compiled reference, which throws on an unknown one:
    /// there is no such thing as an unspecified commander, so a quiet default
    /// would be a beat of zero.</summary>
    public AiTuningDef GetAiTuning(int tuningId)
        => _aiTuning.TryGetValue(tuningId, out var d) ? d : AiTuning.Get(tuningId);

    /// <summary>Match setup may overwrite the AI tuning table before tick 0,
    /// mirroring RegisterUnitType, RegisterStructureType and RegisterWeaponType.
    /// After tick 0 it is frozen: a mid-match change would be a silent replay
    /// divergence. Two invariants are checked here rather than in the parser, so
    /// that a code caller is held to them as tightly as a file is - the row must
    /// belong to the same family as the compiled row it replaces, or a rung slot
    /// would end up holding a personality with no beat ratio at all, and the
    /// beat denominator must be at least 1, because the commander divides by
    /// it.</summary>
    public void RegisterAiTuning(int tuningId, AiTuningDef def)
    {
        if (Tick != 0) throw new InvalidOperationException("catalogue is fixed once the match starts");
        var reference = AiTuning.Get(tuningId);
        if (def.Kind != reference.Kind)
            throw new FormatException(
                $"AI tuning id {tuningId} is a {reference.Kind} row and was offered a {def.Kind} one");
        if (def.BeatDenominator < 1)
            throw new FormatException(
                $"AI tuning id {tuningId} has a beat denominator of {def.BeatDenominator}; the commander divides by it");
        _aiTuning[tuningId] = def;
    }

    /// <summary>
    /// ADR-006 commitment 1: the catalogue checksum. FNV-1a in the sim's own
    /// StateHash idiom over the CANONICALISED registered defs, never over file
    /// bytes: unit types first, then structure types, then weapon types, then
    /// the AI tuning rows, each
    /// walked in ascending id (dictionary iteration order must never leak into
    /// an artefact), each def contributing every field in declaration order with
    /// the prerequisite list length-prefixed. Two worlds agreeing here are playing
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
                // P7-11b: the build cap rides the checksum for the reason every
                // other column does. It decides whether a Produce command is
                // ACCEPTED, so two peers holding different caps would build
                // different armies from the same command stream while agreeing
                // on every stat in the game, which is a desync that no other
                // comparison in the protocol could see.
                h.Add(d.MaxAlive);
                // And Air, which has been missing from this fold since ADR-028
                // shipped it. Same argument, worse case: two peers disagreeing
                // about which units FLY would disagree about what can even be
                // SHOT, because ADR-028 clause 3 makes engagement an equality
                // between a weapon's anti-air flag and its target's airborne
                // one. Every stat would match and the protocol would see
                // nothing. Found while adding MaxAlive beside it.
                h.Add(d.Air);
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
                // P7-11c: the building cap rides the checksum for the argument
                // its unit twin above records verbatim. It decides whether a
                // BuildStructure and a PlaceStructure are ACCEPTED, so two peers
                // holding different caps would lay different numbers of mines
                // from the same command stream while agreeing on every stat in
                // the game - a desync no other comparison in the protocol sees.
                h.Add(d.MaxAlive);
                // P7-5: the structure's SIDE, which had been missing since P7-1
                // made it decide whether a BuildStructure is accepted. Its unit
                // twin has been folded since the roster existed; this one was
                // simply never added when the field arrived, and the omission
                // is the same shape as the Air one recorded above.
                //
                // Two peers holding different /data could disagree about which
                // side may build the Bastion while every other number in the
                // game matched, and the protocol would see nothing. Latent
                // until now because both peers load the same /data - and NOT
                // latent from here, because DR-02 makes the answer to "may I
                // build a power plant" faction-dependent for the first time.
                h.Add(d.Faction);
                // P7-5b: whether this building reveals cloak. It decides what a
                // player can SEE and therefore what its units may target, so two
                // peers disagreeing about it would resolve the same firefight
                // differently while every stat in the game matched. That is the
                // ADR-032 clause exactly, and detection is the clearest case of
                // it yet: the disagreement would not even be about a number.
                h.Add(d.Detector);
                // P7-5e: whether this building's strike deletes ferrite fields.
                // It decides what a superweapon DOES, so two peers disagreeing
                // would watch the same launch take the map's economy apart on
                // one machine and not the other.
                h.Add(d.DestroysFields);
                h.Add(d.Prereqs?.Length ?? 0);
                if (d.Prereqs != null) foreach (int p in d.Prereqs) h.Add(p);
            }
            // Weapons joined the catalogue when their numbers moved into
            // data/weapons. They are folded LAST and appended rather than
            // interleaved, so the unit and structure sections contribute
            // exactly what they always did and the change to this value is one
            // thing rather than three. It does change, by construction: a
            // checksum that ignored a weapon range would let two players fight
            // with different guns and call it agreement. Pre-existing saves and
            // replays therefore refuse, the same pre-first-public-build trade
            // P7-2, P7-3, P7-4 and P7-11a each took.
            var weaponIds = new List<int>(_weaponTypes.Keys);
            weaponIds.Sort();
            h.Add(weaponIds.Count);
            foreach (int id in weaponIds)
            {
                var d = _weaponTypes[id];
                h.Add(id); h.Add(d.Range); h.Add(d.Damage); h.Add((int)d.Warhead);
                h.Add(d.CooldownTicks); h.Add(d.MinRange); h.Add(d.SplashRadius); h.Add(d.AntiAir);
            }
            // The AI tuning joined the catalogue when its numbers moved into
            // data/ai, and it is the section with the sharpest safety argument.
            // Every other section describes something both peers can SEE going
            // wrong; this one describes how the commander thinks, so peers
            // holding different files would issue different AI commands while
            // agreeing on every unit, building and gun in the game. Folded LAST
            // and appended rather than interleaved, so the three older sections
            // contribute exactly what they always did and the change to this
            // value is one thing rather than four. It does change, by
            // construction, which is the same pre-first-public-build trade the
            // weapons wave and P7-2, P7-3, P7-4 and P7-11a each took.
            var aiIds = new List<int>(_aiTuning.Keys);
            aiIds.Sort();
            h.Add(aiIds.Count);
            foreach (int id in aiIds)
            {
                var d = _aiTuning[id];
                h.Add(id); h.Add((int)d.Kind); h.Add(d.ActEvery); h.Add(d.WaveSize);
                h.Add(d.BeatNumerator); h.Add(d.BeatDenominator);
                h.Add(d.HarvestersPerRefinery); h.Add(d.StartingCreditHandicap);
            }
            return h.Value;
        }
    }

    /// <summary>The default footprint: every structure type except a barrier is 2x2.</summary>
    public const int FootprintSize = 2;

    /// <summary>ADR-005 reserved type 10 for the gate and P7-10 filled it. Named
    /// for the reason the producer ids below are named: a bare 10 in a comparison
    /// is a number nobody can check on sight. The 1x1 footprint that used to be
    /// stated here now comes off the def like every other type's.</summary>
    public const int GateStructType = 10;

    /// <summary>The two unit producers as STRUCT type ids (ADR-009): the
    /// values a UnitTypeDef's ProducedAt carries. Named because the AI, the
    /// client's tab membership and the sim's own split all compare against
    /// them, and a literal 11 in four files is how the barracks and the
    /// ENGINEER (unit type 11, a different namespace entirely) get confused.</summary>
    public const int FactoryStructType = 2;
    public const int BarracksStructType = 11;
    /// <summary>ADR-028. Named for the same reason the two above are: the
    /// sidebar routes a unit to its tab by its produced_at, and a bare 16 in
    /// that comparison is a number nobody can check on sight.</summary>
    public const int AirfieldStructType = 16;

    /// <summary>Cells per side of a structure type's square footprint (ADR-005), read from the def. Barriers are 1x1; everything else, including an unknown type (Footprint 0 on the default def), takes the 2x2 default that the placement path has always assumed.</summary>
    public int FootprintOf(int structType)
    {
        // P7-10 removed a special case for GateStructType here. It existed only
        // because the reserved type had no def to read a footprint off; it has
        // one now (footprint 1, the wall's), so the catalogue answers.
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
    /// <summary>Per-player barrier cap (ADR-005 clause 5). Derived from the TDD s6 ratified budget of 200 structures (03-technical-design-document.md:59): 2 x 80 + ~32 real buildings = ~192, inside budget. A performance guarantee, not a design flourish. P7-10's gates count against it with the walls, because they are barriers by the same predicate and the budget the cap protects does not care which of the two is standing.</summary>
    public const int MaxBarriersPerPlayer = 80;

    /// <summary>
    /// P7-10: how close an ALLY of the gate's owner must come for it to open,
    /// SQUARED, so the per-tick scan never takes a square root. Three cells,
    /// Luke's number, and it is the reach of a gatehouse rather than of a
    /// sentry: two cells would have a tank's nose in the doorway before the
    /// gate moved, and a longer reach would open a perimeter to a scout that
    /// merely drove past it.
    /// </summary>
    public static readonly Fix64 GateOpenRadiusSq = Fix64.FromInt(9);

    /// <summary>
    /// P7-10: how long a gate stays open after the last ally left its radius,
    /// in ticks. 45 is three seconds at 15 Hz, and it is Luke's number.
    ///
    /// THIS IS LOAD-BEARING RATHER THAN POLISH, and the reason is the one
    /// ADR-005 clause 6 was written about. Every toggle calls the only flow
    /// invalidation this sim has, FlowFieldCache.Clear, which throws away EVERY
    /// cached field on the map; a gate that flickered as an army milled about
    /// would rebuild every route in the game several times a second. The delay
    /// is what turns "units are near" from a per-tick reading into an interval,
    /// and it bounds the toggle rate at one close per 45 ticks per gate no
    /// matter what the units do. Three seconds is also long enough for a column
    /// to follow the unit that opened it, which is what makes it read as a gate
    /// rather than as a trapdoor.
    /// </summary>
    public const int GateHysteresisTicks = 45;

    /// <summary>P7-11c: the Mine's structure type id, named for the reason the
    /// three above it are - a bare 19 in a comparison is a number nobody can
    /// check on sight.</summary>
    public const int MineStructType = 19;

    /// <summary>
    /// The per-player mine cap, authored as com_mine.yaml's max_alive and
    /// stated here so the file and the compiled reference def name one number.
    /// Read through the def like every other column; nothing tests this
    /// constant at runtime.
    /// </summary>
    public const int MinesPerPlayer = 20;

    /// <summary>
    /// What a mine does when it goes off, in ApplyAreaDamage's terms: full
    /// Omni damage within 1.5 cells of the charge and half within 3, exactly
    /// as a superweapon strike lands.
    ///
    /// A COMPILED constant rather than an authored one, and the precedent is
    /// deliberate: the superweapon's 900 and the hero's DemolitionDamage are
    /// both compiled, because neither is a WEAPON. Authoring this in
    /// data/weapons and hanging it off the def's weapon_ids would put a
    /// non-zero WeaponId on the entity, and an armed structure in this sim
    /// AUTO-ACQUIRES and FIRES (CombatSystem keys on exactly that), so a mine
    /// would sit in the ground shooting at people instead of waiting for them.
    /// </summary>
    public const int MineDamage = 400;

    /// <summary>
    /// How close an enemy must come, SQUARED, so the per-tick scan never takes
    /// a square root: 1.5 cells, which is ApplyAreaDamage's own inner radius,
    /// so a triggered mine's full damage covers exactly what set it off.
    /// Fix64.FromFraction(9, 4) is 1.5^2 stated without a multiply.
    /// </summary>
    public static readonly Fix64 MineTriggerRadiusSq = Fix64.FromFraction(9, 4);

    /// <summary>TICKET-P5-REP-07: the Service Depot's field-repair reach, in
    /// cells. A system constant, deliberately NOT sight_range - the depot sees
    /// 4 cells by coincidence and com_service_depot.yaml:12-15 warns about
    /// exactly that confusion; probes confirm radius 4 heals and radius 5 does
    /// not. The client draws the selected depot's aura ring from this same
    /// constant so the two cannot drift. Named from the existing literal in
    /// ProductionSystem's depot loop (a squared-distance compare against 16),
    /// so behaviour is bit-identical.</summary>
    public const int DepotRepairRadiusCells = 4;

    /// <summary>ADR-019 (P6 Wave C2): the repair vehicle's unit type id. It is an
    /// ordinary EntityKind.Unit (like the MCV is type 7); its only special
    /// behaviour is the mobile field-repair aura in ProductionSystem, which keys
    /// on this id. Named here so the sim heal branch and the client's model and
    /// build wiring test the same number rather than a bare 13 in each.</summary>
    public const int RepairVehicleType = 13;

    /// <summary>The MCV (com_mcv), the deployable that becomes a Construction
    /// Yard, and the ENGINEER (com_engineer), the capture unit. Both were bare
    /// literals scattered across the deploy gate, the victory-hope test, three
    /// AI branches and two client files, plus two PRIVATE constants naming the
    /// same numbers in two projects. Named once here for the reason
    /// RepairVehicleType is: so everything that means "the MCV" tests the same
    /// number rather than a 7 that has to be recognised on sight.</summary>
    public const int McvUnitType = 7;
    /// <summary>Named for the same reason McvUnitType is: SpawnHarvester now
    /// reads its def, and a bare 4 in that lookup is a number nobody can check
    /// on sight.</summary>
    public const int HarvesterUnitType = 4;
    public const int EngineerUnitType = 11;
    /// <summary>P7-7: GDD s7's Infiltrator, the Sodality's economy-denial tool.</summary>
    public const int InfiltratorUnitType = 17;
    /// <summary>P7-11a: GDD s7's Saboteur, the Sodality's tempo tool. It shares
    /// the Infiltrator's walk and its consumed-by-the-act rule and differs only
    /// in the effect, which is why both are named here rather than recognised on
    /// sight in CaptureSystem.</summary>
    public const int SaboteurUnitType = 18;
    /// <summary>P7-11b: GDD s7's two heroes, lines 62 and 64. Named for the
    /// reason the three above are - they are recognised by ContactEffectOf, not
    /// on sight - and named as a PAIR because they are one unit authored twice.
    /// The Directorate's walks in the open and the Sodality's is cloaked (GDD
    /// line 30's doctrine), and that is the only difference between them.</summary>
    public const int CommandoUnitType = 19;
    public const int ShadowCommandoUnitType = 20;

    /// <summary>
    /// P7-11b: what a unit DOES when it reaches the enemy structure it was
    /// ordered onto. This replaced a chain of `unitType == X` booleans in
    /// CaptureSystem, which its own comment had already called the eighth
    /// enumeration of the phase and which the hero would have made a fourth and
    /// fifth entry in. The shared shape - the walk, the reach test, the target
    /// validity rule - is now written once against this notion, and only the
    /// effect branches.
    ///
    /// Behaviour-identical for the three units that existed before it: the
    /// boolean chain it replaces tested exactly these three ids and every other
    /// unit type, including 0, falls through to None as it always did.
    /// </summary>
    private enum ContactEffect { None, Capture, Theft, Sabotage, Demolition }

    private static ContactEffect ContactEffectOf(int unitType) => unitType switch
    {
        EngineerUnitType => ContactEffect.Capture,
        InfiltratorUnitType => ContactEffect.Theft,
        SaboteurUnitType => ContactEffect.Sabotage,
        CommandoUnitType or ShadowCommandoUnitType => ContactEffect.Demolition,
        _ => ContactEffect.None,
    };

    /// <summary>
    /// P7-11b: how much of a building a hero takes away in one go. Applied
    /// through the ordinary damage path, so this is a BASE figure that the
    /// warhead matrix and the target's armour class then rule on, and a death
    /// here produces rubble and a Died event exactly as a shell would.
    ///
    /// 1000 against an anti-building warhead is 1000 against a structure. It is
    /// chosen to sit between the two halves of the catalogue rather than to be a
    /// round number: the power plant (150), the barracks (800) and the veil
    /// projector (900) are gone in one visit, while the Bastion (1600), the
    /// factory (1500) and the refinery (2000) are badly hurt and still standing.
    /// A hero that deleted a Construction Yard on contact would end games on one
    /// unseen walk; one that could not kill a power plant would be a rifle
    /// squad with a price tag.
    /// </summary>
    public const int DemolitionDamage = 1000;

    /// <summary>ADR-021 (P6 Wave C4): what a captured Outpost pays its owner,
    /// once per second (the pre-increment tick's positive multiples of
    /// TicksPerSecond, the ADR-012 regrowth schedule idiom, so it resumes
    /// correctly across save/load). GDD line 41 reads "+15 credits/tick", which
    /// at 15 Hz would be 225/s, roughly ten harvesters; doc 22 P5-ECON-14 flags
    /// that as a units error and the ratified rate is 15 per SECOND. Balance
    /// owns the number under A11.</summary>
    public const int OutpostIncomePerSecond = 15;

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
    /// <param name="structType">P7-5: which side's plant. Defaults to the
    /// Directorate's, so every existing caller and every golden scenario spawns
    /// exactly the building it always did.</param>
    public int SpawnPowerPlant(int player, int ax, int ay, int? supply = null, int? hp = null,
                               int structType = DirectoratePlantStructType)
    {
        var def = GetStructureType(structType);
        int sup = supply ?? def.PowerSupply, php = hp ?? def.Hp;
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.PowerPlant,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = structType,
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
    /// <param name="structType">P7-5c: which side's superweapon. Defaults to the
    /// Directorate's orbital cannon, so every existing caller and every golden
    /// scenario spawns exactly the building it always did.</param>
    public int SpawnSuperweapon(int player, int ax, int ay, int chargeTicks = 1500,
                                int structType = OrbitalCannonStructType)
    {
        var def = GetStructureType(structType);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Superweapon,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = structType,
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

    /// <summary>ADR-021: the neutral Outpost, map-placed via the ordinary
    /// structure line with player -1 (the FerriteField neutrality convention);
    /// capture flips it to the capturer through the untouched CaptureSystem.</summary>
    public int SpawnOutpost(int player, int ax, int ay)
    {
        var def = GetStructureType(13);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Outpost,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 13,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1,
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

    /// <summary>P7-2: the Emplacement, the anti-infantry hardpoint. Identical
    /// in shape to SpawnTurret because it IS a turret in every structural
    /// sense - the difference that matters is its weapon, which is authored in
    /// /data and read from the def rather than written here.</summary>
    /// <summary>ADR-028: the Airfield. Structurally an ordinary producer - the
    /// air units name it in produced_at and ADR-009's routing does the rest.</summary>
    public int SpawnAirfield(int player, int ax, int ay)
    {
        var def = GetStructureType(16);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Airfield,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 16,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    /// <summary>
    /// P7-5b: the Sodality's Watch Post, the first STRUCTURE in the game that
    /// reveals cloak.
    ///
    /// Detector is read from the def rather than written as a literal here, and
    /// that is the point rather than a style preference: it is what makes the
    /// authored `detector:` key drive the runtime instead of decorating it,
    /// which is this project's most-repeated defect. The gate proves it by
    /// registering this same type with the flag OFF and watching the post go
    /// blind.
    /// </summary>
    public int SpawnWatchPost(int player, int ax, int ay)
    {
        var def = GetStructureType(WatchPostStructType);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.WatchPost,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = WatchPostStructType,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
            Detector = def.Detector,
        });
    }

    /// <summary>P7-2b: the Directorate's Bastion and the Sodality's Shroud
    /// Nest. One spawner for both, taking the struct type, because they differ
    /// only in their def and their cloak - writing two near-identical methods
    /// is how the pair drifts apart later.</summary>
    public int SpawnFactionDefence(int player, int structType, int ax, int ay)
    {
        var def = GetStructureType(structType);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = def.Kind,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = structType,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, WeaponId = def.WeaponId, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
            // GDD s3: the Sodality's structures cloak. CanTarget and the
            // decloak-on-firing rule are already entity-level, so a stealthed
            // STRUCTURE inherits both halves with no new machinery.
            Stealth = def.Faction == FactionSodality,
        });
    }

    public int SpawnEmplacement(int player, int ax, int ay)
    {
        var def = GetStructureType(15);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Emplacement,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 15,
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

    /// <summary>
    /// P7-10: a gate segment. The wall's spawner in every respect but one - it
    /// sets the fast-path flag that GateSystem's scan is gated on.
    ///
    /// It lands CLOSED, which is why it calls BlockFootprint exactly as the wall
    /// does. A gate that appeared open would be a hole in the perimeter for the
    /// first 45 ticks of its life, and worse, the map bit and the open-state
    /// collection would disagree at birth.
    /// </summary>
    public int SpawnGate(int player, int ax, int ay)
    {
        var def = GetStructureType(GateStructType);
        BlockFootprint(ax, ay, def.Footprint);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        _gatesInPlay = true;
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Gate,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = GateStructType,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, WeaponId = def.WeaponId, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
        });
    }

    /// <summary>
    /// ADR-025: a destroyable bridge deck cell. Map-placed and NEUTRAL
    /// (PlayerId -1, the ferrite-field and outpost convention).
    ///
    /// The one spawn in the sim that deliberately does NOT call BlockFootprint:
    /// a bridge IS the crossing, so its cell must stay passable while it stands.
    /// The block happens on DEATH instead, which is the inversion this wave
    /// exists to add (see FootprintOnDeath).
    /// </summary>
    public int SpawnBridge(int ax, int ay)
    {
        var def = GetStructureType(14);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = -1, Kind = EntityKind.Bridge,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = 14,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1,
        });
    }

    /// <summary>
    /// P7-11c: the Mine. The SECOND spawn in the sim that deliberately does not
    /// call BlockFootprint, and the reason is not the bridge's.
    ///
    /// A mine that blocked would be a wall that explodes, and worse than that
    /// it would LEAK. Blocked cells are what the flow field routes around, and
    /// the flow field is shared ground truth rather than anything fog hides, so
    /// an enemy could read a cloaked minefield straight off the way their own
    /// units chose to walk. The Stealth flag would be decoration. The whole
    /// point of the property is that the field is invisible until a detector
    /// finds it, so the cells must stay exactly as passable as bare ground.
    ///
    /// The complement is in FootprintOnDeath, which must skip the UNBLOCK for
    /// the same reason. See the note there: it is not symmetry for its own
    /// sake, it is a correctness requirement.
    ///
    /// Stealth is the entity flag every other cloaked thing in the game sets,
    /// so the mine inherits CanTarget, the decloak-on-firing rule and
    /// DetectionSystem's detector sweep with no new machinery - which is what
    /// gives GDD line 56 its public counter here (a Sentinel Scout reveals the
    /// field and a revealed mine can simply be shot).
    /// </summary>
    public int SpawnMine(int player, int ax, int ay)
    {
        var def = GetStructureType(MineStructType);
        Fix64 x = FootprintCentre(ax, def.Footprint), y = FootprintCentre(ay, def.Footprint);
        _minesInPlay = true;
        return Add(new Entity
        {
            Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Mine,
            X = x, Y = y, TargetX = x, TargetY = y, StructType = MineStructType,
            Hp = def.Hp, MaxHp = def.Hp, Armour = ArmourClass.Structure, ExplicitTarget = -1,
            Sight = Fix64.FromInt(def.SightCells), FieldId = -1, RefineryId = -1, PowerDraw = def.PowerDraw,
            // WeaponId is left at 0 on purpose and MineDamage explains why: an
            // armed structure fires, and a mine waits.
            Stealth = true,
        });
    }

    /// <summary>
    /// ADR-025: what a destroyed structure does to its footprint. Everything
    /// leaves passable rubble, EXCEPT a bridge, which was the crossing: its
    /// wreck BLOCKS the cell, so routes re-form around the gap.
    ///
    /// This is the only place in the sim where an entity dying makes ground
    /// LESS passable, and it is why the wave needed its own gate.
    ///
    /// P7-11c adds the third case, and it is a correctness requirement rather
    /// than tidiness. A mine never blocked, so it has nothing to give back, and
    /// calling UnblockFootprint here would clear a cell the mine does not own:
    /// ValidPlacement skips structure cells as "already blocked", which a mine's
    /// are not, so a 2x2 building may legally be sited over a live mine. Were
    /// the detonation to unblock, that building's cell would go passable
    /// underneath it and units would path INTO it. The unblock would also flush
    /// the whole flow cache on every detonation for nothing.
    /// </summary>
    private void FootprintOnDeath(in Entity t)
    {
        if (t.Kind == EntityKind.Mine) return;
        int ax = AnchorOf(t.X, t.StructType), ay = AnchorOf(t.Y, t.StructType);
        int f = FootprintOf(t.StructType);
        if (t.Kind == EntityKind.Bridge) BlockFootprint(ax, ay, f);
        else UnblockFootprint(ax, ay, f);
    }

    /// <summary>
    /// Living barrier count for a player, enforcing MaxBarriersPerPlayer.
    /// Entity-index scan: deterministic.
    ///
    /// PUBLIC so the placement ghost can ask the SIM how many walls stand rather
    /// than counting them in the interpolated view, which trails the sim by up
    /// to eight ticks: at the cap boundary that let a segment tint green and be
    /// refused on arrival. The client had to count something because this was
    /// private; now it does not.
    /// </summary>
    /// <summary>
    /// BD-05: refund everything the player has already PAID toward what this
    /// producer was building, because selling discards all of it.
    ///
    /// The amounts are not invented here. CancelProduce already rules on every
    /// one of them, and this applies THE SAME RULES to the path that never
    /// asked: a READY structure refunds in full ("it was fully paid", as that
    /// code says), and an in-progress head refunds exactly what was drained,
    /// which pay-as-you-build makes pro-rata for free. Queued-but-unstarted
    /// items are owed nothing, because nothing has been charged for them yet.
    ///
    /// FOUR places hold paid credits on a Construction Yard since ADR-023 - the
    /// first lane's ready slot and build payment, and the second lane's - and
    /// selling burned all four. A factory or barracks sold mid-build lost its
    /// head payment the same way.
    /// </summary>
    private void RefundPendingOnSell(ref Entity e)
    {
        if (e.ReadyStructure != 0)
        {
            _credits[e.PlayerId] += GetStructureType(e.ReadyStructure).Cost;
            e.ReadyStructure = 0;
        }
        if (e.BuildPaid > 0)
        {
            _credits[e.PlayerId] += e.BuildPaid;
            e.BuildPaid = 0;
            e.BuildProgress = 0;
        }
        if (LaneOf(e.Id) is { } lane)
        {
            if (lane.Ready != 0)
            {
                _credits[e.PlayerId] += GetStructureType(lane.Ready).Cost;
                lane.Ready = 0;
            }
            if (lane.Paid > 0)
            {
                _credits[e.PlayerId] += lane.Paid;
                lane.Paid = 0;
                lane.Progress = 0;
            }
            lane.Queue.Clear();
            PruneLane(e.Id);
        }
        _queues.Remove(e.Id);
    }

    public int CountBarriers(int player)
    {
        int n = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Alive && IsOwnedBy(in e, player) && IsBarrier(e.Kind)) n++;
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
        StanceSystem();
        MovementSystem();
        SeparationSystem();
        DetectionSystem();
        // P7-11c: mines read the tick's SETTLED positions, so this sits after
        // movement and separation and before anything that shoots. It is a
        // single branch when no mine has ever been placed, which is every
        // golden scenario, and that is what keeps their hashes byte-identical.
        MineSystem();
        // P7-10: gates read the tick's settled positions for the mine's reason,
        // so they sit beside it. A single branch when no gate has ever been
        // placed, which is every golden scenario, and that is what keeps their
        // hashes byte-identical.
        GateSystem();
        // P7-3: a destroyed carrier takes its hold with it. Walked by ENTITY
        // INDEX rather than over the dictionary's keys, because dictionary
        // iteration order is a determinism hazard and this runs every tick on
        // every client. Done as one sweep rather than at each death site: there
        // are several of those, and enumerating them is the trap this phase has
        // already been bitten by three times.
        if (_cargo.Count > 0)
        {
            for (int i = 0; i < _entities.Count; i++)
                if (!_entities[i].Alive && _cargo.ContainsKey(i)) _cargo.Remove(i);
        }
        // P7-11a: a lapsed sabotage, and a sabotaged building that has since
        // been destroyed, both leave the collection here. Walked by ENTITY
        // INDEX for the reason the hold above is: dictionary iteration order is
        // a determinism hazard.
        //
        // The predicate is the EXACT complement of IsDisabled - an entry goes
        // when `until <= Tick` or the entity is not alive - and it has to be,
        // because "present" is what the hash fold and the save block record. A
        // prune that disagreed with its guard would leave entries that gate
        // nothing yet still fold into the hash, which is the shape of bug this
        // project has been bitten by before.
        if (_disabledUntil.Count > 0)
        {
            for (int i = 0; i < _entities.Count; i++)
                if (_disabledUntil.TryGetValue(i, out int until) && (until <= Tick || !_entities[i].Alive))
                    _disabledUntil.Remove(i);
        }
        // P7-10: a DESTROYED gate leaves the open-state collection here, walked
        // by entity index for the reason the two sweeps above are. It is the only
        // way an entry can outlive its meaning - GateSystem itself removes the
        // entry as it shuts the gate - and it has to be swept, because "present"
        // is exactly what the hash fold and the save block record. There is
        // deliberately no unblock here: FootprintOnDeath has already run for the
        // dead gate and left its cell passable, which is what a demolished gate
        // should leave behind whichever state it died in.
        if (_gateOpenUntil.Count > 0)
        {
            for (int i = 0; i < _entities.Count; i++)
                if (!_entities[i].Alive && _gateOpenUntil.ContainsKey(i)) _gateOpenUntil.Remove(i);
        }
        CaptureSystem();
        CombatSystem();
        HarvestSystem();
        RegrowthSystem();
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

    /// <summary>
    /// ADR-015: the per-unit command-stance step. Runs immediately after
    /// OrderDispatchSystem and before MovementSystem, so a stance's movement or
    /// targeting decision takes effect the same tick. It is a NO-OP for
    /// Aggressive and HoldFire units - it touches only Guard and Patrol - which
    /// is what keeps the golden move purely the hashed-field append: nothing
    /// here runs unless a SetStance put a unit into Guard or Patrol, and no
    /// scenario, AI or save at seed 2026 does. Guard engagement is routed
    /// through ExplicitTarget so CombatSystem's battle-tested close-and-fire
    /// machinery does the work (it is immune to the crowd-arrival shortcut, so
    /// even a short-range guard closes properly); Patrol reuses the attack-move
    /// completion and pursuit machinery whole and adds only the endpoint flip.
    /// </summary>
    private void StanceSystem()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.Kind != EntityKind.Unit) continue;
            switch (e.Stance)
            {
                case Stance.Guard:
                {
                    // The leash is the unit's own SIGHT measured from the POST.
                    // Scan for the nearest targetable enemy inside it; ties break
                    // to the lower id, the auto-acquire convention. An unarmed
                    // guard cannot engage, so it only ever holds and returns.
                    int target = -1;
                    if (e.WeaponId != 0)
                    {
                        Fix64 bestD = Fix64.MaxValue;
                        Fix64 leashSq = e.Sight * e.Sight;
                        var gw = GetWeaponType(e.WeaponId);
                        for (int j = 0; j < _entities.Count; j++)
                        {
                            var t = _entities[j];
                            // P7-8g: the shared target-selection gate. It carries
                            // the hostility question AND ADR-028's air rule,
                            // which is what this path used to spell out for
                            // itself - it was the THIRD of three paths and the
                            // one a first pass at the air layer forgot.
                            if (!CanBeEngagedBy(e.PlayerId, gw.AntiAir, in t)) continue;
                            if (t.Kind == EntityKind.FerriteField || IsBarrier(t.Kind)) continue;
                            if (!CanTarget(e.PlayerId, in t)) continue; // stealth: unseen is untargetable
                            Fix64 d = Fix64.DistSq(t.X - e.PostX, t.Y - e.PostY);
                            if (d <= leashSq && d < bestD) { bestD = d; target = j; }
                        }
                    }
                    if (target >= 0)
                    {
                        // Hand the engagement to the explicit-attack path:
                        // CombatSystem (later this tick) closes to weapon range
                        // and fires, or pursues, exactly as an ordered attack.
                        e.ExplicitTarget = target;
                    }
                    else
                    {
                        // Nothing in the leash: drop any standing target and walk
                        // back to the post, settling within the crowd radius. Only
                        // re-issue the return move when actually off-post, so a
                        // guard already home stays parked rather than re-pathing
                        // every tick (and the crowd-arrival stop handles the last
                        // four cells for free).
                        e.ExplicitTarget = -1;
                        if (Fix64.DistSq(e.PostX - e.X, e.PostY - e.Y) > Fix64.FromInt(16))
                        {
                            e.TargetX = e.PostX; e.TargetY = e.PostY;
                            e.Moving = true; e.UseFlow = true; e.AMove = false;
                        }
                    }
                    _entities[i] = e;
                    break;
                }
                case Stance.Patrol:
                {
                    // A leg completes exactly when AMove clears - the attack-move
                    // completion rule only drops AMove at or near the endpoint
                    // with the area clear, which is precisely "reached this
                    // endpoint". Flip to the other endpoint, re-arm the
                    // no-progress backstop, and attack-move back. Engaging any
                    // enemy met en route and resuming afterwards is inherited
                    // from the attack-move machinery whole.
                    if (!e.AMove)
                    {
                        e.PatrolOutbound = !e.PatrolOutbound;
                        Fix64 dx = e.PatrolOutbound ? e.PatrolX : e.PostX;
                        Fix64 dy = e.PatrolOutbound ? e.PatrolY : e.PostY;
                        e.TargetX = dx; e.TargetY = dy;
                        e.AMoveX = dx; e.AMoveY = dy;
                        e.Moving = true; e.UseFlow = true; e.AMove = true;
                        e.ExplicitTarget = -1; e.HState = HarvestState.Idle;
                        e.StallTicks = 0; e.NearestApproachSq = Fix64.Zero; e.NoProgressTicks = 0;
                        _entities[i] = e;
                    }
                    break;
                }
            }
        }
    }

    private void ApplyCommand(in Command c)
    {
        if (!ValidId(c.EntityId)) return;
        {
            var carrier = _entities[c.EntityId];
            if (!carrier.Alive || !IsOwnedBy(in carrier, c.PlayerId)) return;
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
        // P7-8g: OWNERSHIP, and the site that most needs the name. Teams must not
        // change this line: an ally's tank is not an enemy and is still not yours
        // to order about.
        if (!e.Alive || !IsOwnedBy(in e, c.PlayerId)) return; // players command only their own entities

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
                // Q013 / ADR-014: a fresh destination re-arms the no-progress
                // backstop. Zeroing NearestApproachSq is the "unseeded" sentinel
                // so the next eligible tick reseeds it from the new distance;
                // without this a unit that reaches one point then is re-ordered
                // to a far one would carry a stale tiny nearest-approach and be
                // benched mid-march.
                e.NearestApproachSq = Fix64.Zero;
                e.NoProgressTicks = 0;
                // ADR-015: a fresh movement order supersedes a standing Guard or
                // Patrol (the unit is being told to go elsewhere), but PRESERVES
                // HoldFire - fire discipline persists across a Move, which is
                // Q003's engineer walking past the sentry it must not wake.
                CancelPositionalStance(ref e);
                break;
            case CommandType.Stop:
                e.Moving = false; e.HState = HarvestState.Idle; e.ExplicitTarget = -1; e.AMove = false;
                CancelPositionalStance(ref e); // ADR-015: Stop ends Guard/Patrol; HoldFire stays
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
                // ADR-015: an explicit attack ends Guard/Patrol so StanceSystem
                // stops overwriting this target next tick; HoldFire is preserved
                // and the explicit target still fires (Q003: "defends only if
                // explicitly ordered to attack").
                CancelPositionalStance(ref e);
                break;
            case CommandType.CancelProduce:
            {
                // ADR-023 clause 6: the lane rides in AuxId's high bits, with
                // lane 0 encoded as the unchanged small integer, so every
                // command and every replay recorded before this ADR decodes
                // identically and the .frep format is untouched.
                if ((c.AuxId & LaneFlag) != 0 && e.Kind == EntityKind.ConstructionYard
                    && LaneOf(e.Id) is { } cl)
                {
                    int li = c.AuxId & ~LaneFlag;
                    if (cl.Ready != 0)
                    {
                        _credits[e.PlayerId] += GetStructureType(cl.Ready).Cost;
                        cl.Ready = 0;
                    }
                    else if (li >= 0 && li < cl.Queue.Count)
                    {
                        if (li == 0)
                        {
                            _credits[e.PlayerId] += cl.Paid;
                            cl.Paid = 0;
                            cl.Progress = 0;
                        }
                        cl.Queue.RemoveAt(li);
                    }
                    PruneLane(e.Id);
                    break;
                }
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
                // ADR-023: which lane holds the readiness being consumed. -1 is
                // lane 1 (the Entity's own ReadyStructure), otherwise the yard's
                // second lane.
                bool readyInLane2 = false;
                // P7-11c: the per-type building cap, checked before either
                // payment model runs, so a refused placement costs nothing and
                // (for the queued path) keeps its readiness for a later attempt
                // once a mine has gone off. A no-op for every type whose def
                // carries no cap, which is every type but the Mine.
                if (AtMaxAliveStructure(c.PlayerId, c.AuxId)) break;
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
                        if (o.Alive && IsOwnedBy(in o, c.PlayerId) && o.Kind == EntityKind.ConstructionYard
                            && o.ReadyStructure == c.AuxId) { readyCy = i; break; }
                    }
                    // ADR-023: nothing ready in any lane 1, so try the second
                    // lanes. Walked by ENTITY INDEX, never by dictionary order,
                    // so the choice cannot depend on hash-table layout.
                    if (readyCy < 0)
                    {
                        for (int i = 0; i < _entities.Count; i++)
                        {
                            var o = _entities[i];
                            if (!o.Alive || !IsOwnedBy(in o, c.PlayerId) || o.Kind != EntityKind.ConstructionYard) continue;
                            if (LaneOf(i) is { } l && l.Ready == c.AuxId) { readyCy = i; readyInLane2 = true; break; }
                        }
                    }
                    if (readyCy < 0) break;
                }
                // Order matters: validate before charging, charge before spawning.
                if (!ValidPlacement(c.PlayerId, ax, ay, c.AuxId)) break;
                if (barrier)
                {
                    _credits[c.PlayerId] -= sd.Cost;
                }
                else if (readyInLane2)
                {
                    // ADR-023: the second lane's slot clears, and the lane is
                    // pruned if that was the last thing it was holding.
                    var l = _lanes[readyCy];
                    l.Ready = 0;
                    PruneLane(readyCy);
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
                    // P7-5 (DR-02): the TYPE, not just the kind. This switch is
                    // keyed on Kind, and Kind stopped identifying a building the
                    // moment a second power plant existed - so a Sodality player
                    // placing a generator got a Directorate plant, silently and
                    // at the generator's price. The Emplacement case below has
                    // carried the same collision since P7-2b and answers it the
                    // same way, by asking c.AuxId which building was actually
                    // ordered.
                    case EntityKind.PowerPlant: SpawnPowerPlant(c.PlayerId, ax, ay, structType: c.AuxId); break;
                    case EntityKind.Factory: SpawnFactory(c.PlayerId, ax, ay); break;
                    case EntityKind.Refinery: SpawnRefinery(c.PlayerId, ax, ay); break;
                    case EntityKind.ConstructionYard: SpawnConstructionYard(c.PlayerId, ax, ay); break;
                    case EntityKind.Turret: SpawnTurret(c.PlayerId, ax, ay); break;
                    case EntityKind.Emplacement:
                        if (c.AuxId == 18) SpawnFactionDefence(c.PlayerId, 18, ax, ay);
                        else SpawnEmplacement(c.PlayerId, ax, ay);
                        break;   // P7-2 / P7-2b
                    case EntityKind.Airfield: SpawnAirfield(c.PlayerId, ax, ay); break;         // ADR-028
                    // P7-2b: routed by STRUCT TYPE rather than kind, because the
                    // Shroud Nest shares EntityKind.Emplacement with the common
                    // one and only its type tells them apart.
                    case EntityKind.Bastion: SpawnFactionDefence(c.PlayerId, 17, ax, ay); break;
                    // P7-5c: the TYPE, not just the kind - the second superweapon
                    // walks straight into the trap P7-5a paid for at the power
                    // plant one wave ago. Without c.AuxId a Sodality player
                    // ordering a seismic charge would be handed an orbital
                    // cannon, silently, and the whole row would be decoration.
                    case EntityKind.Superweapon: SpawnSuperweapon(c.PlayerId, ax, ay, structType: c.AuxId); break;
                    case EntityKind.VeilProjector: SpawnVeilProjector(c.PlayerId, ax, ay); break;
                    case EntityKind.ServiceDepot: SpawnServiceDepot(c.PlayerId, ax, ay); break;
                    case EntityKind.Wall: SpawnWall(c.PlayerId, ax, ay); break;
                    case EntityKind.Gate: SpawnGate(c.PlayerId, ax, ay); break;                // P7-10
                    case EntityKind.Barracks: SpawnBarracks(c.PlayerId, ax, ay); break;
                    case EntityKind.RadarUplink: SpawnRadarUplink(c.PlayerId, ax, ay); break;
                    case EntityKind.Mine: SpawnMine(c.PlayerId, ax, ay); break;               // P7-11c
                    case EntityKind.WatchPost: SpawnWatchPost(c.PlayerId, ax, ay); break;    // P7-5b
                }
                break;
            }
            case CommandType.BuildStructure:
            {
                if (e.Kind != EntityKind.ConstructionYard) break;
                var bd = GetStructureType(c.AuxId);
                if (bd.Cost <= 0 || bd.BuildTicks <= 0) break; // CYs are MCV-deployed, never queued
                // The veil projector is Sodality doctrine; all other structures are common (for now).
                // ADR-009 clause 3: the faction gate stays and is ORTHOGONAL
                // to the tree - the veil now also needs a power plant, and a
                // Directorate player is refused it either way.
                if (!StructureAllowedForFaction(c.AuxId, _playerFaction[c.PlayerId])) break;
                // ADR-009 clause 3: the structure tech tree, authored in
                // /data/buildings and enforced here. Clause 4 pins the
                // semantic: the gate is on QUEUEING, so killing a
                // prerequisite mid-build does not cancel what is already in
                // the queue.
                if (!HasPrereqs(c.PlayerId, bd.Prereqs)) break;
                // P7-11c: the per-type building cap, refused where the order is
                // QUEUED as well as where it is placed, so a player at the cap
                // never sinks credits into a mine that can never be sited.
                if (AtMaxAliveStructure(c.PlayerId, c.AuxId)) break;
                if (!_queues.TryGetValue(e.Id, out var bq)) _queues[e.Id] = bq = new List<int>();
                // ADR-023, THE OVERFLOW RULE, and the whole reason this wave is
                // hash-neutral. Lane 1 takes the order whenever lane 1 is IDLE;
                // the second lane is reached only when lane 1 is already busy.
                //
                // The rejected alternative - route by category, defences always
                // to lane 2 - moves goldens twice over: the construction
                // scenario's turret would leave lane 1, and QueueLength(cy)
                // would read 0 while a turret built in lane 2, so the AI (which
                // only ever queues when that reads 0) would order extra
                // buildings and every AI-driven golden would DIVERGE
                // BEHAVIOURALLY. Under overflow, a serial commander never
                // reaches the second lane at all, which is exactly why no
                // golden scenario does.
                if (bq.Count == 0 && e.BuildProgress == 0 && e.ReadyStructure == 0)
                {
                    bq.Add(c.AuxId);
                    break;
                }
                if (!_lanes.TryGetValue(e.Id, out var lane)) _lanes[e.Id] = lane = new BuildLane();
                lane.Queue.Add(c.AuxId);
                break;
            }
            case CommandType.Deploy:
            {
                if (e.Kind != EntityKind.Unit || e.UnitType != McvUnitType) break; // MCVs only
                int dax = Map.CellOf(e.X), day = Map.CellOf(e.Y);
                if (!ValidFoundation(dax, day, c.EntityId)) break;
                e.Alive = false; // the vehicle IS the building
                int newCy = SpawnConstructionYard(e.PlayerId, dax, day);
                _events.Add(new GameEvent(GameEventType.Deployed, c.EntityId, newCy));
                break;
            }
            case CommandType.LoadTransport:
            {
                // P7-3. EntityId is the unit doing the boarding, AuxId the
                // transport, matching Attack and Harvest where the actor is the
                // entity and the target is the aux.
                if (e.Kind != EntityKind.Unit || !IsCarryable(e.UnitType)) break;
                if (c.AuxId < 0 || c.AuxId >= _entities.Count) break;
                var carrier = _entities[c.AuxId];
                // P7-8g: ownership. You board YOUR transport, and teams does not
                // change that even once an ally's transport is not an enemy.
                if (!carrier.Alive || !IsOwnedBy(in carrier, e.PlayerId)
                    || carrier.Kind != EntityKind.Unit || carrier.UnitType != CarrierUnitType) break;
                if (!_cargo.TryGetValue(c.AuxId, out var hold)) hold = null;
                if ((hold?.Count ?? 0) >= CarrierCapacity) break;

                // Out of reach: WALK there rather than refusing. The order is
                // re-issued each beat by whoever gave it, so this is
                // self-healing if the transport moves - the same shape the AI's
                // engineer-to-outpost order uses.
                if (Fix64.DistSq(e.X - carrier.X, e.Y - carrier.Y) > Fix64.FromInt(4))
                {
                    e.TargetX = carrier.X; e.TargetY = carrier.Y;
                    e.Moving = true; e.UseFlow = true; e.AMove = false;
                    e.StallTicks = 0; e.NearestApproachSq = Fix64.Zero; e.NoProgressTicks = 0;
                    break;
                }

                if (hold is null) _cargo[c.AuxId] = hold = new List<CargoUnit>();
                hold.Add(new CargoUnit(e.UnitType, e.Hp, e.Rank));
                // Despawned, not flagged. A carried unit that stayed alive would
                // have to be skipped by movement, combat, separation, selection
                // and drawing, and every one of those skips is an enumeration
                // somebody forgets - this phase has already paid for that lesson
                // three times.
                e.Alive = false;
                e.Moving = false;
                _events.Add(new GameEvent(GameEventType.Died, c.EntityId, -1));
                break;
            }
            case CommandType.UnloadTransport:
            {
                // P7-3. Sets the whole hold down around the transport, in the
                // order it was loaded, using the same deterministic spawn ring
                // the producers use so two clients place them identically.
                if (e.Kind != EntityKind.Unit || e.UnitType != CarrierUnitType) break;
                if (!_cargo.TryGetValue(c.EntityId, out var hold) || hold.Count == 0) break;
                int ax = Map.CellOf(e.X), ay = Map.CellOf(e.Y);
                int placed = 0;
                foreach (var cu in hold)
                {
                    // The producers' own spawn ring, walked in its committed
                    // order, so two clients set the hold down identically.
                    int sx = -1, sy = -1;
                    foreach (var (dx, dy) in SpawnOffsets)
                    {
                        int nx = ax + dx, ny = ay + dy;
                        if (nx < 0 || ny < 0 || nx >= Map.Width || ny >= Map.Height) continue;
                        if (Map.IsBlocked(nx, ny) || CellOccupied(nx, ny)) continue;
                        sx = nx; sy = ny; break;
                    }
                    if (sx < 0) break;   // ringed in: what is left stays aboard
                    var def = GetUnitType(cu.UnitType);
                    int id = SpawnUnit(e.PlayerId, Map.CellCentre(sx), Map.CellCentre(sy),
                                       def.Speed, cu.Hp, def.Armour, def.WeaponId,
                                       veterancy: def.Veterancy, unitType: cu.UnitType);
                    var landed = _entities[id];
                    landed.Rank = cu.Rank;          // a veteran does not lose its rank in transit
                    landed.Hp = cu.Hp;
                    _entities[id] = landed;
                    _events.Add(new GameEvent(GameEventType.ProductionComplete, c.EntityId, id));
                    placed++;
                }
                hold.RemoveRange(0, placed);
                // PRUNE on empty, which is what makes the hash fold sound: no
                // entry provably means nothing carried.
                if (hold.Count == 0) _cargo.Remove(c.EntityId);
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
                // BD-05: give back what the player has already PAID toward
                // whatever this producer was building. Selling looked only at
                // the building's own cost, so a Construction Yard holding a
                // finished superweapon in its ready slot sold for 1500 while the
                // 4000 already spent simply vanished, with nothing said.
                RefundPendingOnSell(ref e);
                e.Alive = false;
                FootprintOnDeath(in e);   // ADR-025
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
            case CommandType.SetStance:
            {
                // ADR-015: units only carry a stance (ownership is guarded at the
                // top of this method). AuxId is the target stance value; an
                // unknown value is ignored rather than defaulting to a real one.
                if (e.Kind != EntityKind.Unit) break;
                switch ((Stance)c.AuxId)
                {
                    case Stance.Aggressive:
                    case Stance.HoldFire:
                        // Fire-discipline stances leave movement untouched: a unit
                        // set to hold-fire mid-march keeps marching, it just stops
                        // auto-firing. Clear the positional fields so a hold-fire
                        // or aggressive unit serialises canonically (posts belong
                        // to guard and patrol alone).
                        e.Stance = (Stance)c.AuxId;
                        e.PostX = Fix64.Zero; e.PostY = Fix64.Zero;
                        e.PatrolX = Fix64.Zero; e.PatrolY = Fix64.Zero;
                        e.PatrolOutbound = false;
                        break;
                    case Stance.Guard:
                        // Guard-in-place: the post is where the unit stands now.
                        // StanceSystem halts it here next tick and thereafter
                        // engages intruders within its leash and returns.
                        e.Stance = Stance.Guard;
                        e.PostX = e.X; e.PostY = e.Y;
                        e.PatrolX = Fix64.Zero; e.PatrolY = Fix64.Zero;
                        e.PatrolOutbound = false;
                        break;
                    case Stance.Patrol:
                        // Ping-pong between the current spot (endpoint A) and the
                        // ordered point (endpoint B), clamped exactly as Move does.
                        // Kick off the outbound attack-move leg toward B; the leg
                        // engages en route and StanceSystem flips endpoints when it
                        // completes.
                        e.Stance = Stance.Patrol;
                        e.PostX = e.X; e.PostY = e.Y;
                        e.PatrolX = Fix64.Clamp(c.X, Fix64.Zero, Fix64.FromInt(Map.Width) - Fix64.Half);
                        e.PatrolY = Fix64.Clamp(c.Y, Fix64.Zero, Fix64.FromInt(Map.Height) - Fix64.Half);
                        e.PatrolOutbound = true;
                        e.TargetX = e.PatrolX; e.TargetY = e.PatrolY;
                        e.AMoveX = e.PatrolX; e.AMoveY = e.PatrolY;
                        e.Moving = true; e.UseFlow = true; e.AMove = true;
                        e.ExplicitTarget = -1; e.HState = HarvestState.Idle;
                        e.StallTicks = 0; e.NearestApproachSq = Fix64.Zero; e.NoProgressTicks = 0;
                        break;
                    default:
                        break; // unknown stance value: ignore
                }
                break;
            }
            case CommandType.Produce:
            {
                // ADR-009 clause 1: any producer may receive Produce.
                if (!IsProducer(e.Kind)) break;
                var pdef = GetUnitType(c.AuxId);
                if (pdef.Cost <= 0) break;
                if (pdef.Faction != FactionCommon
                    && pdef.Faction != _playerFaction[c.PlayerId]) break; // not your side's hardware
                // ADR-009 clause 2, THE BARRACKS SPLIT, and it really is this
                // one line: the producing structure must be the kind the unit
                // is authored to come out of. A factory refuses infantry, a
                // barracks refuses vehicles, and a Construction Yard refuses
                // both (its StructType is 4 and no unit names it), which is
                // what keeps a unit order out of the structure queue. `break`
                // rather than `return` so the shared writeback epilogue below
                // still runs, exactly as the ADR specifies.
                if (e.StructType != pdef.ProducedAt) break;
                // ADR-009 clause 2: and you must own what it is built behind.
                if (!HasPrereqs(c.PlayerId, pdef.Prereqs)) break;
                // P7-11b: and you may not order one you already have your
                // allowance of. The FIRST of two enforcement points, and both are
                // needed: this one alone would let a player order two heroes
                // while owning none, since the count is of living units and none
                // is fewer than one. `break` rather than `return`, matching every
                // other refusal in this case, so the writeback epilogue still
                // runs. Inert unless the def carries a cap, which is two units in
                // the whole catalogue.
                if (AtMaxAlive(c.PlayerId, c.AuxId)) break;
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
    // Public since DR-10: the AI's Fire Sale asks the same question the sell
    // handler asks ("would SellStructure accept this kind?"), and the standing
    // rule is one conceptual rule, one implementation - the AI calls the sell
    // path's own predicate rather than keeping a copy that could drift.
    public static bool IsStructure(EntityKind k)
        => k is EntityKind.Refinery or EntityKind.Factory or EntityKind.PowerPlant
             or EntityKind.ConstructionYard or EntityKind.Turret or EntityKind.Superweapon
             or EntityKind.VeilProjector or EntityKind.ServiceDepot or EntityKind.Wall
             or EntityKind.Barracks or EntityKind.RadarUplink or EntityKind.Airfield
             // P7-2: the Emplacement, and Bastion alongside it. Bastion spawns
             // from nothing today and is therefore inert here, but it is added
             // in the same breath deliberately: this predicate is an
             // ENUMERATION, and the Emplacement has just proved what that
             // costs. Left out of this list a building is not a structure to
             // the sim at all - not power-gated, not repairable, not sellable,
             // not counted for victory - and every one of those failures is
             // silent. Adding the reserved kind now closes the identical trap
             // before it is stepped in.
             or EntityKind.Emplacement or EntityKind.Bastion
             // P7-5b: the Watch Post, added in the same breath as its kind for
             // the reason this comment already gives twice over.
             or EntityKind.WatchPost
             // ADR-021: the Outpost is a structure, which is what makes it
             // engineer-capturable through the untouched CaptureSystem (whose
             // only ownership test, IsOwnedBy(t, e.PlayerId), a neutral -1
             // passes). VictorySystem excludes it from hope explicitly.
             or EntityKind.Outpost
             // ADR-025: the Bridge is a structure so that it takes damage, dies,
             // and runs the footprint path on death (where it BLOCKS rather than
             // unblocks). It is not hope, because the hope test skips
             // PlayerId < 0 and a bridge is always neutral; and it is not
             // capturable, which CaptureSystem excludes explicitly, because
             // capture is the outpost's whole point and would be nonsense here.
             or EntityKind.Bridge
             // P7-11c: the Mine is a structure, and that membership is the
             // feature rather than a detail. It is what makes it placeable
             // through PlaceStructure, damageable, killable, sellable and
             // counted by every scan that says "a building". Left out of this
             // enumeration it would be a thing the sim does not recognise at
             // all, which the Emplacement's entry above records the cost of.
             or EntityKind.Mine
             // P7-10: the Gate, on the Mine's argument. It must be a structure
             // to be placeable, sellable, repairable, damageable and blocking,
             // and every one of those is inherited by being in this list.
             or EntityKind.Gate;

    /// <summary>
    /// A barrier is a structure for blocking, selling, repairing and damage, and
    /// is excluded from the victory test, engineer capture and combat
    /// auto-acquisition (ADR-005 clause 2).
    ///
    /// P7-10 added the gate, which is what this predicate spent three waves
    /// waiting for, and made it PUBLIC in the same breath. Four places outside
    /// the sim ask exactly this question and each had written it as
    /// `Kind == EntityKind.Wall`: the sidebar deciding whether a button enters
    /// placement or queues at a yard, the client's placement path, /data's own
    /// queueability check in StructureCatalogue.ToTypeDef, and
    /// reachabilitygate's twin of it. A rule that names one kind is missed by
    /// whoever adds the next one, and here the miss would have been silent - a
    /// gate button that queued an order BuildStructure refuses, so the button
    /// would exist and do nothing.
    /// </summary>
    public static bool IsBarrier(EntityKind k) => k is EntityKind.Wall or EntityKind.Gate;

    /// <summary>
    /// ADR-009 clause 1: the producer notion. Factory, Construction Yard,
    /// Barracks and, since this was finally actioned, the Airfield. Used at
    /// FOUR sites, three of which fail silently if missed: Produce's kind test,
    /// ProductionSystem's producer test (miss it and the queue never advances
    /// with no error), CancelProduce (miss it and orders are uncancellable),
    /// and the queue hash (which widens to all producer queues, closing
    /// PROD-D5, inside PROD-04's regeneration).
    ///
    /// This comment used to end "the Airfield joins when it exists (it is a
    /// slot-model producer and waits on the air-layer ADR)". ADR-028 shipped
    /// the air layer, the Airfield exists, and NOBODY CAME BACK. The
    /// consequence was not subtle: Produce breaks on this predicate before it
    /// reads anything else, so the Strike Flyer could not be built by anybody,
    /// in any mode, from the day it shipped. An entire tier of the game was
    /// unreachable behind a to-do note.
    ///
    /// It went unseen because `airgate` spawns its flyers with SpawnUnit and
    /// never ORDERS one, so it proved everything about how an aircraft behaves
    /// and nothing about whether a player can have one. That is the same shape
    /// as P7-7a and the sidebar's missing buttons, and it is why the gate for
    /// this fix issues a Produce command rather than constructing the outcome.
    ///
    /// "Slot-model producer" is left UNBUILT rather than invented: aircraft
    /// occupying pads, with the airfield's capacity limiting how many can be
    /// aloft, is a real design and is not what this fixes. The Airfield queues
    /// like every other producer, which is the smallest thing that makes the
    /// tier reachable.
    /// </summary>
    private static bool IsProducer(EntityKind k)
        => k is EntityKind.Factory or EntityKind.ConstructionYard or EntityKind.Barracks
             or EntityKind.Airfield;

    /// <summary>
    /// ADR-009 clause 2: does this player own a LIVING instance of every
    /// required structure type? An entity-index scan, which is deterministic
    /// by construction and O(entities) per command rather than per tick, so it
    /// is negligible against the TDD s6 budget. Null and empty both mean "no
    /// prerequisites" and both pass. StructType is 0 on every non-structure,
    /// and no required id is ever 0, so units can never satisfy a
    /// <summary>TICKET-P2-SIM-08: what a repair costs and mends per TICK, for a
    /// structure under the repair toggle and for each unit inside a depot's or a
    /// repair vehicle's aura alike. Named because the CLIENT quoted the derived
    /// per-second figure ("15 cr/s") hand-multiplied in three readouts, so a
    /// rate change would have made the HUD lie in three places at once while the
    /// treasury drained at the new rate.</summary>
    public const int RepairCreditsPerTick = 1;
    public const int RepairHpPerTick = 2;

    /// <summary>Struct type 7, the Veil Projector: Sodality doctrine. Named
    /// rather than spelled 7 in the gate below, because it was spelled 7 in the
    /// sim AND in the sidebar, in two projects, with nothing tying them.</summary>
    public const int VeilStructType = 7;

    /// <summary>
    /// May this faction build this structure? Sodality doctrine covers the Veil
    /// Projector; everything else is common, for now.
    ///
    /// PUBLIC and called by the sidebar, which used to hold its own `typeId !=
    /// VeilType || faction == Sodality` and its own `const int VeilType = 7`.
    /// The two agreed, so the visible symptom was nil - but the UNIT faction
    /// gate beside it reads the live catalogue column, so the day a second
    /// faction-specific structure is authored, the sidebar offers it to both
    /// sides and the sim refuses it for one, silently.
    ///
    /// The permanent fix is a Faction column on StructureTypeDef to match
    /// UnitTypeDef, which is a /data schema change and therefore its own wave
    /// with its own catalogue-checksum argument. This collapses the two copies
    /// to one in the meantime, at no hash cost: a pure static predicate.
    /// </summary>
    /// P7-1 replaced the static special case with a read of the catalogue. It
    /// used to be `structType != VeilStructType || faction == FactionSodality`
    /// - one structure named in code, and every building's authored `faction:`
    /// ignored. Now the def answers, so /data is the authority the ADR-006 wave
    /// said it was. An unfactioned def is FactionCommon and buildable by both,
    /// which is what every compiled default and every previously-ignored YAML
    /// line resolved to in practice, so this reads exactly as before until a
    /// building actually declares a side.
    public bool StructureAllowedForFaction(int structType, int faction)
    {
        int owner = GetStructureType(structType).Faction;
        return owner == FactionCommon || owner == faction;
    }

    /// <summary>
    /// P7-5d: the buildable structure of a given KIND that this player's side may
    /// build, or 0 for none. The capability query the AI ladder needed.
    ///
    /// Three faction rows in a row (ADR-042, 043, 044) each split a building the
    /// two sides used to share, and each time the AI's ladder went on naming the
    /// Directorate's type id as a literal. That is how a Sodality commander ended
    /// up unable to queue a superweapon AT ALL: the ladder asks for "structure
    /// type 6" and type 6 stopped being something it could build.
    ///
    /// So the ladder asks what it actually means - "the superweapon I can build"
    /// - and gets it from the catalogue. A side with no answer gets 0 and the
    /// rung is skipped rather than jamming, which is the behaviour that matters
    /// most: a commander must never queue a building it will be refused, because
    /// the yard stalls on it forever.
    ///
    /// Ascending type id, so the answer cannot depend on dictionary order.
    /// BuildTicks &gt; 0 excludes the map-placed kinds, matching the yard's own
    /// queueability rule rather than restating it.
    /// </summary>
    public int BuildableStructOfKind(int player, EntityKind kind)
    {
        int faction = _playerFaction[player];
        for (int t = 1; t <= MaxStructType; t++)
        {
            var d = GetStructureType(t);
            if (d.Kind == kind && d.BuildTicks > 0 && StructureAllowedForFaction(t, faction)) return t;
        }
        return 0;
    }

    /// <summary>
    /// P7-5d: the buildable DETECTOR building this player's side may build, or 0
    /// for none - which is the honest answer for the Directorate, whose detector
    /// is a unit (the Sentinel Scout) rather than a building.
    ///
    /// Asked as a property rather than by naming the Watch Post, so a Directorate
    /// detector building would be picked up the day one is authored, and so this
    /// says what it means: "something of mine that reveals cloak".
    /// </summary>
    public int BuildableDetectorStruct(int player)
    {
        int faction = _playerFaction[player];
        for (int t = 1; t <= MaxStructType; t++)
        {
            var d = GetStructureType(t);
            if (d.Detector && d.BuildTicks > 0 && StructureAllowedForFaction(t, faction)) return t;
        }
        return 0;
    }

    /// <summary>
    /// ADR-009 clause 2: does this player own a living instance of every
    /// prerequisite?
    ///
    /// PUBLIC so the sidebar can CALL it instead of keeping its own copy. The
    /// client had an OwnsStructType plus a PrereqsMet fold that reproduced this
    /// scan exactly, and the failure mode on any drift between them is the worst
    /// kind: a LIT BUTTON whose order the sim then silently drops, because the
    /// panel and the gate disagreed about what is buildable. A read-only
    /// question over state the client can already see, so exporting it moves no
    /// hash and hands the client no lever.
    /// </summary>
    /// <summary>
    /// P7-11b: does this player already own as many LIVING units of this type as
    /// the catalogue allows? GDD s7 line 62's "one at a time" is the only
    /// instance today, but it is built as a general per-type cap rather than a
    /// rule about the hero, because the sim had no per-unit-type build limit at
    /// all (the only per-player limit anywhere was MaxBarriersPerPlayer) and a
    /// hero-shaped one would have to be rewritten by whoever wants the second.
    ///
    /// A cap of 0 means UNLIMITED and returns false before anything is counted.
    /// That is what every unit but the two heroes carries, so both call sites are
    /// a single dictionary read and a compare for the entire existing catalogue,
    /// which is why all 24 goldens stand byte-identical.
    ///
    /// PUBLIC so the sidebar can ask the same question the sim gates on, exactly
    /// as HasPrereqs is public: a lit button whose order the sim then silently
    /// drops is the worst failure mode a build panel has.
    ///
    /// An entity-index scan, deterministic by construction, and the count stops
    /// at the cap rather than totalling the army.
    /// </summary>
    public bool AtMaxAlive(int player, int unitType)
    {
        int cap = GetUnitType(unitType).MaxAlive;
        if (cap <= 0) return false;
        int alive = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (o.Alive && IsOwnedBy(in o, player) && o.UnitType == unitType && ++alive >= cap) return true;
        }
        return false;
    }

    /// <summary>
    /// P7-11c: AtMaxAlive for the BUILDING catalogue, and deliberately its twin
    /// down to the shape rather than a differently-argued rule. A cap of 0 means
    /// unlimited and returns false before anything is counted, which is what
    /// every structure but the Mine carries, so this is one dictionary read and
    /// a compare for the whole existing catalogue and no golden can move on it.
    ///
    /// Enforced at BOTH command-path points, which is the lesson P7-11b's own
    /// gate paid for: refusing only at PlaceStructure would let a player queue
    /// and PAY for a mine past the cap and then find it unplaceable, with the
    /// credits sunk in a ready slot that can never be spent.
    ///
    /// PUBLIC for HasPrereqs' and AtMaxAlive's reason: the sidebar must be able
    /// to ask the same question the sim gates on, or it lights a button whose
    /// order is then silently dropped.
    ///
    /// An entity-index scan, deterministic by construction, stopping at the cap
    /// rather than totalling the base.
    /// </summary>
    public bool AtMaxAliveStructure(int player, int structType)
    {
        int cap = GetStructureType(structType).MaxAlive;
        if (cap <= 0) return false;
        int alive = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (o.Alive && IsOwnedBy(in o, player) && o.StructType == structType
                && IsStructure(o.Kind) && ++alive >= cap) return true;
        }
        return false;
    }

    /// <summary>
    /// P7-5 (DR-02) made this ask for a CAPABILITY rather than an instance, and
    /// the row could not be built without it.
    ///
    /// Every prerequisite in the tree names a structure TYPE ID, and five of
    /// them name type 1, the power plant. The moment the two sides stop sharing
    /// one plant - which is exactly what GDD s3's "centralised" against
    /// "decentralised" asks for - a Sodality player holding three generators
    /// satisfies no prerequisite in the game and can build nothing but a
    /// generator, forever. Not a balance problem: a dead end.
    ///
    /// So a prerequisite is satisfied by any owned structure of the same KIND as
    /// the type named, and the authored id is an EXEMPLAR of the capability
    /// rather than the only thing that provides it. "You need a power plant",
    /// not "you need building number one".
    ///
    /// This is the same correction P7 has now made about a dozen times - read a
    /// rule as the property it means rather than the instance it names - and it
    /// is hash-neutral by construction here, because no two structure types
    /// share a Kind that anything requires. The one Kind with two types today is
    /// Emplacement (15 and 18), which nothing takes as a prerequisite; when
    /// something does, the Shroud Nest satisfying an Emplacement requirement is
    /// the intended reading rather than a leak.
    /// </summary>
    public bool HasPrereqs(int player, int[]? ids)
    {
        if (ids == null) return true;
        for (int r = 0; r < ids.Length; r++)
        {
            EntityKind need = GetStructureType(ids[r]).Kind;
            bool found = false;
            for (int i = 0; i < _entities.Count; i++)
            {
                var o = _entities[i];
                // P7-8g: ownership. A prerequisite is a building YOU hold.
                //
                // P7-8c ANSWERED THE QUESTION AND LEFT THIS LINE ALONE, and the
                // answer is deliberate rather than an oversight: an ally's radar
                // does NOT unlock your tech, and each player builds their own
                // tree. Sharing a tech tree is a separate design lever from
                // sharing a war, and it is the one that would matter most - a 4v4
                // where one seat builds the radar and three seats spend nothing
                // makes the tree free, which is not what "up to 4v4" was asking
                // for. IsOwnedBy, never IsAlliedTo.
                if (o.Alive && IsOwnedBy(in o, player) && IsStructure(o.Kind) && o.Kind == need) { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>
    /// ADR-007: the structures SetRally accepts - producing structures only.
    /// ADR-009 widened this predicate to IsProducer in place, exactly as B2's
    /// delivery note said it would, so the wire format never changed twice:
    /// the barracks rallies (infantry want it most) and the Construction Yard
    /// keeps accepting the command as inert state (its products are placed,
    /// not spawned; the client offers no affordance on it).
    /// </summary>
    private static bool IsRallyable(EntityKind k) => IsProducer(k);

    /// <summary>
    /// ADR-015: a new movement, stop or attack order supersedes a standing
    /// GUARD or PATROL (both are positional activities the order overrides),
    /// dropping the unit back to Aggressive and clearing the post/waypoint
    /// fields. HoldFire is deliberately PRESERVED: fire discipline is a
    /// persisting preference that must survive the very Move that carries Q003's
    /// engineer past the sentry. Aggressive and HoldFire units are left
    /// untouched, so this is a no-op for the default stance and the golden move
    /// stays purely the hashed-field append.
    /// </summary>
    private static void CancelPositionalStance(ref Entity e)
    {
        if (e.Stance is not (Stance.Guard or Stance.Patrol)) return;
        e.Stance = Stance.Aggressive;
        e.PostX = Fix64.Zero; e.PostY = Fix64.Zero;
        e.PatrolX = Fix64.Zero; e.PatrolY = Fix64.Zero;
        e.PatrolOutbound = false;
    }

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
        // P7-10 removed a wholesale `IsStructure(o.Kind) continue` from this
        // loop, whose comment read "structure cells are already blocked". That
        // was true of every structure until the Mine, and the gate makes it
        // false a second time and dangerously so.
        //
        // Dropping the skip is INERT for everything that does block: the loop
        // above has already refused any candidate footprint overlapping a blocked
        // cell, so a blocking structure can never reach this test with an
        // overlap. What it now catches is the structure that does NOT block its
        // ground - an OPEN gate, and the mine before it - whose cell would
        // otherwise read as free ground. Left in, a player could open their own
        // gate by standing beside it, drop a wall segment on top of it, and then
        // watch the gate's next opening UNBLOCK the wall's cell: units walking
        // through a wall, from a legal sequence of ordinary commands.
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (!o.Alive) continue;
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
            if (!o.Alive || !IsOwnedBy(in o, player) || !IsStructure(o.Kind)) continue;
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
            if (!r.Alive || r.Kind != EntityKind.Refinery || !IsOwnedBy(in r, player)) continue;
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
            // P7-11a: a sabotaged building is off the grid entirely, supplying
            // nothing and drawing nothing. This is the load-bearing site of the
            // three: switching a plant off browns the base out through ADR-008's
            // EXISTING rules - the turret gate, the production rate scaling, the
            // superweapon charge - rather than through any consequence invented
            // for the saboteur. Dropping the draw as well as the supply is
            // deliberate: a building that consumes nothing while it does nothing
            // is the honest reading, and it stops sabotaging a barracks from
            // browning out the base that owns it.
            if (IsDisabled(e.Id)) continue;
            supply[e.PlayerId] += e.PowerSupply;
            draw[e.PlayerId] += e.PowerDraw;
        }
    }

    /// <summary>P7-11a: one player's power tally, computed by the SIM's own
    /// ComputePower rather than by a copy of its loop. Public for the reason
    /// AtLeast75 is public: a gate that re-summed PowerSupply itself would pass
    /// whether or not the sabotage rule had been applied at all, so it would
    /// prove nothing. Pure and stateless, so exporting it moves no hash and
    /// gives no caller a lever: it is a question, not a command.</summary>
    public (int Supply, int Draw) PowerOf(int player)
    {
        Span<int> supply = stackalloc int[_players];
        Span<int> draw = stackalloc int[_players];
        ComputePower(supply, draw);
        return (supply[player], draw[player]);
    }

    /// <summary>
    /// GDD s7 line 85's 75 per cent threshold in integer maths with no
    /// division. Read by CombatSystem's turret gate (ADR-008 clause 1) against
    /// the pre-combat tally; the boundary is inclusive.
    ///
    /// PUBLIC so the client can CALL it rather than copy it. This comment used
    /// to claim it was "the same expression the client's power bar already uses
    /// (Sidebar.cs), so the sim's notion of a brown-out and the UI's cannot
    /// drift". That was an aspiration, not a mechanism: there were FOUR copies
    /// of the expression - this one, the client's klaxon, its per-owner turret
    /// dim, and its power bar - agreeing only because nobody had yet edited one
    /// of them. Now there is one, and the claim is true. Pure and stateless, so
    /// exporting it moves no hash and gives the client no way to reach into the
    /// sim: it is a question, not a lever.
    /// </summary>
    public static bool AtLeast75(int supply, int draw) => draw <= 0 || supply * 4 >= draw * 3;

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

        // ADR-028: an aircraft does not consult the flow field. It steps
        // straight at its destination and no terrain, structure or unit is in
        // its way, which is the whole of what "air" means in this sim. Gated on
        // the TYPE, so a world with no aircraft in it never reaches this branch
        // and executes exactly the code it did before the air layer existed -
        // which is why the goldens do not move.
        if (IsAirborne(e))
        {
            Fix64 adx = aimX - e.X, ady = aimY - e.Y;
            Fix64 adistSq = Fix64.DistSq(adx, ady);
            Fix64 astepSq = e.Speed * e.Speed;
            if (adistSq <= astepSq) { e.X = aimX; e.Y = aimY; e.Moving = false; }
            else
            {
                Fix64 adist = Fix64.Sqrt(adistSq);
                e.X += adx * e.Speed / adist;
                e.Y += ady * e.Speed / adist;
            }
            return;
        }

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
                // P7-8g: ownership. The veil covers YOUR units.
                //
                // P7-8c ANSWERED THE QUESTION AND LEFT THIS LINE ALONE: a
                // projector hides its OWNER's units, not an ally's. Deliberate,
                // not an oversight - the projector is a building one player paid
                // for and powers, and a cloak that spilled onto every allied unit
                // parked beside it would make one seat's investment the whole
                // team's, which is a different (and much stronger) building than
                // the one that was priced. IsOwnedBy, never IsAlliedTo.
                if (!e.Alive || !IsOwnedBy(in e, vp.PlayerId)) continue;
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
                // P7-8g: written as NOT-MINE rather than as hostility, and the
                // two are not the same predicate - "not mine" also uncloaks a
                // NEUTRAL. Nothing neutral carries Stealth or FieldCloaked today,
                // so the choice is unobservable, and the exact expression that
                // stood here was kept rather than tightened on a guess.
                //
                // P7-8c widened NOT-MINE to NOT-ALLIED, which is the same
                // reasoning applied one seat wider: a sweep exists to find things
                // you cannot see, and a teammate's phantom is not one of them.
                // Revealing it would also cost the ally its stealth against
                // everyone, because DetectedMask is read per observer but written
                // here for every detector in the game. The neutral case is
                // untouched: a neutral is allied to nobody, so it stays uncloaked
                // exactly as before.
                if (!e.Alive || !(e.Stealth || e.FieldCloaked) || IsAlliedTo(in e, det.PlayerId)) continue;
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
    /// Has this world ever held a mine? The gate on MineSystem's scan, and the
    /// reason all 24 goldens stay byte-identical: no golden scenario, save or
    /// replay places one, so the flag is never set and the system costs a
    /// single branch per tick.
    ///
    /// Deliberately NOT hashed and NOT serialized. It is a fast path and
    /// nothing else: with no LIVING mine the scan below writes no entity,
    /// raises no event and draws no random number, so a world that resumes with
    /// this false is in the identical state to one that resumes with it true.
    /// The deserializer sets it from the entities it reads (see
    /// World.Serialization.cs) so a save taken mid-minefield resumes on the
    /// fast path's correct side rather than relying on that equivalence.
    ///
    /// Monotone on purpose: it is never cleared when the last mine dies,
    /// because a player who laid one will lay another and a flag that flickered
    /// would be one more thing to keep true.
    /// </summary>
    private bool _minesInPlay;

    /// <summary>
    /// P7-11c: the proximity trigger. A mine detonates when a living enemy UNIT
    /// or HARVESTER comes within MineTriggerRadiusSq of it, and detonating
    /// consumes it.
    ///
    /// SCAN THEN APPLY, and the choice is the whole determinism argument of
    /// this wave rather than a style preference.
    ///
    /// Phase one reads the tick's settled positions and collects every mine
    /// whose trigger holds, walking ENTITY INDICES ascending - never a
    /// dictionary, never a set, so the collection order is the world's own
    /// order on every machine. Phase two consumes all of them, then blasts all
    /// of them, in that same index order.
    ///
    /// Apply-as-you-go was rejected and the counter-example is exact: mine A at
    /// a lower index kills the scout that was one tick from mine B, so under
    /// apply-as-you-go B reads a dead trigger and does NOT go off, and whether
    /// it goes off depends on which mine happens to hold the lower index. That
    /// is a rule the player cannot predict and, worse, a rule whose outcome
    /// changes if entity ids are ever assigned differently. Under scan-then-
    /// apply both mines saw a living target when the tick began, so both go off.
    ///
    /// Consuming every triggered mine BEFORE any blast lands is the second half
    /// of the same argument. A mine is a structure and ApplyAreaDamage damages
    /// structures, so mine A's blast would otherwise kill mine B outright -
    /// same asymmetry, decided by index. Marking them all dead first makes
    /// sympathetic detonation total: each triggered mine lands its own charge
    /// and none of them absorbs another's.
    ///
    /// An UNTRIGGERED mine caught in the blast simply dies, with no detonation.
    /// That is the deliberate rule and not an oversight: proximity is the only
    /// thing that sets a mine off, so being shot, sold or splashed destroys it
    /// the way it destroys any other building.
    /// </summary>
    private void MineSystem()
    {
        if (!_minesInPlay) return;

        List<int>? triggered = null;
        for (int i = 0; i < _entities.Count; i++)
        {
            var m = _entities[i];
            if (!m.Alive || m.Kind != EntityKind.Mine) continue;
            for (int j = 0; j < _entities.Count; j++)
            {
                var t = _entities[j];
                // P7-8g: the mine goes through the SAME target-selection gate as
                // the two combat scans, because it is a target-selection path and
                // being treated as something else is how it went wrong before.
                //
                // Enemy only, which is what makes a minefield a defence rather
                // than a hazard to its own side, and the gate's PlayerId >= 0
                // clause keeps the neutrals (ferrite, outposts, bridges) out with
                // the same test.
                //
                // AIRCRAFT DO NOT SET OFF GROUND MINES, which is what the false
                // says. ADR-028 clause 2 makes an aircraft not on the ground: it
                // ignores terrain, blocks no cell and takes no part in
                // separation. A buried charge it flies over is the same category
                // of thing, and a mine that downed a Strike Flyer would also be
                // the only anti-air answer in the game that is not the flak
                // track, which ADR-028 clause 4 makes deliberate. Passing that
                // flag is no longer optional: the gate demands it, so the fifth
                // path cannot repeat the omission the fourth one made in its
                // first draft.
                //
                // The gate is asked WITHOUT CanTarget, deliberately: a cloaked
                // scout still treads on a mine.
                if (!CanBeEngagedBy(m.PlayerId, antiAir: false, in t)) continue;
                if (t.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
                if (Fix64.DistSq(t.X - m.X, t.Y - m.Y) > MineTriggerRadiusSq) continue;
                (triggered ??= new List<int>()).Add(i);
                break;   // one trigger is enough; the blast is not per victim
            }
        }
        if (triggered is null) return;

        for (int k = 0; k < triggered.Count; k++)
        {
            int i = triggered[k];
            var m = _entities[i];
            m.Alive = false;
            _entities[i] = m;
            // The ordinary death event, so the client removes the actor exactly
            // as it does for any other building. FootprintOnDeath is not called
            // and must not be: a mine never blocked (see the note there).
            _events.Add(new GameEvent(GameEventType.Died, i, -1));
        }
        for (int k = 0; k < triggered.Count; k++)
        {
            var m = _entities[triggered[k]];
            ApplyAreaDamage(m.X, m.Y, MineDamage);
        }
    }

    /// <summary>
    /// Has this world ever held a gate? The gate on GateSystem's scan, and the
    /// reason all 24 goldens stay byte-identical: no golden scenario, save or
    /// replay places one, so the flag is never set and the system costs a single
    /// branch per tick. The mine's `_minesInPlay` is the precedent, down to the
    /// argument for each of its properties.
    ///
    /// Deliberately NOT hashed and NOT serialized: with no living gate the scan
    /// writes no entity, touches no map cell, clears no flow field and raises no
    /// event, so a world that resumes with this false is in the identical state
    /// to one that resumes with it true. The deserializer sets it from the
    /// entities it reads rather than relying on that equivalence.
    ///
    /// Monotone on purpose, the mine's reason exactly: a player who built one
    /// gate will build another, and a flag that flickered would be one more
    /// thing to keep true.
    /// </summary>
    private bool _gatesInPlay;

    /// <summary>
    /// P7-10: for each OPEN gate, the earliest tick it may close. An absent entry
    /// means the gate is shut, which is the state it is born in.
    ///
    /// A pruned side collection rather than an Entity field, for the reason
    /// `_lanes`, `_cargo` and `_disabledUntil` are: an absent entry contributes
    /// nothing to the FNV accumulator, so a world with no gate in it hashes
    /// byte-identically to one compiled before gates existed and all 24 goldens
    /// stand. What makes the guard SOUND rather than merely convenient is that
    /// the entry is removed on exactly the two events that end the openness - the
    /// gate closes, or the gate dies - so "no entry" provably means "no state
    /// that could gate behaviour".
    /// </summary>
    private readonly Dictionary<int, int> _gateOpenUntil = new();

    /// <summary>Is this entity an OPEN gate? The public read behind the
    /// collection, so the client can draw a raised gate without keeping its own
    /// copy of a rule the sim owns.</summary>
    public bool IsGateOpen(int entityId) => _gateOpenUntil.ContainsKey(entityId);

    /// <summary>
    /// P7-10: the gate opens for its own side and shuts again behind them.
    ///
    /// ONE GLOBAL STATE, and that is the whole design rather than a limitation
    /// worked around. ADR-005 clause 6 deferred gates because "a gate that is
    /// passable to its owner and solid to the enemy" needs either per-player flow
    /// fields or an incremental flow repair, and neither exists. That blocker is
    /// scoped to SIMULTANEOUS per-player passability and it is entirely right
    /// about it. A gate with a single open/closed state needs neither mechanism:
    /// an open gate is passable to EVERYBODY and a closed one is solid to
    /// everybody, which is exactly what the one global grid already expresses.
    ///
    /// SO AN ENEMY CAN WALK THROUGH AN OPEN GATE, and that is chosen rather than
    /// missed. It is the honest consequence of a global state and it is a real
    /// mechanic: you follow somebody in. Nothing here tries to prevent it, and
    /// the gate that proves this wave asserts it as a REQUIREMENT (stage 5) so
    /// that a later wave cannot quietly "fix" it into the per-player rule the
    /// ADR refused.
    ///
    /// Read the tick's SETTLED positions, after movement and separation, for
    /// MineSystem's reason: a proximity question answered mid-movement depends on
    /// where in the tick it was asked.
    ///
    /// THE HYSTERESIS IS THE EXPENSIVE PART OF THE DESIGN. Toggling calls
    /// BlockFootprint or UnblockFootprint, whose only invalidation is
    /// FlowFieldCache.Clear - every cached field on the map, thrown away. See
    /// GateHysteresisTicks for why a delay rather than a per-tick reading, and
    /// note the shape here: the deadline is REFRESHED on every tick an ally is
    /// near, so the close is 45 ticks after the last one left rather than 45
    /// ticks after the first one arrived.
    ///
    /// A gate does not close on somebody standing in the doorway, whoever they
    /// belong to. The rule is not politeness: a unit inside a blocked cell is
    /// unreachable by the flow field (Dijkstra never relaxes a blocked cell), so
    /// it would sit there stuck with no way to order it out. An enemy parked in
    /// the gateway therefore holds it open, which is the tailgating trade above
    /// stated once more in its most literal form.
    /// </summary>
    private void GateSystem()
    {
        if (!_gatesInPlay) return;

        for (int i = 0; i < _entities.Count; i++)
        {
            var g = _entities[i];
            if (!g.Alive || g.Kind != EntityKind.Gate) continue;
            int ax = AnchorOf(g.X, g.StructType), ay = AnchorOf(g.Y, g.StructType);
            int size = FootprintOf(g.StructType);

            bool allyNear = false, occupied = false;
            for (int j = 0; j < _entities.Count; j++)
            {
                var t = _entities[j];
                if (!t.Alive) continue;
                // Ground units and harvesters only, and the two exclusions are
                // the mine's own. A structure is not somebody arriving at a gate;
                // an AIRCRAFT is not on the ground at all (ADR-028 clause 2), so
                // it neither opens a gate nor keeps one from closing, exactly as
                // it neither treads on a mine nor blocks a cell.
                if (t.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
                if (IsAirborne(in t)) continue;
                int tcx = Map.CellOf(t.X), tcy = Map.CellOf(t.Y);
                if (tcx >= ax && tcx < ax + size && tcy >= ay && tcy < ay + size) occupied = true;
                // IsAlliedTo, not IsOwnedBy: a gate that shut in the face of the
                // ally you spent a lobby setting up would make an alliance
                // something you have to route around (P7-8c).
                if (!IsAlliedTo(in t, g.PlayerId)) continue;
                if (Fix64.DistSq(t.X - g.X, t.Y - g.Y) > GateOpenRadiusSq) continue;
                allyNear = true;
            }

            bool open = _gateOpenUntil.TryGetValue(g.Id, out int until);
            if (allyNear)
            {
                if (!open) UnblockFootprint(ax, ay, size);
                _gateOpenUntil[g.Id] = Tick + GateHysteresisTicks;
            }
            else if (open && Tick >= until && !occupied)
            {
                _gateOpenUntil.Remove(g.Id);
                BlockFootprint(ax, ay, size);
            }
        }
    }

    /// <summary>
    /// TICKET-P3-FAC-03: engineers (unit type 11) ordered onto an enemy
    /// structure pursue it and convert it on contact. The engineer is
    /// consumed by the act; the captured structure keeps its hit points but
    /// loses its production queue and ready slot (the crews flee with the
    /// blueprints). The signature 90s-RTS personality mechanic - and the seed of
    /// the Reclaimers' salvage identity when the third faction arrives.
    /// </summary>
    /// <summary>
    /// Is this a structure that a contact unit may act on? Named as its own
    /// predicate by P7-11b because TWO systems now need the same answer: this is
    /// the rule CaptureSystem has always applied inline, and CombatSystem asks
    /// it too, so that an ARMED contact unit walking in to demolish a building
    /// does not halt at weapon range and shoot it instead.
    ///
    /// ADR-005 clause 2: engineers do not capture fences.
    /// ADR-025: a bridge is excluded explicitly. It is neutral, so it would
    /// otherwise pass the only ownership test this guard has (!IsOwnedBy)
    /// exactly as a neutral outpost does, and an engineer walking into a river
    /// crossing to "capture" it is nonsense. You fell a bridge with an explicit
    /// Attack instead - and by the same token a hero ordered at a bridge shoots
    /// it, because this predicate says no and CombatSystem's skip does not fire.
    /// </summary>
    /// <remarks>P7-8g: the player test here was NOT-MINE rather than hostility,
    /// and the difference is the feature - a neutral outpost is nobody's enemy
    /// and capturing one is the outpost's whole point (ADR-021).
    ///
    /// P7-8c widened NOT-MINE to NOT-ALLIED, which is the smallest change that
    /// keeps both halves true: an engineer cannot capture a teammate's refinery
    /// and a Saboteur cannot switch one off, because an alliance you can rob is
    /// not an alliance; and a NEUTRAL outpost is still admitted, untouched,
    /// because a rock is allied to nobody. Note this is deliberately NOT
    /// IsEnemyOf: hostility would exclude the neutral and delete ADR-021.</remarks>
    private bool CanBeActedOn(in Entity actor, in Entity t)
        => t.Alive && IsStructure(t.Kind) && !IsBarrier(t.Kind)
           && t.Kind != EntityKind.Bridge && !IsAlliedTo(in t, actor.PlayerId);

    private void CaptureSystem()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            // P7-7 generalised this from `UnitType != 11`. FOUR unit types now
            // ACT ON CONTACT with an enemy structure - the engineer captures it,
            // the infiltrator robs it, (P7-11a) the saboteur switches it off and
            // (P7-11b) the hero demolishes it - and the walk, the reach test and
            // the target-validity rule are identical for all four. Adding
            // literal types here would have been the eighth enumeration this
            // phase; the shared shape is named once and the EFFECT branches at
            // the point where they actually differ, which is why P7-11a added
            // one branch below and not one line of pursuit or reach logic of its
            // own, and why P7-11b turned the boolean chain that stood here into
            // ContactEffectOf rather than adding a fourth and a fifth id to it.
            //
            // Note what is NO LONGER shared as of P7-11b: consumption. The first
            // three are consumed by the act and the hero is not, so the writes
            // that end a contact unit's life sit inside each effect rather than
            // after the branch. A consumed hero would be an expensive engineer.
            var effect = ContactEffectOf(e.UnitType);
            if (!e.Alive || effect == ContactEffect.None || e.ExplicitTarget < 0) continue;
            if (!ValidId(e.ExplicitTarget)) { e.ExplicitTarget = -1; _entities[i] = e; continue; }
            var t = _entities[e.ExplicitTarget];
            if (!CanBeActedOn(in e, in t)) { e.ExplicitTarget = -1; _entities[i] = e; continue; }
            Fix64 d = Fix64.DistSq(t.X - e.X, t.Y - e.Y);
            if (d <= Fix64.FromFraction(49, 16)) // within 1.75 cells of the footprint centre: through the door
            {
                int touched = e.ExplicitTarget;
                // A NEUTRAL target has no treasury to rob. CanBeActedOn admits
                // one deliberately - capturing a neutral Outpost is ADR-021's
                // whole feature - and the theft branch then indexed
                // _credits[-1], which is an index-out-of-range crash reachable
                // by ordering an Infiltrator onto an outpost. Latent since
                // P7-7, found while adding the fourth effect. Refused rather
                // than clamped: there is nothing to take, so taking nothing and
                // walking away is the honest behaviour, and the actor is not
                // consumed for a theft that did not happen.
                if (effect == ContactEffect.Theft && t.PlayerId < 0)
                {
                    e.ExplicitTarget = -1;
                    _entities[i] = e;
                    continue;
                }
                if (effect == ContactEffect.Theft)
                {
                    // P7-7: the theft. A SHARE of the victim's treasury rather
                    // than a flat sum, because the Sodality's written identity
                    // is economy denial and a percentage is what punishes the
                    // hoard - robbing a rich enemy is worth the walk, robbing a
                    // broke one is not. A fifth is my call and is recorded as
                    // one; the GDD names the unit and not the number.
                    long taken = _credits[t.PlayerId] / 5;
                    if (taken > 0)
                    {
                        _credits[t.PlayerId] -= taken;
                        _credits[e.PlayerId] += taken;
                    }
                    // The structure is UNHARMED and unchanged hands: this is a
                    // robbery, not a capture, and conflating them would give the
                    // Sodality a second engineer instead of a different tool.
                    e.Alive = false; e.Moving = false; e.ExplicitTarget = -1;
                    // P7-7a: Robbed, not Captured. This line said Captured from
                    // P7-7 until now, and the client reads Captured as an
                    // ownership change, so robbing a building told its owner
                    // "STRUCTURE LOST TO CAPTURE" about a building they still
                    // held, klaxon and all. The comment two paragraphs up
                    // asserted the robbery/capture distinction while this line
                    // was busy erasing it at the only point the player can see.
                    _events.Add(new GameEvent(GameEventType.Robbed, touched, e.PlayerId, C: (int)taken));
                }
                else if (effect == ContactEffect.Sabotage)
                {
                    // P7-11a: the sabotage. A tick STAMP for the building's
                    // return, and a second saboteur EXTENDS rather than
                    // shortens - the max, not an assignment, because a fresh
                    // charge planted on a building that is already off must
                    // never hand the defender time back.
                    int until = Tick + SabotageDurationTicks;
                    if (!_disabledUntil.TryGetValue(touched, out int standing) || until > standing)
                        _disabledUntil[touched] = until;
                    // The structure is UNHARMED and does not change hands, for
                    // the reason the theft leaves it standing: this is sabotage,
                    // and a saboteur that damaged or took the building would be
                    // a demolition charge or a second engineer rather than the
                    // tempo tool GDD s7 names.
                    e.Alive = false; e.Moving = false; e.ExplicitTarget = -1;
                    _events.Add(new GameEvent(GameEventType.Sabotaged, touched, e.PlayerId, C: until));
                }
                else if (effect == ContactEffect.Demolition)
                {
                    // P7-11b: the demolition. Large FIXED DAMAGE through the
                    // ordinary path rather than deletion, which is the whole
                    // design of the ability: armour class rules on it, the
                    // repair vehicle can answer it, a death leaves rubble and
                    // raises Died like any other death, and a tough building
                    // survives one visit. A hero that removed a building
                    // outright would make hit points meaningless for the one
                    // unit they matter most against.
                    //
                    // The shape is ApplyAreaDamage's, aimed at one entity: the
                    // matrix, then the hit points, then the death rules. No kill
                    // credit is awarded, matching the superweapon - and matching
                    // this unit's own veterancy_enabled false, so there is
                    // nothing to award it to.
                    t.Hp -= DamageMatrix.Apply(DemolitionDamage, Warhead.AntiBuilding, t.Armour);
                    if (t.Hp <= 0)
                    {
                        _events.Add(new GameEvent(GameEventType.Died, touched, -1));
                        t.Alive = false; t.Moving = false; t.HState = HarvestState.Idle;
                        FootprintOnDeath(in t);   // ADR-025: a bridge BLOCKS instead, and cannot be demolished anyway
                    }
                    _entities[touched] = t;
                    // The hero LIVES. It is the one contact unit the act does not
                    // consume, and that is what makes it a piece a player keeps
                    // rather than an expensive engineer. Clearing the target is
                    // the pacing limit in place of a cooldown: demolishing again
                    // means being ordered again, so a hero parked in a base does
                    // not chew through it unattended. It halts where it planted
                    // the charge rather than walking on into the footprint it
                    // was pursuing, which is now rubble.
                    e.Moving = false; e.ExplicitTarget = -1;
                }
                else
                {
                t.PlayerId = e.PlayerId;
                t.Repairing = false;
                t.ReadyStructure = 0;
                _queues.Remove(touched);
                _orderQueues.Remove(touched);
                _entities[touched] = t;
                e.Alive = false; e.Moving = false; e.ExplicitTarget = -1; // the act consumes the engineer
                _events.Add(new GameEvent(GameEventType.Captured, touched, e.PlayerId));
                }
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
            // P7-8g: hostility, and hostility ALONE. This pick hands its answer
            // to CombatSystem's explicit-target branch, which is where the air
            // rule then lands on it, so it does not go through CanBeEngagedBy.
            if (!t.Alive || !IsBarrier(t.Kind) || !IsEnemyOf(in t, e.PlayerId)) continue;
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
            // P7-8g: hostility, NOT engagement. This is the "is the ordered
            // ground clear" question, so it must count an enemy this unit cannot
            // personally shoot; sending it through CanBeEngagedBy would let an
            // attack-move report success with the enemy still standing there.
            if (!t.Alive || !IsEnemyOf(in t, e.PlayerId) || t.Kind == EntityKind.FerriteField || IsBarrier(t.Kind)) continue;
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
            // (ADR-008 clause 1).
            //
            // P7-2 generalised this from `Kind == Turret` to ANY ARMED
            // STRUCTURE. The old form named one kind, and the comment here
            // promised that "doc 22's emplacement and bastion join by kind when
            // they land" - an enumeration somebody has to remember. The
            // Emplacement proved nobody would: it shipped firing straight
            // through a brown-out until a gate caught it. What the rule MEANS
            // is that a building's gun needs power, so it now keys on the
            // building having a gun. Behaviour-identical on the day it changed,
            // because the turret was the only armed structure in the
            // catalogue - which is why the goldens do not move.
            //
            // The continue sits ABOVE the cooldown decrement deliberately: a
            // dead turret does not reload. Inclusive boundary via divisionless
            // AtLeast75: supply 15 against draw 20 FIRES.
            //
            // P7-11a joins the same gate rather than adding a second one: a
            // sabotaged gun is silent for the same reason an unpowered one is,
            // and the continue sits above the cooldown decrement for the same
            // reason too, so a switched-off turret does not reload either.
            if (IsStructure(e.Kind) && e.WeaponId != 0
                && (IsDisabled(e.Id)
                    || !AtLeast75(combatSupply[e.PlayerId], combatDraw[e.PlayerId])))
            { _entities[i] = e; continue; }
            if (e.Cooldown > 0) { e.Cooldown--; _entities[i] = e; continue; }

            // P7-11b: a unit walking in to ACT ON a building does not stop to
            // shoot it. The hero is the first ARMED contact unit in the game and
            // the collision is total without this: CaptureSystem sets the pursuit
            // each tick and the explicit-target branch below answers with "in
            // range: hold and fire", so a hero ordered onto a power plant would
            // halt four cells short and plink at it with an anti-infantry rifle
            // forever, never reaching the 1.75 cells its ability needs.
            //
            // Provably inert for everything that existed before: the three older
            // contact units carry weapon 0 and never reach this line, and this
            // clause asks CaptureSystem's OWN target predicate rather than a
            // second copy of it, so a hero ordered at a unit, a wall, a bridge or
            // its own side's building shoots exactly as any other unit would.
            if (ContactEffectOf(e.UnitType) != ContactEffect.None && e.ExplicitTarget >= 0
                && ValidId(e.ExplicitTarget) && CanBeActedOn(in e, _entities[e.ExplicitTarget]))
            { _entities[i] = e; continue; }

            // The world's table, never Weapons.Get: this is the line that makes
            // data/weapons drive the game rather than describe it.
            var w = GetWeaponType(e.WeaponId);
            int target = -1, sightTarget = -1;

            // A dead, invalid, or no-longer-targetable (stealthed) explicit
            // target is cleared so the unit falls back to auto-acquire next
            // tick instead of standing idle or chasing a ghost.
            if (e.ExplicitTarget >= 0 && (!ValidId(e.ExplicitTarget) || !_entities[e.ExplicitTarget].Alive
                || !CanTarget(e.PlayerId, _entities[e.ExplicitTarget])
                // ADR-028: a ground weapon ordered at an aircraft drops the
                // order rather than executing it. Without this an explicit
                // Attack would shoot a plane down with a rifle and the whole
                // point of the layer would be a scan-only rule.
                || !WeaponCanEngage(in w, _entities[e.ExplicitTarget])
                // P7-5: and a ferrite field is not a target. Found while
                // reading GDD s8 for DR-04, which gives DESTROYING A FIELD to
                // one superweapon on one side as that faction's economic-warfare
                // identity - so anything else being able to do it is a defect,
                // and this one was total. Every other system already excludes
                // fields by hand (auto-acquire, splash, area damage, the guard
                // leash, EnemyNearAMovePoint); this branch was the only way in,
                // and a field has Hp 1, so ONE rifle shot deleted an entire
                // field and its whole remaining ferrite, permanently, because
                // regrowth skips dead fields. Unreachable from the sidebar, and
                // that is not a defence: it is reachable from a LAN peer's
                // command stream, which is the seat this project has been
                // caught by three times.
                || _entities[e.ExplicitTarget].Kind == EntityKind.FerriteField))
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
            else if (e.Stance != Stance.HoldFire)
            {
                // ADR-015 hold-fire, the ONE combat change: a HoldFire unit skips
                // this auto-acquire scan entirely, so target stays -1 and it does
                // not fire even with an enemy in range. The explicit-target branch
                // above is untouched, so an ordered Attack still fires (Q003:
                // "defends only if explicitly ordered to attack"). This is also
                // the load-bearing neutralisation point: for the default
                // Aggressive stance the condition is always true, so seed-2026
                // behaviour is byte-identical to before this ADR.
                //
                // Auto-acquire nearest enemy in range; ties break to lower id.
                // Attack-movers additionally track the nearest enemy within
                // SIGHT so they can hunt flankers instead of marching past.
                Fix64 bestD = Fix64.MaxValue, bestSightD = Fix64.MaxValue;
                for (int j = 0; j < _entities.Count; j++)
                {
                    var t = _entities[j];
                    // P7-8g: the shared target-selection gate carries both the
                    // hostility question and ADR-028's air rule, which this scan
                    // used to spell out for itself. A first pass at the air layer
                    // patched two paths and left this one, and the gate caught it
                    // immediately by shooting a plane down with a rifle.
                    if (!CanBeEngagedBy(e.PlayerId, w.AntiAir, in t)) continue;
                    // Barriers are skipped exactly as ferrite fields are (ADR-005
                    // clause 2): without this your tanks stop to plink at a wall
                    // instead of the turret behind it, and this O(n) inner scan
                    // grows by 160 entities for every armed unit every tick. An
                    // explicit Attack order still targets a wall; only
                    // auto-acquisition declines to.
                    if (t.Kind == EntityKind.FerriteField || IsBarrier(t.Kind)) continue;
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
                if (IsStructure(t.Kind)) FootprintOnDeath(in t);   // ADR-025: a bridge BLOCKS instead
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

    /// <summary>
    /// ADR-012: ferrite fields regrow. Each FerriteField regrows _regrowAmount
    /// every _regrowIntervalTicks, up to its spawn amount (FerriteCap),
    /// deterministically in entity-index order (a list walk, never a dictionary).
    /// Ordered explicitly right after HarvestSystem: depletion then replenish,
    /// the economy pair. Two rules make it a strategy, not a faucet:
    ///   1. The OWN-AMOUNT rule is load-bearing: a field regrows only while its
    ///      remaining amount is ABOVE ZERO. A field harvested to zero is set
    ///      not-alive at depletion (HarvestSystem) and this loop skips it, so
    ///      dead ground stays dead forever and strip-to-deny survives. The
    ///      >0 guard also covers a field spawned empty. This is the ratified
    ///      rule; the ADR's rejected "regrow regardless of remaining" and
    ///      "spread to neighbours" alternatives are NOT implemented.
    ///   2. The schedule is DERIVED FROM THE TICK, not a stored counter: it
    ///      fires when the tick is a positive multiple of the interval. Tick is
    ///      saved, so regrowth resumes sanely across save/load with no new
    ///      hashed or serialized counter. RegrowthSystem runs before Tick++, so
    ///      the pre-increment tick is the one tested; the first regrow lands at
    ///      tick 75, a full interval after tick 0.
    /// FerriteAmount is hashed, so a regrow is a hashed mutation and MOVES the
    /// goldens of scenarios whose fields have been harvested below cap and run
    /// long enough to reach an interval; a world with no ferrite field, or one
    /// whose fields sit at cap, sees this loop change nothing (ADR-012's
    /// required asymmetry).
    /// </summary>
    private void RegrowthSystem()
    {
        if (_regrowAmount <= 0 || _regrowIntervalTicks <= 0) return;
        if (Tick <= 0 || Tick % _regrowIntervalTicks != 0) return;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.Kind != EntityKind.FerriteField) continue;
            if (e.FerriteAmount <= 0 || e.FerriteAmount >= e.FerriteCap) continue; // own-amount rule and the cap
            int grown = e.FerriteAmount + _regrowAmount;
            e.FerriteAmount = grown < e.FerriteCap ? grown : e.FerriteCap;
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
            // ADR-028: aircraft do not shove and are not shoved. They are not on
            // the ground, so nothing there is in their way and they are in
            // nothing's way either.
            if (IsAirborne(e)) continue;
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
            // ADR-027 / Q018: the inverse of pressedOnStationary. That flag
            // notices a MOVER jammed against a stalled unit; this one notices a
            // STALLED unit being pressed by a mover, which is the other half of
            // the same event and the half nothing acted on.
            bool pressedByMover = false;
            Fix64 yieldX = Fix64.Zero, yieldY = Fix64.Zero;

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
                            && IsEnemyOf(in o, e.PlayerId))   // P7-8g: hostility, not ownership
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
                        // ADR-027: somebody is trying to get past this unit.
                        // dx/dy already point AWAY from the neighbour, so the
                        // accumulated vector is the direction out of the way.
                        // P7-8g: ownership. Today you only step aside for your
                        // OWN units; whether an ally earns the courtesy is a
                        // P7-8c call and it belongs here, not in the hostility
                        // predicate.
                        if (!e.Moving && o.Moving && IsOwnedBy(in o, e.PlayerId))
                        {
                            pressedByMover = true;
                            yieldX += dx * overlap / d;
                            yieldY += dy * overlap / d;
                        }
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

            // ADR-027, THE YIELD. Q018 traced the jam to a cascade: the first
            // unit to give up becomes a STATIONARY obstacle, and pressing
            // against a stationary unit is exactly what feeds the next unit's
            // stall counter, so a production cluster freezes solid from the
            // inside out. The backstops are not the fault - they cannot make a
            // route through a crowd exist, which is what Option B proved when
            // removing them traded a freeze for a never-settles.
            //
            // What was missing is that a unit standing in the way had no reason
            // to move. Now it does: pressed by a friendly mover, a stalled unit
            // steps aside two cells along the direction out of the way. That
            // breaks the cascade at its source rather than disabling the
            // machinery that detects it.
            //
            // UseFlow FALSE on purpose. The nudge is a short straight step that
            // ends itself on arrival, and it deliberately does not touch
            // StallTicks or the ADR-014 counters: a yield is not a new order,
            // and re-arming the backstops here would recreate the trap
            // ApplyCommandCore already falls into.
            if (pressedByMover && !e.Moving && e.Kind == EntityKind.Unit
                && e.Speed != Fix64.Zero && e.Stance != Stance.HoldFire)
            {
                Fix64 len = Fix64.Sqrt(Fix64.DistSq(yieldX, yieldY));
                if (len > Fix64.Zero)
                {
                    e.TargetX = e.X + yieldX * Fix64.FromInt(2) / len;
                    e.TargetY = e.Y + yieldY * Fix64.FromInt(2) / len;
                    e.Moving = true; e.UseFlow = false; e.AMove = false;
                }
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

                // Q013 / ADR-014: the monotone no-progress backstop. The leaky
                // StallTicks net above rides out rim churn, but a unit whose
                // blocked and progressing ticks balance evenly - a persistent
                // two- or three-tick orbit at a crowd rim - holds StallTicks
                // below its 4s threshold forever and never settles. That is the
                // seed-900913 soak failure: two units orbit ~20 cells short of
                // the target while 498 pack cleanly. This watchdog tracks the
                // NEAREST approach to the destination, which a genuine orbit can
                // never better once locked, and benches the unit after
                // NoProgressDeadline ticks without a new best - catching any
                // period of orbit regardless of its per-tick churn, where the
                // leaky net's balance point hides it. Runs only if StallTicks
                // did not already settle the unit this tick.
                if (e.Moving)
                {
                    Fix64 curSq = Fix64.DistSq(e.TargetX - e.X, e.TargetY - e.Y);
                    if (e.NearestApproachSq.Raw == 0 || curSq < e.NearestApproachSq)
                    {
                        e.NearestApproachSq = curSq;
                        e.NoProgressTicks = 0;
                    }
                    else if (++e.NoProgressTicks >= NoProgressDeadline)
                    {
                        // Same near-destination rule as the StallTicks path: drop
                        // the attack-move stance only if this IS the objective.
                        if (e.AMove && Fix64.DistSq(e.TargetX - e.X, e.TargetY - e.Y) <= Fix64.FromInt(16)) e.AMove = false;
                        e.Moving = false;
                        e.NoProgressTicks = 0;
                        e.NearestApproachSq = Fix64.Zero;
                    }
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
                else if (_credits[e.PlayerId] >= RepairCreditsPerTick)
                {
                    _credits[e.PlayerId] -= RepairCreditsPerTick;
                    e.Hp = Math.Min(e.MaxHp, e.Hp + RepairHpPerTick);
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
                    // P7-8g: ownership - a depot mends its OWN, and it is the
                    // owner's treasury that pays per tick just below.
                    if (!v.Alive || !IsOwnedBy(in v, e.PlayerId) || v.Hp >= v.MaxHp) continue;
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

            // Repair vehicle (ADR-019, P6 Wave C2): the mobile field-repair unit
            // runs the depot heal loop as a moving aura, with three deliberate
            // departures from the depot (ADR-019). It is NOT power-gated (a field
            // repairer must work away from base power); it excludes ITSELF (u == i;
            // it mends others, not itself); and, like the depot, it mends only
            // mobile own units (Unit or Harvester), never structures. Same rate and
            // price as the depot: radius 4, 2 hp/tick, 1 credit per unit per tick,
            // halting when broke. This branch fires only for unit type 13, which no
            // golden scenario spawns, so the goldens are untouched (ADR-019).
            if (e.Kind == EntityKind.Unit && e.UnitType == RepairVehicleType)
            {
                for (int u = 0; u < _entities.Count; u++)
                {
                    if (u == i) continue;              // repairs others, not itself
                    var v = _entities[u];
                    if (!v.Alive || !IsOwnedBy(in v, e.PlayerId) || v.Hp >= v.MaxHp) continue;   // P7-8g: ownership, as the depot
                    if (v.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
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

            // Neutral Outpost (ADR-021, P6 Wave C4): a CAPTURED outpost pays its
            // owner a once-per-second trickle, GDD line 41's secondary income.
            // Guarded on PlayerId >= 0 twice over: a neutral outpost pays nobody,
            // and _credits[-1] must never be indexed. The schedule is derived
            // from the tick (positive multiples only, the regrowth idiom), never
            // a stored counter, so a loaded save resumes on the same beat. This
            // branch fires only for Kind == Outpost, which no golden scenario
            // spawns, so the goldens are untouched (ADR-021).
            if (e.Kind == EntityKind.Outpost)
            {
                if (e.PlayerId >= 0 && Tick > 0 && Tick % TicksPerSecond == 0)
                    _credits[e.PlayerId] += OutpostIncomePerSecond;
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
                    // P7-5c: THE ONE PLACE the two superweapons differ. Charge,
                    // the ready event, the launch command and the five-second
                    // warning are all shared, because GDD s8 gives both sides
                    // "one superweapon per faction" on the same terms; what
                    // arrives is the decision.
                    // P7-5e: asked of the DEF, where P7-5c named the type id.
                    // Same branch, and now the authored key decides it - which
                    // is what lets the AI ask the same question when it aims.
                    if (GetStructureType(e.StructType).DestroysFields) ApplySeismicCharge(e.StrikeX, e.StrikeY);
                    else ApplyAreaDamage(e.StrikeX, e.StrikeY, 900);
                    e = _entities[i]; // the strike may have killed the launcher itself
                }
                _entities[i] = e;
                continue;
            }

            // ADR-009 clause 1, ProductionSystem site: miss this and the
            // barracks queue never advances, with no error to say so.
            if (!IsProducer(e.Kind)) continue;
            // P7-11a: a sabotaged producer stops the line. Placed ABOVE the
            // queue read and below the producer test, so the lane HOLDS exactly
            // as it holds when the treasury is empty: the queue is untouched,
            // the head is not popped, and BuildProgress and BuildPaid keep their
            // values, so nothing is lost and nothing is charged twice when the
            // building comes back. Zeroing progress here would be a second, invented
            // punishment on top of the stopped clock.
            if (IsDisabled(e.Id)) continue;
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
                // P7-11b: the SECOND enforcement point, and the one that makes
                // the cap true rather than merely advertised. A player who
                // queued two heroes while owning none passed the Produce check
                // twice, so the completion is where the second one is stopped.
                //
                // It HOLDS rather than cancels, reusing the blocked-spawn-cell
                // case immediately below: full progress, fully paid, queue head
                // not popped, so owed is exactly zero every held tick, the
                // credits are not lost, and the unit walks out the moment the
                // standing one dies. Cancelling here would charge a player 1500
                // credits for a unit they never received, and dropping the head
                // silently would be SPAWN-D2 all over again.
                if (AtMaxAlive(p, queuedType)) { _entities[i] = e; continue; }
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

        AdvanceBuildLanes(supply, draw);
    }

    /// <summary>
    /// ADR-023: advance every yard's SECOND build lane. A separate pass rather
    /// than a branch inside the main producer loop, deliberately: that loop
    /// exits through half a dozen `continue`s, and threading a second head
    /// through them would risk perturbing lane-1 behaviour, which is the one
    /// thing that must stay byte-identical. This pass touches nothing unless a
    /// lane entry exists, and no golden scenario ever creates one.
    ///
    /// Mirrors the lane-1 Construction Yard path exactly: same rate scaling,
    /// same pay-as-you-build slice, same ready-slot handoff, same event. It is
    /// simpler only because a yard's product is PLACED rather than spawned, so
    /// there is no spawn-offset search and no held-at-100-per-cent case.
    /// </summary>
    private void AdvanceBuildLanes(ReadOnlySpan<int> supply, ReadOnlySpan<int> draw)
    {
        if (_lanes.Count == 0) return;   // the common case, and every golden
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.Kind != EntityKind.ConstructionYard) continue;
            // P7-11a: a sabotaged yard stops BOTH its lines. Guarded here as
            // well as in the main loop because this pass is deliberately
            // separate from it; a rule applied to one lane and not the other
            // would let a switched-off yard keep building out of its second
            // head, which is the enumeration trap in a different coat. The lane
            // holds untouched, exactly as it holds when the player is broke.
            if (IsDisabled(i)) continue;
            if (LaneOf(i) is not { } lane) continue;
            if (lane.Ready != 0) continue;               // placement pending pauses THIS lane only
            if (lane.Queue.Count == 0) { PruneLane(i); continue; }

            int p = e.PlayerId;
            int rate = draw[p] <= 0 || supply[p] >= draw[p]
                ? 100
                : 50 + 50 * supply[p] / draw[p];

            int queuedType = lane.Queue[0];
            var sd = GetStructureType(queuedType);
            int total = sd.BuildTicks * 100;
            if (total <= 0) { lane.Queue.RemoveAt(0); PruneLane(i); continue; }
            int tentative = Math.Min(lane.Progress + rate, total);
            int owed = (int)((long)sd.Cost * tentative / total) - lane.Paid;
            if (_credits[p] < owed) continue;            // broke: the lane holds, exactly as lane 1 does
            _credits[p] -= owed;
            lane.Paid += owed;
            lane.Progress = tentative;
            if (lane.Progress >= total)
            {
                lane.Progress = 0;
                lane.Paid = 0;
                _events.Add(new GameEvent(GameEventType.ProductionComplete, i, queuedType, C: i));
                lane.Ready = queuedType;
                lane.Queue.RemoveAt(0);
            }
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
    ///
    /// P7-8c: NOTHING CHANGED HERE, and the absence is deliberate. This scan
    /// asks no ownership question at all - it already hits friend and foe alike,
    /// including the detonator's OWN units - so an ally is treated exactly as
    /// your own squad already is. An ally standing in your howitzer's splash
    /// takes it. Sparing allies would mean sparing yourself too, or else
    /// inventing a rule that treats a teammate better than your own men, and
    /// neither is what "alliance" was asked to mean.
    /// </summary>
    /// <summary>
    /// P7-5c (DR-04): the Sodality seismic charge, written by GDD s8 as "wide,
    /// lower-damage area denial that also destroys resource fields - economic
    /// warfare flavour".
    ///
    /// A SEPARATE function rather than parameters on ApplyAreaDamage, and that
    /// is the load-bearing decision here. ApplyAreaDamage is shared with the
    /// MINE detonation (MineDamage), and its 1.5/3-cell shape is asserted by
    /// minegate and by the artillery and superweapon scenarios. Widening it to
    /// take a radius would have made every mine in the game a candidate for
    /// changing shape by accident, on a function whose comment already records
    /// that its constants are load-bearing in three places.
    ///
    /// The three numbers are INVENTED, because GDD s8 gives adjectives and not
    /// values, and ADR-044 records the alternatives beside each:
    ///   damage 350 against the cannon's 900 - "lower-damage". Enough to clear
    ///     infantry, harvesters and a power plant; NOT enough to kill a factory
    ///     (1500) or a Construction Yard (3000). That boundary is the design:
    ///     the orbital cannon ends a base, the seismic charge denies ground.
    ///   inner 3 cells, outer 6 - "wide", exactly double the cannon's 1.5/3, so
    ///     it covers four times the area for under half the damage.
    ///   fields DIE rather than draining, because "destroys" is the written
    ///     word and a half-emptied field is a slower version of harvesting it.
    ///
    /// Like ApplyAreaDamage it asks NO ownership question: it hits the firing
    /// player's own units and its allies, and it destroys whoever's fields lie
    /// under it including the launcher's own. That is deliberate and it is the
    /// ADR-038 splash rule applied unchanged - a weapon that spared its owner's
    /// ground would make area denial free.
    /// </summary>
    private void ApplySeismicCharge(Fix64 x, Fix64 y)
    {
        Fix64 innerSq = Fix64.FromInt(9);    // 3^2
        Fix64 outerSq = Fix64.FromInt(36);   // 6^2
        for (int i = 0; i < _entities.Count; i++)
        {
            var t = _entities[i];
            if (!t.Alive) continue;
            Fix64 d = Fix64.DistSq(t.X - x, t.Y - y);
            if (d > outerSq) continue;
            if (t.Kind == EntityKind.FerriteField)
            {
                // The economic-warfare half, and the reason this weapon exists.
                // The ground is denied outright: a dead field does not regrow,
                // because RegrowthSystem skips fields at zero amount.
                t.FerriteAmount = 0;
                t.Alive = false;
                _events.Add(new GameEvent(GameEventType.Died, i, -1));
                _entities[i] = t;
                continue;
            }
            int dmg = DamageMatrix.Apply(SeismicDamage, Warhead.Omni, t.Armour);
            if (d > innerSq) dmg /= 2;
            t.Hp -= dmg;
            if (t.Hp <= 0)
            {
                _events.Add(new GameEvent(GameEventType.Died, i, -1));
                t.Alive = false; t.Moving = false; t.HState = HarvestState.Idle;
                if (IsStructure(t.Kind)) FootprintOnDeath(in t);   // ADR-025: a bridge BLOCKS instead
            }
            _entities[i] = t;
        }
    }

    /// <summary>P7-5c: the seismic charge's base damage, named rather than
    /// buried, so the one number a balance pass would reach for is findable.</summary>
    public const int SeismicDamage = 350;

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
                if (IsStructure(t.Kind)) FootprintOnDeath(in t);   // ADR-025: a bridge BLOCKS instead
            }
            _entities[i] = t;
        }
    }

    /// <summary>
    /// P7-8c: ALLIES DO NOT SHARE VISION, and the omission is a decision rather
    /// than an oversight. Every sighting below is written into the OWNER's
    /// bitset alone, which is what it always did, and no allied fan-out was
    /// added. Shared sight is a separate design lever from a shared war: it is
    /// the single largest thing an alliance could grant, it would let a 4v4 see
    /// four times the map for one seat's scouting, and it would make the veil
    /// projector and every stealth unit answer to a team's worth of detectors.
    /// If a lobby wants it later it is a per-team OR of these bitsets and a
    /// deliberate ADR, not a quiet widening here.
    /// </summary>
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

    /// <summary>ADR-029: the ONE definition of "this player is still in the
    /// game". ADR-005 clause 2: a barrier is not hope, or a player whose last
    /// possession is one 100-credit wall is never eliminated and the match
    /// never ends. ADR-021 adds the Outpost to the same exclusion: a captured
    /// income node is not a base.</summary>
    private static bool IsHope(in Entity e)
        // P7-11c joins the Mine to the same exclusion on the barrier's exact
        // argument: a player whose last possession is one buried 400-credit
        // charge nobody can even see is not still in the game, and counting it
        // would mean the match never ends.
        => (IsStructure(e.Kind) && !IsBarrier(e.Kind) && e.Kind != EntityKind.Outpost
            && e.Kind != EntityKind.Mine)
           || e.UnitType == McvUnitType;

    /// <summary>Does this player still hold anything that counts as being in
    /// the game? The short-game rule below and the mission trigger condition
    /// 'eliminated P' both ask THIS, so a campaign defeat cannot drift from
    /// the skirmish one. Independent of ShortGameEnabled, which decides
    /// whether the sim ACTS on the answer, not what the answer is.</summary>
    public bool HasHope(int player)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            // P7-8g: ownership. Hope is what YOU still hold. Whether a team is
            // eliminated only when every member is is a P7-8c question, and the
            // answer would be given by the caller, not by widening this scan.
            if (!e.Alive || !IsOwnedBy(in e, player)) continue;
            if (IsHope(e)) return true;
        }
        return false;
    }

    private void VictorySystem()
    {
        if (!ShortGameEnabled || Winner >= 0 || _players < 2) return;
        Span<bool> hasHope = stackalloc bool[_players];
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Alive || e.PlayerId < 0) continue;
            if (IsHope(e)) hasHope[e.PlayerId] = true;
        }
        // P7-8c: the match ends when one TEAM is left, not one player.
        //
        // ELIMINATION IS STILL PER PLAYER and the announcement below is
        // untouched: a commander with no structures and no MCV is out and is
        // told so, whether or not a teammate is still fighting. Being carried is
        // not the same as being in the game, and the client's defeat banner and
        // the campaign's 'eliminated P' trigger both rest on that reading.
        // What teams change is only the END condition.
        //
        // With the default team map each player is their own team, so
        // livingTeams equals the living-player count this counted before and the
        // whole block is behaviour-identical.
        Span<bool> teamStands = stackalloc bool[_players];   // team ids are seat ids, so this span fits by construction
        int livingTeams = 0, last = -1;
        for (int p = 0; p < _players; p++)
        {
            if (hasHope[p])
            {
                last = p;
                int team = _playerTeam[p];
                if (!teamStands[team]) { teamStands[team] = true; livingTeams++; }
            }
            else if (!_eliminatedAnnounced[p])
            {
                _eliminatedAnnounced[p] = true;
                _events.Add(new GameEvent(GameEventType.PlayerEliminated, -1, p));
            }
        }
        // Winner stays a PLAYER id, and it is the last standing seat of the
        // winning team - which at default teams is the sole survivor this always
        // named, so nothing moves. Ask TeamOf(Winner) for the winning side.
        if (livingTeams == 1) Winner = last;
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
            // Q013 / ADR-014: the no-progress backstop state. Unlike FerriteCap
            // below these are MUTABLE and gate when a unit stops moving, so they
            // are hashed (and serialized, save v6) following the rally precedent
            // for mutable movement state. Appended after the rally tail.
            h.Add(e.NearestApproachSq); h.Add(e.NoProgressTicks);
            // ADR-015: the unit command stance and its post/patrol geometry,
            // appended after the no-progress tail in declaration order (the save
            // order too). All six are MUTABLE state that gates what a unit does -
            // a stance changes its target acquisition and its movement - so they
            // are hashed following the rally precedent, unlike the immutable
            // FerriteCap below. Aggressive (0) with every position field zero is
            // the fresh-spawn default, so this append moves every golden purely
            // mechanically and changes no seed-2026 behaviour (the neutralisation
            // proof in ADR-015's consequences).
            h.Add((int)e.Stance);
            h.Add(e.PostX); h.Add(e.PostY);
            h.Add(e.PatrolX); h.Add(e.PatrolY);
            h.Add(e.PatrolOutbound);
            // ADR-012: FerriteCap is deliberately NOT hashed here. Like Sight
            // it is immutable spawn-time state, identical on every client and
            // reconstructed exactly on load (save v5), so it needs no desync
            // coverage; hashing it would move every golden, including the
            // no-ferrite scenarios the ADR requires stay still. FerriteAmount,
            // which regrowth mutates, is already hashed above.
            // ADR-009 clause 1, the fourth IsProducer site and PROD-D5's
            // close: the queue hash covered FACTORY queues only, so a
            // Construction Yard divergence between two same-cost, same-ticks
            // structure types (factory and refinery are both 2000 credits and
            // 300 ticks) was bit-identical every tick until ReadyStructure was
            // written, and any new producer kind inherited the hole. Every
            // producer's queue is hashed now.
            if (IsProducer(e.Kind) && _queues.TryGetValue(e.Id, out var q))
            { h.Add(q.Count); foreach (int t in q) h.Add(t); }
            // ADR-023: the second build lane, folded ONLY when one exists. The
            // _orderQueues block below is the precedent for a guarded fold, and
            // it is shipped and golden-covered; an unexecuted fold contributes
            // literally nothing to an FNV accumulator. What makes the guard
            // SOUND rather than merely convenient is that a lane entry is
            // pruned the instant it goes inert, so "no entry" provably means
            // "no state that could gate behaviour" (BuildLane.Inert).
            //
            // Folded here, inside the entity loop, rather than as a second
            // top-level block: two adjacent variable-length untagged folds can
            // in principle present the same int sequence, and scoping this one
            // to its entity removes that ambiguity entirely.
            if (_lanes.TryGetValue(e.Id, out var lane))
            {
                h.Add(lane.Progress); h.Add(lane.Paid); h.Add(lane.Ready);
                h.Add(lane.Queue.Count);
                foreach (int t in lane.Queue) h.Add(t);
            }
            // P7-3: a transport's hold, folded on exactly the lane's terms and
            // for exactly its reason. The entry is pruned the instant the hold
            // empties, so no entry provably means no carried state, and a world
            // with no transport in it hashes byte-identically to one compiled
            // before transports existed.
            if (_cargo.TryGetValue(e.Id, out var hold))
            {
                h.Add(hold.Count);
                foreach (var cu in hold) { h.Add(cu.UnitType); h.Add(cu.Hp); h.Add(cu.Rank); }
            }
            // P7-11a: a sabotaged building's return tick, folded on exactly the
            // hold's terms and for exactly its reason. It gates the power tally,
            // the structure guns and the production lines, so it is state a
            // desync could hide in and must be hashed; the entry is pruned the
            // tick it lapses, so no entry provably means no disable, and a world
            // with no saboteur in it hashes byte-identically to one compiled
            // before saboteurs existed. Guarded, never unconditional: a bare
            // h.Add here would move all 24 goldens for a feature no golden
            // scenario uses.
            if (_disabledUntil.TryGetValue(e.Id, out int until)) h.Add(until);
            // P7-10: an OPEN gate's close deadline, folded on exactly the terms
            // above and for exactly their reason. It decides when a cell of the
            // passability grid flips, which decides where every unit on the map
            // walks, so it is state a desync could hide in and must be hashed;
            // the entry is removed the tick the gate shuts or dies, so no entry
            // provably means no openness, and a world with no gate in it hashes
            // byte-identically to one compiled before gates existed. Guarded,
            // never unconditional: a bare h.Add here would move all 24 goldens
            // for a feature no golden scenario uses.
            if (_gateOpenUntil.TryGetValue(e.Id, out int openUntil)) h.Add(openUntil);
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
            // P7-8c: this player's team, folded ONLY when they have been put on
            // somebody else's. Teams gate target acquisition, the guard leash,
            // the mine trigger, the contact effects, the detector sweep and the
            // end of the match, so they are state a desync could hide in and
            // MUST be hashed; a bare h.Add here would move all 24 goldens for a
            // feature no golden scenario uses. The guarded fold is the ADR-023
            // lane pattern and the P7-3 cargo pattern, and it is sound for the
            // same reason those are: an absent entry provably means no state
            // that could gate behaviour, because a player on their own team is
            // exactly the free-for-all every expression above reduces to.
            //
            // The whole MAP is recovered from the entries that are present, not
            // just this player's: if q is on p's team then q's own entry folds,
            // so the alliance is hashed once from q's side. Contributing nothing
            // therefore means the map is the identity, which is the only case
            // that has to stay byte-identical.
            //
            // Tagged with p, like the lane fold is scoped to its entity, so a
            // variable-length fold inside a fixed-length per-player record
            // cannot present the same int sequence as a different team map.
            if (_playerTeam[p] != p) { h.Add(p); h.Add(_playerTeam[p]); }
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
