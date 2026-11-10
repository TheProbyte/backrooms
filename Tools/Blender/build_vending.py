"""
A drinks vending machine (2026-11-04, remade after the owner's photo the same day), built in Blender and baked
(bake_kit.py). Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_vending.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>]

The 1990s full-front can vendor of the owner's photo (a Dixie-Narco 501E's class: 72 x 37 x 33 in, 1.83 x 0.94 x 0.84 m,
nine selections, a coin changer): a black steel cabinet with a top cap, a black door with a tall backlit sign behind a
polycarbonate face in a black bezel on its left, and on its right a black panel of controls: a lit picture of a can
on ice, a service sticker, the coin entry with its acceptor window and the DEPOSIT NICKELS, DIMES OR QUARTERS label,
the price window, a second lit picture, the T-handle lock, five lit picture buttons in two columns, the coin return
and its chrome cup; under them a black band with the delivery port and its flap, the maker's badge, a red kick panel,
the black plinth.

  - VendingMachine: everything that does not glow.
  - VendingGlow: the sign, the two pictures, the price and the five buttons (lit by the game when it has power), with
    VendingGlow_Emission.png: what shines, brighter over the sign's two fluorescent tubes and dimmer at their ends.
  - VendingPlug: its moulded 15 A plug.

No real brand (an invented one, "Polar"); the artwork is rendered in Blender from type and small scenes of cans on ice.
Frame: origin on the floor at the middle of its foot, front toward -Y (Unity +Z), back against the wall at +Y.
Unity (x, y, z) = (-x, z, -y): Blender +X is the viewer's right seen from the front, as in the photo.
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

W_, H_, D_ = 0.94, 1.83, 0.84
FRONT = -D_ / 2                    # the door's face
C = FRONT + 0.003                  # the control panel's face (3 mm back)
# The sign's window, the controls' panel (from the photo at about 1.25 mm a pixel).
SX0, SX1, SZ0, SZ1 = -0.4075, 0.1175, 0.6125, 1.725
PX0, PX1, PZ0, PZ1 = 0.135, 0.445, 0.58, 1.765
PIC1 = (0.156, 0.35, 1.47, 1.6275)
PIC2 = (0.149, 0.34, 1.119, 1.265)
PRICE = (0.344, 0.382, 1.329, 1.375)
TILE_W, TILE_H = 0.0835, 0.059
TILES = [(0.149, 1.035), (0.2465, 1.035), (0.149, 0.951), (0.2465, 0.951), (0.149, 0.869)]   # (x0, z0) of each
PORT = (-0.17, 0.1275, 0.36, 0.475)
FONTS = "C:/Windows/Fonts/"
RED = (0.6, 0.018, 0.024)


def card_uv(name, w, h, center, material, rect, normal=(0, -1, 0)):
    """card() with its UVs over a part of the print: rect = (u0, v0, u1, v1)."""
    ob = card(name, w, h, center, material, normal)
    u0, v0, u1, v1 = rect
    for l in ob.data.uv_layers[0].data:
        l.uv = (u0 + (u1 - u0) * l.uv[0], v0 + (v1 - v0) * l.uv[1])
    return ob


def frame(p, name, x0, x1, z0, z1, t, y0, y1, m, bev=0.002):
    """Four bars round the opening x0..x1 by z0..z1, t wide, from y0 (front) back to y1."""
    d, yc = y1 - y0, (y0 + y1) / 2
    p.append(rbox(name, (x1 - x0 + 2 * t, d, t), ((x0 + x1) / 2, yc, z1 + t / 2), m, bev, 2))
    p.append(rbox(name, (x1 - x0 + 2 * t, d, t), ((x0 + x1) / 2, yc, z0 - t / 2), m, bev, 2))
    p.append(rbox(name, (t, d, z1 - z0), (x0 - t / 2, yc, (z0 + z1) / 2), m, bev, 2))
    p.append(rbox(name, (t, d, z1 - z0), (x1 + t / 2, yc, (z0 + z1) / 2), m, bev, 2))


def box(p, name, x0, x1, z0, z1, y0, y1, m, bev=0.003, segs=2):
    p.append(rbox(name, (x1 - x0, y1 - y0, z1 - z0), ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), m, bev, segs))


def img_mat(name, img, rough=0.45):
    """A slot printed with a rendered image (its finish from the name's prefix, as the game reads it)."""
    finish = {"Chrome": "metal", "Alu": "metal", "Print": "paper", "Plastic": "plastic", "Glow": "plastic",
              "Paint": "paint", "Paper": "paper"}.get(name.split("_")[0], "plastic")
    m = S(name, (1, 1, 1), finish, rough, 1.0 if finish == "metal" else 0.0)
    m["print"] = img.name
    return m


# ------------------------------------------------------------------------------------------------- artwork

class Art:
    """A little emissive scene laid out flat (x right, y up, metres) and rendered straight down, orthographic: how the
    machine's printed and backlit artwork is made. Later things lie on top."""

    def __init__(self, col):
        self.col, self.z = col, 0.0

    def _z(self, z):
        if z is not None:
            return z
        self.z += 0.0004
        return self.z

    def emit(self, rgb, s=1.0):
        m = bpy.data.materials.new("ArtE")
        m.use_nodes = True
        nt = m.node_tree
        for n in list(nt.nodes):
            if n.type != "OUTPUT_MATERIAL":
                nt.nodes.remove(n)
        e = nt.nodes.new("ShaderNodeEmission")
        e.inputs["Color"].default_value = (*rgb, 1)
        e.inputs["Strength"].default_value = s
        nt.links.new(e.outputs[0], next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL").inputs["Surface"])
        return m

    def grad(self, c0, c1, axis="Y"):
        """An emission ramp over the object's own box: c0 at its bottom (or left), c1 at its top."""
        m = self.emit((1, 1, 1))
        nt = m.node_tree
        tc = nt.nodes.new("ShaderNodeTexCoord")
        sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].color = (*c0, 1)
        ramp.color_ramp.elements[1].color = (*c1, 1)
        nt.links.new(tc.outputs["Generated"], sep.inputs[0])
        nt.links.new(sep.outputs[axis], ramp.inputs["Fac"])
        nt.links.new(ramp.outputs["Color"], next(n for n in nt.nodes if n.type == "EMISSION").inputs["Color"])
        return m

    def lit(self, rgb, rough, metal=0.0, coat=0.0, emit=0.0):
        m = bpy.data.materials.new("ArtL")
        m.use_nodes = True
        b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        b.inputs["Base Color"].default_value = (*rgb, 1)
        b.inputs["Roughness"].default_value = rough
        b.inputs["Metallic"].default_value = metal
        for k, v in (("Coat Weight", coat), ("Emission Strength", emit)):
            try:
                b.inputs[k].default_value = v
            except KeyError:
                pass
        try:
            b.inputs["Emission Color"].default_value = (*rgb, 1)
        except KeyError:
            pass
        return m

    def obj(self, name, data, mats=()):
        o = bpy.data.objects.new(name, data)
        self.col.objects.link(o)
        for m in mats:
            o.data.materials.append(m)
        return o

    def rect(self, x0, y0, x1, y1, m, z=None):
        z = self._z(z)
        me = bpy.data.meshes.new("R")
        me.from_pydata([(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], [], [(0, 1, 2, 3)])
        return self.obj("R", me, [m])

    def strip(self, pts, widths, m, z=None):
        """A band along a polyline (x, y), widths[i] across it."""
        z = self._z(z)
        vs, fs = [], []
        n = len(pts)
        for i in range(n):
            a, b = pts[max(0, i - 1)], pts[min(n - 1, i + 1)]
            tx, ty = b[0] - a[0], b[1] - a[1]
            l = math.hypot(tx, ty) or 1.0
            nx, ny = -ty / l, tx / l
            w = widths[i] / 2
            vs += [(pts[i][0] - nx * w, pts[i][1] - ny * w, z), (pts[i][0] + nx * w, pts[i][1] + ny * w, z)]
        for i in range(n - 1):
            fs.append((2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1))
        me = bpy.data.meshes.new("S")
        me.from_pydata(vs, [], fs)
        return self.obj("S", me, [m])

    def disc(self, x, y, r, m, z=None, n=32):
        z = self._z(z)
        me = bpy.data.meshes.new("D")
        vs = [(x, y, z)] + [(x + r * math.cos(2 * math.pi * k / n), y + r * math.sin(2 * math.pi * k / n), z) for k in range(n)]
        me.from_pydata(vs, [], [(0, 1 + k, 1 + (k + 1) % n) for k in range(n)])
        return self.obj("D", me, [m])

    def text(self, body, size, x, y, m, font="arialbd.ttf", z=None, rot=0.0, align="CENTER", space=1.0, offset=0.0,
             valign="CENTER", shear=0.0):
        z = self._z(z)
        cu = bpy.data.curves.new("T", "FONT")
        cu.body = body
        cu.size = size
        cu.align_x = align
        cu.align_y = valign
        cu.space_character = space
        cu.offset = offset
        cu.shear = shear
        try:
            cu.font = bpy.data.fonts.load(FONTS + font, check_existing=True)
        except Exception:
            pass
        o = self.obj("T", cu, [m if isinstance(m, bpy.types.Material) else self.emit(m)])
        o.location = (x, y, z)
        o.rotation_euler = (0, 0, math.radians(rot))
        return o

    def light(self, x, y, z, energy, size=0.3, rgb=(1, 1, 1)):
        L = bpy.data.lights.new("K", "AREA")
        L.energy = energy
        L.size = size
        L.color = rgb
        o = self.obj("K", L)
        o.location = (x, y, z)
        o.rotation_euler = Vector((-x, -y, -z)).to_track_quat("-Z", "Y").to_euler()
        return o

    def can(self, x, y, tilt, r, h, body, label="Polar", z=0.05, lying=False):
        """A drinks can (its axis up the picture, leaning tilt degrees), its brand on its face, beaded with cold."""
        import bmesh as _bm
        me = bpy.data.meshes.new("Can")
        bb = _bm.new()
        _bm.ops.create_cone(bb, cap_ends=True, segments=48, radius1=r, radius2=r, depth=h)
        bb.to_mesh(me)
        bb.free()
        o = self.obj("Can", me, [self.lit(body, 0.25, 0.55, 0.8)])
        for poly in o.data.polygons:
            poly.use_smooth = True
        R = Matrix.Rotation(math.radians(tilt), 3, "Z") @ Matrix.Rotation(math.radians(-90), 3, "X")
        o.matrix_world = Matrix.Translation((x, y, z)) @ R.to_4x4()
        up = R @ Vector((0, 0, 1))
        lid = bpy.data.meshes.new("Lid")
        bb = _bm.new()
        _bm.ops.create_cone(bb, cap_ends=True, segments=48, radius1=r * 0.98, radius2=r * 0.8, depth=h * 0.09)
        bb.to_mesh(lid)
        bb.free()
        lo = self.obj("Lid", lid, [self.lit((0.8, 0.8, 0.82), 0.22, 1.0)])
        lo.matrix_world = Matrix.Translation(Vector((x, y, z)) + up * h * 0.54) @ R.to_4x4()
        # The brand down the can, white, and a white wave under it.
        t = self.text(label, r * 1.05, x, y, (0.98, 0.97, 0.95), "segoescb.ttf", z=z + r + 0.002, rot=tilt + 90)
        rnd = random.Random(int(x * 1000) + 7)
        drop = self.lit((0.5, 0.52, 0.55), 0.02, 0.0, 1.0)
        for _ in range(90):
            a = rnd.uniform(-1.3, 1.3)
            s = rnd.uniform(-0.45, 0.45) * h
            p = Vector((x, y, z)) + up * s + (Matrix.Rotation(math.radians(tilt), 3, "Z") @ Vector((math.sin(a) * r, 0, 0))) \
                + Vector((0, 0, math.cos(a) * r))
            dm = bpy.data.meshes.new("Drop")
            bb = _bm.new()
            _bm.ops.create_uvsphere(bb, u_segments=10, v_segments=6, radius=rnd.uniform(0.0008, 0.0022) * (r / 0.03))
            bb.to_mesh(dm)
            bb.free()
            do = self.obj("Drop", dm, [drop])
            do.location = p
        return o

    def ice(self, x0, x1, y0, y1, n, size, seed=3, z=0.02):
        import bmesh as _bm
        rnd = random.Random(seed)
        m = self.lit((0.7, 0.82, 0.9), 0.05, 0.0, 1.0, 0.25)
        for _ in range(n):
            d = bpy.data.meshes.new("Ice")
            bb = _bm.new()
            _bm.ops.create_cube(bb, size=rnd.uniform(0.6, 1.0) * size)
            _bm.ops.bevel(bb, geom=list(bb.verts) + list(bb.edges), offset=size * 0.12, offset_type="OFFSET", segments=2,
                          profile=0.5, affect="EDGES", clamp_overlap=True)
            bb.to_mesh(d)
            bb.free()
            io = self.obj("Ice", d, [m])
            io.location = (rnd.uniform(x0, x1), rnd.uniform(y0, y1), z + rnd.uniform(0, size))
            io.rotation_euler = (rnd.uniform(0, 3), rnd.uniform(0, 3), rnd.uniform(0, 3))


def art(path, W, H, px, build, samples=64, world=0.35):
    """Renders build(Art, W, H) over W x H metres into a px-wide PNG and loads it."""
    prev = bpy.context.window.scene if bpy.context.window else bpy.context.scene
    sc = bpy.data.scenes.new("Art")
    try:
        bpy.context.window.scene = sc
    except Exception:
        pass
    A = Art(sc.collection)
    build(A, W, H)
    cam = bpy.data.cameras.new("ArtCam")
    cam.type = "ORTHO"
    cam.ortho_scale = max(W, H)
    co = A.obj("ArtCam", cam)
    co.location = (0, 0, 2.0)
    sc.camera = co
    sc.render.resolution_x = px
    sc.render.resolution_y = max(8, int(round(px * H / W)))
    try:
        sc.render.engine = "CYCLES"
    except TypeError:
        pass
    use_gpu(sc)
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "Standard"
    sc.world = bpy.data.worlds.new("ArtWorld")
    sc.world.use_nodes = True
    bgw = next(n for n in sc.world.node_tree.nodes if n.type == "BACKGROUND")
    bgw.inputs["Color"].default_value = (0.32, 0.34, 0.38, 1)
    bgw.inputs["Strength"].default_value = world
    sc.render.image_settings.file_format = "PNG"
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


def _tmp(name):
    import tempfile
    return os.path.join(tempfile.gettempdir(), "vend_" + name + ".png")


def _ss(a, b, t):
    t = min(1.0, max(0.0, (t - a) / (b - a)))
    return t * t * (3 - 2 * t)


def sign_build(A, W, H):
    """The big sign: a red field, a white swoosh sweeping down its right (a grey line inside it), POLAR in a heavy
    script up the middle reading bottom to top, outlined in red where it crosses the swoosh, Enjoy beside it, a line
    of small print, and the hairline crack across it the owner's photo has."""
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.grad((0.45, 0.008, 0.014), RED))
    # The swoosh's inner edge (as in the photo: in from the top, out toward the right, back in down to the bottom),
    # the white to its right: a hairline at the top, widening to the panel's edge below the middle.
    edge = catmull([(0.03, H / 2 + 0.05), (0.12, 0.44), (0.2, 0.3), (0.19, 0.16), (0.12, -0.04), (0.085, -0.3),
                    (0.065, -H / 2 - 0.06)], 16)
    cs = [(q[0], q[1]) for q in edge]
    n = len(cs) - 1

    def wid_at(y):
        if y > 0.16:
            return 0.006 + 0.005 * _ss(0.55, 0.16, y)
        if y > 0.02:
            return 0.011 + 0.299 * _ss(0.16, 0.02, y)
        return 0.31 - 0.2 * _ss(-0.04, -0.5, y)       # (back inside the panel near the foot: red to its right)
    wid = [wid_at(q[1]) for q in cs]
    shifted = []
    for i in range(n + 1):
        a, b = cs[max(0, i - 1)], cs[min(n, i + 1)]
        tx, ty = b[0] - a[0], b[1] - a[1]
        l = math.hypot(tx, ty) or 1.0
        nx, ny = -ty / l, tx / l          # left of the way down: toward +x
        shifted.append((cs[i][0] + nx * wid[i] / 2, cs[i][1] + ny * wid[i] / 2))
    A.strip(shifted, wid, A.emit((0.97, 0.96, 0.95)))
    inner = []
    for i in range(n + 1):
        a, b = cs[max(0, i - 1)], cs[min(n, i + 1)]
        tx, ty = b[0] - a[0], b[1] - a[1]
        l = math.hypot(tx, ty) or 1.0
        nx, ny = -ty / l, tx / l
        inner.append((cs[i][0] - nx * 0.017, cs[i][1] - ny * 0.017))
    A.strip(inner, [0.003 + 0.004 * _ss(0.35, 0.0, q[1]) for q in cs], A.emit((0.8, 0.76, 0.76)))
    # The brand: outlined, then white over it.
    A.text("Polar", 0.38, 0.0, -0.05, RED, "segoescb.ttf", rot=90, offset=0.011)
    A.text("Polar", 0.38, 0.0, -0.05, (0.98, 0.975, 0.965), "segoescb.ttf", rot=90)
    A.text("Enjoy", 0.064, -0.165, 0.33, (0.97, 0.96, 0.95), "seguibli.ttf", rot=90)
    A.text("POLAR BOTTLING COMPANY", 0.011, -0.238, -0.36, (0.92, 0.7, 0.7), "arialbd.ttf", rot=90)
    # The crack: a hairline running in from the left edge.
    rnd = random.Random(12)
    pts, x, y = [], -W / 2 - 0.01, -0.025
    while x < 0.06:
        pts.append((x, y))
        x += rnd.uniform(0.008, 0.02)
        y += rnd.uniform(-0.004, 0.006)
    A.strip(pts, [0.0012] * len(pts), A.emit((0.95, 0.72, 0.72)))


def pic1_build(A, W, H):
    """The top picture: a can on ice in a frosty white-blue, 'Nothing beats it ice cold.'"""
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.grad((0.55, 0.7, 0.84), (0.96, 0.97, 0.98)))
    A.text("Nothing beats it", 0.0145, -W / 2 + 0.008, H / 2 - 0.02, (0.55, 0.02, 0.03), "segoescb.ttf", align="LEFT")
    A.text("ice cold.", 0.02, -W / 2 + 0.012, H / 2 - 0.043, (0.55, 0.02, 0.03), "segoescb.ttf", align="LEFT")
    A.ice(-W / 2, W / 2, -H / 2 - 0.01, -H / 2 + 0.035, 45, 0.022, seed=4)
    A.can(0.045, -0.005, -14, 0.031, 0.112, (0.55, 0.015, 0.02), z=0.06)
    A.ice(-0.02, W / 2, -H / 2 - 0.01, -H / 2 + 0.025, 14, 0.02, seed=9, z=0.1)
    A.light(-0.2, 0.25, 0.6, 14, 0.5)
    A.light(0.25, -0.1, 0.5, 4, 0.5, (0.8, 0.9, 1.0))


