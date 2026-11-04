"""
Baked surface textures for the game's models (2026-11-04): each model gets one unique colour texture painted by Cycles from
its own shape, the way game props are textured (a tiling material for the fine grain, a unique bake for what happened to
this one object):

  - colour variation: a mottle in the paint or plastic, a few per cent;
  - dirt in the creases: ambient occlusion (only the model's own geometry, a few centimetres), so seams, corners and
    the gaps between parts are darker and browner;
  - worn edges: where a Bevel node's rounded normal leaves the true face normal (the convex edges), broken up with noise
    so they do not wear evenly: paint chipped to the metal under it, plastic and enamel scuffed paler, wood rubbed lighter;
  - grime: a low-frequency darkening, streaks running down vertical faces, a little dust on the faces that look up;
  - prints: a slot with a "print" image samples it with the model's own print UVs ("Tile").

Exec this file after build_items2.py's helpers (it uses their mat/finish conventions). In each build script:

    bake(ob, "Fridge", out_dir)        # after finish(), before export()

The object keeps its material slots (the game gives each its gloss, metal and fine-grain map); the bake UV ("Bake",
packed 0..1 at one texel density, islands from the model's own smart projection) becomes the first UV map, so the game's
uv0 reads the bake and its detail map tiles over it at the old 0.25 m (FittingModels / ItemModels work the density out
from the mesh). Writes <out_dir>/<name>_Albedo.png.

Finishes (how a slot ages) come from FINISH by slot-name keywords, or a material's own ["finish"] property.
Sources: Adobe's Substance 3D Painter docs on edge wear from curvature and dirt from AO
(experienceleague.adobe.com/en/docs/substance-3d-painter/using/effects/generators/metal-edge-wear), and the
bake-the-masks-then-break-them-up-with-noise recipe (strayspark.studio/blog/procedural-damage-game-assets-complete-workflow).
"""
import math
import os

import bpy
from mathutils import Vector

# finish: (dirt, edge wear, edge colour (None: base lightened), mottle, grime, streaks, dust)
FINISH = {
    "paint":     (0.55, 0.55, (0.36, 0.36, 0.35), 0.05, 0.35, 0.35, 0.25),   # painted steel: chips to bare metal
    "enamel":    (0.50, 0.35, None,               0.03, 0.30, 0.30, 0.25),   # appliance enamel, porcelain
    "plastic":   (0.55, 0.45, None,               0.05, 0.35, 0.20, 0.30),   # moulded plastic: scuffs paler
    "beige":     (0.60, 0.40, None,               0.06, 0.45, 0.20, 0.35),   # old computer plastic: yellowed, grubby
    "metal":     (0.45, 0.25, None,               0.04, 0.25, 0.25, 0.15),   # stainless, chrome: polished edges
    "galv":      (0.50, 0.30, None,               0.10, 0.35, 0.35, 0.20),   # galvanised, zinc
    "rubber":    (0.35, 0.20, (0.13, 0.13, 0.13), 0.04, 0.30, 0.10, 0.40),   # black rubber: edges greyed
    "wood":      (0.55, 0.50, None,               0.10, 0.35, 0.15, 0.25),   # wood: edges rubbed lighter
    "laminate":  (0.40, 0.30, None,               0.03, 0.25, 0.05, 0.20),
    "fabric":    (0.45, 0.15, None,               0.06, 0.35, 0.05, 0.30),
    "glass":     (0.20, 0.05, None,               0.00, 0.10, 0.10, 0.10),
    "print":     (0.30, 0.25, None,               0.02, 0.25, 0.10, 0.15),   # a printed label: a little scuffed
    "clean":     (0.25, 0.10, None,               0.02, 0.10, 0.05, 0.05),
    "stone":     (0.70, 0.30, None,               0.08, 0.50, 0.25, 0.20),   # moulded stone, terrazzo, concrete
    "board":     (0.55, 0.45, None,               0.08, 0.40, 0.10, 0.35),   # particleboard, card
    "chalk":     (0.25, 0.05, None,               0.05, 0.20, 0.00, 0.10),   # chalk: a little grubby where held
}

