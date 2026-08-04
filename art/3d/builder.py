# Ferrostorm 3D asset library - procedural low-poly, style guide faithful.
import bpy, bmesh, math

def srgb(h):
    """Hex as seen on screen -> scene-linear, which is what Blender Base
    Color slots hold. PAL is authored in linear; the hexes in doc 16 are the
    rendered result of these triples, not the entry format. Call as
    srgb('e8762c'). Provided per doc 22 C-05 clause 1 as the documented
    encoding for any future palette entry; the table below is the ratified
    result and is kept as literals so a reader can diff it against doc 16."""
    def c(v):
        v = int(h[v:v+2], 16) / 255.0
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    return (c(0), c(2), c(4), 1)

# doc 22 C-05 (scheduled by doc 25 V2-04, landed in wave V4). The old PAL held
# sRGB-looking fractions in scene-linear Base Color slots, so every model
# rendered 50-60 sRGB units lighter and 30-40 per cent LESS saturated than doc
# 16's hexes: gun rendered #95A2AC at HSV S 0.13 against doc 16's 0.19, rust
# #B6866F at 0.39 against 0.62, teal #86DAD0 at 0.39 against 0.57. That
# desaturation, under V2's brighter exposure, is the confirmed blown-tank read
# V2 diagnosed and deferred here. This table holds the SHIPPED value (which is
# fine) and restores the missing CHROMA. It is NOT doc 16's hexes read as
# linear: doc 22 line 942 forbids that by name (rust would fall 62 per cent in
# luminance and Sodality would vanish against the ground). The trailing comment
# on each line is the sRGB hex it renders as.
PAL = dict(
    cinder=(0.048,0.052,0.062,1),   # #3E4046
    gun=(0.20,0.34,0.50,1),         # #7C9EBC  S 0.34 (was 0.134), hue 208
    plate=(0.34,0.48,0.66,1),       # #9EB8D4  S 0.26
    gundark=(0.105,0.185,0.28,1),   # #5B7790  S 0.37
    orange=(0.807,0.181,0.025,1),   # #E8762C  S 0.81 - doc 16 signal orange
    rust=(0.55,0.185,0.075,1),      # #C4774D  S 0.61, hue 20, luminance -10%
    rustp=(0.68,0.26,0.115,1),      # #D78B5F  S 0.56
    rustd=(0.32,0.105,0.045,1),     # #995B3C  S 0.61
    teal=(0.078,0.480,0.392,1),     # #4FB8A8  S 0.57 - doc 16 corroded teal
    olive=(0.30,0.33,0.185,1),      # #959C77  S 0.24 - a real olive drab
    olived=(0.185,0.205,0.105,1),   # #777D5B  S 0.27
    ferrite=(0.82,0.55,0.175,1),    # #EAC474  S 0.50
    fhi=(0.92,0.72,0.30,1),         # #F6DD95  S 0.39
    bone=(0.83,0.81,0.75,1),        # unchanged
    glow=(1.0,0.78,0.42,1),         # warmer lamp (W4-02 self-lit family)
    beacon=(1.0,0.16,0.10,1))       # unchanged red beacon

_mats = {}
USE_WEATHERED = False  # set True (see lineup.py) to route every part through
                       # materials2.wmat - roster-wide weathering, one switch

def mat(name, emit=0.0, rough=0.7, metal=0.0):
    # V2-01 (doc 25). The default was 0.15 and the line below floored it at
    # 0.2, so the metallic channel of every one of the 27 shipped models was
    # the literal constant 0.2 - byte 51 at the 5th, 50th and 95th percentiles
    # alike. The metallic-roughness BRDF has no valid material there: it takes
    # about a fifth of the diffuse albedo away and hands it to a specular lobe
    # with a muddy F0, and that lobe reflects the sky. Nothing in this game is
    # twenty per cent metal. Painted steel is a dielectric, so 0.0, and the
    # bare metal showing through a chip is 1.0; materials2.wmat now drives the
    # Metallic socket from the chip mask to get exactly that.
    if USE_WEATHERED and emit == 0:
        import materials2
        return materials2.wmat(name, rough=rough, metal=metal)
    key=(name,emit)
    if key in _mats: return _mats[key]
    m = bpy.data.materials.new(f"{name}_{emit}")
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = PAL[name]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit > 0:
        b.inputs["Emission Color"].default_value = PAL[name]
        b.inputs["Emission Strength"].default_value = emit
    _mats[key] = m
    return m

def box(name, sx, sy, sz, x=0, y=0, z=0, m='gun', bevel=0.06, emit=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, z + sz/2))
    o = bpy.context.object; o.name = name
    # size=1 gives verts at ±0.5 in Blender 5.x, so scale by the full
    # dimension for a box spanning exactly sx x sy x sz (under the container's
    # Blender 4.0 the sx/2 factors produced correct output; 5.x halved every
    # box while part locations stayed full-scale, exploding every model)
    o.scale = (sx, sy, sz)
    # scale only: location/rotation default to True and would reset the
    # origin to world zero, making any post-hoc rotation pivot around the
    # world origin instead of the part itself
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        md = o.modifiers.new('b','BEVEL'); md.width = bevel; md.segments = 2
        bpy.ops.object.modifier_apply(modifier='b')
    o.data.materials.append(mat(m, emit=emit))
    return o

def cyl(name, r, h, x=0, y=0, z=0, m='gun', vs=12, rx=0, ry=0, emit=0.0):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=h, vertices=vs, location=(x, y, z))
    o = bpy.context.object; o.name = name
    o.rotation_euler = (rx, ry, 0)
    o.data.materials.append(mat(m, emit=emit))
    return o

def wedge(name, pts, h, z=0, m='rust'):
    # extruded asymmetric polygon: the Sodality signature form
    me = bpy.data.meshes.new(name); o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    bm = bmesh.new()
    vs = [bm.verts.new((px, py, z)) for px, py in pts]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [g for g in r['geom'] if isinstance(g, bmesh.types.BMVert)]:
        v.co.z += h
    bm.to_mesh(me); bm.free()
    o.data.materials.append(mat(m))
    return o

def join(objs, name):
    # Blender 5.x headless defers depsgraph evaluation: rotation_euler set
    # after creation is not yet in matrix_world when join() bakes vertices,
    # so rotated parts (gun barrels etc.) join unrotated. Force the update.
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = bpy.context.object; obj.name = name
    # normalise: verts in world space, identity transform - downstream code
    # (battle-scene instancing, hero placement) sets .location as a world
    # offset and relies on the origin being world zero
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj

def team_band(w, y, z, colour, d=0.06):
    # W4-02: the one team-colour place is now self-lit (night identification)
    return box('band', w, d, 0.06, 0, y, z, colour, bevel=0.012, emit=1.2)

def tracks(x, length, wheel_r=0.082, wheels=4, band_w=0.15, m_band='gundark', m_skirt='gun'):
    """Detailed track unit for one side: upper tread run + top skirt, road
    wheels exposed below (the classic tank read), drive sprocket at the bow.
    Returns a parts list; caller joins. x = side offset (signed)."""
    parts = []
    top = wheel_r * 2
    parts.append(box(f'band{x}', band_w, length, 0.09, x, 0, top - 0.01, m_band, 0.02))
    parts.append(box(f'skirt{x}', band_w + 0.02, length - 0.12, 0.045, x, -0.02, top + 0.08, m_skirt, 0.015))
    span = length * 0.72
    for i in range(wheels):
        wy = -span / 2 + span * i / (wheels - 1)
        parts.append(cyl(f'w{x}{i}', wheel_r, band_w - 0.05, x, wy, wheel_r, m_band, vs=12, ry=math.pi / 2))
    parts.append(cyl(f'spr{x}', wheel_r * 0.8, band_w - 0.04, x, length / 2 - 0.03, wheel_r * 1.25, 'plate', vs=8, ry=math.pi / 2))
    return parts

def hatch(x, y, z, r=0.075):
    return cyl(f'hatch{x}{y}', r, 0.035, x, y, z, 'gundark', vs=10)

def antenna(x, y, z, h=0.34):
    return cyl(f'ant{x}{y}', 0.006, h, x, y, z + h / 2, 'gundark', vs=6)

def headlights(hx, y, z):
    # W4-02: paired glacis headlights on Directorate vehicles, self-lit
    return [box(f'hl{s}', 0.05, 0.02, 0.03, s * hx, y, z, 'glow', 0.005, emit=2.5)
            for s in (-1, 1)]

# ---------------- UNITS (1 blender unit = 1 cell) ----------------
def dir_cannon_tank():
    parts = [box('hull', 0.62, 0.78, 0.24, 0, -0.02, 0.04, 'gun', 0.05)]
    glacis = box('glacis', 0.56, 0.18, 0.16, 0, 0.40, 0.07, 'plate', 0.05)
    glacis.rotation_euler = (-0.5, 0, 0)
    parts.append(glacis)
    parts.append(box('deck', 0.5, 0.2, 0.07, 0, -0.36, 0.28, 'gundark', 0.03))
    for i, py in enumerate((-0.2, 0.05)):
        parts.append(box(f'panel{i}', 0.64, 0.16, 0.04, 0, py, 0.20, 'gundark', 0.01))
    for hx in (-0.2, 0.2):
        parts.append(box(f'hook{hx}', 0.06, 0.08, 0.06, hx, 0.46, 0.10, 'gundark', 0.01))
    parts += tracks(-0.38, 0.92)
    parts += tracks(0.38, 0.92)
    parts.append(cyl('ring', 0.20, 0.05, 0, -0.04, 0.315, 'gundark', vs=14))
    parts.append(team_band(0.34, -0.395, 0.315, 'orange'))
    parts += headlights(0.22, 0.47, 0.16)
    hull = join(parts, 'dir_cannon_tank')
    tparts = [box('tur', 0.38, 0.42, 0.15, 0, -0.04, 0.34, 'plate', 0.05)]
    tparts.append(box('bustle', 0.30, 0.14, 0.11, 0, -0.29, 0.35, 'gundark', 0.03))
    tparts.append(box('mant', 0.18, 0.10, 0.13, 0, 0.22, 0.375, 'gundark', 0.03))
    tparts.append(cyl('gun', 0.038, 0.58, 0, 0.52, 0.385, 'gundark', rx=math.pi/2))
    tparts.append(cyl('sleeve', 0.052, 0.20, 0, 0.34, 0.385, 'gundark', rx=math.pi/2))
    tparts.append(cyl('brake', 0.056, 0.10, 0, 0.78, 0.385, 'plate', vs=10, rx=math.pi/2))
    tparts.append(hatch(-0.10, -0.12, 0.43))
    tparts.append(antenna(0.15, -0.24, 0.41))
    tur = join(tparts, 'turret')
    tur.parent = hull
    return hull

def dir_bulwark_tank():
    parts = [box('hull', 0.9, 0.98, 0.32, 0, -0.02, 0.06, 'gun', 0.06)]
    glacis = box('glacis', 0.82, 0.2, 0.2, 0, 0.5, 0.1, 'plate', 0.05)
    glacis.rotation_euler = (-0.45, 0, 0)
    parts.append(glacis)
    parts.append(box('deck', 0.7, 0.26, 0.08, 0, -0.42, 0.38, 'gundark', 0.03))
    for sx in (-0.28, 0.28):   # side applique armour slabs
        parts.append(box(f'app{sx}', 0.08, 0.7, 0.16, sx * 1.62, 0, 0.2, 'plate', 0.02))
    parts += tracks(-0.52, 1.1, wheel_r=0.1, wheels=5, band_w=0.19)
    parts += tracks(0.52, 1.1, wheel_r=0.1, wheels=5, band_w=0.19)
    parts.append(cyl('ring', 0.28, 0.06, 0, -0.05, 0.41, 'gundark', vs=16))
    parts.append(team_band(0.5, -0.5, 0.42, 'orange'))
    parts += headlights(0.30, 0.58, 0.20)
    hull = join(parts, 'dir_bulwark_tank')
    tparts = [box('tur', 0.58, 0.54, 0.2, 0, -0.05, 0.44, 'plate', 0.06)]
    tparts.append(box('bustle', 0.44, 0.18, 0.14, 0, -0.38, 0.46, 'gundark', 0.03))
    tparts.append(box('mant', 0.34, 0.12, 0.18, 0, 0.24, 0.52, 'gundark', 0.03))
    for gx in (-0.13, 0.13):
        tparts.append(cyl(f'g{gx}', 0.05, 0.72, gx, 0.6, 0.54, 'gundark', rx=math.pi/2))
        tparts.append(cyl(f'brk{gx}', 0.065, 0.1, gx, 0.94, 0.54, 'plate', vs=10, rx=math.pi/2))
    tparts.append(hatch(-0.15, -0.15, 0.65))
    tparts.append(hatch(0.15, -0.15, 0.65, r=0.06))
    tparts.append(antenna(0.24, -0.34, 0.52))
    tur = join(tparts, 'turret')
    tur.parent = hull
    return hull