def pic2_build(A, W, H):
    """The second picture: a can lying in crushed ice on red, ICE COLD and the brand."""
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.grad((0.3, 0.01, 0.015), (0.72, 0.05, 0.05)))
    A.ice(-W / 2, W / 2, -H / 2 - 0.01, 0.0, 70, 0.022, seed=7)
    A.can(-0.005, -0.01, -62, 0.03, 0.11, (0.55, 0.015, 0.02), z=0.07)
    A.ice(-W / 2, W / 2, -H / 2 - 0.01, -0.035, 18, 0.02, seed=11, z=0.11)
    A.text("ICE COLD", 0.019, -W / 2 + 0.008, H / 2 - 0.018, (0.98, 0.97, 0.95), "ariblk.ttf", align="LEFT", z=0.2)
    A.text("Polar", 0.024, W / 2 - 0.01, -H / 2 + 0.016, (0.98, 0.97, 0.95), "segoescb.ttf", align="RIGHT", z=0.2)
    A.light(-0.2, 0.25, 0.6, 14, 0.5)
    A.light(0.25, -0.1, 0.5, 4, 0.5, (0.9, 0.9, 1.0))


FLAVOURS = [
    # (top colour, bottom colour, lines [(text, size, font, colour)])
    ((0.9, 0.9, 0.92), (0.6, 0.6, 0.64), [("diet", 0.011, "arialbd.ttf", (0.55, 0.02, 0.03)), ("Polar", 0.02, "segoescb.ttf", (0.6, 0.02, 0.03))]),
    ((0.35, 0.72, 0.3), (0.08, 0.42, 0.14), [("LEMON", 0.0125, "ariblk.ttf", (0.98, 0.97, 0.6)), ("LIME", 0.0155, "ariblk.ttf", (0.98, 0.98, 0.98))]),
    ((0.96, 0.9, 0.74), (0.8, 0.68, 0.48), [("Root", 0.016, "seguibli.ttf", (0.35, 0.16, 0.05)), ("Beer", 0.016, "seguibli.ttf", (0.35, 0.16, 0.05))]),
    ((1.0, 0.62, 0.15), (0.85, 0.32, 0.03), [("Orange", 0.0175, "seguibli.ttf", (0.99, 0.98, 0.95))]),
    ((0.3, 0.55, 0.88), (0.08, 0.25, 0.62), [("Fruit", 0.0145, "seguibli.ttf", (1.0, 0.85, 0.3)), ("Punch", 0.0145, "seguibli.ttf", (1.0, 0.85, 0.3))]),
]
SOLD_OUT = 3
TILE_GAP = 0.006


