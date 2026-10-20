# World generation: how it works now, and where it can go

## How it works today

Nothing is purely random. Every chunk is built from **(world seed, chunk coordinate)** alone, by fixed rules
(`OfficeLayoutGenerator.cs`). The same seed always gives the same world, and chunks can be built in any order.

**Grid.** The world is a grid of 1.2 m cells (one wall panel, two ceiling tiles). A chunk is 12 x 12 cells
(14.4 m). Walls sit on cell edges; posts sit on cell corners.

**Pipeline for one chunk:**

1. **Border openings.** Each chunk edge is a wall with 1-3 openings. Their positions come from a hash of the
   *border itself*, so the two chunks on either side always agree. This is the only "communication" between chunks.
2. **Partition (BSP).** The chunk rectangle is split recursively into rooms. At each split there is a chance to
   stop (bigger rooms are less likely but common) and a chance to carve a 1-2 cell hallway along the split line.
   One-wide hallways sometimes end in a crawl slot too narrow to enter. About 1 in 5 chunks is one big hall, and
   the starting chunk always is.
3. **Room kinds.** Office, BigOffice (area >= 30), Lounge, Conference, Hallway, Crawl, picked by size + dice.
4. **Ceilings and finish.** Big rooms often rise to 3.9-5.4 m plaster ceilings; others get 2.7 m drop ceilings.
   Walls are wallpaper or painted plaster.
5. **Doors.** A random spanning tree over neighbouring rooms guarantees every room is reachable, plus extra
   loops and some fully open-plan walls. A flood fill then verifies connectivity and adds doors if anything is
   cut off. Because every room connects and every border opening touches a room, the whole infinite world is
   one connected space.
6. **Height rule.** Any opening out of a taller room becomes a headed doorway, so you never see above a lower
   ceiling.
7. **Lighting.** Each room is all lit, mixed or dark; fixtures sit on a 2.4 m lattice; ~2.5% of tubes are faulty.
8. **Dressing.** Pillars and half walls in big rooms, sofas/lamps in lounges, tables and chairs in conference
   rooms, clocks, outlets and switches.

**Streaming.** 7 x 7 chunks stay loaded around you; new ones are built on worker threads at the outer ring,
29+ m away inside the fog, and power on with a fluorescent flicker. Chunks behind you stutter off and unload.

## What Hytale's World Gen V2 does (for comparison)

- Terrain and placement are driven by **density fields**: maps of values (usually -1..1) produced by noise
  (Simplex, Cellular), context, and processing nodes. ([Hytale modding docs][concepts])
- A **World Structure** uses a density field to place biomes: each *delimiter* claims the places where the
  density is within its bounds, with a default biome for the rest, and the edge between two biomes is
  **interpolated over a configurable distance**. ([world structure][structure])
- Everything is authored in a **node graph editor**, and the official biomes ship as editable graphs.
  V2 replaced V1 because V1 couldn't scale to more zones and complex biomes. ([Hytale news][news])

## Proposed rule system for the Backrooms

The same ideas map well onto rooms instead of terrain. Suggested layers, top-down:

| Layer | Decides | Driven by |
| --- | --- | --- |
| **Zone** (e.g. Level 0) | Overall look, fog, audio, which biomes are allowed | Very low-frequency noise or hand-placed regions |
| **Biome** | Generation rules for an area: Offices, Endless Hallways, Warehouse, Pillar Hall, Lounges... | A biome density field (noise sampled at chunk/cell scale) + delimiters |
| **Room recipe** | Size range, ceiling height, wall finish, props, lighting | Weighted tables inside each biome |
| **Features** | Pillars, half walls, crawl slots, furniture, lights | Per-recipe rules |

**Biome assets.** Each biome would be a data asset (ScriptableObject, editable in the Inspector; later a node
graph): split/stop chances per size, hallway width and frequency, open-plan chance, ceiling height
distribution, room-kind weights, light mode weights, prop tables, density of pillars, etc. Today's offices
become just one biome asset.

**Placement.** A 2D density field over the world (for example a few octaves of cellular + simplex noise,
sampled per cell) picks the biome at each cell via delimiters, exactly like Hytale. Hand overrides could
pin a biome to a region ("this section is hallways only").

**Transitions.** Two options, usable together:
- *Interpolated parameters*: near a boundary, blend numeric rules (room size, hallway chance, ceiling
  height) over a transition distance, so offices gradually open up into a warehouse.
- *Connector rules*: a boundary can demand a specific transition piece (a loading-dock door, a long corridor,
  a stairwell-like height change).

**Connectivity stays guaranteed** because the border-opening rule and the per-chunk connectivity check don't
depend on the biome. Each biome only changes what happens *inside* the rules.

**Suggested first step:** biome map + biome assets for Offices (current), Endless Hallways and Warehouse, with
blended transitions, plus a top-down debug map view to see the biome field.

[concepts]: https://hytalemodding.dev/en/docs/official-documentation/worldgen/worldgen-tutorial/world-generation-concepts
[structure]: https://hytalemodding.dev/en/docs/official-documentation/worldgen/technical-hytale-generator/world-structure
[news]: https://hytale.com/news/2026/1/the-future-of-world-generation
