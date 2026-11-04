# Status (where to resume)

The game is called **Still Recording** (working title).

Last updated 2026-11-04 (world version 13).

## Newest (2026-11-04)

- **Baked textures** (Tools/Blender/bake_kit.py, Generation/BakedMaterials.cs): a model can carry its own colour texture,
  painted by Cycles from its shape (dirt in the creases from AO, worn edges from a Bevel-node curvature mask broken up
  with noise, grime and streaks, dust on top, wood grain, flecks, prints); its slots keep their gloss and the fine grain
  as the shader's detail normal map over the bake's UVs (the carpet's _NORMALMAP + _DETAIL_MULX2 variants: none new).
- **The good rooms' furniture modelled** (Tools/Blender/build_rooms.py): a 28" top-freezer fridge with a food notice, a
  countertop microwave, base cabinets and a post-formed worktop made to each counter's length with a drop-in sink, a
  steel tanker desk with 15" CRTs and a keyboard, a break-room table, a moulded-stone mop sink with its service faucet,
  hose and a string mop, the breaker panel with its labels and conduit, boltless shelving with paint cans and cleaner.
- **The ceiling lights' parts modelled** (Tools/Blender/build_lights.py): the troffer's door frame and its hanging-out
  housing; the warehouse strip's reflector, lampholders, round T8 tubes with their caps, chains on a short drop and
  a cable kit on a long one.
- **The hand-held items baked** (build_items2.py, build_items.py): wear and grime on all of them, a printed spray paint
  label and a printed nail box.
- **The warehouse** (Tools/Blender/build_warehouse.py): modelled stringer pallets (notched runners, nails, a heat-treatment
  stamp; new, weathered, or with a broken board), rack base plates with anchor bolts, riveted beam connectors with
  safety locks, bay location labels, yellow upright guards, a capacity plate. The concrete floor no longer has the
  carpet's parallax depth, sheen and fibre detail (a copy of the carpet material had kept them).
- **The Poolrooms' fittings** (Tools/Blender/build_pool.py): a 6 ft lifeguard stand (stainless frame, fibreglass seat,
  steps, a red rescue tube), exposed shower columns with push-button valves, stainless ladder treads, changing stalls of
  solid plastic on aluminium brackets with coat hooks and a headrail, aluminium seat planks on the bleachers. Drawn with
  PoolLit copies of their baked materials (PoolMaterials.ForBaked).
- **A drinks vending machine** (Tools/Blender/build_vending.py, Interaction/VendingMachine.cs): remade after the
  owner's photo of a 1990s can vendor (72 x 37 x 33 in): a tall backlit sign (an invented brand, Polar) brighter over
  its two tubes, a control panel of lit pictures and five picture buttons, the coin entry and its label, the T-handle,
  the delivery port, a red kick panel. It hums (fan, a compressor that cycles, ballast buzz), lights the floor in
  front, and its cord runs to the nearest outlet (or it stands unplugged, dark). In most break rooms, rarely in a
  hallway or office. Poke it for a can.
- **Signs** (Tools/Blender/build_signs.py): the exit sign remodelled (thermoplastic housing, stencil letters that glow,
  some with emergency lamp heads); a 24 in stop sign on a post in a rubber base at some warehouse cross aisles.
- **Doors and light**: a shut door no longer shows the lit room's light on its edges from the dark side, and light
  comes through gradually as it opens (the light grid's opening grows with the door's angle).
- **Plastic chairs** (Tools/Blender/build_chairs.py): a white or green resin lawn chair (remade after the owner's photo:
  a one-piece fan-back monobloc, fused with a voxel remesh) and a blue or orange school chair (one shell with a hand
  hole on a bolted steel frame), both movable and carried like the office chair; rare in offices, lawn chairs on some
  Poolrooms decks. Tools/Blender/showcase.py renders any of the baked models put together.
