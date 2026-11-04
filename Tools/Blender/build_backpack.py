"""
A school / day backpack (2026-11-04, the owner: a backpack found fairly often, set down and opened, the things inside
real things), built in Blender and baked (bake_kit.py). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_backpack.py -- Assets/Backrooms/Resources/Items [--preview <dir>] [--only Backpack_Navy]

The late-1990s two-compartment daypack's kind (about 22 L: 0.31 m wide at the foot, 0.27 at the shoulders, 0.44 tall,
0.16 deep at the foot and less toward the top): 600D polyester, a black suede-look base wrapping the bottom, piping
round the back's seam, a padded back of air mesh, S-shaped padded shoulder straps running into webbing and ladder-lock
buckles at the bottom corners, a webbing grab loop on top. The main compartment opens on a U-shaped zip round the
front panel's top and sides (two sliders meeting at the top), the front panel hinged along the foot: unzipped it flops
forward and the grey lining and whatever is inside show. On the front panel a zipped front pocket, a suede lash tab and
a rubber patch with the brand. No real brand: an invented one, NORREN.

  - Backpack_<Colour>: the shell (gusset, back, base, lining, straps, grab loop, the zip's shell half).
  - BackpackLid_<Colour>: the front panel with the pocket, the tab, the patch and the zip's other half; origin on its
    hinge along the foot of the front (it turns about Blender X).
  - BackpackPull: one of the two zip sliders with its cord pull (the game runs them along the zip); origin at the
    slider's middle, the pull hanging toward -Z.
  - Backpack_Layout.json: the hinge, the zip's path (from the right foot round the top to the left foot), the inside's
    box, the grab loop (Blender frame).

Colours: Navy, Red, Forest. Frame: origin on the floor at the middle of the foot, front toward -Y (Unity +Z), the back
(and the straps) toward +Y. Unity (x, y, z) = (-x, z, -y) for these item models (as the boombox).
"""
import os
import sys
import math
import json

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_rooms.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- break room")
exec(compile(_src[:_cut], "build_rooms_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix

# ------------------------------------------------------------------------------------------------- the shape

W0, W1 = 0.305, 0.272          # width at the foot, at the shoulders
H = 0.435                      # height of the outline (the grab loop stands above)
RB, RT = 0.05, 0.122           # corner radii: the foot's, the top's
YB = 0.08                      # the back's seam (the back panel bulges a little behind it)
D0 = 0.158                     # depth at the foot (less toward the top)
N = 176                        # points round an outline
BULGE_F, BULGE_B = 0.024, 0.012
LINING = 0.005                 # the shell's thickness (lining inside)
ZIPW = 0.006                   # the zip's half on each side of the seam


def depth(z):
    t = min(1.0, max(0.0, (z - 0.2) / 0.235))
    t = t * t * (3 - 2 * t)
    return D0 * (1.0 - 0.36 * t)


def taper(z):
    return (W0 + (W1 - W0) * min(1.0, max(0.0, z / H))) / W0


def rr_dense(w, h, rb, rt, z0, step=0.0015):
    """A rounded rectangle in X,Z (counter-clockwise seen from the front), from the foot's middle toward +X."""
    pts = []
    hw = w / 2

    def line(a, b):
        L = (b - a).length
        k = max(1, int(L / step))
        for i in range(k):
            pts.append(a + (b - a) * (i / k))

    def arc(c, r, a0, a1):
        L = abs(a1 - a0) * r
        k = max(2, int(L / step))
        for i in range(k):
            a = a0 + (a1 - a0) * i / k
            pts.append(c + Vector((math.cos(a) * r, math.sin(a) * r)))

    V2 = lambda x, z: Vector((x, z))
    line(V2(0, z0), V2(hw - rb, z0))
    arc(V2(hw - rb, z0 + rb), rb, -math.pi / 2, 0)
    line(V2(hw, z0 + rb), V2(hw, z0 + h - rt))
    arc(V2(hw - rt, z0 + h - rt), rt, 0, math.pi / 2)
    line(V2(hw - rt, z0 + h), V2(-hw + rt, z0 + h))
    arc(V2(-hw + rt, z0 + h - rt), rt, math.pi / 2, math.pi)
    line(V2(-hw, z0 + h - rt), V2(-hw, z0 + rb))
    arc(V2(-hw + rb, z0 + rb), rb, math.pi, 1.5 * math.pi)
    line(V2(-hw + rb, z0), V2(0, z0))
    return pts


def resample(dense, n):
    L = [0.0]
    for a, b in zip(dense, dense[1:] + dense[:1]):
        L.append(L[-1] + (b - a).length)
    total = L[-1]
    out, j = [], 0
    for i in range(n):
        s = total * i / n
        while L[j + 1] < s:
            j += 1
        a, b = dense[j], dense[(j + 1) % len(dense)]
        t = (s - L[j]) / max(1e-9, L[j + 1] - L[j])
        out.append(a + (b - a) * t)
    return out, total


def outline(inset=0.0, w=W0, h=H, rb=RB, rt=RT, z0=0.0, n=N, tap=True):
    """n points (x, z) round the outline, inset inward by inset; tapered toward the top."""
    d = rr_dense(w - 2 * inset, h - 2 * inset, max(0.004, rb - inset), max(0.004, rt - inset), z0 + inset)
    pts, total = resample(d, n)
    if tap:
        pts = [Vector((p.x * taper(p.y), p.y)) for p in pts]
    return pts


OUT2 = outline()
IN2 = outline(LINING)
# The foot's straight run (the hinge, no zip there): outline indices whose point is on the foot's straight part.
FOOT = [i for i, p in enumerate(OUT2) if p.y < 1e-4 and abs(p.x) < (W0 / 2 - RB) * taper(0) + 1e-4]
ZIP = [i for i in range(N) if i not in FOOT]


def normal2(pts, i):
    a, b = pts[i - 1], pts[(i + 1) % len(pts)]
    t = (b - a).normalized()
    return Vector((t.y, -t.x))              # outward (the loop is counter-clockwise)


DENSE = rr_dense(W0, H, RB, RT, 0.0, 0.003)
DENSE = [Vector((p.x * taper(p.y), p.y)) for p in DENSE]


def rim_dist(x, z, pts=None):
    """Distance from a point inside the outline to it (m)."""
    pts = pts or DENSE
    best = 1e9
    for p in pts:
        d = (p.x - x) ** 2 + (p.y - z) ** 2
        if d < best:
            best = d
    return math.sqrt(best)


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def front_y(x, z):
    """The lid's outer face: its rim at the gusset's front edge, bulging forward toward the middle."""
    g = smooth(rim_dist(x, z) / 0.075) ** 0.7
    return YB - depth(z) - ZIPW - BULGE_F * g


POCKET = dict(w=0.232, h=0.2, rb=0.035, rt=0.085, z0=0.068)
POCKET2 = outline(0.0, POCKET["w"], POCKET["h"], POCKET["rb"], POCKET["rt"], POCKET["z0"], 120, tap=True)
POCKET_DENSE = [Vector((p.x * taper(p.y), p.y)) for p in rr_dense(POCKET["w"], POCKET["h"], POCKET["rb"], POCKET["rt"], POCKET["z0"], 0.003)]


def pocket_y(x, z):
    g = smooth(rim_dist(x, z, POCKET_DENSE) / 0.05) ** 0.8
    return front_y(x, z) - 0.03 * g


def surf_normal(fn, x, z, e=0.002):
    """The outward normal of a face y = fn(x, z) that faces -Y."""
    dx = (fn(x + e, z) - fn(x - e, z)) / (2 * e)
    dz = (fn(x, z + e) - fn(x, z - e)) / (2 * e)
    return Vector((dx, -1.0, dz)).normalized()


# ------------------------------------------------------------------------------------------------- mesh building

def grid(bm, rings, mi, want, closed=True):
    """Quads between consecutive rings of points (a ring may be one point, a pole); each face turned to face want(c)."""
    R = [[bm.verts.new(p) for p in r] if isinstance(r, list) else bm.verts.new(r) for r in rings]
    for a, b in zip(R[:-1], R[1:]):
        if isinstance(a, list) and isinstance(b, list):
            n = len(a)
            for i in range(n if closed else n - 1):
                j = (i + 1) % n
                _f(bm, (a[i], a[j], b[j], b[i]), mi, want)
        elif isinstance(a, list):
            for i in range(len(a)):
                _f(bm, (a[i], a[(i + 1) % len(a)], b), mi, want)
        else:
            for i in range(len(b)):
                _f(bm, (a, b[(i + 1) % len(b)], b[i]), mi, want)
    return R


def _f(bm, vs, mi, want):
    try:
        f = bm.faces.new(vs)
    except ValueError:
        return None
    f.material_index = mi if not callable(mi) else mi(f)
    f.normal_update()
    c = f.calc_center_median()
    if f.normal.dot(want(c)) < 0:
        f.normal_flip()
    return f


def base_seam(bm, z, body, base, others=()):
    """Cuts the faces along z (the base panel's straight seam) and gives the outside below it the base's suede."""
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z), plane_no=(0, 0, 1))
    for f in bm.faces:
        cz = f.calc_center_median().z
        if f.material_index in (body,) + tuple(others) and cz < z:
            f.material_index = base
        elif f.material_index == base and cz >= z:
            f.material_index = body


