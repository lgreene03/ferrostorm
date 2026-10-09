# Sidebar icons rendered from the Blender meshes (P8-45).
#
# Run:  blender -b -P art/3d/icons.py [-- id ...]
#
# Each icon is the shipped mesh seen from straight above, the angle the
# reference sprites in art/png were drawn at, so a rendered icon sits beside a
# sprite-derived one without a change of viewpoint. The mesh comes out of
# builder.BUILDERS with the weathered materials on (the lineup.py and bake.py
# setting), is framed to its own footprint with a margin, rendered at 256 px
# on a transparent film, and scaled to the 64 px every icon in game/ui/icons
# already is.
#
# The output is named by the /data id, the convention the sidebar reads
# (Sidebar.UnitItem and StructItem take the icon name from the id). MESH maps
# an id to its builder mesh where the two differ: the barrier is six meshes
# chosen by neighbour mask, and its icon is the straight segment, the piece a
# player draws first.
#
# ICONS lists the 22 buttons that had no icon of their own when P8-45 landed.
# The seventeen older icons were cut from the 2D reference sprites and are not
# re-rendered here, so a run without arguments changes none of them. The
# harness's hudgate/icons stage holds the property this list serves (every
# button the sidebar offers carries an icon named by its own id), so a button
# added later without an icon fails there by name.
#
# PATHS ARE DERIVED FROM THIS FILE'S LOCATION (export_glb.py's rule).
import bpy, os, sys, tempfile, mathutils

HERE = os.path.dirname(os.path.abspath(__file__))          # art/3d
ROOT = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
# ICON_OUT redirects a trial run away from the shipped directory.
OUT = os.environ.get('ICON_OUT') or os.path.join(ROOT, 'game', 'ui', 'icons')

sys.path.insert(0, HERE)
import builder

ICONS = [
    # units
    'com_carrier', 'com_flak_track', 'com_repair_vehicle', 'com_strike_flyer',
    'dir_commando', 'sod_infiltrator', 'sod_phantom_tank', 'sod_saboteur',
    'sod_shade_raider', 'sod_shadow_commando',
    # buildings
    'com_airfield', 'com_barracks', 'com_emplacement', 'com_gate', 'com_mine',
    'com_radar_uplink', 'com_wall', 'dir_bastion', 'sod_generator',
    'sod_seismic_charge', 'sod_shroud_nest', 'sod_watch_post',
]
MESH = {'com_wall': 'com_wall_straight'}

RENDER_PX = 256
ICON_PX = 64
MARGIN = 1.12   # ortho scale over the footprint's longer side

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ids = argv or ICONS
unknown = [i for i in ids if MESH.get(i, i) not in builder.BUILDERS]
if unknown:
    # Refuse by name (export_glb.py's rule): a typo must not pass as an icon
    # that did not need redrawing.
    raise SystemExit(f"no mesh for: {', '.join(unknown)}")


def world_bounds(objs):
    lo = mathutils.Vector((1e9, 1e9, 1e9))
    hi = mathutils.Vector((-1e9, -1e9, -1e9))
    for ob in objs:
        if ob.type != 'MESH':
            continue
        for corner in ob.bound_box:
            p = ob.matrix_world @ mathutils.Vector(corner)
            lo = mathutils.Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = mathutils.Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    return lo, hi


os.makedirs(OUT, exist_ok=True)
for icon in ids:
    builder.USE_WEATHERED = True
    builder.scene_setup(sun_rot=(0.3, 0.12, 0.6), strength=3.4)
    o = builder.BUILDERS[MESH.get(icon, icon)]()
    bpy.context.view_layer.update()
    lo, hi = world_bounds([o] + list(o.children_recursive))
    centre = (lo + hi) / 2

    # Straight down. Blender +Y is north, which is up on the battle screen
    # (ModelLibrary's axis note), so the icon faces the way the unit is drawn.
    bpy.ops.object.camera_add(location=(centre.x, centre.y, hi.z + 10.0), rotation=(0, 0, 0))
    cam = bpy.context.object
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = max(hi.x - lo.x, hi.y - lo.y) * MARGIN
    cam.data.clip_end = 100.0
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.film_transparent = True
    sc.render.resolution_x = RENDER_PX
    sc.render.resolution_y = RENDER_PX
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.cycles.samples = 48
    # The full-size render goes to the temp directory, never beside the
    # icons, where the Godot import pass would pick it up.
    big = os.path.join(tempfile.gettempdir(), f'ferrostorm_icon_{icon}_{RENDER_PX}.png')
    sc.render.filepath = big
    bpy.ops.render.render(write_still=True)

    img = bpy.data.images.load(big)
    img.scale(ICON_PX, ICON_PX)
    path = os.path.join(OUT, f'{icon}.png')
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)
    os.remove(big)
    print(f'ICON {icon} <- {MESH.get(icon, icon)} -> {path}')

print(f'ICONS DONE ({len(ids)} to {OUT})')
