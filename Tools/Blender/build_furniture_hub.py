"""
Level 0's furniture in the hub's style (2026-11-07, the owner: "make sure backrooms everything is my new style, chairs,
couches etc. I like the look of the hub"): the same pieces as build_furniture.py and the wooden chair, remade in the
hub's design language (build_hub.py: HubSofa, HubCoffeeTable, HubReception): seventies office furniture, chunky boxes with
big soft rounded edges, fat crowned cushions, wooden plinths and rails in walnut, chunky tube and pedestal metalwork,
few parts, read from across a room. Run headless, always with the Overflow style:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_furniture_hub.py -- Assets/Backrooms/Models/Overflow --style overflow [--preview <dir>] [--only Sofa_Modern]

Same names, frames, sizes and material slots (in the same order) as the models they replace, so the Overflow prefabs
(Prefabs/Props/Overflow), the colliders (Interaction.MovableShapes: from the bounds), the floor lamp's light (its shade
1.38-1.70 m, 0.225 / 0.185 m round) and the clock's dial texture still fit. Origin on the floor at the footprint's middle
(the clock: on the wall at its middle), front toward -Y (Unity +Z). The materials are the game's (the room's fabric, the
painted wood ...): only the shapes change.
"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_furniture.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- sofas")
exec(compile(_src[:_cut], "build_furniture_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix


def order_slots(ob, names):
    """The material slots in exactly this order (the prefabs set materials by slot index)."""
    me = ob.data
    have = {m.name.split(".")[0]: m for m in me.materials if m}
    old = [m.name.split(".")[0] if m else "" for m in me.materials]
    for n in names:
        if n not in have:
            raise RuntimeError(ob.name + ": no part uses slot " + n)
    remap = [names.index(o) if o in names else 0 for o in old]
    idx = [remap[p.material_index] for p in me.polygons]
    # (Slots set in place: clearing the list would reset every face's material.)
    while len(me.materials) < len(names):
        me.materials.append(have[names[len(me.materials)]])
    for i, n in enumerate(names):
        me.materials[i] = have[n]
    while len(me.materials) > len(names):
        me.materials.pop(index=len(me.materials) - 1)
    for p, k in zip(me.polygons, idx):
        p.material_index = k
    return ob


def done(world, own, name, slots, sharp=55):
    ob = finish_split(world, own, name, sharp)
    return order_slots(ob, slots)


def wood_box(name, size, center, m=None, bevel=0.02, axis=(1, 0, 0)):
    ob = rbox(name, size, center, m or WOOD, bevel, 3)
    return set_uv(ob, grain(axis))


def capsule_y(name, x, z, y0, y1, r, m, seg=24):
    """A fat roll along Y (a sofa's rolled arm), its ends domed."""
    prof = [(0.0, 0.0)]
    for k in range(1, 7):
        a = math.pi / 2 * k / 6
        prof.append((r * math.sin(a), r * (1 - math.cos(a))))
    L = y1 - y0
    prof += [(r, L - r * (1 - math.cos(math.pi / 2 * k / 6))) for k in range(6, 0, -1)]
    prof.append((0.0, L))
    return lathe(name, prof, m, seg, xf=M4(rot('X', -90), (x, y0, z)))


# ------------------------------------------------------------------------------------------------- sofas

def sofa_modern():
    """The hub's sofa (HubSofa): a walnut plinth, a deep upholstered base, square arms and back, three fat seat cushions
    and three back cushions, everything rounded soft."""
    L, D, H = 2.1, 0.88, 0.87
    world, own = [], []
    own.append(wood_box("Plinth", (L - 0.12, D - 0.12, 0.1), (0, 0, 0.05), bevel=0.02))
    world.append(rbox("Base", (L, D, 0.27), (0, 0, 0.1 + 0.135), FAB, 0.07, 4))
    world.append(rbox("Back", (L, 0.24, H - 0.37), (0, D / 2 - 0.12, 0.37 + (H - 0.37) / 2), FAB, 0.09, 4))
    for s in (-1, 1):
        world.append(rbox("Arm", (0.23, D, 0.33), (s * (L / 2 - 0.115), 0, 0.37 + 0.165), FAB, 0.09, 4))
    cw = (L - 0.46) / 3
    for i in range(3):
        x = -L / 2 + 0.23 + cw * (i + 0.5)
        world += cushion("Seat", x, -0.08, 0.37, cw - 0.02, D - 0.3, 0.15, crown=0.022, sag=0.02 if i == 1 else 0.012, p=5, pipe=0.006)
        bc = cushion("BackCushion", 0, 0, 0, cw - 0.02, 0.36, 0.16, crown=0.02, p=5, pipe=0.006)
        for o in bc:
            place(o, rot('X', 90), (0, 0, 0))
            place(o, rot('X', -7), (x, D / 2 - 0.27, 0.52 + 0.18))
        world += bc
    return done(world, own, "Sofa_Modern", ["Fabric", "Wood"])


def sofa_channel():
    """(Sofa_Chesterfield's slot) A seventies channel-back sofa: rolled arms, the back in six fat vertical channels, one
    long bench seat cushion, a walnut rail round the foot on chrome glides."""
    L, D, H = 2.03, 0.88, 0.79
    world, own = [], []
    own.append(wood_box("Rail", (L - 0.04, D - 0.06, 0.09), (0, 0, 0.075), bevel=0.02))
    for x in (-L / 2 + 0.1, L / 2 - 0.1):
        for y in (-D / 2 + 0.1, D / 2 - 0.1):
            world.append(lathe("Glide", [(0.0, 0.0), (0.03, 0.0), (0.034, 0.012), (0.026, 0.03), (0.0, 0.03)], METAL, 16, xf=M4(None, (x, y, 0.0))))
    world.append(rbox("Base", (L - 0.02, D - 0.04, 0.24), (0, 0, 0.12 + 0.12), FAB, 0.07, 4))
    for s in (-1, 1):
        x = s * (L / 2 - 0.14)
        world.append(rbox("ArmBody", (0.24, D - 0.04, 0.3), (x, 0, 0.36 + 0.13), FAB, 0.08, 4))
        world.append(capsule_y("ArmRoll", x, 0.6, -D / 2 + 0.01, D / 2 - 0.03, 0.14, FAB))
    world.append(rbox("BackFrame", (L - 0.5, 0.16, 0.36), (0, D / 2 - 0.1, 0.36 + 0.18), FAB, 0.06, 4))
    n = 6
    cw = (L - 0.52) / n
    for i in range(n):
        x = -L / 2 + 0.26 + cw * (i + 0.5)
        world.append(rbox("Channel", (cw - 0.012, 0.17, H - 0.4), (x, D / 2 - 0.2, 0.4 + (H - 0.4) / 2), FAB, min(0.075, cw / 2 - 0.01), 5))
    world += cushion("Bench", 0, -0.09, 0.36, L - 0.52, D - 0.34, 0.14, crown=0.02, sag=0.015, p=6, pipe=0.006)
    return done(world, own, "Sofa_Chesterfield", ["Fabric", "Metal", "Wood"])


def armchair():
    """The sofa's chair: a walnut plinth, a deep base, fat square arms, a back with its own cushion, a seat cushion."""
    W, D, H = 1.0, 0.86, 0.97
    world, own = [], []
    own.append(wood_box("Plinth", (W - 0.12, D - 0.12, 0.1), (0, 0, 0.05), bevel=0.02))
    world.append(rbox("Base", (W, D, 0.28), (0, 0, 0.1 + 0.14), FAB, 0.07, 4))
    world.append(rbox("Back", (W, 0.24, H - 0.38), (0, D / 2 - 0.12, 0.38 + (H - 0.38) / 2), FAB, 0.09, 4))
    for s in (-1, 1):
        world.append(rbox("Arm", (0.22, D, 0.34), (s * (W / 2 - 0.11), 0, 0.38 + 0.17), FAB, 0.09, 4))
    world += cushion("Seat", 0, -0.08, 0.38, W - 0.46, D - 0.3, 0.15, crown=0.022, sag=0.018, p=5, pipe=0.006)
    bc = cushion("BackCushion", 0, 0, 0, W - 0.46, 0.42, 0.17, crown=0.02, p=5, pipe=0.006)
    for o in bc:
        place(o, rot('X', 90), (0, 0, 0))
        place(o, rot('X', -8), (0, D / 2 - 0.28, 0.53 + 0.21))
    world += bc
    return done(world, own, "Armchair", ["Fabric", "Wood"])


# ------------------------------------------------------------------------------------------------- the office

def office_chair():
    """A seventies office chair: a fat moulded seat and back, each a plastic shell with a thick buttoned pad, chunky
    loop arms, a fat gas column and a five-star base of rounded legs on big casters."""
    world = []
    # The base: five rounded legs and their casters.
    for k in range(5):
        a = 2 * math.pi * k / 5 + math.pi / 2
        c, s = math.cos(a), math.sin(a)
        world.append(tube("Leg", [(c * 0.04, s * 0.04, 0.115), (c * 0.2, s * 0.2, 0.1), (c * 0.27, s * 0.27, 0.085)], 0.022, METAL, 12))
        world.append(lathe("Caster", [(0.0, 0.0), (0.03, 0.0), (0.034, 0.03), (0.03, 0.06), (0.0, 0.065)], PLASTIC, 14, xf=M4(None, (c * 0.27, s * 0.27, 0.0))))
    world.append(lathe("Hub", [(0.0, 0.08), (0.06, 0.08), (0.065, 0.12), (0.045, 0.15), (0.0, 0.15)], METAL, 20))
    world.append(lathe("Column", [(0.0, 0.14), (0.03, 0.14), (0.03, 0.42), (0.0, 0.42)], METAL, 16))
    world.append(lathe("Shroud", [(0.0, 0.14), (0.042, 0.14), (0.042, 0.3), (0.0, 0.3)], PLASTIC, 16))
    # Seat: shell and pad.
    world.append(rbox("SeatShell", (0.52, 0.5, 0.05), (0, -0.01, 0.44), PLASTIC, 0.02, 3))
    world += cushion("SeatPad", 0, -0.01, 0.46, 0.5, 0.48, 0.07, crown=0.018, sag=0.008, p=4, pipe=0.0)
    # Back: on a fat spine from under the seat.
    world.append(tube("Spine", [(0, 0.17, 0.44), (0, 0.27, 0.48), (0, 0.29, 0.62)], 0.022, METAL, 12))
    bs = rbox("BackShell", (0.48, 0.05, 0.48), (0, 0.3, 0.86), PLASTIC, 0.022, 3)
    bp = cushion("BackPad", 0, 0, 0, 0.46, 0.46, 0.07, crown=0.016, p=4, pipe=0.0)
    for o in bp:
        place(o, rot('X', 90), (0, 0, 0))
        place(o, None, (0, 0.27, 0.86))
    world += [bs] + bp
    # Loop arms.
    for s in (-1, 1):
        x = s * 0.29
        world.append(tube("Arm", [(x * 0.85, 0.12, 0.45), (x, 0.1, 0.6), (x, -0.08, 0.66), (x, -0.2, 0.63)], 0.02, PLASTIC, 12))
        world.append(rbox("ArmPad", (0.07, 0.24, 0.035), (x, -0.06, 0.665), PLASTIC, 0.014, 3))
    return done(world, [], "OfficeChair", ["Fabric", "Plastic", "Metal"])


def floor_lamp():
    """A seventies floor lamp: a heavy domed base, a thick stem with a chunky switch knuckle, a drum shade (its size and
    height the light's: 1.38-1.70 m, 0.225 / 0.185 m round) trimmed top and bottom, the bulb under it, a finial."""
    shade = mat("Shade", (0.95, 0.9, 0.78), 0.9)
    glass = mat("Glass", (0.95, 0.95, 0.92), 0.1)
    world = [lathe("Base", [(0.0, 0.0), (0.2, 0.0), (0.21, 0.015), (0.2, 0.035), (0.14, 0.07), (0.05, 0.085), (0.0, 0.085)], METAL, 40),
             lathe("Stem", [(0.0, 0.08), (0.022, 0.08), (0.022, 1.36), (0.0, 1.36)], METAL, 20),
             lathe("Knuckle", [(0.0, 0.9), (0.034, 0.9), (0.04, 0.94), (0.034, 0.98), (0.0, 0.98)], PLASTIC, 20),
             lathe("Harp", [(0.0, 1.34), (0.03, 1.34), (0.03, 1.45), (0.0, 1.45)], METAL, 16),
             sphere("Bulb", (0, 0, 1.5), (0.045, 0.045, 0.06), glass, 16, 10)]
    world.append(lathe("Shade", [(0.225, 1.38), (0.185, 1.70), (0.181, 1.70), (0.221, 1.38)], shade, 48, cap_bottom=False, cap_top=False))
    for z, r in ((1.385, 0.226), (1.695, 0.186)):
        pts = [Vector((r * math.cos(2 * math.pi * i / 48), r * math.sin(2 * math.pi * i / 48), z)) for i in range(49)]
        world.append(tube("Trim", pts, 0.008, FAB, 8, False, False))
    world.append(lathe("Finial", [(0.0, 1.70), (0.02, 1.70), (0.028, 1.715), (0.02, 1.735), (0.0, 1.74)], PLASTIC, 16))
    return done(world, [], "FloorLamp", ["Metal", "Plastic", "Shade", "Fabric", "Glass"])


def table_module():
    """One metre of the conference table (the game lays them end to end): a thick laminate top with a fat rounded
    edge band all round, its underside, a steel stretcher under it."""
    lam = mat("Laminate", (0.5, 0.36, 0.22), 0.4)
    edge = mat("Edge", (0.23, 0.15, 0.09), 0.5)
    under = mat("Underside", (0.3, 0.25, 0.2), 0.8)
    world = [rbox("Top", (1.0, 1.04, 0.012), (0, 0, 0.734), lam, 0.0, 1),
             rbox("Band", (1.0, 1.1, 0.055), (0, 0, 0.7), edge, 0.025, 4),
             rbox("Under", (0.98, 1.0, 0.012), (0, 0, 0.676), under, 0.0, 1),
             rbox("Stretcher", (1.0, 0.08, 0.04), (0, 0, 0.67 + 0.004), METAL, 0.012, 2)]
    return done(world, [], "ConferenceTable_Module", ["Underside", "Edge", "Metal", "Laminate"])


def table_leg():
    """A pedestal end for the table: a chunky steel column on a long rounded foot, a cap plate under the top, glides."""
    world = [rbox("Column", (0.1, 0.14, 0.6), (0, 0, 0.36), METAL, 0.04, 4),
             rbox("Foot", (0.11, 0.78, 0.07), (0, -0.015, 0.05), METAL, 0.03, 4),
             rbox("Cap", (0.11, 0.6, 0.03), (0, -0.015, 0.69), METAL, 0.012, 3)]
    for y in (-0.38, 0.35):
        world.append(rbox("Glide", (0.1, 0.06, 0.02), (0, y, 0.01), PLASTIC, 0.008, 2))
    return done(world, [], "ConferenceTable_Leg", ["Metal", "Plastic"])


def wall_clock():
    """A seventies wall clock (34 cm): a fat rounded case, a chunky bezel ring, the printed dial (slot ClockDial, as
    before), chunky hands and a red second hand, a cap."""
    rimm = mat("Plastic", (0.05, 0.05, 0.055), 0.4)
    dialm = mat("ClockDial", (1, 1, 1), 0.4)
    red = mat("Hand", (0.7, 0.05, 0.04), 0.4)
    blk = mat("Metal", (0.03, 0.03, 0.03), 0.4, 0.5)
    X = rot('X', 90)
    world = [lathe("Case", [(0.0, 0.0), (0.165, 0.0), (0.172, 0.01), (0.172, 0.036), (0.164, 0.048), (0.15, 0.05), (0.142, 0.044), (0.142, 0.014), (0.0, 0.014)],
                   rimm, 64, xf=M4(X, (0, 0, 0)))]
    world.append(lathe("Bezel", [(0.142, 0.03), (0.15, 0.03), (0.152, 0.045), (0.146, 0.05), (0.14, 0.046)], blk, 64, cap_bottom=False, cap_top=False, xf=M4(X, (0, 0, 0))))
    bm = bmesh.new()
    vs = [bm.verts.new((0.142 * math.cos(2 * math.pi * i / 64), -0.0145, 0.142 * math.sin(2 * math.pi * i / 64))) for i in range(64)]
    bm.faces.new(vs)
    dial = new_obj("Dial", bm, [dialm])
    set_uv(dial, lambda p, n: (p.x / 0.284 + 0.5, p.z / 0.284 + 0.5))

    def hand(name, length, w, tail, ang, y, m, t=0.004):
        pts = [(-w * 0.7, -tail), (w * 0.7, -tail), (w, 0.0), (w * 0.8, length * 0.8), (0.0, length), (-w * 0.8, length * 0.8), (-w, 0.0)]
        bm = bmesh.new()
        top = [bm.verts.new((x, y, z)) for x, z in pts]
        bot = [bm.verts.new((x, y + t, z)) for x, z in pts]
        bm.faces.new(top); bm.faces.new(list(reversed(bot)))
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            bm.faces.new((top[i], top[j], bot[j], bot[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        ob = new_obj(name, bm, [m])
        place(ob, rot('Y', ang), (0, 0, 0))
        return ob
    world.append(hand("Hour", 0.08, 0.012, 0.018, -64, -0.02, blk))
    world.append(hand("Minute", 0.118, 0.009, 0.022, 112, -0.025, blk))
    world.append(hand("Second", 0.125, 0.003, 0.03, 200, -0.03, red, 0.003))
    world.append(lathe("Cap", [(0.0, 0.0), (0.012, 0.0), (0.011, 0.005), (0.0, 0.007)], blk, 16, xf=M4(rot('X', 90), (0, -0.031, 0))))
    return done(world, [dial], "WallClock", ["Plastic", "Metal", "Hand", "ClockDial"], 50)


def wooden_chair():
    """A seventies wooden stacking chair: chunky square legs rounded soft, a thick seat with a rounded front, a wide curved
    back rail on two posts, a stretcher low down; felt pads under the feet; the worn wood where it is touched (WoodWorn:
    the seat's front, the top of the back rail, the front stretcher)."""
    worn = mat("WoodWorn", (0.5, 0.34, 0.2), 0.7)
    felt = mat("Felt", (0.35, 0.33, 0.3), 0.95)
    own = []
    for x in (-0.18, 0.18):
        own.append(wood_box("FrontLeg", (0.05, 0.05, 0.43), (x, -0.16, 0.215), bevel=0.016, axis=(0, 0, 1)))
        own.append(wood_box("BackPost", (0.05, 0.05, 0.84), (x, 0.2, 0.42), bevel=0.016, axis=(0, 0, 1)))
    own.append(wood_box("Seat", (0.44, 0.4, 0.04), (0, 0.02, 0.45), bevel=0.016))
    own.append(wood_box("SeatFront", (0.44, 0.05, 0.045), (0, -0.185, 0.448), worn, bevel=0.02))
    own.append(wood_box("BackRail", (0.44, 0.05, 0.14), (0, 0.2, 0.76), bevel=0.02))
    own.append(wood_box("BackTop", (0.44, 0.055, 0.03), (0, 0.2, 0.845), worn, bevel=0.012))
    own.append(wood_box("BackSlat", (0.3, 0.035, 0.08), (0, 0.2, 0.6), bevel=0.012))
    own.append(wood_box("Stretcher", (0.36, 0.035, 0.035), (0, -0.16, 0.14), worn, bevel=0.01))
    own.append(wood_box("SideStretcher", (0.035, 0.36, 0.035), (-0.18, 0.02, 0.12), bevel=0.01, axis=(0, 1, 0)))
    own.append(wood_box("SideStretcher", (0.035, 0.36, 0.035), (0.18, 0.02, 0.12), bevel=0.01, axis=(0, 1, 0)))
    world = []
    for x in (-0.18, 0.18):
        for y in (-0.16, 0.2):
            world.append(rbox("Felt", (0.048, 0.048, 0.006), (x, y, 0.003), felt, 0.002, 1))
    ob = finish_split(world, own, "WoodenChair", 50)
    return order_slots(ob, ["Wood", "Felt", "WoodWorn"])


V = (0.75, -1.0, 0.55)
run([
    ("Sofa_Chesterfield", sofa_channel, V),
    ("Sofa_Modern", sofa_modern, V),
    ("Armchair", armchair, (0.6, -1.0, 0.55)),
    ("OfficeChair", office_chair, (0.7, -1.0, 0.45)),
    ("FloorLamp", floor_lamp, (0.6, -1.0, 0.3)),
    ("ConferenceTable_Module", table_module, (0.6, -1.0, 0.6)),
    ("ConferenceTable_Leg", table_leg, (0.9, -0.6, 0.4)),
    ("WallClock", wall_clock, (0.3, -1.0, 0.2)),
    ("WoodenChair", wooden_chair, (0.7, -1.0, 0.5)),
])
print("[furniture hub] done ->", OUT)
