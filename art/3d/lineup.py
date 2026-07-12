# Roster lineup render: all 20 models in a grid, weathered materials on.
# Replaces the container-era lineup script that was never committed.
# Run: blender -b -P lineup.py   (writes lineup.png next to this script)
import bpy, os, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import builder

builder.USE_WEATHERED = True
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lineup.png')

builder.scene_setup(sun_rot=(0.95, 0.18, 0.6), strength=3.2)
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
ground = bpy.context.object
builder.USE_WEATHERED = False   # ground stays flat cinder
ground.data.materials.append(builder.mat('cinder', rough=0.95))
builder.USE_WEATHERED = True

COLS = 7
SPACING = 2.6
names = list(builder.BUILDERS.keys())
for i, name in enumerate(names):
    o = builder.BUILDERS[name]()
    row, col = divmod(i, COLS)
    o.location = (col * SPACING - (COLS - 1) * SPACING / 2, -row * SPACING, 0)
    print("PLACED", name, tuple(round(v, 1) for v in o.location))

rows = math.ceil(len(names) / COLS)
cy = -(rows - 1) * SPACING / 2
bpy.ops.object.camera_add(location=(0, cy - 12.5, 9.5))
cam = bpy.context.object
cam.data.lens = 33
import mathutils
direction = mathutils.Vector((0, cy + 0.8, 0.4)) - cam.location
cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
bpy.context.scene.camera = cam
sc = bpy.context.scene
sc.render.resolution_x = 1600
sc.render.resolution_y = 900
sc.cycles.samples = 64
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("LINEUP DONE:", OUT)
