"""
Exports the MPFB player (Tools/Blender/mpfb/PlayerMPFB.blend, as last saved: hand edits included) for the game.

  blender -b Tools/Blender/mpfb/PlayerMPFB.blend --python Tools/Blender/export_player_mpfb.py -- <out_dir>

It works on a copy (the .blend is never saved): the masks MPFB uses (helper geometry, the skin under the clothes) and
the slider targets are baked in, and every mesh is split into parts by which bones move it, so the game can draw some
parts into the shadows only:
  <mesh>.Head   the head and neck (hair, eyes, brows and lashes whole): the camera is inside it
  <mesh>.ArmL   the left arm from the shoulder (the hand that holds the camcorder to the eye, for a right-hander)
  <mesh>.ArmR   the right arm
  <mesh>        the rest
Writes <out_dir>/PlayerMPFB.fbx and its textures (and the clothes' normal maps) into <out_dir>/Textures.
"""
import os
import shutil
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.join(os.path.dirname(bpy.data.filepath), "export")
TEX = os.path.join(OUT, "Textures")
os.makedirs(TEX, exist_ok=True)

from bl_ext.blender_org.mpfb.services.exportservice import ExportService
from bl_ext.blender_org.mpfb.services.objectservice import ObjectService

body = next(o for o in bpy.data.objects if o.type == 'MESH' and ObjectService.get_object_type(o) == "Basemesh")
rig = body.parent
assert rig and rig.type == 'ARMATURE', "the human has no rig"

# ---------------- A copy to bake ----------------
copy = ExportService.create_character_copy(body, name_suffix="_x")
copy_body = next(c for c in copy.children if c.type == 'MESH' and ObjectService.get_object_type(c) == "Basemesh")
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active = copy_body
copy_body.select_set(True)
ExportService.bake_modifiers_remove_helpers(copy_body, bake_masks=True, bake_subdiv=False, remove_helpers=True)
for c in copy.children:
    if c.type == 'MESH' and c.data.shape_keys:
        bpy.context.view_layer.objects.active = c
        bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
    if c.type == 'MESH':
        for m in list(c.modifiers):
            if m.type in ('SUBSURF', 'MASK'):
                c.modifiers.remove(m)

keep = set([copy] + list(copy.children))
for o in list(bpy.data.objects):
    if o not in keep:
        o.name = o.name + "_src"
for o in keep:
    if o.name.endswith("_x"):
        o.name = o.name[:-2]
copy.data.name = "PlayerMPFB.rig"

# Drop vertex groups no bone uses (MPFB's helper and delete groups) so the FBX carries only skin weights
# (and the shares below are of bone weight only).
bone_names = {b.name for b in copy.data.bones}
for c in copy.children:
    if c.type != 'MESH':
        continue
    for g in list(c.vertex_groups):
        if g.name not in bone_names:
            c.vertex_groups.remove(g)

# ---------------- Split by the bones that move each face ----------------
HEAD = {"head", "neck_01"}


def arm_bones(s):
    names = {f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"}
    for f in ("index", "middle", "ring", "pinky", "thumb"):
        names |= {f"{f}_0{i}_{s}" for i in (1, 2, 3)}
    return names


SETS = [("Head", HEAD), ("ArmL", arm_bones("l")), ("ArmR", arm_bones("r"))]


def split(obj):
    me = obj.data
    gi = {g.index: g.name for g in obj.vertex_groups}
    share = {}
    for v in me.vertices:
        tot = sum(g.weight for g in v.groups) or 1.0
        share[v.index] = {k: sum(g.weight for g in v.groups if gi.get(g.group) in bones) / tot for k, bones in SETS}
    out = []
    for key, _ in SETS:
        faces = [p.index for p in me.polygons
                 if sum(share[i][key] for i in p.vertices) / len(p.vertices) > 0.5]
        if not faces:
            continue
        if len(faces) == len(me.polygons):
            obj.name = obj.name + "." + key
            return out
        bpy.ops.object.mode_set(mode='OBJECT')
        for p in me.polygons:
            p.select = False
        for e in me.edges:
            e.select = False
        for v in me.vertices:
            v.select = False
        for i in faces:
            me.polygons[i].select = True
        bpy.ops.object.select_all(action='DESELECT')
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_mode(type='FACE')
        bpy.ops.mesh.separate(type='SELECTED')
        bpy.ops.object.mode_set(mode='OBJECT')
        part = next(o for o in bpy.context.selected_objects if o is not obj)
        part.name = obj.name + "." + key
        out.append(part)
        me = obj.data
        # the share table is by vertex index of the original, which separate renumbers: rebuild it
        gi = {g.index: g.name for g in obj.vertex_groups}
        share = {}
        for v in me.vertices:
            tot = sum(g.weight for g in v.groups) or 1.0
            share[v.index] = {k: sum(g.weight for g in v.groups if gi.get(g.group) in bones) / tot for k, bones in SETS}
    return out


for c in [c for c in copy.children if c.type == 'MESH']:
    split(c)


# ---------------- Textures ----------------
def tex_paths():
    found = {}
    for m in bpy.data.materials:
        if not m.node_tree:
            continue
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image and n.image.filepath:
                p = bpy.path.abspath(n.image.filepath)
                if os.path.isfile(p):
                    found[os.path.basename(p)] = p
    return found


tex = tex_paths()
# The clothes' and shoes' normal maps sit beside their colour maps in MPFB's data (the game skin type does not use them).
for name, p in list(tex.items()):
    if name.endswith("_diffuse.png"):
        nm = p.replace("_diffuse.png", "_normal.png")
        if os.path.isfile(nm):
            tex[os.path.basename(nm)] = nm
for name, p in tex.items():
    shutil.copy2(p, os.path.join(TEX, name))

# ---------------- Export ----------------
bpy.ops.object.select_all(action='DESELECT')
copy.select_set(True)
for c in copy.children:
    c.select_set(True)
bpy.context.view_layer.objects.active = copy
fbx = os.path.join(OUT, "PlayerMPFB.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=False,
                         add_leaf_bones=False, mesh_smooth_type='FACE', use_armature_deform_only=True,
                         bake_anim=False, path_mode='STRIP', embed_textures=False)

for c in copy.children:
    if c.type == 'MESH':
        tris = sum(len(p.vertices) - 2 for p in c.data.polygons)
        print("PART", c.name, "tris", tris, "material", [s.material.name for s in c.material_slots if s.material])
print("TEXTURES", sorted(tex))
print("EXPORTED", fbx)
