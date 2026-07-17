#!/usr/bin/env python3
"""The wall yaw gate: prove the DEF-08 mask tables against the exported bytes.

Re-derives every WallVariant and WallYaw entry from the actual vertex data in
game/assets/models/com_wall_*.glb and hard-fails (exit 1) if the shipped
tables in ModelLibrary.cs, the spec tables in doc 22, or the ledger's derived
table have drifted from what the meshes really are. A wrong yaw entry is
invisible until a corner faces the wrong way in a screenshot; this gate makes
it a red exit code instead. The defect class it guards against has already
caught two readers (doc 22's draft table and DEF-07's ledger finding (d)),
so the check is derived from bytes, not from any comment or table.

Stdlib only, no Blender, no Godot. Run from anywhere:

    python3 art/3d/wall-yaw-gate.py

The path flags exist so the gate itself can be tested against deliberately
broken copies (see the bite test in doc 22's DEF-08 spec).
"""
import argparse
import json
import re
import struct
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]

# Grid compass bits, exactly as ModelLibrary.cs states them:
# N=1 (cell y-1), E=2 (x+1), S=4 (y+1), W=8 (x-1).
N, E, S, W = 1, 2, 4, 8
BIT_NAME = {N: 'N', E: 'E', S: 'S', W: 'W'}

# Godot's yaw about +Y sends +X to -Z, i.e. E->N->W->S for increasing yaw.
# This is the sense the shipped hull-yaw line Mathf.Atan2(-to.X, -to.Z)
# depends on; it visibly orients every unit in the game.
R90 = {E: N, N: W, W: S, S: E}

# Geometry constants of the shipped meshes. An arm reaches 0.475 from the
# origin; the tallest non-arm feature in any lateral direction is the post
# body at 0.25. The 0.40 threshold separates the two with margin on both
# sides, and the footing filter removes the symmetric 0.95 plinth whose top
# sits at 0.06 in Blender Z, i.e. glTF +Y.
ARM_REACH = 0.40
FOOT_TOP = 0.07

# The DEF-07 orientation contract (art/3d/builder.py, the block above the six
# com_wall_* functions) read through the axis conversion: Blender +X is EAST
# and Blender +Y is NORTH, because the exporter maps Blender (x,y,z) to glTF
# (x,z,-y) and the client maps sim X to world X and sim Y to world Z.
CONTRACT = {
    'com_wall_post': frozenset(),
    'com_wall_straight': frozenset({E, W}),
    'com_wall_cap': frozenset({E}),
    'com_wall_corner': frozenset({N, E}),
    'com_wall_tee': frozenset({N, E, S}),
    'com_wall_cross': frozenset({N, E, S, W}),
}


def fail(msgs, text):
    msgs.append('FAIL: ' + text)


def load_glb(path):
    data = path.read_bytes()
    magic, _version, length = struct.unpack_from('<III', data, 0)
    if magic != 0x46546C67:
        raise ValueError(f'{path} is not a GLB container')
    off = 12
    gltf = None
    binary = None
    while off < length:
        clen, ctype = struct.unpack_from('<II', data, off)
        chunk = data[off + 8: off + 8 + clen]
        if ctype == 0x4E4F534A:
            gltf = json.loads(chunk)
        elif ctype == 0x004E4942:
            binary = chunk
        off += 8 + clen + ((4 - clen % 4) % 4 if clen % 4 else 0)
    if gltf is None or binary is None:
        raise ValueError(f'{path} lacks a JSON or BIN chunk')
    return gltf, binary


def positions(gltf, binary):
    for mesh in gltf.get('meshes', []):
        for prim in mesh['primitives']:
            acc = gltf['accessors'][prim['attributes']['POSITION']]
            if acc['type'] != 'VEC3' or acc['componentType'] != 5126:
                raise ValueError('POSITION accessor is not float VEC3')
            bv = gltf['bufferViews'][acc['bufferView']]
            base = bv.get('byteOffset', 0) + acc.get('byteOffset', 0)
            stride = bv.get('byteStride') or 12
            for i in range(acc['count']):
                yield struct.unpack_from('<3f', binary, base + i * stride)


