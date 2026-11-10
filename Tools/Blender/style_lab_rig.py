"""The rigged player suit (2026-11-06): round nine's hazmat suit (style_lab_characters9.py) on a skeleton with the same
bone names and hierarchy as the game's player body (MPFB's game-engine skeleton: Root, pelvis, spine_01..03, neck_01,
head, clavicle/upperarm/lowerarm/hand and the fingers, thigh/calf/foot/ball), placed at the suit's own joints. The game's
body code (FirstPersonBody, RemotePlayer) finds bones by name and measures the rig when it starts, so it drives the suit
as it drives the MakeHuman body: walking, crouching, the hands' IK, the camcorder to the eye, sitting, crawling.
  - The suit is one skinned mesh, each vertex weighted to the bones nearest it among those that move its part (its
    own, its neighbours): blended near the joints so elbows, knees, hips, shoulders and the neck bend smoothly, at most
    four bones a vertex.
  - It is split like the player body, by the bones that move each face, so first person can draw parts into the shadows
    only: Suit.Head (the hood, the window), Suit.ArmL, Suit.ArmR, Suit.Torso, Suit (the legs).
  - The face's choices stay separate pieces fixed to the head bone, each named <choice>.Head (Nose_Round.Head,
    Mask_Full.Head, Paint_Features.Head...), and the name tape and number markers ride the spine; HazmatAppearance
    shows the chosen ones and builds the text.
Writes Assets/Backrooms/Resources/Player/Hazmat/PlayerHazmat.fbx (and Tools/Blender/StyleLab/PlayerHazmat.blend).
Run:  blender -b -P Tools/Blender/style_lab_rig.py   (about two minutes, most of it round nine's bake)"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import style_lab_models as L
import style_lab_characters9 as S9
from style_lab_characters8 import GLOVE

OUT_DIR = os.path.join(L.ROOT, 'Assets', 'Backrooms', 'Resources', 'Player', 'Hazmat')
os.makedirs(OUT_DIR, exist_ok=True)

# build round nine without exporting it
S9.export = lambda name: None
S9.build_suit()

W = lambda name: bpy.data.objects[name].matrix_world.translation.copy()


# ------------------------------------------------------------------------------------------------ the skeleton
def build_armature():
    j = {n: W(n) for n in ('Pelvis', 'Chest', 'Neck', 'ShoulderL', 'ShoulderR', 'ElbowL', 'ElbowR', 'WristL', 'WristR',
                           'HipL', 'HipR', 'KneeL', 'KneeR')}
    bones = {}   # name: (head, tail, parent)
    bones['Root'] = (Vector((0, 0, 0)), Vector((0, 0.03, 0)), None)
    bones['pelvis'] = (Vector((0, 0, 0.84)), Vector((0, 0, 0.95)), 'Root')
    bones['spine_01'] = (Vector((0, 0, 0.95)), Vector((0, 0, 1.06)), 'pelvis')
    bones['spine_02'] = (Vector((0, 0, 1.06)), Vector((0, 0, 1.17)), 'spine_01')
    bones['spine_03'] = (Vector((0, 0, 1.17)), Vector((0, 0.005, 1.36)), 'spine_02')
    bones['neck_01'] = (Vector((0, 0.005, 1.38)), Vector((0, -0.005, 1.45)), 'spine_03')
    bones['head'] = (Vector((0, -0.005, 1.45)), Vector((0, -0.01, 1.66)), 'neck_01')
    for s, sx in (('l', 1), ('r', -1)):
        S = 'L' if s == 'l' else 'R'
        sh, el, wr = j['Shoulder' + S], j['Elbow' + S], j['Wrist' + S]
        d = (wr - el).normalized()
        bones['clavicle_' + s] = (Vector((sx * 0.03, -0.01, 1.34)), sh.copy(), 'spine_03')
        bones['upperarm_' + s] = (sh.copy(), el.copy(), 'clavicle_' + s)
        bones['lowerarm_' + s] = (el.copy(), wr.copy(), 'upperarm_' + s)
        g = GLOVE
        inner, fwd = Vector((-sx, 0, 0)), Vector((0, -1, 0))
        hand_end = wr + d * 0.075 * g
        bones['hand_' + s] = (wr.copy(), hand_end, 'lowerarm_' + s)
        # the fingers, at the glove's index finger, its three-finger mitt and its thumb (ring and pinky beside the middle)
        def chain(name, a, b, parent):
            for i in range(3):
                p0, p1 = a + (b - a) * (i / 3), a + (b - a) * ((i + 1) / 3)
                bones['%s_0%d_%s' % (name, i + 1, s)] = (p0, p1, parent if i == 0 else '%s_0%d_%s' % (name, i, s))
        chain('index', wr + (d * 0.09 + fwd * 0.024) * g, wr + (d * 0.16 + fwd * 0.026 + inner * 0.006) * g, 'hand_' + s)
        mid0 = wr + (d * 0.095 + Vector((0, 0.008, 0))) * g
        mid1 = wr + (d * 0.16 + Vector((0, 0.012, 0)) + inner * 0.004) * g
        chain('middle', mid0, mid1, 'hand_' + s)
        chain('ring', mid0 + Vector((0, 0.012, 0)) * g, mid1 + Vector((0, 0.012, 0)) * g, 'hand_' + s)
        chain('pinky', mid0 + Vector((0, 0.022, 0)) * g, mid1 + Vector((0, 0.02, -0.004)) * g, 'hand_' + s)
        chain('thumb', wr + (d * 0.035 + fwd * 0.024 + inner * 0.008) * g, wr + (d * 0.095 + fwd * 0.054 + inner * 0.017) * g, 'hand_' + s)
        hp, kn = j['Hip' + S], j['Knee' + S]
        x = hp.x
        bones['thigh_' + s] = (Vector((x, 0, 0.84)), kn.copy(), 'pelvis')
        bones['calf_' + s] = (kn.copy(), Vector((x, 0, 0.09)), 'thigh_' + s)
        bones['foot_' + s] = (Vector((x, 0, 0.09)), Vector((x, -0.11, 0.035)), 'calf_' + s)
        bones['ball_' + s] = (Vector((x, -0.11, 0.035)), Vector((x, -0.17, 0.035)), 'foot_' + s)
    arm = bpy.data.armatures.new('PlayerHazmat.rig')
    rig = bpy.data.objects.new('PlayerHazmat', arm)
    bpy.context.collection.objects.link(rig)
    L.activate(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (h, t, parent) in bones.items():
        eb = arm.edit_bones.new(name)
        eb.head, eb.tail = h, t
        eb.use_deform = True
    for name, (h, t, parent) in bones.items():
        if parent:
            arm.edit_bones[name].parent = arm.edit_bones[parent]
            arm.edit_bones[name].use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig, bones


rig, BONES = build_armature()


# ------------------------------------------------------------------------------------------------ which bones move what
def ancestors(o):
    names = []
    a = o.parent
    while a is not None:
        names.append(a.name.split('.')[0])
        a = a.parent
    return names


def candidates(o):
    """The bones that may move a part: its own and its neighbours (by the joint it hangs from, and its name)."""
    n = o.name.split('.')[0]
    anc = ancestors(o)
    side = next((a[-1] for a in anc if a[:-1] in ('Shoulder', 'Elbow', 'Wrist', 'Hip', 'Knee')), None)
    s = side.lower() if side else None
    if 'Wrist' + str(side) in anc:
        fingers = ['%s_0%d_%s' % (f, i, s) for f in ('index', 'middle', 'ring', 'pinky', 'thumb') for i in (1, 2, 3)]
        return ['hand_' + s, 'lowerarm_' + s] + fingers
    if 'Elbow' + str(side) in anc:
        return ['lowerarm_' + s, 'upperarm_' + s, 'hand_' + s]
    if 'Shoulder' + str(side) in anc:
        return ['upperarm_' + s, 'clavicle_' + s, 'lowerarm_' + s, 'spine_03']
    if 'Knee' + str(side) in anc:
        if n.startswith(('Boot', 'Sole', 'Tread')):
            return ['foot_' + s, 'ball_' + s, 'calf_' + s]
        return ['calf_' + s, 'thigh_' + s, 'foot_' + s]
    if 'Hip' + str(side) in anc:
        return ['thigh_' + s, 'pelvis', 'calf_' + s]
    if 'Neck' in anc:
        return ['head', 'neck_01', 'spine_03']
    if 'Chest' in anc:
        if n == 'Body':
            return ['pelvis', 'spine_01', 'spine_02', 'spine_03', 'thigh_l', 'thigh_r', 'neck_01']
        return ['spine_03', 'spine_02', 'spine_01', 'pelvis']
    return ['pelvis']


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (a + ab * t - p).length


def weigh(o, cands, power=6.0, keep=4):
    """Each vertex to the candidate bones nearest it (inverse distance to the power), the strongest four kept."""
    mw = o.matrix_world
    groups = {b: (o.vertex_groups.get(b) or o.vertex_groups.new(name=b)) for b in cands}
    own = cands[0]
    for v in o.data.vertices:
        p = mw @ v.co
        ws = []
        for i, b in enumerate(cands):
            h, t, _ = BONES[b]
            d = seg_dist(p, h, t) + 0.01
            bias = 1.0 if i == 0 else 0.85        # a part leans to its own bone
            ws.append((b, bias / d ** power))
        ws.sort(key=lambda x: -x[1])
        ws = ws[:keep]
        tot = sum(w for _, w in ws)
        for b, w in ws:
            if w / tot > 0.01:
                groups[b].add([v.index], w / tot, 'REPLACE')


# ------------------------------------------------------------------------------------------------ sort the pieces
OPTION_PREFIX = ('Nose_', 'Brows_', 'Hair_', 'FacialHair_', 'Glasses', 'Mask_')
option_roots = [o for o in bpy.data.objects if o.type == 'EMPTY' and o.name.startswith(OPTION_PREFIX)]
head_parts = bpy.data.objects.get('HeadParts')


def under(o, root):
    a = o.parent
    while a is not None:
        if a == root:
            return True
        a = a.parent
    return False


def detach(o):
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw


def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        detach(o)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    return o


def to_head_bone(o):
    mw = o.matrix_world.copy()
    o.parent = rig
    o.parent_type = 'BONE'
    o.parent_bone = 'head'
    o.matrix_world = mw


rigid = []
for root in option_roots:
    parts = [o for o in bpy.data.objects if o.type == 'MESH' and under(o, root)]
    if parts:
        rigid.append(join(parts, root.name + '.Head'))
if head_parts:
    face_bits = [o for o in bpy.data.objects if o.type == 'MESH' and under(o, head_parts)]
    paint = [o for o in face_bits if o.name.startswith(('Paint_Features', 'Paint_Shade'))]
    for p in paint:
        detach(p)
        p.name = p.name.split('.')[0] + '.Head'
        rigid.append(p)
    rest = [o for o in face_bits if o not in paint]
    if rest:
        rigid.append(join(rest, 'Face.Head'))
for o in rigid:
    to_head_bone(o)

# the text markers ride the top of the spine
for o in [o for o in bpy.data.objects if o.type == 'EMPTY' and o.name.startswith(('NameTape', 'Number'))]:
    mw = o.matrix_world.copy()
    o.parent = rig
    o.parent_type = 'BONE'
    o.parent_bone = 'spine_03'
    o.matrix_world = mw

# everything else is the suit: weigh each part, then join
suit_parts = [o for o in bpy.data.objects if o.type == 'MESH' and o not in rigid and o.parent is not None
              or (o.type == 'MESH' and o not in rigid and o.name not in ('Cutter',))]
suit_parts = [o for o in suit_parts if o.type == 'MESH' and o not in rigid]
# trim the weight (round ten): the fused and lofted parts carry far more triangles than they need at game distances
BUDGET = {'Hood': 3200, 'Body': 2400}
before = after = 0
heavy = []
for o in suit_parts:
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    before += tris
    base = o.name.split('.')[0]
    budget = BUDGET.get(base, 1400)
    if tris > budget and not base.startswith(('ZipTeeth', 'VisorGlass', 'WindowFrame', 'NameTape', 'Sleeve', 'Zip')):
        md = o.modifiers.new('Trim', 'DECIMATE')
        md.ratio = max(0.15, budget / tris)
        L.activate(o)
        bpy.ops.object.modifier_apply(modifier=md.name)
        heavy.append((base, tris, sum(len(p.vertices) - 2 for p in o.data.polygons)))
    after += sum(len(p.vertices) - 2 for p in o.data.polygons)
print('TRIS before', before, 'after', after)
for h in sorted(heavy, key=lambda x: -x[1])[:12]:
    print('TRIMMED', h)
for o in suit_parts:
    weigh(o, candidates(o))
suit = join(suit_parts, 'Suit')
mw = suit.matrix_world.copy()
suit.parent = rig
suit.matrix_world = mw
md = suit.modifiers.new('Armature', 'ARMATURE')
md.object = rig

# the joints' empties are done with
for o in [o for o in bpy.data.objects if o.type == 'EMPTY' and not o.name.startswith(('NameTape', 'Number'))]:
    bpy.data.objects.remove(o, do_unlink=True)


# ------------------------------------------------------------------------------------------------ split like the player body
HEAD = {'head', 'neck_01'}


def arm_bones(s):
    names = {'upperarm_' + s, 'lowerarm_' + s, 'hand_' + s}
    for f in ('index', 'middle', 'ring', 'pinky', 'thumb'):
        names |= {'%s_0%d_%s' % (f, i, s) for i in (1, 2, 3)}
    return names


TORSO = {'pelvis', 'spine_01', 'spine_02', 'spine_03', 'clavicle_l', 'clavicle_r'}
SETS = [('Head', HEAD), ('ArmL', arm_bones('l')), ('ArmR', arm_bones('r')), ('Torso', TORSO)]


def shares(obj):
    gi = {g.index: g.name for g in obj.vertex_groups}
    out = {}
    for v in obj.data.vertices:
        tot = sum(g.weight for g in v.groups) or 1.0
        out[v.index] = {k: sum(g.weight for g in v.groups if gi.get(g.group) in bones) / tot for k, bones in SETS}
    return out


def split(obj):
    for key, _ in SETS:
        sh = shares(obj)
        me = obj.data
        faces = [p.index for p in me.polygons if sum(sh[i][key] for i in p.vertices) / len(p.vertices) > 0.5]
        if not faces:
            continue
        for p in me.polygons:
            p.select = p.index in faces
        bpy.ops.object.select_all(action='DESELECT')
        L.activate(obj)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_mode(type='FACE')
        bpy.ops.object.mode_set(mode='OBJECT')
        for p in me.polygons:
            p.select = p.index in faces
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.separate(type='SELECTED')
        bpy.ops.object.mode_set(mode='OBJECT')
        part = next(o for o in bpy.context.selected_objects if o is not obj)
        part.name = 'Suit.' + key


split(suit)
for o in bpy.data.objects:
    if o.type == 'MESH' and o.name.startswith('Suit'):
        print('PART', o.name, 'tris', sum(len(p.vertices) - 2 for p in o.data.polygons), 'materials',
              [s.material.name for s in o.material_slots if s.material])
for o in rigid:
    print('RIGID', o.name)

# ------------------------------------------------------------------------------------------------ export
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(L.BLEND, 'PlayerHazmat.blend'))
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
for o in bpy.data.objects:
    if o.parent == rig:
        o.select_set(True)
bpy.context.view_layer.objects.active = rig
fbx = os.path.join(OUT_DIR, 'PlayerHazmat.fbx')
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
                         apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=False,
                         add_leaf_bones=False, mesh_smooth_type='FACE', use_armature_deform_only=True,
                         bake_anim=False, path_mode='STRIP', embed_textures=False)
print('EXPORTED', fbx)
