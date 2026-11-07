"""
An old 1990s office door for the Backrooms' doorways: a stained wood six-panel door (raised panels between stiles and
rails), a plain wood frame (jambs, stop, casing on both faces), three hinges, and a simple satin metal lever handle on each
face. Built for the openings the chunk builder makes: 1.05 m wide, 2.1 m high, in a 0.15 m wall.

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_door.py -- Assets/Backrooms/Resources/Items [--preview <dir>]

Writes Door.fbx. Coordinates are written in Unity's frame (x along the wall, y up, z out of the wall toward the side the
door opens to) and converted; Unity (x, y, z) = Blender (x, -z, y), as for the other items.

Hierarchy (Unity names):
  Door          root, the opening's bottom centre on the wall's centre line
    Frame       jambs, stops, casings, strike plate, hinge leaves on the jamb (does not move)
    Leaf        empty on the hinge axis: (-0.48, 0, 0.065); turn it about +y to open (negative angles swing toward +z)
      Slab      the door itself, in Leaf space: x 0.003..0.957, y 0.012..2.052, z -0.04..0 (z = 0: the face toward +z)
      HandleFront  on the +z face, origin on the spindle (0.892, 0.97, 0); the lever points toward the hinge (-x) and
                   turns about the spindle (z) to go down
      HandleBack   the same on the -z face (origin (0.892, 0.97, -0.04))
Note: Unity's FBX import mirrors x, so in the game the hinge is on +x, the door opens by turning +y-wise and the levers
point toward +x (Interaction/Door.cs uses those, as imported).
Material slots: DoorWood (slab), DoorFrame (frame), DoorMetal (handles, hinges, strike). UVs: 1 unit = 0.5 m, with the
grain (texture U) up the stiles, panels and jambs and along the rails and the head casing.
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS and not ARGS[0].startswith("--") else os.getcwd()
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None
# --style overflow (2026-11-06, Docs/StyleSheet.md): every hard edge rounded off (1 cm on the slab, frame and casings,
# less on small parts), smooth shading with weighted normals, the handle's lever and its posts a fifth fatter.
OVERFLOW = "--style" in ARGS and ARGS[ARGS.index("--style") + 1] == "overflow"
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

UVM = 0.5          # metres per UV unit

# Opening and parts (Unity frame, metres).
W, H, T = 1.05, 2.1, 0.15
JAMB = 0.045                       # jamb lining thickness (sides and head)
HINGE_X, HINGE_Z = -W / 2 + JAMB, 0.065
SLAB_W, SLAB_H, SLAB_T = 0.954, 2.04, 0.04
SLAB_X0, SLAB_Y0 = 0.003, 0.012
SPINDLE = (SLAB_X0 + SLAB_W - 0.065, 0.97)


def B(p):
    return Vector((p[0], -p[2], p[1]))


def mat(name, rgb, rough=0.5, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    m.diffuse_color = (*rgb, 1)
    return m


WOOD = mat("DoorWood", (0.55, 0.36, 0.2), 0.45)
FRAME = mat("DoorFrame", (0.52, 0.34, 0.19), 0.5)
METAL = mat("DoorMetal", (0.72, 0.71, 0.68), 0.35, 1.0)


class Mesh:
    """One mesh being built: faces with their own UVs and material."""

    def __init__(self, name, mats):
        self.name, self.mats = name, mats
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def face(self, pts, uvs, m):
        vs = [self.bm.verts.new(B(p)) for p in pts]
        f = self.bm.faces.new(vs)
        f.material_index = m
        for loop, uv in zip(f.loops, uvs):
            loop[self.uv].uv = uv
        return f

    def quad(self, a, b, c, d, m, grain):
        """A flat quad (Unity points, counter-clockwise seen from outside); UVs projected along its normal with the
        grain axis ('x', 'y' or 'z') as U."""
        n = (Vector(b) - Vector(a)).cross(Vector(c) - Vector(a))
        ax = max(range(3), key=lambda i: abs(n[i]))
        others = [i for i in range(3) if i != ax]
        g = "xyz".index(grain)
        if g == ax:
            g = others[0]
        o = [i for i in others if i != g][0]
        self.face([a, b, c, d], [(p[g] / UVM, p[o] / UVM) for p in (a, b, c, d)], m)

    def box(self, lo, hi, m, grain, skip=()):
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        P = lambda x, y, z: (x, y, z)
        faces = {
            "+z": (P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)),
            "-z": (P(x1, y0, z0), P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0)),
            "+x": (P(x1, y0, z1), P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1)),
            "-x": (P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1), P(x0, y1, z0)),
            "+y": (P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0), P(x0, y1, z0)),
            "-y": (P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1)),
        }
        for k, q in faces.items():
            if k not in skip:
                self.quad(*q, m, grain)

    def frustum(self, lo, hi, inset, z0, z1, m, grain):
        """A raised panel field on a face at z0 (toward z1, either side): the panel's rectangle at z0, sloping in by
        inset to a flat top at z1."""
        x0, y0 = lo
        x1, y1 = hi
        o = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
        i = [(x0 + inset, y0 + inset), (x1 - inset, y0 + inset), (x1 - inset, y1 - inset), (x0 + inset, y1 - inset)]
        out = z1 > z0
        top = [(p[0], p[1], z1) for p in i]
        self.quad(*(top if out else top[::-1]), m, grain)
        for k in range(4):
            a, b = o[k], o[(k + 1) % 4]
            c, d = i[(k + 1) % 4], i[k]
            q = [(a[0], a[1], z0), (b[0], b[1], z0), (c[0], c[1], z1), (d[0], d[1], z1)]
            self.quad(*(q if out else q[::-1]), m, grain)

    def facing(self, pts, uvs, m, hint):
        """A face, wound so its normal points along hint."""
        n = (Vector(pts[1]) - Vector(pts[0])).cross(Vector(pts[2]) - Vector(pts[0]))
        if n.dot(Vector(hint)) < 0:
            pts, uvs = pts[::-1], uvs[::-1]
        self.face(pts, uvs, m)

    def cylinder(self, centre, axis, r, length, m, seg=16, caps=True):
        """A cylinder along 'x', 'y' or 'z' from centre (one end) for length (either sign)."""
        a = "xyz".index(axis)
        u, v = [k for k in range(3) if k != a]
        if OVERFLOW and r < 0.03:
            r *= 1.2
        ring = []
        for k in range(seg):
            t = 2 * math.pi * k / seg
            p0 = list(centre); p0[u] += r * math.cos(t); p0[v] += r * math.sin(t)
            p1 = list(p0); p1[a] += length
            ring.append((tuple(p0), tuple(p1)))
        for k in range(seg):
            (a0, a1), (b0, b1) = ring[k], ring[(k + 1) % seg]
            mid = (Vector(a0) + Vector(b0)) / 2 - Vector(centre)
            mid[a] = 0
            self.facing([a0, b0, b1, a1], [(k / seg, 0), ((k + 1) / seg, 0), ((k + 1) / seg, 0.1), (k / seg, 0.1)], m, mid)
        if caps:
            d = [0, 0, 0]; d[a] = 1 if length > 0 else -1
            self.facing([p[0] for p in ring], [(0, 0)] * seg, m, [-x for x in d])
            self.facing([p[1] for p in ring], [(0, 0)] * seg, m, d)

    def finish(self, parent=None, at=(0, 0, 0)):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=0.00002)
        if OVERFLOW:
            # Round every hard edge (faces meeting at over 30 degrees), as far as the parts' sizes allow.
            edges = [e for e in self.bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(30)]
            size = max((max(v.co[i] for v in self.bm.verts) - min(v.co[i] for v in self.bm.verts)) for i in range(3)) if self.bm.verts else 0
            if edges:
                bmesh.ops.bevel(self.bm, geom=edges, offset=0.008 if size > 0.3 else 0.0025, offset_type="OFFSET", segments=2,
                                profile=0.5, affect="EDGES", clamp_overlap=True)
        bmesh.ops.triangulate(self.bm, faces=self.bm.faces)
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(m)
        for p in me.polygons:
            p.use_smooth = OVERFLOW
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.collection.objects.link(ob)
        if OVERFLOW:
            # Weighted normals: the big faces stay flat, the rounded edges carry the shading round.
            bpy.ops.object.select_all(action="DESELECT")
            ob.select_set(True)
            bpy.context.view_layer.objects.active = ob
            wm = ob.modifiers.new("Weighted", "WEIGHTED_NORMAL")
            wm.keep_sharp = True
            bpy.ops.object.modifier_apply(modifier=wm.name)
        ob.location = B(at)
        if parent:
            ob.parent = parent
        return ob


def empty(name, at=(0, 0, 0), parent=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.05
    bpy.context.collection.objects.link(e)
    e.location = B(at)
    if parent:
        e.parent = parent
    return e


def frame(root):
    m = Mesh("Frame", [FRAME, METAL])
    zf, zb = T / 2 + 0.005, -T / 2 - 0.005
    hx = W / 2
    # Jambs lining the opening (a little proud of both wall faces), the head under the header.
    m.box((-hx, 0, zb), (-hx + JAMB, H - JAMB, zf), 0, "y", skip=("-y",))
    m.box((hx - JAMB, 0, zb), (hx, H - JAMB, zf), 0, "y", skip=("-y",))
    m.box((-hx, H - JAMB, zb), (hx, H, zf), 0, "x")
    # The stop the door closes against (behind it, on the -z side of the slab).
    sz0, sz1 = HINGE_Z - SLAB_T - 0.013, HINGE_Z - SLAB_T
    stop = 0.012
    m.box((-hx + JAMB, 0, sz0), (-hx + JAMB + stop, H - JAMB - stop, sz1), 0, "y", skip=("-y",))
    m.box((hx - JAMB - stop, 0, sz0), (hx - JAMB, H - JAMB - stop, sz1), 0, "y", skip=("-y",))
    m.box((-hx + JAMB, H - JAMB - stop, sz0), (hx - JAMB, H - JAMB, sz1), 0, "x")
    # Casing on both faces of the wall: two legs and a head, with a small reveal of the jamb's edge.
    cw, ct, rev = 0.075, 0.018, 0.005
    for z0, z1 in ((T / 2, T / 2 + ct), (-T / 2 - ct, -T / 2)):
        m.box((-hx - cw + JAMB - rev - JAMB, 0, z0), (-hx + JAMB - rev, H - JAMB - rev + cw, z1), 0, "y", skip=("-y",))
        m.box((hx - JAMB + rev, 0, z0), (hx + cw - JAMB + rev + JAMB, H - JAMB - rev + cw, z1), 0, "y", skip=("-y",))
        m.box((-hx + JAMB - rev, H - JAMB - rev, z0), (hx - JAMB + rev, H - JAMB - rev + cw, z1), 0, "x")
        # (2026-11-03) the back band: a raised step along the casing's outer edge, the stepped profile of a real casing.
        bw, bh = 0.014, 0.007
        out = (z1, z1 + bh) if z0 > 0 else (z0 - bh, z0)
        xl, xr = -hx - cw + JAMB - rev - JAMB, hx + cw - JAMB + rev + JAMB
        top = H - JAMB - rev + cw
        m.box((xl, 0, out[0]), (xl + bw, top, out[1]), 0, "y", skip=("-y",))
        m.box((xr - bw, 0, out[0]), (xr, top, out[1]), 0, "y", skip=("-y",))
        m.box((xl + bw, top - bw, out[0]), (xr - bw, top, out[1]), 0, "x")
    # Strike plate on the latch jamb, hinge leaves on the hinge jamb.
    m.box((hx - JAMB - 0.002, SPINDLE[1] - 0.04, HINGE_Z - SLAB_T + 0.003), (hx - JAMB, SPINDLE[1] + 0.04, HINGE_Z - 0.006), 1, "y")
    for y in (0.2, 1.0, 1.84):
        m.box((-hx + JAMB - 0.002, y - 0.045, HINGE_Z - 0.032), (-hx + JAMB, y + 0.045, HINGE_Z), 1, "y")
        # (2026-11-03) its three screws.
        for dy in (-0.03, 0.0, 0.03):
            m.cylinder((-hx + JAMB - 0.0026, y + dy, HINGE_Z - 0.016), "x", 0.0035, 0.0012, 1, seg=10)
    # The strike plate's screws.
    for dy in (-0.03, 0.03):
        m.cylinder((hx - JAMB - 0.0026, SPINDLE[1] + dy, (HINGE_Z - SLAB_T + 0.003 + HINGE_Z - 0.006) / 2), "x", 0.003, 0.0012, 1, seg=10)
    return m.finish(root)


def slab(leaf):
    m = Mesh("Slab", [WOOD, METAL])
    x0, x1 = SLAB_X0, SLAB_X0 + SLAB_W
    y0 = SLAB_Y0
    zf, zb = 0.0, -SLAB_T
    st, mull = 0.12, 0.1
    rails = [(0.0, 0.24), (0.76, 0.94), (1.56, 1.68), (1.92, 2.04)]
    rows = [(0.24, 0.76), (0.94, 1.56), (1.68, 1.92)]
    cx = x0 + SLAB_W / 2
    # Stiles (full height), rails and the middle mullions (full thickness).
    m.box((x0, y0, zb), (x0 + st, y0 + SLAB_H, zf), 0, "y")
    m.box((x1 - st, y0, zb), (x1, y0 + SLAB_H, zf), 0, "y")
    for a, b in rails:
        m.box((x0 + st, y0 + a, zb), (x1 - st, y0 + b, zf), 0, "x", skip=("-x", "+x"))
    for a, b in rows:
        m.box((cx - mull / 2, y0 + a, zb), (cx + mull / 2, y0 + b, zf), 0, "y", skip=("-y", "+y"))
    # Panels: a thinner board set back between them, with a raised field on each face.
    rec, hz = 0.012, 0.008
    zc = (zf + zb) / 2
    for a, b in rows:
        for px0, px1 in ((x0 + st, cx - mull / 2), (cx + mull / 2, x1 - st)):
            ya, yb = y0 + a, y0 + b
            # The panel board's faces (the sides of the opening it sits in are the frame's own faces, set back).
            m.quad((px0, ya, zc + hz), (px1, ya, zc + hz), (px1, yb, zc + hz), (px0, yb, zc + hz), 0, "y")
            m.quad((px1, ya, zc - hz), (px0, ya, zc - hz), (px0, yb, zc - hz), (px1, yb, zc - hz), 0, "y")
            # The frame's edges round the panel, from the face down to the board.
            for (ax0, ay0, ax1, ay1) in ((px0, ya, px1, ya), (px1, ya, px1, yb), (px1, yb, px0, yb), (px0, yb, px0, ya)):
                for zt, zbd in ((zf, zc + hz), (zb, zc - hz)):
                    q = [(ax0, ay0, zt), (ax1, ay1, zt), (ax1, ay1, zbd), (ax0, ay0, zbd)]
                    m.quad(*(q if zt == zf else q[::-1]), 0, "y")
            # Raised fields, both faces.
            m.frustum((px0 + 0.006, ya + 0.006), (px1 - 0.006, yb - 0.006), 0.042, zc + hz, zc + hz + 0.0055, 0, "y")
            m.frustum((px0 + 0.006, ya + 0.006), (px1 - 0.006, yb - 0.006), 0.042, zc - hz, zc - hz - 0.0055, 0, "y")
            # (2026-11-03) the sticking: a fine bead round the panel where the frame meets it, both faces.
            # Not in the new look: the rounding (8 mm) is bigger than the bead, which then showed through the frame's
            # edge as dashed lines (and small fussy parts go: Docs/StyleSheet.md, "Chunkification").
            bd, bp = 0.007, 0.004
            for zt, zs in (() if OVERFLOW else ((zf, -1), (zb, 1))):
                z0b, z1b = (zt - bp, zt) if zs < 0 else (zt, zt + bp)
                z0b, z1b = (zc + hz, zc + hz + bp) if zs < 0 else (zc - hz - bp, zc - hz)
                m.box((px0, ya, z0b), (px0 + bd, yb, z1b), 0, "y", skip=("-y", "+y"))
                m.box((px1 - bd, ya, z0b), (px1, yb, z1b), 0, "y", skip=("-y", "+y"))
                m.box((px0 + bd, ya, z0b), (px1 - bd, ya + bd, z1b), 0, "x", skip=("-x", "+x"))
                m.box((px0 + bd, yb - bd, z0b), (px1 - bd, yb, z1b), 0, "x", skip=("-x", "+x"))
    # Hinge knuckles on the hinge edge.
    for y in (0.2, 1.0, 1.84):
        m.cylinder((-0.002, y - 0.045, 0.004), "y", 0.0068, 0.09, 1, seg=12)
        # (2026-11-03) the pin's caps, a little wider, top and bottom.
        m.cylinder((-0.002, y - 0.049, 0.004), "y", 0.0074, 0.004, 1, seg=12)
        m.cylinder((-0.002, y + 0.045, 0.004), "y", 0.0074, 0.004, 1, seg=12)
    return m.finish(leaf)


def handle(name, leaf, back):
    s = -1 if back else 1
    origin = (SPINDLE[0], SPINDLE[1], -SLAB_T if back else 0.0)
    m = Mesh(name, [METAL])
    # Rose, neck, lever toward the hinge with a slight turn back toward the door at its end.
    m.cylinder((0, 0, 0), "z", 0.027, 0.006 * s, 0, seg=32)
    m.cylinder((0, 0, 0.006 * s), "z", 0.023, 0.004 * s, 0, seg=32)            # (the rose stepped, 2026-11-03)
    m.cylinder((0, 0, 0.01 * s), "z", 0.0105, 0.004 * s, 0, seg=20)
    m.cylinder((0, 0, 0.014 * s), "z", 0.0085, 0.039 * s, 0, seg=16)
    # The lever: round, tapering from the neck toward its end, the end rounded off.
    zc = 0.053 * s
    n = 8
    for k in range(n):
        x0, x1 = -0.1 * k / n, -0.1 * (k + 1) / n
        r = 0.0092 - 0.0016 * (k / n)
        m.cylinder((x1, 0, zc), "x", r, x0 - x1 + 0.0004, 0, seg=14, caps=(k == n - 1))
    m.cylinder((-0.1, 0, zc), "x", 0.0068, -0.006, 0, seg=14)
    return m.finish(leaf, at=origin)


root = empty("Door")
frame(root)
leaf = empty("Leaf", (HINGE_X, 0, HINGE_Z), root)
slab(leaf)
handle("HandleFront", leaf, False)
handle("HandleBack", leaf, True)
bpy.context.view_layer.update()

objs = [root] + [o for o in bpy.context.scene.objects if o is not root]
bpy.ops.object.select_all(action="DESELECT")
for o in objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = root
path = os.path.join(OUT, "Door.fbx")
bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=False, object_types={"MESH", "EMPTY"},
                         use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False)
for o in objs:
    if o.type == "MESH":
        print("MESH", o.name, "tris", len(o.data.polygons), "parent", o.parent.name if o.parent else None)
print("EXPORTED", path)

if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.world = bpy.data.worlds.new("World")
    scene.world.color = (0.3, 0.31, 0.33)
    scene.render.resolution_x, scene.render.resolution_y = 600, 900
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    for i, (label, angle) in enumerate((("closed", 0.0), ("open", -55.0))):
        leaf.rotation_euler = (0, 0, math.radians(angle))       # Unity +y = Blender +z, same sense; negative opens toward +z
        for view, pos in (("front", (0.6, -4.2, 1.3)), ("back", (-0.9, 4.0, 1.4))):
            cam.location = Vector(pos)
            q = (Vector((0, 0, 1.05)) - cam.location).to_track_quat("-Z", "Y")
            cam.rotation_euler = q.to_euler()
            cam.data.lens = 38
            scene.render.filepath = os.path.join(PREVIEW, f"door_{label}_{view}.png")
            bpy.ops.render.render(write_still=True)
    # The handle close up.
    leaf.rotation_euler = (0, 0, 0)
    hp = B((HINGE_X + SPINDLE[0], SPINDLE[1], HINGE_Z + 0.04))
    cam.location = hp + Vector((0.25, -0.35, 0.12))
    q = (hp - cam.location).to_track_quat("-Z", "Y")
    cam.rotation_euler = q.to_euler()
    cam.data.lens = 50
    scene.render.filepath = os.path.join(PREVIEW, "door_handle.png")
    bpy.ops.render.render(write_still=True)