def dir_howitzer():
    parts = [box('hull', 0.58, 0.68, 0.2, 0, 0, 0.05, 'gun', 0.05)]
    parts.append(box('deck', 0.44, 0.2, 0.06, 0, -0.28, 0.25, 'gundark', 0.02))
    parts += tracks(-0.33, 0.8, wheel_r=0.075, wheels=4, band_w=0.13)
    parts += tracks(0.33, 0.8, wheel_r=0.075, wheels=4, band_w=0.13)
    # gun cradle + recoil spades: the siege silhouette
    parts.append(box('cradle', 0.26, 0.32, 0.16, 0, -0.08, 0.25, 'plate', 0.03))
    # Rx(+t) tips a cylinder's +Z end toward -Y, so an ELEVATED forward
    # barrel needs the NEGATIVE angle; the +Z end then points (0, sin t,
    # cos t) and the sleeve/muzzle must sit along that same axis.
    ga = math.pi / 2 - 0.5
    dy, dz = math.sin(ga), math.cos(ga)
    gx, gy, gz = 0, 0.35, 0.42
    # 'plate' not 'gundark': at full elevation the barrel reads against the
    # dark ground, where near-black vanishes and the tip appears to float
    parts.append(cyl('gun', 0.05, 1.1, gx, gy, gz, 'plate', rx=-ga))
    parts.append(cyl('sleeve', 0.068, 0.3, gx, gy - 0.31 * dy, gz - 0.31 * dz, 'gun', rx=-ga))
    parts.append(cyl('muzz', 0.062, 0.09, gx, gy + 0.50 * dy, gz + 0.50 * dz, 'gundark', vs=10, rx=-ga))
    for sx in (-0.18, 0.18):   # rear stabiliser spades
        sp = box(f'spade{sx}', 0.1, 0.22, 0.05, sx, -0.42, 0.02, 'gundark', 0.015)
        sp.rotation_euler = (0.35, 0, 0)
        parts.append(sp)
    parts.append(hatch(-0.16, 0.14, 0.27, r=0.06))
    parts.append(team_band(0.32, -0.36, 0.2, 'orange'))
    return join(parts, 'dir_howitzer')

def dir_sentinel_scout():
    parts = [box('hull', 0.4, 0.58, 0.18, 0, 0, 0.08, 'gun', 0.04)]
    glacis = box('glacis', 0.34, 0.12, 0.1, 0, 0.3, 0.1, 'plate', 0.03)
    glacis.rotation_euler = (-0.5, 0, 0)
    parts.append(glacis)
    for sx in (-0.21, 0.21):   # wheeled scout: 3 wheels per side
        for i, wy in enumerate((-0.2, 0.0, 0.2)):
            parts.append(cyl(f'w{sx}{i}', 0.09, 0.08, sx, wy, 0.09, 'gundark', vs=12, ry=math.pi/2))
    parts.append(box('cab', 0.3, 0.22, 0.1, 0, 0.1, 0.26, 'gun', 0.03))
    parts.append(cyl('mast', 0.045, 0.42, 0, -0.08, 0.5, 'gundark', vs=8))
    sdish = cyl('sdish', 0.16, 0.035, 0, -0.08, 0.73, 'orange', vs=16)
    parts.append(cyl('emitter', 0.03, 0.08, 0, -0.08, 0.78, 'plate', vs=8))
    parts.append(box('pod', 0.1, 0.16, 0.08, 0.18, -0.18, 0.26, 'gundark', 0.02))
    parts.append(team_band(0.26, -0.28, 0.2, 'orange'))
    hull = join(parts, 'dir_sentinel_scout')
    child_part(hull, sdish, 'dish')
    return hull

def sod_phantom_tank():
    parts = [wedge('body', [(-0.42, -0.5), (-0.2, 0.52), (0.34, 0.44), (0.5, -0.28), (0.18, -0.56)], 0.3, m='rust')]
    parts.append(wedge('top', [(-0.28, -0.34), (-0.12, 0.34), (0.24, 0.28), (0.34, -0.2)], 0.14, z=0.3, m='rustd'))
    # welded-on salvage plates: asymmetric, overlapping the hull facets
    parts.append(wedge('plate1', [(-0.44, -0.2), (-0.34, 0.3), (-0.16, 0.24), (-0.24, -0.3)], 0.04, z=0.3, m='rustp'))
    parts.append(wedge('plate2', [(0.2, -0.4), (0.4, -0.16), (0.3, 0.05), (0.12, -0.2)], 0.04, z=0.3, m='rustd'))
    for i, (tx, ty) in enumerate(((-0.02, 0.42), (0.14, 0.4))):
        parts.append(cyl(f't{i}', 0.045, 0.6, tx, ty, 0.4, 'rustd', rx=math.pi/2))
        parts.append(cyl(f'tm{i}', 0.058, 0.1, tx, ty + 0.26, 0.4, 'rustp', vs=8, rx=math.pi/2))
    parts.append(cyl('exh1', 0.03, 0.18, -0.3, -0.5, 0.36, 'rustd', vs=8, rx=0.5))
    parts.append(cyl('exh2', 0.03, 0.18, -0.2, -0.53, 0.36, 'rustd', vs=8, rx=0.5))
    parts.append(box('sl', 0.3, 0.07, 0.1, -0.26, -0.34, 0.28, 'teal', 0.02, emit=1.5))
    return join(parts, 'sod_phantom_tank')

def sod_shade_raider():
    parts = [wedge('body', [(-0.3, -0.36), (-0.14, 0.38), (0.26, 0.3), (0.34, -0.24)], 0.2, m='rust')]
    parts.append(wedge('top', [(-0.18, -0.22), (-0.08, 0.22), (0.18, 0.16)], 0.1, z=0.2, m='rustp'))
    # canted stabiliser fin + skirt blades: the raider reads fast even parked
    parts.append(wedge('fin', [(-0.06, -0.34), (0.0, -0.1), (0.08, -0.32)], 0.22, z=0.2, m='rustd'))
    parts.append(wedge('blade1', [(-0.34, -0.3), (-0.26, 0.28), (-0.2, 0.24), (-0.28, -0.3)], 0.06, z=0.04, m='rustd'))
    parts.append(wedge('blade2', [(0.3, -0.22), (0.36, 0.2), (0.3, 0.24), (0.24, -0.18)], 0.06, z=0.04, m='rustd'))
    parts.append(cyl('exh', 0.025, 0.16, -0.14, -0.36, 0.16, 'rustd', vs=8, rx=0.6))
    parts.append(box('sl', 0.22, 0.06, 0.08, -0.18, -0.24, 0.18, 'teal', 0.02, emit=1.5))
    return join(parts, 'sod_shade_raider')

def infantry(name, tube=False, colour='olive'):
    # W4-09: keep the dot-cluster silhouette, add soldier read at zoom -
    # slimmer bodies, backpacks, cross-held rifles, per-man facing variety
    # (per-part spin, the spec-accepted approximation at this scale)
    men = []
    for i, (dx, dy) in enumerate([(-0.2, -0.15), (0.2, -0.1), (0, 0.2)]):
        zrot = (0.4, 2.5, 4.2)[i]
        b = cyl(f'b{i}', 0.065, 0.20, dx, dy, 0.10, colour, vs=8)
        b.rotation_euler = (0, 0, zrot)
        # head lowered 0.025 to stay seated on the shortened body
        h = cyl(f'h{i}', 0.05, 0.07, dx, dy, 0.235, 'bone', vs=8)
        pk = box(f'pk{i}', 0.09, 0.05, 0.09, dx, dy - 0.07, 0.16, 'olived', 0.008)
        pk.rotation_euler = (0, 0, zrot)
        r = box(f'r{i}', 0.02, 0.22, 0.02, dx + 0.07, dy + 0.02, 0.18, 'gundark', 0.004)
        r.rotation_euler = (0, 0, 0.5 + zrot)
        men += [b, h, pk, r]
        if tube:
            men.append(cyl(f't{i}', 0.03, 0.24, dx + 0.06, dy, 0.3, 'ferrite', vs=8, ry=math.pi/2))
    # base disc shrunk: the contact-blob decal owns the grounding job now
    base = cyl('base', 0.30, 0.02, 0, 0, 0.01, 'olived', vs=16)
    return join(men + [base], name)

def com_harvester():
    parts = [box('body', 0.78, 1.0, 0.36, 0, 0.02, 0.08, 'olive', 0.12)]
    parts.append(box('cab', 0.4, 0.24, 0.16, 0, 0.44, 0.44, 'olived', 0.04))
    parts.append(box('screen', 0.32, 0.03, 0.09, 0, 0.565, 0.47, 'glow', 0.008, emit=1.8))
    parts += tracks(-0.45, 1.1, wheel_r=0.11, wheels=4, band_w=0.17, m_band='olived', m_skirt='olive')
    parts += tracks(0.45, 1.1, wheel_r=0.11, wheels=4, band_w=0.17, m_band='olived', m_skirt='olive')
    parts.append(cyl('hop', 0.26, 0.16, 0, 0.02, 0.5, 'ferrite', vs=14))
    parts.append(cyl('hoprim', 0.29, 0.04, 0, 0.02, 0.58, 'olived', vs=14))
    # W4-09: visible ore heap in the hopper - at night the glowing full
    # hopper is the economy telling its own story
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.20,
                                          location=(0, 0.02, 0.60))
    heap = bpy.context.object; heap.name = 'heap'
    heap.scale = (1, 1, 0.5)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    heap.data.materials.append(mat('fhi', emit=1.6, rough=0.4))
    parts.append(heap)
    parts.append(cyl('pipe', 0.045, 0.5, 0.3, -0.2, 0.44, 'olived', vs=8, rx=math.pi/2))
    for s in (-1, 1):   # W4-09: rear mud flaps
        parts.append(box(f'flap{s}', 0.16, 0.03, 0.12, 0.45 * s, -0.55, 0.06, 'gundark', 0.005))
    parts.append(team_band(0.4, 0.56, 0.34, 'ferrite'))
    hull = join(parts, 'com_harvester')
    # W4-09 intake assembly: drum + 8 drum teeth replace the 4 static hull
    # teeth. Joined via bpy.ops.object.join() with the 'in' box active so the
    # hinge origin of the de-merged 'intake' child (W2 churn animation) is
    # preserved - builder.join() would re-origin to world zero and break it.
    intake = box('in', 0.5, 0.22, 0.14, 0, -0.56, 0.04, 'olived', 0.04)
    intake.rotation_euler = (0.3, 0, 0)
    iparts = [intake]
    iparts.append(cyl('drum', 0.13, 0.62, 0, -0.60, 0.11, 'gundark', vs=12, ry=math.pi/2))
    for a in range(8):
        ang = a * math.pi / 4
        iparts.append(box(f'dt{a}', 0.05, 0.04, 0.04, -0.24 + (a % 4) * 0.16,
                          -0.60 + 0.15 * math.cos(ang), 0.11 + 0.15 * math.sin(ang),
                          'plate', 0.005))
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    for p in iparts: p.select_set(True)
    bpy.context.view_layer.objects.active = intake
    bpy.ops.object.join()
    intake = bpy.context.object; intake.name = 'in'
    child_part(hull, intake, 'intake')
    return hull

