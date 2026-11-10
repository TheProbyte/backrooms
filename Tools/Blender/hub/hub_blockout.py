"""Overflow hub blockout: a top-down floor plan and a mood shot from the lift. Run:
blender --background --factory-startup --python hub_blockout.py -- <outDir>"""
import bpy, sys, math, os
from mathutils import Vector

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "."
os.makedirs(OUT, exist_ok=True)

for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
scene = bpy.context.scene

def hexc(h, a=1.0):
    h = h.lstrip("#"); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (a,)

_mats = {}
def mat(name, col, rough=0.8, emit=0.0, emit_col=None):
    key = (name,)
    if key in _mats: return _mats[key]
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = hexc(col); b.inputs["Roughness"].default_value = rough
    if emit > 0:
        b.inputs["Emission Color"].default_value = hexc(emit_col or col); b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = hexc(col)
    _mats[key] = m; return m

PLAN_ONLY, MOOD_ONLY = set(), set()
def box(name, x0, y0, z0, x1, y1, z1, m, tag=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    o = bpy.context.active_object; o.name = name
    o.scale = (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)); bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(m)
    if tag == "plan": PLAN_ONLY.add(o.name)
    if tag == "mood": MOOD_ONLY.add(o.name)
    return o

def label(text, x, y, size=0.55, col="#1E1510", z=3.3, rot=0.0, tag="plan", align="CENTER"):
    cu = bpy.data.curves.new(text[:12], "FONT"); cu.body = text; cu.size = size
    cu.align_x = align; cu.align_y = "CENTER"
    o = bpy.data.objects.new("T_" + text[:12], cu); scene.collection.objects.link(o)
    o.location = (x, y, z); o.rotation_euler = (0, 0, rot)
    o.data.materials.append(mat("txt" + col, col, emit=0.0))
    if tag == "plan": PLAN_ONLY.add(o.name)
    if tag == "mood": MOOD_ONLY.add(o.name)
    return o

# ---- rooms: x0, y0, x1, y1, name, floor colour, ceiling height
ROOMS = [
    (9.6, 0.0, 13.2, 3.6, "SERVICE LIFT", "#6E6A62", 2.7),
    (7.2, 3.6, 15.6, 7.2, "LIFT HALL", "#B9AA8A", 3.0),
    (4.8, 7.2, 19.2, 14.4, "LOBBY", "#6B7232", 3.0),
    (0.0, 3.6, 4.8, 8.4, "FIRST AID", "#A9BFA4", 2.7),
    (0.0, 8.4, 4.8, 14.4, "LOCKERS", "#7E6448", 2.7),
    (2.4, 14.4, 9.6, 21.6, "TRAINING ROOM", "#8C877D", 3.9),
    (9.6, 14.4, 14.4, 18.0, "PAY OFFICE", "#4A4640", 2.7),
    (14.4, 14.4, 19.2, 19.2, "MANAGER", "#7A3A30", 2.7),
    (19.2, 8.4, 21.6, 12.0, "VENDING", "#8E8270", 3.0),
]
WALL_H = 3.0
TR = (2.4, 14.4, 9.6, 21.6)  # the training room: taller, greyer, a concrete floor
# doors and openings: centre x, y, width, axis ('x' = the gap runs along x), kind
OPEN = [
    (11.4, 3.6, 2.4, "x", "lift"), (11.4, 7.2, 6.0, "x", "open"), (2.4, 8.4, 1.2, "x", "door"),
    (4.8, 12.0, 1.2, "y", "door"), (6.6, 14.4, 1.8, "x", "door"), (15.6, 14.4, 1.2, "x", "locked"),
    (19.2, 10.2, 3.6, "y", "open"), (13.2, 14.4, 1.2, "x", "hatch"), (17.4, 14.4, 1.8, "x", "window"),
]
STEP = 0.6
def k(v): return round(v / STEP)
segs = set()
for x0, y0, x1, y1, *_ in ROOMS:
    for i in range(k(x0), k(x1)): segs.add(("x", i, k(y0))); segs.add(("x", i, k(y1)))
    for j in range(k(y0), k(y1)): segs.add(("y", k(x0), j)); segs.add(("y", k(x1), j))
special = {}
for cx, cy, w, ax, kind in OPEN:
    if ax == "x":
        for i in range(k(cx - w / 2), k(cx + w / 2)): segs.discard(("x", i, k(cy))); special[("x", i, k(cy))] = kind
    else:
        for j in range(k(cy - w / 2), k(cy + w / 2)): segs.discard(("y", k(cx), j)); special[("y", k(cx), j)] = kind

