#!/usr/bin/env python3
"""Shared machinery for the skirmish-map generators (TICKET-P6-MAP-01).

The maps are generated rather than hand-typed because their fairness invariant
is mechanical: every feature must be placed as a 180-degree rotation-symmetric
pair about the map centre, the symmetry that maps start 0 onto start 1 exactly.
A human editing thousands of characters cannot hold that invariant; a script
can, and can then prove it. This module is the single place the invariant lives,
so it is defined once and every generator inherits the same proof.

The map format spec is sim/Ferrostorm.Sim/MapLoader.cs. Grid characters:
    '.' open      '#' blocked        'F' ferrite (12000 each)
    'w' water (blocked)   'h' hill (blocked)
    'r' ruin (blocked)    'f' fence (blocked)
    'B' bridge (OPEN to the sim: the pathable crossing)

The reachability flood fill here is 4-connected over the non-blocked cells with
'B' open and 'w' closed. That is deliberately the CONSERVATIVE model of the
sim's own passability: the flow field also allows diagonal moves but forbids
corner cutting, so a 4-connected path is a subset of what a unit can walk. If
this module proves a 4-connected route through a crossing, the sim's flow field
has at least that route, and the army flows. Written stdlib-only, matching the
project's zero-dependency posture.
"""
import math
from collections import deque

BLOCKING = set('#whrf')   # 'B' is a bridge: open to the sim


def rot(x, y, w, h):
    """(x,y) -> (w-1-x, h-1-y): the 180-degree rotation that maps each start
    onto the other. A sine centred on the map centre is symmetric under it by
    construction, but rounding to integer cells can slip a cell, so every
    feature below is placed as an explicit rotation pair and then re-proved
    cell by cell rather than trusted."""
    return w - 1 - x, h - 1 - y


