"""
The hub's own fittings (2026-11-07, Docs/HubPlan.md), built in the Overflow look: chunky, rounded, in the company's colours
(amber #F0A21E; the stripe orange #D9661F, gold #E8B23A, avocado #7C8A2E; chocolate #3B2A1E), baked with dirt and wear
(bake_kit.py). Run headless, always with the Overflow style:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_hub.py -- Assets/Backrooms/Resources/Overflow/Fittings --style overflow [--preview <dir>] [--only HubLockers,HubCot]

  - HubLockers: a bank of four 0.3 m steel lockers (1.2 x 0.45 x 1.85 m) in avocado, louvres top and bottom, lift handles,
    number plates, on a chocolate plinth.
  - HubBench: a locker-room bench, three wooden slats on two steel pedestals (1.5 x 0.32 x 0.45).
  - HubCot: a first-aid cot (1.9 x 0.75), tube frame, mint vinyl mattress, a pillow and a folded avocado blanket.
  - HubFirstAid: a wall first-aid cabinet (0.45 x 0.55 x 0.16), white enamel, a red cross. (Origin: on the wall at its foot.)
  - HubTvCart: an AV cart with a wood-cased 25" CRT and a VCR, on casters (0.66 x 0.48, 1.5 m tall).
  - HubBoard: the contracts board (2.4 x 1.3 x 0.1), split-flap rows reading NO CONTRACTS POSTED. (Wall.)
  - HubPunchClock: a time clock (0.3 x 0.42 x 0.2), its dial and card slot. HubCardRack: a rack of time cards. (Wall.)
  - HubCoffee: a coffee vending machine (0.75 x 0.72 x 1.85), chocolate and orange, COFFEE and a cup.
  - HubPlant: a dead potted plant.
  - HubPosterA/B/C: the company's framed posters (0.62 x 0.86). (Wall.)
  - HubReception: the reception counter (2.4 x 0.75 x 1.05), board-panelled front with the stripe, a bell.
  - HubCoffeeTable: a low lobby table (1.1 x 0.6 x 0.42).

Frames: Blender front toward -Y, Z up, origin on the floor at the middle of the foot; wall things with their back on the
wall (y = 0) and origin at their foot. Unity (x, y, z) = (x, z, -y).
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
from mathutils import Vector


def hx(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


AMBER, ORANGE, GOLD, AVOCADO, CHOC = hx('#F0A21E'), hx('#D9661F'), hx('#E8B23A'), hx('#7C8A2E'), hx('#3B2A1E')
CREAM = hx('#E8DCC0')
C_AMBER, C_ORANGE, C_GOLD, C_AVO, C_CHOC, C_CREAM = AMBER, ORANGE, GOLD, AVOCADO, CHOC, CREAM


def stripe(prefix, w, z0, y, h=0.045, depth=0.008):
    """The company stripe as three bands on a front facing -Y (orange low, gold, avocado high)."""
    out = []
    for i, (nm, c) in enumerate((("Or", ORANGE), ("Go", GOLD), ("Av", AVOCADO))):
        out.append(rbox(prefix + nm, (w, depth, h), (0, y, z0 + h * (i + 0.5)), S("Paint_" + prefix + nm, c, "paint", 0.5), 0.0, 1))
    return out


# ------------------------------------------------------------------------------------------------- lockers

def lockers():
    P = S("Paint_HubLocker", AVOCADO, "paint", 0.5)
    DK = S("Paint_HubLockerVent", hx('#3E4614'), "paint", 0.6)
    HD = S("Metal_HubLockerHandle", (0.76, 0.74, 0.7), "metal", 0.3, 1.0)
    PL = S("Paint_HubLockerPlinth", CHOC, "paint", 0.6)
    W_, D_, H_, base, n = 1.2, 0.45, 1.85, 0.1, 4
    dw = W_ / n
    p = [rbox("Plinth", (W_ - 0.03, D_ - 0.05, base), (0, 0.025, base / 2), PL, 0.012, 2),
         rbox("Body", (W_, D_, H_ - base), (0, 0, base + (H_ - base) / 2), P, 0.02, 3),
         rbox("Cap", (W_ + 0.02, D_ + 0.02, 0.03), (0, 0, H_ + 0.012), DK, 0.01, 2)]
    plates = []
    fy = -D_ / 2
    for i in range(n):
        x = -W_ / 2 + dw * (i + 0.5)
        z0, z1 = base + 0.03, H_ - 0.03
        p.append(rbox("Door", (dw - 0.03, 0.022, z1 - z0), (x, fy - 0.009, (z0 + z1) / 2), P, 0.01, 2))
        for zt in (z1 - 0.1, z0 + 0.16):
            for k in range(3):
                p.append(rbox("Vent", (dw * 0.5, 0.012, 0.014), (x, fy - 0.024, zt - 0.035 * k), DK, 0.003, 1))
        p.append(rbox("Handle", (0.034, 0.035, 0.17), (x + dw * 0.3, fy - 0.036, base + 0.9), HD, 0.01, 2))
        plates.append((x, z1 - 0.3, str(i + 1)))
    body = finish(p, "HubLockers", sharp_deg=50)
    cards = []
    for i, (x, z, t) in enumerate(plates):
        c = Canvas(48, 32, (0.92, 0.88, 0.74))
        c.text_c(t, 24, 6, 3, C_CHOC)
        cards.append(card("Num", 0.065, 0.044, (x, fy - 0.024, z), print_mat("Print_HubLockerNum%d" % i, c)))
    return join([body] + cards, "HubLockers")


def bench():
    WD = S("Wood_HubBench", hx('#9A6A3A'), "wood", 0.6)
    ST = S("Paint_HubBenchLeg", CHOC, "paint", 0.5)
    L, Wd, H = 1.5, 0.32, 0.45
    p = [rbox("Slat", (L, Wd / 3 - 0.014, 0.04), (0, -Wd / 2 + Wd / 6 + k * Wd / 3, H - 0.02), WD, 0.008, 2) for k in range(3)]
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.2)
        p.append(rbox("Leg", (0.06, Wd - 0.05, H - 0.04), (x, 0, (H - 0.04) / 2), ST, 0.012, 2))
        p.append(rbox("Foot", (0.1, Wd, 0.035), (x, 0, 0.0175), ST, 0.01, 2))
    p.append(rbox("Rail", (L - 0.4, 0.045, 0.045), (0, 0, 0.2), ST, 0.01, 2))
    return finish(p, "HubBench", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- first aid

def cot():
    FR = S("Paint_HubCotFrame", hx('#B9B2A2'), "paint", 0.45)
    MT = S("Fabric_HubCotMattress", hx('#8DBA9B'), "fabric", 0.55)
    PW = S("Fabric_HubCotPillow", hx('#EFE8D8'), "fabric", 0.8)
    BL = S("Fabric_HubCotBlanket", AVOCADO, "fabric", 0.85)
    L, Wd, H = 1.9, 0.75, 0.42
    p = []
    for sy in (-1, 1):
        p.append(rbox("RailL", (L, 0.045, 0.045), (0, sy * (Wd / 2 - 0.022), H - 0.13), FR, 0.018, 3))
    for sx in (-1, 1):
        p.append(rbox("RailE", (0.045, Wd, 0.045), (sx * (L / 2 - 0.022), 0, H - 0.13), FR, 0.018, 3))
        for sy in (-1, 1):
            p.append(rbox("Leg", (0.04, 0.04, H - 0.13), (sx * (L / 2 - 0.05), sy * (Wd / 2 - 0.05), (H - 0.13) / 2), FR, 0.014, 3))
            p.append(rbox("Foot", (0.06, 0.06, 0.02), (sx * (L / 2 - 0.05), sy * (Wd / 2 - 0.05), 0.01), S("Rubber_HubCotFoot", (0.1, 0.1, 0.1), "rubber", 0.6), 0.008, 2))
    p.append(rbox("Mattress", (L - 0.03, Wd - 0.03, 0.13), (0, 0, H - 0.065), MT, 0.045, 4))
    p.append(rbox("Pillow", (0.38, Wd - 0.16, 0.09), (L / 2 - 0.27, 0, H + 0.04), PW, 0.04, 4))
    p.append(rbox("Blanket", (0.42, Wd - 0.06, 0.06), (-L / 2 + 0.3, 0, H + 0.028), BL, 0.025, 3))
    return finish(p, "HubCot", sharp_deg=50)


def first_aid():
    EN = S("Enamel_HubFirstAid", hx('#EEE8DA'), "enamel", 0.35)
    RD = S("Paint_HubFirstAidCross", hx('#C4321E'), "paint", 0.4)
    HD = S("Metal_HubFirstAidLatch", (0.72, 0.71, 0.68), "metal", 0.3, 1.0)
    W_, H_, D_ = 0.45, 0.55, 0.16
    p = [rbox("Box", (W_, D_, H_), (0, -D_ / 2, H_ / 2), EN, 0.025, 3),
         rbox("Door", (W_ - 0.025, 0.022, H_ - 0.025), (0, -D_ - 0.009, H_ / 2), EN, 0.012, 2),
         rbox("CrossV", (0.075, 0.012, 0.24), (0, -D_ - 0.024, H_ / 2 + 0.03), RD, 0.005, 2),
         rbox("CrossH", (0.24, 0.012, 0.075), (0, -D_ - 0.024, H_ / 2 + 0.03), RD, 0.005, 2),
         rbox("Latch", (0.03, 0.025, 0.07), (W_ / 2 - 0.05, -D_ - 0.03, H_ / 2), HD, 0.008, 2)]
    body = finish(p, "HubFirstAid", sharp_deg=50)
    c = Canvas(160, 36, (0.93, 0.9, 0.82))
    c.text_c("FIRST AID", 80, 8, 3, (0.72, 0.16, 0.1))
    lab = card("Label", 0.32, 0.072, (0, -D_ - 0.022, 0.085), print_mat("Print_HubFirstAid", c))
    return join([body, lab], "HubFirstAid")


# ------------------------------------------------------------------------------------------------- training room

def tv_cart():
    ST = S("Paint_HubAvCart", hx('#2B2420'), "paint", 0.45)
    WD = S("Wood_HubTvCabinet", hx('#7A4E2A'), "wood", 0.45)
    BZ = S("Plastic_HubTvBezel", hx('#2E2925'), "plastic", 0.4)
    SC = S("Glass_HubTvScreen", (0.08, 0.1, 0.09), "glass", 0.08)
    KN = S("Plastic_HubTvKnob", hx('#CDBF9E'), "plastic", 0.4)
    VC = S("Plastic_HubVcr", hx('#1E1B19'), "plastic", 0.4)
    RB = S("Rubber_HubCaster", (0.1, 0.1, 0.1), "rubber", 0.6)
    W_, D_ = 0.66, 0.48
    p = []
    for z in (0.14, 0.56, 0.98):
        p.append(rbox("Shelf", (W_, D_, 0.03), (0, 0, z), ST, 0.01, 2))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(rbox("Post", (0.035, 0.035, 0.88), (sx * (W_ / 2 - 0.025), sy * (D_ / 2 - 0.025), 0.12 + 0.44), ST, 0.01, 2))
            p.append(sphere("Wheel", (sx * (W_ / 2 - 0.05), sy * (D_ / 2 - 0.05), 0.05), (0.05, 0.05, 0.05), RB, 12, 8))
    tw, th, td, z0 = 0.64, 0.52, 0.46, 0.995
    p.append(rbox("TvBox", (tw, td, th), (0, 0.0, z0 + th / 2), WD, 0.035, 3))
    fy = -td / 2
    p.append(rbox("Bezel", (tw * 0.66, 0.02, th * 0.78), (-0.06, fy - 0.008, z0 + th / 2), BZ, 0.02, 3))
    scr = bulge_grid("Screen", tw * 0.56, th * 0.62, fy - 0.02, fy - 0.045, z0 + th / 2, SC)
    for v in scr.data.vertices:
        v.co.x -= 0.06
    p.append(scr)
    for k, z in enumerate((z0 + th * 0.7, z0 + th * 0.48)):
        p.append(tube_y("Knob", tw / 2 - 0.075, z, fy - 0.035, fy, 0.026, KN, 16))
    for k in range(5):
        p.append(rbox("Grille", (0.08, 0.008, 0.008), (tw / 2 - 0.075, fy - 0.003, z0 + 0.08 + 0.02 * k), BZ, 0.0, 1))
    for sx in (-1, 1):
        p.append(tube("Antenna", [(sx * 0.05, 0.05, z0 + th), (sx * 0.2, 0.08, z0 + th + 0.32)], 0.006, ST, 8))
    p.append(sphere("AntBase", (0, 0.05, z0 + th + 0.01), (0.05, 0.04, 0.025), ST, 12, 6))
    p.append(rbox("Vcr", (0.44, 0.32, 0.095), (0, 0.0, 0.575 + 0.0475), VC, 0.012, 2))
    p.append(rbox("VcrSlot", (0.2, 0.01, 0.02), (-0.06, -0.165, 0.575 + 0.06), S("Plastic_HubVcrSlot", (0.05, 0.05, 0.05), "plastic", 0.4), 0.003, 1))
    body = finish(p, "HubTvCart", sharp_deg=50)
    c = Canvas(96, 24, (0.12, 0.11, 0.1))
    c.text_c("VHS", 30, 5, 2, (0.9, 0.62, 0.15))
    c.rect(60, 9, 76, 15, (0.2, 0.75, 0.25))
    lab = card("VcrPrint", 0.12, 0.03, (0.13, -0.161, 0.575 + 0.048), print_mat("Print_HubVcr", c))
    return join([body, lab], "HubTvCart")


# ------------------------------------------------------------------------------------------------- lobby

def board():
    FR = S("Wood_HubBoardFrame", CHOC, "wood", 0.5)
    W_, H_, D_ = 2.4, 1.3, 0.1
    body = finish([rbox("Frame", (W_, D_, H_), (0, -D_ / 2, H_ / 2), FR, 0.03, 3)], "HubBoard", sharp_deg=50)
    c = Canvas(480, 260, (0.08, 0.07, 0.06))
    c.rect(0, 0, 480, 44, C_AMBER)
    c.text_c("CONTRACTS", 240, 8, 4, C_CHOC)
    for r in range(7):
        y = 56 + r * 26
        for k in range(20):
            x = 11 + k * 23
            c.rect(x, y, x + 20, y + 22, (0.14, 0.13, 0.12))
            c.rect(x, y + 10, x + 20, y + 11, (0.04, 0.04, 0.04))
    msg = "NO CONTRACTS POSTED"
    for k, ch in enumerate(msg):
        if ch != " ":
            c.text(ch, 11 + k * 23 + 5, 56 + 2 * 26 + 4, 2, (0.96, 0.76, 0.3))
    c.rect(0, 242, 480, 248, C_ORANGE); c.rect(0, 248, 480, 254, C_GOLD); c.rect(0, 254, 480, 260, C_AVO)
    face = card("Face", W_ - 0.1, H_ - 0.1, (0, -D_ - 0.003, H_ / 2), print_mat("Print_HubBoardFace", c, sharp=True))
    return join([body, face], "HubBoard")


def reception():
    WD = S("Wood_HubReception", hx('#5A3A22'), "wood", 0.5)
    GR = S("Wood_HubReceptionGroove", hx('#2E1D12'), "wood", 0.6)
    LM = S("Laminate_HubReceptionTop", hx('#E3D5B4'), "laminate", 0.4)
    CH = S("Metal_HubBell", (0.85, 0.82, 0.7), "metal", 0.2, 1.0)
    L, D, H = 2.4, 0.75, 1.05
    fy = -D / 2
    p = [rbox("Front", (L, 0.06, H - 0.05), (0, fy + 0.03, (H - 0.05) / 2), WD, 0.015, 2),
         rbox("Back", (L, 0.03, 0.72), (0, D / 2 - 0.015, 0.36), WD, 0.01, 2),
         rbox("Top", (L + 0.06, 0.34, 0.045), (0, fy + 0.13, H - 0.022), LM, 0.012, 3),
         rbox("Work", (L - 0.1, D - 0.2, 0.035), (0, 0.06, 0.74), LM, 0.01, 2),
         rbox("Kick", (L - 0.04, 0.05, 0.09), (0, fy + 0.06, 0.045), S("Paint_HubReceptionKick", CHOC, "paint", 0.6), 0.008, 2)]
    for sx in (-1, 1):
        p.append(rbox("End", (0.05, D, H - 0.05), (sx * (L / 2 - 0.025), 0, (H - 0.05) / 2), WD, 0.015, 2))
    n = 14
    for k in range(1, n):
        p.append(rbox("Groove", (0.01, 0.008, 0.66), (-L / 2 + L * k / n, fy - 0.003, 0.12 + 0.33), GR, 0.0, 1))
    p += stripe("RecStripe", L - 0.02, 0.82, fy - 0.004)
    p.append(lathe("Bell", [(0.0, 0.0), (0.045, 0.0), (0.045, 0.01), (0.04, 0.03), (0.025, 0.055), (0.0, 0.062)], CH, 20, xf=M4(None, (0.7, fy + 0.13, H))))
    p.append(lathe("BellKnob", [(0.0, 0.0), (0.006, 0.0), (0.006, 0.015), (0.0, 0.018)], CH, 10, xf=M4(None, (0.7, fy + 0.13, H + 0.062))))
    return finish(p, "HubReception", sharp_deg=50)


def coffee_table():
    WD = S("Wood_HubCoffeeTable", hx('#7A4E2A'), "wood", 0.45)
    L, Wd, H = 1.1, 0.6, 0.42
    p = [rbox("Top", (L, Wd, 0.05), (0, 0, H - 0.025), WD, 0.018, 3),
         rbox("Shelf", (L - 0.14, Wd - 0.14, 0.03), (0, 0, 0.12), WD, 0.01, 2)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(rbox("Leg", (0.055, 0.055, H - 0.05), (sx * (L / 2 - 0.08), sy * (Wd / 2 - 0.08), (H - 0.05) / 2), WD, 0.014, 3))
    return finish(p, "HubCoffeeTable", sharp_deg=50)


def punch_clock():
    BG = S("Beige_HubTimeClock", hx('#CDBF9E'), "beige", 0.45)
    DK = S("Plastic_HubTimeClockDark", (0.16, 0.13, 0.11), "plastic", 0.4)
    W_, H_, D_ = 0.3, 0.42, 0.2
    p = [rbox("Body", (W_, D_, H_), (0, -D_ / 2, H_ / 2), BG, 0.035, 3),
         rbox("Slot", (0.16, 0.04, 0.02), (0, -D_ * 0.55, H_ + 0.004), DK, 0.006, 2),
         rbox("Hood", (W_ - 0.04, 0.03, 0.05), (0, -D_ - 0.012, 0.07), DK, 0.01, 2)]
    body = finish(p, "HubPunchClock", sharp_deg=50)
    c = Canvas(160, 160, (0.95, 0.93, 0.86))
    c.disc(80, 80, 78, C_CHOC)
    c.disc(80, 80, 70, (0.96, 0.94, 0.86))
    for i in range(12):
        a = math.radians(i * 30)
        x, y = 80 + math.sin(a) * 58, 80 - math.cos(a) * 58
        c.rect(x - 3, y - 3, x + 3, y + 3, C_CHOC)
    for (ang, ln, wd) in ((-50, 36, 4), (100, 54, 3)):           # 8:20, the hands
        a = math.radians(ang)
        for t in range(int(ln)):
            x, y = 80 + math.sin(a) * t, 80 - math.cos(a) * t
            c.rect(x - wd / 2, y - wd / 2, x + wd / 2, y + wd / 2, C_CHOC)
    c.disc(80, 80, 6, C_ORANGE)
    dial = card("Dial", 0.22, 0.22, (0, -D_ - 0.002, H_ * 0.6), print_mat("Print_HubTimeClockDial", c, sharp=True))
    c2 = Canvas(120, 30, C_CHOC)
    c2.text_c("IN - OUT", 60, 8, 2, (0.95, 0.75, 0.3))
    lab = card("InOut", 0.2, 0.05, (0, -D_ - 0.028, 0.072), print_mat("Print_HubTimeClockPlate", c2))
    return join([body, dial, lab], "HubPunchClock")


def card_rack():
    ST = S("Paint_HubCardRack", hx('#6E665A'), "paint", 0.45)
    CD = S("Board_HubTimeCard", hx('#E9DFC4'), "board", 0.8)
    W_, H_, D_ = 0.18, 0.86, 0.04
    p = [rbox("Back", (W_, 0.012, H_), (0, -0.006, H_ / 2), ST, 0.006, 2)]
    for k in range(8):
        z = 0.05 + k * 0.1
        p.append(rbox("Pocket", (W_ - 0.01, D_, 0.055), (0, -D_ / 2 - 0.006, z + 0.0275), ST, 0.005, 2))
        if k not in (2, 5):
            p.append(rbox("Card", (W_ - 0.04, 0.004, 0.09), (random.Random(k).uniform(-0.01, 0.01), -D_ / 2 - 0.006, z + 0.06), CD, 0.0, 1))
    return finish(p, "HubCardRack", sharp_deg=50)


def coffee_machine():
    BD = S("Paint_HubCoffeeBody", CHOC, "paint", 0.45)
    PN = S("Paint_HubCoffeePanel", ORANGE, "paint", 0.4)
    RC = S("Plastic_HubCoffeeRecess", (0.08, 0.07, 0.06), "plastic", 0.5)
    BT = S("Plastic_HubCoffeeButton", AMBER, "plastic", 0.35)
    CH = S("Metal_HubCoffeeTrim", (0.75, 0.73, 0.68), "metal", 0.3, 1.0)
    W_, D_, H_ = 0.75, 0.72, 1.85
    fy = -D_ / 2
    p = [rbox("Body", (W_, D_, H_ - 0.06), (0, 0, 0.06 + (H_ - 0.06) / 2), BD, 0.03, 3),
         rbox("Foot", (W_ - 0.06, D_ - 0.06, 0.06), (0, 0, 0.03), S("Rubber_HubCoffeeFoot", (0.1, 0.1, 0.1), "rubber", 0.6), 0.01, 2),
         rbox("Panel", (W_ - 0.1, 0.02, 0.8), (0, fy - 0.008, 1.3), PN, 0.02, 3),
         rbox("Alcove", (0.3, 0.06, 0.32), (0, fy + 0.01, 0.5), RC, 0.02, 3),
         rbox("Drip", (0.28, 0.08, 0.02), (0, fy - 0.01, 0.35), CH, 0.006, 2),
         rbox("Coin", (0.07, 0.025, 0.12), (W_ / 2 - 0.12, fy - 0.01, 0.95), CH, 0.008, 2)]
    for k in range(4):
        p.append(rbox("Button", (0.06, 0.025, 0.04), (-W_ / 2 + 0.14 + k * 0.1, fy - 0.012, 0.86), BT, 0.008, 2))
    body = finish(p, "HubCoffee", sharp_deg=50)
    c = Canvas(200, 200, C_ORANGE)
    c.text_c("COFFEE", 100, 14, 5, C_CHOC)
    c.rect(66, 70, 134, 150, C_CREAM)                 # a cup
    c.rect(134, 88, 152, 96, C_CREAM); c.rect(146, 88, 152, 130, C_CREAM); c.rect(134, 124, 152, 130, C_CREAM)
    c.rect(60, 150, 140, 158, C_CHOC)
    for k, x in enumerate((84, 100, 116)):           # steam
        for t in range(14):
            c.rect(x + int(4 * math.sin(t * 0.8 + k)), 64 - t * 3, x + 4 + int(4 * math.sin(t * 0.8 + k)), 66 - t * 3, C_CREAM)
    c.text_c("HOT - 25", 100, 172, 2, C_CHOC)
    art = card("Art", W_ - 0.16, 0.74, (0, fy - 0.021, 1.3), print_mat("Print_HubCoffee", c, sharp=True))
    return join([body, art], "HubCoffee")


def plant():
    PT = S("Paint_HubPlantPot", hx('#A4552E'), "paint", 0.6)
    SO = S("Stone_HubPlantSoil", hx('#3A2A1E'), "stone", 0.9)
    SM = S("Wood_HubPlantStem", hx('#6B5230'), "wood", 0.8)
    LF = S("Board_HubPlantLeaf", hx('#8C7A3C'), "board", 0.8)
    p = [lathe("Pot", [(0.0, 0.0), (0.17, 0.0), (0.2, 0.02), (0.24, 0.42), (0.26, 0.44), (0.26, 0.48), (0.22, 0.48), (0.21, 0.45), (0.0, 0.45)], PT, 24),
         lathe("Soil", [(0.0, 0.0), (0.215, 0.0), (0.215, 0.012), (0.0, 0.02)], SO, 20, xf=M4(None, (0, 0, 0.452)))]
    rng = random.Random(5)
    for k in range(5):
        a = k / 5 * math.tau + rng.uniform(-0.3, 0.3)
        lean = rng.uniform(0.15, 0.4)
        top = Vector((math.cos(a) * lean, math.sin(a) * lean, 0.45 + rng.uniform(0.5, 0.85)))
        mid = Vector((math.cos(a) * lean * 0.4, math.sin(a) * lean * 0.4, 0.45 + (top.z - 0.45) * 0.6))
        p.append(tube("Stem", catmull([(0, 0, 0.44), tuple(mid), tuple(top)], 4), 0.009, SM, 6))
        for j in range(3):                            # dried leaves drooping off the stem
            t = 0.45 + j * 0.25
            at = Vector((0, 0, 0.44)).lerp(top, t)
            off = Vector((math.cos(a), math.sin(a), 0.0)) * 0.07
            R = rot('Z', math.degrees(a) + rng.uniform(-40, 40)) @ rot('Y', rng.uniform(35, 70))
            p.append(sphere("Leaf", tuple(at + off), (0.09, 0.035, 0.01), LF, 10, 6, rot=R))   # a dried, drooping oval
    return finish(p, "HubPlant", sharp_deg=50)


def poster(kind):
    FR = S("Wood_HubPosterFrame", CHOC, "wood", 0.5)
    W_, H_, D_ = 0.62, 0.86, 0.03
    body = finish([rbox("Frame", (W_, D_, H_), (0, -D_ / 2, H_ / 2), FR, 0.01, 2)], "HubPoster" + kind, sharp_deg=50)
    c = Canvas(224, 312, C_AMBER)
    if kind == "A":
        c.rect(0, 0, 224, 312, C_AMBER)
        for i, t in enumerate(("WORK HARD.", "WORK FAST.", "BE BACK BY", "CLOSING.")):
            c.text_c(t, 112, 40 + i * 44, 3, C_CHOC)
    elif kind == "B":
        c.rect(0, 0, 224, 312, C_CREAM)
        c.rect(62, 40, 162, 140, C_ORANGE)                       # a box, and more room behind it
        c.frame(52, 30, 172, 150, 4, C_CHOC)
        for i, t in enumerate(("THERE'S", "ALWAYS", "MORE ROOM.")):
            c.text_c(t, 112, 170 + i * 34, 3, C_CHOC)
    else:
        c.rect(0, 0, 224, 312, C_AVO)
        c.rect(70, 46, 150, 96, C_CHOC); c.rect(150, 56, 176, 86, C_CHOC); c.disc(100, 71, 16, C_AMBER)   # a camcorder
        c.disc(80, 54, 4, (0.85, 0.15, 0.1))
        for i, t in enumerate(("IF IT ISN'T", "ON TAPE,", "IT DIDN'T", "HAPPEN.")):
            c.text_c(t, 112, 124 + i * 36, 3, C_CREAM)
    c.rect(0, 280, 224, 290, C_ORANGE); c.rect(0, 290, 224, 300, C_GOLD); c.rect(0, 300, 224, 312, C_AVO)
    c.text_c("OVERFLOW", 112, 8, 2, C_CHOC if kind != "C" else C_CREAM)
    # Sun-faded: every colour a third of the way to the paper's cream.
    c.px = [[q * 0.67 + w * 0.33 for q, w in zip(px[:3], (0.93, 0.89, 0.8))] + [1.0] for px in c.px]
    art = card("Art", W_ - 0.06, H_ - 0.06, (0, -D_ - 0.002, H_ / 2), print_mat("Print_HubPoster" + kind, c, sharp=True))
    return join([body, art], "HubPoster" + kind)


# ------------------------------------------------------------------------------------------------- round 2 (2026-11-07)

def arrow(c, cx, cy, s, direction, col):
    """A chunky arrow painted on a canvas: 'L', 'R', or 'U' (straight ahead), centred at (cx, cy), about 2s across."""
    if direction in ("L", "R"):
        sx = -1 if direction == "L" else 1
        x0, x1 = (cx - s * 0.2, cx + s) if sx < 0 else (cx - s, cx + s * 0.2)
        c.rect(x0, cy - s * 0.22, x1, cy + s * 0.22, col)
        for k in range(int(s * 0.9)):
            h = s * 0.85 - k
            x = cx - s * 0.2 - k if sx < 0 else cx + s * 0.2 + k
            if h > 0:
                c.rect(x - 1, cy - h, x + 1, cy + h, col)
    else:
        c.rect(cx - s * 0.22, cy - s * 0.2, cx + s * 0.22, cy + s, col)
        for k in range(int(s * 0.9)):
            w = s * 0.85 - k
            y = cy - s * 0.2 - k
            if w > 0:
                c.rect(cx - w, y - 1, cx + w, y + 1, col)


def logo():
    PL = S("Wood_HubLogoPlate", CHOC, "wood", 0.5)
    LT = S("Paint_HubLogoLetter", AMBER, "paint", 0.35)
    W_, H_, D_ = 2.6, 0.5, 0.05
    p = [rbox("Plate", (W_, D_, H_), (0, -D_ / 2, H_ / 2), PL, 0.02, 3)]
    p += stripe("LogoStripe", W_ - 0.06, 0.03, -D_ - 0.004, h=0.025)
    # Chunky block capitals from the 5 x 7 glyphs: each dot a rounded block standing proud of the plate.
    word, dot = "OVERFLOW", 0.042
    adv = 6 * dot
    x0 = -(len(word) * adv - dot) / 2
    for k, ch in enumerate(word):
        g = GLYPHS[ch]
        for row in range(7):
            for col in range(5):
                if g[row * 5 + col] == "1":
                    x = x0 + k * adv + (col + 0.5) * dot
                    z = 0.12 + (6 - row + 0.5) * dot
                    p.append(rbox("Dot", (dot * 1.02, 0.03, dot * 1.02), (x, -D_ - 0.015, z), LT, 0.006, 2))
    return finish(p, "HubLogo", sharp_deg=50)


def sign(name, lines, arrow_dir):
    BD = S("Wood_HubSignBoard", CHOC, "wood", 0.5)
    W_, H_, D_ = 0.62, 0.24, 0.03
    body = finish([rbox("Board", (W_, D_, H_), (0, -D_ / 2, H_ / 2), BD, 0.012, 2)], name, sharp_deg=50)
    c = Canvas(248, 96, C_CHOC)
    c.rect(0, 84, 248, 88, C_ORANGE); c.rect(0, 88, 248, 92, C_GOLD); c.rect(0, 92, 248, 96, C_AVO)
    tx = 16 if arrow_dir in ("R", "U") else 76
    for i, t in enumerate(lines):
        c.text(t, tx, (12 + i * 34) if len(lines) > 1 else 30, 3, C_AMBER)
    ax = 216 if arrow_dir in ("R", "U") else 34
    arrow(c, ax, 42, 22, arrow_dir, (0.96, 0.92, 0.82))
    art = card("Art", W_ - 0.03, H_ - 0.03, (0, -D_ - 0.002, H_ / 2), print_mat("Print_" + name, c, sharp=True))
    return join([body, art], name)


def pay_window():
    FR = S("Wood_HubPayFrame", hx('#5A3A22'), "wood", 0.5)
    SH = S("Metal_HubPayShutter", (0.6, 0.6, 0.58), "galv", 0.4, 0.6)
    LD = S("Laminate_HubPayLedge", hx('#E3D5B4'), "laminate", 0.4)
    GR = S("Plastic_HubPayGrille", (0.15, 0.14, 0.13), "plastic", 0.5)
    W_, sill, top = 1.2, 0.95, 1.85
    p = [rbox("Ledge", (W_ + 0.2, 0.32, 0.05), (0, -0.1, sill - 0.025), LD, 0.012, 3),
         rbox("FrameL", (0.08, 0.06, top - sill + 0.1), (-W_ / 2 - 0.04, -0.03, (sill + top) / 2), FR, 0.012, 2),
         rbox("FrameR", (0.08, 0.06, top - sill + 0.1), (W_ / 2 + 0.04, -0.03, (sill + top) / 2), FR, 0.012, 2),
         rbox("FrameT", (W_ + 0.16, 0.06, 0.1), (0, -0.03, top + 0.05), FR, 0.012, 2),
         rbox("Box", (W_ + 0.1, 0.12, 0.16), (0, 0.02, top - 0.04), SH, 0.02, 2)]
    n = 14
    for k in range(n):                                   # the roller shutter, down
        z = sill + (top - sill - 0.08) * (k + 0.5) / n
        p.append(rbox("Slat", (W_, 0.02, (top - sill - 0.08) / n - 0.006), (0, 0.04, z), SH, 0.004, 1))
    p.append(lathe("Grille", [(0.0, 0.0), (0.06, 0.0), (0.06, 0.012), (0.0, 0.014)], GR, 20, xf=M4(rot('X', 90), (0.0, -0.002, top + 0.28))))
    body = finish(p, "HubPayWindow", sharp_deg=50)
    c = Canvas(120, 48, (0.95, 0.92, 0.84))
    c.frame(0, 0, 120, 48, 3, (0.72, 0.16, 0.1))
    c.text_c("CLOSED", 60, 14, 3, (0.72, 0.16, 0.1))
    closed = card("Closed", 0.24, 0.096, (0.15, 0.028, sill + 0.35), print_mat("Print_HubPayClosed", c))
    c2 = Canvas(96, 32, C_CHOC)
    c2.text_c("PAY", 48, 9, 2, C_AMBER)
    plate = card("Pay", 0.24, 0.08, (0, -0.064, top + 0.06), print_mat("Print_HubPayPlate", c2))
    return join([body, closed, plate], "HubPayWindow")


def blinds():
    FR = S("Wood_HubBlindsFrame", hx('#5A3A22'), "wood", 0.5)
    SL = S("Plastic_HubBlindsSlat", hx('#D6C9A0'), "plastic", 0.5)
    W_, sill, top = 1.8, 1.0, 2.0
    p = [rbox("Sill", (W_ + 0.12, 0.2, 0.04), (0, -0.06, sill - 0.02), FR, 0.01, 2),
         rbox("FrameL", (0.06, 0.06, top - sill), (-W_ / 2 - 0.03, -0.03, (sill + top) / 2), FR, 0.01, 2),
         rbox("FrameR", (0.06, 0.06, top - sill), (W_ / 2 + 0.03, -0.03, (sill + top) / 2), FR, 0.01, 2),
         rbox("FrameT", (W_ + 0.12, 0.06, 0.06), (0, -0.03, top + 0.03), FR, 0.01, 2),
         rbox("Head", (W_ - 0.02, 0.05, 0.05), (0, 0.04, top - 0.03), SL, 0.01, 2)]
    n = 22
    rng = random.Random(3)
    for k in range(n):                                   # slats tilted half open; two bent where someone peeked
        z = sill + 0.03 + (top - sill - 0.09) * k / (n - 1)
        sl = rbox("Slat", (W_ - 0.04, 0.045, 0.006), (0, 0, 0), SL, 0.002, 1)
        tilt = 22 + rng.uniform(-4, 4) - (20 if k in (11, 12) else 0)      # (0: flat, open) half open; two pushed flat where someone peeked
        place(sl, rot('X', tilt), (0, 0.035, z))
        p.append(sl)
    for x in (-W_ / 2 + 0.12, W_ / 2 - 0.12):
        p.append(rbox("Ladder", (0.008, 0.008, top - sill - 0.04), (x, 0.012, (sill + top) / 2), S("Fabric_HubBlindsTape", hx('#BFB28A'), "fabric", 0.8), 0.0, 1))
    return finish(p, "HubBlinds", sharp_deg=50)


def door_locked():
    FR = S("Wood_HubDoorFrame", CHOC, "wood", 0.5)
    LF = S("Wood_HubDoorLeaf", hx('#7A5232'), "wood", 0.45)
    BR = S("Metal_HubDoorBrass", (0.78, 0.62, 0.3), "metal", 0.3, 1.0)
    W_, H_ = 0.95, 2.1
    p = [rbox("JambL", (0.08, 0.05, H_ + 0.06), (-W_ / 2 - 0.04, -0.025, (H_ + 0.06) / 2), FR, 0.012, 2),
         rbox("JambR", (0.08, 0.05, H_ + 0.06), (W_ / 2 + 0.04, -0.025, (H_ + 0.06) / 2), FR, 0.012, 2),
         rbox("Head", (W_ + 0.16, 0.05, 0.08), (0, -0.025, H_ + 0.04), FR, 0.012, 2),
         rbox("Leaf", (W_, 0.04, H_), (0, -0.02, H_ / 2), LF, 0.01, 2)]
    for (z0, z1) in ((0.15, 0.95), (1.1, 1.95)):        # two raised panels
        p.append(rbox("Panel", (W_ - 0.22, 0.012, z1 - z0), (0, -0.045, (z0 + z1) / 2), LF, 0.02, 3))
    p.append(sphere("Knob", (W_ / 2 - 0.09, -0.08, 1.0), (0.032, 0.03, 0.032), BR, 14, 8))
    p.append(rbox("Rose", (0.06, 0.012, 0.06), (W_ / 2 - 0.09, -0.046, 1.0), BR, 0.008, 2))
    p.append(rbox("Keyhole", (0.03, 0.01, 0.07), (W_ / 2 - 0.09, -0.046, 0.92), BR, 0.006, 2))
    body = finish(p, "HubDoorLocked", sharp_deg=50)
    c = Canvas(144, 40, (0.78, 0.62, 0.3))
    c.text_c("MANAGER", 72, 12, 2, C_CHOC)
    plate = card("Name", 0.26, 0.072, (0, -0.052, 1.55), print_mat("Print_HubDoorName", c))
    c2 = Canvas(120, 30, (0.85, 0.82, 0.74))
    c2.text_c("PRIVATE", 60, 8, 2, (0.6, 0.12, 0.08))
    priv = card("Private", 0.18, 0.045, (0, -0.052, 1.42), print_mat("Print_HubDoorPrivate", c2))
    return join([body, plate, priv], "HubDoorLocked")


def calendar():
    PP = S("Board_HubCalendar", hx('#ECE3CC'), "board", 0.8)
    body = finish([rbox("Back", (0.32, 0.006, 0.46), (0, -0.003, 0.23), PP, 0.0, 1)], "HubCalendar", sharp_deg=50)
    c = Canvas(128, 184, (0.94, 0.91, 0.82))
    c.rect(0, 0, 128, 60, C_ORANGE)
    c.text_c("OVERFLOW", 64, 8, 2, C_CHOC)
    c.disc(64, 40, 12, C_GOLD)
    c.text_c("MARCH 1979", 64, 68, 2, C_CHOC)
    for r in range(5):
        for k in range(7):
            x, y = 8 + k * 16, 88 + r * 18
            c.frame(x, y, x + 15, y + 17, 1, (0.6, 0.55, 0.48))
    for r, k in ((0, 0), (0, 1), (0, 2), (0, 3), (1, 0), (1, 1), (1, 2)):    # crossed off, day by day, then nothing
        x, y = 8 + k * 16, 88 + r * 18
        for t in range(14):
            c.rect(x + t, y + t, x + t + 2, y + t + 2, (0.72, 0.16, 0.1))
    art = card("Art", 0.31, 0.445, (0, -0.0065, 0.23), print_mat("Print_HubCalendar", c, sharp=True))
    return join([body, art], "HubCalendar")


def dust_sheet(name, w, d, h):
    CL = S("Fabric_HubDustSheet", hx('#E2D8C2'), "fabric", 0.9)
    # A soft lump: the sheet over the top, slumping wider to a skirt that pools on the floor.
    p = [rbox("Drape", (w, d, h), (0, 0, h / 2), CL, min(0.14, h * 0.3), 5),
         rbox("Slump", (w + 0.05, d + 0.05, h * 0.45), (0, 0, h * 0.225), CL, min(0.1, h * 0.2), 4),
         rbox("Skirt", (w + 0.12, d + 0.12, 0.05), (0, 0, 0.025), CL, 0.024, 3)]
    return finish(p, name, sharp_deg=50)


def speaker():
    RM = S("Paint_HubSpeakerRim", hx('#E6DFCC'), "paint", 0.45)
    GR = S("Paint_HubSpeakerGrille", hx('#9B9483'), "paint", 0.6)
    p = [lathe("Rim", [(0.0, 0.0), (0.16, 0.0), (0.16, -0.02), (0.14, -0.035), (0.0, -0.035)], RM, 28),
         lathe("Grille", [(0.0, -0.034), (0.12, -0.034), (0.12, -0.038), (0.0, -0.042)], GR, 24)]
    for k in range(5):
        r = 0.02 + k * 0.02
        p.append(lathe("Ring", [(r, -0.039), (r + 0.006, -0.039), (r + 0.006, -0.043), (r, -0.043)], RM, 24))
    return finish(p, "HubSpeaker", sharp_deg=50)


def clock_stopped():
    RM = S("Plastic_HubClockRim", CHOC, "plastic", 0.4)
    body = finish([lathe("Case", [(0.0, 0.0), (0.17, 0.0), (0.17, 0.045), (0.155, 0.055), (0.0, 0.055)], RM, 32, xf=M4(rot('X', 90), (0, 0.0, 0.17)))],
                  "HubClockStopped", sharp_deg=50)
    c = Canvas(160, 160, C_CHOC)                          # (round: the corners the rim's colour)
    c.disc(80, 80, 74, (0.95, 0.93, 0.86))
    for i in range(12):
        a = math.radians(i * 30)
        x, y = 80 + math.sin(a) * 62, 80 - math.cos(a) * 62
        c.rect(x - 3, y - 3, x + 3, y + 3, C_CHOC)
    for (ang, ln, wd) in ((150, 38, 5), (0, 58, 3)):    # five o'clock, closing time: it stopped there
        a = math.radians(ang)
        for t in range(int(ln)):
            x, y = 80 + math.sin(a) * t, 80 - math.cos(a) * t
            c.rect(x - wd / 2, y - wd / 2, x + wd / 2, y + wd / 2, C_CHOC)
    c.disc(80, 80, 6, C_ORANGE)
    face = card("Face", 0.27, 0.27, (0, -0.056, 0.17), print_mat("Print_HubClockFace", c, sharp=True))
    return join([body, face], "HubClockStopped")


def out_of_order():
    TP = S("Plastic_HubTape", hx('#D8CFA8'), "plastic", 0.4)
    body = finish([rbox("TapeL", (0.1, 0.004, 0.03), (-0.1, -0.004, 0.2), TP, 0.0, 1),
                   rbox("TapeR", (0.1, 0.004, 0.03), (0.1, -0.004, 0.2), TP, 0.0, 1)], "HubOutOfOrder", sharp_deg=50)
    c = Canvas(160, 112, (0.96, 0.95, 0.9))
    c.text_c("OUT OF", 80, 18, 3, (0.1, 0.1, 0.1))
    c.text_c("ORDER", 80, 52, 3, (0.1, 0.1, 0.1))
    c.lines(20, 88, 140, 2, 8, (0.45, 0.45, 0.5), seed=4)
    art = card("Paper", 0.3, 0.21, (0, -0.002, 0.105), print_mat("Print_HubOutOfOrder", c), roll=4)
    return join([body, art], "HubOutOfOrder")


def card_pile():
    CD = S("Board_HubTimeCardPile", hx('#E9DFC4'), "board", 0.8)
    BD = S("Fabric_HubRubberBand", hx('#C4321E'), "fabric", 0.5)
    rng = random.Random(9)
    p = []
    for k in range(9):
        cd = rbox("Card", (0.09, 0.18, 0.003), (0, 0, 0), CD, 0.0, 1)
        place(cd, rot('Z', rng.uniform(-35, 35)), (rng.uniform(-0.06, 0.06), rng.uniform(-0.05, 0.05), 0.003 + k * 0.004))
        p.append(cd)
    p.append(rbox("Bundle", (0.095, 0.185, 0.03), (0.16, 0.02, 0.015), CD, 0.004, 1))
    p.append(rbox("Band", (0.1, 0.01, 0.034), (0.16, 0.02, 0.015), BD, 0.002, 1))
    return finish(p, "HubCardPile", sharp_deg=50)


def chair_stack():
    SH = S("Plastic_HubStackSeat", ORANGE, "plastic", 0.45)
    FR = S("Paint_HubStackFrame", hx('#2B2420'), "paint", 0.45)
    p = []
    for k in range(5):
        z = 0.44 + k * 0.08
        y = -k * 0.012
        p.append(rbox("Seat", (0.46, 0.44, 0.03), (0, y, z), SH, 0.012, 2))
        p.append(rbox("Back", (0.44, 0.03, 0.3), (0, y + 0.22, z + 0.28), SH, 0.012, 2))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(rbox("Leg", (0.022, 0.022, 0.46), (sx * 0.2, sy * 0.19, 0.23), FR, 0.008, 2))
        p.append(rbox("Rail", (0.022, 0.022, 0.42), (sx * 0.2, 0.2, 0.78), FR, 0.008, 2))
    return finish(p, "HubChairStack", sharp_deg=50)


def rubber_mat(name, w, d, colour):
    RB = S("Rubber_" + name, colour, "rubber", 0.85)
    p = [rbox("Mat", (w, d, 0.012), (0, 0, 0.006), RB, 0.004, 2)]
    for k in range(int(w / 0.06)):                         # ribs
        p.append(rbox("Rib", (0.02, d - 0.08, 0.006), (-w / 2 + 0.04 + k * 0.06, 0, 0.014), RB, 0.0, 1))
    return finish(p, name, sharp_deg=50)


def sofa70():
    FB = S("Fabric_HubSofa", hx('#C98A2B'), "fabric", 0.85)
    CU = S("Fabric_HubSofaCushion", hx('#D9661F'), "fabric", 0.85)
    WD = S("Wood_HubSofaPlinth", CHOC, "wood", 0.5)
    L, D, H = 2.1, 0.9, 0.78
    p = [rbox("Plinth", (L - 0.1, D - 0.1, 0.1), (0, 0, 0.05), WD, 0.015, 2),
         rbox("Base", (L, D, 0.28), (0, 0, 0.1 + 0.14), FB, 0.06, 4),
         rbox("Back", (L, 0.24, H - 0.38), (0, D / 2 - 0.12, 0.38 + (H - 0.38) / 2), FB, 0.08, 4)]
    for sx in (-1, 1):
        p.append(rbox("Arm", (0.22, D, 0.32), (sx * (L / 2 - 0.11), 0, 0.38 + 0.16), FB, 0.08, 4))
    for k in range(3):                                     # three fat seat cushions and three back cushions
        x = -L / 2 + 0.22 + (L - 0.44) * (k + 0.5) / 3
        p.append(rbox("Seat", ((L - 0.44) / 3 - 0.02, D - 0.3, 0.16), (x, -0.08, 0.38 + 0.08), CU, 0.06, 4))
        p.append(rbox("BackCushion", ((L - 0.44) / 3 - 0.02, 0.16, 0.34), (x, D / 2 - 0.3, 0.38 + 0.3), CU, 0.07, 4))
    return finish(p, "HubSofa", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- round 3 (2026-11-07)

CHUTE_SILL, CHUTE_TOP, CHUTE_W, CHUTE_DEEP = 0.42, 0.92, 0.6, 1.7


def chute():
    """The return chute behind a cot (the owner: "conveyered onto our cots"): a dark tunnel through the wall with a rubber
    belt on rollers, an amber steel mouth, a RETURNS plate. Origin on the room's wall face at floor level; the tunnel runs
    back into the wall (+Y), its mouth toward the room (-Y)."""
    DK = S("Paint_HubChuteInside", hx('#1A1411'), "paint", 0.8)
    BT = S("Rubber_HubChuteBelt", hx('#1E1A17'), "rubber", 0.9)
    RL = S("Metal_HubChuteRoller", (0.55, 0.54, 0.5), "metal", 0.4, 0.8)
    FR = S("Paint_HubChuteFrame", AMBER, "paint", 0.45)
    hw = CHUTE_W / 2
    p = [rbox("SideL", (0.04, CHUTE_DEEP, CHUTE_TOP - CHUTE_SILL + 0.08), (-hw - 0.02, CHUTE_DEEP / 2, (CHUTE_SILL + CHUTE_TOP) / 2), DK, 0.0, 1),
         rbox("SideR", (0.04, CHUTE_DEEP, CHUTE_TOP - CHUTE_SILL + 0.08), (hw + 0.02, CHUTE_DEEP / 2, (CHUTE_SILL + CHUTE_TOP) / 2), DK, 0.0, 1),
         rbox("Roof", (CHUTE_W + 0.08, CHUTE_DEEP, 0.04), (0, CHUTE_DEEP / 2, CHUTE_TOP + 0.02), DK, 0.0, 1),
         rbox("End", (CHUTE_W + 0.08, 0.04, CHUTE_TOP - CHUTE_SILL + 0.08), (0, CHUTE_DEEP + 0.02, (CHUTE_SILL + CHUTE_TOP) / 2), DK, 0.0, 1),
         rbox("Bed", (CHUTE_W, CHUTE_DEEP + 0.05, 0.06), (0, CHUTE_DEEP / 2 - 0.025, CHUTE_SILL - 0.03), DK, 0.0, 1),
         rbox("Belt", (CHUTE_W - 0.06, CHUTE_DEEP + 0.03, 0.012), (0, CHUTE_DEEP / 2 - 0.035, CHUTE_SILL + 0.006), BT, 0.003, 1)]
    for y in (-0.05, CHUTE_DEEP - 0.04):
        p.append(lathe("Roller", [(0.0, 0.0), (0.03, 0.0), (0.03, CHUTE_W - 0.04), (0.0, CHUTE_W - 0.04)], RL, 16,
                       xf=M4(rot('Y', 90), (-(CHUTE_W - 0.04) / 2, y, CHUTE_SILL - 0.02))))
    # The mouth: an amber steel frame round the hole on the room's side, chunky, with a lip over the cot's head.
    p += [rbox("FrameT", (CHUTE_W + 0.2, 0.05, 0.08), (0, -0.025, CHUTE_TOP + 0.04), FR, 0.015, 2),
          rbox("FrameL", (0.1, 0.05, CHUTE_TOP - CHUTE_SILL + 0.16), (-hw - 0.05, -0.025, (CHUTE_SILL + CHUTE_TOP) / 2), FR, 0.015, 2),
          rbox("FrameR", (0.1, 0.05, CHUTE_TOP - CHUTE_SILL + 0.16), (hw + 0.05, -0.025, (CHUTE_SILL + CHUTE_TOP) / 2), FR, 0.015, 2),
          rbox("Lip", (CHUTE_W + 0.2, 0.08, 0.04), (0, -0.04, CHUTE_SILL - 0.04), FR, 0.012, 2)]
    body = finish(p, "HubChute", sharp_deg=50)
    c = Canvas(128, 32, C_CHOC)
    c.text_c("RETURNS", 64, 9, 2, C_AMBER)
    plate = card("Plate", 0.2, 0.05, (0, -0.052, CHUTE_TOP + 0.04), print_mat("Print_HubChutePlate", c))
    return join([body, plate], "HubChute")


def chute_shutter():
    """The chute's shutter: a ribbed steel plate, origin at its bottom middle; the game slides it up into the wall to open."""
    SH = S("Metal_HubChuteShutter", (0.58, 0.57, 0.53), "galv", 0.4, 0.6)
    w, h = CHUTE_W + 0.04, CHUTE_TOP - CHUTE_SILL + 0.02
    p = [rbox("Plate", (w, 0.016, h), (0, 0, h / 2), SH, 0.004, 1)]
    for k in range(5):
        p.append(rbox("Rib", (w - 0.04, 0.01, 0.018), (0, -0.012, 0.06 + k * (h - 0.12) / 4), SH, 0.003, 1))
    p.append(rbox("Grip", (0.12, 0.02, 0.025), (0, -0.02, 0.05), S("Plastic_HubChuteGrip", C_CHOC, "plastic", 0.4), 0.006, 2))
    return finish(p, "HubChuteShutter", sharp_deg=50)


