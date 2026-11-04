"""
Pictures of the game's baked models (2026-11-04): imports their FBX and textures from Resources/Fittings, puts them
together as the game does (a sign's face on its housing, a vending machine's glow on it), stands them in a small
Backrooms room (yellow wallpaper, a carpet, a ceiling light) and renders them with Cycles. Run headless:

    F:\\Blender\\blender.exe -b --factory-startup --python Tools/Blender/showcase.py -- <Fittings dir> <out dir> <set>[,<set>...]

Sets: vending, exit, stop, lawn, school. Writes <out>/<set>_<view>.jpg. Finishes come from the slot names' prefixes as
the game reads them (BakedMaterials): Chrome_ shiny metal, Paint_ satin, Plastic_ ...; a glow model's faces shine with
its _Emission.png (or its colour) at the game's strength.
"""
import os
import sys
import math

import bpy
from mathutils import Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC, OUT = ARGS[0], ARGS[1]
SETS = ARGS[2].split(",") if len(ARGS) > 2 else ["vending"]

FIN = {"Chrome": (0.1, 1.0), "Stainless": (0.28, 1.0), "Steel": (0.4, 1.0), "Galv": (0.45, 1.0), "Alu": (0.32, 1.0),
       "Brass": (0.3, 1.0), "Paint": (0.42, 0.0), "Enamel": (0.3, 0.0), "Plastic": (0.42, 0.0), "Gloss": (0.18, 0.0),
       "Matte": (0.8, 0.0), "Rubber": (0.75, 0.0), "Laminate": (0.35, 0.0), "Wood": (0.55, 0.0), "Fabric": (0.9, 0.0),
       "Card": (0.85, 0.0), "Paper": (0.7, 0.0), "Print": (0.55, 0.0), "Glass": (0.04, 0.0), "Screen": (0.1, 0.0),
       "Glow": (0.18, 0.0), "Concrete": (0.85, 0.0), "Tape": (0.5, 0.0), "Board": (0.7, 0.0)}


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def gpu(scene):
    scene.render.engine = "CYCLES"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for kind in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = kind
                prefs.get_devices()
                if any(d.type == kind for d in prefs.devices):
                    for d in prefs.devices:
                        d.use = d.type == kind
                    scene.cycles.device = "GPU"
                    return
            except TypeError:
                continue
    except Exception:
        pass


def tex(path, srgb=True):
    if not os.path.exists(path):
        return None
    im = bpy.data.images.load(path, check_existing=True)
    if not srgb:
        im.colorspace_settings.name = "Non-Color"
    return im


def load(name, at=(0, 0, 0), yaw=0.0, emit=0.0):
    """Imports <name>.fbx, its albedo on every slot through its one UV map, the finish from each slot's name."""
    before = set(bpy.data.objects)
    if not os.path.exists(os.path.join(SRC, name + ".fbx")):
        print("MISSING", name)
        return []
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, name + ".fbx"))
    obs = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    alb = tex(os.path.join(SRC, name + "_Albedo.png"))
    glow = tex(os.path.join(SRC, name + "_Emission.png"))
    for o in obs:
        o.rotation_mode = "XYZ"
        o.location = Vector(at)
        o.rotation_euler = (0, 0, math.radians(yaw))
        o.scale = (1, 1, 1)
        for i, slot in enumerate(o.material_slots):
            src = slot.material
            key = (src.name if src else "Plastic_x").split(".")[0]
            m = bpy.data.materials.new(name + ":" + key)
            m.use_nodes = True
            nt = m.node_tree
            b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
            rough, metal = FIN.get(key.split("_")[0], (0.5, 0.0))
            b.inputs["Roughness"].default_value = rough
            b.inputs["Metallic"].default_value = metal
            uv = nt.nodes.new("ShaderNodeUVMap")
            uv.uv_map = o.data.uv_layers[0].name if o.data.uv_layers else ""
            if alb:
                t = nt.nodes.new("ShaderNodeTexImage")
                t.image = alb
                nt.links.new(uv.outputs["UV"], t.inputs["Vector"])
                nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
            if emit > 0:
                g = nt.nodes.new("ShaderNodeTexImage")
                g.image = glow or alb
                nt.links.new(uv.outputs["UV"], g.inputs["Vector"])
                nt.links.new(g.outputs["Color"], b.inputs["Emission Color"])
                b.inputs["Emission Strength"].default_value = emit
            if key.startswith("Glass"):
                b.inputs["Coat Weight"].default_value = 0.5
            o.material_slots[i].material = m
        for p in o.data.polygons:
            p.use_smooth = p.use_smooth
    return obs


