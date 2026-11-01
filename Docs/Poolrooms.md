# The Poolrooms

A second zone: endless white-tiled halls flooded with still, shin-deep water, lit by a sun that is always in the same
mid-afternoon place and comes in through openings in the roof. One biome for now ("Poolrooms").

## Where it is and how you get there

- Zone `Poolrooms` (Resources/WorldGen/Zones/Poolrooms.asset, code default `ZoneRules.Poolrooms()`), biome `Poolrooms`
  (Biomes/Poolrooms.asset, `BiomeRules.Poolrooms()`).
- **Pinned**: not a Voronoi region. It is the square of districts within `pinRadius` (5) of `pinCentre` (24, 0), so
  x 1094-1728 m, z -288-346 m, about 1.1 km east of spawn, for any seed. `WorldGenSettings.ZoneAt` checks pinned zones
  first; Voronoi sites never pick a pinned zone.
- **Sealed**: `sealedEdges` makes every district edge between it and another zone a solid wall on both sides
  (`WorldGenSettings.SealedBetween`, used by `DistrictGenerator` for its edge openings). Walking there finds a wall.
- **Entrance** (world version 13): noclip doors. Rare taped door outlines in Level 0 walls (one per 4 x 4 districts)
  that you walk through; you come out of a taped wall on a dry tiled deck in one of the Poolrooms' districts, and that
  wall leads back to the door you came in by. See CrawlSpacesAndNoclipDoors.md. (Before: a far crawl hole from the spawn
  district, `ZoneRules.entrance`, now unused.) F5 > PLACES > POOLROOMS ENTRANCE goes to the nearest noclip door,
  POOLROOMS to a Poolrooms arrival door.
- Settings saved from F5 before the zone existed get it added on load (`AddMissingPinnedZones`).

## Plan (PoolPlan, DistrictGenerator)

The office partition makes the rooms and doorways (the biome asks for big rooms, wide halls, loops, no crawl slots).
Then, instead of lights, furniture and outlets:
- Ceilings 3.3-5.4 m in 0.3 m steps (whole rows of 15 cm tiles). Headed doorways are 2.7 m (`OfficeLayout.headerHeight`).
- Each room is a flooded **basin** (80 %) or a dry **deck**. Big basins may get a raised walkway along one wall.
  The deck is at the storey's floor level, so holes, spawning and the player work as in the offices; basins are sunk
  0.45 m (`PoolBiome.BasinDepth`) with the water 0.12 m below the deck.
- Round tiled columns on a grid in big rooms (sometimes one colonnade down the middle).
- Roof openings per room: one big rectangle, parallel slots, a grid of squares, a few round ones, or slots along one wall
  (bars of light down that wall). Corridors get a centre slot or a row of squares. Openings never touch a column.
  2026-10-31 (world version 17, the owner: "larger roof openings"): 85 % of rooms have them (was 70 %), the big rectangle
  is about half the room (up to 6 x 6 cells) and the commonest pattern, slots are 0.6 m wide (0.3), squares 0.8 m (0.6),
  rounds 1 m across (0.9), slots and squares every other cell.
- **High windows** (`PoolPlan.Windows`, the owner: "some side wall high openings"): in 40 % of rooms 3.6 m or taller,
  every other cell along the wall on the sun's side (+X or +Z) has a 0.6 m wide window from 1.95 m up to 0.45 m under the
  lower ceiling, with the sky in it (`OfficeChunkBuilder.WindowFaces`: the wall round it, the reveal, a PoolSky quad on the
  wall's middle line; this is the Backrooms, the room behind sees the same sky). Stored per cell (`PoolCell.windows`:
  1 its -X edge, 2 its -Z edge), edge code 3 in the shaders' map: the sun comes in through them in beams (`PoolSun`,
  `PoolSunVisibility`), reflections see sky in them (`PoolTrace`), bounce and ripples treat them as wall.
- Only the far hole: no ordinary crawl-hole pairs. It opens from a deck; if every usable wall is in a basin room, one
  room is drained (`CrawlPortals.Place`).
- No landmarks (atriums, colonnades and custom rooms are office architecture).

## Geometry (OfficeChunkBuilder.Pool.cs)

Walls, doorways, soffits, posts and holes are the office builder's in one tile material. Added: deck and basin floors,
walls and posts carried down into basins, pool edges where a deck meets a basin, three-step stairs in the basin cell
of every doorway that steps down into water, a water quad per basin cell, ceilings cut around openings with a 1.2 m
light well and a sky quad on top, round columns (capsule colliders). Pool renderers cast no shadows and use rendering
layer Default only (no fixture lights here).

## Light (no lamps, no shadow maps)

- **Sun**: traced in the shader (Shaders/PoolCommon.hlsl `PoolSunVisibility`; same algorithm in C# `PoolSun`). From a
  point toward the sun, walk the 1.2 m cell grid: walls, door headers, lower ceilings and columns stop the ray; it must
  reach the ceiling inside an opening and leave the light well's top inside it too. The edge of each opening softens
  with distance. Unlimited range, exact walls, no shadow passes, the same in portal views.
- **Bounce**: baked per cell when a chunk's data is built: sun samples on each cell's floor and walls plus sky through
  openings, spread 18 steps through open edges (walls keep it in), stored in the map's alpha and read wall-aware.
- The map: `PoolWorld` keeps a 144 x 144 RGBA32 texture, toroidal by world cell, of every loaded Poolrooms chunk
  (r: edges/basin/columns, g: ceiling, b: opening shape, a: bounce). Chunks write it when they appear and clear it when
  destroyed.
- `PoolWorld` light settings (sun colour/intensity, ambient, bounce, sky) are inspector fields, set as shader globals.
- The zone's fog is swapped in per camera (the player's, or a portal camera looking into the Poolrooms). 2026-10-31: thinner
  (0.0035, was 0.008: the owner wanted less haze).
- **Far fade** (`PoolFarFade`, `_PoolFar`): the world is loaded 3 chunks (43 m) round you; past that there was nothing
  and the camera's clear colour showed black. Every Poolrooms shader now fades into the fog's colour from 60 % of that
  distance to its end (`PoolWorld.farFadeStart`), and the camera clears to the fog's colour while it looks at the
  Poolrooms.
- SSAO: only a gentle share (38 %) of it on the bounce light, none on the sun (PoolTile/PoolLit). At full strength it drew
  hard dark bands along every floor-wall join and in corners, far heavier than in Level 0, which is lit mostly by
  direct light.

## Tiles, reflections, water and caustics (2026-10-31)

The owner's reference pictures (sunlit tiled pool halls in pale sage and cream, deep emerald water, glossy tiles): "the
tiles more reflective and 3D looking, the water better, better caustics and reflections".
- **Tiles** (`Backrooms/PoolTile`): pale sage walls, ceilings and basins (`_TileColor`), cream dry decks with tiles twice
  the size (`_DeckColor`), light thin joints. Each tile is shaded as a slightly domed face with its edges rolled into a
  recessed joint (`_Bevel`, `_Dome`; the joint gets less light, `_JointShade`) and set a little off true (`_Tilt`), so the
  glaze catches light and breaks reflections up tile by tile; the per-tile shape fades out where tiles get smaller than
  a few pixels.
