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
| Ceiling and floor | A suspended ceiling of tiles with flush panel lenses. One continuous carpet slab through both rooms. |
| Trim | Narrow (7.5 cm) light trim close to the wall colour. |
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
| `M_Ref_Wall` | Pale cream, with a faint striped wallpaper albedo (`RefWall_Albedo`, 1.2 m repeat) |
| `M_Ref_WallOlive` | Olive, for the header |
| `M_Ref_Carpet` | Tan, mottled at a large scale (`RefCarpet_Albedo`, 3 m repeat), so the carpet shades smoothly rather than tiling |
| `M_Ref_Ceiling` | Subdued cream ceiling tile, so the white panels stand out |
| `M_Ref_Trim` | Light trim |
| `M_Ref_Recess` | Dark band |

All of these have low smoothness and no environment reflections. The textures come from
`BackroomsTextureGenerator.GenerateReferenceMatch()`.

## Camera and camcorder (preset "Reference Match")

| Setting | Value and reason |
|---|---|
| Field of view | 75° |
| Lens distortion | −0.08 |
| Recording resolution | 340 lines (new). The image is box-averaged down to a virtual 340-line picture and bilinearly scaled back up. This gives the soft but stable standard-definition look, without the old blocky or shimmering downsample. |
| Colour detail | 0.4 (new). Chroma is recorded at lower resolution than luma: more horizontally than vertically, and with a small chroma shift. |
| Sharpening | 0.7, cored and clamped |
| Exposure bias | 0.25, max gain 0.6. Exposed for the walls and carpet; the panels clip slightly, as in the photo. |
| Noise | Low fine noise. The noise grid follows the recording resolution, so it scales with the recording rather than with screen pixels. Noise grows with gain. |
| Focus | Autofocus off, so depth of field is negligible |
| Motion | Sensor blur 0.5 and motion blur 0.15 (tested while walking and panning, see captures 17 and 18) |
| Secondary artifacts | Head-switching band 0.25 and blockiness 0.1, kept light |

The two new controls are in **F5 → IMAGE PROCESSING**: *Recording resolution (lines)* and *Colour detail*.
They are saved with the user's settings like the other sliders. Setting lines to 0 keeps the old behaviour, so every
other preset looks as before.

## Captures (`Docs/screenshots/refmatch`)

| File | Contents |
|---|---|
| `00_baseline.png` | Same geometry with game materials and the default preset, before tuning |
| `01`–`15` | Intermediate iterations |
| `16_final.png` | Final capture from a fresh Play Mode start |
| `16_repeat.png` | The same capture after walking around and returning with F8; pixel-identical to `16_final.png` |
| `17_walking.png`, `18_walking_pan.png` | Normal (unfrozen) frames while moving |

The reference photo itself is kept out of the repository. The side-by-side sheets were made locally.

## Remaining differences

- The opening's corners are square; the photo's are rounded. The jamb faces read a little narrower.
- The photo is softer overall, with a stronger glow around bright areas. The capture still shows faint wallpaper
  stripes the photo doesn't.
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
