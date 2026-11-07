"""
Carts (2026-11-04; the owner chose all three: a two-shelf utility cart, a janitor cart, a flat platform trolley), built
in Blender and baked (bake_kit.py). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_carts.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>] [--only UtilityCart]

Sizes from real products of each kind (no real brand: an invented maker, HAULWELL):
  - UtilityCart: a two-shelf moulded-plastic service cart (the 1.0 x 0.55 m class, about 0.85 m to the handle): two deep
    trays with raised lips and ribs under them, four round posts, a moulded handle at one end, 4 in casters.
  - JanitorCart: a yellow janitor's cart (1.15 x 0.55 x 0.98 m): a top tray with two spray bottles, two shelves, a
    vinyl bag hanging in its hoop at one end, a low platform at the other with a mop bucket and wringer on it, a mop
    standing in it; 8 in fixed wheels at the bag end, 4 in swivel casters at the handle end.
  - PlatformTruck: a steel platform truck (a 36 x 24 in deck, 0.23 m high) painted blue with a ribbed rubber mat, a
    tubular U handle at one end, channel stiffeners under the deck, 5 in casters.
  - Caster4Swivel / Caster4Rigid / Caster5Swivel / Caster5Rigid: the casters' forks (a swivel turns about its axis: origin
    on the axis at the top plate; a rigid one: origin at its top plate's middle); Wheel4, Wheel5, Wheel8: the wheels,
    origin on the axle (they spin about Blender X).
  - Cart_Layout.json: for each cart its casters (where each is mounted, swivel or rigid, its size), the wheels' axles
    under each fork, the handle's bar, its decks (what rides on them) and its collision boxes (Blender frame).

Frame: origin on the floor at the middle of the cart, its length along Blender Y with the handle at +Y (Unity -Z: the
game pushes it from there toward its front, Unity +Z), X across. Unity (x, y, z) = (x, z, -y) for these fittings.
"""
import os
import sys
import math
import json

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_rooms.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- break room")
exec(compile(_src[:_cut], "build_rooms_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix

LAYOUT = {}

# ------------------------------------------------------------------------------------------------- helpers


def tray(name, L, W, z_top, floor_t, lip_h, wall_t, r, material, ribs=True):
    """A moulded tray: a floor slab (top at z_top) with a raised lip round it and ribs under it."""
    p = []
    # the floor: a rounded slab
    rings = [rrect(W, L, r, z_top - floor_t, 6), rrect(W, L, r, z_top, 6)]
    p.append(loft(name, rings, material, True, True, recalc=True))
    # the lip: a rounded wall standing up round the edge, its top rolled
    outer0 = rrect(W, L, r, z_top, 6)
    outer1 = rrect(W, L, r, z_top + lip_h, 6)
    inner1 = rrect(W - 2 * wall_t, L - 2 * wall_t, max(0.004, r - wall_t), z_top + lip_h, 6)
    inner0 = rrect(W - 2 * wall_t, L - 2 * wall_t, max(0.004, r - wall_t), z_top, 6)
    p.append(loft(name + "_lip", [outer0, outer1, inner1, inner0], material, False, False, recalc=True))
    if ribs:
        # ribs under it: two along, three across, and a skirt round the edge
        for x in (-W * 0.22, W * 0.22):
            p.append(rbox(name + "_rib", (0.012, L - 2 * r, 0.03), (x, 0, z_top - floor_t - 0.015), material, 0.003, 1))
        for y in (-L * 0.3, 0.0, L * 0.3):
            p.append(rbox(name + "_rib", (W - 2 * r, 0.012, 0.03), (0, y, z_top - floor_t - 0.015), material, 0.003, 1))
        sk0 = rrect(W, L, r, z_top - floor_t - 0.035, 6)
        sk1 = rrect(W, L, r, z_top - floor_t, 6)
        si1 = rrect(W - 0.012, L - 0.012, max(0.004, r - 0.006), z_top - floor_t, 6)
        si0 = rrect(W - 0.012, L - 0.012, max(0.004, r - 0.006), z_top - floor_t - 0.035, 6)
        p.append(loft(name + "_skirt", [sk0, sk1, si1, si0], material, False, False, recalc=True))
    return p


def shift(objs, dy):
    """Moves objects' meshes along Y (not their origins: the export keeps one origin, the cart's)."""
    for o in objs:
        o.data.transform(Matrix.Translation((0.0, dy, 0.0)))
        o.data.update()


def post(name, x, y, z0, z1, rad, material, seg=16):
    return lathe(name, [(0.0, 0.0), (rad, 0.0), (rad, z1 - z0), (0.0, z1 - z0)], material, seg, xf=M4(None, (x, y, z0)))


def label_canvas(lines, w=256, h=96, ground=(0.9, 0.88, 0.8), ink=(0.12, 0.12, 0.13), size=3):
    c = Canvas(w, h, ground)
    c.frame(2, 2, w - 2, h - 2, 2, ink)
    y = 10
    for s in lines:
        sz = max(1, min(size, int((w - 12) / (6 * max(1, len(s))))))      # (shrunk to fit its width)
        c.text_c(s, w / 2, y, sz, ink)
        y += 8 * size + 4
    return c


# ------------------------------------------------------------------------------------------------- casters and wheels

CASTER = {
    # size: wheel radius, wheel width, fork height above the axle to the plate's top, swivel lead (axle behind the axis)
    "4": dict(r=0.0508, w=0.032, h=0.077, lead=0.032, plate=0.064),     # 128 mm overall
    "5": dict(r=0.0635, w=0.032, h=0.1015, lead=0.038, plate=0.07),     # 165 mm overall
}


def caster_mats():
    return dict(
        zinc=S("Galv_CasterFork", (0.66, 0.67, 0.68), "galv", 0.4, 1.0),
        steel=S("Steel_CasterAxle", (0.5, 0.5, 0.5), "metal", 0.35, 1.0),
        tread=S("Rubber_CasterTread", (0.05, 0.05, 0.055), "rubber", 0.75),
        hub=S("Plastic_CasterHub", (0.28, 0.29, 0.3), "plastic", 0.45),
    )


def wheel(size_key, name):
    """A caster's wheel: a rubber tread on a plastic hub, the axle's bolt through it; origin on the axle, along X."""
    M = caster_mats()
    if size_key == "8":
        r, w = 0.1016, 0.045
    else:
        r, w = CASTER[size_key]["r"], CASTER[size_key]["w"]
    # The tread's section (x along the axle, rr out from it), lathed about Blender Z then turned onto X.
    tread = [(0.0, -w / 2 + 0.004), (r * 0.72, -w / 2), (r - 0.004, -w / 2 + 0.002), (r, -w / 2 + 0.008), (r, w / 2 - 0.008),
             (r - 0.004, w / 2 - 0.002), (r * 0.72, w / 2), (0.0, w / 2 - 0.004)]
    tr = lathe(name + "_tread", [(rr, z) for rr, z in tread], M["tread"], 36, xf=M4(rot('Y', 90), (0, 0, 0)))
    hub = lathe(name + "_hub", [(0.0, -w / 2 - 0.002), (r * 0.62, -w / 2 - 0.002), (r * 0.62, w / 2 + 0.002), (0.0, w / 2 + 0.002)],
                M["hub"], 28, xf=M4(rot('Y', 90), (0, 0, 0)))
    bolt = lathe(name + "_bolt", [(0.0, -w / 2 - 0.012), (0.0055, -w / 2 - 0.012), (0.0055, w / 2 + 0.012), (0.0, w / 2 + 0.012)],
                 M["steel"], 6, xf=M4(rot('Y', 90), (0, 0, 0)))
    return finish([tr, hub, bolt], name, sharp_deg=50)


def fork(size_key, swivel, name):
    """A caster's fork: the top plate (its four bolts), the swivel's raceway (a swivel), the two legs down to the axle."""
    M = caster_mats()
    c = CASTER[size_key]
    r, w, h, lead, pl = c["r"], c["w"], c["h"], c["lead"] if swivel else 0.0, c["plate"]
    p = []
    p.append(rbox(name + "_plate", (pl, pl, 0.004), (0, 0, -0.002), M["zinc"], 0.002, 1))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(lathe(name + "_bolt", [(0.0, 0.0), (0.0045, 0.0), (0.0045, 0.004), (0.0, 0.005)], M["steel"], 8,
                           xf=M4(None, (sx * pl * 0.36, sy * pl * 0.36, 0.0))))
    top = -0.004
    if swivel:
        # the raceway: two pressed rings with the balls between, a kingpin's nut under it
        p.append(lathe(name + "_race", [(0.0, 0.0), (0.029, 0.0), (0.03, -0.004), (0.027, -0.009), (0.0, -0.009)], M["zinc"], 28,
                       xf=M4(None, (0, 0, top))))
        p.append(lathe(name + "_nut", [(0.0, 0.0), (0.008, 0.0), (0.008, -0.006), (0.0, -0.006)], M["steel"], 6, xf=M4(None, (0, 0, top - 0.009))))
        top -= 0.009
    # the fork: a yoke under the plate, two legs down either side of the wheel to the axle (behind the axis by the lead)
    yoke_w = w + 0.012
    p.append(rbox(name + "_yoke", (yoke_w + 0.006, 0.045 + lead * 0.5, 0.004), (0, lead * 0.35, top - 0.002), M["zinc"], 0.0015, 1))
    axle_z = -h                            # the axle, h under the plate's top
    for sx in (-1, 1):
        x = sx * (yoke_w / 2 + 0.0015)
        pts = [Vector((x, lead * 0.35, top - 0.004)), Vector((x, lead * 0.8, top - 0.025)), Vector((x, lead, axle_z))]
        legs = []
        for i in range(len(pts) - 1):
            a, b = pts[i], pts[i + 1]
            d = (b - a)
            mid = (a + b) / 2
            L = d.length
            bm = rbox(name + "_leg", (0.003, 0.03, L + 0.012), (0, 0, 0), M["zinc"], 0.001, 1)
            ang = math.atan2(d.y, -d.z)
            bm.matrix_world = Matrix.Translation(mid) @ Matrix.Rotation(ang, 4, 'X')
            bpy.context.view_layer.update()
            bm.data.transform(bm.matrix_world)
            bm.matrix_world = Matrix.Identity(4)
            legs.append(bm)
        p.extend(legs)
        p.append(lathe(name + "_axlenut", [(0.0, 0.0), (0.0075, 0.0), (0.0075, 0.005), (0.0, 0.005)], M["steel"], 6,
                       xf=M4(rot('Y', 90 * sx), (x + sx * 0.0015, lead, axle_z))))
    ob = finish(p, name, sharp_deg=45)
    return ob, (0.0, lead, axle_z)


# ------------------------------------------------------------------------------------------------- the utility cart

def utility_cart():
    G = S("Plastic_CartGrey", (0.21, 0.215, 0.22), "plastic", 0.55)
    LBL = print_mat("Print_CartLabel", label_canvas(["HAULWELL", "MAX LOAD 136 KG"], 256, 80, size=3), 0.5)
    L, W = 1.0, 0.55
    p = []
    z_top, z_low = 0.80, 0.25
    p += tray("TopTray", L, W, z_top, 0.012, 0.045, 0.007, 0.05, G)
    p += tray("LowTray", L, W, z_low, 0.012, 0.045, 0.007, 0.05, G)
    # posts at the corners, inside the lips, from the caster pads up through both trays
    PX, PY = W / 2 - 0.045, L / 2 - 0.045
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(post("Post", sx * PX, sy * PY, 0.128, z_top + 0.03, 0.021, G))
            # the caster's pad under the post
            p.append(rbox("Pad", (0.075, 0.075, 0.016), (sx * PX, sy * PY, 0.136), G, 0.004, 2))
    # the handle, moulded on at the +Y end over the top tray
    for sx in (-1, 1):
        p.append(rbox("HandleUp", (0.04, 0.03, 0.15), (sx * 0.2, L / 2 - 0.01, z_top + 0.075), G, 0.008, 2))
    grip = lathe("Grip", [(0.0, -0.22), (0.018, -0.22), (0.018, 0.22), (0.0, 0.22)], G, 18, xf=M4(rot('Y', 90), (0, L / 2 + 0.005, z_top + 0.15)))
    p.append(grip)
    body = finish(p, "UtilityCart", sharp_deg=55)
    lab = card("Label", 0.12, 0.0375, (0.0, -L / 2 - 0.0005, z_top + 0.0225), LBL, (0, -1, 0))
    ob = finish_mixed([body], [lab], "UtilityCart")
    casters = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            casters.append({"at": [sx * PX, sy * PY, 0.128], "swivel": sy > 0, "size": "4"})
    LAYOUT["UtilityCart"] = {
        "casters": casters,
        "handle": [[-0.2, L / 2 + 0.005, z_top + 0.15], [0.2, L / 2 + 0.005, z_top + 0.15]],
        "decks": [[-W / 2 + 0.01, -L / 2 + 0.01, z_top, W / 2 - 0.01, L / 2 - 0.01, z_top + 0.045],
                  [-W / 2 + 0.01, -L / 2 + 0.01, z_low, W / 2 - 0.01, L / 2 - 0.01, z_low + 0.045]],
        "boxes": boxes_for_trays(L, W, [z_top, z_low]) + [
            [0, 0, 0.064, W - 0.06, L - 0.06, 0.128],                      # (the casters: what rolls)
        ] + [[sx * PX, sy * PY, (0.128 + z_top) / 2, 0.042, 0.042, z_top - 0.128] for sx in (-1, 1) for sy in (-1, 1)]
          + [[0, L / 2 + 0.005, z_top + 0.15, 0.44, 0.036, 0.036]],
        "mass": 11.5,
    }
    return ob


def boxes_for_trays(L, W, zs, lip=0.045):
    b = []
    for z in zs:
        b.append([0, 0, z - 0.006 - 0.0175, W, L, 0.047])                    # the floor and its ribs
        b.append([0, -L / 2 + 0.004, z + lip / 2, W, 0.008, lip])            # the lip, four sides
        b.append([0, L / 2 - 0.004, z + lip / 2, W, 0.008, lip])
        b.append([-W / 2 + 0.004, 0, z + lip / 2, 0.008, L, lip])
        b.append([W / 2 - 0.004, 0, z + lip / 2, 0.008, L, lip])
    return b


# ------------------------------------------------------------------------------------------------- the janitor cart

def spray_bottle(name, x, y, z, liquid, label_text, turn=0.0):
    """A 32 oz trigger bottle: a frosted body with its liquid, a printed label, a white trigger sprayer."""
    BOT = S("Plastic_BottleClear", (0.8, 0.82, 0.84), "plastic", 0.25)
    LIQ = S("Plastic_Bottle" + name, liquid, "plastic", 0.3)
    TRG = S("Plastic_Trigger", (0.9, 0.9, 0.88), "plastic", 0.4)
    LBL = print_mat("Print_Bottle" + name, label_canvas(["SHINEBRITE", label_text], 128, 96, (0.95, 0.95, 0.92), (0.1, 0.2, 0.45), 2), 0.5)
    p = []
    prof = [(0.0, 0.0), (0.04, 0.0), (0.044, 0.006), (0.044, 0.17), (0.036, 0.205), (0.016, 0.225), (0.016, 0.245), (0.0, 0.245)]
    xf = M4(rot('Z', turn), (x, y, z))
    p.append(lathe(name + "_body", prof, LIQ, 28, xf=xf))
    p.append(lathe(name + "_cap", [(0.0, 0.0), (0.019, 0.0), (0.019, 0.022), (0.0, 0.022)], TRG, 16, xf=M4(None, (x, y, z + 0.24))))
    p.append(rbox(name + "_head", (0.03, 0.07, 0.035), (x, y - 0.015, z + 0.275), TRG, 0.008, 2))
    p.append(rbox(name + "_nozzle", (0.018, 0.022, 0.018), (x, y - 0.058, z + 0.282), TRG, 0.004, 1))
    p.append(rbox(name + "_trigger", (0.014, 0.012, 0.05), (x, y - 0.03, z + 0.245), TRG, 0.004, 1))
    lab = card(name + "_label", 0.06, 0.09, (x, y - 0.0445, z + 0.09), LBL, (0, -1, 0))
    return p, lab


def janitor_cart():
    Y = S("Plastic_JanitorYellow", (0.86, 0.66, 0.07), "plastic", 0.5)
    GB = S("Plastic_JanitorGrey", (0.24, 0.245, 0.25), "plastic", 0.55)
    BAG = S("Fabric_JanitorBag", (0.13, 0.13, 0.14), "fabric", 0.7)
    STL = S("Steel_JanitorHoop", (0.6, 0.6, 0.62), "metal", 0.35, 1.0)
    LBL = print_mat("Print_JanitorLabel", label_canvas(["HAULWELL", "CLEANING CART"], 256, 80, size=3), 0.5)
    L, W = 1.15, 0.55
    y_bag0, y_bag1 = -L / 2, -0.24       # the bag's end
    y_body1 = 0.30                       # the cabinet's end toward the bucket
    p = []
    # The cabinet: a top tray, a middle shelf and a bottom shelf between two moulded sides.
    yc, Lc = (y_bag1 + y_body1) / 2, y_body1 - y_bag1
    for z, lip in ((0.95, 0.05), (0.56, 0.035), (0.19, 0.035)):
        tp = tray("Shelf", Lc, W, z, 0.014, lip, 0.008, 0.035, Y, ribs=(z > 0.5))
        shift(tp, yc)
        p += tp
    for sx in (-1, 1):
        p.append(rbox("Side", (0.024, Lc - 0.04, 0.76), (sx * (W / 2 - 0.02), yc, 0.19 + 0.38), Y, 0.006, 2))
    # Pads under the bottom shelf for the casters; the big wheels' axle brackets low in the sides at the bag's end.
    for sx in (-1, 1):
        p.append(rbox("Pad", (0.075, 0.075, 0.05), (sx * (W / 2 - 0.06), y_body1 - 0.05, 0.153), Y, 0.005, 2))
        p.append(rbox("AxleBracket", (0.03, 0.1, 0.09), (sx * (W / 2 - 0.02), y_bag1 + 0.06, 0.14), Y, 0.006, 2))
    p.append(lathe("Axle", [(0.0, -W / 2 - 0.035), (0.008, -W / 2 - 0.035), (0.008, W / 2 + 0.035), (0.0, W / 2 + 0.035)],
                   S("Steel_JanitorAxle", (0.5, 0.5, 0.5), "metal", 0.35, 1.0), 10, xf=M4(rot('Y', 90), (0, y_bag1 + 0.06, 0.1016))))
    # The bag's hoop: a steel rim at the top of the bag end, the vinyl bag hanging in it, its top folded over the rim.
    hoop = rrect(W - 0.06, (y_bag1 - y_bag0) - 0.03, 0.06, 0.95, 6, 0.0, (y_bag0 + y_bag1) / 2)
    p.append(tube("Hoop", hoop + hoop[:1], 0.007, STL, 8, False, False))
    for sx in (-1, 1):
        p.append(rbox("HoopArm", (0.02, 0.06, 0.03), (sx * (W / 2 - 0.035), y_bag1 - 0.02, 0.95), Y, 0.005, 1))
    # the bag: rings from its top (over the rim) down to its bottom, sagging out a little
    rings = []
    for k, (z, s) in enumerate(((0.965, 1.02), (0.94, 1.03), (0.8, 1.06), (0.55, 1.08), (0.32, 1.05), (0.2, 0.96), (0.175, 0.8))):
        rings.append(rrect((W - 0.07) * s, ((y_bag1 - y_bag0) - 0.045) * s, 0.07, z, 6, 0.0, (y_bag0 + y_bag1) / 2))
    rings.append(Vector((0.0, (y_bag0 + y_bag1) / 2, 0.172)))
    p.append(loft("Bag", rings, BAG, False, False, recalc=True))
    # the fold over the rim
    fold = [rrect((W - 0.06) * 1.035, ((y_bag1 - y_bag0) - 0.03) * 1.04, 0.065, z, 6, 0.0, (y_bag0 + y_bag1) / 2) for z in (0.965, 0.92)]
    p.append(loft("BagFold", fold, BAG, False, False, recalc=True))
    # The bucket's platform at the handle end, low, and its edge.
    pl = tray("Platform", L / 2 - y_body1 + 0.02, W, 0.13, 0.016, 0.03, 0.008, 0.04, Y, ribs=False)
    shift(pl, (y_body1 + L / 2) / 2 - 0.01)
    p += pl
    # the handle over the bucket's end
    for sx in (-1, 1):
        p.append(rbox("HandleUp", (0.035, 0.03, 0.14), (sx * 0.21, y_body1 + 0.0, 1.0), Y, 0.008, 2))
    p.append(lathe("Grip", [(0.0, -0.23), (0.019, -0.23), (0.019, 0.23), (0.0, 0.23)], GB, 18, xf=M4(rot('Y', 90), (0, y_body1 + 0.01, 1.07))))
    # The mop bucket on the platform: a yellow 35 qt bucket, its wringer on the cart's side, a mop standing in it.
    bx, by, bz = 0.0, (y_body1 + L / 2) / 2 + 0.02, 0.13
    bucket = []
    for k, (z, s) in enumerate(((0.0, 0.86), (0.02, 0.9), (0.3, 1.0), (0.31, 1.02), (0.295, 0.97), (0.035, 0.85))):
        bucket.append(rrect(0.42 * s, 0.3 * s, 0.08 * s, bz + z, 6, bx, by))
    p.append(loft("Bucket", bucket, Y, True, False, recalc=True))
    p.append(rbox("Water", (0.36, 0.24, 0.004), (bx, by, bz + 0.18), S("Glass_MopWater", (0.33, 0.3, 0.22), "glass", 0.1), 0.02, 2))
    p.append(rbox("Wringer", (0.2, 0.26, 0.12), (bx - 0.09, by, bz + 0.37), GB, 0.012, 2))
    p.append(tube("WringerLever", [(bx - 0.09, by + 0.12, bz + 0.4), (bx - 0.09, by + 0.18, bz + 0.62), (bx - 0.09, by + 0.2, bz + 0.86)], 0.011, GB, 10))
    mop = [(bx + 0.06, by - 0.02, bz + 0.15), (bx + 0.07, by - 0.03, bz + 0.6), (bx + 0.1, by - 0.05, bz + 1.32)]
    p.append(tube("MopHandle", mop, 0.012, S("Wood_MopHandle", (0.72, 0.6, 0.42), "wood", 0.5), 10))
    # Two spray bottles in the top tray.
    labels = []
    for i, (x, liq, txt) in enumerate(((-0.12, (0.25, 0.5, 0.85), "GLASS"), (0.1, (0.3, 0.75, 0.35), "ALL-PURPOSE"))):
        bp, lb = spray_bottle("Bottle%d" % i, x, -0.1, 0.95, liq, txt, 15 * (i * 2 - 1))
        p += bp
        labels.append(lb)
    body = finish(p, "JanitorCart", sharp_deg=55)
    lab = card("Label", 0.12, 0.0375, (0.0, y_bag1 + 0.0, 0.62), LBL, (0, -1, 0))
    lab2 = card("Label2", 0.12, 0.0375, (W / 2 - 0.007, yc, 0.75), LBL, (1, 0, 0))
    ob = finish_mixed([body], [lab, lab2] + labels, "JanitorCart")
    # 8 in wheels at the bag's end (on axle brackets in the sides), 4 in swivel casters at the handle's
    casters = [{"at": [sx * (W / 2 - 0.06), y_body1 - 0.05, 0.128], "swivel": True, "size": "4"} for sx in (-1, 1)]
    wheels8 = [{"at": [sx * (W / 2 + 0.012), y_bag1 + 0.06, 0.1016], "size": "8"} for sx in (-1, 1)]
    LAYOUT["JanitorCart"] = {
        "casters": casters,
        "wheels": wheels8,
        "handle": [[-0.23, y_body1 + 0.01, 1.07], [0.23, y_body1 + 0.01, 1.07]],
        "decks": [[-W / 2 + 0.01, y_bag1, 0.95, W / 2 - 0.01, y_body1, 1.0], [-W / 2 + 0.03, y_bag1, 0.56, W / 2 - 0.03, y_body1, 0.6]],
        "boxes": [
            [0, yc, 0.19 + 0.4, W, Lc, 0.8],                                     # the cabinet
            [0, (y_bag0 + y_bag1) / 2, 0.57, W - 0.04, y_bag1 - y_bag0, 0.8],     # the bag
            [0, (y_body1 + L / 2) / 2, 0.13, W, L / 2 - y_body1, 0.05],           # the platform
            [0, by, bz + 0.2, 0.42, 0.3, 0.4],                                     # the bucket and wringer
            [0, y_body1 + 0.01, 1.07, 0.46, 0.04, 0.04],                           # the handle
            [0, 0, 0.07, W - 0.04, L - 0.1, 0.14],                                 # (what rolls)
        ],
        "mass": 19.0,
    }
    return ob


# ------------------------------------------------------------------------------------------------- the platform truck

def platform_truck():
    B = S("Paint_TruckBlue", (0.09, 0.24, 0.55), "paint", 0.45, 0.3)
    MAT = S("Rubber_TruckMat", (0.06, 0.06, 0.065), "rubber", 0.8)
    L, W = 0.91, 0.61
    z_deck = 0.23
    p = []
    # the deck: a steel pan with its edges turned down, the mat on top, two channels under it
    p.append(rbox("Deck", (W, L, 0.004), (0, 0, z_deck - 0.002), B, 0.0015, 1))
    for sx in (-1, 1):
        p.append(rbox("Flange", (0.004, L, 0.035), (sx * (W / 2 - 0.002), 0, z_deck - 0.0175), B, 0.0015, 1))
    for sy in (-1, 1):
        p.append(rbox("Flange", (W, 0.004, 0.035), (0, sy * (L / 2 - 0.002), z_deck - 0.0175), B, 0.0015, 1))
    for sx in (-1, 1):
        p.append(rbox("Channel", (0.05, L - 0.02, 0.004), (sx * 0.2, 0, z_deck - 0.06), B, 0.0015, 1))
        for so in (-1, 1):
            p.append(rbox("ChannelWeb", (0.004, L - 0.02, 0.058), (sx * 0.2 + so * 0.023, 0, z_deck - 0.031), B, 0.0015, 1))
    # the mat: ribbed (ribs across, as moulded)
    p.append(rbox("Mat", (W - 0.04, L - 0.04, 0.004), (0, 0, z_deck + 0.002), MAT, 0.002, 1))
    for i in range(23):
        y = -L / 2 + 0.04 + i * (L - 0.08) / 22
        p.append(rbox("Rib", (W - 0.06, 0.008, 0.003), (0, y, z_deck + 0.0055), MAT, 0.001, 1))
    # the handle: a U of 25 mm tube in sockets at the +Y end, a brace across part way up
    hy = L / 2 - 0.03
    u = [(-0.27, hy, z_deck), (-0.27, hy + 0.02, 0.75), (-0.25, hy + 0.035, 0.9), (0.0, hy + 0.04, 0.925),
         (0.25, hy + 0.035, 0.9), (0.27, hy + 0.02, 0.75), (0.27, hy, z_deck)]
    p.append(tube("Handle", [Vector(q) for q in catmull(u, 6)], 0.0127, B, 12))
    p.append(tube("Brace", [(-0.27, hy + 0.009, 0.5), (0.27, hy + 0.009, 0.5)], 0.0095, B, 10))
    for sx in (-1, 1):
        p.append(rbox("Socket", (0.05, 0.05, 0.05), (sx * 0.27, hy, z_deck + 0.025), B, 0.004, 1))
    # the caster pads (the channels' ends) under the corners
    PX, PY = 0.2, L / 2 - 0.08
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(rbox("Pad", (0.08, 0.08, 0.006), (sx * PX, sy * PY, 0.165 + 0.003), B, 0.002, 1))
    ob = finish(p, "PlatformTruck", sharp_deg=50)
    casters = [{"at": [sx * PX, sy * PY, 0.165], "swivel": sy > 0, "size": "5"} for sx in (-1, 1) for sy in (-1, 1)]
    LAYOUT["PlatformTruck"] = {
        "casters": casters,
        "handle": [[-0.25, hy + 0.04, 0.925], [0.25, hy + 0.04, 0.925]],
        "decks": [[-W / 2 + 0.02, -L / 2 + 0.02, z_deck + 0.004, W / 2 - 0.02, L / 2 - 0.06, z_deck + 0.01]],
        "boxes": [
            [0, 0, z_deck - 0.03, W, L, 0.064],                    # the deck
            [-0.27, hy + 0.02, (z_deck + 0.92) / 2, 0.03, 0.04, 0.92 - z_deck],
            [0.27, hy + 0.02, (z_deck + 0.92) / 2, 0.03, 0.04, 0.92 - z_deck],
            [0, hy + 0.04, 0.925, 0.56, 0.03, 0.03],               # the handle's bar
            [0, 0, 0.0825, W - 0.1, L - 0.08, 0.165],               # (what rolls)
        ],
        "mass": 21.0,
    }
    return ob


def platform_cart_overflow():
    """The Overflow look's trolley (--style overflow): the style lab's platform cart at the truck's size and on its
    casters: a thick moulded teal deck with rounded edges, a ribbed rubber mat inset in it, a chunky U handle in two
    moulded sockets, a brace across it and a rubber grip on its bar."""
    T = S("Paint_TruckBlue", (0.184, 0.435, 0.416), "plastic", 0.5)
    MAT = S("Rubber_TruckMat", (0.12, 0.105, 0.095), "rubber", 0.8)
    GRIP = S("Rubber_TruckGrip", (0.12, 0.105, 0.095), "rubber", 0.8)
    L, W = 0.91, 0.61
    z0, z_deck = 0.165, 0.235
    p = []
    rings = [rrect(W - 0.02, L - 0.02, 0.05, z0, 6), rrect(W, L, 0.06, z0 + 0.012, 6), rrect(W, L, 0.06, z_deck - 0.012, 6),
             rrect(W - 0.016, L - 0.016, 0.052, z_deck, 6)]
    p.append(loft("Deck", rings, T, True, True, recalc=True))
    p.append(rbox("Mat", (W - 0.07, L - 0.09, 0.008), (0, -0.015, z_deck + 0.003), MAT, 0.003, 2))
    for i in range(9):
        y = -L / 2 + 0.07 + i * (L - 0.18) / 8 - 0.015
        p.append(rbox("Rib", (W - 0.1, 0.014, 0.006), (0, y, z_deck + 0.009), MAT, 0.002, 1))
    hy = L / 2 - 0.035
    u = [(-0.26, hy, z_deck + 0.03), (-0.26, hy + 0.015, 0.76), (-0.235, hy + 0.03, 0.9), (0.0, hy + 0.035, 0.92),
         (0.235, hy + 0.03, 0.9), (0.26, hy + 0.015, 0.76), (0.26, hy, z_deck + 0.03)]
    p.append(tube("Handle", [Vector(q) for q in catmull(u, 6)], 0.0155, T, 12))
    p.append(tube("Brace", [(-0.26, hy + 0.007, 0.52), (0.26, hy + 0.007, 0.52)], 0.012, T, 10))
    p.append(lathe("Grip", [(0.0, -0.17), (0.022, -0.17), (0.024, -0.16), (0.024, 0.16), (0.022, 0.17), (0.0, 0.17)], GRIP, 16,
                   xf=M4(rot('Y', 90), (0, hy + 0.035, 0.92))))
    for sx in (-1, 1):
        p.append(rbox("Socket", (0.07, 0.06, 0.07), (sx * 0.26, hy, z_deck + 0.03), T, 0.007, 2))
    PX, PY = 0.2, L / 2 - 0.08
    ob = finish(p, "PlatformTruck", sharp_deg=55)
    casters = [{"at": [sx * PX, sy * PY, z0], "swivel": sy > 0, "size": "5"} for sx in (-1, 1) for sy in (-1, 1)]
    LAYOUT["PlatformTruck"] = {
        "casters": casters,
        "handle": [[-0.24, hy + 0.035, 0.92], [0.24, hy + 0.035, 0.92]],
        "decks": [[-W / 2 + 0.035, -L / 2 + 0.03, z_deck + 0.006, W / 2 - 0.035, L / 2 - 0.07, z_deck + 0.012]],
        "boxes": [
            [0, 0, (z0 + z_deck) / 2 + 0.003, W, L, z_deck - z0 + 0.006],     # the deck
            [-0.26, hy + 0.015, (z_deck + 0.92) / 2, 0.035, 0.045, 0.92 - z_deck],
            [0.26, hy + 0.015, (z_deck + 0.92) / 2, 0.035, 0.045, 0.92 - z_deck],
            [0, hy + 0.035, 0.92, 0.56, 0.05, 0.05],               # the handle's bar
            [0, 0, 0.0825, W - 0.1, L - 0.08, 0.165],               # (what rolls)
        ],
        "mass": 19.0,
    }
    return ob


if OVERFLOW:
    platform_truck = platform_cart_overflow


# ------------------------------------------------------------------------------------------------- run

FORKS = {}


def fork_model(size, swivel):
    name = "Caster%s%s" % (size, "Swivel" if swivel else "Rigid")

    def f():
        ob, axle = fork(size, swivel, name)
        FORKS[name] = axle
        return ob
    return name, f


def write_layout():
    os.makedirs(os.path.abspath(OUT), exist_ok=True)
    path = os.path.join(os.path.abspath(OUT), "Cart_Layout.json")
    old = {}
    if os.path.exists(path):
        try:
            old = json.load(open(path, encoding="utf-8"))
        except Exception:
            old = {}
    old.update(LAYOUT)
    axles = old.get("axles", {})
    axles.update({k: list(v) for k, v in FORKS.items()})
    old["axles"] = axles
    # (JsonUtility reads no nested lists: each list of points or boxes is also written flat)
    for k, v in list(old.items()):
        if isinstance(v, dict) and "boxes" in v:
            v["boxes_flat"] = [c for b in v["boxes"] for c in b]
            v["decks_flat"] = [c for b in v["decks"] for c in b]
            v["handle_flat"] = [c for b in v["handle"] for c in b]
            v["casters_flat"] = [c for cs in v["casters"] for c in (cs["at"] + [1.0 if cs.get("swivel") else 0.0, float(cs["size"])])]
            v["wheels_flat"] = [c for cs in v.get("wheels", []) for c in (cs["at"] + [float(cs["size"])])]
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(old, fh, indent=1)
    print("[carts] layout", list(LAYOUT.keys()), FORKS)


models = [
    ("UtilityCart", utility_cart, (0.8, -1.0, 0.6), {"res": 2048, "scale": 0.4, "wear": 0.6, "dirt": 0.7}),
    ("JanitorCart", janitor_cart, (0.8, -1.0, 0.55), {"res": 2048, "scale": 0.4, "wear": 0.6, "dirt": 0.75}),
    ("PlatformTruck", platform_truck, (0.8, -1.0, 0.6), {"res": 1024, "scale": 0.4, "wear": 0.8, "dirt": 0.7}),
]
for size in ("4", "5"):
    for sw in (True, False):
        n, f = fork_model(size, sw)
        models.append((n, f, (0.6, -1.0, 0.4), {"res": 256, "scale": 0.05, "wear": 0.6, "dirt": 0.7}))
for size in ("4", "5", "8"):
    models.append(("Wheel%s" % size, (lambda s=size: wheel(s, "Wheel%s" % s)), (0.6, -1.0, 0.3), {"res": 256, "scale": 0.05, "wear": 0.5, "dirt": 0.6}))
run_baked(models)
write_layout()
print("[carts] done ->", OUT)
