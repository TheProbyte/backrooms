"""
The good rooms' furniture (2026-11-04, the realism pass): break room, security office, janitor's closet and stock room,
built procedurally in Blender at the sizes of real products, then aged with a baked colour texture (bake_kit.py). Run
headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_rooms.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>] [--only Fridge,Microwave]

Uses build_fittings.py's helpers (and through it build_items2.py's: lathe, loft, tube, rbox, finish, export ...) and
bake_kit.py (the bake, prints). Sizes:
  - Fridge: a 28" top-freezer (0.72 x 1.72 x 0.70 m, freezer door a little under a third of the height);
  - Microwave: a 0.9 cu ft countertop oven (0.50 x 0.29 x 0.37);
  - CrtMonitor: a 15" CRT (bezel 0.38 x 0.355, 0.38 deep with its bell) on a swivel foot; Keyboard: a 104-key board;
  - Desk: a 66 x 30" steel "tanker" desk, a woodgrain laminate top in a black T-moulding, one pedestal of drawers;
  - DiningTable: a 48 x 30" break-room table, speckled laminate on a steel apron and legs;
  - MopSink: a 24 x 20 x 10" moulded-stone mop basin; ServiceFaucet: a wall service faucet with its vacuum breaker,
    hose and pail hook; Mop: a string mop leaning by it;
  - BreakerPanel: a surface load centre with its cover, door, labels and conduit (the main switch is the game's);
  - Shelving_100 / Shelving_110: boltless rivet shelving (keyhole angle posts, beams, particleboard decks);
  - CounterCabinet: a 600 mm base cabinet (drawer over door) the game repeats along a counter; CounterSink: a
    drop-in stainless sink and its faucet; PaintCan, CleanerJug: what stands on the shelves.

Frames as the game places them (Blender: front toward -Y, Z up; Unity (x, y, z) = (x, z, -y)): origin on the floor at
the middle of the foot (CounterSink: on the worktop at the bowl's middle; wall things are built in their prop's frame).
Material slots are named finish first ("Enamel_Fridge"): the game gives each its gloss and grain (BakedMaterials).
"""
import os
import sys
import math
import random

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_fittings.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- fire")
exec(compile(_src[:_cut], "build_fittings_helpers", "exec"))
exec(compile(open(os.path.join(HERE, "bake_kit.py"), encoding="utf-8").read(), "bake_kit", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix


# ------------------------------------------------------------------------------------------------- helpers

def S(name, srgb, finish, rough=0.5, metal=0.0):
    return smat(name, srgb, rough, metal, finish)


def card(name, w, h, center, material, normal=(0, -1, 0), roll=0.0):
    """A flat print (one quad) facing normal, UVs 0..1 over it (u along its width, v up)."""
    n = Vector(normal).normalized()
    up = Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((0, 1, 0))
    right = up.cross(n).normalized() if abs(n.z) < 0.9 else Vector((1, 0, 0))
    if abs(n.z) >= 0.9 and n.z < 0:
        right = -right
    up = n.cross(right).normalized()
    if roll:
        R = Matrix.Rotation(math.radians(roll), 3, n)
        right, up = R @ right, R @ up
    # u runs along "right" as seen from the front: the front is -n... seen from where the normal points
    c = Vector(center)
    if globals().get("OVERFLOW"):
        c = c + n * 0.0025          # (stood off a little: the chunkier look's thin plates grow up to 2 mm a face)
    pts = [c + right * (-w / 2) * s1 + up * (h / 2) * s2 for s1, s2 in ((1, -1), (-1, -1), (-1, 1), (1, 1))]
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    f = bm.faces.new(vs)
    f.normal_update()
    if f.normal.dot(n) < 0:
        f.normal_flip()
    ob = new_obj(name, bm, [material])
    me = ob.data
    uv = me.uv_layers.new(name="UVMap").data
    for p in me.polygons:
        p.use_smooth = False
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co - c
            uv[li].uv = (v.dot(right) / w + 0.5, v.dot(up) / h + 0.5)
    return ob


def rrect(w, d, r, z, nc=4, cx=0.0, cy=0.0):
    """A rounded rectangle in the XY plane at height z, counter-clockwise seen from +Z, 4 * (nc + 1) points."""
    r = min(r, w / 2 - 1e-4, d / 2 - 1e-4)
    out = []
    for k, (sx, sy, a0) in enumerate(((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270))):
        ccx, ccy = cx + sx * (w / 2 - r), cy + sy * (d / 2 - r)
        for i in range(nc + 1):
            a = math.radians(a0 + 90 * i / nc)
            out.append(Vector((ccx + r * math.cos(a), ccy + r * math.sin(a), z)))
    return out


def rrect_xz(w, h, r, y, nc=4, cx=0.0, cz=0.0):
    """The same in the XZ plane (a front face) at depth y."""
    return [Vector((p.x, y, p.y)) for p in rrect(w, h, r, 0.0, nc, cx, cz)]


def tube_y(name, x, z, y0, y1, r, material, seg=12):
    return lathe(name, [(0.0, 0.0), (r, 0.0), (r, y1 - y0), (0.0, y1 - y0)], material, seg,
                 xf=M4(rot('X', -90), (x, y0, z)))


def bar_pull(name, center, length, axis, material, depth=0.032, r=0.0055, foot=0.012):
    """A stainless bar pull standing off its front on two posts (axis 'X' or 'Z'), its front toward -Y."""
    c = Vector(center)
    A = Vector((1, 0, 0)) if axis == "X" else Vector((0, 0, 1))
    parts = []
    bar = lathe(name, [(0.0, 0.0), (r, 0.0), (r * 1.02, 0.002), (r * 1.02, length - 0.002), (r, length), (0.0, length)],
                material, 14, xf=M4(rot_to(A), c - A * (length / 2) + Vector((0, -depth, 0))))
    parts.append(bar)
    for s in (-1, 1):
        p = c + A * (s * (length / 2 - foot * 1.6))
        parts.append(lathe(name + "_post", [(0.0, 0.0), (r * 0.9, 0.0), (r * 0.8, depth), (0.0, depth)], material, 12,
                           xf=M4(rot('X', 90), p)))
    return parts


def bulge_grid(name, w, h, y_edge, y_mid, cz, material, n=14):
    """A gently convex rectangle facing -Y (a CRT's glass): y_edge at its rim, y_mid at its middle."""
    bm = bmesh.new()
    vs = []
    for j in range(n + 1):
        row = []
        for i in range(n + 1):
            u, v = i / n * 2 - 1, j / n * 2 - 1
            y = y_edge + (y_mid - y_edge) * (1 - u * u * 0.85) * (1 - v * v * 0.85)
            row.append(bm.verts.new((u * w / 2, y, cz + v * h / 2)))
        vs.append(row)
    for j in range(n):
        for i in range(n):
            bm.faces.new((vs[j][i], vs[j + 1][i], vs[j + 1][i + 1], vs[j][i + 1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.y > 0:
            f.normal_flip()
    return new_obj(name, bm, [material])


def run_baked(models):
    """Build, bake, export and (with --preview) a lit picture of each model."""
    for name, fn, view, kw in models:
        if ONLY and name not in ONLY:
            continue
        reset()
        for im in list(bpy.data.images):
            bpy.data.images.remove(im)
        ob = fn()
        info = bake(ob, name, OUT, **kw)
        path = os.path.join(OUT, name + ".fbx")
        export(ob, path)
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        lo = Vector([min((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
        hi = Vector([max((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
        print("EXPORTED", name, "tris", tris, "size", tuple(round(c, 3) for c in hi - lo), "bake", info["res"],
              "uv/m", info["uv_per_m"], "mats", [m.name for m in ob.data.materials])
        if PREVIEW:
            os.makedirs(PREVIEW, exist_ok=True)
            preview_lit(ob, os.path.join(PREVIEW, name + ".png"), albedo=bpy.data.images[name + "_Albedo"], view=view)


# ------------------------------------------------------------------------------------------------- break room

def fridge():
    E = S("Enamel_Fridge", (0.86, 0.85, 0.8), "enamel", 0.3)
    RUB = S("Rubber_FridgeGasket", (0.05, 0.05, 0.05), "rubber", 0.6)
    GR = S("Plastic_FridgeGrille", (0.07, 0.07, 0.07), "plastic", 0.6)
    HND = S("Plastic_FridgeHandle", (0.8, 0.79, 0.75), "plastic", 0.35)
    HNG = S("Plastic_FridgeHinge", (0.84, 0.83, 0.79), "plastic", 0.4)
    STL = S("Steel_FridgeHinge", (0.55, 0.55, 0.55), "metal", 0.4, 1.0)
    W_, H_, D_ = 0.72, 1.72, 0.70
    p = [rbox("Cabinet", (W_, 0.64, 1.67), (0, 0.03, 0.03 + 0.835), E, 0.012, 3)]
    # Doors: the freezer above, the fresh food below, rounded at the front, a gasket behind each.
    for z0, z1 in ((1.213, 1.70), (0.105, 1.203)):
        hz, zc = z1 - z0, (z0 + z1) / 2
        p.append(rbox("Door", (0.716, 0.055, hz), (0, -0.3175, zc), E, 0.016, 4))
        p.append(rbox("Gasket", (0.694, 0.016, hz - 0.022), (0, -0.287, zc), RUB, 0.005, 2))
    # Handles: plastic bars on standoffs on the opening side, near the doors' meeting.
    for z0, z1 in ((1.235, 1.50), (0.86, 1.18)):
        L = z1 - z0
        x = -0.305
        p.append(rbox("Handle", (0.028, 0.024, L), (x, -0.345 - 0.046, (z0 + z1) / 2), HND, 0.0105, 3))
        for zz in (z0 + 0.018, z1 - 0.018):
            p.append(rbox("Standoff", (0.026, 0.048, 0.036), (x, -0.345 - 0.024, zz), HND, 0.009, 2))
    # The hinges: a cap on top, a plate between the doors, one under the lower door.
    p.append(rbox("HingeCap", (0.064, 0.105, 0.026), (0.3, -0.305, 1.712), HNG, 0.009, 2))
    p.append(rbox("HingeMid", (0.046, 0.07, 0.009), (0.33, -0.31, 1.208), STL, 0.003, 1))
    p.append(rbox("HingeLow", (0.046, 0.065, 0.01), (0.33, -0.31, 0.099), STL, 0.003, 1))
    # The toe grille: a frame, louvres, a dark back; two levelling feet at the front.
    p.append(rbox("GrilleBack", (0.64, 0.006, 0.06), (0, -0.262, 0.068), GR, 0.0, 1))
    for zz in (0.042, 0.094):
        p.append(rbox("GrilleRail", (0.66, 0.02, 0.008), (0, -0.282, zz), GR, 0.003, 1))
    for xx in (-0.326, 0.326):
        p.append(rbox("GrilleEnd", (0.008, 0.02, 0.06), (xx, -0.282, 0.068), GR, 0.003, 1))
    for k in range(4):
        lv = rbox("Louvre", (0.644, 0.016, 0.0045), (0, 0, 0), GR, 0.0015, 1)
        place(lv, rot('X', 30), (0, -0.28, 0.054 + k * 0.0115))
        p.append(lv)
    for xx in (-0.31, 0.31):
        p.append(lathe("Foot", [(0.0, 0.0), (0.017, 0.0), (0.017, 0.006), (0.01, 0.01), (0.01, 0.032), (0.0, 0.032)], RUB, 12,
                       xf=M4(None, (xx, -0.25, 0.0))))
    # The coil at the back (seen from behind or beside): a black grid.
    for k in range(9):
        p.append(rbox("Coil", (0.56, 0.006, 0.006), (0, 0.357, 0.35 + k * 0.12), GR, 0.0, 1))
    for xx in (-0.27, 0.27):
        p.append(rbox("CoilSide", (0.008, 0.008, 1.0), (xx, 0.357, 0.83), GR, 0.0, 1))
    base = finish(p, "Fridge", sharp_deg=50)
    # Prints: a badge on the freezer door, a notice held by a magnet, a small note.
    c = Canvas(160, 32, (0.78, 0.79, 0.8))
    c.frame(0, 0, 160, 32, 2, (0.5, 0.5, 0.52))
    c.text_c("FROST FREE", 80, 9, 2, (0.12, 0.12, 0.14), 1.05)
    badge = card("Badge", 0.12, 0.024, (-0.19, -0.3456, 1.655), print_mat("Chrome_FridgeBadge", c))
    n = Canvas(200, 260, (0.93, 0.91, 0.84))
    n.text_c("PLEASE", 100, 22, 4, (0.08, 0.08, 0.08))
    n.text_c("LABEL AND DATE", 100, 62, 2, (0.08, 0.08, 0.08))
    n.text_c("YOUR FOOD", 100, 84, 2, (0.08, 0.08, 0.08))
    n.lines(24, 118, 176, 7, 9, (0.35, 0.35, 0.35), seed=3)
    n.text_c("UNLABELED ITEMS", 100, 192, 2, (0.55, 0.06, 0.05))
    n.text_c("THROWN OUT FRIDAY", 100, 210, 2, (0.55, 0.06, 0.05))
    n.text("- MGMT", 120, 236, 2, (0.1, 0.1, 0.1))
    notice = card("Notice", 0.2, 0.26, (0.06, -0.3457, 0.98), print_mat("Paper_FridgeNotice", n), roll=-2.0)
    s = Canvas(100, 80, (0.98, 0.93, 0.55))
    s.text_c("CLEANED", 50, 16, 2, (0.15, 0.15, 0.5))
    s.text_c("FRIDAYS", 50, 36, 2, (0.15, 0.15, 0.5))
    s.lines(16, 58, 84, 2, 7, (0.4, 0.4, 0.6), seed=5)
    note = card("Note", 0.085, 0.068, (-0.17, -0.3457, 0.62), print_mat("Paper_FridgeNote", s), roll=4.0)
    MAG = S("Gloss_Magnet", (0.62, 0.08, 0.06), "plastic", 0.3)
    MAG2 = S("Gloss_MagnetB", (0.12, 0.3, 0.62), "plastic", 0.3)
    mags = [lathe("Magnet", [(0.0, 0.0), (0.013, 0.0), (0.013, 0.004), (0.011, 0.007), (0.0, 0.007)], MAG, 16,
                  xf=M4(rot('X', 90), (0.06, -0.3457, 1.098))),
            lathe("Magnet2", [(0.0, 0.0), (0.011, 0.0), (0.011, 0.004), (0.009, 0.006), (0.0, 0.006)], MAG2, 16,
                  xf=M4(rot('X', 90), (-0.17, -0.3457, 0.65)))]
    mob = finish(mags, "Mags", sharp_deg=50)
    return join([base, badge, notice, note, mob], "Fridge")


def microwave():
    CASE = S("Paint_MicroCase", (0.86, 0.84, 0.78), "paint", 0.35)
    DOOR = S("Plastic_MicroDoor", (0.88, 0.86, 0.8), "plastic", 0.35)
    PANEL = S("Plastic_MicroPanel", (0.22, 0.22, 0.22), "plastic", 0.4)
    RUB = S("Rubber_MicroFeet", (0.05, 0.05, 0.05), "rubber", 0.6)
    p = [rbox("Case", (0.5, 0.36, 0.268), (0, 0.005, 0.012 + 0.134), CASE, 0.012, 3)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(lathe("Foot", [(0.0, 0.0), (0.012, 0.0), (0.012, 0.012), (0.0, 0.012)], RUB, 10,
                           xf=M4(None, (sx * 0.2, sy * 0.14, 0.0))))
    p.append(rbox("Door", (0.355, 0.02, 0.252), (-0.068, -0.185, 0.146), DOOR, 0.006, 2))
    p.append(rbox("Pull", (0.03, 0.028, 0.2), (0.088, -0.205, 0.146), DOOR, 0.009, 2))
    # The window's raised frame.
    for (sx, sz, cx, cz) in ((0.27, 0.008, -0.088, 0.233), (0.27, 0.008, -0.088, 0.067), (0.008, 0.174, -0.219, 0.15), (0.008, 0.174, 0.043, 0.15)):
        p.append(rbox("WinFrame", (sx, 0.004, sz), (cx, -0.1965, cz), DOOR, 0.0015, 1))
    p.append(rbox("Panel", (0.128, 0.012, 0.252), (0.181, -0.181, 0.146), PANEL, 0.004, 2))
    base = finish(p, "Microwave", sharp_deg=50)
    # Window: a dark screen of holes; the keypad's print; the vent slots on its side.
    w = Canvas(256, 160, (0.03, 0.03, 0.03))
    for y in range(4, 156, 6):
        for x in range(4 + (y // 6 % 2) * 3, 252, 6):
            w.disc(x, y, 1.6, (0.13, 0.12, 0.11))
    win = card("Window", 0.252, 0.158, (-0.088, -0.1952, 0.15), print_mat("Screen_MicroWindow", w))
    k = Canvas(128, 256, (0.2, 0.2, 0.21))
    k.rect(10, 12, 118, 46, (0.1, 0.13, 0.11))
    k.text_c("12:00", 64, 20, 3, (0.25, 0.5, 0.3))
    labels = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "+", "0", "-"]
    for i, lab in enumerate(labels):
        cx, cy = 26 + (i % 3) * 38, 66 + (i // 3) * 30
        k.rect(cx - 15, cy - 11, cx + 15, cy + 11, (0.34, 0.34, 0.35))
        k.text_c(lab, cx, cy - 5, 1.5, (0.9, 0.9, 0.88))
    k.rect(10, 190, 118, 214, (0.12, 0.42, 0.16))
    k.text_c("START", 64, 197, 1.5, (0.95, 0.95, 0.9))
    k.rect(10, 222, 118, 246, (0.6, 0.1, 0.08))
    k.text_c("STOP", 64, 229, 1.5, (0.95, 0.95, 0.9))
    keys = card("Keypad", 0.118, 0.236, (0.181, -0.1872, 0.146), print_mat("Print_MicroKeypad", k))
    v = Canvas(128, 64, (0.86, 0.84, 0.78))
    for i in range(10):
        v.rect(8 + i * 12, 8, 14 + i * 12, 56, (0.05, 0.05, 0.05))
    vent = card("Vent", 0.15, 0.075, (0.2502, 0.03, 0.2), print_mat("Paint_MicroVent", v), normal=(1, 0, 0))
    return join([base, win, keys, vent], "Microwave")


def dining_table():
    LAM = S("Laminate_BreakTable", (0.76, 0.75, 0.71), "laminate", 0.4)
    LAM["speckle"] = (180.0, 0.45, 0.44, 0.42, 0.9, 0.89, 0.86)
    EDGE = S("Rubber_BreakTableEdge", (0.05, 0.05, 0.05), "rubber", 0.5)
    STL = S("Paint_BreakTableSteel", (0.12, 0.12, 0.12), "paint", 0.45)
    UND = S("Board_BreakTableUnder", (0.55, 0.45, 0.33), "board", 0.8)
    UND["speckle"] = (220.0, 0.35, 0.27, 0.18, 0.72, 0.62, 0.48)
    GLD = S("Plastic_BreakTableGlide", (0.06, 0.06, 0.06), "plastic", 0.5)
    W_, D_, H_ = 1.2, 0.8, 0.74
    p = [rbox("Top", (W_ - 0.008, D_ - 0.008, 0.028), (0, 0, H_ - 0.014), LAM, 0.0015, 1),
         rbox("Under", (W_ - 0.02, D_ - 0.02, 0.002), (0, 0, H_ - 0.0285), UND, 0.0, 1)]
    for (sx, sy, cx, cy) in ((W_, 0.006, 0, -D_ / 2 + 0.003), (W_, 0.006, 0, D_ / 2 - 0.003),
                             (0.006, D_ - 0.012, -W_ / 2 + 0.003, 0), (0.006, D_ - 0.012, W_ / 2 - 0.003, 0)):
        p.append(rbox("TMould", (sx, sy, 0.032), (cx, cy, H_ - 0.015), EDGE, 0.0025, 2))
    ix, iy = W_ / 2 - 0.075, D_ / 2 - 0.075
    for (sx, sy, cx, cy) in ((2 * ix, 0.018, 0, -iy), (2 * ix, 0.018, 0, iy), (0.018, 2 * iy, -ix, 0), (0.018, 2 * iy, ix, 0)):
        p.append(rbox("Apron", (sx, sy, 0.07), (cx, cy, H_ - 0.029 - 0.035), STL, 0.003, 1))
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (ix - 0.006), sy * (iy - 0.006)
            p.append(rbox("Leg", (0.038, 0.038, H_ - 0.029 - 0.014), (x, y, 0.014 + (H_ - 0.043) / 2), STL, 0.005, 2))
            p.append(lathe("Glide", [(0.0, 0.0), (0.017, 0.0), (0.018, 0.004), (0.014, 0.014), (0.0, 0.014)], GLD, 12,
                           xf=M4(None, (x, y, 0.0))))
            p.append(rbox("Bracket", (0.09, 0.09, 0.004), (x - sx * 0.03, y - sy * 0.03, H_ - 0.031), STL, 0.0, 1))
    return finish(p, "DiningTable", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- security office

def desk():
    LAM = S("Laminate_DeskTop", (0.6, 0.47, 0.33), "laminate", 0.45)
    LAM["grain"] = (0.64, 0.5, 0.35, 0.5, 0.37, 0.24, 20.0)
    LAM["grain_axis"] = "X"
    EDGE = S("Rubber_DeskEdge", (0.05, 0.05, 0.05), "rubber", 0.5)
    PNT = S("Paint_Desk", (0.5, 0.53, 0.49), "paint", 0.45)
    KICK = S("Paint_DeskKick", (0.13, 0.13, 0.13), "paint", 0.5)
    PULL = S("Chrome_DeskPull", (0.78, 0.78, 0.76), "chrome", 0.2, 1.0)
    GLD = S("Plastic_DeskGlide", (0.06, 0.06, 0.06), "plastic", 0.5)
    W_, D_, H_ = 1.7, 0.72, 0.74
    p = [rbox("Top", (W_ - 0.01, D_ - 0.01, 0.028), (0, 0, H_ - 0.014), LAM, 0.0015, 1)]
    for (sx, sy, cx, cy) in ((W_, 0.007, 0, -D_ / 2 + 0.0035), (W_, 0.007, 0, D_ / 2 - 0.0035),
                             (0.007, D_ - 0.014, -W_ / 2 + 0.0035, 0), (0.007, D_ - 0.014, W_ / 2 - 0.0035, 0)):
        p.append(rbox("TMould", (sx, sy, 0.032), (cx, cy, H_ - 0.015), EDGE, 0.003, 2))
    top = H_ - 0.03
    # The pedestal, its drawers: two box drawers over a file drawer.
    px, pw = W_ / 2 - 0.235, 0.42
    p.append(rbox("Pedestal", (pw, 0.66, top - 0.07), (px, 0.01, 0.07 + (top - 0.07) / 2), PNT, 0.012, 3))
    p.append(rbox("PedKick", (pw - 0.03, 0.6, 0.075), (px, 0.04, 0.0375), KICK, 0.004, 1))
    fronts = [(top - 0.012 - 0.14, top - 0.012), (top - 0.012 - 0.29, top - 0.012 - 0.148), (0.078, top - 0.012 - 0.298)]
    labels = []
    for i, (z0, z1) in enumerate(fronts):
        zc = (z0 + z1) / 2
        p.append(rbox("Drawer", (pw - 0.012, 0.022, z1 - z0), (px, -0.331, zc), PNT, 0.008, 3))
        pz = z1 - 0.045 if i == 2 else zc + 0.012
        p += bar_pull("Pull", (px, -0.342, pz), 0.11, "X", PULL, depth=0.022, r=0.006)
        # A label holder (a small chrome frame) under the pull.
        lz = pz - 0.036
        for (sx, sz, ox, oz) in ((0.074, 0.004, 0, 0.0155), (0.074, 0.004, 0, -0.0155), (0.004, 0.031, -0.035, 0), (0.004, 0.031, 0.035, 0)):
            p.append(rbox("Holder", (sx, 0.003, sz), (px + ox, -0.3435, lz + oz), PULL, 0.0, 1))
        labels.append((px, lz, ["FORMS", "LOG BOOKS", "KEYS"][i]))
    # The end panel on the other side, the modesty panel at the back, a pencil drawer.
    p.append(rbox("EndPanel", (0.03, 0.66, top - 0.07), (-W_ / 2 + 0.03, 0.01, 0.07 + (top - 0.07) / 2), PNT, 0.01, 3))
    p.append(rbox("EndKick", (0.026, 0.6, 0.075), (-W_ / 2 + 0.03, 0.04, 0.0375), KICK, 0.004, 1))
    p.append(rbox("Modesty", (W_ - 0.5, 0.02, 0.44), (-0.205, 0.315, top - 0.24), PNT, 0.006, 2))
    p.append(rbox("Pencil", (0.84, 0.02, 0.062), (-0.22, -0.331, top - 0.045), PNT, 0.006, 2))
    p += bar_pull("PencilPull", (-0.22, -0.341, top - 0.045), 0.096, "X", PULL, depth=0.02, r=0.005)
    p.append(rbox("Rail", (0.86, 0.6, 0.012), (-0.22, 0.0, top - 0.008), PNT, 0.003, 1))
    for (x, y) in ((px - pw / 2 + 0.03, -0.29), (px + pw / 2 - 0.03, -0.29), (px - pw / 2 + 0.03, 0.31), (px + pw / 2 - 0.03, 0.31),
                   (-W_ / 2 + 0.03, -0.29), (-W_ / 2 + 0.03, 0.31)):
        p.append(lathe("Glide", [(0.0, 0.0), (0.014, 0.0), (0.014, 0.006), (0.0, 0.006)], GLD, 10, xf=M4(None, (x, y, 0.0))))
    base = finish(p, "Desk", sharp_deg=50)
    cards = []
    for i, (x, z, txt) in enumerate(labels):
        c = Canvas(128, 48, (0.92, 0.9, 0.82))
        c.text_c(txt, 64, 17, 2, (0.15, 0.15, 0.3))
        cards.append(card("Label", 0.066, 0.025, (x, -0.3436, z), print_mat("Paper_DeskLabel%d" % i, c)))
    return join([base] + cards, "Desk")


def crt_monitor():
    CASE = S("Beige_Crt", (0.78, 0.74, 0.64), "beige", 0.4)
    SCR = S("Screen_Crt", (0.035, 0.04, 0.042), "glass", 0.08)
    BTN = S("Beige_CrtButton", (0.7, 0.66, 0.57), "beige", 0.45)
    LED = S("Gloss_CrtLed", (0.12, 0.32, 0.12), "glass", 0.2)
    zc = 0.035 + 0.1775
    p = [lathe("Foot", [(0.0, 0.0), (0.105, 0.0), (0.11, 0.004), (0.104, 0.014), (0.06, 0.02), (0.045, 0.03), (0.045, 0.04), (0.0, 0.04)],
               CASE, 40, xf=M4(None, (0, 0.03, 0.0)))]
    # The front: a rounded shell, its face, the sloped lip into the screen's recess.
    nc = 5
    rings = [rrect_xz(0.34, 0.31, 0.03, -0.05, nc, 0, zc - 0.005),
             rrect_xz(0.38, 0.355, 0.022, -0.06, nc, 0, zc),
             rrect_xz(0.38, 0.355, 0.022, -0.138, nc, 0, zc),
             rrect_xz(0.372, 0.347, 0.018, -0.15, nc, 0, zc),
             rrect_xz(0.305, 0.238, 0.012, -0.15, nc, 0, zc + 0.012),
             rrect_xz(0.29, 0.224, 0.008, -0.139, nc, 0, zc + 0.012)]
    shell = loft("Shell", rings, CASE, cap0=False, cap1=False)
    bmesh_fix = bmesh.new()
    bmesh_fix.from_mesh(shell.data)
    bmesh.ops.recalc_face_normals(bmesh_fix, faces=bmesh_fix.faces)
    # (normals out: the face at the front must look toward -Y)
    fr = max(bmesh_fix.faces, key=lambda f: -f.calc_center_median().y)
    if fr.normal.y > 0:
        for f in bmesh_fix.faces:
            f.normal_flip()
    bmesh_fix.to_mesh(shell.data)
    bmesh_fix.free()
    p.append(shell)
    p.append(bulge_grid("Glass", 0.3, 0.234, -0.1385, -0.1475, zc + 0.012, SCR))
    # The bell behind, to a back plate.
    bell = [rrect_xz(0.34, 0.31, 0.03, -0.05, nc, 0, zc - 0.005), rrect_xz(0.3, 0.27, 0.05, 0.06, nc, 0, zc - 0.015),
            rrect_xz(0.23, 0.2, 0.05, 0.2, nc, 0, zc - 0.03)]
    b = loft("Bell", bell, CASE, cap0=False, cap1=True, recalc=False)
    bm2 = bmesh.new()
    bm2.from_mesh(b.data)
    bmesh.ops.recalc_face_normals(bm2, faces=bm2.faces)
    back = max(bm2.faces, key=lambda f: f.calc_center_median().y)
    if back.normal.y < 0:
        for f in bm2.faces:
            f.normal_flip()
    bm2.to_mesh(b.data)
    bm2.free()
    p.append(b)
    p.append(rbox("Neck", (0.12, 0.12, 0.03), (0, 0.03, 0.045), CASE, 0.01, 2))
    # The chin: the power button, its lamp, a row of small buttons.
    p.append(rbox("Power", (0.02, 0.008, 0.014), (0.145, -0.152, 0.065), BTN, 0.003, 1))
    p.append(sphere("Led", (0.122, -0.1505, 0.065), (0.0028, 0.0018, 0.0028), LED, 10, 6))
    for k in range(4):
        p.append(rbox("Btn", (0.011, 0.006, 0.008), (0.03 + k * 0.02, -0.151, 0.065), BTN, 0.002, 1))
    base = finish(p, "CrtMonitor", sharp_deg=45)
    c = Canvas(96, 24, (0.78, 0.74, 0.64))
    c.text_c("SVGA", 48, 5, 2, (0.35, 0.33, 0.3))
    badge = card("Badge", 0.05, 0.0125, (-0.12, -0.1502, 0.066), print_mat("Print_CrtBadge", c))
    v = Canvas(128, 96, (0.78, 0.74, 0.64))
    for row in range(5):
        for i in range(12):
            v.rect(6 + i * 10, 8 + row * 17, 12 + i * 10, 20 + row * 17, (0.08, 0.075, 0.07))
    vent = card("Vent", 0.18, 0.13, (0, 0.2005, zc - 0.03), print_mat("Beige_CrtVent", v), normal=(0, 1, 0))
    return join([base, badge, vent], "CrtMonitor")


def keyboard():
    CASE = S("Beige_Keyboard", (0.76, 0.72, 0.63), "beige", 0.45)
    KEY = S("Beige_Keys", (0.82, 0.79, 0.71), "beige", 0.4)
    MOD = S("Beige_KeysDark", (0.62, 0.59, 0.52), "beige", 0.45)
    CAB = S("Plastic_KeyboardCable", (0.7, 0.67, 0.6), "plastic", 0.5)
    u = 0.019
    p = [rbox("Case", (0.452, 0.162, 0.022), (0, 0, 0.011), CASE, 0.007, 2)]
    rows = [  # (y offset in key rows from the front, [(width in units, modifier?) ...] ) for the main block
        (0, [(1.25, 1), (1.25, 1), (1.25, 1), (6.25, 0), (1.25, 1), (1.25, 1), (1.25, 1), (1.25, 1)]),
        (1, [(2.25, 1)] + [(1, 0)] * 10 + [(2.75, 1)]),
        (2, [(1.75, 1)] + [(1, 0)] * 11 + [(2.25, 1)]),
        (3, [(1.5, 1)] + [(1, 0)] * 12 + [(1.5, 0)]),
        (4, [(1, 0)] * 13 + [(2, 1)]),
        (5.4, [(1, 1), (1, -1)] + [(1, 0)] * 4 + [(0.5, -1)] + [(1, 1)] * 4 + [(0.5, -1)] + [(1, 0)] * 4),
    ]
    x0 = -0.215
    y0 = -0.066

    def key(cx, cy, wu, mod, hu=1.0):
        m = MOD if mod else KEY
        p.append(rbox("Key", (wu * u - 0.0035, hu * u - 0.0035, 0.008), (cx, cy, 0.0262), m, 0.0022, 1))

    for ry, keys in rows:
        x = x0
        for wu, mod in keys:
            if mod >= 0:
                key(x + wu * u / 2, y0 + ry * u, wu, mod)
            x += wu * u
    # The arrows and the six above them; the numeric pad.
    nx = x0 + 15.25 * u
    for col in range(3):
        for r in (3, 4):
            key(nx + (col + 0.5) * u, y0 + r * u, 1, 1)
    key(nx + 1.5 * u, y0 + 1 * u, 1, 1)
    for col in range(3):
        key(nx + (col + 0.5) * u, y0, 1, 1)
    px = nx + 3.25 * u
    for col in range(4):
        key(px + (col + 0.5) * u, y0 + 4 * u, 1, 1)
    for r in (3, 2, 1):
        for col in range(3):
            key(px + (col + 0.5) * u, y0 + r * u, 1, 0)
    key(px + 3.5 * u, y0 + 2.5 * u, 1, 1, 2.0)
    key(px + 3.5 * u, y0 + 0.5 * u, 1, 1, 2.0)
    key(px + 1.0 * u, y0, 2, 0)
    key(px + 2.5 * u, y0, 1, 0)
    # The cable out of the back.
    p.append(tube("Cable", catmull([(0.0, 0.08, 0.012), (0.0, 0.11, 0.008), (0.03, 0.16, 0.004), (0.09, 0.2, 0.004)], 5), 0.0028, CAB, 6))
    ob = finish(p, "Keyboard", sharp_deg=50)
    place(ob, rot('X', -3.0), (0, 0, 0.004))
    return ob


# ------------------------------------------------------------------------------------------------- janitor's closet

def mop_sink():
    STN = S("Stone_MopSink", (0.66, 0.66, 0.64), "stone", 0.6)
    STN["speckle"] = (150.0, 0.42, 0.42, 0.41, 0.82, 0.82, 0.8)
    DRN = S("Stainless_MopDrain", (0.62, 0.62, 0.62), "metal", 0.35, 1.0)
    rings = [rrect(0.61, 0.51, 0.03, 0.0), rrect(0.61, 0.51, 0.03, 0.238), rrect(0.598, 0.498, 0.026, 0.254),
             rrect(0.53, 0.43, 0.024, 0.254), rrect(0.516, 0.416, 0.03, 0.24), rrect(0.49, 0.39, 0.05, 0.075),
             rrect(0.13, 0.13, 0.065, 0.058), rrect(0.11, 0.11, 0.055, 0.054)]
    b = loft("Basin", rings, STN, cap0=True, cap1=True, recalc=True)
    p = [b]
    p.append(lathe("Drain", [(0.0, 0.0), (0.058, 0.0), (0.058, 0.003), (0.0, 0.003)], DRN, 24, xf=M4(None, (0, 0, 0.054))))
    base = finish(p, "MopSink", sharp_deg=40)
    c = Canvas(96, 96, (0.62, 0.62, 0.62))
    for ring_r in (10, 20, 30, 40):
        n = max(6, int(ring_r * 0.6))
        for i in range(n):
            a = 2 * math.pi * i / n
            c.disc(48 + math.cos(a) * ring_r, 48 + math.sin(a) * ring_r, 2.6, (0.05, 0.05, 0.05))
    grate = card("Grate", 0.112, 0.112, (0, 0, 0.0575), print_mat("Stainless_MopGrate", c), normal=(0, 0, 1))
    return join([base, grate], "MopSink")


def service_faucet():
    CH = S("Chrome_ServiceFaucet", (0.72, 0.72, 0.7), "metal", 0.3, 1.0)
    CH["finish"] = "metal"
    HOSE = S("Rubber_ServiceHose", (0.12, 0.2, 0.12), "rubber", 0.6)
    RED = S("Gloss_FaucetRed", (0.65, 0.07, 0.05), "plastic", 0.3)
    BLU = S("Gloss_FaucetBlue", (0.08, 0.2, 0.6), "plastic", 0.3)
    wall = 0.255
    z = 0.92
    p = []
    for sx in (-1, 1):
        x = sx * 0.1
        p.append(lathe("Flange", [(0.0, 0.0), (0.032, 0.0), (0.032, 0.004), (0.026, 0.012), (0.014, 0.016), (0.0, 0.016)], CH, 24,
                       xf=M4(rot('X', 90), (x, wall, z))))
        p.append(tube_y("Inlet", x, z, wall - 0.05, wall - 0.012, 0.012, CH, 14))
        p.append(lathe("Valve", [(0.0, 0.0), (0.022, 0.0), (0.024, 0.008), (0.024, 0.05), (0.018, 0.056), (0.012, 0.07), (0.0, 0.07)],
                       CH, 18, xf=M4(None, (x, wall - 0.05, z - 0.02))))
        # The four-arm handle and its index button.
        for k in range(4):
            arm = rbox("Arm", (0.046, 0.009, 0.007), (0.023, 0, 0), CH, 0.003, 1)
            place(arm, rot('Z', 45 + k * 90), (x, wall - 0.05, z + 0.058))
            p.append(arm)
        p.append(lathe("Index", [(0.0, 0.0), (0.009, 0.0), (0.009, 0.006), (0.0, 0.008)], RED if sx < 0 else BLU, 14,
                       xf=M4(None, (x, wall - 0.05, z + 0.058))))
    p.append(lathe("Bar", [(0.0, 0.0), (0.015, 0.0), (0.015, 0.2), (0.0, 0.2)], CH, 16, xf=M4(rot('Y', 90), (-0.1, wall - 0.05, z))))
    # The vacuum breaker on top, the spout down and out, its hose thread, the pail hook, a brace to the wall.
    p.append(lathe("Breaker", [(0.0, 0.0), (0.016, 0.0), (0.016, 0.05), (0.022, 0.055), (0.022, 0.075), (0.012, 0.085), (0.0, 0.085)],
                   CH, 18, xf=M4(None, (0, wall - 0.05, z))))
    sp = catmull([(0, wall - 0.05, z), (0, wall - 0.1, z - 0.02), (0, wall - 0.135, z - 0.07), (0, wall - 0.14, z - 0.14)], 6)
    p.append(tube("Spout", sp, 0.0135, CH, 14))
    end = Vector(sp[-1])
    p.append(lathe("Thread", [(0.0, 0.0), (0.0145, 0.0), (0.0145, 0.025), (0.0, 0.025)], CH, 16, xf=M4(rot('X', 180), end + Vector((0, 0, 0.0)))))
    hook = catmull([(0, wall - 0.135, z - 0.1), (0, wall - 0.2, z - 0.1), (0, wall - 0.225, z - 0.12), (0, wall - 0.215, z - 0.145)], 5)
    p.append(tube("Hook", hook, 0.004, CH, 8))
    p.append(tube("Brace", [(0, wall - 0.135, z - 0.12), (0, wall - 0.06, z - 0.16), (0, wall, z - 0.16)], 0.005, CH, 8))
    p.append(lathe("BraceFoot", [(0.0, 0.0), (0.016, 0.0), (0.016, 0.004), (0.0, 0.006)], CH, 14, xf=M4(rot('X', 90), (0, wall, z - 0.16))))
    # A hose hanging from the spout into the basin.
    hose = catmull([(end.x, end.y, end.z - 0.02), (0.01, wall - 0.15, z - 0.3), (-0.04, 0.05, 0.26), (-0.1, -0.05, 0.12),
                    (-0.16, -0.12, 0.085), (-0.2, -0.13, 0.08)], 6)
    p.append(tube("Hose", hose, 0.011, HOSE, 10))
    return finish(p, "ServiceFaucet", sharp_deg=50)


def mop():
    WOOD = S("Wood_MopHandle", (0.62, 0.5, 0.34), "wood", 0.6)
    WOOD["grain"] = (0.66, 0.54, 0.38, 0.5, 0.38, 0.25, 60.0)
    WOOD["grain_axis"] = "Z"
    GAL = S("Galv_MopClamp", (0.6, 0.61, 0.6), "galv", 0.5, 1.0)
    STR = S("Fabric_MopHead", (0.62, 0.6, 0.53), "fabric", 0.95)
    rnd = random.Random(11)
    # (At the sink's Unity +x end, as the old box mop: Blender -x.)
    foot = Vector((-0.4, 0.13, 0.0))
    top = Vector((-0.415, 0.235, 1.45))
    ax = (top - foot).normalized()
    p = [lathe("Handle", [(0.0, 0.0), (0.0118, 0.0), (0.0118, 1.32), (0.009, 1.34), (0.0, 1.345)], WOOD, 14,
               xf=M4(rot_to(ax), foot + ax * 0.11))]
    p.append(lathe("Clamp", [(0.0, 0.0), (0.02, 0.0), (0.03, 0.03), (0.03, 0.07), (0.016, 0.1), (0.0, 0.1)], GAL, 16,
                   xf=M4(rot_to(ax), foot + ax * 0.03)))
    p.append(rbox("Band", (0.13, 0.02, 0.03), foot + ax * 0.05, GAL, 0.006, 2))
    for k in range(80):
        a = rnd.random() * 2 * math.pi
        r0 = 0.02 + rnd.random() * 0.04
        start = foot + Vector((math.cos(a) * r0 * 0.6, math.sin(a) * r0 * 0.4, 0.06))
        L = 0.16 + rnd.random() * 0.12
        out = Vector((math.cos(a), math.sin(a) * 0.7, 0)) * (L * (0.6 + rnd.random() * 0.4))
        pts = [start, start + out * 0.25 + Vector((0, 0, -0.02)), start + out * 0.6 + Vector((0, 0, -0.045)),
               start + out + Vector((0, 0, -0.054))]
        pts = [Vector((q.x, min(q.y, 0.245), max(q.z, 0.005))) for q in pts]
        p.append(tube("String", catmull(pts, 3), 0.0058, STR, 5))
    return finish(p, "Mop", sharp_deg=60)


def breaker_panel():
    PNT = S("Paint_Panel", (0.56, 0.57, 0.56), "paint", 0.45)
    DK = S("Plastic_PanelLatch", (0.06, 0.06, 0.06), "plastic", 0.5)
    GAL = S("Galv_PanelConduit", (0.66, 0.67, 0.66), "galv", 0.5, 1.0)
    SCW = S("Steel_PanelScrew", (0.55, 0.55, 0.53), "metal", 0.4, 1.0)
    zc = 1.25
    p = [rbox("Box", (0.36, 0.085, 0.5), (0, 0.0075, zc), PNT, 0.004, 2),
         rbox("Cover", (0.376, 0.006, 0.516), (0, -0.0375, zc), PNT, 0.003, 2),
         rbox("Door", (0.222, 0.006, 0.35), (-0.054, -0.0435, zc + 0.045), PNT, 0.003, 2)]
    # (The game's main switch stands at Unity x -0.115, the viewer's right: Blender +x, Unity mirrors x.)
    p.append(lathe("Hinge", [(0.0, 0.0), (0.004, 0.0), (0.004, 0.35), (0.0, 0.35)], PNT, 10, xf=M4(None, (-0.167, -0.044, zc + 0.045 - 0.175))))
    p.append(rbox("Latch", (0.018, 0.008, 0.05), (0.044, -0.0478, zc + 0.05), DK, 0.004, 2))
    p.append(rbox("Opening", (0.056, 0.004, 0.11), (0.115, -0.0405, zc - 0.07), DK, 0.0015, 1))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p += screw("Screw", (sx * 0.168, -0.0405, zc + sz * 0.236), (0, -1, 0), 0.0045, SCW)
    # The conduit up into the ceiling: a connector into the box, a strap, a coupling.
    p.append(lathe("Connector", [(0.0, 0.0), (0.017, 0.0), (0.017, 0.008), (0.014, 0.01), (0.014, 0.03), (0.0, 0.03)], GAL, 16,
                   xf=M4(None, (0.0, 0.012, zc + 0.25))))
    p.append(lathe("Conduit", [(0.0, 0.0), (0.0117, 0.0), (0.0117, 2.7 - zc - 0.28), (0.0, 2.7 - zc - 0.28)], GAL, 14,
                   xf=M4(None, (0.0, 0.012, zc + 0.28))))
    p.append(lathe("Coupling", [(0.0, 0.0), (0.0135, 0.0), (0.0135, 0.045), (0.0, 0.045)], GAL, 14, xf=M4(None, (0.0, 0.012, 2.15))))
    strap = tube("Strap", [(-0.025, 0.05, 1.9), (-0.016, 0.03, 1.9), (0.0, 0.0, 1.9), (0.016, 0.03, 1.9), (0.04, 0.05, 1.9)], 0.0022, GAL, 6)
    p.append(strap)
    base = finish(p, "BreakerPanel", sharp_deg=50)
    d = Canvas(200, 120, (0.97, 0.97, 0.95))
    d.rect(0, 0, 200, 36, (0.78, 0.08, 0.06))
    d.rect(10, 6, 60, 30, (0.05, 0.05, 0.05))
    d.text("DANGER", 66, 8, 3, (0.98, 0.98, 0.96))
    d.text_c("HAZARDOUS VOLTAGE", 100, 46, 2, (0.05, 0.05, 0.05))
    d.text_c("TURN OFF POWER", 100, 66, 2, (0.05, 0.05, 0.05))
    d.text_c("BEFORE SERVICING", 100, 86, 2, (0.05, 0.05, 0.05))
    danger = card("Danger", 0.1, 0.06, (-0.054, -0.0467, zc + 0.17), print_mat("Print_PanelDanger", d))
    c = Canvas(200, 140, (0.93, 0.92, 0.86))
    c.text_c("PANEL B-2", 100, 8, 3, (0.1, 0.1, 0.1))
    c.text_c("120/240V 1PH", 100, 34, 2, (0.1, 0.1, 0.1))
    c.lines(14, 58, 186, 8, 9, (0.3, 0.3, 0.4), seed=9)
    dirc = card("Directory", 0.11, 0.077, (-0.054, -0.0467, zc - 0.035), print_mat("Paper_PanelDirectory", c), roll=1.0)
    m = Canvas(64, 24, (0.56, 0.57, 0.56))
    m.text_c("MAIN", 32, 5, 2, (0.08, 0.08, 0.08))
    main = card("Main", 0.045, 0.017, (0.115, -0.0406, zc - 0.002), print_mat("Paint_PanelMain", m))
    return join([base, danger, dirc, main], "BreakerPanel")


# ------------------------------------------------------------------------------------------------- storage

def shelving(W_, H_, D_, name):
    post = Canvas(16, 1024, (0.64, 0.65, 0.65))
    pitch = 1024 / (H_ / 0.038)
    y = pitch * 0.5
    while y < 1024 - 8:
        post.disc(8, y, 2.6, (0.05, 0.05, 0.05))
        post.rect(7, y, 9.5, y + pitch * 0.42, (0.05, 0.05, 0.05))
        y += pitch
    PST = print_mat("Paint_ShelfPost", post)
    PST["finish"] = "paint"
    BM = S("Paint_ShelfBeam", (0.64, 0.65, 0.65), "paint", 0.45)
    DECK = S("Board_ShelfDeck", (0.6, 0.49, 0.35), "board", 0.85)
    DECK["speckle"] = (200.0, 0.36, 0.28, 0.19, 0.74, 0.64, 0.5)
    FT = S("Plastic_ShelfFoot", (0.06, 0.06, 0.06), "plastic", 0.5)
    RIV = S("Paint_ShelfRivet", (0.6, 0.61, 0.61), "paint", 0.4)
    levels = max(4, min(5, round(H_ / 0.45)))
    lp = (H_ - 0.12) / (levels - 1)
    a, t = 0.038, 0.0022
    p = []
    flanges = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * (W_ / 2 - a / 2), sy * (D_ / 2 - a / 2)
            # an angle: one flange across the front/back, one along the side, the corner outward
            f1 = rbox("PostA", (a, t, H_ - 0.012), (cx, sy * (D_ / 2 - t / 2), 0.012 + (H_ - 0.012) / 2), PST, 0.0006, 1)
            f2 = rbox("PostB", (t, a, H_ - 0.012), (sx * (W_ / 2 - t / 2), cy, 0.012 + (H_ - 0.012) / 2), PST, 0.0006, 1)
            for f in (f1, f2):
                set_uv(f, lambda q, n, h=H_: (0.5 + 0.5 * math.copysign(1, q.x + q.y), (q.z - 0.012) / (h - 0.012)))
            flanges += [f1, f2]
            p.append(rbox("Foot", (a + 0.004, a + 0.004, 0.014), (cx, cy, 0.007), FT, 0.003, 1))
    for l in range(levels):
        yl = 0.1 + l * lp
        top = yl + 0.011
        p.append(rbox("Deck", (W_ - 0.012, D_ - 0.012, 0.016), (0, 0, top - 0.008), DECK, 0.0015, 1))
        for sy in (-1, 1):
            p.append(rbox("BeamWeb", (W_ - 2 * a + 0.01, 0.003, 0.042), (0, sy * (D_ / 2 - 0.0035), top - 0.016 - 0.021), BM, 0.001, 1))
            p.append(rbox("BeamLip", (W_ - 2 * a + 0.01, 0.026, 0.003), (0, sy * (D_ / 2 - 0.016), top - 0.0175), BM, 0.001, 1))
            for sx in (-1, 1):
                p.append(sphere("Rivet", (sx * (W_ / 2 - a * 0.55), sy * (D_ / 2 + 0.0002), top - 0.03), (0.0045, 0.0022, 0.0045), RIV, 10, 5))
        for sx in (-1, 1):
            p.append(rbox("SideWeb", (0.003, D_ - 2 * a + 0.01, 0.042), (sx * (W_ / 2 - 0.0035), 0, top - 0.016 - 0.021), BM, 0.001, 1))
            p.append(rbox("SideLip", (0.026, D_ - 2 * a + 0.01, 0.003), (sx * (W_ / 2 - 0.016), 0, top - 0.0175), BM, 0.001, 1))
    base = finish(p, name, sharp_deg=50)
    return join([base] + flanges, name)


def counter_cabinet():
    WD = S("Wood_CabinetFront", (0.66, 0.5, 0.33), "wood", 0.5)
    WD["grain"] = (0.7, 0.54, 0.36, 0.55, 0.4, 0.25, 30.0)
    WD["grain_axis"] = "Z"
    CAR = S("Wood_CabinetSide", (0.66, 0.5, 0.33), "wood", 0.55)
    CAR["grain"] = (0.7, 0.54, 0.36, 0.55, 0.4, 0.25, 30.0)
    CAR["grain_axis"] = "Y"
    KICK = S("Plastic_CabinetKick", (0.07, 0.07, 0.07), "plastic", 0.6)
    PULL = S("Stainless_CabinetPull", (0.7, 0.7, 0.7), "metal", 0.3, 1.0)
    W_ = 0.6
    front = -0.28
    p = [rbox("Carcass", (W_ - 0.002, 0.59, 0.765), (0, front + 0.295, 0.1 + 0.3825), CAR, 0.002, 1),
         rbox("Kick", (W_, 0.016, 0.1), (0, front + 0.075, 0.05), KICK, 0.002, 1)]
    for xx in (-W_ / 2 + 0.009, W_ / 2 - 0.009):
        p.append(rbox("Side", (0.018, 0.075, 0.1), (xx, front + 0.0375, 0.05), CAR, 0.001, 1))
    p.append(rbox("Drawer", (W_ - 0.004, 0.019, 0.142), (0, front - 0.0095, 0.865 - 0.073), WD, 0.003, 2))
    p.append(rbox("Door", (W_ - 0.004, 0.019, 0.6), (0, front - 0.0095, 0.104 + 0.3), WD, 0.003, 2))
    p += bar_pull("DrawerPull", (0, front - 0.019, 0.865 - 0.073), 0.14, "X", PULL, depth=0.03, r=0.0055)
    p += bar_pull("DoorPull", (0.245, front - 0.019, 0.6), 0.14, "Z", PULL, depth=0.03, r=0.0055)
    return finish(p, "CounterCabinet", sharp_deg=50)


def counter_sink():
    SS = S("Stainless_Sink", (0.66, 0.66, 0.66), "metal", 0.3, 1.0)
    CH = S("Chrome_SinkTap", (0.75, 0.75, 0.74), "metal", 0.15, 1.0)
    rings = [rrect(0.52, 0.44, 0.035, 0.0005), rrect(0.515, 0.435, 0.033, 0.006), rrect(0.5, 0.42, 0.03, 0.0085),
             rrect(0.46, 0.38, 0.03, 0.0075), rrect(0.43, 0.35, 0.04, -0.004), rrect(0.4, 0.32, 0.05, -0.175),
             rrect(0.1, 0.1, 0.05, -0.183), rrect(0.088, 0.088, 0.044, -0.186)]
    bowl = loft("Bowl", rings, SS, cap0=False, cap1=True, recalc=True)
    bm = bmesh.new()
    bm.from_mesh(bowl.data)
    top = max(bm.faces, key=lambda f: f.calc_center_median().z)
    if top.normal.z < 0:
        for f in bm.faces:
            f.normal_flip()
    bm.to_mesh(bowl.data)
    bm.free()
    p = [bowl]
    p.append(lathe("Basket", [(0.0, 0.0), (0.045, 0.0), (0.045, 0.002), (0.04, 0.004), (0.0, 0.004)], SS, 24, xf=M4(None, (0, 0, -0.188))))
    p.append(lathe("Post", [(0.0, 0.0), (0.006, 0.0), (0.006, 0.012), (0.004, 0.016), (0.0, 0.016)], SS, 10, xf=M4(None, (0, 0, -0.186))))
    # The tap behind the bowl: a deck plate, a body, a gooseneck, a lever.
    ty = 0.255
    p.append(rbox("Deck", (0.07, 0.05, 0.006), (0, ty, 0.003), CH, 0.003, 2))
    p.append(lathe("Body", [(0.0, 0.0), (0.024, 0.0), (0.024, 0.01), (0.021, 0.07), (0.016, 0.09), (0.0, 0.09)], CH, 20, xf=M4(None, (0, ty, 0.006))))
    neck = catmull([(0, ty, 0.09), (0, ty, 0.24), (0, ty - 0.06, 0.32), (0, ty - 0.16, 0.29), (0, ty - 0.2, 0.22)], 7)
    p.append(tube("Neck", neck, 0.011, CH, 14))
    p.append(lathe("Aerator", [(0.0, 0.0), (0.012, 0.0), (0.012, 0.02), (0.0, 0.02)], CH, 14, xf=M4(rot('X', 180), Vector(neck[-1]) + Vector((0, 0, 0.0)))))
    lev = rbox("Lever", (0.014, 0.012, 0.11), (0, 0, 0.055), CH, 0.005, 2)
    place(lev, rot('X', 60), (0.03, ty + 0.0, 0.075))
    p.append(lev)
    return finish(p, "CounterSink", sharp_deg=45)


def paint_can():
    TIN = S("Galv_PaintCan", (0.72, 0.72, 0.7), "galv", 0.45, 1.0)
    WIRE = S("Steel_PaintBail", (0.5, 0.5, 0.48), "metal", 0.5, 1.0)
    R, H = 0.083, 0.19
    prof = [(0.0, 0.0), (R - 0.004, 0.0), (R, 0.003), (R + 0.002, 0.008), (R, 0.012), (R, H - 0.012), (R + 0.002, H - 0.006),
            (R, H), (R - 0.008, H), (R - 0.009, H - 0.006), (R - 0.016, H - 0.006), (R - 0.016, H - 0.002), (0.0, H - 0.002)]
    p = [lathe("Can", prof, TIN, 40)]
    for sx in (-1, 1):
        p.append(rbox("Ear", (0.006, 0.022, 0.026), (sx * (R + 0.002), 0, H - 0.035), TIN, 0.002, 1))
    bail = [Vector((math.cos(a) * (R + 0.005), 0.0, H - 0.035 + math.sin(a) * 0.075)) for a in [math.pi * k / 12 for k in range(13)]]
    bail = [Vector((b.x, -0.004 * math.sin(math.pi * k / 12) * 6, b.z)) for k, b in enumerate(bail)]
    p.append(tube("Bail", bail, 0.0017, WIRE, 6))
    base = finish(p, "PaintCan", sharp_deg=50)
    # The label round it: a band of colour, the words, a drip down from the rim.
    lab = Canvas(512, 128, (0.95, 0.94, 0.9))
    lab.rect(0, 0, 512, 26, (0.12, 0.25, 0.55))
    lab.rect(0, 102, 512, 128, (0.12, 0.25, 0.55))
    for k in range(2):
        x0 = 30 + k * 256
        lab.text("INTERIOR", x0, 34, 3, (0.1, 0.15, 0.35))
        lab.text("LATEX FLAT", x0, 60, 2, (0.1, 0.1, 0.1))
        lab.rect(x0, 80, x0 + 60, 96, (0.86, 0.83, 0.72))
        lab.text("1 GAL", x0 + 120, 82, 2, (0.1, 0.1, 0.1))
    for x in (150, 154, 380):
        lab.rect(x, 0, x + 3, 22 + (x % 7) * 4, (0.88, 0.86, 0.8))
    LAB = print_mat("Print_PaintLabel", lab)
    LAB["finish"] = "print"
    bm = bmesh.new()
    segs, z0, z1 = 40, 0.03, 0.165
    ring0 = [bm.verts.new((math.cos(2 * math.pi * i / segs) * (R + 0.0004), math.sin(2 * math.pi * i / segs) * (R + 0.0004), z0)) for i in range(segs + 1)]
    ring1 = [bm.verts.new((math.cos(2 * math.pi * i / segs) * (R + 0.0004), math.sin(2 * math.pi * i / segs) * (R + 0.0004), z1)) for i in range(segs + 1)]
    for i in range(segs):
        bm.faces.new((ring0[i], ring0[i + 1], ring1[i + 1], ring1[i]))
    lob = new_obj("Label", bm, [LAB])
    me = lob.data
    uv = me.uv_layers.new(name="UVMap").data
    for f in me.polygons:
        f.use_smooth = True
        for li in f.loop_indices:
            vi = me.loops[li].vertex_index
            i = vi % (segs + 1)
            uv[li].uv = (i / segs, 0.0 if vi < segs + 1 else 1.0)
    return join([base, lob], "PaintCan")


def cleaner_jug():
    HD = S("Plastic_Jug", (0.86, 0.85, 0.8), "plastic", 0.5)
    CAP = S("Plastic_JugCap", (0.75, 0.12, 0.08), "plastic", 0.4)
    W_, D_, H_ = 0.15, 0.11, 0.27
    rings = []
    prof = [(0.0, 0.3, 0.15), (0.004, 0.96, 0.6), (0.02, 1.0, 1.0), (0.18, 1.0, 1.0), (0.2, 0.92, 0.95), (0.23, 0.55, 0.62),
            (0.245, 0.26, 0.3), (0.25, 0.22, 0.26)]
    for z, f, g in prof:
        rings.append(ring_pts(0.0, 0.0, z, W_ / 2 * f, D_ / 2 * g, 24, 3.5))
    body = loft("Body", rings, HD, cap0=True, cap1=False, recalc=True)
    p = [body]
    p.append(lathe("Neck", [(0.0, 0.0), (0.017, 0.0), (0.017, 0.012), (0.0, 0.012)], HD, 16, xf=M4(None, (0.0, 0.0, 0.248))))
    p.append(lathe("Cap", [(0.0, 0.0), (0.02, 0.0), (0.02, 0.022), (0.017, 0.025), (0.0, 0.025)], CAP, 20, xf=M4(None, (0.0, 0.0, 0.256))))
    hand = catmull([(-0.03, 0.0, 0.235), (-0.06, 0.0, 0.25), (-0.07, 0.0, 0.215), (-0.072, 0.0, 0.17), (-0.07, 0.0, 0.14)], 5)
    p.append(tube("Handle", hand, 0.011, HD, 10))
    base = finish(p, "CleanerJug", sharp_deg=50)
    lab = Canvas(160, 160, (0.95, 0.95, 0.93))
    lab.rect(0, 0, 160, 40, (0.1, 0.45, 0.25))
    lab.text_c("ALL PURPOSE", 80, 12, 2, (0.97, 0.97, 0.95))
    lab.text_c("CLEANER", 80, 52, 3, (0.1, 0.35, 0.2))
    lab.lines(16, 90, 144, 5, 8, (0.35, 0.35, 0.35), seed=2)
    lab.text_c("1 GAL", 80, 138, 2, (0.1, 0.1, 0.1))
    LAB = print_mat("Print_JugLabel", lab)
    label = card("Label", 0.105, 0.105, (0.0, -D_ / 2 - 0.0008, 0.1), LAB)
    return join([base, label], "CleanerJug")


V = (0.75, -1.0, 0.45)
run_baked([
    ("Fridge", fridge, (0.7, -1.0, 0.35), {"scale": 1.0}),
    ("Microwave", microwave, (0.7, -1.0, 0.5), {"scale": 0.5, "px_per_m": 700.0}),
    ("DiningTable", dining_table, (0.7, -1.0, 0.6), {"scale": 1.0}),
    ("Desk", desk, (0.7, -1.0, 0.45), {"scale": 1.0}),
    ("CrtMonitor", crt_monitor, (0.7, -1.0, 0.35), {"scale": 0.5, "px_per_m": 700.0}),
    ("Keyboard", keyboard, (0.4, -1.0, 0.9), {"scale": 0.3, "px_per_m": 800.0}),
    ("MopSink", mop_sink, (0.6, -1.0, 0.9), {"scale": 0.6}),
    ("ServiceFaucet", service_faucet, (0.7, -1.0, 0.3), {"scale": 0.3, "px_per_m": 500.0}),
    ("Mop", mop, (0.7, -1.0, 0.3), {"scale": 0.6, "px_per_m": 330.0}),
    ("BreakerPanel", breaker_panel, (0.6, -1.0, 0.3), {"scale": 0.6}),
    ("Shelving_100", lambda: shelving(1.0, 1.85, 0.42, "Shelving_100"), V, {"scale": 1.0}),
    ("Shelving_110", lambda: shelving(1.1, 2.0, 0.46, "Shelving_110"), V, {"scale": 1.0}),
    ("CounterCabinet", counter_cabinet, V, {"scale": 1.0}),
    ("CounterSink", counter_sink, (0.5, -1.0, 0.9), {"scale": 0.5, "px_per_m": 600.0}),
    ("PaintCan", paint_can, V, {"scale": 0.25, "px_per_m": 1100.0}),
    ("CleanerJug", cleaner_jug, V, {"scale": 0.25, "px_per_m": 1100.0}),
])
print("[rooms] done ->", OUT)
