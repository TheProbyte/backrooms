"""
A portable CD radio cassette player and a cassette (2026-11-04, after the owner's photo of a late-1990s silver boombox,
the Aiwa CSD-ED37's kind: 507 x 206 x 300 mm, 4.4 kg), built in Blender and baked (bake_kit.py). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_boombox.py -- Assets/Backrooms/Resources/Items [--preview <dir>]

  - Boombox: the body: a shell round in plan, dark grey below with the speaker grilles wrapping round its ends (a
    repeated print of their holes), silver above with ribbed shoulders; the centre panel standing proud of it, the
    cassette deck's well behind the door opening, the volume knob, the buttons' bodies, the LCD's bezel, the keys' slot;
    on top the CD block and its smoked lid, the side plateaus, the built-in mic; at the back the battery door, the AC
    inlet, the telescopic antenna; rubber feet.
  - BoomboxFace: everything printed (the panel's labels, the buttons' faces, the knob's face, the sticker on top, the
    model plate, the back's labels) and what lights (the LCD, the power LED), on its own texture so small print reads;
    BoomboxFace_Emission: the LCD's backlight and the LED.
  - BoomboxKey0..5: the deck's piano keys (PAUSE, STOP/EJECT, F.FWD, REWIND, PLAY, REC), each its own model with its
    origin at its hinge (they go down).
  - BoomboxDoor: the cassette door, smoked window, the brand; origin on its hinge along the bottom (it tips open).
  - BoomboxHandle: the carry handle, folded forward over the top; origin on its pivot (it swings up to carry).
  - Cassette: a C60 normal-position cassette (100.4 x 63.8 x 12 mm), black shell, a paper label written on by hand
    ("lunch break mix"), the window onto its hubs and tape, side A's tab broken out.
  - Boombox_Layout.json: the moving parts' pivots and where the cassette sits in the door (Blender frame).

No real brand (KOHARA). Frame: origin on the floor at the middle of the foot, front toward -Y (Unity +Z).
Unity (x, y, z) = (-x, z, -y).
"""
import os
import sys
import math
import json
import random

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_vending.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- the machine")
exec(compile(_src[:_cut], "build_vending_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix

TH = math.radians(6.0)                  # the centre panel leans back
CT, ST = math.cos(TH), math.sin(TH)
NRM = Vector((0.0, -CT, ST))            # out of the panel's face
UPF = Vector((0.0, ST, CT))             # up along it
Z0 = 0.018                              # the face's s = 0 (the door's foot)
YF0 = -0.158 + 0.006 * math.tan(TH)
R_FACE = rot('X', -6.0)
PX = 0.117                              # the panel's half width


def F(x, s, out=0.0):
    """A point on the centre panel's face: x across, s up along it from the door's foot, out of it."""
    return Vector((x, YF0 + s * ST, Z0 + s * CT)) + NRM * out


def face_box(name, x, s, w, h, d, mat, bev=0.0015, out0=0.0, segs=2):
    ob = rbox(name, (w, d, h), (0, 0, 0), mat, bev, segs)
    place(ob, R_FACE, tuple(F(x, s, out0 + d / 2)))
    return ob


def sheared_box(name, x0, x1, z0, z1, mat, bev=0.004, depth=0.1):
    """A block of the centre panel: its front on the panel's leaning face, depth back into the body."""
    yb = -0.158
    ob = rbox(name, (x1 - x0, depth, z1 - z0), ((x0 + x1) / 2, yb + depth / 2, (z0 + z1) / 2), mat, bev, 2)
    t = math.tan(TH)
    for v in ob.data.vertices:
        v.co.y += (v.co.z - 0.012) * t
    return ob


def se_ring(z, a, b, n=2.8, N=96):
    """A superellipse round the body in plan (|x/a|^n + |y/b|^n = 1), counter-clockwise from the front's middle."""
    out = []
    for k in range(N):
        t = -math.pi / 2 + 2 * math.pi * k / N
        c, s = math.cos(t), math.sin(t)
        out.append(Vector((a * math.copysign(abs(c) ** (2 / n), c), b * math.copysign(abs(s) ** (2 / n), s), z)))
    return out


SHELL = [(0.000, 0.226, 0.124), (0.006, 0.238, 0.133), (0.018, 0.247, 0.141), (0.040, 0.251, 0.145), (0.120, 0.252, 0.146),
         (0.138, 0.249, 0.143), (0.160, 0.242, 0.136), (0.180, 0.233, 0.127), (0.196, 0.224, 0.118), (0.204, 0.217, 0.112)]
TOP = 0.204
DECK = TOP + 0.006                      # the raised top deck's face


def shell_ab(z):
    for (z0, a0, b0), (z1, a1, b1) in zip(SHELL[:-1], SHELL[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / (z1 - z0)
            return a0 + (a1 - a0) * t, b0 + (b1 - b0) * t
    return SHELL[-1][1], SHELL[-1][2]


def back_y(x, z, n=2.8):
    """The shell's back surface (y) at x, height z."""
    a, b = shell_ab(z)
    return b * max(0.0, 1.0 - abs(x / a) ** n) ** (1.0 / n)


def end_path(z, side, x_from=0.119, y_to=0.07, N=720, out=0.0):
    """Points along the shell's outline at height z round one end (side -1 left, +1 right): from the centre panel's
    edge on the front, round the end, to y_to on the side; and their outward normals (in plan)."""
    a, b = shell_ab(z)
    n = 2.8
    pts = []
    for k in range(N + 1):
        t = -math.pi / 2 + (math.pi * 0.8) * k / N * (-1 if side < 0 else 1)
        c, s_ = math.cos(t), math.sin(t)
        x = a * math.copysign(abs(c) ** (2 / n), c)
        y = b * math.copysign(abs(s_) ** (2 / n), s_)
        if abs(x) < x_from or y > y_to:
            continue
        g = Vector((math.copysign(abs(x / a) ** (n - 1), x) / a, math.copysign(abs(y / b) ** (n - 1), y) / b, 0)).normalized()
        pts.append((Vector((x, y, z)) + g * out, g))
    return pts


# ------------------------------------------------------------------------------------------------- panel layout

KEY_NAMES = ["PAUSE", "STOP/EJECT", "F.FWD", "REWIND", "PLAY", "REC"]
KEY_SYMS = ["II", "\u25a0\u25b2", "\u25ba\u25ba", "\u25c4\u25c4", "\u25ba", "\u25cf"]
KEY_W, KEY_GAP = 0.021, 0.0015
KEY_X = [-(6 * KEY_W + 5 * KEY_GAP) / 2 + KEY_W / 2 + i * (KEY_W + KEY_GAP) for i in range(6)]
KEY_S0, KEY_S1 = 0.1, 0.123             # their faces' span up the panel
ART_X, ART_S0, ART_S1 = 0.112, 0.074, 0.184
FUNC = [("TAPE", -0.028, 0.141, 0.025, 0.0085), ("RADIO", 0.0, 0.141, 0.025, 0.0085), ("CD", 0.028, 0.141, 0.025, 0.0085),
        ("3-MODE\nEQUALIZER", -0.028, 0.1295, 0.025, 0.0078), ("T-BASS", 0.0, 0.1295, 0.025, 0.0078), ("GROUND", 0.028, 0.1295, 0.025, 0.0078),
        ("\u25ba/II", 0.082, 0.170, 0.036, 0.009), ("\u25a0", 0.082, 0.157, 0.036, 0.0085),
        ("\u25c4\u25c4", 0.0725, 0.143, 0.016, 0.0085), ("\u25ba\u25ba", 0.0915, 0.143, 0.016, 0.0085)]
KNOB = (-0.079, 0.153, 0.0195)
SMALL = (-0.052, 0.131, 0.005)
LCD = (0.0, 0.162, 0.06, 0.018)
LEDS = [(-0.047, 0.081, (0.95, 0.2, 0.08), True), (-0.019, 0.081, (0.2, 0.9, 0.25), False), (0.005, 0.081, (0.2, 0.9, 0.25), False),
        (0.052, 0.083, (0.25, 0.55, 1.0), False), (0.070, 0.083, (0.25, 0.55, 1.0), False), (0.088, 0.083, (0.25, 0.55, 1.0), False)]
DOOR_X, DOOR_S0, DOOR_S1 = 0.08, 0.001, 0.070


def art_rect(x, s, w, h):
    """UVs of a rectangle of the panel (x, s middle) in the face artwork."""
    W, H = 2 * ART_X, ART_S1 - ART_S0
    return ((x - w / 2 + ART_X) / W, (s - h / 2 - ART_S0) / H, (x + w / 2 + ART_X) / W, (s + h / 2 - ART_S0) / H)


def art_xy(x, s):
    """The panel point (x, s) in the artwork's own frame (its middle at 0)."""
    return x, s - (ART_S0 + ART_S1) / 2


DARK_TXT = (0.12, 0.12, 0.13)


def face_art(A, W, H, lit=False):
    """The centre panel's print above the door: silver, the LED band, the keys' labels, the function area."""
    def xy(x, s):
        return art_xy(x, s)
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.grad((0.6, 0.61, 0.62), (0.74, 0.745, 0.75)))
    # the LED band
    x0, y0 = xy(-ART_X, ART_S0)
    x1, y1 = xy(ART_X, 0.089)
    A.rect(x0 - 0.01, y0 - 0.01, x1 + 0.01, y1, A.emit((0.06, 0.06, 0.065)))
    for (label, lx) in (("OPE/BATT", -0.064), ("RADIO", -0.031), ("CD", -0.006)):
        A.text(label, 0.0028, *xy(lx, 0.0805), (0.78, 0.78, 0.76), "arialbd.ttf")
    for (lx, ls, col, on) in LEDS:
        A.disc(*xy(lx, ls), 0.0016, A.emit((0.02, 0.02, 0.02)))
    for (label, lx) in (("3-MODE EQUALIZER", 0.07), ("T-BASS", 0.052), ("GROUND", 0.088)):
        pass
    A.text("3-MODE EQUALIZER", 0.0024, *xy(0.07, 0.0868), (0.45, 0.65, 0.95), "arialbd.ttf")
    A.text("T-BASS    GROUND", 0.0022, *xy(0.07, 0.0778), (0.7, 0.7, 0.68), "arialbd.ttf")
    # the keys' labels under them, their slot
    for i, (name, kx) in enumerate(zip(KEY_NAMES, KEY_X)):
        A.text(name, 0.0026 if len(name) < 7 else 0.0022, *xy(kx, 0.0935), (0.75, 0.08, 0.06) if name == "REC" else DARK_TXT, "arialbd.ttf")
    sx0, sy0 = xy(KEY_X[0] - KEY_W / 2 - 0.002, KEY_S0 - 0.002)
    sx1, sy1 = xy(KEY_X[-1] + KEY_W / 2 + 0.002, KEY_S1 + 0.002)
    A.rect(sx0, sy0, sx1, sy1, A.emit((0.05, 0.05, 0.05)))
    # the function area: the LCD's frame and its title, the buttons' faces and labels, the knob's face, the CD keys'
    lx, ls, lw, lh = LCD
    fx0, fy0 = xy(-0.046, 0.149)
    fx1, fy1 = xy(0.046, 0.182)
    A.rect(fx0, fy0, fx1, fy1, A.grad((0.52, 0.56, 0.62), (0.62, 0.66, 0.72)))
    A.text("DIGITAL SYNTHESIZED TUNER", 0.0027, *xy(0.0, 0.1765), (0.95, 0.96, 0.98), "arialbd.ttf")
    A.rect(*xy(lx - lw / 2 - 0.0015, ls - lh / 2 - 0.0015), *xy(lx + lw / 2 + 0.0015, ls + lh / 2 + 0.0015), A.emit((0.15, 0.16, 0.17)))
    A.text("FUNCTION", 0.0022, *xy(0.0, 0.1475), (0.3, 0.45, 0.8), "arialbd.ttf")
    for (label, bx, bs, bw, bh) in FUNC:
        x0, y0 = xy(bx - bw / 2, bs - bh / 2)
        x1, y1 = xy(bx + bw / 2, bs + bh / 2)
        A.rect(x0, y0, x1, y1, A.grad((0.74, 0.75, 0.76), (0.9, 0.9, 0.91)))
        lines = label.split("\n")
        size = 0.0034 if len(lines) == 1 and len(label) <= 6 else 0.0021
        for k, ln in enumerate(lines):
            off = (len(lines) - 1) * size * 0.6 - k * size * 1.2
            A.text(ln, size, *xy(bx, bs + off), DARK_TXT, "arialbd.ttf")
    A.text("PLAY/PAUSE  PRESET", 0.0021, *xy(0.082, 0.1635), DARK_TXT, "arialbd.ttf")
    A.text("STOP/MEMORY", 0.0021, *xy(0.082, 0.1505), DARK_TXT, "arialbd.ttf")
    A.text("\u2013  TUNING  +", 0.0021, *xy(0.082, 0.1365), DARK_TXT, "arialbd.ttf")
    A.text("REPEAT/", 0.0019, *xy(-0.052, 0.1405), DARK_TXT, "arialbd.ttf")
    A.text("FM MODE/DSC", 0.0019, *xy(-0.052, 0.1245), DARK_TXT, "arialbd.ttf")
    # the knob's face: a dished silver disc, VOLUME across it, the arrows
    kx, ks, kr = KNOB
    A.disc(*xy(kx, ks), kr + 0.002, A.emit((0.5, 0.5, 0.52)))
    A.disc(*xy(kx, ks), kr, A.grad((0.62, 0.63, 0.65), (0.86, 0.86, 0.87)))
    A.disc(*xy(kx, ks), kr * 0.45, A.grad((0.78, 0.78, 0.79), (0.66, 0.66, 0.68)))
    A.text("VOLUME", 0.0034, *xy(kx, ks), DARK_TXT, "arialbd.ttf")
    A.text("\u25b2", 0.0035, *xy(kx, ks + kr * 0.72), DARK_TXT, "arialbd.ttf")
    A.text("\u25bc", 0.0035, *xy(kx, ks - kr * 0.72), DARK_TXT, "arialbd.ttf")
    A.disc(*xy(SMALL[0], SMALL[1]), SMALL[2], A.grad((0.7, 0.7, 0.72), (0.88, 0.88, 0.89)))


def lcd_art(A, W, H, lit):
    """The LCD: grey-green glass; lit, a yellow-green backlight with dark segments (TAPE, the tape's arrows, a counter)."""
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.grad((0.55, 0.75, 0.32), (0.72, 0.86, 0.45)) if lit else A.grad((0.28, 0.31, 0.27), (0.34, 0.37, 0.33)))
    seg = (0.06, 0.08, 0.05) if lit else (0.26, 0.29, 0.25)
    A.text("TAPE", 0.0042, -W / 2 + 0.006, H / 2 - 0.004, seg, "consolab.ttf", align="LEFT")
    A.text("\u25ba", 0.004, W / 2 - 0.008, H / 2 - 0.004, seg, "arialbd.ttf")
    A.text("0:00", 0.0085, 0.006, -0.0015, seg, "consolab.ttf")
    A.text("ST  FM  AM  MHz", 0.0019, 0.0, -H / 2 + 0.0022, (0.2, 0.24, 0.18) if lit else (0.27, 0.3, 0.26), "arialbd.ttf")


def top_sticker(A, W, H):
    """The promotional sticker on top (an invented one): DIGITAL TUNER over a burst, MEGA BASS."""
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.0, 0.0, 0.0)))
    pts = []
    for k in range(48):
        a = 2 * math.pi * k / 48
        r = (0.5 if k % 2 == 0 else 0.42)
        pts.append((math.cos(a) * W * r, math.sin(a) * H * r))
    for k in range(48):
        a0 = pts[k]
        a1 = pts[(k + 1) % 48]
        me = bpy.data.meshes.new("Burst")
        me.from_pydata([(0, 0, 0.001), (a0[0], a0[1], 0.001), (a1[0], a1[1], 0.001)], [], [(0, 1, 2)])
        A.obj("Burst", me, [A.emit((0.98, 0.84, 0.12))])
    A.rect(-W / 2, -H * 0.12, W / 2, H * 0.12, A.emit((0.85, 0.08, 0.1)), z=0.002)
    A.text("DIGITAL TUNER", H * 0.13, 0.0, -0.0005, (0.98, 0.97, 0.95), "ariblk.ttf", z=0.003)
    A.text("MEGA BASS", H * 0.16, 0.0, H * 0.26, (0.1, 0.2, 0.6), "ariblk.ttf", z=0.003, shear=0.2)
    A.text("SYSTEM", H * 0.1, 0.0, -H * 0.27, (0.1, 0.2, 0.6), "ariblk.ttf", z=0.003)