- **Room builder: POOLROOMS ROOM** (CustomRooms/PoolRoomStamp.cs): a room can be made in the Poolrooms' style with its
  tiles, water (a pool with a deck, flooded, or deck only), deck width, shallow and deep ends, slope, lanes, roof
  openings and fittings (ladders, lifeguard's chair, drain, showers, bleachers, lawn chairs). Spouts and slides are not
  options yet.

## Newest (2026-11-03)

- **The furniture remade** (Tools/Blender/build_furniture.py): the chesterfield (deep buttoned in a diamond with its
  pleats, one rolled top round arms and back, nailheads, bun feet), the modern sofa and armchair (crowned, piped cushions
  that sag where sat on), the office chair (contoured seat with a waterfall front, lumbar back, gas lift, twin-wheel
  casters), the floor lamp (harp, finial, bound shade, pull chain), the conference table (laminate sections with
  T-moulded edges end to end on T-legs) and the wall clock (a printed dial); the door's casing, panel beads, hinges and
  lever refined. Same sizes and slots, so the prefabs and their colliders are unchanged.
- **Level 0's rarer things** (Docs/WorldGeneration.md, "Water, old fittings and what the office left behind"): damp
  carpet (it squelches), water run down walls, leaks dripping into puddles and buckets, mould in corners; blackened and
  failing tubes; fire extinguishers, pull stations, horn strobes, thermostats, low vents, drinking fountains in alcoves,
  filled-in windows, corner guards; door closers, kick plates, wedges; floor boxes and cut cables, a taped-down extension
  cord, cubicle partitions, and a modelled wooden chair alone in the middle of a room. All rare; every kind has its own
  teleport in F5 > PLACES > WEAR AND DETAIL. All of them are now real models (Tools/Blender/build_fittings.py): real
  sizes, bevelled, printed labels and dials, real parts (an extinguisher's pin, seal, gauge, hose and tag; a closer's
  working arm), the chair's wood grained and worn.
- **More real handheld motion** (Docs/Camcorder.md, "Standing, side-steps, turning and the walk jitter"): standing still,
  side-stepping and the shake of turning now come from the owner's phone recordings too, and the walk has a third take.
  Camera menu > CAMERA SHAKE > Walk jitter turns the walk's step-to-step irregularity down (or up) without recording again.

## 2026-11-02

- **Road rugs**: some living rooms have a children's road rug on the floor (the owner's design), at real rug sizes,
  with a sewn binding round the edge and now and then a corner come up; fixed in place, carpet underfoot
  (Docs/WorldGeneration.md, Living rooms).
- **Pool ring and shadow ghosting fixed**: the line in a ring round the player on the pool floor (the refraction
  switching off past 4 m) is gone; the ray-traced shadows no longer leave a trail of old foot shadows when strafing,
  and their grain is lower (FidelityFX-style history clamp, two filter passes).
- **Living-room walls from a photo**: the homes' orange-peel drywall is now the owner's photo of a real sprayed wall
  (Tools/Textures/make_surface_textures.py orange_peel), its relief recovered from the photo's raking light, with
  parallax and micro-shadowing.
- **The shadow remade** (Docs/Audio.md, "The shadow"): a tall, thin, faceless, Slender-like figure that stands perfectly
  still in a room it has darkened, its edge rippling like heat haze. It only moves when unseen, coming a little nearer each
  time; it is gone the moment it is out of frame; a glance sends it back; staring builds static until it is too late: it is
  in your face and the tape cuts.
- **Ray tracing and surface detail** (Docs/RuntimeLighting.md, "Ray-traced light" and "Surface detail"; F5 > LIGHT >
  RAY TRACING / SURFACES): on a card with ray tracing (Direct3D 12), every ceiling fixture shadows every chair, box, door
  and person round it with a panel's soft, contact-hardening shadow (the 16-32 shadow maps are gone: the GPU frame got
  faster); glossy surfaces reflect what is off screen too; bounce light is hidden by what really stands over a surface.
  Carpet, ceiling tiles, wallpaper, plaster and fabric have relief (parallax close up, darker crevices, the relief shading
  raking light) and carpet and upholstery a soft sheen. Without such a card everything is as before.

