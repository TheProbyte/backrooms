"""
More hand-held items and room props, built procedurally in Blender (run headless):

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_items2.py -- <output_dir> [--preview <dir>] [--only Chalk,Plug]

Same conventions as build_items.py: units are metres, real-world scale; origin at the bottom centre unless a model says
otherwise; the item's "front" faces -Y in Blender (+Z forward in Unity with the export axes below). Material slots are
named for the colours Unity rebuilds them with. Every model is one joined, triangulated mesh object.

Unity coordinates of a Blender point (x, y, z) are (x, z, -y).
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector, Matrix, noise

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS and not ARGS[0].startswith("--") else os.getcwd()
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None
ONLY = set(ARGS[ARGS.index("--only") + 1].split(",")) if "--only" in ARGS else None
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)

REPORT = {}


def U(v):
    """Blender point/vector -> Unity (x, y, z) tuple, rounded to 0.1 mm."""
    return tuple(round(c, 4) + 0.0 for c in (v[0], v[2], -v[1]))


# ----------------------------------------------------------------------------------------------------------- helpers
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


def new_obj(name, bm, materials):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in materials:
        me.materials.append(m)
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return o


def lathe(name, profile, material, segments=48, cap_bottom=True, cap_top=True, flip=False, xf=None):
    """Surface of revolution about Z from (radius, z) points. Walking the profile with the solid on the left (up the
    outside) gives outward normals; flip=True reverses them (an inside surface). xf: optional 4x4 Matrix applied."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r < 1e-6:
            rings.append(bm.verts.new((0, 0, z)))
            continue
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / segments), r * math.sin(2 * math.pi * i / segments), z))
                      for i in range(segments)])
    for a, b in zip(rings[:-1], rings[1:]):
        if isinstance(a, list) and isinstance(b, list):
            for i in range(segments):
                j = (i + 1) % segments
                f = (a[i], a[j], b[j], b[i])
                bm.faces.new(f[::-1] if flip else f)
        elif isinstance(a, list):
            for i in range(segments):
                f = (a[i], a[(i + 1) % segments], b)
                bm.faces.new(f[::-1] if flip else f)
        elif isinstance(b, list):
            for i in range(segments):
                f = (a, b[(i + 1) % segments], b[i])
                bm.faces.new(f[::-1] if flip else f)
    if cap_bottom and isinstance(rings[0], list):
        f = list(reversed(rings[0]))
        bm.faces.new(f[::-1] if flip else f)
    if cap_top and isinstance(rings[-1], list):
        f = rings[-1]
        bm.faces.new(f[::-1] if flip else f)
    if xf is not None:
        bmesh.ops.transform(bm, matrix=xf, verts=bm.verts)
    return new_obj(name, bm, [material])


def loft(name, rings, material, cap0=True, cap1=True, recalc=False):
    """Join consecutive rings (lists of points, all the same length, counter-clockwise about the direction of travel)
    into a tube. A ring may be a single point (a pole)."""
    bm = bmesh.new()
    R = []
    for ring in rings:
        if isinstance(ring, Vector):
            R.append(bm.verts.new(ring))
        else:
            R.append([bm.verts.new(p) for p in ring])
    for a, b in zip(R[:-1], R[1:]):
        if isinstance(a, list) and isinstance(b, list):
            n = len(a)
            for i in range(n):
                j = (i + 1) % n
                bm.faces.new((a[i], a[j], b[j], b[i]))
        elif isinstance(a, list):
            for i in range(len(a)):
                bm.faces.new((a[i], a[(i + 1) % len(a)], b))
        else:
            for i in range(len(b)):
                bm.faces.new((a, b[(i + 1) % len(b)], b[i]))
    if cap0 and isinstance(R[0], list):
        bm.faces.new(list(reversed(R[0])))
    if cap1 and isinstance(R[-1], list):
        bm.faces.new(R[-1])
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj(name, bm, [material])


def catmull(pts, n=8):
    """Catmull-Rom through tuples of any length (positions plus extra channels), n samples per span."""
    P = [list(p) for p in pts]
    m = len(P)
    out = []
    for i in range(m - 1):
        p1, p2 = P[i], P[i + 1]
        p0 = P[i - 1] if i > 0 else [2 * a - b for a, b in zip(p1, p2)]
        p3 = P[i + 2] if i + 2 < m else [2 * a - b for a, b in zip(p2, p1)]
        for k in range(n):
            t = k / n
            t2, t3 = t * t, t * t * t
            out.append([0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3)
                        for a, b, c, d in zip(p0, p1, p2, p3)])
    out.append(P[-1][:])
    return out


def tangents(pts):
    T = []
    n = len(pts)
    for i in range(n):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, n - 1)]
        T.append((b - a).normalized())
    return T


def tube(name, pts, radius, material, sides=8, cap0=True, cap1=True, up=None):
    """Sweep a circle along a polyline with parallel-transport frames. radius: float or per-point list."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    rad = list(radius) if isinstance(radius, (list, tuple)) else [radius] * n
    T = tangents(pts)
    ref = Vector(up) if up else (Vector((0, 0, 1)) if abs(T[0].z) < 0.9 else Vector((1, 0, 0)))
    N = (ref - T[0] * ref.dot(T[0])).normalized()
    rings = []
    for i in range(n):
        if i > 0:
            axis = T[i - 1].cross(T[i])
            if axis.length > 1e-9:
                N = Matrix.Rotation(T[i - 1].angle(T[i]), 3, axis.normalized()) @ N
            N = (N - T[i] * N.dot(T[i])).normalized()
        B = T[i].cross(N)
        rings.append([pts[i] + (N * math.cos(2 * math.pi * k / sides) + B * math.sin(2 * math.pi * k / sides)) * rad[i]
                      for k in range(sides)])
    return loft(name, rings, material, cap0, cap1)


def rbox(name, size, center, material, bevel=0.0, segs=2, taper=None):
    """Box of size (x, y, z) about center, optional bevelled edges."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges), offset=bevel, offset_type="OFFSET", segments=segs,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
    for v in bm.verts:
        v.co += Vector(center)
    return new_obj(name, bm, [material])


def sphere(name, center, radii, material, u=16, v=10, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    for vt in bm.verts:
        p = Vector((vt.co.x * radii[0], vt.co.y * radii[1], vt.co.z * radii[2]))
        if rot is not None:
            p = rot @ p
        vt.co = p + Vector(center)
    return new_obj(name, bm, [material])


def join(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob


def finish(parts, name, sharp_deg=None, merge=0.00003, weighted=True):
    """Join, merge doubles, triangulate, smooth shade (with optional sharp edges by angle), weighted normals."""
    ob = join(parts, name)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = True
    if sharp_deg is not None:
        lim = math.radians(sharp_deg)
        for e in bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > lim:
                e.smooth = False
    bm.to_mesh(ob.data)
    bm.free()
    if weighted:
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        m = ob.modifiers.new("Weighted", "WEIGHTED_NORMAL")
        m.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=m.name)
    return ob


def rot_to(direction):
    """3x3 rotation taking +Z onto direction."""
    return Vector((0, 0, 1)).rotation_difference(Vector(direction).normalized()).to_matrix()


def M4(rot3=None, loc=(0, 0, 0)):
    m = rot3.to_4x4() if rot3 is not None else Matrix.Identity(4)
    m.translation = Vector(loc)
    return m


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, object_types={"MESH"},
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)