KEYS = [
    # the hand-held items' slot names (build_items*.py)
    ("chalk", "chalk"), ("tapecore", "board"), ("core", "board"), ("tape", "fabric"), ("cord", "rubber"),
    ("lampbody", "paint"), ("shade", "paint"), ("lanternmetal", "metal"), ("lantern", "plastic"), ("chicken", "rubber"),
    ("bag", "fabric"), ("strap", "fabric"), ("shelf", "paint"), ("hammerwood", "wood"), ("hammerhead", "metal"),
    ("nailbox", "board"), ("socket", "plastic"), ("plugmetal", "metal"), ("prong", "metal"), ("plug", "plastic"),
    ("canmetal", "metal"), ("cancap", "plastic"), ("canlabel", "print"), ("canred", "paint"), ("bulb", "glass"),
    ("globe", "glass"), ("nail", "metal"),
    ("print", "print"), ("label", "print"), ("glass", "glass"), ("lens", "glass"), ("screen", "glass"),
    ("rubber", "rubber"), ("gasket", "rubber"), ("tyre", "rubber"), ("foot", "rubber"), ("glide", "rubber"),
    ("wood", "wood"), ("laminate", "laminate"), ("fabric", "fabric"), ("cushion", "fabric"),
    ("stainless", "metal"), ("chrome", "metal"), ("steelbare", "metal"), ("brass", "metal"), ("pin", "metal"),
    ("galv", "galv"), ("zinc", "galv"), ("stone", "stone"), ("concrete", "stone"), ("board", "board"),
    ("card", "board"), ("paper", "print"),
    ("enamel", "enamel"), ("porcelain", "enamel"), ("beige", "beige"),
    ("paint", "paint"), ("steel", "paint"), ("housing", "paint"),
    ("plastic", "plastic"), ("dark", "plastic"), ("black", "plastic"), ("white", "plastic"),
]

DIRT = (0.105, 0.088, 0.066)          # the dirt colour (sRGB-ish, used linear): brown-grey
DUST = (0.55, 0.53, 0.5)


def lin(c):
    """An sRGB colour (as the game's materials give them) to Blender's linear base colour."""
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def smat(name, srgb, rough=0.5, metal=0.0, finish=None):
    """A slot material from an sRGB colour (the bake saves sRGB, so the game shows this colour)."""
    m = mat(name, lin(srgb), rough, metal)
    m["linear"] = 1
    if finish:
        m["finish"] = finish
    return m


def finish_of(m):
    f = m.get("finish") if hasattr(m, "get") else None
    if f in FINISH:
        return f
    n = m.name.lower()
    for k, v in KEYS:
        if k in n:
            return v
    return "plastic"


# ---------------------------------------------------------------------------------------------------- the GPU

_gpu_ready = None


def use_gpu(scene):
    """Cycles on the GPU when there is one (OptiX, then CUDA), else the CPU."""
    global _gpu_ready
    try:
        scene.render.engine = "CYCLES"
    except TypeError:
        pass
    if _gpu_ready is None:
        _gpu_ready = False
        try:
            prefs = bpy.context.preferences.addons["cycles"].preferences
            for kind in ("OPTIX", "CUDA", "HIP", "ONEAPI", "METAL"):
                try:
                    prefs.compute_device_type = kind
                except TypeError:
                    continue
                prefs.get_devices()
                devs = [d for d in prefs.devices if d.type == kind]
                if devs:
                    for d in prefs.devices:
                        d.use = d.type == kind
                    _gpu_ready = True
                    break
        except Exception as e:      # (headless without the add-on: the CPU)
            print("bake_kit: no GPU:", e)
    scene.cycles.device = "GPU" if _gpu_ready else "CPU"


# ---------------------------------------------------------------------------------------------------- the layout

def _objs(ob):
    return [o for o in (ob if isinstance(ob, (list, tuple)) else [ob]) if o.type == "MESH"]


def bake_layout(ob, res):
    """A second UV map, "Bake": the model's smart projection again, islands at one texel density, packed into 0..1 with
    a few pixels between them. The existing map is renamed "Tile" (prints and grain read from it while baking).
    ob: an object, or a list of them packed together into one layout."""
    objs = _objs(ob)
    for o in objs:
        me = o.data
        if not me.uv_layers:
            me.uv_layers.new(name="Tile")
        if "Tile" not in me.uv_layers:
            me.uv_layers[0].name = "Tile"
        if "Bake" in me.uv_layers:
            me.uv_layers.remove(me.uv_layers["Bake"])
        bl = me.uv_layers.new(name="Bake")
        me.uv_layers.active = bl
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0, area_weight=0.0, correct_aspect=True,
                             scale_to_bounds=False)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    try:
        bpy.ops.uv.pack_islands(rotate=True, scale=True, margin_method="FRACTION", margin=6.0 / res, shape_method="CONCAVE")
    except TypeError:
        bpy.ops.uv.pack_islands(rotate=True, margin=6.0 / res)
    bpy.ops.object.mode_set(mode="OBJECT")


def px_per_metre(ob):
    """The bake's texel density (UV units per metre of surface), area weighted: the game tiles its detail map with it."""
    a3 = a2 = 0.0
    for o in _objs(ob):
        me = o.data
        sc = o.matrix_world.to_scale()
        k = abs(sc.x * sc.y * sc.z) ** (2.0 / 3.0)
        uv = me.uv_layers["Bake"].data
        a3 += sum(p.area for p in me.polygons) * k
        a2 += sum(0.5 * abs(sum(uv[p.loop_indices[i - 1]].uv[0] * uv[p.loop_indices[i]].uv[1] -
                                uv[p.loop_indices[i]].uv[0] * uv[p.loop_indices[i - 1]].uv[1]
                                for i in range(len(p.loop_indices)))) for p in me.polygons)
    return math.sqrt(a2 / max(a3, 1e-9))