## Earlier (2026-11-01)

- **Water and effects, modern pass** (Docs/Poolrooms.md, "Water and effects pass"): the pool water's refraction is
  traced through the surface (deep floors shift and wobble, shallow edges barely), with a faint colour split; sunlit water
  glows from within (the sun scattered in the water); the mirror on the water is sharp where things meet it and bends and
  softens far off; pool tiles and props reflect what is on screen (props, people, arches, slides) as well as the traced
  room; dust in the air in Level 0 is lit by the volumetric light (sparkles in shafts, not in their shadows).
- **Lighting engine, modern pass** (Docs/RuntimeLighting.md, F5 > LIGHT > SCREEN LIGHT and AIR): bounce and occlusion
  found in the picture (visibility bitmask: corners and contact shade softly, and what hides light gives its own back, so
  nothing goes black the way plain AO made it), glossy reflections found in the picture (the probes where nothing is
  found), reflection occlusion, highlights that no longer sparkle, and volumetric light (the haze lit cell by cell of the
  view: light falls through doorways in shafts, the air glows toward fixtures). Settled dust and dust in the air are back
  in Level 0 (off everywhere since the Poolrooms came in).

## Newest (2026-10-27)

- **Item tuning, F2** (Docs/Interaction.md): with an item in hand or in view, F2 opens its settings (every public number
  or switch of its class, and its weight, found by reflection: new items show up by themselves). Changes apply to every
  item of that kind at once and are saved into the project (`Resources/ItemTuning.json`) as its defaults in every world.

- **Sledgehammer and breaking walls** (Docs/BreakingWalls.md): a short-handled sledgehammer (a 4 lb club hammer, 43 cm),
  swung one-handed (hold left mouse to wind up, release to swing), found in maintenance closets and sealed storage or from
  the L menu. Ordinary Level 0 walls break realistically: each panel's two gypsum boards come apart in irregular plates
  where the hammer meets them (up to 22 cm round a full blow; cracks round the hole, pieces falling as debris, dust), held
  only by their screws. Inside: one steel stud a panel in the Offices, off to one side so you fit through beside it once
  the boards are gone (it does not break), or wood studs at 400 mm (Hallways, Warehouse) that split; electrical boxes and
  cable behind the outlets and switches, the odd copper pipe (batts are off for now). Light from the real lights near you
  comes through a hole exactly (the broken pieces cast the shadows), grid light softly through the open share of its
  footprint; the chunk behind is drawn when you can see through; sound goes through. Saved with the world.
- **Second round** (2026-10-27): debris wedged in a wall drops to the floor (no more floating bits); the boards' collider
  is the shape of the pieces drawn (a cord through a hole rests on the edge); crumbs of rubble are tidied away out of
  sight; the graphics card does about a third less (the 3D view drawn at the camcorder's resolution, AO at half
  resolution, reflections re-captured less): 72 % -> 47 % at 60 fps.
- **Fixed from play-testing** (2026-10-27): square patches of light opposite a hole; a lit band over doorways in dark
  rooms (the bounce blended across the header); the bounce shimmering in dark rooms (probes now settle); floating drywall
  chunks and batts; the lighting shader compiling mid-play (two crawl-hole view variants were missing from the warmup);
  the fans (frames are capped at 60 by default).

- **Reference room on the game's own lighting** (Escape > Reference room, Docs/ReferenceMatch.md): Kane's room is now a
  room of the procedural world (rebuilt on the 1.2 m grid, forced in beside the spawn hall), lit exactly like every other
  room, so tuning it tunes the game. Matched to the footage within about a quarter stop everywhere: ceiling panels throw
  light like real flat panels (Lambertian: walls fade from the top, no round hot spots), bounce light passes on all of
  each bounce (rooms with light walls are evenly lit), AO no longer darkens direct light (the dark smudge at pillar tops
  and the dark lines along the ceiling grid are gone), off-white ceiling tiles, panels bright enough to clip to white,
  a calmer carpet, a camcorder white balance that keeps yellow rooms yellow, and the footage's highlight colour in the
  Reference Match preset. Screenshots: Docs/screenshots/refmatch/compare_kane_world_lit.jpg,
  Docs/screenshots/shadows/game_vs_chatgpt.jpg.

