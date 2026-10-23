# World generation and lighting: how it works (world version 6)

Everything is built from **(seed, settings, game version)** by fixed rules. Every layer depends only on the
layers above it, never on what was generated before, so any part of the world can be generated in any
order, on any thread, and comes out the same. The world code `seed-settingshash-v4` (F5 > World Gen) pins it.

**Grid.** 1.2 m cells (one wall panel, two ceiling tiles). Walls sit on cell edges, posts on cell corners.

## The layers

| Layer | Unit | Decides | Code |
| --- | --- | --- | --- |
| **Zone** | Voronoi region, ~1.2 km | The "level": biome table, edge biome, fog/ambience/power, storeys, which zones it may touch | `WorldGenSettings.ZoneAt`, `ZoneRules`, `ZoneDefinition` |
| **Biome** | per chunk (density field) | Room rules: splitting, hallways, ceilings, lights, dressing (~30 numbers) | `WorldGenSettings.Evaluate`, `BiomeRules`, `BiomeDefinition` |
| **Landmark** | sparse grid (3 x 3 districts) | Rare set pieces, reserved before rooms | `Landmarks`, `LandmarkRules`, `LandmarkDefinition` |
| **District** | 4 x 4 chunks = 48 x 48 cells, per storey | Rooms, hallways, doors, open plan, light zones and colours, tall spaces, arches, dressing | `DistrictGenerator` |
| **Chunk** | 12 x 12 cells = 14.4 m, (x, storey, z) | Nothing: a slice of the district plan, meshed and streamed | `WorldGenerator.Slice`, `OfficeChunkBuilder`, `WorldStreamer` |

`WorldGenerator` is the single entry point (`Chunk`, `District`, `Sample`, `LandmarksIn`, `TopStoreyAt`); the
streamer, audio, NavProbe, tests and the World Map all read layouts through it.

### Zones
Zone sites sit on a jittered grid (`zoneSize`); each site picks a zone by weight. Sites around the origin
always pick the first zone (spawn is always Level 0). A district takes the zone of its nearest site. Within
`zoneEdge` metres of a boundary a district is a **transition district**: it uses the zone's `edgeBiome`.
Where two zones that may not touch meet (`neighbours`), the edge band becomes the `transitionZone`.
Only Level 0 exists today (weight 1, biomes Hallways / Offices / Warehouse).

### Biomes
Unchanged from version 3: fractal value noise with domain warp per chunk, split into the zone's ordered
bands (`upTo`), with rules interpolated over `transition` so areas change gradually. `maxRoomCells` (new)
is the largest room side before a district split is forced (12 for offices, 20 for warehouse halls).

### Landmarks
One landmark at most per landmark cell (`landmarkSpacing` x spacing districts): the cell's dice pick a
district, a landmark allowed by its zone and biome, a chance roll, and a position (3 cells clear of the
district edge, clear of the spawn hall). Both storeys of a district find the same landmark, so
multi-storey landmarks agree between storeys. Two exist:
- **Colonnade hall** (test landmark): tall painted hall, two rows of columns, arched doorways mid-side.
- **Atrium** (the multi-storey test volume): a two-cell arcade ring around a lightwell. Storey 0's well
  rises to storey 1's ceiling (cells flagged `OpenAbove`); storey 1 has a railed balcony and a void
  (`NoFloor`). The well's dome lights hang at storey 1's ceiling and belong to storey 0.

### Districts
1. **Edge contracts**: each chunk-length segment of a district edge gets 1-3 openings (some 2 wide, some
   headed) from a hash of that segment. Both districts compute the same list; this is the only thing
   neighbours agree on. (These keys are the old chunk-border keys.)
2. **Reservations**: landmarks on this storey, and the spawn hall (chunk (0, 0) is one big room).
3. **Partition**: guillotine cuts around reservations, then binary splits. Regions longer than
   `maxRoomCells` always split (often with a long hallway along the cut); a region that first fits may
   stay one big hall (`bigHallChance`), smaller ones stop by size as before.
4. Room kinds, ceilings, finishes; walls; crawl mouths; a random spanning tree of doors plus loops and
   open plan; landmarks open their insides and force entrances; flood-fill repair; headers where
   ceilings change; arches (decided once per edge).
5. Lights, fixtures, pillars, half walls, props, outlets; landmark dressing.
6. **Light colours** (rendering layers, used by room-layer lighting): zones near the district edge take
   one of 7 colours from the district's parity set (4 sets, so neighbouring districts never share one);
   zones deeper than any light's reach use any of the 28.

### Warehouse dressing (tall halls)
Storage racks are steel pallet racking: slotted blue uprights (`RackUpright` texture), orange box beams, deck
boards, end bracing and base plates, loaded shelf by shelf with matching or mixed cartons (some stacked, some
gaps). Wooden pallets with stacked cartons stand against the walls (`PlacePallets`, new in world version 6).
Code: `OfficeChunkBuilder.Warehouse.cs`, `CardboardBoxGeometry.cs`. Racks, boxes and pallets cast shadows through
a few proxy boxes, not their detailed meshes.

### Chunks and storeys
A chunk is cut from its district: clipped room rectangles with the district room id (`globalId`),
edges, arch flags, cell flags, and every fixture/prop/pillar/plate whose anchor lies in it. The builder
reads the neighbouring chunks for border openings and corner posts, so a room spanning a chunk border
has no seam. Chunk coordinates are `Vector3Int (x, storey, z)`; storeys are `storeyPitch` (9 m) apart.
Storey 0 streams everywhere; upper storeys stream only within a chunk of a multi-storey volume.
`WorldStreamer.ChunkLoaded / ChunkUnloaded` fire per chunk (the hook for Steam Audio geometry later).

