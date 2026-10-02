namespace Ferrostorm.Sim;

/// <summary>Passability grid. Cell coordinates are ints; world positions are Fix64 in cell units.</summary>
public sealed class Map
{
    public readonly int Width, Height;
    private readonly bool[] _blocked;

    public Map(int width, int height)
    {
        Width = width; Height = height;
        _blocked = new bool[width * height];
    }

    public int CellIndex(int cx, int cy) => cy * Width + cx;
    public bool InBounds(int cx, int cy) => (uint)cx < (uint)Width && (uint)cy < (uint)Height;
    public bool IsBlocked(int cx, int cy) => _blocked[CellIndex(cx, cy)];
    public void SetBlocked(int cx, int cy, bool blocked) => _blocked[CellIndex(cx, cy)] = blocked;

    public static int CellOf(Fix64 v) => v.ToIntFloor();
    public static Fix64 CellCentre(int c) => Fix64.FromInt(c) + Fix64.Half;
}

/// <summary>
/// Flow field toward a single target cell (TICKET-P1-04, TDD s3 pathfinding).
/// Built with Dijkstra over the passability grid using an array binary heap
/// whose comparisons tie-break on cell index, so pop order - and therefore the
/// resulting field - is bit-identical on every platform. Straight moves cost 2,
/// diagonals 3; diagonal moves require both adjacent orthogonals passable
/// (no corner cutting). Fields are cached per target cell.
///
/// ADR-071 adds the second shape, a field toward a structure's FOOTPRINT
/// (BuildToFootprint): many sources rather than one, seeded at every passable
/// cell of the one-cell ring round the footprint, so a building is reachable
/// from whichever face is open. The single-target field is unchanged.
/// </summary>
public sealed class FlowField
{
    // 8 neighbours: E, W, N, S, NE, NW, SE, SW. Index 255 = unreachable/target.
    private static readonly int[] Dx = { 1, -1, 0, 0, 1, -1, 1, -1 };
    private static readonly int[] Dy = { 0, 0, -1, 1, -1, -1, 1, 1 };
    private static readonly int[] Cost = { 2, 2, 2, 2, 3, 3, 3, 3 };
    private static readonly byte[] Opposite = { 1, 0, 3, 2, 7, 6, 5, 4 };

    private const byte Unreachable = 255;
    /// <summary>ADR-071: a goal cell of a footprint field, one of its sources.
    /// Only BuildToFootprint writes it, so a single-target field never holds it
    /// and NextCell answers exactly as it always has for those.</summary>
    private const byte AtGoal = 254;

    public readonly int TargetCell;
    private readonly byte[] _next; // per cell: neighbour index toward target

    private FlowField(int targetCell, byte[] next) { TargetCell = targetCell; _next = next; }

    /// <summary>Next cell index on the route from (cx,cy), or -1 if at target/goal/unreachable.</summary>
    public int NextCell(Map map, int cx, int cy)
    {
        byte d = _next[map.CellIndex(cx, cy)];
        if (d >= AtGoal) return -1;
        return map.CellIndex(cx + Dx[d], cy + Dy[d]);
    }

    /// <summary>ADR-071: is (cx,cy) one of the ring cells a footprint field was seeded at?</summary>
    public bool IsGoal(Map map, int cx, int cy) => _next[map.CellIndex(cx, cy)] == AtGoal;

    /// <summary>ADR-071: does a footprint field reach the footprint from (cx,cy)?
    /// True on a goal cell and on every cell with a route to one. Meaningful for
    /// footprint fields only: a single-target field marks its own target 255.</summary>
    public bool Reaches(Map map, int cx, int cy) => _next[map.CellIndex(cx, cy)] != Unreachable;

    public static FlowField Build(Map map, int targetCx, int targetCy)
    {
        int n = map.Width * map.Height;
        var dist = new int[n];
        var next = new byte[n];
        Array.Fill(dist, int.MaxValue);
        Array.Fill(next, Unreachable);

        int target = map.CellIndex(targetCx, targetCy);
        dist[target] = 0;

        Relax(map, dist, next, new List<(int D, int C)> { (0, target) });
        return new FlowField(target, next);
    }

