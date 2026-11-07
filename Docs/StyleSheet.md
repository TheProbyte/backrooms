# Style sheet: the Overflow look (2026-11-06)

One page for making anything new or remaking anything old so it matches. The look was settled in the style lab
(`Docs/StyleLab.md`); the rollout plan is `Docs/StyleRollout.md`.

**In a sentence:** chunky, rounded, low-poly shapes in a few flat colours, painted with big soft patches and baked dirt,
lit by the game's real lights and fog, seen through the work camcorder.

References: Lethal Company, PEAK, RV There Yet?, The Big Walk. Not Minecraft or Roblox blocky, not realistic either.

## Colour

| Use | Colour |
|---|---|
| Company amber (the suit, Overflow's own things) | #F0A21E |
| Company stripe: orange, gold, avocado, on dark brown | #D9661F, #E8B23A, #7C8A2E on #3B2A1E |
| Camcorder body | chocolate #3B2A1E |
| Level 0 wallpaper | mustard #D6BE68, its stripes and chevrons a shade lighter |
| Level 0 carpet | dark mustard #A18545 |
| Ceiling tiles | warm off-white #D2CCB6 |
| Painted walls (warehouse) | cream #D2BE8C |
| Concrete slab | warm grey #98928A |
| Rack beams | safety orange #DB6614; uprights blue-grey |
| Cardboard | kraft #926E44 |

- The darker part of any patch is a little warmer and browner (hue shifts with value), never just greyer.
- Keep each surface's average brightness near the old one: the light grid takes its bounce colour from it.

## Shapes

- Bevel every edge you can see: about 1.5-3 cm on furniture and machines, 0.5-1 cm on small items.
- Round where a real thing would be moulded or worn; flat and square only where it is cut or stamped.
- Proportions a little chunkier than real (thicker handles, bigger knobs, deeper frames), never cartoon-squashed.
- Triangle budgets: small items 1-3k, furniture 3-6k, carts and machines 6-10k, a player 47k (the suit, all parts).
- Silhouettes first: a thing must read from 10 m in fog.
- "Chunkification" (Blender Studio's rule for Sprite Fright): some parts get bigger, small fussy ones go, no sharp
  corners, and the real thing stays recognisable. In the scripts' `--style overflow` that is:
  - plates, strips, slats and frames under 12 mm thick grow by up to their own thickness (at most 4 mm, 2 mm a face)
  - thin turned parts (knobs, pins, posts, thin handles: under 1.5 cm radius) a fifth fatter (not bigger ones:
    a can's body, shoulder and base ring are separate parts that must stay one size)
  - tubes 15 % thicker, wires and cords up to 5 mm
  - screw heads 1.4x, and every box bevelled
  - prints stood 2.5 mm off their faces (so the thicker plates do not cover them)

## Textures

- **Painted, not photographed.** Big soft shapes first (noise cut into 3-5 flat levels, edges softened), then a few
  bold small details, with plain areas left between.
- **Baked dirt where it gathers:** in grooves and joints, round knots and bolts, low on things near the floor. Worn
  edges lighter on painted metal and plastic.
- **Sizes:** 512 for small things, 1024 for big or tiling surfaces. Texel density about 340 px a metre for tiling
  surfaces (1024 over 3 m), more on things held in the hand.
- **Normal maps soft:** 0.2-0.6 strength. No parallax, no detail maps, no photo stamps.
- Made by scripts so they can be re-run: `Tools/Textures/make_overflow_textures.py` (surfaces), the Blender scripts'
  bakes (models, `Tools/Blender/style_lab_rig.py` for the suit's bake).

## Materials

- Backrooms/Lit, one material per finish. The colour is in the texture; the material's colour stays white or tints a
  shared texture (one plaster texture for walls and ceilings).
- Smoothness low on paint, paper and carpet (0.05-0.2), medium on plastic and painted metal (0.3-0.45). Metallic 0.1 at
  most on painted metal; real bare metal only where it is the point (a hand truck's axle).
- Overflow's materials live in `Materials/Overflow/OF_*.mat` and swap in through `Resources/Style/Overflow.asset`
  (`ArtStyle`); the old `M_*.mat` stay untouched until the new look is approved.

## Brands and print

- Overflow and invented makers only, never real ones. Print is simple: bold type, one or two colours, a pictogram.

## The picture

- The work camcorder's look stays as it is (approved over the lab). No outline or band pass on top.
- Fog and the real lights do the mood: don't paint shadows or light into textures beyond the dirt in the creases.