def preview(obj, path, view=(0.75, -1.0, 0.6), res=640, margin=1.12):
    """Workbench render with the camera framed so the object fills the image."""
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.world.color = (0.30, 0.31, 0.33)
    scene.render.resolution_x = scene.render.resolution_y = res
    cam = bpy.data.objects.get("Cam")
    if cam is None:
        cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
        bpy.context.collection.objects.link(cam)
    verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = Vector([min(v[i] for v in verts) for i in range(3)])
    hi = Vector([max(v[i] for v in verts) for i in range(3)])
    c = (lo + hi) / 2
    radius = max((v - c).length for v in verts)
    d = Vector(view).normalized()
    cam.location = c + d * radius * 3.2
    q = (c - cam.location).to_track_quat("-Z", "Y")
    cam.rotation_euler = q.to_euler()
    qi = q.inverted()
    us, vs = [], []
    for v in verts:
        p = qi @ (v - cam.location)
        us.append(p.x / -p.z)
        vs.append(p.y / -p.z)
    cd = cam.data
    cd.sensor_fit = "AUTO"
    cd.sensor_width = 36.0
    cd.clip_start = radius * 0.2
    cd.clip_end = radius * 10
    span = max(max(us) - min(us), max(vs) - min(vs)) * margin
    cd.lens = cd.sensor_width / span
    cd.shift_x = (max(us) + min(us)) / 2 * cd.lens / cd.sensor_width
    cd.shift_y = (max(vs) + min(vs)) / 2 * cd.lens / cd.sensor_width
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def reset():
    for o in list(bpy.data.objects):
        if o.name != "Cam":
            bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)


# ------------------------------------------------------------------------------------------------------------ models
def chalk():
    white = mat("ChalkWhite", (0.93, 0.92, 0.88), 0.95)
    R, L, seg = 0.008, 0.090, 20
    st = [(0.0, 0.0), (0.0, 0.78), (0.0005, 0.92), (0.0016, 0.985), (0.003, 1.0)]
    n = 12
    for i in range(1, n + 1):
        st.append((0.003 + (L - 0.020 - 0.003) * i / n, 1.0))
    st += [(L - 0.0155, 0.985), (L - 0.0110, 0.94), (L - 0.0072, 0.86), (L - 0.0044, 0.75), (L - 0.0023, 0.59),
           (L - 0.0009, 0.38), (L - 0.0002, 0.18), (L, 0.0)]
    rings = []
    for t, f in st:
        if f == 0.0:
            rings.append(Vector((0, -t, 0)))
            continue
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            ca, sa = math.cos(a), math.sin(a)
            wob = 1 + 0.035 * noise.noise(Vector((ca * 1.7, sa * 1.7, t * 55))) + 0.02 * math.cos(3 * a + 0.7)
            r = R * f * wob
            x, z = r * ca, r * sa
            # worn drawing face: shear the underside of the tip back, flattening it into an angled facet
            ramp = max(0.0, (t - (L - 0.026)) / 0.026)
            tt = t - 0.60 * (R - z) * ramp ** 1.5
            # slight dents along the stick
            ring.append(Vector((x, -tt, z)))
        rings.append(ring)
    # the tip pole sits on the sheared (worn) end too
    rings[-1] = Vector((0, -(L - 0.60 * R), 0.0))
    # orientation: rings go along -Y, ccw about -Y needs angle from +X toward -Z... flip via recalc (closed mesh)
    ob = loft("Chalk", rings, white, recalc=True)
    ob = finish([ob], "Chalk")
    REPORT["Chalk"] = {"tip (Unity)": U((0, -(L - 0.60 * R), 0)), "butt (Unity)": (0.0, 0.0, 0.0)}
    return ob


def tape_roll():
    blue = mat("TapeBlue", (0.12, 0.38, 0.78), 0.85)
    core = mat("TapeCore", (0.50, 0.36, 0.22), 0.9)
    edge = mat("TapeEdge", (0.085, 0.29, 0.62), 0.8)
    W, Ri, Rc, Ro = 0.048, 0.038, 0.042, 0.0525
    h = W / 2
    xf = M4(Matrix.Rotation(math.pi / 2, 3, "Y"), (0, 0, Ro))      # lathe Z axis -> X axis, centre at height Ro
    seg = 64
    parts = [
        lathe("Outer", [(Ro - 0.0007, -h), (Ro, -h + 0.0007), (Ro, h - 0.0007), (Ro - 0.0007, h)], blue, seg, False, False, xf=xf),
        lathe("SideTop", [(Ro - 0.0007, h), (Rc, h)], edge, seg, False, False, xf=xf),
        lathe("SideBot", [(Rc, -h), (Ro - 0.0007, -h)], edge, seg, False, False, xf=xf),
        lathe("CoreTop", [(Rc, h), (Ri, h)], core, seg, False, False, xf=xf),
        lathe("CoreBot", [(Ri, -h), (Rc, -h)], core, seg, False, False, xf=xf),
        lathe("CoreIn", [(Ri, h), (Ri, -h)], core, seg, False, False, xf=xf),
    ]
    # Free tail: still stuck to the roll above the lift point, then peeling off and lying on the floor in front.
    def on_roll(deg, lift=0.0003):
        p = math.radians(deg)
        r = Ro + lift
        return (-r * math.sin(p), Ro - r * math.cos(p))
    ctrl = [on_roll(84), on_roll(76), on_roll(68), on_roll(62, 0.0006), (-0.0505, 0.0195), (-0.0535, 0.0100),
            (-0.0590, 0.0034), (-0.0665, 0.0009), (-0.0745, 0.0008), (-0.0790, 0.0016)]
    path = [Vector((0, y, z)) for y, z in catmull(ctrl, 6)]
    T = tangents(path)
    th, hw = 0.00025, W / 2 - 0.0006
    bm = bmesh.new()
    secs = []
    for p, t in zip(path, T):
        nrm = Vector((0, t.z, -t.y))
        o, i_ = p + nrm * th, p - nrm * th
        secs.append([bm.verts.new((-hw, o.y, o.z)), bm.verts.new((hw, o.y, o.z)),
                     bm.verts.new((hw, i_.y, i_.z)), bm.verts.new((-hw, i_.y, i_.z))])
    for a, b in zip(secs[:-1], secs[1:]):
        for k in range(4):
            j = (k + 1) % 4
            f = bm.faces.new((a[k], a[j], b[j], b[k]))
            f.material_index = 0 if k in (0, 2) else 1
    bm.faces.new(list(reversed(secs[0]))).material_index = 1
    bm.faces.new(secs[-1]).material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    parts.append(new_obj("Tail", bm, [blue, edge]))
    ob = finish(parts, "TapeRoll", sharp_deg=55)
    REPORT["TapeRoll"] = {"roll centre (Unity)": U((0, 0, Ro)), "roll axis (Unity)": (1, 0, 0),
                          "tail end (Unity)": U(path[-1])}
    return ob