def strip_obj(name, rows, material, u_scale, want):
    """A band (two or more rows of points along it) with UVs: u along it (metres / u_scale), v 0..1 across."""
    bm = bmesh.new()
    R = [[bm.verts.new(p) for p in r] for r in rows]
    n = len(rows[0])
    faces = []
    for a, b in zip(R[:-1], R[1:]):
        for i in range(n - 1):
            f = _f(bm, (a[i], a[i + 1], b[i + 1], b[i]), 0, want)
            if f:
                faces.append(f)
    uv = bm.loops.layers.uv.new("UVMap")
    along = [0.0]
    for i in range(1, n):
        along.append(along[-1] + (rows[0][i] - rows[0][i - 1]).length)
    index = {}
    for r, row in enumerate(R):
        for i, v in enumerate(row):
            index[v] = (along[i] / u_scale, r / (len(R) - 1))
    for f in bm.faces:
        for l in f.loops:
            l[uv].uv = index[l.vert]
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def sweep(name, pts, up_fn, section, material, n=6):
    """A strap: a section [(a, b)] (a across, b through) swept along a Catmull-Rom path; up_fn(p) is roughly b."""
    P = [Vector(q[:3]) for q in catmull([tuple(p) for p in pts], n)]
    rings = []
    for i, p in enumerate(P):
        T = (P[min(len(P) - 1, i + 1)] - P[max(0, i - 1)]).normalized()
        B0 = Vector(up_fn(p))
        A = T.cross(B0).normalized()
        B = A.cross(T).normalized()
        sec = section(i / (len(P) - 1)) if callable(section) else section
        rings.append([p + A * a + B * b for a, b in sec])
    return loft(name, rings, material, True, True, recalc=True)


