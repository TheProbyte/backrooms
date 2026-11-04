"""
Two chairs (2026-11-04), built in Blender and baked (bake_kit.py). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_chairs.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>]

  - LawnChair_White / LawnChair_Green: the one-piece injection-moulded resin stacking chair (the "monobloc", about
    23 x 22 x 32 in, its seat 16.5 in up, 2.5 kg): a dished seat rolled over at the front, a back curved round you of
    a frame and four slats, arms sweeping down into the front legs, tapered splayed legs of U section, ribs under the
    seat. Weathered: grey in its creases, chalky on top.
  - SchoolChair_Blue / SchoolChair_Orange: a classroom stacking chair (18 in seat): a one-piece polypropylene shell
    (a contoured seat, a curved back with its hand hole) on a frame of 7/8 in steel tube bent into two sides joined by
    stretchers, its glides, the shell's rivets to the frame.

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


def shell_rings(w0, w1, d, z_of, y0, y1, n=10, r=0.04, nc=4):
    """A seat-like surface as rounded-rect rings from its rim in: ring k shrinks toward the middle; z_of(t) its height."""
    rings = []
    for k in range(n + 1):
        t = k / n
        w = w0 + (w1 - w0) * 0  # (a rectangle that keeps its size: the dish is in the heights)
        s = 1.0 - t * 0.98
        ring = rrect((w0 * s), (d * s), max(0.002, r * s), 0.0, nc, 0.0, (y0 + y1) / 2)
        rings.append([Vector((p.x, p.y, z_of(t, p))) for p in ring])
    return rings


def curved_panel(name, w, h, z0, y_of, material, thick=0.008, nx=14, nz=10, top_bulge=0.0):
    """A panel curved round the sitter (y_of(x, z) gives its surface), solidified to a thickness: one sheet, then
    Blender's Solidify so both faces and the rim come out right."""
    bm = bmesh.new()
    vs = []
    for j in range(nz + 1):
        z = z0 + h * j / nz
        row = []
        for i in range(nx + 1):
            x = -w / 2 + w * i / nx
            row.append(bm.verts.new((x, y_of(x, z), z)))
        vs.append(row)
    for j in range(nz):
        for i in range(nx):
            bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
    for f in bm.faces:
        f.normal_update()
        if f.normal.y > 0:
            f.normal_flip()
    ob = new_obj(name, bm, [material])
    m = ob.modifiers.new("Solid", "SOLIDIFY")
    m.thickness = thick
    m.offset = -1.0
    m.use_even_offset = True
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=m.name)
    return ob


