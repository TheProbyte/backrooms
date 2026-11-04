"""
The warehouse's pallets and its racks' hardware (2026-11-04, the realism pass), built in Blender and baked
(bake_kit.py). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_warehouse.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>] [--only Pallet_0]

  - Pallet_0/1/2: a stringer pallet at the game's 1.2 x 1.0 x 0.145 m (PalletGeometry): three notched runners (two
    forklift notches each), seven top deck boards (the lead boards wider), three bottom boards, two nails at every
    crossing, a heat-treatment stamp on a runner's end; 0 new pine, 1 weathered grey, 2 old with a split, broken board.
    The GMA pallet's construction (notched stringers, 5/8" deck boards, two or three helical nails a joint), at the
    game's size.
  - RackBasePlate: an upright's foot plate with two anchor bolts, nuts and washers. RackConnector: a beam's end plate
    with its rivets and the safety lock. RackLabel_0..3: a bay's location label with its barcode. ColumnGuard: a
    yellow upright protector. LoadSign: the end frame's capacity plate.

Frames: origin on the floor at the middle of the foot (Pallet, base plate, guard); RackConnector at the beam's end on
the upright's face (its +X toward the beam); labels and the sign: their middle on the face they are stuck to, facing
-Y. Unity (x, y, z) = (-x, z, -y).
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


def pallet(kind):
    rnd = random.Random(31 + kind)
    if kind == 1:
        light, dark = (0.6, 0.56, 0.48), (0.44, 0.4, 0.33)
    elif kind == 2:
        light, dark = (0.55, 0.46, 0.34), (0.38, 0.3, 0.21)
    else:
        light, dark = (0.8, 0.67, 0.48), (0.64, 0.5, 0.33)
    BRD = S("Wood_PalletBoard", light, "wood", 0.8)
    BRD["grain"] = (*light, *dark, 26.0)
    BRD["grain_axis"] = "X"
    RUN = S("Wood_PalletRunner", light, "wood", 0.8)
    RUN["grain"] = (*light, *dark, 26.0)
    RUN["grain_axis"] = "Y"
    NL = S("Steel_PalletNail", (0.32, 0.31, 0.3), "metal", 0.5, 1.0)
    L, Wd, H = 1.2, 1.0, 0.145
    bt, tt = 0.02, 0.022
    p = []
    # Runners along the depth (Blender y), notched twice underneath.
    for i in (-1, 0, 1):
        x = i * 0.5
        rh = H - bt - tt
        prof = [(-Wd / 2 + 0.01, 0.0), (-0.36, 0.0), (-0.345, 0.038), (-0.075, 0.038), (-0.06, 0.0), (0.06, 0.0), (0.075, 0.038),
                (0.345, 0.038), (0.36, 0.0), (Wd / 2 - 0.01, 0.0), (Wd / 2 - 0.01, rh), (-Wd / 2 + 0.01, rh)]
        bm = bmesh.new()
        for sx in (-1, 1):
            pass
        front = [bm.verts.new((x - 0.0475, y, bt + z)) for y, z in prof]
        back = [bm.verts.new((x + 0.0475, y, bt + z)) for y, z in prof]
        n = len(prof)
        bm.faces.new(front[::-1])
        bm.faces.new(back)
        for k in range(n):
            j = (k + 1) % n
            bm.faces.new((front[k], front[j], back[j], back[k]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.002, segments=1, affect="EDGES", clamp_overlap=True)
        p.append(new_obj("Runner", bm, [RUN]))
    # Bottom boards across the runners, at the front, middle and back.
    for z in (-0.44, 0.0, 0.44):
        w = 0.1 if z else 0.14
        p.append(rbox("Bottom", (L - 0.004, w, bt), (0, z, bt / 2), BRD, 0.002, 1))
    # Top deck: seven boards, the lead ones wider, small gaps, each a little uneven.
    widths = [0.14, 0.1, 0.1, 0.1, 0.1, 0.1, 0.14]
    gap = (Wd - sum(widths)) / 6
    y = -Wd / 2
    boards = []
    for k, w in enumerate(widths):
        cy = y + w / 2
        y += w + gap
        if kind == 2 and k == 2:
            # A split, broken board: the half nearest the front snapped and sagging, a piece gone.
            a = rbox("TopBroken", (0.46, w, tt), (0, 0, 0), BRD, 0.002, 1)
            place(a, rot('Y', 4.0), (-0.36, cy, H - tt / 2 - 0.006))
            p.append(a)
            p.append(rbox("Top", (0.58, w, tt), (0.3, cy, H - tt / 2), BRD, 0.002, 1))
            boards.append((cy, w, True))
            continue
        b = rbox("Top", (L - 0.004 + rnd.uniform(-0.006, 0.0), w - rnd.uniform(0.0, 0.004), tt), (rnd.uniform(-0.003, 0.003), cy, H - tt / 2 - rnd.uniform(0.0, 0.0015)), BRD, 0.0025, 1)
        place(b, rot('Z', rnd.uniform(-0.4, 0.4)))
        p.append(b)
        boards.append((cy, w, False))
    # Nails: two through every board where it crosses a runner, a little proud, some rusted dark.
    for cy, w, broken in boards:
        for i in (-1, 0, 1):
            if broken and i < 0:
                continue
            for d in (-w * 0.27, w * 0.27):
                c = Vector((i * 0.5 + rnd.uniform(-0.015, 0.015), cy + d, H + 0.0004))
                p.append(lathe("Nail", [(0.0, 0.0), (0.0042, 0.0), (0.0042, 0.0006), (0.0, 0.001)], NL, 8, xf=M4(None, c)))
    for z, w in ((-0.44, 0.14), (0.0, 0.1), (0.44, 0.14)):
        for i in (-1, 0, 1):
            for d in (-w * 0.27, w * 0.27):
                c = Vector((i * 0.5 + rnd.uniform(-0.015, 0.015), z + d, -0.0004))
                p.append(lathe("NailB", [(0.0, 0.0), (0.0042, 0.0), (0.0042, -0.0006), (0.0, -0.001)], NL, 8, xf=M4(None, c)))
    base = finish(p, "Pallet_%d" % kind, sharp_deg=40)
    # The heat-treatment stamp on a runner's end (a framed HT and a number, burnt in).
    st = Canvas(128, 96, (*light,))
    ink = (0.14, 0.1, 0.07)
    st.frame(6, 6, 122, 90, 4, ink)
    st.text("HT", 18, 18, 5, ink)
    st.text("US-4012", 18, 64, 2, ink)
    st.rect(76, 18, 80, 54, ink)
    st.text("DB", 86, 26, 3, ink)
    stamp = card("Stamp", 0.085, 0.064, (0.5, -Wd / 2 + 0.0095, bt + (H - bt - tt) / 2 + 0.006), print_mat("Wood_PalletStamp", st), normal=(0, -1, 0))
    return join([base, stamp], "Pallet_%d" % kind)


def rack_base_plate():
    BL = S("Paint_RackUpright", (0.12, 0.27, 0.55), "paint", 0.45)
    GAL = S("Galv_RackAnchor", (0.62, 0.62, 0.6), "galv", 0.45, 1.0)
    p = [rbox("Plate", (0.14, 0.14, 0.008), (0, 0, 0.004), BL, 0.002, 1)]
    for x in (-0.045, 0.045):
        p.append(lathe("Washer", [(0.0, 0.0), (0.014, 0.0), (0.014, 0.003), (0.0, 0.003)], GAL, 14, xf=M4(None, (x, 0.0, 0.008))))
        nut = lathe("Nut", [(0.0, 0.0), (0.0105, 0.0), (0.0105, 0.01), (0.0, 0.01)], GAL, 6, xf=M4(None, (x, 0.0, 0.011)))
        p.append(nut)
        p.append(lathe("Stud", [(0.0, 0.0), (0.006, 0.0), (0.006, 0.012), (0.005, 0.014), (0.0, 0.014)], GAL, 10, xf=M4(None, (x, 0.0, 0.021))))
    # The weld fillet where the upright stands on it.
    p.append(rbox("Fillet", (0.074, 0.074, 0.004), (0, 0, 0.01), BL, 0.002, 1))
    return finish(p, "RackBasePlate", sharp_deg=45)


def rack_connector():
    OR = S("Paint_RackBeam", (0.88, 0.38, 0.06), "paint", 0.45)
    ST = S("Steel_RackRivet", (0.45, 0.45, 0.44), "metal", 0.45, 1.0)
    RED = S("Paint_RackLock", (0.7, 0.07, 0.05), "paint", 0.4)
    p = [rbox("Plate", (0.006, 0.05, 0.16), (0.003, 0, -0.07), OR, 0.0015, 1)]
    for z in (-0.02, -0.07, -0.12):
        p.append(sphere("Rivet", (0.0065, 0.0, z), (0.0018, 0.005, 0.005), ST, 6, 4))
    # The safety lock: a spring tab through the upright's slot.
    lk = rbox("Lock", (0.004, 0.012, 0.028), (0, 0, 0), RED, 0.0012, 1)
    place(lk, rot('Y', -12), (-0.002, 0.018, -0.02))
    p.append(lk)
    return finish(p, "RackConnector", sharp_deg=45)


def rack_label(i):
    W, H = 0.1, 0.045
    codes = ["A-12-1", "B-07-2", "C-03-3", "D-19-2"]
    c = Canvas(256, 112, (0.97, 0.95, 0.75) if i % 2 else (0.96, 0.96, 0.95))
    c.text(codes[i], 12, 10, 4, (0.05, 0.05, 0.05))
    rr = random.Random(i * 7 + 3)
    x = 12
    while x < 244:
        wd = rr.choice((2, 2, 3, 4, 6))
        c.rect(x, 54, x + wd, 100, (0.05, 0.05, 0.05))
        x += wd + rr.choice((2, 3, 4))
    m = print_mat("Print_RackLabel%d" % i, c)
    return join([card("Label", W, H, (0, -0.0006, 0), m)], "RackLabel_%d" % i)


def column_guard():
    YL = S("Paint_ColumnGuard", (0.92, 0.72, 0.05), "paint", 0.45)
    BK = S("Paint_GuardStripe", (0.06, 0.06, 0.06), "paint", 0.45)
    GAL = S("Galv_GuardBolt", (0.62, 0.62, 0.6), "galv", 0.45, 1.0)
    # A U of 6 mm plate wrapped round the front of an upright, 300 mm high, on a foot plate.
    p = []
    sec = [(-0.055, 0.03), (-0.055, -0.045), (-0.045, -0.055), (0.045, -0.055), (0.055, -0.045), (0.055, 0.03)]
    rings = []
    for z in (0.012, 0.3):
        outer = [Vector((x, y, z)) for x, y in sec]
        rings.append(outer)
    bm = bmesh.new()
    lo = [bm.verts.new(v) for v in rings[0]]
    hi = [bm.verts.new(v) for v in rings[1]]
    lo2 = [bm.verts.new(v + Vector((0, 0, 0)) - Vector((math.copysign(0.006, v.x) if abs(v.x) > 0.05 else 0, -0.006 if v.y < -0.05 else 0, 0))) for v in rings[0]]
    hi2 = [bm.verts.new(v - Vector((math.copysign(0.006, v.x) if abs(v.x) > 0.05 else 0, -0.006 if v.y < -0.05 else 0, 0))) for v in rings[1]]
    for k in range(len(sec) - 1):
        bm.faces.new((lo[k], lo[k + 1], hi[k + 1], hi[k]))
        bm.faces.new((lo2[k + 1], lo2[k], hi2[k], hi2[k + 1]))
        bm.faces.new((hi[k], hi[k + 1], hi2[k + 1], hi2[k]))
    for a, b, c2, d in ((lo[0], hi[0], hi2[0], lo2[0]), (hi[-1], lo[-1], lo2[-1], hi2[-1])):
        bm.faces.new((a, b, c2, d))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    p.append(new_obj("Guard", bm, [YL]))
    # Black diagonal stripes on its face: thin bands just proud of it.
    for k in range(4):
        band = rbox("Stripe", (0.1, 0.0008, 0.028), (0, 0, 0), BK, 0.0, 1)
        place(band, rot('Y', 35), (0.0, -0.0556, 0.06 + k * 0.065))
        p.append(band)
    p.append(rbox("Foot", (0.15, 0.12, 0.012), (0, -0.01, 0.006), YL, 0.003, 1))
    for x in (-0.06, 0.06):
        p.append(lathe("Bolt", [(0.0, 0.0), (0.009, 0.0), (0.009, 0.006), (0.0, 0.006)], GAL, 6, xf=M4(None, (x, -0.05, 0.012))))
    return finish(p, "ColumnGuard", sharp_deg=40)


def load_sign():
    c = Canvas(256, 192, (0.96, 0.96, 0.94))
    c.rect(0, 0, 256, 44, (0.1, 0.25, 0.6))
    c.text_c("CAPACITY", 128, 12, 3, (0.98, 0.98, 0.96))
    c.text_c("MAX LOAD", 128, 58, 2, (0.08, 0.08, 0.08))
    c.text_c("2000 LB", 128, 80, 4, (0.08, 0.08, 0.08))
    c.text_c("PER LEVEL", 128, 120, 2, (0.08, 0.08, 0.08))
    c.text_c("EVENLY DISTRIBUTED", 128, 142, 2, (0.4, 0.4, 0.4))
    c.text_c("DO NOT CLIMB", 128, 166, 2, (0.7, 0.07, 0.05))
    m = print_mat("Print_LoadSign", c)
    PL = S("Alu_LoadSign", (0.75, 0.75, 0.74), "metal", 0.4, 1.0)
    plate = rbox("Plate", (0.2, 0.002, 0.15), (0, 0.001, 0), PL, 0.003, 1)
    base = finish([plate], "LoadSignPlate", sharp_deg=45)
    return join([base, card("Face", 0.192, 0.144, (0, -0.0003, 0), m)], "LoadSign")


run_baked([
    ("Pallet_0", lambda: pallet(0), (0.7, -1.0, 0.6), {"scale": 1.0, "px_per_m": 420.0}),
    ("Pallet_1", lambda: pallet(1), (0.7, -1.0, 0.6), {"scale": 1.0, "px_per_m": 420.0}),
    ("Pallet_2", lambda: pallet(2), (0.7, -1.0, 0.6), {"scale": 1.0, "px_per_m": 420.0}),
    ("RackBasePlate", rack_base_plate, (0.6, -1.0, 0.7), {"res": 256, "scale": 0.25}),
    ("RackConnector", rack_connector, (0.6, -1.0, 0.3), {"res": 256, "scale": 0.25}),
    ("RackLabel_0", lambda: rack_label(0), (0.0, -1.0, 0.0), {"res": 256, "scale": 0.1}),
    ("RackLabel_1", lambda: rack_label(1), (0.0, -1.0, 0.0), {"res": 256, "scale": 0.1}),
    ("RackLabel_2", lambda: rack_label(2), (0.0, -1.0, 0.0), {"res": 256, "scale": 0.1}),
    ("RackLabel_3", lambda: rack_label(3), (0.0, -1.0, 0.0), {"res": 256, "scale": 0.1}),
    ("ColumnGuard", column_guard, (0.6, -1.0, 0.4), {"res": 512, "scale": 0.3}),
    ("LoadSign", load_sign, (0.3, -1.0, 0.1), {"res": 512, "scale": 0.2}),
])
print("[warehouse] done ->", OUT)
