"""
The contract floor's machines (2026-11-08, Docs/GameDesign.md section 8, "Light and power"), until now plain blocks in
the house colours (Interaction.MachineUnit.Blocks): the generator, a conveyor section and the floodlight, in the hub's
design language (build_hub.py, build_leftovers_hub.py): seventies, chunky boxes and fat tubes with big soft rounded edges,
few parts, the company's colours (amber, avocado, cream, chocolate) and the yellow industrial socket. Shapes after the
real things: a portable generator in a tubular roll cage on two wheels, its control panel with a dial and a socket; a
portable belt conveyor section on four legs (side frames, the belt over end drums, a motor and chain guard under the
discharge end); a site floodlight on a tripod (one big lamp head in a yoke on a telescoping pole). Run headless, always
with the Overflow style:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_machines.py -- Assets/Backrooms/Resources/Overflow/Fittings --style overflow [--preview <dir>] [--only Generator]

Sizes as Interaction.Machines.Size (Unity x, y, z = width, height, depth) and the parts the game adds itself where it
expects them (MachineUnit): the generator's needle on its dial at (0.08, 0.41, 0.296) and its socket at (0.3, 0.45, 0.3)
where the cables come in; the conveyor's belt top at 0.52 (Machines.BeltTop), running toward +Z (Blender -Y), its plug at
the back end (0, 0.25, -0.58); the floodlight's lens (the game's, lit when powered) at (0, 1.84, 0.135), its switch box
on the back leg (0, 0.26, -0.2).
  - Generator: 0.95 x 0.75 x 0.6. Baked.
  - ConveyorSection: 0.62 x 0.52 x 1.2. Baked.
  - Floodlight: 0.7 x 2.0 x 0.7. Baked.

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


AMBER, AVOCADO, CHOC, CREAM = hx('#F0A21E'), hx('#7C8A2E'), hx('#3B2A1E'), hx('#F3E6C8')
YELLOW, DARK = hx('#F7CC1A'), hx('#2B2420')


def smooth(pts, n=6):
    """A Catmull-Rom curve through the points (bends are round, not kinked)."""
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


def disc_y(name, x, y, z, r, depth, material, seg=24):
    """A round boss facing -Y (a dial, a cap seen from the front): its face at y, depth deep behind it."""
    return lathe(name, [(0.0, 0.0), (r, 0.0), (r, depth), (0.0, depth)], material, seg, xf=M4(rot('X', -90), (x, y, z)))


def wheel_x(name, x, y, z, r, w, material, seg=20):
    """A wheel on an axle along X, centred at (x, y, z)."""
    prof = [(0.0, -w / 2), (r * 0.82, -w / 2), (r, -w / 2 + w * 0.2), (r, w / 2 - w * 0.2), (r * 0.82, w / 2), (0.0, w / 2)]
    return lathe(name, prof, material, seg, xf=M4(rot('Y', 90), (x, y, z)))


# ------------------------------------------------------------------------------------------------- the generator

def generator():
    FR = S("Paint_GenCage", AVOCADO, "paint", 0.5)
    BODY = S("Paint_GenBody", AMBER, "paint", 0.45)
    TANK = S("Paint_GenTank", CREAM, "paint", 0.4)
    DK = S("Paint_GenDark", DARK, "paint", 0.6)
    FACE = S("Plastic_GenDial", CREAM, "plastic", 0.4)
    SOCK = S("Plastic_GenSocket", YELLOW, "plastic", 0.45)
    RUB = S("Rubber_GenWheel", (0.1, 0.09, 0.08), "rubber", 0.8)
    MUF = S("Galv_GenMuffler", (0.6, 0.6, 0.57), "galv", 0.4)
    W, D, H = 0.95, 0.6, 0.75
    p = []
    r = 0.022
    # The roll cage: a loop each side, bars across the top and the foot.
    for sx in (-1, 1):
        x = sx * (W / 2 - 0.035)
        p.append(tube("CageSide", smooth([(x, -0.255, 0.07), (x, -0.262, 0.4), (x, -0.25, 0.69), (x, 0.0, 0.715), (x, 0.25, 0.69),
                                          (x, 0.262, 0.4), (x, 0.255, 0.07)], 4), r, FR, 12))
        p.append(tube("CageFoot", [(x, -0.27, 0.05), (x, 0.27, 0.05)], r, FR, 12))
    for y in (-0.255, 0.255):
        p.append(tube("CageBar", [(-W / 2 + 0.035, y, 0.69), (W / 2 - 0.035, y, 0.69)], r, FR, 12))
    for y in (-0.2, 0.2):
        p.append(tube("Skid", [(-W / 2 + 0.035, y, 0.05), (W / 2 - 0.035, y, 0.05)], r, FR, 12))
    # The engine and alternator: one chunky amber body, the recoil starter on its end, the tank on top.
    p.append(rbox("Body", (0.66, 0.42, 0.4), (-0.04, 0.02, 0.33), BODY, 0.06, 4))
    p.append(rbox("Base", (0.7, 0.44, 0.06), (-0.04, 0.02, 0.11), DK, 0.02, 3))
    p.append(lathe("Starter", [(0.0, 0.0), (0.11, 0.0), (0.12, 0.02), (0.12, 0.05), (0.0, 0.05)], DK, 24, xf=M4(rot('Y', -90), (-0.37, 0.04, 0.34))))
    p.append(rbox("StarterGrip", (0.03, 0.07, 0.025), (-0.43, 0.04, 0.34), DK, 0.01, 2))
    for k in range(5):
        p.append(rbox("Fin", (0.03, 0.3, 0.012), (-0.35 + 0.032 * k, 0.04, 0.54), DK, 0.004, 1))
    p.append(rbox("Tank", (0.48, 0.32, 0.12), (0.06, 0.02, 0.6), TANK, 0.05, 4))
    p.append(lathe("TankCap", [(0.0, 0.0), (0.045, 0.0), (0.045, 0.03), (0.035, 0.04), (0.0, 0.04)], DK, 20, xf=M4(None, (0.16, 0.06, 0.66))))
    p.append(lathe("Muffler", [(0.0, 0.0), (0.05, 0.0), (0.055, 0.01), (0.055, 0.2), (0.05, 0.21), (0.0, 0.21)], MUF, 18, xf=M4(rot('Y', 90), (0.04, 0.275, 0.25))))
    # The control panel on the front: dark, with a dial (the game's needle on it) and the yellow industrial socket.
    p.append(rbox("Panel", (0.44, 0.04, 0.3), (0.19, -0.265, 0.42), DK, 0.02, 3))
    p.append(disc_y("Dial", 0.08, -0.292, 0.45, 0.072, 0.012, FACE, 28))
    p.append(disc_y("DialRim", 0.08, -0.289, 0.45, 0.082, 0.006, DK, 28))
    for k in range(5):
        a = math.radians(-60 + 30 * k)
        p.append(rbox("Tick", (0.006, 0.004, 0.018), (0.08 + 0.055 * math.sin(a), -0.296, 0.41 + 0.055 * math.cos(a) + 0.0), DK, 0.0, 1))
    p.append(rbox("Socket", (0.11, 0.04, 0.11), (0.3, -0.29, 0.45), SOCK, 0.025, 3))
    p.append(disc_y("SocketHole", 0.3, -0.312, 0.45, 0.032, 0.01, DK, 20))
    p.append(rbox("Switch", (0.05, 0.03, 0.07), (0.3, -0.29, 0.33), SOCK, 0.012, 2))
    p.append(rbox("Lamp", (0.03, 0.02, 0.03), (0.17, -0.29, 0.33), S("Plastic_GenLamp", (0.85, 0.25, 0.12), "plastic", 0.3), 0.008, 2))
    # Two wheels at the back, rubber feet at the front, a handle to wheel it by.
    for sx in (-1, 1):
        p.append(wheel_x("Wheel", sx * (W / 2 - 0.08), 0.27, 0.085, 0.085, 0.06, RUB, 20))
        p.append(rbox("Foot", (0.06, 0.06, 0.04), (sx * (W / 2 - 0.08), -0.24, 0.02), RUB, 0.012, 2))
    p.append(tube("Handle", smooth([(-0.3, 0.255, 0.69), (-0.3, 0.33, 0.72), (0.3, 0.33, 0.72), (0.3, 0.255, 0.69)], 4), 0.018, FR, 12))
    return finish(p, "Generator", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- the conveyor

def conveyor():
    FR = S("Paint_ConvFrame", AVOCADO, "paint", 0.5)
    BELT = S("Rubber_ConvBelt", (0.13, 0.12, 0.11), "rubber", 0.8)
    DRUM = S("Galv_ConvDrum", (0.6, 0.6, 0.57), "galv", 0.4)
    LEG = S("Paint_ConvLeg", DARK, "paint", 0.6)
    MOT = S("Paint_ConvMotor", AMBER, "paint", 0.45)
    SOCK = S("Plastic_ConvSocket", YELLOW, "plastic", 0.45)
    MARK = S("Paint_ConvArrow", CREAM, "paint", 0.5)
    RUB = S("Rubber_ConvFoot", (0.1, 0.09, 0.08), "rubber", 0.8)
    W, L, TOP = 0.62, 1.2, 0.52
    p = []
    # The side frames: fat channels standing a little above the belt (they keep the boxes on).
    for sx in (-1, 1):
        p.append(rbox("Side", (0.05, L, 0.15), (sx * (W / 2 - 0.025), 0.0, TOP - 0.05), FR, 0.018, 3))
    # The belt over its bed, round a drum at each end.
    p.append(rbox("Belt", (W - 0.12, L - 0.1, 0.03), (0.0, 0.0, TOP - 0.015), BELT, 0.008, 2))
    for y in (-(L / 2 - 0.05), L / 2 - 0.05):
        p.append(lathe("Drum", [(0.0, -(W - 0.12) / 2), (0.032, -(W - 0.12) / 2), (0.032, (W - 0.12) / 2), (0.0, (W - 0.12) / 2)],
                       BELT, 20, xf=M4(rot('Y', 90), (0.0, y, TOP - 0.032))))
        p.append(lathe("Axle", [(0.0, -W / 2 + 0.01), (0.014, -W / 2 + 0.01), (0.014, W / 2 - 0.01), (0.0, W / 2 - 0.01)],
                       DRUM, 12, xf=M4(rot('Y', 90), (0.0, y, TOP - 0.032))))
    # Arrows printed along the side frames: the way it carries (+Z in the game, -Y here).
    for sx in (-1, 1):
        for y in (-0.3, 0.1):
            x = sx * (W / 2 + 0.002)
            p.append(rbox("Arrow", (0.006, 0.12, 0.022), (x, y - 0.03, TOP - 0.04), MARK, 0.0, 1))
            p.append(rbox("ArrowHead", (0.006, 0.05, 0.05), (x, y - 0.1, TOP - 0.04), MARK, 0.0, 1))
    # Four legs, braced, on rubber feet.
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (W / 2 - 0.06), sy * (L / 2 - 0.12)
            p.append(rbox("Leg", (0.045, 0.045, TOP - 0.13), (x, y, (TOP - 0.13) / 2 + 0.02), LEG, 0.01, 2))
            p.append(rbox("Foot", (0.08, 0.08, 0.025), (x, y, 0.0125), RUB, 0.01, 2))
        p.append(rbox("Brace", (0.035, L - 0.24, 0.035), (sx * (W / 2 - 0.06), 0.0, 0.16), LEG, 0.008, 2))
    for sy in (-1, 1):
        p.append(rbox("CrossBrace", (W - 0.12, 0.035, 0.035), (0.0, sy * (L / 2 - 0.12), 0.16), LEG, 0.008, 2))
    # The motor and its chain guard under the discharge end, the yellow plug box at the back.
    p.append(rbox("Motor", (0.2, 0.2, 0.16), (0.1, -(L / 2 - 0.2), TOP - 0.2), MOT, 0.04, 3))
    p.append(lathe("MotorFan", [(0.0, 0.0), (0.06, 0.0), (0.065, 0.02), (0.0, 0.02)], LEG, 18, xf=M4(rot('Y', 90), (0.2, -(L / 2 - 0.2), TOP - 0.2))))
    p.append(rbox("ChainGuard", (0.03, 0.2, 0.2), (W / 2 + 0.005, -(L / 2 - 0.13), TOP - 0.1), MOT, 0.012, 2))
    p.append(rbox("PlugBar", (W - 0.12, 0.03, 0.03), (0.0, L / 2 - 0.12, 0.25), LEG, 0.008, 2))
    p.append(rbox("PlugBox", (0.12, 0.06, 0.1), (0.0, L / 2 - 0.08, 0.25), SOCK, 0.02, 3))
    return finish(p, "ConveyorSection", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- the floodlight

def floodlight():
    POLE = S("Paint_FloodPole", AVOCADO, "paint", 0.5)
    HEAD = S("Paint_FloodHead", AMBER, "paint", 0.45)
    DK = S("Paint_FloodDark", DARK, "paint", 0.6)
    SOCK = S("Plastic_FloodSwitch", YELLOW, "plastic", 0.45)
    RUB = S("Rubber_FloodFoot", (0.1, 0.09, 0.08), "rubber", 0.8)
    CABLE = S("Rubber_FloodCable", (0.12, 0.11, 0.1), "rubber", 0.7)
    HY = 1.84                        # the head's middle (Interaction.MachineUnit: its light and lens)
    p = []
    # The tripod: a hub on the pole, three splayed legs (one to the back), braces, rubber feet.
    hub = 0.6
    p.append(rbox("Hub", (0.09, 0.09, 0.1), (0.0, 0.0, hub), DK, 0.025, 3))
    for k, a in enumerate((90, 210, 330)):
        ang = math.radians(a)
        fx, fy = 0.31 * math.cos(ang), 0.31 * math.sin(ang)
        p.append(tube("Leg", [(0.0, 0.0, hub), (fx, fy, 0.03)], 0.016, DK, 10))
        p.append(rbox("Foot", (0.06, 0.06, 0.03), (fx, fy, 0.015), RUB, 0.01, 2))
        p.append(tube("Brace", [(fx * 0.55, fy * 0.55, hub * 0.45), (0.0, 0.0, 0.3)], 0.009, DK, 8))
    # The telescoping pole, its clamp, then the yoke and the lamp head.
    p.append(lathe("PoleLow", [(0.0, 0.0), (0.026, 0.0), (0.026, hub + 0.5), (0.0, hub + 0.5)], POLE, 16, xf=M4(None, (0.0, 0.0, 0.25))))
    p.append(lathe("Collar", [(0.0, 0.0), (0.036, 0.0), (0.036, 0.05), (0.0, 0.05)], DK, 16, xf=M4(None, (0.0, 0.0, hub + 0.72))))
    p.append(lathe("Knob", [(0.0, 0.0), (0.018, 0.0), (0.018, 0.04), (0.0, 0.04)], DK, 12, xf=M4(rot('X', 90), (0.0, 0.03, hub + 0.745))))
    p.append(lathe("PoleHigh", [(0.0, 0.0), (0.019, 0.0), (0.019, HY - 0.2 - hub - 0.7), (0.0, HY - 0.2 - hub - 0.7)], POLE, 14, xf=M4(None, (0.0, 0.0, hub + 0.7))))
    yb = HY - 0.21
    p.append(tube("Yoke", smooth([(-0.29, 0.0, HY), (-0.29, 0.0, yb + 0.02), (-0.27, 0.0, yb), (0.27, 0.0, yb), (0.29, 0.0, yb + 0.02), (0.29, 0.0, HY)], 4), 0.016, DK, 12))
    for sx in (-1, 1):
        p.append(lathe("YokeKnob", [(0.0, 0.0), (0.03, 0.0), (0.03, 0.035), (0.0, 0.035)], DK, 16, xf=M4(rot('Y', 90 * sx), (sx * 0.29, 0.0, HY))))
    # The head: a deep amber housing, a dark bezel round the lens (the game's), a wire guard across it, fins behind.
    p.append(rbox("Housing", (0.5, 0.24, 0.32), (0.0, 0.0, HY), HEAD, 0.05, 4))
    p.append(rbox("BezelTop", (0.52, 0.03, 0.04), (0.0, -0.135, HY + 0.145), DK, 0.012, 2))
    p.append(rbox("BezelBottom", (0.52, 0.03, 0.04), (0.0, -0.135, HY - 0.145), DK, 0.012, 2))
    for sx in (-1, 1):
        p.append(rbox("BezelSide", (0.04, 0.03, 0.32), (sx * 0.24, -0.135, HY), DK, 0.012, 2))
    for k in range(-1, 2):
        p.append(tube("Guard", [(k * 0.12, -0.155, HY - 0.13), (k * 0.12, -0.165, HY), (k * 0.12, -0.155, HY + 0.13)], 0.006, DK, 6))
    for k in range(5):
        p.append(rbox("Fin", (0.42, 0.02, 0.025), (0.0, 0.125, HY - 0.1 + 0.05 * k), DK, 0.006, 1))
    p.append(rbox("Handle", (0.18, 0.04, 0.03), (0.0, 0.0, HY + 0.18), DK, 0.012, 2))
    # The cable down the pole to the yellow switch box on the back leg, where the game's cable comes in.
    p.append(tube("Cable", smooth([(0.0, 0.12, HY - 0.1), (0.0, 0.08, HY - 0.3), (0.03, 0.03, 1.2), (0.03, 0.035, 0.8),
                                   (0.0, 0.1, hub - 0.05), (0.0, 0.18, 0.32)], 4), 0.008, CABLE, 8))
    p.append(rbox("SwitchBox", (0.1, 0.06, 0.12), (0.0, 0.2, 0.26), SOCK, 0.02, 3))
    return finish(p, "Floodlight", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- the work lamp

def work_lamp():
    """
    The work lamp (an item, Interaction.WorkLamp: the floor lamp's ways, its own look): a heavy round foot, a pole you
    take hold of like the floor lamp's (0.014 round, the hand on it at 0.95 m), a yoke and a small amber work-light head
    facing the front (the game's lens in its bezel at (0, 1.42, 0.072), its spotlight just in front).
    """
    POLE = S("Paint_WorkPole", AVOCADO, "paint", 0.5)
    HEAD = S("Paint_WorkHead", AMBER, "paint", 0.45)
    DK = S("Paint_WorkDark", DARK, "paint", 0.6)
    SW = S("Plastic_WorkSwitch", YELLOW, "plastic", 0.45)
    RUB = S("Rubber_WorkFoot", (0.1, 0.09, 0.08), "rubber", 0.8)
    HY = 1.42
    p = []
    p.append(lathe("Foot", [(0.0, 0.0), (0.175, 0.0), (0.18, 0.006), (0.17, 0.026), (0.05, 0.034), (0.0, 0.034)], DK, 32))
    p.append(lathe("FootRing", [(0.0, 0.0), (0.182, 0.0), (0.182, 0.008), (0.0, 0.008)], RUB, 32))
    p.append(lathe("Pole", [(0.0, 0.0), (0.014, 0.0), (0.014, HY - 0.1 - 0.03), (0.0, HY - 0.1 - 0.03)], POLE, 14, xf=M4(None, (0.0, 0.0, 0.03))))
    p.append(lathe("Collar", [(0.0, 0.0), (0.024, 0.0), (0.024, 0.04), (0.0, 0.04)], DK, 14, xf=M4(None, (0.0, 0.0, 0.03))))
    p.append(rbox("Switch", (0.045, 0.035, 0.06), (0.0, -0.022, 1.12), SW, 0.01, 2))
    yb = HY - 0.1
    p.append(tube("Yoke", smooth([(-0.165, 0.0, HY), (-0.165, 0.0, yb + 0.02), (-0.15, 0.0, yb), (0.15, 0.0, yb), (0.165, 0.0, yb + 0.02), (0.165, 0.0, HY)], 4), 0.011, DK, 10))
    for sx in (-1, 1):
        p.append(lathe("YokeKnob", [(0.0, 0.0), (0.022, 0.0), (0.022, 0.025), (0.0, 0.025)], DK, 14, xf=M4(rot('Y', 90 * sx), (sx * 0.165, 0.0, HY))))
    p.append(rbox("Housing", (0.28, 0.14, 0.18), (0.0, 0.0, HY), HEAD, 0.035, 4))
    p.append(rbox("BezelTop", (0.3, 0.02, 0.025), (0.0, -0.075, HY + 0.09), DK, 0.008, 2))
    p.append(rbox("BezelBottom", (0.3, 0.02, 0.025), (0.0, -0.075, HY - 0.09), DK, 0.008, 2))
    for sx in (-1, 1):
        p.append(rbox("BezelSide", (0.025, 0.02, 0.2), (sx * 0.1375, -0.075, HY), DK, 0.008, 2))
    for x in (-0.05, 0.05):
        p.append(tube("Guard", [(x, -0.086, HY - 0.075), (x, -0.092, HY), (x, -0.086, HY + 0.075)], 0.004, DK, 6))
    for k in range(4):
        p.append(rbox("Fin", (0.24, 0.016, 0.018), (0.0, 0.075, HY - 0.06 + 0.04 * k), DK, 0.005, 1))
    p.append(tube("Handle", smooth([(-0.08, 0.0, HY + 0.09), (-0.07, 0.0, HY + 0.13), (0.07, 0.0, HY + 0.13), (0.08, 0.0, HY + 0.09)], 4), 0.009, DK, 8))
    return finish(p, "WorkLamp", sharp_deg=50)


V = (0.75, -1.0, 0.5)
run_baked([
    ("WorkLamp", work_lamp, (0.75, -1.0, 0.4), {"scale": 0.6, "px_per_m": 480.0}),
    ("Generator", generator, V, {"scale": 0.8}),
    ("ConveyorSection", conveyor, (0.9, -1.0, 0.7), {"scale": 0.8}),
    ("Floodlight", floodlight, (0.75, -1.0, 0.4), {"scale": 1.0}),
])
print("[machines] done ->", OUT)
