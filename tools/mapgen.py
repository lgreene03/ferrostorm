#!/usr/bin/env python3
"""Shared machinery for the skirmish-map generators (TICKET-P6-MAP-01).

The maps are generated rather than hand-typed because their fairness invariant
is mechanical: every feature must be placed as its full ORBIT under the map's
symmetry group, the group whose members carry each start onto the others
exactly. A human editing thousands of characters cannot hold that invariant; a
script can, and can then prove it. This module is the single place the invariant
lives, so it is defined once and every generator inherits the same proof.

The group is named, not hardcoded (P7-8b). "rot180" is the identity and the
180-degree rotation, an orbit of two, and it is the default because it is what
every two-start map in the pool is built on and proved against. "mirror2" adds
the two centre-line mirrors for an orbit of four, which is what a four-start map
needs. A one-member group containing only the identity is the asymmetric mission
mode: the same code path, not a boolean special case, so the modes cannot drift.

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


def ident(x, y, w, h):
    """The identity. Every group contains it, so the authored cell is always a
    member of its own orbit and a mutator never has to special-case it."""
    return x, y


def rot180(x, y, w, h):
    """(x,y) -> (w-1-x, h-1-y): the 180-degree rotation about the map centre.
    A sine centred on the map centre is symmetric under it by construction, but
    rounding to integer cells can slip a cell, so every feature below is placed
    as an explicit orbit and then re-proved cell by cell rather than trusted."""
    return w - 1 - x, h - 1 - y


def mirror_v(x, y, w, h):
    """Reflection across the VERTICAL centre line: left half onto right half."""
    return w - 1 - x, y


def mirror_h(x, y, w, h):
    """Reflection across the HORIZONTAL centre line: top half onto bottom."""
    return x, h - 1 - y


# The symmetry groups. Each is an ordered tuple of transforms with the identity
# FIRST, because the order is observable: outpost() emits one entity line per
# orbit member in group order, so reordering a group would rewrite committed
# maps. Every group is closed under composition, which is what makes "the orbit
# of a cell" a well-defined set rather than a list that depends on where you
# started.
#
# There is deliberately no 90-degree rotation group. A quarter turn maps a
# w-by-h rectangle onto an h-by-w one, so it is only a symmetry of a SQUARE map,
# and not one of the eight shipped maps is square (96x64, 128x96, 192x128,
# 256x192). Adding it would mean a group that silently fails on every map in the
# pool. Its absence is a decision, not an oversight; a square map wanting an
# orbit of four uses mirror2, which works on any rectangle.
SYMMETRY_GROUPS = {
    "none": (ident,),                             # asymmetric: mission maps
    "rot180": (ident, rot180),                    # orbit 2: the two-start pool
    "mirror2": (ident, mirror_v, mirror_h, rot180),   # orbit 4: four starts
}


class Canvas:
    """A grid under construction. Every mutator writes a cell together with the
    whole of its orbit under the map's symmetry group, so the grid is symmetric
    by construction; validate() then proves it, along with density,
    reachability, load-bearing crossings and ferrite fairness."""

    def __init__(self, w, h, starts, apron=4, symmetric=True, symmetry="rot180"):
        self.w, self.h = w, h
        self.starts = dict(starts)          # {0:(x,y), 1:(x,y), ...}
        self.apron = apron
        # symmetric=False is the asymmetric mission mode and it selects the
        # one-member group rather than switching off a code path, so there is a
        # single orbit machine underneath both and the two cannot drift apart.
        assert symmetry in SYMMETRY_GROUPS, \
            f"unknown symmetry '{symmetry}': the groups are {sorted(SYMMETRY_GROUPS)}"
        self.symmetry = symmetry if symmetric else "none"
        self.group = SYMMETRY_GROUPS[self.symmetry]
        # "symmetric" now means "this map has a fairness symmetry to prove",
        # which is exactly "its group is bigger than the identity". Kept as a
        # plain attribute because report() and the generators read it.
        self.symmetric = len(self.group) > 1
        self.grid = [['.' for _ in range(w)] for _ in range(h)]
        if self.symmetric:
            # The starts must themselves BE an orbit, or nothing built on top of
            # the symmetry can rescue the fairness.
            assert sorted(self.starts) == list(range(len(self.starts))), \
                f"start ids must be 0..n-1, got {sorted(self.starts)}"
            want = sorted({t(*self.starts[0], w, h) for t in self.group})
            got = sorted(self.starts.values())
            assert got == want, (
                f"the starts {got} are not the orbit of start 0 under '{self.symmetry}': "
                f"that group expects exactly {want}")
            # Start ORDER is a requirement, not a detail. rot180 is a member of
            # every group here, so seats 0 and 1 being a 180-degree pair (and 2
            # and 3 being the other one) makes a TWO-player game on a four-start
            # map exactly as fair as a game on any two-start map in the pool.
            # That is what makes it safe to offer a mirror2 map in the menu
            # while the lobby still only expresses two seats: the two seats it
            # fills are a rotation pair and the spare starts go unused.
            if rot180 in self.group:
                for i in range(0, len(self.starts) - 1, 2):
                    assert rot180(*self.starts[i], w, h) == self.starts[i + 1], \
                        (f"starts {i} {self.starts[i]} and {i + 1} {self.starts[i + 1]} are not a "
                         f"180-rotation pair: a two-player game seated at 0 and 1 would be unfair")
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

    def _imgs(self, x, y):
        """The cells a single authored cell stands for: its full ORBIT under the
        map's symmetry group, written together so an orbit can never land part
        placed. On a rot180 map that is the cell and its rotation image, on a
        mirror2 map the four quadrant images, on an asymmetric mission map the
        cell alone. Every mutator goes through this, so the modes cannot drift.

        De-duplicated and sorted. A cell sitting ON a mirror axis is its own
        image under that mirror, and writing it twice would be harmless for a
        grid write but not for the counting that cluster() and the budgets do.
        Sorted so the order is a property of the cell rather than of the group's
        internal order, which keeps failure messages stable."""
        return tuple(sorted({t(x, y, self.w, self.h) for t in self.group}))

    # -- rivers -----------------------------------------------------------
    def river(self, centre_fn, halfwidth_fn, vertical=True):
        """Mark a winding river as water. centre_fn(t) and halfwidth_fn(t) take
        the along-axis coordinate (rows for a vertical river) and return the
        cross-axis centre and half-width in cells. The river is closed under the
        symmetry group before it is written, so a sine that rounds a cell
        off-centre is corrected rather than left to bias one bank."""
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
            sym.update(self._imgs(x, y))
        for (x, y) in sym:
            assert (x, y) not in self.apron_cells, f"river runs through an apron at {(x, y)}"
            self.grid[y][x] = 'w'
        self.river_cells |= sym
        self._vertical = vertical
        return sym

    def bridges(self, bands):
        """Turn the river cells inside each along-axis band into bridge decks.
        bands is a list of (t0, t1) half-open ranges. Closed under the symmetry
        group, so three bridges are three orbits of identical crossings. These
        become the load-bearing crossings validate() proves are the ONLY way
        across."""
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
            sym.update(self._imgs(x, y))
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
        """Write a dx-by-dy rectangle and its whole orbit. A cell is written
        only if EVERY member of its orbit is free of water, bridge and apron, so
        an orbit can never land part placed, the failure mode that quietly hands
        one player an advantage. If choke=True the cells are recorded as a
        crossing whose removal validate() will require to disconnect the map
        (used to prove a ridge's passes are load-bearing)."""
        for y in range(y0, y0 + dy):
            for x in range(x0, x0 + dx):
                if not self.inb(x, y):
                    continue
                imgs = self._imgs(x, y)
                if any(self.grid[iy][ix] in 'wB' for (ix, iy) in imgs):
                    continue
                if any(i in self.apron_cells for i in imgs):
                    continue
                for (ix, iy) in imgs:
                    self.grid[iy][ix] = ch
                    if choke:
                        self.choke_cells.add((ix, iy))

    def decor(self, x0, y0, dx, dy, ch, fill=1):
        """Dress a dx-by-dy patch and its whole orbit with a DECORATIVE
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
                imgs = self._imgs(x, y)
                # Every image must be free, exactly as stamp() requires, or the
                # orbit lands part placed and the map stops being symmetric.
                if any(self.grid[iy][ix] != '.' for (ix, iy) in imgs):
                    continue
                if any(i in self.apron_cells for i in imgs):
                    continue
                for (ix, iy) in imgs:
                    self.grid[iy][ix] = ch

    def mark_pass(self, cells):
        """Record open cells as a load-bearing pass through a ridge: validate()
        proves that blocking them disconnects every start from every other."""
        for (x, y) in cells:
            self.choke_cells.update(self._imgs(x, y))

    def cluster(self, cx, cy, shape):
        """Place a ferrite field and its whole orbit. Every cell of every orbit
        must be open, or the orbit would part land and break both the budget
        and the symmetry, so fail loudly instead."""
        for dx, dy in shape:
            x, y = cx + dx, cy + dy
            assert self.inb(x, y), f"cluster ({cx},{cy}) runs off-map at {(x, y)}"
            imgs = self._imgs(x, y)
            for (ix, iy) in imgs:
                assert self.grid[iy][ix] == '.', \
                    f"cluster cell {(ix, iy)} is '{self.grid[iy][ix]}', not open"
            for (ix, iy) in imgs:
                self.grid[iy][ix] = 'F'

    def _block_anchor(self, t, ax, ay, size=2):
        """The TOP-LEFT anchor of the image of a size-by-size block under one
        transform. This is the one place where an orbit is not just "apply the
        transform to the cell": a block is named by its min corner, and a
        transform that flips an axis carries the min corner of that axis onto
        the MAX corner of the image, so the anchor moves by size-1 along every
        flipped axis. Derived rather than tabulated - both opposite corners are
        transformed and the componentwise minimum taken - because a per-group
        table of offsets is exactly the thing that goes silently wrong by one
        cell when a group is added, and a one-cell shift can still pass the
        distance-profile check."""
        x0, y0 = t(ax, ay, self.w, self.h)
        x1, y1 = t(ax + size - 1, ay + size - 1, self.w, self.h)
        return min(x0, x1), min(y0, y1)

    def outpost(self, ax, ay):
        """ADR-021: place a neutral capturable Outpost and its whole orbit.
        (ax, ay) is the TOP-LEFT anchor of the 2x2 footprint, the anchor
        convention World.SpawnOutpost takes. Every footprint must be wholly
        open, outside every apron (a base must not start owning one) and clear
        of the load-bearing crossings (an outpost is 2x2 and would part-seal a
        pass it stood in). The image anchors come from _block_anchor: under
        rot180 the image of the block at (ax, ay) is anchored at
        rot180(ax+1, ay+1), the min corner of the rotated cells, and under a
        mirror the anchor moves along the flipped axis only."""
        anchors = tuple(self._block_anchor(t, ax, ay) for t in self.group)
        for (bx, by) in anchors:
            for y in range(by, by + 2):
                for x in range(bx, bx + 2):
                    assert self.inb(x, y), f"outpost ({bx},{by}) runs off-map at {(x, y)}"
                    assert self.grid[y][x] == '.', \
                        f"outpost cell {(x, y)} is '{self.grid[y][x]}', not open"
                    assert (x, y) not in self.apron_cells, \
                        f"outpost cell {(x, y)} sits in a start apron: a base would own it for free"
                    assert (x, y) not in self.choke_cells, \
                        f"outpost cell {(x, y)} sits on a load-bearing crossing"
        assert len(set(anchors)) == len(self.group), \
            (f"outpost at {(ax, ay)} has an orbit of {len(set(anchors))} under '{self.symmetry}', "
             f"not {len(self.group)}: it sits on a symmetry axis and is its own image, "
             f"so one commander would get a copy the others do not")
        self.outposts.extend(anchors)

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

    def validate(self, expected_fields, density_range, min_separation=None, objectives=()):
        """Prove the map. `objectives` is the mission-map addition: cells every
        start must be able to walk to. A skirmish map's objectives are implicit
        (the far starts, the ferrite, the aprons) and the symmetry makes them
        fair; a mission map has NO far start and no fairness to prove, so what
        has to be checked instead is that the thing the script tells the player
        to go and do is somewhere they can actually reach."""
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
        #
        #    A mission map has one player start and a scripted opponent, so
        #    there is no second start to be separated FROM and the guard has
        #    nothing to say. It is skipped rather than weakened.
        #
        #    With more than two starts it is the MINIMUM pairwise separation
        #    that has to clear the floor, not the separation of some chosen
        #    pair. On a four-start map the diagonal pairs are far apart by
        #    construction while the adjacent ones are not, so measuring only
        #    seats 0 and 1 would pass a map on which seats 0 and 2 are
        #    neighbours - the same accidental rush, hidden behind a comfortable
        #    number. For two starts there is one pair and the meaning is
        #    unchanged.
        if self.symmetric:
            pairs = [(p, q) for p in sorted(self.starts) for q in sorted(self.starts) if p < q]
            sep, near = min(
                (max(abs(self.starts[p][0] - self.starts[q][0]),
                     abs(self.starts[p][1] - self.starts[q][1])), (p, q))
                for (p, q) in pairs)
            floor = min_separation if min_separation is not None else int(0.7 * max(w, h))
            assert sep >= floor, (
                f"starts {near[0]} and {near[1]} are {sep} cells apart (Chebyshev), the closest pair on "
                f"the map and below the {floor} this map size wants. "
                f"A short run makes whoever attacks first the winner before the other has an army; "
                f"pass min_separation= explicitly if a rush map is the INTENT.")

        # 1. Symmetry of blocked cells, fields and bridges, cell by cell, over
        #    the WHOLE ORBIT of each cell rather than one rotation partner.
        #    This is the FAIRNESS invariant and it is meaningless on a mission
        #    map: a campaign mission is asymmetric on purpose - the player and
        #    the scripted enemy are not meant to be evenly matched, and a
        #    mirrored mission would be a skirmish with dialogue.
        #
        #    Every member of the orbit is checked against the authored cell, so
        #    an orbit of four is proved to agree four ways rather than pairwise
        #    round a cycle. Under rot180 the orbit is the cell and its rotation
        #    image, so this is the identical check it has always been.
        if self.symmetric:
            for y in range(h):
                for x in range(w):
                    a = grid[y][x]
                    for (ix, iy) in self._imgs(x, y):
                        b = grid[iy][ix]
                        assert (a in BLOCKING) == (b in BLOCKING), \
                            f"blocked asymmetry at {(x, y)}: its orbit image {(ix, iy)} disagrees"
                        assert (a == 'F') == (b == 'F'), \
                            f"ferrite asymmetry at {(x, y)}: its orbit image {(ix, iy)} disagrees"
                        assert (a == 'B') == (b == 'B'), \
                            f"bridge asymmetry at {(x, y)}: its orbit image {(ix, iy)} disagrees"

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
            # 5b. Mission objectives. The one reachability question a mission
            #     map has that a skirmish map does not: a scripted objective
            #     the player cannot walk to is a mission that cannot be
            #     finished, and it would only be found by playing it.
            for o in objectives:
                assert o in seen, f"player {p} cannot reach objective cell {o}"

        # 6. The crossings are load-bearing: close them and prove the starts
        #    fall into separate components. A river without this is decoration.
        #
        #    Checked over EVERY ordered pair of starts, not just the first two.
        #    A crossing set that severs seats 0 and 1 while leaving seats 0 and
        #    2 joined by a lane round the end of the ridge is exactly the map
        #    this check exists to reject, and on a two-start map there is one
        #    pair, so the meaning is unchanged.
        #
        #    A mission map has no second start to be cut off from, so the same
        #    proof is restated against what the mission actually cares about:
        #    close the crossings and the OBJECTIVE must become unreachable.
        #    Without this the check simply vanished on a one-start map, and a
        #    ridge with an accidental gap round the end of it would have been
        #    described in the generator's own report as a pass to be fought
        #    over while the army quietly walked past it.
        if self.choke_cells:
            saved = [(x, y, grid[y][x]) for (x, y) in self.choke_cells]
            for (x, y, _) in saved:
                grid[y][x] = '#'
            try:
                if len(self.starts) >= 2:
                    for p, s in sorted(self.starts.items()):
                        seen = self._flood(*s)
                        for q, s2 in sorted(self.starts.items()):
                            if q == p:
                                continue
                            assert s2 not in seen, (
                                f"starts {p} and {q} stay connected with every crossing closed: "
                                f"the crossings are not load-bearing")
                elif objectives:
                    seen = self._flood(*next(iter(self.starts.values())))
                    for o in objectives:
                        assert o not in seen, (
                            f"objective {o} is still reachable with every crossing closed: "
                            f"the crossings are not load-bearing and there is a way round")
            finally:
                for (x, y, ch) in saved:
                    grid[y][x] = ch

        # 7. Chebyshev-distance fairness: the multiset of distances from EVERY
        #    start to all fields must be identical, or one player is closer to
        #    the economy. The symmetry guarantees it; this proves it held. Each
        #    start is compared against start 0's profile, so an orbit of four is
        #    held to one shared standard rather than checked round a ring, and
        #    with two starts it is the same single comparison as before.
        if self.symmetric:
            def cheb(s):
                return sorted(max(abs(x - s[0]), abs(y - s[1])) for x, y in fields)
            base = cheb(self.starts[0])
            for p, s in sorted(self.starts.items()):
                assert cheb(s) == base, \
                    f"ferrite distance profile of start {p} at {s} differs from start 0's"

        # 8. ADR-021 outposts. They are entities, not terrain, so they are absent
        #    from the grid and from the density above; what has to be proved is
        #    that the map still works WITH them standing, because the sim blocks
        #    each 2x2 footprint the moment it spawns. Block them here and re-run
        #    the reachability proof: an outpost that seals a lane or walls off a
        #    field would otherwise only be discovered in a match.
        if self.outposts:
            if self.symmetric:
                assert len(self.outposts) % len(self.group) == 0, \
                    (f"{len(self.outposts)} outposts is not a whole number of orbits under "
                     f"'{self.symmetry}' (orbit size {len(self.group)}): outposts must be placed "
                     f"as complete orbits or one commander gets one the others do not")
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
                    for q, s2 in self.starts.items():
                        assert s2 in seen, \
                            f"with outposts standing, player {p} cannot reach start {q}"
                    for c in self.apron_cells:
                        assert c in seen, f"with outposts standing, player {p} cannot reach apron cell {c}"
                    for o in objectives:
                        assert o in seen, \
                            f"with outposts standing, player {p} cannot reach objective cell {o}"
            finally:
                for (x, y, ch) in saved:
                    grid[y][x] = ch
            # Fairness: the same Chebyshev-profile rule the ferrite obeys. An
            # outpost nearer one base is a free income lead.
            #
            # Measured to the footprint CENTRE, not the anchor, and in DOUBLED
            # integers so it stays exact. Ferrite can use the cell itself
            # because a transform maps a single cell onto a single cell, but the
            # 180-rotation of a 2x2 block maps its top-left anchor onto the
            # rotated block's BOTTOM-RIGHT, so anchor distances differ by one
            # between the two starts even when the placement is perfectly
            # symmetric. A mirror does the same along the axis it flips. The
            # centre is what the sim itself uses (World.FootprintCentre) and it
            # is the only measure that transforms cleanly. (Written the naive
            # way first; this check caught it.) It is also the check that would
            # catch a wrong image ANCHOR under a mirror, which is why the
            # anchors are derived from the block's corners in _block_anchor
            # rather than written out per group.
            if self.symmetric:
                def cheb_out(s):
                    return sorted(max(abs((2 * x + 1) - 2 * s[0]), abs((2 * y + 1) - 2 * s[1]))
                                  for x, y in self.outposts)
                base_out = cheb_out(self.starts[0])
                for p, s in sorted(self.starts.items()):
                    assert cheb_out(s) == base_out, \
                        f"outpost distance profile of start {p} at {s} differs from start 0's"

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
                    for q_, s2_ in self.starts.items():
                        assert s2_ in seen, \
                            "with every destroyable bridge rubbled the starts are severed: the AI would halt at the bank"
                    for f in fields:
                        assert f in seen, \
                            f"with every destroyable bridge rubbled player {p_} cannot reach ferrite at {f}"
                    for o in objectives:
                        assert o in seen, \
                            f"with every destroyable bridge rubbled player {p_} cannot reach objective cell {o}"
            finally:
                for (x, y, ch) in saved:
                    grid[y][x] = ch
            # Fairness: the spans are a closed set under the symmetry, like
            # everything else. Every member of a span's orbit must itself be a
            # span, or one commander can cut a crossing the others cannot.
            if self.symmetric:
                for (x, y) in sorted(self.span_cells):
                    for (ix, iy) in self._imgs(x, y):
                        assert (ix, iy) in self.span_cells, \
                            (f"destroyable span {(x, y)} has no partner at its orbit image {(ix, iy)}: "
                             f"one player can cut a crossing the others cannot")

        return fields, blocked, density

    def check_entities(self, lines, struct_footprint=2):
        """Prove every mission entity stands on ground it can stand on.

        The entity and trigger section is raw text, because the vocabulary
        belongs to the map format and this generator has no business owning a
        second copy of it. That leaves exactly one thing unchecked, and it is
        the thing a hand-typed mission gets wrong: a building anchored half
        inside a ridge, or a squad placed in a river. Parse the lines back and
        assert the cells are open - a structure over its whole 2x2 footprint,
        a unit over its cell - and report how many were proved, so a silent
        parse failure cannot masquerade as a pass."""
        checked = 0
        for line in lines:
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            p = line.split()
            if p[0] not in ('unit', 'structure'):
                continue        # trigger lines address cells, not occupy them
            cx, cy = int(p[3]), int(p[4])
            span = struct_footprint if p[0] == 'structure' else 1
            for y in range(cy, cy + span):
                for x in range(cx, cx + span):
                    assert self.inb(x, y), f"{p[0]} at {(cx, cy)} runs off-map at {(x, y)}"
                    ch = self.grid[y][x]
                    assert ch not in BLOCKING, \
                        f"{p[0]} at {(cx, cy)} stands on '{ch}' at {(x, y)}: blocked ground"
                    assert ch != 'F', \
                        f"{p[0]} at {(cx, cy)} stands on ferrite at {(x, y)}: it would sit on the economy"
            checked += 1
        return checked

    # -- emit -------------------------------------------------------------
    def emit(self, path, header_lines, version=1, pre_grid=(), post_grid=()):
        """Write the map. `version` selects the format line; `pre_grid` carries
        header directives a mission needs and a skirmish map does not (faction,
        rules), and `post_grid` carries its entity and trigger lines. Both are
        raw lines, because the mission vocabulary is the map format's and this
        generator has no business owning a second copy of it."""
        lines = [f"ferrostorm-map v{version}"]
        lines.extend(header_lines)
        lines.append(f"size {self.w} {self.h}")
        for p, (cx, cy) in sorted(self.starts.items()):
            lines.append(f"start {p} {cx} {cy}")
        lines.extend(pre_grid)
        lines.append("grid:")
        lines.extend("".join(row) for row in self.grid)
        # ADR-021: entity lines follow the grid, the mission-map convention.
        # Player -1 is neutral (the ferrite-field convention) and 13 is the
        # outpost struct type; the loader's structure line already parses a
        # negative player, so this needs no map-format bump.
        for (ax, ay) in self.outposts:
            lines.append(f"structure -1 13 {ax} {ay}")
        lines.extend(post_grid)
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
    print(f"  starts:  {', '.join(str(s) for _, s in sorted(canvas.starts.items()))}, "
          f"apron {canvas.apron * 2 + 1}x{canvas.apron * 2 + 1}")
    print(f"  crossings: {crossings}")
    if canvas.span_cells:
        print(f"  spans:   {len(canvas.span_cells)} destroyable bridge decks (ADR-025)")
    if canvas.outposts:
        print(f"  outposts: {len(canvas.outposts)} neutral (ADR-021) at {canvas.outposts}")
    if canvas.symmetric:
        if canvas.symmetry != "rot180":
            print(f"  symmetry: '{canvas.symmetry}', orbit size {len(canvas.group)} "
                  f"(seats 0 and 1 are the 180-degree pair)")
        print("  all symmetry, density, reachability, crossing and fairness checks passed")
    else:
        print("  asymmetric MISSION map: density, reachability and objective-reachability "
              "checks passed (symmetry and fairness do not apply and were not claimed)")
