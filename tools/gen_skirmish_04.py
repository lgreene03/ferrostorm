#!/usr/bin/env python3
"""Generate data/maps/skirmish-04.fmap - "Tarnwater Crossing", 192x128, two players.

TICKET-P5-MAP-01 (doc 22 MAP-04). The map is generated rather than hand-typed
because its fairness invariant is mechanical: every feature must be placed as a
180-degree rotation-symmetric pair about the map centre, which is the symmetry
that maps start 0 onto start 1 exactly. A human editing 24576 characters cannot
hold that invariant; a script can, and can then prove it.

Grid characters (sim/Ferrostorm.Sim/MapLoader.cs is the format spec):
    '.' open      '#' blocked        'F' ferrite (12000 each)
    'w' water (blocked)    'h' hill (blocked)
    'r' ruin (blocked)     'f' fence (blocked)
    'B' bridge (OPEN to the sim: the pathable crossing)

Run:  python3 tools/gen_skirmish_04.py data/maps/skirmish-04.fmap
Every constraint in the ticket is asserted below, so a bad edit fails here
rather than in a match.
"""
import sys
from collections import deque

W, H = 192, 128
STARTS = {0: (12, 20), 1: (179, 107)}
APRON = 4          # 9x9 open apron := start +/- 4
RIVER = range(94, 98)
BRIDGE_ROWS = [(20, 24), (60, 68), (104, 108)]
BLOCKING = set('#whrf')   # 'B' is a bridge: open to the sim


def rot(x, y):
    """The fairness axis. Starts (12,20) and (179,107) have midpoint
    (95.5, 63.5), so the symmetry is a 180-degree rotation, not a mirror:
    (x,y) -> (191-x, 127-y) maps each start onto the other exactly."""
    return W - 1 - x, H - 1 - y


grid = [['.' for _ in range(W)] for _ in range(H)]

# Cells the aprons own. Nothing may be stamped into them, so the aprons do not
# have to be re-cleared afterwards (which would silently delete features).
apron_cells = set()
for sx, sy in STARTS.values():
    for y in range(sy - APRON, sy + APRON + 1):
        for x in range(sx - APRON, sx + APRON + 1):
            apron_cells.add((x, y))

# ---- The river: north-south at x=94..97, with exactly three crossings. ------
# Three crossings on a 128-tall map is the tempo lever: attack routes are
# committed, bridge control is the map's central question, and the defender has
# something to hold that is not their own base.
for y in range(H):
    for x in RIVER:
        grid[y][x] = 'w'
for y0, y1 in BRIDGE_ROWS:
    for y in range(y0, y1):
        for x in RIVER:
            grid[y][x] = 'B'

# The river and the bridge rows must already be rotation-symmetric, or nothing
# stamped on top of them can be. Check before relying on it.
for y in range(H):
    for x in RIVER:
        rx, ry = rot(x, y)
        assert grid[y][x] == grid[ry][rx], f"river asymmetric at ({x},{y})"


def stamp(x0, y0, dx, dy, ch):
    """Write a rectangle and its rotation image. A cell is written only if BOTH
    it and its partner are writable, so the pair can never land half-placed -
    that is the failure mode that quietly hands one player an advantage."""
    for y in range(y0, y0 + dy):
        for x in range(x0, x0 + dx):
            rx, ry = rot(x, y)
            if not (0 <= x < W and 0 <= y < H):
                continue
            # Never overwrite water, a bridge, or a start apron.
            if grid[y][x] in 'wB' or grid[ry][rx] in 'wB':
                continue
            if (x, y) in apron_cells or (rx, ry) in apron_cells:
                continue
            grid[y][x] = ch
            grid[ry][rx] = ch


# ---- Terrain. Hills frame each base, ruins litter the midfield, fences give
# light cover. Density target is 8-10% blocked: below that the map reads as an
# empty field, above it pathing and the draw-call budget suffer.
stamp(4, 8, 22, 4, 'h')      # ridge arc north of base 0
stamp(30, 14, 6, 14, 'h')    # east flank of base 0
stamp(2, 34, 12, 6, 'h')     # south flank of base 0
stamp(20, 96, 10, 10, 'h')   # far south-west massif
stamp(84, 30, 6, 16, 'h')    # west bank bluff, north
stamp(84, 76, 6, 16, 'h')    # west bank bluff, south

stamp(46, 30, 10, 8, 'r')    # midfield ruins
stamp(62, 52, 8, 10, 'r')
stamp(38, 78, 12, 6, 'r')
stamp(56, 8, 10, 6, 'r')

stamp(24, 62, 16, 2, 'f')    # fence lines: light cover, not a wall
stamp(70, 96, 14, 2, 'f')

# ---- Ferrite: 12 clusters of 5 = 60 cells = 720,000 credits. Three times
# skirmish-02/03's 20 for four times the area, i.e. a deliberate slight
# tightening per unit of area to keep expansion contested.
# Per player: 1 base + 2 near + 2 mid + 1 contested centre.
CLUSTER_SHAPE = [(0, 0), (1, 0), (0, 1), (2, 1), (1, 2)]   # as skirmish-02
CLUSTERS = [
    (20, 28),    # base 0
    (34, 44),    # near 0
    (8, 56),     # near 0
    (52, 70),    # mid 0
    (66, 22),    # mid 0
    (86, 60),    # contested centre, beside the middle bridge
]


