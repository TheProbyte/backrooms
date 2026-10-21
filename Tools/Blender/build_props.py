"""
Backrooms prop models, built procedurally in Blender (run headless):

    blender -b --factory-startup --python Tools/Blender/build_props.py -- <output_dir>

Every prop is low-poly with bevelled edges and weighted normals so it shades smoothly at a few hundred
to ~2k triangles. Material slots are named after the Unity materials they map to:
  Fabric, Wood, Metal, Shade, Plastic, Laminate, Glass.
Units are metres; each prop's origin is on the floor at its footprint centre, front facing -Y in Blender
(which becomes +Z forward in Unity with the export axes used here).
"""
import bpy
import bmesh
import math
import os
import sys

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.path.join(os.getcwd(), "Models")
os.makedirs(OUT, exist_ok=True)


# ---------------------------------------------------------------- helpers

def reset():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    return m


def assign(obj, material_name):
    obj.data.materials.clear()
    obj.data.materials.append(mat(material_name))


def box(name, size, loc, material, bevel=0.0, segments=2):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    assign(o, material)
    if bevel > 0:
        m = o.modifiers.new("bevel", "BEVEL")
        m.width = bevel
        m.segments = segments
        m.limit_method = "ANGLE"
    return o


def cyl(name, radius, depth, loc, material, verts=16, rot=(0, 0, 0), bevel=0.0, cap=True):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot,
                                        end_fill_type="NGON" if cap else "NOTHING")
    o = bpy.context.active_object
    o.name = name
    assign(o, material)
    if bevel > 0:
        m = o.modifiers.new("bevel", "BEVEL")
        m.width = bevel
        m.segments = 2
        m.limit_method = "ANGLE"
    return o


def cone(name, r1, r2, depth, loc, material, verts=24, cap=False):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc,
                                    end_fill_type="NGON" if cap else "NOTHING")
    o = bpy.context.active_object
    o.name = name
    assign(o, material)
    return o


def finish(name, parts):
    """Applies modifiers, joins parts, smooths with weighted normals, triangulates, exports FBX."""
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
        bpy.context.view_layer.objects.active = p
        for m in list(p.modifiers):
            bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.shade_smooth()
    wn = o.modifiers.new("wn", "WEIGHTED_NORMAL")
    wn.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier="wn")
    tri = o.modifiers.new("tri", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier="tri")
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False,
                             object_types={"MESH"})
    print("[props] exported", path, "tris:", sum(len(p.vertices) - 2 for p in o.data.polygons))
    bpy.ops.object.delete()


# ---------------------------------------------------------------- props

def chesterfield():
    """Low, deep sofa with rolled arms the same height as the back (ref: grey chesterfield)."""
    L, D = 2.05, 0.9
    parts = [
        box("base", (L - 0.1, D - 0.1, 0.24), (0, 0, 0.26), "Fabric", 0.04),
        box("seat", (L - 0.46, D - 0.32, 0.12), (0, -0.07, 0.43), "Fabric", 0.05, 3),
        box("back", (L - 0.1, 0.2, 0.38), (0, D / 2 - 0.12, 0.55), "Fabric", 0.06, 3),
    ]
    for s in (-1, 1):
        parts.append(box("arm", (0.2, D - 0.04, 0.3), (s * (L / 2 - 0.12), 0, 0.5), "Fabric", 0.05, 3))
        parts.append(cyl("roll", 0.11, D - 0.02, (s * (L / 2 - 0.12), 0, 0.66), "Fabric", 16, (math.pi / 2, 0, 0)))
    parts.append(cyl("backroll", 0.1, L - 0.1, (0, D / 2 - 0.12, 0.74), "Fabric", 16, (0, math.pi / 2, 0)))
    # Tufting suggestion: shallow seams across the back.
    for i in range(-3, 4):
        parts.append(box("tuft", (0.012, 0.03, 0.28), (i * 0.24, D / 2 - 0.235, 0.56), "Fabric"))
    for x in (-1, 1):
        for y in (-1, 1):
            parts.append(cyl("leg", 0.03, 0.14, (x * (L / 2 - 0.12), y * (D / 2 - 0.12), 0.07), "Wood", 10))
    finish("Sofa_Chesterfield", parts)


