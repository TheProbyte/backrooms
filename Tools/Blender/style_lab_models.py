"""Builds the style lab's models and exports each as FBX for Unity: the room (with a dark corridor off it), the hazmat
worker (parts on joints so it can be posed), the platform cart and the work camcorder.
Run headless:  blender -b -P Tools/Blender/style_lab_models.py
Stylised low-poly: chunky shapes, few segments, bevelled edges, one flat colour per material (Unity's own materials
take over on import)."""
import bpy, math, os
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Backrooms', 'Models', 'StyleLab')
BLEND = os.path.join(ROOT, 'Tools', 'Blender', 'StyleLab')
os.makedirs(OUT, exist_ok=True)
os.makedirs(BLEND, exist_ok=True)


def lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]


def mat(name, hexcol, rough=0.7, metal=0.0, emit=0.0):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (*lin(hexcol), 1)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    if emit:
        b.inputs['Emission Color'].default_value = (*lin(hexcol), 1)
        b.inputs['Emission Strength'].default_value = emit
    m.diffuse_color = (*lin(hexcol), 1)
    return m


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)


def activate(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def finish(o, m, bevel=0.0, segs=2, smooth=35, parent=None, origin=None):
    if m:
        o.data.materials.clear()
        o.data.materials.append(m)
    activate(o)
    if bevel > 0:
        md = o.modifiers.new('Bevel', 'BEVEL')
        md.width = bevel
        md.segments = segs
        md.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier=md.name)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth))
    except Exception:
        bpy.ops.object.shade_flat()
    if origin is not None:
        bpy.context.scene.cursor.location = origin
        bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
        bpy.context.scene.cursor.location = (0, 0, 0)
    if parent is not None:
        mw = o.matrix_world.copy()
        o.parent = parent
        o.matrix_world = mw
    return o


def box(name, size, loc, m, bevel=0.0, segs=2, parent=None, origin=None, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    activate(o)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, m, bevel, segs, parent=parent, origin=origin)


def cyl(name, r, depth, loc, m, verts=10, rot=(0, 0, 0), r2=None, bevel=0.0, parent=None, origin=None, smooth=50):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=depth, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    return finish(o, m, bevel, 2, smooth, parent=parent, origin=origin)


def ball(name, r, loc, m, scale=(1, 1, 1), segs=12, rings=8, parent=None, origin=None, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=r, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    activate(o)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, m, 0, smooth=60, parent=parent, origin=origin)


def tube(name, pts, r, m, parent=None, res=6):
    cu = bpy.data.curves.new(name, 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = r
    cu.bevel_resolution = 1
    sp = cu.splines.new('POLY')
    sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        sp.points[i].co = (*p, 1)
    o = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(o)
    activate(o)
    bpy.ops.object.convert(target='MESH')
    o = bpy.context.active_object
    return finish(o, m, 0, smooth=60, parent=parent)


def empty(name, loc, parent=None):
    o = bpy.data.objects.new(name, None)
    o.empty_display_size = 0.05
    o.location = loc
    bpy.context.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()
    return o


def export(name):
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLEND, name + '.blend'))
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + '.fbx'), use_selection=False,
                             object_types={'MESH', 'EMPTY'}, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE',
                             add_leaf_bones=False, bake_anim=False)
    print('exported', name)


