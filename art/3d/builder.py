# Ferrostorm 3D asset library - procedural low-poly, style guide faithful.
import bpy, bmesh, math

PAL = dict(
    cinder=(0.055,0.06,0.065,1), gun=(0.30,0.36,0.41,1), plate=(0.44,0.50,0.54,1),
    gundark=(0.17,0.20,0.23,1), orange=(0.91,0.42,0.13,1),
    rust=(0.47,0.24,0.16,1), rustp=(0.60,0.32,0.21,1), rustd=(0.30,0.15,0.10,1), teal=(0.24,0.70,0.63,1),
    olive=(0.39,0.37,0.32,1), olived=(0.25,0.24,0.20,1),
    ferrite=(0.79,0.63,0.36,1), fhi=(0.92,0.78,0.50,1), bone=(0.83,0.81,0.75,1))

_mats = {}
USE_WEATHERED = False  # set True (see lineup.py) to route every part through
                       # materials2.wmat - roster-wide weathering, one switch

def mat(name, emit=0.0, rough=0.7, metal=0.15):
    if USE_WEATHERED and emit == 0:
        import materials2
        return materials2.wmat(name, rough=rough, metal=max(metal, 0.2))
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

def box(name, sx, sy, sz, x=0, y=0, z=0, m='gun', bevel=0.06):
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
    o.data.materials.append(mat(m))
    return o

def cyl(name, r, h, x=0, y=0, z=0, m='gun', vs=12, rx=0, ry=0):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=h, vertices=vs, location=(x, y, z))
    o = bpy.context.object; o.name = name
    o.rotation_euler = (rx, ry, 0)
    o.data.materials.append(mat(m))
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
    return box('band', w, d, 0.06, 0, y, z, colour, bevel=0.012)

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
    parts.append(box('sl', 0.3, 0.07, 0.1, -0.26, -0.34, 0.28, 'teal', 0.02))
    return join(parts, 'sod_phantom_tank')

def sod_shade_raider():
    parts = [wedge('body', [(-0.3, -0.36), (-0.14, 0.38), (0.26, 0.3), (0.34, -0.24)], 0.2, m='rust')]
    parts.append(wedge('top', [(-0.18, -0.22), (-0.08, 0.22), (0.18, 0.16)], 0.1, z=0.2, m='rustp'))
    # canted stabiliser fin + skirt blades: the raider reads fast even parked
    parts.append(wedge('fin', [(-0.06, -0.34), (0.0, -0.1), (0.08, -0.32)], 0.22, z=0.2, m='rustd'))
    parts.append(wedge('blade1', [(-0.34, -0.3), (-0.26, 0.28), (-0.2, 0.24), (-0.28, -0.3)], 0.06, z=0.04, m='rustd'))
    parts.append(wedge('blade2', [(0.3, -0.22), (0.36, 0.2), (0.3, 0.24), (0.24, -0.18)], 0.06, z=0.04, m='rustd'))
    parts.append(cyl('exh', 0.025, 0.16, -0.14, -0.36, 0.16, 'rustd', vs=8, rx=0.6))
    parts.append(box('sl', 0.22, 0.06, 0.08, -0.18, -0.24, 0.18, 'teal', 0.02))
    return join(parts, 'sod_shade_raider')

def infantry(name, tube=False, colour='olive'):
    men = []
    for i, (dx, dy) in enumerate([(-0.2, -0.15), (0.2, -0.1), (0, 0.2)]):
        b = cyl(f'b{i}', 0.07, 0.22, dx, dy, 0.11, colour, vs=8)
        h = cyl(f'h{i}', 0.05, 0.07, dx, dy, 0.26, 'bone', vs=8)
        men += [b, h]
        if tube:
            men.append(cyl(f't{i}', 0.03, 0.24, dx + 0.06, dy, 0.3, 'ferrite', vs=8, ry=math.pi/2))
    base = cyl('base', 0.34, 0.02, 0, 0, 0.01, 'olived', vs=16)
    return join(men + [base], name)

def com_harvester():
    parts = [box('body', 0.78, 1.0, 0.36, 0, 0.02, 0.08, 'olive', 0.12)]
    parts.append(box('cab', 0.4, 0.24, 0.16, 0, 0.44, 0.44, 'olived', 0.04))
    parts.append(box('screen', 0.32, 0.03, 0.09, 0, 0.565, 0.47, 'gundark', 0.008))
    parts += tracks(-0.45, 1.1, wheel_r=0.11, wheels=4, band_w=0.17, m_band='olived', m_skirt='olive')
    parts += tracks(0.45, 1.1, wheel_r=0.11, wheels=4, band_w=0.17, m_band='olived', m_skirt='olive')
    parts.append(cyl('hop', 0.26, 0.16, 0, 0.02, 0.5, 'ferrite', vs=14))
    parts.append(cyl('hoprim', 0.29, 0.04, 0, 0.02, 0.58, 'olived', vs=14))
    intake = box('in', 0.5, 0.22, 0.14, 0, -0.56, 0.04, 'olived', 0.04)
    intake.rotation_euler = (0.3, 0, 0)
    parts.append(intake)
    for i in range(4):   # intake teeth
        parts.append(box(f'tooth{i}', 0.06, 0.08, 0.06, -0.18 + i * 0.12, -0.65, 0.02, 'gundark', 0.01))
    parts.append(cyl('pipe', 0.045, 0.5, 0.3, -0.2, 0.44, 'olived', vs=8, rx=math.pi/2))
    parts.append(team_band(0.4, 0.56, 0.34, 'ferrite'))
    return join(parts, 'com_harvester')

