"""Round nine of the player's suit (2026-11-06): HazmatSuit7, round eight (HazmatSuit6) with the owner's notes:
  - the zip looks like a zip: a dark fabric tape down to the crotch, metal teeth alternating either side, a slider with
    a pull tab at the top and a stop at the bottom, so it no longer just stops
  - the arms and legs are shaped, not cylinders: a deltoid cap at each shoulder, the upper arm narrowing to the elbow,
    the forearm fuller then flaring to the cuff, the thigh fuller at the top, a shaped calf, each limb end matched to
    its joint ball so a bent elbow or knee stays continuous; the limb seams follow the new surfaces
Everything else as round eight.
Run:  blender -b -P Tools/Blender/style_lab_characters9.py   (about two minutes, most of it the bake)"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(__file__))
from style_lab_models import mat, box, cyl, ball, empty, finish, activate, export, reset, tube
from style_lab_characters import mats, seg, arm_points
from style_lab_characters2 import blob, fold
import style_lab_characters3 as S3
import style_lab_characters5 as S5
from style_lab_characters5 import build_masks
from style_lab_characters6 import (surface_strip, surface_point, marker, build_head, bake_suit, SEAM_HEX, SOLE_HEX)
from style_lab_characters7 import fuse, loft
from style_lab_characters8 import strip_along, thigh_seam, GLOVE


def limb_loft(name, a, b, radii, m, parent, segs=28, steps=4):
    """A shaped limb piece from a to b: round cross-sections whose radius follows radii [(t 0..1, r)] (Catmull-Rom).
    Built along its own Z axis (so the bake's creases, which run along object Z, still work), placed by location and
    rotation."""
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    pts = [Vector((t, r)) for t, r in radii]
    def cr(p0, p1, p2, p3, t):
        return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
    dense = []
    for i in range(len(pts) - 1):
        p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, len(pts) - 1)]
        for k in range(steps):
            dense.append(cr(p0, p1, p2, p3, k / steps))
    dense.append(pts[-1])
    verts, faces = [], []
    for t, r in dense:
        z = -L / 2 + t * L
        for j in range(segs):
            ang = 2 * math.pi * j / segs
            verts.append((r * math.cos(ang), r * math.sin(ang), z))
    n = len(dense)
    for i in range(n - 1):
        for j in range(segs):
            p, q = i * segs + j, i * segs + (j + 1) % segs
            faces.append((p, q, q + segs, p + segs))
    c0, c1 = len(verts), len(verts) + 1
    verts += [(0, 0, -L / 2), (0, 0, L / 2)]
    for j in range(segs):
        faces.append((c0, (j + 1) % segs, j))
        faces.append((c1, (n - 1) * segs + j, (n - 1) * segs + (j + 1) % segs))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    o.location = (a + b) / 2
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = (b - a).normalized().to_track_quat('Z', 'Y')
    return finish(o, m, 0, smooth=180, parent=parent)


def boxes_mesh(name, items, m, parent):
    """Many small boxes in one mesh: items of (centre, (sx, sy, sz) half sizes, (right, up, out) axes)."""
    bm = bmesh.new()
    for c, (hx, hy, hz), (ax, ay, az) in items:
        vs = []
        for dx in (-1, 1):
            for dy in (-1, 1):
                for dz in (-1, 1):
                    vs.append(bm.verts.new(c + ax * dx * hx + ay * dy * hy + az * dz * hz))
        idx = lambda dx, dy, dz: vs[(dx > 0) * 4 + (dy > 0) * 2 + (dz > 0)]
        for f in (((-1, -1, -1), (-1, 1, -1), (1, 1, -1), (1, -1, -1)), ((-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)),
                  ((-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)), ((-1, 1, -1), (-1, 1, 1), (1, 1, 1), (1, 1, -1)),
                  ((-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)), ((1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1))):
            bm.faces.new([idx(*v) for v in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return finish(o, m, 0, smooth=1, parent=parent)


def zipper(body, M, SEAM, chest, zx=0.072, z_top=1.385, z_bot=0.885):
    K, G = M['K'], M['G']
    # the storm flap beside it, the fabric tape, then the teeth, slider and stop
    surface_strip('ZipFlap', body, SEAM, (zx + 0.024, -0.3, (z_top + z_bot) / 2), (0, 0, 0), (0.03, 0.4, z_top - z_bot + 0.02), 0.004, chest)
    surface_strip('ZipTape', body, K, (zx, -0.3, (z_top + z_bot) / 2), (0, 0, 0), (0.022, 0.4, z_top - z_bot), 0.0055, chest)
    teeth = []
    z, i = z_bot + 0.012, 0
    while z < z_top - 0.03:
        p, n = surface_point(body, (zx, -1.0, z), (0, 1, 0))
        right = Vector((1, 0, 0))
        right = (right - n * right.dot(n)).normalized()
        up = n.cross(right).normalized()
        if up.z < 0:
            up = -up
        side = -1 if i % 2 == 0 else 1
        teeth.append((p + n * 0.0072 + right * side * 0.0028, (0.0034, 0.0019, 0.0016), (right, up, n)))
        z += 0.0055
        i += 1
    boxes_mesh('ZipTeeth', teeth, G, chest)
    # the slider near the top, its pull tab hanging from it, and the stop at the bottom
    p, n = surface_point(body, (zx, -1.0, z_top - 0.03), (0, 1, 0))
    blob('ZipSlider', (0.0085, 0.006, 0.013), p + n * 0.0105, G, 0.55, parent=chest)
    tab = blob('ZipTab', (0.0055, 0.0022, 0.014), p + n * 0.0145 + Vector((0, 0, -0.018)), G, 0.5, parent=chest)
    p, n = surface_point(body, (zx, -1.0, z_bot + 0.006), (0, 1, 0))
    blob('ZipStop', (0.0085, 0.0045, 0.0055), p + n * 0.008, G, 0.55, parent=chest)


def arm9(side, sx, chest, M, SEAM, sh_pos, up, lo, splay, stripes=False):
    S, K, D = M['S'], M['K'], M['D']
    s, e, w = arm_points(sx, sh_pos, up, lo, splay)
    d_up, d = (e - s).normalized(), (w - e).normalized()
    out = sx * Vector((-d.z, 0, d.x)).normalized()
    out_u = sx * Vector((-d_up.z, 0, d_up.x)).normalized()
    sh = empty('Shoulder' + side, s, chest)
    # a deltoid cap rounding the shoulder into the body
    blob('Deltoid' + side, (0.088, 0.084, 0.08), s + Vector((sx * 0.004, 0, 0.012)), S, 0.82, parent=sh)
    upper = limb_loft('UpperArm' + side, s, e, [(0, 0.082), (0.18, 0.083), (0.55, 0.077), (0.85, 0.071), (1, 0.068)], S, sh)
    fold('ArmholeSeam' + side, s + d_up * 0.035, d_up, 0.083, 0.0034, SEAM, sh)
    el = empty('Elbow' + side, e, sh)
    elb = ball('ElbowBall' + side, 0.068, e, S, (1, 1, 1), 28, 18, parent=el)
    lower = limb_loft('LowerArm' + side, e, w, [(0, 0.067), (0.25, 0.074), (0.55, 0.076), (0.85, 0.079), (1, 0.08)], S, el)
    if stripes:
        a, b = s + d_up * 0.045, w - d * 0.03
        fwd_v = Vector((0, -1, 0))
        tilt = math.radians(35)
        ou = (out_u * math.cos(tilt) + fwd_v * math.sin(tilt)).normalized()
        ol = (out * math.cos(tilt) + fwd_v * math.sin(tilt)).normalized()
        for part, joint, oo in ((upper, sh, ou), (elb, el, ol), (lower, el, ol)):
            strip_along('SleeveBand' + side, part, D, a, b, oo, 0.056, 0.004, joint)
            for i, sm in enumerate(M['st']):
                off = (i - 1) * 0.016
                sd = oo.cross((b - a).normalized()).normalized()
                strip_along('SleeveStripe%d' % i + side, part, sm, a + sd * off, b + sd * off, oo, 0.012, 0.0072, joint)
    else:
        strip_along('ArmSeamUpper' + side, upper, SEAM, s + d_up * 0.04, e, out_u, 0.007, 0.0035, sh)
        strip_along('ArmSeamLower' + side, lower, SEAM, e, w - d * 0.03, out, 0.007, 0.0035, el)
    seg('GloveRing' + side, w - d * 0.025, w + d * 0.01, 0.086, 0.086, K, 24, el)
    wr = empty('Wrist' + side, w, el)
    g = GLOVE
    inner, fwd = Vector((-sx, 0, 0)), Vector((0, -1, 0))
    parts = [seg('g', w - d * 0.03, w + d * 0.022 * g, 0.059, 0.05 * g, K, 20),
             blob('g', (0.026 * g, 0.042 * g, 0.046 * g), w + d * 0.06 * g, K, 0.72),
             blob('g', (0.023 * g, 0.027 * g, 0.034 * g), w + (d * 0.125 + Vector((0, 0.012, 0)) + inner * 0.004) * g, K, 0.72)]
    i0, i1 = w + (d * 0.1 + fwd * 0.024) * g, w + (d * 0.15 + fwd * 0.026 + inner * 0.006) * g
    parts += [seg('g', i0, i1, 0.0125 * g, 0.0115 * g, K, 12), ball('g', 0.0115 * g, i1, K, (1, 1, 1), 12, 8)]
    t0, t1 = w + (d * 0.045 + fwd * 0.03 + inner * 0.01) * g, w + (d * 0.085 + fwd * 0.05 + inner * 0.016) * g
    parts += [seg('g', t0, t1, 0.0135 * g, 0.012 * g, K, 12), ball('g', 0.012 * g, t1, K, (1, 1, 1), 12, 8)]
    fuse('Glove' + side, parts, K, 0.0025, 0.7, 10, 0.3, parent=wr)
    return sh


def leg9(side, sx, pelvis, body, M, SEAM, SOLE, hip_z, knee_z, hip_x=0.1):
    S, K = M['S'], M['K']
    x = sx * hip_x
    hem_top, hem_bot = 0.2, 0.168
    outv = Vector((sx, 0, 0))
    hp = empty('Hip' + side, (x, 0, hip_z), pelvis)
    ball('HipBall' + side, 0.106, (x, 0, hip_z + 0.02), S, (1, 1, 1), 32, 20, parent=hp)
    thigh = limb_loft('UpperLeg' + side, (x, 0, hip_z + 0.02), (x, 0, knee_z),
                      [(0, 0.108), (0.25, 0.107), (0.6, 0.1), (0.88, 0.092), (1, 0.09)], S, hp, segs=36)
    thigh_seam('ThighSeam' + side, body, x, 0.108, SEAM, hp)
    strip_along('LegSeamUpper' + side, thigh, SEAM, Vector((x, 0, hip_z - 0.02)), Vector((x, 0, knee_z)), outv, 0.007, 0.0035, hp)
    kn = empty('Knee' + side, (x, 0, knee_z), hp)
    ball('KneeBall' + side, 0.09, (x, 0, knee_z), S, (1, 1, 1), 28, 18, parent=kn)
    calf = limb_loft('LowerLeg' + side, (x, 0, knee_z), (x, 0, hem_top),
                     [(0, 0.089), (0.22, 0.094), (0.45, 0.095), (0.75, 0.094), (1, 0.1)], S, kn, segs=32)
    strip_along('LegSeamLower' + side, calf, SEAM, Vector((x, 0, knee_z)), Vector((x, 0, hem_top + 0.01)), outv, 0.007, 0.0035, kn)
    seg('Hem' + side, (x, 0, hem_top), (x, 0, hem_bot), 0.1, 0.081, S, 28, kn)
    fold('HemBand' + side, Vector((x, 0, hem_bot + 0.009)), (0, 0, 1), 0.083, 0.0065, SEAM, kn)
    parts = [seg('b', (x, -0.004, 0.06), (x, -0.004, 0.205), 0.072, 0.07, K, 24),
             blob('b', (0.07, 0.125, 0.056), (x, -0.035, 0.07), K, 0.65),
             blob('b', (0.064, 0.05, 0.044), (x, -0.12, 0.056), K, 0.7)]
    fuse('Boot' + side, parts, K, 0.003, 0.6, 6, 0.3, parent=kn)
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
    root = empty('HazmatSuit7', (0, 0, 0))
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
    zipper(body, M, SEAM, chest)
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
        arm9(side, sx, chest, M, SEAM, (sx * 0.255, 0, 1.355), 0.255, 0.235, 10, stripes=(side == 'L'))
        leg9(side, sx, pelvis, body, M, SEAM, SOLE, 0.82, 0.45)
    bake_suit(root, M, SEAM, SOLE)
    export('HazmatSuit7')


if __name__ == '__main__':
    build_suit()
    print('done')
