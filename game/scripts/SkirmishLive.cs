using Godot;
using Ferrostorm.Presentation;
using Ferrostorm.Sim;
using System.Collections.Generic;
using System.IO;

namespace Ferrostorm.Client;

/// <summary>
/// Live 3D skirmish: the human commands player 0 against a SkirmishAI on
/// player 1, on the committed map, in the full 3D battlefield. This is the
/// Main.cs loopback pattern (World stepped at exactly 15 Hz, rendering only
/// ever from SnapshotInterpolator samples - ADR-001) bound to the Battle3D
/// visual layer. The sim is never read mid-tick and never mutated outside
/// Step's command list.
///
/// Controls: left click / drag selects own units; right click orders
/// (move, attack an enemy under the cursor, harvest a ferrite field);
/// shift adds to selection and queues orders; arrows/edge/wheel camera.
/// Every key is an InputMap action and rebindable (TICKET-P5-SET-01); nothing
/// in this file matches a literal keycode any more except the bare modifiers
/// (shift to queue, ctrl to assign a group), which are not actions because a
/// modifier is not a binding.
/// </summary>
public partial class SkirmishLive : Node3D
{
    private const double TickSeconds = 1.0 / World.TicksPerSecond;

    private World _world = null!;
    private SkirmishAI? _enemy;
    private MissionRunner? _mission;
    private readonly List<Command> _missionCmds = new();
    private int _seenMessages;
    private readonly SnapshotInterpolator _interp = new(windowTicks: 8);
    private readonly List<Command> _pending = new();
    private readonly List<Command> _tickCmds = new();
    private readonly List<SnapshotInterpolator.ViewEntity> _view = new();
    private readonly HashSet<int> _selection = new();
    private readonly Dictionary<int, Node3D> _actors = new();
    private readonly Dictionary<int, Vector3> _targets = new();
    private readonly Dictionary<int, SnapshotInterpolator.ViewEntity> _latest = new();
    private ModelLibrary _models = null!;
    private RtsCamera _cam = null!;
    private double _accumulator;
    private double _renderTime;
    private int _winner = -1;

    // TICKET-P5-SAVE-01: persistence and replays. Exactly one of these three
    // states holds for a scene: recording a fresh live match (_rec set), playing
    // a recording back (_replay set), or resumed from a save (_resumed, which
    // records nothing - see ResumeFromSave).
    private MatchSetup _setup = new();
    private ReplayWriter? _rec;
    private string _recPath = "";
    private bool _recDone;
    private Replay? _replay;
    private int _replayTicks;
    private bool _replayDone;
    private bool _replayVerified;
    private ulong _replayFinalHash;
    private bool _resumed;
    private PauseMenu? _pauseMenu;
    // ADR-006: match assembly failed (broken /data, foreign catalogue in a
    // save or replay) and the scene is standing down to the menu. Guards the
    // per-frame callbacks for the deferred frames between refusal and change.
    private bool _refused;

    // HUD
    private CanvasLayer _hud = null!;
    private Label _status = null!;
    private Label _banner = null!;
    private Panel _dragRect = null!;
    private Vector2 _dragStart;
    private bool _dragging;
    private Sidebar _sidebar = null!;
    private AudioDirector _audio = null!;
    private CombatEffects _effects = null!;
    // W3-19: production-complete flyout toast beside the sidebar.
    private Label _toast = null!;
    private Tween? _toastTween;
    // TICKET-P5-SET-01: the LAN desync notice (doc 18 Phase D: "desync notice
    // surfaced in the HUD"). Driven by NetSession, latched, never fades.
    private Label _desyncNotice = null!;

    // Structure placement mode
    private int _placingType;
    private MeshInstance3D _ghost = null!;
    private int _yardId = -1, _factoryId = -1;
    // DEF-01: the ghost's own range ring (a child of the ghost, so it tracks
    // the cursor for free) plus a dim ring on every own armed structure while
    // placing - the coverage-gap read that makes turret siting a decision.
    private Node3D _ghostRing = null!;
    private readonly List<MeshInstance3D> _coverageRings = new();
    // DEF-08: drag-a-line barrier placement.
    private bool _wallDrag;
    private (int X, int Y) _wallDragStart;
    private readonly List<MeshInstance3D> _dragGhosts = new();
    private int _dragRun;
    private static readonly BoxMesh WallGhostMesh = new() { Size = new Vector3(1, 0.5f, 1) };
    // DEF-08 clause 3: neighbour masks are rebuilt only when the wall
    // population changes, never per frame - 160 segments rescanned every frame
    // for a set that changes at most four cells per placement is not free.
    private readonly Dictionary<int, int> _wallMask = new();
    private bool _wallsDirty;
    // DEF-09 clause 4: a mass sell is irreversible and the sim has no cancel,
    // so it asks once. -1 means no confirmation is pending.
    private double _sellConfirmUntil = -1;
    // TICKET-P5-SET-01: attack-move is armed by its key and committed by the
    // next left click, so the player picks the destination rather than the
    // mouse happening to be somewhere when the key went down.
    private bool _attackMoveArmed;
    // DEF-01 clause 9: the ghost tint was rebuilding a StandardMaterial3D every
    // frame while placing. Two immutable materials, assigned by reference.
    private static readonly StandardMaterial3D GhostValidMat = GhostMat(new Color(0.3f, 0.9f, 0.4f, 0.4f));
    private static readonly StandardMaterial3D GhostInvalidMat = GhostMat(new Color(0.9f, 0.25f, 0.2f, 0.4f));
    private static StandardMaterial3D GhostMat(Color c) => new()
    {
        AlbedoColor = c,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    /// <summary>DEF-01: the WeaponId a structure type spawns with. This mirrors
    /// the WeaponId hardcoded in World.SpawnTurret (World.cs:486) and MUST be
    /// updated alongside it - the sim is the source of truth, this is a
    /// presentation-side echo of it for the placement preview only.</summary>
    private static int WeaponOfStruct(int structType) => structType == 5 ? 4 : 0;

    /// <summary>DEF-01: a weapon's range in world units. Fix64.Raw is public and
    /// FracBits is 32, so this is the same conversion SnapshotInterpolator.ToDouble
    /// uses. Weapon 0 (none) has no ring.</summary>
    private static float RangeOf(int weaponId) =>
        weaponId == 0 ? 0f : (float)(Weapons.Get(weaponId).Range.Raw / 4294967296.0);

    // Fog, minimap, groups, alerts
    private FogOfWar _fog = null!;
    private Minimap _minimap = null!;
    private readonly Dictionary<int, HashSet<int>> _groups = new();
    private double _lastAttackAlert = -60;
    // GDD s7 line 85 names "harvester under attack" and "base under attack" as
    // SEPARATE alerts, so they carry separate cooldowns. Sharing one would let
    // a harassed harvester swallow the alert that says the base is falling,
    // which is the exact moment the player most needs telling.
    private double _lastHarvesterAlert = -60;
    // TICKET-P5-PWR-01 (part of ALERT-02): the low-power alert is EDGE
    // triggered on the 75 per cent crossing, not on the state - fire when the
    // ratio drops below three quarters having been above it, re-arm when it
    // recovers. Once per crossing, never once per frame, and not at the mere
    // sub-100 dip either: below 100 the player is only slowed and the sidebar
    // bar already reads amber; below 75 the guns are next (PWR-04).
    private bool _wasBrownOut;
    /// <summary>Verification read (the _autoHarvestIssues pattern): how many
    /// times the low-power alert actually fired, so a test can prove the edge
    /// trigger fires once per crossing rather than once per frame.</summary>
    public int LowPowerAlerts { get; private set; }
    // TICKET-P5-ALERT-02: GDD s7 line 85's "jump-to-event key". Every alert
    // site records the world position it pinged; the key flies the camera to
    // the MOST RECENT one, which a plain overwrite gives for free. -1 means
    // no alert has fired this match, and the key then does nothing and is
    // deliberately not consumed (the deploy key's rule: an event that meant
    // nothing is left to whatever else might answer the key after a rebind).
    private Vector3 _lastAlertPos;
    private double _lastAlertAt = -1;
    /// <summary>Verification read: how many enemy superweapon launches this
    /// client has alerted on (the LowPowerAlerts pattern).</summary>
    public int LaunchAlerts { get; private set; }
    // TICKET-P5-REP-06: mass-repair confirmation, the sell-guard shape.
    // -1 means no confirmation is pending.
    private double _repairConfirmUntil = -1;
    // TICKET-P5-SPAWN-02: own Deploys awaiting a verdict (MCV id -> the tick
    // the order was applied on), so the sim's silent refusal can be reported.
    private readonly Dictionary<int, int> _pendingDeploys = new();
    // TICKET-P5-SPAWN-03: the MCV's catalogue id (com_mcv in World's compiled
    // unit catalogue, and the id the sim's own Deploy guard tests). Named here
    // because the deploy control tests it in three places and a bare 7 in each
    // is how one of them drifts.
    private const int McvUnitType = 7;
    // TICKET-P5-REP-02: rolling bay counter, so no two depot-send orders ever
    // carry the identical destination (see SendMobilesToDepot for why exact
    // equality matters: the sim's arrival contagion keys on it).
    private int _depotBay;
    private int _mapW, _mapH;

    // Structure interaction: rally markers per producer, selection readout.
    // TICKET-P5-BD-14: one marker per rallied structure, shown only while that
    // structure is selected (the classic rule). ADR-007 moved the rally POINT
    // itself into the sim (RallyX/RallyY on the producer): the old `_rally`
    // dictionary is gone, the click path issues CommandType.SetRally, and
    // marker positions are written every frame from the sim's own fields, so
    // the marker can never drift from the truth. _rallyMarkers survives as a
    // node-lifetime cache only.
    private readonly Dictionary<int, MeshInstance3D> _rallyMarkers = new();
    private Label _selInfo = null!;

    // P5-ECON-07. A harvester the player parked on purpose stays parked: an
    // explicit Stop, a bare move order, or a rally point enrols it here, and
    // only an explicit Harvest order takes it out again. _lastAutoHarvest
    // rate-limits the re-issue to one attempt per harvester per second.
    private readonly HashSet<int> _manuallyStopped = new();
    private readonly Dictionary<int, int> _lastAutoHarvest = new();
    private const int AutoHarvestEvery = 15;   // ticks; World.TicksPerSecond
    /// <summary>How many auto-Harvest orders this client has issued. The rate
    /// limit is the point of the feature rather than a detail of it, so it is
    /// counted where it happens: the commands themselves are drained into the
    /// tick and gone, and a test that counts them afterwards counts nothing and
    /// passes for the wrong reason. Same pattern as _wallRebuilds.</summary>
    private int _autoHarvestIssues;

    /// <summary>Producers the CLIENT offers a rally click on (TICKET-P5-BD-14
    /// clause 5). Factory only: it is the one producer that spawns units. The
    /// sim's own SetRally predicate additionally accepts a Construction Yard
    /// (ADR-007, ahead of ADR-009's producer split), but a CY's products are
    /// placed rather than spawned, so offering the click would be the Service
    /// Depot dead affordance again (TICKET-P5-REP-10 retired that honestly).
    /// This stays a predicate rather than a compare because PROD-05 adds
    /// Barracks and Airfield here when those producers exist.</summary>
    private static bool Ralliable(EntityKind k) => k is EntityKind.Factory;

    // W2-01 ActorRig: named child nodes become animation handles. Turrets
    // slew (TICKET-P4-SLICE-01) and recoil; wheels spin; dishes rotate;
    // antennas sway - coverage grows as models de-merge parts.
    private sealed class ActorRig
    {
        public Node3D? Turret;
        public Vector3 TurretRest;
        public Tween? RecoilTw;
        public readonly List<Node3D> Wheels = new();
        public Node3D? Dish;
        public Node3D? Intake;
        public Vector3 IntakeRest;
        public readonly List<(Node3D Node, Vector3 Home)> Doors = new();
        public Tween? DoorTw;
        public float PrevSpeed;   // W2-04 hull pitch spring
        public float Pitch;
        public float BobPhase;    // W2-07 infantry stride
    }
    private readonly Dictionary<int, ActorRig> _rigs = new();
    private readonly Dictionary<int, (Vector3 At, double Until)> _aim = new();
    // W4-16: tyre-track decals - last stamp position per vehicle, plus a
    // recycling pool capped at 96 decals.
    private readonly Dictionary<int, Vector3> _lastTrack = new();
    private readonly Queue<Decal> _trackPool = new();
    private double _now;
    private GpuParticles3D _dust = null!;

    // W3-14: billboard health bars, shared static resources (the
    // CombatEffects pattern). Bars are children of the scene, not the actor,
    // so hull yaw never swings the offset. CenterOffset 0.45 anchors each
    // quad's left edge at its node origin, so Scale.X shrinks rightward from
    // a fixed left edge; the RTS camera never yaws, so world X == billboard X
    // and the two quads stay aligned. Team colour is NOT used here (doc 16
    // one-place law lives on the ground ring); the ramp is health state.
    private static readonly QuadMesh HpBackMesh = new() { Size = new Vector2(0.9f, 0.10f), CenterOffset = new Vector3(0.45f, 0, 0) };
    private static readonly QuadMesh HpFillMesh = new() { Size = new Vector2(0.9f, 0.07f), CenterOffset = new Vector3(0.45f, 0, 0) };
    private static StandardMaterial3D Bar(Color c, int prio) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = c,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        BillboardKeepScale = true,
        NoDepthTest = true,
        RenderPriority = prio,
    };
    private static readonly StandardMaterial3D BackMat = Bar(new Color(0.04f, 0.04f, 0.05f, 0.85f), 10);
    private static readonly StandardMaterial3D FillGreen = Bar(new Color(0.42f, 0.78f, 0.32f), 11);
    private static readonly StandardMaterial3D FillAmber = Bar(new Color(0.90f, 0.68f, 0.22f), 11);
    private static readonly StandardMaterial3D FillRed = Bar(new Color(0.85f, 0.28f, 0.20f), 11);
    private readonly Dictionary<int, (MeshInstance3D Back, MeshInstance3D Fill)> _hpBars = new();

