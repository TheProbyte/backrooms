"""
The last of Level 0's furniture in the hub's style (2026-11-08, the owner: "remake all in the hub style"): the shelving,
the cubicle partitions and the two plastic chairs, remade in the hub's design language (build_hub.py: HubLockers,
HubChairStack, HubBench): seventies, chunky boxes and fat tubes with big soft rounded edges, few parts, the company's
colours where the piece is the company's (the shelving), read from across a room. Run headless, always with the
Overflow style:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_leftovers_hub.py -- Assets/Backrooms/Resources/Overflow/Fittings --style overflow [--preview <dir>] [--only Shelving_100]

Same names, frames and sizes as the models they replace, so the game's colliders and placement still fit:
  - Shelving_100 / Shelving_110 (1.0 x 0.42 x 1.85, 1.1 x 0.46 x 2.0): the decks at the same heights (OfficeChunkBuilder
    .Special: levels round(h / 0.45) clamped 4..5, deck tops at 0.1 + l * pitch), so the stock and the contract floor's
    boxes still sit on them. Fat avocado posts with chunky slots, amber beams, warm board decks. Baked.
  - LawnChair_White / LawnChair_Green (0.62 x 0.6 x 0.86, the seat 0.43 up): a fat monobloc: a thick rounded seat, four
    fat splayed legs, the arms one fat tube from the back round to the front legs, five fat round slats up to a fat top
    rail. Baked.
  - SchoolChair_Blue / SchoolChair_Orange (0.46 x 0.52 x 0.88, the seat 0.457 up): the hub's stacking chair, alone: a
    thick moulded seat and back on a fat dark tube frame, glides. Baked.
  - Partition_120_135 / _120_165 / _90_135 / _90_165: the cubicle panel, chunky: a fat rounded frame and cap, the fabric
    in fat vertical channels both sides, a deep base, big feet. Material slots as before (PanelFrame, PanelFabric,
    PanelTrim, PanelDark: the game colours them, the fabric the room's). Not baked.

Frames: Blender front toward -Y, Z up; origin on the floor at the middle of the foot. Unity (x, y, z) = (x, z, -y).
"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_rooms.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- break room")
exec(compile(_src[:_cut], "build_rooms_helpers", "exec"))

import bpy
from mathutils import Vector


def hx(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


AMBER, AVOCADO, CHOC = hx('#F0A21E'), hx('#7C8A2E'), hx('#3B2A1E')
FRAME_DARK = hx('#2B2420')


# ------------------------------------------------------------------------------------------------- shelving

def shelving(W_, H_, D_, name, levels=None, a=0.05):
    PST = S("Paint_ShelfPost", AVOCADO, "paint", 0.5)
    SLOT = S("Paint_ShelfSlot", CHOC, "paint", 0.6)
    BM = S("Paint_ShelfBeam", AMBER, "paint", 0.45)
    DECK = S("Board_ShelfDeck", (0.62, 0.5, 0.35), "board", 0.85)
    FT = S("Plastic_ShelfFoot", (0.06, 0.06, 0.06), "plastic", 0.5)
    # (Deck heights as Interaction.ShelfTypes: 0.1 m up, then evenly to the top.)
    if levels is None:
        levels = max(4, min(5, round(H_ / 0.45)))
    lp = (H_ - 0.12) / (levels - 1)
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * (W_ / 2 - a / 2), sy * (D_ / 2 - a / 2)
            p.append(rbox("Post", (a, a, H_ - 0.02), (cx, cy, 0.02 + (H_ - 0.02) / 2), PST, 0.014, 3))
            p.append(rbox("Foot", (a + 0.016, a + 0.016, 0.02), (cx, cy, 0.01), FT, 0.006, 2))
            p.append(rbox("Cap", (a + 0.006, a + 0.006, 0.014), (cx, cy, H_ - 0.007), FT, 0.006, 2))
            # Chunky slots up the post's outer faces (where the beams hook on).
            z = 0.2
            while z < H_ - 0.12:
                p.append(rbox("Slot", (0.014, 0.004, 0.045), (cx, sy * (D_ / 2 + 0.0005), z), SLOT, 0.002, 1))
                z += 0.15
    for l in range(levels):
        top = 0.1 + l * lp + 0.011
        p.append(rbox("Deck", (W_ - 0.03, D_ - 0.03, 0.03), (0, 0, top - 0.015), DECK, 0.008, 2))
        for sy in (-1, 1):
            p.append(rbox("Beam", (W_ - 2 * a + 0.006, 0.036, 0.065), (0, sy * (D_ / 2 - 0.02), top - 0.03 - 0.0325), BM, 0.012, 3))
        for sx in (-1, 1):
            p.append(rbox("SideBeam", (0.036, D_ - 2 * a + 0.006, 0.05), (sx * (W_ / 2 - 0.02), 0, top - 0.03 - 0.025), BM, 0.012, 3))
    return finish(p, name, sharp_deg=50)


# ------------------------------------------------------------------------------------------------- the lawn chair

def smooth(pts, n=6):
    """A Catmull-Rom curve through the points (a moulding's bends are round, not kinked)."""
    P = [Vector(p) for p in pts]
    P = [P[0] * 2 - P[1]] + P + [P[-1] * 2 - P[-2]]
    out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for k in range(n):
            t = k / n
            out.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t ** 3))
    out.append(P[-2])
    return out