M_WOOD = mat("wood", "#4E3220", 0.6); M_WALL = mat("wall", "#C4AC6C", 0.9)
M_STR = [mat("s_or", "#D9661F"), mat("s_go", "#E8B23A"), mat("s_av", "#7C8A2E")]
M_PLANWALL = mat("planwall", "#2A1E16")
T = 0.14
def wall_run(ax, a0, a1, c):
    # a0..a1 along the axis (metres), c the fixed coordinate
    if ax == "x": X0, X1, Y0, Y1 = a0, a1, c - T / 2, c + T / 2
    else: X0, X1, Y0, Y1 = c - T / 2, c + T / 2, a0, a1
    box("Wainscot", X0 - 0.01, Y0 - 0.01, 0, X1 + 0.01, Y1 + 0.01, 1.0, M_WOOD, "mood")
    for n, mm in enumerate(M_STR):
        box("Stripe", X0 - 0.012, Y0 - 0.012, 1.0 + n * 0.05, X1 + 0.012, Y1 + 0.012, 1.05 + n * 0.05, mm, "mood")
    box("Wall", X0, Y0, 1.15, X1, Y1, WALL_H, M_WALL, "mood")
    box("PlanWall", X0, Y0, 0, X1, Y1, 3.05, M_PLANWALL, "plan")

for ax in ("x", "y"):
    lines = {}
    for s in segs:
        if s[0] != ax: continue
        a, c = (s[1], s[2]) if ax == "x" else (s[2], s[1])
        lines.setdefault(c, []).append(a)
    for c, arr in lines.items():
        arr.sort(); start = prev = arr[0]
        for a in arr[1:] + [None]:
            if a is not None and a == prev + 1: prev = a; continue
            wall_run(ax, start * STEP, (prev + 1) * STEP, c * STEP)
            if a is not None: start = prev = a

# training room: walls up to its taller ceiling and a greyer skin inside
M_TRW = mat("trwall", "#ABA38E", 0.9); M_TRW_PLAN = M_PLANWALL
x0, y0, x1, y1 = TR; TH = 3.9; e = 0.08
for (a0, b0, a1, b1) in ((x0, y0, x1, y0), (x0, y1, x1, y1), (x0, y0, x0, y1), (x1, y0, x1, y1)):
    if a0 == a1: box("TrWallUp", a0 - T / 2, b0, WALL_H, a0 + T / 2, b1, TH, M_WALL, "mood")
    else: box("TrWallUp", a0, b0 - T / 2, WALL_H, a1, b0 + T / 2, TH, M_WALL, "mood")
box("TrSkinS", x0 + e, y0 + e, 1.15, x1 - e, y0 + e + 0.01, TH, M_TRW, "mood")
box("TrSkinN", x0 + e, y1 - e - 0.01, 1.15, x1 - e, y1 - e, TH, M_TRW, "mood")
box("TrSkinW", x0 + e, y0 + e, 1.15, x0 + e + 0.01, y1 - e, TH, M_TRW, "mood")
box("TrSkinE", x1 - e - 0.01, y0 + e, 1.15, x1 - e, y1 - e, TH, M_TRW, "mood")
# concrete floor: a few darker patches and saw-cut joints
M_CONC2 = mat("conc2", "#7D786E", 0.7); M_JOINT = mat("joint", "#5E5A53", 0.8)
for (px, py, w, h) in ((4.0, 16.0, 1.8, 1.2), (7.5, 19.5, 1.4, 1.6), (3.4, 20.6, 1.0, 0.7), (8.2, 15.4, 0.9, 0.8)):
    box("ConcPatch", px - w / 2, py - h / 2, 0.0, px + w / 2, py + h / 2, 0.002, M_CONC2)
for gx in (4.8, 7.2): box("Joint", gx - 0.01, y0, 0.0, gx + 0.01, y1, 0.003, M_JOINT)
for gy in (16.8, 19.2): box("Joint", x0, gy - 0.01, 0.0, x1, gy + 0.01, 0.003, M_JOINT)

