# Ferrostorm weathered PBR materials - procedural, style-guide faithful.
# Extends builder.mat()'s palette with field wear: edge chipping (pointiness
# mask), grunge-driven roughness variation, and a dust/soot darkening pass.
# Signature-compatible with builder.mat so unit functions can swap per-part:
#   from materials2 import wmat
#   hull.data.materials.append(wmat('gun'))
# NOTE (step c of the plan): these are node graphs - glTF cannot export them.
# They must be BAKED to image textures before .glb export. bake.py owns that.
import bpy
from builder import PAL

_wmats = {}

def _node(nt, kind, x, y, **inputs):
    n = nt.nodes.new(kind)
    n.location = (x, y)
    for k, v in inputs.items():
        n.inputs[k].default_value = v
    return n

def wmat(name, emit=0.0, rough=0.7, metal=0.25, wear=0.5, grime=0.5):
    """Weathered version of builder.mat(name). wear 0..1 = edge chipping
    amount; grime 0..1 = dust/roughness mottling amount."""
    key = (name, emit, wear, grime)
    if key in _wmats: return _wmats[key]
    m = bpy.data.materials.new(f"w_{name}_{emit}_{wear}")
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    base = PAL[name]
    b.inputs["Metallic"].default_value = metal
    if emit > 0:
        b.inputs["Emission Color"].default_value = base
        b.inputs["Emission Strength"].default_value = emit

    # --- Edge wear: pointiness -> sharp ramp -> mix bare-metal chips ---
    geo = nt.nodes.new('ShaderNodeNewGeometry'); geo.location = (-1000, 300)
    ramp = nt.nodes.new('ShaderNodeValToRGB'); ramp.location = (-800, 300)
    # pointiness clusters tightly around 0.5; a narrow window right of it
    # isolates convex edges only
    ramp.color_ramp.elements[0].position = 0.535
    ramp.color_ramp.elements[1].position = 0.565 + (1.0 - wear) * 0.06
    nt.links.new(geo.outputs['Pointiness'], ramp.inputs['Fac'])
    # break the wear line up with fine noise so chips look chipped, not airbrushed
    n1 = _node(nt, 'ShaderNodeTexNoise', -1000, 60)
    n1.inputs['Scale'].default_value = 34.0
    n1.inputs['Detail'].default_value = 6.0
    mul = nt.nodes.new('ShaderNodeMath'); mul.location = (-620, 250)
    mul.operation = 'MULTIPLY'
    nt.links.new(ramp.outputs['Color'], mul.inputs[0])
    nt.links.new(n1.outputs['Fac'], mul.inputs[1])

    # --- Grime: large soft noise darkens colour and roughens surface ---
    n2 = _node(nt, 'ShaderNodeTexNoise', -1000, -180)
    n2.inputs['Scale'].default_value = 5.5
    n2.inputs['Detail'].default_value = 4.0
    gramp = nt.nodes.new('ShaderNodeValToRGB'); gramp.location = (-800, -180)
    gramp.color_ramp.elements[0].position = 0.30
    gramp.color_ramp.elements[1].position = 0.62
    nt.links.new(n2.outputs['Fac'], gramp.inputs['Fac'])

    # --- W4-04 shading normals: bevel shader rounds every hard edge, a
    # brick-mortar grid cuts panel seam lines in object space, and the
    # 34-scale wear noise adds micro grain. Cycles' NORMAL bake picks all
    # of this up, so the baked tangent normal map carries the edge read.
    coord = nt.nodes.new('ShaderNodeTexCoord'); coord.location = (-1400, -500)
    bev = nt.nodes.new('ShaderNodeBevel'); bev.location = (-1200, -700)
    bev.samples = 8
    bev.inputs['Radius'].default_value = 0.015
    seam = nt.nodes.new('ShaderNodeTexBrick'); seam.location = (-1200, -500)
    seam.inputs['Scale'].default_value = 4.0
    seam.inputs['Mortar Size'].default_value = 0.006
    seam.inputs['Color1'].default_value = (0.5, 0.5, 0.5, 1)
    seam.inputs['Color2'].default_value = (0.5, 0.5, 0.5, 1)
    seam.inputs['Mortar'].default_value = (0, 0, 0, 1)
    nt.links.new(coord.outputs['Object'], seam.inputs['Vector'])
    bump1 = nt.nodes.new('ShaderNodeBump'); bump1.location = (-1000, -500)
    bump1.inputs['Strength'].default_value = 0.25
    bump1.inputs['Distance'].default_value = 0.005
    nt.links.new(seam.outputs['Color'], bump1.inputs['Height'])
    nt.links.new(bev.outputs['Normal'], bump1.inputs['Normal'])
    bump2 = nt.nodes.new('ShaderNodeBump'); bump2.location = (-800, -500)
    bump2.inputs['Strength'].default_value = 0.06
    bump2.inputs['Distance'].default_value = 0.002
    nt.links.new(n1.outputs['Fac'], bump2.inputs['Height'])
    nt.links.new(bump1.outputs['Normal'], bump2.inputs['Normal'])
    nt.links.new(bump2.outputs['Normal'], b.inputs['Normal'])

    # Base colour: base -> darkened by grime -> chipped to bare metal on edges
    dark = tuple(c * 0.42 for c in base[:3]) + (1,)
    chip = (0.50, 0.53, 0.56, 1) if name not in ('rust', 'rustp', 'rustd') \
        else (0.24, 0.13, 0.09, 1)  # sodality salvage chips darker, not brighter
    mix1 = nt.nodes.new('ShaderNodeMix'); mix1.location = (-420, 150)
    mix1.data_type = 'RGBA'
    mix1.inputs['A'].default_value = base
    mix1.inputs['B'].default_value = dark
    fac1 = nt.nodes.new('ShaderNodeMath'); fac1.location = (-560, 40)
    fac1.operation = 'MULTIPLY'
    fac1.inputs[1].default_value = grime * 0.85
    nt.links.new(gramp.outputs['Color'], fac1.inputs[0])
    nt.links.new(fac1.outputs['Value'], mix1.inputs['Factor'])
    mix2 = nt.nodes.new('ShaderNodeMix'); mix2.location = (-220, 200)
    mix2.data_type = 'RGBA'
    nt.links.new(mix1.outputs['Result'], mix2.inputs['A'])
    mix2.inputs['B'].default_value = chip
    nt.links.new(mul.outputs['Value'], mix2.inputs['Factor'])
    nt.links.new(mix2.outputs['Result'], b.inputs['Base Color'])

    # Roughness: base rough mottled up by grime, chips slightly glossier
    radd = nt.nodes.new('ShaderNodeMath'); radd.location = (-420, -220)
    radd.operation = 'MULTIPLY_ADD'
    nt.links.new(gramp.outputs['Color'], radd.inputs[0])
    radd.inputs[1].default_value = 0.25 * grime   # grime adds up to +0.25
    radd.inputs[2].default_value = rough - 0.1
    rsub = nt.nodes.new('ShaderNodeMath'); rsub.location = (-220, -220)
    rsub.operation = 'SUBTRACT'
    nt.links.new(radd.outputs['Value'], rsub.inputs[0])
    sc = nt.nodes.new('ShaderNodeMath'); sc.location = (-420, -380)
    sc.operation = 'MULTIPLY'
    nt.links.new(mul.outputs['Value'], sc.inputs[0])
    sc.inputs[1].default_value = 0.3              # chips gloss up by 0.3
    nt.links.new(sc.outputs['Value'], rsub.inputs[1])
    nt.links.new(rsub.outputs['Value'], b.inputs['Roughness'])

    _wmats[key] = m
    return m