# ---------------------------------------------------------------------------------------------------- the paint

def _n(nt, kind, x, y, **inputs):
    node = nt.nodes.new(kind)
    node.location = (x, y)
    for k, v in inputs.items():
        try:
            node.inputs[k].default_value = v
        except (KeyError, TypeError):
            setattr(node, k, v)
    return node


def _math(nt, op, a, b=None, x=0, y=0, clamp=False, c=None):
    m = nt.nodes.new("ShaderNodeMath")
    m.operation = op
    m.use_clamp = bool(clamp)
    m.location = (x, y)
    for i, v in enumerate((a, b, c)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            m.inputs[i].default_value = v
        else:
            nt.links.new(v, m.inputs[i])
    return m.outputs[0]


def _mix(nt, fac, a, b, x=0, y=0, blend="MIX"):
    m = nt.nodes.new("ShaderNodeMix")
    m.data_type = "RGBA"
    m.blend_type = blend
    m.location = (x, y)
    ins = [i for i in m.inputs if i.type in ("RGBA", "VALUE")]
    # inputs: Factor (float), A (color), B (color) for RGBA data
    fac_in = m.inputs["Factor"] if "Factor" in m.inputs else m.inputs[0]
    a_in = next(i for i in m.inputs if i.name == "A" and i.type == "RGBA")
    b_in = next(i for i in m.inputs if i.name == "B" and i.type == "RGBA")
    for sock, v in ((fac_in, fac), (a_in, a), (b_in, b)):
        if isinstance(v, (int, float)):
            sock.default_value = v
        elif isinstance(v, tuple):
            sock.default_value = (*v[:3], 1.0) if len(v) == 3 else v
        else:
            nt.links.new(v, sock)
    out = next(o for o in m.outputs if o.type == "RGBA")
    return out


def paint_material(m, image, seed, scale=1.0, ao_dist=0.05, wear=1.0, dirt=1.0, edge_r=0.0025):
    """Rewires a slot's material to emit its aged colour for the bake (the Principled node is kept for the export).
    scale: the model's size (1 for furniture, ~0.2 for a hand-held item): noise and AO distances follow it."""
    f = FINISH[finish_of(m)]
    d_amt, e_amt, e_col, mottle, grime, streak, dust = f
    d_amt *= dirt
    e_amt *= wear
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    base_col = tuple(bsdf.inputs["Base Color"].default_value)[:3]
    if not m.get("linear") and not m.get("print"):
        # (an older model's colour: the game read it from the FBX as an sRGB colour)
        base_col = lin(base_col)

    # The base: the slot's colour, or its print through the Tile UVs.
    pimg = m.get("print")
    if pimg and bpy.data.images.get(pimg):
        uvn = _n(nt, "ShaderNodeUVMap", -1400, 300)
        uvn.uv_map = "Tile"
        tex = _n(nt, "ShaderNodeTexImage", -1200, 300)
        tex.image = bpy.data.images[pimg]
        tex.interpolation = "Closest" if m.get("print_sharp") else "Linear"
        tex.extension = "EXTEND"
        nt.links.new(uvn.outputs["UV"], tex.inputs["Vector"])
        base = tex.outputs["Color"]
    else:
        rgb = _n(nt, "ShaderNodeRGB", -1200, 300)
        rgb.outputs[0].default_value = (*base_col, 1.0)
        base = rgb.outputs[0]

    geo = _n(nt, "ShaderNodeNewGeometry", -1600, -200)
    tco = _n(nt, "ShaderNodeTexCoord", -1800, -500)
    obj = _n(nt, "ShaderNodeTexCoord", -1600, -500)
    off = nt.nodes.new("ShaderNodeVectorMath")
    off.operation = "ADD"
    off.location = (-1450, -500)
    nt.links.new(obj.outputs["Object"], off.inputs[0])
    off.inputs[1].default_value = (seed * 1.37 % 50, seed * 2.11 % 50, seed * 0.73 % 50)
    pos = off.outputs[0]

    # Wood: growth rings round a log lying along the grain axis, sliced by each face (flat-sawn figure on the broad
    # faces, end grain at the ends), the rings wobbled by noise; m["grain"] = (light, dark, rings a metre), m["grain_axis"].
    gr = m.get("grain")
    if gr:
        axis = m.get("grain_axis", "X")
        light, dark, freq = tuple(gr[0:3]), tuple(gr[3:6]), gr[6]
        keep = {"X": (0, 1, 1), "Y": (1, 0, 1), "Z": (1, 1, 0)}[axis]
        # The log lies a few degrees off the board (so the rings cut the faces in arches, the cathedral figure), its
        # heart well off to one side.
        tilt = nt.nodes.new("ShaderNodeMapping")
        tilt.location = (-1450, 500)
        tilt.inputs["Rotation"].default_value = tuple(math.radians(a) for a in
                                                      {"X": (0, 1.2, 1.8), "Y": (1.5, 0, 1.0), "Z": (1.0, 1.5, 0)}[axis])
        tilt.inputs["Location"].default_value = tuple(((seed * 0.37) % 1.0 + 0.45) * (1 if k else 0) for k in keep)
        nt.links.new(tco.outputs["Object"], tilt.inputs["Vector"])
        warp = _n(nt, "ShaderNodeTexNoise", -1450, 700, Scale=2.5, Detail=4.0, Roughness=0.55)
        st = nt.nodes.new("ShaderNodeMapping")
        st.location = (-1600, 700)
        st.inputs["Scale"].default_value = tuple(0.3 if not k else 3.0 for k in keep)
        nt.links.new(tco.outputs["Object"], st.inputs["Vector"])
        nt.links.new(st.outputs["Vector"], warp.inputs["Vector"])
        lg = nt.nodes.new("ShaderNodeVectorMath")
        lg.operation = "MULTIPLY"
        lg.location = (-1300, 500)
        nt.links.new(tilt.outputs["Vector"], lg.inputs[0])
        lg.inputs[1].default_value = keep
        rr = nt.nodes.new("ShaderNodeVectorMath")
        rr.operation = "LENGTH"
        rr.location = (-1150, 500)
        nt.links.new(lg.outputs[0], rr.inputs[0])
        ring = _math(nt, "MULTIPLY_ADD", warp.outputs["Fac"], 0.05, -1000, 600, c=-0.025)
        ring = _math(nt, "ADD", rr.outputs["Value"], ring, -850, 550)
        ring = _math(nt, "MULTIPLY", ring, freq * 2 * math.pi, -700, 550)
        ring = _math(nt, "SINE", ring, None, -550, 550)
        ring = _math(nt, "MULTIPLY_ADD", ring, 0.5, -400, 550, c=0.5)
        ring = _math(nt, "POWER", ring, 2.5, -250, 550)
        # Fine streaks along the grain (the pores), and broad changes in tone board to board.
        fine = _n(nt, "ShaderNodeTexNoise", -500, 750, Scale=1.0, Detail=3.0, Roughness=0.6)
        st2 = nt.nodes.new("ShaderNodeMapping")
        st2.location = (-650, 750)
        st2.inputs["Scale"].default_value = tuple(6.0 if not k else 260.0 for k in keep)
        nt.links.new(tco.outputs["Object"], st2.inputs["Vector"])
        nt.links.new(st2.outputs["Vector"], fine.inputs["Vector"])
        pores = _math(nt, "MULTIPLY_ADD", fine.outputs["Fac"], 0.7, -100, 650, c=-0.25)
        tone = _math(nt, "MULTIPLY_ADD", warp.outputs["Fac"], 0.3, 120, 700, c=-0.1)
        ring2 = _math(nt, "ADD", _math(nt, "MULTIPLY", ring, 0.4, 0, 450), pores, 120, 450)
        ring2 = _math(nt, "ADD", ring2, tone, 200, 600, clamp=True)
        base = _mix(nt, ring2, lin(light), lin(dark), 350, 500)

    # Flecks (moulded stone, terrazzo, particleboard): m["speckle"] = (flecks a metre, dark rgb, light rgb).
    sp = m.get("speckle")
    if sp:
        freq, dk, lt = sp[0], tuple(sp[1:4]), tuple(sp[4:7])
        for k, (fc, thr) in enumerate(((dk, 0.16), (lt, 0.12))):
            vor = nt.nodes.new("ShaderNodeTexVoronoi")
            vor.location = (-1100, 900 + k * 200)
            vor.inputs["Scale"].default_value = freq * (1.0 + 0.6 * k)
            nt.links.new(pos, vor.inputs["Vector"])
            spot = _math(nt, "LESS_THAN", vor.outputs["Distance"], thr, -900, 900 + k * 200)
            base = _mix(nt, spot, base, lin(fc), -700, 900 + k * 200)

    # Mottle: a soft, slightly coloured variation in the finish.
    if mottle > 0:
        nz = _n(nt, "ShaderNodeTexNoise", -1200, 0, Scale=6.0 / scale, Detail=4.0, Roughness=0.55)
        nt.links.new(pos, nz.inputs["Vector"])
        v = _math(nt, "SUBTRACT", nz.outputs["Fac"], 0.5, -1000, 0)
        v = _math(nt, "MULTIPLY", v, mottle * 2.0, -850, 0)
        v = _math(nt, "ADD", v, 1.0, -700, 0)
        mul = nt.nodes.new("ShaderNodeVectorMath")
        mul.operation = "SCALE"
        mul.location = (-500, 250)
        nt.links.new(base, mul.inputs[0])
        nt.links.new(v, mul.inputs["Scale"])
        base = mul.outputs[0]

    # Worn edges: the bevelled normal parts from the face's own normal on convex edges; noise breaks it up.
    col = base
    if e_amt > 0:
        bev = _n(nt, "ShaderNodeBevel", -1400, -300)
        bev.samples = 8
        bev.inputs["Radius"].default_value = edge_r * max(scale, 0.3)
        dot = nt.nodes.new("ShaderNodeVectorMath")
        dot.operation = "DOT_PRODUCT"
        dot.location = (-1200, -300)
        nt.links.new(bev.outputs["Normal"], dot.inputs[0])
        nt.links.new(geo.outputs["Normal"], dot.inputs[1])
        edge = _math(nt, "SUBTRACT", 1.0, dot.outputs["Value"], -1000, -300)
        edge = _math(nt, "MULTIPLY", edge, 9.0, -850, -300, clamp=True)
        # convex only: an AO with "inside" off is bright on convex edges; use the plain AO to keep crevices out
        ao_e = _n(nt, "ShaderNodeAmbientOcclusion", -1200, -450)
        ao_e.samples = 8
        ao_e.only_local = True
        ao_e.inputs["Distance"].default_value = 0.01 * max(scale, 0.3)
        edge = _math(nt, "MULTIPLY", edge, ao_e.outputs["AO"], -700, -300)
        nz2 = _n(nt, "ShaderNodeTexNoise", -1000, -550, Scale=38.0 / scale, Detail=6.0, Roughness=0.7)
        nt.links.new(pos, nz2.inputs["Vector"])
        brk = _math(nt, "SUBTRACT", nz2.outputs["Fac"], 0.42, -850, -550)
        brk = _math(nt, "MULTIPLY", brk, 6.0, -700, -550, clamp=True)
        edge = _math(nt, "MULTIPLY", edge, brk, -550, -400)
        edge = _math(nt, "MULTIPLY", edge, e_amt, -400, -400, clamp=True)
        if e_col is None:
            light = nt.nodes.new("ShaderNodeVectorMath")
            light.operation = "MULTIPLY_ADD"
            light.location = (-400, 0)
            nt.links.new(base, light.inputs[0])
            light.inputs[1].default_value = (1.35, 1.33, 1.3)
            light.inputs[2].default_value = (0.05, 0.05, 0.05)
            ecol = light.outputs[0]
        else:
            ecol = e_col
        col = _mix(nt, edge, col, ecol, -250, 100)

    # Dirt in the creases: AO over a few centimetres (scaled with the model), browner and darker.
    if d_amt > 0:
        ao = _n(nt, "ShaderNodeAmbientOcclusion", -1000, -800)
        ao.samples = 16
        ao.only_local = True
        ao.inputs["Distance"].default_value = ao_dist * max(scale, 0.25)
        occ = _math(nt, "SUBTRACT", 1.0, ao.outputs["AO"], -800, -800)
        occ = _math(nt, "POWER", occ, 0.8, -650, -800, clamp=True)
        nz3 = _n(nt, "ShaderNodeTexNoise", -800, -950, Scale=14.0 / scale, Detail=3.0, Roughness=0.6)
        nt.links.new(pos, nz3.inputs["Vector"])
        var = _math(nt, "MULTIPLY_ADD", nz3.outputs["Fac"], 0.9, -650, -950, c=0.55)
        occ = _math(nt, "MULTIPLY", occ, var, -500, -850)
        occ = _math(nt, "MULTIPLY", occ, d_amt * 1.4, -350, -850, clamp=True)
        col = _mix(nt, occ, col, DIRT, -100, 0)

    # Grime: a broad darkening, streaks down vertical faces, dust on faces looking up.
    if grime > 0:
        nz4 = _n(nt, "ShaderNodeTexNoise", -800, -1200, Scale=2.2 / scale, Detail=3.0, Roughness=0.5)
        nt.links.new(pos, nz4.inputs["Vector"])
        g = _math(nt, "SUBTRACT", nz4.outputs["Fac"], 0.45, -650, -1200)
        g = _math(nt, "MULTIPLY", g, grime * 0.6, -500, -1200, clamp=True)
        col = _mix(nt, g, col, DIRT, 50, -100)
    if streak > 0:
        sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        sep.location = (-1400, -1400)
        nt.links.new(geo.outputs["Normal"], sep.inputs[0])
        vert = _math(nt, "ABSOLUTE", sep.outputs["Z"], None, -1250, -1400)
        vert = _math(nt, "SUBTRACT", 1.0, vert, -1100, -1400, clamp=True)
        vert = _math(nt, "POWER", vert, 3.0, -950, -1400)
        mapn = nt.nodes.new("ShaderNodeMapping")
        mapn.location = (-1250, -1600)
        mapn.inputs["Scale"].default_value = (22.0 / scale, 22.0 / scale, 1.5 / scale)
        nt.links.new(pos, mapn.inputs["Vector"])
        nz5 = _n(nt, "ShaderNodeTexNoise", -1050, -1600, Scale=1.0, Detail=2.0, Roughness=0.5)
        nt.links.new(mapn.outputs["Vector"], nz5.inputs["Vector"])
        s = _math(nt, "SUBTRACT", nz5.outputs["Fac"], 0.58, -900, -1600)
        s = _math(nt, "MULTIPLY", s, 4.0, -750, -1600, clamp=True)
        s = _math(nt, "MULTIPLY", s, vert, -600, -1500)
        s = _math(nt, "MULTIPLY", s, streak * 0.22, -450, -1500, clamp=True)
        col = _mix(nt, s, col, DIRT, 200, -200)
    if dust > 0:
        sep2 = nt.nodes.new("ShaderNodeSeparateXYZ")
        sep2.location = (-1400, -1800)
        nt.links.new(geo.outputs["Normal"], sep2.inputs[0])
        up = _math(nt, "SUBTRACT", sep2.outputs["Z"], 0.6, -1250, -1800)
        up = _math(nt, "MULTIPLY", up, 2.5, -1100, -1800, clamp=True)
        nz6 = _n(nt, "ShaderNodeTexNoise", -1100, -1950, Scale=9.0 / scale, Detail=5.0, Roughness=0.6)
        nt.links.new(pos, nz6.inputs["Vector"])
        dn = _math(nt, "MULTIPLY_ADD", nz6.outputs["Fac"], 0.8, -950, -1950, clamp=True, c=0.3)
        up = _math(nt, "MULTIPLY", up, dn, -800, -1850)
        up = _math(nt, "MULTIPLY", up, dust * 0.35, -650, -1850, clamp=True)
        col = _mix(nt, up, col, DUST, 350, -300)

    em = _n(nt, "ShaderNodeEmission", 500, 0)
    nt.links.new(col, em.inputs["Color"])
    em.inputs["Strength"].default_value = 1.0
    # The bake's target image, active (Cycles bakes into the active image node of each material).
    img = _n(nt, "ShaderNodeTexImage", 500, 300)
    img.image = image
    uvb = _n(nt, "ShaderNodeUVMap", 300, 300)
    uvb.uv_map = "Bake"
    nt.links.new(uvb.outputs["UV"], img.inputs["Vector"])
    nt.nodes.active = img
    keep = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].links else None
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    return keep, em


