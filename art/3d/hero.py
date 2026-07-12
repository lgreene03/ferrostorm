# Hero asset study: detailed dir_cannon_tank vs the blockout, side by side.
# Silhouette law (doc 16): same recognisable shape as builder.dir_cannon_tank
# - beveled hull, side tracks, offset turret, single barrel, one orange band.
# Detail budget: road wheels, track links, skirt plates, mantlet + muzzle
# brake, commander hatch, antenna, tow hooks, panel inserts, weathering.
# Run: blender -b -P hero.py   (writes hero.png next to this script)
import bpy, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import builder
from builder import box, cyl, join, mat
from materials2 import wmat

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'hero.png')

def wset(o, name, **kw):
    """Swap an object's material for the weathered version."""
    o.data.materials.clear()
    o.data.materials.append(wmat(name, **kw))
    return o

def track_assembly(side):
    """Detailed track: upper tread run + top skirt, road wheels exposed
    below (classic tank read), drive sprocket at the bow."""
    x = -0.38 if side == 'l' else 0.38
    parts = []
    band = box(f'band{side}', 0.15, 0.92, 0.09, x, 0, 0.155, 'gundark', 0.02)
    wset(band, 'gundark', rough=0.85, wear=0.7, grime=0.8)
    parts.append(band)
    skirt = box(f'skirt{side}', 0.17, 0.80, 0.045, x, -0.02, 0.245, 'gun', 0.015)
    wset(skirt, 'gun', wear=0.8, grime=0.6)
    parts.append(skirt)
    for i, wy in enumerate((-0.32, -0.11, 0.10, 0.31)):
        w = cyl(f'w{side}{i}', 0.082, 0.10, x, wy, 0.085, 'gundark', vs=12, ry=math.pi/2)
        wset(w, 'gundark', rough=0.6, metal=0.5, wear=0.9, grime=0.7)
        parts.append(w)
    spr = cyl(f'spr{side}', 0.065, 0.11, x, 0.43, 0.105, 'plate', vs=8, ry=math.pi/2)
    wset(spr, 'plate', metal=0.6, wear=0.9)
    parts.append(spr)
    return parts

def hero_cannon_tank():
    parts = []
    # Hull: main body + glacis wedge + rear deck step
    hull = box('hull', 0.62, 0.78, 0.24, 0, -0.02, 0.04, 'gun', 0.05)
    wset(hull, 'gun', wear=0.55, grime=0.5)
    parts.append(hull)
    glacis = box('glacis', 0.56, 0.18, 0.16, 0, 0.40, 0.07, 'plate', 0.05)
    glacis.rotation_euler = (-0.5, 0, 0)
    wset(glacis, 'plate', wear=0.7, grime=0.4)
    parts.append(glacis)
    deck = box('deck', 0.5, 0.2, 0.07, 0, -0.36, 0.28, 'gundark', 0.03)
    wset(deck, 'gundark', grime=0.9)   # engine deck: sootiest surface
    parts.append(deck)
    # Panel inserts along the hull sides (read as access hatches)
    for i, py in enumerate((-0.2, 0.05)):
        p = box(f'panel{i}', 0.64, 0.16, 0.04, 0, py, 0.20, 'gundark', 0.01)
        wset(p, 'gundark', wear=0.4)
        parts.append(p)
    # Tow hooks front
    for hx in (-0.2, 0.2):
        h = box(f'hook{hx}', 0.06, 0.08, 0.06, hx, 0.46, 0.10, 'gundark', 0.01)
        wset(h, 'gundark', metal=0.6, wear=1.0)
        parts.append(h)
    parts += track_assembly('l')
    parts += track_assembly('r')
    # Turret: base ring, faceted body, bustle, mantlet, barrel, muzzle brake
    ring = cyl('ring', 0.20, 0.05, 0, -0.04, 0.315, 'gundark', vs=14)
    wset(ring, 'gundark', metal=0.5, wear=0.8)
    parts.append(ring)
    tur = box('tur', 0.38, 0.42, 0.15, 0, -0.04, 0.34, 'plate', 0.05)
    wset(tur, 'plate', wear=0.5, grime=0.45)
    parts.append(tur)
    bustle = box('bustle', 0.30, 0.14, 0.11, 0, -0.29, 0.35, 'gundark', 0.03)
    wset(bustle, 'gundark', grime=0.7)
    parts.append(bustle)
    mant = box('mant', 0.18, 0.10, 0.13, 0, 0.22, 0.375, 'gundark', 0.03)
    wset(mant, 'gundark', metal=0.4, wear=0.85)
    parts.append(mant)
    gun = cyl('gun', 0.038, 0.58, 0, 0.52, 0.385, 'gundark', rx=math.pi/2)
    wset(gun, 'gundark', metal=0.55, rough=0.5, wear=0.6, grime=0.35)
    parts.append(gun)
    sleeve = cyl('sleeve', 0.052, 0.20, 0, 0.34, 0.385, 'gundark', rx=math.pi/2)
    wset(sleeve, 'gundark', metal=0.5, wear=0.7)
    parts.append(sleeve)
    brake = cyl('brake', 0.056, 0.10, 0, 0.78, 0.385, 'plate', vs=10, rx=math.pi/2)
    wset(brake, 'plate', metal=0.6, wear=1.0, grime=0.6)
    parts.append(brake)
    # Commander hatch + antenna
    hatch = cyl('hatch', 0.075, 0.035, -0.10, -0.12, 0.43, 'gundark', vs=10)
    wset(hatch, 'gundark', wear=0.6)
    parts.append(hatch)
    ant = cyl('ant', 0.006, 0.34, 0.15, -0.24, 0.58, 'gundark', vs=6)
    parts.append(ant)
    # THE team band: one place, signal orange (kept clean - it is repainted)
    bd = box('band', 0.34, 0.05, 0.06, 0, -0.395, 0.315, 'orange', 0.012)
    parts.append(bd)
    return join(parts, 'hero_cannon_tank')

builder.scene_setup(sun_rot=(0.95, 0.18, 0.6), strength=3.2)
# ground catch plane so the tanks are not floating in void
bpy.ops.mesh.primitive_plane_add(size=14, location=(0, 0, 0))
bpy.context.object.data.materials.append(mat('cinder', rough=0.95))

blockout = builder.dir_cannon_tank()
blockout.location = (-0.85, 0, 0)
hero = hero_cannon_tank()
hero.location = (0.85, 0, 0)

bpy.context.view_layer.update()
for label, o in (("BLOCKOUT", blockout), ("HERO", hero)):
    ys = [(o.matrix_world @ v.co).y for v in o.data.vertices]
    zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
    print(f"PROBE {label}: Y {min(ys):.2f}..{max(ys):.2f}  Z {min(zs):.2f}..{max(zs):.2f}")

# Three-quarter view: barrels read in profile, not end-on at the camera
bpy.ops.object.camera_add(location=(3.1, -3.3, 2.1))
cam = bpy.context.object
cam.data.lens = 60
import mathutils
direction = mathutils.Vector((0, 0.05, 0.25)) - cam.location
cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
bpy.context.scene.camera = cam
sc = bpy.context.scene
sc.render.resolution_x = 1400
sc.render.resolution_y = 800
sc.cycles.samples = 96
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("HERO RENDER DONE:", OUT)