# floors and ceilings
M_CEIL = mat("ceil", "#CBC1A6", 0.95); M_PANEL = mat("panel", "#EEF2E4", 0.4, emit=4.0, emit_col="#E9F2DA")
M_DEAD = mat("deadpanel", "#9C9A8E", 0.5)
for x0, y0, x1, y1, name, col, ch in ROOMS:
    box("Floor_" + name, x0, y0, -0.05, x1, y1, 0.0, mat("f_" + name, col))
    box("Ceil_" + name, x0, y0, ch, x1, y1, ch + 0.05, M_CEIL, "mood")
    label(name, (x0 + x1) / 2, (y0 + y1) / 2 + (0.0 if name not in ("LOBBY",) else 1.6), 0.5 if (x1 - x0) > 3 else 0.36)
    # ceiling panels on a 2.4 grid; a few dead
    n = 0
    gx = x0 + 1.2
    while gx < x1 - 0.6:
        gy = y0 + 1.2
        while gy < y1 - 0.6:
            dead = name == "LOBBY" and n % 5 == 3
            box("Panel", gx - 0.6, gy - 0.3, ch - 0.02, gx + 0.6, gy + 0.3, ch, M_DEAD if dead else M_PANEL, "mood")
            if not dead:
                L = bpy.data.lights.new("PL", "AREA"); L.shape = "RECTANGLE"; L.size = 1.2; L.size_y = 0.6
                L.energy = 70; L.color = (0.93, 1.0, 0.86)
                lo = bpy.data.objects.new("PL", L); scene.collection.objects.link(lo); lo.location = (gx, gy, ch - 0.03)
                MOOD_ONLY.add(lo.name)
            n += 1; gy += 2.4
        gx += 2.4

# door leaves and special openings
M_DOOR = mat("door", "#6A4528", 0.6); M_STEEL = mat("steel", "#8D8C86", 0.4)
M_SHUT = mat("shutter", "#7D7A70", 0.5); M_BLIND = mat("blind", "#D6C9A0", 0.8)
for cx, cy, w, ax, kind in OPEN:
    def sp(a0, a1, z0, z1, m, tag=None, th=T):
        if ax == "x": return box(kind, cx + a0, cy - th / 2, z0, cx + a1, cy + th / 2, z1, m, tag)
        return box(kind, cx - th / 2, cy + a0, z0, cx + th / 2, cy + a1, z1, m, tag)
    hw = w / 2
    if kind in ("door", "locked", "lift", "hatch", "window"):
        sp(-hw, hw, 2.2 if kind != "lift" else 2.4, WALL_H, M_WALL, "mood")  # header
    if kind == "locked":
        sp(-hw, hw, 0, 2.2, M_DOOR, None, 0.06)
    if kind == "hatch":
        sp(-hw, hw, 0, 1.0, M_WOOD, "mood"); sp(-hw, hw, 1.0, 1.06, M_STEEL, "mood", 0.4)
        sp(-hw, hw, 1.06, 2.2, M_SHUT, None, 0.04); sp(-hw, hw, 0, 1.0, M_PLANWALL, "plan")
    if kind == "window":
        sp(-hw, hw, 0, 1.0, M_WOOD, "mood"); sp(-hw, hw, 1.0, 2.2, M_BLIND, None, 0.03)
        sp(-hw, hw, 0, 1.0, M_PLANWALL, "plan")
    if kind == "lift":
        # a folding freight gate, half open
        for i in range(9):
            a = -hw + i * 0.15
            sp(a, a + 0.03, 0, 2.3, M_STEEL, "mood", 0.04)

# ---- props
M_CORK = mat("cork", "#8B6239", 0.9); M_BOARD = mat("boardframe", "#3B2A1E", 0.5)
M_VEND = mat("vend", "#B8432A", 0.4); M_VEND2 = mat("vend2", "#2F5E74", 0.4)
M_SOFA = mat("sofa", "#C3842C", 0.9); M_DESK = mat("desk", "#6B4A2B", 0.6)
M_CHAIR = mat("chair", "#D9661F", 0.7); M_COT = mat("cot", "#E3E0D2", 0.9)
M_LOCK = mat("locker", "#7C8A2E", 0.5); M_PLANT = mat("plant", "#6E5A2E", 0.9)
M_TV = mat("tv", "#2B2622", 0.5); M_SCREEN = mat("screen", "#4C7A86", 0.3, emit=1.5)
M_CLOCK = mat("punch", "#B9B2A0", 0.5); M_POSTER = [mat("p1", "#E8B23A"), mat("p2", "#D9661F"), mat("p3", "#7C8A2E")]
M_MACH = mat("machine", "#5B5A55", 0.4); M_FLAP = mat("flap", "#1F1A16", 0.4, emit=0.6, emit_col="#F0A21E")