def tiles_build(A, W, H):
    """The five picture buttons, one over the other in one print."""
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.05, 0.05, 0.05)))
    for i, (top, bot, lines) in enumerate(FLAVOURS):
        yc = H / 2 - TILE_H / 2 - i * (TILE_H + TILE_GAP)
        A.rect(-W / 2, yc - TILE_H / 2, W / 2, yc + TILE_H / 2, A.grad(bot, top), z=0.001)
        A.rect(-W / 2, yc + TILE_H / 2 - 0.006, W / 2, yc + TILE_H / 2, A.emit(tuple(min(1, c * 1.15 + 0.05) for c in top)), z=0.0015)
        tot = sum(l[1] * 1.15 for l in lines)
        y = yc + tot / 2
        for (t, s, f, c) in lines:
            y -= s * 1.15 / 2
            A.text(t, s, 0.0006, y - 0.0008, (0.05, 0.03, 0.03), f, z=0.002)
            A.text(t, s, 0.0, y, c, f, z=0.003)
            y -= s * 1.15 / 2
        if i == SOLD_OUT:
            A.rect(-W / 2 + 0.004, yc - TILE_H / 2 + 0.003, W / 2 - 0.004, yc - TILE_H / 2 + 0.013, A.emit((0.85, 0.05, 0.04)), z=0.004)
            A.text("SOLD OUT", 0.0075, 0.0, yc - TILE_H / 2 + 0.008, (1.0, 0.95, 0.9), "arialbd.ttf", z=0.005)


