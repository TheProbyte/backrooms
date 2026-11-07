"""Round five of the player's suit (2026-11-06). Round four read a little tall, and its faces needed work: this one is
about five heads tall (between round three's toy and round four's), and has new faces, still simple but each a person.
The faces follow the usual advice for simple stylised faces: the eyes and brows carry the expression, a few details
make each one someone, and nothing else.
Faces (one shows, under the Neck joint):
  Face_Worker   tired: heavy upper lids, flat brows, a moustache, short hair
  Face_Rookie   nervous: open eyes with a shine, raised brows, freckles, a side-swept fringe
  Face_Veteran  steady: square glasses, thick brows, a firm mouth, a receding hairline
Masks (none, or one, over any face):
  Mask_Half     a half mask over the nose and mouth with two filters
  Mask_Full     a full-face mask: a rubber frame round its own lens, a chin cup and a front filter
The player picks the face, the mask and the skin tone (HazmatAppearance in Unity).
Run:  blender -b -P Tools/Blender/style_lab_characters5.py"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from style_lab_models import mat, box, cyl, ball, empty, finish, activate, export, reset, tube
from style_lab_characters import mats, seg
from style_lab_characters2 import blob, fold, arm, leg, company_band
import style_lab_characters3 as S3

Z = 1.515                                    # hood centre height (the suit is about 1.72 m)
S3.HOOD_C = Vector((0, 0.02, Z))
S3.HOOD_S = (0.175, 0.18, 0.205)
S3.WIN_W, S3.WIN_H, S3.WIN_ZC, S3.WIN_P = 0.114, 0.094, Z + 0.003, 4.0
HC = Vector((0, -0.025, Z - 0.012))          # the head inside
FY = HC.y - 0.088                            # the front of the face


def feature_eye(grp, sx, E, R, skin, lid=0.0, size=1.0, shine=True, z=0.012, x=0.034):
    """An eye; lid > 0 makes it half-closed: flatter, with a dark lash line along its top (no separate lid shape, which
    the outline pass would draw as a ball of its own)."""
    p = Vector((sx * x, FY + 0.006, HC.z + z))
    h = 1.25 * (1 - 0.45 * lid)
    ball('Eye', 0.0125 * size, p, E, (0.75, 0.45, h), 12, 8, parent=grp)
    if shine:
        ball('Catchlight', 0.0036 * size, p + Vector((0.004, -0.004, 0.004 * size * h)), R, (1, 1, 1), 8, 6, parent=grp)
    if lid > 0:
        top = p + Vector((0, -0.002, 0.0125 * size * h * 0.8))
        blob('Lash', (0.0125 * size, 0.004, 0.0028), top, E, 0.6, parent=grp, bend=8.0)
    return p


def brow(grp, sx, H, z, tilt, thick=1.0, x=0.035):
    b = blob('Brow', (0.019, 0.006, 0.0055 * thick), (sx * x, FY + 0.003, HC.z + z), H, 0.6, parent=grp)
    b.rotation_euler = (0, math.radians(-sx * tilt), 0)
    return b


def face_decal(name, head, m, grp, x0=-0.06, z_lo=-0.07, size=0.12, lift=0.0008):
    """The painted face (eyes, mouth): a thin copy of the front of the head, lifted just off it, with UVs projected
    straight from the front over a size x size square (x from x0, z from z_lo below the head centre), the same square
    face_textures.py paints. Flat features stay undistorted that way, and work on any skin tone."""
    me = head.data.copy()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.matrix_world = head.matrix_world.copy()
    mw, nm = o.matrix_world, o.matrix_world.to_3x3()
    z0 = HC.z + z_lo
    bm = bmesh.new()
    bm.from_mesh(me)
    def keep(f):
        c, n = mw @ f.calc_center_median(), (nm @ f.normal).normalized()
        return n.y < -0.2 and abs(c.x - (x0 + size / 2)) < size / 2 + 0.01 and z0 - 0.01 < c.z < z0 + size + 0.01
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep(f)], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * lift
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            w = mw @ l.vert.co
            l[uv].uv = ((w.x - x0) / size, (w.z - z0) / size)
    bm.to_mesh(me)
    bm.free()
    o.data.materials.clear()
    o.data.materials.append(m)
    mwc = o.matrix_world.copy()
    o.parent = grp
    o.matrix_world = mwc
    return o


def build_face(kind, neck, M, F, skin):
    grp = empty('Face_' + kind, HC, neck)
    K, E, H, R = M['K'], F['eye'], F['hair'], M['R']
    head = blob('Head', (0.092, 0.088, 0.108), HC, skin, 0.82, segs=40, rings=26, parent=grp)
    for sx in (-1, 1):
        ball('Ear', 0.02, (sx * 0.091, HC.y + 0.008, HC.z - 0.004), skin, (0.55, 1, 1.15), 10, 6, parent=grp)
    blob('Nose', (0.012, 0.014, 0.017), (0, FY - 0.006, HC.z - 0.012), skin, 0.75, parent=grp)
    if kind == 'Worker':
        blob('Hair', (0.094, 0.09, 0.085), HC + Vector((0, 0.012, 0.035)), H, 0.8, parent=grp)
        # eyes and mouth painted on (face_textures.py); nose, brows and moustache stay 3D
        face_decal('FacePaint', head, F['paint_worker'], grp)
        for sx in (-1, 1):
            brow(grp, sx, H, 0.033, 4, 0.85)
        blob('Moustache', (0.03, 0.008, 0.008), (0, FY - 0.001, HC.z - 0.034), H, 0.6, parent=grp, bend=6.0)
    elif kind == 'Rookie':
        blob('Hair', (0.095, 0.09, 0.082), HC + Vector((0, 0.014, 0.04)), H, 0.8, parent=grp)
        # the fringe swept to one side
        for i, (x, zz, rz) in enumerate(((-0.045, 0.062, 30), (-0.015, 0.068, 22), (0.018, 0.07, 14))):
            f = blob('Fringe', (0.026, 0.014, 0.012), (x, FY + 0.018, HC.z + zz), H, 0.7, parent=grp)
            f.rotation_euler = (0, math.radians(rz), 0)
        for sx in (-1, 1):
            feature_eye(grp, sx, E, R, skin, size=1.15)
            brow(grp, sx, H, 0.04, -12, 0.9)
            for j, (dx, dz) in enumerate(((0.035, -0.014), (0.047, -0.01), (0.041, -0.022))):
                ball('Freckle', 0.0028, (sx * dx, FY + 0.006, HC.z + dz), F['freckle'], (1, 0.4, 1), 6, 4, parent=grp)
        ball('Mouth', 0.007, (0, FY + 0.006, HC.z - 0.048), E, (1.2, 0.45, 0.9), 10, 6, parent=grp)
    else:   # Veteran
        blob('Hair', (0.094, 0.09, 0.07), HC + Vector((0, 0.03, 0.045)), H, 0.8, parent=grp)
        for sx in (-1, 1):
            feature_eye(grp, sx, E, R, skin, lid=0.25, shine=True)
            brow(grp, sx, H, 0.033, 6, 1.7)
            # square glasses: a frame round each eye
            c = Vector((sx * 0.034, FY - 0.006, HC.z + 0.01))
            w, h = 0.021, 0.016
            pts = [c + Vector((dx, 0, dz)) for dx, dz in ((-w, -h), (w, -h), (w, h), (-w, h), (-w, -h))]
            tube('GlassesRim', [tuple(p) for p in pts], 0.0025, K, grp)
            tube('GlassesArm', [tuple(c + Vector((sx * w, 0, h * 0.6))), (sx * 0.088, HC.y - 0.01, HC.z + 0.016)], 0.002, K, grp)
        tube('GlassesBridge', [(-0.013, FY - 0.008, HC.z + 0.014), (0.013, FY - 0.008, HC.z + 0.014)], 0.0022, K, grp)
        box('Mouth', (0.024, 0.005, 0.0045), (0, FY + 0.005, HC.z - 0.046), E, 0.0015, 1, parent=grp)
    return grp


def build_masks(neck, M, F):
    K, G, O = M['K'], M['G'], M['O']
    half = empty('Mask_Half', HC, neck)
    blob('HalfMask', (0.064, 0.044, 0.04), (0, FY - 0.006, HC.z - 0.04), K, 0.7, parent=half, bend=4.5)
    for sx in (-1, 1):
        cyl('MaskFilter', 0.022, 0.024, (sx * 0.045, FY - 0.028, HC.z - 0.05), O, 14,
            rot=(math.radians(90), 0, math.radians(sx * 35)), parent=half)
        tube('MaskStrap', [(sx * 0.06, FY + 0.03, HC.z - 0.03), (sx * 0.09, HC.y, HC.z - 0.01)], 0.004, K, half)
    full = empty('Mask_Full', HC, neck)
    # its own lens over the eyes, framed in rubber, so the eyes still show
    lc, lw, lh = Vector((0, FY - 0.02, HC.z + 0.012)), 0.06, 0.034
    blob('MaskLens', (lw, 0.01, lh), lc, F['glass'], 0.45, parent=full, bend=4.0)
    pts = []
    for i in range(41):
        t = 2 * math.pi * i / 40
        c, s = math.cos(t), math.sin(t)
        x = lw * math.copysign(abs(c) ** 0.5, c)
        zz = lh * math.copysign(abs(s) ** 0.5, s)
        pts.append((x, lc.y + 4.0 * x * x, lc.z + zz))
    tube('MaskFrame', pts, 0.008, K, full)
    blob('ChinCup', (0.062, 0.045, 0.042), (0, FY - 0.006, HC.z - 0.045), K, 0.7, parent=full, bend=4.5)
    for sx in (-1, 1):
        tube('MaskSide', [(sx * lw, lc.y + 4.0 * lw * lw, lc.z), (sx * 0.09, HC.y, HC.z)], 0.007, K, full)
    cyl('MaskFilter', 0.028, 0.024, (0, FY - 0.04, HC.z - 0.055), G, 16, rot=(math.radians(100), 0, 0), parent=full)
    cyl('FilterCap', 0.022, 0.01, (0, FY - 0.054, HC.z - 0.057), O, 16, rot=(math.radians(100), 0, 0), parent=full)


def build_suit():
    reset()
    M = mats()
    S, K, G, O, W, D, R = M['S'], M['K'], M['G'], M['O'], M['W'], M['D'], M['R']
    F = dict(eye=mat('SL_Eye', '#121010', 0.15), hair=mat('SL_Hair', '#3A2618', 0.8),
             glass=mat('SL_VisorGlass', '#B8D4DA', 0.03), freckle=mat('SL_Freckle', '#9A5A3C', 0.8),
             paint_worker=mat('SL_FacePaint_Worker', '#FFFFFF', 0.8))
    skin = mat('SL_SkinA', '#E9B892', 0.7)
    root = empty('HazmatSuit3', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.84), root)
    blob('Hips', (0.185, 0.145, 0.14), (0, 0, 0.89), S, 0.75, parent=pelvis)
    chest = empty('Chest', (0, 0, 0.93), pelvis)
    torso = blob('Torso', (0.235, 0.17, 0.265), (0, 0, 1.14), S, 0.72, 0.1, parent=chest)
    blob('Hump', (0.19, 0.12, 0.23), (0, 0.2, 1.17), S, 0.72, 0.05, parent=chest)
    cyl('Badge', 0.045, 0.016, (-0.196, 0.2, 1.2), D, 24, rot=(0, math.radians(90), 0), parent=chest)
    for i, sm in enumerate(M['st']):
        box('BadgeStripe%d' % i, (0.008, 0.055, 0.009), (-0.205, 0.2, 1.215 - i * 0.013), sm, parent=chest)
    company_band('Chest', torso, M, 1.24, chest, width=0.095)
    # the diagonal zip, a plain dark line
    diag = (0, math.radians(-58), 0)
    me = torso.data.copy()
    z = bpy.data.objects.new('Zip', me)
    bpy.context.collection.objects.link(z)
    z.matrix_world = torso.matrix_world.copy()
    for v in me.vertices:
        v.co += v.normal * 0.007
    c = torso.matrix_world.translation
    bpy.ops.mesh.primitive_cube_add(size=1, location=(c.x, c.y - 0.25, c.z), rotation=diag)
    cut = bpy.context.active_object
    cut.scale = (3, 0.5, 0.022)
    md = z.modifiers.new('Cut', 'BOOLEAN')
    md.operation, md.object, md.solver = 'INTERSECT', cut, 'EXACT'
    activate(z)
    bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.data.objects.remove(cut, do_unlink=True)
    finish(z, K, 0, smooth=70, parent=chest)
    cyl('ExhaleValve', 0.03, 0.028, (-0.13, -0.17, 1.09), K, 16, rot=(math.radians(95), 0, 0), parent=chest)
    cyl('ValveCap', 0.021, 0.016, (-0.13, -0.19, 1.09), G, 16, rot=(math.radians(95), 0, 0), parent=chest)

    neck = empty('Neck', (0, 0, 1.4), chest)
    blob('HoodDrape', (0.205, 0.18, 0.065), (0, 0.02, 1.385), S, 0.8, parent=neck)
    hood = blob('Hood', S3.HOOD_S, S3.HOOD_C, S, 0.82, -0.05, segs=48, rings=32, parent=neck)
    S3.build_window(hood, neck, F['glass'], K)
    for kind in ('Worker', 'Rookie', 'Veteran'):
        build_face(kind, neck, M, F, skin)
    build_masks(neck, M, F)

    for side, sx in (('L', 1), ('R', -1)):
        arm(side, sx, chest, M, (sx * 0.255, 0, 1.355), 0.255, 0.235, 10, 0.074, S, (0.052, 0.044, 0.066), bell=1.1,
            stripes_at=0.62)
        el = next(o for o in bpy.data.objects if o.name.startswith('Elbow' + side))
        wr = next(o for o in bpy.data.objects if o.name.startswith('Wrist' + side))
        e, w = el.matrix_world.translation, wr.matrix_world.translation
        d = (w - e).normalized()
        seg('GloveRing' + side, w - d * 0.025, w + d * 0.01, 0.082, 0.082, K, 20, el)
        kn = leg(side, sx, pelvis, M, 0.82, 0.45, 0.11, 0.102, S, (0.078, 0.135, 0.06), flare=1.1, toe=False, hip_x=0.1)
        seg('Overboot' + side, (sx * 0.1, 0, 0.16), (sx * 0.1, -0.01, 0.075), 0.104, 0.114, S, 20, kn)
    export('HazmatSuit3')


if __name__ == '__main__':
    build_suit()
    print('done')
