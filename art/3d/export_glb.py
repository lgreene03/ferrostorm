import bpy, sys
sys.path.insert(0, '/home/claude/b3d')
import builder
builder.scene_setup()
out = '/home/claude/project-ferrostorm/game/assets/models/'
for name, fn in builder.BUILDERS.items():
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
    o = fn()
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=out + name + '.glb', use_selection=True)
print("GLB EXPORT DONE")