def superellipse(w, t, n=16, p=3.0):
    out = []
    for k in range(n):
        a = 2 * math.pi * k / n
        c, s = math.cos(a), math.sin(a)
        out.append((w / 2 * math.copysign(abs(c) ** (2 / p), c), t / 2 * math.copysign(abs(s) ** (2 / p), s)))
    return out


# ------------------------------------------------------------------------------------------------- materials and prints

COLOURS = {
    "Navy": (0.105, 0.135, 0.245),
    "Red": (0.46, 0.075, 0.07),
    "Forest": (0.12, 0.215, 0.15),
}


def zip_canvas():
    """A zip's coil seen from its face: black tape, the coil's teeth across it (a tile repeated along the zip)."""
    c = Canvas(32, 32, (0.035, 0.035, 0.038))
    for x in range(0, 32, 8):
        c.rect(x + 1, 9, x + 6, 23, (0.09, 0.09, 0.095))
        c.rect(x + 2, 10, x + 5, 22, (0.13, 0.13, 0.135))
    c.rect(0, 15, 32, 17, (0.06, 0.06, 0.065))
    return c


def patch_canvas():
    c = Canvas(256, 80, (0.06, 0.06, 0.065))
    c.frame(3, 3, 253, 77, 3, (0.2, 0.2, 0.21))
    # a little mountain mark and the name, raised rubber in light grey
    for i in range(22):
        c.rect(22 + i, 58 - i * 1.6, 23 + i, 60, (0.72, 0.72, 0.7))
        c.rect(66 - i, 58 - i * 1.6, 67 - i, 60, (0.72, 0.72, 0.7))
    c.text("NORREN", 84, 26, 4, (0.78, 0.78, 0.76), 1.12)
    return c


