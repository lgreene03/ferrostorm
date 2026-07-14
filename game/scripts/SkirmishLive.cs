using Godot;
using Ferrostorm.Presentation;
using Ferrostorm.Sim;
using System.Collections.Generic;

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
    private Camera3D _cam = null!;
    private double _accumulator;
    private double _renderTime;
    private int _winner = -1;

    // HUD
    private Label _status = null!;
    private Label _banner = null!;
    private Panel _dragRect = null!;
    private Vector2 _dragStart;
    private bool _dragging;
    private Sidebar _sidebar = null!;
    private AudioDirector _audio = null!;
    private CombatEffects _effects = null!;

    // Structure placement mode
    private int _placingType;
    private MeshInstance3D _ghost = null!;
    private int _yardId = -1, _factoryId = -1;

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

    // Turret tracking (TICKET-P4-SLICE-01): models exporting a child node
    // named "turret" slew it toward whatever they last fired at.
    private readonly Dictionary<int, Node3D> _turrets = new();
    private readonly Dictionary<int, (Vector3 At, double Until)> _aim = new();
    private double _now;
    private GpuParticles3D _dust = null!;

    private static Node3D? FindTurret(Node n)
    {
        if (n is Node3D t && n.Name.ToString().StartsWith("turret")) return t;
        foreach (var c in n.GetChildren())
            if (FindTurret(c) is { } f) return f;
        return null;
    }

    public override void _Ready()
    {
        _models = new ModelLibrary();
        AddChild(_models);

        MapData map;
        if (MatchConfig.MissionPath is { } missionPath)
        {
            // Campaign mission: the map places both sides' forces and the
            // triggers script the enemy - no skirmish AI. Per-mission setup
            // mirrors the runner's gated scenarios.
            map = MapData.Load(missionPath);
            _world = map.BuildWorld(seed: 2026, players: 2, out var tags);
            switch (MatchConfig.MissionIndex)
            {
                case 1:
                    _world.GrantCredits(0, 5000);
                    _world.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
                    break;
                case 3:
                    _world.GrantCredits(0, 4000);
                    break;
            }
            _mission = new MissionRunner(map, tags);
        }
        else
        {
            string mapPath = MatchConfig.MapPath ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(
                ProjectSettings.GlobalizePath("res://"), "..", "data", "maps", "skirmish-01.fmap"));
            map = MapData.Load(mapPath);
            _world = map.BuildWorld(seed: 2026, players: 2);
            _world.GrantCredits(0, MatchConfig.StartCredits);
            _world.GrantCredits(1, MatchConfig.StartCredits);
            _world.SpawnConstructionYard(0, map.Starts[0].Cx, map.Starts[0].Cy);
            _world.SpawnConstructionYard(1, map.Starts[1].Cx, map.Starts[1].Cy);
            // Mirrored starting force (common hardware only, so faction-neutral):
            // a harvester and three rifle squads each - the classic opening hand.
            for (int p = 0; p < 2; p++)
            {
                int sx = map.Starts[p].Cx, sy = map.Starts[p].Cy;
                int side = p == 0 ? 1 : -1;
                _world.SpawnHarvester(p, Fix64.FromInt(sx + 3 * side), Fix64.FromInt(sy + 2));
                for (int i = 0; i < 3; i++)
                    _world.SpawnUnit(p, Fix64.FromInt(sx + (2 + i) * side), Fix64.FromInt(sy - 2),
                        Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
            }
            _enemy = MatchConfig.AiPreset switch
            {
                1 => SkirmishAI.Rusher(1),
                2 => SkirmishAI.Turtle(1),
                _ => SkirmishAI.Standard(1),
            };
        }

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
        _cam.Current = true;

        BattlefieldView.BuildLightRig(this);

        _audio = new AudioDirector();
        AddChild(_audio);
        _audio.PlayAmbient();
        _effects = new CombatEffects();
        AddChild(_effects);

        BuildHud();

        _ghost = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(2, 0.5f, 2) },
            Visible = false,
        };
        AddChild(_ghost);

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
        _audio.Play("ui_click", -6);
    }

    private void BuildHud()
    {
        var hud = new CanvasLayer { Name = "Hud" };
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
            Text = "click: select unit or building   right click: move / attack / harvest / rally   R repair  X sell  P pause   ctrl+1-9 groups   wasd/edge/wheel camera",
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
            _cam.Position = new Vector3(world.X, _cam.Position.Y, world.Y + _cam.Position.Y * 0.55f);
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
        if (_winner < 0 && !_paused)
        {
            _accumulator += delta;
            while (_accumulator >= TickSeconds)
            {
                _accumulator -= TickSeconds;
                _tickCmds.Clear();
                _enemy?.Act(_world, _tickCmds);
                _tickCmds.AddRange(_missionCmds);
                _missionCmds.Clear();
                _tickCmds.AddRange(_pending);
                _pending.Clear();
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
                _effects.OnTickEvents(_world.Events, _actors, _audio);
                foreach (var ev in _world.Events)
                {
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
                    if (ev.Type == GameEventType.Fired && _latest.TryGetValue(ev.B, out var tgt))
                        _aim[ev.A] = (new Vector3((float)tgt.X, 0, (float)tgt.Y), _now + 1.6);
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
                        }
                    }
                }
            }
        }
        _renderTime = _world.Tick - 1 + _accumulator / TickSeconds;
        SyncActors((float)delta);
        int supply = 0, draw = 0;
        foreach (var e in _world.Entities)
            if (e.Alive && e.PlayerId == 0) { supply += e.PowerSupply; draw += e.PowerDraw; }
        string pwr = draw > supply ? $"PWR {supply}/{draw} LOW" : $"PWR {supply}/{draw}";
        _status.Text = $"CREDITS {_world.Credits(0)}    {pwr}    UNITS {CountOwn()}    TICK {_world.Tick}";

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
        _minimap.Refresh(_fog.FogImage, dots, new Vector2(_cam.Position.X, _cam.Position.Z - _cam.Position.Y * 0.55f));
        _selInfo.Text = SelectionSummary();

        _yardId = FindOwnStructure(EntityKind.ConstructionYard);
        _factoryId = FindOwnStructure(EntityKind.Factory);
        int ready = _yardId >= 0 ? _world.Entities[_yardId].ReadyStructure : 0;
        _sidebar.Refresh(_world.Credits(0), ready, _factoryId >= 0, _yardId >= 0,
            _yardId >= 0 ? _world.QueueLength(_yardId) : 0,
            _factoryId >= 0 ? _world.QueueLength(_factoryId) : 0);

        if (_placingType > 0 && _ghost.Visible)
        {
            var gp = GroundPoint(GetViewport().GetMousePosition());
            if (gp is { } p)
            {
                int ax = Mathf.FloorToInt(p.X), ay = Mathf.FloorToInt(p.Z);
                _ghost.Position = new Vector3(ax + 1f, 0.25f, ay + 1f);
                bool ok = _world.ValidPlacement(0, ax, ay);
                _ghost.MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = ok ? new Color(0.3f, 0.9f, 0.4f, 0.4f) : new Color(0.9f, 0.25f, 0.2f, 0.4f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                };
            }
        }
    }

    private static readonly string[] UnitNames = { "", "CANNON TANK", "RIFLE SQUAD", "ROCKET SQUAD", "HARVESTER", "SHADE RAIDER", "SENTINEL SCOUT", "MCV", "HOWITZER", "PHANTOM TANK", "BULWARK TANK", "ENGINEER", "VANGUARD CAR" };

    private string SelectionSummary()
    {
        if (_selection.Count == 0) return "";
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

    private void SyncActors(float dt)
    {
        if (!_interp.TrySample(_renderTime, _view)) return;
        var seen = new HashSet<int>();
        _latest.Clear();
        foreach (var v in _view)
        {
            if (!v.Alive) continue;
            seen.Add(v.Id);
            _latest[v.Id] = v;
            var pos = new Vector3((float)v.X, 0, (float)v.Y);
            if (!_actors.TryGetValue(v.Id, out var node))
            {
                node = _models.Instantiate((int)v.Kind, v.UnitType);
                node.Position = pos;
                if (Mobile(v.Kind) && v.PlayerId >= 0)
                    BattlefieldView.DressMobile(node, v.PlayerId,
                        v.Kind == EntityKind.Harvester ? 1.7f : 1.15f);
                else if (v.Kind != EntityKind.FerriteField)
                    BattlefieldView.AddContactBlob(node, 2.6f);
                AddChild(node);
                _actors[v.Id] = node;
                if (FindTurret(node) is { } tur) _turrets[v.Id] = tur;
            }
            _targets[v.Id] = pos;
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
            { _actors[id].QueueFree(); _actors.Remove(id); _targets.Remove(id); _selection.Remove(id); _turrets.Remove(id); _aim.Remove(id); }

        _now += dt;
        foreach (var (id, node) in _actors)
        {
            if (!_targets.TryGetValue(id, out var t)) continue;
            var to = t - node.Position;
            node.Position = node.Position.Lerp(t, dt * 10f);
            if (_latest.TryGetValue(id, out var v) && Mobile(v.Kind) && to.LengthSquared() > 0.003f)
            {
                float desired = Mathf.Atan2(-to.X, -to.Z);
                var r = node.Rotation;
                r.Y = Mathf.LerpAngle(r.Y, desired, 1f - Mathf.Exp(-6f * dt));
                node.Rotation = r;
            }
            // Turret slew: aim at the last fired-at position (relative to the
            // hull's own yaw), then return to centre once the memory fades.
            if (_turrets.TryGetValue(id, out var tur))
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
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } pm when _placingType > 0:
                TryPlace(pm.Position);
                break;
            case InputEventKey { Keycode: Key.Escape, Pressed: true } when _placingType > 0:
                _placingType = 0; _ghost.Visible = false;
                break;
            case InputEventKey { Keycode: Key.Escape, Pressed: true } when _winner >= 0:
                GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
                break;
            case InputEventKey { Keycode: Key.R, Pressed: true, Echo: false } when _selection.Count > 0:
                foreach (int sid in _selection)
                    if (_latest.TryGetValue(sid, out var rv) && !Mobile(rv.Kind))
                        _pending.Add(new Command(0, 0, CommandType.Repair, sid, Fix64.Zero, Fix64.Zero));
                _audio.Play("ui_confirm", -8);
                break;
            case InputEventKey { Keycode: Key.X, Pressed: true, Echo: false } when _selection.Count > 0:
                foreach (int sid in _selection)
                    if (_latest.TryGetValue(sid, out var xv) && !Mobile(xv.Kind))
                        _pending.Add(new Command(0, 0, CommandType.SellStructure, sid, Fix64.Zero, Fix64.Zero));
                _audio.Play("ui_click", -8);
                break;
            case InputEventKey { Keycode: Key.P, Pressed: true, Echo: false }:
                _paused = !_paused;
                _banner.Text = "PAUSED";
                _banner.AddThemeColorOverride("font_color", new Color(0.84f, 0.82f, 0.77f));
                _banner.Visible = _paused && _winner < 0;
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
                if (_actors.TryGetValue(hit, out var n) && n.GetNodeOrNull<MeshInstance3D>("SelRing") == null)
                    BattlefieldView.AddSelRing(n, 1.5f);   // structures ring on demand
                _audio.Play("ui_click", -14);
            }
            return;
        }
        var tl = new Vector2(Mathf.Min(_dragStart.X, at.X), Mathf.Min(_dragStart.Y, at.Y));
        var br = new Vector2(Mathf.Max(_dragStart.X, at.X), Mathf.Max(_dragStart.Y, at.Y));
        foreach (var v in _view)
        {
            if (!v.Alive || v.PlayerId != 0 || !Mobile(v.Kind)) continue;
            var sp = _cam.UnprojectPosition(new Vector3((float)v.X, 0.3f, (float)v.Y));
            if (sp.X >= tl.X && sp.X <= br.X && sp.Y >= tl.Y && sp.Y <= br.Y) _selection.Add(v.Id);
        }
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
        if (_placingType <= 0 || !_world.ValidPlacement(0, ax, ay)) { _audio.Play("ui_click", -12); return false; }
        _pending.Add(new Command(0, 0, CommandType.PlaceStructure, _yardId,
            Fix64.FromInt(ax), Fix64.FromInt(ay), _placingType));
        _placingType = 0; _ghost.Visible = false;
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
    public bool AimActive(int unitType)
    {
        foreach (var v in _view)
            if (v.Alive && v.PlayerId == 0 && v.UnitType == unitType && _aim.TryGetValue(v.Id, out var a) && a.Until > _now)
                return true;
        return false;
    }
    public long CreditsNow => _world.Credits(0);
    public int ReadyAtYard => _yardId >= 0 ? _world.Entities[_yardId].ReadyStructure : 0;
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
        _audio.Play("order_move", -8);
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