def lift_dial():
    """The floor dial over a lift's doors: a brass half-moon, HUB on the left, LEVEL 0 on the right (the game swings its
    needle). Origin on the wall at the pivot's foot; the needle's pivot is 0.05 up, 0.03 out."""
    BR = S("Metal_HubDialBrass", (0.76, 0.6, 0.3), "metal", 0.35, 1.0)
    body = finish([rbox("Plate", (0.52, 0.025, 0.3), (0, -0.0125, 0.13), BR, 0.012, 2),
                   lathe("Hub", [(0.0, 0.0), (0.022, 0.0), (0.022, 0.012), (0.0, 0.015)], BR, 16, xf=M4(rot('X', 90), (0, -0.025, 0.05)))],
                  "HubLiftDial", sharp_deg=50)
    c = Canvas(208, 112, (0.95, 0.9, 0.76))
    for i in range(11):                                   # ticks round the arc
        a = math.radians(-60 + i * 12)
        for t in range(10 if i % 5 == 0 else 6):
            r = 88 - t
            c.rect(104 + math.sin(a) * r - 1, 92 - math.cos(a) * r - 1, 104 + math.sin(a) * r + 1, 92 - math.cos(a) * r + 1, C_CHOC)
    c.text("HUB", 12, 70, 2, C_CHOC)
    c.text("L0", 172, 70, 2, C_CHOC)
    c.text_c("OVERFLOW", 104, 100, 1, C_ORANGE)
    face = card("Face", 0.48, 0.26, (0, -0.0265, 0.13), print_mat("Print_HubDialFace", c, sharp=True))
    return join([body, face], "HubLiftDial")


