"""Round six of the player's suit (2026-11-06): HazmatSuit4. The owner kept the round-five suit and the Worker's painted
face, and asked for these improvements (simple, but not too simple):
Suit
  1. seams instead of the ring folds: side and shoulder seams, the outside of each leg and arm, round the armholes,
     over the top of the hood (thin raised tape a shade darker than the suit)
  2. a faint fabric weave and soft creases at the elbows and knees
  3. gloves with a thumb and grouped fingers (a separate index finger and three together), boots with a thick sole,
     tread and a toe cap
  4. wear and dirt: grime rising from the boots, scuffs on the gloves, knees and boots, dust on what faces up, darker
     where parts meet
  5. a name tape on the chest and the crew number on the back hump (the text itself is made in Unity:
     HazmatAppearance, from the markers NameTape*/Number* exported here)
  2 and 4 are baked into one picture (T_Suit_Albedo.png) with Cycles from a procedural material, onto UVs shared by
  every suit and rubber part, so Unity only needs the plain Lit shader (material SL_SuitBaked).
Face (the player chooses; HazmatAppearance in Unity shows the choice)
  painted eyes and mouth (one of three sets in T_FacePaint_Atlas, with a blink frame each), painted shading
  (T_FaceShade) always, and parts: Nose_Round/Long/Button, Brows_Flat/Worried/Thick, Hair_Short/Fringe/Receding/Buzz,
  FacialHair_None/Moustache/Stubble/Beard (stubble is painted, T_FaceStubble), Glasses, Mask_Half/Mask_Full.
Run:  blender -b -P Tools/Blender/style_lab_characters6.py   (about a minute or two for the bake)"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from style_lab_models import mat, box, cyl, ball, empty, finish, activate, export, reset, tube, lin, ROOT
from style_lab_characters import mats, seg, arm_points
from style_lab_characters2 import blob, fold, company_band
import style_lab_characters3 as S3
import style_lab_characters5 as S5
from style_lab_characters5 import HC, FY, face_decal, brow, build_masks

TEX = os.path.join(ROOT, 'Assets', 'Backrooms', 'Textures', 'StyleLab')
SUIT_HEX, SEAM_HEX, RUBBER_HEX, SOLE_HEX = '#F0A21E', '#B97813', '#1E1C1A', '#3A3836'


# ------------------------------------------------------------------------------------------------ helpers
def surface_strip(name, src, m, loc, rot, size, inflate=0.004, parent=None):
    """The part of src's surface inside a box (centre loc, turned by rot, half... full sizes `size`), lifted by
    inflate: seams, patches and tapes that follow the body's shape."""
    me = src.data.copy()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.matrix_world = src.matrix_world.copy()
    for v in me.vertices:
        v.co += v.normal * inflate
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    cut = bpy.context.active_object
    cut.scale = size
    md = o.modifiers.new('Cut', 'BOOLEAN')
    md.operation, md.object, md.solver = 'INTERSECT', cut, 'EXACT'
    activate(o)
    bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.data.objects.remove(cut, do_unlink=True)
    return finish(o, m, 0, smooth=70, parent=parent)


def surface_point(obj, origin, direction):
    mi = obj.matrix_world.inverted()
    hit, loc, nrm, _ = obj.ray_cast(mi @ Vector(origin), (mi.to_3x3() @ Vector(direction)).normalized())
    mw = obj.matrix_world
    return (mw @ loc, (mw.to_3x3() @ nrm).normalized()) if hit else (Vector(origin), Vector(direction) * -1)


def marker(name, p, parent):
    return empty(name, tuple(p), parent)


def seam_line(name, a, b, m, parent, r=0.0032):
    return tube(name, [tuple(a), tuple(b)], r, m, parent)


