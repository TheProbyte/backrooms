"""
A contract floor's edge (2026-11-07, Docs/ContractFloorsPlan.md): what stands on the walls where Overflow's rented space
ends (Generation/LeaseEdges places them). Built in the Overflow look like the hub's fittings (build_hub.py), baked with
dirt and wear (bake_kit.py). Run headless, always with the Overflow style:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_lease.py -- Assets/Backrooms/Resources/Overflow/Fittings --style overflow [--preview <dir>] [--only LeaseFireDoor]

  - LeaseFireDoor: a pair of steel fire doors (1.84 x 2.15 m) in a frame on the wall, push bars chained together through
    their handles and padlocked, a tag on the chain, a NOT LEASED plate over them.
  - LeaseBricked: an old door frame (1.16 x 2.16 m) filled with concrete blocks, its hinges left on, a plywood board
    stencilled NOT LEASED screwed over the blocks.
  - LeaseSheeting: heavy plastic sheeting (2.1 x 2.35 m) stapled to a batten, hanging in folds, hazard tape across it
    and a printed notice: a way through that was closed off.
  - LeaseSign: a wall sign (0.56 x 0.3) in the company's colours: NOT LEASED / NO ACCESS BEYOND THIS POINT. (Its foot is
    its bottom edge; the game hangs it 1.45 m up.)

Frames: Blender front toward -Y, Z up, back on the wall (y = 0), origin at the middle of the foot. Unity (x, y, z) =
(x, z, -y).
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
from mathutils import Vector


def hx(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


AMBER, ORANGE, GOLD, AVOCADO, CHOC = hx('#F0A21E'), hx('#D9661F'), hx('#E8B23A'), hx('#7C8A2E'), hx('#3B2A1E')
CREAM = hx('#E8DCC0')
RED = (0.72, 0.14, 0.09)


def plate_art(name, w_px, h_px, big, small):
    """The company's sign: chocolate, amber capitals, the stripe along the bottom."""
    c = Canvas(w_px, h_px, CHOC)
    c.rect(0, h_px - 12, w_px, h_px - 8, ORANGE); c.rect(0, h_px - 8, w_px, h_px - 4, GOLD); c.rect(0, h_px - 4, w_px, h_px, AVOCADO)
    c.text_c(big, w_px / 2, 10, 3, AMBER)
    if small:
        c.text_c(small, w_px / 2, 42, 1, CREAM)
    return print_mat("Print_" + name, c, sharp=True)


def link(name, center, axis_x, mat, L=0.05, W=0.03, r=0.0055):
    """One oval chain link round its centre, lying in the plane of axis_x and up (axis_x True) or of axis_x and -Y."""
    pts = []
    for k in range(13):
        a = 2 * math.pi * k / 12
        u, v = math.cos(a) * (L / 2 - r), math.sin(a) * (W / 2 - r)
        if axis_x:
            pts.append((center[0] + u, center[1], center[2] + v))
        else:
            pts.append((center[0] + u, center[1] + v, center[2]))
    return tube(name, pts, r, mat, sides=6, cap0=False, cap1=False)


# ------------------------------------------------------------------------------------------------- fire door

