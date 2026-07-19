#!/usr/bin/env python3
"""LOOK-02 (doc 25 Wave V0): the before-and-after contact sheet and the
measurement table.

The image exists because the numbers are not the point. The numbers exist so
that "it looks better" can be checked. Doc 25 section 3 clause 5 is the rule
this tool serves: a visual ticket ships only if the after-capture is visibly
better as an image, the metrics are a floor rather than a pass, and a change
that satisfies its metric while making the sheet worse is a failed ticket.

THE MASKS ARE STATED IN THE OUTPUT, EVERY TIME, AND THAT IS DELIBERATE. Doc 25
records two independent audits reaching opposite conclusions about the same
asset because they silently disagreed about which pixels counted. Every table
this tool prints carries the mask that produced it and the exact rectangles
that mask is made of, so the disagreement can happen out loud or not at all.

Standard library plus Pillow. No numpy, consistent with the project's
zero-dependency posture; /sim and /game gain no dependency from this file.

Usage:
    contact.py BEFORE AFTER --out SHEET.png [--before-tag T] [--after-tag T]
    contact.py --stats DIR [--tag T]
    contact.py --compare DIR_A DIR_B [--prefix T]
"""
import argparse
import os
import sys
import warnings

# Pillow 12 deprecates Image.getdata in favour of get_flattened_data, which
# does not exist in the Pillow versions this repository may meet elsewhere.
# getdata is correct and supported until 2027; the warning is noise on a tool
# whose entire output is a table meant to be read.
warnings.filterwarnings("ignore", category=DeprecationWarning)

from PIL import Image, ImageDraw  # noqa: E402

CAMERAS = ["camA", "camB", "camC"]

# --------------------------------------------------------------------------
# The masks. Every rectangle here was chosen once, by eye, against the
# committed reference state, and is printed with every table that uses it.
# Rectangles are (left, top, right, bottom) in the 1600x900 capture.
# --------------------------------------------------------------------------

# "unit": pixels that land on hardware rather than on ground, shroud or sky.
# There is no object-id buffer to ask, so these are hand-placed boxes over
# known vehicles and structures in the reference state. They are deliberately
# tight: a box that spills onto ground would let a ground change masquerade as
# a materials change, which is the exact failure doc 25 warns about.
UNIT_BOXES = {
    # CAM-A: the Directorate vehicle north of the bridge, the Sodality vehicle
    # on the bridge deck, and the Directorate base cluster on the left.
    "camA": [(415, 528, 452, 556), (352, 618, 398, 658), (250, 190, 440, 300)],
    # CAM-B: at max zoom a vehicle is a handful of pixels, so the boxes are on
    # the two base clusters, which are the only hardware with enough area to
    # measure at this height. This is itself a finding, not a limitation of the
    # tool: doc 25's camera-distance discount is visible in the box sizes.
    "camB": [(370, 315, 490, 390), (940, 700, 1180, 790)],
    # CAM-C: the whole point of CAM-C. One Directorate hull and one Sodality
    # hull, both large enough to judge a material on.
    "camC": [(785, 435, 855, 495), (762, 660, 822, 755)],
}

# "ground": open ground with no hardware, no water, no bridge and no scatter
# cluster in it. This is the mask V1-03's and V1-07's "mean open-ground
# luminance stays inside 90 to 135 out of 255" criterion is measured over.
GROUND_BOXES = {
    "camA": [(600, 350, 900, 430), (950, 400, 1250, 500)],
    "camB": [(700, 640, 1000, 720), (1050, 560, 1250, 620)],
    "camC": [(950, 380, 1250, 470), (300, 790, 600, 870)],
}

# "scene": every pixel that is not pure black. Pure black in these captures is
# the off-map void beyond the terrain mesh and nothing else; the procedural sky
# never renders to zero because SkyTopColor is (0.015, 0.02, 0.032) before AgX,
# and the shroud's unexplored fill is (0.008, 0.012, 0.022) at alpha 0.985,
# which also does not reach zero. The exclusion therefore removes exactly the
# geometry that does not exist, and nothing that does.
#
# "frame": literally every pixel, stated so the two can be compared.

