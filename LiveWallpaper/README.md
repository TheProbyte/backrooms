# Pines at Dusk: 1970s live wallpaper

One photo, animated in a WebGL shader and finished as faded 16mm film (graded to match a Craft 1979 frame).

**What moves:** grass and bushes sway with passing gusts, treetops lean, the sun flickers through branches, light shafts and haze sit in the clearing, gnats hang in the sun gap, pollen drifts through, and the man breathes. **The film look:** 24 fps cadence, gate weave, exposure flicker, lifted print blacks, red halation round the sun, live grain, dust and the odd hair.

## Desktop (Windows)
- **Lively Wallpaper** (free, Microsoft Store): `+` > *Browse* > pick `wallpaper/index.html`.
- **Wallpaper Engine**: *Create Wallpaper* > open `wallpaper/index.html` (or drop the `wallpaper` folder in).
- Any browser works for a preview: open `wallpaper/index.html`.

Options go on the end of the file URL: `index.html?scale=0.75` renders lighter on weak GPUs, `?loop=90` slows the repeat of the wind pattern.
Grain and dust never repeat on desktop.

## Phone (Android)
`out/pines_at_dusk_phone_1080x2400.mp4`: 15 s seamless loop, 1080x2400.
- **Samsung**: Gallery > open the video > ⋮ > *Set as wallpaper* (lock screen video, 15 s max, which is why the loop is 15 s).
- **Other Android**: a video live wallpaper app, e.g. *Video Live Wallpaper*, set it to loop with sound off.

## Rebuilding
```
python build.py                         # masks, glow map, denoised photo -> wallpaper/assets.js
cd tools && npm install
node render.mjs --w 1080 --h 2400 --loop 15 --video ../out/pines_at_dusk_phone_1080x2400.mp4
node render.mjs --w 1920 --h 1080 --stills 0,120      # PNG stills into debug/
```
Needs Python (numpy, opencv-python, pillow, imageio-ffmpeg), Node, and Microsoft Edge.
Trunk and figure outlines live in `build.py` (pixel coords on the 1816x1014 source).
