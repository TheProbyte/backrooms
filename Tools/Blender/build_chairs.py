"""
Two chairs (2026-11-04, remade after the owner's photo the same day), built in Blender and baked (bake_kit.py). Run
headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_chairs.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>]

  - LawnChair_White / LawnChair_Green: the one-piece injection-moulded resin low-back armchair of the owner's photo
    (a "monobloc", the Grosfillex Miami's class: about 0.6 x 0.57 x 0.78 m, the seat 0.43 m up, the arms 0.63 m, 2.5 kg):
    a fan back of nine slats spreading up from a solid lumbar band to a top rail that sweeps round and forward as the
    arms and down into the front legs; a dished seat with a rolled skirt; four tapered legs of U section, open inside
    (how they stack). Built as overlapping parts, fused into one moulding (Blender's voxel remesh), its joins softened
    into fillets as a mould's are, then reduced.
  - SchoolChair_Blue / SchoolChair_Orange: a classroom stacking chair (18 in seat): one polypropylene shell, seat
    curving up into the back, its sides wrapping round, a hand hole through the top of the back; on a frame of 7/8 in
    black steel tube (each side a front leg, the rail under the seat and the upright behind the back in one bend; the
    back legs welded on), bolted through the back, glides on its feet.

Frame: origin on the floor at the middle of its foot, its front toward -Y (Unity +Z), its back at +Y. Unity (x, y, z) =
(-x, z, -y).
"""
import os
import sys
import math
import random

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_rooms.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- break room")
exec(compile(_src[:_cut], "build_rooms_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix


def apply_mod(ob, kind, **props):
    m = ob.modifiers.new(kind, kind)
    for k, v in props.items():
        setattr(m, k, v)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=m.name)
    return ob


def superellipse(w, t, n=16, p=4.0):
    """A rounded-rectangle-like section w across (a), t through (b), counter-clockwise."""
    out = []
    for k in range(n):
        a = 2 * math.pi * k / n
        c, s = math.cos(a), math.sin(a)
        out.append((w / 2 * math.copysign(abs(c) ** (2 / p), c), t / 2 * math.copysign(abs(s) ** (2 / p), s)))
    return out


def sweep(name, pts, frame_fn, section_fn, material, caps=True):
    """A solid swept along pts: at point i, frame_fn(i, T) gives the section's axes (A, B) and section_fn(i) its
    outline [(a, b)]; the ring is p + A a + B b."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    rings = []
    for i in range(n):
        T = (pts[min(n - 1, i + 1)] - pts[max(0, i - 1)]).normalized()
        A, B = frame_fn(i, T)
        rings.append([pts[i] + A * a + B * b for (a, b) in section_fn(i)])
    return loft(name, rings, material, caps, caps, recalc=True)


def surface(name, rows, material, thick, offset=-1.0, face=None):
    """A sheet through a grid of points (rows of equal length), solidified to a thickness (closed). face = (point,
    direction): the sheet's front is the side the direction points to from the face nearest the point (the thickness
    goes behind it with offset -1)."""
    bm = bmesh.new()
    V = [[bm.verts.new(p) for p in row] for row in rows]
    for j in range(len(V) - 1):
        for i in range(len(V[j]) - 1):
            bm.faces.new((V[j][i], V[j][i + 1], V[j + 1][i + 1], V[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if face is not None:
        pt, d = Vector(face[0]), Vector(face[1])
        f0 = min(bm.faces, key=lambda f: (f.calc_center_median() - pt).length)
        f0.normal_update()
        if f0.normal.dot(d) < 0:
            for f in bm.faces:
                f.normal_flip()
    ob = new_obj(name, bm, [material])
    return apply_mod(ob, "SOLIDIFY", thickness=thick, offset=offset, use_even_offset=True)


# ------------------------------------------------------------------------------------------------- lawn chair

def lawn_chair(colour, name):
    PP = S("Plastic_LawnChair", colour, "plastic", 0.5)
    p = []
    SH = 0.43                                   # the seat's front edge, on top
    yf, yb = -0.23, 0.2                         # the seat's front and back
    lean = math.radians(12.5)

    # The seat: a dish, wider at the front, tilted back 4 degrees, hollowed in the middle; then its deep rolled skirt.
    def seat_w(y):
        return 0.5 - 0.06 * (y - yf) / (yb - yf)

    def seat_z(x, y):
        u, v = x / (seat_w(y) / 2), (y - yf) / (yb - yf)
        return SH - 0.03 * v - 0.026 * (1 - u * u) * math.sin(math.pi * min(1.0, v * 1.05)) + 0.008 * u ** 4

    rows = []
    NY, NX = 24, 24
    for j in range(NY + 1):
        v = j / NY
        y = yf + (yb - yf) * v
        row = []
        for i in range(NX + 1):
            u = -1 + 2 * i / NX
            e = abs(v - 0.5) * 2
            corner = 1.0 - 0.08 * max(0.0, (abs(u) - 0.7) / 0.3) * max(0.0, (e - 0.7) / 0.3)
            x = u * seat_w(y) / 2 * corner
            row.append(Vector((x, y, seat_z(x, y))))
        rows.append(row)
    p.append(surface("Seat", rows, PP, 0.017, face=((0, 0, SH), (0, 0, 1))))
    ring = [r for r in rows[0]] + [rows[j][-1] for j in range(1, NY + 1)] + list(reversed(rows[-1][:-1])) + \
           [rows[j][0] for j in range(NY - 1, 0, -1)]
    # the skirt: rolled over at the front (out and down), straight down round the sides and back
    mid_r = [Vector((q.x * 1.02, q.y + (-0.012 if q.y < yf + 0.03 else 0.0), q.z - 0.016)) for q in ring]
    low = [Vector((q.x * 1.02, q.y + (-0.01 if q.y < yf + 0.03 else 0.0), q.z - 0.048)) for q in ring]
    sk = loft("Skirt", [ring, mid_r, low], PP, cap0=False, cap1=False)
    p.append(apply_mod(sk, "SOLIDIFY", thickness=0.01, offset=1.0, use_even_offset=True))
    # ribs under it, following its underside (a cross of three lengthwise and one across)
    def under(x, y):
        return Vector((x, y, seat_z(x, y) - 0.017 - 0.013))
    for x in (-0.12, 0.0, 0.12):
        rib = [under(x, yf + 0.03 + (yb - yf - 0.06) * t / 10) for t in range(11)]
        p.append(sweep("Rib", rib, lambda i, T: (Vector((1, 0, 0)), T.cross(Vector((1, 0, 0))).normalized()),
                       lambda i: superellipse(0.011, 0.03, 10, 4.0), PP))
    ym = (yf + yb) / 2 + 0.03
    rib = [under(-0.21 + 0.42 * t / 12, ym) for t in range(13)]
    p.append(sweep("RibX", rib, lambda i, T: (Vector((0, 1, 0)), T.cross(Vector((0, 1, 0))).normalized()),
                   lambda i: superellipse(0.011, 0.03, 10, 4.0), PP))

    # The back's surface: leaning back, curving round toward the sitter at its sides.
    def back_y(x, z):
        return yb - 0.012 + (z - (SH - 0.03)) * math.tan(lean) - 0.05 * (x / 0.25) ** 2

    # The frame: one sweep from the right front foot up the leg, bent back into the arm (sloping up a little), up
    # round the back as its top rail and down the other side to the left front foot: wide across the leg's front,
    # lying flat along the arm, standing up across the back (as in the photo).
    half = [(0.298, -0.272, 0.0), (0.284, -0.24, 0.25), (0.27, -0.212, 0.5), (0.264, -0.2, 0.585), (0.264, -0.172, 0.622),
            (0.266, -0.1, 0.632), (0.266, 0.0, 0.642), (0.26, 0.09, 0.658), (0.24, 0.17, 0.69), (0.2, 0.232, 0.732),
            (0.135, 0.268, 0.772), (0.065, 0.285, 0.795), (0.0, 0.289, 0.801)]
    path = half + [(-x, y, z) for (x, y, z) in reversed(half[:-1])]
    pts = [Vector(q[:3]) for q in catmull(path, 7)]

    def _ss2(a, b, t):
        t = min(1.0, max(0.0, (t - a) / (b - a)))
        return t * t * (3 - 2 * t)

    def rail_frame(i, T):
        q = pts[i]
        kb = _ss2(0.12, 0.25, q.y)                  # across the back: the band stands up
        D = (Vector((1, 0, 0)) * (1 - kb) + Vector((0, 0, 1)) * kb).normalized()
        A = D - T * D.dot(T)
        if A.length < 1e-5:
            A = Vector((0, 0, 1)) - T * T.z
        A.normalize()
        return A, T.cross(A).normalized()

    def rail_section(i):
        q = pts[i]
        kb = _ss2(0.12, 0.25, q.y)
        kl = _ss2(0.61, 0.5, q.z)                   # down the leg
        arm = (0.06, 0.026)
        rail = (0.052, 0.028)
        legd = (0.042 + 0.02 * q.z / 0.55, 0.036 + 0.018 * q.z / 0.55)
        w = arm[0] + (rail[0] - arm[0]) * kb
        t = arm[1] + (rail[1] - arm[1]) * kb
        w, t = w + (legd[0] - w) * kl, t + (legd[1] - t) * kl
        return superellipse(w, t, 20, 3.2)
    p.append(sweep("Frame", pts, rail_frame, rail_section, PP))

    # The lumbar band: a curved band a little above the seat (rain runs out under it), its ends into the back's sides.
    rows = []
    for j in range(5):
        z = SH + 0.035 + 0.06 * j / 4
        row = []
        for i in range(25):
            x = -0.232 + 0.464 * i / 24
            row.append(Vector((x, back_y(x, z), z)))
        rows.append(row)
    p.append(surface("Lumbar", rows, PP, 0.018, face=((0, yb + 0.03, SH + 0.06), (0, -1, 0))))
    # the back's sides: from the seat's back corners up past the band into the frame behind the arms
    for sx in (-1, 1):
        side = [Vector((sx * 0.2, yb - 0.025, SH - 0.035)), Vector((sx * 0.228, back_y(0.228, SH + 0.06) + 0.008, SH + 0.06)),
                Vector((sx * 0.236, back_y(0.236, SH + 0.17) + 0.006, SH + 0.17)), Vector((sx * 0.24, 0.175, 0.69))]
        side = [Vector(q[:3]) for q in catmull(side, 6)]

        def sf(i, T, sx=sx):
            out = Vector((sx, 0, 0))
            A = (out - T * out.dot(T)).normalized()
            return A, T.cross(A).normalized()
        p.append(sweep("BackSide", side, sf, lambda i: superellipse(0.034, 0.026, 12, 3.0), PP))

    # The slats: nine, fanning straight up from the band to the rail (closer together at the bottom), on the back's curve.
    def rail_at(x):
        best = None
        for q in pts:
            if q.y > 0.12 and (best is None or abs(q.x - x) < abs(best.x - x)):
                best = q
        return best
    NS = 9
    for k in range(NS):
        f = -1 + 2 * k / (NS - 1)
        xb, xt = f * 0.125, f * 0.212
        top = rail_at(xt)
        zt = top.z - 0.008
        zb = SH + 0.08
        sl = []
        for m in range(9):
            t = m / 8
            z = zb + (zt - zb) * t
            x = xb + (xt - xb) * t
            y = back_y(x, z) + 0.007 if t < 0.9 else top.y
            sl.append(Vector((x, y, z)))

        def sl_frame(i, T, sl=sl):
            c = sl[i]
            nrm = Vector((back_y(c.x + 0.001, c.z) - back_y(c.x - 0.001, c.z), -0.002, 0)).normalized()
            A = T.cross(nrm).normalized()
            return A, T.cross(A).normalized()
        p.append(sweep("Slat", sl, sl_frame, lambda i: superellipse(0.014 + 0.008 * i / 8, 0.017, 12, 3.0), PP))

    # The back legs: tapered, splayed back and out, of U section (open toward the chair's middle: they stack).
    def leg(top, bot, a0, b0, a1, b1):
        top, bot = Vector(top), Vector(bot)
        inward = Vector((-top.x, -top.y * 0.6, 0)).normalized()
        T = (bot - top).normalized()
        U = (-inward - T * (-inward).dot(T)).normalized()     # outward: the U's closed side
        V = T.cross(U).normalized()
        pts_ = [top.lerp(bot, t / 6) for t in range(7)]

        def fr(i, T_):
            return U, V

        def sec(i):
            t = i / 6
            a, b, w = a0 + (a1 - a0) * t, b0 + (b1 - b0) * t, 0.006
            r = min(a, b) * 0.3
            ring = [(-a / 2, -b / 2)]
            for k in range(5):
                ang = -math.pi / 2 + (math.pi / 2) * k / 4
                ring.append((a / 2 - r + r * math.cos(ang), -b / 2 + r + r * math.sin(ang)))
            for k in range(5):
                ang = (math.pi / 2) * k / 4
                ring.append((a / 2 - r + r * math.cos(ang), b / 2 - r + r * math.sin(ang)))
            ring += [(-a / 2, b / 2), (-a / 2, b / 2 - w), (a / 2 - w, b / 2 - w), (a / 2 - w, -b / 2 + w), (-a / 2, -b / 2 + w)]
            return ring
        return sweep("Leg", pts_, fr, sec, PP)
    for sx in (-1, 1):
        p.append(leg((sx * 0.2, 0.165, SH - 0.005), (sx * 0.262, 0.322, 0.0), 0.062, 0.056, 0.044, 0.04))

    # One moulding: fuse, soften the joins (a mould's fillets), reduce.
    ob = join(p, name)
    apply_mod(ob, "REMESH", mode="VOXEL", voxel_size=0.0028, adaptivity=0.0, use_smooth_shade=True)
    apply_mod(ob, "SMOOTH", factor=0.65, iterations=6)
    tris = sum(len(f.vertices) - 2 for f in ob.data.polygons)
    apply_mod(ob, "DECIMATE", decimate_type="COLLAPSE", ratio=min(1.0, 20000.0 / max(1, tris)))
    ob = finish([ob], name, sharp_deg=None)
    return ob


# ------------------------------------------------------------------------------------------------- school chair

def school_chair(colour, name):
    PP = S("Plastic_SchoolShell", colour, "plastic", 0.38)
    ST = S("Paint_SchoolFrame", (0.06, 0.06, 0.065), "paint", 0.35)
    GL = S("Plastic_SchoolGlide", (0.08, 0.08, 0.08), "plastic", 0.55)
    RV = S("Steel_SchoolBolt", (0.55, 0.55, 0.56), "metal", 0.35, 1.0)
    p = []
    SH = 0.457
    # The shell's middle line seen from the side (y, z): the front lip, the seat's dish, the bend, the back leaning
    # 12 degrees; its width along it; how far its sides wrap toward the sitter.
    line = catmull([(-0.222, SH - 0.03), (-0.208, SH - 0.002), (-0.15, SH - 0.008), (-0.03, SH - 0.02), (0.1, SH - 0.016),
                    (0.168, SH - 0.002), (0.198, SH + 0.04), (0.212, SH + 0.12), (0.236, SH + 0.24), (0.262, SH + 0.37)], 6)
    L = [Vector((0, q[0], q[1])) for q in line]
    n = len(L)
    acc = [0.0]
    for i in range(1, n):
        acc.append(acc[-1] + (L[i] - L[i - 1]).length)
    tot = acc[-1]
    s_bend0, s_bend1 = acc[int(n * 0.58)] / tot, acc[int(n * 0.72)] / tot

    def width(s):
        if s < s_bend0:
            return 0.43 - 0.02 * s / s_bend0
        if s < s_bend1:
            t = (s - s_bend0) / (s_bend1 - s_bend0)
            return 0.41 - 0.07 * math.sin(math.pi * t)
        return 0.41 + 0.02 * (s - s_bend1) / (1 - s_bend1)

    def wrap(s):
        return 0.014 if s < s_bend0 else 0.014 + 0.026 * min(1.0, (s - s_bend0) / (s_bend1 - s_bend0))
    rows = []
    for i in range(n):
        T = (L[min(n - 1, i + 1)] - L[max(0, i - 1)]).normalized()
        N = Vector((0, -T.z, T.y))            # toward the sitter: up on the seat, forward on the back
        s = acc[i] / tot
        w = width(s)
        row = []
        for k in range(29):
            u = -1 + 2 * k / 28
            row.append(L[i] + Vector((u * w / 2, 0, 0)) + N * wrap(s) * u * u)
        rows.append(row)
    shell = surface("Shell", rows, PP, 0.0065, offset=-1.0, face=((0, 0.0, SH), (0, 0, 1)))
    apply_mod(shell, "BEVEL", width=0.0022, segments=2, limit_method="ANGLE", angle_limit=math.radians(50))
    # The hand hole: cut through the top of the back.
    iz = min(range(n), key=lambda i: abs(L[i].z - (SH + 0.315)))
    T = (L[min(n - 1, iz + 1)] - L[max(0, iz - 1)]).normalized()
    cut = rbox("Hole", (0.11, 0.1, 0.032), (0, 0, 0), PP, 0.0155, 4)
    cut.matrix_world = Matrix.Translation(L[iz]) @ Vector((0, 0, 1)).rotation_difference(T).to_matrix().to_4x4()
    b = shell.modifiers.new("Hole", "BOOLEAN")
    b.operation = "DIFFERENCE"
    b.object = cut
    try:
        b.solver = "EXACT"
    except TypeError:
        pass
    bpy.ops.object.select_all(action="DESELECT")
    shell.select_set(True)
    bpy.context.view_layer.objects.active = shell
    bpy.ops.object.modifier_apply(modifier=b.name)
    bpy.data.objects.remove(cut, do_unlink=True)
    p.append(shell)

    def back_pt(z, x):
        """the shell's rear face at height z, x across (behind it by its thickness)."""
        i = min(range(n), key=lambda k: abs(L[k].z - z))
        T = (L[min(n - 1, i + 1)] - L[max(0, i - 1)]).normalized()
        N = Vector((0, -T.z, T.y))
        s = acc[i] / tot
        u = x / (width(s) / 2)
        return L[i] + Vector((x, 0, 0)) + N * wrap(s) * u * u - N * 0.0065

    # The frame: each side one bent tube (front leg, the rail under the seat, the upright behind the back), the back
    # leg welded on; two stretchers; glides; bolts through the back and the seat.
    r = 0.011
    for sx in (-1, 1):
        x = sx * 0.17
        up0 = back_pt(SH + 0.1, x * 1.0)
        up1 = back_pt(SH + 0.27, x * 1.0)
        side = [Vector((sx * 0.205, -0.205, 0.012)), Vector((sx * 0.19, -0.18, SH - 0.1)), Vector((x, -0.15, SH - 0.032)),
                Vector((x, 0.1, SH - 0.034)), Vector((x, 0.165, SH - 0.02)), up0 + Vector((0, r, 0)), up1 + Vector((0, r, 0))]
        p.append(tube("Side", catmull(side, 6), r, ST, 12))
        leg = [Vector((x, 0.15, SH - 0.034)), Vector((sx * 0.19, 0.2, SH * 0.5)), Vector((sx * 0.21, 0.255, 0.012))]
        p.append(tube("BackLeg", catmull(leg, 5), r, ST, 12))
        for c in (Vector((sx * 0.205, -0.205, 0.0)), Vector((sx * 0.21, 0.255, 0.0))):
            p.append(lathe("Glide", [(0.0, 0.0), (0.015, 0.0), (0.015, 0.005), (0.012, 0.016), (0.0, 0.016)], GL, 14, xf=M4(None, c)))
        for z in (SH + 0.13, SH + 0.24):
            q = back_pt(z, x)
            i = min(range(n), key=lambda k: abs(L[k].z - z))
            T = (L[min(n - 1, i + 1)] - L[max(0, i - 1)]).normalized()
            N = Vector((0, -T.z, T.y))
            p.append(sphere("Bolt", q + N * 0.0068, (0.0065, 0.0065, 0.0065), RV, 12, 6,
                            rot=Vector((0, 0, 1)).rotation_difference(N).to_matrix() @ Matrix.Diagonal((1, 1, 0.35))))
        for y in (-0.1, 0.08):
            p.append(sphere("Rivet", (x, y, SH - 0.026), (0.006, 0.006, 0.003), RV, 10, 4))
    for (y, z) in ((-0.15, SH - 0.06), (0.15, SH - 0.06)):
        p.append(lathe("Stretcher", [(0.0, 0.0), (r * 0.8, 0.0), (r * 0.8, 0.36), (0.0, 0.36)], ST, 10, xf=M4(rot('Y', 90), (-0.18, y, z))))
    return finish(p, name, sharp_deg=55)


run_baked([
    ("LawnChair_White", lambda: lawn_chair((0.9, 0.9, 0.87), "LawnChair_White"), (0.8, -1.0, 0.5), {"scale": 0.6, "px_per_m": 480.0, "dirt": 1.1}),
    ("LawnChair_Green", lambda: lawn_chair((0.17, 0.36, 0.22), "LawnChair_Green"), (0.8, -1.0, 0.5), {"scale": 0.6, "px_per_m": 480.0, "dirt": 1.1}),
    ("SchoolChair_Blue", lambda: school_chair((0.13, 0.28, 0.58), "SchoolChair_Blue"), (0.8, -1.0, 0.4), {"scale": 0.6, "px_per_m": 500.0}),
    ("SchoolChair_Orange", lambda: school_chair((0.85, 0.4, 0.1), "SchoolChair_Orange"), (0.8, -1.0, 0.4), {"scale": 0.6, "px_per_m": 500.0}),
])
print("[chairs] done ->", OUT)
