"""
Hand-held items, built procedurally in Blender (run headless):

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_items.py -- <output_dir> [--preview <dir>]

Units are metres. Each item's origin is at its bottom centre, and the "front" (a spray can's nozzle) faces -Y in
Blender, which is +Z forward in Unity with the export axes used here (same as build_props.py). Material slots are
named for the colours Unity gives them (Interaction/ItemMaterials).
Every item gets one UV map at 1 UV unit = 0.25 m of surface (the same density as build_items2.py).
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else os.getcwd()
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgb, rough=0.5, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    m.diffuse_color = (*rgb, 1)          # what the workbench preview shows
    m.metallic = metal
    m.roughness = rough
    return m


def lathe(name, profile, material, segments=48, cap_bottom=True, cap_top=True):
    """Surface of revolution about Z from (radius, z) points listed bottom to top."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        ring = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    if cap_bottom and profile[0][0] > 1e-5:
        bm.faces.new(list(reversed(rings[0])))
    if cap_top and profile[-1][0] > 1e-5:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    me.materials.append(material)
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return o


TEXEL = 0.25      # metres of surface per UV unit, the same on every item (for tiling detail maps)


def uv_area(pts):
    return 0.5 * abs(sum(pts[i - 1][0] * pts[i][1] - pts[i][0] * pts[i - 1][1] for i in range(len(pts))))


def uv_world(ob, angle=66.0, margin=0.004):
    """One UV map: smart UV project (islands keep their relative scale), then scaled so 1 UV unit = TEXEL metres."""
    me = ob.data
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UVMap")
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    uv = me.uv_layers[0].data
    a3 = sum(p.area for p in me.polygons)
    a2 = sum(uv_area([uv[i].uv for i in p.loop_indices]) for p in me.polygons)
    k = math.sqrt(a3 / a2) / TEXEL
    for d in uv:
        d.uv = d.uv * k


