#!/usr/bin/env python3
"""Top-down .fmap preview renderer: a high-camera look at a skirmish map without
touching Godot or the GPU. The look-development harness (capture.sh, contact.py)
renders the running client and is the right tool for materials and lighting;
this is the right tool for reading LAYOUT - is the river winding, are there
lanes and chokepoints, does the centre look contested - which is what a map
redesign has to be judged on. Stdlib plus Pillow, matching contact.py's posture.

Usage:
    map_preview.py MAP.fmap --out OUT.png [--scale N] [--title T]
    map_preview.py --grid A.fmap B.fmap ... --out SHEET.png   (labelled column)
"""
import argparse
import sys

from PIL import Image, ImageDraw

# Terrain palette, chosen so the map reads at a glance: water blue, hills brown,
# ruins grey, fences tan, bridges bright, ferrite amber, open a muted olive.
COLOURS = {
    '.': (58, 66, 48),      # open ground
    '#': (40, 40, 44),      # generic blocked
    'w': (36, 74, 120),     # water
    'B': (196, 170, 96),    # bridge deck
    'h': (104, 82, 58),     # hills
    'r': (96, 96, 104),     # ruins
    'f': (140, 120, 82),    # fences
    'F': (240, 190, 70),    # ferrite
}
START = (232, 60, 60)


def load(path):
    with open(path) as fh:
        lines = fh.read().replace('\r\n', '\n').split('\n')
    w = h = 0
    starts = {}
    gi = -1
    for i, line in enumerate(lines):
        s = line.strip()
        if s.startswith('size '):
            _, ws, hs = s.split()
            w, h = int(ws), int(hs)
        elif s.startswith('start '):
            p = s.split()
            starts[int(p[1])] = (int(p[2]), int(p[3]))
        elif s == 'grid:':
            gi = i + 1
            break
    grid = lines[gi:gi + h]
    return w, h, starts, grid


def render(path, scale):
    w, h, starts, grid = load(path)
    img = Image.new('RGB', (w * scale, h * scale), (18, 20, 24))
    px = img.load()
    for y in range(h):
        row = grid[y]
        for x in range(w):
            col = COLOURS.get(row[x], (255, 0, 255))
            for dy in range(scale):
                for dx in range(scale):
                    px[x * scale + dx, y * scale + dy] = col
    d = ImageDraw.Draw(img)
    # starts as ringed markers
    for p, (sx, sy) in starts.items():
        cx, cy = sx * scale + scale // 2, sy * scale + scale // 2
        r = max(3, scale * 2)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=START, width=max(1, scale // 2))
        d.text((cx - 3, cy - 5), str(p), fill=(255, 255, 255))
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('maps', nargs='+')
    ap.add_argument('--out', required=True)
    ap.add_argument('--scale', type=int, default=6)
    ap.add_argument('--grid', action='store_true')
    ap.add_argument('--title', default='')
    args = ap.parse_args()

    if args.grid or len(args.maps) > 1:
        imgs = [(m, render(m, args.scale)) for m in args.maps]
        cap = 18
        wmax = max(im.width for _, im in imgs)
        htot = sum(im.height + cap for _, im in imgs) + 8
        sheet = Image.new('RGB', (wmax + 16, htot + 8), (18, 20, 24))
        d = ImageDraw.Draw(sheet)
        y = 8
        for name, im in imgs:
            d.text((8, y), name.rsplit('/', 1)[-1], fill=(220, 216, 200))
            sheet.paste(im, (8, y + cap))
            y += im.height + cap
        sheet.save(args.out)
    else:
        im = render(args.maps[0], args.scale)
        im.save(args.out)
    print(f"wrote {args.out}")
    return 0


if __name__ == '__main__':
    sys.exit(main())