class Canvas:
    """A grid under construction. Every mutator writes a cell together with its
    rotation image, so the grid is symmetric by construction; validate() then
    proves it, along with density, reachability, load-bearing crossings and
    ferrite fairness."""

    def __init__(self, w, h, starts, apron=4):
        self.w, self.h = w, h
        self.starts = dict(starts)          # {0:(x,y), 1:(x,y)}
        self.apron = apron
        self.grid = [['.' for _ in range(w)] for _ in range(h)]
        # The starts must themselves be a rotation pair, or nothing built on top
        # of the symmetry can rescue the fairness.
        assert rot(*self.starts[0], w, h) == self.starts[1], \
            f"starts {self.starts[0]} and {self.starts[1]} are not a 180-rotation pair"
        # Cells the aprons own. Nothing may be stamped into them, so they never
        # have to be re-cleared (which would silently delete a feature).
        self.apron_cells = set()
        for sx, sy in self.starts.values():
            for y in range(sy - apron, sy + apron + 1):
                for x in range(sx - apron, sx + apron + 1):
                    assert 0 <= x < w and 0 <= y < h, f"apron of {(sx, sy)} runs off-map"
                    self.apron_cells.add((x, y))
        self.river_cells = set()
        self.choke_cells = set()            # bridges, or ridge passes: the load-bearing crossings

    def inb(self, x, y):
        return 0 <= x < self.w and 0 <= y < self.h

    # -- rivers -----------------------------------------------------------
    def river(self, centre_fn, halfwidth_fn, vertical=True):
        """Mark a winding river as water. centre_fn(t) and halfwidth_fn(t) take
        the along-axis coordinate (rows for a vertical river) and return the
        cross-axis centre and half-width in cells. The river is closed under
        rotation before it is written, so a sine that rounds a cell off-centre
        is corrected rather than left to bias one bank."""
        cells = set()
        span = self.h if vertical else self.w
        for t in range(span):
            c = centre_fn(t)
            hw = halfwidth_fn(t)
            lo, hi = int(round(c - hw)), int(round(c + hw))
            for k in range(lo, hi + 1):
                x, y = (k, t) if vertical else (t, k)
                if self.inb(x, y):
                    cells.add((x, y))
        sym = set()
        for (x, y) in cells:
            sym.add((x, y))
            sym.add(rot(x, y, self.w, self.h))
        for (x, y) in sym:
            assert (x, y) not in self.apron_cells, f"river runs through an apron at {(x, y)}"
            self.grid[y][x] = 'w'
        self.river_cells |= sym
        self._vertical = vertical
        return sym

    def bridges(self, bands):
        """Turn the river cells inside each along-axis band into bridge decks.
        bands is a list of (t0, t1) half-open ranges. Closed under rotation, so
        three bridges are three pairs of identical crossings. These become the
        load-bearing crossings validate() proves are the ONLY way across."""
        want = set()
        for (t0, t1) in bands:
            for t in range(t0, t1):
                want.add(t)
        bcells = set()
        for (x, y) in self.river_cells:
            t = y if self._vertical else x
            if t in want:
                bcells.add((x, y))
        sym = set()
        for (x, y) in bcells:
            sym.add((x, y))
            sym.add(rot(x, y, self.w, self.h))
        for (x, y) in sym:
            self.grid[y][x] = 'B'
        self.choke_cells |= sym
        return sym

    # -- terrain ----------------------------------------------------------
    def stamp(self, x0, y0, dx, dy, ch, choke=False):
        """Write a dx-by-dy rectangle and its rotation image. A cell is written
        only if BOTH it and its partner are free of water, bridge and apron, so
        a pair can never land half-placed, the failure mode that quietly hands
        one player an advantage. If choke=True the cells are recorded as a
        crossing whose removal validate() will require to disconnect the map
        (used to prove a ridge's passes are load-bearing)."""
        for y in range(y0, y0 + dy):
            for x in range(x0, x0 + dx):
                if not self.inb(x, y):
                    continue
                rx, ry = rot(x, y, self.w, self.h)
                if self.grid[y][x] in 'wB' or self.grid[ry][rx] in 'wB':
                    continue
                if (x, y) in self.apron_cells or (rx, ry) in self.apron_cells:
                    continue
                self.grid[y][x] = ch
                self.grid[ry][rx] = ch
                if choke:
                    self.choke_cells.add((x, y))
                    self.choke_cells.add((rx, ry))

    def mark_pass(self, cells):
        """Record open cells as a load-bearing pass through a ridge: validate()
        proves that blocking them disconnects the two starts."""
        for (x, y) in cells:
            rx, ry = rot(x, y, self.w, self.h)
            self.choke_cells.add((x, y))
            self.choke_cells.add((rx, ry))

    def cluster(self, cx, cy, shape):
        """Place a ferrite field and its rotation image. Both cells of every
        pair must be open, or the pair would half-land and break both the budget
        and the symmetry, so fail loudly instead."""
        for dx, dy in shape:
            x, y = cx + dx, cy + dy
            rx, ry = rot(x, y, self.w, self.h)
            assert self.inb(x, y), f"cluster ({cx},{cy}) runs off-map at {(x, y)}"
            assert self.grid[y][x] == '.', f"cluster cell {(x, y)} is '{self.grid[y][x]}', not open"
            assert self.grid[ry][rx] == '.', f"cluster cell {(rx, ry)} is '{self.grid[ry][rx]}', not open"
            self.grid[y][x] = 'F'
            self.grid[ry][rx] = 'F'

    # -- proof ------------------------------------------------------------
    def _flood(self, sx, sy, grid=None):
        g = grid or self.grid
        seen = {(sx, sy)}
        q = deque([(sx, sy)])
        while q:
            x, y = q.popleft()
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= nx < self.w and 0 <= ny < self.h and (nx, ny) not in seen \
                        and g[ny][nx] not in BLOCKING:
                    seen.add((nx, ny))
                    q.append((nx, ny))
        return seen

    def validate(self, expected_fields, density_range):
        w, h, grid = self.w, self.h, self.grid
        assert len(grid) == h
        for row in grid:
            assert len(row) == w

        # 1. Rotation symmetry of blocked cells, fields and bridges, cell by cell.
        for y in range(h):
            for x in range(w):
                rx, ry = rot(x, y, w, h)
                a, b = grid[y][x], grid[ry][rx]
                assert (a in BLOCKING) == (b in BLOCKING), f"blocked asymmetry at {(x, y)}"
                assert (a == 'F') == (b == 'F'), f"ferrite asymmetry at {(x, y)}"
                assert (a == 'B') == (b == 'B'), f"bridge asymmetry at {(x, y)}"

        # 2. Aprons fully open, so the 2x2 CY footprint and the MCV always fit.
        for (x, y) in self.apron_cells:
            assert grid[y][x] == '.', f"apron cell {(x, y)} is '{grid[y][x]}'"

        # 3. Ferrite budget.
        fields = [(x, y) for y in range(h) for x in range(w) if grid[y][x] == 'F']
        assert len(fields) == expected_fields, f"expected {expected_fields} ferrite cells, got {len(fields)}"

        # 4. Density: below the floor the map reads as an empty field, above the
        #    ceiling pathing and the draw-call budget suffer.
        blocked = [(x, y) for y in range(h) for x in range(w) if grid[y][x] in BLOCKING]
        density = len(blocked) / (w * h)
        assert density_range[0] <= density <= density_range[1], \
            f"blocked density {density:.4f} outside {density_range}"

        # 5. Reachability: from each start, over non-blocked cells with bridges
        #    open, every field, the far start and every apron cell must be
        #    reached. No ferrite walled off, no base sealed in.
        for p, s in self.starts.items():
            seen = self._flood(*s)
            for f in fields:
                assert f in seen, f"player {p} cannot reach ferrite at {f}"
            for q, s2 in self.starts.items():
                assert s2 in seen, f"player {p} cannot reach start {q} at {s2}"
            for c in self.apron_cells:
                assert c in seen, f"player {p} cannot reach apron cell {c}"

        # 6. The crossings are load-bearing: close them and prove the two starts
        #    fall into separate components. A river without this is decoration.
        if self.choke_cells:
            saved = [(x, y, grid[y][x]) for (x, y) in self.choke_cells]
            for (x, y, _) in saved:
                grid[y][x] = '#'
            assert self.starts[1] not in self._flood(*self.starts[0]), \
                "starts stay connected with every crossing closed: the crossings are not load-bearing"
            for (x, y, ch) in saved:
                grid[y][x] = ch

        # 7. Chebyshev-distance fairness: the multiset of distances from each
        #    start to all fields must be identical, or one player is closer to
        #    the economy. Rotation guarantees it; this proves it held.
        def cheb(s):
            return sorted(max(abs(x - s[0]), abs(y - s[1])) for x, y in fields)
        assert cheb(self.starts[0]) == cheb(self.starts[1]), "ferrite distance profiles differ between starts"

        return fields, blocked, density

    # -- emit -------------------------------------------------------------
    def emit(self, path, header_lines):
        lines = ["ferrostorm-map v1"]
        lines.extend(header_lines)
        lines.append(f"size {self.w} {self.h}")
        for p, (cx, cy) in sorted(self.starts.items()):
            lines.append(f"start {p} {cx} {cy}")
        lines.append("grid:")
        lines.extend("".join(row) for row in self.grid)
        with open(path, "w") as fh:
            fh.write("\n".join(lines) + "\n")

    def census(self):
        c = {}
        for row in self.grid:
            for ch in row:
                c[ch] = c.get(ch, 0) + 1
        return dict(sorted(c.items()))


def report(name, canvas, fields, blocked, density, path, crossings):
    print(f"{name}: {canvas.w}x{canvas.h} -> {path}")
    print(f"  census:  {canvas.census()}")
    print(f"  blocked: {len(blocked)} / {canvas.w * canvas.h} = {density * 100:.2f}%")
    print(f"  ferrite: {len(fields)} cells = {len(fields) * 12000:,} credits")
    print(f"  starts:  {canvas.starts[0]} and {canvas.starts[1]}, "
          f"apron {canvas.apron * 2 + 1}x{canvas.apron * 2 + 1}")
    print(f"  crossings: {crossings}")
    print("  all symmetry, density, reachability, crossing and fairness checks passed")