def modern_sofa():
    """Square-armed 1990s office lounge sofa with loose seat and back cushions."""
    L, D = 2.1, 0.88
    parts = [box("base", (L, D, 0.2), (0, 0, 0.19), "Fabric", 0.03)]
    for s in (-1, 1):
        parts.append(box("arm", (0.17, D, 0.42), (s * (L / 2 - 0.085), 0, 0.42), "Fabric", 0.045, 3))
    parts.append(box("backframe", (L - 0.02, 0.15, 0.5), (0, D / 2 - 0.075, 0.54), "Fabric", 0.04))
    n = 3
    cw = (L - 0.34) / n
    for i in range(n):
        x = -L / 2 + 0.17 + cw * (i + 0.5)
        parts.append(box("seat", (cw - 0.01, D - 0.22, 0.14), (x, -0.06, 0.36), "Fabric", 0.05, 3))
        parts.append(box("cush", (cw - 0.01, 0.16, 0.4), (x, D / 2 - 0.22, 0.62), "Fabric", 0.06, 3))
    for x in (-1, 1):
        for y in (-1, 1):
            parts.append(box("foot", (0.05, 0.05, 0.09), (x * (L / 2 - 0.08), y * (D / 2 - 0.08), 0.045), "Wood"))
    finish("Sofa_Modern", parts)


def armchair():
    """Rolled-arm upholstered armchair (ref: grey armchair).
    Parts overlap generously and no two faces are coplanar, so nothing flickers or shows a seam."""
    W, D = 0.95, 0.9
    arm_w = 0.18
    inner = W / 2 - arm_w                     # inside face of each arm
    parts = [
        box("base", (W - 0.02, D - 0.04, 0.26), (0, -0.01, 0.24), "Fabric", 0.05, 3),
        # Seat cushion sits between the arms with a small tuck gap, slightly proud of the base front.
        box("seat", (inner * 2 - 0.03, D - 0.22, 0.15), (0, -0.075, 0.435), "Fabric", 0.06, 3),
        box("back", (inner * 2 + 0.08, 0.2, 0.6), (0, D / 2 - 0.12, 0.67), "Fabric", 0.08, 3),
    ]
    for s in (-1, 1):
        x = s * (W / 2 - arm_w / 2)
        # Arm block is recessed behind the roll so the roll's rounded front reads cleanly.
        parts.append(box("arm", (arm_w - 0.01, D - 0.1, 0.32), (x, 0.02, 0.5), "Fabric", 0.05, 3))
        parts.append(cyl("roll", 0.105, D - 0.04, (x, -0.01, 0.665), "Fabric", 20, (math.pi / 2, 0, 0), bevel=0.03))
    for x in (-1, 1):
        for y in (-1, 1):
            parts.append(cyl("leg", 0.025, 0.13, (x * (W / 2 - 0.1), y * (D / 2 - 0.1), 0.065), "Wood", 10))
    finish("Armchair", parts)


def floor_lamp():
    """Thin pole lamp with a drum shade and round weighted base (ref: lounge floor lamps)."""
    parts = [
        cyl("base", 0.16, 0.025, (0, 0, 0.0125), "Metal", 32, bevel=0.006),
        cyl("pole", 0.012, 1.42, (0, 0, 0.735), "Metal", 10),
        cyl("collar", 0.025, 0.05, (0, 0, 1.43), "Metal", 12),
        cone("shade", 0.23, 0.2, 0.32, (0, 0, 1.55), "Shade", 32),
        cyl("bulb", 0.035, 0.09, (0, 0, 1.49), "Glass", 12),
    ]
    # Inner face of the shade so it reads from below.
    inner = cone("shade_in", 0.225, 0.195, 0.315, (0, 0, 1.55), "Shade", 32)
    bpy.context.view_layer.objects.active = inner
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.flip_normals()
    bpy.ops.object.mode_set(mode="OBJECT")
    parts.append(inner)
    finish("FloorLamp", parts)