def lawn_chair(colour, name):
    PP = S("Plastic_LawnChair", colour, "plastic", 0.55)
    PP["finish"] = "plastic"
    p = []
    W, D, SH = 0.56, 0.44, 0.42          # seat width, depth, height (top of the seat at the front)
    yf, yb = -D / 2, D / 2 - 0.04
    # The seat: a dish (lowest a little behind its middle), rolled over at the front edge.
    def z_seat(t, q):
        return SH - 0.02 * math.sin(t * math.pi * 0.5) + 0.004 * (q.y - yf) / D
    rings = shell_rings(W, W, D, z_seat, yf, yb, 8, 0.07)
    top = loft("SeatTop", rings, PP, cap0=False, cap1=True)
    under = [[Vector((q.x, q.y, q.z - 0.008)) for q in ring] for ring in rings]
    bot = loft("SeatBot", under, PP, cap0=False, cap1=True)
    rim = loft("SeatRim", [rings[0], [Vector((q.x * 1.02, q.y * 1.02 + (0.01 if q.y < 0 else 0), q.z - 0.028)) for q in rings[0]]], PP, cap0=False, cap1=False)
    for o in (top, bot, rim):
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(o.data); bm.free()
    # flip so the top looks up, the underside down
    for o, up in ((top, True), (bot, False)):
        bm = bmesh.new(); bm.from_mesh(o.data)
        f0 = max(bm.faces, key=lambda f: f.calc_area())
        if (f0.normal.z > 0) != up:
            for f in bm.faces:
                f.normal_flip()
        bm.to_mesh(o.data); bm.free()
    p += [top, bot, rim]
    # Ribs under the seat (a cross and a ring), as moulded.
    for x in (-0.15, 0.0, 0.15):
        p.append(rbox("Rib", (0.008, D - 0.08, 0.03), (x, 0.0, SH - 0.045), PP, 0.003, 1))
    p.append(rbox("RibX", (W - 0.1, 0.008, 0.03), (0, 0.0, SH - 0.045), PP, 0.003, 1))
    # Legs: tapered, splayed out a little and the back ones back; U section shown by a dark groove card.
    def leg(top_c, bot_c, w_top, w_bot):
        rings = [rrect(w_top, w_top * 0.8, 0.012, 0.0, 3, 0, 0), rrect(w_bot, w_bot * 0.85, 0.008, 0.0, 3, 0, 0)]
        rr = []
        for ring, c in zip(rings, (top_c, bot_c)):
            rr.append([Vector((q.x + c.x, q.y + c.y, c.z)) for q in ring])
        o = loft("Leg", rr, PP, cap0=True, cap1=True, recalc=True)
        return o
    for sx in (-1, 1):
        p.append(leg(Vector((sx * 0.24, -0.18, SH - 0.03)), Vector((sx * 0.285, -0.255, 0.0)), 0.06, 0.04))
        p.append(leg(Vector((sx * 0.22, 0.16, SH - 0.03)), Vector((sx * 0.27, 0.27, 0.0)), 0.06, 0.04))
    # The back: one moulded panel curved round the sitter and leaning back, a rolled rim along its top, the slots
    # moulded through it (dark, both faces), thick uprights at its sides down into the back legs.
    def arc_pt(x, z, lean=12.0, curve=0.05):
        y = yb + 0.01 + curve * (x / (W / 2)) ** 2 * -1 + (z - SH) * math.tan(math.radians(lean))
        return Vector((x, y, z))
    back = curved_panel("Back", 0.5, 0.4, SH + 0.01, lambda x, z: arc_pt(x, z).y, PP, 0.009, 16, 10)
    p.append(back)
    p.append(tube("Rim", [arc_pt(-0.25 + 0.5 * i / 16, SH + 0.41) + Vector((0, 0.004, 0)) for i in range(17)], 0.012, PP, 8))
    for sx in (-1, 1):
        up = [arc_pt(sx * 0.252, SH - 0.02), arc_pt(sx * 0.255, SH + 0.15), arc_pt(sx * 0.252, SH + 0.3), arc_pt(sx * 0.25, SH + 0.41)]
        p.append(tube("Upright", catmull(up, 4), 0.017, PP, 8))
    slots = []
    for k in range(4):
        z = SH + 0.12 + k * 0.065
        for side in (-1, 1):
            c = arc_pt(0.0, z) + Vector((0, side * (0.0045 + 0.0012) - 0.0045 + (0.0 if side < 0 else 0.0), 0))
            slots.append((c, side, z))
    # Arms: from the uprights at 0.2 m above the seat, forward and down into the front legs.
    for sx in (-1, 1):
        arm = [arc_pt(sx * 0.255, SH + 0.2) + Vector((0, -0.01, 0)), Vector((sx * 0.275, 0.05, SH + 0.22)), Vector((sx * 0.28, -0.12, SH + 0.21)),
               Vector((sx * 0.27, -0.2, SH + 0.15)), Vector((sx * 0.255, -0.2, SH + 0.02))]
        p.append(tube("Arm", catmull(arm, 5), 0.02, PP, 8))
    ob = finish(p, name, sharp_deg=55)
    # The back's moulded slots: dark cards on both faces, following its curve a little (four short spans each).
    sc = Canvas(256, 40, tuple(v * 0.22 for v in colour))
    SL = print_mat("Plastic_LawnSlot", sc)
    cards = []
    for k in range(4):
        z = SH + 0.12 + k * 0.065
        for seg in range(4):
            x0 = -0.17 + 0.34 * seg / 4
            x1 = x0 + 0.34 / 4
            xm = (x0 + x1) / 2
            for side in (-1, 1):
                pnt = arc_pt(xm, z)
                cards.append(card("Slot", x1 - x0 + 0.002, 0.022, (xm, pnt.y + (-0.0012 if side < 0 else 0.009 + 0.0012), z), SL,
                                  normal=(0, side, 0)))
    return join([ob] + cards, name)