def com_mcv():
    parts = [box('body', 0.68, 1.16, 0.38, 0, 0, 0.1, 'olive', 0.07)]
    parts.append(box('cab', 0.5, 0.3, 0.2, 0, 0.44, 0.48, 'ferrite', 0.04))
    parts.append(box('screen', 0.4, 0.03, 0.1, 0, 0.585, 0.52, 'glow', 0.008, emit=1.8))
    parts += tracks(-0.4, 1.24, wheel_r=0.105, wheels=5, band_w=0.15, m_band='olived', m_skirt='olive')
    parts += tracks(0.4, 1.24, wheel_r=0.105, wheels=5, band_w=0.15, m_band='olived', m_skirt='olive')
    # deployment crane: post, boom, cable, hook block
    parts.append(cyl('post', 0.05, 0.3, 0, -0.25, 0.6, 'olived', vs=10))
    parts.append(cyl('boom', 0.035, 0.8, 0, -0.2, 0.72, 'olived', ry=0.9))
    parts.append(cyl('cable', 0.012, 0.24, 0.31, 0.03, 0.55, 'gundark', vs=6))
    parts.append(box('block', 0.08, 0.08, 0.1, 0.31, 0.03, 0.38, 'ferrite', 0.015))
    for i, py in enumerate((-0.3, -0.05, 0.2)):   # stowed segment ribs
        parts.append(box(f'rib{i}', 0.7, 0.05, 0.05, 0, py, 0.48, 'olived', 0.01))
    return join(parts, 'com_mcv')

def com_engineer():
    b = cyl('b', 0.08, 0.24, 0, 0, 0.12, 'olive', vs=8)
    h = cyl('h', 0.055, 0.08, 0, 0, 0.3, 'bone', vs=8)
    hard = cyl('hard', 0.06, 0.025, 0, 0, 0.345, 'ferrite', vs=8)
    case = box('c', 0.16, 0.1, 0.1, 0.14, 0.05, 0, 'ferrite', 0.02)
    base = cyl('base', 0.2, 0.02, 0, 0, 0.01, 'olived', vs=12)
    return join([b, h, hard, case, base], 'com_engineer')

# ---------------- STRUCTURES (2x2 cells) ----------------
def pad(m='olived'):
    return box('pad', 1.9, 1.9, 0.08, m=m, bevel=0.05)

def com_power_plant():
    parts = [pad()]
    parts.append(box('hall', 1.5, 1.5, 0.5, 0, 0, 0.08, 'olive', 0.08))
    parts.append(box('roof', 1.2, 1.0, 0.08, 0.1, -0.15, 0.58, 'olived', 0.02))
    parts.append(cyl('cool', 0.42, 0.9, -0.35, 0.1, 0.55, 'olived', vs=14))
    parts.append(cyl('ring', 0.3, 0.06, -0.35, 0.1, 1.02, 'ferrite', vs=14))
    parts.append(cyl('ring2', 0.44, 0.05, -0.35, 0.1, 0.76, 'olive', vs=14))
    parts.append(box('v', 0.34, 0.9, 0.7, 0.5, -0.1, 0.58, 'orange', 0.04))
    for i in range(3):   # vent louvres (W4-08: inset, not proud-overlapping)
        parts.append(box(f'lv{i}', 0.35, 0.7, 0.04, 0.5, -0.1, 0.72 + i * 0.14, 'gundark', 0.008))
    # W4-08: dark recess behind the louvres so the vent reads as an opening
    parts.append(box('vrec', 0.30, 0.86, 0.62, 0.5, -0.1, 0.62, 'cinder', 0.01))
    parts.append(cyl('feed', 0.06, 0.7, 0.05, 0.1, 0.62, 'olived', vs=8, ry=math.pi/2))
    parts.append(cyl('stack', 0.05, 0.35, 0.62, 0.55, 0.72, 'olived', vs=8))
    # W4-08: wall pilaster ribs, roof vents + elbow pipe, cooling heat rim
    for i in range(4):
        parts.append(box(f'ribx{i}', 0.06, 1.56, 0.46, -0.6 + i * 0.4, 0, 0.10, 'olived', 0.01))
    parts.append(box('vent1', 0.18, 0.28, 0.10, 0.15, -0.35, 0.66, 'olived', 0.01))
    parts.append(box('vent2', 0.18, 0.28, 0.10, 0.45, 0.15, 0.66, 'olived', 0.01))
    parts.append(cyl('vpipe', 0.035, 0.3, 0.15, -0.2, 0.72, 'olived', vs=8, rx=0.8))
    parts.append(cyl('coolglow', 0.27, 0.02, -0.35, 0.1, 1.00, 'glow', vs=14, emit=1.4))
    # W4-02: lit window strip on the hall wall, red beacon on the stack top
    parts.append(box('win', 0.02, 0.9, 0.08, 0.76, 0, 0.30, 'glow', 0.005, emit=1.6))
    parts.append(cyl('bcn', 0.02, 0.05, 0.62, 0.55, 0.92, 'beacon', vs=6, emit=3.0))
    return join(parts, 'com_power_plant')

def com_factory():
    parts = [pad()]
    parts.append(box('hall', 1.6, 1.4, 0.62, 0, 0, 0.08, 'olive', 0.08))
    for i in range(3):   # sawtooth roof monitors
        parts.append(box(f'saw{i}', 1.5, 0.28, 0.14, 0, -0.45 + i * 0.42, 0.7, 'olived', 0.02))
    d1 = box('d1', 0.55, 0.1, 0.44, -0.4, 0.72, 0.14, 'olived', 0.03)
    d2 = box('d2', 0.55, 0.1, 0.44, 0.4, 0.72, 0.14, 'olived', 0.03)
    parts.append(box('rail1', 0.06, 0.5, 0.03, -0.4, 0.95, 0.09, 'gundark', 0.008))
    parts.append(box('rail2', 0.06, 0.5, 0.03, 0.4, 0.95, 0.09, 'gundark', 0.008))
    parts.append(box('lip', 1.5, 0.12, 0.1, 0, 0.72, 0.62, 'ferrite', 0.02))
    parts.append(cyl('chim', 0.08, 0.5, -0.6, -0.5, 0.85, 'olived', vs=10))
    parts.append(cyl('chimcap', 0.1, 0.04, -0.6, -0.5, 1.1, 'gundark', vs=10))
    # W4-02: chimney beacon plus a glow lintel strip above each door
    parts.append(cyl('bcn', 0.02, 0.05, -0.6, -0.5, 1.14, 'beacon', vs=6, emit=3.0))
    for n, dx in enumerate((-0.4, 0.4)):
        parts.append(box(f'dglow{n}', 0.5, 0.02, 0.04, dx, 0.78, 0.60, 'glow', 0.005, emit=1.4))
    hull = join(parts, 'com_factory')
    child_part(hull, d1, 'door0')
    child_part(hull, d2, 'door1')
    return hull

def com_refinery():
    parts = [pad()]
    parts.append(box('hall', 1.1, 1.0, 0.44, 0.3, -0.3, 0.08, 'olive', 0.08))
    parts.append(box('hut', 0.4, 0.34, 0.22, 0.55, -0.05, 0.52, 'olived', 0.03))
    parts.append(cyl('silo', 0.45, 1.0, -0.4, 0.25, 0.58, 'olived', vs=14))
    parts.append(cyl('siloband', 0.47, 0.05, -0.4, 0.25, 0.75, 'olive', vs=14))
    parts.append(cyl('core', 0.3, 0.1, -0.4, 0.25, 1.1, 'fhi', vs=14, emit=2.0))
    parts.append(cyl('pipe', 0.055, 0.62, -0.05, 0.0, 0.75, 'olived', vs=8, ry=math.pi/2))
    parts.append(cyl('valve', 0.09, 0.03, -0.05, 0.0, 0.84, 'ferrite', vs=10, rx=math.pi/2))
    parts.append(box('dock', 0.8, 0.5, 0.06, 0.35, 0.62, 0.08, 'ferrite', 0.02))
    for sx in (0.05, 0.65):   # dock guide posts
        parts.append(cyl(f'post{sx}', 0.03, 0.3, sx, 0.85, 0.23, 'olived', vs=8))
    return join(parts, 'com_refinery')

def com_construction_yard():
    parts = [pad()]
    parts.append(box('b1', 1.7, 0.4, 0.5, 0, 0.6, 0.08, 'olive', 0.06))
    parts.append(box('b2', 1.7, 0.4, 0.5, 0, -0.6, 0.08, 'olive', 0.06))
    parts.append(box('cabin', 0.4, 0.34, 0.24, -0.55, 0.6, 0.58, 'olived', 0.03))
    parts.append(cyl('g1', 0.05, 1.3, -0.7, 0, 0.85, 'ferrite', ry=math.pi/2))
    parts.append(cyl('g2', 0.05, 1.3, 0.7, 0, 0.85, 'ferrite', ry=math.pi/2))
    parts.append(cyl('beam', 0.05, 1.5, 0, 0, 0.85, 'ferrite', rx=math.pi/2))
    parts.append(box('trolley', 0.16, 0.2, 0.1, 0, 0.15, 0.83, 'gundark', 0.02))
    parts.append(cyl('cable', 0.014, 0.2, 0, 0.15, 0.7, 'gundark', vs=6))
    parts.append(box('hook', 0.24, 0.24, 0.3, 0, 0.15, 0.48, 'olived', 0.03))
    for i in range(3):   # stacked plate cargo between the halls
        parts.append(box(f'plate{i}', 0.5 - i * 0.08, 0.4 - i * 0.05, 0.06, 0.45, 0, 0.11 + i * 0.06, 'plate' if i % 2 else 'olived', 0.01))
    parts.append(cyl('drum', 0.12, 0.2, -0.5, 0, 0.12, 'ferrite', vs=12, ry=math.pi/2))
    # W4-08: legs + cross-braces connect the gantry to the ground (the crane
    # read as detached white sticks), ferrite-striped trolley, more clutter
    for lx in (-0.62, 0.62):
        for ly in (-0.55, 0.55):
            parts.append(cyl(f'leg{lx}{ly}', 0.045, 0.82, lx, ly, 0.41, 'olived', vs=8))
    for n, (bx, brx) in enumerate([(-0.62, 0.9), (-0.62, -0.9), (0.62, 0.9), (0.62, -0.9)]):
        br = box(f'brace{n}', 0.04, 0.7, 0.05, bx, 0, 0.62, 'olived', 0.01)
        br.rotation_euler = (brx, 0, 0)
        parts.append(br)
    parts.append(box('trolleymark', 0.17, 0.21, 0.02, 0, 0.15, 0.895, 'ferrite', 0.005))
    parts.append(cyl('drum2', 0.12, 0.2, -0.5, 0.28, 0.12, 'olived', vs=12, ry=math.pi/2))
    parts.append(box('pallet', 0.4, 0.3, 0.04, 0.45, -0.45, 0.10, 'gundark', 0.008))
    return join(parts, 'com_construction_yard')

