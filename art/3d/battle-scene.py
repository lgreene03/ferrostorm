import bpy, sys, json, math, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import builder

builder.USE_WEATHERED = True
REPLAY = os.path.normpath(os.path.join(HERE, '..', '..', 'game', 'replay.json'))
R = json.load(open(REPLAY))
frame = max(range(len(R['frames'])), key=lambda i: len([e for e in R['frames'][i]['ev'] if e[0] == 1]))
f = R['frames'][frame]
print(f"rendering tick {f['t']}")
builder.scene_setup(sun_rot=(0.95, 0.15, 0.55), strength=3.4)

# Terrain: cinder plane + the Spine as bevelled ridge blocks
W, H = R['map']['w'], R['map']['h']
bpy.ops.mesh.primitive_plane_add(size=1, location=(W/2, -H/2, 0))
ground = bpy.context.object
ground.scale = (W + 10, H + 10, 1)
ground.data.materials.append(builder.mat('cinder', rough=0.95))
# Merge vertical runs of blocked cells into single ridge strips (248 cells -> ~10 objects)
cols = {}
for bx, by in R['map']['blocked']: cols.setdefault(bx, []).append(by)
for bx, ys in cols.items():
    ys.sort()
    start = prev = ys[0]
    for yv in ys[1:] + [None]:
        if yv is not None and yv == prev + 1: prev = yv; continue
        h = prev - start + 1
        builder.box('ridge', 1.02, h + 0.02, 0.85, bx + 0.5, -(start + h / 2), 0, 'gundark', bevel=0.12)
        if yv is not None: start = prev = yv

MODEL_FOR_UNIT = {1:'dir_cannon_tank',2:'com_rifle_squad',3:'com_rocket_squad',5:'sod_shade_raider',
                  6:'dir_sentinel_scout',7:'com_mcv',8:'dir_howitzer',9:'sod_phantom_tank',
                  10:'dir_bulwark_tank',11:'com_engineer'}
MODEL_FOR_KIND = {1:'com_harvester',2:'com_refinery',4:'com_power_plant',5:'com_factory',
                  6:'com_construction_yard',7:'dir_turret',8:'dir_superweapon',
                  9:'sod_veil_projector',10:'com_service_depot'}
protos = {}
def proto(name):
    if name not in protos:
        o = builder.BUILDERS[name]()
        o.location = (-100, -100, 0)
        protos[name] = o
    return protos[name]

def place(name, x, y, rz=0.0, scale=1.0):
    src = proto(name)
    o = src.copy()  # linked mesh: cheap instancing
    bpy.context.collection.objects.link(o)
    o.location = (x, -y, 0)
    o.rotation_euler = (0, 0, rz)
    if scale != 1.0: o.scale = (scale, scale, scale)
    return o

import random
rng = random.Random(2026)
for e in f['e']:
    eid, kind, ut, pl, x, y, hp, mx, cloak = e
    x /= 100; y /= 100
    if kind == 3:
        amt = max(0.15, hp / 12000)
        place('ferrite_cluster', x, y, rng.uniform(0, 6.28), scale=0.6 + amt * 0.9)
    elif kind in MODEL_FOR_KIND:
        place(MODEL_FOR_KIND[kind], x, y)
    elif kind == 0 and ut in MODEL_FOR_UNIT:
        place(MODEL_FOR_UNIT[ut], x, y, rng.uniform(0, 6.28))
    elif kind == 0:
        place('com_rifle_squad', x, y, rng.uniform(0, 6.28))

# Camera: classic RTS three-quarter, framing the Ferrite Gap fight
fx, fy = 44, 30
bpy.ops.object.camera_add(location=(fx - 6, -(fy + 26), 20), rotation=(math.radians(52), 0, math.radians(-10)))
cam = bpy.context.object
cam.data.lens = 26
bpy.context.scene.camera = cam
sc = bpy.context.scene
sc.render.resolution_x = 1600; sc.render.resolution_y = 900
sc.cycles.samples = 48

# --- Atmosphere pass (plan step d) ---
# Distance mist: enable the mist pass and composite a cold cinder haze in
# by camera depth; compositor glare (fog glow) blooms the emissive ferrite.
w = sc.world
w.mist_settings.use_mist = True
w.mist_settings.start = 14.0
w.mist_settings.depth = 42.0
w.mist_settings.falloff = 'QUADRATIC'
sc.view_layers[0].use_pass_mist = True

# Blender 5: Scene.node_tree is gone; the compositor lives in a node group
# assigned to scene.compositing_node_group
nt = bpy.data.node_groups.new('battle_comp', 'CompositorNodeTree')
sc.compositing_node_group = nt
rl = nt.nodes.new('CompositorNodeRLayers'); rl.location = (0, 0)
glare = nt.nodes.new('CompositorNodeGlare'); glare.location = (300, 100)
# Blender 5: glare settings are input sockets, and Type is a spelled-out enum
glare.inputs['Type'].default_value = 'Fog Glow'
glare.inputs['Quality'].default_value = 'High'
glare.inputs['Threshold'].default_value = 0.9
glare.inputs['Size'].default_value = 0.6
# Blender 5 unified node types: the compositor uses shader Mix nodes
mix = nt.nodes.new('ShaderNodeMixRGB'); mix.location = (600, 0)
mix.blend_type = 'MIX'
mix.inputs['Color2'].default_value = (0.055, 0.065, 0.08, 1)  # cold haze
# Blender 5 node groups have no Composite node: expose an Image output on
# the group interface and wire a group output node instead
nt.interface.new_socket(name='Image', in_out='OUTPUT', socket_type='NodeSocketColor')
comp = nt.nodes.new('NodeGroupOutput'); comp.location = (860, 0)
nt.links.new(rl.outputs['Image'], glare.inputs['Image'])
nt.links.new(glare.outputs['Image'], mix.inputs['Color1'])
nt.links.new(rl.outputs['Mist'], mix.inputs['Fac'])
nt.links.new(mix.outputs['Color'], comp.inputs['Image'])

sc.render.filepath = os.path.join(HERE, 'battle-atmosphere.png')
bpy.ops.render.render(write_still=True)
print("BATTLE DONE:", sc.render.filepath)