    /// <summary>
    /// ADR-071: a field toward a size x size footprint anchored at (ax,ay). The
    /// sources are every in-bounds, passable cell of the one-cell ring round the
    /// footprint, faces and corners alike, each at distance 0 and marked AtGoal.
    /// The single-target Build, seeded at the footprint's centre cell, could only
    /// expand into that cell's own open neighbours: for a 2x2 building, the
    /// cells east, south and south-east of its bottom-right cell, so blocking
    /// those three made the whole building unreachable from every side (ML-01).
    ///
    /// The ring is walked row by row, y then x, which is ascending cell index:
    /// a fixed, sorted seed order, and an ascending list is already a valid
    /// min-heap. No dictionary or set is enumerated anywhere in the build.
    /// Relaxation never overwrites a source (it has distance 0 and every step
    /// costs at least 2), so a goal cell keeps its mark.
    /// </summary>
    public static FlowField BuildToFootprint(Map map, int ax, int ay, int size)
    {
        int n = map.Width * map.Height;
        var dist = new int[n];
        var next = new byte[n];
        Array.Fill(dist, int.MaxValue);
        Array.Fill(next, Unreachable);

        var heap = new List<(int D, int C)>();
        for (int y = ay - 1; y <= ay + size; y++)
            for (int x = ax - 1; x <= ax + size; x++)
            {
                bool inside = x >= ax && x < ax + size && y >= ay && y < ay + size;
                if (inside || !map.InBounds(x, y) || map.IsBlocked(x, y)) continue;
                int c = map.CellIndex(x, y);
                dist[c] = 0;
                next[c] = AtGoal;
                heap.Add((0, c));
            }

        Relax(map, dist, next, heap);
        return new FlowField(map.CellIndex(ax, ay), next);
    }

    /// <summary>The Dijkstra relaxation both shapes share, from whatever the
    /// heap was seeded with. Moved here unchanged from Build by ADR-071.</summary>
    private static void Relax(Map map, int[] dist, byte[] next, List<(int D, int C)> heap)
    {
        // Array binary min-heap of (dist, cell); ties broken by lower cell index.
        void Push((int, int) item)
        {
            heap.Add(item);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (Less(heap[i], heap[p])) { (heap[i], heap[p]) = (heap[p], heap[i]); i = p; }
                else break;
            }
        }
        (int, int) Pop()
        {
            var top = heap[0];
            heap[0] = heap[^1];
            heap.RemoveAt(heap.Count - 1);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < heap.Count && Less(heap[l], heap[m])) m = l;
                if (r < heap.Count && Less(heap[r], heap[m])) m = r;
                if (m == i) break;
                (heap[i], heap[m]) = (heap[m], heap[i]); i = m;
            }
            return top;
        }
        static bool Less((int D, int C) a, (int D, int C) b)
            => a.D != b.D ? a.D < b.D : a.C < b.C;

        while (heap.Count > 0)
        {
            var (d, c) = Pop();
            if (d > dist[c]) continue; // stale entry
            int cx = c % map.Width, cy = c / map.Width;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + Dx[k], ny = cy + Dy[k];
                if (!map.InBounds(nx, ny) || map.IsBlocked(nx, ny)) continue;
                if (k >= 4) // no corner cutting
                {
                    if (map.IsBlocked(cx + Dx[k], cy) || map.IsBlocked(cx, cy + Dy[k])) continue;
                }
                int nc = map.CellIndex(nx, ny);
                int nd = d + Cost[k];
                if (nd < dist[nc])
                {
                    dist[nc] = nd;
                    // Direction FROM neighbour TOWARD c is the opposite of k.
                    next[nc] = Opposite[k];
                    Push((nd, nc));
                }
            }
        }
    }
}

/// <summary>Cache of flow fields keyed by target cell. Lookup order never affects sim state.</summary>
public sealed class FlowFieldCache
{
    private readonly Dictionary<int, FlowField> _fields = new();
    // ADR-071: footprint fields, keyed by anchor cell and size. A separate
    // dictionary so a footprint key can never collide with a target-cell key.
    private readonly Dictionary<int, FlowField> _footprints = new();

    public FlowField Get(Map map, int targetCx, int targetCy)
    {
        int key = map.CellIndex(targetCx, targetCy);
        if (!_fields.TryGetValue(key, out var f))
        {
            f = FlowField.Build(map, targetCx, targetCy);
            _fields[key] = f;
        }
        return f;
    }

    /// <summary>ADR-071: the field toward a size x size footprint anchored at
    /// (ax,ay). Sizes are 1 to 4 (schema.structure.json), so anchor x 8 + size is unique.</summary>
    public FlowField GetFootprint(Map map, int ax, int ay, int size)
    {
        int key = map.CellIndex(ax, ay) * 8 + size;
        if (!_footprints.TryGetValue(key, out var f))
        {
            f = FlowField.BuildToFootprint(map, ax, ay, size);
            _footprints[key] = f;
        }
        return f;
    }

    public void Clear() { _fields.Clear(); _footprints.Clear(); } // required if the map's passability changes
}
