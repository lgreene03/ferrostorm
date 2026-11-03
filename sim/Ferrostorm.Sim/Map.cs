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
    /// <summary>P8-31: passability by cell index, read only, for the flow-field build's inner loop.</summary>
    internal ReadOnlySpan<bool> BlockedCells => _blocked;

    public static int CellOf(Fix64 v) => v.ToIntFloor();
    public static Fix64 CellCentre(int c) => Fix64.FromInt(c) + Fix64.Half;
}

/// <summary>
/// Flow field toward a single target cell (TICKET-P1-04, TDD s3 pathfinding).
/// Built with Dijkstra over the passability grid, settling cells in ascending
/// (distance, cell index) order, so the resulting field is bit-identical on
/// every platform. Straight moves cost 2, diagonals 3; diagonal moves require
/// both adjacent orthogonals passable (no corner cutting). Fields are cached
/// per target cell.
///
/// ADR-071 adds the second shape, a field toward a structure's FOOTPRINT
/// (BuildToFootprint): many sources rather than one, seeded at every passable
/// cell of the one-cell ring round the footprint, so a building is reachable
/// from whichever face is open. The single-target field is unchanged.
///
/// P8-31: the build itself lives in <see cref="FlowFieldBuilder"/>, which owns
/// the scratch a build needs so that a cache's builds reuse it, and settles
/// cells from ordered buckets rather than a binary heap (D28). The static
/// Build and BuildToFootprint below keep their signatures for callers outside
/// a cache and use a builder of their own per call.
/// </summary>
public sealed class FlowField
{
    // 8 neighbours: E, W, N, S, NE, NW, SE, SW. Index 255 = unreachable/target.
    // P8-31: Cost and Opposite are the encoding FlowFieldBuilder.Relax writes
    // out step by step; they are kept here as its statement.
    private static readonly int[] Dx = { 1, -1, 0, 0, 1, -1, 1, -1 };
    private static readonly int[] Dy = { 0, 0, -1, 1, -1, -1, 1, 1 };
    private static readonly int[] Cost = { 2, 2, 2, 2, 3, 3, 3, 3 };
    private static readonly byte[] Opposite = { 1, 0, 3, 2, 7, 6, 5, 4 };

    internal const byte Unreachable = 255;
    /// <summary>ADR-071: a goal cell of a footprint field, one of its sources.
    /// Only BuildToFootprint writes it, so a single-target field never holds it
    /// and NextCell answers exactly as it always has for those.</summary>
    internal const byte AtGoal = 254;

    public readonly int TargetCell;
    private readonly byte[] _next; // per cell: neighbour index toward target

    internal FlowField(int targetCell, byte[] next) { TargetCell = targetCell; _next = next; }

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

    /// <summary>P8-31: the raw direction every cell holds, in cell index
    /// order: 0 to 7 a neighbour (E, W, N, S, NE, NW, SE, SW), 254 a footprint
    /// goal, 255 unreachable or the single target. Read only by the runner's
    /// `flowparity` stage, which compares it byte for byte with what the
    /// pre-P8-31 build gives; nothing in the sim reads it.</summary>
    public ReadOnlySpan<byte> Directions => _next;

    public static FlowField Build(Map map, int targetCx, int targetCy) => Build(map, targetCx, targetCy, out _);

    /// <summary>P8-30: Build, also reporting how many cells the relaxation
    /// lowered (see FlowFieldBuilder). The field is the same field; the count
    /// is only an observation of the work done to make it.</summary>
    public static FlowField Build(Map map, int targetCx, int targetCy, out int relaxed)
        => new FlowFieldBuilder().Build(map, targetCx, targetCy, out relaxed);

    /// <summary>
    /// ADR-071: a field toward a size x size footprint anchored at (ax,ay). The
    /// sources are every in-bounds, passable cell of the one-cell ring round the
    /// footprint, faces and corners alike, each at distance 0 and marked AtGoal.
    /// The single-target Build, seeded at the footprint's centre cell, could only
    /// expand into that cell's own open neighbours: for a 2x2 building, the
    /// cells east, south and south-east of its bottom-right cell, so blocking
    /// those three made the whole building unreachable from every side (ML-01).
    /// </summary>
    public static FlowField BuildToFootprint(Map map, int ax, int ay, int size) => BuildToFootprint(map, ax, ay, size, out _);

    /// <summary>P8-30: BuildToFootprint, also reporting the relaxation count, as Build does.</summary>
    public static FlowField BuildToFootprint(Map map, int ax, int ay, int size, out int relaxed)
        => new FlowFieldBuilder().BuildToFootprint(map, ax, ay, size, out relaxed);
}

