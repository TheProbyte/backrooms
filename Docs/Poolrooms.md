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
and fades in a water bed (`ProceduralSounds.PoolAmbience`). The listener's reverb takes a tiled-hall profile: 2.4-5.5 s
decay depending on the visible volume, bright (HF ratio 0.95), strong early reflections and tail.
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
- **Running water** (`Spout`, `Cascade`): a few dark slots in pool walls pouring a sheet of water into the pool, and now
  and then a wall-long flight of steps with water pouring onto it from a long slot and running down in a film
  (`Backrooms/PoolFall`). The only place you hear running water (`ProceduralSounds.RunningWater`: bubbles over a
  fluttering splash), with spray and a stir of the water where it lands (`PoolRunningWater`).
- **Rooms with a purpose**: small deck rooms become changing rooms (tiled stall partitions along a wall, a tiled bench) or
  showers (chrome shower heads, a drain, dripping). A slide (a blue half pipe from a wall, curling down to just over
  the water) in some big pools.
- Cost: 1920 x 1080 in the editor, a free pool hall with bleachers: GPU ~5.4 ms.

## Known gaps / ideas

- Crawling inside a basin puts the camera under the water surface, which is single-sided (nothing drawn from below).
- The duct of the Poolrooms' hole is shaded by the Backrooms' light grid, so its mouth is dark.
- More biomes: winding walkways beside water channels (reference image 5).
- The sun is not traced through the curved walls (they cast no sun shadow) or between the two floors (the stair towers
  are lit by the bounce only).