def desk_lamp():
    body = mat("LampBody", (0.055, 0.16, 0.095), 0.42, 0.35)
    inner = mat("LampShadeInner", (0.78, 0.75, 0.70), 0.6)
    bulb = mat("LampBulb", (1.0, 0.95, 0.82), 0.3)
    switch = mat("LampSwitch", (0.86, 0.83, 0.74), 0.4)
    cord = mat("CordBlack", (0.03, 0.03, 0.03), 0.55)
    parts = []
    # weighted round base
    parts.append(lathe("Base", [(0.0, 0.0), (0.071, 0.0), (0.075, 0.0025), (0.075, 0.011), (0.0725, 0.0165), (0.063, 0.0225),
                                (0.046, 0.0275), (0.026, 0.0305), (0.0, 0.0315)], body, 36))
    sy = 0.035                                                        # stem near the back of the base
    S = Vector((0, sy, 0))
    parts.append(lathe("Collar", [(0.0155, 0.026), (0.0145, 0.034), (0.0115, 0.039), (0.0085, 0.041), (0.0, 0.041)], body, 32, xf=M4(None, S)))
    J = Vector((0, sy, 0.165))                                        # the visible joint
    parts.append(lathe("Stem", [(0.0075, 0.038), (0.0075, J.z - 0.010)], body, 20, False, False, xf=M4(None, S)))
    xr = Matrix.Rotation(math.pi / 2, 3, "Y")
    parts.append(lathe("Joint", [(0.0, -0.016), (0.0115, -0.016), (0.0135, -0.013), (0.0135, 0.013), (0.0115, 0.016), (0.0, 0.016)], body, 24, xf=M4(xr, J)))
    parts.append(lathe("Knob", [(0.0, 0.016), (0.0085, 0.016), (0.0085, 0.022), (0.0075, 0.0235), (0.0, 0.0235)], switch, 16, xf=M4(xr, J)))
    K = Vector((0, -0.045, 0.372))                                    # arm end / shade knuckle
    arm_dir = (K - J).normalized()
    parts.append(lathe("Arm", [(0.0058, 0.0), (0.0058, (K - J).length)], body, 16, False, False, xf=M4(rot_to(arm_dir), J)))
    parts.append(lathe("Knuckle", [(0.0, -0.012), (0.0085, -0.012), (0.0100, -0.010), (0.0100, 0.010), (0.0085, 0.012), (0.0, 0.012)], body, 20, xf=M4(xr, K)))
    # shade: opening faces front and down
    down = math.radians(40)
    d = Vector((0, -math.cos(down), -math.sin(down)))
    O = K + d * 0.030
    R3 = rot_to(d)
    XS = M4(R3, O)
    outer = [(0.0, -0.0175), (0.010, -0.0160), (0.0175, -0.0120), (0.0235, -0.0055), (0.0265, 0.002), (0.0335, 0.024),
             (0.0440, 0.050), (0.0550, 0.078), (0.0630, 0.100), (0.0680, 0.1150), (0.0700, 0.1185), (0.0688, 0.1200)]
    innr = [(0.0, -0.0160), (0.0095, -0.0148), (0.0165, -0.0108), (0.0222, -0.0045), (0.0252, 0.003), (0.0322, 0.025),
            (0.0427, 0.051), (0.0537, 0.079), (0.0617, 0.101), (0.0668, 0.1162), (0.0688, 0.1200)]
    parts.append(lathe("ShadeOut", outer, body, 32, False, False, xf=XS))
    parts.append(lathe("ShadeIn", innr, inner, 32, False, False, flip=True, xf=XS))
    parts.append(lathe("Neck", [(0.0085, -0.034), (0.0085, -0.014)], body, 16, False, False, xf=XS))
    # socket and bulb
    parts.append(lathe("Socket", [(0.0, -0.014), (0.0135, -0.014), (0.0135, 0.004), (0.0120, 0.009), (0.0, 0.009)], body, 24, xf=XS))
    bulb_prof = [(0.0, 0.006), (0.0108, 0.006), (0.0108, 0.011), (0.0130, 0.017), (0.0195, 0.027), (0.0238, 0.037), (0.0255, 0.046),
                 (0.0243, 0.055), (0.0198, 0.063), (0.0120, 0.0685), (0.0, 0.0705)]
    parts.append(lathe("Bulb", bulb_prof, bulb, 20, xf=XS))
    bulb_c = O + d * 0.046
    # push-button switch on the base, front right
    P = Vector((0.036, -0.036, 0.0))
    parts.append(lathe("SwHouse", [(0.0, 0.018), (0.0095, 0.018), (0.0095, 0.0295), (0.0085, 0.0315), (0.0, 0.0315)], body, 20, xf=M4(None, P)))
    parts.append(lathe("SwButton", [(0.0, 0.030), (0.0062, 0.030), (0.0062, 0.0355), (0.0052, 0.0370), (0.0, 0.0372)], switch, 16, xf=M4(None, P)))
    # cord stub out of the back of the base, settling onto the desk
    cr = 0.0028
    ctrl = [(0, 0.066, 0.0075), (0, 0.078, 0.0072), (0, 0.092, 0.0058), (0.003, 0.106, 0.0036), (0.007, 0.120, cr + 0.0001)]
    cpath = [Vector(p) for p in catmull(ctrl, 4)]
    parts.append(tube("Cord", cpath, cr, cord, sides=10, cap0=True, cap1=True))
    parts.append(lathe("Grommet", [(0.0, 0.0), (0.0048, 0.0), (0.0048, 0.006), (0.0036, 0.0075), (0.0, 0.0075)], cord, 16,
                       xf=M4(Matrix.Rotation(-math.pi / 2, 3, "X"), (0, 0.069, 0.0075))))
    ob = finish(parts, "DeskLamp", sharp_deg=50)
    end_dir = (cpath[-1] - cpath[-2]).normalized()
    REPORT["DeskLamp"] = {"bulb centre": U(bulb_c), "shade opening dir": U(d), "shade rim centre": U(O + d * 0.12),
                          "cord stub end": U(cpath[-1]), "cord end dir": U(end_dir), "cord radius": cr}
    return ob