# contracts board on the pay office wall, facing the lobby
box("Board", 9.85, 14.3, 0.95, 12.5, 14.33, 2.5, M_BOARD); box("Cork", 10.0, 14.27, 1.1, 12.35, 14.3, 2.35, M_FLAP)
label("NO CONTRACTS POSTED", 11.18, 14.25, 0.15, "#F0A21E", z=1.72, tag="mood").rotation_euler = (math.radians(90), 0, 0)
label("CONTRACTS", 11.18, 14.25, 0.16, "#E8B23A", z=2.18, tag="mood").rotation_euler = (math.radians(90), 0, 0)
label("PAY", 13.2, 14.25, 0.16, "#E8B23A", z=2.3, tag="mood").rotation_euler = (math.radians(90), 0, 0)
# punch clock and card rack on the lift hall's west wall
box("PunchClock", 7.28, 5.0, 1.15, 7.5, 5.45, 1.6, M_CLOCK); box("CardRack", 7.28, 5.65, 0.9, 7.34, 6.4, 1.8, M_STEEL)
# empty reception desk
box("Reception", 14.6, 10.0, 0, 17.6, 10.8, 1.05, M_DESK); box("RecChair", 15.8, 11.1, 0, 16.4, 11.7, 0.95, M_CHAIR)
# sofas and dead plants
box("SofaA", 6.0, 8.0, 0, 6.9, 10.4, 0.8, M_SOFA); box("SofaB", 9.0, 8.0, 0, 9.9, 10.4, 0.8, M_SOFA)
box("LowTable", 7.3, 8.6, 0, 8.6, 9.8, 0.42, M_DESK)
for px, py in ((5.3, 13.8), (18.6, 13.8), (18.6, 7.8), (7.7, 6.6)):
    box("Pot", px - 0.25, py - 0.25, 0, px + 0.25, py + 0.25, 0.55, M_PLANT)
    box("Leaves", px - 0.35, py - 0.35, 0.55, px + 0.35, py + 0.35, 1.1, mat("leafdead", "#8A7A3C", 0.9))
# vending alcove: two machines and coffee
box("Vend1", 20.6, 8.6, 0, 21.5, 9.7, 1.9, M_VEND); box("Vend2", 20.6, 9.9, 0, 21.5, 11.0, 1.9, M_VEND2)
box("Coffee", 20.8, 11.2, 0, 21.5, 11.85, 1.6, M_MACH)
# posters
for x, y, ax, i in ((5.2, 7.25, "x", 0), (16.8, 7.25, "x", 1), (19.13, 13.0, "y", 2), (4.87, 9.6, "y", 0)):
    if ax == "x": box("Poster", x - 0.4, y, 1.4, x + 0.4, y + 0.02, 2.4, M_POSTER[i])
    else: box("Poster", x, y - 0.4, 1.4, x + 0.02, y + 0.4, 2.4, M_POSTER[i])
# training room: TV on a trolley and rows of chairs facing it
box("Trolley", 5.4, 20.5, 0, 6.6, 21.1, 0.9, M_STEEL); box("TV", 5.5, 20.55, 0.9, 6.5, 21.05, 1.65, M_TV)
box("Screen", 5.6, 20.53, 1.0, 6.4, 20.56, 1.55, M_SCREEN)
for r in range(3):
    for c in range(4):
        x = 3.9 + c * 1.1 + (0.0 if c < 2 else 0.6); y = 18.8 - r * 1.3
        box("Chair", x - 0.25, y - 0.25, 0, x + 0.25, y + 0.25, 0.85, M_CHAIR)
# lockers and benches
for i in range(8):
    y = 8.7 + i * 0.6
    box("Locker", 0.08, y, 0, 0.55, y + 0.56, 1.9, M_LOCK)
box("Bench", 1.6, 9.3, 0, 2.0, 13.6, 0.45, M_DESK)
# first aid: four cots and a cabinet
for i in range(4):
    x = 0.3 + i * 1.15
    box("Cot", x, 6.3, 0, x + 0.8, 8.2, 0.55, M_COT)
box("Cabinet", 3.9, 3.75, 0, 4.7, 4.2, 1.8, mat("cab", "#E7E4DA", 0.5))
# manager's office: desk, chair, filing cabinets
box("MgrDesk", 15.6, 17.4, 0, 17.8, 18.3, 0.76, M_DESK); box("MgrChair", 16.4, 18.4, 0, 17.0, 19.0, 1.1, mat("leather", "#3A2418", 0.5))
box("Files", 18.5, 15.0, 0, 19.1, 16.8, 1.35, M_MACH)
# pay office: the machines behind the hatch
box("CashMachine", 10.2, 15.2, 0, 13.8, 16.0, 1.5, M_MACH); box("Conveyor", 12.8, 14.6, 0.9, 13.6, 15.2, 1.0, M_STEEL)
# lift cab floor
box("CabFloor", 10.2, 0.4, 0.0, 12.6, 3.4, 0.03, mat("cabfloor", "#4F4C46", 0.5))