def arms_of(glb_path, msgs):
    """Which compass directions a mesh's arms reach at yaw 0, from its bytes.

    Raw accessor positions are read without applying node transforms, which
    is only sound while every node is an identity. A translation or scale
    would trip the AABB bound below, but a pure 90-degree node rotation
    would not, so the node check is explicit: local-space arms mean nothing
    if the scene graph turns the mesh before the game does.
    """
    gltf, binary = load_glb(glb_path)
    for node in gltf.get('nodes', []):
        offending = [k for k in ('rotation', 'scale', 'matrix', 'translation')
                     if k in node]
        if offending:
            fail(msgs, f'{glb_path.name}: node {node.get("name", "?")} '
                       f'carries {"/".join(offending)}; this gate measures '
                       f'raw vertex data and cannot see through node '
                       f'transforms, so the exporter changed and the gate '
                       f'must be taught the new shape')
            return None
    pts = list(positions(gltf, binary))
    if not pts:
        fail(msgs, f'{glb_path.name}: no vertices at all')
        return None
    x0 = min(p[0] for p in pts)
    x1 = max(p[0] for p in pts)
    y1 = max(p[1] for p in pts)
    z0 = min(p[2] for p in pts)
    z1 = max(p[2] for p in pts)
    if x0 < -0.51 or x1 > 0.51 or z0 < -0.51 or z1 > 0.51 or y1 > 0.81:
        fail(msgs, f'{glb_path.name}: AABB outside the 1x1x0.8 cell budget '
                   f'(X[{x0:+.3f},{x1:+.3f}] Ymax {y1:.3f} Z[{z0:+.3f},{z1:+.3f}]); '
                   f'either the mesh overhangs its cell or the exporter no '
                   f'longer bakes node transforms, and this gate cannot '
                   f'measure it either way')
        return None
    above = [p for p in pts if p[1] > FOOT_TOP]
    if not above:
        fail(msgs, f'{glb_path.name}: nothing above the footing deck')
        return None
    arms = set()
    if max(p[0] for p in above) > ARM_REACH:
        arms.add(E)      # glTF +X is EAST
    if min(p[0] for p in above) < -ARM_REACH:
        arms.add(W)      # glTF -X is WEST
    if min(p[2] for p in above) < -ARM_REACH:
        arms.add(N)      # glTF -Z is NORTH
    if max(p[2] for p in above) > ARM_REACH:
        arms.add(S)      # glTF +Z is SOUTH
    return frozenset(arms)


def rotate(arms, steps):
    for _ in range(steps % 4):
        arms = frozenset(R90[d] for d in arms)
    return arms


def mask_arms(mask):
    return frozenset(b for b in (N, E, S, W) if mask & b)


def arm_names(arms):
    return ','.join(BIT_NAME[b] for b in sorted(arms)) or 'none'


def strip_comments(text):
    """Remove C# comments so braces inside them cannot derail the regexes.

    Block comments go first, then line comments; the negative lookbehind
    spares :// inside string literals such as res:// resource paths. Not a
    lexer, deliberately: every residual blind spot fails CLOSED (an entry
    count other than 16, or an unmatched initialiser), never as a wrong PASS.
    """
    text = re.sub(r'/\*.*?\*/', '', text, flags=re.DOTALL)
    return re.sub(r'(?<!:)//[^\n]*', '', text)


def parse_yaw_literal(raw, where, msgs):
    """Parse yaw numbers, keeping the sign: dropping a minus here turned a
    behaviourally wrong -270 into a clean PASS during the gate's own review,
    so the sign is load-bearing."""
    out = []
    for tok in re.findall(r'-?\d+(?:\.\d+)?', raw):
        v = float(tok)
        if v != int(v):
            fail(msgs, f'{where}: yaw entry {tok} is not a whole number')
            return None
        out.append(int(v))
    return out


