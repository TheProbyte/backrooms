"""Renders a quick preview of each style lab model (workbench, flat studio light) to Tools/Blender/StyleLab/*.png."""
import bpy, os, math
from mathutils import Vector

D = os.path.abspath(os.path.join(os.path.dirname(__file__), 'StyleLab'))
VIEWS = {'Hazmat': ((1.6, -3.2, 1.4), (0, 0, 0.9), 35), 'PlatformCart': ((1.4, -1.6, 1.1), (0, 0, 0.35), 35),
         'Camcorder': ((0.45, -0.55, 0.38), (0, -0.04, 0.09), 35), 'StyleLab_Room': ((-3.6, -3.6, 1.7), (1.0, 2.5, 1.2), 20)}
for name, (cam, tgt, lens) in VIEWS.items():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(D, name + '.blend'))
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_shadows = True
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 800, 800
    c = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
    sc.collection.objects.link(c)
    c.location = cam
    c.data.lens = lens
    d = Vector(tgt) - Vector(cam)
    c.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc.camera = c
    sc.render.filepath = os.path.join(D, name + '.png')
    bpy.ops.render.render(write_still=True)
    print('rendered', name)