def model_plate(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.7, 0.705, 0.71)))
    A.text("CX-470", H * 0.42, -W / 2 + 0.002, H * 0.12, (0.3, 0.3, 0.32), "arialbd.ttf", align="LEFT", shear=0.15)
    A.text("COMPACT DISC STEREO RADIO CASSETTE RECORDER", H * 0.13, -W / 2 + 0.002, -H * 0.3, (0.32, 0.32, 0.34), "arial.ttf", align="LEFT")


def back_label(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.82, 0.81, 0.76)))
    lines = ["KOHARA CORPORATION", "MODEL NO. CX-470", "AC 120V ~ 60Hz 17W   DC 9V (R20 x 6)", "SERIAL No. 70423318",
             "CLASS 1 LASER PRODUCT", "MADE IN MALAYSIA"]
    for k, ln in enumerate(lines):
        A.text(ln, H * 0.085, -W / 2 + 0.003, H / 2 - H * 0.13 - k * H * 0.145, (0.12, 0.12, 0.12), "arialbd.ttf", align="LEFT")


def battery_door_art(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.0, 0.0, 0.0)))
    A.text("\u25bc OPEN", H * 0.12, 0.0, H * 0.3, (0.55, 0.55, 0.56), "arialbd.ttf")
    A.text("R20 (SIZE D, UM-1) x 6", H * 0.1, 0.0, -H * 0.05, (0.55, 0.55, 0.56), "arialbd.ttf")
    A.text("BATTERY", H * 0.1, 0.0, -H * 0.25, (0.55, 0.55, 0.56), "arialbd.ttf")