# ---------------------------------------------------------------- the room: 8 x 8 m, 2.7 m high, a doorway in the
# north wall onto a 6 m corridor whose lights are off
def build_room():
    reset()
    W, H, T = 8.0, 2.7, 0.15
    carpet, wall = mat('SL_Carpet', '#A88B45', 0.95), mat('SL_Wallpaper', '#CDB45E', 0.85)
    tile, tbar = mat('SL_CeilingTile', '#E3DCC0', 0.9), mat('SL_TBar', '#CBC4AA', 0.5)
    panel, base = mat('SL_LightPanel', '#FFF4D6', 0.4, emit=4.0), mat('SL_Baseboard', '#7E6E46', 0.6)
    root = empty('StyleLab_Room', (0, 0, 0))
    box('Floor', (W, W, 0.1), (0, 0, -0.05), carpet, parent=root)
    box('Ceiling', (W, W, 0.05), (0, 0, H + 0.025), tile, parent=root)
    # walls: south, east, west whole; north in two pieces and a lintel round a 1.2 x 2.1 doorway
    dw, dh = 1.2, 2.1
    box('Wall_S', (W + 2 * T, T, H), (0, -W / 2 - T / 2, H / 2), wall, parent=root)
    box('Wall_E', (T, W, H), (W / 2 + T / 2, 0, H / 2), wall, parent=root)
    box('Wall_W', (T, W, H), (-W / 2 - T / 2, 0, H / 2), wall, parent=root)
    side = (W + 2 * T - dw) / 2
    box('Wall_N_L', (side, T, H), (-(dw / 2 + side / 2), W / 2 + T / 2, H / 2), wall, parent=root)
    box('Wall_N_R', (side, T, H), ((dw / 2 + side / 2), W / 2 + T / 2, H / 2), wall, parent=root)
    box('Wall_N_Top', (dw, T, H - dh), (0, W / 2 + T / 2, dh + (H - dh) / 2), wall, parent=root)
    # baseboards
    bh, bt = 0.1, 0.02
    box('Base_S', (W, bt, bh), (0, -W / 2 + bt / 2, bh / 2), base, parent=root)
    box('Base_E', (bt, W, bh), (W / 2 - bt / 2, 0, bh / 2), base, parent=root)
    box('Base_W', (bt, W, bh), (-W / 2 + bt / 2, 0, bh / 2), base, parent=root)
    for sx in (-1, 1):
        box('Base_N', ((W - dw) / 2, bt, bh), (sx * (dw / 2 + (W - dw) / 4), W / 2 - bt / 2, bh / 2), base, parent=root)
    # the suspended ceiling's T-bar grid, every 0.6 m, and four light panels (0.6 x 1.2)
    n = int(W / 0.6)
    for i in range(n + 1):
        c = -W / 2 + i * (W / n)
        box('TBar_X', (W, 0.025, 0.012), (0, c, H - 0.006), tbar, parent=root)
        box('TBar_Y', (0.025, W, 0.012), (c, 0, H - 0.006), tbar, parent=root)
    for (x, y) in ((-1.8, -1.8), (1.8, -1.8), (-1.8, 1.8), (1.8, 1.8)):
        box('LightPanel', (0.56, 1.16, 0.02), (x, y, H - 0.012), panel, parent=root)
    # the corridor beyond the doorway: 1.6 m wide, 6 m long, a dead light panel half way
    cw, cl = 1.6, 6.0
    y0 = W / 2 + T
    box('Corr_Floor', (cw, cl, 0.1), (0, y0 + cl / 2, -0.05), carpet, parent=root)
    box('Corr_Ceiling', (cw, cl, 0.05), (0, y0 + cl / 2, H + 0.025), tile, parent=root)
    box('Corr_Wall_E', (T, cl, H), (cw / 2 + T / 2, y0 + cl / 2, H / 2), wall, parent=root)
    box('Corr_Wall_W', (T, cl, H), (-cw / 2 - T / 2, y0 + cl / 2, H / 2), wall, parent=root)
    box('Corr_Wall_End', (cw, T, H), (0, y0 + cl + T / 2, H / 2), wall, parent=root)
    box('Corr_PanelDead', (0.56, 1.16, 0.02), (0, y0 + 3.0, H - 0.012), mat('SL_LightPanelOff', '#9C9579', 0.5), parent=root)
    export('StyleLab_Room')