def price_build(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.93, 0.93, 0.9)))
    A.text("PRICE", 0.006, 0.0, H / 2 - 0.009, (0.6, 0.05, 0.05), "arialbd.ttf")
    A.text("1.00", 0.0175, 0.0, -0.004, (0.05, 0.05, 0.05), "arialbd.ttf")


def label_build(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.04, 0.04, 0.04)))
    for k, s in enumerate(("Deposit nickels, dimes", "or quarters.", "Change returned below")):
        A.text(s, 0.0078, -W / 2 + 0.006, H / 2 - 0.009 - k * 0.0095, (0.93, 0.93, 0.9), "arialbd.ttf", align="LEFT")
    A.text("Bills not accepted. Use exact change when lit.", 0.0042, -W / 2 + 0.006, -H / 2 + 0.005, (0.6, 0.6, 0.58), "arial.ttf", align="LEFT")


def sticker_build(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.92, 0.91, 0.86)))
    A.disc(-W / 2 + 0.017, 0.004, 0.0135, A.emit((0.85, 0.62, 0.12)))
    A.disc(-W / 2 + 0.017, 0.004, 0.0105, A.emit((0.98, 0.86, 0.35)))
    A.text("OK", 0.008, -W / 2 + 0.017, 0.004, (0.5, 0.3, 0.05), "ariblk.ttf")
    for k, s in enumerate(("IF THIS MACHINE", "FAILS TO VEND", "CALL FOR REFUND", "1-800-555-0142")):
        A.text(s, 0.0052, -W / 2 + 0.036, H / 2 - 0.009 - k * 0.0085, (0.15, 0.15, 0.15), "arialbd.ttf", align="LEFT")
    A.rect(-W / 2 + 0.002, -H / 2 + 0.002, W / 2 - 0.002, -H / 2 + 0.006, A.emit((0.6, 0.05, 0.05)))


