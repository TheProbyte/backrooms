"""
Level 0's ceiling lights (2026-11-04, the realism pass), built in Blender and baked (bake_kit.py). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_lights.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>] [--only TrofferFrame]

  - TrofferFrame: a 2x2 lay-in troffer's door frame, the die-formed steel C-channel round its lens (mitred corners, a
    25 mm face, a lip the lens rests on 10 mm up), two spring latches on one side, the hinge pins on the other. The
    lens itself is the game's (its glow and its ageing are textures: MaterialMaps.TrofferUV).
  - TrofferHousing: the sheet-metal pan above it (seen only when a fitting hangs out of the grid): sloped sides, the
    ballast channel, knockouts, a label, the flexible conduit whip up into the plenum.
  - StripChannel: a 4 ft two-lamp industrial strip with its white reflector hood, the channel, the four lampholders
    ("tombstones"), the tubes' aluminium end caps and pins, two hanging hooks. StripTube: one T8 tube's glass (25.4 mm
    across, between its caps), drawn by the game with the fixture's lit or unlit material. ChainLinks: 100 mm of
    sash chain the game repeats up to the roof.

Sizes from manufacturers' sheets: Lithonia/Metalux 2x2 lensed troffers (lens 0.1" acrylic in a die-formed door frame,
lamp to lens about 2-9/16"), 4 ft two-lamp T8 industrial strips with a reflector (a little over 48" long). Frames as the
game places them: origin at the fixture's middle on the ceiling's plane (troffer) or at the channel's underside (strip);
Blender Z up, tubes along X. Unity (x, y, z) = (-x, z, -y): the handedness changes, nothing reads mirrored.
"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_rooms.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- break room")
exec(compile(_src[:_cut], "build_rooms_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix


def rect_ring(half, z, nc=0, r=0.0):
    """A square ring (half-size half) at height z, counter-clockwise from +Z; nc > 0 rounds its corners."""
    if nc and r > 0:
        return rrect(2 * half, 2 * half, r, z, nc)
    return [Vector((half, half, z)), Vector((-half, half, z)), Vector((-half, -half, z)), Vector((half, -half, z))]


def troffer_frame():
    PNT = S("Paint_TrofferFrame", (0.9, 0.9, 0.88), "paint", 0.45)
    DK = S("Steel_TrofferLatch", (0.6, 0.6, 0.58), "metal", 0.4, 1.0)
    # The channel's section, outer edge first: (in from the outer edge, height); the lens rests on the last ledge.
    prof = [(0.0, -0.001), (0.0, -0.011), (0.0008, -0.0145), (0.003, -0.0165), (0.0055, -0.017), (0.0155, -0.017),
            (0.0172, -0.0162), (0.018, -0.0148), (0.018, -0.0078), (0.0205, -0.0075)]
    rings = [rect_ring(0.3 - d, z) for d, z in prof]
    fr = loft("Frame", rings, PNT, cap0=False, cap1=False)
    bm = bmesh.new()
    bm.from_mesh(fr.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    low = min(bm.faces, key=lambda f: f.calc_center_median().z)
    if low.normal.z > 0:
        for f in bm.faces:
            f.normal_flip()
    bm.to_mesh(fr.data)
    bm.free()
    p = [fr]
    # The mitres: a hairline at each corner of the face.
    for sx in (-1, 1):
        for sy in (-1, 1):
            seam = rbox("Mitre", (0.022, 0.0009, 0.0004), (0, 0, 0), DK, 0.0, 1)
            place(seam, rot('Z', 45 * sx * sy), (sx * (0.3 - 0.0085), sy * (0.3 - 0.0085), -0.0172))
            p.append(seam)
    # Two spring latches on one side, the hinge pins on the other.
    for x in (-0.16, 0.16):
        p.append(rbox("LatchSlot", (0.022, 0.006, 0.0006), (x, 0.3 - 0.0105, -0.0171), DK, 0.0, 1))
        p.append(rbox("Latch", (0.014, 0.004, 0.003), (x, 0.3 - 0.0105, -0.0168), DK, 0.0012, 1))
        p.append(lathe("Pin", [(0.0, 0.0), (0.0025, 0.0), (0.0025, 0.012), (0.0, 0.012)], DK, 8,
                       xf=M4(rot('Y', 90), (x - 0.006, -0.3 + 0.0015, -0.012))))
    return finish(p, "TrofferFrame", sharp_deg=50)


def troffer_housing():
    PNT = S("Paint_TrofferHousing", (0.86, 0.86, 0.84), "paint", 0.5)
    GAL = S("Galv_TrofferWhip", (0.62, 0.63, 0.62), "galv", 0.5, 1.0)
    # The pan: open below, its sides sloping in to the top, a flange round the bottom.
    rings = [rect_ring(0.282, 0.0), rect_ring(0.282, 0.012), rect_ring(0.268, 0.016), rect_ring(0.25, 0.085),
             rect_ring(0.24, 0.095), rect_ring(0.0001, 0.095)]
    pan = loft("Pan", rings, PNT, cap0=False, cap1=False, recalc=True)
    bm = bmesh.new()
    bm.from_mesh(pan.data)
    top = max(bm.faces, key=lambda f: f.calc_center_median().z)
    if top.normal.z < 0:
        for f in bm.faces:
            f.normal_flip()
    bm.to_mesh(pan.data)
    bm.free()
    p = [pan]
    # The ballast channel across the top, its end plates, knockouts, the whip and its connector.
    p.append(rbox("Channel", (0.5, 0.085, 0.024), (0, 0, 0.107), PNT, 0.004, 2))
    for sx in (-1, 1):
        p.append(rbox("EndPlate", (0.006, 0.09, 0.03), (sx * 0.253, 0, 0.11), PNT, 0.002, 1))
    for (x, y) in ((0.15, 0.17), (-0.18, -0.16), (0.2, -0.15)):
        p.append(lathe("Knockout", [(0.0, 0.0), (0.014, 0.0), (0.014, 0.001), (0.0, 0.001)], PNT, 16, xf=M4(None, (x, y, 0.0952))))
    p.append(lathe("Connector", [(0.0, 0.0), (0.016, 0.0), (0.016, 0.006), (0.012, 0.009), (0.012, 0.03), (0.0, 0.03)], GAL, 14,
                   xf=M4(None, (0.12, 0.0, 0.119))))
    whip = catmull([(0.12, 0.0, 0.149), (0.13, 0.02, 0.24), (0.2, 0.08, 0.33), (0.32, 0.12, 0.37), (0.5, 0.1, 0.38)], 7)
    ribs = []
    for i, q in enumerate(whip):
        ribs.append(0.0105 if i % 2 else 0.0118)
    p.append(tube("Whip", whip, ribs, GAL, 10))
    base = finish(p, "TrofferHousing", sharp_deg=45)
    c = Canvas(160, 96, (0.93, 0.92, 0.88))
    c.rect(0, 0, 160, 22, (0.85, 0.7, 0.1))
    c.text_c("CAUTION", 80, 5, 2, (0.05, 0.05, 0.05))
    c.text_c("RISK OF FIRE", 80, 30, 2, (0.05, 0.05, 0.05))
    c.lines(10, 52, 150, 4, 9, (0.35, 0.35, 0.35), seed=4)
    lab = card("Label", 0.09, 0.054, (-0.07, 0.03, 0.1195), print_mat("Paper_TrofferLabel", c), normal=(0, 0, 1))
    return join([base, lab], "TrofferHousing")


def strip_channel():
    REF = S("Paint_StripReflector", (0.9, 0.9, 0.88), "paint", 0.4)
    HOL = S("Plastic_StripHolder", (0.88, 0.87, 0.83), "plastic", 0.45)
    CAP = S("Alu_TubeCap", (0.74, 0.74, 0.72), "metal", 0.35, 1.0)
    HK = S("Galv_StripHook", (0.6, 0.61, 0.6), "galv", 0.5, 1.0)
    L = 1.24
    p = []
    # The hood: a shallow trapezoid open below, a rolled hem along its bottom edges, closed at the ends.
    sec = [(-0.08, 0.002), (-0.079, 0.0), (-0.0775, 0.0005), (-0.076, 0.004), (-0.042, 0.058), (-0.038, 0.06), (0.038, 0.06),
           (0.042, 0.058), (0.076, 0.004), (0.0775, 0.0005), (0.079, 0.0), (0.08, 0.002)]
    bm = bmesh.new()
    rows = []
    for x in (-L / 2, L / 2):
        rows.append([bm.verts.new((x, y, z)) for y, z in sec])
    for i in range(len(sec) - 1):
        bm.faces.new((rows[0][i], rows[0][i + 1], rows[1][i + 1], rows[1][i]))
    # the inside, a sheet's thickness in, so the hood has two faces
    inner = [(y * 0.97, z - 0.0012) for y, z in sec[3:-3]]
    irows = []
    for x in (-L / 2 + 0.002, L / 2 - 0.002):
        irows.append([bm.verts.new((x, y, z)) for y, z in inner])
    for i in range(len(inner) - 1):
        bm.faces.new((irows[0][i + 1], irows[0][i], irows[1][i], irows[1][i + 1]))
    # Each sheet faces the right way: the outer one away from the hood's hollow, the inner one into it.
    nouter = len(sec) - 1
    for k, f in enumerate(bm.faces):
        f.normal_update()
        c = f.calc_center_median()
        away = f.normal.dot(c - Vector((c.x, 0.0, -0.01))) > 0
        if (k < nouter) != away:
            f.normal_flip()
    hood = new_obj("Hood", bm, [REF])
    p.append(hood)
    for sx in (-1, 1):
        endp = [Vector((sx * L / 2, y, z)) for y, z in sec[3:-3]]
        p.append(loft("End", [endp, Vector((sx * L / 2, 0.0, 0.03))], REF, cap0=False, cap1=False, recalc=False))
    # The channel inside the hood, the lampholders at each end, the tubes' caps and pins.
    p.append(rbox("Channel", (L - 0.03, 0.06, 0.035), (0, 0, 0.0375), REF, 0.004, 2))
    for sx in (-1, 1):
        x = sx * 0.597
        for y in (-0.04, 0.04):
            p.append(rbox("Holder", (0.016, 0.032, 0.05), (x + sx * 0.002, y, 0.0), HOL, 0.004, 2))
            p.append(lathe("Cap", [(0.0, 0.0), (0.0128, 0.0), (0.0128, 0.017), (0.0115, 0.019), (0.0, 0.019)], CAP, 18,
                           xf=M4(rot('Y', -90 * sx), (sx * 0.579, y, -0.012))))
            for dz in (-0.006, 0.006):
                p.append(lathe("PinStub", [(0.0, 0.0), (0.0012, 0.0), (0.0012, 0.004), (0.0, 0.004)], CAP, 6,
                               xf=M4(rot('Y', -90 * sx), (sx * 0.5985, y, -0.012 + dz))))
    # The hooks it hangs from (the chain runs on up from these).
    for x in (-0.45, 0.45):
        p.append(tube("Hook", catmull([(x, 0.0, 0.06), (x, 0.0, 0.075), (x + 0.008, 0.0, 0.085), (x, 0.0, 0.094), (x - 0.008, 0.0, 0.1),
                                        (x, 0.0, 0.108)], 4), 0.002, HK, 6))
        p.append(rbox("Bracket", (0.03, 0.03, 0.004), (x, 0.0, 0.0605), HK, 0.001, 1))
    base = finish(p, "StripChannel", sharp_deg=45)
    c = Canvas(256, 48, (0.9, 0.9, 0.88))
    for i in range(14):
        c.rect(10 + i * 17, 14, 22 + i * 17, 34, (0.06, 0.06, 0.06))
    slots = card("Slots", 0.5, 0.03, (0, 0, 0.0602), print_mat("Paint_StripSlots", c), normal=(0, 0, 1))
    return join([base, slots], "StripChannel")


def strip_tube():
    G = S("Glass_StripTube", (0.95, 0.95, 0.93), "clean", 0.2)
    L = 1.158
    return finish([lathe("Tube", [(0.0, 0.0), (0.0127, 0.0), (0.0127, L), (0.0, L)], G, 18, xf=M4(rot('Y', 90), (-L / 2, 0, 0)))],
                  "StripTube", sharp_deg=60)


def chain_links():
    GAL = S("Galv_Chain", (0.6, 0.61, 0.6), "galv", 0.5, 1.0)
    p = []
    pitch = 0.025
    for k in range(4):
        z = k * pitch + pitch / 2
        pts = []
        for i in range(7):
            a = 2 * math.pi * i / 6 + math.pi / 2
            pts.append(Vector((0.0045 * math.cos(a), 0.0, z + 0.0145 * math.sin(a))))
        ln = tube("Link", pts, 0.0017, GAL, 3, cap0=False, cap1=False)
        if k % 2:
            place(ln, rot('Z', 90))
        p.append(ln)
    return finish(p, "ChainLinks", sharp_deg=70)


def cable_gripper():
    GAL = S("Galv_Gripper", (0.62, 0.63, 0.62), "galv", 0.45, 1.0)
    p = [lathe("Body", [(0.0, 0.0), (0.004, 0.0), (0.0065, 0.004), (0.0065, 0.034), (0.005, 0.038), (0.0015, 0.04), (0.0, 0.04)], GAL, 12)]
    p.append(tube("Hook", catmull([(0.0, 0.0, 0.0), (0.0, 0.0, -0.012), (0.007, 0.0, -0.02), (0.0, 0.0, -0.028), (-0.007, 0.0, -0.02)], 4),
                  0.0016, GAL, 5))
    # The ceiling's end: an anchor plate and an eye.
    return finish(p, "CableGripper", sharp_deg=50)


def cable_anchor():
    GAL = S("Galv_Anchor", (0.6, 0.61, 0.6), "galv", 0.45, 1.0)
    p = [lathe("Plate", [(0.0, 0.0), (0.018, 0.0), (0.018, 0.004), (0.012, 0.006), (0.0, 0.006)], GAL, 14, xf=M4(rot('X', 180), (0, 0, 0)))]
    p.append(tube("Eye", catmull([(-0.004, 0.0, -0.006), (-0.006, 0.0, -0.014), (0.0, 0.0, -0.02), (0.006, 0.0, -0.014), (0.004, 0.0, -0.006)], 4),
                  0.0018, GAL, 5))
    return finish(p, "CableAnchor", sharp_deg=50)


run_baked([
    ("CableGripper", cable_gripper, (0.6, -1.0, 0.2), {"res": 256, "scale": 0.2}),
    ("CableAnchor", cable_anchor, (0.6, -1.0, -0.4), {"res": 256, "scale": 0.2}),
    ("TrofferFrame", troffer_frame, (0.3, -0.5, -1.0), {"scale": 0.4, "px_per_m": 500.0}),
    ("TrofferHousing", troffer_housing, (0.6, -0.8, 0.7), {"scale": 0.4, "px_per_m": 500.0}),
    ("StripChannel", strip_channel, (0.4, -1.0, -0.6), {"scale": 0.5, "px_per_m": 500.0}),
    ("StripTube", strip_tube, (0.4, -1.0, -0.3), {"res": 256, "scale": 0.3}),
    ("ChainLinks", chain_links, (0.6, -1.0, 0.2), {"res": 256, "scale": 0.2}),
])
print("[lights] done ->", OUT)