MASKS = ["frame", "scene", "ground", "unit"]


def describe_mask(name, cam):
    if name == "frame":
        return "every pixel of the 1600x900 capture"
    if name == "scene":
        return ("every pixel except pure black (0,0,0). MEASURED AT BASELINE: "
                "no pixel in these captures is pure black, because the off-map "
                "void still shows the procedural sky, so this mask currently "
                "selects the same pixels as 'frame'. Both are printed anyway, "
                "so the day a change makes them differ, it is visible")
    if name == "ground":
        return "open-ground boxes " + repr(GROUND_BOXES[cam])
    if name == "unit":
        return "hardware boxes " + repr(UNIT_BOXES[cam])
    raise SystemExit(f"unknown mask {name}")


# --------------------------------------------------------------------------
# Measurement
# --------------------------------------------------------------------------

def luma(r, g, b):
    """Rec.709 relative luminance over sRGB bytes, kept in 0..255 so every
    number in the table is directly comparable with the /255 thresholds doc 25
    and doc 22 are written in. Deliberately NOT linearised: the acceptance
    criteria are written about what the frame looks like."""
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def hsv_sv(r, g, b):
    """Saturation and hue in degrees, computed inline rather than through
    colorsys because this runs over 1.44 million pixels three times."""
    mx = r if r > g else g
    if b > mx:
        mx = b
    mn = r if r < g else g
    if b < mn:
        mn = b
    if mx == 0:
        return 0.0, 0.0
    d = mx - mn
    s = d / mx
    if d == 0:
        return s, 0.0
    if mx == r:
        h = 60.0 * (((g - b) / d) % 6)
    elif mx == g:
        h = 60.0 * (((b - r) / d) + 2)
    else:
        h = 60.0 * (((r - g) / d) + 4)
    return s, h


def pixels_for(img, mask, cam):
    """The pixel list a mask selects, as flat (r, g, b) tuples."""
    if mask == "frame":
        return list(img.getdata())
    if mask == "scene":
        return [p for p in img.getdata() if p[0] or p[1] or p[2]]
    boxes = GROUND_BOXES[cam] if mask == "ground" else UNIT_BOXES[cam]
    out = []
    for box in boxes:
        out.extend(img.crop(box).getdata())
    return out


def percentile(sorted_vals, q):
    if not sorted_vals:
        return 0.0
    i = (len(sorted_vals) - 1) * q
    lo = int(i)
    hi = min(lo + 1, len(sorted_vals) - 1)
    return sorted_vals[lo] + (sorted_vals[hi] - sorted_vals[lo]) * (i - lo)


def hue_span(hues):
    """The smallest arc in degrees that contains every sampled hue. Doc 22's
    C-01 asks for "hue values spanning >= 40 degrees total range" against a
    pre-change span under 10; a naive max-minus-min answers 350 for two hues
    either side of red, which is why this walks the gaps instead."""
    if len(hues) < 2:
        return 0.0
    hs = sorted(hues)
    biggest_gap = 360.0 - (hs[-1] - hs[0])
    for a, b in zip(hs, hs[1:]):
        if b - a > biggest_gap:
            biggest_gap = b - a
    return 360.0 - biggest_gap


def measure(path, cam):
    img = Image.open(path).convert("RGB")
    out = {}
    for mask in MASKS:
        px = pixels_for(img, mask, cam)
        if not px:
            continue
        lums = sorted(luma(*p) for p in px)
        sats = []
        hues = []
        for r, g, b in px:
            s, h = hsv_sv(r, g, b)
            sats.append(s)
            # A hue is only meaningful where there is enough chroma AND enough
            # light to have one. Both floors were set by measurement, not by
            # taste: at s >= 0.08 alone the CAM-A hardware mask reported a hue
            # span of 314 degrees, which is not a finding about the art, it is
            # near-black pixels whose hue is quantisation noise being counted
            # as colours. s >= 0.20 with a value floor of 40/255 reports the
            # hues a viewer can actually see.
            if s >= 0.20 and max(r, g, b) >= 40:
                hues.append(h)
        out[mask] = {
            "n": len(px),
            "mean_luma": sum(lums) / len(lums),
            "mean_sat": sum(sats) / len(sats),
            "p5_luma": percentile(lums, 0.05),
            "p95_luma": percentile(lums, 0.95),
            "hue_span": hue_span(hues[:20000]),
            "chroma_frac": len(hues) / len(px),
        }
    return img, out


