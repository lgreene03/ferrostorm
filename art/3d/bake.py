# Bake + export pipeline (Wave 4, doc 20): procedural weathered materials
# cannot cross the glTF boundary, so per model: UV smart-project, bake the
# full PBR set to images, swap in a baked-texture material, export .glb
# into game/assets/models/. Per object the passes are:
#   diffuse (albedo, AO multiplied in - W4-03), tangent normal (bevel
#   shader + panel seams + micro grain - W4-04), emission (per-model
#   emissive map, black-detected and dropped when unused - W4-01), and a
#   packed ORM (AO/rough/metal, metallic via the emission value bridge -
#   W4-06). Resolution is per class: 2048 structures / 1024 vehicles /
#   512 infantry (W4-05).
# Run: blender -b -P bake.py            (all 20 models, ~minutes on CPU)
#      blender -b -P bake.py -- dir_cannon_tank   (just one, for testing)
import bpy, os, sys
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import builder

builder.USE_WEATHERED = True
OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'game', 'assets', 'models'))

# W4-05: texel density per class. Structures are 2x2 cells and were visibly
# soft at 1024 next to a 0.4-cell engineer baked at the same size.
STRUCT = {'com_power_plant', 'com_factory', 'com_refinery',
          'com_construction_yard', 'dir_turret', 'dir_superweapon',
          'sod_veil_projector', 'com_service_depot'}
INF = {'com_rifle_squad', 'com_rocket_squad', 'com_engineer'}

def size_for(name):
    return 2048 if name in STRUCT else (512 if name in INF else 1024)

only = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
names = only or list(builder.BUILDERS.keys())

def bake_pass(obj, bake_type, img_name, colorspace, size):
    img = bpy.data.images.new(img_name, size, size, alpha=False)
    img.colorspace_settings.name = colorspace
    # every material on the object needs an active image node targeting img
    nodes_added = []
    for slot in obj.material_slots:
        nt = slot.material.node_tree
        node = nt.nodes.new('ShaderNodeTexImage')
        node.image = img
        nt.nodes.active = node
        nodes_added.append((nt, node))
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.bake(type=bake_type)
    # Blender 5.x trap (doc 17 family): bake results land in the image's
    # unsaved buffer, and the NEXT object's bake re-evaluates the scene and
    # silently drops any such buffer that is not packed - the first object's
    # normal map exported pure black. Pack immediately to pin the pixels.
    img.pack()
    for nt, node in nodes_added:
        nt.nodes.remove(node)
    return img

def bake_value_pass(obj, img_name, size):
    # W4-06 value bridge: Metallic is not a bakeable pass, so temporarily
    # drive each material's output from an Emission shader holding the
    # metallic value, bake EMIT, then restore the original surface link.
    # Materials are cached and shared across slots/objects: swap each
    # unique material once or the second swap would capture the bridge
    # itself as the link to restore.
    saved, seen = [], set()
    for slot in obj.material_slots:
        m = slot.material
        if m.name in seen:
            continue
        seen.add(m.name)
        nt = m.node_tree
        outn = nt.nodes['Material Output']
        old = outn.inputs['Surface'].links[0].from_socket
        em = nt.nodes.new('ShaderNodeEmission')
        v = (nt.nodes['Principled BSDF'].inputs['Metallic'].default_value
             if 'Principled BSDF' in nt.nodes else 0.0)
        em.inputs['Color'].default_value = (v, v, v, 1)
        nt.links.new(em.outputs['Emission'], outn.inputs['Surface'])
        saved.append((nt, outn, old, em))
    img = bake_pass(obj, 'EMIT', img_name, 'Non-Color', size)
    for nt, outn, old, em in saved:
        nt.links.new(old, outn.inputs['Surface'])
        nt.nodes.remove(em)
    return img

def pixels_of(img):
    arr = np.empty(len(img.pixels), np.float32)
    img.pixels.foreach_get(arr)
    return arr