def plug():
    body = mat("PlugBody", (0.035, 0.035, 0.035), 0.5)
    metal = mat("PlugMetal", (0.80, 0.68, 0.42), 0.3, 1.0)
    parts = []
    # moulded body: 35 (X) x 30 (Z) x 20 (Y) behind the front face at y = 0, tapering a little toward the back
    b = rbox("Body", (0.035, 0.020, 0.030), (0, 0.010, 0), body, bevel=0.0035, segs=3)
    for v in b.data.vertices:
        k = max(0.0, (v.co.y - 0.004) / 0.016)
        s = 1 - 0.14 * k
        v.co.x *= s
        v.co.z *= s
    parts.append(b)
    # finger grips: shallow ridges on both sides
    for sx in (-1, 1):
        for i in range(3):
            y = 0.008 + i * 0.0035
            s = 1 - 0.14 * max(0.0, (y - 0.004) / 0.016)
            parts.append(rbox("Grip", (0.0012, 0.0016, 0.020 * s), (sx * (0.0175 * s + 0.0001), y, 0), body, bevel=0.0005, segs=1))
    # strain relief stub
    xr = Matrix.Rotation(-math.pi / 2, 3, "X")          # lathe Z -> +Y
    prof = [(0.0090, 0.0175), (0.0086, 0.022), (0.0074, 0.027), (0.0066, 0.030), (0.0066, 0.0318), (0.0060, 0.0324),
            (0.0060, 0.0350), (0.0055, 0.0356), (0.0055, 0.0384), (0.0050, 0.0390), (0.0050, 0.0420), (0.0046, 0.0440),
            (0.0045, 0.0460)]
    parts.append(lathe("Relief", prof, body, 20, False, True, xf=M4(xr)))
    # two flat blades (6.3 x 1.5 x 15.9 mm, 12.7 mm apart) and the round ground pin below them
    for sx in (-1, 1):
        bl = rbox("Blade", (0.0015, 0.0179, 0.0063), (sx * 0.00635, -0.0159 / 2 + 0.001, 0.0045), metal, bevel=0.0003, segs=1)
        for v in bl.data.vertices:          # chamfered tips
            if v.co.y < -0.0150:
                v.co.z = 0.0045 + (v.co.z - 0.0045) * 0.75
        parts.append(bl)
    parts.append(lathe("Ground", [(0.0024, -0.002), (0.0024, 0.0165), (0.0019, 0.0182), (0.0010, 0.0189), (0.0, 0.0191)], metal, 16,
                       True, False, xf=M4(Matrix.Rotation(math.pi / 2, 3, "X"), (0, 0, -0.0075))))
    ob = finish(parts, "Plug", sharp_deg=50)
    REPORT["Plug"] = {"cord leaves back": U((0, 0.046, 0)), "cord dir": U((0, 1, 0)), "stub end radius": 0.0045,
                      "prong dir": U((0, -1, 0)), "blades at": [U((-0.00635, 0, 0.0045)), U((0.00635, 0, 0.0045))],
                      "ground pin at": U((0, 0, -0.0075)), "prong length": 0.0159}
    return ob


def cord_socket():
    orange = mat("CordOrange", (0.95, 0.36, 0.03), 0.5)
    face = mat("SocketFace", (1.0, 0.50, 0.12), 0.45)
    slot = mat("SocketSlot", (0.015, 0.015, 0.015), 0.7)
    parts = []
    LX, LY, H = 0.045, 0.075, 0.035
    b = rbox("Box", (LX, LY, H), (0, 0, H / 2), orange, bevel=0.0055, segs=3)
    for v in b.data.vertices:              # taper the back toward the cord
        k = max(0.0, (v.co.y - 0.024) / (LY / 2 - 0.024))
        v.co.x *= 1 - 0.20 * k
        v.co.z *= 1 - 0.22 * k
    parts.append(b)
    zc = H * (1 - 0.22) / 2                # centre height of the tapered back face
    xr = Matrix.Rotation(-math.pi / 2, 3, "X")
    prof = [(0.0105, LY / 2 - 0.004), (0.0098, LY / 2 + 0.003), (0.0082, LY / 2 + 0.008), (0.0072, LY / 2 + 0.010),
            (0.0072, LY / 2 + 0.0118), (0.0066, LY / 2 + 0.0124), (0.0066, LY / 2 + 0.0150), (0.0060, LY / 2 + 0.0156),
            (0.0060, LY / 2 + 0.0185), (0.0054, LY / 2 + 0.0200), (0.0050, LY / 2 + 0.0225)]
    parts.append(lathe("Relief", prof, orange, 20, False, True, xf=M4(xr, (0, 0, zc))))
    cord_end = Vector((0, LY / 2 + 0.0225, zc))
    # three sockets on the top, in a row front to back; each lies the way a plug's face does (slots toward the front)
    pitch = 0.0225
    centres = [Vector((0, -pitch * 1.15 + i * pitch, H)) for i in range(3)]
    for c in centres:
        parts.append(rbox("Face", (0.031, 0.0205, 0.0016), (c.x, c.y, H + 0.0004), face, bevel=0.0006, segs=1))
        top = H + 0.0012
        for sx in (-1, 1):
            parts.append(rbox("Slot", (0.0020, 0.0072, 0.0004), (sx * 0.00635, c.y - 0.0045, top), slot))
        g = lathe("GHole", [(0.0, top - 0.0002), (0.0026, top - 0.0002), (0.0026, top + 0.0002), (0.0, top + 0.0002)], slot, 12,
                  xf=M4(None, (0, c.y + 0.0075, 0)))
        parts.append(g)
    ob = finish(parts, "CordSocket", sharp_deg=50)
    c0 = centres[0]
    REPORT["CordSocket"] = {
        "socket (front one) face centre": U((0, c0.y, H + 0.0012)), "socket facing": U((0, 0, 1)),
        "other socket centres": [U((0, c.y, H + 0.0012)) for c in centres[1:]],
        "plug orientation when inserted": "plug forward (prongs, Unity +Z local) = world Unity (0,-1,0); plug up (Unity +Y local, toward blades) = world Unity (0,0,1)",
        "cord entry (strain relief end)": U(cord_end), "cord dir out": U((0, 1, 0)), "relief end radius": 0.005}
    return ob


