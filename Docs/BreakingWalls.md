# The sledgehammer and breaking walls

## Playing it
- **Finding one**: a sledgehammer often lies along a wall in the maintenance closets behind crawl spaces (45 %), sometimes
  in sealed storage (25 %). L (items menu) > ITEMS > Sledgehammer spawns one.
- **Carrying**: click it to pick it up. The right hand carries it choked up near the head (the head in view, lower right);
  the left keeps filming. It is heavy (3.3 kg): you walk and turn a little slower.
- **Swinging**: hold the left mouse button to wind up. The hand slides down to the end of the handle and raises it over
  the shoulder; the longer you hold (up to 0.6 s) the harder the blow (a quick click is a tap). Release to swing. The head
  comes over and down onto whatever the view is on. A wall a little out of reach: you step in while winding up.
- **What happens**: it stops dead against a stud or a post and springs back (big jolt), punches on into a hole it makes,
  and sails through when it meets nothing. Furniture and loose things are knocked; doors are shoved. Q drops or throws it
  as any item; thrown hard into a wall it breaks it too.

## What breaks
Ordinary walls between rooms of Level 0. Not: the posts at each 1.2 m (the corner and jamb studs), pillars, the
Poolrooms' tiled walls, landmarks (atrium, colonnade) and their stair halls and shafts, walls round a void, walls with a
crawl hole or a noclip door. Tall walls break up to 2.7 m; above that they stay whole. (`WallPanelModel.Breakable`.)

## What is inside a wall
Each 1.2 m wall panel (`Destruction/WallPanel.cs`, all from the world seed and the layout, never saved):
- two 12.7 mm gypsum boards; the wall's own finish on the face, grey kraft paper on the back, white gypsum core with a
  paper line at each face where it breaks;
- studs at 400 mm centres between the posts: **galvanised steel C-studs** (web, flanges, lips) on steel tracks in the
  Offices, **wood** (2x studs, a bottom plate and a double top plate) in the older Hallways and the Warehouse's storage;
- fibreglass batts in some stretches of wall (yellow in the offices, 40 % of walls; pink in wood walls, 20 %);
- behind every outlet and switch its electrical box (steel on steel studs, blue plastic on wood) on a strap or a block,
  with its cable up to the top track (armoured MC cable in steel walls, yellow NM in wood ones); wood walls also carry a
  cable along at outlet height through the studs in some stretches; now and then a copper water pipe;
- the baseboard on each side in three lengths, where the room has one as the chunk draws it (the current look has none:
  `OfficeBiomeAssets.noBaseboard`, so neither do its broken walls).
- Metals are only partly metallic (steel 0.45, copper 0.5), as the game's own: inside a wall there is next to nothing to
  reflect, and a full metal shows black.

## How it breaks (`Destruction/WallDamage.cs`)
- Each board breaks along a fixed pattern (`WallFracture.cs`): a Voronoi diagram of jittered sites about 11 cm apart, a
  fifth of them left out (so some plates are twice the size), its shared edges jagged the same way from both sides. Pieces
  are 6 to 25 cm. A save lists only which are gone.
- A blow is traced through the panel along the swing: the baseboard, the near board's face, then inside (studs, tracks,
  boxes, pipe, batts) and the far board from behind.
  - **Board**: the plates within 5-13 cm of the face of the hammer come out (more with a harder swing); the ones round
    them crack (pushed in up to a millimetre and tipped up to 0.6 degrees, so the cracks show as fine lines). A tap (under 30 %) only
    cracks; a tap on a cracked plate knocks it out. Over a stud the board is backed: a smaller hole, and the stud takes
    some of the blow. Once the near board is open, the next blow hits the far board from behind and knocks it out into
    the other room.
  - **Stud**: damage builds up in 0.45 m lengths; steel tears at the second full blow, wood splits at the third. It parts
    there (an 11 cm gap for steel, 7 cm for wood); a length held at neither end falls out. Steel ends are ragged, wood
    ends splinter.
  - **Batt**: one 0.3 m length comes out per blow. **Baseboard**: cracks, then breaks off. Boxes, pipe, tracks: clang.
  - **What is left hanging**: a board piece screwed to nothing (no stud, post or track under it) and joined to nothing
    that is falls out.
- Everything that comes out falls as debris (`WallDebris.cs`: light bodies that do not trip the player, at most 70 at
  once); bits under about 6 x 6 cm crumble into crumbs and dust. A board piece always leaves the board's plane (at least
  0.45 m/s out of it, more for a harder blow; 0.3-0.6 m/s for one that drops) and tips over at the top as it goes:
  dropping straight down it could come to rest standing on the edge of the hole below. Pieces that fall into the wall
  can still lodge against a stud there. Debris that comes to rest becomes rubble: its pose is
  kept with its wall (and saved), drawn as one mesh with the wall.
- Dust: a cloud of soft puffs lit by what is behind them (`Resources/Shaders/Dust.shader`, the light of the camera's
  opaque copy) and crumbs that bounce on the floor.
- Paint, chalk and tape on a piece that is gone go with it (`SprayPaint.Erase`, `Marks.Erase`). An outlet or switch plate
  stays on its box when the board round it is gone, and keeps working.

