#!/usr/bin/env python3
"""Ferrostorm contextual cursor generator (TICKET-P6-CURSOR-01).

Renders the eight battle cursors as 32x32 RGBA PNGs into game/ui/cursors/.
Pure standard library (struct, zlib, math): shapes are composed from a few
signed-membership primitives, evaluated 4x supersampled and box-filtered
down, so the edges read cleanly at pointer size without any image library.

Palette: doc 16 tokens only. Ferrite gold and bone over a cinder outline,
and the invalid strike in the health-bar red the HUD already owns. No new
colours.

Run:  python3 art/cursors/gen_cursors.py
"""

import math
import os
import struct
import zlib

SIZE = 32          # output pixels
SS = 4             # supersample factor

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(HERE, "..", "..", "game", "ui", "cursors"))

GOLD = (201, 161, 92)    # FerriteGold (0.79, 0.63, 0.36)
BONE = (214, 209, 196)   # Bone (0.84, 0.82, 0.77)
RED = (217, 71, 51)      # FillRed (0.85, 0.28, 0.20), the health-bar red
DARK = (14, 15, 17)      # Cinder (0.055, 0.06, 0.065), the outline


# ---------------------------------------------------------------------------
# Membership primitives (logical 32x32 coordinate space, y down)
# ---------------------------------------------------------------------------

def _seg(x, y, ax, ay, bx, by, r):
    """Within r of the segment a-b (a thick line with round caps)."""
    vx, vy = bx - ax, by - ay
    wx, wy = x - ax, y - ay
    ll = vx * vx + vy * vy
    t = 0.0 if ll == 0 else max(0.0, min(1.0, (wx * vx + wy * vy) / ll))
    dx, dy = x - (ax + vx * t), y - (ay + vy * t)
    return dx * dx + dy * dy <= r * r


def _disc(x, y, cx, cy, rr):
    dx, dy = x - cx, y - cy
    return dx * dx + dy * dy <= rr * rr


def _ring(x, y, cx, cy, rad, r):
    """Within r of the circle of radius rad."""
    d = math.hypot(x - cx, y - cy)
    return abs(d - rad) <= r


def _poly(x, y, pts):
    """Even-odd point-in-polygon."""
    inside = False
    n = len(pts)
    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        if (y1 > y) != (y2 > y):
            xi = x1 + (y - y1) * (x2 - x1) / (y2 - y1)
            if x < xi:
                inside = not inside
    return inside


def _poly_edge(x, y, pts, r):
    """Within r of any polygon edge (the outline pass for _poly)."""
    n = len(pts)
    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        if _seg(x, y, x1, y1, x2, y2, r):
            return True
    return False


# ---------------------------------------------------------------------------
# Rendering and the stdlib PNG writer
# ---------------------------------------------------------------------------

def render(layers):
    """Evaluate paint layers (colour, test) in order, later layers on top,
    at SS x SS subsamples per pixel; box filter to 32x32 RGBA rows."""
    rows = []
    for py in range(SIZE):
        row = []
        for px in range(SIZE):
            r = g = b = 0.0
            covered = 0
            for sy in range(SS):
                for sx in range(SS):
                    x = px + (sx + 0.5) / SS
                    y = py + (sy + 0.5) / SS
                    hit = None
                    for colour, test in layers:
                        if test(x, y):
                            hit = colour
                    if hit is not None:
                        r += hit[0]
                        g += hit[1]
                        b += hit[2]
                        covered += 1
            if covered == 0:
                row.append((0, 0, 0, 0))
            else:
                row.append((int(r / covered), int(g / covered), int(b / covered),
                            int(255 * covered / (SS * SS))))
        rows.append(row)
    return rows


def write_png(name, rows):
    raw = b"".join(
        b"\x00" + b"".join(bytes(px) for px in row) for row in rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))
    path = os.path.join(OUT_DIR, name)
    with open(path, "wb") as f:
        f.write(png)
    opaque = sum(1 for row in rows for px in row if px[3] > 0)
    print("%-20s %4d painted px" % (name, opaque))


def outlined(colour, tests, width=1.3):
    """The house style: every shape wears the cinder outline under its
    colour, so both read on light ground and dark. `tests` maps a widening
    amount to a membership test, so the outline is the same geometry grown
    by `width`."""
    return [(DARK, tests(width)), (colour, tests(0.0))]


# ---------------------------------------------------------------------------
# The eight cursors
# ---------------------------------------------------------------------------

def cursor_select():
    """Arrow pointer, tip at the hotspot (2, 2). Bone: select is the neutral
    verb."""
    pts = [(2, 2), (2, 21), (8, 16), (12, 25), (16, 23), (12, 15), (19, 14)]

    def tests(w):
        return lambda x, y: _poly(x, y, pts) or _poly_edge(x, y, pts, 0.4 + w)
    return outlined(BONE, tests)