def cord_coil():
    orange = mat("CordOrange", (0.95, 0.36, 0.03), 0.5)
    cr = 0.0045
    loops = 8
    per = 30
    pts = []
    total = loops * per
    for i in range(total + 1):
        th = 2 * math.pi * i / per
        u = i / total
        wob = 3.0 * noise.noise(Vector((th * 0.08, 3.3, 0.0)))        # each loop squashed a different way
        r = 0.121 + 0.015 * noise.noise(Vector((th * 0.23, 1.3, 0.4))) + 0.010 * math.sin(2 * th + wob) \
            + 0.006 * noise.noise(Vector((th * 0.9, 4.1, 2.2)))
        cx = 0.020 * noise.noise(Vector((th * 0.12, 7.7, 0.2)))
        cy = 0.020 * noise.noise(Vector((th * 0.12, 2.9, 5.1)))
        z = cr + 0.0034 * (th / (2 * math.pi)) + 0.0025 * max(0.0, noise.noise(Vector((th * 0.6, 9.1, 3.3))))
        # first and last half-loop settle back onto the floor
        z = cr + (z - cr) * min(1.0, i / (per * 0.5))
        pts.append(Vector((cx + r * math.cos(th), cy + r * math.sin(th), z)))
    # lead-in (start end) and tail (finish end), both on the +X side, sticking out about 5 cm
    p0, p1 = pts[0], pts[1]
    t0 = (p1 - p0).normalized()
    out0 = Vector((p0.x, p0.y, 0)).normalized()
    start = [p0 - t0 * 0.055 + out0 * 0.018, p0 - t0 * 0.035 + out0 * 0.009, p0 - t0 * 0.016 + out0 * 0.002]
    for s in start:
        s.z = cr
    pe, pe1 = pts[-1], pts[-2]
    te = (pe - pe1).normalized()
    oute = Vector((pe.x, pe.y, 0)).normalized()
    finish_pts = [pe + te * 0.018 + oute * 0.003, pe + te * 0.036 + oute * 0.011, pe + te * 0.056 + oute * 0.022]
    zs = [pe.z * 0.6 + cr * 0.4, cr + 0.0006, cr]
    for f, z in zip(finish_pts, zs):
        f.z = z
    path = start + pts + finish_pts
    # light smoothing
    for _ in range(2):
        path = [path[0]] + [(path[i - 1] + path[i] * 2 + path[i + 1]) / 4 for i in range(1, len(path) - 1)] + [path[-1]]
    ob = tube("CordCoil", path, cr, orange, sides=6)
    ob = finish([ob], "CordCoil", sharp_deg=80)
    # re-centre to the bottom centre of the footprint
    vs = ob.data.vertices
    lo = Vector([min(v.co[i] for v in vs) for i in range(3)])
    hi = Vector([max(v.co[i] for v in vs) for i in range(3)])
    off = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    ob.data.transform(Matrix.Translation(-off))
    a, b = path[0] - off, path[-1] - off
    da = (path[0] - path[1]).normalized()
    db = (path[-1] - path[-2]).normalized()
    REPORT["CordCoil"] = {"end A (bottom loop) point": U(a), "end A outward dir": U(da),
                          "end B (top loop) point": U(b), "end B outward dir": U(db), "cord radius": cr}
    return ob


