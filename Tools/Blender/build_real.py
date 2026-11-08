"""
The real world's things (2026-11-07, Docs/ContractFloorsPlan.md "Walk-in seams"): what fills the four places behind the
seams (CustomRooms/SeamRooms) so they read at a glance as somewhere that is not the Backrooms: real light fittings and
daylight, a loading door, safety signs, a shop's counter and shelves of stock, a furniture showroom. The mid-90s (the
camcorder's date), clean and cool where the Backrooms are worn and yellow. Built in the game's Overflow look (chunky,
rounded, painted: Docs/StyleSheet.md) like everything else, but with the real world's colours. Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_real.py -- Assets/Backrooms/Resources/Overflow/Fittings --style overflow [--preview <dir>] [--only RealHighBay]

Slots named "Glow_..." shine (BakedMaterials: their colour is their light): lenses, windows, the daylight under a door.

  - RealHighBay: a round LED high-bay on a 0.75 m pendant (origin: the ceiling mount; it hangs below).
  - RealBatten: a 1.5 m surface LED batten (origin: on the ceiling).
  - RealWindow: an aluminium window, 2.4 x 1.1 m, three panes of wired obscure glass bright with daylight, a sill.
  - RollerDoor: a 3 x 3 m roller shutter in its guides and headbox, daylight under its bottom bar, a KEEP CLEAR box
    painted on the floor in front, the push-button box.
  - ShopCounter: a shop's service counter (1.8 x 0.65 m) with a beige cash register and its pole display.
  - RealGondolaA / RealGondolaB: a wall bay of shop shelving (1.3 x 0.5 x 1.9 m) stocked with an office supplier's
    goods: binders, reams, boxed staplers and pens, price strips on the shelf edges.
  - ShowroomSofa / ShowroomSofaB: a modern three-seat sofa with its SALE tag; ShowroomChair: a leather club chair;
    ShowroomTable: a white coffee table on oak legs; ShowroomLamp: an arc floor lamp; ShowroomRug: a patterned rug;
    SaleBanner: CLEARANCE SALE hanging on cords (origin: the ceiling).
  - PalletJack: a hand pallet truck (forks 1.15 m), red.
  - RealSignForklift / RealSignHiVis / RealSignFire: wall safety signs (origin: their bottom edge).
  - HiVisHooks: a coat rail with three hi-vis vests.
  - RealWallPhone: a beige wall phone with its coiled cord. RealClock: a plain white office clock.
  - Guillotine: a print shop's stack cutter on its stand.

Frames: Blender front toward -Y, Z up; wall things' backs on the wall (y = 0); origin at the middle of the foot.
Unity (x, y, z) = (x, z, -y).
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


def glow(name, srgb):
    """A lit face: its colour is the light it gives (BakedMaterials "glow"); baked clean."""
    m = mat("Glow_" + name, lin(srgb), 0.3, 0.0)          # (not through the Overflow look's warmer remap: daylight stays cool)
    m["linear"] = 1
    m["ov"] = 1
    m["finish"] = "glass"
    return m


def cyl(name, r, z0, z1, m, seg=24, cx=0.0, cy=0.0):
    return lathe(name, [(0.0, 0.0), (r, 0.0), (r, z1 - z0), (0.0, z1 - z0)], m, seg, xf=M4(None, (cx, cy, z0)))


DAY = (0.86, 0.93, 1.0)
LED = (0.95, 0.97, 1.0)


# ------------------------------------------------------------------------------------------------- lights

def high_bay():
    AL = S("Alu_HighBay", (0.62, 0.64, 0.66), "metal", 0.35, 1.0)
    DR = S("Paint_HighBayDriver", (0.3, 0.31, 0.33), "paint", 0.5, 0.2)
    CB = S("Rubber_HighBayCable", (0.08, 0.08, 0.08), "rubber", 0.6)
    LN = glow("HighBay", LED)
    p = [cyl("Canopy", 0.07, -0.035, 0.0, AL),
         tube("Cable", [(0, 0, -0.035), (0, 0, -0.74)], 0.005, CB, sides=8),
         cyl("Driver", 0.075, -0.86, -0.74, DR, 20)]
    # The heat sink: a shallow bowl with radial fins, the lens under it.
    p.append(lathe("Sink", [(0.0, 0.0), (0.1, 0.0), (0.2, 0.05), (0.21, 0.09), (0.0, 0.09)], AL, 36, xf=M4(None, (0, 0, -0.95))))
    for k in range(18):
        a = 2 * math.pi * k / 18
        fin = rbox("Fin", (0.12, 0.006, 0.06), (0, 0, 0), AL, 0.0, 1)
        fin.rotation_euler = (0, 0, a)
        fin.location = (math.cos(a) * 0.16, math.sin(a) * 0.16, -0.87)
        p.append(fin)
    p.append(cyl("Lens", 0.185, -0.965, -0.95, LN, 36))
    for o in p:
        bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action="DESELECT")
    for o in p:
        o.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return finish(p, "RealHighBay", sharp_deg=40)


def batten():
    WH = S("Paint_Batten", (0.93, 0.94, 0.94), "paint", 0.4, 0.1)
    LN = glow("Batten", LED)
    p = [rbox("Body", (1.5, 0.075, 0.05), (0, 0, -0.025), WH, 0.008, 2),
         rbox("Diffuser", (1.44, 0.055, 0.02), (0, 0, -0.055), LN, 0.008, 2),
         rbox("CapL", (0.02, 0.08, 0.06), (-0.755, 0, -0.03), WH, 0.004, 1),
         rbox("CapR", (0.02, 0.08, 0.06), (0.755, 0, -0.03), WH, 0.004, 1)]
    return finish(p, "RealBatten", sharp_deg=40)


def window():
    """Three panes of wired obscure glass in an aluminium frame on the wall, daylight behind them."""
    AL = S("Alu_Window", (0.7, 0.71, 0.72), "metal", 0.35, 0.9)
    SL = S("Paint_WindowSill", (0.9, 0.91, 0.9), "paint", 0.45, 0.1)
    W_, H_, F = 2.4, 1.1, 0.06
    c = Canvas(192, 96, DAY)
    rng = random.Random(4)
    for y in range(96):                                   # obscure glass: soft brighter blotches, a cooler top
        for x in range(192):
            k = y * 192 + x
            t = 0.97 + 0.03 * math.sin(x * 0.21 + y * 0.13) + 0.03 * math.sin(x * 0.07 - y * 0.29) - 0.06 * (1 - y / 96)
            c.px[k] = [min(1, DAY[0] * t), min(1, DAY[1] * t), min(1, DAY[2] * t), 1.0]
    for d in range(-96, 192, 12):                          # the wire mesh, square on the diagonal
        for i in range(96):
            for x in (d + i, d + 96 - i):
                if 0 <= x < 192:
                    c.px[i * 192 + x] = [0.62, 0.67, 0.72, 1.0]
    glass = print_mat("Glow_WindowGlass", c, sharp=True)
    p, cards = [], []
    pane = (W_ - 4 * F) / 3
    for i in range(3):
        x = -W_ / 2 + F + pane / 2 + i * (pane + F)
        cards.append(card("Pane", pane, H_ - 2 * F, (x, -0.012, H_ / 2), glass))
    p += [rbox("Top", (W_, 0.07, F), (0, -0.035, H_ - F / 2), AL, 0.006, 2),
          rbox("Bottom", (W_, 0.07, F), (0, -0.035, F / 2), AL, 0.006, 2)]
    for i in range(4):
        x = -W_ / 2 + F / 2 + i * (pane + F)
        p.append(rbox("Mullion", (F, 0.07, H_), (x, -0.035, H_ / 2), AL, 0.006, 2))
    p.append(rbox("Sill", (W_ + 0.12, 0.16, 0.035), (0, -0.08, -0.018), SL, 0.008, 2))
    for i in range(3):                                    # the opening lights' handles
        x = -W_ / 2 + F + pane / 2 + i * (pane + F)
        p.append(rbox("Handle", (0.1, 0.03, 0.025), (x, -0.08, H_ - F - 0.04), AL, 0.006, 2))
    return join([finish(p, "RealWindow", sharp_deg=40)] + cards, "RealWindow")


def roller_door():
    GD = S("Galv_DoorGuide", (0.58, 0.6, 0.6), "galv", 0.45, 0.8)
    SLT = S("Paint_DoorSlat", (0.72, 0.75, 0.78), "paint", 0.45, 0.4)
    HB = S("Galv_DoorHood", (0.55, 0.57, 0.58), "galv", 0.45, 0.8)
    BB = S("Alu_DoorBottom", (0.5, 0.5, 0.5), "metal", 0.4, 0.9)
    RB = S("Rubber_DoorSeal", (0.07, 0.07, 0.07), "rubber", 0.6)
    BX = S("Paint_DoorControl", (0.82, 0.82, 0.8), "paint", 0.45, 0.1)
    GR = S("Plastic_ButtonGreen", (0.15, 0.6, 0.25), "plastic", 0.4)
    RD = S("Plastic_ButtonRed", (0.8, 0.12, 0.1), "plastic", 0.4)
    W_, H_ = 3.0, 3.0
    p = [rbox("GuideL", (0.09, 0.11, H_ + 0.1), (-W_ / 2 - 0.045, -0.055, (H_ + 0.1) / 2), GD, 0.006, 2),
         rbox("GuideR", (0.09, 0.11, H_ + 0.1), (W_ / 2 + 0.045, -0.055, (H_ + 0.1) / 2), GD, 0.006, 2),
         rbox("Hood", (W_ + 0.4, 0.5, 0.55), (0, -0.25, H_ + 0.33), HB, 0.02, 2)]
    n = int((H_ - 0.08) / 0.075)
    for i in range(n):                                    # the curtain's slats, each rolled to a rib
        z = 0.08 + i * 0.075 + 0.0375
        p.append(rbox("Slat", (W_, 0.03, 0.068), (0, -0.04 - 0.008 * (i % 2), z), SLT, 0.01, 2))
    p += [rbox("BottomBar", (W_, 0.07, 0.06), (0, -0.045, 0.06), BB, 0.008, 2),
          rbox("Seal", (W_, 0.05, 0.025), (0, -0.045, 0.045), RB, 0.008, 2)]
    # Daylight under the door and spilling a little way in.
    cards = [card("Gap", W_ - 0.04, 0.03, (0, -0.012, 0.016), glow("DoorGap", DAY))]
    spill = Canvas(64, 32, (0, 0, 0))
    for y in range(32):
        t = (1 - y / 31) ** 2.2 * 0.55
        for x in range(64):
            e = min(1.0, min(x, 63 - x) / 6.0)
            spill.px[y * 64 + x] = [DAY[0] * t * e, DAY[1] * t * e, DAY[2] * t * e, 1.0]
    cards.append(card("Spill", W_ - 0.1, 0.45, (0, -0.24, 0.0015), print_mat("Glow_DoorSpill", spill), normal=(0, 0, 1)))
    # KEEP CLEAR: a hatched box painted on the floor in front.
    zone = Canvas(300, 120, (0.45, 0.47, 0.48))
    zone.frame(0, 0, 300, 120, 6, (0.93, 0.76, 0.1))
    zone.text_c("KEEP CLEAR", 150, 46, 4, (0.93, 0.76, 0.1))
    cards.append(card("Zone", W_, 1.2, (0, -1.1, 0.002), print_mat("Print_DoorZone", zone, sharp=True), normal=(0, 0, 1)))
    # The push-button box beside the right guide.
    p += [rbox("Control", (0.14, 0.08, 0.24), (W_ / 2 + 0.32, -0.04, 1.35), BX, 0.01, 2),
          cyl("Up", 0.018, 0, 0.02, GR, 16, 0, 0), cyl("Down", 0.018, 0, 0.02, RD, 16, 0, 0)]
    for o, z in ((p[-2], 1.4), (p[-1], 1.3)):
        o.rotation_euler = (math.radians(90), 0, 0)
        o.location = (W_ / 2 + 0.32, -0.08, z)
    return join([finish(p, "RollerDoor", sharp_deg=40)] + cards, "RollerDoor")


# ------------------------------------------------------------------------------------------------- shops

def counter():
    BODY = S("Laminate_CounterFront", hx('#4A5D73'), "laminate", 0.4)
    TOP = S("Laminate_CounterTop", hx('#D5D8D6'), "laminate", 0.45)
    KICK = S("Rubber_CounterKick", (0.08, 0.08, 0.08), "rubber", 0.6)
    BG = S("Beige_Register", hx('#D6CDB6'), "beige", 0.45)
    KY = S("Plastic_RegisterKeys", hx('#3A3A3A'), "plastic", 0.5)
    DR = S("Plastic_RegisterDrawer", hx('#2E2E30'), "plastic", 0.5)
    W_, D_, H_ = 1.8, 0.62, 0.98
    p = [rbox("Body", (W_, D_, H_ - 0.12), (0, 0, 0.1 + (H_ - 0.12) / 2), BODY, 0.01, 2),
         rbox("Kick", (W_ - 0.04, D_ - 0.06, 0.1), (0, 0.02, 0.05), KICK, 0.01, 2),
         rbox("Top", (W_ + 0.06, D_ + 0.08, 0.04), (0, 0.01, H_ - 0.02), TOP, 0.008, 2)]
    # The register, its keys toward the staff side (+Y), its drawer under it.
    x = 0.45
    p += [rbox("Drawer", (0.42, 0.42, 0.1), (x, 0.06, H_ + 0.05), DR, 0.01, 2),
          rbox("Register", (0.4, 0.36, 0.14), (x, 0.08, H_ + 0.17), BG, 0.02, 2),
          rbox("Keys", (0.3, 0.2, 0.02), (x, 0.12, H_ + 0.245), KY, 0.006, 2),
          tube("Pole", [(x - 0.1, -0.05, H_ + 0.24), (x - 0.1, -0.05, H_ + 0.42)], 0.012, BG, sides=10),
          rbox("Display", (0.14, 0.05, 0.06), (x - 0.1, -0.05, H_ + 0.45), BG, 0.01, 2)]
    pc = Canvas(56, 20, (0.05, 0.12, 0.08))
    pc.text_c("12.95", 28, 6, 1, (0.4, 1.0, 0.55))
    pole_text = card("PoleText", 0.12, 0.04, (x - 0.1, -0.0775, H_ + 0.45), print_mat("Glow_RegisterDisplay", pc, sharp=True))
    # A counter mat, a pen cup.
    p.append(rbox("Mat", (0.4, 0.3, 0.006), (-0.35, -0.08, H_ + 0.003), KICK, 0.002, 1))
    p.append(cyl("PenCup", 0.035, H_, H_ + 0.1, KY, 16, -0.7, 0.1))
    return join([finish(p, "ShopCounter", sharp_deg=40), pole_text], "ShopCounter")


PRODUCT = [hx('#C62828'), hx('#1565C0'), hx('#2E7D32'), hx('#F9A825'), hx('#FFFFFF'), hx('#6A1B9A'), hx('#EF6C00'), hx('#263238')]


def gondola(seed, name):
    PB = S("Paint_GondolaBack", hx('#C9CED2'), "paint", 0.45, 0.2)
    SH = S("Paint_GondolaShelf", hx('#E4E7E8'), "paint", 0.45, 0.2)
    UP = S("Paint_GondolaUpright", hx('#8D959B'), "paint", 0.45, 0.3)
    TK = S("Plastic_TicketStrip", hx('#F3E9A8'), "plastic", 0.4)
    mats = [S("Print_Product%d" % i, c, "plastic", 0.45) for i, c in enumerate(PRODUCT)]
    WHITE = S("Paper_Ream", (0.95, 0.95, 0.93), "paper", 0.7)
    BAND = S("Print_ReamBand", hx('#1565C0'), "print", 0.5)
    rng = random.Random(seed)
    W_, D_, H_ = 1.3, 0.5, 1.9
    p = [rbox("Back", (W_, 0.03, H_), (0, -0.015, H_ / 2), PB, 0.006, 2),
         rbox("UprightL", (0.04, 0.05, H_), (-W_ / 2, -0.03, H_ / 2), UP, 0.006, 2),
         rbox("UprightR", (0.04, 0.05, H_), (W_ / 2, -0.03, H_ / 2), UP, 0.006, 2),
         rbox("Base", (W_, D_, 0.12), (0, -D_ / 2, 0.06), UP, 0.008, 2),
         rbox("BaseDeck", (W_ - 0.02, D_ - 0.02, 0.02), (0, -D_ / 2, 0.13), SH, 0.005, 2)]
    levels = [0.14, 0.56, 0.96, 1.34]
    for i, z in enumerate(levels):
        d = D_ if i == 0 else 0.4
        if i > 0:
            p.append(rbox("Shelf", (W_ - 0.04, d, 0.025), (0, -d / 2 - 0.02, z), SH, 0.005, 2))
        p.append(rbox("Ticket", (W_ - 0.06, 0.008, 0.035), (0, -d - 0.02 - 0.004, z - 0.005), TK, 0.002, 1))
        # Stock: a run of one kind of thing per shelf, a gap or two where it has sold.
        kind = rng.choice(["binders", "reams", "boxes", "boxes"])
        x = -W_ / 2 + 0.05
        top = z + 0.0125
        while x < W_ / 2 - 0.08:
            if rng.random() < 0.08:
                x += 0.12
                continue
            if kind == "binders":
                w, dd, h = 0.06, 0.29, 0.32
                p.append(rbox("Binder", (w - 0.006, dd, h), (x + w / 2, -0.04 - dd / 2, top + h / 2), mats[rng.randrange(len(mats))], 0.006, 2))
            elif kind == "reams":
                w, dd, h = 0.22, 0.29, 0.055
                for k in range(rng.randint(3, 5)):
                    zz = top + h / 2 + k * h
                    p.append(rbox("Ream", (w - 0.008, dd, h - 0.003), (x + w / 2, -0.04 - dd / 2, zz), WHITE, 0.004, 1))
                    p.append(rbox("Band", (0.05, dd + 0.002, h - 0.002), (x + w / 2, -0.04 - dd / 2, zz), BAND, 0.002, 1))
            else:
                w, dd, h = rng.choice([(0.14, 0.2, 0.09), (0.1, 0.16, 0.18), (0.18, 0.25, 0.12)])
                m = mats[rng.randrange(len(mats))]
                for k in range(rng.randint(1, 3)):
                    p.append(rbox("Box", (w - 0.008, dd, h - 0.004), (x + w / 2, -0.04 - dd / 2, top + h / 2 + k * h), m, 0.006, 2))
            x += w
    return finish(p, name, sharp_deg=40)


def sofa(name, colour):
    FB = S("Fabric_Showroom" + name, colour, "fabric", 0.85)
    WD = S("Wood_ShowroomLeg", hx('#B88B5A'), "wood", 0.5)
    TG = S("Paper_PriceTag", (0.97, 0.97, 0.95), "paper", 0.7)
    W_, D_ = 2.1, 0.9
    p = [rbox("Base", (W_, D_, 0.2), (0, 0, 0.26), FB, 0.04, 3),
         rbox("ArmL", (0.18, D_, 0.42), (-W_ / 2 + 0.09, 0, 0.37), FB, 0.05, 3),
         rbox("ArmR", (0.18, D_, 0.42), (W_ / 2 - 0.09, 0, 0.37), FB, 0.05, 3),
         rbox("Back", (W_ - 0.36, 0.2, 0.36), (0, D_ / 2 - 0.1, 0.52), FB, 0.05, 3)]
    for i in range(3):
        x = -W_ / 2 + 0.18 + (W_ - 0.36) / 6 * (2 * i + 1)
        p.append(rbox("Seat", ((W_ - 0.36) / 3 - 0.015, D_ - 0.24, 0.13), (x, -0.1, 0.42), FB, 0.05, 3))
        p.append(rbox("BackCushion", ((W_ - 0.36) / 3 - 0.02, 0.16, 0.36), (x, D_ / 2 - 0.27, 0.66), FB, 0.06, 3))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(lathe("Leg", [(0.0, 0.0), (0.02, 0.0), (0.028, 0.16), (0.0, 0.16)], WD, 12,
                           xf=M4(None, (sx * (W_ / 2 - 0.1), sy * (D_ / 2 - 0.1), 0.0))))
    # The tag, hung from the right arm on a loop of string.
    tc = Canvas(80, 110, (0.97, 0.97, 0.95))
    tc.rect(0, 0, 80, 30, (0.78, 0.1, 0.1))
    tc.text_c("SALE", 40, 8, 2, (1, 1, 1))
    tc.text_c("$499", 40, 48, 3, (0.1, 0.1, 0.1))
    tc.text_c("WAS $699", 40, 84, 1, (0.4, 0.4, 0.4))
    p.append(card("Tag", 0.08, 0.11, (W_ / 2 - 0.09, -D_ / 2 - 0.006, 0.45), print_mat("Print_SofaTag" + name, tc, sharp=True)))
    return finish(p, name, sharp_deg=40)


def club_chair():
    LT = S("Plastic_ShowroomLeather", hx('#A8714A'), "fabric", 0.45)
    ST = S("Steel_ShowroomChairLeg", (0.15, 0.15, 0.15), "metal", 0.4, 0.8)
    p = [rbox("Seat", (0.82, 0.78, 0.18), (0, 0, 0.36), LT, 0.05, 3),
         rbox("ArmL", (0.16, 0.82, 0.3), (-0.37, 0, 0.5), LT, 0.06, 3),
         rbox("ArmR", (0.16, 0.82, 0.3), (0.37, 0, 0.5), LT, 0.06, 3),
         rbox("Back", (0.82, 0.18, 0.46), (0, 0.33, 0.6), LT, 0.06, 3),
         rbox("Cushion", (0.56, 0.6, 0.12), (0, -0.07, 0.5), LT, 0.05, 3)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(tube("Leg", [(sx * 0.33, sy * 0.3, 0.0), (sx * 0.31, sy * 0.28, 0.27)], 0.016, ST, sides=10))
    return finish(p, "ShowroomChair", sharp_deg=40)


def coffee_table():
    TP = S("Laminate_ShowroomTable", (0.95, 0.95, 0.94), "laminate", 0.6)
    WD = S("Wood_ShowroomTableLeg", hx('#B88B5A'), "wood", 0.5)
    MG = S("Print_Magazine", hx('#1565C0'), "print", 0.5)
    VS = S("Enamel_Vase", hx('#2E7D72'), "enamel", 0.7)
    p = [rbox("Top", (1.1, 0.6, 0.04), (0, 0, 0.4), TP, 0.012, 3)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(tube("Leg", [(sx * 0.48, sy * 0.24, 0.0), (sx * 0.46, sy * 0.22, 0.385)], 0.02, WD, sides=10))
    p.append(rbox("Magazine", (0.22, 0.29, 0.008), (-0.25, 0.05, 0.424), MG, 0.002, 1))
    p.append(lathe("Vase", [(0.0, 0.0), (0.05, 0.0), (0.07, 0.08), (0.04, 0.2), (0.045, 0.24), (0.0, 0.24)], VS, 20, xf=M4(None, (0.3, -0.05, 0.42))))
    return finish(p, "ShowroomTable", sharp_deg=40)


def arc_lamp():
    CH = S("Chrome_ArcLamp", (0.8, 0.8, 0.82), "metal", 0.85, 1.0)
    MB = S("Stone_ArcLampBase", (0.9, 0.9, 0.88), "stone", 0.6)
    SH = S("Paint_ArcLampShade", (0.95, 0.95, 0.94), "paint", 0.5)
    pts = [(0.0, 0.0, 0.1)]
    for k in range(1, 13):
        t = k / 12
        a = t * math.pi * 0.62
        pts.append((math.sin(a) * 0.9 * t ** 0.3, 0.0, 0.1 + math.sin(min(1.0, t * 1.4) * math.pi * 0.5) * 1.9 - t ** 3 * 0.35))
    p = [rbox("Base", (0.32, 0.32, 0.1), (0, 0, 0.05), MB, 0.02, 2), tube("Arc", pts, 0.012, CH, sides=10)]
    end = pts[-1]
    p.append(lathe("Shade", [(0.0, 0.18), (0.08, 0.18), (0.2, 0.0), (0.19, 0.0), (0.075, 0.165), (0.0, 0.165)], SH, 28,
                   xf=M4(None, (end[0], 0.0, end[2] - 0.2))))
    return finish(p, "ShowroomLamp", sharp_deg=40)


def rug():
    c = Canvas(200, 140, hx('#2F4858'))
    c.frame(6, 6, 194, 134, 6, hx('#E0D3B8'))
    for i in range(5):
        for j in range(3):
            cx, cy = 30 + i * 35, 35 + j * 35
            c.disc(cx, cy, 11, hx('#C8553D') if (i + j) % 2 else hx('#F2A541'))
            c.disc(cx, cy, 5, hx('#E0D3B8'))
    return join([card("Rug", 2.0, 1.4, (0, 0, 0.004), print_mat("Fabric_ShowroomRug", c), normal=(0, 0, 1))], "ShowroomRug")


def sale_banner():
    CD = S("Rubber_BannerCord", (0.1, 0.1, 0.1), "rubber", 0.6)
    bc = Canvas(320, 80, hx('#C62828'))
    bc.frame(0, 0, 320, 80, 4, (1, 1, 1))
    bc.text_c("CLEARANCE", 160, 10, 4, (1, 1, 1))
    bc.text_c("SALE  -  ALL MUST GO", 160, 52, 2, hx('#FFD54F'))
    pm = print_mat("Print_SaleBanner", bc, sharp=True)
    cards = [card("Front", 2.4, 0.6, (0, -0.002, -0.9), pm), card("Rear", 2.4, 0.6, (0, 0.002, -0.9), pm, normal=(0, 1, 0))]
    p = [tube("Cord", [(sx * 1.1, 0, 0.0), (sx * 1.1, 0, -0.6)], 0.003, CD, sides=6) for sx in (-1, 1)]
    return join([finish(p, "SaleBanner", sharp_deg=40)] + cards, "SaleBanner")


# ------------------------------------------------------------------------------------------------- the warehouse

def pallet_jack():
    RD = S("Paint_PalletJack", hx('#C8401E'), "paint", 0.5, 0.2)
    BK = S("Rubber_PalletJackWheel", (0.07, 0.07, 0.07), "rubber", 0.6)
    ST = S("Steel_PalletJackRod", (0.55, 0.55, 0.55), "metal", 0.4, 0.9)
    L_ = 1.15
    p = []
    for sx in (-1, 1):
        p.append(rbox("Fork", (0.16, L_, 0.05), (sx * 0.19, -L_ / 2, 0.06), RD, 0.01, 2))
        p.append(cyl("Roller", 0.035, 0, 0.07, BK, 14, 0, 0))
        p[-1].rotation_euler = (0, math.radians(90), 0)
        p[-1].location = (sx * 0.19 - 0.035, -L_ + 0.08, 0.035)
    p += [rbox("Body", (0.55, 0.24, 0.16), (0, 0.12, 0.12), RD, 0.02, 2),
          rbox("Pump", (0.16, 0.16, 0.34), (0, 0.18, 0.34), RD, 0.02, 2),
          cyl("WheelL", 0.09, 0, 0.05, BK, 18), cyl("WheelR", 0.09, 0, 0.05, BK, 18)]
    for o, x in ((p[-2], -0.12), (p[-1], 0.07)):
        o.rotation_euler = (0, math.radians(90), 0)
        o.location = (x, 0.18, 0.09)
    h = [(0, 0.2, 0.5), (0, 0.32, 1.0), (0, 0.36, 1.18)]
    p.append(tube("Handle", h, 0.016, ST, sides=10))
    p.append(tube("Grip", [(-0.17, 0.37, 1.22), (0.17, 0.37, 1.22)], 0.02, BK, sides=10))
    p.append(tube("GripL", [(-0.17, 0.37, 1.22), (0, 0.36, 1.12)], 0.016, RD, sides=10))
    p.append(tube("GripR", [(0.17, 0.37, 1.22), (0, 0.36, 1.12)], 0.016, RD, sides=10))
    bpy.ops.object.select_all(action="DESELECT")
    for o in p:
        o.select_set(True)
    bpy.context.view_layer.objects.active = p[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return finish(p, "PalletJack", sharp_deg=40)


def safety_sign(name, ground, ink, title, line2, symbol):
    BD = S("Plastic_" + name, ground, "plastic", 0.4)
    W_, H_ = 0.45, 0.3
    c = Canvas(225, 150, ground)
    if symbol == "triangle":
        for y in range(60):
            half = y * 0.55
            c.rect(112 - half, 10 + y, 112 + half, 11 + y, ink)
            if y > 8:
                c.rect(112 - half + 6, 10 + y, 112 + half - 6, 11 + y, ground)
        c.text_c("!", 112, 38, 3, ink)
    elif symbol == "circle":
        c.disc(112, 40, 32, ink)
        c.disc(112, 30, 7, ground)
        c.rect(102, 40, 122, 64, ground)
    else:
        c.rect(70, 14, 155, 66, ink)
        c.text_c("EXIT", 112, 32, 3, ground)
    c.text_c(title, 112, 84, 2, ink)
    c.text_c(line2, 112, 112, 2, ink)
    body = finish([rbox("Board", (W_, 0.006, H_), (0, -0.003, H_ / 2), BD, 0.004, 2)], name, sharp_deg=50)
    art = card("Art", W_ - 0.01, H_ - 0.01, (0, -0.0065, H_ / 2), print_mat("Print_" + name, c, sharp=True))
    return join([body, art], name)


def hivis_hooks():
    WD = S("Wood_HookRail", hx('#9C7A54'), "wood", 0.5)
    ST = S("Steel_Hook", (0.5, 0.5, 0.5), "metal", 0.45, 0.9)
    VS = S("Fabric_HiVis", hx('#C6E82A'), "fabric", 0.6)
    RF = S("Print_HiVisStripe", (0.78, 0.8, 0.8), "print", 0.3)
    p = [rbox("Rail", (1.25, 0.025, 0.1), (0, -0.0125, 1.6), WD, 0.006, 2)]
    for i, x in enumerate((-0.42, 0.0, 0.42)):
        p.append(tube("Hook", [(x, -0.02, 1.62), (x, -0.07, 1.6), (x, -0.08, 1.64)], 0.005, ST, sides=6))
        sway = (i - 1) * 0.015
        # Hung by its neck: two shoulders either side of the hook, the body below, the reflective bands round it.
        for s in (-1, 1):
            p.append(rbox("Shoulder", (0.09, 0.04, 0.16), (x + sway + s * 0.085, -0.085, 1.5), VS, 0.015, 2))
        p.append(rbox("Vest", (0.32, 0.045, 0.46), (x + sway, -0.085, 1.2), VS, 0.02, 3))
        for z in (1.08, 1.18):
            p.append(rbox("Stripe", (0.325, 0.047, 0.03), (x + sway, -0.085, z), RF, 0.004, 1))
    return finish(p, "HiVisHooks", sharp_deg=40)


def wall_phone():
    BG = S("Beige_WallPhone", hx('#D9D0BA'), "beige", 0.45)
    KY = S("Plastic_PhoneKeys", hx('#5A5A5A'), "plastic", 0.5)
    CD = S("Plastic_PhoneCord", hx('#D9D0BA'), "plastic", 0.5)
    p = [rbox("Base", (0.1, 0.05, 0.22), (0, -0.025, 1.3), BG, 0.01, 2),
         rbox("Handset", (0.055, 0.05, 0.22), (0, -0.07, 1.31), BG, 0.02, 2),
         rbox("Keys", (0.05, 0.005, 0.07), (0.0, -0.052, 1.23), KY, 0.003, 1)]
    pts = []
    for k in range(60):
        t = k / 59
        a = t * 2 * math.pi * 14
        pts.append((0.035 + math.cos(a) * 0.012 + t * 0.02, -0.07 + math.sin(a) * 0.012, 1.2 - t * 0.35))
    p.append(tube("Cord", pts, 0.003, CD, sides=6))
    return finish(p, "RealWallPhone", sharp_deg=40)


def real_clock():
    RM = S("Plastic_ClockRim", (0.95, 0.95, 0.95), "plastic", 0.5)
    c = Canvas(160, 160, (0.98, 0.98, 0.97))
    for k in range(12):
        a = 2 * math.pi * k / 12
        cx, cy = 80 + math.sin(a) * 66, 80 - math.cos(a) * 66
        c.rect(cx - 3, cy - 3, cx + 3, cy + 3, (0.1, 0.1, 0.1))
    def hand(a, L, w):
        for i in range(int(L)):
            c.rect(80 + math.sin(a) * i - w, 80 - math.cos(a) * i - w, 80 + math.sin(a) * i + w, 80 - math.cos(a) * i + w, (0.08, 0.08, 0.08))
    hand(math.radians(305), 44, 3); hand(math.radians(60), 60, 2)
    c.disc(80, 80, 5, (0.8, 0.1, 0.1))
    rim = lathe("Rim", [(0.0, 0.0), (0.16, 0.0), (0.16, 0.045), (0.145, 0.045), (0.145, 0.01), (0.0, 0.01)], RM, 40,
                xf=M4(rot('X', 90), (0, 0, 0.16)))
    # The face: a round print filling the rim (a square card showed its corners against the rim's back).
    r, y, zc = 0.145, -0.012, 0.16
    bm = bmesh.new()
    ring = [bm.verts.new((r * math.cos(2 * math.pi * i / 48), y, zc + r * math.sin(2 * math.pi * i / 48))) for i in range(48)]
    f = bm.faces.new(ring)
    f.normal_update()
    if f.normal.y > 0:
        f.normal_flip()
    face = new_obj("Face", bm, [print_mat("Print_RealClockFace", c, sharp=True)])
    me = face.data
    uv = me.uv_layers.new(name="UVMap").data
    for p in me.polygons:
        p.use_smooth = False
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            uv[li].uv = (v.x / (2 * r) + 0.5, (v.z - zc) / (2 * r) + 0.5)
    return join([finish([rim], "RealClock", sharp_deg=40), face], "RealClock")


def guillotine():
    ST = S("Paint_Guillotine", hx('#2F5F8A'), "paint", 0.45, 0.2)
    TB = S("Laminate_GuillotineBed", (0.85, 0.86, 0.85), "laminate", 0.5)
    BL = S("Steel_GuillotineBlade", (0.72, 0.72, 0.72), "metal", 0.6, 1.0)
    RD = S("Plastic_GuillotineHandle", (0.75, 0.12, 0.1), "plastic", 0.4)
    PP = S("Paper_GuillotineStack", (0.96, 0.96, 0.94), "paper", 0.7)
    p = [rbox("Stand", (0.8, 0.6, 0.8), (0, 0, 0.4), ST, 0.02, 2),
         rbox("Bed", (0.82, 0.62, 0.05), (0, 0, 0.825), TB, 0.01, 2),
         rbox("Back", (0.82, 0.06, 0.35), (0, 0.28, 1.0), ST, 0.02, 2),
         rbox("ArmL", (0.06, 0.1, 0.45), (-0.38, 0.0, 1.05), ST, 0.02, 2),
         rbox("ArmR", (0.06, 0.1, 0.45), (0.38, 0.0, 1.05), ST, 0.02, 2),
         rbox("Beam", (0.82, 0.12, 0.1), (0, 0.0, 1.25), ST, 0.02, 2),
         rbox("Blade", (0.74, 0.02, 0.12), (0, -0.07, 1.16), BL, 0.004, 1),
         tube("Lever", [(0.38, -0.02, 1.25), (0.55, -0.15, 1.45)], 0.018, ST, sides=10),
         rbox("Grip", (0.05, 0.05, 0.12), (0.57, -0.17, 1.49), RD, 0.012, 2),
         rbox("Paper", (0.42, 0.3, 0.08), (0.0, 0.08, 0.89), PP, 0.003, 1)]
    return finish(p, "Guillotine", sharp_deg=40)


V = (0.75, -1.0, 0.45)
SIGN = (0.3, -1.0, 0.2)
run_baked([
    ("RealHighBay", high_bay, (0.6, -1.0, -0.4), {"scale": 0.6}),
    ("RealBatten", batten, (0.6, -1.0, -0.4), {"scale": 0.6}),
    ("RealWindow", window, SIGN, {"scale": 1.0, "px_per_m": 300.0}),
    ("RollerDoor", roller_door, V, {"scale": 1.0, "px_per_m": 200.0}),
    ("ShopCounter", counter, V, {"scale": 1.0}),
    ("RealGondolaA", lambda: gondola(11, "RealGondolaA"), V, {"scale": 1.0}),
    ("RealGondolaB", lambda: gondola(23, "RealGondolaB"), V, {"scale": 1.0}),
    ("ShowroomSofa", lambda: sofa("ShowroomSofa", hx('#56677A')), V, {"scale": 1.0}),
    ("ShowroomSofaB", lambda: sofa("ShowroomSofaB", hx('#7D9A84')), V, {"scale": 1.0}),
    ("ShowroomChair", club_chair, V, {"scale": 1.0}),
    ("ShowroomTable", coffee_table, V, {"scale": 1.0}),
    ("ShowroomLamp", arc_lamp, V, {"scale": 1.0}),
    ("ShowroomRug", rug, (0.3, -0.6, 1.0), {"scale": 1.0, "px_per_m": 160.0}),
    ("SaleBanner", sale_banner, SIGN, {"scale": 1.0, "px_per_m": 200.0}),
    ("PalletJack", pallet_jack, V, {"scale": 1.0}),
    ("RealSignForklift", lambda: safety_sign("RealSignForklift", hx('#F4C20D'), hx('#111111'), "FORKLIFTS", "OPERATE HERE", "triangle"), SIGN, {"scale": 0.4, "px_per_m": 600.0}),
    ("RealSignHiVis", lambda: safety_sign("RealSignHiVis", (0.97, 0.97, 0.97), hx('#1E5AA8'), "HI-VIS VESTS", "MUST BE WORN", "circle"), SIGN, {"scale": 0.4, "px_per_m": 600.0}),
    ("RealSignFire", lambda: safety_sign("RealSignFire", hx('#1B8A3A'), (1, 1, 1), "FIRE EXIT", "KEEP CLEAR", "exit"), SIGN, {"scale": 0.4, "px_per_m": 600.0}),
    ("HiVisHooks", hivis_hooks, V, {"scale": 1.0}),
    ("RealWallPhone", wall_phone, SIGN, {"scale": 0.5}),
    ("RealClock", real_clock, SIGN, {"scale": 0.4, "px_per_m": 500.0}),
    ("Guillotine", guillotine, V, {"scale": 1.0}),
])