def bake(ob, name, out_dir, res=None, scale=1.0, wear=1.0, dirt=1.0, px_per_m=420.0, samples=48, seed=1, glow=None):
    """Bakes the aged colour of a finished object into <out_dir>/<name>_Albedo.png and makes "Bake" its first UV map
    (the only one exported). res: the image size (None: from the surface area at px_per_m, 256..2048).
    glow: for a lit face, glow(material, node_tree, colour_socket) gives the colour it shines with (or None: dark),
    baked over the same layout into <name>_Emission.png (a backlit sign brighter over its tubes, say)."""
    objs = _objs(ob)
    fixed = res is not None
    if res is None:
        area = sum(p.area for o in objs for p in o.data.polygons)
        side = math.sqrt(area / 0.62) * px_per_m           # (the packing fills about 62 %)
        res = 256
        while res < side and res < 2048:
            res *= 2
    bake_layout(ob, res)
    if not fixed:
        # As many pixels as the packing turned out to need for px_per_m (thin parts pack loosely; the margins, a few
        # pixels, take less of a bigger image, so settle it in a few steps).
        for _ in range(3):
            d = px_per_metre(ob)
            want = 256
            while want * d < px_per_m and want < 2048:
                want *= 2
            if want == res:
                break
            res = want
            bake_layout(ob, res)
    scene = bpy.context.scene
    use_gpu(scene)
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    try:
        scene.cycles.bake_type = "EMIT"
    except Exception:
        pass
    scene.render.bake.margin = 8
    scene.render.bake.use_clear = True
    img = bpy.data.images.new(name + "_Albedo", res, res, alpha=False)
    img.colorspace_settings.name = "sRGB"
    restore = []
    done = set()
    for o in objs:
        for i, m in enumerate(o.data.materials):
            if m is None or m.name in done:
                continue
            done.add(m.name)
            restore.append((m, paint_material(m, img, seed + len(restore) * 7, scale=scale, wear=wear, dirt=dirt)))
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    import time
    t0 = time.time()
    bpy.ops.object.bake(type="EMIT", margin=8, use_clear=True)
    print("bake_kit: %s %dpx on %s in %.1f s" % (name, res, scene.cycles.device, time.time() - t0))
    out_dir = os.path.abspath(out_dir)
    path = os.path.join(out_dir, name + "_Albedo.png")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    if glow is not None:
        gimg = bpy.data.images.new(name + "_Emission", res, res, alpha=False)
        gimg.colorspace_settings.name = "sRGB"
        for m, (keep, em) in restore:
            nt = m.node_tree
            tn = next(n for n in nt.nodes if n.type == "TEX_IMAGE" and n.image == img)
            tn.image = gimg
            nt.nodes.active = tn
            src = em.inputs["Color"].links[0].from_socket if em.inputs["Color"].links else None
            g = glow(m, nt, src)
            for l in list(em.inputs["Color"].links):
                nt.links.remove(l)
            if g is None:
                em.inputs["Color"].default_value = (0.0, 0.0, 0.0, 1.0)
            else:
                nt.links.new(g, em.inputs["Color"])
        bpy.ops.object.bake(type="EMIT", margin=8, use_clear=True)
        gimg.filepath_raw = os.path.join(out_dir, name + "_Emission.png")
        gimg.file_format = "PNG"
        gimg.save()
        print("bake_kit: %s emission baked" % name)
    # Back to the Principled output (what the FBX exports), and the bake UV first, alone.
    for m, (keep, em) in restore:
        out = next(n for n in m.node_tree.nodes if n.type == "OUTPUT_MATERIAL")
        if keep is not None:
            m.node_tree.links.new(keep, out.inputs["Surface"])
    for o in objs:
        me = o.data
        tile = me.uv_layers.get("Tile")
        if tile is not None:
            me.uv_layers.remove(tile)
        me.uv_layers.active = me.uv_layers["Bake"]
        me.uv_layers["Bake"].active_render = True
    d = px_per_metre(ob)
    # Its texel density beside it (the game tiles the fine grain with it when it cannot read the mesh).
    with open(os.path.join(out_dir, name + "_Bake.json"), "w", encoding="utf-8") as fh:
        fh.write('{"uv_per_m": %.4f, "res": %d}' % (d, res) + chr(10))
    return {"res": res, "uv_per_m": round(d, 3), "path": path}


