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
    private SkirmishAI _enemy = null!;
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

    public override void _Ready()
    {
        _models = new ModelLibrary();
        AddChild(_models);

        string mapPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "..", "data", "maps", "skirmish-01.fmap"));
        var map = MapData.Load(mapPath);
        _world = map.BuildWorld(seed: 2026, players: 2);
        _world.GrantCredits(0, 8000);
        _world.GrantCredits(1, 8000);
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
        _enemy = SkirmishAI.Standard(1);

        BattlefieldView.BuildEnvironment(this);
        BattlefieldView.BuildTerrain(this, map.Width, map.Height, map.Blocked);

        _cam = new RtsCamera();
        AddChild(_cam);
        _cam.Position = new Vector3(map.Starts[0].Cx, 22, map.Starts[0].Cy + 14);
        _cam.Current = true;

        var sun = new DirectionalLight3D
        {
            Rotation = new Vector3(-0.9f, 0.5f, 0),
            LightEnergy = 1.4f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 120f,
        };
        AddChild(sun);
        AddChild(new DirectionalLight3D { Rotation = new Vector3(-0.5f, 2.6f, 0), LightEnergy = 0.35f });

        BuildHud();
        SnapshotNow();
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
            Text = "left click / drag: select   right click: move / attack / harvest   shift: add + queue   wasd/edge/wheel: camera",
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

        _dragRect = new Panel { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.9f, 0.75f, 0.3f, 0.10f),
            BorderColor = new Color(0.9f, 0.75f, 0.3f, 0.8f),
        };
        style.SetBorderWidthAll(1);
        _dragRect.AddThemeStyleboxOverride("panel", style);
        hud.AddChild(_dragRect);
    }

    // ---------------- sim loop ----------------

    private void SnapshotNow()
    {
        var (tick, entities, _) = _world.TakeSnapshot();
        _interp.AddSnapshot(tick, entities);
    }

    public override void _Process(double delta)
    {
        if (_winner < 0)
        {
            _accumulator += delta;
            while (_accumulator >= TickSeconds)
            {
                _accumulator -= TickSeconds;
                _tickCmds.Clear();
                _enemy.Act(_world, _tickCmds);
                _tickCmds.AddRange(_pending);
                _pending.Clear();
                var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_tickCmds);
                _world.Step(span);
                SnapshotNow();
                foreach (var ev in _world.Events)
                    if (ev.Type == GameEventType.PlayerEliminated)
                        OnEliminated(ev.B);
            }
        }
        _renderTime = _world.Tick - 1 + _accumulator / TickSeconds;
        SyncActors((float)delta);
        _status.Text = $"CREDITS {_world.Credits(0)}    UNITS {CountOwn()}    TICK {_world.Tick}";
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
        _banner.Text = _winner == 0 ? "VICTORY" : "DEFEAT";
        _banner.AddThemeColorOverride("font_color",
            _winner == 0 ? BattlefieldView.DirectorateMark : new Color(0.8f, 0.25f, 0.2f));
        _banner.Visible = true;
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
                if (Mobile(v.Kind) && v.PlayerId >= 0) BattlefieldView.DressMobile(node, v.PlayerId);
                AddChild(node);
                _actors[v.Id] = node;
            }
            _targets[v.Id] = pos;
            if (v.Kind == EntityKind.FerriteField)
            {
                float g = Mathf.Max(0.2f, v.Hp / 12000f);
                node.Scale = Vector3.One * (0.6f + g * 0.9f);
            }
            if (node.GetNodeOrNull<MeshInstance3D>("SelRing") is { } sel)
                sel.Visible = _selection.Contains(v.Id);
        }
        foreach (var id in new List<int>(_actors.Keys))
            if (!seen.Contains(id))
            { _actors[id].QueueFree(); _actors.Remove(id); _targets.Remove(id); _selection.Remove(id); }

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

    private void FinishSelect(Vector2 at, bool add)
    {
        if (!add) _selection.Clear();
        if ((at - _dragStart).Length() <= 8)
        {
            int hit = PickEntity(at, 0.9f, v => v.PlayerId == 0 && Mobile(v.Kind));
            if (hit >= 0) _selection.Add(hit);
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

    /// <summary>Right-click order resolution; public so scripted verification
    /// can drive the exact input path a player uses.</summary>
    public void IssueOrder(Vector2 screen, bool queued)
    {
        if (_selection.Count == 0) return;
        var gp = GroundPoint(screen);
        if (gp is not { } p) return;
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
}