def rubber_chicken():
    yellow = mat("ChickenYellow", (0.95, 0.80, 0.10), 0.45)
    red = mat("ChickenRed", (0.80, 0.06, 0.04), 0.45)
    orange = mat("ChickenOrange", (0.98, 0.45, 0.05), 0.45)
    eye = mat("ChickenEye", (0.01, 0.01, 0.01), 0.2)
    S = 0.9
    # spine: (y, z, half-width, half-height), tail (+Y) to the front of the head (-Y)
    spine = [(0.150, 0.046, 0.003, 0.003), (0.138, 0.034, 0.012, 0.012), (0.120, 0.022, 0.024, 0.024), (0.095, 0.012, 0.036, 0.036),
             (0.060, 0.004, 0.045, 0.044), (0.020, 0.0, 0.049, 0.047), (-0.020, 0.0, 0.048, 0.047), (-0.055, 0.004, 0.042, 0.043),
             (-0.082, 0.012, 0.030, 0.031), (-0.100, 0.016, 0.017, 0.018), (-0.118, 0.016, 0.0125, 0.0130),
             (-0.150, 0.012, 0.0115, 0.0120), (-0.185, 0.004, 0.0110, 0.0115), (-0.215, 0.000, 0.0110, 0.0115),
             (-0.240, 0.004, 0.0115, 0.0120), (-0.258, 0.010, 0.0135, 0.0140), (-0.270, 0.016, 0.0175, 0.0180),
             (-0.284, 0.020, 0.0205, 0.0210), (-0.300, 0.021, 0.0210, 0.0210), (-0.315, 0.018, 0.0180, 0.0170),
             (-0.326, 0.014, 0.0120, 0.0110), (-0.331, 0.012, 0.004, 0.004)]
    spine = [(y * S, z, w, h) for y, z, w, h in spine]
    samp = catmull([(0.0, y, z, w, h) for y, z, w, h in spine], 3)
    P = [Vector((s[0], s[1], s[2])) for s in samp]
    T = tangents(P)
    sides = 14
    rings = [P[0]]
    for i in range(1, len(samp) - 1):
        y, z, w, h = samp[i][1], samp[i][2], samp[i][3], samp[i][4]
        t = T[i]
        sv = Vector((1, 0, 0))
        sv = (sv - t * sv.dot(t)).normalized()
        uv = t.cross(sv)
        # wrinkly neck
        wr = 1.0
        if -0.245 * S < y < -0.105 * S:
            wr = 1 + 0.10 * math.sin(y / (0.0075 * S) * 2.0)
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            bump = 1 + 0.03 * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, y * 30)))
            ring.append(P[i] + sv * (w * wr * bump * math.cos(a)) + uv * (h * wr * bump * math.sin(a)))
        rings.append(ring)
    rings.append(P[-1])
    body = loft("Body", rings, yellow, recalc=True)
    vg = body.vertex_groups.new(name="Body")
    vg.add(list(range(len(body.data.vertices))), 1.0, "REPLACE")
    parts = [body]

    # head reference: the front of the head where the beak hinges
    hy = -0.322 * S
    H = Vector((0, hy, 0.015))
    head_c = Vector((0, -0.294 * S, 0.021))

    def mandible(name, base_c, base_w, base_h, direction, length, hook, group):
        dvec = Vector(direction).normalized()
        sv = Vector((1, 0, 0))
        uv = dvec.cross(sv)
        rings = []
        n = 6
        for i in range(n):
            f = i / n
            c = base_c + dvec * (length * f) + uv * (hook * f * f * length)
            w = base_w * (1 - f) ** 0.6
            hh = base_h * (1 - f) ** 0.7
            rings.append([c + sv * (w * math.cos(2 * math.pi * k / 12)) + uv * (hh * math.sin(2 * math.pi * k / 12)) for k in range(12)])
        rings.append(base_c + dvec * length + uv * (hook * length))
        o = loft(name, rings, orange, cap0=True, cap1=False, recalc=True)
        g = o.vertex_groups.new(name=group)
        g.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
        return o

    up_a, lo_a = math.radians(-4), math.radians(-50)
    parts.append(mandible("BeakUp", H + Vector((0, 0.006, 0.0050)), 0.0125, 0.0100,
                          (0, -math.cos(up_a), math.sin(up_a)), 0.040, -0.16, "Upper"))
    parts.append(mandible("BeakLo", H + Vector((0, 0.006, -0.0050)), 0.0112, 0.0075,
                          (0, -math.cos(lo_a), math.sin(lo_a)), 0.032, 0.10, "Lower"))
    # dark red mouth inside the open beak
    m_in = sphere("Mouth", H + Vector((0, -0.004, -0.003)), (0.0090, 0.0070, 0.0075), red, 10, 6)
    g = m_in.vertex_groups.new(name="Lower")
    g.add(list(range(len(m_in.data.vertices))), 0.5, "REPLACE")
    parts.append(m_in)
    # comb: a row of lobes along the top of the head
    for i, (dy, rz, rr) in enumerate([(0.020, 0.010, 0.0065), (0.008, 0.015, 0.0085), (-0.004, 0.016, 0.0090), (-0.016, 0.013, 0.0075), (-0.026, 0.008, 0.0055)]):
        c = Vector((0, head_c.y + dy * S, head_c.z + 0.016 + rz * 0.35))
        parts.append(sphere("Comb", c, (0.0032, rr, rr * 1.15), red, 10, 7))
    # wattles under the beak
    for sx in (-1, 1):
        parts.append(sphere("Wattle", H + Vector((sx * 0.0045, 0.006, -0.019)), (0.0042, 0.0055, 0.0095), red, 10, 6,
                            rot=Matrix.Rotation(math.radians(-15), 3, "X")))
    # eyes
    for sx in (-1, 1):
        parts.append(sphere("Eye", head_c + Vector((sx * 0.0170, -0.009, 0.007)), (0.0056, 0.0056, 0.0056), eye, 10, 6))
    # thin, droopy wings
    for sx in (-1, 1):
        rot = Matrix.Rotation(math.radians(18 * sx), 3, "Z") @ Matrix.Rotation(math.radians(-14), 3, "X") @ Matrix.Rotation(math.radians(-8 * sx), 3, "Y")
        parts.append(sphere("Wing", (sx * 0.043, 0.020, 0.012), (0.0065, 0.052, 0.026), yellow, 12, 8, rot=rot))
    # scrawny dangling legs with feet
    for sx in (-1, 1):
        ctrl = [(sx * 0.020, 0.040, -0.022), (sx * 0.024, 0.046, -0.050), (sx * 0.026, 0.056, -0.088), (sx * 0.027, 0.060, -0.112),
                (sx * 0.028, 0.052, -0.140), (sx * 0.029, 0.046, -0.158)]
        lp = [Vector(p) for p in catmull(ctrl, 4)]
        n = len(lp)
        rad = []
        for i in range(n):
            f = i / (n - 1)
            rad.append(0.0150 * (1 - f) ** 3 + 0.0052 + 0.0008 * (1 - f))
        parts.append(tube("Leg", lp, rad, orange, sides=8, cap0=True, cap1=True))
        ank = lp[-1]
        for ang, ln in ((-38, 0.046), (0, 0.054), (38, 0.046), (180, 0.020)):
            a = math.radians(ang)
            fwd = Vector((math.sin(a) * sx * (1 if ang != 180 else 0), -math.cos(a), 0))
            dirn = (fwd * 0.75 + Vector((0, 0, -0.65))).normalized() if ang != 180 else Vector((0, 0.7, -0.6)).normalized()
            tp = [ank, ank + dirn * ln * 0.5 + Vector((0, 0, -0.002)), ank + dirn * ln + Vector((0, -0.002 if ang != 180 else 0.002, 0.003))]
            tp = [Vector(p) for p in catmull(tp, 2)]
            parts.append(tube("Toe", tp, [0.0042, 0.0036, 0.0031, 0.0027, 0.0020], orange, sides=6))
    ob = finish(parts, "RubberChicken", sharp_deg=None, merge=0.00002)

    # ---- the Squeeze shape key (modifiers are already applied: finish() leaves none)
    assert len(ob.modifiers) == 0
    me = ob.data
    gi = {g.name: g.index for g in ob.vertex_groups}

    def wt(v, name):
        for ge in v.groups:
            if ge.group == gi[name]:
                return ge.weight
        return 0.0

    basis = ob.shape_key_add(name="Basis", from_mix=False)
    sq = ob.shape_key_add(name="Squeeze", from_mix=False)
    yc = 0.012
    hinge = H + Vector((0, 0.004, 0))
    ro_lo = Matrix.Rotation(math.radians(22), 3, "X")       # lower mandible tip swings down
    ro_up = Matrix.Rotation(math.radians(-7), 3, "X")       # upper tip lifts a little
    for v in me.vertices:
        p = v.co.copy()
        lo_w, up_w = wt(v, "Lower"), wt(v, "Upper")
        if lo_w > 0:
            p = hinge + (ro_lo @ (p - hinge)) * lo_w + (p - hinge) * (1 - lo_w)
        elif up_w > 0:
            p = hinge + ro_up @ (p - hinge)
        else:
            # body squash: weight 1 over the body, fading out up the neck and at the tail tip
            y = p.y
            if y >= yc:
                w = max(0.0, min(1.0, (0.150 * S - y) / 0.05))
            else:
                w = max(0.0, min(1.0, (y - (-0.115 * S)) / 0.06))
            w = w * w * (3 - 2 * w)
            p = Vector((p.x * (1 - 0.30 * w), yc + (p.y - yc) * (1 + 0.09 * w), p.z * (1 + 0.10 * w) if p.z > -0.05 else p.z + 0.004 * w))
        sq.data[v.index].co = p
    ob.data.shape_keys.use_relative = True
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    REPORT["RubberChicken"] = {"origin": "body centre (grip point)", "head/beak dir": U((0, -1, 0)),
                               "beak tip (closed-ish)": U(H + Vector((0, -0.034, -0.004))), "shape keys": [k.name for k in ob.data.shape_keys.key_blocks]}
    return ob


