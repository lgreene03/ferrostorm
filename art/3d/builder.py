# Ferrostorm 3D asset library - procedural low-poly, style guide faithful.
import bpy, bmesh, math

PAL = dict(
    cinder=(0.055,0.06,0.065,1), gun=(0.30,0.36,0.41,1), plate=(0.44,0.50,0.54,1),
    gundark=(0.17,0.20,0.23,1), orange=(0.91,0.42,0.13,1),
    rust=(0.47,0.24,0.16,1), rustp=(0.60,0.32,0.21,1), rustd=(0.30,0.15,0.10,1), teal=(0.24,0.70,0.63,1),
    olive=(0.39,0.37,0.32,1), olived=(0.25,0.24,0.20,1),
    ferrite=(0.79,0.63,0.36,1), fhi=(0.92,0.78,0.50,1), bone=(0.83,0.81,0.75,1))

_mats = {}
def mat(name, emit=0.0, rough=0.7, metal=0.15):
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
    o.scale = (sx/2, sy/2, sz/2)
    bpy.ops.object.transform_apply(scale=True)
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
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = bpy.context.object; obj.name = name
    return obj

def team_band(w, y, z, colour, d=0.1):
    return box('band', w, d, 0.12, 0, y, z, colour, bevel=0.02)

# ---------------- UNITS (1 blender unit = 1 cell) ----------------
def dir_cannon_tank():
    hull = box('hull', 0.62, 0.82, 0.28, m='gun')
    tl = box('tl', 0.16, 0.9, 0.2, -0.36, 0, 0, 'gundark', 0.04)
    tr = box('tr', 0.16, 0.9, 0.2, 0.36, 0, 0, 'gundark', 0.04)
    tur = box('tur', 0.4, 0.44, 0.16, 0, -0.04, 0.28, 'plate')
    gun = cyl('gun', 0.05, 0.62, 0, 0.42, 0.38, 'gundark', rx=math.pi/2)
    bd = team_band(0.4, -0.38, 0.30, 'orange')
    return join([hull, tl, tr, tur, gun, bd], 'dir_cannon_tank')

def dir_bulwark_tank():
    hull = box('hull', 0.92, 1.0, 0.36, m='gun')
    tl = box('tl', 0.2, 1.1, 0.26, -0.52, 0, 0, 'gundark', 0.05)
    tr = box('tr', 0.2, 1.1, 0.26, 0.52, 0, 0, 'gundark', 0.05)
    tur = box('tur', 0.6, 0.56, 0.22, 0, -0.05, 0.36, 'plate')
    g1 = cyl('g1', 0.055, 0.75, -0.13, 0.5, 0.5, 'gundark', rx=math.pi/2)
    g2 = cyl('g2', 0.055, 0.75, 0.13, 0.5, 0.5, 'gundark', rx=math.pi/2)
    bd = team_band(0.6, -0.48, 0.4, 'orange')
    return join([hull, tl, tr, tur, g1, g2, bd], 'dir_bulwark_tank')

def dir_howitzer():
    hull = box('hull', 0.6, 0.7, 0.24, m='gun')
    tl = box('tl', 0.14, 0.8, 0.18, -0.33, 0, 0, 'gundark', 0.04)
    tr = box('tr', 0.14, 0.8, 0.18, 0.33, 0, 0, 'gundark', 0.04)
    gun = cyl('gun', 0.05, 1.1, 0, 0.35, 0.42, 'gundark', rx=math.pi/2 - 0.5)
    mount = box('m', 0.24, 0.3, 0.18, 0, -0.1, 0.24, 'plate')
    bd = team_band(0.36, -0.32, 0.26, 'orange')
    return join([hull, tl, tr, gun, mount, bd], 'dir_howitzer')

def dir_sentinel_scout():
    hull = box('hull', 0.42, 0.6, 0.22, m='gun')
    mast = cyl('mast', 0.03, 0.5, 0, 0.06, 0.45, 'gundark')
    dish = cyl('dish', 0.16, 0.04, 0, 0.06, 0.72, 'orange', vs=16)
    bd = team_band(0.3, -0.28, 0.24, 'orange')
    return join([hull, mast, dish, bd], 'dir_sentinel_scout')

