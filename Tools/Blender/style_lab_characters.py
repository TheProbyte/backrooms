"""Three proof-of-concept versions of the player's hazmat suit for the style lab (2026-11-06), all in the company's
orange-yellow with its triple stripe (orange, gold, avocado on a dark band, as on the work camcorder), each a different
shape:
  HazmatSoft   rounded and friendly: one soft body, a big round hood with a wide visor, mitten hands
  HazmatCrew   a worker's proportions, faceted low-poly and flat shaded, a baggy suit, a square visor, stripes on the arms
  HazmatStout  short and chunky like a toy: a big head with a round porthole visor, a big backpack, big hands and boots
Every version keeps the same joints as the first Hazmat (Pelvis/Chest/Neck, Shoulder/Elbow/Wrist, Hip/Knee), with a ball
at each bending joint so nothing splits when posed. Faces -Y (forward in Unity).
Run headless:  blender -b -P Tools/Blender/style_lab_characters.py"""
import bpy, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import style_lab_models as L
from style_lab_models import mat, box, cyl, ball, empty, finish, activate, export, reset

SUIT_HEX = '#F0A21E'


def mats():
    return dict(
        S=mat('SL_SuitAmber', SUIT_HEX, 0.6), K=mat('SL_Rubber', '#1E1C1A', 0.8), V=mat('SL_Visor', '#1A2226', 0.08, 0.3),
        G=mat('SL_FilterGrey', '#8A8A84', 0.5), O=mat('SL_PatchOrange', '#D9622B', 0.7), W=mat('SL_TankWhite', '#DCD8CC', 0.4),
        D=mat('SL_CamDark', '#2B1C14', 0.6), R=mat('SL_Reflect', '#E9E4D2', 0.35),
        st=[mat('SL_StripeOrange', '#D9622B', 0.5), mat('SL_StripeGold', '#E1A73A', 0.5), mat('SL_StripeAvo', '#8A9A3B', 0.5)])