def hub_map():
    """YOU ARE HERE: the hub's plan on a board, a little out of date (a cafeteria that is not there). Origin on the wall at
    its foot."""
    FR = S("Wood_HubMapFrame", CHOC, "wood", 0.5)
    W_, H_ = 0.9, 0.66
    body = finish([rbox("Frame", (W_, 0.03, H_), (0, -0.015, H_ / 2), FR, 0.012, 2)], "HubMap", sharp_deg=50)
    c = Canvas(288, 208, (0.93, 0.89, 0.8))
    c.rect(0, 0, 288, 26, C_AMBER)
    c.text_c("YOU ARE HERE", 144, 6, 2, C_CHOC)
    s, ox, oy = 9, 48, 196                                 # 9 px a cell; y up the board
    rooms = [((0, 3, 4, 4), "AID", (0.62, 0.78, 0.66)), ((0, 7, 4, 5), "LOCK", (0.6, 0.66, 0.36)), ((2, 12, 6, 6), "TRAIN", (0.9, 0.6, 0.4)),
             ((4, 6, 12, 6), "LOBBY", (0.95, 0.8, 0.45)), ((6, 3, 7, 3), "", (0.85, 0.8, 0.7)), ((8, 0, 3, 3), "LIFT", (0.6, 0.6, 0.62)),
             ((8, 12, 4, 3), "PAY", (0.7, 0.66, 0.6)), ((12, 12, 4, 4), "MGR", (0.75, 0.55, 0.45)), ((16, 7, 2, 3), "", (0.8, 0.7, 0.55)),
             ((13, 0, 5, 3), "CAFE", (0.88, 0.86, 0.8))]          # (the cafeteria: walled up long ago)
    for (x, z, w, d), label, col in rooms:
        x0, y0, x1, y1 = ox + x * s, oy - (z + d) * s, ox + (x + w) * s, oy - z * s
        c.rect(x0, y0, x1, y1, col)
        c.frame(x0, y0, x1, y1, 2, C_CHOC)
        if label:
            c.text_c(label, (x0 + x1) / 2, (y0 + y1) / 2 - 3, 1, C_CHOC)
    c.disc(ox + 11.4 * s, oy - 4.6 * s, 6, (0.85, 0.12, 0.08))     # the red dot: the lift hall
    art = card("Art", W_ - 0.06, H_ - 0.06, (0, -0.032, H_ / 2), print_mat("Print_HubMap", c, sharp=True))
    return join([body, art], "HubMap")


