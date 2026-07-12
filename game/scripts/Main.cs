using Godot;
using Ferrostorm.Presentation;
using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Game;

/// <summary>
/// TICKET-P1-07 ugly-box renderer. Deliberately primitive: circles and rects,
/// no art. Single-player loopback (no relay) - it drives World directly with
/// a local command list, steps at exactly 15 Hz via an accumulator, and draws
/// every frame from the SnapshotInterpolator, never from live sim state.
///
/// STATUS: UNTESTED IN AUTOMATION. The build container has no Godot and no
/// nuget.org, so this scaffold compiles only on a dev machine with the Godot
/// 4.2 .NET editor. The interpolation maths it relies on IS tested headless
/// (runner mode: spectate). Treat any behaviour difference between this file
/// and the spectate assertions as a bug in this file.
/// </summary>
public partial class Main : Node2D
{
    private const double TickSeconds = 1.0 / World.TicksPerSecond;
    private const float PixelsPerCell = 14f;

    private World _world = null!;
    private readonly SnapshotInterpolator _interp = new(windowTicks: 8);
    private readonly List<Command> _pending = new();
    private readonly List<SnapshotInterpolator.ViewEntity> _view = new();
    private readonly List<int> _selection = new();
    private double _accumulator;
    private double _renderTime;

    public override void _Ready()
    {
        _world = new World(seed: 2026, mapWidth: 96, mapHeight: 64, players: 2);
        _world.GrantCredits(0, 5000);
        _world.SpawnPowerPlant(0, 8, 8);
        _world.SpawnFactory(0, 12, 8);
        _world.SpawnRefinery(0, 8, 12);
        _world.SpawnHarvester(0, Fix64.FromInt(10), Fix64.FromInt(14));
        _world.SpawnFerriteField(Fix64.FromInt(30), Fix64.FromInt(30), 4000);
        for (int i = 0; i < 5; i++)
            _world.SpawnUnit(1, Fix64.FromInt(80), Fix64.FromInt(40 + i), Fix64.FromFraction(1, 4), 100, ArmourClass.None, 2);
        SnapshotNow();
    }

    private void SnapshotNow()
    {
        var (tick, entities, _) = _world.TakeSnapshot();
        _interp.AddSnapshot(tick, entities);
    }

    public override void _Process(double delta)
    {
        _accumulator += delta;
        while (_accumulator >= TickSeconds)
        {
            _accumulator -= TickSeconds;
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pending);
            _world.Step(span);
            _pending.Clear();
            SnapshotNow();
        }
        // Render half a tick behind the newest snapshot so there is always a
        // bracket pair to interpolate inside (the classic latency/smoothness trade).
        _renderTime = _world.Tick - 1 + _accumulator / TickSeconds;
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } click) return;
        Vector2 cell = click.Position / PixelsPerCell;
        var cx = Fix64.FromFraction((int)(cell.X * 100), 100);
        var cy = Fix64.FromFraction((int)(cell.Y * 100), 100);

        if (click.ButtonIndex == MouseButton.Left)
        {
            _selection.Clear();
            _interp.TrySample(_renderTime, _view);
            foreach (var v in _view)
                if (v.Alive && v.PlayerId == 0
                    && new Vector2((float)v.X, (float)v.Y).DistanceTo(cell) < 1.0f)
                { _selection.Add(v.Id); break; }
        }
        else if (click.ButtonIndex == MouseButton.Right && _selection.Count > 0)
        {
            foreach (int id in _selection)
                _pending.Add(new Command(0, 0, CommandType.PathMove, id, cx, cy));
        }
    }

    public override void _Draw()
    {
        if (!_interp.TrySample(_renderTime, _view)) return;
        foreach (var v in _view)
        {
            if (!v.Alive) continue;
            var pos = new Vector2((float)v.X, (float)v.Y) * PixelsPerCell;
            Color colour = v.PlayerId switch
            {
                0 => Colors.SteelBlue,
                1 => Colors.IndianRed,
                _ => Colors.Goldenrod, // neutral ferrite fields
            };
            if (v.Kind is EntityKind.Refinery or EntityKind.Factory or EntityKind.PowerPlant)
                DrawRect(new Rect2(pos - new Vector2(14, 14), new Vector2(28, 28)), colour);
            else if (v.Kind == EntityKind.FerriteField)
                DrawRect(new Rect2(pos - new Vector2(10, 10), new Vector2(20, 20)), colour);
            else
                DrawCircle(pos, v.Kind == EntityKind.Harvester ? 9f : 6f, colour);
            if (_selection.Contains(v.Id))
                DrawArc(pos, 11f, 0, Mathf.Tau, 24, Colors.White, 1.5f);
        }
        DrawString(ThemeDB.FallbackFont,
            new Vector2(12, 24),
            $"tick {_world.Tick}   credits {_world.Credits(0)}   hash 0x{_world.ComputeStateHash():X16}");
    }
}