- **Reflections** (`PoolTrace` / `PoolShadeHit` in PoolCommon.hlsl): Unity's own screen-space reflections for URP are
  only due in 6.7, and the screen cannot show what is behind you; so the glaze reflects a ray traced through the same
  cell map the sun uses (walls, door headers, lower ceilings, round columns, decks, the water, the sky through an
  opening or the inside of its light well), what it meets lit by the traced sun (its edges softened with distance:
  `_ReflBlur`) and the bounce. Fresnel: little face on, a mirror at grazing angles. Up to 34 m; skipped under water and
  beyond 48 m. Curved walls, arches and props are not in the map, so not in these reflections.
- **Water** (`Backrooms/PoolWater`): transparent queue, reads the opaque and depth copies (main camera only;
  `_PoolScreenTextures`): refraction, colour by the thickness of water looked through (deeper emerald since 2026-10-31:
  `_Absorb` 2.6 / 0.95 / 1.35 per metre), screen-space reflection with the traced reflection as the fallback (it was the
  ceiling straight above), the sun's glint, a fine shimmer from the same small waves as the caustics (`_Shimmer`).
- **Caustics** (`PoolCausticMap`, `Hidden/Backrooms/PoolCausticSplat`): Evan Wallace's method. Each frame in the
  Poolrooms a 512 x 512 grid over one 2.4 m tile of the water's small waves (`PoolWaveSlope`: 12 waves, 11-38 cm, whole
  numbers across the tile so it repeats seamlessly, each curving the surface about the same so the sun comes to a focus
  a little below the basin floor) is drawn into a 1024 x 1024 map with each vertex moved to where its refracted sun ray
  meets a floor 0.33 m down; each fragment adds the ratio of the grid's area before and after (spread out: dim; gathered:
  bright lines). Flat water gives 1 everywhere; mipmapped, so it averages to 1 far off. Tiles under the water read it at
  the point on the surface their sunlight came through (`PoolCausticLight`), split slightly by colour, wobbled by the
  player's ripples, at full contrast at the basin's depth and none at the surface. Speed `speed` (0.22: calm).
- **Light off the water** (`PoolWaterLight`): walls and ceilings facing the reflected sun get the wavy light that sunlit
  water throws up: back along the reflected sun ray to the water (in a basin, in the sun, the way clear of walls),
  about 7 % of the sun, rippled by the caustic map seen larger and softer the further it travels.
- Cost: 1920 x 1080 in the editor, a Poolrooms hall: GPU ~6.5 ms with all of it, within noise of it all off.
- Checking: `DebugTools.PoolShots.Run(folder)` finds sunlit water near the player (`PoolWorld.SunAt`, the shaders' own
  map) and saves views of it; `PoolTour` saves views of a district's big rooms.
- **Ripples**: `PoolRipples` runs a 256 x 256 wave simulation (8 cm texels, 20 m square following the player, 60 Hz;
  Resources/PoolRipples.compute) while the player is in the Poolrooms. Legs push the water as they move, stepping in
  splashes; walls reflect waves, decks absorb them. Footsteps in water are splashes (`ProceduralSounds.WaterStep`).

## Cost

Editor, 1920 x 1080: a Poolrooms hall ~3 ms median (no shadowed lights; the light-grid and fixture systems are idle), a
Level 0 corridor 17.7 ms in the same session.

## Arches, curves and the second floor (world version 12)

- **Arches** (`DistrictGenerator.PoolArches`, `OfficeChunkBuilder.PoolShapes.cs`): every open span between two rooms
  is an arch, and about half of the single doorways in long shared walls are widened to two or three cells and arched.
  Runs longer than four cells become arcades (three-cell arches on one-cell piers). The curve is a half circle where
  the room is tall enough, else a flatter half ellipse from 1.8 m; its crown is 0.45 m under the lower ceiling.
  Stored per edge in `OfficeLayout.spanV / spanH` (index in the run, run length).
- **Rounded corners**: quarter-round tiled fillets (0.9 m radius) in the inside corners of rooms, away from stairs,
  holes and columns.
- **Curved walls**: in about half the big halls a free-standing arc of wall (2.4-3.3 m radius, 140-215 degrees), full
  height or 2.1 m, well clear of the room's walls (`OfficeLayout.arcs`; built by the chunk its centre is in).
- **Spiral stairs** (`PoolPlan.SpiralSite`, `MarkSpiral`, `BuildSpiral`): in about two districts of three, a 5 x 5 cell
  tower inside one chunk. Ground floor: a dry room open to the floor above, a tiled column, 50 wedge steps of 0.18 m
  (16 a turn) with a railing on the open side, ending in a railed bridge at the upper floor's level. Upper floor: the
  well (no floor, no random doors, four square openings in its ceiling) with one door at the bridge (`LandingDoor`,
  made before the rooms are joined so the well is connected through it).
- **The second floor** is a whole Poolrooms storey 9 m up (its own rooms, basins and openings). `ZoneRules.fullStoreys`
  (1 here) makes the streamer load it in every column of the zone, not only near tall rooms. The shaders' map holds both
  storeys (rows 0-143 and 144-287; `PoolStorey` picks by height), as do the ripples, water and audio queries.
- Light wells of the ground floor end under the second floor's basins: the sky up there is the Poolrooms' logic.

## Sound