# ------------------------------------------------------------------------------------------------ limbs
def arm6(side, sx, chest, M, SEAM, sh_pos, up, lo, splay, r, bell=1.1):
    S, K, G = M['S'], M['K'], M['G']
    s, e, w = arm_points(sx, sh_pos, up, lo, splay)
    d_up, d = (e - s).normalized(), (w - e).normalized()
    out = sx * Vector((-d.z, 0, d.x)).normalized()           # the outside of the arm
    sh = empty('Shoulder' + side, s, chest)
    ball('ShoulderBall' + side, r, s, S, (1, 1, 1), 16, 10, parent=sh)
    seg('UpperArm' + side, s, e, r, r * 0.94, S, 16, sh)
    fold('ArmholeSeam' + side, s + d_up * 0.035, d_up, r * 1.0, 0.0034, SEAM, sh)
    seam_line('ArmSeamUpper' + side, s + d_up * 0.04 + out * r * 1.0, e + out * r * 0.95, SEAM, sh)
    from style_lab_characters import limb_stripes
    limb_stripes('Arm' + side, s, e, r * 0.97, 0.62, M, sh, 16)
    el = empty('Elbow' + side, e, sh)
    ball('ElbowBall' + side, r * 0.9, e, S, (1, 1, 1), 16, 10, parent=el)
    seg('LowerArm' + side, e, w, r * 0.92, r * 0.92 * bell, S, 16, el)
    seam_line('ArmSeamLower' + side, e + out * r * 0.92, w - d * 0.03 + out * r * 0.92 * bell, SEAM, el)
    seg('GloveRing' + side, w - d * 0.025, w + d * 0.01, 0.082, 0.082, K, 20, el)
    wr = empty('Wrist' + side, w, el)
    # the glove: a gauntlet, the palm, a separate index finger, the other three together, a thumb. In this pose the
    # palm faces the leg (inward), the thumb points forward (-Y), the fingers down the arm.
    inner, fwd = Vector((-sx, 0, 0)), Vector((0, -1, 0))
    seg('Gauntlet' + side, w - d * 0.02, w + d * 0.018, 0.064, 0.058, K, 20, wr)
    blob('Palm' + side, (0.026, 0.042, 0.046), w + d * 0.06, K, 0.72, parent=wr)
    blob('Fingers' + side, (0.023, 0.027, 0.034), w + d * 0.125 + Vector((0, 0.012, 0)) + inner * 0.004, K, 0.72, parent=wr)
    i0, i1 = w + d * 0.1 + fwd * 0.024, w + d * 0.15 + fwd * 0.026 + inner * 0.006
    seg('IndexFinger' + side, i0, i1, 0.0125, 0.0115, K, 12, wr)
    ball('IndexTip' + side, 0.0115, i1, K, (1, 1, 1), 12, 8, parent=wr)
    t0, t1 = w + d * 0.045 + fwd * 0.03 + inner * 0.01, w + d * 0.085 + fwd * 0.05 + inner * 0.016
    seg('Thumb' + side, t0, t1, 0.0135, 0.012, K, 12, wr)
    ball('ThumbTip' + side, 0.012, t1, K, (1, 1, 1), 12, 8, parent=wr)
    return sh


def leg6(side, sx, pelvis, M, SEAM, SOLE, hip_z, knee_z, ankle_z, r, flare=1.1, hip_x=0.1):
    S, K = M['S'], M['K']
    x = sx * hip_x
    hp = empty('Hip' + side, (x, 0, hip_z), pelvis)
    seg('UpperLeg' + side, (x, 0, hip_z + 0.03), (x, 0, knee_z), r, r * 0.95, S, 16, hp)
    seam_line('LegSeamUpper' + side, (x + sx * r, 0, hip_z + 0.02), (x + sx * r * 0.95, 0, knee_z), SEAM, hp)
    kn = empty('Knee' + side, (x, 0, knee_z), hp)
    ball('KneeBall' + side, r * 0.92, (x, 0, knee_z), S, (1, 1, 1), 16, 10, parent=kn)
    seg('LowerLeg' + side, (x, 0, knee_z), (x, 0, ankle_z), r * 0.93, r * 0.93 * flare, S, 16, kn)
    seam_line('LegSeamLower' + side, (x + sx * r * 0.93, 0, knee_z), (x + sx * r * 0.93 * flare, 0, ankle_z + 0.06), SEAM, kn)
    seg('Overboot' + side, (x, 0, 0.16), (x, -0.01, 0.075), 0.104, 0.114, S, 20, kn)
    # the boot: an upper, a thick sole with tread, a toe cap
    blob('BootUpper' + side, (0.07, 0.125, 0.058), (x, -0.035, 0.078), K, 0.65, parent=kn)
    box('Sole' + side, (0.15, 0.275, 0.03), (x, -0.035, 0.019), SOLE, 0.009, 2, parent=kn)
    for i in range(6):
        box('Tread' + side, (0.128, 0.02, 0.008), (x, -0.035 - 0.105 + i * 0.042, 0.004), SOLE, 0.002, 1, parent=kn)
    blob('ToeCap' + side, (0.062, 0.042, 0.034), (x, -0.13, 0.058), SOLE, 0.65, parent=kn)
    return kn


