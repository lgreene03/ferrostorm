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

BLOCKING = set('#whrf')   # 'B' and 'b' are bridges: open to the sim while they stand
# Decoration: drawn by the client, PASSABLE to the sim, and deliberately absent
# from BLOCKING so it never reaches the density budget, the reachability proof
# or the crossing proof. Every previous visual character was an obstacle, so
# the 8-to-10-per-cent density cap was also a cap on how much of a map could be
# seen at all. These are how a map gets detail without getting harder to walk.
DECOR = set(',:=~')       # scrub, gravel, road, shallows


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
        # ADR-021: neutral capturable Outposts, as (ax, ay) top-left anchors of
        # their 2x2 footprints. Held APART from the grid because an outpost is
        # an ENTITY, not terrain: the sim blocks its own footprint when it
        # spawns (World.SpawnOutpost calls BlockFootprint), so writing it into
        # the grid would block those cells twice and inflate the density.
        self.outposts = []
        # ADR-025: destroyable bridge deck cells ('b').
        self.span_cells = set()

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

    def destroyable(self, cells):
        """ADR-025: promote already-placed bridge cells to DESTROYABLE decks
        ('b'). Plain 'B' stays the permanent, indestructible crossing, so a map
        opts in span by span and nothing existing changes meaning.

        Pass the return value of a bridges() call, or a subset of it. Every cell
        must already be a bridge: promoting open ground would put a deck over
        dry land. validate() then proves the map survives losing ALL of them at
        once, which is what makes severing impossible to author by accident."""
        for (x, y) in sorted(cells):
            assert self.grid[y][x] in 'Bb', \
                f"destroyable cell {(x, y)} is '{self.grid[y][x]}', not a bridge"
            self.grid[y][x] = 'b'
            self.span_cells.add((x, y))

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

    def decor(self, x0, y0, dx, dy, ch, fill=1):
        """Dress a dx-by-dy patch and its rotation image with a DECORATIVE
        character. Passable, drawn, and invisible to every invariant.

        Writes only over OPEN ground ('.'), never over terrain, ferrite, a
        bridge deck or an apron - decoration must never overwrite something
        that means anything, and a decor cell that landed on a ferrite cell
        would silently cost the map part of its economy. `fill` thins the patch
        deterministically (every nth cell by index, no randomness in a
        generator whose output is committed), so a patch can read as scattered
        scrub rather than a solid rectangle.
        """
        assert ch in DECOR, f"'{ch}' is not a decorative character"
        n = 0
        for y in range(y0, y0 + dy):
            for x in range(x0, x0 + dx):
                n += 1
                if fill > 1 and n % fill: continue
                if not self.inb(x, y):
                    continue
                rx, ry = rot(x, y, self.w, self.h)
                # Both halves must be free, exactly as stamp() requires, or the
                # pair lands half-placed and the map stops being symmetric.
                if self.grid[y][x] != '.' or self.grid[ry][rx] != '.':
                    continue
                if (x, y) in self.apron_cells or (rx, ry) in self.apron_cells:
                    continue
                self.grid[y][x] = ch
                self.grid[ry][rx] = ch

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

    def outpost(self, ax, ay):
        """ADR-021: place a neutral capturable Outpost and its rotation image.
        (ax, ay) is the TOP-LEFT anchor of the 2x2 footprint, the anchor
        convention World.SpawnOutpost takes. Both footprints must be wholly
        open, outside every apron (a base must not start owning one) and clear
        of the load-bearing crossings (an outpost is 2x2 and would part-seal a
        pass it stood in). The rotation image of the block anchored at (ax, ay)
        is anchored at rot(ax+1, ay+1), the min corner of the rotated cells."""
        rax, ray = rot(ax + 1, ay + 1, self.w, self.h)
        for (bx, by) in ((ax, ay), (rax, ray)):
            for y in range(by, by + 2):
                for x in range(bx, bx + 2):
                    assert self.inb(x, y), f"outpost ({bx},{by}) runs off-map at {(x, y)}"
                    assert self.grid[y][x] == '.', \
                        f"outpost cell {(x, y)} is '{self.grid[y][x]}', not open"
                    assert (x, y) not in self.apron_cells, \
                        f"outpost cell {(x, y)} sits in a start apron: a base would own it for free"
                    assert (x, y) not in self.choke_cells, \
                        f"outpost cell {(x, y)} sits on a load-bearing crossing"
        assert (ax, ay) != (rax, ray), "an outpost placed on the map centre is its own mirror"
        self.outposts.extend([(ax, ay), (rax, ray)])

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

    def validate(self, expected_fields, density_range, min_separation=None):
        w, h, grid = self.w, self.h, self.grid
        assert len(grid) == h
        for row in grid:
            assert len(row) == w

        # 0. Start separation. This guard exists because a map can pass every
        #    other check in this file and still be broken: skirmish-05's first
        #    draft was perfectly symmetric, perfectly reachable, perfectly fair,
        #    and an accidental rush map, because its starts were 47 cells apart
        #    where the rest of the pool sits near 75. Measured AI-vs-AI it read
        #    37 against 2 on approach distance and 8 against 17 on peak army:
        #    whichever commander crossed first arrived before the other had an
        #    army, and the match was decided there.
        #
        #    That fix lived only as a comment on one generator, so nothing
        #    stopped the same mistake landing silently on the next map. It is a
        #    rule now. The default scales with the map rather than being the
        #    literal 75 that suited 96x64, because separation is only meaningful
        #    relative to the ground it crosses.
        sep = max(abs(self.starts[0][0] - self.starts[1][0]),
                  abs(self.starts[0][1] - self.starts[1][1]))
        floor = min_separation if min_separation is not None else int(0.7 * max(w, h))
        assert sep >= floor, (
            f"starts are {sep} cells apart (Chebyshev), below the {floor} this map size wants. "
            f"A short run makes whoever attacks first the winner before the other has an army; "
            f"pass min_separation= explicitly if a rush map is the INTENT.")

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

        # 8. ADR-021 outposts. They are entities, not terrain, so they are absent
        #    from the grid and from the density above; what has to be proved is
        #    that the map still works WITH them standing, because the sim blocks
        #    each 2x2 footprint the moment it spawns. Block them here and re-run
        #    the reachability proof: an outpost that seals a lane or walls off a
        #    field would otherwise only be discovered in a match.
        if self.outposts:
            assert len(self.outposts) % 2 == 0, "outposts must be placed in rotation pairs"
            saved = []
            for (ax, ay) in self.outposts:
                for y in range(ay, ay + 2):
                    for x in range(ax, ax + 2):
                        saved.append((x, y, grid[y][x]))
                        grid[y][x] = '#'
            try:
                for p, s in self.starts.items():
                    seen = self._flood(*s)
                    for f in fields:
                        assert f in seen, f"with outposts standing, player {p} cannot reach ferrite at {f}"
                    assert self.starts[1 - p] in seen, \
                        f"with outposts standing, player {p} cannot reach the far start"
                    for c in self.apron_cells:
                        assert c in seen, f"with outposts standing, player {p} cannot reach apron cell {c}"
            finally:
                for (x, y, ch) in saved:
                    grid[y][x] = ch
            # Fairness: the same Chebyshev-profile rule the ferrite obeys. An
            # outpost nearer one base is a free income lead.
            #
            # Measured to the footprint CENTRE, not the anchor, and in DOUBLED
            # integers so it stays exact. Ferrite can use the cell itself
            # because rotation maps a single cell onto a single cell, but the
            # 180-rotation of a 2x2 block maps its top-left anchor onto the
            # rotated block's BOTTOM-RIGHT, so anchor distances differ by one
            # between the two starts even when the placement is perfectly
            # symmetric. The centre is what the sim itself uses
            # (World.FootprintCentre) and it is the only measure that rotates
            # cleanly. (Written the naive way first; this check caught it.)
            def cheb_out(s):
                return sorted(max(abs((2 * x + 1) - 2 * s[0]), abs((2 * y + 1) - 2 * s[1]))
                              for x, y in self.outposts)
            assert cheb_out(self.starts[0]) == cheb_out(self.starts[1]), \
                "outpost distance profiles differ between starts"

        # 9. ADR-025: the map must survive losing EVERY destroyable span at
        #    once. A rubbled bridge is a neutral blocker, so the DEF-05 breach
        #    path will not fire against it (that path wants an enemy-owned
        #    barrier) and an attack-move across a fully severed river would go
        #    inert, halting the AI's waves. Rather than widen a sim predicate
        #    every golden exercises, the severing is made impossible to author:
        #    block every span and re-prove the map still connects.
        if self.span_cells:
            for (x, y) in self.span_cells:
                assert grid[y][x] == 'b', f"span cell {(x, y)} is '{grid[y][x]}', not a destroyable deck"
            saved = [(x, y, grid[y][x]) for (x, y) in self.span_cells]
            for (x, y, _) in saved:
                grid[y][x] = '#'
            try:
                for p_, s_ in self.starts.items():
                    seen = self._flood(*s_)
                    assert self.starts[1 - p_] in seen, \
                        "with every destroyable bridge rubbled the starts are severed: the AI would halt at the bank"
                    for f in fields:
                        assert f in seen, \
                            f"with every destroyable bridge rubbled player {p_} cannot reach ferrite at {f}"
            finally:
                for (x, y, ch) in saved:
                    grid[y][x] = ch
            # Fairness: spans are a rotation-symmetric set like everything else.
            for (x, y) in self.span_cells:
                assert rot(x, y, w, h) in self.span_cells, \
                    f"destroyable span {(x, y)} has no rotation partner: one player can cut a crossing the other cannot"

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
        # ADR-021: entity lines follow the grid, the mission-map convention.
        # Player -1 is neutral (the ferrite-field convention) and 13 is the
        # outpost struct type; the loader's structure line already parses a
        # negative player, so this needs no map-format bump.
        for (ax, ay) in self.outposts:
            lines.append(f"structure -1 13 {ax} {ay}")
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
    # Reported beside the blocked figure on purpose: the two answer different
    # questions and confusing them is how the pool ended up bare. Blocked is
    # what the density CAP governs and what pathing pays for; decorated is how
    # much of the map is actually drawn, and it is free.
    dec = sum(1 for row in canvas.grid for ch in row if ch in DECOR)
    if dec:
        print(f"  decorated: {dec} cells = {dec / (canvas.w * canvas.h) * 100:.2f}% "
              f"(passable; outside the density budget)")
    print(f"  ferrite: {len(fields)} cells = {len(fields) * 12000:,} credits")
    print(f"  starts:  {canvas.starts[0]} and {canvas.starts[1]}, "
          f"apron {canvas.apron * 2 + 1}x{canvas.apron * 2 + 1}")
    print(f"  crossings: {crossings}")
    if canvas.span_cells:
        print(f"  spans:   {len(canvas.span_cells)} destroyable bridge decks (ADR-025)")
    if canvas.outposts:
        print(f"  outposts: {len(canvas.outposts)} neutral (ADR-021) at {canvas.outposts}")
    print("  all symmetry, density, reachability, crossing and fairness checks passed")