- **Shadows like real ceiling panels** (Docs/RuntimeLighting.md): walls no longer draw hard wedge lines on the floor;
  past a wall end or through a doorway the panel is hidden gradually, so the light fades over a penumbra that is sharp
  at the wall and widens away from it. Furniture shadows come from shadow maps about 9 times sharper (they covered the
  lights' whole 173-179 degree cone; now 120, the rest drawn by the light grid), which is also 1.9 ms faster. Shadows
  are filtered in the shape of the panel: a strip light's are long and soft along the tube, crisp across it.
  Screenshots: Docs/screenshots/shadows/.

## Earlier (2026-10-26)

- **Camcorder rebuilt as a real camera** (F5 > CAMERA, Docs/Camcorder.md): every old filter was checked against how a
  camcorder makes a picture; most were redone. Lens (barrel distortion sign fixed, lens glare, rounded vignette, real
  small-sensor depth of field only when it shows), exposure metered from its own picture (iris, then gain, then low-lux
  slow shutter; no extra probe render), auto white balance, a fixed 640 x 480 recording raster with the camera's
  transfer curve, knee and clip, sensor noise from the real gain, CCD smear, one shutter motion blur in linear light,
  and formats (VHS-C SP / EP, Hi8, MiniDV) with their own colour, tape wobble, dropouts, head switching and DV
  compression. Only the 4:3 part of the screen is rendered. Old saves are converted (field of view, frame, handheld and
  audio kept; the old values are backed up in PlayerPrefs).

- **Lighting look and new light** (F5 > LIGHT): ready-made looks (STILL RECORDING, EVEN & PALE, DIM & MOODY, HARSH TUBES,
  WARM & OLD) plus "my look"; light colour (kelvin) and tube tint, panel glow, bounce strength, colour bleed and second
  bounce, shadow softness, contact shadows, haze, the camera's exposure and contrast, and the Poolrooms' sun, sky and
  bounce. Under it: ceiling panels lit as rectangles (rectangular reflections on glossy surfaces), bounce light traced
  through the rooms by probes on the 1.2 m grid (spills through doorways, stopped by walls, shut doors and furniture),
  shadows that are sharp where things touch and soft farther away, short contact shadows for small things, and thin
  haze lit by the lights. All of it together costs about 0.3-1.5 ms of GPU time. See Docs/RuntimeLighting.md.
- **Right click on a door**: the quick way through (shove it open, tug it toward you, shove an open one).
- **Editor note**: after a change to the lit shader, the editor compiles its variants in the background (a few minutes;
  Play waits for it). Without this, Play Mode stalled on them and Direct3D 12 crashed (see RuntimeLighting.md).

- **Realism pass**: shut doors block light and sound (and a line of light shows under a door from a lit room); Steam
  Audio has the world's walls (items, doors and switches are occluded and muffled through them); fingers stop on what
  they hold; footsteps from the feet on carpet, wood, tile, water or duct metal, a scuff when stopping; lean round
  corners (Z/X); the free hand steadies on door jambs; shoulders brush walls; breathing after running; clothes rustle;
  wind and handling on the mic; the camcorder winds its gain up in the dark and its focus hunts there (and focuses on
  your hand up close); doors drift in the air, tape ends flutter near vents, ceiling tiles creak, furniture ticks,
  brownouts dim the lights and the hum; dust in the desk lamp's beam; heavy things slow you, and you bend to pick
  things up.

- **Third pass**: wooden doors in some Backrooms doorways (take the lever, walk to open; they swing on and remember
  where they were left); working light switches (a finger flicks the toggle, the room's lights go out or strike back
  on); tape lined up with right mouse, pulled with left, bending a little and wrapping wall ends and floor-to-wall
  corners; cords reel in from either end and hold you back before a plug pulls out; walking with the lamp no longer
  shoves you; arm joint limits and grips placed on the furniture's real surface; feet that plant and step (no snapping
  between walk directions); no watch or ring; drywall, drop-ceiling and vent textures from the player's photos.

