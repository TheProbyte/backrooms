"""
The Poolrooms' fittings (2026-11-04, the realism pass), built in Blender and baked (bake_kit.py). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_pool.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>] [--only LifeguardChair]

  - LifeguardChair: a 6 ft stand (seat about 1.7 m up, as the game's): a T304 stainless tube frame splayed to four deck
    anchors, a moulded fibreglass seat and back, a footrest, three ABS steps between the back legs, armrests, and a red
    rescue tube hung from one armrest. (Paragon / SR Smith stands: stainless frames, fibreglass seats, ABS steps.)
  - PoolShower: an exposed shower column: the supply out of the wall, a push-button metering valve at 1.15 m, a wall
    bracket, the arm and a vandal-resistant head tilted down.
  - LadderTread: a pool ladder's stainless tread with its grooves and bolts.
  - StallBracket, CoatHook: a changing stall's aluminium wall bracket and a chrome hook. BleacherCap, BleacherBracket:
    an aluminium bleacher plank's black end cap and its seat bracket.

Frames: LifeguardChair and PoolShower origin on the deck (the shower's at the wall's foot, the wall at y = 0, the room
toward -Y); the small parts at their middle. Unity (x, y, z) = (-x, z, -y).
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


def bent(pts, r, material, n=5, sides=14):
    return tube("Tube", catmull(pts, n), r, material, sides)


def lifeguard_chair():
    SS = S("Stainless_GuardFrame", (0.7, 0.71, 0.72), "metal", 0.25, 1.0)
    FG = S("Fibreglass_GuardSeat", (0.92, 0.92, 0.89), "plastic", 0.3)
    STP = S("Plastic_GuardStep", (0.62, 0.64, 0.66), "plastic", 0.5)
    FT = S("Rubber_GuardTube", (0.72, 0.08, 0.06), "rubber", 0.6)
    STR = S("Fabric_GuardStrap", (0.08, 0.08, 0.08), "fabric", 0.9)
    r = 0.024
    seat = 1.62
    p = []
    for sx in (-1, 1):
        # A side frame: the front leg up to the seat, along it, and down the back leg, one bent tube.
        f0, b0 = Vector((sx * 0.42, -0.45, 0.0)), Vector((sx * 0.42, 0.45, 0.0))
        p.append(bent([f0, Vector((sx * 0.3, -0.24, seat - 0.4)), Vector((sx * 0.27, -0.2, seat - 0.05)), Vector((sx * 0.265, -0.14, seat)),
                       Vector((sx * 0.265, 0.12, seat)), Vector((sx * 0.27, 0.19, seat - 0.05)), Vector((sx * 0.31, 0.27, seat - 0.4)), b0], r, SS, 6))
        # The back upright and the armrest, one tube.
        p.append(bent([Vector((sx * 0.265, 0.16, seat)), Vector((sx * 0.265, 0.2, seat + 0.3)), Vector((sx * 0.265, 0.21, seat + 0.5)),
                       Vector((sx * 0.265, 0.12, seat + 0.56)), Vector((sx * 0.265, 0.0, seat + 0.3)), Vector((sx * 0.265, -0.13, seat + 0.27)),
                       Vector((sx * 0.265, -0.17, seat + 0.18)), Vector((sx * 0.265, -0.15, seat + 0.02))], 0.019, SS, 5))
        # Its deck anchor: a flange plate and its bolts.
        for c in (f0, b0):
            p.append(lathe("Anchor", [(0.0, 0.0), (0.05, 0.0), (0.05, 0.006), (0.03, 0.012), (0.03, 0.05), (0.0, 0.05)], SS, 20, xf=M4(None, c)))
            for k in range(3):
                a = 2 * math.pi * k / 3
                p.append(lathe("Bolt", [(0.0, 0.0), (0.006, 0.0), (0.006, 0.004), (0.0, 0.005)], SS, 6,
                               xf=M4(None, c + Vector((math.cos(a) * 0.038, math.sin(a) * 0.038, 0.006)))))
    # Cross ties between the front legs and the back legs, a top rail on the back.
    for y, z in ((-0.37, 0.25), (-0.26, 1.0), (0.37, 0.25)):
        x = 0.42 - (0.42 - 0.3) * min(1.0, z / (seat - 0.4))
        p.append(lathe("Tie", [(0.0, 0.0), (0.017, 0.0), (0.017, 2 * x), (0.0, 2 * x)], SS, 12, xf=M4(rot('Y', 90), (-x, y, z))))
    p.append(lathe("TopRail", [(0.0, 0.0), (0.019, 0.0), (0.019, 0.53), (0.0, 0.53)], SS, 12, xf=M4(rot('Y', 90), (-0.265, 0.205, seat + 0.5))))
    # The steps up the back (between the back legs), the footrest at the front.
    for k, z in enumerate((0.42, 0.84, 1.26)):
        t = z / (seat - 0.4) if z < seat - 0.4 else 1.0
        y = 0.45 - (0.45 - 0.27) * min(1.0, t)
        x = 0.42 - (0.42 - 0.31) * min(1.0, t)
        p.append(rbox("Step", (2 * x - 0.04, 0.13, 0.03), (0, y - 0.02, z), STP, 0.006, 2))
    p.append(rbox("Footrest", (0.5, 0.2, 0.025), (0, -0.3, 1.0), FG, 0.006, 2))
    # The seat and its back: moulded fibreglass, dished, rolled edges.
    seat_rings = [rrect(0.6, 0.44, 0.06, seat + 0.012, 5, 0, 0.0), rrect(0.6, 0.44, 0.06, seat + 0.04, 5, 0, 0.0),
                  rrect(0.56, 0.4, 0.05, seat + 0.05, 5, 0, 0.0), rrect(0.5, 0.34, 0.06, seat + 0.03, 5, 0, 0.0),
                  rrect(0.02, 0.02, 0.009, seat + 0.025, 5, 0, 0.0)]
    sb = loft("Seat", seat_rings, FG, cap0=True, cap1=True, recalc=True)
    p.append(sb)
    back = rbox("Back", (0.5, 0.035, 0.3), (0, 0, 0), FG, 0.012, 3)
    place(back, rot('X', -8), (0, 0.215, seat + 0.33))
    p.append(back)
    # The rescue tube on its strap from the left armrest.
    p.append(tube("Strap", [(-0.265, -0.05, seat + 0.29), (-0.29, -0.05, seat + 0.22), (-0.3, -0.05, seat + 0.1)], 0.006, STR, 6))
    rt = rbox("RescueTube", (0.08, 0.15, 0.95), (0, 0, 0), FT, 0.035, 4)
    place(rt, rot('Y', 4), (-0.31, -0.05, seat + 0.1 - 0.475))
    p.append(rt)
    base = finish(p, "LifeguardChair", sharp_deg=50)
    c = Canvas(64, 256, (0.72, 0.08, 0.06))
    for i, ch in enumerate("GUARD"):
        c.text(ch, 17, 30 + i * 40, 5, (0.97, 0.96, 0.92))
    word = card("Word", 0.075, 0.6, (-0.3105 - 0.0412, -0.05, seat + 0.1 - 0.475), print_mat("Print_GuardTube", c), normal=(-1, 0, 0), roll=-4.0)
    return join([base, word], "LifeguardChair")


def pool_shower():
    CH = S("Chrome_PoolShower", (0.74, 0.75, 0.76), "metal", 0.15, 1.0)
    BT = S("Chrome_ShowerButton", (0.78, 0.78, 0.78), "metal", 0.2, 1.0)
    y = -0.045
    p = []
    # The supply out of the wall at 1.1 m, up the wall on a bracket, over in an arm.
    p.append(lathe("Flange", [(0.0, 0.0), (0.038, 0.0), (0.038, 0.004), (0.03, 0.012), (0.0, 0.014)], CH, 24, xf=M4(rot('X', 90), (0, 0, 1.1))))
    p.append(tube("Elbow", catmull([(0, -0.012, 1.1), (0, -0.03, 1.1), (0, y, 1.115), (0, y, 1.14)], 4), 0.016, CH, 14))
    p.append(lathe("Riser", [(0.0, 0.0), (0.016, 0.0), (0.016, 0.88), (0.0, 0.88)], CH, 14, xf=M4(None, (0, y, 1.14))))
    p.append(tube("Arm", catmull([(0, y, 2.0), (0, y, 2.04), (0, y - 0.03, 2.07), (0, y - 0.12, 2.07), (0, -0.27, 2.04)], 5), 0.016, CH, 14))
    p.append(rbox("Bracket", (0.05, 0.04, 0.03), (0, -0.022, 1.75), CH, 0.006, 2))
    p.append(lathe("BracketBand", [(0.0, 0.0), (0.019, 0.0), (0.019, 0.025), (0.0, 0.025)], CH, 14, xf=M4(None, (0, y, 1.737))))
    # The head: a ball joint and a vandal-resistant head tilted down toward the room.
    p.append(sphere("Ball", (0, -0.28, 2.035), (0.02, 0.02, 0.02), CH, 14, 8))
    head = lathe("Head", [(0.0, 0.0), (0.042, 0.0), (0.046, 0.006), (0.044, 0.018), (0.022, 0.04), (0.0, 0.042)], CH, 28)
    place(head, rot('X', -150), (0, -0.29, 2.03))
    p.append(head)
    # The push-button metering valve at 1.15 m: its body on the riser, a round button toward the room.
    p.append(rbox("Valve", (0.07, 0.06, 0.1), (0, y - 0.01, 1.2), CH, 0.012, 3))
    p.append(lathe("Button", [(0.0, 0.0), (0.024, 0.0), (0.024, 0.008), (0.02, 0.014), (0.0, 0.016)], BT, 24, xf=M4(rot('X', 90), (0, y - 0.04, 1.2))))
    return finish(p, "PoolShower", sharp_deg=45)


def ladder_tread():
    SS = S("Stainless_LadderTread", (0.76, 0.77, 0.78), "metal", 0.35, 1.0)
    p = [rbox("Tread", (0.46, 0.1, 0.03), (0, 0, 0), SS, 0.006, 2)]
    for x in (-0.215, 0.215):
        for y in (-0.025, 0.025):
            p.append(lathe("Bolt", [(0.0, 0.0), (0.007, 0.0), (0.007, 0.002), (0.0, 0.003)], SS, 8, xf=M4(None, (x, y, 0.015))))
    base = finish(p, "LadderTreadBody", sharp_deg=45)
    c = Canvas(512, 112, (0.76, 0.77, 0.78))
    for i in range(9):
        c.rect(30, 10 + i * 11, 482, 15 + i * 11, (0.22, 0.23, 0.24))
    grooves = card("Grooves", 0.4, 0.088, (0, 0, 0.0152), print_mat("Stainless_TreadGrooves", c), normal=(0, 0, 1))
    return join([base, grooves], "LadderTread")


def stall_bracket():
    AL = S("Alu_StallBracket", (0.7, 0.71, 0.71), "metal", 0.4, 1.0)
    p = [rbox("Web", (0.003, 0.05, 0.06), (0.014, -0.025, 0), AL, 0.001, 1), rbox("Web2", (0.003, 0.05, 0.06), (-0.014, -0.025, 0), AL, 0.001, 1),
         rbox("Flange", (0.06, 0.003, 0.06), (0, -0.0015, 0), AL, 0.001, 1)]
    for z in (-0.018, 0.018):
        p.append(lathe("Screw", [(0.0, 0.0), (0.004, 0.0), (0.004, 0.0015), (0.0, 0.002)], AL, 8, xf=M4(rot('X', 90), (0.022, -0.003, z))))
        p.append(lathe("Screw", [(0.0, 0.0), (0.004, 0.0), (0.004, 0.0015), (0.0, 0.002)], AL, 8, xf=M4(rot('X', 90), (-0.022, -0.003, z))))
    return finish(p, "StallBracket", sharp_deg=45)


def coat_hook():
    CH = S("Chrome_CoatHook", (0.76, 0.76, 0.76), "metal", 0.15, 1.0)
    p = [lathe("Rose", [(0.0, 0.0), (0.018, 0.0), (0.018, 0.004), (0.012, 0.008), (0.0, 0.009)], CH, 20, xf=M4(rot('X', 90), (0, 0, 0)))]
    p.append(tube("Hook", catmull([(0, -0.008, 0.0), (0, -0.03, 0.002), (0, -0.05, 0.02), (0, -0.055, 0.035), (0, -0.05, 0.045)], 5), 0.005, CH, 10))
    p.append(sphere("Tip", (0, -0.05, 0.047), (0.007, 0.007, 0.007), CH, 10, 6))
    return finish(p, "CoatHook", sharp_deg=50)


def bleacher_cap():
    BK = S("Plastic_BleacherCap", (0.05, 0.05, 0.05), "plastic", 0.5)
    p = [rbox("Cap", (0.012, 0.26, 0.055), (0, 0, 0), BK, 0.005, 2)]
    return finish(p, "BleacherCap", sharp_deg=45)


def bleacher_bracket():
    GAL = S("Galv_BleacherBracket", (0.6, 0.61, 0.6), "galv", 0.45, 1.0)
    p = [rbox("Web", (0.006, 0.24, 0.05), (0, 0, -0.025), GAL, 0.002, 1), rbox("Foot", (0.08, 0.06, 0.006), (0, 0.06, -0.047), GAL, 0.002, 1)]
    for y in (-0.08, 0.08):
        p.append(lathe("Bolt", [(0.0, 0.0), (0.007, 0.0), (0.007, 0.005), (0.0, 0.006)], GAL, 6, xf=M4(rot('Y', 90), (0.003, y, -0.02))))
    return finish(p, "BleacherBracket", sharp_deg=45)


run_baked([
    ("LifeguardChair", lifeguard_chair, (0.7, -1.0, 0.35), {"scale": 1.0, "px_per_m": 500.0}),
    ("PoolShower", pool_shower, (0.6, -1.0, 0.3), {"scale": 0.4, "px_per_m": 600.0}),
    ("LadderTread", ladder_tread, (0.4, -1.0, 0.6), {"res": 256, "scale": 0.3}),
    ("StallBracket", stall_bracket, (0.5, -1.0, 0.4), {"res": 256, "scale": 0.2}),
    ("CoatHook", coat_hook, (0.6, -1.0, 0.3), {"res": 256, "scale": 0.2}),
    ("BleacherCap", bleacher_cap, (0.6, -1.0, 0.3), {"res": 256, "scale": 0.2}),
    ("BleacherBracket", bleacher_bracket, (0.6, -1.0, 0.3), {"res": 256, "scale": 0.2}),
])
print("[pool] done ->", OUT)