/// <summary>
/// P8-31 (D28): builds flow fields of both shapes, owning the scratch a build
/// needs so that a cache's builds reuse it instead of allocating it each time.
/// One builder serves one caller at a time: a FlowFieldCache holds its own, so
/// each World, stepped on one thread, has its own. Nothing in the scratch
/// outlives a build (each build resets every cell it will read and empties
/// every bucket it fills), so which builder made a field cannot change the
/// field. The only allocations per build are the field and its direction
/// array, which the cache keeps; the scratch is allocated once per builder,
/// and again only if a larger map comes along.
/// </summary>
public sealed class FlowFieldBuilder
{
    // Per-cell distance to the nearest source, valid for the last build only.
    private int[] _dist = Array.Empty<int>();
    private int _cells;
    // Dial's buckets: the cells waiting at distance d sit in bucket d & 3.
    // Four suffice because every step costs 2 or 3, so while bucket d drains,
    // every push lands in d + 2 or d + 3, and the buckets for d - 2 and d - 1,
    // which share those slots, are already empty. A bucket never holds a cell
    // twice (a cell enters bucket d only when its distance falls to d, and it
    // only ever falls), so a map's cell count bounds each one.
    private readonly int[][] _bucket = { Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>() };
    private readonly int[] _count = new int[4];

    /// <summary>The distance every cell had at the end of the last build, in
    /// cell index order (int.MaxValue where unreachable). Read only by the
    /// runner's `flowparity` stage; nothing in the sim reads it.</summary>
    public ReadOnlySpan<int> Distances => _dist.AsSpan(0, _cells);

    // Sized to the map and reset for one build: every cell unreachable, every
    // bucket empty.
    private int[] Reset(Map map, out byte[] next)
    {
        int n = map.Width * map.Height;
        if (_dist.Length < n)
        {
            _dist = new int[n];
            for (int b = 0; b < 4; b++) _bucket[b] = new int[n];
        }
        _cells = n;
        Array.Fill(_dist, int.MaxValue, 0, n);
        Array.Clear(_count);
        next = new byte[n];
        Array.Fill(next, FlowField.Unreachable);
        return _dist;
    }

    /// <summary>A field toward one target cell, reporting the relaxation count (see Relax).</summary>
    public FlowField Build(Map map, int targetCx, int targetCy, out int relaxed)
    {
        var dist = Reset(map, out var next);
        int target = map.CellIndex(targetCx, targetCy);
        dist[target] = 0;
        _bucket[0][0] = target;
        _count[0] = 1;

        relaxed = Relax(map, dist, next, 1);
        return new FlowField(target, next);
    }

    /// <summary>
    /// ADR-071: a field toward a size x size footprint anchored at (ax,ay).
    /// The ring is walked row by row, y then x, which is ascending cell index,
    /// so the sources enter bucket 0 already in the order it is drained in. No
    /// dictionary or set is enumerated anywhere in the build. Relaxation never
    /// overwrites a source (it has distance 0 and every step costs at least
    /// 2), so a goal cell keeps its mark.
    /// </summary>
    public FlowField BuildToFootprint(Map map, int ax, int ay, int size, out int relaxed)
    {
        var dist = Reset(map, out var next);

        var sources = _bucket[0];
        int seeds = 0;
        for (int y = ay - 1; y <= ay + size; y++)
            for (int x = ax - 1; x <= ax + size; x++)
            {
                bool inside = x >= ax && x < ax + size && y >= ay && y < ay + size;
                if (inside || !map.InBounds(x, y) || map.IsBlocked(x, y)) continue;
                int c = map.CellIndex(x, y);
                dist[c] = 0;
                next[c] = FlowField.AtGoal;
                sources[seeds++] = c;
            }
        _count[0] = seeds;

        relaxed = Relax(map, dist, next, seeds);
        return new FlowField(map.CellIndex(ax, ay), next);
    }

