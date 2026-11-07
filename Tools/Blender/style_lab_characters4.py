"""Round four of the player's suit (2026-11-06): the enclosed suit of round three made less toy-like. It is about six
heads tall instead of three: a smaller hood, window and face, a longer body and limbs, narrower hips, a smaller hump.
The face is smaller and plainer (smaller eyes, no blush). The company stripe is no longer diagonal: it runs as a band
round the chest and round both upper arms, like the reflective tape on real protective suits. The zip stays diagonal,
as in the reference photo.
The three faces stay in the model under the Neck joint (Face_Plain, Face_FullMask, Face_HalfMask): the player chooses
one (HazmatAppearance in Unity shows it and sets the skin tone).
Run:  blender -b -P Tools/Blender/style_lab_characters4.py"""
import bpy, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from style_lab_models import mat, box, cyl, ball, empty, finish, activate, export, reset, tube
from style_lab_characters import mats, seg
from style_lab_characters2 import blob, fold, arm, leg, company_band
import style_lab_characters3 as S3

# the hood and its window, smaller than round three's (read by S3.build_window)
S3.HOOD_C = Vector((0, 0.02, 1.66))
S3.HOOD_S = (0.165, 0.175, 0.2)
S3.WIN_W, S3.WIN_H, S3.WIN_ZC, S3.WIN_P = 0.108, 0.088, 1.665, 4.0


def face(kind, neck, M, skin, F):
    grp = empty('Face_' + kind, (0, 0, 1.65), neck)
    K, E, H, G, O, R = M['K'], F['eye'], F['hair'], M['G'], M['O'], M['R']
    hc = Vector((0, -0.025, 1.65))
    ball('Head', 0.098, hc, skin, (1.0, 0.95, 1.1), 20, 14, parent=grp)
    ball('Hair', 0.102, hc + Vector((0, 0.018, 0.026)), H, (1.0, 0.95, 1.02), 20, 14, parent=grp)
    fy = hc.y - 0.093
    for sx in (-1, 1):
        if kind != 'FullMask':
            ball('Eye', 0.016, (sx * 0.035, fy + 0.008, 1.668), E, (0.8, 0.5, 1.1), 12, 8, parent=grp)
            ball('Catchlight', 0.0045, (sx * 0.035 + 0.005, fy - 0.001, 1.676), R, (1, 1, 1), 8, 6, parent=grp)
            b = box('Brow', (0.03, 0.009, 0.008), (sx * 0.036, fy + 0.011, 1.698), H, 0.003, 1, parent=grp)
            b.rotation_euler = (0, math.radians(-sx * 8), 0)
        ball('Ear', 0.022, (sx * 0.097, hc.y + 0.008, 1.65), skin, (0.6, 1, 1), 10, 6, parent=grp)
    if kind == 'Plain':
        ball('Nose', 0.014, (0, fy - 0.003, 1.642), skin, (1, 0.9, 1.1), 10, 6, parent=grp)
        box('Mouth', (0.022, 0.006, 0.005), (0, fy + 0.008, 1.608), E, 0.002, 1, parent=grp)
    elif kind == 'HalfMask':
        blob('HalfMask', (0.062, 0.045, 0.04), (0, fy - 0.004, 1.615), K, 0.7, parent=grp, bend=4.0)
        for sx in (-1, 1):
            cyl('MaskFilter', 0.022, 0.024, (sx * 0.044, fy - 0.026, 1.605), O, 14,
                rot=(math.radians(90), 0, math.radians(sx * 35)), parent=grp)
            tube('MaskStrap', [(sx * 0.058, fy + 0.03, 1.625), (sx * 0.095, hc.y, 1.65)], 0.004, K, grp)
    else:   # FullMask
        blob('MaskRim', (0.08, 0.045, 0.095), (0, fy + 0.008, 1.655), K, 0.6, parent=grp, bend=3.5)
        blob('MaskLens', (0.064, 0.022, 0.044), (0, fy - 0.022, 1.68), F['glass'], 0.5, parent=grp, bend=4.0)
        # the skin and eyes seen through the mask's lens (in front of the rim, inside the lens)
        blob('MaskSkin', (0.056, 0.008, 0.036), (0, fy - 0.034, 1.68), skin, 0.6, parent=grp, bend=4.0)
        for sx in (-1, 1):
            ball('Eye', 0.013, (sx * 0.026, fy - 0.04, 1.68), E, (0.8, 0.4, 1.1), 12, 8, parent=grp)
            ball('Catchlight', 0.0038, (sx * 0.026 + 0.004, fy - 0.044, 1.686), R, (1, 1, 1), 8, 6, parent=grp)
        cyl('MaskFilter', 0.03, 0.026, (0, fy - 0.032, 1.6), G, 16, rot=(math.radians(100), 0, 0), parent=grp)
        cyl('FilterCap', 0.024, 0.01, (0, fy - 0.047, 1.598), O, 16, rot=(math.radians(100), 0, 0), parent=grp)
    return grp


