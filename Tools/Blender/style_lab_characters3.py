"""Round three of the player's suit (2026-11-06): a fully enclosed hazmat suit after the owner's reference photo (a gas-tight
suit: one big hood with a large window, the breathing set inside a hump on the back, a diagonal zip from the shoulder to
the hip, glove rings at the wrists, baggy legs over the boots), in the softer style of round two, with the wearer's face
seen through the glass.
The hood is a shell with the window cut out of it (so it is hollow behind the glass); the glass is its own piece
(SL_VisorGlass, made transparent in Unity) and a rubber frame runs round the window. Three faces sit inside under the
Neck joint, one to show at a time:
  Face_Plain     a cartoon face: big eyes with a catchlight, eyebrows, a small worried mouth
  Face_FullMask  a full-face respirator under the hood, as in the photo: rubber rim, its own lens, a round filter
  Face_HalfMask  a half mask over the nose and mouth, so the eyes and brows still show
Same joints as the other suits, facing -Y. Run:  blender -b -P Tools/Blender/style_lab_characters3.py"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from style_lab_models import mat, box, cyl, ball, empty, finish, activate, export, reset, tube
from style_lab_characters import mats, seg
from style_lab_characters2 import blob, fold, arm, leg

HOOD_C = Vector((0, 0.03, 1.62))     # hood centre
HOOD_S = (0.275, 0.27, 0.3)          # hood half-sizes
WIN_W, WIN_H, WIN_ZC, WIN_P = 0.175, 0.145, 1.63, 4.0   # the window: a rounded rectangle seen from the front


def win_edge(t, grow=1.0):
    """A point on the window's rounded-rectangle outline (x, z), seen from the front."""
    c, sn = math.cos(t), math.sin(t)
    return (grow * WIN_W * math.copysign(abs(c) ** (2 / WIN_P), c),
            WIN_ZC + grow * WIN_H * math.copysign(abs(sn) ** (2 / WIN_P), sn))


def on_hood(probe, x, z):
    """Where a ray from the front at (x, z) meets the hood's surface (world space), and the surface normal."""
    mi = probe.matrix_world.inverted()
    o = mi @ Vector((x, -2.0, z))
    d = (mi.to_3x3() @ Vector((0, 1, 0))).normalized()
    hit, loc, nrm, _ = probe.ray_cast(o, d)
    mw = probe.matrix_world
    return mw @ loc, (mw.to_3x3() @ nrm).normalized()


def build_window(hood, neck, glass_m, frame_m, n_edge=72, n_ring=8):
    """Cuts the window out of the hood (a pocket reaching back behind the face, so the hood is hollow there), and makes
    the curved glass and the rubber frame along the cut's edge."""
    probe = hood.copy()
    probe.data = hood.data.copy()
    bpy.context.collection.objects.link(probe)
    # the cutter: the outline pushed straight back through the hood
    ts = [2 * math.pi * i / n_edge for i in range(n_edge)]
    front = [Vector((x, -1.0, z)) for x, z in (win_edge(t) for t in ts)]
    back = [Vector((v.x, HOOD_C.y + 0.1, v.z)) for v in front]
    me = bpy.data.meshes.new('Cutter')
    n = n_edge
    faces = [list(range(n))[::-1], list(range(n, 2 * n))]
    faces += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    me.from_pydata([tuple(v) for v in front + back], [], faces)
    me.update()
    me.validate()
    cut = bpy.data.objects.new('Cutter', me)
    bpy.context.collection.objects.link(cut)
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    md = hood.modifiers.new('Window', 'BOOLEAN')
    md.operation = 'DIFFERENCE'
    md.object = cut
    md.solver = 'EXACT'
    activate(hood)
    bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.data.objects.remove(cut, do_unlink=True)
    # the glass: a curved patch on the hood's surface, a little inside it
    verts, fcs = [], []
    for r in range(1, n_ring + 1):
        g = r / n_ring
        for t in ts:
            x, z = win_edge(t, g)
            p, nr = on_hood(probe, x, z)
            verts.append(p - nr * 0.004)
    c, nr = on_hood(probe, 0, WIN_ZC)
    verts.append(c - nr * 0.004)
    ci = len(verts) - 1
    fcs += [(ci, (i + 1) % n, i) for i in range(n)]
    for r in range(n_ring - 1):
        a0, a1 = r * n, (r + 1) * n
        fcs += [(a0 + i, a0 + (i + 1) % n, a1 + (i + 1) % n, a1 + i) for i in range(n)]
    gm = bpy.data.meshes.new('VisorGlass')
    gm.from_pydata([tuple(v) for v in verts], [], fcs)
    gm.update()
    glass = bpy.data.objects.new('VisorGlass', gm)
    bpy.context.collection.objects.link(glass)
    bm = bmesh.new(); bm.from_mesh(gm)
    for f in bm.faces:
        if f.normal.y > 0: f.normal_flip()
    # UVs seen from the front across the window (round ten: for the visor's fog and glare picture)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            co = l.vert.co
            l[uv].uv = ((co.x + WIN_W) / (2 * WIN_W), (co.z - (WIN_ZC - WIN_H)) / (2 * WIN_H))
    bm.to_mesh(gm); bm.free()
    finish(glass, glass_m, 0, smooth=80, parent=neck)
    # the frame along the edge
    edge = []
    for t in ts + [ts[0]]:
        x, z = win_edge(t, 1.0)
        p, nr = on_hood(probe, x, z)
        edge.append(tuple(p + nr * 0.002))
    fr = tube('WindowFrame', edge, 0.017, frame_m, parent=neck)
    bpy.data.objects.remove(probe, do_unlink=True)
    return glass, fr


