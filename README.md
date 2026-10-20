# Backrooms (working title)

Realistic camcorder-style Backrooms exploration game. Unity 6 (6000.4.5f1), URP.

![Infinite offices](Docs/screenshots/infinite_spawn.png)

## Play

Open `Assets/Backrooms/Scenes/Backrooms_Infinite.unity` and press Play. (The milestone-1 room is still in
`OfficeRoom_Prototype.unity`.)

| Input | Action |
| --- | --- |
| W A S D | Walk |
| Hold Left Shift | Run (forward) |
| Mouse | Look |
| F5 | Camcorder visual settings panel |
| Esc | Close the panel / release cursor |
| Left click | Re-capture cursor |

## Infinite offices (milestone 2)

The world is streamed in 14.4 m chunks (12 x 12 cells of 1.2 m) around the player.

- **Rules** (`OfficeLayoutGenerator`): each chunk is generated from `(seed, chunk coord)` only. Its borders are
  walls except for openings derived from the shared border's key, so neighbouring chunks always agree. Inside,
  rooms and hallways come from a BSP split, a spanning tree of doors (plus loops and occasional open-plan walls),
  and a flood-fill check that repairs any unreachable room. Nothing ever opens onto the void.
- **Variety**: big open offices with pillars and half walls, offices, 1- and 2-wide hallways, crawl slots too narrow to
  enter (0.42 m), lounges with mismatched sofas and floor lamps, conference rooms with tables and chairs, doors with and
  without headers, outlets and light switches, and lit, mixed or dead lighting per room.
- **No visible generation**: 7 x 7 chunks stay loaded. New chunks are only created on the outer ring, at least ~29 m
  away and inside the fog. Their layout and vertex data are built on worker threads; the main thread only uploads
  the meshes (~1-2 ms per chunk).
- **Efficiency**: each chunk is about 10k vertices in ~30 combined meshes (one per material and room light layer),
  with merged box colliders. Lights come from a pool (`FixtureLightManager`): close or visible fixtures get a
  shadowed spot plus a soft "bounce" point light that only affects its own room's rendering layer.
  The hum is pooled to 5 voices.

Rebuild the scene with **Backrooms > Build Infinite Offices Scene** (`InfiniteWorldBuilder.cs`). `WorldStreamer.seed`
= 0 gives a new world each run.

### Fixes (milestone 2.2)

- Where ceiling heights change, every opening out of the taller room becomes a headed doorway, so the plenum above
  a drop ceiling can never be seen. Posts only rise as high as the rooms around them.
- Rooms joined by open-plan walls share one bounce-light zone (no lighting seams across the carpet).
- Bigger, more open rooms; the starting chunk is always one big hall.
- F5 has VISUAL and GAMEPLAY tabs. Gameplay: noclip (Space up, Ctrl/C down, Shift faster), debug readout
  (position, chunk, seed, fps), mouse and movement settings.
- `Scripts/Runtime/Debug/NavProbe.cs`: automated collision test. Add it in play mode; it walks BFS paths across
  chunks (or, with `wallHug`, shoves into corners and jambs) and reports walk-through walls and invisible barriers.

### Realism pass (milestone 2.1)

- **Tall rooms**: big halls (and some large offices/lounges) rise to 3.9-5.4 m with plaster ceilings and round dome
  fittings. Height changes get soffits; posts and colliders always reach full height so no gap can open to the void.
  Walls are wallpaper or painted plaster per room.
- **Stable lighting**: every fixture within 14 m stays lit whether visible or not; up to 26 nearest lights cast
  shadows with hysteresis and fade their shadow strength in and out instead of popping.
- **Power flicker**: newly streamed areas strike on like fluorescent tubes, unloading areas stutter off (both happen
  far away, so it is rarely noticed). About 2.5% of tubes are faulty and stutter now and then.
- **Props** are modelled in Blender by `Tools/Blender/build_props.py` (headless, no MCP needed):
  `blender -b --factory-startup --python Tools/Blender/build_props.py -- Assets/Backrooms/Models`. Chesterfield and
  modern sofas, armchair, floor lamp, mesh office chair, conference table modules and a wall clock, 200-1800 triangles
  each, instanced as prefabs with shared meshes.

## Camcorder look (F5)

`CamcorderRig` applies `CamcorderSettings` (saved to PlayerPrefs) to the post-processing volume, auto-iris,
handheld motion and a full-screen video shader (`Shaders/CamcorderVideo.shader`, added to `PC_Renderer` after
post-processing). The panel has presets (Default, Hi8 '98, MiniDV '04, Low light gain, Clean) and these groups:

- Lens: zoom, barrel distortion, chromatic aberration, vignette, bloom.
- Focus: contrast autofocus with hunting driving depth of field, aperture.
- Exposure / iris: auto exposure, bias, iris speed, max gain-up, fluorescent flicker beat.
- Sensor noise: base noise, gain noise that grows with exposure gain, chroma blotches.
- Image processing: softness, edge enhancement halos, chroma bleed and delay, highlight knee, black pedestal.
- Colour: white balance temp and tint, saturation, contrast.
- Handheld: shake, breathing sway, image stabilisation, motion blur.
- Format: 4:3 pillarbox, viewfinder OSD (REC, counter, date), scanlines, tape tracking noise, frame-rate cap.

## Milestone 1 room

**Backrooms > Build Office Room Scene** rebuilds the hand-made 7.2 x 6 m room with baked bounce lighting. All
textures are procedural and seeded (`BackroomsTextureGenerator`), and all audio is synthesised (`ProceduralAmbience`),
so there are no third-party assets to license.