# ---------------------------------------------------------------------------------------------------- printing

GLYPHS = {
    "A": "01110100011000111111100011000110001", "B": "11110100011000111110100011000111110",
    "C": "01110100011000010000100001000101110", "D": "11110100011000110001100011000111110",
    "E": "11111100001000011110100001000011111", "F": "11111100001000011110100001000010000",
    "G": "01110100011000010111100011000101111", "H": "10001100011000111111100011000110001",
    "I": "01110001000010000100001000010001110", "J": "00111000100001000010000101001001100",
    "K": "10001100101010011000101001001010001", "L": "10000100001000010000100001000011111",
    "M": "10001110111010110101100011000110001", "N": "10001100011100110101100111000110001",
    "O": "01110100011000110001100011000101110", "P": "11110100011000111110100001000010000",
    "Q": "01110100011000110001101011001001101", "R": "11110100011000111110101001001010001",
    "S": "01111100001000001110000010000111110", "T": "11111001000010000100001000010000100",
    "U": "10001100011000110001100011000101110", "V": "10001100011000110001100010101000100",
    "W": "10001100011000110101101011010101010", "X": "10001100010101000100010101000110001",
    "Y": "10001100010101000100001000010000100", "Z": "11111000010001000100010001000011111",
    "0": "01110100011001110101110011000101110", "1": "00100011000010000100001000010001110",
    "2": "01110100010000100010001000100011111", "3": "11111000100010000010000011000101110",
    "4": "00010001100101010010111110001000010", "5": "11111100001111000001000011000101110",
    "6": "00110010001000011110100011000101110", "7": "11111000010001000100010000100001000",
    "8": "01110100011000101110100011000101110", "9": "01110100011000101111000010001001100",
    "-": "00000000000000011111000000000000000", ".": "00000000000000000000000000110001100",
    "/": "00001000010001000100010001000010000", ":": "00000011000110000000011000110000000",
    "%": "11001110010001000100010001001110011", "+": "00000001000010011111001000010000000",
    "#": "01010010101111101010111110101001010", "!": "00100001000010000100001000000000100",
    "(": "00010001000100001000010000010000010", ")": "01000001000001000010000100010001000",
    "=": "00000000001111100000111110000000000", "'": "00100001000100000000000000000000000",
    "<": "00010001000100010000010000010000010", ">": "01000001000001000001000100010001000",
}