def face(kind, neck, M, skin, F):
    """The wearer's head and face inside the hood, under an empty named Face_<kind>."""
    grp = empty('Face_' + kind, (0, 0, 1.6), neck)
    K, E, H, G, O, R = M['K'], F['eye'], F['hair'], M['G'], M['O'], M['R']
    hc = Vector((0, -0.045, 1.6))
    ball('Head', 0.135, hc, skin, (1.0, 0.95, 1.08), 20, 14, parent=grp)
    ball('Hair', 0.14, hc + Vector((0, 0.025, 0.035)), H, (1.0, 0.95, 1.02), 20, 14, parent=grp)
    fy = hc.y - 0.128   # front of the face
    eyes = kind != 'FullMask'
    for sx in (-1, 1):
        if kind != 'FullMask':
            ball('Eye', 0.026, (sx * 0.048, fy + 0.012, 1.625), E, (0.85, 0.5, 1.15), 12, 8, parent=grp)
            ball('Catchlight', 0.0075, (sx * 0.048 + 0.008, fy - 0.0015, 1.638), R, (1, 1, 1), 8, 6, parent=grp)
            b = box('Brow', (0.042, 0.012, 0.011), (sx * 0.05, fy + 0.016, 1.672), H, 0.004, 1, parent=grp)
            b.rotation_euler = (0, math.radians(-sx * 12), 0)
        ball('Ear', 0.03, (sx * 0.133, hc.y + 0.01, 1.6), skin, (0.6, 1, 1), 10, 6, parent=grp)
    if kind == 'Plain':
        ball('Nose', 0.02, (0, fy - 0.004, 1.595), skin, (1, 0.9, 1), 10, 6, parent=grp)
        ball('Mouth', 0.014, (0, fy + 0.012, 1.545), E, (1.0, 0.5, 0.75), 10, 6, parent=grp)
        for sx in (-1, 1):
            ball('Blush', 0.02, (sx * 0.075, fy + 0.02, 1.585), F['blush'], (1, 0.3, 0.6), 10, 6, parent=grp)
    elif kind == 'HalfMask':
        blob('HalfMask', (0.085, 0.06, 0.055), (0, fy - 0.005, 1.545), K, 0.7, parent=grp, bend=3.0)
        for sx in (-1, 1):
            cyl('MaskFilter', 0.03, 0.03, (sx * 0.06, fy - 0.035, 1.53), O, 14,
                rot=(math.radians(90), 0, math.radians(sx * 35)), parent=grp)
            tube('MaskStrap', [(sx * 0.08, fy + 0.04, 1.56), (sx * 0.13, hc.y, 1.6)], 0.006, K, grp)
    else:   # FullMask
        blob('MaskRim', (0.11, 0.06, 0.13), (0, fy + 0.01, 1.6), K, 0.6, parent=grp, bend=2.5)
        blob('MaskLens', (0.088, 0.03, 0.06), (0, fy - 0.03, 1.635), F['glass'], 0.5, parent=grp, bend=3.0)
        for sx in (-1, 1):
            ball('Eye', 0.022, (sx * 0.042, fy + 0.008, 1.632), E, (0.85, 0.5, 1.15), 12, 8, parent=grp)
            ball('Catchlight', 0.006, (sx * 0.042 + 0.007, fy - 0.004, 1.643), R, (1, 1, 1), 8, 6, parent=grp)
        cyl('MaskFilter', 0.042, 0.035, (0, fy - 0.045, 1.53), G, 16, rot=(math.radians(100), 0, 0), parent=grp)
        cyl('FilterCap', 0.034, 0.012, (0, fy - 0.065, 1.527), O, 16, rot=(math.radians(100), 0, 0), parent=grp)
    return grp