# ------------------------------------------------------------------------------------------------ the head
def build_head(neck, M, F, skin):
    grp = empty('HeadParts', HC, neck)
    K, E, H, R = M['K'], F['eye'], F['hair'], M['R']
    head = blob('Head', (0.092, 0.088, 0.108), HC, skin, 0.82, segs=40, rings=26, parent=grp)
    for sx in (-1, 1):
        ball('Ear', 0.02, (sx * 0.091, HC.y + 0.008, HC.z - 0.004), skin, (0.55, 1, 1.15), 10, 6, parent=grp)
    face_decal('Paint_Shade', head, F['shade'], grp, lift=0.0005)
    face_decal('Paint_Features', head, F['paint'], grp, lift=0.0009)
    # noses
    g = empty('Nose_Round', HC, neck)
    blob('Nose', (0.012, 0.014, 0.017), (0, FY - 0.006, HC.z - 0.012), skin, 0.75, parent=g)
    g = empty('Nose_Long', HC, neck)
    blob('Nose', (0.0105, 0.017, 0.024), (0, FY - 0.008, HC.z - 0.01), skin, 0.7, parent=g)
    g = empty('Nose_Button', HC, neck)
    ball('Nose', 0.0105, (0, FY - 0.004, HC.z - 0.016), skin, (1, 0.9, 0.85), 12, 8, parent=g)
    # brows
    for name, z, tilt, thick in (('Flat', 0.033, 4, 0.85), ('Worried', 0.037, -14, 0.8), ('Thick', 0.032, 6, 1.5)):
        g = empty('Brows_' + name, HC, neck)
        for sx in (-1, 1):
            brow(g, sx, H, z, tilt, thick)
    # hair
    g = empty('Hair_Short', HC, neck)
    blob('Hair', (0.094, 0.09, 0.085), HC + Vector((0, 0.012, 0.035)), H, 0.8, parent=g)
    g = empty('Hair_Fringe', HC, neck)
    blob('Hair', (0.095, 0.09, 0.082), HC + Vector((0, 0.014, 0.04)), H, 0.8, parent=g)
    for x, zz, rz in ((-0.045, 0.062, 30), (-0.015, 0.068, 22), (0.018, 0.07, 14)):
        f = blob('Fringe', (0.026, 0.014, 0.012), (x, FY + 0.018, HC.z + zz), H, 0.7, parent=g)
        f.rotation_euler = (0, math.radians(rz), 0)
    g = empty('Hair_Receding', HC, neck)
    blob('Hair', (0.094, 0.09, 0.07), HC + Vector((0, 0.03, 0.045)), H, 0.8, parent=g)
    g = empty('Hair_Buzz', HC, neck)
    blob('Hair', (0.093, 0.089, 0.088), HC + Vector((0, 0.01, 0.03)), H, 0.85, parent=g)
    # facial hair
    empty('FacialHair_None', HC, neck)
    g = empty('FacialHair_Moustache', HC, neck)
    blob('Moustache', (0.03, 0.008, 0.008), (0, FY - 0.001, HC.z - 0.034), H, 0.6, parent=g, bend=6.0)
    g = empty('FacialHair_Stubble', HC, neck)
    face_decal('Paint_Stubble', head, F['stubble'], g, lift=0.0007)
    g = empty('FacialHair_Beard', HC, neck)
    blob('Beard', (0.08, 0.07, 0.034), (0, HC.y - 0.016, HC.z - 0.072), H, 0.7, parent=g)
    blob('Moustache', (0.03, 0.008, 0.008), (0, FY - 0.001, HC.z - 0.034), H, 0.6, parent=g, bend=6.0)
    # glasses
    g = empty('Glasses', HC, neck)
    for sx in (-1, 1):
        c = Vector((sx * 0.034, FY - 0.006, HC.z + 0.01))
        w, h = 0.021, 0.016
        pts = [c + Vector((dx, 0, dz)) for dx, dz in ((-w, -h), (w, -h), (w, h), (-w, h), (-w, -h))]
        tube('GlassesRim', [tuple(p) for p in pts], 0.0025, K, g)
        tube('GlassesArm', [tuple(c + Vector((sx * w, 0, h * 0.6))), (sx * 0.088, HC.y - 0.01, HC.z + 0.016)], 0.002, K, g)
    tube('GlassesBridge', [(-0.013, FY - 0.008, HC.z + 0.014), (0.013, FY - 0.008, HC.z + 0.014)], 0.0022, K, g)


