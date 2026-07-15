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
/// shift adds to selection and queues orders; WASD/edge/wheel camera.
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
    private int _mapW, _mapH;

    // Structure interaction: rally points per factory, selection readout
    private readonly Dictionary<int, Vector3> _rally = new();
    private MeshInstance3D _rallyMarker = null!;
    private Label _selInfo = null!;

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
        _models = new ModelLibrary();
        AddChild(_models);

        _setup = MatchConfig.CurrentSetup();
        MapData map = MapData.Load(GameFiles.Abs(_setup.MapPath));
        _world = BuildStartingWorld(_setup, map, out var tags);
        if (_setup.IsMission) _mission = new MissionRunner(map, tags);
        else _enemy = _setup.AiPreset switch
        {
            1 => SkirmishAI.Rusher(1),
            2 => SkirmishAI.Turtle(1),
            _ => SkirmishAI.Standard(1),
        };

        // TICKET-P5-SAVE-01: a scene is one of three things, decided here once.
        if (MatchConfig.LoadPath is { } loadPath) ResumeFromSave(loadPath, map);
        else if (MatchConfig.ReplayPath is { } replayPath) BeginPlayback(replayPath);
        else BeginRecording();
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

        var rallyGold = new Color(0.79f, 0.63f, 0.36f);
        _rallyMarker = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.7f },
            Visible = false,
            MaterialOverride = new StandardMaterial3D
            { AlbedoColor = rallyGold, EmissionEnabled = true, Emission = rallyGold, EmissionEnergyMultiplier = 1.2f },
        };
        AddChild(_rallyMarker);

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
            // mirrors the runner's gated scenarios.
            var m = map.BuildWorld(setup.Seed, players: 2, out tags);
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
        var w = map.BuildWorld(setup.Seed, players: 2, out tags);
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

    /// <summary>Replace the freshly built world with a saved one. The fresh
    /// build above is not waste: it is what produces the mission tags, which a
    /// save deliberately does not store (MissionRunner's own note - the mission
    /// is rebuilt from the same map, then its state is restored on top).</summary>
    private void ResumeFromSave(string path, MapData map)
    {
        // Read the whole file into memory first and drive both readers off the
        // one stream, which is the exact shape the campaignsave gate proves.
        var ms = new MemoryStream(File.ReadAllBytes(path));
        _world = World.Load(ms);
        // SIM DEFECT WORKAROUND, and it is a real one, found by this ticket's
        // own verification rather than by reading: ComputeStateHash hashes
        // _playerFaction (World.cs:1776) but World.Save never writes it, so
        // World.Load returns a world where every player is Directorate again
        // ("everyone Directorate until told otherwise", World.cs:216). It is not
        // cosmetic - faction gates what a player may build (World.cs:745, :789),
        // so a loaded save would let a side build the wrong faction's hardware.
        // Both sim gates miss it because skirmish-01.fmap and mission-01.fmap
        // declare no factions at all, so their worlds are all-Directorate and
        // the dropped field happens to round-trip as 0.
        // The honest client-side repair is to re-apply the faction from the map,
        // exactly as the mission tags are rebuilt from the map rather than
        // stored (MissionRunner's own note). This is sound ONLY because a
        // faction is map content: SetFaction is called from exactly one place in
        // the whole sim, MapLoader.BuildWorld, and nothing mutates it mid-match.
        // If the sim ever lets a player change faction during a game, this stops
        // being correct and the field must go into the save format instead.
        // Reported for sim-engineer rather than patched here: the save format is
        // not this ticket's to change, and every non-client caller of
        // World.Save/Load is still affected.
        foreach (var (p, f) in map.Factions) _world.SetFaction(p, f);
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
        _replayTicks = MatchConfig.ReplayTicks;
        _enemy = null;   // the recorded stream already carries the AI's decisions
    }

    private void BeginRecording()
    {
        _rec = new ReplayWriter(_setup.Seed, _setup.MapName);
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
    /// than none.</summary>
    public override void _ExitTree() => FinishRecording();

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
        float f = World.FootprintOf(structType);
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

        var hint = new Label
        {
            // TICKET-P5-SAVE-01: a replay takes no orders, so it must not advertise
            // orders. Telling a spectator to right-click to attack, and then
            // swallowing the click, is the exact "silently ignores orders" read
            // that makes a working replay look like a broken game.
            Text = _replay != null
                ? "REPLAY PLAYBACK   the recorded stream issues every order   click: select   wasd/edge/wheel camera   escape: uplink"
                : "click: select unit or building   right click: move / attack / harvest / rally   R repair  X sell  P menu (save/load)   ctrl+1-9 groups   wasd/edge/wheel camera",
            AnchorTop = 1, AnchorBottom = 1, AnchorRight = 1,
            OffsetTop = -30, OffsetLeft = 16,
        };
        hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.48f));
        hud.AddChild(hint);

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
        _sidebar.Init(this);
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
        BattlefieldView.TickWater(delta);
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
            if (ev.Type == GameEventType.ProductionComplete && _rally.Count > 0
                && ev.A >= 0 && ev.A < _world.EntityCount)
            {
                var nu = _world.Entities[ev.A];
                if (nu.Alive && nu.PlayerId == 0 && nu.Kind is EntityKind.Unit or EntityKind.Harvester)
                    foreach (var (fid, rp) in _rally)
                        if (_latest.TryGetValue(fid, out var fv)
                            && System.Math.Abs(fv.X - (double)(nu.X.Raw / 4294967296.0)) < 4
                            && System.Math.Abs(fv.Y - (double)(nu.Y.Raw / 4294967296.0)) < 4)
                        {
                            _pending.Add(new Command(0, 0, CommandType.PathMove, ev.A,
                                Fix64.FromFraction((int)(rp.X * 100), 100),
                                Fix64.FromFraction((int)(rp.Z * 100), 100)));
                            break;
                        }
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
                }
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
            // Base under attack: own structure took fire, cooled alert
            if (ev.Type == GameEventType.Fired && ev.B >= 0 && ev.B < _world.EntityCount)
            {
                var target = _world.Entities[ev.B];
                if (target.PlayerId == 0 && target.Kind != EntityKind.Unit && target.Kind != EntityKind.Harvester
                    && Time.GetTicksMsec() / 1000.0 - _lastAttackAlert > 12.0)
                {
                    _lastAttackAlert = Time.GetTicksMsec() / 1000.0;
                    _audio.Play("alert_attack", -4);
                    // W3-20: red minimap ping at the struck structure.
                    _minimap.Ping(
                        new Vector2((float)(target.X.Raw / 4294967296.0), (float)(target.Y.Raw / 4294967296.0)),
                        new Color(0.85f, 0.25f, 0.2f));
                }
            }
            // W3-20: orange ping where the superweapon lands.
            if (ev.Type == GameEventType.SuperweaponImpact)
                _minimap.Ping(
                    new Vector2((float)(ev.X.Raw / 4294967296.0), (float)(ev.Y.Raw / 4294967296.0)),
                    new Color(0.91f, 0.42f, 0.13f));
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
        SyncActors((float)delta);
        int supply = 0, draw = 0;
        foreach (var e in _world.Entities)
            if (e.Alive && e.PlayerId == 0) { supply += e.PowerSupply; draw += e.PowerDraw; }
        string pwr = draw > supply ? $"PWR {supply}/{draw} LOW" : $"PWR {supply}/{draw}";
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

        _yardId = FindOwnStructure(EntityKind.ConstructionYard);
        _factoryId = FindOwnStructure(EntityKind.Factory);
        int ready = _yardId >= 0 ? _world.Entities[_yardId].ReadyStructure : 0;
        // W3-15: hand the sidebar the full queue contents plus the head's
        // build fraction (BuildProgress counts percent-ticks, total is
        // BuildTicks * 100). Pure post-Step reads, the rally-code precedent.
        var yardQ = _yardId >= 0 ? _world.QueueContents(_yardId) : System.Array.Empty<int>();
        var facQ = _factoryId >= 0 ? _world.QueueContents(_factoryId) : System.Array.Empty<int>();
        float yardProg = _yardId >= 0 && yardQ.Count > 0
            ? _world.Entities[_yardId].BuildProgress / (World.GetStructureType(yardQ[0]).BuildTicks * 100f) : 0f;
        float facProg = _factoryId >= 0 && facQ.Count > 0
            ? _world.Entities[_factoryId].BuildProgress / (_world.GetUnitType(facQ[0]).BuildTicks * 100f) : 0f;
        _sidebar.Refresh(_world.Credits(0), ready, _factoryId >= 0, _yardId >= 0, yardQ, facQ, yardProg, facProg);

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
                    float half = World.FootprintOf(_placingType) / 2f;
                    _ghost.Position = new Vector3(ax + half, 0.25f, ay + half);
                    // Passing the type makes the tint agree with the sim's own
                    // footprint test; for types 1-8 FootprintOf is 2, which is
                    // what the defaulted call already meant.
                    _ghost.MaterialOverride = _world.ValidPlacement(0, ax, ay, _placingType)
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
    private static readonly string[] StructNames = { "", "POWER PLANT", "FACTORY", "REFINERY", "STRUCTURE", "TURRET", "SUPERWEAPON", "STRUCTURE", "SERVICE DEPOT", "WALL" };

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

    /// <summary>DEF-08 clause 8: while drawing, the readout is the running cost
    /// and whether the cap refuses the run - the two numbers that decide
    /// whether to release the button.</summary>
    private string WallDragSummary()
    {
        long cost = _dragRun * (long)World.GetStructureType(9).Cost;
        int have = OwnCount(EntityKind.Wall);
        string cap = have + _dragRun > World.MaxBarriersPerPlayer
            ? $"   CAP {have}/{World.MaxBarriersPerPlayer} - RUN TRUNCATED"
            : $"   {have + _dragRun}/{World.MaxBarriersPerPlayer}";
        return $"{_dragRun} WALL SEGMENTS   {cost} cr{cap}";
    }

    private string SelectionSummary()
    {
        if (_selection.Count == 0) return "";
        // DEF-09 clause 5: a wall run's readout is the two actions it supports
        // plus the refund, which is the number the player actually needs.
        int walls = 0;
        foreach (int sid in _selection)
            if (_latest.TryGetValue(sid, out var wv) && wv.Kind == EntityKind.Wall) walls++;
        if (walls > 1 && walls == _selection.Count)
        {
            long refund = walls * (long)World.GetStructureType(9).Cost / 2;
            return $"{walls} WALL SEGMENTS   R repair  X sell   {refund} cr";
        }
        if (_selection.Count == 1)
        {
            foreach (int sid in _selection)
                if (_latest.TryGetValue(sid, out var v))
                {
                    string name = v.Kind == EntityKind.Unit && v.UnitType < UnitNames.Length
                        ? UnitNames[v.UnitType] : v.Kind.ToString().ToUpperInvariant();
                    string hp = v.MaxHp > 0 ? $"  {v.Hp}/{v.MaxHp}" : $"  {v.Hp} HP";
                    string acts = !Mobile(v.Kind)
                        ? (v.Kind == EntityKind.Factory ? "   right-click: rally   R repair  X sell" : "   R repair  X sell")
                        : "";
                    return name + hp + acts;
                }
            return "";
        }
        return $"{_selection.Count} SELECTED";
    }

    private int CountOwn()
    {
        int n = 0;
        foreach (var v in _view)
            if (v.Alive && v.PlayerId == 0 && (v.Kind == EntityKind.Unit || v.Kind == EntityKind.Harvester)) n++;
        return n;
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
    private static bool IsBarrier(int structType) =>
        World.GetStructureType(structType).Kind == EntityKind.Wall;

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
        BattlefieldView.AddSelRing(n, World.FootprintOf(st) * 0.75f);
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
                _actors.Remove(id); _targets.Remove(id); _selection.Remove(id); _rigs.Remove(id); _aim.Remove(id); _lastTrack.Remove(id);
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
            if (_latest.TryGetValue(id, out var hv) && Mobile(hv.Kind))
            {
                bool show = node.Visible && hv.MaxHp > 0 && (_selection.Contains(id) || hv.Hp < hv.MaxHp);
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
                    b.Back.Position = node.Position + new Vector3(-0.45f, 1.15f, 0);
                    b.Fill.Position = node.Position + new Vector3(-0.45f, 1.15f, 0);
                    b.Fill.Scale = new Vector3(Mathf.Max(frac, 0.02f), 1, 1);
                    b.Fill.MaterialOverride = frac > 0.5f ? FillGreen : frac > 0.25f ? FillAmber : FillRed;
                }
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
            // TICKET-P5-SAVE-01: the pause menu owns Escape while it is up, and
            // it is tested first - a player who opened it wants out of it, not
            // out of placement mode underneath it.
            case InputEventKey { Keycode: Key.Escape, Pressed: true } when _pauseMenu != null:
                ClosePause();
                break;
            case InputEventKey { Keycode: Key.Escape, Pressed: true } when _placingType > 0:
                ExitPlacement();
                break;
            case InputEventKey { Keycode: Key.Escape, Pressed: true } when _winner >= 0 || _replayDone:
                QuitToMenu();
                break;
            case InputEventKey { Keycode: Key.R, Pressed: true, Echo: false } when _selection.Count > 0:
                foreach (int sid in _selection)
                    if (_latest.TryGetValue(sid, out var rv) && !Mobile(rv.Kind))
                        _pending.Add(new Command(0, 0, CommandType.Repair, sid, Fix64.Zero, Fix64.Zero));
                _audio.Play("ui_confirm", -8);
                break;
            // DEF-09 clause 4: selling 40 walls for 2000 credits on a stray
            // keypress is a match-losing misclick and the sim has no cancel for
            // it, so past 8 structures the first X only asks. R needs no such
            // guard: repair is reversible and cheap.
            case InputEventKey { Keycode: Key.X, Pressed: true, Echo: false } when _selection.Count > 0:
            {
                int n = 0;
                foreach (int sid in _selection)
                    if (_latest.TryGetValue(sid, out var cv) && !Mobile(cv.Kind)) n++;
                if (n > 8 && _now > _sellConfirmUntil)
                {
                    _sellConfirmUntil = _now + 2.0;
                    ShowToast($"SELL {n} STRUCTURES?   PRESS X AGAIN");
                    _audio.Play("ui_click", -8);
                    break;
                }
                _sellConfirmUntil = -1;
                foreach (int sid in _selection)
                    if (_latest.TryGetValue(sid, out var xv) && !Mobile(xv.Kind))
                        _pending.Add(new Command(0, 0, CommandType.SellStructure, sid, Fix64.Zero, Fix64.Zero));
                _audio.Play("ui_click", -8);
                break;
            }
            // TICKET-P5-SAVE-01: P still pauses, but the pause is now a menu -
            // the overlay is the pause indicator the banner used to be, and it
            // is where saving, loading and abandoning live.
            case InputEventKey { Keycode: Key.P, Pressed: true, Echo: false }:
                TogglePause();
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
            case InputEventKey { Pressed: true, Echo: false } key when key.Keycode is >= Key.Key1 and <= Key.Key9:
                int slot = (int)key.Keycode - (int)Key.Key1;
                if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta))
                    _groups[slot] = new HashSet<int>(_selection);          // assign
                else if (_groups.TryGetValue(slot, out var g))
                {
                    _selection.Clear();                                    // recall
                    foreach (int id in g) if (_latest.ContainsKey(id)) _selection.Add(id);
                }
                break;
        }
    }

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
        if (_placingType <= 0 || !_world.ValidPlacement(0, ax, ay, _placingType))
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
    public int SelectionCount => _selection.Count;
    public void ClearSelection() => _selection.Clear();
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
    /// are tested as the player triggers them.</summary>
    public void PressKey(Key k) =>
        _UnhandledInput(new InputEventKey { Keycode = k, Pressed = true, Echo = false });

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
        // Factory selected: right-click sets its rally point (client-side;
        // fresh units get a PathMove there on ProductionComplete).
        bool onlyStructures = true;
        foreach (int sid in _selection)
            if (_latest.TryGetValue(sid, out var sv) && Mobile(sv.Kind)) { onlyStructures = false; break; }
        if (onlyStructures)
        {
            foreach (int sid in _selection)
                if (_latest.TryGetValue(sid, out var sv) && sv.Kind == EntityKind.Factory)
                {
                    _rally[sid] = new Vector3(p.X, 0, p.Z);
                    _rallyMarker.Position = new Vector3(p.X, 0.35f, p.Z);
                    _rallyMarker.Visible = true;
                    // W3-17: rally clicks acknowledge in gold.
                    _effects.OrderMarker(new Vector3(p.X, 0, p.Z), 2);
                    _audio.Play("ui_confirm", -8);
                }
            return;
        }
        var cx = Fix64.FromFraction((int)(p.X * 100), 100);
        var cy = Fix64.FromFraction((int)(p.Z * 100), 100);
        int enemy = PickEntity(screen, 0.8f, v => v.PlayerId == 1 && v.Kind != EntityKind.FerriteField);
        int field = PickEntity(screen, 1.1f, v => v.Kind == EntityKind.FerriteField);
        foreach (int id in _selection)
        {
            if (!_latest.TryGetValue(id, out var me)) continue;
            if (enemy >= 0)
                _pending.Add(new Command(0, 0, CommandType.Attack, id, cx, cy, enemy, queued));
            else if (field >= 0 && me.Kind == EntityKind.Harvester)
                _pending.Add(new Command(0, 0, CommandType.Harvest, id, cx, cy, field, queued));
            else if (Mobile(me.Kind))
                _pending.Add(new Command(0, 0, CommandType.PathMove, id, cx, cy, -1, queued));
        }
        // W3-17: contracting acknowledgement ring at the order point, colour
        // coded by order type (attack rings sit on the target itself).
        int mk = enemy >= 0 ? 1 : (field >= 0 ? 2 : 0);
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
