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
  (`NoFloor`). The well's dome lights hang at storey 1's ceiling and belong to storey 0. The atrium has a
  **wing** (below) so the upper storey can be reached.

#### Stairs and elevator (the atrium wing)
`AtriumAnnex` (one file; see its header for the local frame) describes a strip 5 cells deep along one side of
the atrium core, as long as the stairs need (the core grows to at least 16 cells on that side). The placement
rolls which connector(s): stairs (40 %), elevator (40 %), both (20 %), and which side (`LandmarkPlacement.core`,
`.annex`, `.side`, `.connectors`; `.rect` is the whole reserved area). Rooms of the wing come first in the
landmark's room list, the ring and well are the last five.
- **Band S** (2 deep): the stair hall (a `Hallway` with role `StairHall`, `openAbove`, ceiling = storey pitch +
  2.7, no random doors) and the closet under the landing; upstairs a floorless `StairVoid` and the landing.
  Without stairs: one filler office.
- **Band N** (3 deep): the elevator lobby and the shaft (2 x 3 cells, role `ShaftGround` / `ShaftUpper`, a hole
  through both storeys, door in the middle of the lobby side), or one filler room. Doors to the atrium's ring
  at u = 3 and 9; the stair hall's door from the lobby at u = 0; the top step opens onto the landing, which
  has a door to the lobby.
- `Room.noDoors` keeps random doors out of stair halls and shafts (`ConnectRooms`, `EnsureConnected`).
- **Stairs** (`OfficeChunkBuilder.Annex.cs`): 49 steps of about 0.18 m rise and 0.28 m tread after a flat
  1.8 m entry, built per cell so each chunk makes its own part; visual boxes plus one collider per step;
  handrails on both walls; the hall's ceiling slopes with the stairs and meets the upper storey's ceiling at
  the top (headroom always above 2.4 m). Hall lights are strip battens hung from the slope.
  `EdgeFaces` now also builds Wall faces for `noFloor` rooms (a void with walls, not a railed well).
- **Elevator**: `ElevatorSystem` (Interaction/) is built at runtime in the chunk that owns the shaft: cab,
  hoistway doors on both floors, call buttons, a button in the cab, a cab light. Smooth-stepped travel
  (9 s), doors 1.4 s; stepping in and staying 1.8 s sends it to the other floor; doors never close on a
  person in the doorway; the cab's floor survives chunk unloads. The player rides it through
  `CamcorderPlayerController.platformMotion`.
- World version 7. Tests: `AtriumAnnexTests` (every atrium has a way up, flood fills inside the landmark;
  stair numbers).

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

### Wear and neglect (2026-10-27)

`Generation/OfficeChunkBuilder.Wear.cs`. Everything is decided by the world seed and the cell (the same each visit,
the same from either chunk), and costs almost nothing: a few hundred triangles a chunk, one cheap overlay pass.

- **Ceiling tiles**: of the two whole tiles in a cell's middle row (tiles run 0.3 m off the cell edges in z), now and
  then one is stained (about 3.5 %, more where a slow noise says the building leaks), yellowed (3 %), sagging (1-3 cm,
  often stained), pushed up at one edge (1.2 %), slid 2-4 cm off its grid (1 %), or missing (0.5 %; then 60 % of the
  time it lies on the floor below, if no furniture is there). A lifted, slid or missing tile shows the space above
  (`BuildPlenums`): built only round the gaps (gaps of one room within 2.4 m share one), about 2.4 m across, clipped to
  the room, 60 cm deep: the fibrous backs of the tiles round the gap, hanger wires to the grid's main runners every
  1.2 m, old joists every 40 cm under the boards of the floor above, dusty fibreglass in some bays (lumpy, some sagging),
  often a pipe or conduit across the joists, sometimes a sagging foil duct, a cable or two; dark walls close it past
  where it can be seen. All world-aligned (two gaps look into the same structure). The materials are the broken walls'
  (`DemolitionMaterials`) aged and dimmed to a little over half, because the world's light reaches up there as it does
  the room: through a gap it reads as dark and dim, the shapes just showing (measured: about a third of the tiles'
  brightness on screen). Never in a cell with a dome or batten, nor in a square panel's own tile, nor
  under a crawl space. The stained tiles use two copies of the ceiling material whose texture (4 x 4 tiles) has a
  stain painted into every tile at startup (`MaterialMaps.Ceilings`: ragged water stains with uneven tide lines, darker
  toward the rim, and mould hazes with fine specks; about 0.3 s each in the editor), so a stained tile shows one of 32.
  Same shader keywords as the ceiling: no new variants.