def dir_turret():
    parts = [pad('gundark')]
    parts.append(cyl('base', 0.6, 0.3, 0, 0, 0.23, 'plate', vs=14))
    for a in range(6):   # base armour bolts
        bx, by = 0.52 * math.cos(a * math.pi / 3), 0.52 * math.sin(a * math.pi / 3)
        parts.append(cyl(f'bolt{a}', 0.05, 0.06, bx, by, 0.38, 'gundark', vs=6))
    parts.append(cyl('collar', 0.45, 0.08, 0, 0, 0.41, 'gundark', vs=14))
    parts.append(cyl('head', 0.4, 0.26, 0, 0, 0.5, 'gun', vs=12))
    parts.append(box('cheek', 0.5, 0.3, 0.18, 0, 0.22, 0.44, 'plate', 0.03))
    parts.append(cyl('gun', 0.055, 0.9, 0, 0.5, 0.56, 'gundark', rx=math.pi/2))
    parts.append(cyl('sleeve', 0.075, 0.24, 0, 0.28, 0.56, 'gundark', rx=math.pi/2))
    parts.append(cyl('muzz', 0.08, 0.08, 0, 0.93, 0.56, 'plate', vs=10, rx=math.pi/2))
    parts.append(hatch(0, -0.15, 0.65, r=0.09))
    parts.append(box('bd', 0.5, 0.1, 0.06, 0, -0.8, 0.06, 'orange', 0.015))
    return join(parts, 'dir_turret')

def dir_superweapon():
    parts = [pad('gundark')]
    parts.append(cyl('ring', 0.75, 0.22, 0, 0, 0.19, 'plate', vs=20))
    for a in range(4):   # dish support struts
        sx, sy = 0.55 * math.cos(a * math.pi / 2 + 0.785), 0.55 * math.sin(a * math.pi / 2 + 0.785)
        st = cyl(f'strut{a}', 0.035, 0.3, sx, sy, 0.36, 'gundark', vs=8)
        st.rotation_euler = (0.35 * math.sin(a * math.pi / 2 + 0.785), -0.35 * math.cos(a * math.pi / 2 + 0.785), 0)
        parts.append(st)
    parts.append(cyl('dish', 0.55, 0.1, 0, 0, 0.45, 'gun', vs=20))
    parts.append(cyl('dishrim', 0.58, 0.04, 0, 0, 0.5, 'plate', vs=20))
    parts.append(cyl('core', 0.16, 0.5, 0, 0, 0.55, 'orange', vs=10, emit=2.2))
    for i in range(3):   # charge coils climbing the core
        parts.append(cyl(f'coil{i}', 0.2, 0.03, 0, 0, 0.6 + i * 0.12, 'gundark', vs=12))
    parts.append(box('f1', 0.1, 1.5, 0.16, 0, 0, 0.3, 'gundark', 0.02))
    parts.append(box('f2', 1.5, 0.1, 0.16, 0, 0, 0.3, 'gundark', 0.02))
    parts.append(box('console', 0.3, 0.2, 0.18, 0.7, -0.7, 0.11, 'plate', 0.02))
    return join(parts, 'dir_superweapon')

def sod_veil_projector():
    parts = [pad('rustd')]
    parts.append(cyl('base', 0.55, 0.35, 0, 0, 0.26, 'rustp', vs=12))
    parts.append(wedge('shard1', [(-0.5, -0.3), (-0.3, 0.15), (-0.15, -0.1)], 0.5, z=0.08, m='rust'))
    parts.append(wedge('shard2', [(0.2, 0.25), (0.45, 0.4), (0.4, 0.1)], 0.38, z=0.08, m='rustd'))
    parts.append(cyl('spire', 0.08, 1.1, 0, 0, 0.9, 'rustd', vs=8))
    parts.append(cyl('collar', 0.13, 0.06, 0, 0, 1.15, 'rustp', vs=8))
    parts.append(cyl('orb', 0.18, 0.18, 0, 0, 1.5, 'teal', vs=10, emit=1.8))
    parts.append(cyl('r1', 0.5, 0.03, 0, 0, 0.8, 'teal', vs=18, emit=1.8))
    parts.append(cyl('r2', 0.34, 0.025, 0, 0, 1.12, 'teal', vs=16, emit=1.8))
    for a in range(3):   # guy-wire anchor spikes
        gx, gy = 0.72 * math.cos(a * 2.09 + 0.5), 0.72 * math.sin(a * 2.09 + 0.5)
        parts.append(cyl(f'guy{a}', 0.02, 0.5, gx, gy, 0.3, 'rustd', vs=6, rx=0.4 * math.sin(a * 2.09 + 0.5), ry=-0.4 * math.cos(a * 2.09 + 0.5)))
    return join(parts, 'sod_veil_projector')

def com_service_depot():
    parts = [pad()]
    parts.append(cyl('padc', 0.8, 0.1, 0, 0, 0.13, 'olive', vs=18))
    parts.append(box('h1', 1.0, 0.22, 0.06, 0, 0, 0.18, 'ferrite', 0.02))
    parts.append(box('h2', 0.22, 1.0, 0.06, 0, 0, 0.18, 'ferrite', 0.02))
    parts.append(box('armbase', 0.2, 0.2, 0.34, 0.75, -0.6, 0.08, 'olived', 0.03))
    parts.append(cyl('armboom', 0.035, 0.7, 0.75, -0.28, 0.44, 'olived', vs=8, rx=math.pi/2 - 0.35))
    parts.append(cyl('tool', 0.05, 0.12, 0.75, 0.04, 0.32, 'gundark', vs=8))
    for i, (cx, cy) in enumerate(((-0.72, -0.62), (-0.5, -0.72), (-0.62, -0.4))):
        parts.append(box(f'crate{i}', 0.18, 0.18, 0.16, cx, cy, 0.08, 'olived' if i % 2 else 'plate', 0.015))
    return join(parts, 'com_service_depot')

# ---------------- BARRIERS (1x1 cells, ADR-005 struct type 9) ----------------
# SCALE. One cell is one Blender unit and the origin is the footprint centre.
# The structures above are 2x2 and open with pad(); a barrier is 1x1, so it
# must NOT call pad(). A segment spans 0.95 about its own origin and its whole
# AABB fits inside 1.0 x 1.0 x 0.8: overhang the cell and the run intersects
# its own neighbours. The remaining 0.05 reads as a panel seam between
# segments, which is what a modular barrier should look like anyway.
#
# ORIENTATION CONTRACT, load-bearing for DEF-08 and stated in BLENDER axes.
# The client ships six meshes and rotates them by yaw rather than shipping
# sixteen, so every variant is built in its mask-canonical rotation:
#   com_wall_post      isolated, no arms
#   com_wall_straight  one span along the X axis (arms +X and -X)
#   com_wall_cap       ONE arm, +X. The terminating block sits on the origin,
#                      so the run continues toward +X and the block is the
#                      exposed end.
#   com_wall_corner    TWO arms, +X and +Y
#   com_wall_tee       THREE arms: +X, +Y and -Y. The -X arm is OMITTED.
#   com_wall_cross     FOUR arms
# The axis conversion is Blender +Y forward becomes glTF -Z. DEF-08 owns the
# mask-to-yaw table and must derive it from the contract above; the DEF-08
# comment block above WallVariant in ModelLibrary.cs carries that derivation
# and the shipped table, and doc 22's DEF-08 spec repeats it. Read either, but
# treat THIS comment as the authority on which way each mesh points: the
# tables are downstream of it.
#
# CORRECTION, and note the trap because it caught two readers already. The draft
# table in doc 22 was wrong on TEN of its sixteen entries, not on its tee row
# alone: it read the "+X" above as north and used the opposite rotation sense.
# Through the client's mapping (sim X to world X, sim Y to world Z) Blender +X
# is EAST and Blender +Y is NORTH, so the canonical cap is {E} and the canonical
# tee is mask 7, omitting WEST. An earlier version of this comment said the
# draft's tee row was "wrong twice over" and pointed at DEF-07's ledger finding
# (d) for the corrected row. Both claims are WITHDRAWN: the tee row is in fact
# the draft's soundest, with [7] and [14] already correct, and finding (d)'s
# proposed row keeps the draft's two false premises and would break those two.
# Doc 22 and the ledger have both been corrected to match the derivation,
# and art/3d/wall-yaw-gate.py now machine-checks the whole chain (this
# contract, the exported bytes, ModelLibrary.cs, doc 22, the ledger) so a
# third reader cannot repeat the mistake silently.

# Doc 16 as it currently stands, and it is the CURRENT text that governs here:
# "team colour appears in exactly one place per silhouette (the band/slash),
# always", and "COMMON hardware: field olive with ferrite-gold marks". A
# barrier is com_ shared hardware - one mesh serves both players - so the mark
# is ferrite gold, matching the only other com_ team band in the roster
# (com_harvester). Signal orange on a mesh both factions place would paint a
# Directorate stripe on a Sodality wall.
BARRIER_MARK = 'ferrite'
# Doc 22 section 5 PROPOSES marking only where the neighbour count is not 2,
# i.e. no mark on a straight mid-run segment. That amendment is PROPOSED and
# blocked on Luke, and section 5.3 makes this ticket's band rule contingent on
# his ruling, so the current one-place law governs and every variant carries
# its one band. If the amendment is ratified, flip this to False: it is the
# entire mechanical difference.
BARRIER_MARK_MIDRUN = True

_BW = 0.95   # span length: the segment's reach across its own cell
_BT = 0.34   # span thickness
_BH = 0.5    # span height above the footing deck
_BZ = 0.06   # footing top - every part above stands on this deck
# The ticket's floor for the footing was 0.85, which renders a continuous run
# standing on a dashed line of separate plates: the 0.15 inter-cell gap is
# about 4 pixels at the shipped camera and the footing shadow draws attention
# to it. Matching the span at 0.95 gives the run one unbroken plinth, which is
# the whole point of an auto-connecting barrier. Still inside the 1x1 cell.
_BF = 0.95   # footing plan size

def _bfoot():
    return box('foot', _BF, _BF, _BZ, m='gundark', bevel=0.02)

def _barm(name, ax, ay):
    # one half-span, running from the origin out to its cell edge
    h = _BW / 2
    if ax:
        return box(name, h, _BT, _BH, ax * h / 2, 0, _BZ, 'olived', 0.04)
    return box(name, _BT, h, _BH, 0, ay * h / 2, _BZ, 'olived', 0.04)

def _bknuckle():
    return cyl('knuckle', 0.22, 0.62, 0, 0, _BZ + 0.31, 'olive', vs=8)

def _bband(z):
    # the one team-colour place, per doc 16's one-place law
    return box('bd', 0.34, 0.08, 0.06, 0, 0, z, BARRIER_MARK, 0.015)

def com_wall_post():
    # mask 0: an isolated segment with no neighbours
    parts = [_bfoot()]
    parts.append(box('post', 0.5, 0.5, 0.55, 0, 0, _BZ, 'olived', 0.04))
    parts.append(_bband(_BZ + 0.55))
    return join(parts, 'com_wall_post')

def com_wall_straight():
    # masks 5 and 10: the mid-run segment, one span along the X axis. The two
    # stiffener ribs stand proud of the span top so a long run reads as an
    # articulated barrier rather than one extruded bar at 40 pixels.
    parts = [_bfoot()]
    parts.append(box('span', _BW, _BT, _BH, 0, 0, _BZ, 'olived', 0.04))
    for i, rx in enumerate((-0.3, 0.3)):
        parts.append(cyl(f'rib{i}', 0.14, 0.60, rx, 0, _BZ + 0.30, 'gundark', vs=8))
    if BARRIER_MARK_MIDRUN:
        parts.append(_bband(_BZ + _BH))
    return join(parts, 'com_wall_straight')

def com_wall_cap():
    # masks 1/2/4/8: a run's end. The arm reaches its +X neighbour and the
    # thicker block terminates the exposed end on the origin.
    parts = [_bfoot()]
    parts.append(_barm('span', 1, 0))
    parts.append(box('cap', 0.42, 0.42, 0.6, 0, 0, _BZ, 'olive', 0.04))
    parts.append(_bband(_BZ + 0.6))
    return join(parts, 'com_wall_cap')

def com_wall_corner():
    # masks 3/6/12/9: two arms, +X and +Y, knuckled at the joint
    parts = [_bfoot()]
    parts.append(_barm('spanx', 1, 0))
    parts.append(_barm('spany', 0, 1))
    parts.append(_bknuckle())
    parts.append(_bband(_BZ + 0.62))
    return join(parts, 'com_wall_corner')

