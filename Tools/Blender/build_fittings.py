"""
Level 0's fittings and leftovers (2026-11-03, remade for realism), built procedurally in Blender (run headless):

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_fittings.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>] [--only Extinguisher,PullStation]

Uses build_items2.py's helpers (lathe, loft, tube, rbox, finish, export ...: loaded from it, not copied). Real sizes from
real products: a 5 lb ABC stored-pressure extinguisher (body 108 mm across, 390 mm tall to the valve), surface-mount
cabinet, manual pull station (handle at 1.2 m in the game: ADA 48" max), horn strobe, thermostats (a beige box, a round
T87-style dial), a stamped return grille, a wall-hung drinking fountain, filled-in windows, corner guards, a regular-arm
door closer, a kick plate, a rubber wedge, brass floor boxes, a mop bucket, cubicle panels and a wooden chair.

Every edge is bevelled (a sharp real edge still catches a line of light), normals are weighted (flat faces stay flat,
the bevels shade round), and each model has one UV map at 1 unit = 0.25 m of surface (the game's detail maps), except
the printed parts (labels, gauge, dials), whose UVs run 0..1 over their print, and wood, whose UVs follow its grain.

Frames, as the game places them (Blender: front toward -Y, Z up; Unity (x, y, z) = (x, z, -y)):
  - Wall things: the wall's face is y = 0, the thing stands out toward -Y; origin on the wall at the thing's middle
    across and at the height the game names (see each).
  - Floor things: origin on the floor at the middle of the foot.
Material slots are named for what the game maps them to (OfficeChunkBuilder / FittingModels).
"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_items2.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------------------ models")
exec(compile(_src[:_cut], "build_items2_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix


# ------------------------------------------------------------------------------------------------- more helpers

def W(x, d, z):
    """A point on a wall thing: x across (Blender +X), d out from the wall (toward -Y), z up."""
    return Vector((x, -d, z))


def rot(axis, deg):
    return Matrix.Rotation(math.radians(deg), 3, axis)


def place(ob, rot3=None, loc=(0, 0, 0)):
    """Bakes a rotation (about the object's origin) and a move into its mesh."""
    m = M4(rot3, loc)
    ob.data.transform(m)
    return ob


def ring_pts(cx, cy, z, rx, ry, n, p=4.0):
    """A superellipse ring (|x/rx|^p + |y/ry|^p = 1), counter-clockwise seen from +Z."""
    out = []
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        x = rx * math.copysign(abs(c) ** (2.0 / p), c)
        y = ry * math.copysign(abs(s) ** (2.0 / p), s)
        out.append(Vector((cx + x, cy + y, z)))
    return out


def set_uv(ob, fn):
    """A UV map from fn(world position, normal) -> (u, v), for printed or grained parts."""
    me = ob.data
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    uv = me.uv_layers.new(name="UVMap").data
    for p in me.polygons:
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            uv[li].uv = fn(v, p.normal)
    return ob


def planar(origin, ax_u, ax_v, su, sv):
    o, U_, V_ = Vector(origin), Vector(ax_u), Vector(ax_v)
    return lambda p, n: ((p - o).dot(U_) / su, (p - o).dot(V_) / sv)


def grain(axis, s=TEXEL):
    """Wood: u along the grain, v across it on whichever plane the face lies in (box mapping by the face's normal)."""
    A = Vector(axis).normalized()

    def fn(p, n):
        side = n.cross(A)
        if side.length < 1e-4:
            side = Vector((0, 0, 1)) if abs(A.z) < 0.9 else Vector((1, 0, 0))
        side.normalize()
        return (p.dot(A) / s, p.dot(side) / s)
    return fn


def finish_mixed(base, printed, name, sharp_deg=None):
    """finish() for the plain parts (world-scale UVs, weighted normals), then the printed or grained parts joined on with
    their own UVs kept."""
    ob = finish(base, name, sharp_deg=sharp_deg)
    if not printed:
        return ob
    for p in printed:
        for f in p.data.polygons:
            f.use_smooth = True
    return join([ob] + list(printed), name)


def screw(name, center, normal, r, material, head=0.0015):
    """A pan screw head with its slot, facing normal."""
    R = rot_to(normal)
    if OVERFLOW:
        r, head = r * 1.4, head * 1.5          # (chunkier: bold screw heads)
    s = lathe(name, [(0.0, 0.0), (r, 0.0), (r, head * 0.4), (r * 0.8, head), (0.0, head)], material, 14, xf=M4(R, center))
    slot = rbox(name + "_slot", (r * 1.4, r * 0.25, head * 0.6), (0, 0, head * 0.9), material)
    place(slot, R, center)
    return [s, slot]


def run(models):
    for name, fn, view in models:
        if ONLY and name not in ONLY:
            continue
        reset()
        ob = fn()
        path = os.path.join(OUT, name + ".fbx")
        export(ob, path)
        tris = sum(len(p.vertices) - 2 for o in meshes(ob) for p in o.data.polygons)
        if PREVIEW:
            os.makedirs(PREVIEW, exist_ok=True)
            preview(ob, os.path.join(PREVIEW, name + ".png"), view)
        print("EXPORTED", name, "tris", tris, "mats", [m.name for o in meshes(ob) for m in o.data.materials])


# ------------------------------------------------------------------------------------------------- fire

def extinguisher():
    """A 5 lb ABC stored-pressure extinguisher: drawn steel body (108 mm, domed base and shoulder), chrome collar and
    valve, pressed carrying handle and operating lever (pointing back, toward the wall it hangs on), the pull pin's ring
    and its green tamper seal, a gauge, the black hose down the side to its nozzle in a clip, the printed label round
    the front, and an inspection tag on a string. Origin on the floor under its middle; front (label) toward -Y."""
    red = mat("ExtRed", (0.55, 0.03, 0.03), 0.32)
    chrome = mat("ExtChrome", (0.82, 0.82, 0.8), 0.18, 1.0)
    black = mat("ExtBlack", (0.025, 0.025, 0.025), 0.55)
    green = mat("ExtSeal", (0.1, 0.55, 0.15), 0.4)
    tag = mat("ExtTag", (0.95, 0.85, 0.25), 0.7)
    label = mat("ExtLabel", (1, 1, 1), 0.4)
    gauge = mat("ExtGauge", (1, 1, 1), 0.2)
    R = 0.054
    base = []
    body = [(0.0, 0.006), (0.03, 0.006), (0.044, 0.002), (0.05, 0.0), (0.053, 0.004), (R, 0.012), (R, 0.3), (0.0535, 0.31),
            (0.051, 0.322), (0.046, 0.333), (0.038, 0.343), (0.029, 0.35), (0.021, 0.354), (0.0, 0.355)]
    base.append(lathe("Body", body, red, 40))
    base.append(lathe("Collar", [(0.0, 0.352), (0.021, 0.352), (0.022, 0.356), (0.022, 0.372), (0.019, 0.376), (0.0, 0.376)], chrome, 24))
    # The valve: a block with a rounded front, the outlet to the hose on +X, the gauge port on the front.
    base.append(rbox("Valve", (0.036, 0.04, 0.046), (0.0, 0.002, 0.398), chrome, 0.006, 2))
    base.append(lathe("Outlet", [(0.0, 0.0), (0.008, 0.0), (0.008, 0.022), (0.0065, 0.026), (0.0, 0.026)], chrome, 14,
                      xf=M4(rot('Y', 90), (0.016, 0.0, 0.39))))
    base.append(lathe("GaugePort", [(0.0, 0.0), (0.007, 0.0), (0.007, 0.012), (0.0, 0.012)], chrome, 12, xf=M4(rot('X', 90), (0.0, -0.016, 0.402))))
    # The gauge: a bezel with its printed face (dial: green in the middle, red either side, the needle in the green).
    gc = Vector((0.0, -0.03, 0.402))
    base.append(lathe("GaugeBezel", [(0.0, 0.0), (0.0165, 0.0), (0.0175, 0.003), (0.0165, 0.007), (0.014, 0.0075), (0.014, 0.006), (0.0, 0.006)],
                      chrome, 24, xf=M4(rot('X', 90), gc + Vector((0, 0.003, 0)))))
    disc = bmesh.new()
    vs = [disc.verts.new(gc + Vector((0.0142 * math.cos(2 * math.pi * i / 24), -0.0026, 0.0142 * math.sin(2 * math.pi * i / 24)))) for i in range(24)]
    disc.faces.new(vs)
    face = new_obj("GaugeFace", disc, [gauge])
    set_uv(face, lambda p, n: ((p.x - gc.x) / 0.0284 + 0.5, (p.z - gc.z) / 0.0284 + 0.5))
    # Carrying handle below, operating lever above: pressed steel, curving back toward the wall.
    def strap(name, z0, rise, length, w, material):
        pts = catmull([(0.0, 0.0, z0), (0.0, length * 0.35, z0 + rise * 0.3), (0.0, length * 0.75, z0 + rise * 0.8), (0.0, length, z0 + rise)], 4)
        rings = []
        for i, p in enumerate(pts):
            t = i / (len(pts) - 1)
            ww = w * (1.0 - 0.25 * t)
            rings.append([Vector(p) + Vector(o) for o in ((-ww, 0, -0.0025), (ww, 0, -0.0025), (ww, 0, 0.0025), (-ww, 0, 0.0025))])
        return loft(name, rings, material, True, True, recalc=True)
    base.append(strap("Handle", 0.405, -0.01, 0.078, 0.013, chrome))
    base.append(strap("Lever", 0.43, 0.024, 0.075, 0.012, chrome))
    base.append(rbox("LeverGrip", (0.03, 0.036, 0.008), (0.0, 0.056, 0.452), black, 0.003))
    # The pull pin through the valve and its ring, the tamper seal from it to the lever.
    base.append(tube("Pin", [(-0.022, -0.004, 0.418), (0.022, -0.004, 0.418)], 0.0018, chrome, 8))
    ring = [(-0.024 - 0.011 + 0.011 * math.cos(2 * math.pi * i / 16), -0.004, 0.418 + 0.011 * math.sin(2 * math.pi * i / 16)) for i in range(17)]
    base.append(tube("PinRing", ring, 0.0015, chrome, 6))
    base.append(tube("Seal", catmull([(-0.03, -0.004, 0.427), (-0.02, 0.0, 0.437), (-0.008, 0.01, 0.434)], 4), 0.0009, green, 5))
    # The hose: out of the outlet, round and down the side, into the clip, the nozzle at its end.
    hose = catmull([(0.04, 0.0, 0.39), (0.06, -0.006, 0.375), (0.066, -0.012, 0.33), (0.064, -0.014, 0.24), (0.062, -0.012, 0.15), (0.062, -0.01, 0.11)], 6)
    base.append(tube("Hose", hose, 0.0065, black, 10))
    base.append(lathe("Nozzle", [(0.0, 0.0), (0.009, 0.0), (0.009, 0.01), (0.006, 0.04), (0.004, 0.05), (0.0, 0.05)], black, 14,
                      xf=M4(rot('X', 180), (0.062, -0.01, 0.11))))
    base.append(lathe("Clip", [(R + 0.0004, 0.0), (R + 0.003, 0.0), (R + 0.003, 0.012), (R + 0.0004, 0.012)], chrome, 40, cap_bottom=False, cap_top=False,
                      xf=M4(None, (0, 0, 0.282))))
    # The tag on its string, hanging from the pin's ring.
    base.append(tube("TagString", catmull([(-0.035, -0.004, 0.41), (-0.04, -0.012, 0.385), (-0.042, -0.016, 0.36)], 4), 0.0006, black, 4))
    t = rbox("Tag", (0.036, 0.0012, 0.058), (0, 0, 0), tag, 0.0005)
    place(t, rot('Z', 18), (-0.043, -0.018, 0.33))
    base.append(t)
    # The label: a printed band round the front of the body (110 degrees each side of -Y).
    bm = bmesh.new()
    n, z0, z1, a0, a1 = 22, 0.1, 0.27, math.radians(-145), math.radians(-35)
    rows = []
    for z in (z0, z1):
        rows.append([bm.verts.new((math.cos(a0 + (a1 - a0) * i / n) * (R + 0.0004), math.sin(a0 + (a1 - a0) * i / n) * (R + 0.0004), z)) for i in range(n + 1)])
    for i in range(n):
        bm.faces.new((rows[0][i], rows[0][i + 1], rows[1][i + 1], rows[1][i]))
    lab = new_obj("Label", bm, [label])
    set_uv(lab, lambda p, nn: ((math.atan2(p.y, p.x) - a0) / (a1 - a0), (p.z - z0) / (z1 - z0)))   # (u left to right seen from the front)
    return finish_mixed(base, [face, lab], "Extinguisher", sharp_deg=50)


def extinguisher_cabinet():
    """A surface-mounted steel cabinet (280 x 620 x 140 mm) with a rolled front flange, its door frame shut (the glass
    long gone: a few shards left in the corners of the frame, more on the cabinet's floor), a pull, and the printed FIRE
    EXTINGUISHER strip across the top of the door. Origin on the wall at the bottom middle of the cabinet; slot
    CabinetPaint is white enamel or red in the game."""
    paint = mat("CabinetPaint", (0.85, 0.84, 0.8), 0.45)
    frame = mat("CabinetFrame", (0.7, 0.7, 0.68), 0.3, 0.8)
    shard = mat("GlassShard", (0.55, 0.6, 0.6), 0.05)
    strip = mat("CabinetLabel", (1, 1, 1), 0.4)
    Wd, H, D, th = 0.28, 0.62, 0.14, 0.012
    base = [
        rbox("Back", (Wd, th, H), (0, -th / 2, H / 2), paint, 0.002),
        rbox("SideL", (th, D, H), (-Wd / 2 + th / 2, -D / 2, H / 2), paint, 0.002),
        rbox("SideR", (th, D, H), (Wd / 2 - th / 2, -D / 2, H / 2), paint, 0.002),
        rbox("Top", (Wd, D, th), (0, -D / 2, H - th / 2), paint, 0.002),
        rbox("Bottom", (Wd, D, th), (0, -D / 2, th / 2), paint, 0.002),
    ]
    # The rolled flange round the front.
    fl = 0.018
    for (sx, sz, cx, cz) in ((Wd + 2 * fl, fl, 0, H + fl / 2 - 0.002), (Wd + 2 * fl, fl, 0, -fl / 2 + 0.002),
                             (fl, H, -Wd / 2 - fl / 2 + 0.002, H / 2), (fl, H, Wd / 2 + fl / 2 - 0.002, H / 2)):
        base.append(rbox("Flange", (sx, 0.008, sz), (cx, -D + 0.004, cz), paint, 0.003, 3))
    # The door: a frame of rounded bars on piano hinges, a pull.
    fw, fd = 0.024, 0.014
    y = -D - fd / 2 - 0.002
    for (sx, sz, cx, cz) in ((Wd, fw, 0, H - fw / 2), (Wd, fw, 0, fw / 2), (fw, H - 2 * fw, -Wd / 2 + fw / 2, H / 2), (fw, H - 2 * fw, Wd / 2 - fw / 2, H / 2)):
        base.append(rbox("Door", (sx, fd, sz), (cx, y, cz), frame, 0.004, 2))
    base.append(tube("Hinge", [(-Wd / 2 - 0.004, y + 0.004, 0.02), (-Wd / 2 - 0.004, y + 0.004, H - 0.02)], 0.004, frame, 8))
    base.append(rbox("Pull", (0.012, 0.016, 0.08), (Wd / 2 - fw / 2, y - 0.012, H * 0.55), frame, 0.004))
    # Glass left in the corners of the frame, and on the floor of the cabinet.
    def shard_tri(name, a, b, c, thick=0.003):
        bm = bmesh.new()
        A, B, C = Vector(a), Vector(b), Vector(c)
        n = (B - A).cross(C - A).normalized() * thick * 0.5
        v = [bm.verts.new(p) for p in (A - n, B - n, C - n, A + n, B + n, C + n)]
        bm.faces.new((v[0], v[2], v[1])); bm.faces.new((v[3], v[4], v[5]))
        for i, j in ((0, 1), (1, 2), (2, 0)):
            bm.faces.new((v[i], v[j], v[j + 3], v[i + 3]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return new_obj(name, bm, [shard])
    gy = y + 0.001
    ix, iz = Wd / 2 - fw, fw
    base.append(shard_tri("Shard", (-ix, gy, H - iz), (-ix + 0.07, gy, H - iz), (-ix, gy, H - iz - 0.1)))
    base.append(shard_tri("Shard", (ix, gy, iz), (ix - 0.05, gy, iz), (ix, gy, iz + 0.14)))
    base.append(shard_tri("Shard", (ix, gy, H - iz), (ix, gy, H - iz - 0.06), (ix - 0.03, gy, H - iz)))
    for k, (x, d) in enumerate(((-0.06, 0.05), (0.03, 0.09), (0.08, 0.04))):
        base.append(shard_tri("Shard", (x, -d, th + 0.002), (x + 0.025, -d - 0.01, th + 0.002), (x + 0.008, -d + 0.02, th + 0.002)))
    # The printed strip across the top of the door.
    bm = bmesh.new()
    sy = y - fd / 2 - 0.0006
    q = [bm.verts.new(p) for p in ((-Wd / 2 + 0.004, sy, H - fw + 0.003), (Wd / 2 - 0.004, sy, H - fw + 0.003), (Wd / 2 - 0.004, sy, H - 0.003), (-Wd / 2 + 0.004, sy, H - 0.003))]
    bm.faces.new(q)
    lab = new_obj("CabinetLabel", bm, [strip])
    set_uv(lab, lambda p, n: ((p.x + Wd / 2) / Wd, (p.z - (H - fw)) / fw))
    return finish_mixed(base, [lab], "ExtinguisherCabinet", sharp_deg=50)


def extinguisher_bracket():
    """A wall hook for an extinguisher: a pressed steel back plate on two screws and the hooked strap its handle hangs
    on. Origin on the wall at the hook's height (where the handle rests)."""
    steel = mat("BracketSteel", (0.12, 0.12, 0.12), 0.45, 0.6)
    base = [rbox("Plate", (0.05, 0.004, 0.14), (0, -0.002, -0.04), steel, 0.0015, 2)]
    hook = catmull([(0, -0.004, -0.07), (0, -0.035, -0.064), (0, -0.062, -0.035), (0, -0.068, -0.004), (0, -0.064, 0.012)], 5)
    rings = []
    for p in hook:
        rings.append([Vector(p) + Vector(o) for o in ((-0.016, 0, -0.002), (0.016, 0, -0.002), (0.016, 0, 0.002), (-0.016, 0, 0.002))])
    base.append(loft("Hook", rings, steel, True, True, recalc=True))
    for z in (0.02, -0.1):
        base += screw("Screw", (0, -0.004, z), (0, -1, 0), 0.004, steel)
    return finish(base, "ExtinguisherBracket", sharp_deg=50)


def pull_station():
    """A manual pull station (red die-cast, 110 x 150 x 50 mm): its body with a raised front and rounded corners, FIRE
    raised across the top, the white handle that pulls down (PULL DOWN on it), the reset keyhole, and the grey back box
    ring behind it. Origin on the wall at its middle (the game puts it at 1.17 m)."""
    red = mat("PullRed", (0.56, 0.035, 0.03), 0.38)
    white = mat("PullHandle", (0.9, 0.89, 0.86), 0.35)
    grey = mat("BoxGrey", (0.35, 0.35, 0.34), 0.5, 0.4)
    chrome = mat("ExtChrome", (0.82, 0.82, 0.8), 0.18, 1.0)
    fire = mat("PullFire", (1, 1, 1), 0.38)
    pulltxt = mat("PullText", (1, 1, 1), 0.35)
    base = [
        rbox("Ring", (0.125, 0.006, 0.165), (0, -0.003, 0), grey, 0.002),
        rbox("Body", (0.11, 0.044, 0.15), (0, -0.028, 0), red, 0.009, 3),
        rbox("Front", (0.094, 0.006, 0.13), (0, -0.051, 0.0), red, 0.004, 2),
        rbox("HandleWell", (0.086, 0.004, 0.036), (0, -0.0545, -0.006), grey, 0.004),
    ]
    # The handle: a bar standing out from the well, its lip underneath for the fingers.
    base.append(rbox("Handle", (0.08, 0.016, 0.03), (0, -0.062, -0.006), white, 0.005, 2))
    base.append(rbox("HandleLip", (0.08, 0.02, 0.006), (0, -0.068, -0.022), white, 0.0025))
    base.append(lathe("Keyhole", [(0.0, 0.0), (0.0055, 0.0), (0.0055, 0.003), (0.0, 0.003)], chrome, 16, xf=M4(rot('X', 90), (0.0, -0.054, -0.052))))
    base.append(rbox("KeySlot", (0.0012, 0.001, 0.005), (0, -0.0575, -0.052), mat("PullDark", (0.02, 0.02, 0.02), 0.6)))
    # FIRE across the top, PULL DOWN on the handle: printed.
    bm = bmesh.new()
    yf = -0.0542
    q = [bm.verts.new(p) for p in ((-0.042, yf, 0.026), (0.042, yf, 0.026), (0.042, yf, 0.058), (-0.042, yf, 0.058))]
    bm.faces.new(q)
    f = new_obj("FireText", bm, [fire])
    set_uv(f, lambda p, n: ((p.x + 0.042) / 0.084, (p.z - 0.026) / 0.032))
    bm = bmesh.new()
    yh = -0.0705
    q = [bm.verts.new(p) for p in ((-0.036, yh, -0.017), (0.036, yh, -0.017), (0.036, yh, 0.005), (-0.036, yh, 0.005))]
    bm.faces.new(q)
    h = new_obj("PullText", bm, [pulltxt])
    set_uv(h, lambda p, n: ((p.x + 0.036) / 0.072, (p.z + 0.017) / 0.022))
    return finish_mixed(base, [f, h], "PullStation", sharp_deg=50)


def horn_strobe():
    """A horn strobe (dead): a red housing on its plate, the horn's grille slots below, the strobe's ribbed lens above,
    FIRE printed between. Origin on the wall at its middle."""
    red = mat("StrobeRed", (0.55, 0.035, 0.03), 0.38)
    dark = mat("StrobeDark", (0.03, 0.02, 0.02), 0.7)
    lens = mat("StrobeLens", (0.92, 0.92, 0.9), 0.12)
    fire = mat("StrobeFire", (1, 1, 1), 0.38)
    base = [
        rbox("Plate", (0.14, 0.006, 0.14), (0, -0.003, 0), red, 0.003),
        rbox("Housing", (0.125, 0.04, 0.125), (0, -0.026, 0), red, 0.012, 3),
    ]
    for k in range(6):
        base.append(rbox("Slot", (0.07, 0.003, 0.0045), (0, -0.0465, -0.052 + k * 0.0085), dark, 0.0012))
    # The lens: a rounded bar of clear plastic, ribbed.
    base.append(rbox("Lens", (0.075, 0.022, 0.04), (0, -0.054, 0.032), lens, 0.009, 3))
    for k in range(7):
        base.append(rbox("Rib", (0.003, 0.004, 0.032), (-0.03 + k * 0.01, -0.0655, 0.032), lens, 0.0012))
    bm = bmesh.new()
    yf = -0.0465
    q = [bm.verts.new(p) for p in ((-0.034, yf, -0.004), (0.034, yf, -0.004), (0.034, yf, 0.01), (-0.034, yf, 0.01))]
    bm.faces.new(q)
    f = new_obj("FireText", bm, [fire])
    set_uv(f, lambda p, n: ((p.x + 0.034) / 0.068, (p.z + 0.004) / 0.014))
    return finish_mixed(base, [f], "HornStrobe", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- air

def thermostat_box():
    """A beige programmable thermostat (125 x 85 x 28 mm): a rounded body, its door with the display window and three
    buttons, vent slots along the top and bottom. Origin on the wall at its middle (1.45 m)."""
    beige = mat("ThermoBeige", (0.74, 0.7, 0.6), 0.45)
    dark = mat("ThermoDark", (0.06, 0.06, 0.055), 0.5)
    lcd = mat("ThermoLcd", (1, 1, 1), 0.15)
    base = [rbox("Body", (0.125, 0.022, 0.085), (0, -0.011, 0), beige, 0.008, 3),
            rbox("Door", (0.112, 0.007, 0.07), (0, -0.024, -0.002), beige, 0.004, 2),
            rbox("Window", (0.054, 0.003, 0.028), (-0.022, -0.0278, 0.012), dark, 0.002)]
    for k in range(3):
        base.append(rbox("Button", (0.014, 0.004, 0.009), (0.022 + k * 0.0, -0.029, 0.017 - k * 0.014), beige, 0.0025))
    for side in (1, -1):
        for k in range(9):
            base.append(rbox("Vent", (0.006, 0.012, 0.0015), (-0.04 + k * 0.01, -0.011, side * 0.0428), dark, 0.0006))
    bm = bmesh.new()
    yl = -0.0296
    q = [bm.verts.new(p) for p in ((-0.047, yl, 0.0005), (0.003, yl, 0.0005), (0.003, yl, 0.0235), (-0.047, yl, 0.0235))]
    bm.faces.new(q)
    f = new_obj("Lcd", bm, [lcd])
    set_uv(f, lambda p, n: ((p.x + 0.047) / 0.05, (p.z - 0.0005) / 0.023))
    return finish_mixed(base, [f], "ThermostatBox", sharp_deg=45)


def thermostat_round():
    """An old round thermostat: its square wall plate, the round cover with a domed rim, the printed scale ring round the
    clear dial, the setting pointer. Origin on the wall at its middle."""
    beige = mat("ThermoBeige", (0.74, 0.7, 0.6), 0.45)
    gold = mat("ThermoGold", (0.72, 0.55, 0.25), 0.3, 0.9)
    scale = mat("ThermoScale", (1, 1, 1), 0.3)
    dark = mat("ThermoDark", (0.06, 0.06, 0.055), 0.5)
    base = [rbox("Plate", (0.1, 0.008, 0.1), (0, -0.004, 0), beige, 0.004, 2)]
    X = M4(rot('X', 90), (0, -0.008, 0))
    base.append(lathe("Cover", [(0.0, 0.0), (0.042, 0.0), (0.043, 0.006), (0.041, 0.016), (0.036, 0.022), (0.03, 0.024), (0.0, 0.024)], beige, 40, xf=X))
    base.append(lathe("Ring", [(0.03, 0.0236), (0.0335, 0.0236), (0.0335, 0.0256), (0.03, 0.0256)], gold, 40, cap_bottom=False, cap_top=False, xf=X))
    base.append(rbox("Pointer", (0.004, 0.003, 0.014), (0, -0.0335, 0.019), dark, 0.0008))
    disc = bmesh.new()
    vs = [disc.verts.new((0.0298 * math.cos(2 * math.pi * i / 40), -0.0324, 0.0298 * math.sin(2 * math.pi * i / 40))) for i in range(40)]
    disc.faces.new(vs)
    s = new_obj("Scale", disc, [scale])
    set_uv(s, lambda p, n: (p.x / 0.0596 + 0.5, p.z / 0.0596 + 0.5))
    return finish_mixed(base, [s], "ThermostatRound", sharp_deg=45)


def return_grille():
    """A stamped steel return-air grille, 320 x 160 mm: a flange with rounded edges, eight bent louvres, dark behind,
    two screws. Origin on the wall at its middle."""
    white = mat("GrillePaint", (0.86, 0.85, 0.82), 0.4, 0.2)
    dark = mat("GrilleDark", (0.02, 0.02, 0.02), 0.9)
    Wd, H, fw = 0.32, 0.16, 0.018
    base = [rbox("Back", (Wd - 2 * fw, 0.002, H - 2 * fw), (0, -0.001, 0), dark)]
    for (sx, sz, cx, cz) in ((Wd, fw, 0, H / 2 - fw / 2), (Wd, fw, 0, -H / 2 + fw / 2), (fw, H - 2 * fw, -Wd / 2 + fw / 2, 0), (fw, H - 2 * fw, Wd / 2 - fw / 2, 0)):
        base.append(rbox("Flange", (sx, 0.008, sz), (cx, -0.004, cz), white, 0.003, 2))
    for k in range(8):
        z = -H / 2 + fw + (k + 0.5) * (H - 2 * fw) / 8
        l = rbox("Louvre", (Wd - 2 * fw, 0.016, 0.0018), (0, 0, 0), white, 0.0007)
        place(l, rot('X', 38), (0, -0.007, z))
        base.append(l)
    for x in (-Wd / 2 + fw / 2, Wd / 2 - fw / 2):
        base += screw("Screw", (x, -0.008, 0), (0, -1, 0), 0.0035, white)
    return finish(base, "ReturnGrille", sharp_deg=45)


# ------------------------------------------------------------------------------------------------- water

def drinking_fountain():
    """A wall-hung stainless drinking fountain: the basin with its moulded bowl (one surface, rim to drain), the
    bubbler and its guard, the front push bar, the drain grate, the backsplash, and the shroud underneath. Origin on
    the wall at floor level under its middle; the basin's rim at 0.86 m."""
    steel = mat("FountainSteel", (0.62, 0.63, 0.63), 0.32, 0.9)
    chrome = mat("ExtChrome", (0.82, 0.82, 0.8), 0.18, 1.0)
    dark = mat("FountainDark", (0.03, 0.03, 0.03), 0.6)
    top, depth, half = 0.86, 0.3, 0.2
    cy = -depth / 2 - 0.005
    rings = []
    n = 48
    # Down the outside, under it, then back up over the rim and down the bowl to the drain.
    rings.append(ring_pts(0, cy, top - 0.08, half - 0.01, depth / 2 - 0.01, n, 5))
    rings.append(ring_pts(0, cy, top - 0.02, half, depth / 2, n, 5))
    rings.append(ring_pts(0, cy, top, half - 0.008, depth / 2 - 0.008, n, 5))
    rings.append(ring_pts(0, cy - 0.01, top, half - 0.03, depth / 2 - 0.035, n, 3))
    rings.append(ring_pts(0, cy - 0.012, top - 0.025, half - 0.06, depth / 2 - 0.06, n, 2.6))
    rings.append(ring_pts(0, cy - 0.014, top - 0.04, half - 0.11, depth / 2 - 0.1, n, 2.2))
    rings.append(ring_pts(0, cy - 0.015, top - 0.045, 0.02, 0.02, n, 2))
    base = [loft("Basin", rings, steel, True, False, recalc=True)]
    base.append(lathe("Drain", [(0.0, 0.0), (0.021, 0.0), (0.021, 0.002), (0.0, 0.002)], dark, 20, xf=M4(None, (0, cy - 0.015, top - 0.046))))
    for k in range(5):
        base.append(rbox("Grate", (0.03, 0.002, 0.0015), (0, cy - 0.015 - 0.012 + k * 0.006, top - 0.0435), chrome))
    # The bubbler and its guard.
    bx, by = 0.09, cy + 0.06
    base.append(lathe("Bubbler", [(0.0, 0.0), (0.014, 0.0), (0.012, 0.02), (0.009, 0.034), (0.006, 0.04), (0.0, 0.041)], chrome, 18,
                      xf=M4(None, (bx, by, top - 0.03))))
    base.append(tube("Guard", catmull([(bx - 0.02, by + 0.01, top - 0.02), (bx - 0.02, by - 0.01, top + 0.03), (bx + 0.02, by - 0.01, top + 0.03), (bx + 0.02, by + 0.01, top - 0.02)], 5),
                     0.003, chrome, 8))
    # The push bar on the front, the shroud below, the backsplash.
    base.append(rbox("PushBar", (0.14, 0.012, 0.026), (0, -depth - 0.012, top - 0.05), chrome, 0.005, 2))
    base.append(rbox("Shroud", (0.34, 0.24, 0.3), (0, -0.13, top - 0.08 - 0.15), steel, 0.012, 3))
    base.append(rbox("Splash", (0.44, 0.006, 0.38), (0, -0.003, top + 0.17), steel, 0.004, 2))
    for x in (-0.19, 0.19):
        for z in (top + 0.03, top + 0.32):
            base += screw("Screw", (x, -0.006, z), (0, -1, 0), 0.004, chrome)
    return finish(base, "DrinkingFountain", sharp_deg=40)


# ------------------------------------------------------------------------------------------------- windows

def filled_window(kind):
    """A window filled in (kind 0 plain, 1 a mullion, 2 a mullion and a transom): the aluminium frame and its glazing
    bead left in the wall, painted board where the glass was with its taped joints and screw heads, the stool (sill) and
    apron under it. 950 x 1120 mm. Origin on the wall at the bottom middle of the frame."""
    alu = mat("WindowFrame", (0.68, 0.68, 0.66), 0.35, 0.85)
    board = mat("WindowBoard", (0.86, 0.84, 0.78), 0.6)
    tape = mat("WindowJoint", (0.9, 0.88, 0.83), 0.7)
    sill = mat("WindowSill", (0.84, 0.83, 0.8), 0.45)
    screwm = mat("ScrewHead", (0.5, 0.5, 0.48), 0.4, 0.6)
    Wd, H, fw, fd = 0.95, 1.12, 0.05, 0.04
    base = []
    for (sx, sz, cx, cz) in ((Wd, fw, 0, H - fw / 2), (Wd, fw, 0, fw / 2), (fw, H - 2 * fw, -Wd / 2 + fw / 2, H / 2), (fw, H - 2 * fw, Wd / 2 - fw / 2, H / 2)):
        base.append(rbox("Frame", (sx, fd, sz), (cx, -fd / 2, cz), alu, 0.004, 2))
    # The glazing bead, a step inside the frame.
    ib = 0.014
    for (sx, sz, cx, cz) in ((Wd - 2 * fw, ib, 0, H - fw - ib / 2), (Wd - 2 * fw, ib, 0, fw + ib / 2), (ib, H - 2 * fw - 2 * ib, -Wd / 2 + fw + ib / 2, H / 2), (ib, H - 2 * fw - 2 * ib, Wd / 2 - fw - ib / 2, H / 2)):
        base.append(rbox("Bead", (sx, 0.02, sz), (cx, -0.022, cz), alu, 0.002))
    if kind >= 1:
        base.append(rbox("Mullion", (0.038, fd * 0.9, H - 2 * fw), (0, -fd * 0.45, H / 2), alu, 0.004, 2))
    if kind >= 2:
        base.append(rbox("Transom", (Wd - 2 * fw, fd * 0.9, 0.038), (0, -fd * 0.45, H * 0.72), alu, 0.004, 2))
    # The board, set back in the frame; its joints taped and mudded, its screws.
    pw, ph = Wd / 2 - fw - ib, H / 2 - fw - ib
    base.append(rbox("Board", (2 * pw, 0.006, 2 * ph), (0, -0.012, H / 2), board, 0.001))
    base.append(rbox("Joint", (0.05, 0.0012, 2 * ph - 0.01), (0.0 if kind == 0 else -pw * 0.5, -0.0156, H / 2), tape, 0.0005))
    for k in range(7):
        z = fw + ib + 0.05 + (2 * ph - 0.1) * k / 6
        for x in (-pw + 0.03, pw - 0.03):
            base.append(lathe("Screw", [(0.0, 0.0), (0.0045, 0.0), (0.004, 0.0008), (0.0, 0.0011)], screwm, 10, xf=M4(rot('X', 90), (x, -0.015, z))))
    # The stool and its apron, bullnosed, painted.
    base.append(rbox("Stool", (Wd + 0.1, 0.13, 0.028), (0, -0.065, -0.014), sill, 0.008, 3))
    base.append(rbox("Apron", (Wd, 0.016, 0.07), (0, -0.008, -0.063), sill, 0.003, 2))
    return finish(base, ("FilledWindow", "FilledWindowMullion", "FilledWindowCross")[kind], sharp_deg=50)


# ------------------------------------------------------------------------------------------------- corners, doors

def corner_guard(left):
    """A corner guard: a steel angle, 45 mm legs, 1.2 m tall, its outer corner rounded and its edges rolled back,
    screwed on. The corner at the origin; one leg along +X on the wall's face (y = 0, standing out toward -Y), the
    other along +Y round the corner (on the jamb, x = 0, standing out toward -X); left: mirrored in X."""
    steel = mat("GuardSteel", (0.66, 0.66, 0.65), 0.3, 0.9)
    s = 1.0 if not left else -1.0
    leg, th, H = 0.045, 0.0016, 1.2
    # The section, round: out along the wall's face, round the corner (centred on the wall's own corner), back along
    # the jamb, then the inside back the other way.
    sec = [(leg, -th)]
    for k in range(7):
        a = math.radians(-90 - 90 * k / 6)
        sec.append((th * math.cos(a), th * math.sin(a)))
    sec += [(-th, leg), (0.0, leg), (0.0, 0.0), (leg, 0.0)]
    rings = [[Vector((x * s, y, z)) for x, y in sec] for z in (0.0, H)]
    o = loft("Guard", rings, steel, True, True, recalc=True)
    parts = [o]
    for z in (0.12, H / 2, H - 0.12):
        parts.append(lathe("Screw", [(0.0, 0.0), (0.0035, 0.0), (0.003, 0.0008), (0.0, 0.001)], steel, 10, xf=M4(rot('X', 90), (0.028 * s, -th, z))))
    return finish(parts, "CornerGuardL" if left else "CornerGuardR", sharp_deg=60)


def door_closer_body():
    """A door closer's body (cover on): a rounded box with its end caps and the spindle's boss underneath, the two
    adjusting screws in the end. Frame: x along the door (+X toward the latch), standing out toward -Y, origin at the
    middle of its back on the door; the spindle at x = -0.1 under it."""
    paint = mat("CloserPaint", (0.62, 0.62, 0.6), 0.38, 0.7)
    dark = mat("CloserDark", (0.08, 0.08, 0.08), 0.5)
    base = [rbox("Body", (0.27, 0.055, 0.062), (0, -0.0275, 0), paint, 0.012, 3)]
    for x in (-0.137, 0.137):
        base.append(rbox("Cap", (0.006, 0.05, 0.056), (x, -0.0275, 0), dark, 0.004, 2))
    base.append(lathe("Boss", [(0.0, 0.0), (0.014, 0.0), (0.014, 0.012), (0.0, 0.012)], paint, 16, xf=M4(rot('X', 180), (-0.1, -0.03, -0.031))))
    for z in (-0.012, 0.012):
        base.append(lathe("Valve", [(0.0, 0.0), (0.003, 0.0), (0.003, 0.002), (0.0, 0.002)], dark, 10, xf=M4(rot('Y', 90), (0.14, -0.028, z))))
    return finish(base, "CloserBody", sharp_deg=50)


def door_closer_arm():
    """The closer's main arm, 210 mm: pressed steel, wider at the spindle, the elbow's knuckle at its end. Frame: from
    the origin (the spindle) along -Y (Unity +Z), lying flat (its width along X)."""
    paint = mat("CloserPaint", (0.62, 0.62, 0.6), 0.38, 0.7)
    L = 0.21
    rings = []
    for k in range(9):
        t = k / 8
        w = 0.013 * (1 - t) + 0.009 * t
        y = -L * t
        rings.append([Vector(p) for p in ((-w, y, -0.003), (w, y, -0.003), (w, y, 0.003), (-w, y, 0.003))])
    base = [loft("Arm", rings, paint, True, True, recalc=True)]
    base.append(lathe("Spindle", [(0.0, -0.01), (0.012, -0.01), (0.012, 0.006), (0.0, 0.006)], paint, 16))
    base.append(lathe("Knuckle", [(0.0, -0.004), (0.009, -0.004), (0.009, 0.012), (0.0, 0.012)], paint, 14, xf=M4(None, (0, -L, 0))))
    return finish(base, "CloserArm", sharp_deg=60)


def door_closer_forearm():
    """The forearm: a round rod 1 m long along -Y from the origin (the game scales it to the length it needs: its
    knuckles are the arm's and the shoe's)."""
    paint = mat("CloserPaint", (0.62, 0.62, 0.6), 0.38, 0.7)
    return finish([tube("Rod", [(0, 0, 0), (0, -1.0, 0)], 0.0055, paint, 10)], "CloserForearm", sharp_deg=60)


def door_closer_shoe():
    """The shoe on the frame head: a bracket with the forearm's pivot. Origin at the pivot; its back toward +Y (the frame)."""
    paint = mat("CloserPaint", (0.62, 0.62, 0.6), 0.38, 0.7)
    base = [rbox("Shoe", (0.05, 0.014, 0.034), (0, 0.012, 0), paint, 0.004, 2),
            lathe("Pivot", [(0.0, -0.01), (0.008, -0.01), (0.008, 0.01), (0.0, 0.01)], paint, 12)]
    return finish(base, "CloserShoe", sharp_deg=50)


def kick_plate():
    """A brushed stainless kick plate, 907 x 250 x 1.3 mm with bevelled edges and six countersunk screws. Origin at the
    middle of its back; its face toward -Y."""
    steel = mat("KickSteel", (0.7, 0.7, 0.7), 0.42, 1.0)
    base = [rbox("Plate", (0.907, 0.0013, 0.25), (0, -0.00065, 0), steel, 0.0006, 2)]
    for x in (-0.43, 0.0, 0.43):
        for z in (-0.105, 0.105):
            base.append(lathe("Screw", [(0.0, 0.0), (0.0042, 0.0), (0.0042, 0.0003), (0.0, 0.0006)], steel, 12, xf=M4(rot('X', 90), (x, -0.0013, z))))
    return finish(base, "KickPlate", sharp_deg=60)


def door_wedge():
    """A rubber door wedge, 130 x 45 x 35 mm, the grip ridges across its slope, rounded. Origin on the floor at its
    middle; thin end toward -Y."""
    rubber = mat("WedgeRubber", (0.04, 0.04, 0.04), 0.75)
    L, Wd, H = 0.13, 0.045, 0.035
    sec = [(-L / 2, 0.0), (L / 2, 0.0), (L / 2, H), (L / 2 - 0.006, H + 0.001), (-L / 2, 0.003)]
    rings = []
    for x in (-Wd / 2, Wd / 2):
        rings.append([Vector((x, -y, z)) for y, z in sec])
    base = [loft("Wedge", rings, rubber, True, True, recalc=True)]
    for k in range(6):
        t = 0.15 + 0.13 * k
        y = -L / 2 + L * t
        z = 0.003 + (H - 0.003) * t
        r = rbox("Ridge", (Wd - 0.006, 0.004, 0.003), (0, 0, 0), rubber, 0.001)
        place(r, rot('X', -math.degrees(math.atan2(H, L))), (0, -y, z + 0.0012))
        base.append(r)
    return finish(base, "DoorWedge", sharp_deg=45)


# ------------------------------------------------------------------------------------------------- floor

def floor_box(kind):
    """A floor box flush with the carpet: kind 0 round brass with two flip lids, 1 square brass frame with a carpet
    insert (slot CarpetInsert: the room's own carpet in the game), 2 round with its lid gone, the receptacle and cut
    cables showing. Origin on the floor at its middle."""
    brass = mat("FloorBrass", (0.52, 0.4, 0.18), 0.35, 1.0)
    dark = mat("BoxDark", (0.015, 0.015, 0.015), 0.9)
    base = []
    if kind == 1:
        hs, ins = 0.11, 0.088
        for (sx, sy, cx, cy) in ((2 * hs, hs - ins, 0, hs - (hs - ins) / 2), (2 * hs, hs - ins, 0, -hs + (hs - ins) / 2),
                                 (hs - ins, 2 * ins, hs - (hs - ins) / 2, 0), (hs - ins, 2 * ins, -hs + (hs - ins) / 2, 0)):
            base.append(rbox("Frame", (sx, sy, 0.005), (cx, cy, 0.0025), brass, 0.0015, 2))
        carpet = mat("CarpetInsert", (0.6, 0.55, 0.4), 0.9)
        base.append(rbox("Insert", (2 * ins, 2 * ins, 0.004), (0, 0, 0.002), carpet, 0.0008))
        for x in (-hs + 0.011, hs - 0.011):
            for y in (-hs + 0.011, hs - 0.011):
                base += screw("Screw", (x, y, 0.005), (0, 0, 1), 0.0035, brass, 0.0008)
        return finish(base, "FloorBoxSquare", sharp_deg=50)
    R = 0.104
    base.append(lathe("Flange", [(0.08, 0.0), (R, 0.0), (R - 0.002, 0.004), (0.088, 0.0055), (0.08, 0.0055)], brass, 48))
    if kind == 0:
        # Two half lids, the hinge line across, a finger slot in each.
        for s in (1, -1):
            pts = [(0.0, 0.0)] + [(0.0855 * math.cos(math.radians(a)), s * 0.0855 * math.sin(math.radians(a))) for a in range(0, 181, 10)]
            bm = bmesh.new()
            vb = [bm.verts.new((x, y, 0.0)) for x, y in pts]
            vt = [bm.verts.new((x, y, 0.005)) for x, y in pts]
            bm.faces.new(vt if s > 0 else list(reversed(vt)))
            bm.faces.new(list(reversed(vb)) if s > 0 else vb)
            for i in range(len(pts)):
                j = (i + 1) % len(pts)
                bm.faces.new((vb[i], vb[j], vt[j], vt[i]) if s > 0 else (vb[j], vb[i], vt[i], vt[j]))
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            lid = new_obj("Lid", bm, [brass])
            place(lid, None, (0, s * 0.001, 0))
            base.append(lid)
            base.append(rbox("Finger", (0.03, 0.006, 0.0012), (0, s * 0.06, 0.0052), dark, 0.0005))
        base.append(rbox("Hinge", (0.17, 0.002, 0.0015), (0, 0, 0.0048), dark))
        return finish(base, "FloorBoxRound", sharp_deg=50)
    # Open: the box inside, a duplex receptacle, cut cables.
    base.append(lathe("Well", [(0.08, 0.0055), (0.08, -0.06), (0.0, -0.06)], dark, 32, cap_bottom=False, cap_top=False))
    beige = mat("Receptacle", (0.78, 0.74, 0.64), 0.4)
    base.append(rbox("Receptacle", (0.07, 0.04, 0.012), (0, 0.015, -0.03), beige, 0.003))
    for x in (-0.018, 0.018):
        base.append(rbox("Slots", (0.012, 0.016, 0.001), (x, 0.015, -0.0235), dark))
    cables = [((-0.03, -0.03), (-0.045, -0.02, 0.05), mat("CableGrey", (0.45, 0.45, 0.44), 0.5)),
              ((0.02, -0.035), (0.05, -0.05, 0.035), mat("CableBlack", (0.03, 0.03, 0.03), 0.5)),
              ((0.0, -0.02), (-0.01, -0.06, 0.07), mat("CableGrey", (0.45, 0.45, 0.44), 0.5))]
    copper = mat("Copper", (0.72, 0.38, 0.2), 0.3, 1.0)
    for k, (b, tip, m) in enumerate(cables):
        pts = catmull([(b[0], b[1], -0.05), (b[0], b[1], 0.0), (tip[0] * 0.6, tip[1] * 0.8, tip[2] * 0.7), tip], 5)
        base.append(tube("Cable", pts, 0.0045, m, 8))
        d = (Vector(tip) - Vector(pts[-2])).normalized()
        for j, off in enumerate((-0.0015, 0.0015)):
            base.append(tube("Wire", [Vector(tip) + Vector((off, 0, 0)), Vector(tip) + d * 0.012 + Vector((off * 2, 0, 0.002 * j))], 0.0009, copper, 5))
    return finish(base, "FloorBoxOpen", sharp_deg=50)


def bucket():
    """A plastic mop bucket (10 l): tapered, ribs moulded round it, a rolled rim with a pouring lip, the handle's ears,
    the wire handle folded down with its grip. Origin on the floor at its middle."""
    plastic = mat("BucketPlastic", (0.62, 0.5, 0.1), 0.35)
    wire = mat("HandleWire", (0.6, 0.6, 0.58), 0.35, 0.9)
    grip = mat("HandleGrip", (0.08, 0.08, 0.08), 0.5)
    rb, rt, h = 0.12, 0.148, 0.28
    prof = [(0.0, 0.0), (rb - 0.006, 0.0), (rb, 0.006)]
    for k, z in enumerate((0.05, 0.1, 0.15, 0.2, 0.245)):
        r = rb + (rt - rb) * z / h
        prof += [(r, z - 0.006), (r + 0.003, z - 0.002), (r + 0.003, z + 0.002), (r, z + 0.006)]
    prof += [(rt, h - 0.012), (rt + 0.008, h - 0.006), (rt + 0.008, h), (rt - 0.006, h + 0.002), (rt - 0.007, h - 0.008)]
    # down the inside
    prof += [(rt - 0.008, h - 0.01), (rb - 0.006, 0.012), (0.0, 0.012)]
    base = [lathe("Bucket", prof, plastic, 48, cap_bottom=False, cap_top=False)]
    for s in (1, -1):
        base.append(rbox("Ear", (0.016, 0.02, 0.03), (s * (rt + 0.006), 0, h - 0.03), plastic, 0.004))
    # The handle folded down round the front, outside the wall (lower toward the middle), its grip at the front.
    def r_at(z):
        return rb + (rt - rb) * max(0.0, min(1.0, z / h)) + 0.008
    pts = []
    for a in range(0, 181, 10):
        t = math.sin(math.radians(a))
        z = h - 0.03 - 0.1 * t
        rr = r_at(z) + 0.004
        pts.append((math.cos(math.radians(a)) * rr, -math.sin(math.radians(a)) * rr, z))
    base.append(tube("Handle", pts, 0.0028, wire, 6))
    zg = h - 0.13
    base.append(lathe("Grip", [(0.0, -0.05), (0.008, -0.05), (0.009, -0.04), (0.009, 0.04), (0.008, 0.05), (0.0, 0.05)], grip, 12,
                      xf=M4(rot('Y', 90), (0, -(r_at(zg) + 0.004), zg))))
    return finish(base, "Bucket", sharp_deg=50)


# ------------------------------------------------------------------------------------------------- partitions

def partition(width, height):
    """A cubicle panel: an aluminium frame of rounded extrusion, fabric faces set in it on both sides, a top cap, the
    base raceway with its removable covers and outlet knockouts, levelling glides. Origin on the floor at its middle;
    its faces toward -Y and +Y; 64 mm thick."""
    alu = mat("PanelFrame", (0.62, 0.62, 0.6), 0.35, 0.8)
    fabric = mat("PanelFabric", (0.33, 0.37, 0.42), 0.95)
    trim = mat("PanelTrim", (0.22, 0.22, 0.22), 0.4)
    dark = mat("PanelDark", (0.03, 0.03, 0.03), 0.8)
    T, fw, base_h = 0.064, 0.028, 0.11
    base = []
    for x in (-width / 2 + fw / 2, width / 2 - fw / 2):
        base.append(rbox("Upright", (fw, T, height - 0.02), (x, 0, (height - 0.02) / 2 + 0.01), alu, 0.008, 3))
    base.append(rbox("Cap", (width, T + 0.006, 0.022), (0, 0, height - 0.011), trim, 0.009, 3))
    base.append(rbox("Rail", (width - 2 * fw, T - 0.006, 0.02), (0, 0, base_h + 0.01), alu, 0.004))
    # The fabric: two faces each side, a hairline between them.
    fh = height - 0.022 - base_h - 0.02
    for s in (1, -1):
        base.append(rbox("Fabric", (width - 2 * fw - 0.002, 0.008, fh), (0, s * (T / 2 - 0.006), base_h + 0.02 + fh / 2), fabric, 0.0025, 2))
        # The raceway cover, a seam, the knockouts.
        base.append(rbox("Raceway", (width - 2 * fw, 0.006, base_h - 0.02), (0, s * (T / 2 - 0.003), 0.02 + (base_h - 0.02) / 2), trim, 0.002))
        for x in (-width * 0.3, width * 0.3):
            base.append(rbox("Knockout", (0.07, 0.002, 0.036), (x, s * (T / 2 + 0.0006), 0.065), dark, 0.002))
    for x in (-width / 2 + 0.06, width / 2 - 0.06):
        base.append(rbox("Foot", (0.05, T + 0.2, 0.018), (x, 0, 0.012), alu, 0.004, 2))
        for y in (-0.1, 0.1):
            base.append(lathe("Glide", [(0.0, 0.0), (0.016, 0.0), (0.016, 0.006), (0.006, 0.01), (0.006, 0.02), (0.0, 0.02)], dark, 14, xf=M4(None, (x, y, 0.0))))
    name = "Partition_%d_%d" % (round(width * 100), round(height * 100))
    return finish(base, name, sharp_deg=50)


# ------------------------------------------------------------------------------------------------- wooden chair

def wooden_chair():
    """A plain wooden chair (a waiting room's, a school's): legs octagonal and tapered, the back legs running up into
    raked posts, a dished seat with rounded edges, an apron and corner blocks under it, turned stretchers, a curved top
    rail and two curved slats, felt glides on the feet. Grain along each piece (slot Wood), the worn places lighter (slot
    WoodWorn: the seat's front edge, the top of the top rail, the front stretcher where heels rest). 45 cm seat, 86 cm
    back, front toward -Y."""
    wood = mat("Wood", (0.36, 0.22, 0.12), 0.55)
    worn = mat("WoodWorn", (0.5, 0.34, 0.2), 0.7)
    felt = mat("Felt", (0.35, 0.33, 0.3), 0.95)
    grained = []

    def piece(ob, axis):
        set_uv(ob, grain(axis))
        grained.append(ob)
        return ob

    def leg(name, foot, top, w0, w1, material=wood):
        a, b = Vector(foot), Vector(top)
        rings = []
        for k in range(9):
            t = k / 8
            c = a.lerp(b, t)
            w = w0 + (w1 - w0) * t
            rings.append([p + c - Vector((0, 0, c.z)) + Vector((0, 0, c.z)) for p in ring_pts(0, 0, 0, w, w, 12, 3.2)])
        ob = loft(name, rings, material, True, True, recalc=True)
        piece(ob, (b - a))
        return ob

    sh, sw, sd = 0.45, 0.42, 0.4
    fx, fy, bx, by = 0.185, -0.175, 0.175, 0.17
    for s in (-1, 1):
        leg("FrontLeg", (s * (fx + 0.018), fy - 0.022, 0.006), (s * fx, fy, sh - 0.012), 0.014, 0.018)
        leg("BackLeg", (s * (bx + 0.016), by + 0.04, 0.006), (s * bx, by, sh - 0.012), 0.014, 0.018)
        post = catmull([(s * bx, by, sh - 0.03), (s * (bx - 0.004), by + 0.02, 0.62), (s * (bx - 0.01), by + 0.05, 0.86)], 4)
        rings = []
        for k, p in enumerate(post):
            w = 0.017 - 0.002 * k / (len(post) - 1)
            rings.append([q + Vector(p) for q in ring_pts(0, 0, 0, w, w, 12, 3.2)])
        piece(loft("BackPost", rings, wood, True, True, recalc=True), (0, 0.15, 1))
        for (lx, ly) in ((s * (fx + 0.018), fy - 0.022), (s * (bx + 0.016), by + 0.04)):
            grained.append(lathe("Glide", [(0.0, 0.0), (0.012, 0.0), (0.013, 0.004), (0.0, 0.006)], felt, 12, xf=M4(None, (lx, ly, 0.0))))
    # The seat: a board whose top is dished, edges rounded (rings of its outline from the underside up over the edge).
    rings = []
    edge = [(0.0, -0.02), (0.006, -0.006), (0.004, 0.006), (0.0, 0.01)]
    for k, (inset, dz) in enumerate([(0.0, 0.0), (0.0, 0.016), (0.004, 0.024), (0.012, 0.028)]):
        rings.append(ring_pts(0, 0, sh - 0.012 + dz, sw / 2 - inset, sd / 2 - inset, 40, 6))
    # The dish: rings toward the middle, lower.
    for k, (sc, dz) in enumerate([(0.85, 0.026), (0.6, 0.022), (0.3, 0.02)]):
        rings.append(ring_pts(0, 0.01, sh - 0.012 + dz, (sw / 2 - 0.012) * sc, (sd / 2 - 0.012) * sc, 40, 4))
    rings.append(Vector((0, 0.012, sh - 0.012 + 0.019)))
    seat = loft("Seat", rings, wood, True, False, recalc=True)
    piece(seat, (1, 0, 0))
    # The worn front edge of the seat: a lighter strip over it.
    fe = rbox("SeatWear", (sw - 0.08, 0.012, 0.004), (0, 0, 0), worn, 0.002)
    place(fe, rot('X', -35), (0, -sd / 2 + 0.004, sh + 0.013))
    piece(fe, (1, 0, 0))
    # The apron under the seat, corner blocks.
    for (sx, sy, cx, cy) in ((sw - 0.07, 0.02, 0, fy + 0.004), (sw - 0.07, 0.02, 0, by - 0.004), (0.02, sd - 0.07, -0.178, 0), (0.02, sd - 0.07, 0.178, 0)):
        piece(rbox("Apron", (sx, sy, 0.058), (cx, cy, sh - 0.045), wood, 0.003, 2), (1, 0, 0) if sx > sy else (0, 1, 0))
    for (cx, cy) in ((-0.15, -0.14), (0.15, -0.14), (-0.15, 0.14), (0.15, 0.14)):
        blk = rbox("Block", (0.05, 0.05, 0.04), (0, 0, 0), wood, 0.002)
        place(blk, rot('Z', 45), (cx, cy, sh - 0.04))
        piece(blk, (1, 1, 0))
    # Turned stretchers: sides, and the front one worn on top.
    def dowel(name, a, b, r, material=wood):
        n = 10
        pts = [Vector(a).lerp(Vector(b), k / n) for k in range(n + 1)]
        rad = [r * (0.85 + 0.15 * math.sin(math.pi * k / n)) for k in range(n + 1)]
        return piece(tube(name, pts, rad, material, 10), Vector(b) - Vector(a))
    for s in (-1, 1):
        dowel("Stretcher", (s * 0.192, -0.16, 0.17), (s * 0.188, 0.175, 0.17), 0.011)
    dowel("Stretcher", (-0.19, 0.0, 0.2), (0.19, 0.0, 0.2), 0.0105)
    dowel("FrontStretcher", (-0.197, -0.18, 0.13), (0.197, -0.18, 0.13), 0.011, worn)
    # The back: a curved top rail (worn on top), two curved slats.
    for (z, hgt, name, m) in ((0.8, 0.075, "TopRail", wood), (0.68, 0.05, "Slat", wood), (0.575, 0.05, "Slat", wood)):
        y = by + (z - sh) * (0.06 / (0.86 - sh))
        rails = []
        for k in range(13):
            x = -(sw - 0.05) / 2 + (sw - 0.05) * k / 12
            bow = 0.02 * (1 - (x / (sw / 2)) ** 2)
            rails.append([Vector((x, y + bow + dy, z + dz)) for dy, dz in ((-0.011, -hgt / 2), (0.011, -hgt / 2), (0.011, hgt / 2), (-0.011, hgt / 2))])
        # (Rings across, travelling along +X: reorder each ring counter-clockwise about +X.)
        rails = [[r[0], r[3], r[2], r[1]] for r in rails]
        piece(loft(name, rails, m, True, True, recalc=True), (1, 0, 0))
        if name == "TopRail":
            tw = rbox("RailWear", (sw - 0.12, 0.016, 0.003), (0, y + 0.02, z + hgt / 2 + 0.0008), worn, 0.001)
            piece(tw, (1, 0, 0))
    ob = join(grained, "WoodenChair")
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.00003)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(55):
            e.smooth = False
    bm.to_mesh(ob.data)
    bm.free()
    m = ob.modifiers.new("Weighted", "WEIGHTED_NORMAL")
    m.keep_sharp = True
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=m.name)
    return ob


V = (0.75, -1.0, 0.6)
run([
    ("Extinguisher", extinguisher, (0.6, -1.0, 0.45)),
    ("ExtinguisherCabinet", extinguisher_cabinet, V),
    ("ExtinguisherBracket", extinguisher_bracket, (1.0, -0.6, 0.3)),
    ("PullStation", pull_station, (0.5, -1.0, 0.3)),
    ("HornStrobe", horn_strobe, (0.5, -1.0, 0.3)),
    ("ThermostatBox", thermostat_box, (0.5, -1.0, 0.3)),
    ("ThermostatRound", thermostat_round, (0.5, -1.0, 0.3)),
    ("ReturnGrille", return_grille, (0.4, -1.0, 0.5)),
    ("DrinkingFountain", drinking_fountain, (0.7, -1.0, 0.7)),
    ("FilledWindow", lambda: filled_window(0), V),
    ("FilledWindowMullion", lambda: filled_window(1), V),
    ("FilledWindowCross", lambda: filled_window(2), V),
    ("CornerGuardR", lambda: corner_guard(False), (-0.8, -1.0, 0.4)),
    ("CornerGuardL", lambda: corner_guard(True), (0.8, -1.0, 0.4)),
    ("CloserBody", door_closer_body, (0.5, -1.0, 0.5)),
    ("CloserArm", door_closer_arm, (0.6, -0.5, 0.9)),
    ("CloserForearm", door_closer_forearm, (0.6, -0.5, 0.9)),
    ("CloserShoe", door_closer_shoe, (0.5, -1.0, 0.5)),
    ("KickPlate", kick_plate, (0.3, -1.0, 0.3)),
    ("DoorWedge", door_wedge, (1.0, -0.8, 0.6)),
    ("FloorBoxRound", lambda: floor_box(0), (0.4, -0.6, 1.0)),
    ("FloorBoxSquare", lambda: floor_box(1), (0.4, -0.6, 1.0)),
    ("FloorBoxOpen", lambda: floor_box(2), (0.4, -0.6, 1.0)),
    ("Bucket", bucket, (0.7, -1.0, 0.7)),
    ("Partition_120_135", lambda: partition(1.2, 1.35), V),
    ("Partition_120_165", lambda: partition(1.2, 1.65), V),
    ("Partition_90_135", lambda: partition(0.9, 1.35), V),
    ("Partition_90_165", lambda: partition(0.9, 1.65), V),
    ("WoodenChair", wooden_chair, (0.75, -1.0, 0.55)),
])
print("[fittings] done ->", OUT)