def cd_logo(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.0, 0.0, 0.0)))
    A.text("COMPACT", H * 0.18, 0.0, H * 0.18, (0.75, 0.75, 0.76), "ariblk.ttf")
    A.text("disc", H * 0.3, 0.0, -H * 0.12, (0.75, 0.75, 0.76), "seguibli.ttf")
    A.text("1-BIT DUAL D/A CONVERTER", H * 0.09, 0.0, -H * 0.38, (0.6, 0.6, 0.62), "arialbd.ttf")


def door_art(A, W, H):
    """The cassette door's smoked window: its brand, the play arrow, a line of print."""
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.0, 0.0, 0.0)))
    A.text("kohara", H * 0.3, 0.0, H * 0.18, (0.9, 0.9, 0.9), "seguibli.ttf")
    A.text("\u25c4", H * 0.22, W * 0.12, -H * 0.24, (0.78, 0.78, 0.8), "arialbd.ttf")
    A.text("FULL LOGIC CASSETTE DECK", H * 0.075, -W * 0.18, -H * 0.4, (0.55, 0.55, 0.57), "arialbd.ttf")


def grille_tile():
    """One tile of the speaker grilles' perforation (6 mm square, two holes staggered), repeated over them."""
    c = Canvas(48, 48, (0.11, 0.11, 0.12))
    for (cx, cy) in ((12, 12), (36, 36)):
        c.disc(cx, cy, 8.0, (0.015, 0.015, 0.017))
        c.disc(cx - 1, cy - 1, 6.5, (0.02, 0.02, 0.022))
    return c


def sticker_mat(name, build, W, H, px, rough=0.5):
    im = art(_tmp("bb_" + name.lower()), W, H, px, build, samples=24)
    return img_mat(name, im, rough)


def disc_uv(name, centre, normal, up, radius, material, uv_c, uv_r, n=40):
    """A flat disc (n sides) facing normal, its UVs a disc of the print (centre uv_c, radii uv_r)."""
    nrm = Vector(normal).normalized()
    upv = Vector(up).normalized()
    right = upv.cross(nrm).normalized()
    bm = bmesh.new()
    c = Vector(centre)
    vs = [bm.verts.new(c + (right * math.cos(2 * math.pi * k / n) + upv * math.sin(2 * math.pi * k / n)) * radius) for k in range(n)]
    f = bm.faces.new(vs)
    f.normal_update()
    if f.normal.dot(nrm) < 0:
        f.normal_flip()
    ob = new_obj(name, bm, [material])
    me = ob.data
    uv = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        poly.use_smooth = False
        for li in poly.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co - c
            uv[li].uv = (uv_c[0] + v.dot(right) / radius * uv_r[0], uv_c[1] + v.dot(upv) / radius * uv_r[1])
    return ob


# ------------------------------------------------------------------------------------------------- the body