def office_chair():
    """Mesh-back task chair on a five-star base with casters (ref: conference room chairs).
    Every part touches the next: casters hang from forks at the leg tips, legs meet the hub, the gas
    lift joins hub and seat plate, and the arms are bolted to the seat on brackets."""
    parts = [
        box("seat", (0.48, 0.46, 0.07), (0, -0.02, 0.47), "Fabric", 0.025, 3),
        box("plate", (0.26, 0.26, 0.03), (0, -0.02, 0.425), "Plastic", 0.006),
        box("back", (0.44, 0.04, 0.5), (0, 0.24, 0.8), "Fabric", 0.02, 2),
        box("spine", (0.05, 0.03, 0.42), (0, 0.235, 0.6), "Plastic", 0.01),
        box("spine_foot", (0.05, 0.2, 0.03), (0, 0.14, 0.42), "Plastic", 0.006),
        cyl("lift", 0.022, 0.33, (0, 0, 0.27), "Metal", 12),
        cyl("hub", 0.05, 0.06, (0, 0, 0.11), "Plastic", 14, bevel=0.008),
    ]
    reach = 0.3
    for i in range(5):
        a = i * 2 * math.pi / 5
        dx, dy = math.sin(a), math.cos(a)
        # Leg from inside the hub to just past the caster fork.
        leg = box("leg", (0.042, reach, 0.032), (dx * reach / 2, dy * reach / 2, 0.095), "Plastic", 0.008)
        leg.rotation_euler = (0, 0, -a)
        bpy.context.view_layer.objects.active = leg
        bpy.ops.object.transform_apply(rotation=True)
        parts.append(leg)
        tip = reach - 0.025
        parts.append(cyl("fork", 0.012, 0.04, (dx * tip, dy * tip, 0.07), "Plastic", 8))
        parts.append(cyl("caster", 0.026, 0.024, (dx * tip, dy * tip, 0.027), "Plastic", 12, (0, math.pi / 2, -a)))
    for s in (-1, 1):
        parts.append(box("armbracket", (0.09, 0.05, 0.02), (s * 0.235, 0.02, 0.43), "Plastic", 0.005))
        parts.append(box("armpost", (0.03, 0.03, 0.27), (s * 0.27, 0.02, 0.56), "Plastic", 0.008))
        parts.append(box("armpad", (0.06, 0.24, 0.025), (s * 0.27, 0.0, 0.705), "Plastic", 0.01))
    finish("OfficeChair", parts)


def conference_table():
    """1 m long unit module (scaled along X in Unity) with a laminate top and panel legs."""
    parts = [
        box("top", (1.0, 1.1, 0.035), (0, 0, 0.7225), "Laminate", 0.008),
        box("edge", (1.0, 1.112, 0.02), (0, 0, 0.695), "Plastic", 0.004),
        box("apron", (0.9, 0.03, 0.09), (0, 0, 0.64), "Wood"),
    ]
    finish("ConferenceTable_Module", parts)
    legs = [
        box("panel", (0.05, 0.8, 0.66), (0, 0, 0.35), "Wood", 0.01),
        box("foot", (0.08, 0.9, 0.025), (0, 0, 0.0125), "Metal", 0.005),
    ]
    finish("ConferenceTable_Leg", legs)


def wall_clock():
    """Round office wall clock (ref: lounge clock). Origin at the wall, face toward -Y."""
    rim = cyl("rim", 0.17, 0.045, (0, -0.0225, 0), "Plastic", 32, (math.pi / 2, 0, 0), bevel=0.01)
    face = cyl("face", 0.155, 0.002, (0, -0.046, 0), "Laminate", 32, (math.pi / 2, 0, 0))
    glass = cyl("glass", 0.157, 0.003, (0, -0.05, 0), "Glass", 32, (math.pi / 2, 0, 0))
    parts = [rim, face, glass]
    for i in range(12):
        a = i * math.pi / 6
        t = box("tick", (0.008, 0.002, 0.025 if i % 3 == 0 else 0.014), (math.sin(a) * 0.13, -0.048, math.cos(a) * 0.13), "Metal")
        t.rotation_euler = (0, -a, 0)
        bpy.context.view_layer.objects.active = t
        bpy.ops.object.transform_apply(rotation=True)
        parts.append(t)
    hour = box("hour", (0.01, 0.002, 0.075), (0.02, -0.049, 0.03), "Metal")
    hour.rotation_euler = (0, -0.6, 0)
    minute = box("minute", (0.007, 0.002, 0.115), (-0.04, -0.0495, 0.03), "Metal")
    minute.rotation_euler = (0, 0.9, 0)
    for h in (hour, minute):
        bpy.context.view_layer.objects.active = h
        bpy.ops.object.transform_apply(rotation=True)
        parts.append(h)
    finish("WallClock", parts)


reset()
for build in (chesterfield, modern_sofa, armchair, floor_lamp, office_chair, conference_table, wall_clock):
    reset()
    build()
print("[props] done ->", OUT)
