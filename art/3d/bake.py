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
    # drive each material's output from an Emission shader carrying the
    # metallic value, bake EMIT, then restore the original surface link.
    # Materials are cached and shared across slots/objects: swap each
    # unique material once or the second swap would capture the bridge
    # itself as the link to restore.
    #
    # V2-01 (doc 25): bake the NODE OUTPUT, not the default_value. Reading
    # default_value is what put the constant 0.2 into the blue channel of all
    # 27 shipped models: materials2.wmat drives Metallic from a node graph,
    # and an unlinked socket's default_value is simply whatever was last
    # assigned to it before the link was made, which is a number nothing in
    # the render ever uses. When the socket is linked, the bridge is fed from
    # the same socket the Principled BSDF is fed from, so what lands in the
    # image is what the shader actually evaluates per texel. The constant path
    # is kept for materials that genuinely do hold a flat value, which is the
    # emissive family, since builder.mat only routes non-emissive parts
    # through wmat.
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
        msock = (nt.nodes['Principled BSDF'].inputs['Metallic']
                 if 'Principled BSDF' in nt.nodes else None)
        if msock is not None and msock.links:
            nt.links.new(msock.links[0].from_socket, em.inputs['Color'])
        else:
            v = msock.default_value if msock is not None else 0.0
            em.inputs['Color'].default_value = (v, v, v, 1)
        nt.links.new(em.outputs['Emission'], outn.inputs['Surface'])
        saved.append((nt, outn, old, em))
    img = bake_pass(obj, 'EMIT', img_name, 'Non-Color', size)
    for nt, outn, old, em in saved:
        nt.links.new(old, outn.inputs['Surface'])
        nt.nodes.remove(em)
    return img

def mute_metallic(objs):
    """Force Metallic to 0 on every unique material of objs, returning what is
    needed to put it back.

    V2-01: this is required the moment Metallic stops being a constant. The
    glTF base colour of a metal is its reflectance tint and must not be black,
    but the Cycles DIFFUSE pass returns the diffuse LOBE, and a Principled
    BSDF at Metallic 1.0 has no diffuse lobe at all, so it bakes to black.
    Verified on the first V2 test bake: driving Metallic from the chip mask
    without this dropped dir_cannon_tank's baked base-colour luminance from
    0.128 to 0.022, which is the map going dark exactly where the metal is.
    Muting Metallic for the duration of the DIFFUSE bake makes that pass mean
    what the pipeline has always used it to mean, which is base colour.
    """
    saved, seen = [], set()
    for ob in objs:
        for slot in ob.material_slots:
            m = slot.material
            if m is None or m.name in seen:
                continue
            seen.add(m.name)
            nt = m.node_tree
            if 'Principled BSDF' not in nt.nodes:
                continue
            s = nt.nodes['Principled BSDF'].inputs['Metallic']
            link = s.links[0].from_socket if s.links else None
            if link is not None:
                nt.links.remove(s.links[0])
            saved.append((nt, s, link, s.default_value))
            s.default_value = 0.0
    return saved


def restore_metallic(saved):
    for nt, s, link, dv in saved:
        s.default_value = dv
        if link is not None:
            nt.links.new(link, s)


def pixels_of(img):
    arr = np.empty(len(img.pixels), np.float32)
    img.pixels.foreach_get(arr)
    return arr


