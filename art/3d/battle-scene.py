import bpy, sys, json, math
sys.path.insert(0, '/home/claude/b3d')
import builder

R = json.load(open('/tmp/ferrostorm-replay.json'))
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
bpy.ops.object.camera_add(location=(fx - 4, -(fy + 17), 15), rotation=(math.radians(50), 0, math.radians(-8)))
cam = bpy.context.object
cam.data.lens = 32
bpy.context.scene.camera = cam
sc = bpy.context.scene
sc.render.resolution_x = 1600; sc.render.resolution_y = 900
sc.cycles.samples = 48
sc.render.filepath = '/tmp/battle3d.png'
bpy.ops.render.render(write_still=True)
print("BATTLE DONE")
