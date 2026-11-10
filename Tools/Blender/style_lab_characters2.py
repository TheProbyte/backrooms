"""Round two of the player's suit (2026-11-06): three more versions in the direction of HazmatStout (short, chunky, toy
proportions) but softer, with nothing blocky: rounded pillow shapes (superellipsoids), tapered limbs, fabric folds at
the elbows and knees, and the company stripe wrapped onto the body's own surface. Each has its own silhouette and visor:
  HazmatBun     an egg-shaped body, a big round head with a wide wrap-round visor, a rounded backpack
  HazmatHood    a loose, baggy suit: a hood over a rounded face plate, a big filter, flared sleeves and trousers
  HazmatScout   the shortest: a big head with a tall oval visor, a headlamp and aerial, a sash of the stripe, twin tanks
Same joints as the other suits, facing -Y. Run:  blender -b -P Tools/Blender/style_lab_characters2.py"""
import bpy, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from style_lab_models import mat, box, cyl, ball, empty, finish, activate, export, reset, tube
from style_lab_characters import mats, seg, limb_stripes, arm_points


def blob(name, size, loc, m, e=0.7, taper=0.0, segs=28, rings=18, parent=None, rot=(0, 0, 0), bend=0.0):
    """A rounded pillow shape: a sphere pushed toward a box (e = 1 round .. 0.3 boxy), half-sizes `size`;
    taper > 0 widens the top, < 0 the bottom."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=1, location=(0, 0, 0))
    o = bpy.context.active_object
    o.name = name
    for v in o.data.vertices:
        c = v.co
        x, y, z = (math.copysign(abs(a) ** e, a) for a in (c.x, c.y, c.z))
        k = 1 + taper * z
        X = x * size[0] * k
        v.co = Vector((X, y * size[1] * k + bend * X * X, z * size[2]))   # bend: the sides curve back (a visor round a head)
    o.location = loc
    o.rotation_euler = rot
    return finish(o, m, 0, smooth=70, parent=parent)


def shell_band(name, src, m, z0, z1, inflate=0.01, rot=(0, 0, 0), parent=None):
    """A band of the surface of `src` between heights z0..z1 (in a slab turned by `rot`), lifted off it by `inflate`:
    stripes and belts that follow any body shape."""
    me = src.data.copy()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.matrix_world = src.matrix_world.copy()
    for v in me.vertices:
        v.co += v.normal * inflate
    c = src.matrix_world.translation
    bpy.ops.mesh.primitive_cube_add(size=1, location=(c.x, c.y, (z0 + z1) / 2), rotation=rot)
    cut = bpy.context.active_object
    cut.scale = (3, 3, z1 - z0)
    md = o.modifiers.new('Cut', 'BOOLEAN')
    md.operation = 'INTERSECT'
    md.object = cut
    md.solver = 'EXACT'
    activate(o)
    bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.data.objects.remove(cut, do_unlink=True)
    o.parent = None
    return finish(o, m, 0, smooth=70, parent=parent)


def company_band(tag, src, M, zc, parent, rot=(0, 0, 0), width=0.1):
    shell_band(tag + 'Band', src, M['D'], zc - width / 2, zc + width / 2, 0.006, rot, parent)
    for i, sm in enumerate(M['st']):
        z = zc + (1 - i) * 0.028
        shell_band(tag + 'Stripe%d' % i, src, sm, z - 0.011, z + 0.011, 0.011, rot, parent)


def fold(name, c, d, R, r, m, parent):
    """A soft fabric fold round a limb (a torus) at c, its axis along d."""
    q = Vector(d).normalized().to_track_quat('Z', 'Y')
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=20, minor_segments=8,
                                     location=c, rotation=q.to_euler())
    o = bpy.context.active_object
    o.name = name
    return finish(o, m, 0, smooth=70, parent=parent)


def arm(side, sx, chest, M, sh_pos, up, lo, splay, r, fold_m, glove, bell=1.0, stripes_at=None):
    S, K = M['S'], M['K']
    s, e, w = arm_points(sx, sh_pos, up, lo, splay)
    sh = empty('Shoulder' + side, s, chest)
    ball('ShoulderBall' + side, r * 1.0, s, S, (1, 1, 1), 16, 10, parent=sh)
    seg('UpperArm' + side, s, e, r, r * 0.94, S, 16, sh)
    if stripes_at is not None and stripes_at < 1:
        limb_stripes('Arm' + side, s, e, r * 0.97, stripes_at, M, sh, 16)
    el = empty('Elbow' + side, e, sh)
    ball('ElbowBall' + side, r * 0.9, e, S, (1, 1, 1), 16, 10, parent=el)
    fold('ElbowFold' + side, e, w - e, r * 0.92, r * 0.13, fold_m, el)
    seg('LowerArm' + side, e, w, r * 0.92, r * 0.92 * bell, S, 16, el)
    if stripes_at is not None and stripes_at >= 1:
        limb_stripes('Cuff' + side, e, w, r * 0.92 * bell, 0.72, M, el, 16)
    d = (w - e).normalized()
    seg('Cuff' + side, w - d * 0.035, w + d * 0.005, r * 0.8, r * 0.8, K, 16, el)
    wr = empty('Wrist' + side, w, el)
    gx, gy, gz = glove
    blob('Glove' + side, (gx, gy, gz), w + Vector((sx * 0.01, 0, -gz * 0.85)), K, 0.75, parent=wr)
    ball('Thumb' + side, gx * 0.42, w + Vector((-sx * gx * 0.75, -gy * 0.75, -gz * 0.6)), K, (1, 1, 1.3), 12, 8, parent=wr)
    return sh


def leg(side, sx, pelvis, M, hip_z, knee_z, ankle_z, r, fold_m, boot, flare=1.0, roll=False, toe=True, hip_x=0.13):
    S, K, R = M['S'], M['K'], M['R']
    x = sx * hip_x
    hp = empty('Hip' + side, (x, 0, hip_z), pelvis)
    seg('UpperLeg' + side, (x, 0, hip_z + 0.03), (x, 0, knee_z), r, r * 0.95, S, 16, hp)
    kn = empty('Knee' + side, (x, 0, knee_z), hp)
    ball('KneeBall' + side, r * 0.92, (x, 0, knee_z), S, (1, 1, 1), 16, 10, parent=kn)
    fold('KneeFold' + side, (x, 0, knee_z), (0, 0, 1), r * 0.93, r * 0.12, fold_m, kn)
    seg('LowerLeg' + side, (x, 0, knee_z), (x, 0, ankle_z), r * 0.93, r * 0.93 * flare, S, 16, kn)
    if roll:
        fold('Roll' + side, (x, 0, ankle_z + 0.02), (0, 0, 1), r * flare * 0.95, r * 0.3, S, kn)
    bx, by, bz = boot
    blob('Boot' + side, (bx, by, bz), (x, -0.04, bz), K, 0.6, parent=kn)
    blob('Sole' + side, (bx * 1.04, by * 1.03, 0.022), (x, -0.04, 0.022), M['G'], 0.5, parent=kn)
    if toe:
        blob('ToeCap' + side, (bx * 0.8, by * 0.35, bz * 0.6), (x, -0.04 - by * 0.72, bz * 0.75), R, 0.7, parent=kn)
    return kn


# ------------------------------------------------------------------------------------------------ 1. bun
def build_bun():
    reset()
    M = mats()
    S, K, V, G, O, W, D = M['S'], M['K'], M['V'], M['G'], M['O'], M['W'], M['D']
    root = empty('HazmatBun', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.72), root)
    blob('Hips', (0.25, 0.2, 0.13), (0, 0, 0.76), S, 0.7, parent=pelvis)
    chest = empty('Chest', (0, 0, 0.84), pelvis)
    torso = blob('Torso', (0.3, 0.23, 0.33), (0, 0, 1.03), S, 0.72, -0.12, parent=chest)
    company_band('Chest', torso, M, 1.12, chest)
    shell_band('Belt', torso, K, 0.78, 0.83, 0.008, parent=chest)
    box('Zip', (0.016, 0.01, 0.2), (0, -0.247, 0.94), K, parent=chest)
    blob('Backpack', (0.2, 0.1, 0.21), (0, 0.29, 1.04), W, 0.62, parent=chest)
    blob('PackLid', (0.205, 0.105, 0.06), (0, 0.29, 1.22), O, 0.62, parent=chest)
    neck = empty('Neck', (0, 0, 1.32), chest)
    fold('Collar', (0, 0, 1.33), (0, 0, 1), 0.17, 0.045, K, neck)
    ball('Hood', 0.285, (0, 0.01, 1.57), S, (1.0, 0.97, 0.95), 24, 16, parent=neck)
    blob('VisorRim', (0.25, 0.1, 0.112), (0, -0.2, 1.585), K, 0.55, parent=neck, bend=1.5)
    blob('Visor', (0.23, 0.1, 0.092), (0, -0.21, 1.588), V, 0.55, parent=neck, bend=1.5)
    ball('ChinFilter', 0.065, (0, -0.24, 1.42), G, (1, 0.8, 0.9), 16, 10, parent=neck)
    cyl('FilterCap', 0.048, 0.03, (0, -0.29, 1.415), O, 16, rot=(math.radians(90), 0, 0), parent=neck)
    for side, sx in (('L', 1), ('R', -1)):
        arm(side, sx, chest, M, (sx * 0.31, 0, 1.22), 0.22, 0.2, 17, 0.1, S, (0.078, 0.066, 0.085), stripes_at=1)
        leg(side, sx, pelvis, M, 0.7, 0.4, 0.14, 0.14, S, (0.115, 0.17, 0.085))
    export('HazmatBun')


# ------------------------------------------------------------------------------------------------ 2. hood
def build_hood():
    reset()
    M = mats()
    S, K, V, G, O, W, D = M['S'], M['K'], M['V'], M['G'], M['O'], M['W'], M['D']
    root = empty('HazmatHood', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.8), root)
    blob('Hips', (0.26, 0.21, 0.14), (0, 0, 0.84), S, 0.75, parent=pelvis)
    chest = empty('Chest', (0, 0, 0.92), pelvis)
    torso = blob('Torso', (0.31, 0.24, 0.34), (0, 0, 1.13), S, 0.8, 0.08, parent=chest)
    shell_band('Belt', torso, K, 0.86, 0.91, 0.008, parent=chest)
    box('Zip', (0.016, 0.01, 0.44), (0, -0.248, 1.1), K, parent=chest)
    box('ZipFlap', (0.05, 0.012, 0.44), (0.03, -0.246, 1.1), S, 0.005, 1, parent=chest)
    # the company badge on the left chest
    blob('Badge', (0.065, 0.014, 0.045), (0.14, -0.235, 1.26), D, 0.5, parent=chest)
    for i, sm in enumerate(M['st']):
        box('BadgeStripe%d' % i, (0.1, 0.008, 0.012), (0.14, -0.249, 1.276 - i * 0.017), sm, parent=chest)
    seg('Tank', (0, 0.29, 0.98), (0, 0.29, 1.34), 0.095, 0.095, W, 16, chest)
    ball('TankTop', 0.095, (0, 0.29, 1.34), W, (1, 1, 0.6), 16, 8, parent=chest)
    ball('TankBottom', 0.095, (0, 0.29, 0.98), W, (1, 1, 0.6), 16, 8, parent=chest)
    neck = empty('Neck', (0, 0, 1.46), chest)
    # the loose hood: a drape onto the shoulders and a round hood over it
    seg('HoodDrape', (0, 0.01, 1.42), (0, 0.01, 1.6), 0.27, 0.24, S, 20, neck)
    blob('Hood', (0.27, 0.26, 0.27), (0, 0.02, 1.68), S, 0.85, -0.08, parent=neck)
    blob('FaceRim', (0.19, 0.07, 0.17), (0, -0.19, 1.67), K, 0.45, parent=neck, bend=1.6)
    blob('Visor', (0.165, 0.065, 0.145), (0, -0.2, 1.675), V, 0.45, parent=neck, bend=1.6)
    ball('MaskBody', 0.07, (0, -0.24, 1.47), G, (1, 0.8, 0.85), 16, 10, parent=neck)
    cyl('BigFilter', 0.062, 0.045, (0, -0.295, 1.46), O, 20, rot=(math.radians(90), 0, 0), parent=neck)
    for sx in (-1, 1):
        tube('Drawstring', [(sx * 0.09, -0.2, 1.48), (sx * 0.1, -0.23, 1.38), (sx * 0.095, -0.235, 1.3)], 0.008, D, neck)
        ball('Toggle', 0.016, (sx * 0.095, -0.235, 1.29), O, (1, 1, 1.4), 10, 6, parent=neck)
    for side, sx in (('L', 1), ('R', -1)):
        arm(side, sx, chest, M, (sx * 0.31, 0, 1.36), 0.26, 0.24, 15, 0.108, S, (0.08, 0.068, 0.09), bell=1.25, stripes_at=0.45)
        leg(side, sx, pelvis, M, 0.78, 0.45, 0.16, 0.15, S, (0.12, 0.175, 0.09), flare=1.12, roll=True, toe=False)
    export('HazmatHood')


# ------------------------------------------------------------------------------------------------ 3. scout
def build_scout():
    reset()
    M = mats()
    S, K, V, G, O, W, D = M['S'], M['K'], M['V'], M['G'], M['O'], M['W'], M['D']
    lamp = mat('SL_LightPanel', '#FFF4D6', 0.4, emit=4.0)
    root = empty('HazmatScout', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.64), root)
    blob('Hips', (0.24, 0.19, 0.12), (0, 0, 0.68), S, 0.7, parent=pelvis)
    chest = empty('Chest', (0, 0, 0.74), pelvis)
    torso = blob('Torso', (0.29, 0.22, 0.29), (0, 0, 0.94), S, 0.65, parent=chest)
    # the stripe as a sash from the right shoulder to the left hip
    company_band('Sash', torso, M, 0.94, chest, rot=(0, math.radians(-38), 0), width=0.11)
    shell_band('Belt', torso, K, 0.7, 0.75, 0.008, parent=chest)
    for sx in (-1, 1):
        blob('Pouch', (0.065, 0.04, 0.055), (sx * 0.16, -0.22, 0.73), W, 0.55, parent=chest)
        blob('PouchFlap', (0.068, 0.043, 0.018), (sx * 0.16, -0.222, 0.775), O, 0.55, parent=chest)
        seg('TwinTank', (sx * 0.085, 0.27, 0.8), (sx * 0.085, 0.27, 1.12), 0.075, 0.075, W, 16, chest)
        ball('TwinTankTop', 0.075, (sx * 0.085, 0.27, 1.12), W, (1, 1, 0.6), 16, 8, parent=chest)
        ball('TwinTankBottom', 0.075, (sx * 0.085, 0.27, 0.8), W, (1, 1, 0.6), 16, 8, parent=chest)
    blob('TankStrap', (0.26, 0.04, 0.025), (0, 0.24, 1.08), K, 0.5, parent=chest)
    neck = empty('Neck', (0, 0, 1.18), chest)
    fold('Collar', (0, 0, 1.2), (0, 0, 1), 0.16, 0.045, K, neck)
    ball('Hood', 0.3, (0, 0.01, 1.44), S, (1.0, 0.98, 1.02), 24, 16, parent=neck)
    ball('VisorRim', 1.0, (0, -0.235, 1.43), K, (0.18, 0.085, 0.215), 20, 14, parent=neck)
    ball('Visor', 1.0, (0, -0.25, 1.435), V, (0.16, 0.075, 0.195), 20, 14, parent=neck)
    cyl('Headlamp', 0.045, 0.06, (0, -0.25, 1.67), K, 16, rot=(math.radians(70), 0, 0), parent=neck)
    cyl('HeadlampLens', 0.034, 0.012, (0, -0.28, 1.68), lamp, 16, rot=(math.radians(70), 0, 0), parent=neck)
    tube('Aerial', [(0.17, 0.05, 1.66), (0.2, 0.07, 1.8), (0.22, 0.08, 1.9)], 0.007, K, neck)
    ball('AerialTip', 0.02, (0.22, 0.08, 1.9), O, (1, 1, 1), 10, 6, parent=neck)
    for sx in (-1, 1):
        ball('MaskSide', 0.045, (sx * 0.1, -0.255, 1.27), G, (1, 0.8, 1), 12, 8, parent=neck)
    for side, sx in (('L', 1), ('R', -1)):
        arm(side, sx, chest, M, (sx * 0.3, 0, 1.08), 0.2, 0.19, 20, 0.1, S, (0.085, 0.072, 0.09))
        hp = leg(side, sx, pelvis, M, 0.62, 0.37, 0.13, 0.135, S, (0.12, 0.19, 0.09))
        blob('KneePad' + side, (0.075, 0.03, 0.07), (sx * 0.13, -0.125, 0.37), K, 0.6, parent=hp)
    export('HazmatScout')


if __name__ == '__main__':
    build_bun()
    build_hood()
    build_scout()
    print('done')