# ------------------------------------------------------------------------------------------------ the bake
def bake_suit(root, M, SEAM, SOLE):
    """Fabric, creases, dirt, scuffs, dust and the darkening where parts meet, baked into one picture over UVs shared
    by every suit, seam and rubber part."""
    skip = ('Mask_', 'Glasses', 'HeadParts', 'Nose_', 'Brows_', 'Hair_', 'FacialHair_')
    bases = {M['S'].name: SUIT_HEX, SEAM.name: SEAM_HEX, M['K'].name: RUBBER_HEX, SOLE.name: SOLE_HEX}
    objs = []
    for o in bpy.data.objects:
        if o.type != 'MESH' or not o.data.materials or o.data.materials[0] is None:
            continue
        if o.data.materials[0].name not in bases:
            continue
        a, ok = o.parent, True
        while a is not None:
            if a.name.startswith(skip):
                ok = False
            a = a.parent
        if ok:
            objs.append(o)
    wear_of = (('Palm', 1.0), ('Fingers', 1.0), ('Index', 1.0), ('Thumb', 1.0), ('Gauntlet', 0.8), ('Boot', 0.9),
               ('Sole', 0.7), ('Tread', 0.7), ('ToeCap', 1.0), ('KneeBall', 0.8), ('LowerLeg', 0.55),
               ('ElbowBall', 0.5), ('LowerArm', 0.35), ('Overboot', 0.6), ('Glove', 1.0), ('Hem', 0.6))
    for o in objs:
        o['base_rgb'] = lin(bases[o.data.materials[0].name])
        o['wear'] = next((w for k, w in wear_of if o.name.startswith(k)), 0.12)
        ce = 0.0
        if o.name.startswith(('UpperArm', 'UpperLeg')):
            ce = 1.0
        elif o.name.startswith(('LowerArm', 'LowerLeg')):
            ce = -1.0
        o['crease_end'] = ce
        o['half_len'] = max(0.01, max(abs(v.co.z) for v in o.data.vertices))
        if not o.data.uv_layers:
            o.data.uv_layers.new(name='UVMap')
    # one UV space for all of them
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002)
    bpy.ops.uv.pack_islands(margin=0.002, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    # the material that is baked
    img = bpy.data.images.new('T_Suit_Albedo', 2048, 2048, alpha=False)
    m = bpy.data.materials.new('SL_SuitBaked')
    m.use_nodes = True
    nt = m.node_tree
    N, L = nt.nodes, nt.links
    for n in list(N):
        N.remove(n)
    def node(t, **kw):
        n = N.new(t)
        for k, v in kw.items():
            setattr(n, k, v)
        return n
    def attr(name):
        return node('ShaderNodeAttribute', attribute_type='OBJECT', attribute_name=name)
    def math_(op, a, b=None, clamp=False):
        n = node('ShaderNodeMath', operation=op, use_clamp=clamp)
        for i, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L.new(v, n.inputs[i])
        return n.outputs[0]
    def maprange(v, a0, a1, b0, b1):
        n = node('ShaderNodeMapRange', clamp=True)
        L.new(v, n.inputs['Value'])
        n.inputs['From Min'].default_value, n.inputs['From Max'].default_value = a0, a1
        n.inputs['To Min'].default_value, n.inputs['To Max'].default_value = b0, b1
        return n.outputs['Result']
    def mix(a, b, f):
        n = node('ShaderNodeMix', data_type='RGBA', blend_type='MIX', clamp_factor=True)
        for sock, v in ((n.inputs[0], f), (n.inputs[6], a), (n.inputs[7], b)):
            if isinstance(v, (int, float)):
                sock.default_value = v
            elif isinstance(v, tuple):
                sock.default_value = (*v, 1.0)
            else:
                L.new(v, sock)
        return n.outputs[2]
    def mulc(a, f):
        n = node('ShaderNodeMix', data_type='RGBA', blend_type='MULTIPLY', clamp_factor=True)
        n.inputs[0].default_value = 1.0
        L.new(a, n.inputs[6])
        if isinstance(f, (int, float)):
            n.inputs[7].default_value = (f, f, f, 1)
        else:
            cn = node('ShaderNodeCombineColor')
            for i in range(3):
                L.new(f, cn.inputs[i])
            L.new(cn.outputs[0], n.inputs[7])
        return n.outputs[2]
    def noise(vec, scale, detail=2.0):
        n = node('ShaderNodeTexNoise')
        n.inputs['Scale'].default_value = scale
        n.inputs['Detail'].default_value = detail
        L.new(vec, n.inputs['Vector'])
        return n.outputs['Fac']
    def wave(vec, scale, direction, distortion=0.0):
        n = node('ShaderNodeTexWave', wave_type='BANDS', bands_direction=direction)
        n.inputs['Scale'].default_value = scale
        n.inputs['Distortion'].default_value = distortion
        n.inputs['Detail'].default_value = 1.0
        L.new(vec, n.inputs['Vector'])
        return n.outputs['Fac']
    tc = node('ShaderNodeTexCoord')
    geo = node('ShaderNodeNewGeometry')
    obj, pos, nrm = tc.outputs['Object'], geo.outputs['Position'], geo.outputs['Normal']
    col = attr('base_rgb').outputs['Color']
    # fabric: a faint weave and a slow mottling
    weave = math_('ADD', wave(obj, 160.0, 'X'), wave(obj, 160.0, 'Y'))
    col = mulc(col, maprange(weave, 0.0, 2.0, 0.97, 1.03))
    col = mulc(col, maprange(noise(obj, 7.0), 0.3, 0.7, 0.96, 1.04))
    # soft creases at the elbow and knee end of each limb piece (object Z runs along the limb)
    sep = node('ShaderNodeSeparateXYZ')
    L.new(obj, sep.inputs[0])
    t = math_('DIVIDE', math_('MULTIPLY', sep.outputs['Z'], attr('crease_end').outputs['Fac']), attr('half_len').outputs['Fac'])
    cmask = maprange(t, 0.5, 0.97, 0.0, 1.0)
    cwave = wave(obj, 26.0, 'Z', distortion=7.0)
    crease = math_('MULTIPLY', cmask, maprange(cwave, 0.2, 0.8, 1.0, 0.0))
    col = mulc(col, maprange(crease, 0.0, 1.0, 1.0, 0.8))
    # darker where parts meet (ambient occlusion)
    ao = node('ShaderNodeAmbientOcclusion', samples=16, only_local=False)
    ao.inputs['Distance'].default_value = 0.05
    col = mulc(col, maprange(ao.outputs['AO'], 0.0, 1.0, 0.74, 1.0))
    # grime rising from the boots
    psep = node('ShaderNodeSeparateXYZ')
    L.new(pos, psep.inputs[0])
    dirt = maprange(psep.outputs['Z'], 0.62, 0.05, 0.0, 1.0)
    dirt = math_('MULTIPLY', dirt, maprange(noise(pos, 9.0, 4.0), 0.35, 0.65, 0.35, 1.0))
    col = mix(col, (0.05, 0.038, 0.026), math_('MULTIPLY', dirt, 0.5))
    # scuffs where things rub (gloves, knees, boots)
    sc = maprange(noise(pos, 38.0, 3.0), 0.6, 0.67, 0.0, 1.0)
    sc = math_('MULTIPLY', sc, attr('wear').outputs['Fac'])
    col = mix(col, (0.32, 0.3, 0.26), math_('MULTIPLY', sc, 0.55))
    # dust on what faces up
    nsep = node('ShaderNodeSeparateXYZ')
    L.new(nrm, nsep.inputs[0])
    up = math_('POWER', math_('MAXIMUM', nsep.outputs['Z'], 0.0), 3.0)
    dust = math_('MULTIPLY', up, maprange(noise(pos, 22.0), 0.35, 0.7, 0.2, 1.0))
    col = mix(col, (0.36, 0.33, 0.27), math_('MULTIPLY', dust, 0.3))
    em = node('ShaderNodeEmission')
    L.new(col, em.inputs['Color'])
    out = node('ShaderNodeOutputMaterial')
    L.new(em.outputs[0], out.inputs['Surface'])
    tn = node('ShaderNodeTexImage')
    tn.image = img
    N.active = tn
    tn.select = True
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(m)
    sc_ = bpy.context.scene
    sc_.render.engine = 'CYCLES'
    sc_.cycles.device = 'CPU'
    sc_.cycles.samples = 24
    sc_.render.bake.margin = 6
    sc_.render.bake.use_clear = True
    bpy.ops.object.bake(type='EMIT')
    img.filepath_raw = os.path.join(TEX, 'T_Suit_Albedo.png')
    img.file_format = 'PNG'
    img.save()
    print('baked', len(objs), 'parts to', img.filepath_raw)
    # a normal map from the same weave and creases (round ten, 2026-11-06): the light catches the cloth's grain and folds
    nimg = bpy.data.images.new('T_Suit_Normal', 2048, 2048, alpha=False, is_data=True)
    bsdf = node('ShaderNodeBsdfPrincipled')
    bump = node('ShaderNodeBump')
    bump.inputs['Strength'].default_value = 0.55
    bump.inputs['Distance'].default_value = 0.0015
    height = math_('ADD', math_('MULTIPLY', weave, 0.35), math_('MULTIPLY', crease, -1.2))
    L.new(height, bump.inputs['Height'])
    L.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
    L.new(bsdf.outputs[0], out.inputs['Surface'])
    tn.image = nimg
    N.active = tn
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT')
    nimg.filepath_raw = os.path.join(TEX, 'T_Suit_Normal.png')
    nimg.file_format = 'PNG'
    nimg.save()
    L.new(em.outputs[0], out.inputs['Surface'])
    print('baked normals to', nimg.filepath_raw)


# ------------------------------------------------------------------------------------------------ the suit
def build_suit():
    reset()
    M = mats()
    S, K, G, O, W, D, R = M['S'], M['K'], M['G'], M['O'], M['W'], M['D'], M['R']
    SEAM, SOLE = mat('SL_SuitSeam', SEAM_HEX, 0.5), mat('SL_Sole', SOLE_HEX, 0.85)
    F = dict(eye=mat('SL_Eye', '#121010', 0.15), hair=mat('SL_Hair', '#3A2618', 0.8),
             glass=mat('SL_VisorGlass', '#B8D4DA', 0.03), paint=mat('SL_FacePaint', '#FFFFFF', 0.8),
             shade=mat('SL_FaceShade', '#FFFFFF', 0.8), stubble=mat('SL_FaceStubble', '#FFFFFF', 0.8))
    skin = mat('SL_SkinA', '#E9B892', 0.7)
    root = empty('HazmatSuit4', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.84), root)
    hips = blob('Hips', (0.185, 0.145, 0.14), (0, 0, 0.89), S, 0.75, parent=pelvis)
    chest = empty('Chest', (0, 0, 0.93), pelvis)
    torso = blob('Torso', (0.235, 0.17, 0.265), (0, 0, 1.14), S, 0.72, 0.1, parent=chest)
    hump = blob('Hump', (0.19, 0.12, 0.23), (0, 0.2, 1.17), S, 0.72, 0.05, parent=chest)
    cyl('Badge', 0.045, 0.016, (-0.196, 0.2, 1.2), D, 24, rot=(0, math.radians(90), 0), parent=chest)
    for i, sm in enumerate(M['st']):
        box('BadgeStripe%d' % i, (0.008, 0.055, 0.009), (-0.205, 0.2, 1.215 - i * 0.013), sm, parent=chest)
    company_band('Chest', torso, M, 1.24, chest, width=0.095)
    # seams: down the sides and over the shoulders (where the plane y = 0 meets the body), round the hips likewise
    surface_strip('SideSeam', torso, SEAM, (0, 0, 1.14), (math.radians(90), 0, 0), (3, 3, 0.008), 0.004, chest)
    surface_strip('HipSeam', hips, SEAM, (0, 0, 0.89), (math.radians(90), 0, 0), (3, 3, 0.008), 0.004, pelvis)
    # the diagonal zip, a plain dark line
    surface_strip('Zip', torso, K, (0, -0.25, 1.14), (0, math.radians(-58), 0), (3, 0.5, 0.022), 0.007, chest)
    cyl('ExhaleValve', 0.03, 0.028, (-0.13, -0.17, 1.09), K, 16, rot=(math.radians(95), 0, 0), parent=chest)
    cyl('ValveCap', 0.021, 0.016, (-0.13, -0.19, 1.09), G, 16, rot=(math.radians(95), 0, 0), parent=chest)
    # the name tape on the right chest (the text is made in Unity), and where the number goes on the back
    nt_c = Vector((-0.105, -0.2, 1.335))
    surface_strip('NameTape', torso, R, nt_c, (0, 0, 0), (0.13, 0.2, 0.036), 0.006, chest)
    # markers in the surface's own tangent frame there, so the text lies on the body, not through it
    def text_markers(key, p, n, reading, lift):
        right = (reading - n * reading.dot(n)).normalized()
        up = n.cross(right).normalized()
        if up.z < 0:
            up = -up
        p = p + n * lift
        marker(key + 'Anchor', p, chest)
        marker(key + 'Right', p + right * 0.05, chest)
        marker(key + 'Up', p + up * 0.05, chest)
    p, n = surface_point(torso, (nt_c.x, -1.0, nt_c.z), (0, 1, 0))
    text_markers('NameTape', p, n, Vector((1, 0, 0)), 0.0095)
    p, n = surface_point(hump, (0, 1.0, 1.19), (0, -1, 0))
    text_markers('Number', p, n, Vector((-1, 0, 0)), 0.003)

    neck = empty('Neck', (0, 0, 1.4), chest)
    blob('HoodDrape', (0.205, 0.18, 0.065), (0, 0.02, 1.385), S, 0.8, parent=neck)
    hood = blob('Hood', S3.HOOD_S, S3.HOOD_C, S, 0.82, -0.05, segs=48, rings=32, parent=neck)
    S3.build_window(hood, neck, F['glass'], K)
    # a seam over the top of the hood, front to back
    surface_strip('HoodSeam', hood, SEAM, (0, 0.02, S5.Z), (0, math.radians(90), 0), (3, 3, 0.008), 0.003, neck)
    build_head(neck, M, F, skin)
    build_masks(neck, M, F)

    for side, sx in (('L', 1), ('R', -1)):
        arm6(side, sx, chest, M, SEAM, (sx * 0.255, 0, 1.355), 0.255, 0.235, 10, 0.074)
        leg6(side, sx, pelvis, M, SEAM, SOLE, 0.82, 0.45, 0.11, 0.102)
    bake_suit(root, M, SEAM, SOLE)
    export('HazmatSuit4')


if __name__ == '__main__':
    build_suit()
    print('done')