def badge_build(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.72, 0.72, 0.72)))
    A.text("VENDCO", 0.0085, 0.0, -0.0003, (0.12, 0.12, 0.12), "ariblk.ttf", space=1.15)


def push_build(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.035, 0.035, 0.035)))
    A.text("PUSH", 0.013, 0.0, 0.0, (0.75, 0.75, 0.72), "ariblk.ttf", space=1.2)


def plate_build(A, W, H):
    A.rect(-W / 2 - 0.01, -H / 2 - 0.01, W / 2 + 0.01, H / 2 + 0.01, A.emit((0.7, 0.7, 0.68)))
    for k, s in enumerate(("VENDCO REFRIGERATED VENDOR", "MODEL 501-9   115 V  60 Hz  9.5 A", "REFRIGERANT R-134a  10 OZ",
                           "SERIAL 4471-0938")):
        A.text(s, 0.0034, -W / 2 + 0.004, H / 2 - 0.007 - k * 0.0065, (0.1, 0.1, 0.1), "arialbd.ttf", align="LEFT")


# ------------------------------------------------------------------------------------------------- the machine

def vending_machine():
    use_gpu(bpy.context.scene)
    BLK = S("Paint_VendCabinet", (0.045, 0.045, 0.05), "paint", 0.42)
    DOOR = S("Paint_VendDoor", (0.05, 0.05, 0.055), "paint", 0.38)
    KICK = S("Paint_VendKick", (0.62, 0.04, 0.035), "paint", 0.3)
    PAN = S("Plastic_VendPanel", (0.035, 0.035, 0.038), "plastic", 0.45)
    BEZ = S("Plastic_VendBezel", (0.03, 0.03, 0.032), "plastic", 0.35)
    TRIM = S("Chrome_VendTrim", (0.82, 0.82, 0.82), "metal", 0.18, 1.0)
    HOLE = S("Plastic_VendHole", (0.012, 0.012, 0.012), "plastic", 0.7)
    WIN = S("Glass_VendWindow", (0.12, 0.13, 0.13), "glass", 0.08)
    RUB = S("Rubber_VendFoot", (0.05, 0.05, 0.05), "rubber", 0.6)
    p = []
    # The cabinet: its box behind the door, the sides' front flanges either side of the door, a top cap with a lip,
    # the plinth (set back a little), four levelling feet.
    box(p, "Cabinet", -W_ / 2, W_ / 2, 0.095, H_ - 0.03, FRONT + 0.02, D_ / 2, BLK, 0.008)
    for (x0, x1) in ((-W_ / 2, -0.455), (0.455, W_ / 2)):
        box(p, "Flange", x0, x1, 0.095, H_ - 0.03, FRONT + 0.004, FRONT + 0.03, BLK, 0.004)
    box(p, "TopCap", -W_ / 2 - 0.003, W_ / 2 + 0.003, H_ - 0.032, H_, FRONT - 0.006, D_ / 2 + 0.003, BLK, 0.006, 3)
    box(p, "Plinth", -W_ / 2 + 0.01, W_ / 2 - 0.01, 0.022, 0.097, FRONT + 0.014, D_ / 2 - 0.01, BLK, 0.004)
    for k in range(6):
        box(p, "PlinthVent", -0.3 + k * 0.11, -0.3 + k * 0.11 + 0.07, 0.045, 0.075, FRONT + 0.0125, FRONT + 0.016, HOLE, 0.002, 1)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(lathe("Foot", [(0.0, 0.0), (0.028, 0.0), (0.028, 0.008), (0.012, 0.012), (0.012, 0.024), (0.0, 0.024)], RUB, 12,
                           xf=M4(None, (sx * 0.4, sy * 0.33, 0.0))))
    # The door's black face round the sign and the panel (5 cm deep), the band under them, the kick panel's frame.
    DY0, DY1 = FRONT, FRONT + 0.05
    box(p, "DoorTop", -0.455, 0.455, PZ1, H_ - 0.032, DY0, DY1, DOOR, 0.004)
    box(p, "DoorL", -0.455, SX0, PZ0, PZ1, DY0, DY1, DOOR, 0.0, 1)
    box(p, "DoorM", SX1, PX0, PZ0, PZ1, DY0, DY1, DOOR, 0.0, 1)
    box(p, "DoorSignTop", SX0, SX1, SZ1, PZ1, DY0, DY1, DOOR, 0.0, 1)
    box(p, "DoorSignBot", SX0, SX1, PZ0, SZ0, DY0, DY1, DOOR, 0.0, 1)
    box(p, "DoorR", PX1, 0.455, PZ0, PZ1, DY0, DY1, DOOR, 0.0, 1)
    px0, px1, pz0, pz1 = PORT
    t = 0.02
    box(p, "BandL", -0.455, px0 - t, 0.3375, PZ0, DY0, DY1, DOOR, 0.0, 1)
    box(p, "BandR", px1 + t, 0.455, 0.3375, PZ0, DY0, DY1, DOOR, 0.0, 1)
    box(p, "BandTop", px0 - t, px1 + t, pz1 + t, PZ0, DY0, DY1, DOOR, 0.0, 1)
    box(p, "BandBot", px0 - t, px1 + t, 0.3375, pz0 - t, DY0, DY1, DOOR, 0.0, 1)
    kx0, kx1, kz0, kz1 = -0.38, 0.335, 0.112, 0.325
    box(p, "KickL", -0.455, kx0, 0.097, 0.3375, DY0, DY1, DOOR, 0.0, 1)
    box(p, "KickR", kx1, 0.455, 0.097, 0.3375, DY0, DY1, DOOR, 0.0, 1)
    box(p, "KickTop", kx0, kx1, kz1, 0.3375, DY0, DY1, DOOR, 0.0, 1)
    box(p, "KickBot", kx0, kx1, 0.097, kz0, DY0, DY1, DOOR, 0.0, 1)
    box(p, "Kick", kx0, kx1, kz0, kz1, FRONT + 0.003, DY1, KICK, 0.003)
    # The sign: its bezel (black plastic, raised 4 mm, a chamfer inside); behind it the lit face (VendingGlow).
    frame(p, "SignBezel", SX0, SX1, SZ0, SZ1, 0.016, FRONT - 0.004, FRONT + 0.016, BEZ, 0.004)
    box(p, "SignBack", SX0 - 0.01, SX1 + 0.01, SZ0 - 0.01, SZ1 + 0.01, FRONT + 0.03, FRONT + 0.04, HOLE, 0.0, 1)
    # The controls' panel, 3 mm back from the door, a thin edge round it.
    box(p, "Panel", PX0, PX1, PZ0, PZ1, C, FRONT + 0.045, PAN, 0.003)
    # The two pictures' and the price's windows; the five buttons' bezels.
    for (x0, x1, z0, z1) in (PIC1, PIC2):
        frame(p, "PicBezel", x0, x1, z0, z1, 0.006, C - 0.003, C + 0.004, BEZ, 0.0015)
    frame(p, "PriceBezel", *PRICE, 0.004, C - 0.003, C + 0.004, BEZ, 0.0012)
    for (x0, z0) in TILES:
        frame(p, "TileBezel", x0, x0 + TILE_W, z0, z0 + TILE_H, 0.0045, C - 0.004, C + 0.004, BEZ, 0.0015)
    # The coin entry: a black plate, the chrome entry with its slot and the reject lever, the acceptor's window.
    STN = S("Stainless_VendCoin", (0.7, 0.7, 0.7), "metal", 0.3, 1.0)
    box(p, "CoinPlate", 0.159, 0.305, 1.2875, 1.4225, C - 0.006, C + 0.002, STN, 0.003)
    box(p, "LabelBack", 0.163, 0.301, 1.292, 1.336, C - 0.0064, C - 0.005, PAN, 0.001, 1)
    box(p, "CoinEntry", 0.166, 0.226, 1.348, 1.414, C - 0.013, C - 0.005, TRIM, 0.004, 3)
    box(p, "CoinSlot", 0.1795, 0.1835, 1.366, 1.398, C - 0.0136, C - 0.012, HOLE, 0.0, 1)
    box(p, "Reject", 0.2, 0.216, 1.364, 1.4, C - 0.019, C - 0.012, TRIM, 0.004, 2)
    box(p, "AcceptorRim", 0.234, 0.298, 1.366, 1.41, C - 0.009, C - 0.005, TRIM, 0.003, 2)
    box(p, "Acceptor", 0.238, 0.294, 1.37, 1.406, C - 0.0094, C - 0.008, WIN, 0.001, 1)
    # The T-handle lock: a chrome escutcheon, its dark recess, the handle lying in it, the lock under.
    box(p, "Escutcheon", 0.3525, 0.405, 0.9625, 1.11, C - 0.01, C + 0.002, TRIM, 0.006, 3)
    box(p, "HandleWell", 0.362, 0.3955, 0.99, 1.1, C - 0.0102, C - 0.009, HOLE, 0.002, 1)
    box(p, "Handle", 0.37, 0.3875, 1.0, 1.092, C - 0.02, C - 0.009, TRIM, 0.005, 3)
    p.append(lathe("Lock", [(0.0, 0.0), (0.0105, 0.0), (0.0105, 0.004), (0.009, 0.008), (0.0, 0.0085)], TRIM, 20,
                   xf=M4(rot('X', 90), (0.37875, C - 0.0098, 0.976))))
    box(p, "Keyway", 0.3775, 0.38, 0.97, 0.982, C - 0.0185, C - 0.017, HOLE, 0.0, 1)
    # The coin return button and the cup.
    box(p, "ReturnRim", 0.2525, 0.2775, 0.715, 0.759, C - 0.006, C + 0.002, TRIM, 0.003, 2)
    box(p, "ReturnBtn", 0.257, 0.273, 0.72, 0.754, C - 0.009, C - 0.004, PAN, 0.003, 2)
    frame(p, "Cup", 0.253, 0.307, 0.6, 0.69, 0.008, C - 0.016, C + 0.002, TRIM, 0.003)
    box(p, "CupBack", 0.253, 0.307, 0.6, 0.69, C + 0.03, C + 0.034, HOLE, 0.0, 1)
    for (x0, x1, z0, z1) in ((0.252, 0.254, 0.6, 0.69), (0.306, 0.308, 0.6, 0.69), (0.253, 0.307, 0.689, 0.691)):
        box(p, "CupWall", x0, x1, z0, z1, C - 0.008, C + 0.032, HOLE, 0.0, 1)
    tray = rbox("CupTray", (0.054, 0.045, 0.004), (0, 0, 0), TRIM, 0.0015, 1)
    place(tray, rot('X', 18), (0.28, C + 0.008, 0.607))
    p.append(tray)
    # The delivery port: a thick frame, the dark well behind it, the flap hung from its top, pushed in a little.
    frame(p, "PortFrame", px0, px1, pz0, pz1, t, FRONT - 0.016, FRONT + 0.004, BEZ, 0.005)
    box(p, "PortBack", px0, px1, pz0, pz1, FRONT + 0.13, FRONT + 0.14, HOLE, 0.0, 1)
    for (x0, x1, z0, z1) in ((px0 - 0.002, px0, pz0, pz1), (px1, px1 + 0.002, pz0, pz1), (px0, px1, pz1, pz1 + 0.002), (px0, px1, pz0 - 0.002, pz0)):
        box(p, "PortWall", x0, x1, z0, z1, FRONT - 0.004, FRONT + 0.13, HOLE, 0.0, 1)
    flap = rbox("Flap", (px1 - px0 - 0.008, 0.006, pz1 - pz0 - 0.006), (0, 0, -(pz1 - pz0 - 0.006) / 2), BEZ, 0.002, 1)
    place(flap, rot('X', 9), ((px0 + px1) / 2, FRONT + 0.006, pz1 - 0.003))
    p.append(flap)
    # The back: the condenser's louvres, the cord's grommets either side, the hinge side's two pins.
    for k in range(9):
        box(p, "BackLouvre", -0.36, 0.36, 0.12 + k * 0.03, 0.135 + k * 0.03, D_ / 2, D_ / 2 + 0.004, BLK, 0.002, 1)
    for sx in (-1, 1):
        p.append(lathe("Grommet", [(0.0, 0.0), (0.012, 0.0), (0.012, 0.01), (0.0, 0.01)], RUB, 12,
                       xf=M4(rot('X', -90), (sx * 0.4, D_ / 2, 0.12))))
    for z in (0.32, 1.55):
        p.append(lathe("Hinge", [(0.0, 0.0), (0.007, 0.0), (0.007, 0.05), (0.0, 0.05)], BLK, 10, xf=M4(None, (-0.4585, FRONT + 0.012, z))))
    base = finish(p, "VendingMachine", sharp_deg=50)
    # Its prints: the coin label, the service sticker, the maker's badge, PUSH on the flap, the rating plate.
    prints = []
    bd = art(_tmp("badge"), 0.056, 0.015, 256, badge_build)
    badge = rbox("Badge", (0.058, 0.004, 0.017), (0.302, FRONT - 0.002, 0.556), TRIM, 0.0015, 1)
    prints.append(card("BadgeFace", 0.056, 0.015, (0.302, FRONT - 0.0041, 0.556), img_mat("Chrome_VendBadge", bd, 0.25)))
    pu = art(_tmp("push"), 0.09, 0.028, 256, push_build)
    R = rot('X', 9)
    hinge = Vector(((px0 + px1) / 2, FRONT + 0.006, pz1 - 0.003))
    prints.append(card("Push", 0.09, 0.028, tuple(hinge + R @ Vector((0, -0.0031, -0.03))), img_mat("Plastic_VendPush", pu, 0.4),
                       normal=tuple(R @ Vector((0, -1, 0)))))
    pl = art(_tmp("plate"), 0.09, 0.03, 420, plate_build)
    prints.append(card("Plate", 0.09, 0.03, (W_ / 2 + 0.0004, 0.25, 0.3), img_mat("Alu_VendPlate", pl, 0.35), normal=(1, 0, 0)))
    return join([base, badge] + prints, "VendingMachine")