def lawn_chair(colour, name):
    PP = S("Plastic_LawnChair", colour, "plastic", 0.5)
    SH = 0.43
    p = [rbox("Seat", (0.5, 0.47, 0.065), (0, -0.01, SH - 0.0325), PP, 0.028, 4),
         rbox("Skirt", (0.52, 0.06, 0.07), (0, -0.235, SH - 0.05), PP, 0.028, 4)]
    for sx in (-1, 1):
        # Legs: fat, splayed out to the feet.
        p.append(tube("FrontLeg", [(sx * 0.22, -0.2, SH - 0.02), (sx * 0.25, -0.235, 0.2), (sx * 0.275, -0.265, 0.02)], [0.034, 0.032, 0.03], PP, 14))
        p.append(tube("BackLeg", [(sx * 0.22, 0.19, SH - 0.02), (sx * 0.25, 0.24, 0.2), (sx * 0.275, 0.285, 0.02)], [0.034, 0.032, 0.03], PP, 14))
        for y in (-0.265, 0.285):
            p.append(rbox("Foot", (0.07, 0.07, 0.024), (sx * 0.275, y, 0.012), PP, 0.01, 2))
        # The arm: up from the front leg, back along the side, round into the back.
        p.append(tube("Arm", smooth([(sx * 0.24, -0.215, SH - 0.03), (sx * 0.27, -0.205, 0.59), (sx * 0.285, -0.13, 0.645),
                                     (sx * 0.285, 0.1, 0.655), (sx * 0.26, 0.24, 0.67)]), 0.034, PP, 16))
    # The back: a fat lumbar band, five fat round slats spreading up to a fat top rail that sweeps into the arms.
    p.append(rbox("Lumbar", (0.5, 0.06, 0.11), (0, 0.225, SH + 0.045), PP, 0.026, 4))
    for k in range(5):
        x = -0.18 + 0.09 * k
        p.append(tube("Slat", [(x, 0.235, SH + 0.08), (x * 1.18, 0.275, 0.8)], 0.026, PP, 12))
    p.append(tube("TopRail", smooth([(-0.26, 0.24, 0.67), (-0.25, 0.27, 0.77), (-0.15, 0.29, 0.825), (0, 0.295, 0.835),
                                     (0.15, 0.29, 0.825), (0.25, 0.27, 0.77), (0.26, 0.24, 0.67)]), 0.038, PP, 16))
    return finish(p, name, sharp_deg=55)


# ------------------------------------------------------------------------------------------------- the school chair

def school_chair(colour, name):
    SHL = S("Plastic_SchoolShell", colour, "plastic", 0.45)
    FR = S("Paint_SchoolFrame", FRAME_DARK, "paint", 0.45)
    GL = S("Plastic_SchoolGlide", (0.05, 0.05, 0.05), "plastic", 0.6)
    SH = 0.457
    p = [rbox("Seat", (0.44, 0.42, 0.05), (0, -0.02, SH - 0.025), SHL, 0.02, 4),
         rbox("SeatLip", (0.44, 0.05, 0.06), (0, -0.215, SH - 0.04), SHL, 0.022, 4),
         rbox("Back", (0.43, 0.045, 0.27), (0, 0.235, SH + 0.3), SHL, 0.02, 4),
         rbox("BackRoll", (0.43, 0.06, 0.05), (0, 0.235, SH + 0.425), SHL, 0.022, 4),
         rbox("HandHole", (0.13, 0.05, 0.03), (0, 0.235, SH + 0.38), GL, 0.012, 2)]
    r = 0.015
    for sx in (-1, 1):
        x = sx * 0.2
        # Each side in one bend: the front leg, the rail under the seat, up behind the back.
        p.append(tube("Side", [(x, -0.2, 0.016), (x, -0.195, SH - 0.06), (x, -0.17, SH - 0.06), (x, 0.18, SH - 0.06),
                               (x, 0.205, SH - 0.03), (x, 0.215, SH + 0.36)], r, FR, 12))
        p.append(tube("BackLeg", [(x, 0.18, SH - 0.06), (x * 1.03, 0.255, 0.016)], r, FR, 12))
        for c in ((x, -0.2), (x * 1.03, 0.255)):
            p.append(lathe("Glide", [(0.0, 0.0), (0.02, 0.0), (0.02, 0.008), (0.016, 0.02), (0.0, 0.02)], GL, 14, xf=M4(None, (c[0], c[1], 0.0))))
    for y in (-0.12, 0.13):
        p.append(tube("Stretcher", [(-0.2, y, SH - 0.06), (0.2, y, SH - 0.06)], r * 0.9, FR, 10))
    return finish(p, name, sharp_deg=55)


