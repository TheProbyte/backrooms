"""
The floral sofa (owner, round 15: a cream three-seater with rolled arms, three piped floral seat cushions and three loose
floral back cushions, tapered walnut legs; the cushions in the owner's tileable floral pattern). Built headless:

    blender -b --factory-startup --python Tools/Blender/build_floral_sofa.py -- <models_dir> --tex <textures_dir>
            --pattern <floral image> [--preview <dir>]

Writes Sofa_Floral.fbx (material slots Floral, Fabric, Wood), and into the textures folder FloralPattern.png (the
pattern as PNG), CreamWeave_Albedo.png and CreamWeave_Normal.png (a tileable woven cream upholstery, made here).
Units are metres; origin on the floor at the footprint centre; front -Y in Blender (+Z in Unity, as build_props.py).
Sized to the game's sofa collider proportions (MovableShapes: seat top ~0.52 of the height, arm top ~0.74).
"""
import bpy
import bmesh
import math
import os
import random
import sys

import numpy as np
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGS[0] if ARGS else os.getcwd())
def arg(name, default=None):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else default
TEX = os.path.abspath(arg("--tex", OUT))           # (absolute: a relative image path was saved nowhere)
PATTERN = arg("--pattern")
PREVIEW = arg("--preview")
# --style overflow: the new look's chunkier shapes (Docs/StyleSheet.md, "Chunkification"): rounder cushions, piping as
# thick as the other scripts' cords. The owner's floral pattern stays; run with --tex in a scratch folder so the
# textures are not rewritten.
OVERFLOW = arg("--style", "classic") == "overflow"


def R(r):
    """A cushion's corner radius: a quarter rounder in the new look."""
    return r * 1.25 if OVERFLOW else r
os.makedirs(OUT, exist_ok=True)
os.makedirs(TEX, exist_ok=True)
rng = random.Random(7)

L, D = 2.20, 0.92                 # length, depth
ARM_W = 0.17                      # arm width at the panel (the roll overhangs a little)
LEG_H = 0.14
BASE_Z0, DECK_Z = 0.13, 0.36      # the frame's underside, the top of the deck the seat cushions sit on
SEAT_T = 0.135                    # seat cushion thickness
FLORAL_TILE = (0.62, 0.62 * 928 / 1232)
WEAVE_TILE = 0.16


# ---------------------------------------------------------------- textures

def save_png(arr, path, srgb=True):
    """arr: (h, w, 3) floats 0..1, row 0 at the bottom."""
    h, w, _ = arr.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False, float_buffer=False)
    if not srgb:
        img.colorspace_settings.name = "Non-Color"
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[:, :, :3] = np.clip(arr, 0, 1)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def blur_wrap(a, r):
    for axis in (0, 1):
        acc = np.zeros_like(a)
        for k in range(-r, r + 1):
            acc += np.roll(a, k, axis=axis)
        a = acc / (2 * r + 1)
    return a