    // TICKET-P5-VET-01: rank chevrons, riding the health-bar machinery (scene
    // children, billboarded, positioned per frame so hull yaw never swings
    // them). One triangle at rank 1 (3 kills), two at rank 2 (6), in the rally
    // gold the HUD already owns. Both pips are made together and visibility
    // does the rest: rank never decreases, so there is no shrink path to test.
    private static readonly ArrayMesh PipMesh = MakePipMesh();
    private static readonly StandardMaterial3D PipMat = MakePipMat();
    private static ArrayMesh MakePipMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.AddVertex(new Vector3(-0.085f, 0f, 0f));
        st.AddVertex(new Vector3(0f, 0.095f, 0f));
        st.AddVertex(new Vector3(0.085f, 0f, 0f));
        return st.Commit();
    }
    private static StandardMaterial3D MakePipMat()
    {
        var m = Bar(new Color(0.79f, 0.63f, 0.36f), 12);
        m.CullMode = BaseMaterial3D.CullModeEnum.Disabled; // a one-sided triangle vanishes at half the billboard's yaws
        return m;
    }
    private MeshInstance3D NewPip() => new()
    {
        Mesh = PipMesh,
        MaterialOverride = PipMat,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Visible = false,
    };
    private readonly Dictionary<int, (MeshInstance3D P1, MeshInstance3D P2)> _rankPips = new();

    private static void ScanRig(Node n, ActorRig rig)
    {
        if (n is Node3D t3)
        {
            string nm = n.Name.ToString();
            if (nm.StartsWith("turret")) { rig.Turret = t3; rig.TurretRest = t3.Position; }
            else if (nm.StartsWith("wheel")) rig.Wheels.Add(t3);
            else if (nm.StartsWith("dish")) rig.Dish = t3;
            else if (nm.StartsWith("intake")) { rig.Intake = t3; rig.IntakeRest = t3.Rotation; }
            else if (nm.StartsWith("door")) rig.Doors.Add((t3, t3.Position));
        }
        foreach (var c in n.GetChildren()) ScanRig(c, rig);
    }

    public override void _Ready()
    {
        // TICKET-P5-SET-01: applied here as well as in the menu, because
        // Skirmish.tscn is launched scene-direct (that is how this client is
        // verified offscreen) and a battle reached that way must still come up
        // with the player's volume, video and key bindings.
        Settings.EnsureLoaded();

        _models = new ModelLibrary();
        AddChild(_models);

        // ADR-006 commitment 2: everything fallible about assembling a match
        // (the map file, the /data catalogue, a save's or replay's recorded
        // catalogue checksum) fails into a readable notice on the menu, never
        // a crash. The battle does not start half-registered: one failure
        // anywhere in this block and the scene stands down entirely.
        MapData map;
        try
        {
            _setup = MatchConfig.CurrentSetup();
            map = MapData.Load(GameFiles.Abs(_setup.MapPath));
            _world = BuildStartingWorld(_setup, map, out var tags);
            if (_setup.IsMission) _mission = new MissionRunner(map, tags);
            else _enemy = _setup.AiPreset switch
            {
                1 => SkirmishAI.Rusher(1),
                2 => SkirmishAI.Turtle(1),
                _ => SkirmishAI.Standard(1),
            };

            // TICKET-P5-SAVE-01: a scene is one of three things, decided here once.
            if (MatchConfig.LoadPath is { } loadPath) ResumeFromSave(loadPath);
            else if (MatchConfig.ReplayPath is { } replayPath) BeginPlayback(replayPath);
            else BeginRecording();
        }
        catch (System.Exception e)
        {
            _refused = true;
            MainMenu.BattleRefusedNotice = e.Message;
            MatchConfig.LoadPath = null;
            MatchConfig.ReplayPath = null;
            // Deferred: tearing the scene down from inside its own _Ready is
            // how a refusal becomes a crash of its own.
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/MainMenu.tscn");
            return;
        }
        // Consumed. Both describe THIS scene change and nothing after it, and a
        // ReplayPath left standing would silently turn the next skirmish the
        // player starts into a playback of the last one they watched.
        MatchConfig.LoadPath = null;
        MatchConfig.ReplayPath = null;

        BattlefieldView.BuildEnvironment(this);
        BattlefieldView.BuildTerrain(this, map.Width, map.Height, map.Blocked, map.Visual);
        _mapW = map.Width; _mapH = map.Height;
        _mapBlocked = map.Blocked;
        _fog = new FogOfWar();
        AddChild(_fog);
        _fog.Init(map.Width, map.Height);

        _cam = new RtsCamera();
        AddChild(_cam);
        _dust = BattlefieldView.BuildDust();
        AddChild(_dust);
        _cam.Position = new Vector3(map.Starts[0].Cx, 22, map.Starts[0].Cy + 14);
        _cam.Snap(_cam.Position);
        _cam.BoundsMin = Vector2.Zero;
        _cam.BoundsMax = new Vector2(map.Width, map.Height);
        _cam.Current = true;

        BattlefieldView.BuildLightRig(this);

        _audio = new AudioDirector();
        AddChild(_audio);
        _audio.PlayAmbient();
        // TICKET-P6-MUSIC-01: the score starts with the battle, calm bed up,
        // combat layer silent until the intensity signal raises it.
        _audio.PlayMusic();
        // TICKET-P6-CURSOR-01: battle scenes own the cursor; menus keep the
        // OS default (nothing outside this scene ever sets one).
        EnsureCursorsLoaded();
        _effects = new CombatEffects();
        AddChild(_effects);
        _effects.Camera = _cam;

        BuildHud();

        _ghost = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(2, 0.5f, 2) },
            Visible = false,
        };
        AddChild(_ghost);
        // DEF-01: the ghost's range ring rides as a CHILD of the ghost, so the
        // ghost's own cursor-follow carries it and nothing per-frame is needed.
        // Built at radius 1 so Scale is literally the radius.
        _ghostRing = BattlefieldView.MakeRangeRing(1f, BattlefieldView.RangeRingOwn);
        _ghost.AddChild(_ghostRing);
        _ghostRing.Visible = false;

        SnapshotNow();
    }

    // ---------------- TICKET-P5-SAVE-01: setup, saves, replays ----------------

    /// <summary>
    /// The world a match begins from, derived from nothing but its setup and its
    /// map. Static and free of scene state on purpose: a fresh match, a replay
    /// playback and offscreen verification must all build the identical world
    /// from the identical inputs, and the only way to guarantee that is for
    /// there to be exactly one function that builds it. Every value it reads is
    /// carried in the sidecar beside the file, so a save or a replay written
    /// today still rebuilds its own world after the setup options grow.
    /// </summary>
    public static World BuildStartingWorld(MatchSetup setup, MapData map,
        out Dictionary<string, List<int>> tags)
    {
        if (setup.IsMission)
        {
            // Campaign mission: the map places both sides' forces and the
            // triggers script the enemy - no skirmish AI. Per-mission setup
            // mirrors the runner's gated scenarios. The catalogue registrar
            // rides the configure hook so mission-placed forces spawn off
            // /data values, not compiled ones (ADR-006).
            var m = map.BuildWorld(setup.Seed, players: 2, out tags, RegisterCatalogue);
            switch (setup.MissionIndex)
            {
                case 1:
                    m.GrantCredits(0, 5000);
                    m.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
                    break;
                case 3:
                    m.GrantCredits(0, 4000);
                    break;
            }
            return m;
        }
        var w = map.BuildWorld(setup.Seed, players: 2, out tags, RegisterCatalogue);
        // TICKET-P6-FACTION-01: the sides, before any spawn and before tick 0.
        // After BuildWorld on purpose: no shipped skirmish map declares
        // factions (Q001), and if one ever does, the player's menu choice is
        // the one that must win in a skirmish. The mission branch above stays
        // untouched: a mission's map speaks for itself.
        w.SetFaction(0, setup.Faction);
        w.SetFaction(1, setup.OppFaction);
        w.GrantCredits(0, setup.StartCredits);
        w.GrantCredits(1, setup.StartCredits);
        w.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
        w.SpawnConstructionYard(1, map.Starts[1].Cx, map.Starts[1].Cy);
        // Mirrored starting force (common hardware only, so faction-neutral):
        // a harvester and three rifle squads each - the classic opening hand.
        for (int p = 0; p < 2; p++)
        {
            int sx = map.Starts[p].Cx, sy = map.Starts[p].Cy;
            int side = p == 0 ? 1 : -1;
            w.SpawnHarvester(p, Fix64.FromInt(sx + 3 * side), Fix64.FromInt(sy + 2));
            for (int i = 0; i < 3; i++)
                w.SpawnUnit(p, Fix64.FromInt(sx + (2 + i) * side), Fix64.FromInt(sy - 2),
                    Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
        }
        return w;
    }

    /// <summary>
    /// ADR-006: the client's half of "the client loads /data before tick 0,
    /// exactly as the runner does". Resolution through GameFiles.RepoRoot is
    /// the established pathing idiom (the ADR names it); the walk, the
    /// registration and every failure message live in the sim's
    /// CatalogueFiles, the runner's own load path made callable, so the gate
    /// and the shipped game exercise ONE implementation. On any failure this
    /// throws, _Ready catches, and the menu shows the message: the compiled
    /// catalogue is deliberately NOT the fallback, because a silent fallback
    /// would resurrect the two-catalogue ambiguity the ADR exists to end.
    /// </summary>
    public static void RegisterCatalogue(World w) =>
        CatalogueFiles.RegisterAll(w,
            Path.Combine(GameFiles.RepoRoot, "data", "units"),
            Path.Combine(GameFiles.RepoRoot, "data", "buildings"));

    /// <summary>Replace the freshly built world with a saved one. The fresh
    /// build above is not waste: it is what produces the mission tags, which a
    /// save deliberately does not store (MissionRunner's own note - the mission
    /// is rebuilt from the same map, then its state is restored on top).</summary>
    private void ResumeFromSave(string path)
    {
        // Read the whole file into memory first and drive both readers off the
        // one stream, which is the exact shape the campaignsave gate proves.
        // ADR-006: the loaded world gets the /data catalogue registered inside
        // its tick 0 window, and a v3 save whose recorded checksum disagrees
        // refuses in World.Load with both checksums named; pre-v3 saves carry
        // none and are never refused.
        var ms = new MemoryStream(File.ReadAllBytes(path));
        _world = World.Load(ms, RegisterCatalogue);
        // The faction re-apply workaround that lived here is gone: the sim
        // round-trips _playerFaction itself as of the Q001 fix (save format
        // v2; the hardened saveload gate pins it). See docs/questions/
        // Q001-save-drops-playerfaction.md for the history.
        if (_mission != null)
        {
            using var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            _mission.LoadState(br);
            // The restored message log is history, not news. Without this the
            // resumed battle fires an objective toast for every message the
            // mission had already delivered, racing a tween per message on one
            // label. Surfacing the current objective persistently on resume is a
            // real gap, but it is a HUD feature and not this ticket's.
            _seenMessages = _mission.Messages.Count;
        }
        // A .frep is a command stream from tick 0. A session that begins at tick
        // T cannot honestly be expressed in that format without shipping the
        // save alongside it, so a resumed game records nothing. Said out loud in
        // the HUD rather than left for the player to discover.
        _resumed = true;
    }

    private void BeginPlayback(string path)
    {
        _replay = Replay.Load(path);
        // ADR-006: a recording made against a different catalogue refuses
        // before a single tick re-simulates, with both checksums named,
        // rather than running to an inevitable DIVERGED verdict. Pre-v3
        // recordings carry no checksum and play exactly as before.
        _replay.AssertCatalogueMatches(_world.CatalogueChecksum);
        _replayTicks = MatchConfig.ReplayTicks;
        _enemy = null;   // the recorded stream already carries the AI's decisions
    }

    private void BeginRecording()
    {
        // ADR-006: every fresh recording carries the catalogue it was played
        // against, for the same reason saves do.
        _rec = new ReplayWriter(_setup.Seed, _setup.MapName, _world.CatalogueChecksum);
        string stamp = Time.GetDatetimeStringFromSystem(utc: true).Replace(':', '-');
        string stem = $"{stamp}-{_setup.MapName}";
        _recPath = Path.Combine(GameFiles.ReplaysDir, stem + ".frep");
        for (int n = 1; File.Exists(_recPath); n++)
            _recPath = Path.Combine(GameFiles.ReplaysDir, $"{stem}-{n}.frep");
    }

    /// <summary>Close the recording and write its sidecar. Idempotent, because
    /// it is reached from victory, from abandoning, and from _ExitTree as a
    /// backstop, and ReplayWriter.Finish appends its hash line every call.</summary>
    private void FinishRecording()
    {
        if (_rec is null || _recDone) return;
        _recDone = true;
        if (_world.Tick < GameFiles.MinRecordedTicks) return;
        ulong hash = _world.ComputeStateHash();
        _rec.Finish(hash, _recPath);
        var meta = MatchMeta.For(_setup, _world.Tick, _world.Credits(0));
        meta.FinalHash = $"{hash:X16}";
        meta.Write(Path.ChangeExtension(_recPath, ".json"));
    }

    /// <summary>The playback verdict: this is the replay acceptance criterion
    /// made visible to the player rather than only to a test. The stream
    /// re-simulated to the recorded hash, or it did not, and the banner says
    /// which.</summary>
    private void FinishPlayback()
    {
        _replayDone = true;
        ulong got = _world.ComputeStateHash();
        _replayFinalHash = got;
        _replayVerified = got == _replay!.FinalHash;
        _banner.AddThemeFontSizeOverride("font_size", 22);
        _banner.Text = _replayVerified
            ? $"REPLAY COMPLETE\n\n{_world.Tick} TICKS RE-SIMULATED\n0x{got:X16} MATCHES THE RECORDING\n\npress escape for uplink"
            : $"REPLAY DIVERGED\n\n0x{got:X16}\nvs recorded 0x{_replay.FinalHash:X16}\n\npress escape for uplink";
        _banner.AddThemeColorOverride("font_color",
            _replayVerified ? BattlefieldView.DirectorateMark : new Color(0.85f, 0.25f, 0.2f));
        _banner.Visible = true;
    }

    public bool CanSave => _replay is null;

    public string ModeLine() => _replay != null
        ? $"REPLAY PLAYBACK   {_setup.Describe()}   TICK {_world.Tick} / {_replayTicks}"
        : _resumed
            ? $"{_setup.Describe()}   TICK {_world.Tick}   (resumed - not recording)"
            : $"{_setup.Describe()}   TICK {_world.Tick}";

    /// <summary>Write the world (and, in a campaign, the mission's own trigger
    /// state) to a slot, with the sidecar the browser reads. World then mission
    /// on one stream is the order the campaignsave gate proves.</summary>
    public void SaveToSlot(int slot)
    {
        using var ms = new MemoryStream();
        _world.Save(ms);
        if (_mission != null)
        {
            using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            _mission.Save(bw);
        }
        File.WriteAllBytes(GameFiles.SlotSave(slot), ms.ToArray());
        MatchMeta.For(_setup, _world.Tick, _world.Credits(0)).Write(GameFiles.SlotMeta(slot));
        _audio.Play("ui_confirm", -6);
    }

    /// <summary>Loading rebuilds the scene rather than mutating this one: every
    /// actor, rig, decal and effect in the tree belongs to the world being
    /// discarded, and reconciling them against a world from another tick is a
    /// far larger surface than simply starting the scene again with a load path
    /// set. _Ready is the one place a match is assembled, so it stays that way.</summary>
    public void LoadFromSlot(int slot, MatchMeta meta)
    {
        FinishRecording();
        MatchConfig.ApplyFrom(meta);
        MatchConfig.LoadPath = GameFiles.SlotSave(slot);
        GetTree().ChangeSceneToFile("res://scenes/Skirmish.tscn");
    }

    public void QuitToMenu()
    {
        FinishRecording();
        GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
    }

    /// <summary>Last-ditch finalisation: a window closed mid-match, or a scene
    /// changed by a path that forgot. A half-written recording is worth more
    /// than none. TICKET-P6-CURSOR-01: the custom cursor is battle furniture,
    /// so leaving the scene hands the pointer back to the OS default.</summary>
    public override void _ExitTree()
    {
        FinishRecording();
        Input.SetCustomMouseCursor(null);
    }

    public void TogglePause()
    {
        if (_pauseMenu != null) { ClosePause(); return; }
        if (_winner >= 0 || _replayDone) return;
        _paused = true;
        _banner.Visible = false;
        _pauseMenu = new PauseMenu { Name = "PauseMenu" };
        _hud.AddChild(_pauseMenu);
        _pauseMenu.Init(this);
        _audio.Play("ui_click", -8);
    }

    public void ClosePause()
    {
        if (_pauseMenu is null) return;
        _pauseMenu.QueueFree();
        _pauseMenu = null;
        _paused = false;
    }

    // ---------------- sidebar command surface ----------------

    private int FindOwnStructure(EntityKind kind)
    {
        foreach (var v in _view)
            if (v.Alive && v.PlayerId == 0 && v.Kind == kind) return v.Id;
        return -1;
    }

    /// <summary>
    /// TICKET-P5-ECON-06. A Harvest order with no refinery standing is accepted
    /// by the sim and accomplishes nothing: it sets FieldId, asks for the
    /// nearest refinery, gets -1 back, and leaves the harvester Idle without
    /// emitting so much as an event. The player sees a right-click that does
    /// nothing at all and concludes harvesting is broken. The sim is not the
    /// place to fix it (refusing the command would change ApplyCommandCore's
    /// state mutation and move every AI-driven golden), so the client refuses to
    /// send it.
    /// </summary>
    private bool HasLiveRefinery()
    {
        foreach (var e in _world.Entities)
            if (e.Alive && e.PlayerId == 0 && e.Kind == EntityKind.Refinery) return true;
        return false;
    }

    /// <summary>
    /// The nearest field with ferrite left, by squared distance in the sim's own
    /// fixed-point maths, ties breaking to the lower entity id. This mirrors
    /// World.RetargetField deliberately and exactly, including its strict
    /// less-than: if the client picked a different field from the one the sim
    /// would, the auto-resume would fight the sim's own reassignment.
    /// </summary>
    private int NearestFieldTo(Fix64 x, Fix64 y)
    {
        int best = -1; Fix64 bestD = Fix64.MaxValue;
        var ents = _world.Entities;
        for (int j = 0; j < ents.Count; j++)
        {
            var f = ents[j];
            if (!f.Alive || f.Kind != EntityKind.FerriteField || f.FerriteAmount <= 0) continue;
            Fix64 d = Fix64.DistSq(f.X - x, f.Y - y);
            if (d < bestD) { bestD = d; best = j; }
        }
        return best;
    }

    /// <summary>
    /// TICKET-P5-ECON-07. A harvester that reaches HarvestState.Idle stays Idle
    /// for the rest of the match: only a fresh Harvest command sets HState, and
    /// nothing in the sim issues one. It bites whenever the player orders Harvest
    /// before building a refinery, whenever every reachable field runs dry and an
    /// expansion opens a new one, and whenever the last refinery dies and is
    /// replaced. The AI hides all three by re-issuing Harvest every single tick;
    /// the player had no equivalent, so this is quality of life the AI already
    /// had and the human did not.
    ///
    /// It is not strategy automation (GDD s1 draws that line): a harvester the
    /// player explicitly Stopped, moved by hand, or sent to a rally point stays
    /// exactly where it was put, forever, and only an explicit Harvest order
    /// re-enrols it. The re-issue goes through the ordinary command path, so it
    /// is deterministic, lockstep-legal and replay-clean, and it is rate-limited
    /// to one attempt per harvester per second: the AI's per-tick flood through a
    /// lockstep relay is a known waste (P5-ECON-15) and reproducing it here would
    /// be a new one.
    ///
    /// The honest home for this is the sim, where HarvestSystem already retries
    /// FindNearestRefinery and already reassigns dry fields; it lives here
    /// because that is touchesSim and this ships today. Recorded as a follow-up
    /// in docs/questions/, not quietly forgotten.
    /// </summary>
    private void AutoResumeHarvesters()
    {
        if (_replay != null) return;             // a spectator issues no orders
        if (!HasLiveRefinery()) return;          // P5-ECON-06's rule, same reason
        int tick = _world.Tick;
        var ents = _world.Entities;
        for (int i = 0; i < ents.Count; i++)
        {
            var e = ents[i];
            if (!e.Alive || e.PlayerId != 0 || e.Kind != EntityKind.Harvester) continue;
            if (e.HState != HarvestState.Idle) continue;
            if (_manuallyStopped.Contains(i)) continue;
            if (_lastAutoHarvest.TryGetValue(i, out int last) && tick - last < AutoHarvestEvery) continue;
            int field = NearestFieldTo(e.X, e.Y);
            if (field < 0) continue;             // nothing left to mine: parking is correct
            _lastAutoHarvest[i] = tick;
            _autoHarvestIssues++;
            _pending.Add(new Command(0, 0, CommandType.Harvest, i, Fix64.Zero, Fix64.Zero, field));
        }
    }

    public void QueueStructure(int structType)
    {
        if (!(MatchConfig.AllowedStructures?.Contains(structType) ?? true)) return;
        if (_yardId >= 0)
        {
            _pending.Add(new Command(0, 0, CommandType.BuildStructure, _yardId, Fix64.Zero, Fix64.Zero, structType));
            _audio.Play("ui_confirm", -6);
        }
    }

    public void QueueUnit(int unitType)
    {
        if (!(MatchConfig.AllowedUnits?.Contains(unitType) ?? true)) return;
        if (_factoryId >= 0)
        {
            _pending.Add(new Command(0, 0, CommandType.Produce, _factoryId, Fix64.Zero, Fix64.Zero, unitType));
            _audio.Play("ui_confirm", -6);
        }
    }

    public void EnterPlacement(int structType)
    {
        _placingType = structType;
        _ghost.Visible = true;
        // DEF-01: the ghost is the real footprint, not a hardcoded 2x2. A
        // barrier is 1x1 (ADR-005 clause 1) and must not preview as a building.
        float f = _world.FootprintOf(structType);
        _ghost.Mesh = new BoxMesh { Size = new Vector3(f, 0.5f, f) };
        // The ghost's own range ring: the mesh is built at radius 1, so Scale
        // IS the radius. Unarmed structures show none.
        float r = RangeOf(WeaponOfStruct(structType));
        _ghostRing.Visible = r > 0;
        if (r > 0) _ghostRing.Scale = new Vector3(r, 1, r);
        // Coverage rings on every armed structure already standing, so the
        // player reads the gap they are filling rather than guessing at it.
        ClearCoverageRings();
        foreach (var v in _view)
        {
            if (!v.Alive || v.PlayerId != 0 || Mobile(v.Kind)) continue;
            if (v.Id < 0 || v.Id >= _world.EntityCount) continue;
            float cr = RangeOf(_world.Entities[v.Id].WeaponId);
            if (cr <= 0) continue;
            var ring = BattlefieldView.MakeRangeRing(1f, BattlefieldView.RangeRingCoverage);
            ring.Position = new Vector3((float)v.X,
                BattlefieldView.GroundHeight((float)v.X, (float)v.Y) + 0.04f, (float)v.Y);
            ring.Scale = new Vector3(cr, 1, cr);
            AddChild(ring);
            _coverageRings.Add(ring);
        }
        _audio.Play("ui_click", -6);
    }

    /// <summary>DEF-01 clause 6: the single teardown for placement mode, which
    /// two call sites previously open-coded as a two-line clear that could not
    /// know about the coverage rings.</summary>
    private void ExitPlacement()
    {
        _placingType = 0;
        _ghost.Visible = false;
        ClearCoverageRings();
        ClearDragGhosts();
        _wallDrag = false;
    }

    private void ClearCoverageRings()
    {
        foreach (var r in _coverageRings) r.QueueFree();
        _coverageRings.Clear();
    }

    private void BuildHud()
    {
        var hud = _hud = new CanvasLayer { Name = "Hud" };
        AddChild(hud);
        _status = new Label
        {
            Position = new Vector2(16, 12),
            Theme = ThemeDB.GetProjectTheme(),
        };
        _status.AddThemeColorOverride("font_color", new Color(0.84f, 0.82f, 0.77f));
        hud.AddChild(_status);

        // TICKET-P5-SET-01: the hint reads the live bindings rather than
        // restating the defaults. A hint that says "R repair" to a player who
        // rebound repair to F is worse than no hint.
        string K(string action) => Settings.KeyName(Settings.BindOf(action));
        var hint = new Label
        {
            // TICKET-P5-SAVE-01: a replay takes no orders, so it must not advertise
            // orders. Telling a spectator to right-click to attack, and then
            // swallowing the click, is the exact "silently ignores orders" read
            // that makes a working replay look like a broken game.
            Text = _replay != null
                ? $"REPLAY PLAYBACK   the recorded stream issues every order   click: select   arrows/edge/wheel camera   {K("cancel")}: uplink"
                : $"click: select   right click: move / attack / harvest / rally   {K("attack_move")} attack-move  {K("stop")} stop  "
                  // TICKET-P5-REP-09: "repair bldgs", because R repairs
                  // structures and the old hint promised more than R could do.
                  // TICKET-P5-ALERT-02: "last alert" advertises the jump key,
                  // read from the live binding like every other key here.
                  + $"{K("repair")} repair bldgs  {K("sell")} sell  {K("jump_to_event")} last alert  {K("pause_menu")} menu   ctrl+1-9 groups   arrows/edge/wheel camera",
            AnchorTop = 1, AnchorBottom = 1, AnchorRight = 1,
            OffsetTop = -30, OffsetLeft = 16,
        };
        hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.48f));
        hud.AddChild(hint);

        // TICKET-P5-SET-01: the desync notice. Latched and unmissable, in the
        // one colour this HUD reserves for a thing that has gone wrong, because
        // a desynced lockstep match is over whether or not it looks like it:
        // both players carry on commanding worlds that no longer agree. Read
        // NetSession's header before believing this can fire in a shipped
        // match - nothing single-player drives it, and no networked mode ships.
        _desyncNotice = new Label
        {
            Name = "DesyncNotice",
            Visible = false,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            OffsetLeft = -300, OffsetRight = 300, OffsetTop = 118,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _desyncNotice.AddThemeFontSizeOverride("font_size", 18);
        _desyncNotice.AddThemeColorOverride("font_color", new Color(0.92f, 0.28f, 0.22f));
        hud.AddChild(_desyncNotice);

        _banner = new Label
        {
            Visible = false,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.4f, AnchorBottom = 0.4f,
            OffsetLeft = -200, OffsetRight = 200,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _banner.AddThemeFontSizeOverride("font_size", 42);
        hud.AddChild(_banner);

        _objective = new Label
        {
            Visible = false,
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            OffsetLeft = -260, OffsetRight = 260, OffsetTop = 52,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _objective.AddThemeFontSizeOverride("font_size", 16);
        _objective.AddThemeColorOverride("font_color", new Color(0.79f, 0.63f, 0.36f));
        hud.AddChild(_objective);

        // W3-19: flyout toast just left of the 190px sidebar, below the top
        // status row; ShowToast animates it in and fades it out.
        _toast = new Label
        {
            Name = "Toast",
            Visible = false,
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0,
            OffsetLeft = -520, OffsetRight = -205, OffsetTop = 84,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _toast.AddThemeFontSizeOverride("font_size", 15);
        _toast.AddThemeColorOverride("font_color", new Color(0.79f, 0.63f, 0.36f));
        hud.AddChild(_toast);

        _dragRect = new Panel { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.9f, 0.75f, 0.3f, 0.10f),
            BorderColor = new Color(0.9f, 0.75f, 0.3f, 0.8f),
        };
        style.SetBorderWidthAll(1);
        _dragRect.AddThemeStyleboxOverride("panel", style);
        hud.AddChild(_dragRect);

        _sidebar = new Sidebar();
        hud.AddChild(_sidebar);
        // BD-02/BD-06: the sidebar gets the catalogue by delegate. GetUnitType
        // was always an instance method and BD-06 made GetStructureType one too,
        // so both reads must come from THIS world: a match may register its own
        // catalogue before tick 0, and a sidebar reading compiled defaults would
        // quietly price the wrong game.
        // TICKET-P6-FACTION-01: the faction column joins the two catalogue
        // reads, by delegate for the same reason they are delegates.
        // ADR-006: and the unit price column joins them, because the sidebar
        // no longer carries a second copy of any cost.
        _sidebar.Init(this, t => _world.GetUnitType(t).BuildTicks, t => _world.GetStructureType(t),
            t => _world.GetUnitType(t).Faction, t => _world.GetUnitType(t).Cost);
        // TICKET-P5-SAVE-01: the sidebar is a command surface, and playback takes
        // no commands - RunOneTick drops _pending outright. Leaving it up meant a
        // replay showed lit build buttons and a "PLACE >>" prompt that did
        // nothing when clicked. Hidden rather than disabled: it is not the
        // spectator's queue, it is a recording of someone else's.
        _sidebar.Visible = _replay is null;

        _selInfo = new Label
        {
            AnchorTop = 1, AnchorBottom = 1,
            OffsetTop = -260, OffsetLeft = 14,
        };
        _selInfo.AddThemeFontSizeOverride("font_size", 13);
        _selInfo.AddThemeColorOverride("font_color", new Color(0.84f, 0.82f, 0.77f));
        hud.AddChild(_selInfo);

        _minimap = new Minimap();
        hud.AddChild(_minimap);
        var blocked = new List<(int, int)>();
        foreach (var (bx, by) in _mapBlocked) blocked.Add((bx, by));
        _minimap.Init(_mapW, _mapH, blocked, _fog.FogImage, world =>
        {
            // W3-11: minimap clicks glide instead of teleporting.
            _cam.FlyTo(new Vector3(world.X, 0, world.Y));
        });
    }
    private IReadOnlyList<(int Cx, int Cy)> _mapBlocked = System.Array.Empty<(int, int)>();

    // ---------------- sim loop ----------------

    private void SnapshotNow()
    {
        var (tick, entities, _) = _world.TakeSnapshot();
        _interp.AddSnapshot(tick, entities);
    }

    public override void _Process(double delta)
    {
        if (_refused) return;   // ADR-006: standing down; nothing here exists
        BattlefieldView.TickWater(delta);
        RefreshDesyncNotice();
        UpdateCursor();   // TICKET-P6-CURSOR-01: one resolve per frame
        if (_dust != null)
            _dust.GlobalPosition = new Vector3(_cam.Position.X, 2.5f, _cam.Position.Z - 8f);
        if (AutoStep && Running)
        {
            _accumulator += delta;
            while (_accumulator >= TickSeconds && Running)
            {
                _accumulator -= TickSeconds;
                RunOneTick();
            }
            if (!Running) _accumulator = 0;
        }
        AfterTicks(delta);
    }

    /// <summary>TICKET-P5-SAVE-01 verification hook. When false the scene never
    /// advances the sim from its own 15 Hz accumulator and only StepTicks moves
    /// it, so an offscreen run can measure a hash at an exact tick instead of
    /// racing the frame clock. Static because it has to be set BEFORE the scene
    /// loads: the race it removes is the one between _Ready finishing and the
    /// first _Process. Always true in a played game; nothing in the client ever
    /// writes it.</summary>
    public static bool AutoStep = true;

    /// <summary>Is the sim allowed to advance? A finished match, a pause, and a
    /// replay that has reached the end of its stream all stop it.</summary>
    private bool Running => _winner < 0 && !_paused && !_replayDone;

    /// <summary>TICKET-P5-SET-01: raise the desync notice if the session has one.
    /// Polled rather than pushed because NetSession is written by a relay reader
    /// thread, and a background thread may not touch a Godot node.</summary>
    private void RefreshDesyncNotice()
    {
        if (!NetSession.Desynced || _desyncNotice.Visible) return;
        _desyncNotice.Text =
            $"DESYNC AT TICK {NetSession.DesyncTick}\nthis match's players no longer share a world; the result is void";
        _desyncNotice.Visible = true;
        _audio.Play("alert_attack", -4);
    }

    /// <summary>
    /// One simulation tick, exactly as the player experiences it. _Process calls
    /// it from the 15 Hz accumulator and offscreen verification calls it
    /// directly; there is deliberately no second implementation, because a test
    /// that drives a parallel copy of the tick loop proves nothing about the one
    /// that ships.
    /// </summary>
    private void RunOneTick()
    {
        // TICKET-P5-SAVE-01. The bucket a command belongs to is the tick it is
        // applied on, which is the world's tick BEFORE the step - the same
        // convention the runner's replay gate records and replays under.
        int recTick = _world.Tick;
        AutoResumeHarvesters();
        _tickCmds.Clear();
        if (_replay != null)
        {
            // Playback: the stream is the only source of orders. Clicks still
            // select and the camera still flies, so a replay can be watched, but
            // nothing a spectator does reaches the sim.
            _tickCmds.AddRange(_replay.CommandsFor(recTick));
            _pending.Clear();
        }
        else
        {
            _enemy?.Act(_world, _tickCmds);
            _tickCmds.AddRange(_pending);
            _pending.Clear();
            // Record what the AI and the player decided, restamped with the tick
            // they are about to be applied on: the player's commands are all
            // built with tick 0 (nothing in the sim reads Command.Tick, and
            // ComputeStateHash does not hash it), so the field is free for the
            // replay to bucket on and useless for anything else.
            if (_rec != null)
                foreach (var c in _tickCmds)
                    _rec.Record(new Command(recTick, c.PlayerId, c.Type, c.EntityId, c.X, c.Y, c.AuxId, c.Queued));
        }
        // Mission commands are NEVER recorded: the same MissionRunner re-derives
        // them from the same world during playback, and recording them as well
        // would issue every scripted assault twice. They are appended last, so
        // live order and playback order are the same order.
        _tickCmds.AddRange(_missionCmds);
        _missionCmds.Clear();
        // TICKET-P5-SPAWN-02: remember own Deploys so their outcome can be
        // reported after the step. The sim decides a Deploy inside this very
        // Step, so the verdict below is never more than one tick stale.
        foreach (var c in _tickCmds)
            if (c.Type == CommandType.Deploy && c.PlayerId == 0)
                _pendingDeploys[c.EntityId] = recTick;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_tickCmds);
        _world.Step(span);
        _mission?.Tick(_world, _missionCmds);
        SnapshotNow();
        _fog.UpdateFrom(_world, 0);
        if (_winner < 0 && _world.Winner >= 0) OnEliminated(_world.Winner == 0 ? 1 : 0);
        if (_mission != null && _mission.Messages.Count > _seenMessages)
        {
            for (int i = _seenMessages; i < _mission.Messages.Count; i++)
                ShowObjective(_mission.Messages[i]);
            _seenMessages = _mission.Messages.Count;
        }
        // W3-01: resolve attacker ids to sim WeaponIds so effects can
        // pick per-weapon families. Reading _world.Entities after Step
        // is precedented by the rally code below; the sim is not
        // modified.
        _effects.OnTickEvents(_world.Events, _actors, _audio,
            id => id >= 0 && id < _world.EntityCount ? _world.Entities[id].WeaponId : 0);
        foreach (var ev in _world.Events)
        {
            // DEF-08 clause 3: a placed or destroyed barrier changes its
            // neighbours' masks. See RefreshWallMasks for why this is a
            // force rather than the sole trigger.
            if ((ev.Type == GameEventType.StructurePlaced || ev.Type == GameEventType.Died)
                && ev.A >= 0 && ev.A < _world.EntityCount
                && _world.Entities[ev.A].Kind == EntityKind.Wall)
                _wallsDirty = true;
            if (ev.Type == GameEventType.ProductionComplete)
                foreach (var (fid2, frig2) in _rigs)
                    if (frig2.Doors.Count > 0 && _latest.TryGetValue(fid2, out var fv2) && fv2.Kind == EntityKind.Factory)
                    {
                        frig2.DoorTw?.Kill();
                        frig2.DoorTw = _actors[fid2].CreateTween();
                        foreach (var (d, home) in frig2.Doors)
                            frig2.DoorTw.Parallel().TweenProperty(d, "position", home + new Vector3(d.Position.X < 0 ? -0.35f : 0.35f, 0, 0), 0.4f);
                        frig2.DoorTw.TweenInterval(1.4);
                        foreach (var (d, home) in frig2.Doors)
                            frig2.DoorTw.Parallel().TweenProperty(d, "position", home, 0.5f);
                    }
            // ADR-007: the sim owns the rally now. The produced unit already
            // left the factory with its sim-side exit move, so the PathMove
            // this block used to issue is gone with the `_rally` dictionary.
            // The one client-side carry-over: a harvester produced by a
            // rallied factory is still treated as parked (a rally beats
            // auto-harvest, P5-ECON-07 clause 2a - a player who pointed a
            // factory somewhere meant it), keyed off the sim's own HasRally
            // on the producer named in ev.C.
            if (ev.Type == GameEventType.ProductionComplete
                && ev.C >= 0 && ev.C < _world.EntityCount && _world.Entities[ev.C].HasRally
                && ev.A >= 0 && ev.A < _world.EntityCount)
            {
                var nu = _world.Entities[ev.A];
                if (nu.Alive && nu.PlayerId == 0 && nu.Kind == EntityKind.Harvester)
                    _manuallyStopped.Add(ev.A);
            }
            // W3-19: flyout toast for own completions. For CY
            // completions ev.A is the yard and ev.B the structure
            // type; for unit completions ev.A is the new unit (the
            // same convention the rally handler relies on).
            if (ev.Type == GameEventType.ProductionComplete && ev.A >= 0 && ev.A < _world.EntityCount)
            {
                var pe = _world.Entities[ev.A];
                if (pe.PlayerId == 0)
                {
                    // TICKET-P5-SAVE-01: "PLACE >>" is a call to action, and a
                    // spectator has no action to take - the ready slot belongs
                    // to whoever recorded the match. The readout stays (it is
                    // real information); the prompt goes.
                    string msg = pe.Kind == EntityKind.ConstructionYard
                        ? $"{(ev.B > 0 && ev.B < StructNames.Length ? StructNames[ev.B] : "STRUCTURE")} READY"
                            + (_replay is null ? "  -  PLACE >>" : "")
                        : $"{(pe.UnitType > 0 && pe.UnitType < UnitNames.Length ? UnitNames[pe.UnitType] : "UNIT")} DEPLOYED";
                    ShowToast(msg);
                    // TICKET-P6-VO-01: the battlefield voice, ALONGSIDE the
                    // toast, never instead of it (doc 24's rule for every line).
                    PlayVo(pe.Kind == EntityKind.ConstructionYard
                        ? "vo_construction_complete" : "vo_unit_ready");
                }
            }
            // TICKET-P6-MUSIC-01: the combat-intensity signal. Any exchange of
            // fire INVOLVING player 0, as attacker or as target, snaps the
            // signal to full; AfterTicks decays it over ~5 seconds of frame
            // time. Post-Step entity reads, the established precedent.
            if (ev.Type == GameEventType.Fired
                && ((ev.A >= 0 && ev.A < _world.EntityCount && _world.Entities[ev.A].PlayerId == 0)
                 || (ev.B >= 0 && ev.B < _world.EntityCount && _world.Entities[ev.B].PlayerId == 0)))
                _combatIntensity = 1f;
            // TICKET-P6-VO-01: a fallen own mobile speaks once per cooldown
            // window, however many died in it - eleven walls falling is a
            // structure problem and stays with the klaxon, so only units and
            // harvesters are the "unit lost" of the classic genre.
            if (ev.Type == GameEventType.Died && ev.A >= 0 && ev.A < _world.EntityCount)
            {
                var fallen = _world.Entities[ev.A];
                if (fallen.PlayerId == 0 && Mobile(fallen.Kind))
                    PlayVo("vo_unit_lost");
            }
            if (ev.Type == GameEventType.Fired && _latest.TryGetValue(ev.B, out var tgt))
            {
                _aim[ev.A] = (new Vector3((float)tgt.X, 0, (float)tgt.Y), _now + 1.6);
                // W2-02: turret recoil - kick along local backward
                // (+Z rotated by yaw), sprung home over 180 ms.
                if (_rigs.TryGetValue(ev.A, out var frig) && frig.Turret is { } rt)
                {
                    frig.RecoilTw?.Kill();
                    float yaw = rt.Rotation.Y;
                    rt.Position = frig.TurretRest + new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * 0.055f;
                    frig.RecoilTw = rt.CreateTween();
                    frig.RecoilTw.TweenProperty(rt, "position", frig.TurretRest, 0.18f)
                        .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
                }
            }
            if (ev.Type == GameEventType.PlayerEliminated)
                OnEliminated(ev.B);
            // Base under attack: own structure took fire, cooled alert.
            // Harvester under attack is a SECOND alert, not a variant of this
            // one: GDD s7 line 85 and s2 line 19 both name it in its own right,
            // and it was previously excluded here by the same test that skips
            // combat units. That exclusion was the defect. A play-test of
            // skirmish-04 lost a harvester and three squads to nine AI waves
            // and the game never said a word, because the first alert that
            // could fire needed a STRUCTURE to be hit, by which point the army
            // that would have answered was already dead.
            //
            // Combat units stay excluded deliberately. A player is expected to
            // be watching an army they sent somewhere; a harvester is sent away
            // and forgotten, which is exactly why the classic games alert on it.
            if (ev.Type == GameEventType.Fired && ev.B >= 0 && ev.B < _world.EntityCount)
            {
                var target = _world.Entities[ev.B];
                double now = Time.GetTicksMsec() / 1000.0;
                bool ownStructure = target.PlayerId == 0
                    && target.Kind != EntityKind.Unit && target.Kind != EntityKind.Harvester;
                bool ownHarvester = target.PlayerId == 0 && target.Kind == EntityKind.Harvester;
                if (ownStructure && now - _lastAttackAlert > 12.0)
                {
                    _lastAttackAlert = now;
                    _audio.Play("alert_attack", -4);
                    ShowToast("BASE UNDER ATTACK");
                    PlayVo("vo_base_under_attack");   // TICKET-P6-VO-01: with the klaxon, not instead
                    // W3-20: red minimap ping at the struck structure.
                    var basePos = new Vector2((float)(target.X.Raw / 4294967296.0), (float)(target.Y.Raw / 4294967296.0));
                    _minimap.Ping(basePos, new Color(0.85f, 0.25f, 0.2f));
                    RecordAlert(basePos, now);
                }
                else if (ownHarvester && now - _lastHarvesterAlert > 12.0)
                {
                    _lastHarvesterAlert = now;
                    // GDD s7 line 85's "distinct audio", honoured for real now:
                    // alert_harvester is its own synthesised cue (a rising
                    // two-blip motif against the klaxon's falling alternation,
                    // art/audio/synth.py), so the pitch shift that stood in for
                    // it while alert_attack was the only alert asset is gone.
                    _audio.Play("alert_harvester", -4);
                    ShowToast("HARVESTER UNDER ATTACK");
                    PlayVo("vo_harvester_under_attack");   // TICKET-P6-VO-01
                    // Amber rather than the base alert's red: the minimap should
                    // say which of the two alerts fired without the toast.
                    var harvPos = new Vector2((float)(target.X.Raw / 4294967296.0), (float)(target.Y.Raw / 4294967296.0));
                    _minimap.Ping(harvPos, new Color(0.95f, 0.62f, 0.15f));
                    RecordAlert(harvPos, now);
                }
            }
            // W3-20: orange ping where the superweapon lands.
            if (ev.Type == GameEventType.SuperweaponImpact)
                _minimap.Ping(
                    new Vector2((float)(ev.X.Raw / 4294967296.0), (float)(ev.Y.Raw / 4294967296.0)),
                    new Color(0.91f, 0.42f, 0.13f));
            // TICKET-P5-ALERT-02: the DETECTION half of GDD s7 line 85's
            // "superweapon detected/launched". The impact ping above tells the
            // player what already happened; this one fires while the five
            // seconds of warning (World.cs LaunchSuper, StrikeTicks = 75) can
            // still be used. Event semantics where it is raised (World.cs
            // ApplyCommandCore): A is the launching superweapon structure,
            // B is -1, (X, Y) is the AIM point. The event carries no owner,
            // so ownership is read from entity A - the established post-Step
            // read. On fog: the sim emits this event identically to every
            // client (the GameEvent header's own contract), already carrying
            // the launcher id and the aim point, so a launch is global
            // knowledge today - the classic-genre convention for
            // superweapons - and pinging the launch site reveals nothing the
            // event does not already carry. No cooldown: a launch happens at
            // most once per charge cycle, minutes apart.
            if (ev.Type == GameEventType.SuperweaponLaunched
                && ev.A >= 0 && ev.A < _world.EntityCount
                && _world.Entities[ev.A].PlayerId != 0)
            {
                LaunchAlerts++;
                // The hard klaxon, not a new cue: an incoming superweapon is
                // exactly the "drop everything" register alert_attack owns.
                _audio.Play("alert_attack", -4);
                ShowToast("SUPERWEAPON LAUNCH DETECTED");
                PlayVo("vo_superweapon_launch");   // TICKET-P6-VO-01
                var sw = _world.Entities[ev.A];
                // The impact ping's orange: launch and impact are two ends of
                // the one weapon sequence, and the minimap should read them as
                // kin. The ping marks the LAUNCH SITE - where to retaliate -
                // not the aim point; the aim point gets its ping when the
                // strike lands, as it always has.
                var launchPos = new Vector2((float)(sw.X.Raw / 4294967296.0), (float)(sw.Y.Raw / 4294967296.0));
                _minimap.Ping(launchPos, new Color(0.91f, 0.42f, 0.13f));
                RecordAlert(launchPos, Time.GetTicksMsec() / 1000.0);
            }
        }
        // TICKET-P5-SPAWN-02: the sim refuses a Deploy on an obstructed
        // foundation by doing nothing at all (World.cs:874-891 - the rule is
        // right, the silence is not). If the ordered MCV is still a live MCV
        // after the step that applied the order, the deploy was refused and
        // the player is told why. An MCV that is no longer a live MCV either
        // deployed (Deployed consumed it) or died, and neither needs this
        // toast. The watcher arms above on any player-0 Deploy in the applied
        // stream rather than on an issue path, which is why the deploy key and
        // the double-click (TICKET-P5-SPAWN-03) inherited it without wiring.
        if (_pendingDeploys.Count > 0)
        {
            List<int>? decided = null;
            foreach (var (mcv, at) in _pendingDeploys)
            {
                if (_world.Tick <= at) continue;   // verdict tick not reached (never in practice)
                bool stillMcv = mcv >= 0 && mcv < _world.EntityCount
                    && _world.Entities[mcv].Alive
                    && _world.Entities[mcv].Kind == EntityKind.Unit
                    && _world.Entities[mcv].UnitType == McvUnitType;
                if (stillMcv)
                {
                    ShowToast("DEPLOY BLOCKED - CLEAR THE AREA");
                    _audio.Play("ui_click", -12);   // the established denial voice
                }
                (decided ??= new List<int>()).Add(mcv);
            }
            if (decided != null) foreach (int id in decided) _pendingDeploys.Remove(id);
        }
        // TICKET-P5-SAVE-01: playback ends the instant the recorded stream is
        // exhausted, and reports whether it landed on the recorded hash.
        if (_replay != null && !_replayDone && _world.Tick >= _replayTicks) FinishPlayback();
    }

    /// <summary>Per-frame work that is not the sim: interpolated actors, the HUD
    /// readouts, the minimap. It runs whether or not a tick happened this frame
    /// and never mutates the world.</summary>
    private void AfterTicks(double delta)
    {
        _renderTime = _world.Tick - 1 + _accumulator / TickSeconds;
        // TICKET-P6-MUSIC-01: the intensity decays over ~5 seconds of frame
        // time from wherever the last exchange of fire snapped it, and the
        // director smooths whatever this hands it onto the faders.
        _combatIntensity = Mathf.Max(0f, _combatIntensity - (float)delta / CombatDecaySeconds);
        _audio.SetCombatIntensity(_combatIntensity);
        SyncActors((float)delta);
        int supply = 0, draw = 0;
        foreach (var e in _world.Entities)
            if (e.Alive && e.PlayerId == 0) { supply += e.PowerSupply; draw += e.PowerDraw; }
        string pwr = draw > supply ? $"PWR {supply}/{draw} LOW" : $"PWR {supply}/{draw}";
        // TICKET-P5-PWR-01: the low-power alert, on the 75 per cent CROSSING.
        // Same integer test as the sidebar's brown-out colour (Sidebar.cs),
        // so the klaxon and the red bar cannot disagree about the threshold.
        bool brownOut = draw > 0 && supply * 4 < draw * 3;
        if (brownOut && !_wasBrownOut)
        {
            LowPowerAlerts++;
            ShowToast("LOW POWER");
            PlayVo("vo_low_power");   // TICKET-P6-VO-01
            // GDD s7 line 85's "distinct audio": alert_low_power is its own
            // synthesised cue (a sagging descent, the sound of something
            // winding down - art/audio/synth.py), replacing the 0.82 pitch
            // shift of the klaxon that stood in while alert_attack was the
            // only alert asset.
            _audio.Play("alert_low_power", -4);
            // Gold ping at the primary Construction Yard: power is a base
            // problem, and the yard is where the base is. No yard, no ping
            // and no recorded jump position: an alert about nowhere in
            // particular must not overwrite one the player can still fly to.
            int yard = FindOwnStructure(EntityKind.ConstructionYard);
            if (yard >= 0 && _latest.TryGetValue(yard, out var yv))
            {
                var yardPos = new Vector2((float)yv.X, (float)yv.Y);
                _minimap.Ping(yardPos, new Color(0.79f, 0.63f, 0.36f));
                RecordAlert(yardPos, Time.GetTicksMsec() / 1000.0);
            }
        }
        _wasBrownOut = brownOut;
        // TICKET-P5-SAVE-01: the mode is on the status line because two of the
        // three modes change what the player's clicks do, and a replay that
        // silently ignores orders reads as a broken game.
        string mode = _replay != null ? $"    REPLAY {_world.Tick}/{_replayTicks}"
            : _resumed ? "    RESUMED (not recording)" : "";
        _status.Text = $"CREDITS {_world.Credits(0)}    {pwr}    UNITS {CountOwn()}    TICK {_world.Tick}{mode}";

        var dots = new List<(float, float, Color)>();
        foreach (var v in _view)
        {
            if (!v.Alive || v.Kind == EntityKind.FerriteField) continue;
            if (v.PlayerId == 1 && !_world.IsVisible(0, (int)v.X, (int)v.Y)) continue;
            var c = v.PlayerId == 0 ? BattlefieldView.DirectorateMark
                : v.PlayerId == 1 ? BattlefieldView.SodalityMark
                : new Color(0.79f, 0.63f, 0.36f);
            dots.Add(((float)v.X, (float)v.Y, c));
        }
        // W3-20: project the four viewport corners onto the ground for the
        // minimap frustum trapezoid. Pitch is fixed at -50 with a ~37 degree
        // vertical half-FOV, so all four rays hit the ground in practice; the
        // null branch is a safety.
        var vs = GetViewport().GetVisibleRect().Size;
        var corners = new[] { new Vector2(0, 0), new Vector2(vs.X, 0), new Vector2(vs.X, vs.Y), new Vector2(0, vs.Y) };
        Vector2[]? fr = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            if (GroundPoint(corners[i]) is { } g) fr[i] = new Vector2(g.X, g.Z);
            else { fr = null; break; }
        }
        _minimap.Refresh(_fog.FogImage, dots, new Vector2(_cam.Position.X, _cam.Position.Z - _cam.Position.Y * 0.55f), fr);
        _selInfo.Text = _wallDrag ? WallDragSummary() : SelectionSummary();
        SyncRallyMarkers();   // ADR-007: markers mirror the sim's own RallyX/RallyY, selected producer only

        _yardId = FindOwnStructure(EntityKind.ConstructionYard);
        _factoryId = FindOwnStructure(EntityKind.Factory);
        int ready = _yardId >= 0 ? _world.Entities[_yardId].ReadyStructure : 0;
        // W3-15: hand the sidebar the full queue contents plus the head's
        // build fraction (BuildProgress counts percent-ticks, total is
        // BuildTicks * 100). Pure post-Step reads, the rally-code precedent.
        var yardQ = _yardId >= 0 ? _world.QueueContents(_yardId) : System.Array.Empty<int>();
        var facQ = _factoryId >= 0 ? _world.QueueContents(_factoryId) : System.Array.Empty<int>();
        float yardProg = _yardId >= 0 && yardQ.Count > 0
            ? _world.Entities[_yardId].BuildProgress / (_world.GetStructureType(yardQ[0]).BuildTicks * 100f) : 0f;
        float facProg = _factoryId >= 0 && facQ.Count > 0
            ? _world.Entities[_factoryId].BuildProgress / (_world.GetUnitType(facQ[0]).BuildTicks * 100f) : 0f;
        // BD-10 hands the sidebar the supply and draw already tallied above, so
        // the bar and the status line cannot disagree about the same numbers.
        _sidebar.Refresh(_world.Credits(0), ready, _factoryId >= 0, _yardId >= 0, yardQ, facQ, yardProg, facProg,
            supply, draw);

        if (_placingType > 0)
        {
            var gp = GroundPoint(GetViewport().GetMousePosition());
            if (gp is { } p)
            {
                int ax = Mathf.FloorToInt(p.X), ay = Mathf.FloorToInt(p.Z);
                // DEF-08: while dragging a barrier line the run ghosts own the
                // whole line, cursor cell included, so the single ghost stands
                // down rather than double-blending over the run's end.
                if (_wallDrag) UpdateDragGhosts(ax, ay);
                else
                {
                    // DEF-01 clause 7: the +1 here was the 2x2 centre offset
                    // hardcoded. Half the real footprint puts a 1x1 barrier at
                    // its cell centre and leaves every 2x2 exactly where it was.
                    float half = _world.FootprintOf(_placingType) / 2f;
                    _ghost.Position = new Vector3(ax + half, 0.25f, ay + half);
                    // TICKET-P5-SPAWN-01: the tint answers the same question
                    // the commit will ask - geometry AND, for a barrier, the
                    // treasury and the cap - through the one shared predicate.
                    _ghost.MaterialOverride = CanPlace(ax, ay, _placingType)
                        ? GhostValidMat : GhostInvalidMat;
                }
            }
        }
    }

    private static readonly string[] UnitNames = { "", "CANNON TANK", "RIFLE SQUAD", "ROCKET SQUAD", "HARVESTER", "SHADE RAIDER", "SENTINEL SCOUT", "MCV", "HOWITZER", "PHANTOM TANK", "BULWARK TANK", "ENGINEER", "VANGUARD CAR" };
    // W3-19: structure names by type id (Sidebar's table is private).
    // Index is the struct type; 9 is the barrier (ADR-005). A barrier never
    // raises ProductionComplete (BuildTicks 0 keeps it out of the queue), so
    // the entry exists for completeness rather than for a live code path.
    // TICKET-P5-PROD-01 named type 7: the Veil Projector has a button now, so
    // its ready toast must name it.
    private static readonly string[] StructNames = { "", "POWER PLANT", "FACTORY", "REFINERY", "STRUCTURE", "TURRET", "SUPERWEAPON", "VEIL PROJECTOR", "SERVICE DEPOT", "WALL" };

    /// <summary>W3-19: slide-and-fade production toast. A fresh completion
    /// retriggers the animation from the top.</summary>
    private void ShowToast(string msg)
    {
        _toastTween?.Kill();
        _toast.Text = msg;
        _toast.Visible = true;
        _toast.Modulate = new Color(1, 1, 1, 0);
        _toast.OffsetTop = 92;
        _toastTween = _toast.CreateTween();
        _toastTween.TweenProperty(_toast, "modulate:a", 1f, 0.15f);
        _toastTween.Parallel().TweenProperty(_toast, "offset_top", 84f, 0.18f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _toastTween.TweenInterval(2.2);
        _toastTween.TweenProperty(_toast, "modulate:a", 0f, 0.5f);
        _toastTween.TweenCallback(Callable.From(() => _toast.Visible = false));
    }

    /// <summary>TICKET-P5-ALERT-02: every alert site calls this with the map
    /// position it pinged (the minimap's own coordinate space, world X and Z),
    /// so the jump-to-event key always has the most recent alert to fly to.
    /// One helper rather than four open-coded writes, because the invariant -
    /// pinged and recorded are the SAME position - is the whole feature.</summary>
    private void RecordAlert(Vector2 mapPos, double at)
    {
        _lastAlertPos = new Vector3(mapPos.X, 0, mapPos.Y);
        _lastAlertAt = at;
    }

    // ---------------- TICKET-P6-MUSIC-01: the combat-intensity signal ----------------

    private const float CombatDecaySeconds = 5f;
    private float _combatIntensity;
    /// <summary>Verification read: the live signal, not a recomputation.</summary>
    public float CombatIntensity => _combatIntensity;

    // ---------------- TICKET-P6-VO-01: the battlefield voice ----------------

    /// <summary>Per-line cooldowns, seconds. The alert-cooldown idiom
    /// (_lastAttackAlert), one entry per line, so a massacre says "unit lost"
    /// once per window rather than once per casualty and the completion lines
    /// cannot stack into a stutter when two producers finish together. The
    /// launch line carries no cooldown: a launch happens at most once per
    /// charge cycle, minutes apart (the ALERT-02 reasoning).</summary>
    private static readonly Dictionary<string, double> VoCooldownSeconds = new()
    {
        ["vo_construction_complete"] = 1.5,
        ["vo_unit_ready"] = 1.5,
        ["vo_unit_lost"] = 8.0,
        ["vo_base_under_attack"] = 12.0,
        ["vo_harvester_under_attack"] = 12.0,
        ["vo_low_power"] = 5.0,
        ["vo_superweapon_launch"] = 0.0,
        ["vo_mission_accomplished"] = 30.0,
        ["vo_mission_failed"] = 30.0,
    };
    private readonly Dictionary<string, double> _voLastAt = new();
    private readonly Dictionary<string, int> _voPlays = new();

    /// <summary>One battlefield line, on the Ui bus, ALONGSIDE whatever toast
    /// or cue its call site already raises - doc 24's rule is alongside, never
    /// instead, so no existing sound moved to make room for the voice. Wall
    /// time for the window, the _lastAttackAlert precedent, so the cooldown
    /// means the same thing live and under stepped verification. The clips are
    /// placeholder TTS pending the legal-review check recorded in doc 24.</summary>
    private void PlayVo(string name)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_voLastAt.TryGetValue(name, out double last)
            && now - last < VoCooldownSeconds.GetValueOrDefault(name)) return;
        if (!_audio.Has(name)) return;   // asset set absent: stay silent, the toasts still speak
        _voLastAt[name] = now;
        _voPlays[name] = _voPlays.GetValueOrDefault(name) + 1;
        _audio.Play(name, -4);
    }

    /// <summary>Verification read: how many times a line ACTUALLY played, so a
    /// test can prove the cooldown held under a massacre (the LowPowerAlerts
    /// counting pattern).</summary>
    public int VoPlays(string name) => _voPlays.GetValueOrDefault(name);

    // ---------------- TICKET-P6-CURSOR-01: contextual cursors ----------------

    private enum GameCursor { None, Select, Move, Attack, Harvest, Enter, Repair, Sell, Invalid }
    private GameCursor _cursorShown = GameCursor.None;

    /// <summary>Textures and hotspots, loaded once per process: the set is
    /// eight small PNGs shared by every battle this client runs. Hotspot at
    /// the arrow tip for select, at the shape centre for everything else.</summary>
    private static readonly Dictionary<GameCursor, (Texture2D? Tex, Vector2 Hot)> CursorSet = new();
    private static void EnsureCursorsLoaded()
    {
        if (CursorSet.Count > 0) return;
        foreach (var (kind, name, hot) in new (GameCursor, string, Vector2)[]
        {
            (GameCursor.Select, "select", new Vector2(2, 2)),
            (GameCursor.Move, "move", new Vector2(16, 16)),
            (GameCursor.Attack, "attack", new Vector2(16, 16)),
            (GameCursor.Harvest, "harvest", new Vector2(16, 16)),
            (GameCursor.Enter, "enter", new Vector2(16, 16)),
            (GameCursor.Repair, "repair", new Vector2(16, 16)),
            (GameCursor.Sell, "sell", new Vector2(16, 16)),
            (GameCursor.Invalid, "invalid", new Vector2(16, 16)),
        })
        {
            string path = $"res://ui/cursors/cursor_{name}.png";
            CursorSet[kind] = (ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null, hot);
        }
    }

    /// <summary>
    /// The one cursor decision, resolved from state this scene already
    /// computes and mirroring what a click at that point would DO, so the
    /// cursor never promises a verb the click would not deliver. Order: the
    /// two armed modes own the pointer outright; the confirmation windows show
    /// their verb while live; then the hover test runs the exact picks
    /// IssueOrder runs (same radii, same filters). An all-engineer selection
    /// over an enemy structure reads as the capture verb; any combat presence
    /// reads as attack, because IssueOrder sends Attack for every selected
    /// mobile alike.
    /// </summary>
    private GameCursor CursorFor(Vector2 screen)
    {
        if (_placingType > 0)
        {
            if (_wallDrag) return GameCursor.Select;   // the run ghosts carry the per-cell verdict
            if (GroundPoint(screen) is not { } p) return GameCursor.Select;
            return CanPlace(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z), _placingType)
                ? GameCursor.Select : GameCursor.Invalid;
        }
        if (_attackMoveArmed) return GameCursor.Attack;
        if (_now <= _sellConfirmUntil) return GameCursor.Sell;
        if (_now <= _repairConfirmUntil) return GameCursor.Repair;
        bool anyMobile = false, anyHarvester = false, anyEngineer = false, anyCombat = false;
        foreach (int id in _selection)
            if (_latest.TryGetValue(id, out var v) && Mobile(v.Kind))
            {
                anyMobile = true;
                if (v.Kind == EntityKind.Harvester) anyHarvester = true;
                else if (v.UnitType == EngineerUnitType) anyEngineer = true;
                else anyCombat = true;
            }
        if (!anyMobile) return GameCursor.Select;
        int enemy = PickEntity(screen, 0.8f, v => v.PlayerId == 1 && v.Kind != EntityKind.FerriteField);
        if (enemy >= 0)
        {
            if (anyEngineer && !anyCombat && !anyHarvester
                && _latest.TryGetValue(enemy, out var te) && !Mobile(te.Kind))
                return GameCursor.Enter;
            return GameCursor.Attack;
        }
        if (anyHarvester && PickEntity(screen, 1.1f, v => v.Kind == EntityKind.FerriteField) >= 0)
            return GameCursor.Harvest;
        return GameCursor.Move;
    }

    /// <summary>The engineer's catalogue id (com_engineer), named for the same
    /// reason McvUnitType is.</summary>
    private const int EngineerUnitType = 11;

    /// <summary>Resolve and apply. SetCustomMouseCursor is only called when
    /// the resolved cursor CHANGES - it re-uploads the texture to the OS every
    /// call, which is not a per-frame cost. A missing PNG falls back to the
    /// OS default rather than a blank pointer.</summary>
    private void UpdateCursor()
    {
        var want = CursorFor(GetViewport().GetMousePosition());
        if (want == _cursorShown) return;
        _cursorShown = want;
        if (CursorSet.TryGetValue(want, out var c) && c.Tex != null)
            Input.SetCustomMouseCursor(c.Tex, Input.CursorShape.Arrow, c.Hot);
        else
            Input.SetCustomMouseCursor(null);
    }

    // ---- TICKET-P6-CURSOR-01 verification surface: the resolver itself and
    // what is actually applied, never a recomputation.
    public string CursorNameAt(Vector2 screen) => CursorFor(screen).ToString();
    public string CursorShownName => _cursorShown.ToString();
    public bool CursorTextureLoaded(string kindName) =>
        System.Enum.TryParse<GameCursor>(kindName, out var k)
        && CursorSet.TryGetValue(k, out var c) && c.Tex != null;

    // ---- TICKET-P6-MUSIC-01 / TICKET-P6-VO-01 verification surface: the
    // director is private scene furniture, so its reads surface here.
    public bool MusicCalmOn() => _audio.MusicCalmPlaying;
    public float MusicCombatDb() => _audio.MusicCombatVolumeDb;
    public bool AudioHas(string name) => _audio.Has(name);
    public bool VoiceIsPlaying(string name) => _audio.IsVoicePlaying(name);

    /// <summary>DEF-08 clause 8: while drawing, the readout is the running cost
    /// and whether the cap refuses the run - the two numbers that decide
    /// whether to release the button.</summary>
    private string WallDragSummary()
    {
        long cost = _dragRun * (long)_world.GetStructureType(9).Cost;
        int have = OwnCount(EntityKind.Wall);
        string cap = have + _dragRun > World.MaxBarriersPerPlayer
            ? $"   CAP {have}/{World.MaxBarriersPerPlayer} - RUN TRUNCATED"
            : $"   {have + _dragRun}/{World.MaxBarriersPerPlayer}";
        return $"{_dragRun} WALL SEGMENTS   {cost} cr{cap}";
    }

    private string SelectionSummary()
    {
        if (_selection.Count == 0) return "";
        // TICKET-P5-SET-01 rule, applied here too: the readout names the LIVE
        // bindings, never the default letters - hard-coded "R repair  X sell"
        // lies to a player who has rebound either key.
        string kRepair = Settings.KeyName(Settings.BindOf("repair"));
        string kSell = Settings.KeyName(Settings.BindOf("sell"));
        // DEF-09 clause 5: a wall run's readout is the two actions it supports
        // plus the refund, which is the number the player actually needs.
        int walls = 0;
        foreach (int sid in _selection)
            if (_latest.TryGetValue(sid, out var wv) && wv.Kind == EntityKind.Wall) walls++;
        if (walls > 1 && walls == _selection.Count)
        {
            long refund = walls * (long)_world.GetStructureType(9).Cost / 2;
            // TICKET-P5-REP-06: the readout carries the repair DRAIN as well
            // as the sell refund - the drain is the number that decides
            // whether to press repair on a forty-segment run.
            return $"{walls} WALL SEGMENTS   {kRepair} repair {walls * 15} cr/s  {kSell} sell {refund} cr";
        }
        if (_selection.Count == 1)
        {
            foreach (int sid in _selection)
                if (_latest.TryGetValue(sid, out var v))
                {
                    string name = v.Kind == EntityKind.Unit && v.UnitType < UnitNames.Length
                        ? UnitNames[v.UnitType] : v.Kind.ToString().ToUpperInvariant();
                    string hp = v.MaxHp > 0 ? $"  {v.Hp}/{v.MaxHp}" : $"  {v.Hp} HP";
                    // TICKET-P5-REP-04: state the real rate - 2 hp per tick at
                    // 15 Hz is 30 hp/s for 15 cr/s, and the player should not
                    // have to derive that. The stall is PER STRUCTURE, not a
                    // global credit test: World.cs charges in entity-index
                    // order, so on a thin treasury the lowest-index buildings
                    // heal while the rest stall, and a global test would print
                    // REPAIRING on a building that is not being repaired.
                    string rep = "";
                    if (!Mobile(v.Kind) && v.PlayerId == 0
                        && sid >= 0 && sid < _world.EntityCount && _world.Entities[sid].Repairing)
                        rep = RepairStalled(sid) ? "   REPAIR STALLED - NO CREDITS" : "   REPAIRING 15 cr/s";
                    string acts = !Mobile(v.Kind)
                        ? (v.Kind == EntityKind.Factory ? $"   right-click: rally   {kRepair} repair  {kSell} sell" : $"   {kRepair} repair  {kSell} sell")
                        // TICKET-P5-SPAWN-03: the MCV advertises its one
                        // action the way the structures advertise repair,
                        // read from the live binding (the SET-01 rule) so a
                        // rebound key never leaves the readout lying.
                        : v.PlayerId == 0 && v.Kind == EntityKind.Unit && v.UnitType == McvUnitType
                            ? $"   {Settings.KeyName(Settings.BindOf("deploy"))} deploy"
                            : "";
                    return name + hp + rep + acts;
                }
            return "";
        }
        return $"{_selection.Count} SELECTED";
    }

    /// <summary>
    /// TICKET-P5-REP-04: will this repairing structure actually be charged
    /// (and so healed) on the next tick? World.cs pays repairing structures
    /// 1 credit each in entity-index order, so a structure stalls when the
    /// treasury cannot also cover every repairing own structure at a lower
    /// index. Honest caveat: the Service Depot's own per-unit drain
    /// interleaves in the same index order and can make this read optimistic
    /// by a few credits while units sit in a depot aura; exactness would need
    /// the sim to publish its charge ledger, which is not this ticket's to add.
    /// </summary>
    private bool RepairStalled(int id)
    {
        int ahead = 0;
        var ents = _world.Entities;
        for (int i = 0; i < id && i < ents.Count; i++)
        {
            var e = ents[i];
            if (e.Alive && e.PlayerId == 0 && e.Repairing
                && !Mobile(e.Kind) && e.Kind != EntityKind.FerriteField) ahead++;
        }
        return _world.Credits(0) < ahead + 1;
    }

    private int CountOwn()
    {
        int n = 0;
        foreach (var v in _view)
            if (v.Alive && v.PlayerId == 0 && (v.Kind == EntityKind.Unit || v.Kind == EntityKind.Harvester)) n++;
        return n;
    }

    /// <summary>TICKET-P5-BD-14: the marker belonging to one rallied structure, made on demand.</summary>
    private MeshInstance3D RallyMarkerFor(int structureId)
    {
        if (_rallyMarkers.TryGetValue(structureId, out var existing)) return existing;
        var rallyGold = new Color(0.79f, 0.63f, 0.36f);
        var m = new MeshInstance3D
        {
            Name = $"RallyMarker{structureId}",
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.7f },
            Visible = false, // ShowRallyMarkers decides; a rally is drawn only for the selected producer
            MaterialOverride = new StandardMaterial3D
            { AlbedoColor = rallyGold, EmissionEnabled = true, Emission = rallyGold, EmissionEnergyMultiplier = 1.2f },
        };
        AddChild(m);
        _rallyMarkers[structureId] = m;
        return m;
    }

    /// <summary>ADR-007: markers are a THIN MIRROR of sim state. Every frame
    /// this writes each marker's position from the producer's own
    /// RallyX/RallyY (a post-Step read, the established pattern), shows it
    /// only while that producer is selected (the classic rule), and reaps any
    /// marker whose producer is dead or no longer carries a rally - so a
    /// marker can neither drift from the truth nor outlive it. Cheap: one
    /// pass over the snapshot and one over the handful of live markers.</summary>
    private void SyncRallyMarkers()
    {
        var ents = _world.Entities;
        for (int i = 0; i < ents.Count; i++)
        {
            var e = ents[i];
            if (!e.Alive || e.PlayerId != 0 || !e.HasRally) continue;
            var m = RallyMarkerFor(i);
            m.Position = new Vector3(
                (float)(e.RallyX.Raw / 4294967296.0), 0.35f, (float)(e.RallyY.Raw / 4294967296.0));
            m.Visible = _selection.Contains(i);
        }
        List<int>? stale = null;
        foreach (var (sid, _) in _rallyMarkers)
            if (sid < 0 || sid >= ents.Count || !ents[sid].Alive
                || ents[sid].PlayerId != 0 || !ents[sid].HasRally)
                (stale ??= new List<int>()).Add(sid);
        if (stale != null) foreach (int sid in stale) ForgetRally(sid);
    }

    /// <summary>A dead producer's rally marker dies with it, or the
    /// scene accumulates gold pins pointing at nothing (BD-14 clause 4).</summary>
    private void ForgetRally(int structureId)
    {
        if (!_rallyMarkers.Remove(structureId, out var node)) return;
        // QueueFree defers to the end of the frame, so rename first: until the
        // node is actually reaped it is still a child, and anything walking the
        // scene by name would find a marker for a structure that no longer
        // exists. Same reason and same shape as the DmgSmokeGone rename above.
        node.Name = "FreedRallyPin";   // deliberately not a RallyMarker* name
        node.Visible = false;
        node.QueueFree();
    }

    private void OnEliminated(int player)
    {
        _winner = player == 0 ? 1 : 0;
        // TICKET-P5-SAVE-01: the match is over, so the recording is complete.
        // Closing it here rather than at scene exit means the file is on disk
        // and in the browser before the player has finished reading the banner.
        FinishRecording();
        ClosePause();
        _banner.Text = (_winner == 0 ? "VICTORY" : "DEFEAT") + "\n\npress escape for uplink";
        _banner.AddThemeColorOverride("font_color",
            _winner == 0 ? BattlefieldView.DirectorateMark : new Color(0.8f, 0.25f, 0.2f));
        _banner.Visible = true;
        // TICKET-P6-VO-01: the closing line, beside the banner. One site
        // covers skirmish and campaign both: a mission verdict arrives here
        // through World.Winner exactly as an elimination does.
        PlayVo(_winner == 0 ? "vo_mission_accomplished" : "vo_mission_failed");
    }

    private bool _paused;
    private Label _objective = null!;

    /// <summary>Mission trigger messages surface as objective toasts that
    /// fade after a few seconds.</summary>
    private void ShowObjective(string key)
    {
        _objective.Text = $"OBJECTIVE UPDATE: {key.Replace('_', ' ').ToUpperInvariant()}";
        _objective.Visible = true;
        _objective.Modulate = Colors.White;
        var tw = _objective.CreateTween();
        tw.TweenInterval(4.0);
        tw.TweenProperty(_objective, "modulate:a", 0f, 1.2f);
        tw.TweenCallback(Callable.From(() => _objective.Visible = false));
        _audio.Play("production_done", -8);
    }

    private static bool Mobile(EntityKind k) => k is EntityKind.Unit or EntityKind.Harvester;

    /// <summary>
    /// DEF-08: is this STRUCT type a barrier? ADR-005 reserves struct type 9 for
    /// the wall and 10 for the deferred gate, and the sim maps both to
    /// EntityKind.Wall (11). The two numbering spaces are different and the ADR
    /// warns that a mismatch is silent and fatal, so ask the sim's own table
    /// rather than writing 9 in the client.
    /// </summary>
    // BD-06 made the catalogue an instance read, so this is no longer static:
    // the answer belongs to this match's catalogue, not to the compiled one.
    private bool IsBarrier(int structType) =>
        _world.GetStructureType(structType).Kind == EntityKind.Wall;

    /// <summary>
    /// TICKET-P5-SPAWN-01: the one placement truth the ghost tint and the
    /// single-click commit share. ValidPlacement stays the sim's GEOMETRY
    /// predicate (other callers depend on that meaning - the cap and credit
    /// tests do NOT move into it); but a barrier is bought upfront, and the
    /// sim refuses a broke or capped placement at the command (World.cs
    /// PlaceStructure), so a ghost that only answers for geometry glows green
    /// over a click that will do nothing. The drag path already asks the cap
    /// question (UpdateDragGhosts); the single-click path not asking was a
    /// defect, not a choice. Reads GetStructureType, the live catalogue, not
    /// DefaultStructureType.
    /// </summary>
    private bool CanPlace(int ax, int ay, int type)
    {
        if (!_world.ValidPlacement(0, ax, ay, type)) return false;
        if (!IsBarrier(type)) return true;
        return _world.Credits(0) >= _world.GetStructureType(type).Cost
            && OwnCount(EntityKind.Wall) < World.MaxBarriersPerPlayer;
    }

    /// <summary>
    /// DEF-01 clause 8: the selection ring was hardcoded at 1.5, which is the
    /// 2x2 radius; a 1x1 barrier would wear a ring wider than itself. Derive it
    /// from the real footprint (ADR-005 clause 1), which reproduces 1.5 for
    /// every 2x2 exactly. Mobiles already carry a hidden ring from DressMobile,
    /// so in practice this only ever fires for structures.
    /// </summary>
    private void AddSelRingFor(int id)
    {
        if (!_actors.TryGetValue(id, out var n)) return;
        if (n.GetNodeOrNull<MeshInstance3D>("SelRing") != null) return;
        int st = id >= 0 && id < _world.EntityCount ? _world.Entities[id].StructType : 0;
        BattlefieldView.AddSelRing(n, _world.FootprintOf(st) * 0.75f);
    }

    /// <summary>
    /// DEF-08 clause 5, THE LINE RULE: a strictly orthogonal run from start to
    /// current. The dominant axis wins, so there are no diagonals and no
    /// L-bends; the player draws two lines for a corner. Walk order is start to
    /// end inclusive, which is the order the segments are bought in.
    /// </summary>
    private static List<(int X, int Y)> WallRun(int sx, int sy, int cx, int cy)
    {
        var run = new List<(int X, int Y)>();
        int dx = cx - sx, dy = cy - sy;
        if (System.Math.Abs(dx) >= System.Math.Abs(dy))
        {
            int step = dx >= 0 ? 1 : -1;
            for (int x = sx; ; x += step) { run.Add((x, sy)); if (x == cx) break; }
        }
        else
        {
            int step = dy >= 0 ? 1 : -1;
            for (int y = sy; ; y += step) { run.Add((sx, y)); if (y == cy) break; }
        }
        return run;
    }

    private void UpdateDragGhosts(int cx, int cy)
    {
        var run = WallRun(_wallDragStart.X, _wallDragStart.Y, cx, cy);
        // The run rewrites itself every time the cursor crosses a cell, so the
        // ghosts are pooled and hidden rather than freed and rebuilt - the
        // _trackPool recycling precedent (clause 10).
        while (_dragGhosts.Count < run.Count)
        {
            var g = new MeshInstance3D
            {
                Mesh = WallGhostMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(g);
            _dragGhosts.Add(g);
        }
        // Clause 8: the cap is a hard sim rule (ADR-005 clause 5), so the run
        // must read as refused BEFORE the credits leave. Segments past the cap
        // tint red exactly as an illegal cell does.
        int have = OwnCount(EntityKind.Wall);
        for (int i = 0; i < _dragGhosts.Count; i++)
        {
            var g = _dragGhosts[i];
            if (i >= run.Count) { g.Visible = false; continue; }
            var (x, y) = run[i];
            g.Visible = true;
            g.Position = new Vector3(x + 0.5f, 0.25f, y + 0.5f);
            bool ok = _world.ValidPlacement(0, x, y, _placingType)
                      && have + i < World.MaxBarriersPerPlayer;
            g.MaterialOverride = ok ? GhostValidMat : GhostInvalidMat;
        }
        _dragRun = run.Count;
    }

    private void ClearDragGhosts()
    {
        foreach (var g in _dragGhosts) g.QueueFree();
        _dragGhosts.Clear();
        _dragRun = 0;
    }

    private void BeginWallDrag(Vector2 screen)
    {
        if (GroundPoint(screen) is not { } p) return;
        BeginWallDragAtCell(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z));
    }

    /// <summary>DEF-08: drag start; public so scripted verification drives the
    /// same path the mouse does, as PlaceAtCell already does for single
    /// placement.</summary>
    public void BeginWallDragAtCell(int cx, int cy)
    {
        if (_placingType <= 0 || !IsBarrier(_placingType)) return;
        _wallDrag = true;
        _wallDragStart = (cx, cy);
        _ghost.Visible = false;
        UpdateDragGhosts(cx, cy);
    }

    /// <summary>
    /// DEF-08 clause 6: commit the run. Deliberately NOT filtered on
    /// ValidPlacement - the sim validates and rejects each segment on its own
    /// (DEF-04 clause 7), and a client-side filter would be a second opinion
    /// that desyncs from the sim's under lockstep. Returns the segment count.
    /// </summary>
    public int CommitWallDragAtCell(int cx, int cy)
    {
        if (!_wallDrag) return 0;
        var run = WallRun(_wallDragStart.X, _wallDragStart.Y, cx, cy);
        foreach (var (x, y) in run)
            _pending.Add(new Command(0, 0, CommandType.PlaceStructure, _yardId,
                Fix64.FromInt(x), Fix64.FromInt(y), _placingType));
        _wallDrag = false;
        ClearDragGhosts();
        // Clause 7: STAY IN MODE. Draw, draw, draw, Escape.
        _ghost.Visible = true;
        _audio.Play("ui_confirm", -4);
        return run.Count;
    }

    /// <summary>
    /// DEF-08 clause 3: recompute barrier neighbour masks, and rebuild only the
    /// actors whose mask actually moved. Placing one segment changes at most its
    /// four neighbours, so this is cheap; rebuilding 160 masks every frame is
    /// not, and re-instantiating 160 meshes every frame certainly is not.
    ///
    /// DEVIATION from the clause's "set _wallsDirty from the event stream" as
    /// the sole trigger, and the reason is load-bearing: the interpolator runs
    /// eight ticks behind the sim (windowTicks: 8), so a StructurePlaced event
    /// arrives LONG before its segment surfaces in _view. An event-only latch
    /// would be consumed on a view that does not yet contain the wall, and the
    /// mask would then never be recomputed - the new segment and its neighbours
    /// would keep the wrong mesh forever. So the trigger is a signature of the
    /// view's own barrier set. Walls never move, so the set of live wall ids
    /// determines every mask exactly; the signature is integer-only over a list
    /// already in cache, and the expensive part still happens only on change.
    /// The event hook is kept as a belt-and-braces force.
    /// </summary>
    private void RefreshWallMasks()
    {
        int sig = 17, n = 0;
        foreach (var v in _view)
            if (v.Alive && v.Kind == EntityKind.Wall) { sig = sig * 31 + v.Id; n++; }
        if (sig == _wallSig && !_wallsDirty) return;
        _wallSig = sig;
        _wallsDirty = false;
        if (n == 0) { _wallMask.Clear(); return; }
        // Same-owner neighbours only (clause 2), so two players' walls meeting
        // along a border do not fuse into a single run.
        var wallCells = new Dictionary<(int, int), int>();
        foreach (var v in _view)
            if (v.Alive && v.Kind == EntityKind.Wall)
                wallCells[((int)v.X, (int)v.Y)] = v.PlayerId;   // 1x1 sits at cell centre, so floor is the cell
        foreach (var v in _view)
        {
            if (!v.Alive || v.Kind != EntityKind.Wall) continue;
            int cx = (int)v.X, cy = (int)v.Y, mask = 0;
            if (wallCells.TryGetValue((cx, cy - 1), out var pN) && pN == v.PlayerId) mask |= 1;
            if (wallCells.TryGetValue((cx + 1, cy), out var pE) && pE == v.PlayerId) mask |= 2;
            if (wallCells.TryGetValue((cx, cy + 1), out var pS) && pS == v.PlayerId) mask |= 4;
            if (wallCells.TryGetValue((cx - 1, cy), out var pW) && pW == v.PlayerId) mask |= 8;
            if (_wallMask.TryGetValue(v.Id, out int old) && old == mask) continue;
            _wallMask[v.Id] = mask;
            // The mask picks the MESH, so a changed mask is a different model:
            // drop the actor and let the loop below re-instantiate it.
            if (_actors.TryGetValue(v.Id, out var stale))
            {
                stale.QueueFree();
                _actors.Remove(v.Id);
                _rigs.Remove(v.Id);
                _wallRebuilds++;
            }
        }
    }
    private int _wallSig = -1;
    /// <summary>Verification read: how many barrier actors have been rebuilt for
    /// a mask change. Lets a test assert that placing a segment touches only the
    /// neighbours that changed, by count rather than by appearance.</summary>
    public int WallRebuildCount => _wallRebuilds;
    private int _wallRebuilds;

    private void SyncActors(float dt)
    {
        if (!_interp.TrySample(_renderTime, _view)) return;
        var seen = new HashSet<int>();
        _latest.Clear();
        RefreshWallMasks();   // DEF-08: before the actor loop, so a fresh segment instantiates at its final mask
        foreach (var v in _view)
        {
            if (!v.Alive) continue;
            seen.Add(v.Id);
            _latest[v.Id] = v;
            // W4-10: actors ride the ground undulation.
            var pos = new Vector3((float)v.X,
                BattlefieldView.GroundHeight((float)v.X, (float)v.Y), (float)v.Y);
            if (!_actors.TryGetValue(v.Id, out var node))
            {
                // DEF-08 clause 2: a barrier picks one of six meshes by its
                // neighbour mask, so it cannot take the generic one-mesh-per-kind
                // path.
                if (v.Kind == EntityKind.Wall)
                {
                    node = _models.InstantiateWall(_wallMask.GetValueOrDefault(v.Id), out float yawDeg);
                    node.RotationDegrees = new Vector3(0, yawDeg, 0);
                }
                else node = _models.Instantiate((int)v.Kind, v.UnitType);
                node.Position = pos;
                if (Mobile(v.Kind) && v.PlayerId >= 0)
                    BattlefieldView.DressMobile(node, v.PlayerId,
                        v.Kind == EntityKind.Harvester ? 1.7f : 1.15f);
                // Clause 4: 2.6 is sized for a 2x2 building and would pool a
                // shadow far outside a 1x1 segment's own cell.
                else if (v.Kind == EntityKind.Wall)
                    BattlefieldView.AddContactBlob(node, 1.0f);
                else if (v.Kind != EntityKind.FerriteField)
                    BattlefieldView.AddContactBlob(node, 2.6f);
                else
                    // W4-17: emissive amber stain pooled under the deposit;
                    // the per-frame drain scale shrinks it as ore is mined.
                    node.AddChild(new Decal
                    {
                        TextureAlbedo = BattlefieldView.FerriteStainTex(),
                        TextureEmission = BattlefieldView.FerriteStainTex(),
                        EmissionEnergy = 0.6f,
                        Size = new Vector3(3.2f, 1.2f, 3.2f),
                        Position = new Vector3(0, 0.3f, 0),
                        Name = "Stain",
                    });
                AddChild(node);
                _actors[v.Id] = node;
                var newRig = new ActorRig { BobPhase = v.Id * 1.7f };
                ScanRig(node, newRig);
                _rigs[v.Id] = newRig;
                // A rebuilt segment loses its ring child with its old mesh.
                if (v.Kind == EntityKind.Wall && _selection.Contains(v.Id)) AddSelRingFor(v.Id);
                // W2-05: structures placed mid-match rise out of the ground.
                // DEF-08 clause 4 excludes barriers: twenty segments popping out
                // of the ground at once reads as an earthquake, and every mask
                // rebuild would re-pop the neighbours. Walls simply appear.
                if (!Mobile(v.Kind) && v.Kind != EntityKind.FerriteField
                    && v.Kind != EntityKind.Wall && _world.Tick > 10)
                {
                    node.Scale = new Vector3(1f, 0.05f, 1f);
                    var riseTw = node.CreateTween();
                    riseTw.TweenProperty(node, "scale", Vector3.One, 1.1f)
                        .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
                }
            }
            _targets[v.Id] = pos;
            // W3-08: damage-state smoke below 50% hp, denser below 25%;
            // repair above the threshold clears it. Tier changes rebuild the
            // emitter (Amount is not safely runtime-mutable while emitting).
            bool hurt = v.MaxHp > 0 && v.Hp * 2 < v.MaxHp && v.Kind != EntityKind.FerriteField;
            int tier = hurt ? (v.Hp * 4 < v.MaxHp ? 2 : 1) : 0;
            var dmg = node.GetNodeOrNull<GpuParticles3D>("DmgSmoke");
            if (dmg != null && (tier == 0 || dmg.GetMeta("tier").AsInt32() != tier))
            {
                dmg.Name = "DmgSmokeGone"; // QueueFree defers; rename so a rebuild this frame finds nothing
                dmg.QueueFree();
                dmg = null;
            }
            if (tier > 0 && dmg == null && CombatEffects.MakeDamageSmoke(tier) is { } fresh)
            {
                fresh.Name = "DmgSmoke";
                fresh.Position = new Vector3(0, Mobile(v.Kind) ? 0.35f : 0.7f, 0);
                node.AddChild(fresh);
            }
            // TICKET-P5-REP-03: an amber pulsing ground ring on own structures
            // while the sim says they are repairing. Post-Step read of
            // Repairing (the rally precedent); the sim clears the flag itself
            // at full health, which removes the ring here. The ring rides as a
            // child of the actor, so a dying structure sinks with its ring.
            if (!Mobile(v.Kind) && v.Kind != EntityKind.FerriteField
                && v.PlayerId == 0 && v.Id >= 0 && v.Id < _world.EntityCount)
            {
                bool repairing = _world.Entities[v.Id].Repairing;
                var rring = node.GetNodeOrNull<MeshInstance3D>("RepairRing");
                if (repairing && rring == null)
                {
                    // The DEF-01 ring machinery: built at radius 1, so Scale
                    // IS the radius, sized off the live footprint. Rally gold,
                    // not a new colour (doc 16 is a closed palette).
                    float rr = _world.FootprintOf(_world.Entities[v.Id].StructType) * 0.85f;
                    rring = BattlefieldView.MakeRangeRing(1f, new Color(0.79f, 0.63f, 0.36f, 0.6f));
                    rring.Name = "RepairRing";
                    rring.Scale = new Vector3(rr, 1, rr);
                    node.AddChild(rring);
                    var pulse = rring.CreateTween().SetLoops();
                    pulse.TweenProperty(rring, "transparency", 0.65f, 0.45f)
                        .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                    pulse.TweenProperty(rring, "transparency", 0.0f, 0.45f)
                        .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                }
                else if (!repairing && rring != null)
                {
                    rring.Name = "RepairRingGone"; // QueueFree defers; the DmgSmokeGone rename precedent
                    rring.QueueFree();
                }
            }
            // TICKET-P5-REP-07: the depot's heal aura, drawn while selected.
            // Radius is World.DepotRepairRadiusCells - the system constant the
            // sim itself heals by, NOT sight_range, which matches it only by
            // coincidence (com_service_depot.yaml warns of exactly that).
            if (v.Kind == EntityKind.ServiceDepot && v.PlayerId == 0)
            {
                var aura = node.GetNodeOrNull<MeshInstance3D>("DepotAura");
                if (aura == null)
                {
                    aura = BattlefieldView.MakeRangeRing(1f, BattlefieldView.RangeRingOwn);
                    aura.Name = "DepotAura";
                    aura.Scale = new Vector3(World.DepotRepairRadiusCells, 1, World.DepotRepairRadiusCells);
                    aura.Visible = false;
                    node.AddChild(aura);
                }
                aura.Visible = _selection.Contains(v.Id);
            }
            // W3-09: ferrite-gold stream while a harvester loads (post-Step
            // read of HState, same precedent as the rally code).
            if (v.Kind == EntityKind.Harvester)
            {
                bool loading = v.Id < _world.EntityCount && _world.Entities[v.Id].HState == HarvestState.Loading;
                var hfx = node.GetNodeOrNull<GpuParticles3D>("HarvestFx");
                if (loading && hfx == null)
                {
                    hfx = CombatEffects.MakeHarvestFx();
                    hfx.Name = "HarvestFx";
                    // +Z is the intake end after the Blender-Y-forward to
                    // glTF minus-Z conversion.
                    hfx.Position = new Vector3(0, 0.15f, 0.6f);
                    node.AddChild(hfx);
                }
                else if (hfx != null) hfx.Emitting = loading;
            }
            if (v.Kind == EntityKind.FerriteField)
            {
                float g = Mathf.Max(0.2f, v.Hp / 12000f);
                node.Scale = Vector3.One * (0.6f + g * 0.9f);
            }
            if (node.GetNodeOrNull<MeshInstance3D>("SelRing") is { } sel)
                sel.Visible = _selection.Contains(v.Id);
            // Fog: enemies are hidden unless their cell is currently visible
            node.Visible = v.PlayerId != 1 || _world.IsVisible(0, (int)v.X, (int)v.Y);
        }
        foreach (var id in new List<int>(_actors.Keys))
            if (!seen.Contains(id))
            {
                // W2-06: mobile dead become tumbling corpses for a moment;
                // structures sink. Both free themselves via the tween.
                var corpse = _actors[id];
                bool wasMobile = _latest.TryGetValue(id, out var lastV) && Mobile(lastV.Kind);
                var tw = corpse.CreateTween();
                if (wasMobile)
                {
                    var rng = new System.Random(id);
                    var tumble = corpse.Rotation + new Vector3(
                        ((float)rng.NextDouble() - 0.5f) * 1.6f,
                        ((float)rng.NextDouble() - 0.5f) * 2.0f,
                        ((float)rng.NextDouble() - 0.5f) * 1.6f);
                    tw.SetParallel();
                    tw.TweenProperty(corpse, "rotation", tumble, 0.9f)
                        .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
                    tw.TweenProperty(corpse, "position",
                        corpse.Position + new Vector3(0, -0.25f, 0), 0.9f)
                        .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                    tw.SetParallel(false);
                }
                else
                {
                    tw.TweenProperty(corpse, "position",
                        corpse.Position + new Vector3(0, -0.9f, 0), 1.1f)
                        .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                }
                tw.TweenCallback(Callable.From(() => corpse.QueueFree()));
                if (_hpBars.TryGetValue(id, out var hb))
                {
                    hb.Back.QueueFree();
                    hb.Fill.QueueFree();
                    _hpBars.Remove(id);
                }
                // TICKET-P5-VET-01: pips are scene children like the bars, so
                // a corpse must not leave its chevrons hanging in the air.
                if (_rankPips.TryGetValue(id, out var deadPips))
                {
                    deadPips.P1.QueueFree();
                    deadPips.P2.QueueFree();
                    _rankPips.Remove(id);
                }
                _actors.Remove(id); _targets.Remove(id); _selection.Remove(id); _rigs.Remove(id); _aim.Remove(id); _lastTrack.Remove(id);
                ForgetRally(id);              // TICKET-P5-BD-14: no orphan markers
                _manuallyStopped.Remove(id);  // P5-ECON-07: ids are reused by nothing, but the set should not grow forever
            }

        _now += dt;
        foreach (var (id, node) in _actors)
        {
            if (!_targets.TryGetValue(id, out var t)) continue;
            var to = t - node.Position;
            node.Position = node.Position.Lerp(t, dt * 10f);
            // W4-16: tyre-track decals behind vehicles. Infantry (2/3/11,
            // plus the untyped starting squads at 0) leave none. Hidden
            // enemy movement prints nothing (node.Visible carries the
            // IsVisible result computed above).
            if (_latest.TryGetValue(id, out var tk) && Mobile(tk.Kind)
                && (tk.Kind == EntityKind.Harvester
                    || (tk.Kind == EntityKind.Unit && tk.UnitType is not (0 or 2 or 3 or 11))))
            {
                if (!_lastTrack.TryGetValue(id, out var lp))
                    _lastTrack[id] = node.Position;
                else if ((node.Position - lp).Length() > 0.55f)
                {
                    if (node.Visible)
                    {
                        var d = new Decal
                        {
                            TextureAlbedo = BattlefieldView.TrackTex(),
                            Size = new Vector3(0.5f, 1.2f, 0.62f),
                            AlbedoMix = 1.0f,
                            Rotation = new Vector3(0, node.Rotation.Y, 0),
                        };
                        AddChild(d);
                        d.GlobalPosition = (node.Position + lp) * 0.5f + new Vector3(0, 0.3f, 0);
                        var trkTw = d.CreateTween();
                        trkTw.TweenInterval(8.0);
                        trkTw.TweenProperty(d, "modulate:a", 0.0f, 6.0f);
                        trkTw.TweenCallback(Callable.From(d.QueueFree));
                        _trackPool.Enqueue(d);
                        while (_trackPool.Count > 96)
                        {
                            var old = _trackPool.Dequeue();
                            if (IsInstanceValid(old)) old.QueueFree();
                        }
                    }
                    _lastTrack[id] = node.Position;
                }
            }
            // W3-14: billboard health bars above damaged or selected mobiles.
            // TICKET-P5-REP-05 widens the gate to own structures (ferrite
            // fields excluded - a deposit's Hp is its yield, not a wound).
            // Wall bars show only while selected, or a forty-segment run
            // grows forty bars. Structure bars scale their WIDTH off the real
            // footprint and ride higher: the shipped 0.9-wide bar is tuned
            // for mobiles and would float a third-width inside a 2x2 roof.
            if (_latest.TryGetValue(id, out var hv)
                && (Mobile(hv.Kind) || (hv.PlayerId == 0 && hv.Kind != EntityKind.FerriteField)))
            {
                bool show = node.Visible && hv.MaxHp > 0
                    && (hv.Kind == EntityKind.Wall
                        ? _selection.Contains(id)
                        : _selection.Contains(id) || hv.Hp < hv.MaxHp);
                float wide = 1f, lift = 1.15f;
                if (!Mobile(hv.Kind))
                {
                    wide = id >= 0 && id < _world.EntityCount
                        ? _world.FootprintOf(_world.Entities[id].StructType) : World.FootprintSize;
                    // The Mobile ? 0.35 : 0.7 smoke precedent: structures are
                    // taller, so the bar rides higher - except the wall, which
                    // is a knee-high slab and keeps a low bar (its footprint
                    // already gives it the mobile width).
                    lift = hv.Kind == EntityKind.Wall ? 0.9f : 2.0f;
                }
                if (show && !_hpBars.ContainsKey(id))
                {
                    var back = new MeshInstance3D { Mesh = HpBackMesh, MaterialOverride = BackMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    var fill = new MeshInstance3D { Mesh = HpFillMesh, MaterialOverride = FillGreen, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    AddChild(back);
                    AddChild(fill);
                    _hpBars[id] = (back, fill);
                }
                if (_hpBars.TryGetValue(id, out var b))
                {
                    float frac = Mathf.Clamp(hv.Hp / (float)hv.MaxHp, 0f, 1f);
                    b.Back.Visible = show;
                    b.Fill.Visible = show;
                    // The quad anchors its left edge at its node origin
                    // (CenterOffset 0.45), so a footprint-wide bar starts
                    // footprint-half to the left and Scale.X carries both the
                    // width and, on the fill, the health fraction.
                    b.Back.Position = node.Position + new Vector3(-0.45f * wide, lift, 0);
                    b.Fill.Position = node.Position + new Vector3(-0.45f * wide, lift, 0);
                    b.Back.Scale = new Vector3(wide, 1, 1);
                    b.Fill.Scale = new Vector3(Mathf.Max(frac, 0.02f) * wide, 1, 1);
                    b.Fill.MaterialOverride = frac > 0.5f ? FillGreen : frac > 0.25f ? FillAmber : FillRed;
                }
            }
            // TICKET-P5-VET-01: rank pips. The elite self-heal has shipped
            // since P2 (1 hp per 15 ticks, free, no depot, no power), but the
            // client never drew rank, so a player watching one tank heal and
            // another not could not learn the rule. Post-Step read of
            // Entity.Rank; thresholds live in the sim (3 kills, then 6).
            if (_latest.TryGetValue(id, out var pv) && Mobile(pv.Kind)
                && id >= 0 && id < _world.EntityCount && _world.Entities[id].Rank > 0)
            {
                int rank = _world.Entities[id].Rank;
                if (!_rankPips.TryGetValue(id, out var pips))
                {
                    pips = (NewPip(), NewPip());
                    AddChild(pips.P1);
                    AddChild(pips.P2);
                    _rankPips[id] = pips;
                }
                pips.P1.Visible = node.Visible;
                pips.P2.Visible = node.Visible && rank >= 2;
                pips.P1.Position = node.Position + new Vector3(rank >= 2 ? -0.11f : 0f, 1.34f, 0);
                pips.P2.Position = node.Position + new Vector3(0.11f, 1.34f, 0);
            }
            if (_latest.TryGetValue(id, out var v) && Mobile(v.Kind) && to.LengthSquared() > 0.003f)
            {
                float desired = Mathf.Atan2(-to.X, -to.Z);
                var r = node.Rotation;
                r.Y = Mathf.LerpAngle(r.Y, desired, 1f - Mathf.Exp(-6f * dt));
                node.Rotation = r;
            }
            // Turret slew: aim at the last fired-at position (relative to the
            // hull's own yaw), then return to centre once the memory fades.
            if (_rigs.TryGetValue(id, out var rig))
            {
                float speed = to.Length();
                foreach (var w in rig.Wheels)
                    w.RotateObjectLocal(Vector3.Right, speed * dt * 40f + (to.LengthSquared() > 0.003f ? 6f * dt : 0f));
                if (rig.Dish != null)
                    rig.Dish.RotateY(1.2f * dt);
                // W2-04: hull pitch from acceleration, critically damped
                if (_latest.TryGetValue(id, out var mv) && Mobile(mv.Kind))
                {
                    float accel = (speed - rig.PrevSpeed) / Mathf.Max(dt, 0.001f);
                    rig.PrevSpeed = speed;
                    float target = Mathf.Clamp(-accel * 0.03f, -0.10f, 0.10f);
                    rig.Pitch = Mathf.Lerp(rig.Pitch, target, 1f - Mathf.Exp(-8f * dt));
                    var pr = node.Rotation;
                    pr.X = rig.Pitch;
                    node.Rotation = pr;
                    // W2-07: infantry stride bob while moving
                    if (mv.Kind == EntityKind.Unit && mv.UnitType is 2 or 3 or 11 && to.LengthSquared() > 0.003f)
                    {
                        rig.BobPhase += dt * 11f;
                        var bp = node.Position;
                        // W4-10: bob rides on top of the ground undulation.
                        bp.Y = BattlefieldView.GroundHeight(bp.X, bp.Z)
                            + Mathf.Abs(Mathf.Sin(rig.BobPhase)) * 0.035f;
                        node.Position = bp;
                    }
                    // Harvester intake churns while moving
                    if (rig.Intake != null && to.LengthSquared() > 0.003f)
                        rig.Intake.Rotation = rig.IntakeRest + new Vector3(Mathf.Sin((float)_now * 9f) * 0.12f, 0, 0);
                }
            }
            if (_rigs.TryGetValue(id, out var rig2) && rig2.Turret is { } tur)
            {
                float desiredLocal = 0f;
                if (_aim.TryGetValue(id, out var aim) && aim.Until > _now)
                {
                    var d = aim.At - node.Position;
                    if (d.LengthSquared() > 0.01f)
                        desiredLocal = Mathf.AngleDifference(node.Rotation.Y, Mathf.Atan2(-d.X, -d.Z));
                }
                var tr = tur.Rotation;
                tr.Y = Mathf.LerpAngle(tr.Y, desiredLocal, 1f - Mathf.Exp(-9f * dt));
                tur.Rotation = tr;
            }
        }
    }

    // ---------------- input ----------------

    private Vector3? GroundPoint(Vector2 screen)
    {
        var origin = _cam.ProjectRayOrigin(screen);
        var dir = _cam.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 0.0001f) return null;
        float t = -origin.Y / dir.Y;
        if (t < 0) return null;
        return origin + dir * t;
    }

    private int PickEntity(Vector2 screen, float radius, System.Func<SnapshotInterpolator.ViewEntity, bool> filter)
    {
        var gp = GroundPoint(screen);
        if (gp is not { } p) return -1;
        int best = -1; float bestD = radius;
        foreach (var v in _view)
        {
            if (!v.Alive || !filter(v)) continue;
            float d = new Vector2((float)v.X - p.X, (float)v.Y - p.Z).Length();
            if (d < bestD) { bestD = d; best = v.Id; }
        }
        return best;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (_refused) return;   // ADR-006: standing down; nothing here exists
        // TICKET-P5-SET-01: every key this scene answers to is an InputMap
        // action now, so the settings scene can rebind any of them and this
        // method never learns of it. Keys are dispatched before the mouse cases
        // because the two sets are disjoint; the ORDER WITHIN each set is the
        // load-bearing part and is preserved exactly.
        if (ev is InputEventKey { Pressed: true, Echo: false } && HandleKeyAction(ev))
            return;

        switch (ev)
        {
            // DEF-08 clause 5: a barrier draws rather than clicks - press
            // starts the line, release commits it. Everything else places on
            // press exactly as before.
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } pm when _placingType > 0:
                if (IsBarrier(_placingType)) BeginWallDrag(pm.Position);
                else TryPlace(pm.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } wm when _wallDrag:
                if (GroundPoint(wm.Position) is { } wp)
                    CommitWallDragAtCell(Mathf.FloorToInt(wp.X), Mathf.FloorToInt(wp.Z));
                break;
            // TICKET-P5-SET-01: armed attack-move consumes the next left click,
            // and is tested before the drag-select case that would otherwise
            // swallow it. Classic two-step: press the key, pick the ground.
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } am when _attackMoveArmed:
                CommitAttackMove(am.Position);
                break;
            // TICKET-P5-SPAWN-03: double-click an own MCV and it unpacks, the
            // classic idiom. The first click of the pair already selected the
            // vehicle through FinishSelect; this second press issues instead
            // of starting a drag. A double-click on anything else fails the
            // guard and falls through to the case below, behaving exactly as
            // two single clicks always have.
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true } dc
                when _replay == null && PickOwnMcv(dc.Position) is >= 0 and var dmcv:
                IssueDeploy(dmcv);
                _audio.Play("ui_confirm", -8);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed) { _dragging = true; _dragStart = mb.Position; }
                else if (_dragging) { _dragging = false; _dragRect.Visible = false; FinishSelect(mb.Position, Input.IsKeyPressed(Key.Shift)); }
                break;
            case InputEventMouseMotion mm when _dragging:
                var tl = new Vector2(Mathf.Min(_dragStart.X, mm.Position.X), Mathf.Min(_dragStart.Y, mm.Position.Y));
                var br = new Vector2(Mathf.Max(_dragStart.X, mm.Position.X), Mathf.Max(_dragStart.Y, mm.Position.Y));
                _dragRect.Position = tl; _dragRect.Size = br - tl;
                _dragRect.Visible = (br - tl).Length() > 8;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rmb:
                IssueOrder(rmb.Position, Input.IsKeyPressed(Key.Shift));
                break;
        }
    }

    /// <summary>
    /// The keyboard half, dispatched on InputMap actions rather than literal
    /// keycodes. Returns true when the event was consumed.
    ///
    /// ev.IsActionPressed, NOT Input.IsActionJustPressed, and the difference
    /// matters here: IsActionJustPressed answers for the whole FRAME, so two
    /// key events arriving in one frame would both see it true and both fire.
    /// Asking the event whether IT is the action is the engine's idiom for an
    /// input callback and is what the literal keycode matches used to do.
    /// Modifier keys are matched non-exactly on purpose, which is how ctrl+3
    /// still reaches group_3 below and is then told apart from a bare 3.
    /// </summary>
    private bool HandleKeyAction(InputEvent ev)
    {
        if (ev.IsActionPressed("cancel"))
        {
            // TICKET-P5-SAVE-01: the pause menu owns cancel while it is up, and
            // it is tested first - a player who opened it wants out of it, not
            // out of placement mode underneath it.
            if (_pauseMenu != null) { ClosePause(); return true; }
            if (_attackMoveArmed) { DisarmAttackMove("attack-move cancelled"); return true; }
            if (_placingType > 0) { ExitPlacement(); return true; }
            if (_winner >= 0 || _replayDone) { QuitToMenu(); return true; }
            return false;
        }
        if (ev.IsActionPressed("attack_move")) { ArmAttackMove(); return true; }
        if (ev.IsActionPressed("stop")) { IssueStop(); return true; }
        if (ev.IsActionPressed("repair") && _selection.Count > 0)
        {
            HandleRepairKey();
            return true;
        }
        // TICKET-P5-SPAWN-03: the deploy key. Only consumed when it issued
        // something - a bare press with no MCV selected means nothing and is
        // left to whatever else might answer the key after a rebind.
        if (ev.IsActionPressed("deploy") && DeploySelectedMcvs()) return true;
        // TICKET-P5-ALERT-02: the jump-to-event key (GDD s7 line 85). Flies
        // the camera to the most recent alert through the exact minimap-jump
        // idiom (RtsCamera.FlyTo). No alert yet this match: nothing to jump
        // to, so the key is not consumed - the deploy key's rule.
        if (ev.IsActionPressed("jump_to_event"))
        {
            if (_lastAlertAt < 0) return false;
            _cam.FlyTo(_lastAlertPos);
            return true;
        }
        // DEF-09 clause 4: selling 40 walls for 2000 credits on a stray
        // keypress is a match-losing misclick and the sim has no cancel for
        // it, so past 8 structures the first press only asks. Repair carries
        // the same guard now (TICKET-P5-REP-06): it is reversible, but not
        // cheap - at the 80-segment barrier cap (World.MaxBarriersPerPlayer)
        // a full wall-run repair drains 1200 credits per second.
        if (ev.IsActionPressed("sell") && _selection.Count > 0)
        {
            int n = 0;
            foreach (int sid in _selection)
                if (_latest.TryGetValue(sid, out var cv) && !Mobile(cv.Kind)) n++;
            if (n > 8 && _now > _sellConfirmUntil)
            {
                _sellConfirmUntil = _now + 2.0;
                ShowToast($"SELL {n} STRUCTURES?   PRESS {Settings.KeyName(Settings.BindOf("sell"))} AGAIN");
                _audio.Play("ui_click", -8);
                return true;
            }
            _sellConfirmUntil = -1;
            foreach (int sid in _selection)
                if (_latest.TryGetValue(sid, out var xv) && !Mobile(xv.Kind))
                    _pending.Add(new Command(0, 0, CommandType.SellStructure, sid, Fix64.Zero, Fix64.Zero));
            _audio.Play("ui_click", -8);
            return true;
        }
        // TICKET-P5-SAVE-01: this still pauses, but the pause is now a menu -
        // the overlay is the pause indicator the banner used to be, and it
        // is where saving, loading and abandoning live.
        if (ev.IsActionPressed("pause_menu")) { TogglePause(); return true; }
        for (int slot = 0; slot < 9; slot++)
        {
            if (!ev.IsActionPressed($"group_{slot + 1}")) continue;
            if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta))
                _groups[slot] = new HashSet<int>(_selection);          // assign
            else if (_groups.TryGetValue(slot, out var g))
            {
                _selection.Clear();                                    // recall
                foreach (int id in g) if (_latest.ContainsKey(id)) _selection.Add(id);
            }
            return true;
        }
        return false;
    }

    // ---------------- TICKET-P5-REP-01/02/06/08: the repair key ----------------

    /// <summary>
    /// The R key, partitioned into what the sim can actually do rather than
    /// sprayed at the selection (TICKET-P5-REP-01: the old handler played the
    /// confirmation chime whether or not anything could be repaired, which on
    /// three damaged tanks was a confident lie). Structures get the Repair
    /// command - set-on across a multi-select rather than a blind toggle
    /// (REP-08), behind a mass-confirmation guard past 8 (REP-06, the sell
    /// guard's shape). A mobile-only selection goes to SendMobilesToDepot
    /// (REP-02). Mixed: the structures repair, and the toast says how many
    /// damaged units still need a depot.
    /// </summary>
    private void HandleRepairKey()
    {
        if (_replay != null) return;   // a spectator issues no orders
        var structures = new List<int>();
        var mobiles = new List<int>();
        foreach (int sid in _selection)
            if (_latest.TryGetValue(sid, out var rv))
                (Mobile(rv.Kind) ? mobiles : structures).Add(sid);
        if (structures.Count == 0)
        {
            if (mobiles.Count > 0) SendMobilesToDepot(mobiles);
            return;
        }

        // REP-08: read each structure's live Repairing flag. If any is off,
        // this press switches ON exactly those (result: all repairing); only
        // when every one is already repairing does the press switch all off.
        // Same key, same sim command, coherent behaviour, zero sim change.
        var targets = new List<int>();
        foreach (int sid in structures)
            if (sid >= 0 && sid < _world.EntityCount && !_world.Entities[sid].Repairing)
                targets.Add(sid);
        bool switchingOff = targets.Count == 0;
        if (switchingOff) targets = structures;

        // REP-06: past 8 structures being switched ON, the first press only
        // asks - the drain is real money (15 cr/s each; 1200 cr/s at the
        // barrier cap). Switching OFF needs no guard: stopping a drain is
        // exactly what a misclick recovery wants.
        if (!switchingOff && targets.Count > 8 && _now > _repairConfirmUntil)
        {
            _repairConfirmUntil = _now + 2.0;
            ShowToast($"REPAIR {targets.Count} STRUCTURES?  {targets.Count * 15} cr/s   PRESS {Settings.KeyName(Settings.BindOf("repair"))} AGAIN");
            _audio.Play("ui_click", -8);
            return;
        }
        _repairConfirmUntil = -1;
        foreach (int sid in targets)
            _pending.Add(new Command(0, 0, CommandType.Repair, sid, Fix64.Zero, Fix64.Zero));
        // REP-01, the single line that kills the lie: the confirmation chime
        // belongs to the branch that actually did something.
        _audio.Play("ui_confirm", -8);
        // REP-01 mixed rule: say how many damaged units the chime did NOT cover.
        int needDepot = 0;
        foreach (int mid in mobiles)
            if (_latest.TryGetValue(mid, out var mv) && mv.MaxHp > 0 && mv.Hp < mv.MaxHp) needDepot++;
        if (needDepot > 0)
            ShowToast(needDepot == 1
                ? "1 DAMAGED UNIT NEEDS A SERVICE DEPOT"
                : $"{needDepot} DAMAGED UNITS NEED A SERVICE DEPOT");
    }

    /// <summary>
    /// TICKET-P5-REP-02: R on a mobile-only selection sends the damaged ones
    /// to the player's nearest live Service Depot - a point about 2 cells from
    /// the depot centre, comfortably inside the radius-4 aura, and the depot
    /// heals moving units, so no arrival logic is needed. The preconditions
    /// are read BEFORE anything is promised, or this recreates REP-01's sin
    /// one layer out: the depot heals nothing in a brown-out and nothing at
    /// zero credits, and sending units to park beside a dead pump while the
    /// game chirps that it handled it is the exact lie this wave exists to
    /// kill. Sent units join the player-directed set (P5-ECON-07), so
    /// auto-resume leaves them parked until told otherwise.
    /// </summary>
    private void SendMobilesToDepot(List<int> mobiles)
    {
        var damaged = new List<int>();
        foreach (int id in mobiles)
            if (_latest.TryGetValue(id, out var v) && v.MaxHp > 0 && v.Hp < v.MaxHp)
                damaged.Add(id);
        if (damaged.Count == 0)
        {
            // Nothing issued, nothing acknowledged - but silence reads as a
            // dead key, so say why (the P5-ECON-06 denial pattern throughout).
            ShowToast("NO DAMAGE TO REPAIR");
            _audio.Play("ui_click", -12);
            return;
        }
        float cxs = 0, cys = 0;
        foreach (int id in damaged) { var v = _latest[id]; cxs += (float)v.X; cys += (float)v.Y; }
        int depot = NearestOwnDepotTo(new Vector2(cxs / damaged.Count, cys / damaged.Count));
        if (depot < 0)
        {
            ShowToast($"NO SERVICE DEPOT. BUILD ONE ({_world.GetStructureType(8).Cost} cr)");
            _audio.Play("ui_click", -12);
            return;
        }
        int supply = 0, draw = 0;
        foreach (var e in _world.Entities)
            if (e.Alive && e.PlayerId == 0) { supply += e.PowerSupply; draw += e.PowerDraw; }
        if (supply < draw)
        {
            ShowToast("DEPOT OFFLINE: LOW POWER");   // World's depot gate, said out loud
            _audio.Play("ui_click", -12);
            return;
        }
        if (_world.Credits(0) < 1)
        {
            ShowToast("NO CREDITS TO REPAIR");       // World.cs charges per tick; broke heals nothing
            _audio.Play("ui_click", -12);
            return;
        }
        var dp = _latest[depot];
        foreach (int id in damaged)
        {
            var v = _latest[id];
            // WHERE to send them is dictated by two sim arrival rules, both
            // found by this ticket's own offscreen verification rather than by
            // reading. (1) Crowd arrival (World.cs StepToward): a combat unit
            // considers a PathMove complete within 4 CELLS of its destination
            // - the same 4 cells as the depot aura - so a unit aimed at a
            // point 2 cells out settles up to 6 cells from the depot, healed
            // not at all. (2) Arrival contagion (World.cs SeparationSystem):
            // a unit pressing against a stopped unit with the IDENTICAL
            // destination stops too, so a pack sharing one destination parks
            // in layers growing outward past the aura edge. So each combat
            // unit is aimed at its OWN point half a cell past the depot
            // centre along its own approach line, with a per-order jitter so
            // no two orders ever share an exact target: crowd arrival then
            // parks each unit ~3.5 cells out, inside the aura, and contagion
            // never fires. Harvesters have neither rule and would drive into
            // the building, so they keep the exact 2-cells-out point on their
            // own side.
            Fix64 dx, dy;
            var dir = new Vector2((float)(v.X - dp.X), (float)(v.Y - dp.Y));
            dir = dir.LengthSquared() < 0.01f ? new Vector2(1f, 0f) : dir.Normalized();
            if (v.Kind == EntityKind.Unit)
            {
                _depotBay = (_depotBay + 1) % 97;
                float px = (float)dp.X - dir.X * 0.5f + 0.003f * (1 + _depotBay);
                float py = (float)dp.Y - dir.Y * 0.5f;
                dx = Fix64.FromFraction((int)(px * 1000), 1000);
                dy = Fix64.FromFraction((int)(py * 1000), 1000);
            }
            else
            {
                dx = Fix64.FromFraction((int)(((float)dp.X + dir.X * 2f) * 1000), 1000);
                dy = Fix64.FromFraction((int)(((float)dp.Y + dir.Y * 2f) * 1000), 1000);
            }
            _pending.Add(new Command(0, 0, CommandType.PathMove, id, dx, dy));
            _manuallyStopped.Add(id);   // player-directed: auto-resume keeps off
        }
        _effects.OrderMarker(new Vector3((float)dp.X, 0, (float)dp.Y), 0);
        _audio.Play("order_move", -8, AudioDirector.Jitter(0.07f));
        ShowToast(damaged.Count == 1
            ? "1 UNIT TO THE SERVICE DEPOT"
            : $"{damaged.Count} UNITS TO THE SERVICE DEPOT");
    }

    /// <summary>TICKET-P5-REP-02: the nearest live own Service Depot to a
    /// point, by squared distance. A NEW finder on purpose: FindOwnStructure
    /// returns the first depot in view order, which is the wrong one as soon
    /// as there are two.</summary>
    private int NearestOwnDepotTo(Vector2 at)
    {
        int best = -1;
        float bestD = float.MaxValue;
        foreach (var v in _view)
        {
            if (!v.Alive || v.PlayerId != 0 || v.Kind != EntityKind.ServiceDepot) continue;
            float d = new Vector2((float)v.X - at.X, (float)v.Y - at.Y).LengthSquared();
            if (d < bestD) { bestD = d; best = v.Id; }
        }
        return best;
    }

    // ---------------- TICKET-P5-SET-01: attack-move and stop ----------------

    /// <summary>Doc 18 Phase A asked for A attack-move and the client never had
    /// it: AttackMove was reachable from a verification hook and from nothing a
    /// player could press. Two-step by design, as the classics are: arming the
    /// order and then picking its destination, because a one-step
    /// attack-move-to-the-cursor is a different order every time the mouse
    /// moves, and the whole point of the order is choosing where the army
    /// fights.</summary>
    private void ArmAttackMove()
    {
        if (_replay != null) return;               // a spectator issues no orders
        int movers = 0;
        foreach (int id in _selection)
            if (_latest.TryGetValue(id, out var v) && v.Kind == EntityKind.Unit) movers++;
        if (movers == 0) { ShowToast("ATTACK-MOVE NEEDS COMBAT UNITS SELECTED"); return; }
        if (_placingType > 0) ExitPlacement();     // the two modes are exclusive
        _attackMoveArmed = true;
        ShowToast($"ATTACK-MOVE: PICK A DESTINATION   ({movers} UNITS)");
        _audio.Play("ui_click", -10);
    }

    private void DisarmAttackMove(string? toast = null)
    {
        if (!_attackMoveArmed) return;
        _attackMoveArmed = false;
        if (toast != null) ShowToast(toast);
    }

    private void CommitAttackMove(Vector2 screen)
    {
        _attackMoveArmed = false;
        if (GroundPoint(screen) is not { } p) return;
        var cx = Fix64.FromFraction((int)(p.X * 100), 100);
        var cy = Fix64.FromFraction((int)(p.Z * 100), 100);
        int n = 0;
        foreach (int id in _selection)
            if (_latest.TryGetValue(id, out var me) && me.Kind == EntityKind.Unit)
            {
                _pending.Add(new Command(0, 0, CommandType.AttackMove, id, cx, cy));
                n++;
            }
        // W3-17's attack colour: an attack-move is an attack order, and it must
        // not acknowledge in the same gold a plain move does.
        _effects.OrderMarker(new Vector3(p.X, 0, p.Z), 1);
        _audio.Play("order_move", -8, AudioDirector.Jitter(0.07f));
        ShowToast($"ATTACK-MOVE ORDERED   ({n} UNITS)");
    }

    /// <summary>Stop: drop the current order where you stand. Issued to every
    /// mobile in the selection, structures ignored (the sim would too).</summary>
    private void IssueStop()
    {
        if (_replay != null) return;
        DisarmAttackMove();
        int n = 0;
        foreach (int id in _selection)
            if (_latest.TryGetValue(id, out var me) && Mobile(me.Kind))
            {
                _pending.Add(new Command(0, 0, CommandType.Stop, id, Fix64.Zero, Fix64.Zero));
                // P5-ECON-07 clause 2, and the line that keeps this feature
                // quality of life rather than strategy automation (GDD s1): a
                // harvester the player explicitly stopped stays stopped until
                // the player explicitly says otherwise. Doc 22 records that
                // BD-15 read this the other way (its 15-tick poll re-tasks a
                // stopped harvester); P5-ECON-07's reading is the adopted one
                // and the Game Designer may still overrule it.
                if (me.Kind == EntityKind.Harvester) _manuallyStopped.Add(id);
                n++;
            }
        if (n == 0) return;
        _audio.Play("ui_click", -10);
        ShowToast($"STOP   ({n} UNITS)");
    }

    // -------- TICKET-P5-SPAWN-03: the deploy control --------

    /// <summary>
    /// One MCV ordered to unpack, through the same pending path every player
    /// command takes. Born as TICKET-P5-SPAWN-02's verification hook, kept
    /// public as one: the deploy key and the double-click both issue through
    /// it now, so a scripted check and a played match share the exact path.
    /// The sim's clear-area rule gives the verdict; a refusal is reported by
    /// the SPAWN-02 watcher in RunOneTick, so no caller needs to ask twice.
    /// </summary>
    public void IssueDeploy(int mcvId) =>
        _pending.Add(new Command(0, 0, CommandType.Deploy, mcvId, Fix64.Zero, Fix64.Zero));

    /// <summary>The deploy key's handler: every own MCV in the selection is
    /// ordered to unpack; whatever else is selected alongside is left alone,
    /// the HandleRepairKey partition principle. Returns whether anything was
    /// issued, so the caller can decline to consume an empty press.</summary>
    private bool DeploySelectedMcvs()
    {
        if (_replay != null) return false;   // a spectator issues no orders
        int n = 0;
        foreach (int sid in _selection)
            if (_latest.TryGetValue(sid, out var dv) && dv.PlayerId == 0
                && dv.Kind == EntityKind.Unit && dv.UnitType == McvUnitType)
            {
                IssueDeploy(sid);
                n++;
            }
        if (n == 0) return false;
        _audio.Play("ui_confirm", -8);
        return true;
    }

    /// <summary>The own MCV under the cursor, or -1. Same pick radius as the
    /// mobile branch of FinishSelect, so the vehicle that answers a
    /// double-click is exactly the one a single click would select.</summary>
    private int PickOwnMcv(Vector2 at)
        => PickEntity(at, 0.9f, v => v.PlayerId == 0
            && v.Kind == EntityKind.Unit && v.UnitType == McvUnitType);

    private void FinishSelect(Vector2 at, bool add)
    {
        if (!add) _selection.Clear();
        if ((at - _dragStart).Length() <= 8)
        {
            int hit = PickEntity(at, 0.9f, v => v.PlayerId == 0 && Mobile(v.Kind));
            if (hit < 0)
                hit = PickEntity(at, 1.4f, v => v.PlayerId == 0 && !Mobile(v.Kind) && v.Kind != EntityKind.FerriteField);
            if (hit >= 0)
            {
                _selection.Add(hit);
                AddSelRingFor(hit);
                _audio.Play("ui_click", -14, AudioDirector.Jitter(0.05f));   // W3-21
            }
            return;
        }
        var tl = new Vector2(Mathf.Min(_dragStart.X, at.X), Mathf.Min(_dragStart.Y, at.Y));
        var br = new Vector2(Mathf.Max(_dragStart.X, at.X), Mathf.Max(_dragStart.Y, at.Y));
        // DEF-09 clause 1: barriers join the box-select, and BARRIERS ONLY -
        // deliberately not all structures. Box-dragging across your own base
        // and silently catching the Construction Yard in a following X (sell)
        // is a catastrophic misclick with no undo, so every other structure
        // stays click-only.
        // DEF-09 clause 2, the MIXED SELECTION RULE: if a drag captures both
        // mobiles and walls, keep ONLY the mobiles. A drag across the
        // battlefield is an army selection and must never quietly include the
        // fence behind the enemy. This mirrors the single-click precedence
        // above, which tries mobiles first and only falls back to structures.
        var mobiles = new List<int>();
        var walls = new List<int>();
        foreach (var v in _view)
        {
            if (!v.Alive || v.PlayerId != 0) continue;
            if (!Mobile(v.Kind) && v.Kind != EntityKind.Wall) continue;
            var sp = _cam.UnprojectPosition(new Vector3((float)v.X, 0.3f, (float)v.Y));
            if (sp.X < tl.X || sp.X > br.X || sp.Y < tl.Y || sp.Y > br.Y) continue;
            (Mobile(v.Kind) ? mobiles : walls).Add(v.Id);
        }
        foreach (int id in mobiles.Count > 0 ? mobiles : walls) _selection.Add(id);
        // Clause 6: rings for box-selected walls too, not just the click path.
        if (mobiles.Count == 0)
            foreach (int id in walls) AddSelRingFor(id);
    }

    private void TryPlace(Vector2 screen)
    {
        var gp = GroundPoint(screen);
        if (gp is not { } p) return;
        PlaceAtCell(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z));
    }

    /// <summary>Placement commit; public so scripted verification can drive
    /// the same command path the mouse ghost uses.</summary>
    public bool PlaceAtCell(int ax, int ay)
    {
        // TICKET-P5-SPAWN-01: same predicate as the ghost tint, so a red
        // ghost and a refused click are the same answer given twice.
        if (_placingType <= 0 || !CanPlace(ax, ay, _placingType))
        { _audio.Play("ui_click", -12); return false; }
        _pending.Add(new Command(0, 0, CommandType.PlaceStructure, _yardId,
            Fix64.FromInt(ax), Fix64.FromInt(ay), _placingType));
        // DEF-08 clause 7: a barrier STAYS IN MODE - the classic loop is draw,
        // draw, draw, Escape. Everything else consumes its ready slot and so
        // leaves placement, through DEF-01's single teardown.
        if (!IsBarrier(_placingType)) ExitPlacement();
        _audio.Play("ui_confirm", -4);
        return true;
    }

    /// <summary>First valid placement anchor near the yard, scanning outward.
    /// Used by scripted verification and future placement hints.</summary>
    public (int X, int Y)? FindPlacementCell()
    {
        if (_yardId < 0 || !_latest.TryGetValue(_yardId, out var yard)) return null;
        int cx = (int)yard.X - 1, cy = (int)yard.Y - 1;
        for (int r = 2; r <= World.BuildRadius; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    if (_world.ValidPlacement(0, cx + dx, cy + dy)) return (cx + dx, cy + dy);
                }
        return null;
    }

    /// <summary>Verification reads.</summary>
    public Vector3? FindOwnUnitPos(int unitType)
    {
        foreach (var v in _view)
            if (v.Alive && v.PlayerId == 0 && v.Kind == EntityKind.Unit && v.UnitType == unitType)
                return new Vector3((float)v.X, 0, (float)v.Y);
        return null;
    }
    public bool RecoilActive(int unitType)
    {
        foreach (var v in _view)
            if (v.Alive && v.PlayerId == 0 && v.UnitType == unitType
                && _rigs.TryGetValue(v.Id, out var r) && r.Turret is { } t
                && (t.Position - r.TurretRest).Length() > 0.01f)
                return true;
        return false;
    }

    public bool AimActive(int unitType)
    {
        foreach (var v in _view)
            if (v.Alive && v.PlayerId == 0 && v.UnitType == unitType && _aim.TryGetValue(v.Id, out var a) && a.Until > _now)
                return true;
        return false;
    }
    public long CreditsNow => _world.Credits(0);
    public int ReadyAtYard => _yardId >= 0 ? _world.Entities[_yardId].ReadyStructure : 0;

    // ---- Verification surface for DEF-01/08/09, following the PlaceAtCell and
    // SelectAllOwn precedent: scripted checks drive the same code the mouse
    // drives rather than a parallel copy of it. Reads only, plus the two drag
    // entry points the mouse itself calls.
    public IReadOnlyList<Command> PendingCommands => _pending;
    public void ClearPending() => _pending.Clear();
    public int PlacingType => _placingType;
    public bool GhostVisible => _ghost.Visible;
    public bool GhostRingVisible => _ghostRing.Visible;
    public float GhostRingScaleX => _ghostRing.Scale.X;
    public float GhostSizeX => ((BoxMesh)_ghost.Mesh).Size.X;
    public int CoverageRingCount => _coverageRings.Count;
    /// <summary>Coverage rings live as scene children, so count them the way the
    /// acceptance asks: by walking the scene, not by trusting the list.</summary>
    public int CoverageRingNodesInScene()
    {
        int n = 0;
        foreach (var c in GetChildren())
            if (c is MeshInstance3D m && m.Name.ToString().StartsWith("RangeRing")) n++;
        return n;
    }
    public string SelectionText() => SelectionSummary();
    /// <summary>The live HUD readout, so a test reads what the player reads.</summary>
    public string SelInfoText => _selInfo.Text;

    // ---- TICKET-P5-BD-01 surface (BD-14 rally, P5-ECON-06/07 harvesting).
    // Same principle as above: read the shipped state, never a copy of it.
    /// <summary>The live world, for scripted checks that must assert on sim state (HState, Carry) the HUD only paraphrases. Reads and scenario scripting only.</summary>
    public World LiveWorld => _world;
    public int RallyMarkerCount => _rallyMarkers.Count;
    /// <summary>Markers are scene children; count them by walking the scene so an orphan cannot hide in a stale dictionary.</summary>
    public int RallyMarkerNodesInScene()
    {
        int n = 0;
        foreach (var c in GetChildren())
            if (c is MeshInstance3D m && m.Name.ToString().StartsWith("RallyMarker")
                && !m.IsQueuedForDeletion()) n++;
        return n;
    }
    public bool RallyMarkerVisible(int structureId) =>
        _rallyMarkers.TryGetValue(structureId, out var m) && m.Visible;
    /// <summary>ADR-007: answered from the sim's own state, the only rally truth left.</summary>
    public bool HasRally(int structureId) =>
        structureId >= 0 && structureId < _world.EntityCount && _world.Entities[structureId].HasRally;
    public bool RefineryLive => HasLiveRefinery();
    public bool IsParked(int harvesterId) => _manuallyStopped.Contains(harvesterId);
    public int AutoHarvestIssues => _autoHarvestIssues;
    public string ToastText => _toast.Text;
    public bool ToastVisible => _toast.Visible;
    /// <summary>Drive one sim tick exactly as the accumulator does, so a test exercises the shipped loop.</summary>
    public void StepOneTick() { RunOneTick(); AfterTicks(0); }
    public string SidebarStructText(int typeId) => _sidebar.StructButtonText(typeId);
    public bool SidebarStructHasIcon(int typeId) => _sidebar.StructButtonHasIcon(typeId);
    public string SidebarUnitText(int typeId) => _sidebar.UnitButtonText(typeId);
    public string SidebarPowerText => _sidebar.PowerText;
    public float SidebarPowerFillWidth => _sidebar.PowerFillWidth;
    public float SidebarPowerTickX => _sidebar.PowerTickX;
    public Color SidebarPowerFillColour => _sidebar.PowerFillColour;
    public bool SidebarPowerPulsing => _sidebar.PowerPulsing;
    public string SidebarStructHeader => _sidebar.StructHeaderText;
    public int SelectionCount => _selection.Count;
    public void ClearSelection() => _selection.Clear();
    /// <summary>Select exactly one entity, as a single left-click on it would.</summary>
    public void SelectOne(int id) { _selection.Clear(); _selection.Add(id); }
    /// <summary>Add to the selection, as a shift-click on it would.</summary>
    public void SelectAlso(int id) => _selection.Add(id);

    // ---- Wave 1 verification surface (TICKET-P5-REP/VET/SPAWN/PWR/PROD),
    // the established principle: read the shipped state, never a copy of it.
    public int HpBarCount => _hpBars.Count;
    /// <summary>Bars are scene children; count them by walking the scene so an
    /// orphan cannot hide in a stale dictionary (the RallyMarker precedent).
    /// Matched on the shared fill mesh, NOT a name: Godot discards a custom
    /// name on sibling collision (auto-renaming to the class name), so bars
    /// past the first would be invisible to a name walk.</summary>
    public int HpBarNodesInScene()
    {
        int n = 0;
        foreach (var c in GetChildren())
            if (c is MeshInstance3D m && m.Mesh == HpFillMesh && !m.IsQueuedForDeletion()) n++;
        return n;
    }
    public bool HpBarShown(int id) => _hpBars.TryGetValue(id, out var b) && b.Fill.Visible;
    public bool RepairRingOn(int id) =>
        _actors.TryGetValue(id, out var n) && n.GetNodeOrNull<MeshInstance3D>("RepairRing") != null;
    public bool DepotAuraVisible(int id) =>
        _actors.TryGetValue(id, out var n) && n.GetNodeOrNull<MeshInstance3D>("DepotAura") is { Visible: true };
    /// <summary>How many chevrons this entity is wearing right now.</summary>
    public int RankPipsShown(int id) =>
        _rankPips.TryGetValue(id, out var p) ? (p.P2.Visible ? 2 : p.P1.Visible ? 1 : 0) : 0;
    /// <summary>The ghost-and-commit truth for the current placement type.</summary>
    public bool CanPlaceAt(int ax, int ay) => _placingType > 0 && CanPlace(ax, ay, _placingType);
    public bool SidebarStructVisible(int typeId) => _sidebar.StructButtonVisible(typeId);
    /// <summary>TICKET-P6-FACTION-01: the unit-side twin.</summary>
    public bool SidebarUnitVisible(int typeId) => _sidebar.UnitButtonVisible(typeId);
    /// <summary>Drive the S hotkey's handler rather than a copy of it.</summary>
    public void PressStop() => IssueStop();
    public int WallMaskOf(int id) => _wallMask.TryGetValue(id, out int m) ? m : -1;
    public float ActorYaw(int id) => _actors.TryGetValue(id, out var n) ? n.RotationDegrees.Y : float.NaN;
    /// <summary>The live barrier at a cell, or -1. Walls sit at cell centre.</summary>
    public int WallAtCell(int cx, int cy)
    {
        foreach (var v in _view)
            if (v.Alive && v.Kind == EntityKind.Wall && (int)v.X == cx && (int)v.Y == cy) return v.Id;
        return -1;
    }
    /// <summary>Where a world point lands on screen, at the same probe height
    /// FinishSelect's box test uses.</summary>
    public Vector2 ScreenOf(float x, float z) => _cam.UnprojectPosition(new Vector3(x, 0.3f, z));
    public int OwnYardId => _yardId;
    public bool IsSelected(int id) => _selection.Contains(id);
    /// <summary>Park the camera over a point at a chosen height, so a capture can
    /// be framed on the thing under test.</summary>
    public void FocusCameraOn(float x, float z, float height)
    {
        _cam.Snap(new Vector3(x, height, z + height * 0.55f));
    }
    public Vector2 GroundPosOf(int id) =>
        _latest.TryGetValue(id, out var v) ? new Vector2((float)v.X, (float)v.Y) : Vector2.Zero;
    public List<Vector2> OwnMobilePositions()
    {
        var l = new List<Vector2>();
        foreach (var v in _view)
            if (v.Alive && v.PlayerId == 0 && Mobile(v.Kind)) l.Add(new Vector2((float)v.X, (float)v.Y));
        return l;
    }
    /// <summary>Drive the real box-select path from a screen rect.</summary>
    public int BoxSelect(Vector2 from, Vector2 to)
    {
        _dragStart = from;
        FinishSelect(to, false);
        return _selection.Count;
    }
    /// <summary>Drive the real key path, so the sell guard and the repair loop
    /// are tested as the player triggers them. TICKET-P5-SET-01: this now goes
    /// through the InputMap exactly as a real keyboard does - the event carries
    /// a keycode and the actions are defined on keycodes, so a rebind moves
    /// this hook's behaviour too, which is the point of testing through it.</summary>
    public void PressKey(Key k) =>
        _UnhandledInput(new InputEventKey { Keycode = k, Pressed = true, Echo = false });

    /// <summary>TICKET-P5-SET-01: drive a real left click through the real
    /// input path, so armed attack-move is committed the way the mouse commits
    /// it rather than by calling the commit directly.</summary>
    public void PressLeftClick(Vector2 at) =>
        _UnhandledInput(new InputEventMouseButton
        { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });

    public bool AttackMoveArmed => _attackMoveArmed;
    public bool DesyncNoticeVisible => _desyncNotice.Visible;
    public string DesyncNoticeText => _desyncNotice.Text;

    // TICKET-P5-ALERT-02 verification surface: what the alert record holds
    // and where the camera actually is, not a recomputation of either.
    // (ToastText already exists in the readout block above.)
    public bool HasAlert => _lastAlertAt >= 0;
    public Vector3 LastAlertPos => _lastAlertPos;
    public Vector3 CameraPosition => _cam.Position;

    // TICKET-P5-SAVE-01 verification surface. StepTicks drives the SHIPPING
    // tick body, not a copy of it, so an offscreen run and a played match reach
    // the same states by the same code.
    public void StepTicks(int n) { for (int i = 0; i < n && Running; i++) RunOneTick(); }
    public int DebugTick => _world.Tick;
    public ulong DebugStateHash() => _world.ComputeStateHash();
    public string RecordingPath => _recPath;
    public bool IsRecording => _rec != null && !_recDone;
    public bool IsReplay => _replay != null;
    public bool ReplayDone => _replayDone;
    public bool ReplayVerified => _replayVerified;
    public ulong ReplayFinalHash => _replayFinalHash;
    public bool Resumed => _resumed;
    public bool PauseOpen => _pauseMenu != null;
    public MatchSetup Setup => _setup;
    public int FactionOf(int player) => _world.FactionOf(player);
    // ADR-006 verification surface: the live match's catalogue reads and the
    // treasury, so an offscreen run can prove an edited YAML is what the sim
    // charges. Reads, not recomputations.
    public int UnitCostOf(int typeId) => _world.GetUnitType(typeId).Cost;
    public int StructCostOf(int typeId) => _world.GetStructureType(typeId).Cost;
    public long DebugCredits => _world.Credits(0);
    public ulong DebugCatalogueChecksum => _world.CatalogueChecksum;
    public int ReadyStructureTypeForTest
    {
        get
        {
            foreach (var e in _world.Entities)
                if (e.Alive && e.PlayerId == 0 && e.Kind == EntityKind.ConstructionYard && e.ReadyStructure > 0)
                    return e.ReadyStructure;
            return 0;
        }
    }
    public Sidebar SidebarView => _sidebar;
    public int OwnCount(EntityKind kind)
    {
        int n = 0;
        foreach (var v in _view) if (v.Alive && v.PlayerId == 0 && v.Kind == kind) n++;
        return n;
    }

    /// <summary>Right-click order resolution; public so scripted verification
    /// can drive the exact input path a player uses.</summary>
    public void IssueOrder(Vector2 screen, bool queued)
    {
        if (_selection.Count == 0) return;
        var gp = GroundPoint(screen);
        if (gp is not { } p) return;
        // Factory selected: right-click sets its rally point. ADR-007: the
        // rally is a sim command on the ordinary path now (which closes
        // SPAWN-D9: whatever player issues it, the stream carries it), not a
        // client dictionary; fresh units leave the factory with a sim-side
        // exit move toward it.
        bool onlyStructures = true;
        foreach (int sid in _selection)
            if (_latest.TryGetValue(sid, out var sv) && Mobile(sv.Kind)) { onlyStructures = false; break; }
        if (onlyStructures)
        {
            foreach (int sid in _selection)
                if (_latest.TryGetValue(sid, out var sv) && Ralliable(sv.Kind) && sv.PlayerId == 0)
                {
                    _pending.Add(new Command(0, 0, CommandType.SetRally, sid,
                        Fix64.FromFraction((int)(p.X * 100), 100),
                        Fix64.FromFraction((int)(p.Z * 100), 100), 0)); // AuxId 0 = set; -1 clears
                    // W3-17: rally clicks acknowledge in gold. The marker
                    // itself appears next tick, from the sim's own fields.
                    _effects.OrderMarker(new Vector3(p.X, 0, p.Z), 2);
                    _audio.Play("ui_confirm", -8);
                }
            return;
        }
        var cx = Fix64.FromFraction((int)(p.X * 100), 100);
        var cy = Fix64.FromFraction((int)(p.Z * 100), 100);
        int enemy = PickEntity(screen, 0.8f, v => v.PlayerId == 1 && v.Kind != EntityKind.FerriteField);
        int field = PickEntity(screen, 1.1f, v => v.Kind == EntityKind.FerriteField);
        // P5-ECON-06: computed ONCE for the whole click, not per selected unit,
        // and the answer decides whether the order is sent at all.
        bool hasRef = HasLiveRefinery();
        bool deniedHarvest = false;
        int issued = 0;
        foreach (int id in _selection)
        {
            if (!_latest.TryGetValue(id, out var me)) continue;
            if (enemy >= 0)
                _pending.Add(new Command(0, 0, CommandType.Attack, id, cx, cy, enemy, queued));
            else if (field >= 0 && me.Kind == EntityKind.Harvester)
            {
                // P5-ECON-06: without a refinery this command is a silent no-op
                // that also mutates FieldId for nothing. Do not send it.
                if (!hasRef) { deniedHarvest = true; continue; }
                _pending.Add(new Command(0, 0, CommandType.Harvest, id, cx, cy, field, queued));
                // P5-ECON-07 clause 2: an explicit Harvest is the one order that
                // un-parks a harvester the player had parked.
                _manuallyStopped.Remove(id);
            }
            else if (Mobile(me.Kind))
            {
                _pending.Add(new Command(0, 0, CommandType.PathMove, id, cx, cy, -1, queued));
                // P5-ECON-07 clause 2: a bare move on a harvester is the player
                // saying where it should be. Auto-harvest must not overrule that.
                if (me.Kind == EntityKind.Harvester) _manuallyStopped.Add(id);
            }
            else continue;
            issued++;
        }
        if (deniedHarvest)
        {
            // The established denial pattern: no ui_deny asset exists, and an
            // invalid structure placement already speaks with ui_click at -12.
            ShowToast("NO REFINERY - BUILD ONE FIRST");
            _audio.Play("ui_click", -12);
        }
        // A click that queued nothing gets no acknowledgement. P5-ECON-06 clause
        // 4 only suppresses the gold harvest marker, which would leave a denied
        // harvest drawing the MOVE ring and playing the move sound instead: the
        // same lie in a different colour. Nothing issued, nothing acknowledged.
        if (issued == 0) return;
        // W3-17: contracting acknowledgement ring at the order point, colour
        // coded by order type (attack rings sit on the target itself).
        int mk = enemy >= 0 ? 1 : (field >= 0 && hasRef ? 2 : 0);
        Vector3 mpos = enemy >= 0 && _latest.TryGetValue(enemy, out var ev2)
            ? new Vector3((float)ev2.X, 0, (float)ev2.Y)
            : new Vector3(p.X, 0, p.Z);
        _effects.OrderMarker(mpos, mk);
        _audio.Play("order_move", -8, AudioDirector.Jitter(0.07f));
    }

    /// <summary>Programmatic hooks for offscreen verification: select all own
    /// mobile units, then order them to a map point through the same command
    /// path the mouse uses.</summary>
    public int SelectAllOwn()
    {
        _selection.Clear();
        foreach (var v in _view)
            if (v.Alive && v.PlayerId == 0 && Mobile(v.Kind)) _selection.Add(v.Id);
        return _selection.Count;
    }

    public void OrderMoveTo(float x, float z, bool queued = false)
    {
        var cx = Fix64.FromFraction((int)(x * 100), 100);
        var cy = Fix64.FromFraction((int)(z * 100), 100);
        foreach (int id in _selection)
            if (_latest.TryGetValue(id, out var me) && Mobile(me.Kind))
                _pending.Add(new Command(0, 0, CommandType.PathMove, id, cx, cy, -1, queued));
    }

    public void OrderAttackMoveTo(float x, float z)
    {
        var cx = Fix64.FromFraction((int)(x * 100), 100);
        var cy = Fix64.FromFraction((int)(z * 100), 100);
        foreach (int id in _selection)
            if (_latest.TryGetValue(id, out var me) && me.Kind == EntityKind.Unit)
                _pending.Add(new Command(0, 0, CommandType.AttackMove, id, cx, cy));
    }
}
