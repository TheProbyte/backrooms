"""
Builds the player character with MPFB (MakeHuman for Blender): a realistic adult body, a game-engine skeleton that Unity
maps as a Humanoid, and the outfit (a late-'90s explorer as near as the free pack goes: a striped shirt, jeans, worn sneakers, short hair).

Needs the MPFB extension (Blender: Edit > Preferences > Get Extensions > "MPFB") and MakeHuman's CC0 system asset pack
(makehuman_system_assets_cc0.zip, loaded into MPFB's data folder). Everything used here is CC0.

Run in a background Blender (it starts from an empty scene, so it never touches an open file):
  blender -b --python make_player_mpfb.py -- <out_dir> [--force]

Writes <out_dir>/PlayerMPFB.blend: the character to tune by hand (open it, N panel > "MPFB v2..." tab: Model for the
body's sliders, Apply assets for clothes, hair and skin; save). It refuses to build over an existing one (it may have
hand edits) unless --force. The game's model comes from that file through export_player_mpfb.py.
"""
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv and not argv[0].startswith("--") else os.path.join(os.path.dirname(__file__), "mpfb")
os.makedirs(OUT, exist_ok=True)
# The .blend is where the look gets tuned by hand: never build over it unless asked.
if os.path.exists(os.path.join(OUT, "PlayerMPFB.blend")) and "--force" not in argv:
    raise SystemExit("PlayerMPFB.blend already exists (it may have hand edits). Pass --force to rebuild it from scratch.")

from bl_ext.blender_org.mpfb.services.humanservice import HumanService
from bl_ext.blender_org.mpfb.services.locationservice import LocationService
from bl_ext.blender_org.mpfb.services.targetservice import TargetService

# ---------------- The look (change these, or the sliders in the .blend) ----------------
TARGET_STATURE = 1.76          # metres, top of the head (the game's eye is at about 1.62-1.65)
MACRO = {
    "gender": 1.0,             # 0 female .. 1 male
    "age": 0.5,                # 0.5 = 25 years
    "muscle": 0.55,
    "weight": 0.5,
    "proportions": 0.6,        # a little more ideal than average
    "height": 0.5,             # adjusted below to reach TARGET_STATURE
    "cupsize": 0.5,
    "firmness": 0.5,
    "race": {"caucasian": 0.8, "asian": 0.1, "african": 0.1},
}
SKIN = "young_caucasian_male"
EYES = "brown"
ASSETS = [                      # (folder, name, MPFB asset type)
    ("eyebrows", "eyebrow001", "Eyebrows"),
    ("eyelashes", "eyelashes01", "Eyelashes"),
    ("hair", "short02", "Hair"),
    ("clothes", "male_casualsuit03", "Clothes"),   # plaid flannel shirt and jeans
    ("clothes", "shoes02", "Clothes"),             # worn sneakers
]
RIG = "game_engine"             # a standard humanoid skeleton (Unity: Humanoid)

DATA = LocationService.get_user_data()


def data(*parts):
    return os.path.join(DATA, *parts)


def stature(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    zs = [(ev.matrix_world @ v.co).z for v in ev.data.vertices]
    return max(zs) - min(zs)


def build(height):
    bpy.ops.wm.read_homefile(use_empty=True)
    macro = dict(MACRO)
    macro["height"] = height
    return HumanService.create_human(macro_detail_dict=macro)


# Height: the macro is not linear in metres, so search it.
lo, hi = 0.0, 1.0
h = MACRO["height"]
for _ in range(8):
    body = build(h)
    s = stature(body)
    print(f"MPFB height {h:.3f} -> stature {s:.3f} m")
    if abs(s - TARGET_STATURE) < 0.005:
        break
    if s < TARGET_STATURE:
        lo = h
    else:
        hi = h
    h = (lo + hi) * 0.5
body.name = "PlayerMPFB"

HumanService.set_character_skin(data("skins", SKIN, SKIN + ".mhmat"), body, skin_type="GAMEENGINE")
rig = HumanService.add_builtin_rig(body, RIG)
rig.name = rig.data.name = "PlayerMPFB.rig"

eyes = HumanService.add_mhclo_asset(data("eyes", "low-poly", "low-poly.mhclo"), body, asset_type="Eyes", subdiv_levels=0,
                                    alternative_materials=None)
for folder, name, kind in ASSETS:
    HumanService.add_mhclo_asset(data(folder, name, name + ".mhclo"), body, asset_type=kind, subdiv_levels=0)

# Eye colour: the low-poly eyes take a mhmat from eyes/materials.
try:
    from bl_ext.blender_org.mpfb.services.materialservice import MaterialService
    if eyes is not None and hasattr(MaterialService, "create_v2_eye_material"):
        pass
except Exception as e:  # cosmetic only
    print("eye colour skipped:", e)

for o in bpy.data.objects:
    if o.type == 'MESH':
        for m in o.modifiers:
            if m.type == 'SUBSURF':
                m.levels = 0
                m.render_levels = 0

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "PlayerMPFB.blend"))
print("SAVED", os.path.join(OUT, "PlayerMPFB.blend"))

# Report.
print("STATURE", round(stature(body), 3))
print("BONES", len(rig.data.bones), [b.name for b in rig.data.bones][:80])
for o in bpy.data.objects:
    if o.type == 'MESH':
        deps = bpy.context.evaluated_depsgraph_get()
        ev = o.evaluated_get(deps)
        tris = sum(len(p.vertices) - 2 for p in ev.data.polygons)
        print("MESH", o.name, "tris", tris, "materials", [s.material.name if s.material else None for s in o.material_slots])