- **Carpet** (carpeted rooms only): a multiplied overlay (`Resources/Shaders/FloorWear.shader`, its own tiny unlit
  shader, one mesh a chunk, broken up by world-space blotches) darkens the carpet along the bottoms of the walls (fading
  where a wall run ends), a trodden patch either side of each doorway, and faint worn paths between a room's doorways
  (two: one path; more: each to their middle). No line across doorways: one at every doorway, and lifted edges there,
  read as chunk seams (removed 2026-10-27). Along walls, 6 % of wall lengths have the carpet come away from its tack
  strip (`WallLift`: the edge lifted 5-12 mm, 1-2.5 cm short of the baseboard, loose threads hanging into the dark gap)
  and 3.5 % a low ripple (8-16 mm, 35-80 cm long). Carpet geometry with the floor's own material and world UVs, no
  colliders.

### Office details (2026-10-27)

`Generation/OfficeChunkBuilder.Details.cs`, seeded like the wear:

- **Ceiling**: square supply diffusers (3 % of tiles: frame, four stepped louvre rings, centre plaque, dark behind) and
  louvred return grilles (1.8 %); sprinkler heads on a world grid (every fifth tile across, every third cell row, 90 %
  present); a smoke detector on about 1 % of tiles, its red light glowing (`MaterialMaps.LedRed`); cracked tiles (1 %:
  a third ceiling texture with a wandering crack painted in each tile, `MaterialMaps.CeilingCracked`, slightly sagging)
  and tiles with a corner crumbled away (0.6 %: a ragged edge with the fibre showing, the space above through the gap,
  crumbs on the floor below).
- **Old lights**: 5 % of square panels have one tube out (its band dark, a third less light), 5 % old tubes gone pink
  and dim (a fifth less), 7 % dead bugs collected along the lens's low edge. `LightSource.dim` lowers the fixture's power
  for the lights, the light grid and the hum (`FixtureLightManager.PowerLevel`).
- **Walls** (every room): scuffs at hip and chair height (40 % of hallway wall cells, 12 % elsewhere), nail holes (6 %),
  the paler ghost of something that hung there (3.5 %: a faint frame of dirt round it and its nail hole), wallpaper
  peeling at a seam by the ceiling or the baseboard (3 % in wallpapered rooms: a curled triangle, its paper back, the
  painted board under it), and an exit sign over 6 % of headed doorways (one side; red letters on a white face,
  glowing, never switched off; only over a flat header with wall above it: a plain opening runs to the ceiling and an
  arch's curve reaches the sign's height, so a sign there hung in the air). Marks are a `WallWear` overlay, rebuilt with the walls when one is broken; the peel is part of
  the wall's own mesh.
- **Doors**: 35 % have black kick marks low on one face, 45 % grime round the handle and along the free edge, as a small
  overlay on the leaf (it swings with it).
- **Carpet**: coffee spills (1.8 % of cells: a round stain with a darker dried rim, sometimes drips) and an aluminium
  threshold strip across any opening where the floor changes (drawn once per edge).

### Height and depth (world version 14, Docs/WorldDepthPlan.md)
`DepthPlan` (its own dice) runs on the district plan once the rooms are joined up (`Apply`) and once at the end
(`Chamfers`); `OfficeChunkBuilder.Depth.cs` builds it. Rooms have a floor height (`Room.floor`) and a feature
(`Room.feature`: Sunken, Pit); `OfficeLayout.FloorAt`, `Plain` (crawl holes, noclip doors, doors and wear only on plain
floors).
- **Sunken wing** (`sunkenChance`, 0.45 a district): one to three joined ordinary rooms (not conference rooms, at least 3
  x 3) 0.45, 0.6 or 0.9 m down, their ceilings where they were. Every passable edge out of the wing has steps in the wing's
  cell beside it, rising to the doorway (`OfficeLayout.steps`: the side per cell, sliced into chunks; 0.15 m rises, treads
  0.3 m or less so a flight fits one cell). A wing never touches the district's edge (districts are planned apart) nor
  has a cell with two ways out. Walls, posts, baseboards, pillars, outlets and furniture start from the room's floor; the
  floor's colliders are runs of one height (the storey's floor 1 m deep so a sunken floor beside it is closed). No doors
  at its doorways, no half walls, no furniture or boxes on the steps (`DepthPlan.Busy`).