class Canvas:
    """A small RGBA image painted in Python (top row first), saved through Blender."""

    def __init__(self, w, h, ground):
        self.w, self.h = w, h
        self.px = [list(ground) + [1.0] for _ in range(w * h)]

    def rect(self, x0, y0, x1, y1, c):
        for y in range(max(0, int(y0)), min(self.h, int(y1))):
            for x in range(max(0, int(x0)), min(self.w, int(x1))):
                self.px[y * self.w + x] = list(c) + [1.0]

    def frame(self, x0, y0, x1, y1, t, c):
        self.rect(x0, y0, x1, y0 + t, c); self.rect(x0, y1 - t, x1, y1, c)
        self.rect(x0, y0, x0 + t, y1, c); self.rect(x1 - t, y0, x1, y1, c)

    def disc(self, cx, cy, r, c):
        for y in range(max(0, int(cy - r)), min(self.h, int(cy + r + 1))):
            for x in range(max(0, int(cx - r)), min(self.w, int(cx + r + 1))):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r:
                    self.px[y * self.w + x] = list(c) + [1.0]

    def text(self, s, x, y, size, c, spacing=1.0):
        """Block capitals: size is a dot's side in pixels; (x, y) the top left of the first letter."""
        cx = x
        for ch in s.upper():
            g = GLYPHS.get(ch)
            if g:
                for row in range(7):
                    for col in range(5):
                        if g[row * 5 + col] == "1":
                            self.rect(cx + col * size, y + row * size, cx + (col + 1) * size, y + (row + 1) * size, c)
            cx += 6 * size * spacing
        return cx

    @staticmethod
    def width(s, size, spacing=1.0):
        return len(s) * 6 * size * spacing - size

    def text_c(self, s, cx, y, size, c, spacing=1.0):
        return self.text(s, cx - self.width(s, size, spacing) / 2, y, size, c, spacing)

    def lines(self, x0, y0, x1, n, pitch, c, seed=1):
        """Small print: rows of grey dashes of varying length."""
        import random
        r = random.Random(seed)
        for i in range(n):
            y = y0 + i * pitch
            end = x0 + (x1 - x0) * (0.55 + 0.45 * r.random())
            x = x0
            while x < end:
                wlen = 3 + r.random() * 9
                self.rect(x, y, min(x + wlen, end), y + max(1, pitch * 0.45), c)
                x += wlen + 2

    def image(self, name):
        img = bpy.data.images.get(name) or bpy.data.images.new(name, self.w, self.h, alpha=False)
        if img.size[0] != self.w or img.size[1] != self.h:
            img.scale(self.w, self.h)
        flat = []
        for y in range(self.h - 1, -1, -1):          # Blender's rows start at the bottom
            row = self.px[y * self.w:(y + 1) * self.w]
            for p in row:
                flat.extend(p)
        img.pixels = flat
        return img.name


