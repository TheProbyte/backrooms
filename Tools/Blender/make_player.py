# Simple first-person player: left arm with a jointed hand (watch, ring) and a body (torso, legs, shoes).
# Every segment is its own object with its origin at its joint, parented in a chain, so Unity can bend the
# joints by rotating transforms. Segments point along -Y (forward) for the arm and -Z (down) for legs.
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix

out_dir = sys.argv[sys.argv.index("--") + 1]

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

def mat(name, rgb, rough=0.6, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m

SKIN = mat("Skin", (0.78, 0.52, 0.40), 0.55)
STRAP = mat("WatchStrap", (0.30, 0.32, 0.27), 0.9)
CASE = mat("WatchCase", (0.75, 0.74, 0.70), 0.3, 1.0)
FACE = mat("WatchFace", (0.02, 0.02, 0.025), 0.2)
RING = mat("Ring", (0.12, 0.12, 0.12), 0.35, 0.8)
SHIRT = mat("Shirt", (0.10, 0.10, 0.11), 0.85)
PANTS = mat("Pants", (0.025, 0.025, 0.03), 0.8)
SHOES = mat("Shoes", (0.03, 0.03, 0.035), 0.6)
SOLE = mat("Sole", (0.06, 0.06, 0.06), 0.7)

def link(obj, parent=None, at=(0, 0, 0)):
    bpy.context.collection.objects.link(obj)
    if parent:
        obj.parent = parent
    obj.location = at
    return obj

def mesh_obj(name, bm, material):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    me.materials.append(material)
    return bpy.data.objects.new(name, me)

def capsule(name, r0, r1, length, material, axis="-Y", seg=14, rings=8, flat=1.0):
    """Capsule from the origin along the axis, radius r0 at the base and r1 at the tip. flat squashes Z (for X-axis width)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for v in bm.verts:
        z = v.co.z
        t = 1.0 if z > 0 else 0.0
        r = r1 if t else r0
        v.co.x *= r
        v.co.y *= r * flat
        v.co.z = z * r + (length if t else 0.0)
    # Taper the side between the two caps.
    if axis == "-Y":
        rot = Matrix.Rotation(math.radians(90), 4, "X")      # +Z -> -Y
    elif axis == "-Z":
        rot = Matrix.Rotation(math.radians(180), 4, "X")     # +Z -> -Z
    else:
        rot = Matrix.Identity(4)
    bmesh.ops.transform(bm, matrix=rot, verts=bm.verts)
    return mesh_obj(name, bm, material)

def rbox(name, size, centre, material, bevel=0.01):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(centre), verts=bm.verts)
    bmesh.ops.bevel(bm, geom=bm.edges[:] + bm.verts[:], offset=bevel, segments=3, affect="EDGES", profile=0.5)
    return mesh_obj(name, bm, material)

def cylinder(name, r, depth, centre, material, axis="Y", seg=24):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=depth)
    if axis == "Y":
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, "X"))
    bmesh.ops.translate(bm, vec=Vector(centre), verts=bm.verts)
    o = mesh_obj(name, bm, material)
    for p in o.data.polygons:
        p.use_smooth = len(p.vertices) == 4
    return o

def empty(name, parent=None, at=(0, 0, 0)):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.02
    return link(e, parent, at)

# ---------------- Left arm ----------------
arm = empty("LeftArm")                                              # shoulder joint
upper = link(capsule("UpperArm", 0.046, 0.04, 0.28, SKIN), arm)
sleeve = link(capsule("Sleeve", 0.058, 0.055, 0.14, SHIRT), upper, (0, 0.012, 0))
fore = link(capsule("Forearm", 0.036, 0.028, 0.245, SKIN, flat=0.82), upper, (0, -0.28, 0))
# Watch on the wrist, face on the back of the wrist (+Z).
strap = link(cylinder("WatchStrap", 0.0315, 0.022, (0, -0.215, 0), STRAP), fore)
strap.scale = (1.0, 1.0, 0.86)
case = link(cylinder("WatchCase", 0.022, 0.011, (0, 0, 0), CASE, axis="Z", seg=32), fore, (0, -0.215, 0.027))
face = link(cylinder("WatchFace", 0.0185, 0.002, (0, 0, 0.0058), FACE, axis="Z", seg=32), case)

hand = empty("Hand", fore, (0, -0.245, 0))                          # wrist joint
palm = link(rbox("Palm", (0.084, 0.096, 0.028), (0, -0.05, 0), SKIN, 0.011), hand)
# Fingers: (name, x, y, radius, [segment lengths]). Left hand, palm down, fingers -Y: the thumb is on -X.
fingers = [
    ("Index", -0.029, -0.098, 0.0088, [0.043, 0.025, 0.021]),
    ("Middle", -0.0095, -0.101, 0.0092, [0.047, 0.028, 0.022]),
    ("Ring", 0.0105, -0.099, 0.0087, [0.044, 0.027, 0.021]),
    ("Pinky", 0.029, -0.092, 0.0076, [0.034, 0.021, 0.018]),
]
for name, x, y, r, lens in fingers:
    parent = palm
    at = (x, y, 0.002)
    for i, L in enumerate(lens):
        rr = r * (1.0 - 0.07 * i)
        seg = link(capsule(f"{name}{i + 1}", rr, rr * 0.93, L, SKIN, seg=12, rings=6, flat=0.9), parent, at)
        if name == "Ring" and i == 0:
            band = link(cylinder("RingBand", rr * 1.18, 0.0075, (0, -L * 0.42, 0), RING), seg)
        parent = seg
        at = (0, -L, 0)
# Thumb: from the heel of the palm, out to the side and forward, rolled toward the palm.
t_root = empty("ThumbBase", palm, (-0.033, -0.022, -0.006))
t_root.rotation_euler = (math.radians(-38), math.radians(-35), math.radians(-24))
parent, at = t_root, (0, 0, 0)
for i, (L, r) in enumerate([(0.042, 0.0125), (0.032, 0.0105), (0.026, 0.0097)]):
    seg = link(capsule(f"Thumb{i + 1}", r, r * 0.93, L, SKIN, seg=12, rings=6, flat=0.88), parent, at)
    parent, at = seg, (0, -L, 0)

# ---------------- Body ----------------
body = empty("PlayerBody")                                          # on the floor, under the eye
hips = empty("Hips", body, (0, 0, 0.93))
torso = link(rbox("Torso", (0.36, 0.22, 0.56), (0, 0.0, 0.30), SHIRT, 0.06), hips)
belt = link(rbox("Waist", (0.35, 0.21, 0.08), (0, 0.0, 0.0), PANTS, 0.03), hips)
for side, x in (("L", -0.1), ("R", 0.1)):
    thigh = link(capsule(f"Thigh{side}", 0.075, 0.06, 0.43, PANTS, axis="-Z"), hips, (x, 0, 0))
    shin = link(capsule(f"Shin{side}", 0.055, 0.045, 0.42, PANTS, axis="-Z"), thigh, (0, 0, -0.43))
    foot = empty(f"Foot{side}", shin, (0, 0, -0.42))
    shoe = link(rbox(f"Shoe{side}", (0.105, 0.28, 0.085), (0, -0.065, -0.035), SHOES, 0.035), foot)
    sole = link(rbox(f"Sole{side}", (0.11, 0.285, 0.02), (0, -0.065, -0.07), SOLE, 0.008), foot)

def export(root, path):
    bpy.ops.object.select_all(action="DESELECT")
    def sel(o):
        o.select_set(True)
        for c in o.children:
            sel(c)
    sel(root)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, object_types={"EMPTY", "MESH"},
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)

bpy.context.view_layer.update()
export(arm, out_dir + "/PlayerArm.fbx")
export(body, out_dir + "/PlayerBody.fbx")
bpy.ops.wm.save_as_mainfile(filepath=out_dir + "/PlayerModel.blend")
print("EXPORTED")