def boombox():
    use_gpu(bpy.context.scene)
    SIL = S("Paint_BoomSilver", (0.68, 0.685, 0.69), "paint", 0.42)
    SIL2 = S("Paint_BoomSilverDark", (0.5, 0.505, 0.51), "paint", 0.45)
    DK = S("Plastic_BoomDark", (0.17, 0.175, 0.18), "plastic", 0.5)
    BLK = S("Plastic_BoomBlack", (0.03, 0.03, 0.032), "plastic", 0.6)
    SMK = S("Glass_BoomSmoke", (0.05, 0.055, 0.06), "glass", 0.08)
    CHR = S("Chrome_BoomTrim", (0.75, 0.75, 0.76), "metal", 0.2, 1.0)
    RUB = S("Rubber_BoomFoot", (0.05, 0.05, 0.05), "rubber", 0.7)
    WHT = S("Plastic_BoomSpindle", (0.85, 0.85, 0.82), "plastic", 0.4)
    p = []
    # The shell: rings up its height, capped; dark grey below the speakers' tops, silver above.
    rings = [se_ring(z, a, b) for (z, a, b) in SHELL]
    sh = loft("Shell", rings, SIL, cap0=True, cap1=True, recalc=True)
    sh.data.materials.append(DK)
    for poly in sh.data.polygons:
        poly.material_index = 1 if poly.center.z < 0.131 else 0
    # (open behind the centre panel: the panel's blocks close it, and the deck's well shows through the door's opening)
    bm = bmesh.new()
    bm.from_mesh(sh.data)
    gone = [f for f in bm.faces if 0.014 < f.calc_center_median().z < 0.2 and abs(f.calc_center_median().x) < PX - 0.004
            and f.calc_center_median().y < -0.06]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    bm.to_mesh(sh.data)
    bm.free()
    p.append(sh)
    # The silver shoulders' ribs above each grille, round the ends.
    for side in (-1, 1):
        for k in range(6):
            z = 0.137 + k * 0.0072
            pts = [q for q, g in end_path(z, side, 0.119, 0.06, 150, 0.0006)]
            if len(pts) > 3:
                p.append(tube("Rib", pts, 0.0017, SIL, 6))
    # The centre panel: four blocks round the door's opening, leaning back; the deck's well behind it.
    p.append(sheared_box("PanelTop", -PX, PX, Z0 + DOOR_S1 * CT, 0.209, SIL, 0.004))
    p.append(sheared_box("PanelL", -PX, -DOOR_X - 0.0005, 0.012, Z0 + DOOR_S1 * CT + 0.001, DK, 0.003))
    p.append(sheared_box("PanelR", DOOR_X + 0.0005, PX, 0.012, Z0 + DOOR_S1 * CT + 0.001, DK, 0.003))
    p.append(sheared_box("PanelB", -DOOR_X, DOOR_X, 0.012, Z0 + 0.001, DK, 0.002))
    well_d = 0.058
    for (x0, x1, s0, s1) in ((-DOOR_X, DOOR_X, DOOR_S0, DOOR_S1),):
        back = face_box("WellBack", 0.0, (s0 + s1) / 2, x1 - x0, s1 - s0, 0.004, BLK, 0.0, -well_d)
        p.append(back)
        for sx in (-1, 1):
            p.append(face_box("WellSide", sx * (DOOR_X - 0.001), (s0 + s1) / 2, 0.002, s1 - s0, well_d, BLK, 0.0, -well_d))
        p.append(face_box("WellTop", 0.0, s1 - 0.001, x1 - x0, 0.002, well_d, BLK, 0.0, -well_d))
        p.append(face_box("WellFloor", 0.0, s0 + 0.001, x1 - x0, 0.002, well_d, BLK, 0.0, -well_d))
    # in the well: the head and pinch roller below, the two reel spindles, the capstan
    p.append(face_box("Head", 0.0, 0.009, 0.014, 0.01, 0.012, CHR, 0.0015, -well_d + 0.004))
    p.append(face_box("Roller", 0.024, 0.009, 0.008, 0.009, 0.01, BLK, 0.002, -well_d + 0.004))
    for sx in (-1, 1):
        c = F(sx * 0.0215, 0.0355, -well_d + 0.004)
        sp = lathe("Spindle", [(0.0, 0.0), (0.0045, 0.0), (0.0045, 0.01), (0.0035, 0.012), (0.0, 0.012)], WHT, 6)
        place(sp, R_FACE @ rot('X', 90), tuple(c))
        p.append(sp)
    # The door's opening's lip; the LED band's lenses are the face's.
    # The function area's parts standing off the face: the LCD bezel, the buttons' bodies, the knob, the small button.
    lx, ls, lw, lh = LCD
    for (bx, bs, bw, bh) in ((lx, ls + lh / 2 + 0.0015, lw + 0.006, 0.003), (lx, ls - lh / 2 - 0.0015, lw + 0.006, 0.003),
                              (lx - lw / 2 - 0.0015, ls, 0.003, lh), (lx + lw / 2 + 0.0015, ls, 0.003, lh)):
        p.append(face_box("LcdBezel", bx, bs, bw, bh, 0.0025, SIL2, 0.0008))
    for (label, bx, bs, bw, bh) in FUNC:
        p.append(face_box("Button", bx, bs, bw, bh, 0.0042, SIL, min(bh, bw) * 0.32, 0.0, 3))
    kx, ks, kr = KNOB
    knob = lathe("Knob", [(0.0, 0.0), (kr + 0.0035, 0.0), (kr + 0.0035, 0.0025), (kr + 0.001, 0.003), (kr, 0.0045), (kr, 0.0085),
                          (kr - 0.0015, 0.0095), (0.0, 0.0098)], SIL, 48)
    place(knob, R_FACE @ rot('X', 90), tuple(F(kx, ks)))
    p.append(knob)
    sm = lathe("Small", [(0.0, 0.0), (SMALL[2], 0.0), (SMALL[2], 0.003), (SMALL[2] - 0.001, 0.004), (0.0, 0.0042)], SIL, 24)
    place(sm, R_FACE @ rot('X', 90), tuple(F(SMALL[0], SMALL[1])))
    p.append(sm)
    # The keys' slot: dark, recessed; the keys are their own models.
    p.append(face_box("KeySlot", 0.0, (KEY_S0 + KEY_S1) / 2, KEY_X[-1] - KEY_X[0] + KEY_W + 0.004, KEY_S1 - KEY_S0 + 0.004, 0.003, BLK, 0.0, -0.002))
    # On top: the CD block and its smoked lid, the plateaus left and right (the sticker's, the model plate's), the mic.
    # the deck: raised over the whole top, its edge following the body's round (a little inside it)
    a_t, b_t = SHELL[-1][1], SHELL[-1][2]
    p.append(loft("Deck", [se_ring(TOP - 0.002, a_t - 0.006, b_t - 0.006), se_ring(DECK - 0.0015, a_t - 0.008, b_t - 0.008),
                           se_ring(DECK, a_t - 0.0105, b_t - 0.0105)], SIL, cap0=True, cap1=True, recalc=True))
    p.append(rbox("CdBlock", (0.2, 0.17, 0.008), (0.0, 0.02, DECK + 0.004), SIL, 0.004, 2))
    p.append(rbox("CdLid", (0.13, 0.135, 0.005), (0.0, 0.028, DECK + 0.0105), SMK, 0.002, 2))
    p.append(rbox("CdLidLip", (0.04, 0.008, 0.003), (0.0, -0.043, DECK + 0.0095), SMK, 0.0012, 1))
    mic = lathe("Mic", [(0.0, 0.0), (0.006, 0.0), (0.006, 0.0015), (0.004, 0.002), (0.0, 0.0012)], CHR, 20)
    place(mic, None, (-0.165, -0.08, DECK))
    p.append(mic)
    # The handle's two rests on the top (the handle is its own model).
    for sx in (-1, 1):
        p.append(rbox("HandleRest", (0.012, 0.03, 0.004), (sx * 0.118, 0.105, DECK + 0.002), DK, 0.0015, 1))
    # The back: the battery door's outline, the AC inlet, the antenna lying along the top edge, its swivel.
    p.append(rbox("BatteryDoor", (0.2, 0.004, 0.085), (0.0, 0.146 + 0.0005, 0.07), DK, 0.0015, 1))
    p.append(rbox("AcInlet", (0.026, 0.008, 0.016), (0.15, back_y(0.15, 0.06) - 0.002, 0.06), BLK, 0.002, 1))
    ant = lathe("Antenna", [(0.0, 0.0), (0.0045, 0.0), (0.0045, 0.12), (0.0038, 0.121), (0.0038, 0.2), (0.0032, 0.201),
                            (0.0032, 0.25), (0.0045, 0.252), (0.0045, 0.258), (0.0, 0.26)], CHR, 12)
    place(ant, rot('Y', -90), (0.15, 0.118, 0.193))
    p.append(ant)
    sw = lathe("Swivel", [(0.0, 0.0), (0.007, 0.0), (0.007, 0.012), (0.0, 0.012)], BLK, 14)
    place(sw, rot('Y', 90), (0.145, 0.118, 0.193))
    p.append(sw)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(lathe("Foot", [(0.0, 0.0), (0.014, 0.0), (0.016, 0.003), (0.016, 0.005), (0.0, 0.005)], RUB, 16,
                           xf=M4(rot('X', 180), (sx * 0.17, sy * 0.08, 0.005))))
    body = finish(p, "Boombox", sharp_deg=48)
    # The speaker grilles: a skin over the shell round each end, its UVs in 6 mm tiles of the perforation, repeated.
    tile = grille_tile()
    GR = print_mat("Plastic_BoomGrille", tile)
    GR["print_repeat"] = 1
    grilles = []
    for side in (-1, 1):
        rows = []
        zs = [0.03 + (0.127 - 0.03) * j / 14 for j in range(15)]
        base = end_path(0.08, side, 0.121, 0.065, 720)
        # sample the same angles at every height
        n = 2.8
        angles = []
        a0, b0 = shell_ab(0.08)
        for q, g in base[::12]:
            angles.append((q.x / a0, q.y / b0))
        bm = bmesh.new()
        V = []
        for z in zs:
            a, b = shell_ab(z)
            row = []
            for (ux, uy) in angles:
                x, y = ux * a, uy * b
                gg = Vector((math.copysign(abs(x / a) ** (n - 1), x) / a, math.copysign(abs(y / b) ** (n - 1), y) / b, 0)).normalized()
                row.append(bm.verts.new(Vector((x, y, z)) + gg * 0.0009))
            V.append(row)
        faces = []
        for j in range(len(V) - 1):
            for i in range(len(V[j]) - 1):
                faces.append(bm.faces.new((V[j][i], V[j][i + 1], V[j + 1][i + 1], V[j + 1][i])))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        for f in bm.faces:
            f.normal_update()
            c = f.calc_center_median()
            if f.normal.dot(Vector((c.x, c.y, 0))) < 0:
                f.normal_flip()
        uvl = bm.loops.layers.uv.new("UVMap")
        # u: along the outline (metres / 6 mm), v: up
        acc = [0.0]
        for i in range(1, len(V[0])):
            acc.append(acc[-1] + (V[7][i].co - V[7][i - 1].co).length)
        index = {}
        for j, row in enumerate(V):
            for i, v in enumerate(row):
                index[v] = (acc[i] / 0.006, zs[j] / 0.006)
        for f in bm.faces:
            for l in f.loops:
                l[uvl].uv = index[l.vert]
        ob = new_obj("Grille", bm, [GR])
        for poly in ob.data.polygons:
            poly.use_smooth = True
        grilles.append(ob)
    return join([body] + grilles, "Boombox")