def label_canvas():
    """The care label sewn in the lining (small print on white)."""
    c = Canvas(96, 128, (0.86, 0.85, 0.8))
    c.text_c("NORREN", 48, 10, 2, (0.15, 0.15, 0.16))
    c.lines(10, 34, 86, 9, 6, (0.35, 0.35, 0.36), seed=7)
    for i in range(4):
        c.frame(12 + i * 19, 98, 26 + i * 19, 112, 2, (0.25, 0.25, 0.26))
    return c


def mats(colour):
    rgb = COLOURS[colour]
    M = {
        "body": S("Fabric_Pack" + colour, rgb, "fabric", 0.9),
        "base": S("Fabric_PackBase", (0.045, 0.043, 0.042), "fabric", 0.85),
        "lining": S("Fabric_PackLining", (0.5, 0.5, 0.49), "fabric", 0.8),
        "mesh": S("Fabric_PackMesh", (0.07, 0.072, 0.075), "fabric", 0.95),
        "web": S("Fabric_PackWebbing", (0.04, 0.04, 0.043), "fabric", 0.7),
        "buckle": S("Plastic_PackBuckle", (0.03, 0.03, 0.032), "plastic", 0.5),
        "metal": S("Metal_PackSlider", (0.42, 0.42, 0.42), "metal", 0.35, 1.0),
    }
    z = print_mat("Print_PackZip", zip_canvas(), 0.5)
    z["print_repeat"] = 1
    M["zip"] = z
    M["patch"] = print_mat("Print_PackPatch", patch_canvas(), 0.55)
    M["label"] = print_mat("Print_PackLabel", label_canvas(), 0.7)
    return M


# ------------------------------------------------------------------------------------------------- the shell

def P3(p2, y):
    return Vector((p2.x, y, p2.y))


def ring_at(pts, t, bow=0.0):
    """The gusset at depth fraction t (0 the back's seam, 1 the front's), bowed outward by bow * sin(pi t)."""
    out = []
    for i, p in enumerate(pts):
        n = normal2(pts, i)
        q = p + n * (bow * math.sin(math.pi * t))
        out.append(P3(q, YB - depth(p.y) * t))
    return out


def panel_rings(pts, y_fn, K=9, centre=(0.0, 0.2)):
    """Rings from an outline in to a pole at the centre; y_fn(x, z) the face's depth there."""
    c = Vector(centre)
    rings = []
    for k in range(K):
        s = 1.0 - k / K
        s = s ** 0.85
        ring = []
        for p in pts:
            q = c + (p - c) * s
            ring.append(Vector((q.x, y_fn(q.x, q.y), q.y)))
        rings.append(ring)
    rings.append(Vector((c.x, y_fn(c.x, c.y), c.y)))
    return rings


