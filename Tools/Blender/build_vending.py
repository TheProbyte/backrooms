"""
A drinks vending machine (2026-11-04), built in Blender and baked (bake_kit.py). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_vending.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>]

The common full-front can and bottle machine at the industry's average size, 72 x 39 x 33 in (1.83 x 0.99 x 0.84 m,
about 290 kg): a steel cabinet painted black on levelling feet, a full-height door with a large backlit sign behind a
polycarbonate face in a bright trim, a selection column (a price display, eight backlit buttons with their flavours,
the coin slot, a bill validator, the coin return button and its cup, an EXACT CHANGE lamp), a T-handle lock, the
delivery bin with its PUSH flap, a louvred kick grille, the condenser's grille at the back and its power cord.

  - VendingMachine: everything that does not glow.
  - VendingGlow: the sign's face, the buttons' faces and the display (lit by the game when the machine has power).
  - VendingPlug: its moulded 15 A plug.

No real brand: the sign says ICE COLD DRINKS. Frame: origin on the floor at the middle of its foot, front toward -Y
(Unity +Z), its back against the wall at +Y. Unity (x, y, z) = (-x, z, -y).
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

W_, H_, D_ = 0.99, 1.83, 0.84
FRONT = -D_ / 2                  # the door's face
SIGN_X0, SIGN_X1, SIGN_Z0, SIGN_Z1 = -0.455, 0.17, 0.45, 1.74
COL_X0, COL_X1 = 0.2, 0.47
FLAVOURS = [("COLA", (0.55, 0.05, 0.05)), ("DIET", (0.62, 0.62, 0.64)), ("LEMON LIME", (0.18, 0.55, 0.15)),
            ("ORANGE", (0.92, 0.45, 0.05)), ("ROOT BEER", (0.4, 0.2, 0.08)), ("GRAPE", (0.4, 0.12, 0.5)),
            ("ICED TEA", (0.75, 0.5, 0.1)), ("WATER", (0.12, 0.35, 0.75))]
BTN_Z0, BTN_DZ = 1.5, 0.068


def card_uv(name, w, h, center, material, rect, normal=(0, -1, 0)):
    """card() with its UVs over a part of the print: rect = (u0, v0, u1, v1)."""
    ob = card(name, w, h, center, material, normal)
    u0, v0, u1, v1 = rect
    for l in ob.data.uv_layers[0].data:
        l.uv = (u0 + (u1 - u0) * l.uv[0], v0 + (v1 - v0) * l.uv[1])
    return ob


def vending_machine():
    BLK = S("Paint_VendCabinet", (0.06, 0.06, 0.065), "paint", 0.4)
    RED = S("Paint_VendDoor", (0.5, 0.05, 0.05), "paint", 0.45)
    TRIM = S("Chrome_VendTrim", (0.8, 0.8, 0.8), "metal", 0.2, 1.0)
    DK = S("Plastic_VendBezel", (0.05, 0.05, 0.05), "plastic", 0.45)
    BIN = S("Plastic_VendBin", (0.03, 0.03, 0.03), "plastic", 0.6)
    RUB = S("Rubber_VendFoot", (0.05, 0.05, 0.05), "rubber", 0.6)
    p = []
    # The cabinet behind the door, on four levelling feet.
    p.append(rbox("Cabinet", (W_ - 0.004, D_ - 0.06, H_ - 0.03), (0, 0.03, 0.03 + (H_ - 0.03) / 2), BLK, 0.01, 3))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(lathe("Foot", [(0.0, 0.0), (0.03, 0.0), (0.03, 0.008), (0.012, 0.012), (0.012, 0.035), (0.0, 0.035)], RUB, 12,
                           xf=M4(None, (sx * 0.43, sy * 0.33, 0.0))))
    # The door: rails and stiles round the sign's window, the selection column, the bottom with the bin.
    dt, y = 0.05, FRONT + 0.025
    def door(name, x0, x1, z0, z1, mat=RED, bev=0.006, yo=0.0, t=dt):
        p.append(rbox(name, (x1 - x0, t, z1 - z0), ((x0 + x1) / 2, y + yo, (z0 + z1) / 2), mat, bev, 2))
    door("Bottom", -W_ / 2 + 0.005, W_ / 2 - 0.005, 0.045, SIGN_Z0)
    door("Top", -W_ / 2 + 0.005, W_ / 2 - 0.005, SIGN_Z1, H_ - 0.01)
    door("Left", -W_ / 2 + 0.005, SIGN_X0, SIGN_Z0, SIGN_Z1)
    door("Mid", SIGN_X1, COL_X0, SIGN_Z0, SIGN_Z1)
    door("Right", COL_X1, W_ / 2 - 0.005, SIGN_Z0, SIGN_Z1)
    door("Column", COL_X0, COL_X1, SIGN_Z0, SIGN_Z1, DK, 0.004, -0.006, 0.04)
    # The sign's recess (its lit face is VendingGlow), its bright trim round the window.
    p.append(rbox("SignBack", (SIGN_X1 - SIGN_X0, 0.01, SIGN_Z1 - SIGN_Z0), ((SIGN_X0 + SIGN_X1) / 2, FRONT + 0.05, (SIGN_Z0 + SIGN_Z1) / 2), DK, 0.0, 1))
    for (x0, x1, z0, z1) in ((SIGN_X0 - 0.012, SIGN_X1 + 0.012, SIGN_Z1, SIGN_Z1 + 0.012), (SIGN_X0 - 0.012, SIGN_X1 + 0.012, SIGN_Z0 - 0.012, SIGN_Z0),
                             (SIGN_X0 - 0.012, SIGN_X0, SIGN_Z0, SIGN_Z1), (SIGN_X1, SIGN_X1 + 0.012, SIGN_Z0, SIGN_Z1)):
        p.append(rbox("Trim", (x1 - x0, 0.012, z1 - z0), ((x0 + x1) / 2, FRONT - 0.004, (z0 + z1) / 2), TRIM, 0.004, 2))
    # The selection buttons' bezels (their faces glow), the price display's window.
    cx = (COL_X0 + COL_X1) / 2
    for i in range(len(FLAVOURS)):
        z = BTN_Z0 - i * BTN_DZ
        p.append(rbox("Bezel", (0.21, 0.012, 0.058), (cx, FRONT - 0.002, z), TRIM, 0.004, 2))
    p.append(rbox("DisplayBezel", (0.16, 0.012, 0.06), (cx, FRONT - 0.002, 1.62), DK, 0.004, 2))
    # The money: a coin slot, the bill validator, the coin return button, its cup; the EXACT CHANGE lamp's bezel.
    p.append(rbox("CoinPlate", (0.06, 0.012, 0.085), (cx - 0.06, FRONT - 0.004, 0.9), TRIM, 0.006, 2))
    p.append(rbox("CoinSlot", (0.004, 0.004, 0.032), (cx - 0.06, FRONT - 0.0105, 0.9), BIN, 0.0, 1))
    p.append(rbox("BillBezel", (0.11, 0.03, 0.06), (cx + 0.045, FRONT - 0.012, 0.8), DK, 0.008, 2))
    p.append(rbox("BillSlot", (0.08, 0.004, 0.004), (cx + 0.045, FRONT - 0.0275, 0.81), BIN, 0.0, 1))
    p.append(lathe("Return", [(0.0, 0.0), (0.016, 0.0), (0.016, 0.008), (0.012, 0.012), (0.0, 0.013)], TRIM, 18,
                   xf=M4(rot('X', 90), (cx - 0.06, FRONT - 0.002, 0.8))))
    p.append(rbox("CupFrame", (0.1, 0.03, 0.07), (cx, FRONT - 0.008, 0.62), TRIM, 0.006, 2))
    p.append(rbox("CupHole", (0.08, 0.004, 0.05), (cx, FRONT - 0.0235, 0.62), BIN, 0.0, 1))
    p.append(rbox("LampBezel", (0.1, 0.01, 0.022), (cx, FRONT - 0.002, 0.98), DK, 0.003, 1))
    # The T-handle lock, the hinge side's pins.
    p.append(lathe("LockRose", [(0.0, 0.0), (0.022, 0.0), (0.022, 0.006), (0.016, 0.01), (0.0, 0.01)], TRIM, 18,
                   xf=M4(rot('X', 90), (cx, FRONT - 0.002, 0.52))))
    p.append(rbox("TBar", (0.012, 0.016, 0.08), (cx, FRONT - 0.02, 0.52), TRIM, 0.005, 2))
    for z in (0.3, 1.5):
        p.append(lathe("Hinge", [(0.0, 0.0), (0.008, 0.0), (0.008, 0.06), (0.0, 0.06)], BLK, 10, xf=M4(None, (-W_ / 2 + 0.002, FRONT + 0.01, z))))
    # The delivery bin: a dark opening in the bottom with its push flap, a trim round it.
    bx0, bx1, bz0, bz1 = -0.4, 0.12, 0.14, 0.36
    p.append(rbox("BinHole", (bx1 - bx0, 0.004, bz1 - bz0), ((bx0 + bx1) / 2, FRONT - 0.0005, (bz0 + bz1) / 2), BIN, 0.0, 1))
    flap = rbox("Flap", (bx1 - bx0 - 0.03, 0.006, bz1 - bz0 - 0.05), (0, 0, 0), DK, 0.003, 1)
    place(flap, rot('X', -6), ((bx0 + bx1) / 2, FRONT - 0.002, (bz0 + bz1) / 2 + 0.005))
    p.append(flap)
    for (x0, x1, z0, z1) in ((bx0 - 0.015, bx1 + 0.015, bz1, bz1 + 0.015), (bx0 - 0.015, bx1 + 0.015, bz0 - 0.015, bz0),
                             (bx0 - 0.015, bx0, bz0, bz1), (bx1, bx1 + 0.015, bz0, bz1)):
        p.append(rbox("BinTrim", (x1 - x0, 0.012, z1 - z0), ((x0 + x1) / 2, FRONT - 0.004, (z0 + z1) / 2), TRIM, 0.004, 2))
    # The kick grille's louvres, the condenser grille at the back, the cord's grommet.
    for k in range(5):
        lv = rbox("Louvre", (W_ - 0.08, 0.004, 0.012), (0, 0, 0), BLK, 0.0015, 1)
        place(lv, rot('X', 30), (0, FRONT + 0.004, 0.06 + k * 0.012))
        p.append(lv)
    for k in range(10):
        p.append(rbox("BackSlat", (W_ - 0.12, 0.006, 0.01), (0, D_ / 2 + 0.002, 0.1 + k * 0.028), BLK, 0.002, 1))
    p.append(lathe("Grommet", [(0.0, 0.0), (0.012, 0.0), (0.012, 0.01), (0.0, 0.01)], RUB, 12, xf=M4(rot('X', -90), (0.4, D_ / 2, 0.12))))
    base = finish(p, "VendingMachine", sharp_deg=50)
    # Small prints: PUSH on the flap, the maker's plate on the side, a sticker of instructions by the coin slot.
    c = Canvas(128, 48, (0.05, 0.05, 0.05))
    c.text_c("PUSH", 64, 12, 3, (0.85, 0.85, 0.82))
    push = card("Push", 0.1, 0.035, ((bx0 + bx1) / 2, FRONT - 0.0052, (bz0 + bz1) / 2 + 0.03), print_mat("Print_VendPush", c))
    c2 = Canvas(192, 96, (0.92, 0.92, 0.9))
    c2.text_c("INSERT COINS", 96, 8, 2, (0.1, 0.1, 0.1))
    c2.text_c("OR BILLS", 96, 26, 2, (0.1, 0.1, 0.1))
    c2.text_c("THEN SELECT", 96, 48, 2, (0.1, 0.1, 0.1))
    c2.lines(14, 72, 178, 2, 8, (0.4, 0.4, 0.4), seed=8)
    instr = card("Instructions", 0.1, 0.05, (cx, FRONT - 0.0065, 0.715), print_mat("Print_VendInstructions", c2))
    c3 = Canvas(160, 96, (0.72, 0.72, 0.7))
    c3.text_c("REFRIGERATED", 80, 10, 2, (0.1, 0.1, 0.1))
    c3.text_c("VENDOR", 80, 28, 2, (0.1, 0.1, 0.1))
    c3.lines(12, 52, 148, 4, 8, (0.3, 0.3, 0.3), seed=11)
    plate = card("Plate", 0.08, 0.048, (W_ / 2 + 0.0004, 0.25, 0.3), print_mat("Alu_VendPlate", c3), normal=(1, 0, 0))
    return join([base, push, instr, plate], "VendingMachine")


def sign_canvas():
    """The backlit sign (512 x 1024 over its 0.625 x 1.29 m): a red field with a white wave, ICE COLD DRINKS, a glass of
    cola full of ice and three cans beaded with cold."""
    w, h = 512, 1024
    c = Canvas(w, h, (0.6, 0.04, 0.04))
    for y in range(h):
        t = y / h
        col = (0.72 - 0.3 * t, 0.05 + 0.02 * t, 0.05)
        c.rect(0, y, w, y + 1, col)
    # the wave
    for x in range(w):
        yc = 600 + 50 * math.sin(x / w * 2 * math.pi * 0.8 + 0.6)
        c.rect(x, yc - 26, x + 1, yc + 26, (0.97, 0.96, 0.93))
        c.rect(x, yc + 26, x + 1, yc + 34, (0.75, 0.75, 0.74))
    c.text_c("ICE COLD", w / 2, 60, 12, (0.98, 0.97, 0.94))
    c.text_c("DRINKS", w / 2, 170, 9, (0.98, 0.97, 0.94))
    # three cans: bodies, a lighter stripe down each, beads of condensation, the tops.
    rnd = random.Random(5)
    for k, (col, x0) in enumerate((((0.5, 0.04, 0.04), 90), ((0.75, 0.75, 0.78), 200), ((0.18, 0.55, 0.15), 310))):
        c.rect(x0, 300, x0 + 100, 520, col)
        c.rect(x0 + 18, 300, x0 + 32, 520, tuple(min(1.0, v + 0.35) for v in col))
        c.rect(x0, 300, x0 + 100, 312, (0.85, 0.85, 0.86))
        c.rect(x0 + 4, 290, x0 + 96, 300, (0.7, 0.7, 0.72))
        c.text_c(["COLA", "DIET", "LIME"][k], x0 + 50, 380, 3, (0.97, 0.96, 0.93))
        for _ in range(40):
            c.disc(x0 + rnd.uniform(6, 94), rnd.uniform(316, 516), rnd.uniform(1.2, 2.6), tuple(min(1.0, v + 0.45) for v in col))
    c.text_c("REFRESHING", w / 2, 700, 4, (0.6, 0.04, 0.04))
    c.text_c("ANY TIME", w / 2, 780, 6, (0.98, 0.97, 0.94))
    c.text_c("1.25", w / 2, 880, 9, (0.98, 0.9, 0.3))
    return c


def sign_render(path, px=(640, 1320)):
    """The sign's artwork made as a sign shop would, rendered: a deep red field lit from above, a sweeping white ribbon,
    ICE COLD DRINKS in a heavy sans with a soft shadow, three cans beaded with cold standing in crushed ice, the price.
    An orthographic Cycles render of a small scene of its own (its own Blender scene, removed after)."""
    import bpy
    prev = bpy.context.window.scene if bpy.context.window else bpy.context.scene
    sc = bpy.data.scenes.new("SignArt")
    try:
        bpy.context.window.scene = sc
    except Exception:
        pass
    col = sc.collection
    W, H = SIGN_X1 - SIGN_X0, SIGN_Z1 - SIGN_Z0

    def m_emit(name, rgb, strength=1.0):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        for n in list(nt.nodes):
            if n.type != "OUTPUT_MATERIAL":
                nt.nodes.remove(n)
        e = nt.nodes.new("ShaderNodeEmission")
        e.inputs["Color"].default_value = (*rgb, 1)
        e.inputs["Strength"].default_value = strength
        nt.links.new(e.outputs[0], next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL").inputs["Surface"])
        return m

    def m_lit(name, rgb, rough, metal=0.0, coat=0.0):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        b.inputs["Base Color"].default_value = (*rgb, 1)
        b.inputs["Roughness"].default_value = rough
        b.inputs["Metallic"].default_value = metal
        try:
            b.inputs["Coat Weight"].default_value = coat
        except KeyError:
            pass
        return m

    def obj(name, data, mats=()):
        o = bpy.data.objects.new(name, data)
        col.objects.link(o)
        for m in mats:
            o.data.materials.append(m)
        return o

    # The field: a gradient (bright at the top), as a big emissive plane with a ramp.
    bg = m_emit("SignBG", (1, 1, 1))
    nt = bg.node_tree
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.22, 0.008, 0.01, 1)
    ramp.color_ramp.elements[1].color = (0.75, 0.03, 0.03, 1)
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    nt.links.new(sep.outputs["Y"], ramp.inputs["Fac"])
    em = next(n for n in nt.nodes if n.type == "EMISSION")
    nt.links.new(ramp.outputs["Color"], em.inputs["Color"])
    me = bpy.data.meshes.new("BG")
    me.from_pydata([(-W / 2, -H / 2, 0), (W / 2, -H / 2, 0), (W / 2, H / 2, 0), (-W / 2, H / 2, 0)], [], [(0, 1, 2, 3)])
    obj("BG", me, [bg])
    # The ribbon: a band swept across, white with a grey edge.
    def band(name, yc, amp, th, rgb, z):
        vs, fs = [], []
        n = 64
        for i in range(n + 1):
            x = -W / 2 - 0.02 + (W + 0.04) * i / n
            y = yc + amp * math.sin(i / n * math.pi * 1.6 + 0.4)
            vs += [(x, y - th / 2, z), (x, y + th / 2, z)]
        for i in range(n):
            fs.append((2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1))
        m2 = bpy.data.meshes.new(name)
        m2.from_pydata(vs, [], fs)
        obj(name, m2, [m_emit(name + "M", rgb, 1.0)])
    band("RibbonEdge", -0.1, 0.07, 0.11, (0.55, 0.55, 0.56), 0.001)
    band("Ribbon", -0.1, 0.07, 0.085, (0.95, 0.94, 0.92), 0.002)

    def text(body, size, y, rgb, z=0.003, shadow=True, x=0.0):
        cu = bpy.data.curves.new("T", "FONT")
        cu.body = body
        cu.size = size
        cu.align_x = "CENTER"
        cu.align_y = "CENTER"
        cu.space_character = 1.02
        cu.extrude = 0.0
        try:
            cu.font = bpy.data.fonts.load("C:/Windows/Fonts/ariblk.ttf")
        except Exception:
            pass
        o = obj("Text", cu, [m_emit("TextM", rgb, 1.0)])
        o.location = (x, y, z)
        if shadow:
            cs = cu.copy()
            so = obj("Shadow", cs, [m_emit("ShadowM", (0.08, 0.0, 0.0), 1.0)])
            so.location = (x + size * 0.04, y - size * 0.05, z - 0.0005)
        return o
    text("ICE COLD", 0.11, H / 2 - 0.12, (0.98, 0.97, 0.94))
    text("DRINKS", 0.085, H / 2 - 0.235, (0.98, 0.97, 0.94))
    text("REFRESHING", 0.045, -0.235, (1.0, 0.85, 0.25), shadow=True, z=0.004)
    text("ANY TIME", 0.06, -0.36, (0.98, 0.97, 0.94))
    text("$1.25", 0.09, -H / 2 + 0.12, (1.0, 0.85, 0.25))

    # Three cans in crushed ice, lit from the upper left, beaded with cold (small glossy spheres).
    rnd = random.Random(3)
    for k, (cx, rgb, lab) in enumerate(((-0.17, (0.55, 0.02, 0.02), "COLA"), (0.0, (0.72, 0.73, 0.76), "DIET"), (0.17, (0.08, 0.45, 0.1), "LIME"))):
        cy = 0.12 + (0.02 if k == 1 else 0.0)
        bm = bpy.data.meshes.new("Can")
        import bmesh as _bm
        bb = _bm.new()
        _bm.ops.create_cone(bb, cap_ends=True, segments=48, radius1=0.066, radius2=0.066, depth=0.24)
        bb.to_mesh(bm)
        bb.free()
        can = obj("Can", bm, [m_lit("CanM%d" % k, rgb, 0.22, 0.6, 0.6)])
        for poly in can.data.polygons:
            poly.use_smooth = True
        can.rotation_euler = (math.radians(-90), 0, 0)
        can.location = (cx, cy, 0.08)
        lid = bpy.data.meshes.new("Lid")
        bb = _bm.new()
        _bm.ops.create_cone(bb, cap_ends=True, segments=48, radius1=0.06, radius2=0.052, depth=0.02)
        bb.to_mesh(lid)
        bb.free()
        lo = obj("Lid", lid, [m_lit("LidM", (0.75, 0.75, 0.77), 0.25, 1.0)])
        lo.rotation_euler = (math.radians(-90), 0, 0)
        lo.location = (cx, cy + 0.13, 0.08)
        t = text(lab, 0.032, cy, (0.98, 0.97, 0.94), z=0.15, shadow=False, x=cx)
        t.rotation_euler = (0, 0, math.radians(90))
        for _ in range(60):
            a = rnd.uniform(-1.2, 1.2)
            d = bpy.data.meshes.new("Drop")
            bb = _bm.new()
            _bm.ops.create_uvsphere(bb, u_segments=10, v_segments=6, radius=rnd.uniform(0.0012, 0.0028))
            bb.to_mesh(d)
            bb.free()
            do = obj("Drop", d, [m_lit("DropM", (0.55, 0.6, 0.65), 0.02, 0.0, 1.0)])
            do.location = (cx + math.sin(a) * 0.066, cy + rnd.uniform(-0.11, 0.11), 0.08 + math.cos(a) * 0.066)
    # crushed ice below them
    for _ in range(70):
        d = bpy.data.meshes.new("Ice")
        import bmesh as _bm
        bb = _bm.new()
        _bm.ops.create_cube(bb, size=rnd.uniform(0.025, 0.05))
        bb.to_mesh(d)
        bb.free()
        io = obj("Ice", d, [m_lit("IceM", (0.62, 0.75, 0.85), 0.04, 0.0, 1.0)])
        io.location = (rnd.uniform(-0.29, 0.29), rnd.uniform(-0.04, 0.02), rnd.uniform(0.02, 0.12))
        io.rotation_euler = (rnd.uniform(0, 3), rnd.uniform(0, 3), rnd.uniform(0, 3))
    # light and camera
    key = bpy.data.lights.new("Key", "AREA")
    key.energy = 18
    key.size = 0.6
    ko = obj("Key", key)
    ko.location = (-0.25, 0.45, 0.6)
    ko.rotation_euler = (math.radians(-30), math.radians(-20), 0)
    cam = bpy.data.cameras.new("SignCam")
    cam.type = "ORTHO"
    cam.ortho_scale = max(W, H)
    co = obj("SignCam", cam)
    co.location = (0, 0, 2.0)
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = px
    try:
        sc.render.engine = "CYCLES"
    except TypeError:
        pass
    sc.cycles.device = "GPU" if _gpu_ready else "CPU"
    sc.cycles.samples = 64
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "Standard"
    sc.world = bpy.data.worlds.new("SignWorld")
    sc.world.use_nodes = True
    bgw = next(n for n in sc.world.node_tree.nodes if n.type == "BACKGROUND")
    bgw.inputs["Color"].default_value = (0.3, 0.3, 0.32, 1)
    bgw.inputs["Strength"].default_value = 0.4
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True, scene=sc.name)
    img = bpy.data.images.load(path)
    try:
        bpy.context.window.scene = prev
    except Exception:
        pass
    for o in list(sc.collection.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.scenes.remove(sc)
    return img


def vending_glow():
    use_gpu(bpy.context.scene)
    import tempfile
    art = sign_render(os.path.join(tempfile.gettempdir(), "vend_sign_art.png"))
    SIGN = mat("Glow_VendSign", (1, 1, 1), 0.2)
    SIGN["linear"] = 1
    SIGN["print"] = art.name
    parts = [card("Sign", SIGN_X1 - SIGN_X0, SIGN_Z1 - SIGN_Z0, ((SIGN_X0 + SIGN_X1) / 2, FRONT + 0.0445, (SIGN_Z0 + SIGN_Z1) / 2), SIGN)]
    # The buttons: one print of the eight, each button's face a part of it; two lit SOLD OUT.
    b = Canvas(256, 512, (0.1, 0.1, 0.1))
    for i, (name, col) in enumerate(FLAVOURS):
        y0 = i * 64
        b.rect(4, y0 + 4, 252, y0 + 60, col)
        b.text_c(name, 128, y0 + 18, 3 if len(name) <= 6 else 2, (0.98, 0.97, 0.94))
        if i in (2, 6):
            b.rect(196, y0 + 44, 248, y0 + 56, (0.95, 0.15, 0.1))
    BTN = print_mat("Glow_VendButtons", b)
    cx = (COL_X0 + COL_X1) / 2
    for i in range(len(FLAVOURS)):
        z = BTN_Z0 - i * BTN_DZ
        v1 = 1.0 - i / 8.0
        parts.append(card_uv("Button", 0.196, 0.045, (cx, FRONT - 0.0081, z), BTN, (0.0, v1 - 1.0 / 8.0, 1.0, v1)))
    d = Canvas(128, 48, (0.04, 0.01, 0.01))
    d.text_c("1.25", 64, 12, 4, (0.95, 0.12, 0.06))
    parts.append(card("Display", 0.13, 0.04, (cx, FRONT - 0.0081, 1.62), print_mat("Glow_VendDisplay", d)))
    l = Canvas(128, 32, (0.25, 0.15, 0.02))
    l.text_c("EXACT CHANGE", 64, 9, 1.5, (0.95, 0.7, 0.2))
    parts.append(card("Lamp", 0.088, 0.016, (cx, FRONT - 0.0071, 0.98), print_mat("Glow_VendLamp", l)))
    return join(parts, "VendingGlow")


def vending_plug():
    BK = S("Rubber_VendPlug", (0.05, 0.05, 0.05), "rubber", 0.5)
    BR = S("Brass_VendPlugPins", (0.7, 0.58, 0.32), "metal", 0.35, 1.0)
    p = [rbox("Body", (0.038, 0.034, 0.045), (0, 0, 0.0225), BK, 0.008, 3)]
    for x in (-0.0063, 0.0063):
        p.append(rbox("Pin", (0.0015, 0.0063, 0.016), (x, 0.0, 0.053), BR, 0.0004, 1))
    p.append(lathe("Ground", [(0.0, 0.0), (0.0024, 0.0), (0.0024, 0.017), (0.0, 0.019)], BR, 10, xf=M4(None, (0.0, -0.0075, 0.045))))
    p.append(lathe("Relief", [(0.0, 0.0), (0.009, 0.0), (0.007, 0.03), (0.005, 0.034), (0.0, 0.034)], BK, 12, xf=M4(rot('X', 180), (0, 0, 0))))
    return finish(p, "VendingPlug", sharp_deg=45)


run_baked([
    ("VendingMachine", vending_machine, (0.65, -1.0, 0.25), {"scale": 1.0, "px_per_m": 420.0}),
    ("VendingGlow", vending_glow, (0.3, -1.0, 0.1), {"res": 1024, "scale": 0.5, "dirt": 0.35, "wear": 0.2}),
    ("VendingPlug", vending_plug, (0.6, -1.0, 0.4), {"res": 256, "scale": 0.2}),
])
print("[vending] done ->", OUT)