ROWS = [
    ("mean luminance /255", "mean_luma", "{:8.3f}"),
    ("mean HSV saturation", "mean_sat", "{:8.4f}"),
    ("5th pct luminance  ", "p5_luma", "{:8.3f}"),
    ("95th pct luminance ", "p95_luma", "{:8.3f}"),
    ("hue span (degrees) ", "hue_span", "{:8.2f}"),
    ("chromatic fraction ", "chroma_frac", "{:8.4f}"),
]


def print_table(cam, before, after, before_tag, after_tag, fh=sys.stdout):
    print(f"\n=== {cam} " + "=" * 56, file=fh)
    for mask in MASKS:
        if mask not in before:
            continue
        b = before[mask]
        print(f"\n  mask '{mask}': {describe_mask(mask, cam)}", file=fh)
        print(f"  {b['n']} pixels sampled", file=fh)
        if after is None:
            print(f"    {'metric':22}{'value':>10}", file=fh)
            for label, key, fmt in ROWS:
                print(f"    {label:22}{fmt.format(b[key])}", file=fh)
            continue
        a = after[mask]
        print(f"    {'metric':22}{before_tag:>10}{after_tag:>10}{'delta':>10}",
              file=fh)
        for label, key, fmt in ROWS:
            d = a[key] - b[key]
            print(f"    {label:22}{fmt.format(b[key])}{fmt.format(a[key])}"
                  f"{fmt.format(d)}", file=fh)


# --------------------------------------------------------------------------
# The sheet
# --------------------------------------------------------------------------

def build_sheet(pairs, out_path, before_tag, after_tag, title):
    """Three rows, before on the left and after on the right, each pair
    labelled with its camera. Half scale, because two 1600-wide frames side by
    side is 3200 pixels and nobody looks at that on a laptop."""
    w, h = 800, 450
    pad, head, cap = 8, 54, 26
    sheet = Image.new("RGB", (w * 2 + pad * 3, head + (h + cap + pad) * len(pairs) + pad),
                      (18, 20, 24))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 10), title, fill=(230, 226, 210))
    d.text((pad, 30), f"left: {before_tag}    right: {after_tag}"
                      "     (1600x900 captures at half scale)",
           fill=(150, 155, 165))
    y = head
    for cam, bpath, apath in pairs:
        for i, p in enumerate((bpath, apath)):
            x = pad + i * (w + pad)
            d.text((x, y), f"{cam}  {os.path.basename(p)}", fill=(200, 200, 200))
            sheet.paste(Image.open(p).convert("RGB").resize((w, h), Image.LANCZOS),
                        (x, y + cap))
        y += h + cap + pad
    sheet.save(out_path)
    return out_path


# --------------------------------------------------------------------------

def find(directory, tag, cam):
    p = os.path.join(directory, f"{tag}-{cam}.png")
    if not os.path.exists(p):
        raise SystemExit(f"missing capture: {p}")
    return p


