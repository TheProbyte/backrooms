"""
Signs (2026-11-04), built in Blender and baked (bake_kit.py), their faces' artwork rendered from real type. Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/build_signs.py -- Assets/Backrooms/Resources/Fittings [--preview <dir>]

  - ExitSign / ExitCombo: a thermoplastic LED exit sign (the common 12.5 x 7.75 x 2.25 in single face, wall mount):
    a white housing with a rolled front, its face plate held by two screws, the test button and the green AC lamp; the
    combo adds the two adjustable emergency lamp heads ("bug eyes") on its top. ExitFace: the face, its stencil EXIT
    in 6 in letters (red diffuser behind white plastic) and the knocked-out chevrons; ExitFace_Emission.png is what
    glows (the letters only).
  - StopSign: a 24 in stop sign (an octagon of 0.080 in aluminium with rounded corners, red sheeting, a white border
    and STOP in 8 in letters, as the MUTCD's R1-1 at its smallest) on a perforated square steel post in a portable
    rubber base, as warehouses put them at forklift crossings; bolts and washers through it, the back bare aluminium.

Frames: ExitSign/ExitCombo/ExitFace origin on the wall at the sign's middle (the wall at y = 0, the sign toward -Y);
StopSign on the floor at the middle of its base, its face toward -Y. Unity (x, y, z) = (-x, z, -y).
"""
import os
import sys
import math
import random
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(HERE, "build_rooms.py"), encoding="utf-8").read()
_cut = _src.index("# ------------------------------------------------------------------------------------------------- break room")
exec(compile(_src[:_cut], "build_rooms_helpers", "exec"))

import bpy
import bmesh
from mathutils import Vector, Matrix

FONT_BOLD = "C:/Windows/Fonts/arialbd.ttf"