## How it is drawn
- A struck panel is taken over from its chunks: `WallDamage` keeps a copy of the taken walls that the chunk builder
  reads on its worker threads, and `OfficeChunkBuilder` leaves the panel's faces, baseboards and wall collider out
  (`Broken`, `BrokenPanelColliders`: the posts and the wall above 2.7 m keep colliders). A loaded chunk is prepared again
  off the main thread and only its walls, baseboards and shadow boxes, and the colliders that changed, are swapped in
  (`WorldStreamer.RebuildWalls`, `OfficeChunkBuilder.ReplaceWalls`). For a frame or two the old face still shows; the dust
  covers it.
- `BrokenPanel.cs` draws what is left (`PanelGeometry.cs`): the board pieces with the wall's own texture mapping (the
  pattern runs on unbroken across cracks), core edges only where a neighbour is gone or loose, the frame, batts, boxes,
  cable, pipe; a separate shadow caster (plain pieces); colliders (each board in 5 cm cells merged into rectangles, studs,
  tracks, batts, boxes); and the rubble. It exists while a chunk on either side is loaded (`WallBreaker`).

## Light, sight and sound through holes
- **Light grid** (`LightGridMath.Encode`, `LightGrid.Holes.cs`, `LightGridCommon.hlsl`): a panel with any piece gone is
  still a wall, flagged as holed (occupancy bits 29 and 30, west and south edge). A table of holed walls (16 uints a
  record: key, 160 bits of 0.15 m blocks open right through, 8 across by 18 up, 160 bits of blocks with any opening, and
  the share open) and a per-cell index laid out as the occupancy (the record of each cell's west and south wall) are
  bound to the shaders and the grid's kernels: one read, no search, because these tests sit inside the line-of-sight
  loops (a hashed lookup there made each lit shader variant take minutes to compile with the D3D11 compiler; now about
  17 s, as before).
  - Lights with a shadow map (the real lights near you; `LGrid_EmitterVisibleH` / `LGrid_LightVisibleH` with
    shadowMapped, chosen per light from its shadow parameters) pass a holed wall in the blocks with any opening, except
    at its posts, and the broken panel's pieces stop them in the shadow map: their light comes through the hole exactly,
    soft-edged, broken by the studs. Whole blocks stop them as a wall does (the thin boards' shadows alone let a line of
    light under the foot of the wall: shadow bias).
  - Everything else (the grid's lights, the far part of real lights, the traced bounce, the probe filter) uses the
    blocks: open where at least 5 of 9 points of a block are open right through (no board, no batt, no box).
- **Chunk culling** (`ChunkVisibility.Solid`): a wall with any hole right through can be seen through, so the chunk
  behind it is drawn.
- **Sound**: the building's sound paths (`AcousticField`) go through a broken wall open by 12 % or more, and a smaller hole
  muffles like a shut door. Steam Audio gets the remaining board pieces as a mesh of their own (`SteamAudioWorld`), and
  the chunk's own mesh is rebuilt without the panel.
- Sounds (`ProceduralSounds.Demolition.cs`): the swing through the air, drywall thump and crunch, board crumbling and
  pattering down, a steel stud's tinny bang and its tearing, a wood stud's thock and its splintering crack, a dead thud
  into a post, a clank on a box or pipe, a batt's whump, bits landing.

## Saved
`WorldSaveData.walls` (`WallData` per broken panel): which pieces of each board are gone or cracked (bits, base64), the
baseboards, the batts, each stud's standing lengths and damage, and the rubble's poses (pieces still falling are saved
where they are). Restored before any chunk is built (`WorldSaver.RestoreProps`), so the chunks leave the panels out from
the start. A new seed clears them.

## Tuning
- `WallDamage`: blow radius (`BreakBoard`: 5-13 cm), stud strength (`Damage`: steel 1.5, wood 2.9 full blows), the gaps.
- `FracturePattern.Spacing` (piece size).
- `Sledgehammer`: `armReach`, `fullWindUp`; the poses (`Carry`, `Raised`, `Solve`).
- `WallBreaker.maxDebris`.

## Checking it
`Debug/WallBreakProbe` (Play Mode, a world loaded: add it and call `Run(folder)`): finds the nearest breakable wall,
stands in front of it, picks up a sledgehammer and swings ten times round a spot (logging what each blow met and what is
left), photographs the view before, holding, winding up, at impact, after the blows, into the hole, from the other side,
with that side's lights off and at the foot of the wall; `Revisit(folder, wall)` after saving and loading again.
Screenshots: Docs/screenshots/walls/ (`walls_overview.jpg`: the last run, a wallpapered wood-stud wall in World 3).

## Not done yet
- Ceilings, floors, doors, furniture and lights do not break (the hammer knocks furniture and shoves doors).
- Light through a hole from a light without a shadow map is in 0.15 m blocks; a peephole smaller than a block lets no
  grid light through (the real lights near you do).
- In the old room-layer lighting mode (not the default), light does not reach from one room into the other through a hole.
- Creatures do not break walls yet.