def shell(colour):
    M = mats(colour)
    order = ["body", "base", "lining", "mesh"]
    mi = {k: i for i, k in enumerate(order)}
    bm = bmesh.new()
    centre = Vector((0.0, YB - D0 / 2, 0.2))
    out_want = lambda c: Vector((c.x, (c.y - centre.y) * 0.4, c.z - centre.z))
    in_want = lambda c: -out_want(c)

    def outer_mat(f):
        return mi["base"] if f.calc_center_median().z < 0.055 else mi["body"]

    # The gusset outside, from the back's seam to the zip.
    T = [i / 10 for i in range(11)]
    rings = [ring_at(OUT2, t * (1.0 - ZIPW / D0), 0.006) for t in T]
    grid(bm, rings, outer_mat, out_want)
    # The back: padded, its middle the air mesh.
    def back_y(x, z):
        g = smooth(rim_dist(x, z) / 0.06)
        return YB + BULGE_B * g
    br = panel_rings(OUT2, back_y, 10, (0.0, 0.21))
    def back_mat(f):
        c = f.calc_center_median()
        if c.z < 0.055:
            return mi["base"]
        return mi["mesh"] if rim_dist(c.x, c.z) > 0.03 else mi["body"]
    grid(bm, br, back_mat, lambda c: Vector((0, 1, 0)))
    # The lining: inside the gusset and the back.
    inner = [ring_at(IN2, t, 0.004) for t in T]
    for r in inner:
        for v in r:
            v.y = min(v.y, YB - LINING)
    grid(bm, inner, mi["lining"], in_want)
    ib = panel_rings(IN2, lambda x, z: YB - LINING, 8, (0.0, 0.21))
    grid(bm, ib, mi["lining"], lambda c: Vector((0, -1, 0)))
    # The cut edge between the zip and the lining (it is the zip's tape: shown by the zip strips below).
    edge_o = ring_at(OUT2, 1.0, 0.0)
    edge_i = ring_at(IN2, 1.0, 0.0)
    grid(bm, [edge_o, edge_i], mi["body"], lambda c: Vector((0, -1, 0)))
    base_seam(bm, 0.055, mi["body"], mi["base"], (mi["mesh"],))
    ob = new_obj("Shell", bm, [M[k] for k in order])
    parts = [ob]
    # Piping round the back's seam (a cord in the base's suede).
    seam = [P3(p + normal2(OUT2, i) * 0.003, YB + 0.001) for i, p in enumerate(OUT2)]
    parts.append(tube("Piping", seam + seam[:1], 0.0032, M["base"], 8, False, False))
    # Shoulder straps: padded, S-shaped, from the top of the back to buckles at the foot's corners.
    up = lambda p: (0.0, 1.0, 0.0)
    for sx in (-1, 1):
        pad = [(sx * 0.03, YB + 0.012, 0.392), (sx * 0.068, YB + 0.03, 0.372), (sx * 0.105, YB + 0.042, 0.3),
               (sx * 0.118, YB + 0.04, 0.215), (sx * 0.114, YB + 0.032, 0.145), (sx * 0.112, YB + 0.026, 0.118)]
        def sec(t):
            w = 0.064 * (0.72 + 0.28 * math.sin(math.pi * min(1.0, t * 1.15)))
            th = 0.016 * (0.6 + 0.4 * math.sin(math.pi * t))
            return superellipse(w, th, 18, 3.2)
        parts.append(sweep("Strap", pad, up, sec, M["body"], 6))
        # its underside the air mesh (a thinner band set into the strap's face toward the back)
        web = [(sx * 0.112, YB + 0.022, 0.12), (sx * 0.117, YB + 0.017, 0.09), (sx * 0.126, YB + 0.01, 0.055),
               (sx * 0.134, YB + 0.004, 0.032)]
        parts.append(sweep("Webbing", web, up, superellipse(0.025, 0.0028, 10, 4.0), M["web"], 5))
        # the ladder-lock buckle where the padding runs into the webbing
        bk = rbox("Buckle", (0.034, 0.008, 0.026), (sx * 0.113, YB + 0.026, 0.112), M["buckle"], 0.002, 2)
        parts.append(bk)
        # the webbing's tail through the buckle, hanging a little
        tail = [(sx * 0.113, YB + 0.03, 0.105), (sx * 0.112, YB + 0.034, 0.07), (sx * 0.108, YB + 0.035, 0.045)]
        parts.append(sweep("Tail", tail, up, superellipse(0.025, 0.0028, 10, 4.0), M["web"], 4))
        # the strap's top sewn on with a bar tack: a webbing tab
        parts.append(rbox("Tab", (0.03, 0.004, 0.03), (sx * 0.03, YB + 0.003, 0.398), M["web"], 0.001, 1))
    # The grab loop on top.
    loop = [(-0.03, YB + 0.002, 0.41), (-0.026, YB + 0.006, 0.438), (-0.012, YB + 0.008, 0.452), (0.0, YB + 0.0085, 0.455),
            (0.012, YB + 0.008, 0.452), (0.026, YB + 0.006, 0.438), (0.03, YB + 0.002, 0.41)]
    parts.append(sweep("GrabLoop", loop, lambda p: (0.0, 1.0, 0.0), superellipse(0.024, 0.003, 10, 4.0), M["web"], 5))
    base = finish(parts, "Backpack_" + colour, sharp_deg=70)
    # The zip's shell half (a band along the gusset's front edge, from the right foot round to the left).
    zs = [i for i in ZIP]
    row0 = [ring_at(OUT2, 1.0 - ZIPW / D0, 0.0)[i] for i in zs]
    row1 = [ring_at(OUT2, 1.0, 0.0)[i] for i in zs]
    row2 = [ring_at(IN2, 1.0, 0.0)[i] for i in zs]
    z1 = strip_obj("ZipShell", [row0, row1], M["zip"], 0.008, lambda c: Vector((c.x, 0, c.z - 0.2)))
    z2 = strip_obj("ZipTape", [row1, row2], M["zip"], 0.008, lambda c: Vector((0, -1, 0)))
    # The care label inside, on the lining of the back near the top.
    lab = card("CareLabel", 0.036, 0.048, (0.0, YB - LINING - 0.0008, 0.33), M["label"], (0, -1, 0))
    return finish_mixed([base], [z1, z2, lab], "Backpack_" + colour)


