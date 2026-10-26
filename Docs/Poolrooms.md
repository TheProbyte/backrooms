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
- The zone's fog is swapped in per camera (the player's, or a portal camera looking into the Poolrooms).

## Water

- `Backrooms/PoolWater`: transparent queue, reads the opaque and depth copies (main camera only; `_PoolScreenTextures`):
  refraction, colour by the thickness of water looked through, screen-space reflection with the ceiling or the sky
  straight above as the fallback, the sun's glint.
- Tiles below the waterline are darker, with caustics where the sun is.
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
and fades in a water bed (`ProceduralSounds.PoolAmbience`: distant lapping, a wash of water, occasional drips). The
listener's reverb takes a tiled-hall profile: 2.4-5.5 s decay depending on the visible volume, bright (HF ratio 0.95),
strong early reflections and tail.

## Things in the Poolrooms

The arm, body, items and furniture normally use Backrooms/Lit, which has no light there. `PoolLitSwitch` (on the arm,
body, items and every movable prop) swaps their materials for Backrooms/PoolLit copies (same colours and textures, the
Poolrooms' sun and bounce) while they are in the Poolrooms. Backrooms/Lit itself is untouched: adding the Poolrooms'
light to it made the editor recompile all its variants and crash the D3D12 device twice (see WORKLOG).

## Known gaps / ideas

- Crawling inside a basin puts the camera under the water surface, which is single-sided (nothing drawn from below).
- The duct of the Poolrooms' hole is shaded by the Backrooms' light grid, so its mouth is dark.
- More biomes: winding walkways beside water channels (reference image 5).
- The sun is not traced through the curved walls (they cast no sun shadow) or between the two floors (the stair towers
  are lit by the bounce only).