def fire_door():
    FR = S("Metal_LeaseDoorFrame", hx('#4C5348'), "metal", 0.5, 0.3)
    LF = S("Metal_LeaseDoorLeaf", hx('#6E7A64'), "metal", 0.45, 0.25)      # battleship green, gone grey
    BAR = S("Metal_LeaseDoorBar", (0.62, 0.62, 0.6), "metal", 0.35, 1.0)
    GL = S("Glass_LeaseDoorWire", (0.08, 0.09, 0.09), "glass", 0.15)
    CH = S("Metal_LeaseChain", (0.5, 0.5, 0.48), "galv", 0.4, 1.0)
    LK = S("Metal_LeasePadlock", (0.78, 0.6, 0.28), "metal", 0.35, 1.0)
    W_, H_ = 1.84, 2.1
    leaf = (W_ - 0.04) / 2
    p = [rbox("JambL", (0.08, 0.07, H_ + 0.08), (-W_ / 2 - 0.04, -0.035, (H_ + 0.08) / 2), FR, 0.012, 2),
         rbox("JambR", (0.08, 0.07, H_ + 0.08), (W_ / 2 + 0.04, -0.035, (H_ + 0.08) / 2), FR, 0.012, 2),
         rbox("Head", (W_ + 0.16, 0.07, 0.08), (0, -0.035, H_ + 0.04), FR, 0.012, 2),
         rbox("Mullion", (0.03, 0.06, H_), (0, -0.04, H_ / 2), FR, 0.006, 1)]
    for sx in (-1, 1):
        cx = sx * (leaf / 2 + 0.015)
        p.append(rbox("Leaf", (leaf, 0.05, H_ - 0.01), (cx, -0.045, (H_ - 0.01) / 2), LF, 0.01, 2))
        p.append(rbox("Kick", (leaf - 0.06, 0.006, 0.25), (cx, -0.072, 0.15), BAR, 0.003, 1))
        p.append(rbox("Vision", (0.22, 0.012, 0.38), (cx + sx * -0.12, -0.072, 1.55), GL, 0.004, 1))
        p.append(rbox("VisionFrame", (0.26, 0.008, 0.42), (cx + sx * -0.12, -0.069, 1.55), BAR, 0.004, 1))
        # The push bar: a housing at each end and the bar between, at 1.0 m.
        x0, x1 = cx - leaf / 2 + 0.08, cx + leaf / 2 - 0.08
        p.append(rbox("BarEnd", (0.07, 0.07, 0.11), (x0, -0.105, 1.0), BAR, 0.015, 2))
        p.append(rbox("BarEnd", (0.07, 0.07, 0.11), (x1, -0.105, 1.0), BAR, 0.015, 2))
        p.append(tube("Bar", [(x0, -0.13, 1.0), (x1, -0.13, 1.0)], 0.017, BAR, sides=10))
    # The chain: round both bars near the middle, sagging between them, and a padlock where its ends meet.
    rng = random.Random(5)
    n = 16
    for k in range(n):
        u = k / (n - 1)
        x = -0.36 + 0.72 * u
        z = 1.0 - 0.07 * math.sin(math.pi * u) + rng.uniform(-0.004, 0.004)
        p.append(link("Link", (x, -0.152 - 0.006 * (k % 2), z), k % 2 == 0, CH))
    for x in (-0.36, 0.36):                             # wrapped round each bar
        for k in range(4):
            a = 2 * math.pi * k / 4
            p.append(link("Wrap", (x + (k - 1.5) * 0.02, -0.13 + 0.03 * math.cos(a), 1.0 + 0.03 * math.sin(a)), False, CH))
    p.append(rbox("Lock", (0.065, 0.028, 0.075), (0.02, -0.165, 0.86), LK, 0.01, 2))
    p.append(tube("Shackle", [(0.0, -0.165, 0.895), (0.0, -0.165, 0.93), (0.02, -0.165, 0.945), (0.04, -0.165, 0.93), (0.04, -0.165, 0.895)], 0.0055, CH, sides=6))
    body = finish(p, "LeaseFireDoor", sharp_deg=50)
    sign = card("Plate", 0.7, 0.16, (0, -0.074, H_ + 0.2), plate_art("LeaseFireDoorPlate", 280, 64, "NOT LEASED", "OVERFLOW PROPERTY - NO ENTRY"))
    plate_back = finish([rbox("PlateBack", (0.72, 0.02, 0.18), (0, -0.06, H_ + 0.2), S("Wood_LeasePlate", CHOC, "wood", 0.5), 0.006, 1)], "PlateBack", sharp_deg=50)
    c = Canvas(64, 96, (0.86, 0.78, 0.55))
    c.disc(32, 10, 5, (0.4, 0.33, 0.2))
    c.text_c("VACANT", 32, 26, 1, RED)
    c.text_c("DO NOT", 32, 44, 1, CHOC)
    c.text_c("OPEN", 32, 56, 1, CHOC)
    c.lines(8, 72, 56, 2, 8, (0.5, 0.42, 0.3))
    tag = card("Tag", 0.07, 0.105, (0.07, -0.168, 0.82), print_mat("Print_LeaseTag", c), roll=-8)
    return join([body, sign, plate_back, tag], "LeaseFireDoor")


# ------------------------------------------------------------------------------------------------- bricked doorway