# ---------------------------------------------------------------- the hazmat worker: chunky, about 1.75 m, facing -Y,
# each part on its own joint so Unity can pose it
def build_hazmat():
    reset()
    Y, K = mat('SL_HazmatYellow', '#E8BE1E', 0.65), mat('SL_Rubber', '#1E1C1A', 0.8)
    V, G = mat('SL_Visor', '#1A2226', 0.08, 0.3), mat('SL_FilterGrey', '#8A8A84', 0.5)
    O, S = mat('SL_PatchOrange', '#D9622B', 0.7), mat('SL_TankWhite', '#DCD8CC', 0.4)
    root = empty('Hazmat', (0, 0, 0))
    pelvis = empty('Pelvis', (0, 0, 0.95), root)
    box('Hips', (0.42, 0.3, 0.22), (0, 0, 0.95), Y, 0.06, 3, parent=pelvis)
    box('Belt', (0.43, 0.31, 0.05), (0, 0, 0.99), K, 0.02, 2, parent=pelvis)
    chest = empty('Chest', (0, 0, 1.04), pelvis)
    box('Torso', (0.52, 0.36, 0.52), (0, 0, 1.28), Y, 0.13, 4, parent=chest)
    box('Zip', (0.02, 0.01, 0.38), (0, -0.182, 1.27), K, parent=chest)
    box('Patch', (0.12, 0.008, 0.08), (0.12, -0.181, 1.39), O, 0.005, 1, parent=chest)
    cyl('Tank', 0.09, 0.42, (0, 0.24, 1.28), S, 10, parent=chest)
    box('TankStrap', (0.54, 0.06, 0.04), (0, 0.02, 1.42), K, 0.01, 1, parent=chest)
    neck = empty('Neck', (0, 0, 1.5), chest)
    ball('Hood', 0.23, (0, 0.01, 1.66), Y, (1.0, 0.95, 1.05), 14, 9, parent=neck)
    ball('VisorRim', 0.19, (0, -0.13, 1.67), K, (0.95, 0.5, 0.8), 14, 9, parent=neck)
    ball('Visor', 0.18, (0, -0.148, 1.675), V, (0.9, 0.5, 0.74), 14, 9, parent=neck)
    cyl('Respirator', 0.055, 0.08, (0, -0.19, 1.54), G, 10, rot=(math.radians(90), 0, 0), parent=neck)
    for sx in (-1, 1):
        cyl('Filter', 0.04, 0.05, (sx * 0.1, -0.175, 1.52), O, 10, rot=(math.radians(90), 0, math.radians(sx * 25)), parent=neck)
    for side, sx in (('L', 1), ('R', -1)):
        # arms hang a little out from the body (A pose)
        sh = empty('Shoulder' + side, (sx * 0.29, 0, 1.44), chest)
        a = math.radians(sx * 14)
        cyl('UpperArm' + side, 0.088, 0.32, (sx * 0.29 + sx * 0.16 * math.sin(math.radians(14)), 0, 1.44 - 0.16 * math.cos(math.radians(14))), Y, 9, rot=(0, -a, 0), parent=sh)
        ex, ez = sx * 0.29 + sx * 0.32 * math.sin(math.radians(14)), 1.44 - 0.32 * math.cos(math.radians(14))
        el = empty('Elbow' + side, (ex, 0, ez), sh)
        cyl('LowerArm' + side, 0.08, 0.28, (ex + sx * 0.14 * math.sin(math.radians(14)), 0, ez - 0.14 * math.cos(math.radians(14))), Y, 9, rot=(0, -a, 0), parent=el)
        wx, wz = ex + sx * 0.28 * math.sin(math.radians(14)), ez - 0.28 * math.cos(math.radians(14))
        cyl('Cuff' + side, 0.086, 0.05, (wx, 0, wz + 0.02), K, 9, rot=(0, -a, 0), parent=el)
        wr = empty('Wrist' + side, (wx, 0, wz), el)
        box('Glove' + side, (0.11, 0.1, 0.14), (wx + sx * 0.012, 0, wz - 0.06), K, 0.03, 3, parent=wr)
        box('Thumb' + side, (0.035, 0.04, 0.065), (wx - sx * 0.045, -0.05, wz - 0.03), K, 0.012, 2, parent=wr)
        hp = empty('Hip' + side, (sx * 0.11, 0, 0.9), pelvis)
        cyl('UpperLeg' + side, 0.12, 0.42, (sx * 0.11, 0, 0.69), Y, 9, parent=hp)
        kn = empty('Knee' + side, (sx * 0.11, 0, 0.48), hp)
        cyl('LowerLeg' + side, 0.11, 0.38, (sx * 0.11, 0, 0.3), Y, 9, parent=kn)
        cyl('AnkleTape' + side, 0.116, 0.05, (sx * 0.11, 0, 0.15), K, 9, parent=kn)
        box('Boot' + side, (0.17, 0.3, 0.14), (sx * 0.11, -0.05, 0.07), K, 0.045, 3, parent=kn)
    export('Hazmat')


# ---------------------------------------------------------------- the platform cart: 0.9 x 0.6 m deck, a U handle
def build_cart():
    reset()
    T, M = mat('SL_CartTeal', '#2F6F6A', 0.45, 0.2), mat('SL_Rubber', '#1E1C1A', 0.8)
    S = mat('SL_Steel', '#A7A9A6', 0.35, 0.8)
    root = empty('PlatformCart', (0, 0, 0))
    box('Deck', (0.9, 0.6, 0.05), (0, 0, 0.175), T, 0.012, 2, parent=root)
    box('DeckMat', (0.84, 0.54, 0.01), (0, 0, 0.205), M, 0.003, 1, parent=root)
    for i in range(7):
        box('Rib', (0.82, 0.012, 0.006), (0, -0.24 + i * 0.08, 0.212), M, parent=root)
    for x in (-0.36, 0.36):
        for y in (-0.22, 0.22):
            box('Fork', (0.05, 0.035, 0.085), (x, y, 0.11), S, 0.006, 1, parent=root)
            cyl('Wheel', 0.06, 0.035, (x, y, 0.06), M, 12, rot=(0, math.radians(90), 0), parent=root)
            cyl('Hub', 0.025, 0.04, (x, y, 0.06), S, 8, rot=(0, math.radians(90), 0), parent=root)
    tube('Handle', [(0.43, -0.25, 0.2), (0.43, -0.25, 0.92), (0.43, 0.25, 0.92), (0.43, 0.25, 0.2)], 0.016, T, parent=root)
    tube('HandleBar', [(0.43, -0.25, 0.58), (0.43, 0.25, 0.58)], 0.012, T, parent=root)
    cyl('Grip', 0.022, 0.4, (0.43, 0, 0.92), M, 10, rot=(math.radians(90), 0, 0), parent=root)
    export('PlatformCart')