def seg(name, p0, p1, r0, r1, m, verts=10, parent=None, smooth=50, spin=0.0):
    """A tapered limb piece from p0 (radius r0) to p1 (radius r1)."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    q = d.normalized().to_track_quat('Z', 'Y')
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1, depth=d.length, location=(a + b) / 2,
                                    rotation=q.to_euler())
    o = bpy.context.active_object
    o.name = name
    if spin:
        o.rotation_mode = 'QUATERNION'
        from mathutils import Quaternion
        o.rotation_quaternion = q @ Quaternion((0, 0, 1), spin)
    return finish(o, m, 0, 2, smooth, parent=parent)


def prism(name, r0, r1, z0, z1, sy, m, verts=8, parent=None, smooth=1, loc=(0, 0), spin=math.radians(22.5)):
    """A tapered body piece (bottom radius r0 at z0, top r1 at z1), flattened front to back by sy."""
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1, depth=z1 - z0,
                                    location=(loc[0], loc[1], (z0 + z1) / 2), rotation=(0, 0, spin))
    o = bpy.context.active_object
    o.name = name
    o.scale = (1, sy, 1)
    activate(o)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish(o, m, 0, 2, smooth, parent=parent)


def limb_stripes(tag, p0, p1, r, t, M, parent, verts=10, smooth=50, spin=0.0):
    """The company stripe as rings round a limb, centred at fraction t along p0 -> p1."""
    a, b = Vector(p0), Vector(p1)
    d = (b - a).normalized()
    c = a + (b - a) * t
    w = 0.024
    seg(tag + 'StripeBand', c - d * (w * 2.2), c + d * (w * 2.2), r + 0.006, r + 0.006, M['D'], verts, parent, smooth, spin)
    for i, sm in enumerate(M['st']):
        cc = c + d * (i - 1) * w * 1.25
        seg(tag + 'Stripe%d' % i, cc - d * w * 0.5, cc + d * w * 0.5, r + 0.011, r + 0.011, sm, verts, parent, smooth, spin)


def arm_points(sx, sh, upper, lower, splay):
    a = math.radians(splay)
    dv = Vector((sx * math.sin(a), 0, -math.cos(a)))
    s = Vector(sh)
    e = s + dv * upper
    w = e + dv * lower
    return s, e, w


# ------------------------------------------------------------------------------------------------ 1. soft
def build_soft():
    reset()
    M = mats()
    S, K, V, G, O, W, D = M['S'], M['K'], M['V'], M['G'], M['O'], M['W'], M['D']
    root = empty('HazmatSoft', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.84), root)
    chest = empty('Chest', (0, 0, 0.95), pelvis)
    # one soft body from hips to shoulders
    ball('Body', 1.0, (0, 0, 1.12), S, (0.31, 0.25, 0.42), 18, 12, parent=chest)
    ball('Seat', 1.0, (0, 0.01, 0.86), S, (0.27, 0.22, 0.14), 16, 8, parent=pelvis)
    # the company band round the chest: dark band, three stripes
    def ring_r(z):
        k = max(0.0, 1 - ((z - 1.12) / 0.42) ** 2) ** 0.5
        return 0.31 * k
    zc = 1.2
    r = ring_r(zc)
    cyl('ChestBand', r + 0.008, 0.11, (0, 0, zc), D, 24, parent=chest).scale = (1, 0.25 / 0.31, 1)
    for i, sm in enumerate(M['st']):
        z = zc + (i - 1) * 0.03
        cyl('ChestStripe%d' % i, r + 0.014, 0.022, (0, 0, z), sm, 24, parent=chest).scale = (1, 0.25 / 0.31, 1)
    box('Zip', (0.018, 0.01, 0.3), (0, -0.248, 1.0), K, parent=chest)
    cyl('Belt', 0.29, 0.05, (0, 0, 0.9), K, 24, parent=pelvis).scale = (1, 0.23 / 0.29, 1)
    # the tank: a small white capsule on the back
    seg('Tank', (0, 0.27, 0.98), (0, 0.27, 1.32), 0.085, 0.085, W, 12, chest)
    ball('TankTop', 0.085, (0, 0.27, 1.32), W, (1, 1, 0.6), 12, 6, parent=chest)
    ball('TankBottom', 0.085, (0, 0.27, 0.98), W, (1, 1, 0.6), 12, 6, parent=chest)
    # the hood sits straight on the body (no neck), a wide visor with a dark rim, a small snout
    neck = empty('Neck', (0, 0, 1.46), chest)
    ball('Hood', 0.26, (0, 0.01, 1.64), S, (1.0, 0.96, 0.98), 18, 12, parent=neck)
    ball('VisorRim', 0.212, (0, -0.14, 1.66), K, (0.98, 0.48, 0.63), 18, 12, parent=neck)
    ball('Visor', 0.2, (0, -0.158, 1.66), V, (0.96, 0.48, 0.6), 18, 12, parent=neck)
    ball('Snout', 0.07, (0, -0.215, 1.5), G, (1.0, 0.8, 0.8), 12, 8, parent=neck)
    for sx in (-1, 1):
        cyl('Filter', 0.038, 0.04, (sx * 0.075, -0.235, 1.47), M['O'], 12, rot=(math.radians(90), 0, math.radians(sx * 30)), parent=neck)
    for side, sx in (('L', 1), ('R', -1)):
        s, e, w = arm_points(sx, (sx * 0.3, 0, 1.36), 0.27, 0.25, 14)
        sh = empty('Shoulder' + side, s, chest)
        ball('ShoulderBall' + side, 0.092, s, S, (1, 1, 1), 14, 9, parent=sh)
        seg('UpperArm' + side, s, e, 0.09, 0.08, S, 14, sh)
        el = empty('Elbow' + side, e, sh)
        ball('ElbowBall' + side, 0.08, e, S, (1, 1, 1), 14, 9, parent=el)
        seg('LowerArm' + side, e, w, 0.08, 0.075, S, 14, el)
        seg('Cuff' + side, w + (e - w).normalized() * 0.04, w, 0.083, 0.083, K, 14, el)
        wr = empty('Wrist' + side, w, el)
        ball('Mitten' + side, 0.075, w + Vector((sx * 0.01, 0, -0.07)), K, (0.9, 0.8, 1.15), 14, 9, parent=wr)
        ball('Thumb' + side, 0.03, w + Vector((-sx * 0.045, -0.04, -0.045)), K, (1, 1, 1.3), 10, 6, parent=wr)
        hp = empty('Hip' + side, (sx * 0.13, 0, 0.82), pelvis)
        seg('UpperLeg' + side, (sx * 0.13, 0, 0.84), (sx * 0.13, 0, 0.47), 0.13, 0.12, S, 14, hp)
        kn = empty('Knee' + side, (sx * 0.13, 0, 0.47), hp)
        ball('KneeBall' + side, 0.12, (sx * 0.13, 0, 0.47), S, (1, 1, 1), 14, 9, parent=kn)
        seg('LowerLeg' + side, (sx * 0.13, 0, 0.47), (sx * 0.13, 0, 0.15), 0.12, 0.115, S, 14, kn)
        ball('Boot' + side, 0.12, (sx * 0.13, -0.045, 0.085), K, (1.05, 1.5, 0.72), 14, 9, parent=kn)
        seg('BootCollar' + side, (sx * 0.13, 0, 0.12), (sx * 0.13, 0, 0.18), 0.122, 0.122, K, 14, kn)
    export('HazmatSoft')


# ------------------------------------------------------------------------------------------------ 2. crew
def build_crew():
    reset()
    M = mats()
    S, K, V, G, O, W, D, R = M['S'], M['K'], M['V'], M['G'], M['O'], M['W'], M['D'], M['R']
    F = 1   # flat shading
    root = empty('HazmatCrew', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.95), root)
    prism('Hips', 0.2, 0.22, 0.84, 1.03, 0.72, S, 8, pelvis, F)
    prism('Belt', 0.226, 0.226, 0.98, 1.03, 0.73, K, 8, pelvis, F)
    chest = empty('Chest', (0, 0, 1.03), pelvis)
    prism('Torso', 0.23, 0.3, 1.02, 1.42, 0.7, S, 8, chest, F)
    prism('Shoulders', 0.3, 0.19, 1.42, 1.53, 0.7, S, 8, chest, F)
    # company band across the chest
    def tr(z):
        return 0.23 + (0.3 - 0.23) * (z - 1.02) / 0.4
    prism('ChestBand', tr(1.25) + 0.007, tr(1.36) + 0.007, 1.25, 1.36, 0.7, D, 8, chest, F)
    for i, sm in enumerate(M['st']):
        z = 1.305 + (i - 1) * 0.03
        prism('ChestStripe%d' % i, tr(z - 0.011) + 0.012, tr(z + 0.011) + 0.012, z - 0.011, z + 0.011, 0.7, sm, 8, chest, F)
    box('Zip', (0.018, 0.01, 0.36), (0, -0.2, 1.2), K, parent=chest)
    box('IDPatch', (0.1, 0.008, 0.07), (-0.12, -0.212, 1.17), R, 0.004, 1, parent=chest)
    # back: a tank in a frame
    prism('Tank', 0.095, 0.095, 1.06, 1.46, 1.0, W, 8, chest, F, loc=(0, 0.25))
    box('TankFrame', (0.26, 0.05, 0.36), (0, 0.19, 1.26), K, 0.01, 1, parent=chest)
    neck = empty('Neck', (0, 0, 1.52), chest)
    prism('Collar', 0.19, 0.15, 1.5, 1.57, 0.85, K, 8, neck, F)
    ball('Hood', 0.205, (0, 0.015, 1.7), S, (1.0, 0.98, 1.08), 8, 6, parent=neck)
    for o in bpy.context.scene.objects:
        if o.name == 'Hood':
            activate(o); bpy.ops.object.shade_flat()
    box('VisorRim', (0.3, 0.08, 0.19), (0, -0.155, 1.715), K, 0.035, 1, parent=neck)
    box('Visor', (0.27, 0.06, 0.16), (0, -0.175, 1.72), V, 0.03, 1, parent=neck)
    cyl('Snout', 0.06, 0.1, (0, -0.2, 1.56), G, 6, rot=(math.radians(90), 0, 0), parent=neck, smooth=1)
    for sx in (-1, 1):
        cyl('Filter', 0.042, 0.06, (sx * 0.095, -0.19, 1.54), O, 6, rot=(math.radians(90), 0, math.radians(sx * 35)), parent=neck, smooth=1)
    for side, sx in (('L', 1), ('R', -1)):
        s, e, w = arm_points(sx, (sx * 0.29, 0, 1.45), 0.31, 0.28, 12)
        sh = empty('Shoulder' + side, s, chest)
        ball('ShoulderBall' + side, 0.092, s, S, (1, 1, 1), 8, 6, parent=sh)
        seg('UpperArm' + side, s, e, 0.1, 0.085, S, 7, sh, F)
        limb_stripes('Arm' + side, s, e, 0.093, 0.42, M, sh, 7, F)
        el = empty('Elbow' + side, e, sh)
        ball('ElbowBall' + side, 0.078, e, S, (1, 1, 1), 8, 6, parent=el)
        seg('LowerArm' + side, e, w, 0.088, 0.072, S, 7, el, F)
        seg('Cuff' + side, w + (e - w).normalized() * 0.05, w, 0.08, 0.08, K, 7, el, F)
        wr = empty('Wrist' + side, w, el)
        box('Glove' + side, (0.1, 0.09, 0.14), w + Vector((sx * 0.012, 0, -0.07)), K, 0.02, 1, parent=wr)
        box('Thumb' + side, (0.035, 0.04, 0.06), w + Vector((-sx * 0.045, -0.045, -0.04)), K, 0.01, 1, parent=wr)
        hp = empty('Hip' + side, (sx * 0.11, 0, 0.9), pelvis)
        seg('UpperLeg' + side, (sx * 0.11, 0, 0.92), (sx * 0.115, 0, 0.5), 0.13, 0.115, S, 7, hp, F)
        kn = empty('Knee' + side, (sx * 0.115, 0, 0.5), hp)
        ball('KneeBall' + side, 0.104, (sx * 0.115, 0, 0.5), S, (1, 1, 1), 8, 6, parent=kn)
        seg('LowerLeg' + side, (sx * 0.115, 0, 0.5), (sx * 0.115, 0, 0.16), 0.115, 0.1, S, 7, kn, F)
        seg('ShinReflect' + side, (sx * 0.115, 0, 0.27), (sx * 0.115, 0, 0.31), 0.112, 0.11, R, 7, kn, F)
        seg('AnkleTape' + side, (sx * 0.115, 0, 0.15), (sx * 0.115, 0, 0.2), 0.106, 0.106, K, 7, kn, F)
        box('Boot' + side, (0.16, 0.3, 0.15), (sx * 0.115, -0.05, 0.075), K, 0.03, 1, parent=kn)
    export('HazmatCrew')


# ------------------------------------------------------------------------------------------------ 3. stout
def build_stout():
    reset()
    M = mats()
    S, K, V, G, O, W, D, R = M['S'], M['K'], M['V'], M['G'], M['O'], M['W'], M['D'], M['R']
    root = empty('HazmatStout', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.72), root)
    box('Hips', (0.48, 0.36, 0.2), (0, 0, 0.74), S, 0.08, 3, parent=pelvis)
    box('Belt', (0.5, 0.38, 0.06), (0, 0, 0.81), K, 0.03, 2, parent=pelvis)
    chest = empty('Chest', (0, 0, 0.82), pelvis)
    box('Torso', (0.58, 0.42, 0.46), (0, 0, 1.06), S, 0.11, 4, parent=chest)
    # the company stripe: a dark strip down the right chest, three stripes on it
    box('ChestBand', (0.13, 0.012, 0.3), (-0.15, -0.212, 1.06), D, 0.008, 1, parent=chest)
    for i, sm in enumerate(M['st']):
        box('ChestStripe%d' % i, (0.026, 0.01, 0.27), (-0.15 + (i - 1) * 0.034, -0.219, 1.06), sm, parent=chest)
    box('Zip', (0.018, 0.01, 0.3), (0.04, -0.213, 1.05), K, parent=chest)
    box('ReflectBand', (0.59, 0.43, 0.04), (0, 0, 0.92), R, 0.02, 2, parent=chest)
    # a big backpack with the tank and filter canisters
    box('Backpack', (0.46, 0.22, 0.46), (0, 0.3, 1.05), W, 0.06, 3, parent=chest)
    box('PackFlap', (0.47, 0.23, 0.1), (0, 0.3, 1.25), O, 0.04, 2, parent=chest)
    for sx in (-1, 1):
        cyl('Canister', 0.06, 0.24, (sx * 0.27, 0.32, 1.0), G, 12, parent=chest)
        box('Strap', (0.07, 0.03, 0.4), (sx * 0.17, -0.21, 1.08), K, 0.01, 1, parent=chest)
    neck = empty('Neck', (0, 0, 1.28), chest)
    cyl('Collar', 0.2, 0.08, (0, 0, 1.3), K, 16, parent=neck)
    ball('Hood', 0.29, (0, 0.02, 1.52), S, (1.0, 0.97, 0.95), 18, 12, parent=neck)
    cyl('VisorRim', 0.2, 0.06, (0, -0.25, 1.53), K, 20, rot=(math.radians(90), 0, 0), parent=neck)
    ball('Visor', 0.175, (0, -0.255, 1.53), V, (1, 0.45, 1), 18, 12, parent=neck)
    for sx in (-1, 1):
        cyl('EarFilter', 0.07, 0.07, (sx * 0.29, 0.0, 1.5), O, 14, rot=(0, math.radians(90), 0), parent=neck)
    for side, sx in (('L', 1), ('R', -1)):
        s, e, w = arm_points(sx, (sx * 0.33, 0, 1.18), 0.23, 0.21, 18)
        sh = empty('Shoulder' + side, s, chest)
        ball('ShoulderBall' + side, 0.104, s, S, (1, 1, 1), 14, 9, parent=sh)
        seg('UpperArm' + side, s, e, 0.105, 0.1, S, 12, sh)
        el = empty('Elbow' + side, e, sh)
        ball('ElbowBall' + side, 0.096, e, S, (1, 1, 1), 14, 9, parent=el)
        seg('LowerArm' + side, e, w, 0.1, 0.095, S, 12, el)
        limb_stripes('Cuff' + side, e, w, 0.096, 0.78, M, el, 12)
        wr = empty('Wrist' + side, w, el)
        box('Glove' + side, (0.15, 0.13, 0.15), w + Vector((sx * 0.015, 0, -0.08)), K, 0.05, 3, parent=wr)
        box('Thumb' + side, (0.05, 0.05, 0.07), w + Vector((-sx * 0.06, -0.06, -0.05)), K, 0.02, 2, parent=wr)
        hp = empty('Hip' + side, (sx * 0.13, 0, 0.68), pelvis)
        seg('UpperLeg' + side, (sx * 0.13, 0, 0.7), (sx * 0.13, 0, 0.42), 0.14, 0.135, S, 12, hp)
        kn = empty('Knee' + side, (sx * 0.13, 0, 0.42), hp)
        ball('KneeBall' + side, 0.13, (sx * 0.13, 0, 0.42), S, (1, 1, 1), 14, 9, parent=kn)
        seg('LowerLeg' + side, (sx * 0.13, 0, 0.42), (sx * 0.13, 0, 0.17), 0.135, 0.13, S, 12, kn)
        box('Boot' + side, (0.21, 0.34, 0.18), (sx * 0.13, -0.05, 0.09), K, 0.06, 3, parent=kn)
        box('BootToe' + side, (0.2, 0.1, 0.05), (sx * 0.13, -0.19, 0.03), R, 0.02, 2, parent=kn)
    export('HazmatStout')


if __name__ == '__main__':
    build_soft()
    build_crew()
    build_stout()
    print('done')