def sod_phantom_tank():
    body = wedge('body', [(-0.42, -0.5), (-0.2, 0.52), (0.34, 0.44), (0.5, -0.28), (0.18, -0.56)], 0.3, m='rust')
    top = wedge('top', [(-0.28, -0.34), (-0.12, 0.34), (0.24, 0.28), (0.34, -0.2)], 0.14, z=0.3, m='rustd')
    t1 = cyl('t1', 0.045, 0.6, -0.02, 0.42, 0.4, 'rustd', rx=math.pi/2)
    t2 = cyl('t2', 0.045, 0.6, 0.14, 0.4, 0.4, 'rustd', rx=math.pi/2)
    sl = box('sl', 0.3, 0.07, 0.1, -0.26, -0.34, 0.28, 'teal', 0.02)
    return join([body, top, t1, t2, sl], 'sod_phantom_tank')

def sod_shade_raider():
    body = wedge('body', [(-0.3, -0.36), (-0.14, 0.38), (0.26, 0.3), (0.34, -0.24)], 0.2, m='rust')
    top = wedge('top', [(-0.18, -0.22), (-0.08, 0.22), (0.18, 0.16)], 0.1, z=0.2, m='rustp')
    sl = box('sl', 0.22, 0.06, 0.08, -0.18, -0.24, 0.18, 'teal', 0.02)
    return join([body, top, sl], 'sod_shade_raider')

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
    body = box('body', 0.8, 1.05, 0.4, m='olive', bevel=0.14)
    tl = box('tl', 0.18, 1.1, 0.28, -0.45, 0, 0, 'olived', 0.05)
    tr = box('tr', 0.18, 1.1, 0.28, 0.45, 0, 0, 'olived', 0.05)
    hop = cyl('hop', 0.26, 0.16, 0, 0.1, 0.46, 'ferrite', vs=14)
    intake = box('in', 0.5, 0.2, 0.14, 0, -0.58, 0.04, 'olived', 0.04)
    bd = team_band(0.44, 0.56, 0.3, 'ferrite')
    return join([body, tl, tr, hop, intake, bd], 'com_harvester')

def com_mcv():
    body = box('body', 0.7, 1.2, 0.42, m='olive', bevel=0.08)
    tl = box('tl', 0.16, 1.24, 0.3, -0.4, 0, 0, 'olived', 0.05)
    tr = box('tr', 0.16, 1.24, 0.3, 0.4, 0, 0, 'olived', 0.05)
    cab = box('cab', 0.5, 0.34, 0.2, 0, 0.42, 0.42, 'ferrite', 0.04)
    crane = cyl('cr', 0.035, 0.8, 0, -0.2, 0.66, 'olived', ry=0.9)
    return join([body, tl, tr, cab, crane], 'com_mcv')

def com_engineer():
    b = cyl('b', 0.08, 0.24, 0, 0, 0.12, 'olive', vs=8)
    h = cyl('h', 0.055, 0.08, 0, 0, 0.3, 'bone', vs=8)
    case = box('c', 0.16, 0.1, 0.1, 0.14, 0.05, 0, 'ferrite', 0.02)
    base = cyl('base', 0.2, 0.02, 0, 0, 0.01, 'olived', vs=12)
    return join([b, h, case, base], 'com_engineer')

# ---------------- STRUCTURES (2x2 cells) ----------------
def pad(m='olived'):
    return box('pad', 1.9, 1.9, 0.08, m=m, bevel=0.05)

def com_power_plant():
    p = pad()
    hall = box('hall', 1.5, 1.5, 0.5, 0, 0, 0.08, 'olive', 0.08)
    cool = cyl('cool', 0.42, 0.9, -0.35, 0.1, 0.55, 'olived', vs=14)
    ring = cyl('ring', 0.3, 0.06, -0.35, 0.1, 1.02, 'ferrite', vs=14)
    vent = box('v', 0.34, 0.9, 0.7, 0.5, -0.1, 0.58, 'orange', 0.04)
    return join([p, hall, cool, ring, vent], 'com_power_plant')

def com_factory():
    p = pad()
    hall = box('hall', 1.6, 1.4, 0.62, 0, 0, 0.08, 'olive', 0.08)
    d1 = box('d1', 0.55, 0.1, 0.44, -0.4, 0.72, 0.14, 'olived', 0.03)
    d2 = box('d2', 0.55, 0.1, 0.44, 0.4, 0.72, 0.14, 'olived', 0.03)
    lip = box('lip', 1.5, 0.12, 0.1, 0, 0.72, 0.62, 'ferrite', 0.02)
    return join([p, hall, d1, d2, lip], 'com_factory')