# ------------------------------------------------------------------------------------------------- the face

def boombox_face():
    use_gpu(bpy.context.scene)
    W, H = 2 * ART_X, ART_S1 - ART_S0
    im = art(_tmp("bb_face"), W, H, 2048, lambda A, w, h: face_art(A, w, h), samples=24)
    FACE = img_mat("Print_BoomFace", im, 0.45)
    parts = []
    mid = (ART_S0 + ART_S1) / 2
    parts.append(card("Face", W, H, tuple(F(0.0, mid, 0.0006)), FACE, normal=tuple(NRM)))
    # the buttons' faces (on their bodies, 4.2 mm out), the knob's face, the small button's
    for (label, bx, bs, bw, bh) in FUNC:
        parts.append(card_uv("BtnFace", bw - 0.0012, bh - 0.0012, tuple(F(bx, bs, 0.0043)), FACE,
                             art_rect(bx, bs, bw - 0.0012, bh - 0.0012), normal=tuple(NRM)))
    kx, ks, kr = KNOB
    u0, v0, u1, v1 = art_rect(kx, ks, 2 * kr, 2 * kr)
    parts.append(disc_uv("KnobFace", tuple(F(kx, ks, 0.0099)), tuple(NRM), tuple(UPF), kr - 0.0016, FACE,
                         ((u0 + u1) / 2, (v0 + v1) / 2), ((u1 - u0) / 2 * (kr - 0.0016) / kr, (v1 - v0) / 2 * (kr - 0.0016) / kr)))
    u0, v0, u1, v1 = art_rect(SMALL[0], SMALL[1], 2 * SMALL[2], 2 * SMALL[2])
    parts.append(disc_uv("SmallFace", tuple(F(SMALL[0], SMALL[1], 0.0043)), tuple(NRM), tuple(UPF), SMALL[2] - 0.0008, FACE,
                         ((u0 + u1) / 2, (v0 + v1) / 2), ((u1 - u0) / 2 * 0.84, (v1 - v0) / 2 * 0.84)))
    # the LCD: its glass, unlit grey-green (lit: the glow map's backlight)
    lx, ls, lw, lh = LCD
    off = art(_tmp("bb_lcd_off"), lw, lh, 512, lambda A, w, h: lcd_art(A, w, h, False), samples=16)
    on = art(_tmp("bb_lcd_on"), lw, lh, 512, lambda A, w, h: lcd_art(A, w, h, True), samples=16)
    LM = img_mat("Glow_BoomLcd", off, 0.15)
    LM["glow_print"] = on.name
    parts.append(card("Lcd", lw, lh, tuple(F(lx, ls, 0.0012)), LM, normal=tuple(NRM)))
    # the LEDs' lenses: tiny domes (only the power LED lights)
    for k, (ex, es, col, on_) in enumerate(LEDS):
        m = S("Glow_BoomLed%d" % k, tuple(c * 0.35 for c in col), "glass", 0.15)
        m["glow_rgb"] = col if on_ else (0.0, 0.0, 0.0)
        parts.append(sphere("Led", tuple(F(ex, es, 0.0004)), (0.0015, 0.0015, 0.0015), m, 12, 6,
                            rot=R_FACE.to_3x3() if hasattr(R_FACE, "to_3x3") else None))
    # On top: the sticker, the model plate, the CD logo; at the back, the rating label and the battery door's print.
    st = sticker_mat("Print_BoomSticker", top_sticker, 0.075, 0.05, 512)
    c = card("Sticker", 0.075, 0.05, (-0.152, 0.012, DECK + 0.0004), st, normal=(0, 0, 1), roll=-8.0)
    parts.append(c)
    mp = sticker_mat("Print_BoomPlate", model_plate, 0.07, 0.02, 512)
    parts.append(card("Plate", 0.07, 0.02, (0.15, 0.035, DECK + 0.0004), mp, normal=(0, 0, 1)))
    cdm = sticker_mat("Print_BoomCdLogo", cd_logo, 0.03, 0.02, 256)
    parts.append(card("CdLogo", 0.03, 0.02, (0.0, 0.07, DECK + 0.0132), cdm, normal=(0, 0, 1)))
    bl = sticker_mat("Paper_BoomRating", back_label, 0.07, 0.04, 512, 0.6)
    parts.append(card("Rating", 0.07, 0.04, (0.06, 0.03, -0.0004), bl, normal=(0, 0, -1)))
    bd = sticker_mat("Print_BoomBattery", battery_door_art, 0.12, 0.05, 512)
    parts.append(card("BatteryPrint", 0.12, 0.05, (0.0, 0.1487, 0.07), bd, normal=(0, 1, 0)))
    return join(parts, "BoomboxFace")


