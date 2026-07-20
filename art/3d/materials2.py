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

def wmat(name, emit=0.0, rough=0.7, metal=0.0, wear=0.5, grime=0.5):
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

    # --- Edge wear: convexity -> sharp ramp -> mix bare-metal chips ---
    #
    # V2-01a, and this is a defect found while wiring the metallic channel to
    # this mask rather than one anybody had filed. This chain used to read
    # Geometry.Pointiness through a hard-coded window of 0.535 to 0.595, on
    # the stated theory that "pointiness clusters tightly around 0.5". It does
    # not. Pointiness is normalised over the mesh, so its flat-face value is a
    # property of the topology, and baked across the whole roster it lands at
    # 0.5725 on most of the common and Directorate models, 0.6039 to 0.6118 on
    # the infantry, the scout dish and the vanguard wheels, and 0.30 to 0.33 on
    # the Sodality vehicles. A flat face at 0.5725 sits SIXTY-TWO PER CENT of
    # the way through the old window, so the mask was not an edge mask at all:
    # it read about 0.6 across entire flat panels. Measured share of texels
    # falling inside or above the window: 97 per cent on com_factory, 99.9 on
    # com_wall_post and the vanguard car body, and 100 per cent on the vanguard
    # wheels and the scout dish. The consequence shipped in every .glb: the
    # bare-metal chip colour (0.50, 0.53, 0.56) was mixed over most of the
    # surface of most of the roster instead of over its edges, which is a
    # large part of why the units read pale, chalky and low-contrast, and it
    # is why the Sodality vehicles, whose pointiness happens to fall BELOW the
    # window, were measurably the darkest and least chalky models in the game.
    # No single pair of constants can window a quantity whose flat value
    # ranges from 0.30 to 0.61, so the quantity has to change.
    #
    # The replacement is absolute rather than relative: the angle between the
    # bevel-shaded normal and the true geometric normal. On a flat face those
    # are the same vector and the dot product is exactly 1.0 whatever the mesh
    # looks like; inside the bevel radius of an edge they diverge. Baked over
    # the roster the median is 0.00 to 0.043 on every single object, with the
    # tail where the edges are, which is the separation the old mask never
    # had. It is also scale-correct for free, because the radius is in world
    # units, so a 0.4-unit infantryman and a two-unit structure get chips of
    # the same physical size instead of the same UV size.
    bevm = nt.nodes.new('ShaderNodeBevel'); bevm.location = (-1200, 300)
    # 16 rather than 8: the Bevel node traces rays, so its normal is a random
    # variable, and a threshold applied to a noisy input dithers the mask edge
    # into intermediate values that the bake then averages. Raising the sample
    # count is the cheapest way to keep the metallic channel binary; measured
    # on dir_cannon_tank, the share of texels stranded in the invalid 0.10 to
    # 0.85 middle band falls from 12.5 per cent at 8 samples to 7.7 at 16 and
    # 5.3 at 32, for no change in any other statistic.
    bevm.samples = 32
    bevm.inputs['Radius'].default_value = 0.04
    geo = nt.nodes.new('ShaderNodeNewGeometry'); geo.location = (-1200, 140)
    dot = nt.nodes.new('ShaderNodeVectorMath'); dot.location = (-1000, 300)
    dot.operation = 'DOT_PRODUCT'
    nt.links.new(bevm.outputs['Normal'], dot.inputs[0])
    nt.links.new(geo.outputs['Normal'], dot.inputs[1])
    conv = nt.nodes.new('ShaderNodeMath'); conv.location = (-900, 300)
    conv.operation = 'SUBTRACT'
    conv.inputs[0].default_value = 1.0
    nt.links.new(dot.outputs['Value'], conv.inputs[1])
    ramp = nt.nodes.new('ShaderNodeValToRGB'); ramp.location = (-800, 300)
    # Window chosen from the baked distribution above: the lower edge sits
    # clear of every object's flat-face median and the upper edge lands inside
    # the tail, which gives roughly ten to twenty per cent chip coverage on a
    # bevelled hull and close to none on a flat panel such as a factory door.
    ramp.color_ramp.elements[0].position = 0.20 - wear * 0.16
    ramp.color_ramp.elements[1].position = 0.58 - wear * 0.16
    nt.links.new(conv.outputs['Value'], ramp.inputs['Fac'])
    # break the wear line up with fine noise so chips look chipped, not airbrushed
    n1 = _node(nt, 'ShaderNodeTexNoise', -1000, 60)
    n1.inputs['Scale'].default_value = 34.0
    n1.inputs['Detail'].default_value = 6.0
    mul = nt.nodes.new('ShaderNodeMath'); mul.location = (-620, 250)
    mul.operation = 'MULTIPLY'
    nt.links.new(ramp.outputs['Color'], mul.inputs[0])
    nt.links.new(n1.outputs['Fac'], mul.inputs[1])

    # --- V2-01: Metallic driven from the chip mask, not from a constant ---
    # The metallic-roughness BRDF is a two-material model and the only two
    # materials on any of these assets are painted steel, which is a
    # dielectric at 0.0, and the bare metal exposed where the paint has
    # chipped off, which is 1.0. Anything in between is not a material. The
    # chip mask above is already the map of where paint is missing, so it is
    # also, by construction, the map of where metal shows.
    #
    # It is pushed through a steep ramp rather than used raw because the mask
    # is a mix FACTOR: it is fine for it to be 0.4 when blending toward the
    # chip colour, but a metallic of 0.4 is the exact defect this ticket
    # exists to remove. The narrow 0.08-to-0.20 window makes the channel
    # binary everywhere except the transition texel at a chip edge, which is
    # the one place a blend is physically legitimate. The window sits low
    # because any paint loss at all exposes metal underneath.
    mramp = nt.nodes.new('ShaderNodeValToRGB'); mramp.location = (-420, 380)
    mramp.color_ramp.interpolation = 'CONSTANT'
    mramp.color_ramp.elements[0].position = 0.0
    mramp.color_ramp.elements[1].position = 0.14
    nt.links.new(mul.outputs['Value'], mramp.inputs['Fac'])
    nt.links.new(mramp.outputs['Color'], b.inputs['Metallic'])

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
    # doc 22 C-05 clause 3: the Directorate bare-metal chip was a neutral grey
    # (0.50,0.53,0.56) that fights the new steel-blue body; a cool-tinted chip
    # sits with it. The Sodality salvage chip stays dark (darker, not brighter).
    chip = (0.42, 0.50, 0.60, 1) if name not in ('rust', 'rustp', 'rustd') \
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
