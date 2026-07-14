# Bake + export pipeline (plan step c): procedural weathered materials
# cannot cross the glTF boundary, so per model: UV smart-project, bake
# diffuse + roughness to 1024px images, swap in a simple baked-texture
# material, export .glb into game/assets/models/.
# Run: blender -b -P bake.py            (all 20 models, ~minutes on CPU)
#      blender -b -P bake.py -- dir_cannon_tank   (just one, for testing)
import bpy, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import builder

builder.USE_WEATHERED = True
OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'game', 'assets', 'models'))
SIZE = 1024

only = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
names = only or list(builder.BUILDERS.keys())

def bake_pass(obj, bake_type, img_name, colorspace):
    img = bpy.data.images.new(img_name, SIZE, SIZE, alpha=False)
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
    for nt, node in nodes_added:
        nt.nodes.remove(node)
    return img

for name in names:
    builder.scene_setup()
    o = builder.BUILDERS[name]()
    # The model may carry child objects (e.g. the vanguard turret) which
    # must be UV'd, baked, and exported alongside the root.
    objs = [o] + [c for c in o.children_recursive]
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
    for ob in objs:
        suffix = '' if ob is o else f'_{ob.name}'
        diff = bake_pass(ob, 'DIFFUSE', f'{name}{suffix}_diff', 'sRGB')
        rough = bake_pass(ob, 'ROUGHNESS', f'{name}{suffix}_rough', 'Non-Color')

        # collect emission (ferrite glow) before replacing materials
        emissive = None
        for slot in ob.material_slots:
            b = slot.material.node_tree.nodes.get('Principled BSDF')
            if b and b.inputs['Emission Strength'].default_value > 0:
                emissive = (tuple(b.inputs['Emission Color'].default_value),
                            b.inputs['Emission Strength'].default_value)

        baked = bpy.data.materials.new(f'{name}{suffix}_baked')
        baked.use_nodes = True
        nt = baked.node_tree
        bsdf = nt.nodes['Principled BSDF']
        tex_d = nt.nodes.new('ShaderNodeTexImage'); tex_d.image = diff
        tex_r = nt.nodes.new('ShaderNodeTexImage'); tex_r.image = rough
        tex_r.image.colorspace_settings.name = 'Non-Color'
        nt.links.new(tex_d.outputs['Color'], bsdf.inputs['Base Color'])
        nt.links.new(tex_r.outputs['Color'], bsdf.inputs['Roughness'])
        bsdf.inputs['Metallic'].default_value = 0.25
        if emissive:
            bsdf.inputs['Emission Color'].default_value = emissive[0]
            bsdf.inputs['Emission Strength'].default_value = min(emissive[1], 1.0)
        ob.data.materials.clear()
        ob.data.materials.append(baked)

    bpy.ops.object.select_all(action='DESELECT')
    for ob in objs: ob.select_set(True)
    o.select_set(True)
    path = os.path.join(OUT, f'{name}.glb')
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True)
    print(f'BAKED {name} -> {path}')
print('BAKE PIPELINE DONE')
