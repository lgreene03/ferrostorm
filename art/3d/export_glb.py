# Export every model in builder.BUILDERS to game/assets/models/*.glb.
#
# Run:  blender --background --python art/3d/export_glb.py [-- name ...]
#
# Naming models after the `--` exports only those, which is what a re-bake of
# a few meshes wants; with none it exports the whole roster.
#
# PATHS ARE DERIVED FROM THIS FILE'S LOCATION. They used to be two absolute
# '/home/claude/...' literals from the container this was first written in, so
# the script could not run on the machine the repo was checked out on - the
# import path and the output directory were both somebody else's. A path
# written down is a path that rots; __file__ cannot.
import bpy, sys, os

HERE = os.path.dirname(os.path.abspath(__file__))          # art/3d
ROOT = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
OUT = os.path.join(ROOT, 'game', 'assets', 'models')

sys.path.insert(0, HERE)
import builder

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []

names = argv or list(builder.BUILDERS)
unknown = [n for n in names if n not in builder.BUILDERS]
if unknown:
    # Refuse by name rather than silently exporting nothing, so a typo in a
    # re-bake is not mistaken for a mesh that did not change.
    raise SystemExit(f"unknown model(s): {', '.join(unknown)}. "
                     f"Known: {', '.join(sorted(builder.BUILDERS))}")

os.makedirs(OUT, exist_ok=True)
builder.scene_setup()

for name in names:
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
    o = builder.BUILDERS[name]()
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + '.glb'),
                              use_selection=True)
    print(f"exported {name}")

print(f"GLB EXPORT DONE ({len(names)} model(s) to {OUT})")