def face_glow(m, nt, col):
    """The face's glow: the LCD's backlight (its lit print), the power LED; nothing else."""
    if m.get("glow_print") and bpy.data.images.get(m["glow_print"]):
        t = _n(nt, "ShaderNodeTexImage", -700, 700)
        t.image = bpy.data.images[m["glow_print"]]
        uv = _n(nt, "ShaderNodeUVMap", -900, 700)
        uv.uv_map = "Tile"
        nt.links.new(uv.outputs["UV"], t.inputs["Vector"])
        return t.outputs["Color"]
    if "glow_rgb" in m.keys():
        g = tuple(m["glow_rgb"])
        if max(g) <= 0.0:
            return None
        rgb = _n(nt, "ShaderNodeRGB", -700, 700)
        rgb.outputs[0].default_value = (*g, 1.0)
        return rgb.outputs[0]
    return None


# ------------------------------------------------------------------------------------------------- moving parts

LAYOUT = {}


def to_origin(ob, pivot):
    ob.data.transform(Matrix.Translation(-Vector(pivot)))
    ob.data.update()
    return ob


def key_art(sym, red):
    def build(A, W, H):
        A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.262, 0.251, 0.2)))       # (the key's own colour, linear)
        A.text(sym, H * 0.42, 0.0, 0.0, (0.55, 0.03, 0.02) if red else (0.04, 0.04, 0.04), "arialbd.ttf")
    return build


def boombox_key(i):
    def build():
        use_gpu(bpy.context.scene)
        KM = S("Plastic_BoomKey", (0.55, 0.54, 0.49), "plastic", 0.45)
        x = KEY_X[i]
        s = (KEY_S0 + KEY_S1) / 2
        # a piano key: its face leaning a little forward at the bottom, its top lip; standing 9 mm proud of the slot
        k = face_box("Key", x, s, KEY_W, KEY_S1 - KEY_S0, 0.009, KM, 0.0018, 0.0, 3)
        ribs = []
        im = art(_tmp("bb_key%d" % i), KEY_W - 0.004, 0.009, 128, key_art(KEY_SYMS[i], i == 5), samples=8)
        sym = card("Sym", KEY_W - 0.004, 0.009, tuple(F(x, s + 0.002, 0.0091)), img_mat("Print_BoomKeySym", im, 0.45), normal=tuple(NRM))
        hinge = F(x, KEY_S1, -0.002)
        LAYOUT["key%d" % i] = list(hinge)
        ob = join([finish([k], "Key", sharp_deg=50), sym], "BoomboxKey%d" % i)
        return to_origin(ob, hinge)
    return build


def boombox_door():
    use_gpu(bpy.context.scene)
    DK = S("Plastic_BoomDoor", (0.1, 0.1, 0.105), "plastic", 0.4)
    SMK = S("Glass_BoomDoorSmoke", (0.04, 0.045, 0.05), "glass", 0.06)
    BLK = S("Plastic_BoomHolder", (0.03, 0.03, 0.03), "plastic", 0.6)
    p = []
    w, s0, s1 = 2 * DOOR_X - 0.002, DOOR_S0 + 0.0005, DOOR_S1 - 0.0005
    sm = (s0 + s1) / 2
    wx0, wx1, ws0, ws1 = -0.064, 0.064, 0.012, 0.058
    # the frame round the window, its front flush with the panel's face
    for (x, s, ww, hh) in ((0.0, (s0 + ws0) / 2, w, ws0 - s0), (0.0, (ws1 + s1) / 2, w, s1 - ws1),
                           ((-w / 2 + wx0) / 2, (ws0 + ws1) / 2, wx0 + w / 2, ws1 - ws0), ((wx1 + w / 2) / 2, (ws0 + ws1) / 2, w / 2 - wx1, ws1 - ws0)):
        p.append(face_box("DoorFrame", x, s, ww, hh, 0.009, DK, 0.0012, -0.009))
    p.append(face_box("Window", 0.0, (ws0 + ws1) / 2, wx1 - wx0 + 0.002, ws1 - ws0 + 0.002, 0.002, SMK, 0.0, -0.0035))
    # the cassette holder's clips behind it, a finger notch at the top
    for sx in (-1, 1):
        p.append(face_box("Clip", sx * 0.056, s1 - 0.006, 0.008, 0.006, 0.006, BLK, 0.001, -0.016))
        p.append(face_box("Clip", sx * 0.056, s0 + 0.004, 0.008, 0.005, 0.006, BLK, 0.001, -0.016))
    p.append(face_box("Notch", 0.0, s1 - 0.0015, 0.03, 0.003, 0.0015, BLK, 0.0005, -0.0005))
    ob = finish(p, "BoomboxDoor", sharp_deg=50)
    im = art(_tmp("bb_door"), wx1 - wx0, ws1 - ws0, 512, door_art, samples=16)
    DM = img_mat("Print_BoomDoorGlass", im, 0.1)
    pr = card("DoorPrint", wx1 - wx0, ws1 - ws0, tuple(F(0.0, (ws0 + ws1) / 2, -0.0013)), DM, normal=tuple(NRM))
    ob = join([ob, pr], "BoomboxDoor")
    hinge = F(0.0, DOOR_S0, -0.0045)
    LAYOUT["door"] = list(hinge)
    # where the cassette stands in it: label face 4 mm behind the window; its origin (the back of it, standing) 16 mm in
    cas = F(0.0, (DOOR_S0 + DOOR_S1) / 2, -0.0165)
    LAYOUT["cassette"] = list(cas - hinge)
    LAYOUT["tilt_deg"] = math.degrees(TH)
    return to_origin(ob, hinge)


def boombox_handle():
    use_gpu(bpy.context.scene)
    HM = S("Paint_BoomHandle", (0.66, 0.665, 0.67), "paint", 0.4)
    GRP = S("Rubber_BoomHandleGrip", (0.12, 0.12, 0.125), "rubber", 0.7)
    pivot = Vector((0.0, 0.105, DECK + 0.007))
    # folded forward over the top: two arms from the pivots by the back along the top, the grip across the front
    arm_len = 0.185
    pts = []
    for sx in (-1, 1):
        a = [Vector((sx * 0.118, 0.105, DECK + 0.007)), Vector((sx * 0.118, 0.105 - arm_len + 0.02, DECK + 0.007)),
             Vector((sx * 0.105, 0.105 - arm_len, DECK + 0.007))]
        pts.append(a)

    def fr(i, T):
        A = Vector((0, 0, 1)) - T * T.z
        A = A.normalized() if A.length > 1e-6 else Vector((0, 0, 1))
        return A, T.cross(A).normalized()
    p = []
    for a in pts:
        path = [Vector(q[:3]) for q in catmull(a, 6)]
        p.append(sweep_profile("Arm", path, fr, lambda i: superellipse_pts(0.012, 0.018, 16), HM))
    grip = [Vector((-0.105, 0.105 - arm_len, DECK + 0.007)), Vector((0.105, 0.105 - arm_len, DECK + 0.007))]
    grip = [grip[0].lerp(grip[1], t / 12) for t in range(13)]
    p.append(sweep_profile("Grip", grip, fr, lambda i: superellipse_pts(0.014, 0.024, 16), HM))
    p.append(sweep_profile("GripRubber", grip[2:-2], fr, lambda i: superellipse_pts(0.0145, 0.0242, 16), GRP))
    for sx in (-1, 1):
        pv = lathe("Pivot", [(0.0, 0.0), (0.009, 0.0), (0.009, 0.006), (0.0, 0.006)], HM, 16)
        place(pv, rot('Y', 90 * sx), (sx * 0.124, 0.105, DECK + 0.007))
        p.append(pv)
    ob = finish(p, "BoomboxHandle", sharp_deg=50)
    LAYOUT["handle"] = list(pivot)
    LAYOUT["handle_grip"] = list(Vector((0.0, 0.105 - arm_len, DECK + 0.007)) - pivot)
    return to_origin(ob, pivot)


