"""Round seven of the player's suit (2026-11-06): HazmatSuit5, round six (HazmatSuit4) with the owner's notes:
  - the hands refined: each glove is one smooth piece (the parts fused with a voxel remesh, then smoothed), with a
    slimmer gauntlet
  - the trousers no longer look clipped over the boots: each leg ends in a hem gathered by an elastic band round a
    smooth boot shaft (the boot is fused the same way)
  - no hard line between "trousers" and "shirt", and a better tummy: the body is one smooth piece lofted through
    cross-sections from the crotch to the shoulders
  - the company stripe only round the left upper arm (not the chest and both arms)
  - the zip straight up and down, a little to the left of centre, with a storm flap beside it
  - no valve on the chest
Everything else (seams, the bake, the name tape and number, the face choices) is as round six.
Run:  blender -b -P Tools/Blender/style_lab_characters7.py   (about two minutes, most of it the bake)"""
import bpy, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from style_lab_models import mat, box, cyl, ball, empty, finish, activate, export, reset, tube
from style_lab_characters import mats, seg, arm_points, limb_stripes
from style_lab_characters2 import blob, fold
import style_lab_characters3 as S3
import style_lab_characters5 as S5
from style_lab_characters5 import build_masks
from style_lab_characters6 import (surface_strip, surface_point, marker, seam_line, build_head, bake_suit,
                                   SEAM_HEX, SOLE_HEX)


def fuse(name, parts, m, voxel, smooth=0.6, repeat=6, ratio=None, parent=None):
    """Joins parts into one piece and fuses them into a single smooth surface (voxel remesh, smoothing, decimation)."""
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        mw = p.matrix_world.copy()
        p.parent = None
        p.matrix_world = mw
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    for kind, setup in (('REMESH', lambda md: (setattr(md, 'mode', 'VOXEL'), setattr(md, 'voxel_size', voxel), setattr(md, 'adaptivity', 0.0))),
                        ('SMOOTH', lambda md: (setattr(md, 'factor', smooth), setattr(md, 'iterations', repeat)))):
        md = o.modifiers.new(kind, kind)
        setup(md)
        bpy.ops.object.modifier_apply(modifier=md.name)
    if ratio:
        md = o.modifiers.new('Decimate', 'DECIMATE')
        md.ratio = ratio
        bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.context.scene.cursor.location = (0, 0, 0)
    return finish(o, m, 0, smooth=180, parent=parent)


def loft(name, rings, m, segs=48, e=0.75, steps=4, parent=None):
    """A smooth body through cross-sections (z, half width, half depth, shift back), each a rounded superellipse,
    interpolated between them (Catmull-Rom), closed at both ends."""
    def cr(p0, p1, p2, p3, t):
        return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
    R = [Vector(r) for r in rings]
    dense = []
    for i in range(len(R) - 1):
        p0, p1, p2, p3 = R[max(i - 1, 0)], R[i], R[i + 1], R[min(i + 2, len(R) - 1)]
        for k in range(steps):
            dense.append(cr(p0, p1, p2, p3, k / steps))
    dense.append(R[-1])
    verts, faces = [], []
    for z, rx, ry, yo in dense:
        for j in range(segs):
            a = 2 * math.pi * j / segs
            c, s = math.cos(a), math.sin(a)
            verts.append((rx * math.copysign(abs(c) ** e, c), yo + ry * math.copysign(abs(s) ** e, s), z))
    n = len(dense)
    for i in range(n - 1):
        for j in range(segs):
            a, b = i * segs + j, i * segs + (j + 1) % segs
            faces.append((a, b, b + segs, a + segs))
    bot = len(verts)
    verts.append((0, dense[0][3], dense[0][0] - 0.004))
    top = len(verts)
    verts.append((0, dense[-1][3], dense[-1][0] + 0.004))
    for j in range(segs):
        faces.append((bot, (j + 1) % segs, j))
        faces.append((top, (n - 1) * segs + j, (n - 1) * segs + (j + 1) % segs))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    import bmesh
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    return finish(o, m, 0, smooth=180, parent=parent)