def vending_glow():
    use_gpu(bpy.context.scene)
    W, H = SX1 - SX0, SZ1 - SZ0
    sign = art(_tmp("sign"), W, H, 1100, sign_build, samples=48)
    parts = []
    SIGN = img_mat("Glow_VendSign", sign, 0.15)
    parts.append(card("Sign", W + 0.012, H + 0.012, ((SX0 + SX1) / 2, FRONT + 0.0115, (SZ0 + SZ1) / 2), SIGN))
    # (the card a little bigger than its window, under the bezel: its UVs stretched to keep the art on the window)
    sc = parts[-1]
    for l in sc.data.uv_layers[0].data:
        u, v = l.uv
        l.uv = (0.5 + (u - 0.5) * (W + 0.012) / W, 0.5 + (v - 0.5) * (H + 0.012) / H)
    for key, rect, fn in (("pic1", PIC1, pic1_build), ("pic2", PIC2, pic2_build)):
        x0, x1, z0, z1 = rect
        im = art(_tmp(key), x1 - x0, z1 - z0, 420, fn, samples=96)
        parts.append(card(key, x1 - x0 + 0.004, z1 - z0 + 0.004, ((x0 + x1) / 2, C - 0.0012, (z0 + z1) / 2),
                          img_mat("Glow_Vend" + key.capitalize(), im, 0.15)))
    x0, x1, z0, z1 = PRICE
    pr = art(_tmp("price"), x1 - x0, z1 - z0, 160, price_build)
    parts.append(card("Price", x1 - x0 + 0.003, z1 - z0 + 0.003, ((x0 + x1) / 2, C - 0.0012, (z0 + z1) / 2), img_mat("Glow_VendPrice", pr, 0.2)))
    # The coin label and the service sticker are here too (they never shine: the glow mask leaves them dark), for
    # this sheet's sharper texels: their small print reads.
    lab = art(_tmp("label"), 0.136, 0.042, 600, label_build)
    parts.append(card("Label", 0.136, 0.042, (0.232, C - 0.0066, 1.314), img_mat("Print_VendLabel", lab, 0.5)))
    st = art(_tmp("sticker"), 0.085, 0.052, 420, sticker_build)
    parts.append(card("Sticker", 0.085, 0.052, (0.38, C - 0.0004, 1.433), img_mat("Paper_VendSticker", st, 0.6)))
    TH = len(FLAVOURS) * TILE_H + (len(FLAVOURS) - 1) * TILE_GAP
    tl = art(_tmp("tiles"), TILE_W, TH, 260, tiles_build, samples=32)
    TM = img_mat("Glow_VendButtons", tl, 0.2)
    for i, (x0, z0) in enumerate(TILES):
        v1 = 1.0 - i * (TILE_H + TILE_GAP) / TH
        v0 = v1 - TILE_H / TH
        parts.append(card_uv("Button", TILE_W, TILE_H, (x0 + TILE_W / 2, C - 0.0005, z0 + TILE_H / 2), TM, (0.0, v0, 1.0, v1)))
    return join(parts, "VendingGlow")