def cmd_compare(a_dir, b_dir, prefix):
    """The LOOK-01 determinism gate. See verify-determinism.sh for why this is
    a tolerance rather than a byte comparison, and for the evidence."""
    # The gate is a COUNT and a WHOLE-FRAME MEAN, not a per-pixel ceiling, and
    # the reason is worth stating. The residual is a driver-side floating point
    # difference landing on pixels that already sat on a rounding boundary, so
    # its size in bytes depends on how bright those particular pixels are. Wave
    # V1 raised the exposure and the same handful of pixels started differing
    # by 35/255 instead of 4/255 without anything about the harness changing.
    # A per-pixel ceiling would therefore have to be re-argued after every
    # grading change, which is exactly the kind of gate that gets widened until
    # it means nothing.
    #
    # What actually has to be true is that the residual cannot hide a real
    # visual change. The smallest ticket in this wave moved 385,000 pixels; a
    # change worth shipping moves tens of thousands at least. Two hundred
    # pixels, with a mean absolute difference over the whole frame below a
    # hundredth of a level, cannot conceal one.
    max_px, max_mean = 200, 0.01
    fail = False
    for cam in CAMERAS:
        pa, pb = find(a_dir, prefix, cam), find(b_dir, prefix, cam)
        ia = Image.open(pa).convert("RGB")
        ib = Image.open(pb).convert("RGB")
        if ia.tobytes() == ib.tobytes():
            print(f"{cam}: BYTE-IDENTICAL")
            continue
        npx = 0
        worst = 0
        total = 0
        for x, y in zip(ia.getdata(), ib.getdata()):
            d = max(abs(x[0] - y[0]), abs(x[1] - y[1]), abs(x[2] - y[2]))
            if d:
                npx += 1
                total += d
                worst = max(worst, d)
        n = ia.size[0] * ia.size[1]
        mean_abs = total / n
        bad = npx > max_px or mean_abs > max_mean
        fail = fail or bad
        print(f"{cam}: {npx} px differ of {n}, max delta {worst}/255, "
              f"whole-frame mean absolute difference {mean_abs:.6f}/255"
              + ("   OVER GATE" if bad else ""))
    print(f"\ngate: at most {max_px} differing pixels per frame and a "
          f"whole-frame mean absolute difference below {max_mean}/255")
    print("RESULT:", "FAIL" if fail else "PASS")
    return 1 if fail else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("dirs", nargs="*")
    ap.add_argument("--out", default=None)
    ap.add_argument("--before-tag", default="baseline")
    ap.add_argument("--after-tag", default="after")
    ap.add_argument("--tag", default="baseline")
    ap.add_argument("--prefix", default="det")
    ap.add_argument("--title", default="Ferrostorm look-development contact sheet")
    ap.add_argument("--stats", action="store_true")
    ap.add_argument("--compare", action="store_true")
    ap.add_argument("--report", default=None,
                    help="also write the printed table to this file")
    args = ap.parse_args()

    if args.compare:
        if len(args.dirs) != 2:
            raise SystemExit("--compare needs two directories")
        return cmd_compare(args.dirs[0], args.dirs[1], args.prefix)

    fh = open(args.report, "w") if args.report else None
    def emit(*a, **k):
        print(*a, **k)
        if fh:
            print(*a, **k, file=fh)

    if args.stats:
        d = args.dirs[0]
        emit(f"# look-dev measurements: {d} (tag {args.tag})")
        emit("# luminance is Rec.709 over sRGB bytes, 0..255, NOT linearised")
        for cam in CAMERAS:
            _, m = measure(find(d, args.tag, cam), cam)
            print_table(cam, m, None, args.tag, "", fh=sys.stdout)
            if fh:
                print_table(cam, m, None, args.tag, "", fh=fh)
        return 0

    if len(args.dirs) != 2:
        raise SystemExit("need BEFORE and AFTER directories")
    before_dir, after_dir = args.dirs
    emit(f"# look-dev before/after: {before_dir} -> {after_dir}")
    emit(f"# tags: {args.before_tag} -> {args.after_tag}")
    emit("# luminance is Rec.709 over sRGB bytes, 0..255, NOT linearised")
    pairs = []
    for cam in CAMERAS:
        bp = find(before_dir, args.before_tag, cam)
        ap_ = find(after_dir, args.after_tag, cam)
        _, bm = measure(bp, cam)
        _, am = measure(ap_, cam)
        print_table(cam, bm, am, args.before_tag, args.after_tag, fh=sys.stdout)
        if fh:
            print_table(cam, bm, am, args.before_tag, args.after_tag, fh=fh)
        pairs.append((cam, bp, ap_))
    if args.out:
        build_sheet(pairs, args.out, args.before_tag, args.after_tag, args.title)
        emit(f"\nsheet: {args.out}")
    if fh:
        fh.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