def spray_can():
    red = mat("CanRed", (0.62, 0.03, 0.025), 0.28)
    metal = mat("CanMetal", (0.78, 0.78, 0.80), 0.25, 1.0)
    cap = mat("CanCap", (0.9, 0.9, 0.88), 0.45)
    label = mat("CanLabel", (0.04, 0.04, 0.045), 0.5)
    R, H = 0.0330, 0.180          # 66 mm can, 180 mm body (a 400 ml aerosol)
    parts = []
    # Base: a rolled rim and a domed (concave) bottom, in metal.
    parts.append(lathe("Base", [(0.0, 0.010), (0.022, 0.006), (0.030, 0.0015), (R - 0.0012, 0.0), (R, 0.0025), (R, 0.006)], metal, cap_bottom=False, cap_top=False))
    # Body: the painted cylinder, with a printed band.
    parts.append(lathe("Body", [(R, 0.006), (R, H)], red, cap_bottom=False, cap_top=False))
    # Shoulder dome up to the valve cup rim, in metal.
    dome = [(R, H), (R - 0.0015, H + 0.006), (0.029, H + 0.013), (0.024, H + 0.020), (0.0175, H + 0.025), (0.0135, H + 0.027),
            (0.0130, H + 0.0295), (0.0118, H + 0.031), (0.0105, H + 0.030), (0.0, H + 0.030)]
    parts.append(lathe("Dome", dome, metal, cap_bottom=False, cap_top=False))
    # Actuator (the button you press), white plastic, with the nozzle facing front (-Y).
    act = lathe("Actuator", [(0.0095, H + 0.030), (0.0095, H + 0.043), (0.0085, H + 0.0455), (0.0, H + 0.046)], cap, segments=32, cap_bottom=False, cap_top=False)
    parts.append(act)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.0022, radius2=0.0022, depth=0.004)
    for v in bm.verts:
        v.co = Vector((v.co.x, v.co.z - 0.0095, v.co.y + H + 0.038))   # along -Y, out of the actuator's front
    me = bpy.data.meshes.new("Nozzle"); bm.to_mesh(me); bm.free()
    me.materials.append(label)
    nz = bpy.data.objects.new("Nozzle", me); bpy.context.collection.objects.link(nz)
    parts.append(nz)

    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    can = bpy.context.active_object
    can.name = "SprayCan"
    bpy.ops.object.shade_smooth()
    m = can.modifiers.new("Weighted", "WEIGHTED_NORMAL")
    m.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00005)
    bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.object.mode_set(mode="OBJECT")
    uv_world(can)
    # (2026-11-04) The printed label round the body, its own UVs (u round the can, v up), joined on.
    lab = Canvas(512, 256, (0.62, 0.03, 0.025))
    lab.rect(0, 40, 512, 216, (0.06, 0.06, 0.065))
    lab.rect(0, 46, 512, 52, (0.9, 0.85, 0.2))
    lab.rect(0, 204, 512, 210, (0.9, 0.85, 0.2))
    lab.text("SPRAY PAINT", 40, 64, 4, (0.96, 0.95, 0.92))
    lab.text("GLOSS RED", 40, 112, 3, (0.85, 0.12, 0.08))
    lab.text("PROTECTIVE ENAMEL", 40, 146, 2, (0.9, 0.9, 0.88))
    lab.text("NET WT 12 OZ", 40, 174, 2, (0.9, 0.9, 0.88))
    lab.rect(300, 60, 470, 190, (0.92, 0.92, 0.9))
    lab.text("DANGER", 318, 70, 2, (0.75, 0.05, 0.04))
    lab.lines(312, 96, 460, 9, 9, (0.3, 0.3, 0.3), seed=3)
    for i in range(30):
        lab.rect(300 + i * 5, 222, 302 + i * 5 + (i % 3), 250, (0.05, 0.05, 0.05))
    LB = print_mat("CanLabel", lab)
    bm = bmesh.new()
    segs, z0, z1 = 48, 0.03, 0.15
    rr = R + 0.0003
    r0 = [bm.verts.new((math.cos(2 * math.pi * i / segs) * rr, math.sin(2 * math.pi * i / segs) * rr, z0)) for i in range(segs + 1)]
    r1 = [bm.verts.new((math.cos(2 * math.pi * i / segs) * rr, math.sin(2 * math.pi * i / segs) * rr, z1)) for i in range(segs + 1)]
    for i in range(segs):
        bm.faces.new((r0[i], r0[i + 1], r1[i + 1], r1[i]))
    me2 = bpy.data.meshes.new("Label")
    bm.to_mesh(me2)
    bm.free()
    me2.materials.append(LB)
    lob = bpy.data.objects.new("Label", me2)
    bpy.context.collection.objects.link(lob)
    uvl = me2.uv_layers.new(name=can.data.uv_layers[0].name).data
    for f in me2.polygons:
        f.use_smooth = True
        for li in f.loop_indices:
            vi = me2.loops[li].vertex_index
            i = vi % (segs + 1)
            uvl[li].uv = (i / segs, 0.0 if vi < segs + 1 else 1.0)
    bpy.ops.object.select_all(action="DESELECT")
    can.select_set(True)
    lob.select_set(True)
    bpy.context.view_layer.objects.active = can
    bpy.ops.object.join()
    can = bpy.context.active_object
    can.name = "SprayCan"
    return can


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, object_types={"MESH"},
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)


def preview(obj, path):
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x, scene.render.resolution_y = 512, 768
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    bpy.context.collection.objects.link(cam)
    cam.location = (0.30, -0.42, 0.24)
    d = Vector((0, 0, 0.11)) - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 55
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


exec(compile(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "bake_kit.py"), encoding="utf-8").read(), "bake_kit", "exec"))
can = spray_can()
info = bake(can, "SprayCan", OUT, scale=0.2, px_per_m=1600.0)
print("BAKED SprayCan", info)
tris = len(can.data.polygons)
export(can, os.path.join(OUT, "SprayCan.fbx"))
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    preview(can, os.path.join(PREVIEW, "SprayCan.png"))
print("EXPORTED SprayCan", tris, "triangles")
