"""Round eight of the player's suit (2026-11-06): HazmatSuit6, round seven (HazmatSuit5) with the owner's notes:
  - bigger hands (the fused gloves 1.3 times the size)
  - the join of the legs and the body neater still: a seam tape laid along exactly where each thigh meets the body
    (found by searching up each line of the thigh's surface for where it enters the body)
  - the hood no longer sits on a ring like a scarf: it is one smooth shape lofted from inside the shoulders, through
    the neck, to the dome, so it rises out of the body
  - the stripe runs down the outside of the left sleeve, from the shoulder seam to the glove ring, instead of round
    the arm (where it ended abruptly)
  - boot soles with rounded corners and edges
Everything else as round seven.
Run:  blender -b -P Tools/Blender/style_lab_characters8.py   (about two minutes, most of it the bake)"""
import bpy, math, os, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(__file__))
from style_lab_models import mat, box, cyl, ball, empty, finish, activate, export, reset, tube
from style_lab_characters import mats, seg, arm_points
from style_lab_characters2 import blob, fold
import style_lab_characters3 as S3
import style_lab_characters5 as S5
from style_lab_characters5 import build_masks
from style_lab_characters6 import (surface_strip, surface_point, marker, seam_line, build_head, bake_suit,
                                   SEAM_HEX, SOLE_HEX)
from style_lab_characters7 import fuse, loft

GLOVE = 1.3     # glove size against round seven


def strip_along(name, src, m, a, b, out, width, inflate, parent):
    """The part of src's surface under a straight strip from a to b on its outer side (out), width wide."""
    d = (b - a).normalized()
    side = out.cross(d).normalized()
    out = d.cross(side).normalized()
    rot = Matrix((side, out, d)).transposed().to_euler()
    return surface_strip(name, src, m, (a + b) / 2 + out * 0.03, rot, (width, 0.12, (b - a).length), inflate, parent)


def arm8(side, sx, chest, M, SEAM, sh_pos, up, lo, splay, r, bell=1.1, stripes=False):
    S, K, D = M['S'], M['K'], M['D']
    s, e, w = arm_points(sx, sh_pos, up, lo, splay)
    d_up, d = (e - s).normalized(), (w - e).normalized()
    out = sx * Vector((-d.z, 0, d.x)).normalized()
    sh = empty('Shoulder' + side, s, chest)
    ball('ShoulderBall' + side, r, s, S, (1, 1, 1), 16, 10, parent=sh)
    upper = seg('UpperArm' + side, s, e, r, r * 0.94, S, 24, sh)
    fold('ArmholeSeam' + side, s + d_up * 0.035, d_up, r * 1.0, 0.0034, SEAM, sh)
    el = empty('Elbow' + side, e, sh)
    elb = ball('ElbowBall' + side, r * 0.9, e, S, (1, 1, 1), 24, 14, parent=el)
    lower = seg('LowerArm' + side, e, w, r * 0.92, r * 0.92 * bell, S, 24, el)
    if stripes:
        # the company stripe down the outside of the sleeve, shoulder seam to glove ring: a dark band, three colours
        # turned 35 degrees from the outside of the arm toward the front, so it shows from the front too
        a, b = s + d_up * 0.045, w - d * 0.03
        fwd_v = Vector((0, -1, 0))
        tilt = math.radians(35)
        out_u = (sx * Vector((-d_up.z, 0, d_up.x)).normalized() * math.cos(tilt) + fwd_v * math.sin(tilt)).normalized()
        out_l = (out * math.cos(tilt) + fwd_v * math.sin(tilt)).normalized()
        for part, joint, oo in ((upper, sh, out_u), (elb, el, out_l), (lower, el, out_l)):
            strip_along('SleeveBand' + side, part, D, a, b, oo, 0.056, 0.004, joint)
            for i, sm in enumerate(M['st']):
                off = (i - 1) * 0.016
                sd = oo.cross((b - a).normalized()).normalized()
                strip_along('SleeveStripe%d' % i + side, part, sm, a + sd * off, b + sd * off, oo, 0.012, 0.0072, joint)
    else:
        seam_line('ArmSeamUpper' + side, s + d_up * 0.04 + out * r * 1.0, e + out * r * 0.95, SEAM, sh)
        seam_line('ArmSeamLower' + side, e + out * r * 0.92, w - d * 0.03 + out * r * 0.92 * bell, SEAM, el)
    seg('GloveRing' + side, w - d * 0.025, w + d * 0.01, 0.082, 0.082, K, 20, el)
    wr = empty('Wrist' + side, w, el)
    # the glove, fused into one smooth piece, 1.3 times round seven's size
    g = GLOVE
    inner, fwd = Vector((-sx, 0, 0)), Vector((0, -1, 0))
    parts = [seg('g', w - d * 0.03, w + d * 0.022 * g, 0.056 * 1.05, 0.05 * g, K, 20),
             blob('g', (0.026 * g, 0.042 * g, 0.046 * g), w + d * 0.06 * g, K, 0.72),
             blob('g', (0.023 * g, 0.027 * g, 0.034 * g), w + (d * 0.125 + Vector((0, 0.012, 0)) + inner * 0.004) * g, K, 0.72)]
    i0, i1 = w + (d * 0.1 + fwd * 0.024) * g, w + (d * 0.15 + fwd * 0.026 + inner * 0.006) * g
    parts += [seg('g', i0, i1, 0.0125 * g, 0.0115 * g, K, 12), ball('g', 0.0115 * g, i1, K, (1, 1, 1), 12, 8)]
    t0, t1 = w + (d * 0.045 + fwd * 0.03 + inner * 0.01) * g, w + (d * 0.085 + fwd * 0.05 + inner * 0.016) * g
    parts += [seg('g', t0, t1, 0.0135 * g, 0.012 * g, K, 12), ball('g', 0.012 * g, t1, K, (1, 1, 1), 12, 8)]
    fuse('Glove' + side, parts, K, 0.0025, 0.7, 10, 0.3, parent=wr)
    return sh


