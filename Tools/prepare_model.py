"""
Prepare a generated high-poly GLB for the game.

The source models come out of Tripo at around two million triangles with an 8k texture,
which is far too heavy for a prop that sits a few metres from the camera. This script
joins the model into one mesh, decimates it to a triangle budget, normalises its size and
origin (feet on the ground, centred), and writes an OBJ plus a single albedo PNG.

Usage:
    blender --background --python Tools/prepare_model.py -- <in.glb> <out.obj> <out.png> <tris> <height>
"""
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_obj, out_png = argv[0], argv[1], argv[2]
target_tris = int(argv[3])
target_height = float(argv[4])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def count_tris():
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# The importer splits vertices along UV seams, which stops the collapse decimator
# long before the budget. Weld first, then decimate in passes until we get there.
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=0.0001)
bpy.ops.object.mode_set(mode="OBJECT")

print("source triangles:", count_tris())
for _ in range(4):
    tris = count_tris()
    if tris <= target_tris * 1.15:
        break
    mod = obj.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = target_tris / tris
    bpy.ops.object.modifier_apply(modifier=mod.name)
print("output triangles:", count_tris())

# Normalise: target height, centred on X/Y, resting on Z = 0.
corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
scale = target_height / (hi.z - lo.z)
centre = (lo + hi) * 0.5
for v in obj.data.vertices:
    v.co = Vector(((v.co.x - centre.x) * scale, (v.co.y - centre.y) * scale, (v.co.z - lo.z) * scale))
obj.data.update()
print("size (x, y, z):", (hi - lo) * scale)

bpy.ops.object.shade_smooth()
bpy.ops.wm.obj_export(
    filepath=out_obj,
    export_selected_objects=True,
    export_materials=False,
    export_triangulated_mesh=True,
    forward_axis="NEGATIVE_Z",
    up_axis="Y",
)

# Albedo: whatever feeds Base Color on the first material that has a texture.
image = None
for mat in obj.data.materials:
    if not mat or not mat.use_nodes:
        continue
    for node in mat.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED" and node.inputs["Base Color"].links:
            source = node.inputs["Base Color"].links[0].from_node
            if source.type == "TEX_IMAGE" and source.image:
                image = source.image
    if image:
        break

if image:
    if image.size[0] > 1024:
        image.scale(1024, 1024)
    image.filepath_raw = out_png
    image.file_format = "PNG"
    image.save()
    print("albedo written:", out_png)
else:
    print("no albedo texture found")
