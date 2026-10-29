# Plan: height and depth in the Backrooms (2026-10-28)

**Status (2026-10-28):** built: the foundation (floor height per room, steps, walls and colliders), split levels (sunken
wings), the pit room with mantling and the landing jolt, and cut corners (WorldGeneration.md, "Height and depth").
Not yet: ramps and sloped ceilings, diagonal hallways, mezzanines, holes to the storey below, the wrong rooms.

The reference is a pit room from Kane Pixels' videos: a level office floor broken into a lattice of narrow walkways over
deep square shafts. The scale is ordinary (a 3 m ceiling, troffer lights), and the floor is the thing that is wrong.
That is the kind of depth this plan is for. The world should stay recognisably Level 0, but the floor and ceiling
should stop being one flat plane.

## Where the world is now

- **One flat floor per storey.** Every room's floor is at the storey's level. Storeys are 9 m apart.
- **Exceptions:**
  - Poolroom basins are sunk 0.5 m (`PoolBiome.FloorY`).
  - The atrium's well spans two storeys.
  - The stair hall has a sloped ceiling.
  - Crawl spaces exist.
- **Ceilings** already vary per room (2.7 m up to tall halls, in 0.3 m steps).
- **Rectangles on a 1.2 m grid.** Walls sit on cell edges. There are no diagonal walls anywhere.
- **The light grid** is per storey and per cell. It knows walls, holes and ceiling heights, but not floor heights.
- **The player** has stairs (`OnStairs`, the body's foot placement on treads), crouch and crawl. There are no ramps,
  ledges or long falls.

The basins and the stairs show the builder can already carry walls down, build steps and edges, and move the feet over
treads. Most of this plan generalises those.

## The foundation: a floor height per cell

Everything below needs this first.

- **What it adds:** a floor offset per cell in the layout (`OfficeLayout`), in 0.15 m steps (one tile row), from about
  -6 m to +3 m within a storey. A room has a base height, and a pattern can vary it cell by cell (pits, steps).
  `PoolBiome.FloorY` becomes the general `Layout.FloorY(x, z)`; basins become one user of it.
- **Builder:** walls and posts are carried down to the lowest floor beside them (as for basins). Where two floors
  differ there is a step face with a nosing, or an edge with a low lip and a railing where the drop is over 0.6 m.
  Baseboards follow the floor.
- **Reachability:**
  - The district's flood fill and the World Map checks learn heights. A step of up to 0.3 m is walkable.
  - Stairs or a ramp join anything taller.
  - A drop is one-way (you can fall but not climb back).
  - Every room must still be reachable without falling. That way you can never be trapped.
- **Light grid:** the floor offset goes into the grid's cell data next to the ceiling height. Light reaching a sunken
  floor is cut by the walls of the pit around it, so pit bottoms are dark, as in the reference. A first version
  darkens by depth and opening size; the real test through the grid comes after.
- **Saves:** world version bump. Old worlds regenerate as usual.
- **Cost:** large. It touches the layout, builder, light grid and tests. It's the one big piece; everything after it is
  medium or small.

## What to build on it

### 1. Split levels and sunken rooms (first use, small)
- An office or a hallway sunk 0.45-1.2 m, with 3-8 steps at its doorways: a conversation pit, a lower open-plan floor
  seen from a railing.
- A hallway that steps down twice along its length.
- Raised platforms (a stage, a dais) of 0.3-0.6 m in big halls.
- The odd one with no steps where there should be some: a doorway onto a 1.5 m drop, so you see the far room's floor
  below the doorway's sill.

### 2. Ramps and slants (medium)
- **Ramps:** sloped corridors, 1:12 (gentle) to 1:6 (steep), going down 1.5-4 m over a run. Built per cell as sloped
  floor quads, with the walls and baseboards following.
- **The ceiling either stays level** (the corridor grows taller as it goes down: very Backrooms) **or follows the
  slope** (a tilted tube).
- **Sloped ceilings:** a room whose ceiling falls from 3.6 m to 1.5 m across it (attic-like, crouch at the low end),
  wedge ceilings over stairs, and a ceiling that steps down in tiles.
- **Movement:** the walk slows going up a steep ramp, and items roll down slopes. Feet need angled placement on slopes,
  as they already do on stair treads.

### 3. The pit room (landmark, medium; the reference)
- A big office floor turned into a lattice: 0.48 m walkways on a 2.4 m pitch (two cells) with 1.92 m square shafts.
  That's the reference's proportions, about 4 to 1. Rooms of 10 x 10 to 16 x 16 cells.
- **Shafts** are 2.4-6 m deep, smooth painted walls, dark at the bottom. Lights stay on the ceiling as normal.
- **Variations:**
  - A few shafts are shallow (a 1 m step down: see the floor).
  - A few are filled flush.
  - One has a ladder, and one opens at the bottom into a crawl tunnel to another room (so falling in is a way
    somewhere, not a dead end).
- **Walking a walkway** shows its edge in the camera. Your footsteps echo from the shafts; Steam Audio gets this from
  the geometry.
- **Falling:** a landing jolt from the camera and the body. No damage system is needed; the drop is the scare.
- **Mantle** (new, small): jump at a ledge up to 1.3 m above the feet and the body pulls up onto it. You can't climb
  out of a deep shaft without the ladder.

### 4. Diagonals (medium to large)
- **Chamfered corners first (small):** a room corner cut at 45 degrees across one cell. It breaks the box look
  cheaply, needs only one wall kind across a cell's diagonal, and the light grid treats that cell as half solid.
- **Diagonal hallways (large):** a corridor crossing a district at 45 degrees, its walls running along cell diagonals,
  rooms beside it with one slanted wall. It needs the diagonal wall kind everywhere: doors in slanted walls,
  baseboards, ceiling grid trim (the ceiling tile grid stays square and is cut at the wall, which looks right).
- **Triangular and trapezoid rooms** come from the same wall kind, at the ends of diagonal hallways.
- **Off-angle doors:** a door in a slanted wall.

### 5. Bigger vertical spaces (medium)
- **Mezzanines** in warehouse halls: a steel walkway at about 3.5 m along a wall or across the hall, with stairs up and
  rails.
- **Holes to the storey below:** a floor hole with a railing (or without), looking down 9 m into another office.
  Atriums already do this for one landmark; this makes it a common, smaller feature.
- **Double-height offices** with a balcony on one side.

### 6. Rare wrong rooms (small each, landmarks)
- **The tilted room:** the whole floor tilted 6-10 degrees across the room, the walls and lights square to the floor.
  Loose items slowly slide downhill, and the view feels wrong.
- **Stairs to nowhere:** a flight up to a wall or a ceiling.
- **The sunken hallway:** a long corridor whose floor drops a step every few metres until the doors along it are
  halfway up the walls.
- **The ceiling that comes down:** a hallway whose ceiling slants down until you have to crawl.

## Order

1. **Foundation** (floor height per cell, steps and edges, reachability, light grid floor) together with **split
   levels** as its first use, so it ships as something visible.
2. **Ramps and sloped ceilings** (the slants).
3. **The pit room**, with mantling and the landing jolt.
4. **Chamfered corners**, then **diagonal hallways** (the diagonals).
5. **Mezzanines, holes to the storey below, wrong rooms.**

Each step is its own commit, checked by the World Map checks (reachability now with heights), the edit-mode tests, and
a walk through World 3. How often each feature appears is a biome number, so it can be tuned in the F5 World Gen tab.

## Risks
- **The light grid is the hardest part.** Pits and slopes must not leak light through floors or look flat. If the
  first darkening is not good enough, deep shafts get baked ambient occlusion in their walls as a fallback.
- **Diagonal walls touch everything that assumes walls are on cell edges:** occupancy, doors, crawl mouths, noclip
  doors, wall damage. That's why chamfers come first and full diagonal hallways last.
- **Performance:** more wall faces in pit rooms (each shaft is 4 deep faces). It's one mesh per chunk as now, so draw
  calls do not grow; vertex count does, a little.
- **Never trapped:** every height change is checked by the flood fill. Drops are one-way and every room must be
  reachable without falling.