def make_weave(size=512, threads=96):
    """A tileable cream upholstery weave (a fine basket weave with slubs), albedo and OpenGL normal map."""
    r = np.random.default_rng(3)
    v, u = np.mgrid[0:size, 0:size].astype(np.float32) / size
    tu, tv = u * threads, v * threads
    iu, iv = np.floor(tu).astype(int) % threads, np.floor(tv).astype(int) % threads
    fu, fv = tu - np.floor(tu), tv - np.floor(tv)
    # Over-under: warp on top where (iu + iv) is even (2x2 basket: pairs of threads).
    over = ((iu // 2 + iv // 2) % 2 == 0)
    warp = np.sin(np.pi * fu) ** 0.6           # a warp thread's round profile across it
    weft = np.sin(np.pi * fv) ** 0.6
    h = np.where(over, 0.55 + 0.45 * warp, 0.55 + 0.45 * weft)
    # Thread-to-thread variation and slubs (thicker stretches of yarn).
    row_var = r.uniform(-1, 1, threads)[iv]
    col_var = r.uniform(-1, 1, threads)[iu]
    slub = blur_wrap(r.standard_normal((size, size)).astype(np.float32), 6)
    slub /= max(1e-6, np.abs(slub).max())
    fine = blur_wrap(r.standard_normal((size, size)).astype(np.float32), 1) * 0.08
    h = h + 0.08 * np.where(over, col_var, row_var) + 0.12 * slub + fine
    base = np.array([0.80, 0.75, 0.64], dtype=np.float32)      # cream (sRGB)
    shade = 0.84 + 0.2 * (h - h.mean())
    tint = np.where(over, 1.0, 0.975)[..., None] * np.array([1.0, 0.99, 0.97], dtype=np.float32)
    alb = base[None, None, :] * shade[..., None] * tint
    alb = alb * (1.0 + 0.04 * slub[..., None])
    # Normal map from the height (OpenGL: +V up).
    s = 2.2
    dhu = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
    dhv = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    n = np.stack([-dhu * s, -dhv * s, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    save_png(alb, os.path.join(TEX, "CreamWeave_Albedo.png"))
    save_png(n * 0.5 + 0.5, os.path.join(TEX, "CreamWeave_Normal.png"), srgb=False)


def make_pattern():
    src = bpy.data.images.load(PATTERN)
    w, h = src.size
    px = np.empty(w * h * 4, dtype=np.float32)
    src.pixels.foreach_get(px)
    return save_png(px.reshape(h, w, 4)[:, :, :3], os.path.join(TEX, "FloralPattern.png"))


# ---------------------------------------------------------------- materials (Blender preview only; Unity maps by slot name)

def material(name, color=None, image=None, normal=None, rough=0.85, sheen=0.0, tile=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = rough
    for key in ("Sheen Weight", "Sheen"):
        if key in bsdf.inputs:
            bsdf.inputs[key].default_value = sheen
            break
    if image:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(image, check_existing=True)
        nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    elif color:
        bsdf.inputs["Base Color"].default_value = (*color, 1)
    if normal:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(normal, check_existing=True)
        t.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = 0.8
        nt.links.new(t.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return m


# ---------------------------------------------------------------- geometry

def new_obj(name, bm, mat_name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(bpy.data.materials[mat_name])
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def grid_box(bm, half, cuts):
    """A box (centred at the origin) whose faces are grids: cuts = (nx, ny, nz) divisions."""
    nx, ny, nz = cuts
    verts = {}
    def v(i, j, k):
        key = (i, j, k)
        if key not in verts:
            verts[key] = bm.verts.new(((i / nx * 2 - 1) * half[0], (j / ny * 2 - 1) * half[1], (k / nz * 2 - 1) * half[2]))
        return verts[key]
    def face(a, b, c, d):
        bm.faces.new((a, b, c, d))
    for i in range(nx):
        for j in range(ny):
            face(v(i, j, nz), v(i + 1, j, nz), v(i + 1, j + 1, nz), v(i, j + 1, nz))      # top
            face(v(i, j + 1, 0), v(i + 1, j + 1, 0), v(i + 1, j, 0), v(i, j, 0))          # bottom
    for i in range(nx):
        for k in range(nz):
            face(v(i, 0, k), v(i + 1, 0, k), v(i + 1, 0, k + 1), v(i, 0, k + 1))         # front (-y)
            face(v(i, ny, k + 1), v(i + 1, ny, k + 1), v(i + 1, ny, k), v(i, ny, k))     # back
    for j in range(ny):
        for k in range(nz):
            face(v(0, j, k + 1), v(0, j + 1, k + 1), v(0, j + 1, k), v(0, j, k))         # left (-x)
            face(v(nx, j, k), v(nx, j + 1, k), v(nx, j + 1, k + 1), v(nx, j, k + 1))     # right
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)


def round_box(bm, half, radii):
    """Rounds every vertex of a box onto a box with elliptic corners of the given radii per axis."""
    inner = [max(1e-4, half[a] - radii[a]) for a in range(3)]
    for vt in bm.verts:
        p = vt.co
        q = Vector([max(-inner[a], min(inner[a], p[a])) for a in range(3)])
        d = Vector([(p[a] - q[a]) / radii[a] for a in range(3)])
        if d.length > 1e-6:
            d.normalize()
            vt.co = Vector([q[a] + d[a] * radii[a] for a in range(3)])


def uv_project(bm, tile, offset=(0.0, 0.0), up_axis=2):
    """Box projection in the cushion's own frame: each face gets the plane its normal faces most (the pattern upright on
    the sides, along the length on top)."""
    uv = bm.loops.layers.uv.verify()
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda a: abs(n[a]))
        for lp in f.loops:
            c = lp.vert.co
            if ax == 2:     # top/bottom: x along, y across
                u, w = c.x, -c.y
            elif ax == 1:   # front/back: x along, z up
                u, w = c.x * (1 if n.y < 0 else -1), c.z
            else:           # ends: y along, z up
                u, w = -c.y * (1 if n.x > 0 else -1), c.z
            lp[uv].uv = (u / tile[0] + offset[0], w / tile[1] + offset[1])


def cushion(name, size, loc, mat_name, radius, crown=0.0, pinch=0.0, tilt=0.0, pivot=None, tile=FLORAL_TILE, cuts=None):
    """A soft cushion: a rounded box, crowned on top (seat) or pinched to its edges like a pillow (back), with a
    little unevenness. size = (x, y, z) full extents."""
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    bm = bmesh.new()
    cuts = cuts or (max(8, int(size[0] / 0.04)), max(6, int(size[1] / 0.04)), max(4, int(size[2] / 0.03)))
    grid_box(bm, (hx, hy, hz), cuts)
    round_box(bm, (hx, hy, hz), (radius, radius, radius))
    seed = rng.random() * 100
    for vt in bm.verts:
        nx, ny, nz = vt.co.x / hx, vt.co.y / hy, vt.co.z / hz
        if crown:
            # Seat: the top domes up, the middle most; a little dip where it is sat in.
            dome = max(0.0, 1 - nx * nx) * max(0.0, 1 - ny * ny)
            if vt.co.z > 0:
                vt.co.z += crown * dome * min(1.0, vt.co.z / hz * 1.5)
        if pinch:
            # Pillow: thick in the middle, pinched to a seam round its edge.
            t = max(0.0, 1 - abs(nx) ** 3) * max(0.0, 1 - abs(nz) ** 3)
            vt.co.y *= (1 - pinch) + pinch * t ** 0.5
        # Unevenness: soft, a few millimetres.
        w = 0.004 * math.sin(nx * 3.1 + seed) * math.cos(ny * 2.3 + seed * 0.7) + 0.003 * math.sin(nz * 4.0 + nx * 2.0 + seed)
        vt.co.z += w if vt.co.z > 0 else 0.0
        vt.co.y += w * 0.6 if vt.co.y < 0 else 0.0
    uv_project(bm, tile, (rng.random(), rng.random()))
    o = new_obj(name, bm, mat_name)
    if tilt:
        o.rotation_euler = (math.radians(tilt), 0, 0)
    o.location = loc
    return o, (hx, hy, hz)


def tube(name, points, radius, mat_name, closed=True, verts=8):
    """Piping: a thin round cord along a polyline."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = max(radius * 1.15, min(0.005, radius * 1.6)) if OVERFLOW else radius
    cu.bevel_resolution = max(1, verts // 4)
    sp = cu.splines.new("POLY")
    sp.points.add(len(points) - 1)
    for i, p in enumerate(points):
        sp.points[i].co = (p[0], p[1], p[2], 1)
    sp.use_cyclic_u = closed
    o = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(bpy.data.materials[mat_name])
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.ops.object.convert(target="MESH")
    o = bpy.context.active_object
    o.name = name
    # Cord UVs: a few small repeats so the fabric texture reads as fabric on it.
    bm = bmesh.new(); bm.from_mesh(o.data)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for lp in f.loops:
            c = lp.vert.co
            lp[uv].uv = ((c.x + c.y) / WEAVE_TILE, c.z / WEAVE_TILE)
    bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def rounded_rect(hx, hy, r, z, plane="xy", n=6):
    """Points round a rectangle with rounded corners (half extents hx, hy, corner radius r), in plane xy at height z,
    or xz at depth z."""
    pts = []
    for cx, cy, a0 in ((hx - r, hy - r, 0), (-(hx - r), hy - r, 90), (-(hx - r), -(hy - r), 180), (hx - r, -(hy - r), 270)):
        for k in range(n + 1):
            a = math.radians(a0 + 90 * k / n)
            x, y = cx + r * math.cos(a), cy + r * math.sin(a)
            pts.append((x, y, z) if plane == "xy" else (x, z, y))
    return pts


def place(o, mw):
    o.matrix_world = mw @ o.matrix_world
    return o


def leg(name, x, y):
    """A tapered turned walnut leg with a small collar at the top."""
    bm = bmesh.new()
    seg = 14
    rings = [(LEG_H + 0.005, 0.03), (LEG_H - 0.012, 0.03), (LEG_H - 0.016, 0.026), (0.02, 0.019), (0.006, 0.018), (0.0, 0.016)]
    loops = []
    for z, r in rings:
        loops.append([bm.verts.new((x + r * math.cos(2 * math.pi * i / seg), y + r * math.sin(2 * math.pi * i / seg), z)) for i in range(seg)])
    for a, b in zip(loops, loops[1:]):
        for i in range(seg):
            bm.faces.new((a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]))
    bm.faces.new(list(reversed(loops[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for lp in f.loops:
            c = lp.vert.co
            lp[uv].uv = (math.atan2(c.y - y, c.x - x) / (2 * math.pi) * 0.3, c.z * 2.0)
    return new_obj(name, bm, "Wood")


def arm(name, side):
    """A low rolled arm: an upholstered panel from the base up, its top rolled over outward, piped round the front."""
    parts = []
    xo = side * (L / 2 - ARM_W / 2)
    top = 0.565
    body, _ = cushion(name + "_panel", (ARM_W - 0.01, D - 0.01, top - 0.02 - BASE_Z0), (0, 0, 0), "Fabric", R(0.035),
                      tile=(WEAVE_TILE, WEAVE_TILE))
    body.location = (xo, 0.0, (top - 0.02 + BASE_Z0) / 2)
    parts.append(body)
    # The roll: an elliptic tube along the arm, overhanging outward, its front end rounded off.
    rr = 0.088
    bm = bmesh.new()
    seg, rings = 20, 26
    ring_loops = []
    for j in range(rings + 1):
        t = j / rings
        y = -D / 2 - 0.006 + t * (D + 0.004)
        # Rounded front end: the section shrinks over the first 3 cm.
        front = min(1.0, (y + D / 2 + 0.006) / 0.03)
        s = math.sqrt(max(0.0, 1 - (1 - front) ** 2)) * 0.9 + 0.1 if front < 1 else 1.0
        loop = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            loop.append(bm.verts.new((xo + side * 0.012 + rr * 1.05 * s * math.cos(a), y, top + rr * 0.92 * s * math.sin(a) - 0.035)))
        ring_loops.append(loop)
    for a, b in zip(ring_loops, ring_loops[1:]):
        for i in range(seg):
            bm.faces.new((a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]))
    bm.faces.new(ring_loops[0])
    bm.faces.new(list(reversed(ring_loops[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    uv_project_world = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for lp in f.loops:
            c = lp.vert.co
            lp[uv_project_world].uv = (c.y / WEAVE_TILE, (math.atan2(c.z - top, c.x - xo) * rr) / WEAVE_TILE)
    parts.append(new_obj(name + "_roll", bm, "Fabric"))
    # Piping round the front of the roll and down the front of the panel.
    pts = []
    yf = -D / 2 - 0.006 + 0.03 + 0.004          # where the roll reaches its full section (the welt round its front)
    for k in range(25):
        a = math.radians(-100 + 290 * k / 24)          # from low on the inside, over the top, down the outside
        pts.append((xo + side * 0.012 + side * (rr * 1.05 + 0.002) * math.cos(a), yf,
                    top - 0.035 + (rr * 0.92 + 0.002) * math.sin(a)))
    parts.append(tube(name + "_piping", pts, 0.0055, "Fabric", closed=False))
    return parts


def build():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    make_weave()
    pat = make_pattern() if PATTERN else None
    material("Floral", image=os.path.join(TEX, "FloralPattern.png") if pat else None, color=(0.8, 0.6, 0.5), rough=0.8, sheen=0.35)
    material("Fabric", image=os.path.join(TEX, "CreamWeave_Albedo.png"), normal=os.path.join(TEX, "CreamWeave_Normal.png"), rough=0.9, sheen=0.4)
    material("Wood", color=(0.17, 0.085, 0.045), rough=0.42)

    parts = []
    inner = L / 2 - ARM_W + 0.005           # inside face of the arms
    # The base (front rail, upholstered), the deck under the cushions, the back frame.
    base, _ = cushion("base", (2 * inner + 0.02, D - 0.02, DECK_Z - BASE_Z0), (0, 0, 0), "Fabric", R(0.03), tile=(WEAVE_TILE, WEAVE_TILE))
    base.location = (0, 0.0, (DECK_Z + BASE_Z0) / 2)
    parts.append(base)
    back_y0 = D / 2 - 0.19
    frame, _ = cushion("backframe", (L - 0.06, D / 2 - back_y0 + 0.0, 0.74 - 0.30), (0, 0, 0), "Fabric", R(0.05), tile=(WEAVE_TILE, WEAVE_TILE))
    frame.location = (0, (back_y0 + D / 2) / 2 - 0.005, (0.74 + 0.30) / 2)
    parts.append(frame)
    # Piping along the front rail's top edge.
    parts.append(tube("rail_piping", [(-inner, -D / 2 + 0.011, DECK_Z - 0.008), (inner, -D / 2 + 0.011, DECK_Z - 0.008)], 0.0055, "Fabric", closed=False))
    for s in (-1, 1):
        parts += arm("arm%d" % s, s)

    # Three seat cushions and three back cushions, piped.
    n = 3
    cw = (2 * inner) / n
    seat_d = back_y0 - (-D / 2 - 0.012) - 0.01
    for i in range(n):
        x = -inner + cw * (i + 0.5)
        w = cw - 0.012
        zc = DECK_Z + SEAT_T / 2 - 0.004
        yc = -D / 2 - 0.012 + seat_d / 2
        c, (hx, hy, hz) = cushion("seat%d" % i, (w, seat_d, SEAT_T), (0, 0, 0), "Floral", R(0.04), crown=0.016)
        c.location = (x + rng.uniform(-0.004, 0.004), yc + rng.uniform(-0.006, 0.006), zc)
        c.rotation_euler = (0, 0, math.radians(rng.uniform(-0.6, 0.6)))
        parts.append(c)
        r = R(0.04)
        for zz in (hz - r + r * 0.72, -(hz - r + r * 0.72)):
            p = tube("seatpipe%d" % i, rounded_rect(hx - r + r * 0.72 + 0.003, hy - r + r * 0.72 + 0.003, r * 0.72 + 0.006, zz), 0.0045, "Floral")
            p.location = c.location; p.rotation_euler = c.rotation_euler
            parts.append(p)
        # Back cushion: a pillow leaning back on the frame, its bottom on the seat cushion's back.
        bh, bt = 0.47, 0.22
        b, (bhx, bhy, bhz) = cushion("back%d" % i, (w - 0.004, bt, bh), (0, 0, 0), "Floral", R(0.07), pinch=0.45)
        tilt = -rng.uniform(11.0, 14.0)
        b.rotation_euler = (math.radians(tilt), 0, math.radians(rng.uniform(-0.8, 0.8)))
        bz = DECK_Z + SEAT_T + bh / 2 - 0.03
        b.location = (x + rng.uniform(-0.004, 0.004), back_y0 - bt / 2 + 0.035 + rng.uniform(-0.006, 0.006), bz)
        parts.append(b)
        p = tube("backpipe%d" % i, rounded_rect(bhx - 0.004, bhz - 0.004, R(0.07), 0.0, plane="xz"), 0.0045, "Floral")
        p.location = b.location; p.rotation_euler = b.rotation_euler
        parts.append(p)
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(leg("leg", sx * (L / 2 - 0.075), sy * (D / 2 - 0.075)))
    return parts


def finish(name, parts):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    # (Location too: the joined object keeps the first part's origin, the base's middle, 25 cm up.)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    # Material slots in a fixed order: Floral, Fabric, Wood.
    tri = o.modifiers.new("tri", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier="tri")
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False,
                             object_types={"MESH"})
    dims = o.dimensions
    print("[sofa] exported", path, "tris:", len(o.data.polygons), "size: %.2f x %.2f x %.2f" % (dims.x, dims.y, dims.z),
          "slots:", [m.name for m in o.data.materials])
    return o


def preview(o, folder):
    os.makedirs(folder, exist_ok=True)
    scene = bpy.context.scene
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", "CYCLES"):
        try:
            scene.render.engine = eng
            break
        except TypeError:
            continue
    if scene.render.engine == "CYCLES":
        scene.cycles.samples = 48
    scene.render.resolution_x, scene.render.resolution_y = 1100, 760
    scene.view_settings.view_transform = "AgX" if "AgX" in [i.identifier for i in bpy.types.ColorManagedViewSettings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
    # A room corner: floor and wall, warm sun from the left like the reference, soft fill.
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
    floor = bpy.context.active_object
    fm = material("PreviewFloor", color=(0.33, 0.22, 0.14), rough=0.6)
    floor.data.materials.append(fm)
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, D / 2 + 0.25, 3), rotation=(math.pi / 2, 0, 0))
    wall = bpy.context.active_object
    wall.data.materials.append(material("PreviewWall", color=(0.45, 0.44, 0.36), rough=0.9))
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.0
    sun.data.color = (1.0, 0.82, 0.6)
    sun.data.angle = math.radians(3)
    sun.rotation_euler = (math.radians(62), math.radians(-8), math.radians(-60))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("w"); scene.world = world
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.35, 0.33, 0.3, 1); bg.inputs["Strength"].default_value = 0.5
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam); scene.camera = cam
    cam.data.lens = 40
    for k, (pos, look) in enumerate((((0.0, -3.3, 1.05), (0, 0, 0.45)), ((-2.3, -2.4, 1.3), (0, 0, 0.42)), ((1.2, -1.15, 0.85), (0.75, -0.1, 0.48)))):
        cam.location = pos
        cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, "floral_sofa_%d.png" % k)
        bpy.ops.render.render(write_still=True)
    print("[sofa] previews in", folder, "engine", scene.render.engine)


parts = build()
o = finish("Sofa_Floral", parts)
if PREVIEW:
    preview(o, PREVIEW)