def superellipse_pts(w, t, n=16, p=3.0):
    out = []
    for k in range(n):
        a = 2 * math.pi * k / n
        c, s = math.cos(a), math.sin(a)
        out.append((w / 2 * math.copysign(abs(c) ** (2 / p), c), t / 2 * math.copysign(abs(s) ** (2 / p), s)))
    return out


def sweep_profile(name, pts, frame_fn, section_fn, material):
    pts = [Vector(q) for q in pts]
    n = len(pts)
    rings = []
    for i in range(n):
        T = (pts[min(n - 1, i + 1)] - pts[max(0, i - 1)]).normalized()
        A, B = frame_fn(i, T)
        rings.append([pts[i] + A * a + B * b for (a, b) in section_fn(i)])
    return loft(name, rings, material, True, True, recalc=True)


# ------------------------------------------------------------------------------------------------- the cassette

CW, CD_, CT_ = 0.1004, 0.0638, 0.012     # long, across (the tape's edge toward -y), thick


def cassette_label(A, W, H):
    """The label: a store-bought blank's (an invented SENTRA C60) printed band and lines, written on in ballpoint;
    the window cut out of it shows the tape on its hubs."""
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.05, 0.05, 0.055)))
    lw, lh = 0.088, 0.054
    A.rect(-lw / 2, -lh / 2 + 0.004, lw / 2, lh / 2, A.emit((0.93, 0.91, 0.84)), z=0.001)
    A.rect(-lw / 2, lh / 2 - 0.011, lw / 2, lh / 2, A.emit((0.1, 0.28, 0.62)), z=0.0015)
    A.text("SENTRA", 0.0065, -lw / 2 + 0.003, lh / 2 - 0.0055, (0.97, 0.97, 0.95), "ariblk.ttf", align="LEFT", z=0.002, shear=0.15)
    A.text("C60  NORMAL POSITION  TYPE I", 0.0024, lw / 2 - 0.003, lh / 2 - 0.0058, (0.9, 0.92, 0.98), "arialbd.ttf", align="RIGHT", z=0.002)
    A.rect(-lw / 2 + 0.002, lh / 2 - 0.0215, -lw / 2 + 0.008, lh / 2 - 0.0135, A.emit((0.1, 0.28, 0.62)), z=0.0015)
    A.text("A", 0.0055, -lw / 2 + 0.005, lh / 2 - 0.0177, (0.97, 0.97, 0.95), "ariblk.ttf", z=0.002)
    # the window: smoky clear plastic over the two packs of brown tape on their white hubs (only through it: the
    # label round it hides the rest)
    ww, wh = 0.05, 0.0175
    wy = -0.0045
    A.rect(-ww / 2, wy - wh / 2, ww / 2, wy + wh / 2, A.emit((0.1, 0.09, 0.085)), z=0.002)
    for (hx, rr) in ((-0.0213, 0.0215), (0.0213, 0.0135)):
        A.disc(hx, wy, rr, A.grad((0.13, 0.07, 0.04), (0.22, 0.13, 0.07)), z=0.0025)
    # the label again round the window (over the packs' edges), and the shell beyond the label
    paper = A.emit((0.93, 0.91, 0.84))
    shell = A.emit((0.05, 0.05, 0.055))
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, -lh / 2 + 0.004, shell, z=0.003)
    A.rect(-W / 2 - 0.01, lh / 2, W / 2 + 0.01, H / 2 + 0.01, shell, z=0.003)
    A.rect(-W / 2 - 0.01, -H / 2, -lw / 2, H / 2, shell, z=0.003)
    A.rect(lw / 2, -H / 2, W / 2 + 0.01, H / 2, shell, z=0.003)
    A.rect(-lw / 2, wy + wh / 2, lw / 2, lh / 2 - 0.011, paper, z=0.003)
    A.rect(-lw / 2, -lh / 2 + 0.004, lw / 2, wy - wh / 2, paper, z=0.003)
    A.rect(-lw / 2, wy - wh / 2, -ww / 2, wy + wh / 2, paper, z=0.003)
    A.rect(ww / 2, wy - wh / 2, lw / 2, wy + wh / 2, paper, z=0.003)
    for k in range(2):
        y = lh / 2 - 0.0165 - k * 0.0048
        A.rect(-lw / 2 + 0.01, y, lw / 2 - 0.003, y + 0.00025, A.emit((0.55, 0.6, 0.75)), z=0.0035)
    A.rect(-lw / 2 + 0.002, lh / 2 - 0.0215, -lw / 2 + 0.008, lh / 2 - 0.0135, A.emit((0.1, 0.28, 0.62)), z=0.0035)
    A.text("A", 0.0055, -lw / 2 + 0.005, lh / 2 - 0.0177, (0.97, 0.97, 0.95), "ariblk.ttf", z=0.004)
    A.text("lunch break mix", 0.0072, 0.002, lh / 2 - 0.015, (0.08, 0.12, 0.45), "Inkfree.ttf", z=0.004, shear=0.05)
    A.text("side A  -  1997", 0.0042, 0.0, -lh / 2 + 0.0095, (0.08, 0.12, 0.45), "Inkfree.ttf", z=0.004)
    for (hx, rr) in ((-0.0213, 0.0215), (0.0213, 0.0135)):
        A.disc(hx, wy, 0.0063, A.emit((0.86, 0.86, 0.84)), z=0.0045)
        A.disc(hx, wy, 0.0042, A.emit((0.05, 0.05, 0.05)), z=0.005)
        for t in range(6):
            a = 2 * math.pi * t / 6
            A.disc(hx + math.cos(a) * 0.0042, wy + math.sin(a) * 0.0042, 0.0009, A.emit((0.86, 0.86, 0.84)), z=0.0055)
    A.rect(-ww / 2 - 0.0008, wy + wh / 2, ww / 2 + 0.0008, wy + wh / 2 + 0.0008, A.emit((0.03, 0.03, 0.03)), z=0.006)
    A.rect(-ww / 2 - 0.0008, wy - wh / 2 - 0.0008, ww / 2 + 0.0008, wy - wh / 2, A.emit((0.03, 0.03, 0.03)), z=0.006)