    /// <summary>The Dijkstra relaxation both shapes share, from the sources
    /// waiting in bucket 0 (pending of them).
    ///
    /// P8-31 (D28): Dial's algorithm. The pre-P8-31 build popped (distance,
    /// cell) pairs from a binary heap ordered by distance and then by cell
    /// index, skipping a stale pair (one whose cell had since been lowered).
    /// That pop order is a strict total order over every pair ever pushed,
    /// and every push made while popping a pair at distance d is at d + 2 or
    /// d + 3, so the heap settled cells in exactly ascending (distance, cell
    /// index) order. This settles them in the same order: buckets are drained
    /// in ascending distance, and each bucket is complete before it drains (its
    /// cells were pushed from buckets d - 2 and d - 3, already drained), so it
    /// is sorted by cell index and walked in that order, a stale cell skipped
    /// by the same test. Each settled cell then relaxes its neighbours in the
    /// old order (E, W, N, S, NE, NW, SE, SW) under the old rules, so every
    /// comparison and every write happens in the same sequence against the
    /// same distances, and the distances, the directions and the count come
    /// out identical: the tie order is not merely equivalent, it is the same.
    /// `flowparity` proves it on every shipped map rather than leaving it to
    /// this argument.
    ///
    /// P8-30: returns the number of RELAXATIONS, the times a cell's distance
    /// was lowered (each one a push). That is the build's unit of work, and it
    /// depends only on the grid, the seeds and the settling order, so it is
    /// the same on every machine and the same as the heap's. The count is
    /// returned, never stored here, and nothing in the relaxation reads it.</summary>
    private int Relax(Map map, int[] dist, byte[] next, int pending)
    {
        int w = map.Width, h = map.Height;
        ReadOnlySpan<bool> blocked = map.BlockedCells;
        int[][] buckets = _bucket;
        int[] counts = _count;
        int relaxed = 0;
        for (int d = 0; pending > 0; d++)
        {
            int count = counts[d & 3];
            if (count == 0) continue;
            counts[d & 3] = 0;
            pending -= count;
            int[] bucket = buckets[d & 3];
            if (count > 1) Array.Sort(bucket, 0, count);
            // Every push from this bucket lands in one of these two.
            int d2 = d + 2, d3 = d + 3;
            int[] b2 = buckets[d2 & 3], b3 = buckets[d3 & 3];
            int n2 = counts[d2 & 3], n3 = counts[d3 & 3];
            int before = relaxed;
            for (int i = 0; i < count; i++)
            {
                int c = bucket[i];
                if (dist[c] != d) continue; // stale: lowered since it was pushed
                int cy = c / w, cx = c - cy * w;
                // The four orthogonal neighbours, open and in bounds; a
                // diagonal needs both orthogonals beside it open (no corner
                // cutting), which also puts it in bounds.
                bool e = cx + 1 < w && !blocked[c + 1];
                bool wo = cx > 0 && !blocked[c - 1];
                bool no = cy > 0 && !blocked[c - w];
                bool so = cy + 1 < h && !blocked[c + w];
                int nc;
                // The direction written is FlowField.Opposite of the step: the
                // way from the neighbour back toward c.
                if (e && d2 < dist[nc = c + 1]) { dist[nc] = d2; next[nc] = 1; b2[n2++] = nc; relaxed++; }
                if (wo && d2 < dist[nc = c - 1]) { dist[nc] = d2; next[nc] = 0; b2[n2++] = nc; relaxed++; }
                if (no && d2 < dist[nc = c - w]) { dist[nc] = d2; next[nc] = 3; b2[n2++] = nc; relaxed++; }
                if (so && d2 < dist[nc = c + w]) { dist[nc] = d2; next[nc] = 2; b2[n2++] = nc; relaxed++; }
                if (e && no && !blocked[nc = c - w + 1] && d3 < dist[nc]) { dist[nc] = d3; next[nc] = 7; b3[n3++] = nc; relaxed++; }
                if (wo && no && !blocked[nc = c - w - 1] && d3 < dist[nc]) { dist[nc] = d3; next[nc] = 6; b3[n3++] = nc; relaxed++; }
                if (e && so && !blocked[nc = c + w + 1] && d3 < dist[nc]) { dist[nc] = d3; next[nc] = 5; b3[n3++] = nc; relaxed++; }
                if (wo && so && !blocked[nc = c + w - 1] && d3 < dist[nc]) { dist[nc] = d3; next[nc] = 4; b3[n3++] = nc; relaxed++; }
            }
            counts[d2 & 3] = n2;
            counts[d3 & 3] = n3;
            pending += relaxed - before;
        }
        return relaxed;
    }
}

/// <summary>Cache of flow fields keyed by target cell. Lookup order never affects sim state.</summary>
public sealed class FlowFieldCache
{
    private readonly Dictionary<int, FlowField> _fields = new();
    // ADR-071: footprint fields, keyed by anchor cell and size. A separate
    // dictionary so a footprint key can never collide with a target-cell key.
    private readonly Dictionary<int, FlowField> _footprints = new();
    // P8-31: every build this cache makes shares one builder's pooled scratch.
    private readonly FlowFieldBuilder _builder = new();

    /// <summary>
    /// P8-30: how many fields this cache has built, of either shape, and how
    /// many cells their relaxations lowered in total, since the cache was made.
    /// A deterministic proxy for the pathfinding cost that wall-clock time
    /// measures only noisily: the same commands produce the same counts on
    /// every machine, so a runner can assert on them where it cannot assert on
    /// milliseconds (`longmatchperf`).
    ///
    /// AN OBSERVATION AND NOTHING MORE. Nothing in the sim reads either count,
    /// neither is hashed or saved, and Clear does not reset them (they count
    /// work done, not fields held). A loaded world therefore starts at zero
    /// whatever the saved one had done, which is right for a figure that
    /// describes this process's work rather than the match's state.
    /// </summary>
    public long Builds { get; private set; }
    /// <summary>P8-30: the relaxations behind <see cref="Builds"/> (FlowFieldBuilder.Relax's unit of work).</summary>
    public long CellsRelaxed { get; private set; }

    public FlowField Get(Map map, int targetCx, int targetCy)
    {
        int key = map.CellIndex(targetCx, targetCy);
        if (!_fields.TryGetValue(key, out var f))
        {
            f = _builder.Build(map, targetCx, targetCy, out int relaxed);
            _fields[key] = f;
            Builds++;
            CellsRelaxed += relaxed;
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
            f = _builder.BuildToFootprint(map, ax, ay, size, out int relaxed);
            _footprints[key] = f;
            Builds++;
            CellsRelaxed += relaxed;
        }
        return f;
    }

    public void Clear() { _fields.Clear(); _footprints.Clear(); } // required if the map's passability changes
}