def cluster(cx, cy):
    """Place a 5-cell field and its rotation image. Both cells of every pair
    must be free: a cluster that half-lands would break both the 60-cell budget
    and the symmetry, so fail loudly instead."""
    for dx, dy in CLUSTER_SHAPE:
        x, y = cx + dx, cy + dy
        rx, ry = rot(x, y)
        assert 0 <= x < W and 0 <= y < H, f"cluster ({cx},{cy}) off-map"
        assert grid[y][x] == '.', f"cluster cell ({x},{y}) is '{grid[y][x]}', not open"
        assert grid[ry][rx] == '.', f"cluster cell ({rx},{ry}) is '{grid[ry][rx]}', not open"
        grid[y][x] = 'F'
        grid[ry][rx] = 'F'


for cx, cy in CLUSTERS:
    cluster(cx, cy)

# ---------------------------------------------------------------- validation
# Everything the ticket calls machine-checkable, checked here.

# 1. Shape.
assert len(grid) == H
for row in grid:
    assert len(row) == W

# 2. Rotation symmetry of blocked cells and of fields, cell by cell.
for y in range(H):
    for x in range(W):
        rx, ry = rot(x, y)
        a, b = grid[y][x], grid[ry][rx]
        assert (a in BLOCKING) == (b in BLOCKING), f"blocked asymmetry at ({x},{y})"
        assert (a == 'F') == (b == 'F'), f"ferrite asymmetry at ({x},{y})"

# 3. Aprons are fully open, so the 2x2 CY footprint and the MCV always fit.
for sx, sy in STARTS.values():
    for y in range(sy - APRON, sy + APRON + 1):
        for x in range(sx - APRON, sx + APRON + 1):
            assert grid[y][x] == '.', f"apron cell ({x},{y}) is '{grid[y][x]}'"

# 4. Ferrite budget.
fields = [(x, y) for y in range(H) for x in range(W) if grid[y][x] == 'F']
assert len(fields) == 60, f"expected 60 ferrite cells, got {len(fields)}"

# 5. Density.
blocked = [(x, y) for y in range(H) for x in range(W) if grid[y][x] in BLOCKING]
density = len(blocked) / (W * H)
assert 0.08 <= density <= 0.10, f"blocked density {density:.4f} outside 8-10%"

# 6. Reachability: flood fill from each start over non-blocked cells, treating
#    'B' as open and 'w' as closed. Every field, the far start, and every apron
#    cell must be reached from both starts - no ferrite walled off behind an
#    unbridged river.
def flood(sx, sy):
    seen = {(sx, sy)}
    q = deque([(sx, sy)])
    while q:
        x, y = q.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < W and 0 <= ny < H and (nx, ny) not in seen \
                    and grid[ny][nx] not in BLOCKING:
                seen.add((nx, ny))
                q.append((nx, ny))
    return seen


reach = {p: flood(*s) for p, s in STARTS.items()}
for p, seen in reach.items():
    for f in fields:
        assert f in seen, f"player {p} cannot reach ferrite at {f}"
    for q, s in STARTS.items():
        assert s in seen, f"player {p} cannot reach start {q} at {s}"
    for c in apron_cells:
        assert c in seen, f"player {p} cannot reach apron cell {c}"

# 7. The bridges are the ONLY way across: prove the crossings are load-bearing
#    rather than incidental by closing them and re-flooding.
saved = [(x, y, grid[y][x]) for y in range(H) for x in RIVER if grid[y][x] == 'B']
for x, y, _ in saved:
    grid[y][x] = 'w'
assert STARTS[1] not in flood(*STARTS[0]), "starts connect without using a bridge"
for x, y, ch in saved:
    grid[y][x] = ch

# 8. Chebyshev-distance fairness: the multiset of distances from each start to
#    all 60 fields must be identical, or one player is closer to the economy.
def cheb(s, fs):
    return sorted(max(abs(x - s[0]), abs(y - s[1])) for x, y in fs)


d0, d1 = cheb(STARTS[0], fields), cheb(STARTS[1], fields)
assert d0 == d1, f"ferrite distance profiles differ: {d0[:6]} vs {d1[:6]}"

# ---------------------------------------------------------------------- emit
path = sys.argv[1] if len(sys.argv) > 1 else "skirmish-04.fmap"
lines = [
    "ferrostorm-map v1",
    "# Tarnwater Crossing. The Tarnwater runs the length of the theatre and is",
    "# bridged three times: north, centre and south. Whoever holds two of the",
    "# three chooses where the war happens. Generated by tools/gen_skirmish_04.py;",
    "# edit that script and regenerate rather than editing this file by hand -",
    "# the 180-degree rotation symmetry is the fairness invariant and it is",
    "# checked there.",
    f"size {W} {H}",
]
for p, (cx, cy) in sorted(STARTS.items()):
    lines.append(f"start {p} {cx} {cy}")
lines.append("grid:")
lines.extend("".join(row) for row in grid)
with open(path, "w") as fh:
    fh.write("\n".join(lines) + "\n")

census = {}
for row in grid:
    for ch in row:
        census[ch] = census.get(ch, 0) + 1
print(f"skirmish-04 (Tarnwater Crossing): {W}x{H} -> {path}")
print(f"  census:  {dict(sorted(census.items()))}")
print(f"  blocked: {len(blocked)} / {W * H} = {density * 100:.2f}%  (target 8-10%)")
print(f"  ferrite: {len(fields)} cells = {len(fields) * 12000:,} credits")
print(f"  starts:  {STARTS[0]} and {STARTS[1]}, apron {APRON * 2 + 1}x{APRON * 2 + 1}")
print(f"  bridges: {len(saved)} cells over {len(BRIDGE_ROWS)} crossings")
print("  all symmetry, density, reachability and fairness checks passed")