def cursor_move():
    """Four-arrow move, hotspot centre. Gold: an order verb."""
    c = 16.0

    def tests(w):
        def t(x, y):
            for ddx, ddy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
                tipx, tipy = c + ddx * 12, c + ddy * 12
                bx, by = c + ddx * 4, c + ddy * 4
                if _seg(x, y, bx, by, tipx, tipy, 1.1 + w):
                    return True
                px, py = -ddy, -ddx   # perpendicular
                hx, hy = c + ddx * 7.5, c + ddy * 7.5
                head = [(tipx, tipy), (hx + px * 4, hy + py * 4), (hx - px * 4, hy - py * 4)]
                if _poly(x, y, head) or _poly_edge(x, y, head, 0.3 + w):
                    return True
            return False
        return t
    return outlined(GOLD, tests)


def cursor_attack():
    """Crosshair, hotspot centre: ring, four ticks, centre dot. Gold."""
    c = 16.0

    def tests(w):
        def t(x, y):
            if _ring(x, y, c, c, 8.0, 1.2 + w):
                return True
            for ddx, ddy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
                if _seg(x, y, c + ddx * 6, c + ddy * 6, c + ddx * 12.5, c + ddy * 12.5, 1.0 + w):
                    return True
            return _disc(x, y, c, c, 1.7 + w)
        return t
    return outlined(GOLD, tests)


def cursor_harvest():
    """Pickaxe, hotspot centre: an arced gold head across a bone handle."""
    def head(w):
        def t(x, y):
            # Downward-opening arc: points near a circle below the blade.
            d = math.hypot(x - 16.0, y - 24.0)
            return abs(d - 16.0) <= 1.6 + w and y <= 11.5 and 4.0 <= x <= 28.0
        return t

    def handle(w):
        return lambda x, y: _seg(x, y, 16, 7, 16, 27, 1.5 + w)
    return [(DARK, lambda x, y: head(1.3)(x, y) or handle(1.3)(x, y)),
            (BONE, handle(0.0)),
            (GOLD, head(0.0))]


def cursor_enter():
    """Door with an arrow through it, hotspot centre: the capture verb."""
    frame = [(12, 5), (26, 5), (26, 27), (12, 27)]

    def door(w):
        return lambda x, y: _poly_edge(x, y, frame, 1.2 + w)

    def arrow(w):
        def t(x, y):
            if _seg(x, y, 3, 16, 15, 16, 1.4 + w):
                return True
            head = [(19, 16), (12, 10.5), (12, 21.5)]
            return _poly(x, y, head) or _poly_edge(x, y, head, 0.3 + w)
        return t
    return [(DARK, lambda x, y: door(1.3)(x, y) or arrow(1.3)(x, y)),
            (BONE, door(0.0)),
            (GOLD, arrow(0.0))]


def cursor_repair():
    """Spanner, hotspot centre: open jaw at the upper left, handle to the
    lower right. Gold. The jaw is a ring with a wedge notched out."""
    notch = [(10.5, 10.5), (18.5, 0.5), (22.5, 8.5)]

    def tests(w):
        def t(x, y):
            jaw = _ring(x, y, 10.5, 10.5, 5.0, 1.6 + w) and not _poly(x, y, notch)
            return jaw or _seg(x, y, 14.2, 14.2, 26, 26, 2.0 + w)
        return t
    return outlined(GOLD, tests)


def cursor_sell():
    """Banknote, hotspot centre: bone note, gold centre roundel."""
    note = [(3, 10), (29, 10), (29, 22), (3, 22)]

    def tests(w):
        def t(x, y):
            if _poly_edge(x, y, note, 1.0 + w):
                return True
            if _ring(x, y, 16, 16, 3.4, 1.0 + w):
                return True
            return (_seg(x, y, 6.5, 16, 6.5, 16, 1.1 + w)
                    or _seg(x, y, 25.5, 16, 25.5, 16, 1.1 + w))
        return t

    layers = outlined(BONE, tests)
    layers.append((GOLD, lambda x, y: _ring(x, y, 16, 16, 3.4, 1.0)))
    return layers


def cursor_invalid():
    """Struck circle, hotspot centre, in the HUD's existing red."""
    c, rad = 16.0, 10.0
    k = rad / math.sqrt(2.0)

    def tests(w):
        def t(x, y):
            if _ring(x, y, c, c, rad, 1.7 + w):
                return True
            return _seg(x, y, c - k, c - k, c + k, c + k, 1.7 + w)
        return t
    return outlined(RED, tests)


CURSORS = [
    ("cursor_select.png", cursor_select),
    ("cursor_move.png", cursor_move),
    ("cursor_attack.png", cursor_attack),
    ("cursor_harvest.png", cursor_harvest),
    ("cursor_enter.png", cursor_enter),
    ("cursor_repair.png", cursor_repair),
    ("cursor_sell.png", cursor_sell),
    ("cursor_invalid.png", cursor_invalid),
]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    print("Rendering %d cursors to %s (32x32 RGBA, %dx supersampled)\n"
          % (len(CURSORS), OUT_DIR, SS))
    for name, build in CURSORS:
        write_png(name, render(build()))
    print("\nDone.")


if __name__ == "__main__":
    main()