def glow_mask(m, nt, col):
    """What the lit faces shine with: the sign brighter over its two fluorescent tubes (a quarter of the way in from
    each side), darker toward its corners and the tubes' ends; the small windows and buttons evenly, a touch dimmer."""
    if col is None or m.name in ("Print_VendLabel", "Paper_VendSticker"):
        return None
    if m.name != "Glow_VendSign":
        return _mix(nt, 0.12, col, (0.0, 0.0, 0.0), 700, -200)
    tc = _n(nt, "ShaderNodeTexCoord", -900, -900)
    sep = _n(nt, "ShaderNodeSeparateXYZ", -700, -900)
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    x, z = sep.outputs["X"], sep.outputs["Z"]
    W = SX1 - SX0
    bands = None
    for f in (0.26, 0.74):
        c = SX0 + W * f
        d = _math(nt, "DIVIDE", _math(nt, "SUBTRACT", x, c, -500, -900), 0.085, -350, -900)
        g = _math(nt, "EXPONENT", _math(nt, "MULTIPLY", _math(nt, "MULTIPLY", d, d, -200, -900), -1.0, -100, -900), None, 0, -900)
        bands = g if bands is None else _math(nt, "ADD", bands, g, 100, -900)
    lum = _math(nt, "ADD", _math(nt, "MULTIPLY", bands, 0.3, 200, -900), 0.7, 300, -900)
    ends = _n(nt, "ShaderNodeMapRange", 200, -1100)
    ends.interpolation_type = "SMOOTHSTEP"
    nt.links.new(z, ends.inputs["Value"])
    ends.inputs["From Min"].default_value = SZ0 - 0.02
    ends.inputs["From Max"].default_value = SZ0 + 0.14
    ends2 = _n(nt, "ShaderNodeMapRange", 200, -1300)
    ends2.interpolation_type = "SMOOTHSTEP"
    nt.links.new(z, ends2.inputs["Value"])
    ends2.inputs["From Min"].default_value = SZ1 + 0.02
    ends2.inputs["From Max"].default_value = SZ1 - 0.12
    e = _math(nt, "MULTIPLY", ends.outputs["Result"], ends2.outputs["Result"], 400, -1200)
    e = _math(nt, "ADD", _math(nt, "MULTIPLY", e, 0.3, 500, -1200), 0.7, 600, -1200)
    k = _math(nt, "MULTIPLY", lum, e, 700, -1000, clamp=True)
    return _mul_col(nt, col, k)


def _mul_col(nt, col, k):
    m = nt.nodes.new("ShaderNodeMix")
    m.data_type = "RGBA"
    m.blend_type = "MULTIPLY"
    m.inputs["Factor"].default_value = 1.0
    a_in = next(i for i in m.inputs if i.name == "A" and i.type == "RGBA")
    b_in = next(i for i in m.inputs if i.name == "B" and i.type == "RGBA")
    nt.links.new(col, a_in)
    nt.links.new(k, b_in)
    return next(o for o in m.outputs if o.type == "RGBA")


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
    ("VendingMachine", vending_machine, (0.65, -1.0, 0.25), {"scale": 1.0, "px_per_m": 420.0, "wear": 0.8, "dirt": 0.9}),
    ("VendingGlow", vending_glow, (0.3, -1.0, 0.1), {"res": 2048, "scale": 0.5, "dirt": 0.3, "wear": 0.1, "glow": glow_mask}),
    ("VendingPlug", vending_plug, (0.6, -1.0, 0.4), {"res": 256, "scale": 0.2}),
])
print("[vending] done ->", OUT)
