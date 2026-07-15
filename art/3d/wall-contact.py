# DEF-07 contact sheet: the six barrier variants assembled into real runs.
# Two frames, because the ticket asks two different questions:
#   wall-contact-rts.png    - the RTS camera geometry EXACTLY as shipped
#                             (height 22, pitch 50 degrees, 75 degree vertical
#                             FOV, per SkirmishLive.cs:205 and RtsCamera.cs:30)
#                             so the silhouette test is judged at the only
#                             camera the game actually uses.
#   wall-contact-detail.png - a close three-quarter view for the joinery: do
#                             corners gap, do the marks read as punctuation.
# Run: blender -b -P wall-contact.py   (writes both PNGs next to this script)
import bpy, os, sys, math, mathutils
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import builder

builder.USE_WEATHERED = True
HERE = os.path.dirname(os.path.abspath(__file__))

# (variant, cell x, cell y, yaw degrees about Z)
# Yaw follows the orientation contract in builder.py: a straight runs along X,
# a cap's ARM points +X, a corner joins +X and +Y, a tee omits its -X arm.
LAYOUT = []

# A ten-segment straight run: two caps and eight mid-run segments.
LAYOUT.append(('com_wall_cap', -4.5, 2, 0))
for i in range(8):
    LAYOUT.append(('com_wall_straight', -3.5 + i, 2, 0))
LAYOUT.append(('com_wall_cap', 4.5, 2, 180))

# An isolated post: mask 0, nothing adjacent.
LAYOUT.append(('com_wall_post', -6.5, -1, 0))

# A corner, walked out along both of its arms.
LAYOUT += [('com_wall_corner', -4, -1, 0),
           ('com_wall_straight', -3, -1, 0), ('com_wall_cap', -2, -1, 180),
           ('com_wall_straight', -4, 0, 90), ('com_wall_cap', -4, 1, 270)]

# A tee, all three arms terminated.
LAYOUT += [('com_wall_tee', 0, -1, 0),
           ('com_wall_cap', 1, -1, 180),
           ('com_wall_cap', 0, 0, 270),
           ('com_wall_cap', 0, -2, 90)]

# A cross, all four arms terminated.
LAYOUT += [('com_wall_cross', 4, -1, 0),
           ('com_wall_cap', 5, -1, 180), ('com_wall_cap', 3, -1, 0),
           ('com_wall_cap', 4, 0, 270), ('com_wall_cap', 4, -2, 90)]

def build_scene():
    builder.scene_setup(sun_rot=(0.95, 0.18, 0.6), strength=3.2)
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
    ground = bpy.context.object
    builder.USE_WEATHERED = False   # ground stays flat cinder, as in lineup.py
    ground.data.materials.append(builder.mat('cinder', rough=0.95))
    builder.USE_WEATHERED = True
    for name, cx, cy, yaw in LAYOUT:
        o = builder.BUILDERS[name]()
        o.rotation_euler = (0, 0, math.radians(yaw))
        o.location = (cx, cy, 0)


build_scene()
sc = bpy.context.scene
sc.cycles.samples = 96


def shoot(path, loc, rot, fov_deg, res, fit='VERTICAL', border=None):
    bpy.ops.object.camera_add(location=loc)
    cam = bpy.context.object
    cam.rotation_euler = rot
    cam.data.sensor_fit = fit
    cam.data.angle = math.radians(fov_deg)
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = res
    # A border CROP rather than a tighter lens: the pixels-per-cell stays
    # exactly what the shipped camera gives, so the silhouette test is judged
    # honestly, but the frame is not nine tenths empty ground.
    sc.render.use_border = border is not None
    sc.render.use_crop_to_border = border is not None
    if border:
        (sc.render.border_min_x, sc.render.border_max_x,
         sc.render.border_min_y, sc.render.border_max_y) = border
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print('SHOT', path)
    bpy.data.objects.remove(cam)


# The shipped RTS camera: height 22, tilted 50 degrees below horizontal. The
# ground centre it frames is 22/tan(50) = 18.5 units ahead of it, so sit the
# camera that far back to centre the layout without altering the scale.
shoot(os.path.join(HERE, 'wall-contact-rts.png'),
      (0, 0.5 - 18.5, 22), (math.radians(40), 0, 0), 75, (1920, 1080),
      border=(0.36, 0.65, 0.36, 0.63))

# Detail pass: close three-quarter on the joinery.
cam_at = mathutils.Vector((-1.5, -9.5, 5.0))
look_at = mathutils.Vector((0.4, -0.6, 0.35))
rot = (look_at - cam_at).to_track_quat('-Z', 'Y').to_euler()
shoot(os.path.join(HERE, 'wall-contact-detail.png'), cam_at, rot, 42, (1920, 1080))

# The same frame under the PROPOSED doc 22 section 5 barrier sub-clause, for
# comparison only: the meshes we export are the ones above, built under the
# current one-place law. This third frame exists so the ruling that is blocking
# this ticket can be made against two pictures rather than two paragraphs.
builder.BARRIER_MARK_MIDRUN = False
builder._mats.clear()
import materials2
materials2._wmats.clear()
build_scene()
shoot(os.path.join(HERE, 'wall-contact-amendment.png'), cam_at, rot, 42, (1920, 1080))
builder.BARRIER_MARK_MIDRUN = True

print('WALL CONTACT DONE')