## Lighting (light grid mode, the default)

- The nearest fixtures (F5 "Shadow-casting lights", capped at ~38 to fit the 4096 shadow atlas), faulty
  tubes within 36 m (up to 10, so their flicker is exact) and the 2 nearest lamps are **real URP lights
  with live shadows**. They light every rendering layer: their shadows (closed proxy boxes) stop them at walls.
- Every other powered fixture within 40 m lights the **light grid** (`LightGrid`, `Resources/LightGrid.compute`):
  a 128 x 24 x 128 grid of 0.6 m texels around the player (76.8 x 14.4 x 76.8 m), addressed toroidally, so
  moving only recomputes the newly exposed slabs. Direct light uses URP's exact falloff and spot curves,
  blocked by walls and headers (line of sight across cell edges, checked against each opening's top) and
  by floors except through wells. A 2D **bounce** field (floor light x albedo, spread through open floor
  and weakly through doors) lights ceilings and upper walls; it replaces the bounce and doorway lights.
- **Handover**: a fixture's real light carries share w and the grid 1 - w; w eases over 0.6 s when it
  joins or leaves the nearest set, so light is never counted twice and nothing pops. Only the grid around
  a fixture whose share or power changed is recomputed (`LightGrid.MarkDirty`).
- **Sampling** (`Shaders/LightGridSample.hlsl`, used by `Backrooms/Lit`): the point is pushed 0.3 m off
  the surface, then clamped half a texel from any solid edge of its cell (and from floor/ceiling), so
  trilinear filtering never blends texels from the other side of a wall. Ceiling fittings don't light
  their own ceiling plane directly; ceilings get bounce.
- **Walls block real lights in the shader**: each real light is tested for line of sight through the layout
  (`LGrid_LightVisible`, the same cell-edge test the grid uses), so it never reaches through a wall, door
  header or floor. Walls, corner posts and headers therefore cast no shadow-map shadows in grid mode (their
  proxies are switched off, `OfficeChunkBuilder.WallShadows`): coarse shadow maps of wide spot lights made
  stepped bands on walls and slivers of light in corners. Pillars, racks, lamps, half walls and furniture
  still cast shadows.
- `Backrooms/Lit` is a copy of URP 17.4 Lit with two lines added (see the shader header). All world
  materials use it (`InfiniteWorldBuilder.UseProjectLitShader`). Its forward pass also carries a copy of URP's `UniversalFragmentPBR` with the wall test in the
  additional-light loop (marked `Backrooms:`).
- **Bounce stays in its room**: the 2D bounce field blends only across open floor (doors, arches and walls pass
  nothing, in the compute blur and in the shader's smooth sampling), because even a few percent of a lit
  room's bounce showed as light in a dark room next door once auto-exposure lifted it.
- **Globals are re-bound every frame** (`LightGrid.Tick`): the occupancy buffer and grid textures the shader
  reads are set again each frame. If they ever point at the empty stand-ins from `LightGridGlobals` the shader
  sees no walls and every wall-aware test passes silently (this was the cause of light "through walls" in
  the editor: the editor's stand-in binder could land after the grid had bound the real data). The editor
  binder no longer runs while playing. Debug tip: a quick check is to tint pixels by whether
  `LGrid_Occ` of their own cell is non-zero.
- **Opening tops are 7 bits** (tenths of a metre, up to 12.7 m; `LightGridMath.PackTop`, `LG_WestTop`). They used
  to be 6 bits (6.3 m max), so in rooms taller than that and in the atrium well (11.7 m) every line of sight
  crossing a cell edge above 6.3 m counted as blocked: tall walls lit in striped blocks, hard-edged rectangles
  on the well floor (real lights are tested with the same rule) and weak light on the upper walls of tall
  halls. Tests: `OpeningTopsAboveSixMetresSurvivePacking`, `LightsHungHighInAnAtriumWellReachItsFloor`.
- **Room-layer mode** (F5 > Graphics > Light grid off) is the phase-2 system, kept as a fallback: a large
  pool of unshadowed lights limited to their room's layer, plus doorway and bounce lights.

## How to add things

- **A biome**: Assets > Create > Backrooms > World Gen > Biome, set its rules (the name matters), and add
  it to a zone's biome list with an `upTo` density. Order matters: bands blend into their neighbours.
- **A zone**: create a Zone asset, give it biomes, an edge biome, weight > 0, look/sound/power and storeys,
  and add it to `Resources/WorldGen/World.asset`'s zone list. Use `neighbours` + `transitionZone` to keep
  incompatible zones apart. Nothing else changes (the F5 tab edits the first zone).
- **A landmark**: add a `LandmarkKind`, its rooms per storey in `Landmarks.Rooms`, optional internal edges
  and forced doors in `Landmarks.Connect`, dressing in `Landmarks.Decorate`; then create a Landmark asset
  (size, chance, weight, allowed zones/biomes, storeys) and add it to the World asset.
- Changing anything that alters output: bump `WorldGenSettings.Version`.

## Debugging

- **Backrooms > World Map**: top-down zones, biomes, districts, rooms (landmarks pink, wells cyan), light
  layers; "Run checks" draws border mismatches, unreachable cells and light leaks. Follows the player in play mode.
- **Edit-mode tests** (`Scripts/Editor/Tests`): order independence, district/chunk borders agree,
  reachability, light leaks, zones, landmarks, storeys, slices, light-grid occlusion.
- **LightingTour** (`Debug/LightingTour.cs`): fixed seed, saved views (`Docs/screenshots/tour_views_*.json`),
  `compareLightingModes` captures each view in both modes with frame timings.
- **NavProbe**: walks long paths and reports walk-throughs and stalls.