def bricked():
    FR = S("Wood_LeaseBrickFrame", hx('#5A3A22'), "wood", 0.5)
    BL = S("Concrete_LeaseBlock", (0.62, 0.61, 0.58), "concrete", 0.85)
    MO = S("Concrete_LeaseMortar", (0.5, 0.49, 0.46), "concrete", 0.9)
    HG = S("Metal_LeaseHinge", (0.6, 0.55, 0.42), "metal", 0.4, 1.0)
    PW = S("Wood_LeasePlywood", hx('#C9A877'), "wood", 0.7)
    W_, H_ = 1.0, 2.1
    p = [rbox("JambL", (0.08, 0.06, H_ + 0.06), (-W_ / 2 - 0.04, -0.03, (H_ + 0.06) / 2), FR, 0.01, 2),
         rbox("JambR", (0.08, 0.06, H_ + 0.06), (W_ / 2 + 0.04, -0.03, (H_ + 0.06) / 2), FR, 0.01, 2),
         rbox("Head", (W_ + 0.16, 0.06, 0.08), (0, -0.03, H_ + 0.04), FR, 0.01, 2),
         rbox("Mortar", (W_, 0.02, H_), (0, -0.012, H_ / 2), MO, 0.0, 1)]
    for z in (0.3, 1.0, 1.75):
        p.append(rbox("Hinge", (0.03, 0.012, 0.1), (-W_ / 2 + 0.005, -0.066, z), HG, 0.003, 1))
    rng = random.Random(11)
    bw, bh, g = 0.39, 0.19, 0.012
    rows = int(H_ / (bh + g))
    for r in range(rows):
        z = r * (bh + g) + bh / 2 + g / 2
        off = (bw + g) / 2 if r % 2 else 0.0
        x = -W_ / 2 - off
        while x < W_ / 2:
            xa, xb = max(x, -W_ / 2), min(x + bw, W_ / 2)
            if xb - xa > 0.04:
                d = 0.012 + rng.uniform(0.0, 0.006)
                p.append(rbox("Block", (xb - xa - 0.002, d, bh), ((xa + xb) / 2, -0.022 - d / 2, z), BL, 0.006, 1))
            x += bw + g
    body = finish(p, "LeaseBricked", sharp_deg=50)
    board = finish([rbox("Board", (0.62, 0.012, 0.3), (0, -0.046, 1.42), PW, 0.004, 1)], "Board", sharp_deg=50)
    c = Canvas(248, 120, hx('#C9A877'))
    c.text_c("NOT", 124, 14, 4, RED)
    c.text_c("LEASED", 124, 52, 4, RED)
    c.text_c("NO ACCESS", 124, 96, 2, CHOC)
    for (sx, sy) in ((8, 8), (232, 8), (8, 104), (232, 104)):
        c.disc(sx + 4, sy + 4, 3, (0.45, 0.45, 0.45))
    art = card("BoardArt", 0.61, 0.295, (0, -0.0526, 1.42), print_mat("Print_LeaseBoard", c, sharp=True))
    return join([body, board, art], "LeaseBricked")


# ------------------------------------------------------------------------------------------------- sheeting

def sheeting():
    BT = S("Wood_LeaseBatten", hx('#9C7A50'), "wood", 0.6)
    PL = S("Plastic_LeaseSheet", (0.8, 0.82, 0.79), "plastic", 0.35)
    W_, H_ = 2.1, 2.35
    nx, nz = 44, 24
    bm = bmesh.new()
    rng = random.Random(7)
    folds = [(rng.uniform(-W_ / 2, W_ / 2), rng.uniform(0.02, 0.05), rng.uniform(0.12, 0.3)) for _ in range(7)]
    vs = []
    for j in range(nz + 1):
        v = j / nz
        z = H_ * v
        row = []
        for i in range(nx + 1):
            u = i / nx
            x = -W_ / 2 + W_ * u
            sag = (1 - v) ** 1.5
            y = -0.04
            for (fx, amp, wid) in folds:
                y -= amp * sag * math.exp(-((x - fx) / wid) ** 2)
            y -= 0.012 * math.sin(x * 9.0 + v * 2.0) * sag
            if j == 0:
                y -= 0.03 * (0.5 + 0.5 * math.sin(x * 5.0))      # the hem, lifted and kicked out
            z2 = z + (0.015 * math.sin(x * 7.0) if j == 0 else 0.0)
            row.append(bm.verts.new((x, y, z2)))
        vs.append(row)
    for j in range(nz):
        for i in range(nx):
            bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.normal_update()
        if f.normal.y > 0:
            f.normal_flip()
    sheet = new_obj("Sheet", bm, [PL])
    for poly in sheet.data.polygons:
        poly.use_smooth = True
    sheet_back = new_obj("SheetBack", bm_copy_flipped(sheet), [PL])
    batten = finish([rbox("Batten", (W_ + 0.06, 0.035, 0.05), (0, -0.03, H_ + 0.02), BT, 0.006, 1)], "Batten", sharp_deg=50)
    # Hazard tape in an X across the sheet, and a printed notice in the middle.
    tc = Canvas(256, 16, (0.95, 0.78, 0.1))
    for k in range(0, 256, 32):
        for dx in range(16):
            for yy in range(16):
                xx = k + dx + yy
                if 0 <= xx < 256:
                    tc.rect(xx, yy, xx + 1, yy + 1, (0.08, 0.08, 0.07))
    tape = print_mat("Print_LeaseTape", tc)
    ang = math.degrees(math.atan2(1.7, W_ - 0.3))
    t1 = card("Tape1", 2.4, 0.07, (0, -0.105, 1.3), tape, roll=ang)
    t2 = card("Tape2", 2.4, 0.07, (0, -0.108, 1.3), tape, roll=-ang)
    nc = Canvas(160, 200, (0.96, 0.95, 0.9))
    nc.rect(0, 0, 160, 34, ORANGE)
    nc.text_c("NOTICE", 80, 8, 3, (1, 1, 1))
    nc.text_c("THIS SPACE", 80, 56, 2, CHOC)
    nc.text_c("IS NOT", 80, 80, 2, CHOC)
    nc.text_c("LEASED", 80, 104, 2, RED)
    nc.lines(16, 138, 144, 4, 12, (0.55, 0.52, 0.48))
    nc.text_c("OVERFLOW", 80, 186, 1, ORANGE)
    notice = card("Notice", 0.3, 0.375, (0.0, -0.112, 1.3), print_mat("Print_LeaseNotice", nc, sharp=True))
    return join([sheet, sheet_back, batten, t1, t2, notice], "LeaseSheeting")