def arm7(side, sx, chest, M, SEAM, sh_pos, up, lo, splay, r, bell=1.1, stripes=False):
    S, K = M['S'], M['K']
    s, e, w = arm_points(sx, sh_pos, up, lo, splay)
    d_up, d = (e - s).normalized(), (w - e).normalized()
    out = sx * Vector((-d.z, 0, d.x)).normalized()
    sh = empty('Shoulder' + side, s, chest)
    ball('ShoulderBall' + side, r, s, S, (1, 1, 1), 16, 10, parent=sh)
    seg('UpperArm' + side, s, e, r, r * 0.94, S, 16, sh)
    fold('ArmholeSeam' + side, s + d_up * 0.035, d_up, r * 1.0, 0.0034, SEAM, sh)
    seam_line('ArmSeamUpper' + side, s + d_up * 0.04 + out * r * 1.0, e + out * r * 0.95, SEAM, sh)
    if stripes:
        limb_stripes('Arm' + side, s, e, r * 0.97, 0.62, M, sh, 16)
    el = empty('Elbow' + side, e, sh)
    ball('ElbowBall' + side, r * 0.9, e, S, (1, 1, 1), 16, 10, parent=el)
    seg('LowerArm' + side, e, w, r * 0.92, r * 0.92 * bell, S, 16, el)
    seam_line('ArmSeamLower' + side, e + out * r * 0.92, w - d * 0.03 + out * r * 0.92 * bell, SEAM, el)
    seg('GloveRing' + side, w - d * 0.025, w + d * 0.01, 0.082, 0.082, K, 20, el)
    wr = empty('Wrist' + side, w, el)
    # the glove, fused into one smooth piece: gauntlet, palm, three fingers together, index finger, thumb
    inner, fwd = Vector((-sx, 0, 0)), Vector((0, -1, 0))
    parts = [seg('g', w - d * 0.03, w + d * 0.022, 0.056, 0.05, K, 20),
             blob('g', (0.026, 0.042, 0.046), w + d * 0.06, K, 0.72),
             blob('g', (0.023, 0.027, 0.034), w + d * 0.125 + Vector((0, 0.012, 0)) + inner * 0.004, K, 0.72)]
    i0, i1 = w + d * 0.1 + fwd * 0.024, w + d * 0.15 + fwd * 0.026 + inner * 0.006
    parts += [seg('g', i0, i1, 0.0125, 0.0115, K, 12), ball('g', 0.0115, i1, K, (1, 1, 1), 12, 8)]
    t0, t1 = w + d * 0.045 + fwd * 0.03 + inner * 0.01, w + d * 0.085 + fwd * 0.05 + inner * 0.016
    parts += [seg('g', t0, t1, 0.0135, 0.012, K, 12), ball('g', 0.012, t1, K, (1, 1, 1), 12, 8)]
    fuse('Glove' + side, parts, K, 0.0022, 0.7, 10, 0.3, parent=wr)
    return sh


def leg7(side, sx, pelvis, M, SEAM, SOLE, hip_z, knee_z, r, hip_x=0.1):
    S, K = M['S'], M['K']
    x = sx * hip_x
    hem_top, hem_bot = 0.2, 0.168
    hp = empty('Hip' + side, (x, 0, hip_z), pelvis)
    # the thigh starts inside the body, so the leg comes out from under it with no edge
    ball('HipBall' + side, r * 1.04, (x, 0, hip_z + 0.02), S, (1, 1, 1), 32, 20, parent=hp)
    seg('UpperLeg' + side, (x, 0, hip_z + 0.02), (x, 0, knee_z), r * 1.06, r * 0.95, S, 32, hp)
    seam_line('LegSeamUpper' + side, (x + sx * r, 0, hip_z - 0.02), (x + sx * r * 0.95, 0, knee_z), SEAM, hp)
    kn = empty('Knee' + side, (x, 0, knee_z), hp)
    ball('KneeBall' + side, r * 0.92, (x, 0, knee_z), S, (1, 1, 1), 16, 10, parent=kn)
    seg('LowerLeg' + side, (x, 0, knee_z), (x, 0, hem_top), r * 0.93, r * 0.98, S, 16, kn)
    seam_line('LegSeamLower' + side, (x + sx * r * 0.93, 0, knee_z), (x + sx * r * 0.98, 0, hem_top + 0.01), SEAM, kn)
    # the hem gathered by an elastic band round the boot shaft
    seg('Hem' + side, (x, 0, hem_top), (x, 0, hem_bot), r * 0.98, 0.081, S, 20, kn)
    fold('HemBand' + side, Vector((x, 0, hem_bot + 0.009)), (0, 0, 1), 0.083, 0.0065, SEAM, kn)
    # the boot, fused into one smooth piece (shaft, foot, toe), on a thick sole with tread
    parts = [seg('b', (x, -0.004, 0.06), (x, -0.004, 0.205), 0.072, 0.07, K, 24),
             blob('b', (0.07, 0.125, 0.056), (x, -0.035, 0.07), K, 0.65),
             blob('b', (0.064, 0.05, 0.044), (x, -0.12, 0.056), K, 0.7)]
    fuse('Boot' + side, parts, K, 0.003, 0.6, 6, 0.3, parent=kn)
    box('Sole' + side, (0.15, 0.275, 0.03), (x, -0.035, 0.019), SOLE, 0.009, 2, parent=kn)
    for i in range(6):
        box('Tread' + side, (0.128, 0.02, 0.008), (x, -0.035 - 0.105 + i * 0.042, 0.004), SOLE, 0.002, 1, parent=kn)
    return kn