# ------------------------------------------------------------------------------------------------- partitions

def partition(width, height):
    alu = mat("PanelFrame", (0.62, 0.62, 0.6), 0.35, 0.8)
    fabric = mat("PanelFabric", (0.33, 0.37, 0.42), 0.95)
    trim = mat("PanelTrim", (0.22, 0.22, 0.22), 0.4)
    dark = mat("PanelDark", (0.03, 0.03, 0.03), 0.8)
    T, fw, base_h = 0.075, 0.045, 0.13
    base = []
    for x in (-width / 2 + fw / 2, width / 2 - fw / 2):
        base.append(rbox("Upright", (fw, T, height - 0.04), (x, 0, (height - 0.04) / 2 + 0.02), alu, 0.018, 4))
    base.append(rbox("Cap", (width + 0.008, T + 0.014, 0.045), (0, 0, height - 0.0225), trim, 0.02, 4))
    base.append(rbox("Base", (width - 2 * fw + 0.004, T - 0.004, base_h - 0.03), (0, 0, 0.03 + (base_h - 0.03) / 2), trim, 0.014, 3))
    # The fabric: fat vertical channels both sides.
    fh = height - 0.045 - base_h - 0.006
    n = max(2, round((width - 2 * fw) / 0.27))
    cw = (width - 2 * fw) / n
    for s in (1, -1):
        for i in range(n):
            x = -width / 2 + fw + cw * (i + 0.5)
            base.append(rbox("Fabric", (cw - 0.006, 0.026, fh), (x, s * (T / 2 - 0.009), base_h + 0.003 + fh / 2), fabric, 0.012, 3))
        for x in (-width * 0.28, width * 0.28):
            base.append(rbox("Knockout", (0.08, 0.004, 0.04), (x, s * (T / 2 - 0.001), 0.075), dark, 0.006, 2))
    for x in (-width / 2 + 0.07, width / 2 - 0.07):
        base.append(rbox("Foot", (0.07, T + 0.24, 0.03), (x, 0, 0.017), alu, 0.012, 3))
        for y in (-0.11, 0.11):
            base.append(lathe("Glide", [(0.0, 0.0), (0.02, 0.0), (0.02, 0.006), (0.0, 0.006)], dark, 14, xf=M4(None, (x, y, 0.0))))
    name = "Partition_%d_%d" % (round(width * 100), round(height * 100))
    return finish(base, name, sharp_deg=50)


V = (0.75, -1.0, 0.5)
run_baked([
    ("Shelving_100", lambda: shelving(1.0, 1.85, 0.42, "Shelving_100"), V, {"scale": 1.0}),
    ("Shelving_110", lambda: shelving(1.1, 2.0, 0.46, "Shelving_110"), V, {"scale": 1.0}),
    # The storage ladder (2026-11-08, Docs/GameDesign.md section 10): taller shelving, then pallet racking.
    ("ShelvingTall", lambda: shelving(1.1, 2.6, 0.46, "ShelvingTall", levels=5), V, {"scale": 1.0}),
    ("PalletRack", lambda: shelving(2.7, 2.6, 1.0, "PalletRack", levels=3, a=0.08), V, {"scale": 1.0}),
    ("LawnChair_White", lambda: lawn_chair((0.9, 0.9, 0.87), "LawnChair_White"), (0.8, -1.0, 0.5), {"scale": 0.6, "px_per_m": 480.0, "dirt": 1.1}),
    ("LawnChair_Green", lambda: lawn_chair((0.17, 0.36, 0.22), "LawnChair_Green"), (0.8, -1.0, 0.5), {"scale": 0.6, "px_per_m": 480.0, "dirt": 1.1}),
    ("SchoolChair_Blue", lambda: school_chair((0.13, 0.28, 0.58), "SchoolChair_Blue"), (0.8, -1.0, 0.4), {"scale": 0.6, "px_per_m": 500.0}),
    ("SchoolChair_Orange", lambda: school_chair((0.85, 0.4, 0.1), "SchoolChair_Orange"), (0.8, -1.0, 0.4), {"scale": 0.6, "px_per_m": 500.0}),
])
run([
    ("Partition_120_135", lambda: partition(1.2, 1.35), V),
    ("Partition_120_165", lambda: partition(1.2, 1.65), V),
    ("Partition_90_135", lambda: partition(0.9, 1.35), V),
    ("Partition_90_165", lambda: partition(0.9, 1.65), V),
])
print("[leftovers hub] done ->", OUT)