def surface(name, size, center, rgb, rough=0.8, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_plane_add(size=1.0, location=center, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = (size[0], size[1], 1)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    o.data.materials.append(m)
    return o


def room(wall_y=0.5, light=(0.0, -1.2, 2.7), energy=260, lit=True):
    """A corner of the Backrooms: wallpaper behind (its face at y = wall_y), carpet, a ceiling and a troffer's light."""
    surface("Floor", (8, 8), (0, -2, 0), (0.34, 0.27, 0.13), 0.95)
    surface("Wall", (8, 3), (0, wall_y, 1.5), (0.62, 0.54, 0.27), 0.85, (math.radians(90), 0, 0))
    surface("Ceiling", (8, 8), (0, -2, 2.9), (0.7, 0.68, 0.6), 0.9, (math.radians(180), 0, 0))
    if lit:
        L = bpy.data.lights.new("Troffer", "AREA")
        L.energy = energy
        L.shape = "RECTANGLE"
        L.size, L.size_y = 0.6, 1.2
        L.color = (1.0, 0.96, 0.86)
        o = bpy.data.objects.new("Troffer", L)
        bpy.context.scene.collection.objects.link(o)
        o.location = light
    w = bpy.data.worlds.new("W")
    bpy.context.scene.world = w
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.5, 0.45, 0.3, 1)
    bg.inputs["Strength"].default_value = 0.12


def shot(name, loc, look, lens=35, res=(1200, 900), samples=160, exposure=0.0):
    sc = bpy.context.scene
    cam = bpy.data.objects.get("Cam")
    if cam is None:
        cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
        sc.collection.objects.link(cam)
    cam.data.lens = lens
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "Standard"        # (AgX turns a lit sign's red to salmon)
    sc.view_settings.exposure = exposure
    sc.render.image_settings.file_format = "JPEG"
    sc.render.image_settings.quality = 90
    sc.render.filepath = os.path.join(OUT, name + ".jpg")
    bpy.ops.render.render(write_still=True)
    print("SHOT", sc.render.filepath)


def vending():
    clear()
    sc = bpy.context.scene
    gpu(sc)
    room(wall_y=0.48, light=(0.3, -1.4, 2.85), energy=220)
    load("VendingMachine")
    load("VendingGlow", emit=1.0)
    shot("vending_front", (0.0, -3.1, 1.0), (0.0, 0.0, 0.92), 32, (900, 1300))
    shot("vending_three_quarter", (1.5, -2.4, 1.45), (0.0, 0.0, 0.95), 30, (1100, 1300))
    shot("vending_controls", (0.55, -1.0, 1.25), (0.28, -0.42, 1.12), 40, (1000, 1200))
    shot("vending_port", (-0.15, -1.2, 0.75), (-0.02, -0.42, 0.42), 40, (1200, 900))
    # in the dark: its own glow only
    for o in list(bpy.data.objects):
        if o.type == "LIGHT":
            bpy.data.objects.remove(o)
    shot("vending_dark", (1.0, -2.8, 1.2), (0.0, 0.0, 0.95), 30, (1100, 1300), exposure=0.0)


def exit_signs():
    clear()
    sc = bpy.context.scene
    gpu(sc)
    room(wall_y=0.0, light=(0.0, -1.2, 2.85), energy=30)
    load("ExitSign", (-0.4, 0.0, 2.2), 0)
    load("ExitFace", (-0.4, 0.0, 2.2), 0, emit=1.3)
    load("ExitCombo", (0.4, 0.0, 2.2), 0)
    load("ExitFace", (0.4, 0.0, 2.2), 0, emit=1.3)
    shot("exit_pair", (0.0, -1.6, 1.75), (0.0, 0.0, 2.2), 40, (1400, 800))
    shot("exit_close", (-0.15, -0.55, 2.0), (-0.4, 0.0, 2.2), 45, (1200, 900))


def stop_sign():
    clear()
    sc = bpy.context.scene
    gpu(sc)
    surface("Floor", (8, 8), (0, 0, 0), (0.42, 0.42, 0.4), 0.85)
    room(wall_y=2.0, light=(0.6, -1.4, 2.8), energy=80)
    load("StopSign", (0, 0, 0), 0)
    shot("stop_front", (0.5, -2.6, 1.5), (0.0, 0.0, 1.2), 35, (900, 1200))
    shot("stop_close", (0.35, -0.9, 1.75), (0.0, 0.0, 1.72), 40, (1100, 900))


def chairs(prefix, names):
    clear()
    sc = bpy.context.scene
    gpu(sc)
    room(wall_y=1.2, light=(0.0, -0.6, 2.85), energy=200)
    xs = [(-0.42 + 0.84 * i / max(1, len(names) - 1)) if len(names) > 1 else 0.0 for i in range(len(names))]
    for x, n in zip(xs, names):
        load(n, (x, 0.0, 0.0), 0)
    shot(prefix + "_three_quarter", (1.3, -1.9, 1.25), (0.0, 0.0, 0.42), 35, (1300, 950))
    shot(prefix + "_front", (0.0, -2.1, 0.75), (0.0, 0.0, 0.42), 35, (1300, 950))
    shot(prefix + "_back", (-1.2, 1.0, 1.1), (0.0, 0.0, 0.45), 35, (1300, 950))


def boombox(lit=True):
    """The boombox put together as the game does (its parts at their pivots from Boombox_Layout.json), the cassette
    beside it, and the cassette alone."""
    import json
    clear()
    sc = bpy.context.scene
    gpu(sc)
    room(wall_y=0.6, light=(0.3, -0.6, 2.85), energy=120)
    lay = json.load(open(os.path.join(SRC, "Boombox_Layout.json"), encoding="utf-8"))
    load("Boombox")
    load("BoomboxFace", emit=1.2 if lit else 0.0)
    for i in range(6):
        load("BoomboxKey%d" % i, tuple(lay["key%d" % i]))
    load("BoomboxDoor", tuple(lay["door"]))
    load("BoomboxHandle", tuple(lay["handle"]))
    load("Cassette", (0.34, -0.12, 0.0), 25)
    shot("boombox_front", (0.0, -0.95, 0.42), (0.0, 0.0, 0.1), 40, (1300, 900))
    shot("boombox_three_quarter", (-0.62, -0.75, 0.55), (0.0, 0.0, 0.08), 40, (1300, 900))
    shot("boombox_panel", (0.05, -0.42, 0.2), (0.0, -0.14, 0.11), 45, (1300, 900))
    shot("boombox_top", (0.25, -0.35, 0.75), (0.0, 0.0, 0.18), 40, (1300, 900))
    shot("boombox_back", (0.5, 0.56, 0.42), (0.0, 0.08, 0.1), 38, (1300, 900))
    shot("cassette_close", (0.42, -0.25, 0.16), (0.34, -0.12, 0.005), 45, (1300, 900))


os.makedirs(OUT, exist_ok=True)
for s in SETS:
    if s == "vending":
        vending()
    elif s == "exit":
        exit_signs()
    elif s == "stop":
        stop_sign()
    elif s == "lawn":
        chairs("lawn", ["LawnChair_White", "LawnChair_Green"])
    elif s == "boombox":
        boombox()
    elif s == "school":
        chairs("school", ["SchoolChair_Blue", "SchoolChair_Orange"])
print("[showcase] done ->", OUT)