def com_wall_tee():
    # masks 7/14/13/11: three arms, +X +Y -Y. The -X arm is omitted.
    parts = [_bfoot()]
    parts.append(_barm('spanx', 1, 0))
    parts.append(_barm('spany', 0, 1))
    parts.append(_barm('spanyn', 0, -1))
    parts.append(_bknuckle())
    parts.append(_bband(_BZ + 0.62))
    return join(parts, 'com_wall_tee')

def com_wall_cross():
    # mask 15: four arms
    parts = [_bfoot()]
    parts.append(_barm('spanx', 1, 0))
    parts.append(_barm('spanxn', -1, 0))
    parts.append(_barm('spany', 0, 1))
    parts.append(_barm('spanyn', 0, -1))
    parts.append(_bknuckle())
    parts.append(_bband(_BZ + 0.62))
    return join(parts, 'com_wall_cross')

def ferrite_cluster(scale=1.0):
    # W4-07: seven faceted truncated shards in GOLD body material with small
    # emissive tips plus base rubble. The old whole-emissive cones clamped
    # white in the LDR emit bake; keeping the bodies non-emissive is what
    # finally lets the resource read gold.
    import random
    rnd = random.Random(7)
    objs = []
    shards = [(-0.42, 0.12, 0.95, 0.16), (0.05, -0.22, 1.35, 0.22),
              (0.5, 0.28, 0.7, 0.13), (-0.12, 0.5, 0.55, 0.11),
              (0.3, -0.45, 0.8, 0.14), (-0.55, -0.3, 0.5, 0.10),
              (0.62, -0.05, 0.45, 0.09)]
    def _facet(o):
        # Raw primitives put every vertex on a sharp edge, and wmat's
        # pointiness chip mask interpolates across the whole face - the
        # bodies baked grey, not gold (R/B 1.15 vs the palette's 1.42).
        # Bevel + subdivide give faces interior verts at pointiness 0.5,
        # confining chips to edges (measured back at R/B 1.43).
        md = o.modifiers.new('b', 'BEVEL'); md.width = 0.02; md.segments = 2
        bpy.ops.object.modifier_apply(modifier='b')
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.subdivide(number_cuts=3)
        bpy.ops.object.mode_set(mode='OBJECT')
    for i, (dx, dy, h, r) in enumerate(shards):
        rot = (rnd.uniform(-0.22, 0.22), rnd.uniform(-0.22, 0.22), rnd.uniform(0, 6.28))
        bpy.ops.mesh.primitive_cone_add(radius1=r*scale, radius2=r*0.25*scale,
            depth=h*scale, vertices=5, location=(dx*scale, dy*scale, h*scale*0.45))
        o = bpy.context.object; o.name = f'shard{i}'
        o.rotation_euler = rot
        _facet(o)
        o.data.materials.append(mat('ferrite', rough=0.35, metal=0.1))
        objs.append(o)
        bpy.ops.mesh.primitive_cone_add(radius1=r*0.55*scale, radius2=0.02,
            depth=h*0.38*scale, vertices=5, location=(dx*scale, dy*scale, h*scale*0.78))
        t = bpy.context.object; t.name = f'tip{i}'
        t.rotation_euler = rot
        t.data.materials.append(mat('fhi', emit=2.4, rough=0.3))
        objs.append(t)
    for j in range(5):   # base rubble ring
        a = j * 1.256
        bpy.ops.mesh.primitive_cube_add(size=0.14*scale,
            location=(0.55*scale*math.cos(a), 0.55*scale*math.sin(a), 0.05))
        c = bpy.context.object; c.name = f'rub{j}'
        c.rotation_euler = (0.3, 0.2, a)
        _facet(c)   # same chip-mask fix: keep the rubble dark cinder
        c.data.materials.append(mat('cinder', rough=0.95))
        objs.append(c)
    return join(objs, 'ferrite_cluster')


def child_part(hull, obj, name):
    # De-merged animation part (doc 20 Wave 2): keep obj a CHILD of hull so
    # glTF preserves the named node. Bake any object rotation into the mesh
    # first so the node transform is pure translation and the client can
    # spin around clean local axes.
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.name = name
    obj.parent = hull
    return obj

def dir_vanguard_car():
    # The vertical-slice unit (TICKET-P4-SLICE-01): a wheeled gun car, and
    # the first model whose turret stays a SEPARATE child object so the
    # client can slew it toward targets (glTF preserves object hierarchy
    # when objects are parented rather than joined).
    parts = [box('hull', 0.44, 0.66, 0.16, 0, 0, 0.10, 'gun', 0.04)]
    glacis = box('glacis', 0.38, 0.14, 0.10, 0, 0.34, 0.12, 'plate', 0.03)
    glacis.rotation_euler = (-0.55, 0, 0)
    parts.append(glacis)
    parts.append(box('cab', 0.34, 0.2, 0.1, 0, 0.12, 0.26, 'gun', 0.03))
    parts.append(box('screen', 0.28, 0.02, 0.06, 0, 0.225, 0.29, 'glow', 0.008, emit=1.8))
    vwheels = []
    for sx in (-0.235, 0.235):   # exposed wheels: separate spinning children
        for i, wy in enumerate((-0.22, 0.22)):
            vwheels.append(cyl(f'vw{sx}{i}', 0.105, 0.09, sx, wy, 0.105, 'gundark', vs=12, ry=math.pi/2))
    parts.append(box('bumper', 0.4, 0.06, 0.08, 0, 0.42, 0.08, 'gundark', 0.015))
    parts.append(box('rack', 0.36, 0.18, 0.06, 0, -0.26, 0.22, 'gundark', 0.015))
    parts.append(team_band(0.3, -0.34, 0.2, 'orange'))
    parts += headlights(0.16, 0.44, 0.14)
    hull = join(parts, 'dir_vanguard_car')
    for i, w in enumerate(vwheels):
        child_part(hull, w, f'wheel{i}')

    tparts = [cyl('tring', 0.11, 0.04, 0, -0.04, 0.30, 'gundark', vs=12)]
    tparts.append(box('tbody', 0.16, 0.18, 0.09, 0, -0.04, 0.36, 'plate', 0.02))
    tparts.append(box('tshield', 0.2, 0.03, 0.12, 0, 0.06, 0.37, 'gun', 0.012))
    tparts.append(cyl('tgun', 0.024, 0.34, 0, 0.22, 0.375, 'gundark', rx=math.pi/2))
    tparts.append(cyl('tmuzz', 0.032, 0.05, 0, 0.4, 0.375, 'plate', vs=8, rx=math.pi/2))
    turret = join(tparts, 'turret')
    turret.parent = hull   # identity transforms after join: safe to parent
    return hull

# ---------------------------------------------------------------------------
# Doc 31 builders. Each is written from that document's physical description,
# and each replaces an interim mesh ModelLibrary.cs records as owed. The three
# Sodality entries carry the FIRST teal team bands in the project: every band
# before them was hardcoded 'orange' or 'ferrite', so the Sodality had none.
# ---------------------------------------------------------------------------

def com_barracks():
    """Doc 31: smaller and simpler than the factory, and must not be confused
    with it - a PITCHED roof against the factory's flat one is the distinction.
    Personnel door with a covered porch, small windows, kit rack and water butt."""
    parts = [pad()]
    parts.append(box('hall', 1.5, 1.1, 0.42, 0, 0.1, 0.08, 'olive', 0.06))
    # The pitched roof. First version canted two slabs and they read as floating
    # shelves - the pitch never formed, because they sat above the hall instead
    # of springing from its eaves. Second version builds the pitch as a STEPPED
    # WEDGE springing directly off the wall top, which reads as a roof at 40px
    # and needs no rotation at all. Caught on the render, not in review.
    for i, (w, z) in enumerate(((1.60, 0.32), (1.30, 0.42), (1.00, 0.51), (0.68, 0.58))):
        parts.append(box(f'roof{i}', 1.56, w, 0.10, 0, 0.1, z, 'olived', 0.02))
    parts.append(box('ridge', 1.58, 0.10, 0.08, 0, 0.1, 0.64, 'olived', 0.02))
    # Personnel door with porch: small, human-sized, unlike the factory's shutters.
    parts.append(box('door', 0.22, 0.05, 0.34, -0.3, -0.46, 0.25, 'cinder', 0.01))
    parts.append(box('porch', 0.40, 0.22, 0.05, -0.3, -0.58, 0.42, 'olived', 0.015))
    for i in range(3):
        parts.append(box(f'win{i}', 0.16, 0.04, 0.12, 0.05 + i * 0.34, -0.45, 0.34, 'glow', 0.008, emit=1.1))
    parts.append(box('kit', 0.5, 0.14, 0.16, 0.35, 0.7, 0.16, 'olived', 0.02))
    parts.append(cyl('butt', 0.11, 0.3, -0.62, 0.66, 0.23, 'rustd', vs=10))
    parts.append(team_band(0.34, -0.46, 0.20, 'orange'))
    return join(parts, 'com_barracks')


def com_radar_uplink():
    """Doc 31: the TALLEST THIN structure in the game - a small windowless
    blockhouse with a lattice mast and a large ferrite-gold dish angled skyward,
    guy wires to ground anchors. Pure vertical, which nothing else common has."""
    parts = [pad()]
    parts.append(box('block', 0.8, 0.8, 0.42, -0.25, 0, 0.08, 'olive', 0.05))
    parts.append(box('blockr', 0.7, 0.7, 0.05, -0.25, 0, 0.31, 'olived', 0.02))
    # The mast: four legs plus bracing, so it reads as lattice not as a pole.
    for i, (mx, my) in enumerate(((-0.09, -0.09), (0.09, -0.09), (-0.09, 0.09), (0.09, 0.09))):
        parts.append(box(f'leg{i}', 0.035, 0.035, 1.5, 0.35 + mx, my, 0.82, 'olived', 0.005))
    for i in range(4):
        z = 0.28 + i * 0.36
        parts.append(box(f'brc{i}', 0.20, 0.20, 0.025, 0.35, 0, z, 'olived', 0.005))
    # The dish, angled up: the identifying feature at distance.
    d = cyl('dish', 0.34, 0.06, 0.35, 0.06, 1.62, 'ferrite', vs=16)
    d.rotation_euler = (-0.7, 0, 0)
    parts.append(d)
    parts.append(cyl('horn', 0.045, 0.26, 0.35, -0.14, 1.72, 'gundark', vs=8, rx=-0.7))
    for i, gx in enumerate((-0.5, 0.5)):
        g = cyl(f'guy{i}', 0.012, 1.15, 0.35 + gx * 0.5, 0, 0.85, 'gundark', vs=6)
        g.rotation_euler = (0, gx * 0.55, 0)
        parts.append(g)
    parts.append(team_band(0.26, -0.41, 0.30, 'orange'))
    return join(parts, 'com_radar_uplink')


def com_outpost():
    """Doc 31: must read as NOBODY'S - weathered concrete, bleached, flat roof,
    slit windows, a BARE flagpole with nothing on it. No faction plate, no team
    colour at all until captured. Deliberately the only structure with no band."""
    parts = [pad('cinder')]
    parts.append(box('block', 1.15, 1.15, 0.5, 0, 0, 0.08, 'bone', 0.05))
    parts.append(box('cap', 1.25, 1.25, 0.07, 0, 0, 0.36, 'bone', 0.02))
    # Slit windows: narrow, high, defensive - not the refinery's big openings.
    for i, (wx, wy, sx, sy) in enumerate(((0, -0.59, 0.42, 0.04), (0, 0.59, 0.42, 0.04),
                                          (-0.59, 0, 0.04, 0.42), (0.59, 0, 0.04, 0.42))):
        parts.append(box(f'slit{i}', sx, sy, 0.07, wx, wy, 0.26, 'cinder', 0.008))
    parts.append(box('step', 0.4, 0.22, 0.06, 0, -0.66, 0.09, 'bone', 0.015))
    # The bare pole. Nothing flies from it until somebody claims this.
    parts.append(cyl('pole', 0.022, 1.0, 0.42, 0.42, 0.6, 'bone', vs=8))
    parts.append(cyl('finial', 0.04, 0.05, 0.42, 0.42, 1.12, 'bone', vs=8))
    return join(parts, 'com_outpost')