def com_refinery():
    p = pad()
    hall = box('hall', 1.1, 1.0, 0.44, 0.3, -0.3, 0.08, 'olive', 0.08)
    silo = cyl('silo', 0.45, 1.0, -0.4, 0.25, 0.58, 'olived', vs=14)
    core = cyl('core', 0.3, 0.1, -0.4, 0.25, 1.1, 'fhi', vs=14)
    dockp = box('dock', 0.8, 0.5, 0.06, 0.35, 0.62, 0.08, 'ferrite', 0.02)
    return join([p, hall, silo, core, dockp], 'com_refinery')

def com_construction_yard():
    p = pad()
    b1 = box('b1', 1.7, 0.4, 0.5, 0, 0.6, 0.08, 'olive', 0.06)
    b2 = box('b2', 1.7, 0.4, 0.5, 0, -0.6, 0.08, 'olive', 0.06)
    g1 = cyl('g1', 0.05, 1.3, -0.7, 0, 0.85, 'ferrite', ry=math.pi/2)
    g2 = cyl('g2', 0.05, 1.3, 0.7, 0, 0.85, 'ferrite', ry=math.pi/2)
    beam = cyl('beam', 0.05, 1.5, 0, 0, 0.85, 'ferrite', rx=math.pi/2)
    hook = box('hook', 0.24, 0.24, 0.3, 0, 0, 0.55, 'olived', 0.03)
    return join([p, b1, b2, g1, g2, beam, hook], 'com_construction_yard')

def dir_turret():
    p = pad('gundark')
    base = cyl('base', 0.6, 0.3, 0, 0, 0.23, 'plate', vs=14)
    head = cyl('head', 0.4, 0.26, 0, 0, 0.5, 'gun', vs=12)
    gun = cyl('gun', 0.06, 0.9, 0, 0.5, 0.56, 'gundark', rx=math.pi/2)
    bd = box('bd', 0.5, 0.1, 0.1, 0, -0.8, 0.1, 'orange', 0.02)
    return join([p, base, head, gun, bd], 'dir_turret')

def dir_superweapon():
    p = pad('gundark')
    ring = cyl('ring', 0.75, 0.22, 0, 0, 0.19, 'plate', vs=20)
    dish = cyl('dish', 0.55, 0.1, 0, 0, 0.45, 'gun', vs=20)
    core = cyl('core', 0.16, 0.5, 0, 0, 0.55, 'orange', vs=10)
    fin1 = box('f1', 0.1, 1.5, 0.16, 0, 0, 0.3, 'gundark', 0.02)
    fin2 = box('f2', 1.5, 0.1, 0.16, 0, 0, 0.3, 'gundark', 0.02)
    return join([p, ring, dish, core, fin1, fin2], 'dir_superweapon')

def sod_veil_projector():
    p = pad('rustd')
    base = cyl('base', 0.55, 0.35, 0, 0, 0.26, 'rustp', vs=12)
    spire = cyl('spire', 0.08, 1.1, 0, 0, 0.9, 'rustd', vs=8)
    orb = cyl('orb', 0.18, 0.18, 0, 0, 1.5, 'teal', vs=10)
    r1 = cyl('r1', 0.5, 0.03, 0, 0, 0.8, 'teal', vs=18)
    return join([p, base, spire, orb, r1], 'sod_veil_projector')

def com_service_depot():
    p = pad()
    padc = cyl('padc', 0.8, 0.1, 0, 0, 0.13, 'olive', vs=18)
    h1 = box('h1', 1.0, 0.22, 0.06, 0, 0, 0.18, 'ferrite', 0.02)
    h2 = box('h2', 0.22, 1.0, 0.06, 0, 0, 0.18, 'ferrite', 0.02)
    arm = box('arm', 0.14, 0.7, 0.4, 0.75, -0.5, 0.08, 'olived', 0.03)
    return join([p, padc, h1, h2, arm], 'com_service_depot')

def ferrite_cluster(scale=1.0):
    objs = []
    for i, (dx, dy, h, r) in enumerate([(-0.4,0.1,0.7,0.2),(0.1,-0.2,1.0,0.26),(0.5,0.25,0.55,0.16),(-0.1,0.45,0.45,0.14)]):
        bpy.ops.mesh.primitive_cone_add(radius1=r*scale, depth=h*scale, vertices=6, location=(dx*scale, dy*scale, h*scale/2))
        o = bpy.context.object; o.name = f'c{i}'
        o.rotation_euler = (0.1*i, 0.08*i, i*0.9)
        o.data.materials.append(mat('fhi', emit=1.6, rough=0.3))
        objs.append(o)
    return join(objs, 'ferrite_cluster')

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
    com_service_depot=com_service_depot, ferrite_cluster=ferrite_cluster)

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