def desk():
    """Level 0's office desk in the hub's style (OfficeChunkBuilder.Desk in the Overflow look): a walnut top with a fat
    rounded edge, a pedestal of three drawers with chunky pulls, a modesty panel with the company stripe, a slab end.
    1.6 x 0.75 x 0.76 m (the game scales it to the plan's desk)."""
    WD = S("Wood_HubDesk", hx('#7A4E2A'), "wood", 0.45)
    DK = S("Wood_HubDeskDark", hx('#4A2E1A'), "wood", 0.5)
    LM = S("Laminate_HubDeskTop", hx('#E3D5B4'), "laminate", 0.4)
    MT = S("Metal_HubDeskPull", (0.82, 0.78, 0.66), "metal", 0.3, 1.0)
    L, D, H = 1.6, 0.75, 0.76
    p = [rbox("Top", (L, D, 0.05), (0, 0, H - 0.025), WD, 0.02, 3),
         rbox("Inlay", (L - 0.12, D - 0.12, 0.004), (0, 0, H + 0.001), LM, 0.0, 1),
         rbox("End", (0.06, D - 0.06, H - 0.05), (-L / 2 + 0.05, 0, (H - 0.05) / 2), WD, 0.02, 3),
         rbox("Pedestal", (0.44, D - 0.06, H - 0.05), (L / 2 - 0.24, 0, (H - 0.05) / 2), WD, 0.02, 3),
         rbox("Modesty", (L - 0.5, 0.04, 0.42), (-0.2, D / 2 - 0.06, H - 0.26), DK, 0.012, 2),
         rbox("Kick", (0.42, D - 0.1, 0.06), (L / 2 - 0.24, 0.0, 0.03), DK, 0.01, 2)]
    for i in range(3):
        z = 0.12 + i * 0.2
        p.append(rbox("Drawer", (0.38, 0.03, 0.17), (L / 2 - 0.24, -D / 2 + 0.035, z + 0.085), DK, 0.012, 3))
        p.append(rbox("Pull", (0.12, 0.03, 0.025), (L / 2 - 0.24, -D / 2 + 0.005, z + 0.13), MT, 0.008, 2))
    p += stripe("DeskStripe", L - 0.52, H - 0.2, D / 2 - 0.083)
    return finish(p, "HubDesk", sharp_deg=50)