# ---------------------------------------------------------------- the work camcorder, as in the orientation tape:
# chocolate body, the triple stripe, a ribbed lens with a hood, a carry handle, a microphone clamped on its post
def build_camcorder():
    reset()
    B, D = mat('SL_CamBody', '#4A3427', 0.55), mat('SL_CamDark', '#2B1C14', 0.6)
    K, R = mat('SL_Rubber', '#1E1C1A', 0.8), mat('SL_CamRec', '#E8402A', 0.3, emit=3.0)
    L, F = mat('SL_LensGlass', '#173B38', 0.05, 0.2), mat('SL_Foam', '#3A3634', 0.95)
    st = [mat('SL_StripeOrange', '#D9622B', 0.5), mat('SL_StripeGold', '#E1A73A', 0.5), mat('SL_StripeAvo', '#8A9A3B', 0.5)]
    root = empty('Camcorder', (0, 0, 0))
    box('Body', (0.10, 0.26, 0.13), (0, 0, 0.065), B, 0.014, 3, parent=root)
    box('TapeDoor', (0.006, 0.13, 0.08), (-0.052, 0.03, 0.065), D, 0.004, 1, parent=root)
    for i, m in enumerate(st):
        box('Stripe', (0.004, 0.12, 0.012), (0.052, -0.01, 0.092 - i * 0.016), m, parent=root)
    cyl('Lens', 0.044, 0.09, (0, -0.17, 0.07), K, 14, rot=(math.radians(90), 0, 0), parent=root)
    for i in range(3):
        cyl('LensRib', 0.047, 0.01, (0, -0.145 - i * 0.025, 0.07), D, 14, rot=(math.radians(90), 0, 0), parent=root)
    cyl('LensHood', 0.05, 0.045, (0, -0.235, 0.07), K, 14, rot=(math.radians(90), 0, 0), r2=0.064, parent=root)
    cyl('Glass', 0.046, 0.005, (0, -0.232, 0.07), L, 14, rot=(math.radians(90), 0, 0), parent=root)
    tube('CarryHandle', [(0, 0.1, 0.125), (0, 0.075, 0.2), (0, -0.05, 0.2), (0, -0.075, 0.125)], 0.011, K, parent=root)
    # the microphone: on a post from the top of the body, held in a clamp, a foam windscreen on its front, a cable back
    cyl('MicPost', 0.007, 0.07, (0.035, -0.09, 0.16), K, 8, parent=root)
    box('MicFoot', (0.03, 0.03, 0.01), (0.035, -0.09, 0.132), K, 0.003, 1, parent=root)
    cyl('MicClamp', 0.021, 0.024, (0.035, -0.09, 0.205), K, 12, rot=(math.radians(90), 0, 0), parent=root)
    cyl('Mic', 0.016, 0.1, (0.035, -0.12, 0.205), D, 12, rot=(math.radians(90), 0, 0), parent=root)
    ball('MicFoam', 0.022, (0.035, -0.18, 0.205), F, (1, 1.5, 1), 10, 7, parent=root)
    tube('MicCable', [(0.035, -0.07, 0.205), (0.045, -0.04, 0.2), (0.05, -0.02, 0.15), (0.05, 0.0, 0.13)], 0.004, K, parent=root)
    box('Viewfinder', (0.05, 0.09, 0.045), (-0.035, 0.11, 0.15), K, 0.01, 2, parent=root)
    cyl('Eyecup', 0.026, 0.035, (-0.035, 0.17, 0.15), K, 12, rot=(math.radians(90), 0, 0), parent=root)
    ball('RecLamp', 0.008, (0.04, -0.12, 0.122), R, (1, 1, 1), 8, 6, parent=root)
    for i in range(3):
        box('Button', (0.018, 0.012, 0.008), (0.03, 0.02 + i * 0.025, 0.132), D if i < 2 else R, 0.003, 1, parent=root)
    box('HandStrap', (0.012, 0.16, 0.05), (0.058, 0.0, 0.06), K, 0.005, 1, parent=root)
    export('Camcorder')


if __name__ == '__main__':
    build_room()
    build_hazmat()
    build_cart()
    build_camcorder()
    print('done')
