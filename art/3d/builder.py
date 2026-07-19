# Ferrostorm 3D asset library - procedural low-poly, style guide faithful.
import bpy, bmesh, math

PAL = dict(
    cinder=(0.055,0.06,0.065,1), gun=(0.30,0.36,0.41,1), plate=(0.44,0.50,0.54,1),
    gundark=(0.17,0.20,0.23,1), orange=(0.91,0.42,0.13,1),
    rust=(0.47,0.24,0.16,1), rustp=(0.60,0.32,0.21,1), rustd=(0.30,0.15,0.10,1), teal=(0.24,0.70,0.63,1),
    olive=(0.39,0.37,0.32,1), olived=(0.25,0.24,0.20,1),
    ferrite=(0.79,0.63,0.36,1), fhi=(0.92,0.78,0.50,1), bone=(0.83,0.81,0.75,1),
    # W4-02: warm lamp glow (ferrite-adjacent, palette law kept) and red beacon
    glow=(1.0,0.85,0.55,1), beacon=(1.0,0.16,0.10,1))

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

BUILDERS = dict(
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