def dir_bastion():
    """Doc 31: the toughest building per credit and it must look it. A squat
    armoured blockhouse with steeply SLOPED flanks, a heavy weapon firing through
    an EMBRASURE rather than a turret on a pole, a stepped parapet, and a dish
    cluster on the roof earned by its two support powers."""
    parts = [pad()]
    parts.append(box('core', 1.25, 1.25, 0.34, 0, 0, 0.10, 'gun', 0.04))
    # Sloped glacis, built as a TAPERED STACK rather than canted flaps. The
    # first version rotated four thin plates outward and they read as fins
    # sticking off the sides, not as armour - seen immediately on the first
    # render, which is the whole reason to draw a thing before trusting it.
    for i, (w, z) in enumerate(((1.42, 0.16), (1.30, 0.26), (1.16, 0.35), (1.02, 0.43))):
        parts.append(box(f'gl{i}', w, w, 0.10, 0, 0, z, 'plate' if i % 2 else 'gun', 0.02))
    # Stepped parapet, then the embrasure: a slot, not a turret.
    parts.append(box('para', 0.94, 0.94, 0.13, 0, 0, 0.53, 'gun', 0.03))
    parts.append(box('para2', 0.74, 0.74, 0.10, 0, 0, 0.64, 'plate', 0.03))
    parts.append(box('emb', 0.44, 0.09, 0.12, 0, -0.50, 0.47, 'cinder', 0.01))
    parts.append(cyl('gun', 0.05, 0.55, 0, -0.70, 0.47, 'gundark', vs=10, rx=math.pi/2))
    parts.append(cyl('mzl', 0.07, 0.09, 0, -0.94, 0.47, 'gundark', vs=10, rx=math.pi/2))
    # The support-power aerials, earned by ADR-063 and ADR-064.
    dsh = cyl('sdish', 0.17, 0.04, 0.24, 0.22, 0.76, 'plate', vs=14)
    dsh.rotation_euler = (-0.6, 0, 0)
    parts.append(dsh)
    parts.append(cyl('mast', 0.018, 0.34, -0.26, 0.24, 0.84, 'gundark', vs=6))
    parts.append(team_band(0.42, -0.48, 0.66, 'orange'))
    return join(parts, 'dir_bastion')


def sod_watch_post():
    """Doc 31: a thin SCAFFOLD tower with a crow's nest, external ladder,
    scavenged aerials lashed on. UNARMED by design (GDD line 56) - no weapon
    anywhere on the silhouette, and that absence is the read."""
    parts = [pad('rustd')]
    # Four splayed scaffold legs: spindly and obviously fragile.
    for i, (lx, ly) in enumerate(((-0.24, -0.24), (0.24, -0.24), (-0.24, 0.24), (0.24, 0.24))):
        l = box(f'leg{i}', 0.05, 0.05, 1.25, lx, ly, 0.66, 'rustd', 0.008)
        l.rotation_euler = (-ly * 0.14, lx * 0.14, 0)
        parts.append(l)
    for i in range(3):
        z = 0.36 + i * 0.34
        parts.append(box(f'ring{i}', 0.50, 0.50, 0.03, 0, 0, z, 'rust', 0.006))
    # The crow's nest: a small enclosed box, mismatched plate.
    parts.append(box('nest', 0.62, 0.62, 0.30, 0, 0, 1.42, 'rust', 0.03))
    parts.append(box('nestp', 0.68, 0.30, 0.20, 0.02, -0.20, 1.44, 'rustp', 0.02))
    parts.append(box('nestr', 0.70, 0.70, 0.05, 0, 0, 1.60, 'rustd', 0.02))
    parts.append(box('rail', 0.72, 0.05, 0.10, 0, -0.34, 1.62, 'rustd', 0.01))
    # Ladder rungs up one leg.
    for i in range(6):
        parts.append(box(f'rung{i}', 0.22, 0.02, 0.02, 0, -0.26, 0.30 + i * 0.18, 'rustd', 0.004))
    # Scavenged aerials, lashed on: the jamming array, and nothing that shoots.
    parts.append(cyl('aer1', 0.012, 0.44, -0.18, 0.14, 1.80, 'rustd', vs=6))
    a2 = cyl('aer2', 0.012, 0.34, 0.20, -0.10, 1.74, 'rustd', vs=6)
    a2.rotation_euler = (0.3, 0.25, 0)
    parts.append(a2)
    sd = cyl('sdish', 0.13, 0.03, 0.16, 0.20, 1.70, 'rustp', vs=12)
    sd.rotation_euler = (-0.8, 0, 0)
    parts.append(sd)
    parts.append(team_band(0.30, -0.36, 1.34, 'teal'))
    return join(parts, 'sod_watch_post')


def sod_generator():
    """Doc 31: the cheapest, flimsiest thing in the game at 130cr/70hp. A single
    SALVAGED FUEL DRUM stood on end on a pallet, small motor bolted on top, a
    pull-cord, cables trailing away. Players build a dozen, so it must cluster
    without becoming noise."""
    parts = []
    parts.append(box('pallet', 0.62, 0.62, 0.05, 0, 0, 0.02, 'rustd', 0.01))
    for i in range(3):
        parts.append(box(f'slat{i}', 0.58, 0.10, 0.03, 0, -0.2 + i * 0.2, 0.06, 'rustd', 0.006))
    # The drum: ribbed, dented, obviously second-hand.
    parts.append(cyl('drum', 0.24, 0.52, 0, 0, 0.34, 'rust', vs=14))
    for i in range(2):
        parts.append(cyl(f'rib{i}', 0.255, 0.035, 0, 0, 0.22 + i * 0.24, 'rustd', vs=14))
    parts.append(cyl('lid', 0.245, 0.03, 0, 0, 0.61, 'rustd', vs=14))
    # Motor bolted to the top, with a pull-cord handle.
    parts.append(box('motor', 0.26, 0.20, 0.16, 0.02, 0, 0.70, 'gundark', 0.02))
    parts.append(cyl('cord', 0.015, 0.12, -0.16, 0, 0.72, 'bone', vs=6, ry=math.pi/2))
    parts.append(cyl('exh', 0.028, 0.18, 0.14, 0.06, 0.86, 'gundark', vs=8))
    # Cables trailing off across the ground - the decentralised grid, visibly.
    for i, (cx, cy, rot) in enumerate(((0.34, 0.20, 0.5), (0.30, -0.26, -0.7))):
        c = cyl(f'cab{i}', 0.016, 0.44, cx, cy, 0.03, 'cinder', vs=6, ry=math.pi/2)
        c.rotation_euler = (0, math.pi/2, rot)
        parts.append(c)
    parts.append(team_band(0.16, -0.25, 0.44, 'teal', d=0.04))
    return join(parts, 'sod_generator')


def com_emplacement():
    """Doc 31: cheaper and lower than the turret - a SANDBAGGED RING with a short
    weapon on a pintle, mostly horizontal. Reads as a pit, not a tower."""
    parts = [pad()]
    # Sandbag ring: staggered blocks around a circle, two courses.
    for course, (rad, z, n) in enumerate(((0.62, 0.14, 14), (0.58, 0.26, 12))):
        for i in range(n):
            a = (i + course * 0.5) * (2 * math.pi / n)
            b = box(f'sb{course}_{i}', 0.20, 0.13, 0.12,
                    math.cos(a) * rad, math.sin(a) * rad, z, 'olived', 0.03)
            b.rotation_euler = (0, 0, a)
            parts.append(b)
    parts.append(cyl('floor', 0.52, 0.06, 0, 0, 0.11, 'cinder', vs=14))
    parts.append(cyl('pintle', 0.10, 0.20, 0, 0, 0.22, 'gundark', vs=10))
    parts.append(box('gunbox', 0.20, 0.24, 0.14, 0, 0.02, 0.36, 'olived', 0.02))
    parts.append(cyl('barrel', 0.035, 0.44, 0, -0.28, 0.38, 'gundark', vs=8, rx=math.pi/2))
    parts.append(box('ammo', 0.16, 0.12, 0.10, 0.30, 0.22, 0.20, 'ferrite', 0.02))
    parts.append(team_band(0.24, -0.66, 0.24, 'orange'))
    return join(parts, 'com_emplacement')


def com_gate():
    """Doc 31: visibly a wall segment WITH A MOVING PART - two heavy posts, a
    barred sliding leaf, a mechanism housing. Open and shut must be
    distinguishable at 40px, so the leaf must change the silhouette."""
    parts = []
    for i, px in enumerate((-0.62, 0.62)):
        parts.append(box(f'post{i}', 0.26, 0.34, 0.92, px, 0, 0.46, 'olived', 0.03))
        parts.append(box(f'cap{i}', 0.32, 0.40, 0.07, px, 0, 0.94, 'olive', 0.02))
    # The leaf: barred, so it reads as a gate rather than a solid wall.
    parts.append(box('leafT', 1.00, 0.14, 0.10, 0, 0, 0.74, 'gundark', 0.02))
    parts.append(box('leafB', 1.00, 0.14, 0.10, 0, 0, 0.16, 'gundark', 0.02))
    for i in range(5):
        parts.append(box(f'bar{i}', 0.06, 0.10, 0.52, -0.40 + i * 0.20, 0, 0.45, 'gundark', 0.012))
    parts.append(box('mech', 0.20, 0.22, 0.24, 0.62, 0.30, 0.60, 'gun', 0.03))
    parts.append(cyl('wheel', 0.10, 0.05, 0.62, 0.44, 0.60, 'ferrite', vs=12, rx=math.pi/2))
    parts.append(team_band(0.20, -0.20, 0.74, 'orange'))
    return join(parts, 'com_gate')


def com_mine():
    """Doc 31: small, low, almost FLUSH with the ground - barely a silhouette by
    design. A single ferrite-gold pressure plate is the only visible feature."""
    parts = []
    parts.append(cyl('body', 0.30, 0.08, 0, 0, 0.04, 'olived', vs=14))
    parts.append(cyl('rim', 0.33, 0.04, 0, 0, 0.02, 'cinder', vs=14))
    parts.append(cyl('plate', 0.16, 0.05, 0, 0, 0.10, 'ferrite', vs=12, emit=0.9))
    # Disturbed earth: a few low scattered clods, so it reads as buried.
    for i, (cx, cy, cs) in enumerate(((0.34, 0.12, 0.10), (-0.28, 0.26, 0.08),
                                      (0.10, -0.36, 0.09), (-0.32, -0.18, 0.07))):
        parts.append(box(f'clod{i}', cs, cs, 0.035, cx, cy, 0.02, 'cinder', 0.015))
    return join(parts, 'com_mine')


def com_bridge():
    """Doc 31: a flat roadway span with VISIBLE TRUSS GIRDERS BENEATH and low kerb
    rails. No superstructure above deck level, so it reads as terrain rather than
    building. The underside trusses make felling it feel structural."""
    parts = []
    parts.append(box('deck', 1.9, 0.95, 0.09, 0, 0, 0.30, 'olived', 0.02))
    for i, ky in enumerate((-0.44, 0.44)):
        parts.append(box(f'kerb{i}', 1.9, 0.08, 0.10, 0, ky, 0.39, 'olive', 0.02))
    # Trusses underneath: the whole point of the mesh.
    for i, ty in enumerate((-0.32, 0.32)):
        parts.append(box(f'gird{i}', 1.86, 0.07, 0.14, 0, ty, 0.19, 'gundark', 0.015))
        for k in range(5):
            d = box(f'diag{i}_{k}', 0.30, 0.05, 0.05, -0.72 + k * 0.36, ty, 0.19, 'gundark', 0.01)
            d.rotation_euler = (0, 0.7 if k % 2 else -0.7, 0)
            parts.append(d)
    for i, ax in enumerate((-0.92, 0.92)):
        parts.append(box(f'abut{i}', 0.22, 1.0, 0.34, ax, 0, 0.17, 'cinder', 0.02))
    return join(parts, 'com_bridge')


