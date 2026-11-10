"""
The furniture, remade for realism (2026-11-03): the sofas, armchair, office chair, floor lamp, conference table and wall
clock (they were boxes and cylinders: build_props.py, which still builds the wooden-chair stand-in only). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_furniture.py -- Assets/Backrooms/Models [--preview <dir>] [--only Sofa_Chesterfield]

Uses build_items2.py's helpers (lathe, loft, tube, rbox, finish ...). Same names, frames, sizes and material slots as the
models they replace, so the prefabs, colliders (Interaction.MovableShapes: from the bounds) and the floor lamp's light
(Interaction.FloorLamp: shade 1.38-1.70 m, 0.225 / 0.185 m round) still fit: origin on the floor at the footprint's
middle, front toward -Y (Unity +Z), backs toward +Y. Slots: Fabric (the room's upholstery), Wood, Metal, Shade, Plastic,
Laminate, Glass, Edge (dark vinyl), ClockDial (the printed dial).

What makes them read as real (furniture and prop modelling practice): edges never sharp (bevels), cushions crowned and
sagging where they are sat on, piping (welt cord) swept along the seams as its own geometry, deep button tufting with
the pleats running between the buttons, upholstery UVs at one world scale (the fabric's weave the same size
everywhere), wood UVs along its grain, real sizes (a chesterfield's seat at 46 cm, arms and back level at 76 cm; a task
chair's seat 50 cm wide with a waterfall front).
"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_items2.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------------------ models")
exec(compile(_src[:_cut], "build_items2_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix

FAB = mat("Fabric", (0.45, 0.42, 0.38), 0.95)
WOOD = mat("Wood", (0.36, 0.22, 0.12), 0.55)
METAL = mat("Metal", (0.3, 0.28, 0.26), 0.35, 0.8)
PLASTIC = mat("Plastic", (0.08, 0.08, 0.085), 0.5)


def rot(axis, deg):
    return Matrix.Rotation(math.radians(deg), 3, axis)


def place(ob, rot3=None, loc=(0, 0, 0)):
    ob.data.transform(M4(rot3, loc))
    return ob


def ring_pts(cx, cy, z, rx, ry, n, p=4.0):
    out = []
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        out.append(Vector((cx + rx * math.copysign(abs(c) ** (2.0 / p), c), cy + ry * math.copysign(abs(s) ** (2.0 / p), s), z)))
    return out


def set_uv(ob, fn):
    me = ob.data
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    uv = me.uv_layers.new(name="UVMap").data
    for p in me.polygons:
        for li in p.loop_indices:
            uv[li].uv = fn(me.vertices[me.loops[li].vertex_index].co, p.normal)
    return ob


def grain(axis, s=TEXEL):
    A = Vector(axis).normalized()

    def fn(p, n):
        side = n.cross(A)
        if side.length < 1e-4:
            side = Vector((0, 0, 1)) if abs(A.z) < 0.9 else Vector((1, 0, 0))
        side.normalize()
        return (p.dot(A) / s, p.dot(side) / s)
    return fn


def finish_split(world, own, name, sharp_deg=50):
    """finish() (world-scale UVs) for the upholstery and plain parts; the parts with their own UVs (wood along its grain,
    the dial) joined on after, smooth."""
    ob = finish(world, name, sharp_deg=sharp_deg)
    if not own:
        return ob
    for p in own:
        for f in p.data.polygons:
            f.use_smooth = True
    return join([ob] + list(own), name)


def run(models):
    for name, fn, view in models:
        if ONLY and name not in ONLY:
            continue
        reset()
        ob = fn()
        path = os.path.join(OUT, name + ".fbx")
        export(ob, path)
        tris = sum(len(p.vertices) - 2 for o in meshes(ob) for p in o.data.polygons)
        lo = [min((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)]
        hi = [max((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)]
        if PREVIEW:
            os.makedirs(PREVIEW, exist_ok=True)
            preview(ob, os.path.join(PREVIEW, name + ".png"), view)
        print("EXPORTED", name, "tris", tris, "size (x, depth, height)", [round(h - l, 3) for l, h in zip(lo, hi)],
              "mats", [m.name for m in ob.data.materials])


# ------------------------------------------------------------------------------------------------- upholstery

def cushion(name, cx, cy, z0, w, d, h, crown=0.012, sag=0.0, p=7.0, pipe=0.0045, tilt=None, n=56, material=None):
    """A box cushion: sides bulging a little, the top crowned and (sag) dished where it is sat on, piping round its top
    and bottom seams. Its middle at (cx, cy), bottom at z0, size w x d x h. Returns its parts."""
    m = material or FAB
    rings = []
    rings.append(ring_pts(cx, cy, z0 + 0.004, w / 2 - 0.006, d / 2 - 0.006, n, p))
    rings.append(ring_pts(cx, cy, z0 + h * 0.08, w / 2, d / 2, n, p))
    rings.append(ring_pts(cx, cy, z0 + h * 0.5, w / 2 + 0.004, d / 2 + 0.004, n, p))           # the sides bulge
    rings.append(ring_pts(cx, cy, z0 + h * 0.92, w / 2, d / 2, n, p))
    top = z0 + h
    for k, s in enumerate((0.97, 0.88, 0.7, 0.45, 0.2)):
        z = top + crown * (1 - s * s) - sag * (1 - s) ** 0.7 * 1.0
        rings.append(ring_pts(cx, cy, z, (w / 2) * s, (d / 2) * s, n, p))
    rings.append(Vector((cx, cy, top + crown - sag)))
    base = loft(name, rings, m, True, False, recalc=True)
    # The underside, flat.
    parts = [base]
    if pipe > 0:
        for z, inset in ((z0 + h * 0.92 + 0.002, 0.0), (z0 + h * 0.08 - 0.002, 0.0)):
            pts = ring_pts(cx, cy, z, w / 2 + 0.002, d / 2 + 0.002, n, p)
            pts.append(pts[0].copy())
            parts.append(tube(name + "Pipe", pts, pipe, m, 8, False, False))
    if tilt is not None:
        for o in parts:
            place(o, rot('X', tilt[0]), (0, 0, 0))
    return parts


def tufted_panel(name, origin, U, V, N, width, height, rows, cols, depth=0.022, puff=0.012, res=0.018, material=None, stagger=True):
    """A deep-buttoned panel: a grid surface facing N, origin at its bottom-left, spanning width along U and height along V,
    puffed between buttons set in a diamond (alternate rows offset by half), each button pulled in with the pleats
    running between neighbouring buttons and straight down from the lowest row to the edge. Returns (panel, buttons)."""
    m = material or FAB
    U, V, N, O = Vector(U).normalized(), Vector(V).normalized(), Vector(N).normalized(), Vector(origin)
    nu, nv = max(2, int(width / res)), max(2, int(height / res))
    du, dv = width / cols, height / (rows + 0.5)
    buttons = []
    for r in range(rows):
        off = (du / 2 if (stagger and r % 2) else 0.0)
        for c in range(cols + 1):
            u = c * du + off
            if u < du * 0.45 or u > width - du * 0.45:
                continue
            buttons.append((u, (r + 0.75) * dv))
    segs = []
    for i, (u1, v1) in enumerate(buttons):
        for j, (u2, v2) in enumerate(buttons):
            if j <= i:
                continue
            if abs(v2 - v1) - dv < 1e-6 and abs(abs(u2 - u1) - du / 2) < 1e-4 and stagger:
                segs.append((u1, v1, u2, v2))
            if not stagger and ((abs(v2 - v1) < 1e-6 and abs(abs(u2 - u1) - du) < 1e-4) or (abs(u2 - u1) < 1e-6 and abs(abs(v2 - v1) - dv) < 1e-4)):
                segs.append((u1, v1, u2, v2))
    low = min(v for _, v in buttons) if buttons else 0
    for (u, v) in buttons:
        if abs(v - low) < 1e-6:
            segs.append((u, v, u, 0.0))

    def seg_d(pu, pv, s):
        u1, v1, u2, v2 = s
        ax, ay = u2 - u1, v2 - v1
        L2 = ax * ax + ay * ay
        t = 0.0 if L2 < 1e-12 else max(0.0, min(1.0, ((pu - u1) * ax + (pv - v1) * ay) / L2))
        return math.hypot(pu - (u1 + ax * t), pv - (v1 + ay * t))

    bm = bmesh.new()
    grid = []
    for j in range(nv + 1):
        row = []
        for i in range(nu + 1):
            pu, pv = width * i / nu, height * j / nv
            edge = min(pu, width - pu, pv, height - pv)
            z = puff * min(1.0, edge / 0.05)
            for (bu, bv) in buttons:
                r2 = (pu - bu) ** 2 + (pv - bv) ** 2
                if r2 < 0.01:
                    z -= depth * math.exp(-r2 / (0.018 ** 2))
            for s in segs:
                d = seg_d(pu, pv, s)
                if d < 0.02:
                    z -= depth * 0.35 * math.exp(-(d / 0.006) ** 2)
            row.append(bm.verts.new(O + U * pu + V * pv + N * z))
        grid.append(row)
    for j in range(nv):
        for i in range(nu):
            f = (grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i])
            bm.faces.new(f)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    panel = new_obj(name, bm, [m])
    # Make the face point along N.
    me = panel.data
    if me.polygons and me.polygons[len(me.polygons) // 2].normal.dot(N) < 0:
        for poly in me.polygons:
            poly.flip()
    btn = []
    for (bu, bv) in buttons:
        c = O + U * bu + V * bv + N * (-depth * 0.75)
        btn.append(sphere(name + "Button", tuple(c), (0.011, 0.011, 0.006), m, 10, 6, rot=rot_to(N)))
    return panel, btn


def bun_foot(name, x, y, h=0.11):
    """A turned bun foot."""
    prof = [(0.0, 0.0), (0.026, 0.0), (0.034, 0.012), (0.042, 0.04), (0.04, 0.062), (0.03, 0.078), (0.024, 0.085), (0.024, h), (0.0, h)]
    ob = lathe(name, prof, WOOD, 20, xf=M4(None, (x, y, 0)))
    return set_uv(ob, grain((0, 0, 1)))


def tapered_foot(name, x, y, h, w0, w1, splay=(0, 0)):
    a, b = Vector((x + splay[0], y + splay[1], 0.0)), Vector((x, y, h))
    rings = [[q + a.lerp(b, k / 6) for q in ring_pts(0, 0, 0, w0 + (w1 - w0) * k / 6, w0 + (w1 - w0) * k / 6, 12, 3.5)] for k in range(7)]
    ob = loft(name, rings, WOOD, True, True, recalc=True)
    return set_uv(ob, grain(b - a))


# ------------------------------------------------------------------------------------------------- sofas

def chesterfield():
    """A chesterfield: the back and arms one height (76 cm), deep-buttoned inside in a diamond with the pleats between,
    a rolled top swept round the arms and back in one piece, the arms' fronts scrolled with pleats and a line of
    nailheads, a long bench seat cushion (crowned, piped), the plinth below with its piping, turned bun feet."""
    L, D, H = 2.05, 0.9, 0.78
    world, own = [], []
    world.append(rbox("Plinth", (L - 0.04, D - 0.05, 0.25), (0, 0.0, 0.11 + 0.125), FAB, 0.035, 3))
    pts = ring_pts(0, 0.0, 0.11 + 0.247, L / 2 - 0.02, D / 2 - 0.025, 64, 8)
    pts.append(pts[0].copy())
    world.append(tube("PlinthPipe", pts, 0.005, FAB, 8, False, False))
    world += cushion("Seat", 0, -0.05, 0.36, L - 0.42, D - 0.3, 0.11, crown=0.016, sag=0.008, p=9)
    # The back: its inside tufted, its outside plain, its top under the roll.
    bz0, bz1 = 0.42, H - 0.075
    by = D / 2 - 0.165                                   # (in front of the back's body, whose face is at D / 2 - 0.16)
    world.append(rbox("BackBody", (L - 0.36, 0.16, bz1 - bz0 + 0.05), (0, D / 2 - 0.08, (bz0 + bz1) / 2), FAB, 0.03, 3))
    panel, btn = tufted_panel("BackTuft", (-(L - 0.4) / 2, by, bz0), (1, 0, 0), (0, 0.04, 1), (0, -1, 0), L - 0.4, bz1 - bz0, 3, 8)
    world += [panel] + btn
    # The arms: tufted inside, plain outside.
    for s in (-1, 1):
        ax = s * (L / 2 - 0.11)
        world.append(rbox("ArmBody", (0.2, D - 0.06, H - 0.075 - 0.3), (ax, 0.0, 0.3 + (H - 0.375) / 2), FAB, 0.03, 3))
        inner = ax - s * 0.1
        panel, btn = tufted_panel("ArmTuft", (inner - s * 0.004, -D / 2 + 0.06, 0.42), (0, 1, 0), (0, 0, 1), (-s, 0, 0), D - 0.24, bz1 - 0.42, 2, 3)
        world += [panel] + btn
        # The roll's end: gathered into a button (shallow pleats from the rim in), the arm's front panel under it edged
        # with a line of nailheads down each side.
        fz, fy = H - 0.075, -D / 2 + 0.05
        world.append(lathe("Scroll", [(0.0, 0.0), (0.074, 0.0), (0.077, 0.004), (0.074, 0.008), (0.0, 0.012)], FAB, 32,
                           xf=M4(rot('X', 90), (ax, fy, fz))))
        for k in range(14):
            a = 2 * math.pi * k / 14
            pl = rbox("Gather", (0.004, 0.003, 0.05), (0, 0, 0), FAB, 0.0012)
            place(pl, rot('Y', math.degrees(a)), (ax + math.sin(a) * 0.042, fy - 0.0125, fz + math.cos(a) * 0.042))
            world.append(pl)
        world.append(sphere("ScrollButton", (ax, fy - 0.014, fz), (0.012, 0.006, 0.012), FAB, 12, 6))
        world.append(rbox("ArmFront", (0.19, 0.012, fz - 0.36), (ax, -D / 2 + 0.03, 0.36 + (fz - 0.36) / 2 - 0.03), FAB, 0.004, 2))
        for side in (-1, 1):
            for k in range(int((fz - 0.42) / 0.022)):
                world.append(sphere("Nail", (ax + side * 0.085, -D / 2 + 0.022, 0.38 + k * 0.022), (0.0055, 0.004, 0.0055), METAL, 8, 5))
    # The rolled top, one sweep from the front of one arm round the back to the front of the other.
    r = 0.075
    zt = H - r
    path = [Vector((-L / 2 + 0.11, -D / 2 + 0.05, zt)), Vector((-L / 2 + 0.11, D / 2 - 0.25, zt)), Vector((-L / 2 + 0.14, D / 2 - 0.12, zt)),
            Vector((-L / 2 + 0.3, D / 2 - 0.085, zt)), Vector((L / 2 - 0.3, D / 2 - 0.085, zt)), Vector((L / 2 - 0.14, D / 2 - 0.12, zt)),
            Vector((L / 2 - 0.11, D / 2 - 0.25, zt)), Vector((L / 2 - 0.11, -D / 2 + 0.05, zt))]
    sm = [Vector(p) for p in catmull([tuple(p) for p in path], 8)]
    world.append(tube("Roll", sm, r, FAB, 18, True, True))
    pipe = [p + Vector((0, 0, r * 0.95)) for p in sm]
    for k, (y0, z0) in enumerate(((0.0, 0.0),)):
        world.append(tube("RollPipe", pipe, 0.0045, FAB, 8, False, False))
    for x in (-L / 2 + 0.12, L / 2 - 0.12):
        for y in (-D / 2 + 0.11, D / 2 - 0.11):
            own.append(bun_foot("Foot", x, y))
    return finish_split(world, own, "Sofa_Chesterfield", 55)


def modern_sofa():
    """A 1990s office-lounge sofa: square track arms with a welt round their fronts, a frame with a tight back, three
    loose seat cushions (crowned, piped, the middle ones sagging most) and three loose back cushions leaning back, on
    tapered wood feet."""
    L, D = 2.1, 0.88
    world, own = [], []
    world.append(rbox("Base", (L - 0.02, D - 0.02, 0.2), (0, 0, 0.09 + 0.1), FAB, 0.03, 3))
    for s in (-1, 1):
        x = s * (L / 2 - 0.085)
        world.append(rbox("Arm", (0.17, D, 0.5), (x, 0, 0.09 + 0.25), FAB, 0.035, 3))
        front = ring_pts(x, -D / 2 - 0.001, 0, 0.085, 0.25, 40, 8)
        pts = [Vector((p.x, -D / 2 - 0.002, 0.34 + (p.y + 0.0))) for p in [Vector((q.x, q.y, 0)) for q in ring_pts(x, 0, 0, 0.083, 0.245, 40, 8)]]
        pts.append(pts[0].copy())
        world.append(tube("ArmWelt", pts, 0.0045, FAB, 8, False, False))
    world.append(rbox("BackFrame", (L - 0.32, 0.14, 0.5), (0, D / 2 - 0.07, 0.09 + 0.2 + 0.25), FAB, 0.03, 3))
    n = 3
    cw = (L - 0.34) / n
    for i in range(n):
        x = -L / 2 + 0.17 + cw * (i + 0.5)
        world += cushion("Seat", x, -0.06, 0.29, cw - 0.012, D - 0.24, 0.13, crown=0.014, sag=0.018 if i == 1 else 0.012, p=7)
        # A back cushion: built lying (0.42 tall, 0.15 thick), stood up with its crown to the room, leaning back on the frame.
        bc = cushion("Back", 0, 0, 0, cw - 0.014, 0.42, 0.15, crown=0.012, sag=0.0, p=7)
        for o in bc:
            place(o, rot('X', 90), (0, 0, 0))
            place(o, rot('X', -9), (x, D / 2 - 0.14, 0.42 + 0.21))
        world += bc
    for x in (-L / 2 + 0.07, L / 2 - 0.07):
        for y in (-D / 2 + 0.07, D / 2 - 0.07):
            own.append(tapered_foot("Foot", x, y, 0.09, 0.016, 0.024, (0.006 * math.copysign(1, x), 0.006 * math.copysign(1, y))))
    return finish_split(world, own, "Sofa_Modern", 55)


def armchair():
    """A rolled-arm armchair: a fitted back with a loose crowned cushion, the arms rolled over (a scroll front with
    pleats and a button), a piped T-seat cushion sitting into the arms, the front rail welted, on turned wood legs."""
    W, D = 0.95, 0.9
    world, own = [], []
    arm_w = 0.18
    inner = W / 2 - arm_w
    world.append(rbox("Base", (W - 0.02, D - 0.04, 0.26), (0, -0.01, 0.12 + 0.13), FAB, 0.035, 3))
    pts = ring_pts(0, -0.01, 0.12 + 0.258, W / 2 - 0.012, D / 2 - 0.022, 48, 8)
    pts.append(pts[0].copy())
    world.append(tube("BasePipe", pts, 0.005, FAB, 8, False, False))
    world += cushion("Seat", 0, -0.075, 0.375, inner * 2 - 0.01, D - 0.22, 0.13, crown=0.016, sag=0.014, p=6)
    world.append(rbox("Back", (inner * 2 + 0.1, 0.2, 0.6), (0, D / 2 - 0.12, 0.67), FAB, 0.06, 3))
    bc = cushion("BackCushion", 0, 0, 0, inner * 2 - 0.02, 0.4, 0.12, crown=0.014, p=6)
    for o in bc:
        place(o, rot('X', 90), (0, 0, 0))
        place(o, rot('X', -10), (0, D / 2 - 0.22, 0.505 + 0.2))
    world += bc
    for s in (-1, 1):
        x = s * (W / 2 - arm_w / 2)
        world.append(rbox("Arm", (arm_w - 0.01, D - 0.1, 0.3), (x, 0.02, 0.5), FAB, 0.05, 3))
        roll = [Vector((x, -D / 2 + 0.06 + (D - 0.1) * k / 8, 0.665 - 0.008 * math.sin(math.pi * k / 8))) for k in range(9)]
        world.append(tube("Roll", roll, 0.105, FAB, 22, True, True))
        fy = -D / 2 + 0.058
        world.append(lathe("Scroll", [(0.0, 0.0), (0.1, 0.0), (0.108, 0.008), (0.104, 0.016), (0.0, 0.02)], FAB, 32, xf=M4(rot('X', 90), (x, fy, 0.665))))
        for k in range(16):
            a = 2 * math.pi * k / 16
            pl = rbox("Gather", (0.004, 0.003, 0.075), (0, 0, 0), FAB, 0.0012)
            place(pl, rot('Y', math.degrees(a)), (x + math.sin(a) * 0.055, fy - 0.0195, 0.665 + math.cos(a) * 0.055))
            world.append(pl)
        world.append(sphere("ScrollButton", (x, fy - 0.022, 0.665), (0.014, 0.007, 0.014), FAB, 12, 6))
    for x in (-W / 2 + 0.1, W / 2 - 0.1):
        for y in (-D / 2 + 0.1, D / 2 - 0.1):
            prof = [(0.0, 0.0), (0.017, 0.0), (0.02, 0.02), (0.022, 0.06), (0.027, 0.09), (0.025, 0.1), (0.03, 0.115), (0.03, 0.125), (0.0, 0.125)]
            own.append(set_uv(lathe("Leg", prof, WOOD, 16, xf=M4(None, (x, y, 0))), grain((0, 0, 1))))
    return finish_split(world, own, "Armchair", 55)


# ------------------------------------------------------------------------------------------------- office chair

def office_chair():
    """A task chair: a contoured seat (50 cm wide, a waterfall front, dished) and a back with a lumbar curve, both
    upholstered over black plastic shells, joined by a steel spine; the mechanism under the seat with its paddles, the
    gas lift (chrome cylinder in a telescoping shroud), a five-star base with rounded spokes and twin-wheel casters, and
    T-arms with height posts and padded tops."""
    world = []
    sw, sd, sz = 0.5, 0.48, 0.47
    # The seat: rings across it from back to front, each a rounded section, the front edge rolling down.
    rings = []
    nx = 14
    for k in range(13):
        t = k / 12
        y = sd / 2 - sd * t - 0.02
        front = max(0.0, (t - 0.82) / 0.18)
        top = sz + 0.06 + 0.012 * math.sin(math.pi * min(1.0, t / 0.82)) - 0.045 * front * front
        dish = 0.015 * math.sin(math.pi * min(1.0, t / 0.85))
        ring = []
        for i in range(nx + 1):
            u = i / nx
            x = -sw / 2 + sw * u
            edge = min(u, 1 - u)
            z = top - dish * math.sin(math.pi * u) - 0.02 * max(0.0, (0.12 - edge) / 0.12) ** 2
            ring.append(Vector((x, y, z)))
        for i in range(nx, -1, -1):
            u = i / nx
            ring.append(Vector((-sw / 2 + sw * u, y, sz + 0.012)))
        rings.append(ring)
    seat = loft("Seat", rings, FAB, True, True, recalc=True)
    world.append(seat)
    world.append(rbox("SeatShell", (sw - 0.02, sd - 0.03, 0.02), (0, -0.01, sz + 0.004), PLASTIC, 0.008, 2))
    # The back: a cushion with a lumbar curve on its shell, up the spine.
    rings = []
    bw, bh = 0.46, 0.5
    for k in range(11):
        t = k / 10
        z = sz + 0.17 + bh * t
        y = 0.25 + 0.02 * math.sin(math.pi * t) - 0.025 * math.exp(-((t - 0.3) / 0.18) ** 2) + 0.03 * t * t
        ring = []
        for i in range(nx + 1):
            u = i / nx
            x = (-bw / 2 + bw * u) * (0.92 + 0.08 * math.sin(math.pi * t))
            wrap = 0.04 * (2 * u - 1) ** 2
            ring.append(Vector((x, y - 0.05 + wrap, z)))
        for i in range(nx, -1, -1):
            u = i / nx
            x = (-bw / 2 + bw * u) * (0.92 + 0.08 * math.sin(math.pi * t))
            ring.append(Vector((x, y + 0.02 + 0.04 * (2 * u - 1) ** 2 * 0.5, z)))
        rings.append(ring)
    world.append(loft("Back", rings, FAB, True, True, recalc=True))
    spine = catmull([(0, 0.05, sz - 0.01), (0, 0.2, sz - 0.01), (0, 0.29, sz + 0.06), (0, 0.29, sz + 0.25)], 5)
    rs = []
    for p in spine:
        rs.append([Vector(p) + Vector(o) for o in ((-0.025, 0, -0.006), (0.025, 0, -0.006), (0.025, 0, 0.006), (-0.025, 0, 0.006))])
    world.append(loft("Spine", rs, METAL, True, True, recalc=True))
    # The mechanism and its paddles.
    world.append(rbox("Mechanism", (0.2, 0.24, 0.05), (0, 0.0, sz - 0.03), PLASTIC, 0.008, 2))
    for s in (-1, 1):
        world.append(tube("Paddle", catmull([(s * 0.09, -0.05, sz - 0.035), (s * 0.17, -0.08, sz - 0.04), (s * 0.22, -0.09, sz - 0.03)], 4), 0.006, PLASTIC, 6))
    # The gas lift.
    world.append(lathe("Lift", [(0.0, 0.17), (0.014, 0.17), (0.014, sz - 0.05), (0.0, sz - 0.05)], METAL, 16))
    world.append(lathe("Shroud", [(0.0, 0.09), (0.032, 0.09), (0.032, 0.15), (0.029, 0.16), (0.029, 0.24), (0.026, 0.25), (0.0, 0.25)], PLASTIC, 20))
    world.append(lathe("Hub", [(0.0, 0.06), (0.055, 0.06), (0.06, 0.08), (0.05, 0.12), (0.0, 0.12)], PLASTIC, 20))
    reach = 0.32
    for i in range(5):
        a = i * 2 * math.pi / 5
        d = Vector((math.sin(a), math.cos(a), 0))
        pts = [Vector((0, 0, 0.1)) + d * 0.03, d * 0.15 + Vector((0, 0, 0.096)), d * 0.29 + Vector((0, 0, 0.083)), d * reach + Vector((0, 0, 0.078))]
        rad = [0.026, 0.021, 0.017, 0.015]
        world.append(tube("Spoke", pts, rad, PLASTIC, 10))
        tip = d * (reach - 0.015)
        world.append(lathe("Stem", [(0.0, 0.03), (0.008, 0.03), (0.008, 0.08), (0.0, 0.08)], METAL, 8, xf=M4(None, (tip.x, tip.y, 0))))
        hood = rbox("Hood", (0.05, 0.034, 0.03), (0, 0, 0), PLASTIC, 0.012, 3)
        place(hood, rot('Z', -math.degrees(a)), (tip.x, tip.y, 0.05))
        world.append(hood)
        tan = Vector((math.cos(a), -math.sin(a), 0))
        for s in (-1, 1):
            c = tip + tan * s * 0.021 + Vector((0, 0, 0.025))
            R3 = rot_to(tan)
            world.append(lathe("Wheel", [(0.0, -0.007), (0.02, -0.007), (0.025, -0.004), (0.025, 0.004), (0.02, 0.007), (0.0, 0.007)], PLASTIC, 16, xf=M4(R3, tuple(c))))
    # T-arms.
    for s in (-1, 1):
        x = s * (sw / 2 + 0.03)
        world.append(rbox("ArmBracket", (0.1, 0.05, 0.016), (s * (sw / 2 - 0.02), 0.0, sz - 0.01), PLASTIC, 0.005))
        world.append(rbox("ArmPost", (0.034, 0.05, 0.25), (x, 0.0, sz + 0.11), PLASTIC, 0.01, 2))
        world.append(rbox("ArmPad", (0.075, 0.25, 0.03), (x, -0.01, sz + 0.245), FAB, 0.012, 3))
        world.append(rbox("ArmButton", (0.008, 0.02, 0.012), (x - s * 0.02, -0.02, sz + 0.2), PLASTIC, 0.003))
    return finish(world, "OfficeChair", sharp_deg=55)


# ------------------------------------------------------------------------------------------------- lamp

def floor_lamp():
    """A floor lamp: a weighted dome base on a felt pad, the pole in sections with their couplings, the socket with its
    pull chain, the harp holding the shade on a finial, a drum shade (fabric, bound top and bottom, open) on its wire
    spider, the cord out of the base. Shade 1.38-1.70 m, 0.225 / 0.185 m round (Interaction.FloorLamp)."""
    world = []
    world.append(lathe("Base", [(0.0, 0.0), (0.16, 0.0), (0.165, 0.006), (0.162, 0.014), (0.14, 0.024), (0.06, 0.036), (0.03, 0.04), (0.0, 0.04)], METAL, 40))
    world.append(lathe("Felt", [(0.0, -0.002), (0.15, -0.002), (0.15, 0.001), (0.0, 0.001)], PLASTIC, 32))
    for (z0, z1) in ((0.04, 0.5), (0.5, 0.98), (0.98, 1.36)):
        world.append(lathe("Pole", [(0.0, z0), (0.0115, z0), (0.0115, z1), (0.0, z1)], METAL, 14))
        world.append(lathe("Coupling", [(0.0, z1 - 0.012), (0.0145, z1 - 0.012), (0.0155, z1 - 0.006), (0.0145, z1), (0.0, z1)], METAL, 16))
    world.append(lathe("Socket", [(0.0, 1.36), (0.02, 1.36), (0.022, 1.38), (0.02, 1.43), (0.0, 1.43)], METAL, 18))
    world.append(tube("Chain", [(0.018, 0, 1.40), (0.03, 0, 1.33), (0.034, 0, 1.25)], 0.0012, METAL, 4))
    world.append(sphere("ChainEnd", (0.034, 0, 1.245), (0.004, 0.004, 0.006), METAL, 8, 6))
    # The harp (a wire arch from the socket up over the bulb) and its finial.
    harp = [Vector((math.sin(math.radians(a)) * 0.09, 0, 1.42 + 0.26 * math.cos(math.radians(a)) * 0.0 + 0.25 * (1 - abs(math.cos(math.radians(a))) ** 0.0) * 0 + 0.0)) for a in ()]
    pts = []
    for k in range(13):
        t = k / 12
        a = math.pi * t
        pts.append(Vector((math.cos(a) * 0.09, 0, 1.42 + math.sin(a) * 0.27)))
    world.append(tube("Harp", pts, 0.0025, METAL, 6))
    world.append(lathe("Finial", [(0.0, 1.69), (0.006, 1.69), (0.012, 1.7), (0.014, 1.715), (0.01, 1.73), (0.0, 1.735)], METAL, 14))
    # The shade: fabric (both sides), bound rims, the spider's arms.
    zb, zt, rb, rt = 1.38, 1.70, 0.225, 0.185
    world.append(lathe("Shade", [(rb, zb), (rt, zt)], mat("Shade", (0.82, 0.76, 0.62), 0.9), 48, cap_bottom=False, cap_top=False))
    world.append(lathe("ShadeIn", [(rt - 0.002, zt), (rb - 0.002, zb)], mat("Shade", (0.82, 0.76, 0.62), 0.9), 48, cap_bottom=False, cap_top=False))
    for z, r in ((zb, rb), (zt, rt)):
        world.append(lathe("Binding", [(r - 0.004, z - 0.004), (r + 0.003, z - 0.004), (r + 0.004, z), (r + 0.003, z + 0.004), (r - 0.004, z + 0.004)], FAB, 48, cap_bottom=False, cap_top=False))
    for k in range(3):
        a = 2 * math.pi * k / 3
        world.append(tube("Spider", [(0, 0, 1.69), (math.cos(a) * (rt - 0.004), math.sin(a) * (rt - 0.004), 1.695)], 0.0015, METAL, 4))
    world.append(lathe("Bulb", [(0.0, 1.43), (0.012, 1.43), (0.02, 1.45), (0.03, 1.48), (0.031, 1.5), (0.025, 1.52), (0.0, 1.53)], mat("Glass", (0.95, 0.92, 0.85), 0.2), 18))
    world.append(tube("Cord", catmull([(0.0, 0.14, 0.012), (0.03, 0.2, 0.004), (0.1, 0.22, 0.003), (0.16, 0.16, 0.003), (0.2, 0.06, 0.003)], 5), 0.003, PLASTIC, 6))
    return finish(world, "FloorLamp", sharp_deg=55)


# ------------------------------------------------------------------------------------------------- table

def table_module():
    """One section of a modular conference table, 1 m long (the game lays several end to end): the laminate top, 32 mm,
    its edges in dark T-moulding with a rounded nose, the particleboard underside, two steel support rails. Origin on
    the floor at its middle; top at 0.74 m; 1.1 m deep."""
    edge = mat("Edge", (0.06, 0.055, 0.05), 0.45)
    lam = mat("Laminate", (0.8, 0.72, 0.6), 0.4)
    under = mat("Underside", (0.42, 0.36, 0.28), 0.9)
    L, D, T, top = 1.0, 1.1, 0.032, 0.74
    world, own = [], []
    t = rbox("Top", (L - 0.006, D - 0.006, 0.002), (0, 0, top - 0.001), lam, 0.0)
    own.append(set_uv(t, grain((1, 0, 0))))
    world.append(rbox("Core", (L - 0.008, D - 0.008, T - 0.004), (0, 0, top - T / 2 - 0.001), under, 0.0))
    # The T-moulding round the edge: a band with a rounded nose.
    for (sx, sy, cx, cy) in ((L, 0.008, 0, -D / 2 + 0.004), (L, 0.008, 0, D / 2 - 0.004), (0.008, D - 0.016, -L / 2 + 0.004, 0), (0.008, D - 0.016, L / 2 - 0.004, 0)):
        world.append(rbox("EdgeBand", (sx, sy, T + 0.001), (cx, cy, top - T / 2), edge, 0.003, 3))
    for y in (-0.3, 0.3):
        world.append(rbox("Rail", (L - 0.1, 0.03, 0.04), (0, y, top - T - 0.02), METAL, 0.004, 2))
    return finish_split(world, own, "ConferenceTable_Module", 50)


def table_leg():
    """A T-leg: a steel column on a foot bar across the table's depth (rounded caps, levelling glides), a plate up under
    the top. Origin on the floor at its middle."""
    world = []
    h = 0.708
    world.append(rbox("Column", (0.08, 0.06, h - 0.07), (0, 0, 0.035 + (h - 0.07) / 2), METAL, 0.006, 2))
    world.append(rbox("Foot", (0.07, 0.78, 0.035), (0, 0, 0.012 + 0.0175), METAL, 0.012, 3))
    for y in (-0.39, 0.39):
        world.append(lathe("Cap", [(0.0, 0.0), (0.024, 0.0), (0.024, 0.03), (0.0, 0.03)], PLASTIC, 16, xf=M4(rot('X', 90), (0, y + math.copysign(0.002, y), 0.03))))
        world.append(lathe("Glide", [(0.0, 0.0), (0.018, 0.0), (0.018, 0.006), (0.006, 0.01), (0.006, 0.014), (0.0, 0.014)], PLASTIC, 14, xf=M4(None, (0, y * 0.9, 0.0))))
    world.append(rbox("Plate", (0.12, 0.7, 0.008), (0, 0, h - 0.004), METAL, 0.003))
    return finish(world, "ConferenceTable_Leg", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- clock

def wall_clock():
    """An office wall clock (34 cm): a black plastic case with a rounded rim and a stepped bezel, the printed dial (slot
    ClockDial: hours, minutes, numerals), a domed glass, the hour and minute hands (tapered, with their counterweights),
    the red second hand, the centre cap (no glass: see below). Origin at the wall, its face toward -Y."""
    rimm = mat("Plastic", (0.05, 0.05, 0.055), 0.4)
    dialm = mat("ClockDial", (1, 1, 1), 0.4)
    glass = mat("Glass", (0.95, 0.95, 0.95), 0.05)
    red = mat("Hand", (0.7, 0.05, 0.04), 0.4)
    blk = mat("Metal", (0.03, 0.03, 0.03), 0.4, 0.5)
    X = rot('X', 90)
    world = [lathe("Case", [(0.0, 0.0), (0.168, 0.0), (0.171, 0.006), (0.172, 0.03), (0.168, 0.042), (0.16, 0.047), (0.152, 0.046), (0.15, 0.04), (0.15, 0.012), (0.0, 0.012)],
                   rimm, 64, xf=M4(X, (0, 0, 0)))]
    # (No glass over the dial: props have no see-through glass in the game, and an opaque dome hid the dial and hands.
    # A thin bright ring where the glass meets the bezel stands for it.)
    world.append(lathe("GlassEdge", [(0.15, 0.04), (0.152, 0.04), (0.152, 0.043), (0.149, 0.043)], blk, 64, cap_bottom=False, cap_top=False, xf=M4(X, (0, 0, 0))))
    # The dial.
    bm = bmesh.new()
    vs = [bm.verts.new((0.15 * math.cos(2 * math.pi * i / 64), -0.0125, 0.15 * math.sin(2 * math.pi * i / 64))) for i in range(64)]
    bm.faces.new(vs)
    dial = new_obj("Dial", bm, [dialm])
    set_uv(dial, lambda p, n: (p.x / 0.3 + 0.5, p.z / 0.3 + 0.5))

    def hand(name, length, w, tail, ang, y, m):
        pts = [(-w * 0.6, -tail), (w * 0.6, -tail), (w, 0.0), (w * 0.45, length * 0.85), (0.0, length), (-w * 0.45, length * 0.85), (-w, 0.0)]
        bm = bmesh.new()
        top = [bm.verts.new((x, y, z)) for x, z in pts]
        bot = [bm.verts.new((x, y + 0.0012, z)) for x, z in pts]
        bm.faces.new(top); bm.faces.new(list(reversed(bot)))
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            bm.faces.new((top[i], top[j], bot[j], bot[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        ob = new_obj(name, bm, [m])
        place(ob, rot('Y', ang), (0, 0, 0))
        return ob
    world.append(hand("Hour", 0.085, 0.007, 0.02, -64, -0.015, blk))
    world.append(hand("Minute", 0.125, 0.005, 0.025, 112, -0.0165, blk))
    world.append(hand("Second", 0.13, 0.0012, 0.035, 200, -0.018, red))
    world.append(lathe("Cap", [(0.0, 0.0), (0.007, 0.0), (0.006, 0.003), (0.0, 0.004)], blk, 14, xf=M4(rot('X', 90), (0, -0.0185, 0))))
    return finish_split(world, [dial], "WallClock", 50)


V = (0.75, -1.0, 0.55)
run([
    ("Sofa_Chesterfield", chesterfield, V),
    ("Sofa_Modern", modern_sofa, V),
    ("Armchair", armchair, (0.6, -1.0, 0.55)),
    ("OfficeChair", office_chair, (0.7, -1.0, 0.45)),
    ("FloorLamp", floor_lamp, (0.6, -1.0, 0.3)),
    ("ConferenceTable_Module", table_module, (0.6, -1.0, 0.6)),
    ("ConferenceTable_Leg", table_leg, (0.9, -0.6, 0.4)),
    ("WallClock", wall_clock, (0.3, -1.0, 0.2)),
])
print("[furniture] done ->", OUT)
