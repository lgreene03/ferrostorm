using Ferrostorm.Sim;

namespace Ferrostorm.Presentation;

/// <summary>
/// TICKET-P1-07 presentation contract (TDD s2/ADR-001): the renderer never
/// touches live sim state. It consumes immutable snapshots taken at tick
/// boundaries and draws at any frame rate by interpolating between the two
/// snapshots that bracket the current render time. This class is engine-free
/// pure C# so the contract itself is unit-testable headless; the Godot layer
/// is a thin shell over it.
///
/// This is view-side code: floating point is fine here and never flows back
/// into the sim (the /sim purity grep only guards Ferrostorm.Sim itself).
/// </summary>
public sealed class SnapshotInterpolator
{
    /// <summary>
    /// P5-ECON-01: FerriteAmount and FerriteCap ride here so the client can draw
    /// a field DRAINING. They are appended with defaults, so every existing
    /// construction site and every consumer compiles unchanged.
    ///
    /// This is presentation state, outside the state hash by construction
    /// (Ferrostorm.Presentation is the snapshot contract, ADR-001), so widening
    /// it moves no golden. The client previously tried to scale a field by
    /// v.Hp / 12000, which was dead: a field spawns with Hp = 1, so the
    /// expression pinned to its own floor and every field rendered at a
    /// constant size whether it was full or nearly exhausted.
    /// </summary>
    public readonly record struct ViewEntity(
        int Id, bool Alive, int PlayerId, EntityKind Kind, double X, double Y, int Hp,
        int UnitType = 0, int MaxHp = 0, int FerriteAmount = 0, int FerriteCap = 0);

    private readonly Dictionary<int, Entity[]> _snapshots = new();
    private readonly int _window;
    public int OldestTick { get; private set; } = int.MaxValue;
    public int NewestTick { get; private set; } = int.MinValue;
    public int Count => _snapshots.Count;

    /// <param name="windowTicks">How many ticks of history to retain; older snapshots are evicted.</param>
    public SnapshotInterpolator(int windowTicks = 8) => _window = windowTicks;

    private static double ToDouble(Fix64 v) => v.Raw / 4294967296.0;

    public void AddSnapshot(int tick, Entity[] entities)
    {
        _snapshots[tick] = entities;
        if (tick < OldestTick) OldestTick = tick;
        if (tick > NewestTick) NewestTick = tick;
        while (NewestTick - OldestTick >= _window)
        {
            _snapshots.Remove(OldestTick);
            OldestTick++;
        }
    }

    /// <summary>
    /// Sample the view at a fractional sim time (in ticks). Positions lerp
    /// between bracketing snapshots; everything discrete (Alive, Hp, Kind)
    /// snaps to the earlier snapshot so nothing flickers ahead of its tick.
    /// The loop walks the EARLIER bracket only. An entity present there but
    /// absent from the later bracket renders at the earlier snapshot,
    /// unlerped; an entity that exists only in the later bracket (born
    /// between the two) is not emitted at all until render time reaches its
    /// first snapshot, so a newborn appears one tick late. That latency is
    /// deliberate: at the sim's 15 Hz it is under 67 ms, and it keeps the
    /// rule that nothing ever appears ahead of the tick that created it.
    /// </summary>
    public bool TrySample(double simTime, List<ViewEntity> output)
    {
        output.Clear();
        if (_snapshots.Count == 0) return false;
        double clamped = Math.Clamp(simTime, OldestTick, NewestTick);
        int t0 = Math.Min((int)Math.Floor(clamped), NewestTick);
        int t1 = Math.Min(t0 + 1, NewestTick);
        double alpha = t1 == t0 ? 0.0 : clamped - t0;
        if (!_snapshots.TryGetValue(t0, out var a)) return false;
        _snapshots.TryGetValue(t1, out var b);

        for (int i = 0; i < a.Length; i++)
        {
            var e0 = a[i];
            if (b != null && i < b.Length)
            {
                var e1 = b[i];
                double x = ToDouble(e0.X) + (ToDouble(e1.X) - ToDouble(e0.X)) * alpha;
                double y = ToDouble(e0.Y) + (ToDouble(e1.Y) - ToDouble(e0.Y)) * alpha;
                // Ferrite amount is taken from the EARLIER snapshot rather than
                // interpolated: it is a discrete stock, like Hp and Kind above,
                // and a lerped ore count would render a fractional deposit.
                output.Add(new ViewEntity(e0.Id, e0.Alive, e0.PlayerId, e0.Kind, x, y, e0.Hp, e0.UnitType, e0.MaxHp,
                    e0.FerriteAmount, e0.FerriteCap));
            }
            else
            {
                output.Add(new ViewEntity(e0.Id, e0.Alive, e0.PlayerId, e0.Kind, ToDouble(e0.X), ToDouble(e0.Y), e0.Hp, e0.UnitType, e0.MaxHp,
                    e0.FerriteAmount, e0.FerriteCap));
            }
        }
        return true;
    }
}