- **Second polish pass**: chunks behind walls are no longer drawn (about +20% fps at spawn, no pop-in: Performance.md);
  outlet focus brackets, cords wrapping corners, hold right mouse to reel a cord in, look at a cord to grab its plug;
  thrown items go through noclip doors; a walking body with IK legs (forwards, backwards, strafing), crouch and
  get-up poses, no legs in the camera when getting up from a crawl; grips on the furniture's real surface; sharper
  shadow maps near the player; softer AO in the Poolrooms.

- **Polish pass** (see WORKLOG): forgiving item pick-up (no crosshair needed; focus brackets), hold Q to throw, held
  items move with the hand, cords no longer clip through the world, the lamp's cord starts bundled and yanks out with a
  sound and jolt, chicken wheezes in and squawks on release, contact shadows under items, normal maps on all world
  and item materials (and Poolrooms tile glaze and grout), opaque 3D-looking tape and a roll that empties, modelled
  wall outlets and light switches, a rigged realistic arm and body, camcorder presets with SET AS DEFAULT per preset,
  vents always crawlable, smoother pool ripples, and noclip doors no longer show the far side early.

- **Main menu and world saves**: the world scene opens on a camcorder-style main menu; three save slots, each world
  keeping its seed, the player, moved and spawned furniture, items (and what is plugged into what), spray paint, chalk
  and tape marks, things taken from special rooms, and the way back through a noclip door. Docs/MainMenuAndSaves.md.
- **Items** (L menu): spray paint, chalk, blue tape, desk lamp, extension cord, rubber chicken. Docs/Interaction.md.
- **Power**: wall outlets are live in Level 0; lamp cord 1.8 m, extension cords 8 m (up to 3 chained), physics ropes.
- **Crawl spaces**: every vent is a portal to a finite special room above the ceiling over it (closet, storage, camp)
  with loot. Docs/CrawlSpacesAndNoclipDoors.md.
- **Noclip doors**: one per 4 x 4 districts in Level 0, taped outlines; walk through into the Poolrooms with a camcorder
  glitch; the Poolrooms' taped walls lead back. They replaced the far crawl hole at spawn. F5 > PLACES > TESTING jumps
  to the nearest vent or door.

Earlier notes (some are now out of date where the list above says otherwise): Read `Docs/ProjectBrief.md` for the vision and `Docs/WorldGeneration.md` for how
generation and lighting work now.

## World generation (world version 7)

- Layered: zones (Voronoi, data assets) > biomes (density field per zone) > landmarks (sparse grid) >
  districts (4 x 4 chunks planned at once) > chunk slices (streaming only). See WorldGeneration.md.
- Rules live in assets under `Assets/Backrooms/Resources/WorldGen` (World, Zones/Level0, Biomes, Landmarks).
  Recreate missing ones with Backrooms > World Gen > Create Default Assets. F5 > World Gen edits the
  first zone live and saves on top (older saved settings are migrated).
- Landmarks: Colonnade hall, and the two-storey Atrium (the multi-storey test volume; the upper storey
  streams only around it). Seed 1234: atrium at cells (251, 363), colonnade halls at (-319, -20) and (357, -16).
- The atrium has a wing with stairs and/or an elevator up to its balcony storey (WorldGeneration.md,
  "Stairs and elevator"); pressing buttons is a hand animation (Interaction.md). Nothing is on the upper
  storey yet to reward getting there.
- Chunk coordinates are (x, storey, z). `WorldStreamer.ChunkLoaded/ChunkUnloaded` are the hook for
  registering Steam Audio geometry per chunk (not done yet).