def bm_copy_flipped(ob):
    """A copy of a mesh object's faces turned the other way, a hair behind: the sheet seen from the back."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for v in bm.verts:
        v.co.y += 0.002
    for f in bm.faces:
        f.normal_flip()
    return bm


# ------------------------------------------------------------------------------------------------- sign

def lease_sign():
    BD = S("Wood_LeaseSignBoard", CHOC, "wood", 0.5)
    W_, H_, D_ = 0.56, 0.3, 0.03
    body = finish([rbox("Board", (W_, D_, H_), (0, -D_ / 2, H_ / 2), BD, 0.012, 2)], "LeaseSign", sharp_deg=50)
    art = card("Art", W_ - 0.03, H_ - 0.03, (0, -D_ - 0.002, H_ / 2), plate_art("LeaseSign", 224, 120, "NOT LEASED", ""))
    c = Canvas(224, 40, CHOC)
    c.text_c("NO ACCESS BEYOND", 112, 4, 1, CREAM)
    c.text_c("THIS POINT", 112, 16, 1, CREAM)
    sub = card("Sub", W_ - 0.06, 0.06, (0, -D_ - 0.003, H_ * 0.36), print_mat("Print_LeaseSignSub", c, sharp=True))
    return join([body, art, sub], "LeaseSign")


# ------------------------------------------------------------------------------------------------- seams

# The crack's outline (m: x along the wall, z up), shared with Interaction/Seam.cs (Seam.Outline).
OUTLINE = [(0.02, 0.45), (0.08, 0.62), (0.05, 0.80), (0.16, 0.98), (0.12, 1.15), (0.24, 1.32), (0.18, 1.52), (0.22, 1.70),
           (0.10, 1.88), (0.12, 2.05), (0.03, 2.25), (-0.04, 2.08), (-0.12, 1.92), (-0.08, 1.74), (-0.20, 1.55),
           (-0.14, 1.36), (-0.22, 1.18), (-0.10, 1.00), (-0.14, 0.82), (-0.04, 0.66)]
# Widened half again and stretched down to the floor so a person can step through (as Seam.Outline does).
OUTLINE = [(x * 1.6, 0.06 + (z - 0.45) * (2.19 / 1.8)) for x, z in OUTLINE]


def seam_crack():
    """The torn wall round a seam: shards of painted wallboard peeled back from the tear, the gypsum showing in their broken
    edges, hairline cracks running off its ends, and crumbs of it on the floor. (The glow is the game's.)"""
    PT = S("Paint_SeamWall", hx('#D9CBA6'), "paint", 0.6)
    GY = S("Board_SeamGypsum", (0.86, 0.84, 0.78), "board", 0.9)
    PA = S("Paper_SeamFacing", hx('#9C7F57'), "paper", 0.8)
    HL = S("Matte_SeamHairline", (0.12, 0.1, 0.08), "matte", 0.9)
    rng = random.Random(23)
    p = []
    n = len(OUTLINE)
    cx = sum(x for x, _ in OUTLINE) / n
    cz = sum(z for _, z in OUTLINE) / n
    for i in range(n):
        (x0, z0), (x1, z1) = OUTLINE[i], OUTLINE[(i + 1) % n]
        mx, mz = (x0 + x1) / 2, (z0 + z1) / 2
        dx, dz = x1 - x0, z1 - z0
        L = math.hypot(dx, dz)
        # Outward (away from the tear's middle) in the wall's plane.
        nx, nz = -dz / L, dx / L
        if nx * (mx - cx) + nz * (mz - cz) < 0:
            nx, nz = -nx, -nz
        ang = math.degrees(math.atan2(dz, dx))
        for k in range(2):
            w = L * (rng.uniform(1.0, 1.15) if k == 0 else rng.uniform(0.5, 0.8))
            off = 0.03 + k * 0.045 + rng.uniform(0.0, 0.01)
            lift = rng.uniform(6, 16) * (1.0 if k == 0 else 0.5)       # peeled a little out of the wall
            sh = rbox("Shard", (w, 0.01, rng.uniform(0.05, 0.075) if k == 0 else rng.uniform(0.025, 0.04)), (0, 0, 0), PT, 0.003, 1)
            place(sh, rot('Y', -ang) @ rot('X', -lift), (mx + nx * off, -0.006 - 0.012 * (1 - k), mz + nz * off))
            p.append(sh)
            if k == 0:                                                     # the broken edge: gypsum and its paper
                core = rbox("Core", (w * 0.95, 0.01, 0.012), (0, 0, 0), GY, 0.002, 1)
                place(core, rot('Y', -ang) @ rot('X', -lift), (mx + nx * (off - 0.014), -0.018, mz + nz * (off - 0.014)))
                p.append(core)
                pap = rbox("Facing", (w * 0.9, 0.004, 0.02), (0, 0, 0), PA, 0.001, 1)
                place(pap, rot('Y', -ang) @ rot('X', -lift - 20), (mx + nx * (off + 0.01), -0.024, mz + nz * (off + 0.01)))
                p.append(pap)
    # Hairline cracks off the tear's ends and sides.
    for (x, z), (ux, uz) in ((OUTLINE[10], (0.15, 1.0)), (OUTLINE[0], (-1.0, -0.1)), (OUTLINE[5], (1.0, 0.25)), (OUTLINE[16], (-1.0, -0.2))):
        px, pz = x, z
        for s in range(5):
            ln = rng.uniform(0.05, 0.11)
            a = math.atan2(uz, ux) + rng.uniform(-0.6, 0.6)
            qx, qz = px + math.cos(a) * ln, pz + math.sin(a) * ln
            seg = rbox("Hair", (ln, 0.002, 0.006 - s * 0.0008), (0, 0, 0), HL, 0.0, 1)
            place(seg, rot('Y', -math.degrees(a)), ((px + qx) / 2, -0.001, (pz + qz) / 2))
            p.append(seg)
            px, pz = qx, qz
    # Crumbs on the floor under it.
    for k in range(9):
        c = rbox("Crumb", (rng.uniform(0.02, 0.06), rng.uniform(0.015, 0.04), rng.uniform(0.008, 0.02)), (0, 0, 0), PT if k % 3 else GY, 0.003, 1)
        place(c, rot('Z', rng.uniform(0, 180)), (rng.uniform(-0.18, 0.2), -rng.uniform(0.03, 0.25), 0.007))
        p.append(c)
    return finish(p, "SeamCrack", sharp_deg=40)


def seam_chute():
    """The intake chute beside a seam: a steel hood in the company's amber over a dark mouth hung with rubber flaps, a tray
    sloping out to the floor that boxes slide down, INTAKE stencilled over it."""
    FR = S("Paint_SeamChuteFrame", AMBER, "paint", 0.45, 0.3)
    ST = S("Steel_SeamChuteTray", (0.55, 0.55, 0.53), "steel", 0.4, 1.0)
    DK = S("Matte_SeamChuteMouth", (0.03, 0.03, 0.03), "matte", 0.9)
    RB = S("Rubber_SeamChuteFlap", (0.08, 0.08, 0.08), "rubber", 0.7)
    W_ = 0.72
    p = [rbox("FrameL", (0.06, 0.08, 0.62), (-W_ / 2 + 0.03, -0.04, 0.64), FR, 0.01, 2),
         rbox("FrameR", (0.06, 0.08, 0.62), (W_ / 2 - 0.03, -0.04, 0.64), FR, 0.01, 2),
         rbox("Hood", (W_, 0.24, 0.12), (0, -0.12, 0.98), FR, 0.02, 3),
         rbox("HoodLip", (W_ + 0.02, 0.03, 0.05), (0, -0.235, 0.92), FR, 0.01, 2),
         rbox("Mouth", (W_ - 0.12, 0.02, 0.58), (0, -0.004, 0.63), DK, 0.0, 1)]
    for k in range(6):                                           # rubber flaps across the mouth
        x = -W_ / 2 + 0.08 + k * (W_ - 0.16) / 5.0
        p.append(rbox("Flap", ((W_ - 0.16) / 6.0 - 0.008, 0.006, 0.34), (x + (W_ - 0.16) / 12.0 - 0.004, -0.05, 0.75), RB, 0.0, 1))
    tray = rbox("Tray", (W_ - 0.1, 0.02, 0.5), (0, 0, 0), ST, 0.004, 1)
    place(tray, rot('X', 55), (0, -0.2, 0.47))                   # from the mouth's sill down and out
    p.append(tray)
    for sx in (-1, 1):
        cheek = rbox("Cheek", (0.02, 0.07, 0.5), (0, 0.03, 0), ST, 0.004, 1)       # a rail along each side of the tray
        place(cheek, rot('X', 55), (sx * (W_ / 2 - 0.06), -0.2, 0.47))
        p.append(cheek)
    p.append(rbox("Sill", (W_, 0.08, 0.05), (0, -0.04, 0.335), FR, 0.01, 2))
    body = finish(p, "SeamChute", sharp_deg=50)
    c = Canvas(200, 40, AMBER)
    c.text_c("INTAKE", 100, 6, 3, CHOC)
    c.text_c("OVERFLOW LOGISTICS", 100, 30, 1, CHOC)
    plate = card("Stencil", 0.5, 0.1, (0, -0.242, 0.985), print_mat("Print_SeamChute", c, sharp=True))
    return join([body, plate], "SeamChute")


def storage_sign():
    BD = S("Wood_LeaseStorageBoard", CHOC, "wood", 0.5)
    W_, H_, D_ = 0.7, 0.22, 0.03
    body = finish([rbox("Board", (W_, D_, H_), (0, -D_ / 2, H_ / 2), BD, 0.01, 2)], "LeaseStorageSign", sharp_deg=50)
    c = Canvas(280, 88, CHOC)
    c.rect(0, 76, 280, 80, ORANGE); c.rect(0, 80, 280, 84, GOLD); c.rect(0, 84, 280, 88, AVOCADO)
    c.text_c("STORAGE", 140, 10, 5, AMBER)
    c.text_c("TENANT GOODS ONLY", 140, 58, 1, CREAM)
    art = card("Art", W_ - 0.02, H_ - 0.02, (0, -D_ - 0.002, H_ / 2), print_mat("Print_LeaseStorageSign", c, sharp=True))
    return join([body, art], "LeaseStorageSign")


# ------------------------------------------------------------------------------------------------- the seams' places

def print_press():
    """A sheet-fed offset press as a small print shop has one (2.6 x 1.4, 1.6 m tall): its feeder stack at one end, the
    printing units' green housings with their guards and rollers, the delivery pile at the other end, a control desk."""
    GR = S("Paint_PressBody", hx('#3F6B5A'), "paint", 0.45, 0.2)
    GY = S("Paint_PressGuard", (0.72, 0.72, 0.7), "paint", 0.4, 0.2)
    ST = S("Steel_PressRoller", (0.6, 0.6, 0.6), "steel", 0.3, 1.0)
    BK = S("Rubber_PressRoller", (0.08, 0.08, 0.08), "rubber", 0.6)
    PP = S("Paper_PressSheets", (0.95, 0.94, 0.9), "paper", 0.8)
    PN = S("Plastic_PressPanel", (0.2, 0.2, 0.2), "plastic", 0.5)
    L_, W_ = 2.6, 1.0
    p = [rbox("Base", (L_, W_, 0.35), (0, 0, 0.175), GR, 0.03, 2)]
    for k in range(2):                                       # two printing units
        x = -0.35 + k * 0.7
        p.append(rbox("Unit", (0.6, W_ + 0.06, 0.95), (x, 0, 0.35 + 0.475), GR, 0.05, 3))
        p.append(rbox("Guard", (0.5, 0.04, 0.6), (x, -W_ / 2 - 0.04, 0.85), GY, 0.02, 2))
        for z in (1.0, 1.18):
            p.append(tube("Roller", [(x - 0.2, -W_ / 2 - 0.02, z), (x - 0.2, W_ / 2 + 0.02, z)], 0.06, BK if z < 1.1 else ST, sides=16))
        p.append(rbox("Ink", (0.5, W_ - 0.1, 0.12), (x, 0, 1.36), GY, 0.02, 2))
    p.append(rbox("Feeder", (0.55, W_, 0.7), (-L_ / 2 + 0.28, 0, 0.35 + 0.35), GR, 0.04, 2))
    p.append(rbox("FeedPile", (0.48, 0.66, 0.25), (-L_ / 2 + 0.28, 0, 1.18), PP, 0.004, 1))
    p.append(rbox("Delivery", (0.6, W_, 0.4), (L_ / 2 - 0.3, 0, 0.35 + 0.2), GR, 0.04, 2))
    p.append(rbox("DeliveryPile", (0.48, 0.66, 0.18), (L_ / 2 - 0.3, 0, 0.84), PP, 0.004, 1))
    p.append(rbox("Desk", (0.5, 0.3, 0.12), (L_ / 2 - 0.3, -W_ / 2 - 0.2, 1.05), PN, 0.02, 2))
    p.append(rbox("DeskLeg", (0.06, 0.06, 1.0), (L_ / 2 - 0.3, -W_ / 2 - 0.2, 0.5), GY, 0.01, 1))
    body = finish(p, "PrintPress", sharp_deg=45)
    c = Canvas(160, 40, (0.2, 0.2, 0.2))
    c.text_c("OFFSET 52", 80, 4, 2, (0.9, 0.9, 0.85))
    c.text_c("RUN  STOP  WASH", 80, 26, 1, (0.85, 0.6, 0.2))
    panel = card("PanelArt", 0.46, 0.115, (L_ / 2 - 0.3, -W_ / 2 - 0.351, 1.05), print_mat("Print_PressPanel", c), normal=(0, -1, 0))
    return join([body, panel], "PrintPress")


def paper_stack():
    """Reams of paper on a pallet (1.2 x 1.0, 1 m tall): cartons of ten reams, wrapped, the top tier short."""
    PW = S("Wood_PaperPallet", hx('#A88A5E'), "wood", 0.7)
    CT = S("Card_PaperCarton", hx('#E8E2D2'), "card", 0.7)
    BD = S("Tape_PaperBand", hx('#3E6CA8'), "tape", 0.5)
    p = []
    for x in (-0.5, 0.0, 0.5):
        p.append(rbox("Stringer", (0.1, 1.0, 0.1), (x, 0, 0.05), PW, 0.005, 1))
    for y in (-0.44, 0.0, 0.44):
        p.append(rbox("Deck", (1.2, 0.12, 0.022), (0, y, 0.111), PW, 0.003, 1))
    rng = random.Random(9)
    cw, cd, ch = 0.4, 0.3, 0.27
    for tier in range(3):
        n = 3 if tier < 2 else 2
        for i in range(n):
            for j in range(3):
                x = -0.4 + i * cw + rng.uniform(-0.01, 0.01)
                y = -0.32 + j * cd + 0.02
                z = 0.122 + tier * ch + ch / 2
                p.append(rbox("Carton", (cw - 0.01, cd - 0.01, ch - 0.004), (x, y, z), CT, 0.008, 1))
                p.append(rbox("Band", (cw - 0.006, 0.03, ch), (x, y, z), BD, 0.002, 1))
    return finish(p, "PaperStack", sharp_deg=45)


def pickup_sign():
    BD = S("Wood_SeamPickupBoard", CHOC, "wood", 0.5)
    W_, H_, D_ = 0.7, 0.3, 0.03
    body = finish([rbox("Board", (W_, D_, H_), (0, -D_ / 2, H_ / 2), BD, 0.01, 2)], "SeamPickupSign", sharp_deg=50)
    c = Canvas(280, 120, CHOC)
    c.rect(0, 108, 280, 112, ORANGE); c.rect(0, 112, 280, 116, GOLD); c.rect(0, 116, 280, 120, AVOCADO)
    c.text_c("OVERFLOW", 140, 10, 4, AMBER)
    c.text_c("PICKUP", 140, 50, 4, CREAM)
    c.text_c("TENANT GOODS - DO NOT SELL", 140, 92, 1, AMBER)
    art = card("Art", W_ - 0.02, H_ - 0.02, (0, -D_ - 0.002, H_ / 2), print_mat("Print_SeamPickupSign", c, sharp=True))
    return join([body, art], "SeamPickupSign")


def bay():
    """A DELIVERIES bay taped out on the floor (2.6 x 2.2 m): yellow and black tape round it, stencilled inside."""
    tc = Canvas(256, 16, (0.95, 0.78, 0.1))
    for k in range(0, 256, 32):
        for dx in range(16):
            for yy in range(16):
                xx = k + dx + yy
                if 0 <= xx < 256:
                    tc.rect(xx, yy, xx + 1, yy + 1, (0.08, 0.08, 0.07))
    tape = print_mat("Print_SeamBayTape", tc)
    W_, D_, t = 2.6, 2.2, 0.07
    parts = [card("TapeS", W_, t, (0, D_ / 2 - t / 2, 0.003), tape, normal=(0, 0, 1)),
             card("TapeN", W_, t, (0, -D_ / 2 + t / 2, 0.003), tape, normal=(0, 0, 1)),
             card("TapeW", D_, t, (-W_ / 2 + t / 2, 0, 0.0031), tape, normal=(0, 0, 1), roll=90),
             card("TapeE", D_, t, (W_ / 2 - t / 2, 0, 0.0031), tape, normal=(0, 0, 1), roll=90)]
    sc = Canvas(256, 96, (0.42, 0.42, 0.4))
    sc.text_c("DELIVERIES", 128, 14, 4, (0.95, 0.8, 0.15))
    sc.text_c("OVERFLOW", 128, 62, 3, (0.95, 0.8, 0.15))
    stencil = card("Stencil", 1.8, 0.68, (0, 0, 0.0025), print_mat("Print_SeamBayStencil", sc), normal=(0, 0, 1))
    return join(parts + [stencil], "SeamBay")


SHOPS = [("TALLMORE", "WHOLESALE", hx('#1F3A68'), (0.95, 0.95, 0.95)),
         ("QUILLBY", "PRINT & COPY", hx('#B22A26'), (0.98, 0.96, 0.92)),
         ("PAPERLARK", "OFFICE SUPPLY", hx('#2F6B3A'), (0.97, 0.97, 0.94)),
         ("BELLWEATHER", "HOME FURNISHINGS", hx('#6A1F2E'), (0.96, 0.9, 0.78))]


def shop_sign(k):
    """The business's sign (invented names; nothing in the real world): a painted board, 1.6 x 0.4 m."""
    name, sub_, ground, ink = SHOPS[k]
    BD = S("Paint_ShopBoard%d" % k, ground, "paint", 0.4)
    W_, H_, D_ = 1.6, 0.4, 0.04
    body = finish([rbox("Board", (W_, D_, H_), (0, -D_ / 2, H_ / 2), BD, 0.015, 2)], "SeamShop%d" % k, sharp_deg=50)
    c = Canvas(400, 100, ground)
    c.text_c(name, 200, 12, 6, ink)
    c.text_c(sub_, 200, 70, 2, ink)
    art = card("Art", W_ - 0.03, H_ - 0.03, (0, -D_ - 0.002, H_ / 2), print_mat("Print_SeamShop%d" % k, c, sharp=True))
    return join([body, art], "SeamShop%d" % k)


def hoarding(words):
    """Plywood hoarding (1.2 x 2.7 m, one cell of a doorway or an open run): a sheet of ply on two studs, a hazard
    stripe painted along its foot, NOT LEASED stencilled on it (or plain, every other one). Where a wing not leased
    opens off the core (Interaction.LeaseGate); scaled up in taller rooms."""
    PLY = S("Wood_HoardingPly", hx('#C9A36E'), "wood", 0.6)
    ST = S("Wood_HoardingStud", hx('#A8865A'), "wood", 0.6)
    W_, H_ = 1.2, 2.7
    c = Canvas(240, 540, hx('#C9A36E'))
    rng = random.Random(5 if words else 6)
    for k in range(40):                                     # the ply's grain: long pale and dark streaks
        x = rng.randrange(240)
        w = rng.randrange(2, 7)
        col = hx('#D4B07C') if k % 2 else hx('#B8925E')
        c.rect(x, 0, x + w, 540, col)
    for k in range(0, 240, 30):                              # the hazard stripe at the foot
        for dy in range(40):
            for dx in range(15):
                xx = (k + dx + dy) % 240
                c.rect(xx, 500 + dy, xx + 1, 501 + dy, (0.1, 0.1, 0.09))
    c.rect(0, 496, 240, 500, (0.95, 0.78, 0.1))
    if words:
        c.text_c("NOT", 120, 190, 7, CHOC)
        c.text_c("LEASED", 120, 250, 6, CHOC)
        c.rect(30, 310, 210, 316, ORANGE); c.rect(30, 316, 210, 322, GOLD); c.rect(30, 322, 210, 328, AVOCADO)
        c.text_c("OVERFLOW", 120, 340, 2, CHOC)
    for (x, y) in ((14, 14), (226, 14), (14, 526), (226, 526), (14, 270), (226, 270)):
        c.disc(x, y, 3, (0.35, 0.35, 0.33))                  # screws
    name = "LeaseHoarding" if words else "LeaseHoardingPlain"
    body = finish([rbox("Ply", (W_, 0.018, H_), (0, -0.009, H_ / 2), PLY, 0.003, 1),
                   rbox("StudL", (0.045, 0.09, H_), (-W_ / 2 + 0.03, 0.03, H_ / 2), ST, 0.004, 1),
                   rbox("StudR", (0.045, 0.09, H_), (W_ / 2 - 0.03, 0.03, H_ / 2), ST, 0.004, 1)], name, sharp_deg=50)
    art = card("Face", W_, H_, (0, -0.0185, H_ / 2), print_mat("Print_" + name, c, sharp=True))
    return join([body, art], name)


V = (0.75, -1.0, 0.45)
run_baked([
    ("LeaseHoarding", lambda: hoarding(True), (0.3, -1.0, 0.2), {"scale": 1.0, "px_per_m": 200.0}),
    ("LeaseHoardingPlain", lambda: hoarding(False), (0.3, -1.0, 0.2), {"scale": 1.0, "px_per_m": 200.0}),
    ("PrintPress", print_press, V, {"scale": 1.0}),
    ("PaperStack", paper_stack, V, {"scale": 0.7}),
    ("SeamPickupSign", pickup_sign, (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 700.0}),
    ("SeamBay", bay, (0.2, -0.6, 1.0), {"scale": 1.0}),
    ("SeamShop0", lambda: shop_sign(0), (0.3, -1.0, 0.2), {"scale": 0.6, "px_per_m": 400.0}),
    ("SeamShop1", lambda: shop_sign(1), (0.3, -1.0, 0.2), {"scale": 0.6, "px_per_m": 400.0}),
    ("SeamShop2", lambda: shop_sign(2), (0.3, -1.0, 0.2), {"scale": 0.6, "px_per_m": 400.0}),
    ("SeamShop3", lambda: shop_sign(3), (0.3, -1.0, 0.2), {"scale": 0.6, "px_per_m": 400.0}),
    ("SeamCrack", seam_crack, (0.4, -1.0, 0.2), {"scale": 0.8}),
    ("SeamChute", seam_chute, V, {"scale": 0.6}),
    ("LeaseStorageSign", storage_sign, (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 700.0}),
    ("LeaseFireDoor", fire_door, V, {"scale": 1.0}),
    ("LeaseBricked", bricked, V, {"scale": 0.8}),
    ("LeaseSheeting", sheeting, V, {"scale": 1.0}),
    ("LeaseSign", lease_sign, (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 700.0}),
])
print("[lease] done ->", OUT)