def com_airfield():
    """Doc 31: deliberately HORIZONTAL - a flat pad with ferrite-gold landing
    markings, a low fuel bowser, and a slim control mast at one corner. Low, so
    the aircraft standing on it reads as the tall element."""
    parts = []
    parts.append(box('pad', 1.9, 1.9, 0.07, 0, 0, 0.03, 'cinder', 0.03))
    parts.append(box('apron', 1.7, 1.7, 0.03, 0, 0, 0.08, 'olived', 0.02))
    # Landing markings: the read from directly above, which is this camera.
    parts.append(box('centreline', 1.30, 0.09, 0.02, 0, 0, 0.10, 'ferrite', 0.005))
    for i, my in enumerate((-0.34, 0.34)):
        parts.append(box(f'bar{i}', 0.44, 0.08, 0.02, 0, my, 0.10, 'ferrite', 0.005))
    for i, (cx, cy) in enumerate(((-0.72, -0.72), (0.72, -0.72), (-0.72, 0.72))):
        parts.append(box(f'chev{i}', 0.20, 0.20, 0.02, cx, cy, 0.10, 'ferrite', 0.005))
    # Fuel bowser and control mast: low, at the edges.
    parts.append(cyl('bowser', 0.16, 0.52, -0.74, 0.30, 0.24, 'olive', vs=12, ry=math.pi/2))
    parts.append(box('bwheel', 0.40, 0.16, 0.10, -0.74, 0.30, 0.10, 'gundark', 0.02))
    parts.append(box('mbase', 0.26, 0.26, 0.22, 0.74, 0.74, 0.18, 'olive', 0.03))
    for i in range(4):
        parts.append(box(f'mleg{i}', 0.04, 0.04, 0.62, 0.74 + (0.07 if i % 2 else -0.07),
                         0.74 + (0.07 if i < 2 else -0.07), 0.58, 'olived', 0.006))
    parts.append(box('cab', 0.32, 0.32, 0.22, 0.74, 0.74, 1.00, 'olive', 0.03))
    parts.append(box('glass', 0.34, 0.34, 0.10, 0.74, 0.74, 1.04, 'glow', 0.01, emit=0.8))
    parts.append(team_band(0.30, -0.86, 0.12, 'orange', d=0.05))
    return join(parts, 'com_airfield')


def sod_shroud_nest():
    """Doc 31: a LEAN-TO of mismatched plate propped at an angle, weapon poking
    through a gap. Corrugated iron and salvaged panels of different ages.
    Deliberately temporary against the Bastion's permanence."""
    parts = [pad('rustd')]
    parts.append(box('base', 1.15, 1.05, 0.26, 0, 0, 0.10, 'rustd', 0.03))
    # The lean-to: one big canted sheet, propped. Rotated deliberately here -
    # unlike the barracks roof, a LEAN-TO is meant to read as a tilted plane.
    roof = box('lean', 1.25, 1.05, 0.07, 0.02, 0, 0.56, 'rustp', 0.02)
    roof.rotation_euler = (0, -0.42, 0)
    parts.append(roof)
    parts.append(box('prop1', 0.07, 0.07, 0.62, -0.52, -0.42, 0.42, 'rustd', 0.01))
    parts.append(box('prop2', 0.07, 0.07, 0.62, -0.52, 0.42, 0.42, 'rustd', 0.01))
    # Mismatched plate: three panels of visibly different ages.
    parts.append(box('pl1', 0.42, 0.06, 0.34, -0.30, -0.54, 0.36, 'rust', 0.02))
    parts.append(box('pl2', 0.36, 0.06, 0.26, 0.16, -0.54, 0.32, 'rustp', 0.02))
    parts.append(box('pl3', 0.30, 0.06, 0.30, 0.48, -0.54, 0.34, 'rustd', 0.02))
    # The weapon, through a gap rather than on a mount.
    parts.append(box('gap', 0.22, 0.10, 0.16, 0.04, -0.55, 0.44, 'cinder', 0.01))
    parts.append(cyl('gun', 0.045, 0.46, 0.04, -0.78, 0.44, 'gundark', vs=8, rx=math.pi/2))
    for i, (sx, sy) in enumerate(((-0.46, 0.50), (0.46, 0.50))):
        parts.append(box(f'bag{i}', 0.28, 0.16, 0.14, sx, sy, 0.14, 'rustd', 0.04))
    parts.append(team_band(0.26, -0.60, 0.60, 'teal'))
    return join(parts, 'sod_shroud_nest')


def sod_seismic_charge():
    """Doc 31: the deliberate OPPOSITE of the orbital cannon - where that aims up,
    this DRIVES DOWN. A lattice derrick with a massive piston hammer suspended in
    it, cables and counterweights, and a concrete collar around the borehole."""
    parts = [pad('rustd')]
    parts.append(cyl('collar', 0.44, 0.16, 0, 0, 0.12, 'cinder', vs=16))
    parts.append(cyl('bore', 0.30, 0.10, 0, 0, 0.20, 'cinder', vs=16))
    parts.append(cyl('glow', 0.26, 0.03, 0, 0, 0.23, 'ferrite', vs=16, emit=1.3))
    # The derrick: four splayed legs with cross-bracing, tapering upward.
    for i, (lx, ly) in enumerate(((-0.42, -0.42), (0.42, -0.42), (-0.42, 0.42), (0.42, 0.42))):
        l = box(f'leg{i}', 0.07, 0.07, 1.70, lx, ly, 0.88, 'rustd', 0.012)
        l.rotation_euler = (-ly * 0.16, lx * 0.16, 0)
        parts.append(l)
    for i, z in enumerate((0.42, 0.86, 1.30)):
        w = 0.86 - i * 0.14
        parts.append(box(f'brc{i}', w, w, 0.04, 0, 0, z, 'rust', 0.008))
    # The hammer, suspended: heavy, dark, obviously about to fall.
    parts.append(box('hammer', 0.34, 0.34, 0.52, 0, 0, 1.02, 'gundark', 0.03))
    parts.append(box('hcap', 0.40, 0.40, 0.08, 0, 0, 1.32, 'gun', 0.02))
    parts.append(cyl('cable', 0.022, 0.36, 0, 0, 1.54, 'cinder', vs=6))
    parts.append(box('crown', 0.52, 0.52, 0.12, 0, 0, 1.76, 'rust', 0.02))
    parts.append(cyl('sheave', 0.14, 0.06, 0, 0, 1.84, 'gundark', vs=12, rx=math.pi/2))
    # Counterweights either side, on cables.
    for i, cx in enumerate((-0.56, 0.56)):
        parts.append(box(f'cw{i}', 0.18, 0.18, 0.30, cx, 0, 0.74, 'gundark', 0.02))
        parts.append(cyl(f'cwc{i}', 0.014, 0.80, cx, 0, 1.28, 'cinder', vs=6))
    parts.append(team_band(0.30, -0.50, 0.30, 'teal'))
    return join(parts, 'sod_seismic_charge')


def _single(name, coat='olive', head='bone', hood=False, armour=False,
            satchel=False, rifle=False, tall=1.0, band=None):
    """Doc 31's four single figures share a skeleton so they read as the same
    SCALE of thing, and differ only where the brief says they differ. Built from
    infantry()'s proportions rather than new ones, so a lone figure and a squad
    member are recognisably the same army."""
    parts = []
    bh = 0.22 * tall
    parts.append(cyl('body', 0.075 if armour else 0.065, bh, 0, 0, bh / 2, coat, vs=8))
    if armour:
        # Segmented plate over chest and shoulders: the hero read.
        parts.append(box('chest', 0.19, 0.13, 0.11, 0, 0, bh * 0.72, coat, 0.02))
        for i, sx in enumerate((-0.11, 0.11)):
            parts.append(box(f'pauld{i}', 0.09, 0.11, 0.07,
                             sx, 0, bh * 0.92, coat, 0.02))
    parts.append(cyl('head', 0.05, 0.075, 0, 0, bh + 0.045, head, vs=8))
    if hood:
        # Asymmetric hood: the Sodality hero, assembled rather than issued.
        hd = cyl('hood', 0.068, 0.10, 0.012, -0.01, bh + 0.055, coat, vs=8)
        hd.rotation_euler = (0.2, 0.15, 0)
        parts.append(hd)
    if rifle:
        r = box('rifle', 0.022, 0.30 * tall, 0.022, 0.055, 0.01, bh * 0.7, 'gundark', 0.004)
        r.rotation_euler = (0, 0, 0.45)
        parts.append(r)
    if satchel:
        # Carried LOW at the hip - the one geometric addition over the
        # infiltrator, and the only thing separating them on a second look.
        parts.append(box('satchel', 0.10, 0.07, 0.09, 0.085, -0.02, bh * 0.42, 'rustd', 0.015))
        st = box('strap', 0.015, 0.05, 0.20, 0.03, -0.02, bh * 0.68, 'rustd', 0.004)
        st.rotation_euler = (0, 0.5, 0)
        parts.append(st)
    if band is not None:
        # Doc 16's law: one team-colour place per silhouette, ALWAYS. The first
        # render of this batch caught dir_commando carrying none while its
        # Sodality twin did, which is the law broken and the pair asymmetric.
        parts.append(team_band(0.09, -0.055, bh * 0.80, band, d=0.03))
    parts.append(cyl('base', 0.22, 0.02, 0, 0, 0.01, 'olived', vs=14))
    return join(parts, name)


def dir_commando():
    """Doc 31: larger than any other infantry - hero scale is intended. Segmented
    plate, full helmet, long rifle held across the body, fully UPRIGHT where line
    infantry hunch. Signal orange generous: never lose track of it."""
    o = _single('dir_commando', coat='gun', head='gundark',
                armour=True, rifle=True, tall=1.35, band='orange')
    return o


def sod_shadow_commando():
    """Doc 31: the Sodality hero. Mismatched scavenged plate, deliberately
    asymmetric - a heavy pauldron on ONE shoulder, a hood over a partial mask.
    Assembled from what was available, against the Directorate's issued twin."""
    parts = []
    bh = 0.22 * 1.35
    parts.append(cyl('body', 0.075, bh, 0, 0, bh / 2, 'rust', vs=8))
    parts.append(box('chest', 0.18, 0.12, 0.11, 0.008, 0, bh * 0.72, 'rustp', 0.02))
    # ONE pauldron, not two: the asymmetry is the faction language.
    parts.append(box('pauld', 0.11, 0.12, 0.09, -0.115, 0, bh * 0.93, 'rustd', 0.02))
    parts.append(cyl('head', 0.05, 0.075, 0, 0, bh + 0.045, 'rustd', vs=8))
    hd = cyl('hood', 0.07, 0.10, 0.012, -0.012, bh + 0.058, 'rust', vs=8)
    hd.rotation_euler = (0.22, 0.16, 0)
    parts.append(hd)
    r = box('rifle', 0.022, 0.40, 0.022, 0.06, 0.01, bh * 0.7, 'gundark', 0.004)
    r.rotation_euler = (0, 0, 0.45)
    parts.append(r)
    # Wrapped cloth at the forearms, per the brief.
    for i, wx in enumerate((-0.06, 0.07)):
        parts.append(cyl(f'wrap{i}', 0.026, 0.07, wx, 0.02, bh * 0.55, 'rustd', vs=6))
    parts.append(team_band(0.09, -0.055, bh * 0.80, 'teal', d=0.03))
    parts.append(cyl('base', 0.22, 0.02, 0, 0, 0.01, 'olived', vs=14))
    return join(parts, 'sod_shadow_commando')