# plan-only markers: the wake cots, the way you walk on a shift start
label("you wake here", 2.4, 5.4, 0.3, "#3B2A1E")
label("locked", 15.6, 14.0, 0.26, "#7A1E10")
label("pay window", 13.2, 13.9, 0.24, "#3B2A1E"); label("contracts board", 11.0, 13.9, 0.24, "#3B2A1E")
label("punch clock", 7.5, 6.6, 0.24, "#3B2A1E", align="LEFT")
label("blinds", 17.4, 13.9, 0.24, "#3B2A1E"); label("TV", 6.0, 21.25, 0.24, "#3B2A1E")
label("reception (empty)", 16.1, 9.6, 0.24, "#3B2A1E")
label("to Level 0", 11.4, 1.4, 0.3, "#E8E2D0")
label("1.2 m grid   one floor   about 22 x 22 m", 11.0, -1.0, 0.36, "#3B2A1E")
# path arrows (dots) from the cots to the lift
for (x, y) in ((2.4, 7.6), (2.4, 9.4), (2.4, 11.2), (3.6, 12.0), (5.6, 12.0), (7.8, 11.4), (9.8, 10.4), (11.4, 9.0), (11.4, 7.4), (11.4, 5.4)):
    box("Path", x - 0.12, y - 0.12, 0.02, x + 0.12, y + 0.12, 0.06, mat("path", "#F0A21E", emit=0.0), "plan")

def show(plan):
    for o in scene.objects:
        if o.name in PLAN_ONLY: o.hide_render = not plan
        elif o.name in MOOD_ONLY: o.hide_render = plan
        elif o.name.startswith("Ceil_"): o.hide_render = plan

# ---- plan render: Workbench, top orthographic
cam_d = bpy.data.cameras.new("PlanCam"); cam_d.type = "ORTHO"; cam_d.ortho_scale = 25.5
cam = bpy.data.objects.new("PlanCam", cam_d); scene.collection.objects.link(cam)
cam.location = (10.8, 10.4, 30); cam.rotation_euler = (0, 0, 0)
scene.camera = cam
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading; sh.light = "FLAT"; sh.color_type = "MATERIAL"
scene.display.shading.background_type = "VIEWPORT" if hasattr(sh, "background_type") else sh.background_type
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new("W") if not scene.world else scene.world
scene.world.color = hexc("#EFE6D2")[:3]
scene.render.resolution_x = 1300; scene.render.resolution_y = 1300
show(True)
scene.render.filepath = os.path.join(OUT, "hub_floorplan.png")
bpy.ops.render.render(write_still=True)

# ---- mood render: EEVEE from inside the lift looking into the lobby
show(False)
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try: scene.render.engine = eng; break
    except TypeError: pass
try: scene.eevee.taa_render_samples = 48
except Exception: pass
try: scene.eevee.use_raytracing = True
except Exception: pass
scene.world.use_nodes = True
bg = next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND"); bg.inputs[0].default_value = (0.02, 0.018, 0.015, 1); bg.inputs[1].default_value = 0.3
scene.view_settings.view_transform = "AgX" if "AgX" in [i.identifier for i in bpy.types.ColorManagedViewSettings.bl_rna.properties["view_transform"].enum_items] else scene.view_settings.view_transform
scene.view_settings.exposure = 0.2
mc = bpy.data.cameras.new("MoodCam"); mc.lens = 18
mcam = bpy.data.objects.new("MoodCam", mc); scene.collection.objects.link(mcam)
mcam.location = (11.4, 2.2, 1.62)
mcam.rotation_euler = (math.radians(88), 0, math.radians(-8))
scene.camera = mcam
scene.render.resolution_x = 1280; scene.render.resolution_y = 960
scene.render.filepath = os.path.join(OUT, "hub_mood_from_lift.png")
bpy.ops.render.render(write_still=True)

# a second mood shot: across the lobby towards the board and the pay window
mcam.location = (6.0, 8.2, 1.62); mcam.rotation_euler = (math.radians(86), 0, math.radians(-38))
scene.render.filepath = os.path.join(OUT, "hub_mood_lobby.png")
bpy.ops.render.render(write_still=True)
mcam.location = (8.9, 15.0, 1.62); mcam.rotation_euler = (math.radians(84), 0, math.radians(28))
scene.render.filepath = os.path.join(OUT, "hub_mood_training.png")
bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "hub_blockout.blend"))
print("DONE")
