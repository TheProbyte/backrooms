"""
A can of spray paint (2026-11-04, the owner: "improve the spray paint can 10x"), built in Blender and baked (bake_kit.py).
Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_spraycan.py -- Assets/Backrooms/Resources/Items [--preview <dir>]

A 400 ml (12 oz) aerosol paint can as they are made: a three-piece tinplate can 65 mm across, its body a lithographed
wrap with a welded side seam, the bottom end double-seamed on with its chime and its concave dome, the top end (the
cone) double-seamed on with embossed rings, the one-inch valve mounting cup crimped into its curl, the stem, and the
actuator pressed on it (a dished finger pad with an arrow toward the nozzle, a black orifice insert in its front). Used:
dried overspray flecked over the cone and the actuator and a run of it down from the nozzle. The wrap (an invented maker,
FERRON): GLOSS RED and a swatch, SPRAY PAINT, ALL-SURFACE ENAMEL, a flame pictogram and the warnings, the directions,
the net weight and a barcode, rendered with real fonts (build_vending.py's Art).

  - SprayCan: the can (slots CanMetal, CanPrint, CanCap, CanNozzle, CanPaint: the game reads finish and gloss from them).

Frame: origin on the floor at the middle of its foot, up +Z, the nozzle toward -Y (Unity +Z). The brand faces +Y: the
side the one spraying sees. Unity (x, y, z) = (-x, z, -y). Height to the actuator's top: 0.2075 m.
"""
import os
import sys
import math
import random

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_vending.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- the machine")
exec(compile(_src[:_cut], "build_vending_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix

R = 0.0325                     # the body's radius (65 mm)
Z0, Z1 = 0.0078, 0.1640        # the body between its seams
TOP = 0.2075                   # the actuator's top
NOZZLE_Z = 0.2012
RED = (0.62, 0.035, 0.03)

# The top end, from the seam up to the valve cup's curl (r, z): the cone, its two embossed rings.
CONE = [(0.0325, 0.1640), (0.0334, 0.1648), (0.0338, 0.1666), (0.0335, 0.1686), (0.0322, 0.1695), (0.0310, 0.1690),
        (0.0303, 0.1696), (0.0292, 0.1726), (0.0280, 0.1752), (0.0276, 0.1762), (0.0279, 0.1766), (0.0270, 0.1780),
        (0.0250, 0.1812), (0.0233, 0.1834), (0.0229, 0.1842), (0.0232, 0.1846), (0.0218, 0.1860), (0.0186, 0.1886),
        (0.0160, 0.1903), (0.0149, 0.1911)]
CUP = [(0.0149, 0.1911), (0.0144, 0.1926), (0.0136, 0.1937), (0.0126, 0.1938), (0.0119, 0.1928), (0.0117, 0.1910),
       (0.0112, 0.1898), (0.0099, 0.1897), (0.0093, 0.1910), (0.0086, 0.1919), (0.0074, 0.1916), (0.0062, 0.1907),
       (0.0048, 0.1912), (0.0043, 0.1934), (0.0023, 0.1937), (0.0022, 0.1952), (0.0, 0.1952)]
BOTTOM = [(0.0, 0.0115), (0.012, 0.0108), (0.022, 0.0072), (0.027, 0.0040), (0.0295, 0.0022), (0.0310, 0.0006),
          (0.0328, 0.0), (0.0336, 0.0012), (0.0339, 0.0036), (0.0336, 0.0061), (0.0328, 0.0073), (0.0325, Z0)]


def cone_at(r):
    """The cone's height and its outward normal (r, z) at a radius (for things lying on it)."""
    pts = CONE[6:]
    for (r0, z0), (r1, z1) in zip(pts, pts[1:]):
        if r1 <= r <= r0:
            t = (r0 - r) / max(1e-9, r0 - r1)
            z = z0 + (z1 - z0) * t
            dr, dz = r1 - r0, z1 - z0
            n = Vector((dz, -dr)).normalized()
            if n.y < 0:
                n = -n
            return z, n
    return CONE[-1][1], Vector((0.3, 1.0)).normalized()


# ------------------------------------------------------------------------------------------------- the wrap

W_ART, H_ART = 2 * math.pi * R, Z1 - Z0


def wrap_art(A, W, H):
    """The lithographed wrap, flat: x = 0 is the brand's middle (+Y, the back as you spray), the ends meet at the
    side seam under the nozzle (-Y)."""
    red_d = A.grad((0.36, 0.012, 0.01), (0.62, 0.03, 0.025))
    A.rect(-W / 2, -H / 2, W / 2, H / 2, red_d)
    black = A.emit((0.02, 0.02, 0.022))
    white = A.emit((0.95, 0.94, 0.9))
    cream = A.emit((0.93, 0.91, 0.84))
    yellow = A.emit((0.98, 0.78, 0.08))
    tin = A.emit((0.55, 0.55, 0.56))
    # bare tinplate where the print stops at the seams
    A.rect(-W / 2, -H / 2, W / 2, -H / 2 + 0.0022, tin)
    A.rect(-W / 2, H / 2 - 0.0022, W / 2, H / 2, tin)
    # the brand's slanted black band, a yellow speed line under it
    band = [(-0.068, -0.03), (0.06, -0.03), (0.068, 0.036), (-0.06, 0.036)]
    me = bpy.data.meshes.new("Band")
    z = A._z(None)
    me.from_pydata([(x, y, z) for x, y in band], [], [(0, 1, 2, 3)])
    A.obj("Band", me, [black])
    A.strip([(-0.07, -0.036), (-0.02, -0.039), (0.03, -0.037), (0.075, -0.031)], [0.0032, 0.0036, 0.0032, 0.002], yellow)
    A.text("FERRON", 0.028, 0.0, 0.006, white, "impact.ttf", shear=0.18, space=1.05)
    A.text("ALL-SURFACE ENAMEL", 0.0062, 0.0, -0.0185, cream, "arialbd.ttf", space=1.1)
    A.text("SPRAY PAINT", 0.0115, 0.0, 0.0515, white, "arialbd.ttf", space=1.05)
    A.text("INDOOR / OUTDOOR  ·  METAL  WOOD  PLASTIC", 0.0034, 0.0, 0.0425, cream, "arial.ttf")
    # the colour: a white plate with its name and a swatch
    A.rect(-0.034, -0.072, 0.034, -0.049, white)
    A.disc(-0.024, -0.0605, 0.0075, A.emit(RED))
    A.text("GLOSS RED", 0.0085, 0.006, -0.058, A.emit((0.6, 0.03, 0.025)), "arialbd.ttf")
    A.text("COLOR 7765", 0.0034, 0.006, -0.0665, black, "arial.ttf")
    # the warnings (toward the nozzle on the right): a white panel, a flame pictogram, the words
    A.rect(0.071, -0.07, W / 2 - 0.003, 0.07, white)
    cx, cy, s = 0.0855, 0.05, 0.0105
    dia = [(cx, cy + s), (cx + s, cy), (cx, cy - s), (cx - s, cy)]
    me = bpy.data.meshes.new("Dia")
    z = A._z(None)
    me.from_pydata([(x, y, z) for x, y in dia], [], [(0, 1, 2, 3)])
    A.obj("Dia", me, [A.emit((0.85, 0.05, 0.04))])
    s2 = s * 0.8
    me = bpy.data.meshes.new("Dia2")
    z = A._z(None)
    me.from_pydata([(cx, cy + s2, z), (cx + s2, cy, z), (cx, cy - s2, z), (cx - s2, cy, z)], [], [(0, 1, 2, 3)])
    A.obj("Dia2", me, [white])
    A.disc(cx, cy - 0.0025, 0.0028, black)
    A.strip([(cx - 0.001, cy - 0.002), (cx + 0.0005, cy + 0.0015), (cx - 0.0003, cy + 0.0048)], [0.004, 0.0028, 0.0006], black)
    A.rect(cx - 0.0045, cy - 0.0062, cx + 0.0045, cy - 0.0052, black)
    A.text("DANGER", 0.0072, 0.0855, 0.031, A.emit((0.8, 0.04, 0.03)), "arialbd.ttf")
    A.text("EXTREMELY FLAMMABLE\nCONTENTS UNDER\nPRESSURE", 0.0029, 0.0855, 0.019, black, "arialbd.ttf")
    small = ("Keep away from heat, sparks\nand open flame. Do not\npuncture or incinerate.\nDo not store above 120 F.\n"
             "Use only with adequate\nventilation. Avoid breathing\nvapor or spray mist.\nIf swallowed, call a doctor.\n"
             "KEEP OUT OF REACH\nOF CHILDREN.")
    A.text(small, 0.0021, 0.0855, -0.024, A.emit((0.15, 0.15, 0.16)), "arial.ttf")
    # the directions (left of the brand, toward the seam): a cream panel
    A.rect(-W / 2 + 0.003, -0.07, -0.071, 0.07, cream)
    A.text("DIRECTIONS", 0.0052, -0.0855, 0.058, black, "arialbd.ttf")
    dirs = ("1. Shake can for one minute\n   after the ball rattles.\n2. Hold can upright\n   10 - 16 in. from surface.\n"
            "3. Spray in a steady back\n   and forth motion, slightly\n   overlapping each stroke.\n4. Light coats dry faster\n"
            "   and do not run.\n5. Clean the valve: turn\n   can upside down and\n   spray until clear.")
    A.text(dirs, 0.0021, -0.0855, 0.02, A.emit((0.12, 0.12, 0.13)), "arial.ttf")
    A.text("NET WT 12 OZ (340 g)", 0.0035, -0.0855, -0.034, black, "arialbd.ttf")
    # a barcode and its digits
    rng = random.Random(7765)
    x = -0.099
    while x < -0.072:
        w = rng.choice((0.00025, 0.0005, 0.00075, 0.001))
        if rng.random() < 0.55:
            A.rect(x, -0.062, x + w, -0.045, black)
        x += w + rng.choice((0.00025, 0.0005))
    A.text("0 48925 77650 3", 0.0026, -0.0855, -0.0655, black, "arial.ttf")
    A.text("FERRON COATINGS CO.  LOT 4417B", 0.0019, -0.0855, -0.0705 + 0.0035, A.emit((0.3, 0.3, 0.3)), "arial.ttf")


# ------------------------------------------------------------------------------------------------- the can

def body_mesh(material, segs=96, rows=40):
    """The body: a cylinder with its wrap's UVs (u round from the side seam, v up), a welded seam's ridge and a dent."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rng = random.Random(3)
    dent_a, dent_z = math.radians(205), 0.074
    grid = []
    where = {}
    for j in range(rows + 1):
        z = Z0 + (Z1 - Z0) * j / rows
        row = []
        for i in range(segs + 1):
            a = -math.pi / 2 + 2 * math.pi * i / segs        # from -Y (the seam) round
            r = R
            # the side seam: a narrow weld bead
            da = (i % segs) / segs
            if min(da, 1 - da) * 2 * math.pi * R < 0.0008:
                r += 0.00018
            # a soft dent where it was knocked
            ang = (a - dent_a + math.pi) % (2 * math.pi) - math.pi
            r -= 0.0006 * math.exp(-((ang * R) ** 2 + (z - dent_z) ** 2) / (2 * 0.008 ** 2))
            v = bm.verts.new((r * math.cos(a), r * math.sin(a), z))
            where[v] = (i, j)
            row.append(v)
        grid.append(row)
    for j in range(rows):
        for i in range(segs):
            f = bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
            f.normal_update()
            c = f.calc_center_median()
            if f.normal.dot(Vector((c.x, c.y, 0))) < 0:
                f.normal_flip()
            for l in f.loops:
                ii, jj = where[l.vert]
                # u: 0.5 at +Y (the brand's middle); the wrap's x = 0 there
                l[uv].uv = (ii / segs, jj / rows)
            f.smooth = True
    me = bpy.data.meshes.new("Body")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    ob = bpy.data.objects.new("Body", me)
    bpy.context.collection.objects.link(ob)
    return ob


def actuator(cap, nozzle):
    """The actuator: a cylinder with a dished finger pad, rounded edges, an arrow toward the nozzle, the black insert."""
    p = []
    prof = [(0.0, 0.1950), (0.0080, 0.1950), (0.0086, 0.1958), (0.0087, 0.2052), (0.0084, 0.2068), (0.0076, 0.2075),
            (0.0062, 0.2072), (0.0035, 0.2066), (0.0, 0.2065)]
    p.append(lathe("Actuator", prof, cap, 40))
    # the arrow on the pad, pointing at the nozzle (-Y), raised 0.3 mm
    bm = bmesh.new()
    pts = [(0.0, -0.0062), (0.0028, -0.0030), (0.0010, -0.0030), (0.0010, 0.0028), (-0.0010, 0.0028), (-0.0010, -0.0030), (-0.0028, -0.0030)]
    zt = 0.2067
    top = [bm.verts.new((x, y, zt + 0.0003)) for x, y in pts]
    bot = [bm.verts.new((x, y, zt - 0.0004)) for x, y in pts]
    bm.faces.new(top)
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    p.append(new_obj("Arrow", bm, [cap]))
    # the orifice insert in its front: a black disc proud of the face, its hole a darker dot
    ins = lathe("Insert", [(0.0, 0.0), (0.0024, 0.0), (0.0024, 0.0006), (0.0012, 0.0007), (0.0006, 0.0003), (0.0, 0.0003)], nozzle, 20,
                xf=M4(rot('X', 90), (0.0, -0.0085, NOZZLE_Z)))
    p.append(ins)
    return p


def overspray(paint, rng):
    """Dried paint: flecks round the actuator and on the cone in front of it, and a run down the cone from the nozzle."""
    p = []
    for k in range(26):
        a = -math.pi / 2 + rng.gauss(0, 0.55)
        r = rng.uniform(0.0125, 0.03)
        z, n = cone_at(r)
        pos = Vector((r * math.cos(a), r * math.sin(a), z))
        nrm = Vector((n.x * math.cos(a), n.x * math.sin(a), n.y)).normalized()
        s = rng.uniform(0.0003, 0.0009)
        p.append(sphere("Fleck", tuple(pos + nrm * 0.00002), (s, s, s * 0.12), paint, 8, 4, rot=rot_to(nrm)))
    for k in range(10):
        a = -math.pi / 2 + rng.gauss(0, 0.4)
        z = rng.uniform(0.197, 0.206)
        pos = Vector((0.0087 * math.cos(a), 0.0087 * math.sin(a), z))
        nrm = Vector((math.cos(a), math.sin(a), 0.0))
        s = rng.uniform(0.0002, 0.0006)
        p.append(sphere("Fleck", tuple(pos), (s, s, s * 0.15), paint, 8, 4, rot=rot_to(nrm)))
    # the run: from under the nozzle down the actuator's front, over the cup's curl and down the cone a way
    run = []
    for t in [i / 14 for i in range(15)]:
        if t < 0.25:
            r, z = 0.0089, NOZZLE_Z - 0.003 - t / 0.25 * 0.0045
        elif t < 0.4:
            r, z = 0.0089 + (t - 0.25) / 0.15 * 0.005, 0.1937 + 0.0002
        else:
            r = 0.0139 + (t - 0.4) / 0.6 * 0.011
            z, _ = cone_at(r)
            z += 0.0004
        run.append((0.0004 * math.sin(t * 7.0), -r, z))
    p.append(tube("Run", run, [0.00055 - 0.00025 * i / 14 for i in range(15)], paint, 8))
    p.append(sphere("Bead", run[-1], (0.0008, 0.0008, 0.0004), paint, 10, 6))
    return p


def spray_can():
    metal = S("CanMetal", (0.74, 0.74, 0.76), "metal", 0.3, 1.0)
    cap = S("CanCap", (0.62, 0.04, 0.035), "plastic", 0.45)            # the actuator in the paint's colour
    nozzle = S("CanNozzle", (0.03, 0.03, 0.032), "plastic", 0.5)
    paint = S("CanPaint", RED, "paint", 0.25)
    img = art(_tmp("can_wrap"), W_ART, H_ART, 2048, wrap_art, samples=24)
    PRINT = S("CanPrint", (1, 1, 1), "print", 0.25)
    PRINT["print"] = img.name
    rng = random.Random(11)
    plain = [lathe("Bottom", BOTTOM, metal, 96, cap_bottom=False, cap_top=False),
             lathe("Cone", CONE, metal, 96, cap_bottom=False, cap_top=False),
             lathe("Cup", CUP, metal, 72, cap_bottom=False, cap_top=False)]
    plain += actuator(cap, nozzle)
    plain += overspray(paint, rng)
    base = finish(plain, "SprayCan", sharp_deg=60)
    body = body_mesh(PRINT)
    return finish_mixed([base], [body], "SprayCan")


models = [("SprayCan", spray_can, (0.0, 1.0, 0.25), {"res": 2048, "scale": 0.08, "wear": 0.5, "dirt": 0.45})]
run_baked(models)
print("[spraycan] done ->", OUT)