def parse_csharp_tables(path, msgs):
    src = strip_comments(path.read_text())
    out = {}
    m = re.search(r'string\[\]\s+WallVariant\s*=\s*\{([^}]*)\}', src)
    if m:
        out['variant'] = re.findall(r'"(com_wall_\w+)"', m.group(1))
    else:
        fail(msgs, f'{path}: WallVariant initialiser not found')
    m = re.search(r'float\[\]\s+WallYaw\s*=\s*\{([^}]*)\}', src)
    if m:
        out['yaw'] = parse_yaw_literal(m.group(1), path.name, msgs)
    else:
        fail(msgs, f'{path}: WallYaw initialiser not found')
    return out


def parse_doc22(path, msgs):
    src = path.read_text()
    # Anchor everything to the DEF-08 section so an unrelated table added
    # elsewhere in this long roadmap cannot even spuriously change the count.
    if '#### TICKET-P5-DEF-08' in src:
        sec = src.split('#### TICKET-P5-DEF-08', 1)[1]
        sec = sec.split('#### TICKET-', 1)[0]
    else:
        fail(msgs, f'{path.name}: DEF-08 section heading not found')
        sec = src
    out = {}
    m = re.search(r'string\[\] WallVariant = \{([^}]*)\}', sec)
    if m:
        out['variant'] = re.findall(r'"(com_wall_\w+)"', m.group(1))
    else:
        fail(msgs, f'{path.name}: spec WallVariant literal not found')
    m = re.search(r'float\[\] WallYaw = \{([^}]*)\};', sec)
    if m:
        out['yaw'] = parse_yaw_literal(m.group(1), path.name, msgs)
    else:
        fail(msgs, f'{path.name}: spec WallYaw literal not found')
    rows = re.findall(r'^\| (\d+) \| [A-Za-z, ]+ \| (\w+) \| (-?\d+)',
                      sec, re.MULTILINE)
    if len(rows) == 16:
        out['md_mesh'] = {int(m0): f'com_wall_{name}' for m0, name, _ in rows}
        out['md_yaw'] = {int(m0): int(yaw) for m0, _, yaw in rows}
    else:
        fail(msgs, f'{path.name}: derivation table has {len(rows)} parseable '
                   f'rows, expected 16')
    return out


def parse_ledger(path, msgs):
    m = re.search(r'The derived table is \{([^}]*)\}', path.read_text())
    if not m:
        fail(msgs, f'{path.name}: finding (d) derived-table sentence not found')
        return None
    return [int(n) for n in m.group(1).split(',')]