def build_suit():
    reset()
    M = mats()
    S, K, G, O, W, D, R = M['S'], M['K'], M['G'], M['O'], M['W'], M['D'], M['R']
    F = dict(eye=mat('SL_Eye', '#121010', 0.15), hair=mat('SL_Hair', '#3A2618', 0.8),
             glass=mat('SL_VisorGlass', '#B8D4DA', 0.03))
    skins = [mat('SL_SkinA', '#E9B892', 0.7), mat('SL_SkinB', '#B97A55', 0.7), mat('SL_SkinC', '#7A4E33', 0.7)]
    root = empty('HazmatSuit2', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.95), root)
    blob('Hips', (0.18, 0.14, 0.15), (0, 0, 1.0), S, 0.75, parent=pelvis)
    chest = empty('Chest', (0, 0, 1.05), pelvis)
    torso = blob('Torso', (0.23, 0.165, 0.29), (0, 0, 1.27), S, 0.72, 0.1, parent=chest)
    blob('Hump', (0.19, 0.12, 0.25), (0, 0.2, 1.3), S, 0.72, 0.05, parent=chest)
    cyl('Badge', 0.045, 0.016, (-0.196, 0.2, 1.33), D, 24, rot=(0, math.radians(90), 0), parent=chest)
    for i, sm in enumerate(M['st']):
        box('BadgeStripe%d' % i, (0.008, 0.055, 0.009), (-0.205, 0.2, 1.345 - i * 0.013), sm, parent=chest)
    # the company stripe: a band round the chest (the arms get theirs below)
    company_band('Chest', torso, M, 1.37, chest, width=0.095)
    # the diagonal gas-tight zip from the left shoulder to the right hip (no stripe beside it)
    diag = (0, math.radians(-58), 0)
    nrm = Vector((-math.sin(math.radians(58)), 0, math.cos(math.radians(58))))
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
    cyl('ExhaleValve', 0.03, 0.028, (-0.13, -0.165, 1.22), K, 16, rot=(math.radians(95), 0, 0), parent=chest)
    cyl('ValveCap', 0.021, 0.016, (-0.13, -0.185, 1.22), G, 16, rot=(math.radians(95), 0, 0), parent=chest)

    neck = empty('Neck', (0, 0, 1.55), chest)
    blob('HoodDrape', (0.2, 0.175, 0.065), (0, 0.02, 1.525), S, 0.8, parent=neck)
    hood = blob('Hood', S3.HOOD_S, S3.HOOD_C, S, 0.82, -0.05, segs=48, rings=32, parent=neck)
    S3.build_window(hood, neck, F['glass'], K)
    for kind, skin in (('Plain', skins[0]), ('FullMask', skins[1]), ('HalfMask', skins[2])):
        face(kind, neck, M, skin, F)

    for side, sx in (('L', 1), ('R', -1)):
        arm(side, sx, chest, M, (sx * 0.25, 0, 1.49), 0.29, 0.27, 9, 0.072, S, (0.05, 0.042, 0.065), bell=1.1,
            stripes_at=0.62)
        el = next(o for o in bpy.data.objects if o.name.startswith('Elbow' + side))
        wr = next(o for o in bpy.data.objects if o.name.startswith('Wrist' + side))
        e, w = el.matrix_world.translation, wr.matrix_world.translation
        d = (w - e).normalized()
        seg('GloveRing' + side, w - d * 0.025, w + d * 0.01, 0.08, 0.08, K, 20, el)
        kn = leg(side, sx, pelvis, M, 0.92, 0.5, 0.12, 0.098, S, (0.075, 0.13, 0.06), flare=1.1, toe=False, hip_x=0.1)
        seg('Overboot' + side, (sx * 0.1, 0, 0.17), (sx * 0.1, -0.01, 0.075), 0.1, 0.11, S, 20, kn)
    export('HazmatSuit2')


if __name__ == '__main__':
    build_suit()
    print('done')