def build_suit():
    reset()
    M = mats()
    S, K, G, O, W, D, R = M['S'], M['K'], M['G'], M['O'], M['W'], M['D'], M['R']
    SEAM, SOLE = mat('SL_SuitSeam', SEAM_HEX, 0.5), mat('SL_Sole', SOLE_HEX, 0.85)
    F = dict(eye=mat('SL_Eye', '#121010', 0.15), hair=mat('SL_Hair', '#3A2618', 0.8),
             glass=mat('SL_VisorGlass', '#B8D4DA', 0.03), paint=mat('SL_FacePaint', '#FFFFFF', 0.8),
             shade=mat('SL_FaceShade', '#FFFFFF', 0.8), stubble=mat('SL_FaceStubble', '#FFFFFF', 0.8))
    skin = mat('SL_SkinA', '#E9B892', 0.7)
    root = empty('HazmatSuit5', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.84), root)
    chest = empty('Chest', (0, 0, 0.93), pelvis)
    # the body, one smooth piece: crotch, hips, a soft waist and tummy, chest, shoulders (z, half width, half depth, shift back)
    body = loft('Body', [
        (0.75, 0.03, 0.03, 0.0),
        (0.77, 0.14, 0.098, 0.0),
        (0.81, 0.19, 0.116, 0.0),
        (0.87, 0.204, 0.134, 0.0),
        (0.95, 0.2, 0.148, -0.002),
        (1.04, 0.19, 0.152, -0.004),
        (1.13, 0.205, 0.158, -0.002),
        (1.23, 0.228, 0.164, 0.0),
        (1.31, 0.235, 0.16, 0.004),
        (1.37, 0.222, 0.148, 0.008),
        (1.415, 0.17, 0.118, 0.01),
        (1.44, 0.07, 0.06, 0.01),
    ], S, parent=chest)
    hump = blob('Hump', (0.19, 0.12, 0.23), (0, 0.2, 1.17), S, 0.72, 0.05, parent=chest)
    cyl('Badge', 0.045, 0.016, (-0.196, 0.2, 1.2), D, 24, rot=(0, math.radians(90), 0), parent=chest)
    for i, sm in enumerate(M['st']):
        box('BadgeStripe%d' % i, (0.008, 0.055, 0.009), (-0.205, 0.2, 1.215 - i * 0.013), sm, parent=chest)
    # seams down the sides, over the shoulders and through the crotch (where the plane y = 0 meets the body)
    surface_strip('SideSeam', body, SEAM, (0, 0, 1.12), (math.radians(90), 0, 0), (3, 3, 0.008), 0.004, chest)
    # the zip, straight up and down a little left of centre, with a storm flap beside it
    zx = 0.072
    surface_strip('ZipFlap', body, SEAM, (zx + 0.022, -0.3, 1.15), (0, 0, 0), (0.03, 0.4, 0.5), 0.004, chest)
    surface_strip('Zip', body, K, (zx, -0.3, 1.15), (0, 0, 0), (0.013, 0.4, 0.5), 0.0065, chest)
    p, n = surface_point(body, (zx, -1.0, 1.385), (0, 1, 0))
    box('ZipPull', (0.012, 0.006, 0.024), p + n * 0.008 + Vector((0, 0, -0.012)), G, 0.002, 1, parent=chest)
    # the name tape on the right chest, and where the number goes on the back
    nt_c = Vector((-0.105, -0.2, 1.31))
    surface_strip('NameTape', body, R, nt_c, (0, 0, 0), (0.13, 0.2, 0.036), 0.006, chest)
    def text_markers(key, p, n, reading, lift):
        right = (reading - n * reading.dot(n)).normalized()
        up = n.cross(right).normalized()
        if up.z < 0:
            up = -up
        p = p + n * lift
        marker(key + 'Anchor', p, chest)
        marker(key + 'Right', p + right * 0.05, chest)
        marker(key + 'Up', p + up * 0.05, chest)
    p, n = surface_point(body, (nt_c.x, -1.0, nt_c.z), (0, 1, 0))
    text_markers('NameTape', p, n, Vector((1, 0, 0)), 0.0095)
    p, n = surface_point(hump, (0, 1.0, 1.19), (0, -1, 0))
    text_markers('Number', p, n, Vector((-1, 0, 0)), 0.003)

    neck = empty('Neck', (0, 0, 1.4), chest)
    blob('HoodDrape', (0.205, 0.18, 0.065), (0, 0.02, 1.385), S, 0.8, parent=neck)
    hood = blob('Hood', S3.HOOD_S, S3.HOOD_C, S, 0.82, -0.05, segs=48, rings=32, parent=neck)
    S3.build_window(hood, neck, F['glass'], K)
    surface_strip('HoodSeam', hood, SEAM, (0, 0.02, S5.Z), (0, math.radians(90), 0), (3, 3, 0.008), 0.003, neck)
    build_head(neck, M, F, skin)
    build_masks(neck, M, F)

    for side, sx in (('L', 1), ('R', -1)):
        arm7(side, sx, chest, M, SEAM, (sx * 0.255, 0, 1.355), 0.255, 0.235, 10, 0.074, stripes=(side == 'L'))
        leg7(side, sx, pelvis, M, SEAM, SOLE, 0.82, 0.45, 0.102)
    bake_suit(root, M, SEAM, SOLE)
    export('HazmatSuit5')


if __name__ == '__main__':
    build_suit()
    print('done')