def com_mcv():
    parts = [box('body', 0.68, 1.16, 0.38, 0, 0, 0.1, 'olive', 0.07)]
    parts.append(box('cab', 0.5, 0.3, 0.2, 0, 0.44, 0.48, 'ferrite', 0.04))
    parts.append(box('screen', 0.4, 0.03, 0.1, 0, 0.585, 0.52, 'gundark', 0.008))
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
    for i in range(3):   # vent louvres
        parts.append(box(f'lv{i}', 0.37, 0.7, 0.04, 0.5, -0.1, 0.72 + i * 0.14, 'gundark', 0.008))
    parts.append(cyl('feed', 0.06, 0.7, 0.05, 0.1, 0.62, 'olived', vs=8, ry=math.pi/2))
    parts.append(cyl('stack', 0.05, 0.35, 0.62, 0.55, 0.72, 'olived', vs=8))
    return join(parts, 'com_power_plant')

def com_factory():
    parts = [pad()]
    parts.append(box('hall', 1.6, 1.4, 0.62, 0, 0, 0.08, 'olive', 0.08))
    for i in range(3):   # sawtooth roof monitors
        parts.append(box(f'saw{i}', 1.5, 0.28, 0.14, 0, -0.45 + i * 0.42, 0.7, 'olived', 0.02))
    parts.append(box('d1', 0.55, 0.1, 0.44, -0.4, 0.72, 0.14, 'olived', 0.03))
    parts.append(box('d2', 0.55, 0.1, 0.44, 0.4, 0.72, 0.14, 'olived', 0.03))
    parts.append(box('rail1', 0.06, 0.5, 0.03, -0.4, 0.95, 0.09, 'gundark', 0.008))
    parts.append(box('rail2', 0.06, 0.5, 0.03, 0.4, 0.95, 0.09, 'gundark', 0.008))
    parts.append(box('lip', 1.5, 0.12, 0.1, 0, 0.72, 0.62, 'ferrite', 0.02))
    parts.append(cyl('chim', 0.08, 0.5, -0.6, -0.5, 0.85, 'olived', vs=10))
    parts.append(cyl('chimcap', 0.1, 0.04, -0.6, -0.5, 1.1, 'gundark', vs=10))
    return join(parts, 'com_factory')

def com_refinery():
    parts = [pad()]
    parts.append(box('hall', 1.1, 1.0, 0.44, 0.3, -0.3, 0.08, 'olive', 0.08))
    parts.append(box('hut', 0.4, 0.34, 0.22, 0.55, -0.05, 0.52, 'olived', 0.03))
    parts.append(cyl('silo', 0.45, 1.0, -0.4, 0.25, 0.58, 'olived', vs=14))
    parts.append(cyl('siloband', 0.47, 0.05, -0.4, 0.25, 0.75, 'olive', vs=14))
    parts.append(cyl('core', 0.3, 0.1, -0.4, 0.25, 1.1, 'fhi', vs=14))
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
    parts.append(cyl('core', 0.16, 0.5, 0, 0, 0.55, 'orange', vs=10))
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
    parts.append(cyl('orb', 0.18, 0.18, 0, 0, 1.5, 'teal', vs=10))
    parts.append(cyl('r1', 0.5, 0.03, 0, 0, 0.8, 'teal', vs=18))
    parts.append(cyl('r2', 0.34, 0.025, 0, 0, 1.12, 'teal', vs=16))
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

def ferrite_cluster(scale=1.0):
    objs = []
    for i, (dx, dy, h, r) in enumerate([(-0.4,0.1,0.7,0.2),(0.1,-0.2,1.0,0.26),(0.5,0.25,0.55,0.16),(-0.1,0.45,0.45,0.14)]):
        bpy.ops.mesh.primitive_cone_add(radius1=r*scale, depth=h*scale, vertices=6, location=(dx*scale, dy*scale, h*scale/2))
        o = bpy.context.object; o.name = f'c{i}'
        o.rotation_euler = (0.1*i, 0.08*i, i*0.9)
        o.data.materials.append(mat('fhi', emit=1.6, rough=0.3))
        objs.append(o)
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
    parts.append(box('screen', 0.28, 0.02, 0.06, 0, 0.225, 0.29, 'gundark', 0.008))
    vwheels = []
    for sx in (-0.235, 0.235):   # exposed wheels: separate spinning children
        for i, wy in enumerate((-0.22, 0.22)):
            vwheels.append(cyl(f'vw{sx}{i}', 0.105, 0.09, sx, wy, 0.105, 'gundark', vs=12, ry=math.pi/2))
    parts.append(box('bumper', 0.4, 0.06, 0.08, 0, 0.42, 0.08, 'gundark', 0.015))
    parts.append(box('rack', 0.36, 0.18, 0.06, 0, -0.26, 0.22, 'gundark', 0.015))
    parts.append(team_band(0.3, -0.34, 0.2, 'orange'))
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
    dir_vanguard_car=dir_vanguard_car)

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