def emissive_scale(obj):
    """Largest emission colour*strength product on this object (doc 22 C-06).
    The EMIT bake target is an 8-bit buffer, so anything over 1.0 clips a
    channel, and clipping one channel of a saturated colour is a hue shift, not
    just a brightness cap. Scale every emissive material down by M before baking
    so nothing clips, and hand M back to the exported emissive strength."""
    m = 1.0
    for slot in obj.material_slots:
        if slot.material is None:
            continue
        nt = slot.material.node_tree
        if 'Principled BSDF' not in nt.nodes:
            continue
        b = nt.nodes['Principled BSDF']
        s = b.inputs['Emission Strength'].default_value
        c = b.inputs['Emission Color'].default_value
        m = max(m, s * max(c[0], c[1], c[2]))
    return m


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
    emit_scale_max = 1.0   # C-06 report: largest M applied on any object
    emit_val_max = 0.0     # C-06 report: largest baked emit channel (should
                           # end <= 1.0 with the hue preserved, vs a pre-change
                           # clamp that pinned it at 1.0 by cutting a channel)
    for ob in objs:
        suffix = '' if ob is o else f'_{ob.name}'
        muted = mute_metallic([ob])
        diff = bake_pass(ob, 'DIFFUSE', f'{name}{suffix}_diff', 'sRGB', size)
        restore_metallic(muted)
        rough = bake_pass(ob, 'ROUGHNESS', f'{name}{suffix}_rough', 'Non-Color', size)

        # doc 22 C-06: normalise every emissive material down by M before the
        # 8-bit EMIT bake so a saturated emissive keeps its hue instead of
        # clipping toward white (the superweapon core baked yellow, the veil orb
        # cyan-white, the ferrite tips pure white). The energy is put back at
        # export via 2.0 * M below, so (colour/M) * 2M == colour * 2 exactly.
        M = emissive_scale(ob)
        emit_scale_max = max(emit_scale_max, M)
        saved_es = []
        if M > 1.0:
            seen_es = set()
            for slot in ob.material_slots:
                mm = slot.material
                if mm is None or mm.name in seen_es:
                    continue
                seen_es.add(mm.name)   # materials are shared: divide each once
                nt = mm.node_tree
                if 'Principled BSDF' not in nt.nodes:
                    continue
                inp = nt.nodes['Principled BSDF'].inputs['Emission Strength']
                saved_es.append((inp, inp.default_value))
                inp.default_value = inp.default_value / M
        # W4-01: real per-object emission map, dropped when the bake is black
        emit_img = bake_pass(ob, 'EMIT', f'{name}{suffix}_emit', 'sRGB', size)
        for inp, v in saved_es:     # restore before any later pass reads them
            inp.default_value = v
        earr = pixels_of(emit_img)
        emit_val_max = max(emit_val_max, float(earr.reshape(-1, 4)[:, :3].max()))
        has_glow = earr.reshape(-1, 4)[:, :3].max() > 0.01

        # W4-04: tangent normal map (bevel shader edges + seams + grain)
        norm = bake_pass(ob, 'NORMAL', f'{name}{suffix}_norm', 'Non-Color', size)

        # W4-03: AO at raised quality. It goes into the ORM red channel below
        # and NOWHERE ELSE.
        #
        # V2-02 (doc 25): the multiply into the diffuse that used to live here
        # is deleted, not zeroed, so that a future reader cannot restore it by
        # tuning a constant back up. It read `out = d * (1 - k + k * a)` at
        # k = 0.85, which meant the exported base colour was the albedo with
        # ambient occlusion burned into it at eighty-five per cent. The same
        # AO is packed into the ORM red channel and exported as the glTF
        # occlusion texture, which Godot wires into the material's AO slot
        # automatically, and the environment then runs its own SSAO on top.
        # Occlusion was being applied three times, and the copy in the base
        # colour is the one that cannot be undone at runtime: a lighting term
        # in an albedo map is not recoverable, it is just a darker albedo. The
        # measured median of the shipped diffuse atlas was linear 0.037, which
        # is most of why the game reads dark.
        #
        # NOT touched, deliberately: use_pass_direct and use_pass_indirect are
        # already False above. Setting them was proposed as the fix for this
        # and is a verified no-op; the 0.992 correlation offered as evidence
        # for it was produced by the multiply on this line, by construction.
        sc.cycles.samples = 64
        ao = bake_pass(ob, 'AO', f'{name}{suffix}_ao', 'Non-Color', size)
        sc.cycles.samples = 16
        a = pixels_of(ao)

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
            # C-06: carry the normalisation back so the final look is unchanged
            # while the baked map keeps full chroma. Exports as
            # KHR_materials_emissive_strength, which Godot 4 imports.
            bsdf.inputs['Emission Strength'].default_value = 2.0 * M
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
    print(f'BAKED {name} -> {path} ({size}px, glow={any_glow}, '
          f'emit_scale={emit_scale_max:.3f}, emit_max={emit_val_max:.3f})')
print('BAKE PIPELINE DONE')