def thigh_seam(name, body, x, R, m, parent, z_lo=0.6, z_hi=1.05, n=48):
    """A seam tape along where the thigh (a column of radius R round x) enters the body."""
    mi = body.matrix_world.inverted()
    def inside(p):
        ok, loc, nrm, _ = body.closest_point_on_mesh(mi @ p)
        return ok and (mi @ p - loc).dot(nrm) < 0
    pts = []
    for k in range(n + 1):
        t = 2 * math.pi * (k % n) / n
        c, s = math.cos(t), math.sin(t)
        lo, hi = z_lo, z_hi
        col = lambda z: Vector((x + R * c, R * s, z))
        if inside(col(lo)) or not inside(col(hi)):
            continue
        for _ in range(22):
            mid = (lo + hi) / 2
            if inside(col(mid)):
                hi = mid
            else:
                lo = mid
        pts.append(tuple(Vector((x + (R + 0.002) * c, (R + 0.002) * s, hi))))
    if len(pts) > 3:
        if pts[0] != pts[-1]:
            pts.append(pts[0])
        tube(name, pts, 0.0042, m, parent)


def leg8(side, sx, pelvis, body, M, SEAM, SOLE, hip_z, knee_z, r, hip_x=0.1):
    S, K = M['S'], M['K']
    x = sx * hip_x
    hem_top, hem_bot = 0.2, 0.168
    hp = empty('Hip' + side, (x, 0, hip_z), pelvis)
    ball('HipBall' + side, r * 1.04, (x, 0, hip_z + 0.02), S, (1, 1, 1), 32, 20, parent=hp)
    seg('UpperLeg' + side, (x, 0, hip_z + 0.02), (x, 0, knee_z), r * 1.06, r * 0.95, S, 32, hp)
    thigh_seam('ThighSeam' + side, body, x, r * 1.06, SEAM, hp)
    seam_line('LegSeamUpper' + side, (x + sx * r * 1.06, 0, hip_z - 0.02), (x + sx * r * 0.95, 0, knee_z), SEAM, hp)
    kn = empty('Knee' + side, (x, 0, knee_z), hp)
    ball('KneeBall' + side, r * 0.92, (x, 0, knee_z), S, (1, 1, 1), 16, 10, parent=kn)
    seg('LowerLeg' + side, (x, 0, knee_z), (x, 0, hem_top), r * 0.93, r * 0.98, S, 24, kn)
    seam_line('LegSeamLower' + side, (x + sx * r * 0.93, 0, knee_z), (x + sx * r * 0.98, 0, hem_top + 0.01), SEAM, kn)
    seg('Hem' + side, (x, 0, hem_top), (x, 0, hem_bot), r * 0.98, 0.081, S, 24, kn)
    fold('HemBand' + side, Vector((x, 0, hem_bot + 0.009)), (0, 0, 1), 0.083, 0.0065, SEAM, kn)
    parts = [seg('b', (x, -0.004, 0.06), (x, -0.004, 0.205), 0.072, 0.07, K, 24),
             blob('b', (0.07, 0.125, 0.056), (x, -0.035, 0.07), K, 0.65),
             blob('b', (0.064, 0.05, 0.044), (x, -0.12, 0.056), K, 0.7)]
    fuse('Boot' + side, parts, K, 0.003, 0.6, 6, 0.3, parent=kn)
    # a sole with rounded corners and edges, and its tread
    blob('Sole' + side, (0.077, 0.14, 0.017), (x, -0.035, 0.018), SOLE, 0.45, segs=40, rings=16, parent=kn)
    for i in range(6):
        box('Tread' + side, (0.11, 0.018, 0.007), (x, -0.035 - 0.1 + i * 0.04, 0.0045), SOLE, 0.0025, 1, parent=kn)
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
    root = empty('HazmatSuit6', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.84), root)
    chest = empty('Chest', (0, 0, 0.93), pelvis)
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
    surface_strip('SideSeam', body, SEAM, (0, 0, 1.12), (math.radians(90), 0, 0), (3, 3, 0.008), 0.004, chest)
    zx = 0.072
    surface_strip('ZipFlap', body, SEAM, (zx + 0.022, -0.3, 1.15), (0, 0, 0), (0.03, 0.4, 0.5), 0.004, chest)
    surface_strip('Zip', body, K, (zx, -0.3, 1.15), (0, 0, 0), (0.013, 0.4, 0.5), 0.0065, chest)
    p, n = surface_point(body, (zx, -1.0, 1.36), (0, 1, 0))
    box('ZipPull', (0.012, 0.006, 0.024), p + n * 0.008 + Vector((0, 0, -0.012)), G, 0.002, 1, parent=chest)
    nt_c = Vector((-0.105, -0.2, 1.3))
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
    # the hood: one smooth shape from inside the shoulders, through the neck, to the dome (no collar ring)
    Z = S5.Z
    hood = loft('Hood', [
        (1.3, 0.2, 0.15, 0.012),
        (1.37, 0.205, 0.16, 0.014),
        (1.42, 0.188, 0.164, 0.016),
        (1.46, 0.176, 0.172, 0.018),
        (Z, 0.178, 0.182, 0.02),
        (Z + 0.07, 0.17, 0.176, 0.02),
        (Z + 0.135, 0.14, 0.148, 0.02),
        (Z + 0.18, 0.09, 0.098, 0.02),
        (Z + 0.2, 0.03, 0.035, 0.02),
    ], S, segs=56, e=0.9, steps=5, parent=neck)
    S3.build_window(hood, neck, F['glass'], K)
    surface_strip('HoodSeam', hood, SEAM, (0, 0.02, Z + 0.1), (0, math.radians(90), 0), (3, 3, 0.008), 0.003, neck)
    build_head(neck, M, F, skin)
    build_masks(neck, M, F)

    for side, sx in (('L', 1), ('R', -1)):
        arm8(side, sx, chest, M, SEAM, (sx * 0.255, 0, 1.355), 0.255, 0.235, 10, 0.074, stripes=(side == 'L'))
        leg8(side, sx, pelvis, body, M, SEAM, SOLE, 0.82, 0.45, 0.102)
    bake_suit(root, M, SEAM, SOLE)
    export('HazmatSuit6')


if __name__ == '__main__':
    build_suit()
    print('done')