def sod_infiltrator():
    """Doc 31: a long CIVILIAN COAT, no visible weapon at all, hands at the sides,
    head bare or lightly hooded. It reads as a person rather than a soldier,
    which is what makes it unnerving among welded machines."""
    return _single('sod_infiltrator', coat='rustd', head='bone', tall=1.0)


def sod_saboteur():
    """Doc 31: the same coat and posture, distinguished ONLY by a satchel carried
    low at the hip. Confusable at a glance and separable on a second look - which
    is the doctrine, not an accident."""
    return _single('sod_saboteur', coat='rustd', head='bone', satchel=True, tall=1.0)


def com_repair_vehicle():
    """Doc 31: small boxy unarmed tracked utility, roughly a third of the MCV. The
    defining feature is a FOLDED ARTICULATED ARM along the roof with a claw and
    visible hydraulics. Gold hazard striping. Toolboxes on the flanks."""
    parts = []
    parts += tracks(0.30, 0.62, wheel_r=0.055, wheels=4)
    parts.append(box('hull', 0.46, 0.62, 0.20, 0, 0, 0.20, 'olive', 0.03))
    parts.append(box('cab', 0.34, 0.24, 0.16, 0, -0.16, 0.36, 'olive', 0.03))
    parts.append(box('glass', 0.28, 0.05, 0.09, 0, -0.28, 0.38, 'glow', 0.01, emit=0.6))
    # The folded arm: the whole read, because there is no turret.
    parts.append(box('armb', 0.10, 0.12, 0.09, 0, 0.16, 0.35, 'olived', 0.02))
    parts.append(box('arm1', 0.07, 0.34, 0.06, 0, 0.06, 0.42, 'olived', 0.015))
    a2 = box('arm2', 0.06, 0.26, 0.05, 0, -0.10, 0.47, 'olived', 0.012)
    a2.rotation_euler = (0.35, 0, 0)
    parts.append(a2)
    parts.append(box('claw', 0.08, 0.07, 0.05, 0, -0.24, 0.51, 'gundark', 0.01))
    for i, hx in enumerate((-0.045, 0.045)):
        parts.append(cyl(f'hyd{i}', 0.014, 0.20, hx, 0.06, 0.38, 'gundark', vs=6, rx=math.pi/2))
    # Gold hazard striping, and stowage.
    for i in range(3):
        parts.append(box(f'haz{i}', 0.05, 0.03, 0.021, -0.10 + i * 0.10, 0.24, 0.31, 'ferrite', 0.004))
    parts.append(box('tools', 0.09, 0.16, 0.09, 0.27, 0.08, 0.24, 'olived', 0.02))
    parts.append(cyl('drum', 0.055, 0.10, -0.27, 0.14, 0.25, 'rustd', vs=8, ry=math.pi/2))
    parts.append(team_band(0.22, -0.32, 0.24, 'orange'))
    return join(parts, 'com_repair_vehicle')


def com_carrier():
    """Doc 31: a long low UNARMED transport with a flat open deck over two thirds
    of its length, shallow drop-sides, tie-down rails and a fold-down rear ramp.
    SIX WHEELS, not tracks - a lighter, more civilian look than anything armed."""
    parts = []
    parts.append(box('chassis', 0.44, 1.06, 0.10, 0, 0, 0.14, 'olived', 0.02))
    # Six wheels: the civilian read.
    for i, (wx, wy) in enumerate([(sx, sy) for sx in (-0.25, 0.25)
                                  for sy in (-0.34, 0.06, 0.38)]):
        parts.append(cyl(f'w{i}', 0.095, 0.07, wx, wy, 0.095, 'gundark', vs=10, ry=math.pi/2))
    parts.append(box('cab', 0.40, 0.28, 0.22, 0, -0.38, 0.30, 'olive', 0.03))
    parts.append(box('grille', 0.34, 0.05, 0.12, 0, -0.53, 0.26, 'gundark', 0.015))
    parts.append(box('wind', 0.32, 0.04, 0.10, 0, -0.25, 0.38, 'glow', 0.01, emit=0.5))
    # The deck: deliberately EMPTY on top so cargo reads when loaded.
    parts.append(box('deck', 0.44, 0.66, 0.05, 0, 0.20, 0.22, 'olived', 0.015))
    for i, sx in enumerate((-0.21, 0.21)):
        parts.append(box(f'side{i}', 0.04, 0.66, 0.10, sx, 0.20, 0.28, 'olive', 0.015))
    for i in range(4):
        parts.append(box(f'rail{i}', 0.03, 0.03, 0.05, -0.19 + (i % 2) * 0.38,
                         0.02 + (i // 2) * 0.34, 0.33, 'gundark', 0.006))
    ramp = box('ramp', 0.42, 0.20, 0.04, 0, 0.60, 0.20, 'olived', 0.012)
    ramp.rotation_euler = (-0.5, 0, 0)
    parts.append(ramp)
    parts.append(team_band(0.26, -0.53, 0.34, 'orange'))
    return join(parts, 'com_carrier')


def com_flak_track():
    """Doc 31: a light HALF-TRACKED chassis with an open-topped mount carrying
    four short fat barrels angled steeply UPWARD - the only ground unit whose
    silhouette points at the sky. Ammo boxes, exposed seat, traverse wheel."""
    parts = []
    parts.append(box('hull', 0.42, 0.86, 0.16, 0, 0, 0.18, 'olive', 0.03))
    # Half-track: wheels at the front, track at the back.
    for i, wx in enumerate((-0.23, 0.23)):
        parts.append(cyl(f'fw{i}', 0.085, 0.06, wx, -0.32, 0.085, 'gundark', vs=10, ry=math.pi/2))
    parts += tracks(0.23, 0.40, wheel_r=0.065, wheels=3)
    parts.append(box('cab', 0.38, 0.24, 0.18, 0, -0.24, 0.29, 'olive', 0.03))
    parts.append(box('wind', 0.30, 0.04, 0.09, 0, -0.36, 0.34, 'glow', 0.01, emit=0.5))
    # The mount: open, exposed, obviously converted.
    parts.append(cyl('ring', 0.17, 0.05, 0, 0.20, 0.28, 'olived', vs=12))
    parts.append(box('mount', 0.16, 0.16, 0.10, 0, 0.20, 0.34, 'gundark', 0.02))
    for i, (bx, bz) in enumerate(((-0.045, 0), (0.045, 0), (-0.045, 0.05), (0.045, 0.05))):
        b = cyl(f'bar{i}', 0.022, 0.30, bx, 0.16, 0.50 + bz, 'gundark', vs=8)
        b.rotation_euler = (-0.55, 0, 0)
        parts.append(b)
    parts.append(box('seat', 0.09, 0.09, 0.07, 0.13, 0.24, 0.34, 'olived', 0.015))
    parts.append(cyl('trav', 0.05, 0.02, -0.14, 0.24, 0.34, 'ferrite', vs=10, ry=math.pi/2))
    for i, ax in enumerate((-0.20, 0.20)):
        parts.append(box(f'ammo{i}', 0.08, 0.14, 0.08, ax, 0.36, 0.26, 'ferrite', 0.015))
    parts.append(team_band(0.22, -0.39, 0.24, 'orange'))
    return join(parts, 'com_flak_track')


def com_strike_flyer():
    """Doc 31: a small single-seat aircraft with FORWARD-SWEPT wings, a slim
    tapering fuselage, a bubble canopy set well forward, twin tail fins and an
    underslung rocket pod beneath each wing. Fast and fragile - keep it spare."""
    parts = []
    parts.append(cyl('fuse', 0.075, 0.86, 0, 0, 0.42, 'olive', vs=10, rx=math.pi/2))
    parts.append(cyl('nose', 0.05, 0.18, 0, -0.48, 0.42, 'olived', vs=10, rx=math.pi/2))
    parts.append(box('canopy', 0.11, 0.22, 0.08, 0, -0.20, 0.50, 'glow', 0.02, emit=0.7))
    # Forward-swept wings: the identifying feature, so the sweep must be visible.
    for i, sx in enumerate((-1, 1)):
        w = box(f'wing{i}', 0.44, 0.20, 0.03, sx * 0.28, 0.02, 0.40, 'olive', 0.012)
        w.rotation_euler = (0, 0, sx * -0.42)
        parts.append(w)
        parts.append(cyl(f'pod{i}', 0.035, 0.22, sx * 0.34, 0.02, 0.34, 'gundark', vs=8, rx=math.pi/2))
    # Twin tail fins.
    for i, sx in enumerate((-1, 1)):
        f = box(f'fin{i}', 0.03, 0.16, 0.16, sx * 0.09, 0.38, 0.50, 'olived', 0.01)
        f.rotation_euler = (0, sx * 0.25, 0)
        parts.append(f)
    parts.append(box('tplane', 0.28, 0.12, 0.025, 0, 0.40, 0.42, 'olived', 0.01))
    parts.append(team_band(0.14, 0.30, 0.56, 'orange', d=0.035))
    return join(parts, 'com_strike_flyer')


BUILDERS = dict(
    dir_commando=dir_commando, sod_shadow_commando=sod_shadow_commando,
    sod_infiltrator=sod_infiltrator, sod_saboteur=sod_saboteur,
    com_repair_vehicle=com_repair_vehicle, com_carrier=com_carrier,
    com_flak_track=com_flak_track, com_strike_flyer=com_strike_flyer,
    com_emplacement=com_emplacement, com_gate=com_gate, com_mine=com_mine,
    com_bridge=com_bridge, com_airfield=com_airfield,
    sod_shroud_nest=sod_shroud_nest, sod_seismic_charge=sod_seismic_charge,
    com_barracks=com_barracks, com_radar_uplink=com_radar_uplink,
    com_outpost=com_outpost, dir_bastion=dir_bastion,
    sod_watch_post=sod_watch_post, sod_generator=sod_generator,

    dir_cannon_tank=dir_cannon_tank, dir_bulwark_tank=dir_bulwark_tank,
    dir_howitzer=dir_howitzer, dir_sentinel_scout=dir_sentinel_scout,
    sod_phantom_tank=sod_phantom_tank, sod_shade_raider=sod_shade_raider,
    com_rifle_squad=lambda: infantry('com_rifle_squad'),
    com_rocket_squad=lambda: infantry('com_rocket_squad', tube=True),
    com_harvester=com_harvester, com_mcv=com_mcv, com_engineer=com_engineer,
    com_power_plant=com_power_plant, com_factory=com_factory, com_refinery=com_refinery,
    com_construction_yard=com_construction_yard, dir_turret=dir_turret,
    dir_superweapon=dir_superweapon, sod_veil_projector=sod_veil_projector,
    com_service_depot=com_service_depot, ferrite_cluster=ferrite_cluster,
    dir_vanguard_car=dir_vanguard_car,
    com_wall_post=com_wall_post, com_wall_straight=com_wall_straight,
    com_wall_cap=com_wall_cap, com_wall_corner=com_wall_corner,
    com_wall_tee=com_wall_tee, com_wall_cross=com_wall_cross)

def scene_setup(sun_rot=(0.9, 0.2, 0.7), strength=3.0):
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)
    w = bpy.context.scene.world; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[0].default_value = (0.02, 0.022, 0.025, 1)
    w.node_tree.nodes['Background'].inputs[1].default_value = 0.6
    bpy.ops.object.light_add(type='SUN', rotation=sun_rot)
    bpy.context.object.data.energy = strength
    bpy.ops.object.light_add(type='SUN', rotation=(1.2, -0.4, 2.4))
    bpy.context.object.data.energy = 0.8
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'; sc.cycles.samples = 72
    sc.cycles.use_denoising = False
    sc.view_settings.look = 'AgX - Medium High Contrast'