def utility_shelf():
    steel = mat("ShelfMetal", (0.46, 0.48, 0.49), 0.5, 0.6)
    W, D, Ht = 0.90, 0.40, 1.80
    hx, hy = W / 2, D / 2
    t, fl = 0.0025, 0.035
    parts = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(rbox("UpA", (fl, t, Ht), (sx * (hx - fl / 2), sy * (hy - t / 2), Ht / 2), steel))
            parts.append(rbox("UpB", (t, fl, Ht), (sx * (hx - t / 2), sy * (hy - fl / 2), Ht / 2), steel))
            # small plastic-less feet plates
            parts.append(rbox("Foot", (0.040, 0.040, 0.003), (sx * (hx - 0.020), sy * (hy - 0.020), 0.0015), steel))
    shelf_z = [0.10, 0.62, 1.14, 1.70]
    lip = 0.032
    st = 0.0012
    ix, iy = hx - t, hy - t
    for z in shelf_z:
        parts.append(rbox("Deck", (2 * ix, 2 * iy, st), (0, 0, z - st / 2), steel))
        for sy in (-1, 1):
            parts.append(rbox("LipF", (2 * ix, st, lip), (0, sy * (iy - st / 2), z - lip / 2), steel))
        for sx in (-1, 1):
            parts.append(rbox("LipS", (st, 2 * iy, lip), (sx * (ix - st / 2), 0, z - lip / 2), steel))
        # bolt heads on the outside of every upright flange
        for sx in (-1, 1):
            for sy in (-1, 1):
                bz = z - lip / 2
                parts.append(lathe("Bolt", [(0.0, 0.0), (0.0052, 0.0), (0.0052, 0.0028), (0.0, 0.0034)], steel, 6,
                                   xf=M4(rot_to((0, sy, 0)), (sx * (hx - 0.016), sy * hy, bz))))
                parts.append(lathe("Bolt", [(0.0, 0.0), (0.0052, 0.0), (0.0052, 0.0028), (0.0, 0.0034)], steel, 6,
                                   xf=M4(rot_to((sx, 0, 0)), (sx * hx, sy * (hy - 0.016), bz))))
    # punched slots up the uprights (shallow dark-free dents would cost too much; a few cross-braces instead at the back)
    for sgn in (-1, 1):
        a = Vector((sgn * (hx - 0.01), hy - 0.004, 0.14))
        b = Vector((-sgn * (hx - 0.01), hy - 0.004, 0.60))
        mid = (a + b) / 2
        d = b - a
        ang = math.atan2(d.z, d.x)
        br = rbox("Brace", (d.length, 0.002, 0.016), (0, 0, 0), steel)
        br.data.transform(Matrix.Rotation(-ang, 4, "Y"))
        br.data.transform(Matrix.Translation(mid))
        parts.append(br)
    ob = finish(parts, "UtilityShelf", sharp_deg=30)
    REPORT["UtilityShelf"] = {"shelf top heights (Unity y)": shelf_z, "inner shelf area": (round(2 * ix, 4), round(2 * iy, 4))}
    return ob


def lantern():
    body = mat("LanternBody", (0.07, 0.20, 0.10), 0.55)
    globe = mat("LanternGlobe", (0.93, 0.93, 0.90), 0.6)
    metal = mat("LanternMetal", (0.70, 0.70, 0.72), 0.3, 1.0)
    parts = []
    parts.append(lathe("Base", [(0.0, 0.0), (0.052, 0.0), (0.056, 0.003), (0.057, 0.036), (0.054, 0.044), (0.049, 0.048), (0.0, 0.048)], body, 40))
    parts.append(lathe("Globe", [(0.0, 0.046), (0.0425, 0.046), (0.0445, 0.070), (0.0450, 0.103), (0.0445, 0.136), (0.0425, 0.160), (0.0, 0.160)], globe, 40))
    parts.append(lathe("Cap", [(0.0, 0.157), (0.050, 0.157), (0.054, 0.161), (0.054, 0.172), (0.049, 0.180), (0.036, 0.187), (0.016, 0.190),
                               (0.0, 0.1905)], body, 40))
    # cage bars over the globe
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        c = Vector((math.cos(a) * 0.046, math.sin(a) * 0.046, 0.103))
        b = rbox("Bar", (0.0060, 0.0050, 0.114), (0, 0, 0), body, bevel=0.0012, segs=1)
        b.data.transform(Matrix.Rotation(a, 4, "Z"))
        b.data.transform(Matrix.Translation(c))
        parts.append(b)
    # wire handle on two lugs
    for sx in (-1, 1):
        parts.append(lathe("Lug", [(0.0, 0.0), (0.0055, 0.0), (0.0055, 0.006), (0.0, 0.006)], metal, 12,
                           xf=M4(rot_to((sx, 0, 0)), (sx * 0.052, 0, 0.170))))
    hp = []
    for i in range(25):
        a = math.pi * i / 24
        hp.append(Vector((-math.cos(a) * 0.057, 0, 0.170 + math.sin(a) * 0.050)))
    parts.append(tube("Handle", hp, 0.0019, metal, sides=8))
    # switch knob on the front of the base
    parts.append(lathe("Knob", [(0.0, 0.0), (0.0085, 0.0), (0.0085, 0.005), (0.0072, 0.0065), (0.0, 0.0065)], metal, 16,
                       xf=M4(rot_to((0, -1, 0)), (0, -0.0555, 0.022))))
    parts.append(rbox("Grip", (0.003, 0.004, 0.011), (0, -0.0625, 0.022), metal, bevel=0.0008, segs=1))
    ob = finish(parts, "Lantern", sharp_deg=50)
    REPORT["Lantern"] = {"globe centre": U((0, 0, 0.103)), "globe radius": 0.045, "globe height span (Unity y)": (0.046, 0.160)}
    return ob