for name in names:
    builder.scene_setup()
    o = builder.BUILDERS[name]()
    # The model may carry child objects (e.g. the vanguard turret) which
    # must be UV'd, baked, and exported alongside the root.
    objs = [o] + [c for c in o.children_recursive]
    size = size_for(name)
    for ob in objs:
        bpy.ops.object.select_all(action='DESELECT')
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.003)
        bpy.ops.object.mode_set(mode='OBJECT')

    sc = bpy.context.scene
    sc.cycles.samples = 16          # bakes need few samples
    sc.render.bake.use_pass_direct = False    # DIFFUSE: colour only,
    sc.render.bake.use_pass_indirect = False  # no lighting baked in
    sc.render.bake.margin = max(8, size // 128)   # island dilation scales
    sc.render.bake.normal_space = 'TANGENT'
    any_glow = False
    for ob in objs:
        suffix = '' if ob is o else f'_{ob.name}'
        diff = bake_pass(ob, 'DIFFUSE', f'{name}{suffix}_diff', 'sRGB', size)
        rough = bake_pass(ob, 'ROUGHNESS', f'{name}{suffix}_rough', 'Non-Color', size)

        # W4-01: real per-object emission map, dropped when the bake is black
        emit_img = bake_pass(ob, 'EMIT', f'{name}{suffix}_emit', 'sRGB', size)
        earr = pixels_of(emit_img)
        has_glow = earr.reshape(-1, 4)[:, :3].max() > 0.01

        # W4-04: tangent normal map (bevel shader edges + seams + grain)
        norm = bake_pass(ob, 'NORMAL', f'{name}{suffix}_norm', 'Non-Color', size)

        # W4-03: AO at raised quality, multiplied into the diffuse (glTF
        # cannot export a Mix node, so compose pixels directly)
        sc.cycles.samples = 64
        ao = bake_pass(ob, 'AO', f'{name}{suffix}_ao', 'Non-Color', size)
        sc.cycles.samples = 16
        d = pixels_of(diff)
        a = pixels_of(ao)
        k = 0.85
        out = d * (1.0 - k + k * a)
        out[3::4] = 1.0
        diff.pixels.foreach_set(out)
        diff.update()

        # W4-06: pack AO/rough/metal into one ORM image
        metal = bake_value_pass(ob, f'{name}{suffix}_metal', size)
        r_arr = pixels_of(rough)
        m_arr = pixels_of(metal)
        orm = bpy.data.images.new(f'{name}{suffix}_orm', size, size, alpha=False)
        orm.colorspace_settings.name = 'Non-Color'
        px = np.empty(size * size * 4, np.float32)
        px[0::4] = a[0::4]
        px[1::4] = r_arr[0::4]
        px[2::4] = m_arr[0::4]
        px[3::4] = 1.0
        orm.pixels.foreach_set(px)
        orm.update()
        for im in (rough, metal, ao):   # sources live on in the ORM copy
            bpy.data.images.remove(im)

        baked = bpy.data.materials.new(f'{name}{suffix}_baked')
        baked.use_nodes = True
        nt = baked.node_tree
        bsdf = nt.nodes['Principled BSDF']
        tex_d = nt.nodes.new('ShaderNodeTexImage'); tex_d.image = diff
        nt.links.new(tex_d.outputs['Color'], bsdf.inputs['Base Color'])

        # ORM wiring: Green -> Roughness, Blue -> Metallic through one
        # Separate Color node so the glTF exporter writes a single
        # metallicRoughnessTexture. No flat Metallic value any more.
        tex_orm = nt.nodes.new('ShaderNodeTexImage'); tex_orm.image = orm
        sep = nt.nodes.new('ShaderNodeSeparateColor')
        nt.links.new(tex_orm.outputs['Color'], sep.inputs['Color'])
        nt.links.new(sep.outputs['Green'], bsdf.inputs['Roughness'])
        nt.links.new(sep.outputs['Blue'], bsdf.inputs['Metallic'])
        # Occlusion export: the exporter recognises a node group named
        # 'glTF Material Output' with a float input 'Occlusion' and writes
        # occlusionTexture from whatever feeds it.
        grp = bpy.data.node_groups.get('glTF Material Output')
        if grp is None:
            grp = bpy.data.node_groups.new('glTF Material Output', 'ShaderNodeTree')
            grp.interface.new_socket('Occlusion', in_out='INPUT',
                                     socket_type='NodeSocketFloat')
        gnode = nt.nodes.new('ShaderNodeGroup')
        gnode.node_tree = grp
        nt.links.new(sep.outputs['Red'], gnode.inputs['Occlusion'])

        # Normal map (W4-04): Godot wires glTF normalTexture automatically
        tex_n = nt.nodes.new('ShaderNodeTexImage'); tex_n.image = norm
        tex_n.image.colorspace_settings.name = 'Non-Color'
        nmap = nt.nodes.new('ShaderNodeNormalMap')
        nt.links.new(tex_n.outputs['Color'], nmap.inputs['Color'])
        nt.links.new(nmap.outputs['Normal'], bsdf.inputs['Normal'])

        # Emissive (W4-01): strength 2.0 exports as
        # KHR_materials_emissive_strength, which Godot 4 imports
        if has_glow:
            any_glow = True
            tex_e = nt.nodes.new('ShaderNodeTexImage'); tex_e.image = emit_img
            nt.links.new(tex_e.outputs['Color'], bsdf.inputs['Emission Color'])
            bsdf.inputs['Emission Strength'].default_value = 2.0
        else:
            bpy.data.images.remove(emit_img)
            bsdf.inputs['Emission Strength'].default_value = 0.0

        ob.data.materials.clear()
        ob.data.materials.append(baked)

    bpy.ops.object.select_all(action='DESELECT')
    for ob in objs: ob.select_set(True)
    o.select_set(True)
    path = os.path.join(OUT, f'{name}.glb')
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True)
    print(f'BAKED {name} -> {path} ({size}px, glow={any_glow})')
print('BAKE PIPELINE DONE')