V = (0.75, -1.0, 0.45)
run_baked([
    ("HubDesk", desk, V, {"scale": 1.0}),
    ("HubLockers", lockers, V, {"scale": 1.0}),
    ("HubBench", bench, V, {"scale": 0.6}),
    ("HubCot", cot, V, {"scale": 1.0}),
    ("HubFirstAid", first_aid, (0.5, -1.0, 0.3), {"scale": 0.4, "px_per_m": 600.0}),
    ("HubTvCart", tv_cart, V, {"scale": 0.8}),
    ("HubBoard", board, (0.3, -1.0, 0.2), {"scale": 1.0}),
    ("HubReception", reception, V, {"scale": 1.0}),
    ("HubCoffeeTable", coffee_table, V, {"scale": 0.6}),
    ("HubPunchClock", punch_clock, (0.5, -1.0, 0.3), {"scale": 0.35, "px_per_m": 700.0}),
    ("HubCardRack", card_rack, (0.5, -1.0, 0.3), {"scale": 0.4, "px_per_m": 600.0}),
    ("HubCoffee", coffee_machine, V, {"scale": 1.0}),
    ("HubPlant", plant, V, {"scale": 0.5}),
    ("HubPosterA", lambda: poster("A"), (0.3, -1.0, 0.2), {"scale": 0.4, "px_per_m": 500.0}),
    ("HubPosterB", lambda: poster("B"), (0.3, -1.0, 0.2), {"scale": 0.4, "px_per_m": 500.0}),
    ("HubPosterC", lambda: poster("C"), (0.3, -1.0, 0.2), {"scale": 0.4, "px_per_m": 500.0}),
    # Round 2 (2026-11-07): the arrival, the signs, the pay window, the manager's office, the run-down touches.
    ("HubLogo", logo, (0.3, -1.0, 0.2), {"scale": 0.8}),
    ("HubSignTraining", lambda: sign("HubSignTraining", ["TRAINING"], "U"), (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 600.0}),
    ("HubSignLockers", lambda: sign("HubSignLockers", ["LOCKERS", "FIRST AID"], "L"), (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 600.0}),
    ("HubSignLift", lambda: sign("HubSignLift", ["LIFT"], "R"), (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 600.0}),
    ("HubSignFirstAid", lambda: sign("HubSignFirstAid", ["FIRST AID"], "L"), (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 600.0}),
    ("HubPayWindow", pay_window, V, {"scale": 0.8}),
    ("HubBlinds", blinds, V, {"scale": 0.8}),
    ("HubDoorLocked", door_locked, V, {"scale": 0.8}),
    ("HubCalendar", calendar, (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 700.0}),
    ("HubSheetDesk", lambda: dust_sheet("HubSheetDesk", 1.8, 0.82, 0.8), V, {"scale": 0.8}),
    ("HubSheetChair", lambda: dust_sheet("HubSheetChair", 0.65, 0.65, 1.05), V, {"scale": 0.5}),
    ("HubSpeaker", speaker, (0.4, -0.6, -1.0), {"scale": 0.3, "px_per_m": 600.0}),
    ("HubClockStopped", clock_stopped, (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 700.0}),
    ("HubOutOfOrder", out_of_order, (0.3, -1.0, 0.2), {"scale": 0.25, "px_per_m": 700.0}),
    ("HubCardPile", card_pile, (0.5, -0.8, 0.9), {"scale": 0.25, "px_per_m": 900.0}),
    ("HubChairStack", chair_stack, V, {"scale": 0.6}),
    ("HubMat", lambda: rubber_mat("HubMat", 1.6, 1.0, hx('#3A2C22')), (0.4, -0.8, 0.9), {"scale": 0.6}),
    ("HubRunner", lambda: rubber_mat("HubRunner", 4.8, 1.0, hx('#5C3B24')), (0.4, -0.8, 0.9), {"scale": 1.0}),
    ("HubSofa", sofa70, V, {"scale": 1.0}),
    # Round 3 (2026-11-07): the cots' return chutes, the lift's floor dial, the map.
    ("HubChute", chute, (0.6, -1.0, 0.5), {"scale": 0.6}),
    ("HubChuteShutter", chute_shutter, (0.5, -1.0, 0.3), {"scale": 0.4, "px_per_m": 500.0}),
    ("HubLiftDial", lift_dial, (0.3, -1.0, 0.2), {"scale": 0.3, "px_per_m": 700.0}),
    ("HubMap", hub_map, (0.3, -1.0, 0.2), {"scale": 0.5, "px_per_m": 600.0}),
])
print("[hub] done ->", OUT)