def render_art(path, size_m, px, build, bg=(0, 0, 0)):
    """Renders a flat artwork (an orthographic Cycles render of emissive shapes and type, its own scene) to path; build(sc,
    add) puts its pieces in. Returns the loaded image."""
    sc = bpy.data.scenes.new("Art")
    col = sc.collection

    def emit(name, rgb):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        for n in list(nt.nodes):
            if n.type != "OUTPUT_MATERIAL":
                nt.nodes.remove(n)
        e = nt.nodes.new("ShaderNodeEmission")
        e.inputs["Color"].default_value = (*rgb, 1)
        nt.links.new(e.outputs[0], next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL").inputs["Surface"])
        return m

    def add_poly(name, pts, rgb, z):
        me = bpy.data.meshes.new(name)
        me.from_pydata([(x, y, z) for x, y in pts], [], [list(range(len(pts)))])
        o = bpy.data.objects.new(name, me)
        col.objects.link(o)
        o.data.materials.append(emit(name + "M", rgb))
        return o

    def add_text(body, size, x, y, rgb, z=0.01, font=FONT_BOLD, squeeze=1.0, spacing=1.0):
        cu = bpy.data.curves.new("T", "FONT")
        cu.body = body
        cu.size = size
        cu.align_x = "CENTER"
        cu.align_y = "CENTER"
        cu.space_character = spacing
        try:
            cu.font = bpy.data.fonts.load(font)
        except Exception:
            pass
        o = bpy.data.objects.new("Text", cu)
        col.objects.link(o)
        o.location = (x, y, z)
        o.scale = (squeeze, 1.0, 1.0)
        o.data.materials.append(emit("TM", rgb))
        return o

    W, H = size_m
    add_poly("BG", [(-W, -H), (W, -H), (W, H), (-W, H)], bg, -0.01)
    build(add_poly, add_text)
    cam = bpy.data.cameras.new("C")
    cam.type = "ORTHO"
    cam.ortho_scale = 2 * max(W, H)
    co = bpy.data.objects.new("C", cam)
    col.objects.link(co)
    co.location = (0, 0, 1)
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = px
    try:
        sc.render.engine = "CYCLES"
    except TypeError:
        pass
    sc.cycles.device = "GPU" if _gpu_ready else "CPU"
    sc.cycles.samples = 16
    sc.view_settings.view_transform = "Standard"
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True, scene=sc.name)
    img = bpy.data.images.load(path)
    for o in list(sc.collection.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.scenes.remove(sc)
    return img


# ------------------------------------------------------------------------------------------------- exit sign

EXW, EXH, EXD = 0.318, 0.197, 0.057
FW, FH = 0.29, 0.168


def exit_body(combo):
    WH = S("Plastic_ExitHousing", (0.93, 0.93, 0.9), "plastic", 0.4)
    SC = S("Steel_ExitScrew", (0.6, 0.6, 0.58), "metal", 0.4, 1.0)
    RD = S("Plastic_ExitTest", (0.6, 0.08, 0.06), "plastic", 0.4)
    GN = S("Gloss_ExitLamp", (0.2, 0.55, 0.25), "glass", 0.2)
    p = [rbox("Housing", (EXW, EXD, EXH), (0, -EXD / 2, 0), WH, 0.01, 4)]
    # The face's frame: a raised lip round it.
    for (sx, sz, cx, cz) in ((EXW - 0.012, 0.007, 0, FH / 2 + 0.0035), (EXW - 0.012, 0.007, 0, -FH / 2 - 0.0035),
                             (0.007, FH, FW / 2 + 0.0035, 0), (0.007, FH, -FW / 2 - 0.0035, 0)):
        p.append(rbox("Lip", (sx, 0.004, sz), (cx, -EXD - 0.001, cz), WH, 0.0015, 1))
    for x in (-FW / 2 + 0.01, FW / 2 - 0.01):
        p.append(sphere("Screw", (x, -EXD - 0.0012, FH / 2 - 0.01), (0.0035, 0.0012, 0.0035), SC, 10, 5))
    # The test button and the AC lamp on its underside, toward the room.
    p.append(lathe("Test", [(0.0, 0.0), (0.0045, 0.0), (0.0045, 0.003), (0.0035, 0.004), (0.0, 0.004)], RD, 12, xf=M4(rot('X', 180), (0.06, -EXD + 0.012, -EXH / 2))))
    p.append(sphere("Lamp", (0.09, -EXD + 0.012, -EXH / 2 - 0.0005), (0.0025, 0.0025, 0.0018), GN, 10, 6))
    if combo:
        # Two adjustable lamp heads on its top, on swivels, aimed down and out.
        LH = S("Plastic_ExitHeads", (0.93, 0.93, 0.9), "plastic", 0.4)
        LS = S("Glass_ExitHeadLens", (0.85, 0.85, 0.82), "glass", 0.1)
        for sx in (-1, 1):
            base = Vector((sx * 0.1, -EXD / 2, EXH / 2))
            p.append(lathe("Swivel", [(0.0, 0.0), (0.012, 0.0), (0.012, 0.012), (0.008, 0.02), (0.0, 0.02)], LH, 14, xf=M4(None, base)))
            head = lathe("Head", [(0.0, 0.0), (0.024, 0.0), (0.03, 0.01), (0.032, 0.045), (0.034, 0.05), (0.0, 0.05)], LH, 24)
            lens = lathe("Lens", [(0.0, 0.0), (0.03, 0.0), (0.028, 0.004), (0.0, 0.006)], LS, 24, xf=M4(None, (0, 0, 0.05)))
            for o in (head, lens):
                place(o, rot('Y', sx * 50) @ rot('X', 55), base + Vector((0, 0, 0.028)))
            p += [head, lens]
    return finish(p, "ExitCombo" if combo else "ExitSign", sharp_deg=45)


def exit_face():
    tmp = tempfile.gettempdir()

    def art(lit):
        def build(poly, text):
            red = (0.95, 0.04, 0.03) if lit else (0.7, 0.05, 0.04)
            # Stencil letters: block capitals with the stencil's breaks (the bridges in E and the bowl of nothing here).
            text("EXIT", 0.17, 0.0, -0.004, red, squeeze=0.6, spacing=1.18)
            # The chevrons, knocked out (dark when not lit, never glowing).
            if not lit:
                for sx in (-1, 1):
                    x = sx * 0.137
                    poly("Chev", [(x - sx * 0.0, 0.0), (x - sx * 0.022, 0.03), (x - sx * 0.032, 0.03), (x - sx * 0.01, 0.0),
                                  (x - sx * 0.032, -0.03), (x - sx * 0.022, -0.03)], (0.82, 0.82, 0.8), 0.0)
        return build
    face = render_art(os.path.join(tmp, "exit_face.png"), (FW / 2, FH / 2), (512, 296), art(False), bg=(0.93, 0.93, 0.9))
    glow = render_art(os.path.join(tmp, "exit_glow.png"), (FW / 2, FH / 2), (512, 296), art(True), bg=(0.0, 0.0, 0.0))
    # The glow is saved beside the model as it is (no bake: it is light, not paint).
    import shutil
    shutil.copyfile(os.path.join(tmp, "exit_glow.png"), os.path.join(os.path.abspath(OUT), "ExitFace_Emission.png"))
    m = mat("Print_ExitFace", (1, 1, 1), 0.3)
    m["linear"] = 1
    m["print"] = face.name
    return join([card("Face", FW, FH, (0, -EXD - 0.0006, 0), m)], "ExitFace")


# ------------------------------------------------------------------------------------------------- stop sign

def stop_sign():
    tmp = tempfile.gettempdir()
    S24 = 0.61              # across the flats
    r_out = S24 / 2 / math.cos(math.pi / 8)

    def octa(rr, z=0.0, rnd=0.0, n=4):
        pts = []
        for k in range(8):
            a = math.pi / 8 + k * math.pi / 4
            if rnd > 0:
                # a rounded corner: an arc of radius rnd at the vertex
                c = Vector((math.cos(a), math.sin(a))) * (rr - rnd / math.cos(math.pi / 8))
                for j in range(n + 1):
                    b = a - math.pi / 8 + (math.pi / 4) * j / n
                    pts.append((c.x + rnd * math.cos(b), c.y + rnd * math.sin(b)))
            else:
                pts.append((rr * math.cos(a), rr * math.sin(a)))
        return pts

    def build(poly, text):
        poly("Red", octa(r_out, 0.0, 0.0254), (0.68, 0.035, 0.04), 0.0)
        poly("Border", octa(r_out - 0.006, 0.001, 0.02), (0.95, 0.95, 0.93), 0.001)
        poly("Field", octa(r_out - 0.022, 0.002, 0.012), (0.68, 0.035, 0.04), 0.002)
        text("STOP", 0.27, 0.0, -0.008, (0.97, 0.97, 0.95), z=0.01, squeeze=0.8, spacing=1.04)
    art = render_art(os.path.join(tmp, "stop_face.png"), (r_out, r_out), (768, 768), build, bg=(0.68, 0.035, 0.04))

    FACE = mat("Paint_StopFace", (1, 1, 1), 0.35)
    FACE["linear"] = 1
    FACE["print"] = art.name
    FACE["finish"] = "print"
    AL = S("Alu_StopBack", (0.72, 0.73, 0.74), "metal", 0.45, 1.0)
    GAL = S("Galv_StopPost", (0.62, 0.63, 0.62), "galv", 0.45, 1.0)
    RB = S("Rubber_StopBase", (0.07, 0.07, 0.07), "rubber", 0.8)
    zc = 1.72
    # The plate: its front printed (UV over the octagon), its edge and back aluminium.
    pts = octa(r_out, 0.0, 0.0254)
    bm = bmesh.new()
    fr = [bm.verts.new((x, -0.024, zc + y)) for x, y in pts]
    bk = [bm.verts.new((x, -0.022, zc + y)) for x, y in pts]
    ff = bm.faces.new(fr)
    bf = bm.faces.new(bk[::-1])
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((fr[i], fr[j], bk[j], bk[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    plate = new_obj("Plate", bm, [FACE, AL])
    me = plate.data
    uv = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        front = poly.normal.y < -0.9
        poly.material_index = 0 if front else 1
        for li in poly.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            uv[li].uv = ((v.x + r_out) / (2 * r_out), (v.z - zc + r_out) / (2 * r_out)) if front else (v.x / 0.25, v.z / 0.25)
    # The post: a perforated square tube; its holes printed dark down the faces.
    post = rbox("Post", (0.044, 0.044, 2.0), (0, 0, 0.06 + 1.0), GAL, 0.004, 2)
    p = [post]
    for z in (zc + 0.2, zc - 0.2):
        p.append(lathe("Bolt", [(0.0, 0.0), (0.008, 0.0), (0.008, 0.004), (0.006, 0.006), (0.0, 0.007)], GAL, 8, xf=M4(rot('X', 90), (0, -0.0242, z))))
        p.append(lathe("Washer", [(0.0, 0.0), (0.014, 0.0), (0.014, 0.0015), (0.0, 0.0015)], GAL, 14, xf=M4(rot('X', 90), (0, -0.0243, z))))
    # A portable base: a heavy rubber square with a steel socket.
    p.append(rbox("Base", (0.5, 0.5, 0.06), (0, 0, 0.03), RB, 0.02, 3))
    p.append(rbox("Socket", (0.07, 0.07, 0.12), (0, 0, 0.06 + 0.06), GAL, 0.005, 2))
    for sx in (-1, 1):
        p.append(lathe("Pin", [(0.0, 0.0), (0.008, 0.0), (0.008, 0.02), (0.0, 0.02)], GAL, 8, xf=M4(rot('Y', 90 * sx), (sx * 0.035, 0, 0.14))))
    base = finish(p, "StopSign", sharp_deg=45)
    hole = Canvas(16, 256, (0.62, 0.63, 0.62))
    for k in range(32):
        hole.rect(5, 4 + k * 8, 11, 8 + k * 8, (0.05, 0.05, 0.05))
    holes = [card("Holes", 0.012, 1.4, (0, -0.0221, 0.82), print_mat("Galv_StopHoles", hole))]
    for poly in plate.data.polygons:
        poly.use_smooth = False
    return join([base, plate] + holes, "StopSign")


run_baked([
    ("ExitSign", lambda: exit_body(False), (0.5, -1.0, -0.2), {"res": 512, "scale": 0.25}),
    ("ExitCombo", lambda: exit_body(True), (0.5, -1.0, 0.2), {"res": 512, "scale": 0.25}),
    ("ExitFace", exit_face, (0.0, -1.0, 0.0), {"res": 512, "scale": 0.2, "dirt": 0.4, "wear": 0.3}),
    ("StopSign", stop_sign, (0.5, -1.0, 0.1), {"scale": 0.8, "px_per_m": 700.0}),
])
print("[signs] done ->", OUT)
