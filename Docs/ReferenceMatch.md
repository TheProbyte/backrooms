# Reference Match scene

A playable scene built to match one reference photograph (an empty office room with a large opening into a second room).
It also tunes the lighting, materials and camcorder against that photograph. The procedural world, its scenes and the
existing camcorder presets are unchanged.

## How to use it

| | |
|---|---|
| Scene | `Assets/Backrooms/Scenes/ReferenceMatch.unity` |
| Rebuild | **Backrooms → Build Reference Match Scene** (tuned) or **…(baseline: game materials)** |
| Start pose | Play Mode starts at the **Reference Camera Pose**: position (0.4, 0, 0), eye 1.58 m, yaw −5°, pitch 25° down. These are fields on the `ReferenceMatch` object. |
| F8 | Return to the pose exactly (zero velocity) |
| F9 | Freeze for stills: no handheld motion, exposure and focus locked, time-varying tape effects stopped |
| Capture | `ReferenceMatch.Capture(path)` renders the real game camera at a fixed 1514 × 1240 (the photo's size and aspect) and saves a PNG |
| Preset | **Reference Match** (index 7) in F5 → presets. The scene applies it in memory on start, so the player's saved settings are not overwritten. |

Movement, running, collision, cursor and the F5 menu are the normal game ones; nothing in gameplay or audio was changed.

## Geometry (`ReferenceRoom`)

The geometry is real boxes with real colliders, built when the scene starts. It is not a background, projection or
overlay. Every dimension is a field on the component. The layout was measured from the photo by projecting pixel
positions through the estimated camera (eye height 1.58 m, about 75° vertical field of view, 25° down).

| Part | What was built |
|---|---|
| Opening | 3.3 m wide and 2.35 m high in a 0.5 m thick wall, left of centre. Thick olive header with a visible soffit and returns on both jambs. |
| Left | A pale wall about 30° to the view, joined to the opening by a short return. The two pieces overlap at the corner, so no seam shows. |
| Right | A pale wall with a projecting vertical return (0.5 m) and a shallow 0.35 m header recess along its top. |
| Back room | A back wall at 12.9 m with a wide 0.3 m recess, a dark band near its top and two supports. |
| Ceiling and floor | A suspended ceiling of square 0.6 m tiles; each light is a square panel one tile in size, centred on a tile. One continuous carpet slab through both rooms. |
| Trim | None (`baseboard` is off; the baseline build turns it on). |
| Props | None. |

## Lighting

The room has 15 ceiling panel spot lights with soft shadows, plus 10 unshadowed upward "bounce" spots at floor level.
The panels follow the photo's falloff: dim on the left of the front room, bright on the right. The back room is lit
evenly and slightly brighter, so it reads near white through the opening. Ambient light is a flat, low olive colour; it
was not used to brighten the scene. The broad, hotspot-free look comes from wide inner cones (90–95 % of the outer
cone) and from keeping the panel rows clear of the walls.

**Why bounce stand-ins and not baked GI:** the game's rooms are generated at runtime and streamed, so lightmaps cannot
be baked for them. A result tuned here with baked GI would not carry over to the game. The bounce stand-ins approximate
light reflected off the carpet and walls in a way that also works for procedural rooms: the front-room ones are tinted
like the wallpaper, the back-room ones are whiter. Real-time GI (APV or SSGI) is the long-term option.

## Materials (`Assets/Backrooms/Materials/ReferenceMatch`)

| Material | Description |
|---|---|
| `M_Ref_Wall` | A copy of the game's main wallpaper material (`M_Wallpaper`: its albedo, normal and detail-normal maps), mapped at the game's own scale and tinted pale cream. The tint is slightly above 1 because the wallpaper texture itself is darker and yellower. |
| `M_Ref_WallOlive` | The same wallpaper tinted olive, for the header |
| `M_Ref_Carpet` | The supplied carpet photo (`Textures/Source/RefCarpet_Source.png`), repeating every 0.5 m under a broad 3 m mottling, with a normal map made from its pile (`RefCarpet_Albedo` / `RefCarpet_Normal`). The textures are colour-neutral and the material tints them tan. |
| `M_Ref_Ceiling` | Square 0.6 m mineral-fibre tiles with a soft recessed grid and fissures (`RefCeiling_Albedo` / `RefCeiling_Normal`, 4 × 4 tiles per repeat), tinted green-olive |
| `M_Ref_Recess` | Dark band |

All of these have low smoothness and no environment reflections. The carpet and ceiling textures come from
`BackroomsTextureGenerator.GenerateReferenceMatch()`.

## Camera and camcorder (preset "Reference Match")

| Setting | Value and reason |
|---|---|
| Field of view | 75° |
| Lens distortion | −0.08 |
| Recording resolution | 536 lines, line-doubled. The picture is box-averaged down to a virtual 536-line recording. Brightness is shown with each line repeated rather than blended (*Line doubling* 1); colour is reconstructed smoothly. Softness 0.8 widens the averaging, because the footage resolves much less than its line count. |
| Colour detail | 0.28. Colour is recorded at lower resolution than brightness, more so horizontally. |
| Recording frame rate | 24 fps. The picture changes 24 times a second (a frame hold after the camcorder pass); the game itself still runs and takes input at full rate. |
| Sharpening | 0.7, cored and clamped |
| Exposure bias | 0.25, max gain 0.6. Exposed for the walls and carpet; the panels clip slightly, as in the photo. |
| Noise | Very low (the footage has almost none). The noise grid follows the recording resolution, re-seeds once per recorded frame and grows with gain. |
| Focus | Autofocus off, so depth of field is negligible |
| Motion | Sensor blur 0.5, motion blur 0.15, and *Handheld roughness* 1.3: fast, irregular hand shake and a jolt on each step on top of the smooth bob |
| Compression | Blockiness 0.15. With a recording resolution the blocks are the codec's 8 × 8 blocks of recorded pixels; low-contrast detail inside a block is flattened toward its mean, which leaves faint block borders. |
| Bottom edge | *Bottom-edge line* 1: a steady lilac line over the last few lines, from about 46 % across, with a pink spot where it starts. The old tearing head-switching band is off. |

New controls in **F5**:
- *Recording resolution (lines)*, *Colour detail* (IMAGE PROCESSING)
- *Recording frame rate*, *Line doubling*, *Bottom-edge line* (with the format effects)
- *Handheld roughness* (with the motion settings)

All of them are saved with your settings like the other sliders, and all default to off, so every other preset looks
and moves as before.

## What the episode footage shows (`2026-10-23 13-29-53.mp4`)

The 60 fps screen recording was split into frames and measured; the same measurements were run on game captures.

| Question | Finding | What the preset does |
|---|---|---|
| Frame rate | New pictures arrive on alternating 2 and 3 display frames: **24 fps** source | 24 fps recording frame rate |
| Colour changing frame to frame? | No. Frame-to-frame changes in mean colour are about 0.3 Lab units, below what can be seen. Colour only drifts slowly with what is in view. | No per-frame colour effect added |
| Camera motion | Jerky, not smooth. Walking without panning: about 1.8 px frame-to-frame jitter (on a 1071-line frame) both ways, 2.9 px of vertical movement per frame, 4 bounces a second | Handheld roughness 1.3. The game now measures 2.0 / 2.0 px, 2.7 px and 3.6 bounces a second. |
| Resolution | 536 picture lines, each shown twice; detail is far below that, and colour detail is lower still | 536 lines, line doubling, softness 0.8, colour detail 0.28. The capture's brightness and colour detail now measure within about 10 % of the footage's. |
| Compression | Faint 8 × 8 block edges at the source resolution, mostly visible on smooth areas | Blockiness 0.15 on the recording grid |
| Noise | Almost none (about 0.3–1 grey level) | Noise 0.004 |
| Bottom of frame | A thin, steady lilac line on the right part of the last few lines, no tearing | Bottom-edge line |
| Motion blur | Mild; strong only on fast pans | Unchanged (sensor blur follows how fast the camera turns) |
| Picture shape | 1.23 : 1 (the same as the reference still) | Not changed. The comparison captures use this shape; gameplay keeps the screen's shape. |

## Captures (`Docs/screenshots/refmatch`)

| File | Contents |
|---|---|
| `00_baseline.png` | Same geometry with game materials and the default preset, before tuning |
| `01`–`15` | Intermediate iterations |
| `16_final.png` | Final capture from a fresh Play Mode start |
| `16_repeat.png` | The same capture after walking around and returning with F8; pixel-identical to `16_final.png` |
| `17_walking.png`, `18_walking_pan.png` | Normal (unfrozen) frames while moving (first pass) |
| `20`–`22` | The new wallpaper, carpet and ceiling while tuning |
| `30_final.png`, `31_final.png` | After matching the episode footage |
| `compare_motion_episode_vs_game.png` | Four consecutive recorded frames from the footage and from the game while walking |

`ReferenceMatch.CaptureSequence(folder, seconds)` saves every recorded frame (at 960 × 540) for comparisons of motion.

The reference photo itself is kept out of the repository. The side-by-side sheets were made locally.

## Remaining differences

- The opening's corners are square; the photo's are rounded. The jamb faces read a little narrower.
- The game's wallpaper pattern is more visible than the photo's faint stripes (it is the game's own wallpaper, by request).
- The footage's lens bends straight lines more strongly at the edges than the preset's −0.08 distortion.
- The olive colour at the top of the photo's left wall is not reproduced; a band split there produced a hard line.
- The carpet on the right is slightly darker than the photo's. The photo's carpet also has darker, uneven patches near
  the bottom left.
- Brightness falls off more abruptly at the back room's side walls than in the photo, a limit of the bounce stand-ins.

## Performance

These figures were measured in the editor at 1920 × 1080:

- About 7.7 ms per frame (130 fps).
- 25 lights, of which 15 are soft-shadowed spots.
- The new recording pass adds a few texture taps per pixel inside the existing full-screen camcorder pass. There is no
  extra pass.