- **Pit room** (`pitRoomChance`, 0.12 a district, storey 0): an enclosed big room (at least 7 x 7, only doorways out)
  whose floor is a lattice of walkways (0.48 m between shafts, 0.6 m along the walls) on a 2.4 m pitch over square
  shafts (`OfficeLayout.pits`, layout-local metres, sliced to every chunk a shaft reaches). A 2 x 2 cell block with a
  doorway in it stays floor (a landing), as does an odd row along a wall; one in ten blocks is filled in. Shafts are 1.8-2.4
  m deep with a 0.6 m block in one corner half as high (so both pulls out are 1.3 m or less), or 0.6-0.9 m with none.
  Painted plaster; the shafts' walls in eleven bands darker toward the bottom and their floors darker still
  (`DepthMaterials`, made on the main thread by WorldStreamer): the light grid knows no floor heights, so the dark is in
  the paint. Colliders: the walkways solid 3 m down, the shafts' floors, the blocks. No furniture, boxes, pillars, crawl
  holes or noclip doors in it.
- **Cut corners** (`chamferChance`, 0.18 a room): a room's free corners (walls on both outer sides, nothing in the corner
  cell: no light, outlet, furniture, pillar, half wall, crawl hole, steps) get a wall at 45 degrees across the corner cell,
  face to face of the two walls, floor to ceiling, with its baseboard and a rotated collider (`Room.chamfers`).
- **Ramps**: a hallway (one or two cells wide) beside a sunken wing may be its way in (0.6 of wings try): its floor
  slopes from the wing's depth at one end up to the storey's floor over `RampCells` (1 in 6 at most), flat after
  (`Room.ramp`, `rampCells`). Every doorway within the ramp opens into the wing (at most 0.12 m, `MaxLip`, below the
  ramp there), at least one past it out to the rest. Drawn as a smooth slope (floor, wall bottoms, baseboards, posts);
  walked on 0.2 m slices of collider (3 cm steps). No furniture on it. Wherever floors differ at a doorway the lower side
  draws the riser between them (`DoorwayRiser`).
- **Ceilings that come down** (`ceilingDropChance`, 0.4): a hallway with a crawl slot at its own end has its ceiling
  slope over its last cells (up to four) to 1.35 m at the slot, so you crouch to get there (`Room.ceilDrop`,
  `ceilCells`, `ceilLow`). No lights or grid on the slope; its colliders step down with it; the slot's opening stops at
  the low ceiling.
- Getting about: Space at a ledge 0.45-1.35 m up pulls you onto it (`CamcorderPlayerController` mantle); a drop of
  about a metre or more jolts the camcorder.
- Tests: `DepthPlanTests` (the features turn up; every way into a wing has steps; shafts leave walkways and can be
  climbed out of; cut corners are clear; chunks carry their steps and shafts); the reachability and border checks cover
  them too.

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
- **Soft pools**: a downlight's pool on a wall is narrow and very bright near the fixture, so one sample per
  0.6 m texel aliased into boxes. `Direct` averages the falloff over eight points in each texel (line of
  sight stays one test at the centre) and the shader eases the trilinear weights (`LightGrid.smoothing`).
- **Per-pixel distant light** (`LightGrid.perPixelDirect`, default on; `LGrid_DirectPerPixel` in
  `LightGridSample.hlsl`): the shader loops the grid's own light list for the pixel's 4.8 m tile (the same
  buffers the compute pass uses) and evaluates URP's falloffs, Lambert and the wall line-of-sight test exactly
  at the pixel. Far pools are as crisp as near ones and nothing changes as you walk closer; the 0.6 m texture
  now only carries bounce. F5 > LIGHT > Per-pixel distant light turns it off.
- **Distant real lights** (`FixtureLightManager.farRealLights`, default 56): beyond the shadow-casting
  fixtures, more fixtures are real lights with shadows off. The wall line-of-sight test in the shader still
  stops them at walls, so only furniture shadows are missing, far away. The grid then only lights what is
  beyond them, plus bounce. Far pools are as crisp as near ones; cost is small (the pixel loop only runs the
  lights in range).
- **Ceilings are solid** (`OfficeChunkBuilder.BuildCeilingColliders`): a thin collider above every ceiling
  cell (not over wells), so nothing can get above a ceiling. The upper storey's floor edge beside a void
  carries a strip over the half-wall-thickness slit between the floor and the wall faces below.
- **F5 > LIGHT tab** (`CamcorderVisualMenu.Lighting.cs`, values in `GraphicsOptions`, applied by
  `GraphicsController`): light strength, reach, high-ceiling boost, dark spots (contrast curve on grid light),
  fill light (bounce gain), shadow darkness, pool smoothing, light wrap, reset. Strength, reach and boost change
  every fixture's grid light, so the manager recomputes the whole grid when they change (`MarkAllDirty`).
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