def sleeping_bag():
    fab = mat("BagFabric", (0.42, 0.10, 0.08), 0.85)
    lin = mat("BagLining", (0.66, 0.53, 0.36), 0.95)
    HW = 0.375
    y0, y1 = -0.95, 0.62                  # head end (front, -Y) to under the roll
    open_len = 0.22                       # the head opening, where the bottom layer's lining shows
    nx = 16
    xs = [-HW * math.cos(math.pi * i / nx) for i in range(nx + 1)]          # denser toward the puffy edges
    ys = [y0 + 0.02 * k for k in range(int(round(0.30 / 0.02)))]
    yy = y0 + 0.30
    while yy < y1 - 1e-6:
        ys.append(yy)
        yy += 0.04
    ys.append(y1)
    bm = bmesh.new()

    def rump(x, y):
        return 0.016 * noise.noise(Vector((x * 2.2, y * 1.6, 0.5))) + 0.007 * noise.noise(Vector((x * 6.0, y * 5.0, 2.5)))

    def top_h(x, y):
        u = min(1.0, abs(x) / HW)
        prof = max(0.0, 1 - u ** 2.5) ** 0.55
        d = (y - (y0 + open_len + 0.08)) % 0.24
        d = min(d, 0.24 - d)
        quilt = 1 - 0.22 * math.exp(-(d / 0.028) ** 2)
        full = 0.006 + 0.078 * prof * quilt + rump(x, y) * prof
        low = 0.006 + 0.034 * prof + 0.5 * rump(x, y) * prof
        e = min(1.0, max(0.0, (u - 0.70) / 0.20))
        low = low + (full - low) * e * e * (3 - 2 * e)        # the zipped sides stay puffy around the opening
        k = y - (y0 + open_len)
        if k < -0.015:
            return low
        if k < 0.0:
            return low + (full - low) * 0.45
        if k < 0.03:
            return full * 1.10                # rolled collar of the top layer
        return full

    def warp(x, y):
        return (x * (1 + 0.03 * noise.noise(Vector((y * 3.0, 1.0, 7.0)))),
                y + 0.012 * noise.noise(Vector((x * 3.0, y * 2.0, 4.0))))

    top = []
    for y in ys:
        row = []
        for x in xs:
            wx, wy = warp(x, y)
            row.append(bm.verts.new((wx, wy, top_h(x, y))))
        top.append(row)
    ny = len(ys) - 1
    for j in range(ny):
        yj = (ys[j] + ys[j + 1]) / 2
        for i in range(nx):
            f = bm.faces.new((top[j][i], top[j][i + 1], top[j + 1][i + 1], top[j + 1][i]))
            xi = (xs[i] + xs[i + 1]) / 2
            f.material_index = 1 if (yj < y0 + open_len - 0.012 and abs(xi) < HW * 0.80) else 0
    per = [(0, i) for i in range(nx + 1)] + [(j, nx) for j in range(1, ny + 1)] +           [(ny, i) for i in range(nx - 1, -1, -1)] + [(j, 0) for j in range(ny - 1, 0, -1)]
    bot = []
    for j, i in per:
        c = top[j][i].co
        bot.append(bm.verts.new((c.x, c.y, 0.0)))
    n = len(per)
    for k in range(n):
        a, b = per[k], per[(k + 1) % n]
        bm.faces.new((bot[k], bot[(k + 1) % n], top[b[0]][b[1]], top[a[0]][a[1]]))
    bm.faces.new(list(reversed(bot)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    parts = [new_obj("Flat", bm, [fab, lin])]

    # the rolled-up foot end: a cylinder across the width, with spiral layers showing on its ends
    Rr = 0.15
    yr = y1 + 0.02
    seg, nxs = 24, 10
    hwr = HW + 0.01

    def roll_r(a, x):
        return Rr * (1 + 0.035 * noise.noise(Vector((math.cos(a) * 1.5, math.sin(a) * 1.5, x * 4))))

    def roll_pt(a, x, r):
        z = Rr + r * math.sin(a)
        z = max(z, 0.002 + 0.25 * (z - 0.002)) if z < 0.02 else z
        return Vector((x, yr + r * math.cos(a), z))

    rings = []
    for k in range(nxs + 1):
        x = -hwr + 2 * hwr * k / nxs
        s = 1 - 0.06 * (abs(x) / hwr) ** 6
        rings.append([roll_pt(2 * math.pi * i / seg, x, roll_r(2 * math.pi * i / seg, x) * s) for i in range(seg)])
    # rings run along +X; for ccw about +X order the angle from +Y toward +Z: that is how roll_pt goes, so normals face out
    roll = loft("Roll", rings, fab, cap0=False, cap1=False)
    parts.append(roll)
    pitch = 0.034
    for sx in (-1, 1):
        x = sx * hwr
        bm = bmesh.new()
        nr = 9
        grid = []
        centre = bm.verts.new((x + sx * 0.002, yr, Rr))
        for ri in range(1, nr + 1):
            f = ri / nr
            row = []
            for i in range(seg):
                a = 2 * math.pi * i / seg
                r = roll_r(a, x) * (1 - 0.06) * f
                p = roll_pt(a, x, r)
                p.x = x + sx * 0.004 * math.sin(f * math.pi)
                row.append(bm.verts.new(p))
            grid.append(row)
        grid.insert(0, [centre] * seg)
        for ri in range(nr):
            for i in range(seg):
                j = (i + 1) % seg
                quad = (grid[ri][i], grid[ri][j], grid[ri + 1][j], grid[ri + 1][i])
                if ri == 0:
                    quad = (centre, grid[1][j], grid[1][i])
                f = bm.faces.new(quad[::-1] if sx > 0 else quad)
                rm = (ri + 0.5) / nr * Rr
                a = 2 * math.pi * (i + 0.5) / seg
                ph = ((Rr - rm) / pitch + a / (2 * math.pi)) % 1.0
                f.material_index = 1 if ph < 0.38 else 0
        parts.append(new_obj("RollEnd", bm, [fab, lin]))
    ob = finish(parts, "SleepingBag", sharp_deg=70, merge=0.0005)
    vs = ob.data.vertices
    lo = Vector([min(v.co[i] for v in vs) for i in range(3)])
    hi = Vector([max(v.co[i] for v in vs) for i in range(3)])
    off = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    ob.data.transform(Matrix.Translation(-off))
    REPORT["SleepingBag"] = {"head end (opening) toward": U((0, -1, 0)), "roll centre": U(Vector((0, yr, Rr)) - off)}
    return ob


MODELS = [
    ("Chalk", chalk, (0.9, -0.55, -0.3)),
    ("TapeRoll", tape_roll, (0.85, -1.0, 0.55)),
    ("DeskLamp", desk_lamp, (-0.55, -1.0, 0.12)),
    ("Plug", plug, (0.85, -1.0, 0.55)),
    ("CordSocket", cord_socket, (0.7, -1.0, 0.9)),
    ("CordCoil", cord_coil, (0.35, -0.7, 1.0)),
    ("RubberChicken", rubber_chicken, (0.85, -0.55, 0.75)),
    ("UtilityShelf", utility_shelf, (0.75, -1.0, 0.45)),
    ("Lantern", lantern, (0.7, -1.0, 0.45)),
    ("SleepingBag", sleeping_bag, (0.9, -1.0, 0.9)),
]

for name, fn, view in MODELS:
    if ONLY and name not in ONLY:
        continue
    reset()
    ob = fn()
    tris = len(ob.data.polygons)
    vs = ob.data.vertices
    lo = [min(v.co[i] for v in vs) for i in range(3)]
    hi = [max(v.co[i] for v in vs) for i in range(3)]
    size_u = (hi[0] - lo[0], hi[2] - lo[2], hi[1] - lo[1])
    min_u = (lo[0], lo[2], -hi[1])
    max_u = (hi[0], hi[2], -lo[1])
    path = os.path.join(OUT, name + ".fbx")
    export(ob, path)
    if PREVIEW:
        os.makedirs(PREVIEW, exist_ok=True)
        preview(ob, os.path.join(PREVIEW, name + ".png"), view)
        if name == "RubberChicken":
            sq = [k.co.copy() for k in ob.data.shape_keys.key_blocks["Squeeze"].data]
            tmp = bpy.data.objects.new("SqueezePreview", ob.data.copy())
            bpy.context.collection.objects.link(tmp)
            tmp.shape_key_clear()
            for v, c in zip(tmp.data.vertices, sq):
                v.co = c
            ob.hide_render = True
            preview(tmp, os.path.join(PREVIEW, name + "_Squeeze.png"), view)
            ob.hide_render = False
            bpy.data.objects.remove(tmp, do_unlink=True)
    mats = [m.name for m in ob.data.materials]
    print("EXPORTED", name, "tris", tris, "size_unity", tuple(round(s, 4) for s in size_u),
          "min_unity", tuple(round(s, 4) for s in min_u), "max_unity", tuple(round(s, 4) for s in max_u), "mats", mats)
    if name in REPORT:
        for k, v in REPORT[name].items():
            print("   REPORT", name, k, "=", v)
    if name == "RubberChicken":
        reset()
        bpy.ops.import_scene.fbx(filepath=path)
        for o in bpy.context.scene.objects:
            if o.type == "MESH":
                sk = o.data.shape_keys
                print("   REIMPORT", o.name, "shape keys:", [k.name for k in sk.key_blocks] if sk else None)