def card(name, w, h, center, material, normal=(0, -1, 0), roll=0.0):
    """A flat print (one quad) facing normal, UVs 0..1 over it (u along its width as seen from the front, v up)."""
    import bmesh
    from mathutils import Matrix
    n = Vector(normal).normalized()
    up = Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((0, 1, 0))
    right = up.cross(n).normalized() if abs(n.z) < 0.9 else Vector((1, 0, 0))
    if abs(n.z) >= 0.9 and n.z < 0:
        right = -right
    up = n.cross(right).normalized()
    if roll:
        R = Matrix.Rotation(math.radians(roll), 3, n)
        right, up = R @ right, R @ up
    c = Vector(center)
    pts = [c + right * (-w / 2) * s1 + up * (h / 2) * s2 for s1, s2 in ((1, -1), (-1, -1), (-1, 1), (1, 1))]
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    f = bm.faces.new(vs)
    f.normal_update()
    if f.normal.dot(n) < 0:
        f.normal_flip()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    uv = me.uv_layers.new(name="UVMap").data
    for p in me.polygons:
        p.use_smooth = False
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co - c
            uv[li].uv = (v.dot(right) / w + 0.5, v.dot(up) / h + 0.5)
    return ob


def print_mat(name, canvas, rough=0.45, sharp=False):
    """A material whose colour is a painted print (read through the Tile UVs, 0..1 over the print)."""
    m = mat(name, (1, 1, 1), rough)
    m["linear"] = 1
    m["print"] = canvas.image(name + "_Print")
    if sharp:
        m["print_sharp"] = 1
    return m