def cassette_back(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.05, 0.05, 0.055)))
    lw, lh = 0.088, 0.054
    A.rect(-lw / 2, -lh / 2 + 0.004, lw / 2, lh / 2, A.emit((0.93, 0.91, 0.84)), z=0.001)
    A.rect(-lw / 2, lh / 2 - 0.011, lw / 2, lh / 2, A.emit((0.1, 0.28, 0.62)), z=0.0015)
    A.text("SENTRA", 0.0065, -lw / 2 + 0.003, lh / 2 - 0.0055, (0.97, 0.97, 0.95), "ariblk.ttf", align="LEFT", z=0.002, shear=0.15)
    A.rect(-lw / 2 + 0.002, lh / 2 - 0.0215, -lw / 2 + 0.008, lh / 2 - 0.0135, A.emit((0.1, 0.28, 0.62)), z=0.0015)
    A.text("B", 0.0055, -lw / 2 + 0.005, lh / 2 - 0.0177, (0.97, 0.97, 0.95), "ariblk.ttf", z=0.002)
    for k in range(3):
        y = lh / 2 - 0.016 - k * 0.0048
        A.rect(-lw / 2 + 0.01, y, lw / 2 - 0.003, y + 0.00025, A.emit((0.55, 0.6, 0.75)), z=0.0015)
    ww, wh = 0.05, 0.0175
    wy = -0.0045
    A.rect(-ww / 2, wy - wh / 2, ww / 2, wy + wh / 2, A.emit((0.11, 0.1, 0.1)), z=0.002)
    for (hx, rr) in ((-0.0215, 0.0105), (0.0215, 0.0175)):
        A.disc(hx, wy, rr, A.grad((0.16, 0.09, 0.05), (0.26, 0.16, 0.09)), z=0.003)
        A.disc(hx, wy, 0.0063, A.emit((0.86, 0.86, 0.84)), z=0.0035)
        A.disc(hx, wy, 0.0042, A.emit((0.05, 0.05, 0.05)), z=0.004)


def cassette_edge(A, W, H):
    """The tape's edge (the long side toward the deck's heads): the openings, the brown tape across them, the felt pad."""
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.05, 0.05, 0.055)))
    for (ox, ow) in ((-0.036, 0.008), (-0.019, 0.0105), (0.0, 0.017), (0.019, 0.0105), (0.036, 0.008)):
        A.rect(ox - ow / 2, -H * 0.42, ox + ow / 2, H * 0.32, A.emit((0.01, 0.01, 0.01)), z=0.001)
    A.rect(-W * 0.45, -H * 0.12, W * 0.45, H * 0.12, A.emit((0.22, 0.13, 0.07)), z=0.002)
    A.rect(-0.004, -H * 0.42, 0.004, -H * 0.12, A.emit((0.55, 0.5, 0.42)), z=0.003)


def cassette():
    use_gpu(bpy.context.scene)
    SH = S("Plastic_CassetteShell", (0.06, 0.06, 0.065), "plastic", 0.35)
    SC = S("Steel_CassetteScrew", (0.35, 0.35, 0.36), "metal", 0.4, 1.0)
    p = [rbox("Shell", (CW, CD_, CT_), (0.0, 0.0, CT_ / 2), SH, 0.0016, 2)]
    # the thicker ridge along the tape's edge (the head side is a raised trapezoid on real shells)
    p.append(rbox("Ridge", (0.068, 0.0095, CT_ + 0.0012), (0.0, -CD_ / 2 + 0.00475, CT_ / 2), SH, 0.0012, 1))
    # five screws on each face (the corners and the middle by the tape's edge)
    for z, nz in ((CT_ + 0.0001, 1), (-0.0001, -1)):
        for (sx, sy) in ((-0.045, 0.026), (0.045, 0.026), (-0.045, -0.026), (0.045, -0.026), (0.0, -0.025)):
            sc = sphere("Screw", (sx, sy, z), (0.0021, 0.0021, 0.0006), SC, 10, 4)
            p.append(sc)
    ob = finish(p, "Cassette", sharp_deg=45)
    im = art(_tmp("cas_label"), CW, CD_, 900, cassette_label, samples=24)
    ib = art(_tmp("cas_back"), CW, CD_, 600, cassette_back, samples=16)
    ie = art(_tmp("cas_edge"), CW, CT_, 600, cassette_edge, samples=8)
    top = card("LabelA", CW - 0.003, CD_ - 0.003, (0.0, 0.0, CT_ + 0.0007), img_mat("Paper_CassetteA", im, 0.55), normal=(0, 0, 1))
    for l in top.data.uv_layers[0].data:
        u, v = l.uv
        l.uv = (0.5 + (u - 0.5) * (CW - 0.003) / CW, 0.5 + (v - 0.5) * (CD_ - 0.003) / CD_)
    bot = card("LabelB", CW - 0.003, CD_ - 0.003, (0.0, 0.0, -0.0007), img_mat("Paper_CassetteB", ib, 0.55), normal=(0, 0, -1))
    edge = card("Edge", 0.068, CT_ - 0.001, (0.0, -CD_ / 2 - 0.0007, CT_ / 2), img_mat("Print_CassetteEdge", ie, 0.4), normal=(0, -1, 0))
    for l in edge.data.uv_layers[0].data:
        u, v = l.uv
        l.uv = (0.5 + (u - 0.5) * 0.068 / CW, v)
    # side A's write-protect tab broken out: a dark slot on the back edge (the other tab still there)
    c = Canvas(32, 16, (0.05, 0.05, 0.055))
    c.rect(6, 3, 26, 13, (0.005, 0.005, 0.005))
    tab = card("Tab", 0.0065, 0.004, (-0.04, CD_ / 2 + 0.0006, CT_ * 0.6), print_mat("Print_CassetteTab", c), normal=(0, 1, 0))
    return join([ob, top, bot, edge, tab], "Cassette")


def write_layout():
    os.makedirs(os.path.abspath(OUT), exist_ok=True)
    if not LAYOUT:
        return
    path = os.path.join(os.path.abspath(OUT), "Boombox_Layout.json")
    if os.path.exists(path):
        old = json.load(open(path, encoding="utf-8"))
        old.update(LAYOUT)
        LAYOUT.update(old)
    LAYOUT["keys"] = KEY_NAMES
    LAYOUT["key_x"] = KEY_X
    with open(os.path.join(os.path.abspath(OUT), "Boombox_Layout.json"), "w", encoding="utf-8") as fh:
        json.dump({k: v for k, v in LAYOUT.items()}, fh, indent=1)
    print("[boombox] layout", LAYOUT)


models = [
    ("Boombox", boombox, (0.65, -1.0, 0.45), {"res": 2048, "scale": 0.35, "wear": 0.55, "dirt": 0.7}),
    ("BoomboxFace", boombox_face, (0.4, -1.0, 0.35), {"res": 2048, "scale": 0.2, "wear": 0.3, "dirt": 0.35, "glow": face_glow}),
    ("BoomboxDoor", boombox_door, (0.4, -1.0, 0.2), {"res": 512, "scale": 0.15, "wear": 0.3, "dirt": 0.4}),
    ("BoomboxHandle", boombox_handle, (0.6, -1.0, 0.5), {"res": 512, "scale": 0.2, "wear": 0.6, "dirt": 0.5}),
    ("Cassette", cassette, (0.5, -1.0, 0.8), {"res": 1024, "scale": 0.08, "wear": 0.4, "dirt": 0.4}),
]
for i in range(6):
    models.append(("BoomboxKey%d" % i, boombox_key(i), (0.3, -1.0, 0.3), {"res": 128, "scale": 0.05, "wear": 0.5, "dirt": 0.5}))
run_baked(models)
write_layout()
print("[boombox] done ->", OUT)