def school_chair(colour, name):
    PP = S("Plastic_SchoolShell", colour, "plastic", 0.4)
    ST = S("Paint_SchoolFrame", (0.08, 0.08, 0.085), "paint", 0.4)
    GL = S("Plastic_SchoolGlide", (0.1, 0.1, 0.1), "plastic", 0.5)
    RV = S("Steel_SchoolRivet", (0.6, 0.6, 0.6), "metal", 0.4, 1.0)
    p = []
    W, D, SH = 0.45, 0.42, 0.46
    yf, yb = -D / 2, D / 2 - 0.02
    def z_seat(t, q):
        side = abs(q.x) / (W / 2)
        return SH - 0.022 * math.sin(t * math.pi * 0.5) + 0.01 * side ** 2 + 0.006 * (q.y - yf) / D
    rings = shell_rings(W, W, D, z_seat, yf, yb, 8, 0.06)
    top = loft("Seat", rings, PP, cap0=False, cap1=True)
    under = [[Vector((q.x, q.y, q.z - 0.006)) for q in ring] for ring in rings]
    bot = loft("SeatU", under, PP, cap0=False, cap1=True)
    for o, upw in ((top, True), (bot, False)):
        bm = bmesh.new(); bm.from_mesh(o.data)
        f0 = max(bm.faces, key=lambda f: f.calc_area())
        if (f0.normal.z > 0) != upw:
            for f in bm.faces:
                f.normal_flip()
        bm.to_mesh(o.data); bm.free()
    p += [top, bot]
    p.append(loft("Edge", [rings[0], [Vector((q.x, q.y, q.z - 0.006)) for q in rings[0]]], PP, cap0=False, cap1=False, recalc=True))
    # The back: a curved panel rising from the seat's back, leaning back (solidified: both faces and the rim).
    bw, bh = 0.43, 0.32
    def back_y(x, z):
        return yb + 0.06 - 0.035 * (x / (bw / 2)) ** 2 + (z - SH) * math.tan(math.radians(14))
    bk = curved_panel("Back", bw, bh, SH + 0.07, back_y, PP, 0.007, 14, 9)
    p.append(bk)
    # The neck joining seat and back.
    p.append(rbox("Neck", (0.34, 0.03, 0.09), (0, yb + 0.035, SH + 0.035), PP, 0.012, 3))
    # The frame: two sides of bent tube (front leg, under the seat, up behind as the back support, down as the back leg).
    r = 0.011
    for sx in (-1, 1):
        x = sx * 0.2
        side = [Vector((x * 1.05, -0.2, 0.012)), Vector((x, -0.17, SH - 0.06)), Vector((x, -0.12, SH - 0.03)), Vector((x, 0.12, SH - 0.03)),
                Vector((x, 0.19, SH - 0.06)), Vector((x * 1.05, 0.24, 0.012))]
        p.append(tube("Side", catmull(side, 6), r, ST, 10))
        sup = [Vector((x * 0.9, 0.14, SH - 0.03)), Vector((x * 0.9, 0.2, SH + 0.08)), Vector((x * 0.9, 0.235, SH + 0.2))]
        p.append(tube("Support", catmull(sup, 5), r * 0.9, ST, 10))
        for c in (Vector((x * 1.05, -0.2, 0.0)), Vector((x * 1.05, 0.24, 0.0))):
            p.append(lathe("Glide", [(0.0, 0.0), (0.014, 0.0), (0.014, 0.006), (0.011, 0.014), (0.0, 0.014)], GL, 12, xf=M4(None, c)))
        for y in (-0.1, 0.1):
            p.append(sphere("Rivet", (x, y, SH - 0.017), (0.006, 0.006, 0.003), RV, 8, 4))
    for (y, z) in ((-0.17, SH - 0.08), (0.18, SH - 0.08)):
        p.append(lathe("Stretcher", [(0.0, 0.0), (r * 0.85, 0.0), (r * 0.85, 0.42), (0.0, 0.42)], ST, 10, xf=M4(rot('Y', 90), (-0.21, y, z))))
    base = finish(p, name, sharp_deg=55)
    # The hand hole near the top of the back: a dark slot printed on both faces.
    c = Canvas(128, 48, colour)
    c.rect(14, 12, 114, 36, tuple(v * 0.25 for v in colour))
    hz = SH + 0.07 + bh * 0.84
    hy = back_y(0.0, hz)
    hole_f = card("HoleF", 0.1, 0.03, (0, hy - 0.0008, hz), print_mat("Plastic_SchoolHole", c), normal=(0, -1, 0))
    hole_b = card("HoleB", 0.1, 0.03, (0, hy + 0.0078, hz), print_mat("Plastic_SchoolHoleB", c), normal=(0, 1, 0))
    return join([base, hole_f, hole_b], name)


run_baked([
    ("LawnChair_White", lambda: lawn_chair((0.9, 0.9, 0.87), "LawnChair_White"), (0.8, -1.0, 0.5), {"scale": 0.6, "px_per_m": 500.0}),
    ("LawnChair_Green", lambda: lawn_chair((0.17, 0.36, 0.22), "LawnChair_Green"), (0.8, -1.0, 0.5), {"scale": 0.6, "px_per_m": 500.0}),
    ("SchoolChair_Blue", lambda: school_chair((0.13, 0.28, 0.58), "SchoolChair_Blue"), (0.8, -1.0, 0.4), {"scale": 0.6, "px_per_m": 500.0}),
    ("SchoolChair_Orange", lambda: school_chair((0.85, 0.4, 0.1), "SchoolChair_Orange"), (0.8, -1.0, 0.4), {"scale": 0.6, "px_per_m": 500.0}),
])
print("[chairs] done ->", OUT)