def build_suit():
    reset()
    M = mats()
    S, K, G, O, W, D, R = M['S'], M['K'], M['G'], M['O'], M['W'], M['D'], M['R']
    F = dict(eye=mat('SL_Eye', '#121010', 0.15), hair=mat('SL_Hair', '#3A2618', 0.8),
             blush=mat('SL_Blush', '#E07A6A', 0.8), glass=mat('SL_VisorGlass', '#B8D4DA', 0.03))
    skins = [mat('SL_SkinA', '#E9B892', 0.7), mat('SL_SkinB', '#B97A55', 0.7), mat('SL_SkinC', '#7A4E33', 0.7)]
    root = empty('HazmatSuit', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.8), root)
    blob('Hips', (0.27, 0.22, 0.15), (0, 0, 0.84), S, 0.78, parent=pelvis)
    chest = empty('Chest', (0, 0, 0.92), pelvis)
    torso = blob('Torso', (0.32, 0.25, 0.36), (0, 0, 1.13), S, 0.8, 0.06, parent=chest)
    # the breathing set inside a hump on the back, with the company's round badge on its side
    blob('Hump', (0.29, 0.2, 0.36), (0, 0.27, 1.2), S, 0.72, 0.05, parent=chest)
    cyl('Badge', 0.062, 0.02, (-0.29, 0.27, 1.25), D, 24, rot=(0, math.radians(90), 0), parent=chest)
    for i, sm in enumerate(M['st']):
        box('BadgeStripe%d' % i, (0.008, 0.075, 0.011), (-0.3, 0.27, 1.267 - i * 0.017), sm, parent=chest)
    # the diagonal gas-tight zip from the left shoulder to the right hip, the company stripe running beside it
    diag = (0, math.radians(-58), 0)
    nrm = Vector((-math.sin(math.radians(58)), 0, math.cos(math.radians(58))))   # the band's normal
    def front_band(name, src, m, off, w, inflate):
        me = src.data.copy()
        o = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(o)
        o.matrix_world = src.matrix_world.copy()
        for v in me.vertices:
            v.co += v.normal * inflate
        c = src.matrix_world.translation
        bpy.ops.mesh.primitive_cube_add(size=1, location=(c.x + nrm.x * off, c.y - 0.3, c.z + nrm.z * off), rotation=diag)
        cut = bpy.context.active_object
        cut.scale = (3, 0.6, w)
        md = o.modifiers.new('Cut', 'BOOLEAN')
        md.operation = 'INTERSECT'
        md.object = cut
        md.solver = 'EXACT'
        activate(o)
        bpy.ops.object.modifier_apply(modifier=md.name)
        bpy.data.objects.remove(cut, do_unlink=True)
        return finish(o, m, 0, smooth=70, parent=chest)
    front_band('Zip', torso, K, 0.0, 0.03, 0.008)
    front_band('ZipTape', torso, D, -0.07, 0.1, 0.005)
    for i, sm in enumerate(M['st']):
        front_band('ZipStripe%d' % i, torso, sm, -0.045 - i * 0.028, 0.02, 0.01)
    cyl('ExhaleValve', 0.04, 0.035, (-0.21, -0.215, 1.27), K, 16, rot=(math.radians(80), 0, 0), parent=chest)
    cyl('ValveCap', 0.028, 0.02, (-0.212, -0.24, 1.272), G, 16, rot=(math.radians(80), 0, 0), parent=chest)

    neck = empty('Neck', (0, 0, 1.45), chest)
    blob('HoodDrape', (0.31, 0.27, 0.12), (0, 0.04, 1.42), S, 0.8, parent=neck)
    # the hood: a shell with the window cut out of it
    hood = blob('Hood', HOOD_S, HOOD_C, S, 0.82, -0.06, segs=48, rings=32, parent=neck)
    build_window(hood, neck, F['glass'], K)
    for kind, skin in (('Plain', skins[0]), ('FullMask', skins[1]), ('HalfMask', skins[2])):
        face(kind, neck, M, skin, F)

    for side, sx in (('L', 1), ('R', -1)):
        sh = arm(side, sx, chest, M, (sx * 0.32, 0, 1.36), 0.26, 0.24, 15, 0.11, S, (0.082, 0.07, 0.092), bell=1.15)
        # the glove ring (where the glove locks into the sleeve)
        el = next(o for o in bpy.data.objects if o.name.startswith('Elbow' + side))
        wr = next(o for o in bpy.data.objects if o.name.startswith('Wrist' + side))
        e, w = el.matrix_world.translation, wr.matrix_world.translation
        d = (w - e).normalized()
        seg('GloveRing' + side, w - d * 0.03, w + d * 0.012, 0.122, 0.122, K, 20, el)
        kn = leg(side, sx, pelvis, M, 0.78, 0.45, 0.16, 0.15, S, (0.12, 0.175, 0.09), flare=1.1, toe=False)
        # the suit's leg comes down over the boot
        seg('Overboot' + side, (sx * 0.13, 0, 0.2), (sx * 0.13, -0.01, 0.1), 0.142, 0.155, S, 20, kn)
    export('HazmatSuit')


if __name__ == '__main__':
    build_suit()
    print('done')