def preview_lit(obj, path, albedo=None, view=(0.75, -1.0, 0.45), res=900, margin=1.1):
    """A lit Cycles picture of a baked model with its texture (after export: it rewires the materials to show it)."""
    obs = [o for o in (obj if isinstance(obj, (list, tuple)) else [obj]) if o.type == "MESH"]
    scene = bpy.context.scene
    use_gpu(scene)
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    for o in obs:
        img = bpy.data.images.get(o.name + "_Albedo") if albedo is None else albedo
        for m in o.data.materials:
            if m is None or img is None:
                continue
            nt = m.node_tree
            bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
            out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
            t = nt.nodes.new("ShaderNodeTexImage")
            t.image = img
            uvn = nt.nodes.new("ShaderNodeUVMap")
            uvn.uv_map = "Bake"
            nt.links.new(uvn.outputs["UV"], t.inputs["Vector"])
            nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
            nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    bg = next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.2, 0.2, 0.21, 1)
    bg.inputs["Strength"].default_value = 0.6
    sun = bpy.data.objects.get("PreviewSun")
    if sun is None:
        sun = bpy.data.objects.new("PreviewSun", bpy.data.lights.new("PreviewSun", "SUN"))
        bpy.context.collection.objects.link(sun)
    sun.data.energy = 3.0
    sun.data.angle = 0.2
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(30))
    scene.view_settings.view_transform = "Standard"
    preview(obj, path, view=view, res=res, margin=margin, keep_engine=True)