def run(msgs):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--models-dir', type=Path,
                    default=REPO / 'game/assets/models')
    ap.add_argument('--modellib', type=Path,
                    default=REPO / 'game/scripts/ModelLibrary.cs')
    ap.add_argument('--doc22', type=Path,
                    default=REPO / 'docs/design/22-scale-and-colour-roadmap.md')
    ap.add_argument('--ledger', type=Path,
                    default=REPO / 'docs/tickets/phase-1-backlog.md')
    args = ap.parse_args()

    # Gate 1: each mesh's measured canonical arms match the DEF-07 contract.
    canon = {}
    for name, expected in CONTRACT.items():
        glb = args.models_dir / f'{name}.glb'
        if not glb.exists():
            fail(msgs, f'{glb} is missing')
            continue
        arms = arms_of(glb, msgs)
        if arms is None:
            continue
        canon[name] = arms
        tag = 'ok' if arms == expected else 'MISMATCH'
        print(f'  {name:20s} measured arms {{{arm_names(arms):7s}}} '
              f'contract {{{arm_names(expected):7s}}}  {tag}')
        if arms != expected:
            fail(msgs, f'{name}: measured arms {{{arm_names(arms)}}} do not '
                       f'match the builder.py contract '
                       f'{{{arm_names(expected)}}}; the meshes moved, so '
                       f'every table downstream must be re-derived')

    # Gate 2: the shipped ModelLibrary tables are valid against the bytes.
    # Validity, not equality to one canonical answer: the straight, post and
    # cross are rotationally symmetric, so more than one yaw can be correct.
    code = parse_csharp_tables(args.modellib, msgs)
    variant = code.get('variant') or []
    yaw = code.get('yaw') or []
    if len(variant) != 16:
        fail(msgs, f'ModelLibrary WallVariant has {len(variant)} entries')
    if len(yaw) != 16:
        fail(msgs, f'ModelLibrary WallYaw has {len(yaw)} entries')
    if len(variant) == 16 and len(yaw) == 16 and len(canon) == 6:
        for mask in range(16):
            mesh, deg = variant[mask], yaw[mask]
            if mesh not in canon:
                fail(msgs, f'mask {mask}: variant {mesh} is not one of the '
                           f'six contract meshes')
                continue
            if deg % 90:
                fail(msgs, f'mask {mask}: yaw {deg} is not a multiple of 90')
                continue
            got = rotate(canon[mesh], deg // 90)
            want = mask_arms(mask)
            if got != want:
                fail(msgs, f'mask {mask} ({arm_names(want)}): {mesh} at yaw '
                           f'{deg} presents arms {{{arm_names(got)}}}; this '
                           f'is the corner-faces-the-wrong-way defect')
        print(f'  shipped WallYaw   {{ {", ".join(map(str, yaw))} }}')

    # Gate 3: doc 22's DEF-08 spec matches the shipped code exactly. The spec
    # documents the shipped choice, so this is equality, not mere validity.
    doc = parse_doc22(args.doc22, msgs)
    if doc.get('variant') is not None and doc['variant'] != variant:
        fail(msgs, 'doc 22 spec WallVariant differs from ModelLibrary.cs')
    if doc.get('yaw') is not None and doc['yaw'] != yaw:
        fail(msgs, f'doc 22 spec WallYaw {doc["yaw"]} differs from '
                   f'ModelLibrary.cs {yaw}')
    if 'md_yaw' in doc:
        for mask in range(16):
            if doc['md_yaw'].get(mask) != (yaw[mask] if len(yaw) == 16 else None):
                fail(msgs, f'doc 22 derivation table row for mask {mask} says '
                           f'yaw {doc["md_yaw"].get(mask)}, code says '
                           f'{yaw[mask] if len(yaw) == 16 else "?"}')
            if len(variant) == 16 and doc['md_mesh'].get(mask) != variant[mask]:
                fail(msgs, f'doc 22 derivation table row for mask {mask} says '
                           f'{doc["md_mesh"].get(mask)}, code says '
                           f'{variant[mask]}')

    # Gate 4: the DEF-07 ledger's corrected finding (d) matches the code.
    ledger = parse_ledger(args.ledger, msgs)
    if ledger is not None and ledger != yaw:
        fail(msgs, f'ledger derived table {ledger} differs from '
                   f'ModelLibrary.cs {yaw}')

    print()
    if msgs:
        for m in msgs:
            print(m)
        print(f'\nWALL YAW GATE: {len(msgs)} failure(s)')
        return 1
    print('WALL YAW GATE: PASS. Meshes, ModelLibrary.cs, doc 22 and the '
          'ledger all agree with the exported bytes.')
    return 0


def main():
    # A crash is a failure, not a diagnosis: print whatever the gates had
    # already found before the exception, then the exception, then a verdict
    # line, so a truncated run can never be mistaken for a clean one.
    msgs = []
    try:
        return run(msgs)
    except Exception as exc:
        for m in msgs:
            print(m)
        print(f'FAIL: unhandled {type(exc).__name__}: {exc}')
        print('\nWALL YAW GATE: crashed, treated as failure')
        return 1


if __name__ == '__main__':
    sys.exit(main())