# ------------------------------------------------------------------------------------------------- the lid

HINGE = Vector((0.0, YB - D0 - ZIPW * 0.5, 0.0))


def lid(colour):
    M = mats(colour)
    order = ["body", "base", "lining"]
    mi = {k: i for i, k in enumerate(order)}
    bm = bmesh.new()

    def outer_mat(f):
        return mi["base"] if f.calc_center_median().z < 0.05 else mi["body"]

    # The rim: the zip's other half (below), then the outer face bulging forward, the pocket on it.
    def lid_y(x, z):
        p = POCKET
        inside_pocket = (abs(x) < p["w"] / 2 * taper(z) and p["z0"] < z < p["z0"] + p["h"])
        return pocket_y(x, z) if inside_pocket and rim_dist(x, z, POCKET_DENSE) > 0 else front_y(x, z)

    # The face outside the pocket: rings from the rim inward, the pocket's region cut out by building the pocket on top
    # (its rim sits on this face, so the face under it is simply hidden).
    fr = panel_rings(OUT2, front_y, 12, (0.0, 0.25))
    grid(bm, fr, outer_mat, lambda c: Vector((0, -1, 0)))
    # The pocket: its gusset from the panel out to its face.
    pr = panel_rings(POCKET2, pocket_y, 9, (0.0, POCKET["z0"] + POCKET["h"] * 0.45))
    # its rim a few mm proud of the panel (so they never fight)
    pr[0] = [Vector((v.x, front_y(v.x, v.z) + 0.001, v.z)) for v in pr[0]]
    grid(bm, pr, outer_mat, lambda c: Vector((0, -1, 0)))
    # The lid's inside: the lining, set back from the outer face.
    def inner_y(x, z):
        return front_y(x, z) + 0.004
    ir = panel_rings(IN2, inner_y, 10, (0.0, 0.25))
    grid(bm, ir, mi["lining"], lambda c: Vector((0, 1, 0)))
    # Between the outer rim and the lining's rim: the cut edge (the zip's tape shows over it, below).
    o_rim = [Vector((p.x, front_y(p.x, p.y), p.y)) for p in OUT2]
    i_rim = [Vector((p.x, inner_y(p.x, p.y), p.y)) for p in IN2]
    grid(bm, [o_rim, i_rim], mi["body"], lambda c: Vector((0, 1, 0)))
    base_seam(bm, 0.05, mi["body"], mi["base"])
    ob = new_obj("LidFace", bm, [M[k] for k in order])
    parts = [ob]
    # The lash tab: a suede diamond above the pocket, its two slits printed dark by the bake's creases.
    tz = POCKET["z0"] + POCKET["h"] + 0.04
    bmt = bmesh.new()
    ring = []
    for k in range(16):
        a_ = 2 * math.pi * k / 16
        c_, s_ = math.cos(a_), math.sin(a_)
        # a diamond with softened corners
        x = 0.026 * math.copysign(abs(c_) ** 1.6, c_)
        z = tz + 0.033 * math.copysign(abs(s_) ** 1.6, s_)
        ring.append((x, z))
    fr_ = [bmt.verts.new((x, front_y(x, z) - 0.0022, z)) for x, z in ring]
    bk_ = [bmt.verts.new((x, front_y(x, z) + 0.0008, z)) for x, z in ring]
    cf = bmt.verts.new((0.0, front_y(0.0, tz) - 0.0024, tz))
    for k in range(16):
        j = (k + 1) % 16
        _f(bmt, (fr_[k], fr_[j], bk_[j], bk_[k]), 0, lambda c: Vector((c.x, 0, c.z - tz)))
        _f(bmt, (fr_[k], fr_[j], cf), 0, lambda c: Vector((0, -1, 0)))
    tab = new_obj("LashTab", bmt, [M["base"]])
    parts.append(tab)
    plain = finish(parts, "BackpackLid_" + colour, sharp_deg=75)
    printed = []
    # The zip's lid half: a band round the rim, facing out.
    o_zip = [o_rim[i] for i in ZIP]
    back_zip = [Vector((v.x, v.y + ZIPW - 0.0003, v.z)) for v in o_zip]
    printed.append(strip_obj("ZipLid", [o_zip, back_zip], M["zip"], 0.008, lambda c: Vector((c.x, 0, c.z - 0.2))))
    # The pocket's zip: across the top of its arc, a band on its face 9 mm wide, 7 mm inside its rim.
    arc = [i for i, p in enumerate(POCKET2) if p.y > POCKET["z0"] + POCKET["h"] * 0.55]
    arc.sort(key=lambda i: math.atan2(POCKET2[i].y - (POCKET["z0"] + 0.06), POCKET2[i].x))
    def on_pocket(p2, inset):
        q = p2 - normal2(POCKET2, POCKET2.index(p2)) * inset
        return Vector((q.x, pocket_y(q.x, q.y) - 0.0006, q.y))
    rowa = [on_pocket(POCKET2[i], 0.006) for i in arc]
    rowb = [on_pocket(POCKET2[i], 0.015) for i in arc]
    printed.append(strip_obj("PocketZip", [rowa, rowb], M["zip"], 0.008, lambda c: Vector((0, -1, 0))))
    # The patch: raised rubber with the brand, on the pocket's face.
    px, pz = 0.0, POCKET["z0"] + 0.085
    n = surf_normal(pocket_y, px, pz)
    c = Vector((px, pocket_y(px, pz), pz)) + n * 0.0012
    printed.append(card("Patch", 0.078, 0.025, tuple(c), M["patch"], tuple(n)))
    # The pocket's slider and pull at the right end of its zip (Blender -X is the wearer's right seen from the front).
    end = rowa[0] * 0.5 + rowb[0] * 0.5
    printed.append(rbox("PocketSlider", (0.012, 0.006, 0.018), tuple(end + Vector((0, -0.003, -0.004))), M["metal"], 0.0015, 2))
    cord = [end + Vector((0, -0.005, -0.012)), end + Vector((0.004, -0.008, -0.03)), end + Vector((0.0, -0.009, -0.05))]
    printed.append(tube("PocketCord", cord, 0.0016, M["web"], 8))
    printed.append(lathe("PocketTip", [(0.0, 0.0), (0.0042, 0.0), (0.0048, 0.004), (0.0045, 0.016), (0.0, 0.018)], M["buckle"], 12,
                         xf=M4(rot('X', 180), tuple(end + Vector((0.0, -0.009, -0.047))))))
    ob = finish_mixed([plain], printed, "BackpackLid_" + colour)
    to_origin(ob, HINGE)
    return ob