## Moving things (branch grab-props)

- Sofas, armchairs, office chairs, conference tables and loose cardboard boxes (new, world version 5) can be
  grabbed with the left hand, dragged, swung, lifted at one end, tipped and shoved. See `Docs/Interaction.md`.
- Moved props are remembered for the session (they stay where you left them when their chunk unloads and
  reloads); saving to disk is not done yet.
- Placeholder first-person arm (watch, ring) and body from `Tools/Blender/make_player.py`.

## Lighting (all real-time, live shadows)

- Default **light grid** mode: the nearest 32 tubes (F5 "Shadow-casting lights", capped at 32), up to 6
  faulty tubes and the nearest lamp are real URP lights with shadows that light every layer (shadows stop
  them at walls). Every other fixture lights a GPU light grid (0.6 m texels, ~77 m around you) sampled by
  `Backrooms/Lit`, with wall-aware sampling and a bounce field for ceilings. Fixtures hand over between
  the two over 0.6 s. No doorway or bounce lights.
- F5 > Graphics > **Light grid** off returns to the phase 2 room-layer system (28 rendering layers in 4
  district-parity sets), kept as a fallback.
- All world materials use `Backrooms/Lit` (copy of URP 17.4 Lit + light grid, see shader header). After a
  URP upgrade, re-copy Lit.shader / LitForwardPass.hlsl and re-apply the two marked lines.
- Build Infinite Offices Scene now keeps an existing post profile (the owner's VHS '96 tuning) instead of
  recreating it.

## F5 menu tabs
CAMERA, PLAYER, WORLD, AUDIO, SOUND, DISPLAY, LIGHT (strength, reach, darkness, distant crisp lights, flicker,
pool shape; "Even & pale" preset), PLACES (jump to atriums, balconies, stairs, elevators, colonnade halls,
spawn; press again for the next nearest). Camera preset "Found footage" matches the reference series (sensor motion blur from the camera turn rate,
head-switching band, compression blotches are the new CAMERA sliders; `Debug/SpinProbe` turns the camera for screenshots).
V toggles noclip. Teleports hold you in place until the floor has loaded (`TeleportSafe`).

## Debug tools

- Backrooms > World Map (top-down zones/biomes/districts/rooms/light layers + consistency checks).
- Edit-mode tests in `Scripts/Editor/Tests` (14 tests).
- `LightingTour` (fixed seed, saved views, `compareLightingModes` for A/B shots with frame timings),
  `NavProbe` (long walks across borders).
- Play mode needs `runInBackground` on while Unity is unfocused. The MCP camera screenshot pauses the
  editor; unpause after it.

## Open items (next steps, in order)

0. Owner feel test: chalk, tape, lamp and cord handling, the noclip glitch, the special rooms' look. Special room
   loot is placeholder (whatever items exist); the settings page of the main menu is empty.
0b. Feel test of grabbing (Docs/Interaction.md lists the tuning values); a better hand model (the thumb sticks out);
   creatures moving things while you are away. (Moved props are saved to disk now.)
1. Owner review of the light grid look (Docs/screenshots/phase4_ab_*: `_layers` = old, `_grid` = new).
   Tunables: LightGrid.bounceGain / floorAlbedo / bounceSpread / wrap, FixtureLightManager.handoverTime.
2. The arched-niche/balcony room from references/update11/ref3_arches_balcony.png as a landmark kind
   (the reference image is not in this checkout).
3. Steam Audio geometry per chunk via the ChunkLoaded/ChunkUnloaded events.
4. Something worth finding on the atrium's upper storey. Elevator extras: sound (hum, ding, door), floor
   indicator, a stuck or flickering state. Stairs: a landing light, the camera could smooth the step bob.
5. More zones (the Poolrooms are the second; noclip doors can lead to any pinned zone: see NoclipDoors.TargetZone).
6. Movement (zoom F, crouch C, crawl Ctrl, jump Space) awaits the owner's feel feedback.