`BuildingAudio.PoolBlend` (0..1 over 1.2 s as the listener enters) fades out the building hum, ventilation and room tone
and fades in a water bed (`ProceduralSounds.PoolAmbience`). The listener's reverb takes a tiled-hall profile: 1.4-2.6 s
decay depending on the visible volume (was 2.4-5.5 s, bright and louder than the dry sound: the owner heard "weird infinite
echoing"; treated natatoriums keep near 1.5-2.5 s), the highs dying first (HF ratio 0.55), the tail 4.5 dB under the sound.
- 2026-10-31 (the owner: the ambience "too loud, should be more chill, quiet"; the water steps "sound like blaster
  pistols"): the bed is now a soft low wash with a slow breath, faint far laps and a distant drip every 4-9 s (a tiny
  bubble, not a chirp), at 0.028 (was 0.05); the pool reverb's tail a little lower (200 mB, was 350).
- **Wading**: most of the sound of water is small bubbles caught under it, each ringing a few ms at a pitch set by its size
  (Minnaert, about 6.5 kHz over its diameter in mm) and barely rising; the old steps' droplets swept up four times in pitch
  (the blasters). `ProceduralSounds.WaterStep` (8 variants): the low slosh of the water pushed aside, a swoosh of the leg,
  a little spray, a fizz of 55-85 tiny bubbles crowded at the start, two or three drops falling back. Under the steps a
  wading swish (`WadeLoop`) whose level follows the speed through the water (`PlayerAudio`).

## Things in the Poolrooms

The arm, body, items and furniture normally use Backrooms/Lit, which has no light there. `PoolLitSwitch` (on the arm,
body, items and every movable prop) swaps their materials for Backrooms/PoolLit copies (same colours and textures, the
Poolrooms' sun and bounce) while they are in the Poolrooms. Backrooms/Lit itself is untouched: adding the Poolrooms'
light to it made the editor recompile all its variants and crash the D3D12 device twice (see WORKLOG).

## Water interaction (2026-10-31, built: items 1-4 of the plan below)

The owner: "start that stuff, efficient but the newest tech and best methods; not sure about steps since we don't have a
body any more". With no body the water answers your movement, not feet you could see: the strides, splashes and prints
come from where you walk.
- **Wading** (`CamcorderPlayerController`): in a basin 80 % walking speed, 64 % running, 70 % crawling, and slower to
  get going and to stop (the damper's half-life x1.7), faded in and out over a quarter of a second.
- **Spray** (`PoolSplashes`, `Backrooms/PoolSpray`): one world-space particle system for every splash (one draw call; no
  object per splash): droplets thrown up in bursts, stretched along their flight, lit per vertex by the traced sun and
  the sky, hidden under the surface by the water's depth. Each stride in the water throws a little forward off the
  shin (2-18 drops by speed), stepping or jumping in a burst (60 for a jump), things thrown in by how hard they hit.
  The rings are the ripple simulation's (`PoolRipples.Splash`: anything can disturb the water now).
- **Things in the water** (`PoolBodies`): Archimedes over eight sample points per body (the middles of its collider
  bounds' octants, each pushing by how much of its slice is under), as Unity's BoatAttack water does, plus water drag;
  so floating things find their level and lie flat, sinkers fall slowly and stand lighter on the bottom. Density by what
  it is: the rubber chicken 0.25, tape 0.7, the spray can 0.85 (they float), cartons 0.3 and pallets 0.55 float, chalk,
  plugs, hammers, nails and furniture sink. Wading past pushes floating things aside and along, and wakes a floating prop
  that froze where it settled. Going in: spray, a ring, and a plop (`ProceduralSounds.Plop`: the air cavity pinching
  off, one low bubble rising half an octave, with the splash's noise and a fizz) by size. Only moving bodies within
  30 m are simulated, from the items' and props' own registries (`WorldItem.Live`, `MovableProp.Live`): no physics
  queries.
- **After the water** (`PoolWetPrints`, `Backrooms/PoolWetPrint`): wet shoe prints on the dry deck for 22 s after you
  climb out (fainter as the shoes dry), each drying over 30 s; all in one instanced draw (`Graphics.RenderMeshInstanced`,
  wetness per instance). Drips off you for 6 s, quick then slowing (`ProceduralSounds.Drip`).
- **Water on the lens** (`Camcorder/LensDrops`, CamcorderPipeline's integrate pass): a hard landing in water, a big
  splash right in front of the camcorder, now and then running through the water leaves drops on the lens: each a small
  lens of its own (the scene small and upside down in it, blurred), a dark rim, a bright edge; they run down and dry in
  3-7 s. Up to eight.

## Water interaction: the plan (2026-10-31; 1-4 built, see above)

The owner: "plan out how to make interaction with water much better". What it has now: the ripple simulation round the
player (legs push it, stepping in splashes, walls reflect it), caustics that wobble with those ripples, water steps and
the wading swish. In order of how much they add for the work:
1. **Wading feels like water**: slower in a basin (about 75 % walk, 60 % run) with a little lag starting and stopping;
   the step bob heavier; running throws more.
2. **Splashes you can see**: a ring of foam and spray at each step, more when running, a burst when jumping or stepping
   in, the drops landing back as small ripples (the ripple simulation already takes drops). GPU particles, a handful
   each.
3. **Things in water**: items and props float or sink by how dense they are, bob on the ripples, drift a little, splash
   and plop by size when dropped or thrown in (a plop for a can, a heavy splash for a chair); sunk things lie on the
   bottom, wobbling under the refraction.
4. **Wet**: wet footprints on the dry deck after leaving the water, fading over half a minute; drips from you for a few
   seconds (sound, and tiny ripples if you stand in it); a splash can put drops on the camcorder's lens that blur the
   picture and dry off (found footage).
5. **Hand and water**: crouch at the edge and click to touch it (ripples from the hand); the arm comes out wet.
6. **The water as a horror tell**: ripples with nothing making them (something unseen wading toward you), the sound of
   wading that is not yours, the water stilling all at once. The tension director and the shadow could use it.
7. **Under the surface**: crawling in a basin puts the camera under water: green murk, muffled sound, caustics on
   everything, the surface seen from below (the known gap below).
8. **Laps at the edges**: when the ripples reach a wall or the pool edge, a soft lap sound there (the simulation knows
   where).

## Wading motion (2026-10-31, built)

Studies of walking in water: shorter steps, slower, more knee and hip bend at each foot strike, less even left to right.
- **The step clock** (`CamcorderPlayerController.WadePhase`, `WadeSteps`): in water, steps of 0.48 m (0.7 m running);
  each whole number a foot coming down. The camera's gait, the surges, the water steps (`PlayerAudio`) and the stride
  spray (`PoolRipples`) all keep time with it.
- **The gait** (`HandheldCameraShake`, in water in place of the land walk): each step the leg lifts slowly out of the
  water and drops quickly (a nod as it plants), more rise and fall than on land, rolling leg to leg (one side heavier),
  leaning a degree into the water; running, a lurch held through each step; standing, a faint sway.
- **Surges**: the speed pulses with the step (+-13 %). Deeper water (a deep end, up the thigh) is slower still (up to
  -25 %, `PoolRipples.PlayerWaterDepth`).
- **In and out**: a dip and a little jolt stepping down into a pool, a lift climbing out.
- **Effort**: running through water tires you (BodyPresence: heavier breathing after), wading a little.

## Architecture (2026-10-31, built; world version 18)

Planned per district in `PoolPlan.Features` (`PoolFeature`s on the plan, sliced to every chunk they reach), built in
`OfficeChunkBuilder.PoolFeatures`. A second per-cell map for the shaders (`_PoolMap2`: depth, gallery, lanes, drain,
a round opening) is read only where the first map's opening byte has bit 7 set, so plain cells cost nothing more.
- **Tile detailing**: tile colour by room (`PoolMaterials.Zones`: sage 55 %, cream, deeper mint, pale aqua); accent tiles
  (`PoolMaterials.Accent`, dark teal) as the coping round every pool edge on the deck and as a band at the waterline on
  pool walls; a line of grime at the waterline; dark lane lines (a tile wide, 2.4 m apart) and round drains on some
  pool floors; now and then a cracked tile, rarely a missing one. Chrome ladders at pool edges and walkways, handrails
  down pool steps (`PoolMaterials.Chrome`: PoolLit, metallic, reflecting what a ray through the map meets).
- **Free-form pools** (about three a district): rounded rectangles, kidneys (two circles melted together) or circles cut
  into deck halls, traced cell by cell on an 8 x 8 grid (marching squares: deck, accent border, the curved pool wall,
  the floor, the water; colliders on a 4 x 4 one), a flight of steps with a handrail at the shallow end, ladders by the
  deep end, sometimes a lifeguard's chair beside and bleachers along a wall. `OfficeLayout.WaterAtPoint` follows the
  shape (wading, footsteps, floating things).
- **Depth**: about half the pools slope from a shallow end (0.42 m) to a deep end (0.75-0.95 m), flat at each end. The
  floor, the walls carried down, the steps and the colliders follow it; the water darkens with it.
- **Vaults and channels** (`Vault`): 40 % of corridors are barrel-vaulted (ceiling raised so the vault springs at 2.95 m
  or more), 60 % of those flooded with a walkway along one side; an odd-width vault's only opening is a slot along its
  crown.
- **Round halls** (`RoundHall`, a few in ten districts): a square hall whose doorways all sit near the middle of their
  walls becomes round (a curved wall with gaps at the doorways; the corners behind it are left out of the map), with a
  round pool in the middle under a round opening in the roof (`SkylightShape.Disc`: the shader knows its centre and
  radius from the second map).
- **Galleries** (`Gallery`, about two a district): a tall hall (7.2 m) with a walkway round it at 3.6 m, chrome
  railings, a straight stair along one long wall up to the far corner, and a pool in the middle to look down on. The sun
  and reflections know the gallery is there (a ceiling below it, a floor above it).
- **Running water**: removed (2026-10-31, the owner did not like the waterfalls). Spouts and cascades are still planned
  (`PoolPlan.Features`, so worlds keep the same random draws) but the chunk builder no longer builds them; their shader,
  sound and spray are gone.
- **Rooms with a purpose**: small deck rooms become changing rooms (tiled stall partitions along a wall, a tiled bench) or
  showers (chrome shower heads, a drain; no dripping sound since 2026-10-31, it looped into the reverb). A slide (a blue half pipe from a wall, curling down to just over
  the water) in some big pools.
- Cost: 1920 x 1080 in the editor, a free pool hall with bleachers: GPU ~5.4 ms.

## The water's motion, foam, bubbles, fuller splashes (2026-10-31)

The owner: "see how water movement could be improved and the splash particles, if foam could be added or bubbles, and if
the physical movement of the water can be much better". After Frontier's Planet Coaster 2 pools (shallow-water
equations, foam made by the flow and carried with it) and LightSpeed's grid foam.
- **The simulation** (Resources/PoolRipples.compute, `PoolRipples`): the linearised shallow-water equations. Waves run at
  sqrt(g x depth) (about 1.9 m/s in the usual 0.38 m of water, faster in a deep end, slower in shallows: before, every
  wave ran at about 3.4 m/s), with viscosity on top of the damping (small ripples calm first). A flow field (pushed by
  the surface's slope, losing speed) and foam: made where the water rises and falls fastest and where things go in,
  carried with the flow, about two thirds left after a second (`foamKeep`, `foamGain`, `foamDrop`). `_PoolFlow` for the
  shaders (xy flow, z foam). Up to 16 disturbances a step.
- **Wakes**: each shin pushes up a bow wave in front and leaves a hollow behind.
- **Foam** on the water (PoolWater): a network of bubbles at two scales over larger blots, drifting; how much of it is
  filled follows the amount (thin foam a few lines, thick foam most of it, never a flat sheet); lit by the sky and sun.
- **Splashes** (`PoolSplashes.Splash`): a crown of drops thrown up and outward, for a heavy thing a jet shooting back up a
  moment later (the cavity closing), a puff of mist over big ones, bubbles under the water, a ring and foam.
- **Bubbles** (`PoolSplashes.Bubbles`, Backrooms/PoolBubble): under the water, rising at about 0.25 m/s with a wobble and
  popping at the surface; each a ring of light with a glint, greener and fainter deeper; drawn after the water and tested
  by hand against the depth of what is under it. From splashes, steps, sinking things (a trail) and running water.

## Clearer pictures: anti-aliasing (2026-10-31)

Graphics > Anti-aliasing type: **SMAA** (edges in each frame, the old one), **TAA** (each pixel jittered and blended with
the last frames: no crawling edges or sparkle, a faint trail moving fast) or **STP** (Unity 6's spatial-temporal
upscaler: temporal AA that also rebuilds the full picture from the smaller render; with "Match the recording" the
sharpest). Before the camcorder's own processing (it runs after URP's post-processing). **Tile specular anti-aliasing**
(on): Tokuyoshi & Kaplanyan's geometric specular AA (as HDRP's): where the tiles' normal turns faster than a pixel shows,
their highlight widens instead of sparkling. In a test view SMAA left white sparkles on a far tiled wall, TAA calmed
most, STP was cleanest. The renderer asset's upscaling filter is changed at runtime and put back on exit.

## The player's shadow (2026-10-31)

With no body shown (the default), `PlayerShadow` keeps a stand-in of eight rough capsules (head, torso, two legs, the left
arm holding the camcorder up at the eye, the right arm down, the camcorder) that never draws itself, only its shadow
(ShadowsOnly). It bobs and its legs and free arm swing a little with the step (the wading clock in water), it changes
shape crouching (bent legs, lower hips) and crawling (along the floor), and leans the way the camera points.
- Level 0: the shadow-casting lights take it as they are (soft and faint under many lights, as a person's shadow is there).
- The Poolrooms have no shadow maps: the capsules go to the shaders each frame (`_PoolCapA`, `_PoolCapB`,
  `_PoolCapCount`) and the traced sun is tested against them (`PoolCapsuleSun`, as Unreal's capsule shadows: the sun ray's
  closest approach to each capsule's axis against its radius, softening with distance), so tiles, water, reflections
  and things there all get it. Skipped beyond 5 m of the player.
- Graphics > Player shadow (on by default). The bodies (Camera menu > HANDHELD > Body) cast their own.

## Feedback pass (2026-10-31)

The owner: echoing sounds, the tile steps, the chrome ladders "not rotated correctly", the waterfalls, a "spiderweb like"
look round the body in the water, a shadow that is "not animated", and STP freezing the picture.
- **Echo**: the reverb (above), the shower drips' loop and the waterfalls' loops gone.
- **Tile steps** (`ProceduralSounds.TileStep`): footstep synthesis's exciter and resonator driven by the ground reaction
  force (Nordahl, Serafin, Turchet): the heel's push then the ball's, a soft rubber sole (low-passed noise) into heavy
  tile on concrete (a dull damped thump, a faint glaze tick) and a small wet slap. About 1.5 kHz where the old click was
  3.2 kHz, over ~100 ms.
- **Ladders** on free pools are moved onto the edge itself and turned to face straight into the water (by the shape's
  distance and its gradient: `PoolPlan.EdgeLadder`); they had faced along the pool. **Rails** (`Rail`) bend round each
  corner (up to 9 cm) and are swept as one smooth tube with untwisted rings and capped ends, where straight pieces were
  butted at the corners (cracks and pinches).
- **Foam** (PoolWater): as Crest and Sea of Thieves draw it, with Valve's flow maps for the motion: clumps (noise) and
  round bubbles at two sizes, carried by the water's flow (two blended phases), shown through a threshold set by the
  amount: thick foam a creamy, shaded solid, thinning into clumps, then single bubbles that pop, then nothing; the churned
  water under it a little milkier. Wading makes less of it (`foamGain` 5, was 12), splashes as much.
- **Ripple light**: the player's ripples no longer warp the caustic map (which stretched its web round the body); they
  focus the sun as lenses do (`PoolRippleFocus`: the surface's curvature times the depth by the refraction's share),
  bright under crests, dim under troughs.
- **Shadow**: with no body shown and Player shadow on, the real body (FirstPersonBody, the MakeHuman rig) is drawn into
  the shadows only, so it walks, crouches and reaches; the Poolrooms' capsules (`_PoolCapsA/B/N`, 12) follow its bones
  (head, torso, thighs, shins, upper arms, forearms) plus the camcorder, sharper than before (penumbra 1 cm + 2.2 cm a
  metre, was 5.5). The capsule stand-in (`PlayerShadow`) is left for crawling, when the body is hidden.
- **STP** worked in the editor but froze in the build: URP's `STPResourceStripper` drops STP's shaders when no URP asset
  in the build uses STP. `HdrGradingForBuild` now sets the assets to STP for the length of a build (as it already did
  for HDR grading) and puts them back; the game picks the upscaler itself at runtime.

## Second feedback pass (2026-11-01)

The owner: Sea of Thieves' water for inspiration; windows on both sides of one thin wall, both out into the sun ("ain't
possible"); the chrome rails' look and shape; "weird 90 degree angles" in the free pools; the shadow "disjointed and not
connected".
- **Windows are clerestories** (`PoolBiome.WindowSill/Top/Fits`, `PoolPlan.Windows`, PoolCommon.hlsl `PoolWindowSD`): only
  in a tall room's wall above the roof of a lower room next door (its ceiling + 0.35 m) up to 0.45 m under the taller
  ceiling, at least 0.75 m high, as basilicas and natatoriums are lit. From the lower room the window is above its
  ceiling: a plain wall. The sun's beams still come in through them (they are on the sun's side).
- **The 90 degree angles** were three things: the regular basins' edge (`BasinEdge`) also ran inside free pools' rooms,
  building a square lip of wall along the cell lines between pool and not-pool cells (now skipped there); the tile shader
  drew the parts of a free pool in cells whose middle is dry as dry (no wet darkening or caustics), in squares (now: a
  surface below the deck is in a pool); and lane lines, cut off cell by cell by the curves (free pools have none now).
  Their steps were straight boxes poking into the curved wall: now round "wedding cake" steps, discs round a point on the
  edge cut by the pool's outline (`FreePoolSteps`), with a handrail down their middle.
- **Rails and ladders**: bends of 15 cm (a pool rail's nominal 6 in), 14 sides, a round escutcheon wherever a rail goes
  down into the deck or floor; ladders as they are made: two 48 mm rails from escutcheons, over the coping in one round
  goose neck, straight down the wall 13 cm off it, black rubber bumpers to the wall and flat pale treads. Chrome's colour
  is a metal's F0 (0.6, slightly cool; it was 0.86 and read as white plastic) and PoolLit widens highlights where the
  normal turns fast across a pixel (specular anti-aliasing), so thin tubes do not sparkle.
- **The shadow** in the Poolrooms is the body itself: each frame its renderers are drawn, as the sun sees them, into a
  256-texel map over 2.4 m (`PlayerShadowMap.shader`, `PlayerShadow.DrawMap`, as Unreal's per-object shadows give a
  character its own map) and PoolCommon.hlsl tests it (`PoolBodySun`, soft as PCSS: the blockers' distance sets the
  width). The capsules remain only for the stand-in (crawling).
- **From Sea of Thieves** (Rare, SIGGRAPH 2018, "The Technical Art of Sea of Thieves"): lit crests (the sun through a
  ripple's thin crest comes out turquoise, most looking toward the sun; `_CrestColor`, `_CrestGain`), and the contact
  line (where walls, rails and legs cut the surface, found by comparing depths as Sea of Thieves foams round things in its
  water: a thin broken line of foam, `_ContactWidth`). Its FFT ocean waves, wave-peak foam and storm states do not fit
  still pool water.

## Realism: the plan (2026-11-01)

The owner: "plan out the next steps for realism for the poolrooms section". What makes it look real now: the traced sun
and its soft edges, caustics, glazed tiles with traced reflections, the ripple and shallow-water simulation, floating
things, wet prints, the body's shadow. What a real pool hall has that this does not, in order of how much it adds for
the work (references: indoor pool photographs, natatorium design codes, Rare's and Crest's water, Unreal's and
Frostbite's volumetrics, pool-industry notes on scale and stains):

**First: the biggest visible gains**
1. **Sun shafts in the air.** Pool halls are humid, so every beam from a roof opening shows. March the traced sun
   (`PoolSunVisibility`, exact to the walls and openings, no shadow maps needed) along each view ray at quarter
   resolution, jittered with blue noise, accumulated over frames and upsampled by depth (how froxel and raymarched
   volumetrics are made smooth in Unreal and Frostbite). Denser in a thin layer over the water (evaporation), dust motes
   glinting only inside the beams. Cost about 1 ms.
2. **Clouds passing the sun.** Real sunlight is never constant. A slow cloud-shadow field projected along the sun (as
   Unreal's cloud shadows and light functions do) dims every sun patch, beam, caustic and window together by 20-60 % for
   tens of seconds now and then, the sky in the openings greying with it. Cheap (one noise lookup in
   `PoolSunVisibility`), and eerie in an empty hall.
3. **Exact reflections in the water.** All the water of a storey lies at one height, so one planar reflection (a second
   render of the view mirrored in that plane, half resolution, only while water is on screen, distorted by the ripples'
   normals) is exact: rails, ladders, curved walls, columns, props, the bleachers. The cell-map trace cannot see the
   curved and small things; screen-space cannot see what is off screen. Planar water is what Half-Life: Alyx and most
   indoor-water games use. Cost about 1.5-2.5 ms; a Graphics menu switch.
4. **Weathering.** It is too clean. All procedural in PoolTile from the cell map and positions, no textures:
   - a white, rough crust of calcium scale 2-6 cm above the waterline (evaporation leaves it), heavier at shallow ends
     and round the return jets;
   - grout darkened under water and in shaded corners (biofilm), darker grout along the floor-wall joints;
   - rust tears running down from chrome fittings, escutcheons, drains and ladder anchors;
   - mineral streaks on the walls under roof openings and windows, where rain and condensation run;
   - worn, matte paths in the glossy deck glaze between doorways (from the plan's walking routes); damp, darker tiles at
     the foot of walls near water.

**Second: what real pools are built with**
5. **Pool fittings.** Rounded bullnose coping instead of square edges; rim-flow gutters with grates round the big pools,
   the water brimming over into them with a soft trickle; skimmer mouths and small chrome return jets in the pool walls
   (they also drive item 12); round underwater niche lights, most dead, a few faintly lit; depth markers and NO DIVING
   tiles on the coping and walls, reading the true depth, now and then a wrong one (a Backrooms tell); lane-rope hooks
   and starting blocks on the lap pools, a pace clock, vent grilles high on the walls.
6. **Glazed roof lights and glass block.** Some openings get frames and panes: the frame's bars draw thin shadows across
   the sun patch, a dusty pane softens and dims it (traced: a mullion pattern in the opening's shape). Glass-block walls
   (70s and 80s pools) glowing on the sun's side, by the same rule as the clerestories (only with outdoors behind).
7. **Warm bounce from the sun patches.** Each patch of sun on the floor is a known shape (the opening projected), so its
   light on the walls and ceiling near it can be computed exactly as an area light (Lambert's polygon formula, as
   Unity's and Unreal's area lights do), where now it is one value per cell. The warm glow up the wall beside a sun patch
   is in every photograph of a pool hall.

**Third: hearing it, and the camcorder**
8. **Acoustics.** Steam Audio still gives the Poolrooms drywall walls, carpet floors and ceiling tiles
   (`SteamAudioWorld`): give it glazed tile and water (absorbing almost nothing), so things in the world ring as they
   should; a reverb by room size aimed at real untreated-but-not-extreme halls (1.5-2.5 s); a short flutter echo between
   parallel tiled walls on sharp sounds (a dropped hammer, a step on tile); water lapping in the gutters (water plan 8),
   a filtration hum from the grates, drips under roof openings as single sounds in their places, not a loop.
9. **The lens fogs up.** Walking from Level 0 into the warm, wet Poolrooms, the camcorder's cold lens mists over and
   clears from the edges in about ten seconds, as real cameras do; again after surfacing (item 11).
10. **Exposure.** Check the camcorder's auto-exposure across shade and sun patches: a slow, slightly overshooting
   adjustment walking into a sunlit hall, sun patches clipping to white with a little halation, as consumer camcorders do.
11. **Under the surface** (water plan 7): Snell's window from below (the sky only in a circle overhead, the rest a mirror
   of the pool), green murk, beams in the water, muffled sound.

**Fourth: water that is looked after**
12. **Circulation.** Return jets push slow currents into the flow simulation and skimmers and gutters draw the surface,
   so foam, floating things and a faint dust film drift slowly toward the skimmers and collect at the edges.

Suggested order: 1, 4, 3, 5, 2, then the rest. 1-4 are where the pictures will change most; 5 and 6 make it read as a
real building; 8 and 9 are cheap and felt at once.

## Realism: built (2026-11-01)

The owner: "do it all please". All twelve, as follows (the not-done parts at the end).
1. **Sun shafts** (`PoolAir.cs`, `Hidden/Backrooms/PoolAir`): a render pass on the player's camera in the Poolrooms
   (before the camcorder's processing): the traced sun marched along each view ray at a quarter of the resolution (28
   steps, 28 m, jittered per pixel and frame), smoothed over frames (reprojected, clamped to the neighbourhood) and
   upsampled by depth. Density 0.003 /m plus 0.006 in a layer over the floor and water; Henyey-Greenstein 0.55. It
   ends at the water. The dust motes (`AirDust`) glint in the Poolrooms only inside the beams. At 0.018 the air washed
   the halls white; 0.003 is a faint glow where the sun crosses the air.
2. **Clouds** (`PoolCommon.hlsl` PoolCloudCover / PoolCloudShade, `PoolWorld.Clouds`): a deck of noise 300 m up
   drifting at 4.3 m/s; where the sun's ray crosses a cloud (about a fifth of the time, `cloudCover` 0.35) the sun is
   down to 28 % (`cloudShadow` 0.72) in every opening, window, beam and caustic together; the sky in the openings greys
   with them; the baked bounce dims with the sun round the player (`SunNow`).
3. **Planar reflections** (`PoolMirror.cs`): a second render of the view mirrored in the storey's water plane (oblique
   near plane on it, half resolution, HDR, no post-processing, culling inverted), read by PoolWater at the pixel's own
   screen position bent by the ripples; the water is left out of it (`_PoolMirrorPass`). Graphics > Water reflections
   (on); off: the trace and screen-space as before.
4. **Weathering** (PoolTile): scale crust 1-6 cm over a grime line at the waterline; biofilm in the joints under water;
   dirt in the joints at the foot of walls; puddles with chalky rims under the roof openings (straight below them) that
   mirror the room; streaks down the light wells and under the high windows' sills; the glaze worn matte in front of
   doorways. Rust (`Backrooms/PoolStain`, multiplied): tears under jets, lights and grilles, rings round the rails'
   anchors on the deck.
5. **Fittings** (`OfficeChunkBuilder.PoolFittings.cs`): a rounded bullnose along every pool edge (free pools' outline,
   the basins' deck edges); return jets (chrome flange, nozzle, dark bore) every ~3 m; skimmer mouths at the waterline;
   round underwater niche lights (a quarter lit, `Backrooms/PoolGlow`); depth markers and NO DIVING in 2.4 cm dark
   mosaic tiles on the deck beside the edge (the true depth; one in 23 wrong); vent grilles high on one wall cell in nine
   (never over a window); a pace clock in each lap pool's room. Jets, skimmers and grilles go to `PoolChunk.fittings`
   (`PoolChunk.All`) for the circulation and the sound. **Not built**: rim-flow gutters with grates, starting blocks
   (the lap pools have no deck at their ends), lane-rope hooks.
6. **Glazed roof lights and glass block** (`PoolGlazed`, `PoolMullions`, `PoolGlassBlock`): in about 45 % of 12 x 12
   cell blocks the openings (not round ones) are glazed: a frame of bars every 0.6 m and a dusty pane (82 %) at the top
   of the light well, drawn by the sky shader; the bars throw thin shadow lines across the sun patches and beams. About
   40 % of the clerestories are glass block (`Backrooms/PoolGlassBlock`: 19 cm blocks glowing with the daylight,
   rippled); the sun through them is dim and even (30 %).
7. **Bounce from the sun patches** (`PoolPatchBounce`, PoolTile `_PatchBounce` 0.8, PoolLit): each patch of sun under an
   opening (the part the sun gets through both ends of the light well, moved down along the sun to the floor) lights the
   walls and ceilings near it as a lambertian area light (Lambert's polygon formula), if nothing stands between (a cell
   walk) and the patch is really sunlit; up to two patches a point, within 7 m.
8. **Sound**: Steam Audio's Poolrooms meshes are glazed tile (`SteamAudioWorld.Tile`); a flutter echo on the listener
   in the Poolrooms (`AudioEchoFilter`, a round trip across the room, 32 % decay, 9 % wet); `PoolSounds`: drops from the
   roof openings every 4-11 s (`ProceduralSounds.Plink` into water with a ring, a tick on the deck), laps at the nearest
   pool wall a moment after you wade (`Lap`, at shallow water's speed), the filtration's hum and running water at the
   nearest skimmer or grille (`FilterHum`). The room reverb already scaled with the room (1.4-2.6 s).
9. **Lens fog** (`Camcorder/LensFog.cs`, CamcorderPipeline integrate pass `_CamLensFog`): coming into the Poolrooms the
   lens mists over (patchy, a soft wide blur, greyed) and clears from the edges inward over 11 s; a little after coming
   up from under the water.
10. **Exposure**: checked, unchanged: the camcorder's auto-exposure (CamcorderAutoExposure) already adapts with a lag
   and clips sun patches.
11. **Under the water** (`PoolWorld.CameraUnderwater`, `_PoolUnderwater`): the water is drawn from below (Snell's
   window: the room through the surface in a cone overhead, a mirror of the pool outside it); the air pass multiplies
   what is seen by the water's transmission over the path and adds its green scattered light, and its beams become the
   water's own; sound through a 600 Hz low-pass; coming up leaves drops on the lens.
12. **Circulation** (`PoolCirculation.cs`, PoolRipples.compute `_Jets` / `_Sinks`): the nearest 16 jets push plumes
   (about 0.4 m/s at the nozzle in the simulation's flow) and 8 skimmers draw the surface in, so foam is carried round;
   floating things drift with the analytic current (`CurrentAt`) in their water drag (PoolBodies).
- Cost (editor, a pool hall): GPU 7.75 ms with all of it, 7.44 ms with the shafts and the mirror off.

### Feedback on the realism pass (2026-11-01)

- "Ceiling tile glitches" in a gallery hall's big roof opening: the 1.2 m light well of a 7.2 m hall reached 8.4 m, past
  the bottoms of the deep pools of the storey above (from about 8.0 m), whose steps, lips and floors then hung in it.
  Under another storey a well now stops 1.15 m below that storey's floor (`PoolBiome.Shaft`, PoolCommon `PoolShaft`,
  the same in the C# sun). The roof lights' frame lines are anti-aliased wider (they read as dotted far off).
- "A humming noise thats loud and annoying, and weird drips that are too loud": the filtration hum is removed (with so
  many skimmers and grilles one was always in reach); drips a third as loud, half as often (9-24 s), heard over 10 m,
  and softer (`Plink` lower, low-passed); laps 0.2.
- Reflections that "change oddly when I move closer or further, in brightness": not reproduced as a fault. Planar and
  traced reflections match; the mirror is HDR (up to 3.1 in a sunlit hall) and up to date (the camera moves in Update,
  the mirror renders in LateUpdate). What changes with distance is the water's Fresnel (at a few metres you see the
  floor, far off the room mirrored) and, in the haze, the columns' shadows over the water. The haze's frame smoothing
  over water now reprojects by the water's surface (it used the pool floor under it and smeared as you moved) and the
  humid layer is thinner (0.0025 /m).
- The "super weird corner water thing" in free pools: the round steps' top step lay 3 cm under the surface, where the
  contact line drew a bright bar over it. The steps now take as many risers as the depth wants at about 24 cm (one
  step in a 45 cm shallow end, 10 cm under), and the contact line is drawn only against things that stand up through
  the water (the scene's normal from the depth must be near horizontal).
- Rust "cuts off oddly": the stain's seed came from 33 cm blocks of its world position, so it jumped at their edges; each
  stain now carries its own seed in its UVs, and a tear is a few soft trickles that fade out well before the quad ends.
- The pace clocks are gone.
- The slide ("just doesnt look good at all"), rebuilt: a glossy gelcoat flume with a 3.5 cm wall and rolled rims, open
  about 160 degrees, from a railed white platform on the wall (two steel legs into the water, a ladder up from the
  water) curling down to a level run-out 28 cm over the water, on steel posts with cradles.

## Slides (2026-11-01)

The owner: "the slides should be their own biome inside the pool rooms zone so just remove it for now until we work on
that". Slides are still planned (`PoolPlan.Features.Slide`, so worlds keep their random draws) but not built;
`OfficeChunkBuilder.Slide` (the rebuilt flume, platform and ladder) is kept for that biome.

## The water park and the channels (2026-11-01, built)

The owner: "go ahead on the new biome as well, which will basically be indoor water park, slides, mushroom water things
with water going down them etc". Two new biomes of the Poolrooms zone (`ZoneRules.Poolrooms`, `Zones/Poolrooms.asset`):
**Channels** where the zone's density is under 0.28, **Poolrooms** to 0.64, **WaterPark** above. `WorldGenSettings
.AddMissingBiomes` now also fills in pinned zones' bands.

**The water park** (`BiomeRules.WaterPark`: bigger, taller halls 4.8-7.2 m, more dry decks, fewer columns, skylights
almost everywhere; `PoolPlan.WaterPark`, rooms tagged `Park`). Laid out as indoor water parks are (an attraction to a room):
- **Slide towers** over a plunge pool: four steel legs, a railed platform (2.4-3.8 m), a steep stair up the back with
  rails and stringers, and a gelcoat flume (red, yellow, blue or white) swinging out to one side and down into the pool
  with water running down its bottom and posts with cradles under it (`OfficeChunkBuilder.SlideTower`, `Flume`).
- **A lazy river**: free pool shape 3 (a ring round a rounded island, 2.2 m wide, 0.85 m deep), its return jets all
  pushing one way round (fitting kind 3: 2.5 times a jet's push, longer plumes, so the current is about 0.3-0.5 m/s, as
  real lazy rivers run); the island gets its bullnose too.
- **A wave pool**: a beach entry (14 cm) down to 1.7 m; three grated wave chambers low in the deep end's wall (kind 4).
  Sessions as real ones run: a wave every ~4 s for 40 s, then 30 s of calm; each wave lifts a line of water at the
  chambers that runs down the pool, with a surge sound (`PoolFalls.Waves`, `ProceduralSounds.WaveSurge`).
- **Kiddie pools** (28 cm, no steps or ladders) each round a **mushroom fountain**: a white stem, a domed cap (red with
  white spots, yellow, blue or white, after the fibreglass mushrooms parks buy, about 1.5-2.5 m across) with a rolled rim,
  and a curtain of water off the whole rim, thrown out at 0.45 m/s and falling under gravity to the water (kind 5).
- In flooded park halls: mushrooms standing in the water and **water arches**: chrome nozzles low in the walls throwing
  a stream 1.8-3 m out into the pool (kind 6).
- Falling water is `Backrooms/PoolSheet`: uv.y is the metres fallen and the pattern is keyed to when the water left the
  lip (now minus sqrt(2y/g)), so it speeds up as it falls; clear and glassy at the lip (the room refracted through it),
  breaking into ropes and drops lower down, whiter where air is in it, with Fresnel and sun glints (the usual waterfall
  approach in games: UVs along the fall and a scrolling breakup mask).
- `PoolFalls` (on PoolWorld): where curtains and streams land the ripple simulation is kept stirred and spray thrown;
  the nearest two are heard through Steam Audio, quietly (`ProceduralSounds.WaterCurtain`: patter, churn and Minnaert
  bubbles; 0.07, 12 m).
- Steps are left out of a free pool whose shallow end is under 30 cm (beaches, kiddie pools).

**The channels** (`BiomeRules.Channels`: narrow corridors, many loops, few big halls; `PoolPlan.Channels`): every corridor
is a vaulted channel, flooded, with a walkway on one side; halls are flooded but for a one-cell walkway meandering from one
end to the other (a sideways step of one or two cells every 2-4 cells). Walkways with water on both sides get their
bullnose only (no gutter grates, starting blocks or lettering crowding them).

**Touching the water** (`PropGrabber.Water`): crouched or lying at an edge, a click on the water within reach puts the
free hand in it: the palm goes down through the surface, strokes out and back (a bow wave ahead of it, a hollow behind,
a soft lap), and comes out dripping for 3.5 s (drops plinking into the pool). Works with no body drawn too (the water
still answers).

Checked in World 3: districts round the Poolrooms generate all of it (a slide tower, kiddie pools with mushrooms, a wave
pool, a lazy river with its current measured at 0.27 m/s at the outer wall, arches, channel halls); pictures of each; the
water touch's whole cycle; no errors or warnings. Not checked: the sounds by ear, the waves' look in motion.

## Owner's feedback on the water park and channels (2026-11-01, built)

- The water read as a flat beige sheet with triangular holes ("these pools are super glitched"). The cause: the ripple
  solver (PoolRipples.compute) was unstable in deep water. Its Courant cap of 0.45 is under the plain wave equation's 0.5
  but over this scheme's limit with its viscosity term, (2 + 2 damping - 16 viscosity) / 8, about 0.43, so in water 1.4 m
  and deeper (slide plunge pools, deep ends) the finest ripples grew until the surface was all foam and tilt, a smeared
  mirror; the "holes" were the shallow steps, still clear. Now capped at 0.36; the solver also drops NaNs and runaway
  heights, the shaders clamp ripple slopes, and falling water stirs the surface volume-neutrally (a dip and a ring), as do
  the wave pool's crests (each with its trough). The water is clearer too (absorption 1.25 / 0.36 / 0.5 per metre). The
  planar mirror no longer leans on GL.invertCulling (an x flip in its projection keeps the winding; PoolWater reads 1 - u),
  and NaN reflections from the cell-map trace are caught.
- A pale slab beside a cover reel: a corner behind a round hall's curved wall, left without a floor ("solid"), could be
  seen from a way in. Round halls now have floor and ceiling everywhere (a nook behind the curve where it can be reached),
  and need their openings within 0.6 R of the middle.
- Slide towers rebuilt (OfficeChunkBuilder.WaterPark): painted steel columns on base plates, X bracing, a grating deck on a
  beam frame, 1.1 m guards with bars and toe boards, a grab bar over the start, a steel stair with stringers, grating
  treads and handrails; the flume's start flush with the deck and closed behind, flanged sections, and supports dropped
  from each joint's lowest point in the flume's own frame (they had come up inside the channel). The flume's end pours.
- Where falling water lands: a crown of churned water and a ring of foam (PoolSheet's uv.y < 0 modes); fingers and a
  bright lip on the sheets. Standing inside a curtain the world is muffled (PoolSounds) and the curtain heard all round;
  walking through one washes the lens (LensDrops.Wash, 16 drops now).
- The falls' sound goes round walls like the building's hum (AcousticField via PoolFalls.Route: doorway direction at the
  walking distance, duller; or faint through the wall), over 30 m, louder close, in the room's reverb.
- Lane ropes (PoolLaneRopes): tied to eye bolts on the end walls, simulated as Verlet chains floating on the ripple heights
  (PoolRipples.HeightAt, copied back from the GPU), swayed by waves, given boxes near the player so they stop you (duck
  under or climb over); red within 5 m of the walls (a fifth of a short pool). Only ropes tied at both ends to a wall or
  the pool's edge are drawn: none where a lap area opens into more water.
- Channel halls: flooded wall to wall with a curving 1.2 m walkway between the doorways (and branches to the others)
  instead of cell steps; no gutters, blocks or lettering anywhere in the channels.
- Doorways round-topped in the Poolrooms (archedDoors 0.85, apex on the 2.7 m header).
- Floor drains under roof openings (one cell in three) and now and then elsewhere, with water standing round them;
  puddles shaped by the floor rather than the opening's outline, a damp margin, drops ringing in them under open sky.
- The water touch is a quick tap now (about 0.6 s, a second of drips).
- F5 > PLACES: a dropdown per zone (Level 0, the Poolrooms), groups inside; 22 kinds of Poolrooms places, standing on
  dry floor in the same room with the place in sight.

Checked in World 3 with the owner's seed (288152760): the slide pool from the first picture clear (ripple heights under
3 cm, no NaNs); the tower; a mushroom's foam ring; inside a curtain the listener muffled to 1.3 kHz, walking out a lens
wash; three fall voices routed (duller round walls); lane ropes floating and swaying a few cm on a test wave; a channel
hall's walkway and its branches; arched doorways; a deck drain. Not checked: the sounds by ear; pushing into a rope in
person; the F5 page's look.

## Review and next improvements (2026-11-01)

The owner: "look at everything you've made and make a plan for improvements". A look back over everything in this
document, from what was seen in World 3 while building it, what the owner reported, and what was never checked. In the
order to do them.

**Status (2026-11-01, later):** built: 1 (skimmers and jets thinned), 3 (faster exposure in the Poolrooms), 4 (the
ripples bend far reflections more; the haze is not yet in the mirror's view), 5 (the mirror skips frames with no water
near the view, quarter size when it is far), 7 (drifting density in the haze, beams into the water), 8 (absorption, a
dust film at the edges, jets boiling), 9 (frame bars, dirty and broken panes), 10 (beams under the water, breath
bubbles), 11 (domain-warped clouds, overcast districts), 12 (repair patches, chips, efflorescence, grout by room), 13
(gutter grates, starting blocks, lane ropes, rules boards, benches, cover reels), 14 (the water park, above), 15 (the
channels, above), 16 (touching the water, above). Left: 2 (the bounce still not compared by eye; the C# sun now knows
glazing and glass block), 6 (the flutter echo only made quieter, not moved to one-shots), 17 (not yet, as asked).

**1. Loose ends (things built but not right or not checked)**
1. **Too many skimmers.** 619 in the loaded area (one wall cell in eleven of every basin, plus free pools every 9 m).
   Real pools have one per 37 m² of water (Maricopa code) set along one side: thin them to that, and jets to one every
   3-4 m on two sides only, so the walls are not peppered with fittings.
2. **The sun-patch bounce was never seen working.** The two attempts to compare it on and off found no patch in view.
   Check it on a wall beside a patch, tune `_PatchBounce`, and lower the baked bounce under it if the two add up too
   bright. Also make the C# sun (`PoolSun`, which bakes the bounce and answers `SunAt`) know the glazing, glass block
   and clouds the shaders know, or the baked bounce under a glazed roof is brighter than its patch.
3. **Exposure in bright halls.** After coming into a sunlit hall the picture is blown out for a few seconds (every
   screenshot after a teleport). The camcorder's auto-exposure is tuned for Level 0's dim rooms: give the Poolrooms a
   faster adaptation and a softer highlight roll-off, as consumer camcorders do in daylight.
4. **The reflection complaint.** Not reproduced as a fault (it read as the water's Fresnel and the haze's shadows). Ask
   for a short video. Meanwhile: bend the planar reflection by the ripples more for far things than near ones (it is a
   fixed amount now), and put the haze into the mirror's view so the reflection and the room agree.
5. **Mirror cost.** The mirror renders every frame in the Poolrooms even with no water on screen. Skip it when no
   water cell is in view (a frustum test against the loaded basins), and drop to a quarter size when the water is far.
6. **Sounds not listened to.** Drips, laps, the flutter echo and the underwater muffle were only measured. The flutter
   echo is on the listener, so it repeats the ambience too: move it to sharp sounds only (one-shots through WorldSound).

**2. The look (biggest visible gains)**
7. **Sun shafts with structure.** In big openings they are an even veil. Real beams show drifting density: add a slowly
   moving 3D noise to the haze's density (as Unreal's and Frostbite's volumetric fog do), stronger beams in darker halls,
   and the beams in the water seen from above (the caustic map gives them their ripple).
8. **The water's colour.** The bright floor dominates and reflections only show at grazing angles. Real pool water darkens
   and greens with depth faster than the tiles suggest, and a faint film of dust and hair gathers at the edges. Tune the
   absorption by depth, add the film (carried by the circulation to the skimmers), and a visible boil and ring of
   bubbles where each return jet pushes water.
9. **Glazed roof lights with depth.** The frames are painted in the sky; build the mullions as geometry with depth and
   shadows in the well, dust and streaks on the panes, now and then a broken pane (a Backrooms tell).
10. **Under the water.** The murk is flat: add the sunlit water's own caustic light shafts, a slow wobble, specks
    drifting, the player's breath bubbles.
11. **Clouds.** Value noise reads as soft blobs in the openings: a domain-warped cloud noise with crisper edges, and
    days that differ (some districts overcast, the sun never out).
12. **More wear variety.** Repaired patches of mismatched tiles (in every real pool), chipped tile edges at the coping,
    white efflorescence below windows and leaks, grout colour by room.

**3. What real pools are built with (still missing)**
13. Rim-flow gutters with grates on the big pools (water level with the deck, brimming over); starting blocks on the
    lap pools (they need a deck at one end); lane ropes that float and move with the circulation; rules boards; benches;
    shower heads and drains in the changing areas; hose reels and rolled pool covers.

**4. New biomes in the Poolrooms zone**
14. **The water park** (the owner's idea): slides of several kinds (body flumes, tube slides, a kids' slide), splash
    pools, a wave pool, lazy-river channels, built as their own biome with their own rooms. The rebuilt slide is the start.
15. **Winding walkways beside water channels** (reference image 5, from the known gaps).

**5. Water interaction (left from the 2026-10-31 plan)**
16. Crouch at the edge and touch the water (hand ripples, a wet arm).
17. The water as a horror tell: ripples with nothing making them, wading that is not yours, the water stilling at once.

## Known gaps / ideas

- (Done 2026-11-01: the view under the water.) Crawling inside a basin puts the camera under the water surface, which was single-sided (nothing drawn from below).
- The duct of the Poolrooms' hole is shaded by the Backrooms' light grid, so its mouth is dark.
- The sun is not traced through the curved walls (they cast no sun shadow) or between the two floors (the stair towers
  are lit by the bounce only).