def to_origin(ob, pivot):
    ob.data.transform(Matrix.Translation(-Vector(pivot)))
    ob.data.update()
    return ob


# ------------------------------------------------------------------------------------------------- the zip's pull

def pull():
    M = mats("Navy")
    p = []
    p.append(rbox("Slider", (0.012, 0.007, 0.02), (0, 0, 0), M["metal"], 0.002, 2))
    p.append(rbox("Puller", (0.007, 0.0025, 0.014), (0, -0.003, -0.012), M["metal"], 0.001, 1))
    loop = [(0.0, -0.004, -0.016), (0.004, -0.006, -0.028), (0.003, -0.006, -0.045), (-0.003, -0.006, -0.045),
            (-0.004, -0.006, -0.028), (0.0, -0.004, -0.016)]
    p.append(tube("Cord", loop, 0.0015, M["web"], 8, False, False))
    p.append(lathe("Tip", [(0.0, 0.0), (0.0045, 0.0), (0.005, 0.004), (0.0047, 0.017), (0.0, 0.019)], M["buckle"], 14,
                   xf=M4(rot('X', 180), (0.0, -0.006, -0.04))))
    return finish(p, "BackpackPull", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- the layout

LAYOUT = {}


def write_layout():
    os.makedirs(os.path.abspath(OUT), exist_ok=True)
    zipc = []
    for i in ZIP:
        a = ring_at(OUT2, 1.0, 0.0)[i]
        zipc.extend([round(a.x, 4), round(a.y - 0.001, 4), round(a.z, 4)])
    LAYOUT.update({
        "hinge": [HINGE.x, HINGE.y, HINGE.z],
        "zip": zipc,
        "inside_min": [-0.122, YB - D0 + 0.012, 0.014],
        "inside_max": [0.122, YB - LINING - 0.002, 0.36],
        "grab": [0.0, YB + 0.008, 0.452],
        "size": [W0, D0 + BULGE_B + BULGE_F + 0.03, H + 0.02],
    })
    with open(os.path.join(os.path.abspath(OUT), "Backpack_Layout.json"), "w", encoding="utf-8") as fh:
        json.dump(LAYOUT, fh, indent=1)
    print("[backpack] layout written, zip points", len(zipc) // 3)


models = []
for col in COLOURS:
    models.append(("Backpack_" + col, (lambda c=col: shell(c)), (0.7, 1.0, 0.35), {"res": 1024, "scale": 0.35, "wear": 0.5, "dirt": 0.6}))
    models.append(("BackpackLid_" + col, (lambda c=col: lid(c)), (0.5, -1.0, 0.3), {"res": 1024, "scale": 0.3, "wear": 0.5, "dirt": 0.55}))
models.append(("BackpackPull", pull, (0.5, -1.0, 0.3), {"res": 128, "scale": 0.04, "wear": 0.5, "dirt": 0.4}))
run_baked(models)
write_layout()
print("[backpack] done ->", OUT)
