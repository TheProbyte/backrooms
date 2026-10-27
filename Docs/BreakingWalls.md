# The sledgehammer and breaking walls

## Playing it
- **Finding one**: a sledgehammer often lies along a wall in the maintenance closets behind crawl spaces (45 %), sometimes
  in sealed storage (25 %). L (items menu) > ITEMS > Sledgehammer spawns one.
- **The hammer**: a short-handled sledgehammer (a 4 lb club hammer, 43 cm, 2.1 kg), modelled on an old drilling hammer:
  a forged head with chamfered edges, fuller round the eye, its ends stepped down and bevelled into bright striking faces;
  an oval hickory handle with a knob at the butt (`Items/SledgehammerModel.cs`).
- **Carrying**: click it to pick it up. The right hand carries it choked up near the head (the head in view, lower right);
  the left keeps filming.
- **Swinging**: hold the left mouse button to wind up. The hand slides down to the end of the handle and raises it over
  the shoulder; the longer you hold (up to 0.6 s) the harder and wider the blow (a quick click is a tap). Release to
  swing. The head comes over and down onto whatever the view is on. A wall a little out of reach: you step in while
  winding up.
- **What happens**: it stops dead against a steel stud or a post and springs back (big jolt), punches on into a hole it
  makes, and sails through when it meets nothing. Furniture and loose things are knocked; doors are shoved. Q drops or
  throws it as any item; thrown hard into a wall it breaks it too.
- **Tuning it**: F2 with it in your hand (or looking at it) opens its settings: how wide and how hard it breaks, the
  wind-up time, the swing, its reach and weight (see Tuning below, and Interaction.md, item tuning).

## What breaks
Ordinary walls between rooms of Level 0. Not: the posts at each 1.2 m (the corner and jamb studs), pillars, the
Poolrooms' tiled walls, landmarks (atrium, colonnade) and their stair halls and shafts, walls round a void, walls with a
crawl hole or a noclip door. Tall walls break up to 2.7 m; above that they stay whole. (`WallPanelModel.Breakable`.)

## What is inside a wall
Each 1.2 m wall panel (`Destruction/WallPanel.cs`, all from the world seed and the layout, never saved):
- two 12.7 mm gypsum boards; the wall's own finish on the face, grey kraft paper on the back, white gypsum core with a
  paper line at each face where it breaks;
- studs between the posts: in the Offices **one galvanised steel C-stud** (web, flanges, lips) on steel tracks, set off to
  one side so the gap beside it is 0.75 m clear (the player is 0.6 m across: once the boards are gone you get through
  without breaking anything; which side, by the panel's dice), and it does not break; in the older Hallways and the
  Warehouse's storage **wood studs at 400 mm** (2x studs, a bottom plate and a double top plate), which split;
- (fibreglass batts are switched off for now, `WallPanelModel.InsulationOn`: a batt knocked loose stayed hanging and could
  not be moved; the code for them stays);
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
  - **Board**: the plates within the blow's radius of the face of the hammer come out: 8 cm for a quick click up to 22 cm
    for a full wind-up (the hammer's tapRadius and breakRadius), a little irregular; the ones round them crack, out to 2.2
    times the radius (pushed in up to a millimetre and tipped up to 0.6 degrees, so the cracks show as fine lines). A blow
    landing under 0.3 (power times the strength of the swing) only cracks; a tap on a cracked plate knocks it out. Over a
    stud the board is backed: a smaller hole (55 %), and the stud takes some of the blow. Once the near board is open, the
    next blow hits the far board from behind and knocks it out into the other room (85 % of the radius).
  - **Stud**: wood takes damage in 0.45 m lengths and splits at the third full blow (power 1.3: in two), parting there
    with a 7 cm gap, splintered; a length held at neither end falls out. Steel rings and stops the hammer; it does not give.
  - **Baseboard**: cracks, then breaks off. Boxes, pipe, tracks: clang.
  - **What is left hanging**: drywall hangs on its screws, one every 0.3 m up each post and each standing length of stud
    (`WallDamage.Loose`). A piece with a screw in it holds; so does one joined to a held piece along at least 3 cm of
    unbroken edge (`FracturePiece.joins`); everything else falls out. (Until 2026-10-27 any piece whose box overlapped a
    stud's line held, and so did anything joined to one at a corner: chips stayed floating in the hole.)
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
  cable, pipe; a separate shadow caster (each piece left as a block through the whole wall thickness, as the chunks'
  walls cast: a 12.7 mm plate alone is thin enough for the shadow bias to eat); colliders (each board in 5 cm cells merged
  into rectangles, studs, tracks, boxes); and the rubble. It exists while a chunk on either side is loaded (`WallBreaker`).

## Light, sight and sound through holes
- **Light grid** (`LightGridMath.Encode`, `LightGrid.Holes.cs`, `LightGridCommon.hlsl`): a panel with any piece gone is
  still a wall, flagged as holed (occupancy bits 29 and 30, west and south edge). A table of holed walls (16 uints a
  record: key, 160 bits of 0.15 m blocks open right through, 8 across by 18 up, 160 bits of blocks with any opening, and
  the share open) and a per-cell index laid out as the occupancy (the record of each cell's west and south wall) are
  bound to the shaders and the grid's kernels: one read, no search, because these tests sit inside the line-of-sight
  loops (a hashed lookup there made each lit shader variant take minutes to compile with the D3D11 compiler; now about
  17 s, as before).
  - A light is not a point: the shaders test the light's footprint on the wall, not the one block its centre line meets
    (`LG_HoleOpenBox`: the share of a box open, the blocks weighted by how much of each it covers; the box is the cone to
    the ceiling panel where it crosses the wall, its width along the wall and the height the panel's depth spreads over,
    at least a block). Light through a hole has soft edges as a panel's light would, not 0.15 m steps (2026-10-27: the
    grid's lights drew hard squares on the wall opposite a hole).
  - Lights with a shadow map (the real lights near you; `LGrid_EmitterVisibleH` / `LGrid_LightVisibleH` with
    shadowMapped, chosen per light from its shadow parameters) pass a holed wall where any of the footprint has an
    opening, except at its posts, and the broken panel's pieces stop them in the shadow map: their light comes through
    the hole exactly, broken by the studs. Where the footprint is all whole wall it stops them as the wall does.
  - Everything else (the grid's lights, the far part of real lights) passes the footprint's open share; the traced
    bounce and the probe filter use the blocks open right through (at least 5 of 9 points of a block: no board, no box).
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

Walls saved before 2026-10-27 load under the new rules: pieces nothing holds any more (the screw rule) are found lying
flat on the floor at the foot of the wall, on their own side (`WallDamage.SettleLoose`); batts and their rubble are gone;
a steel wall's saved stud damage is ignored (its one stud stands whole, where the new layout puts it).

## Tuning
- In game: F2 with the hammer in hand (or in view) opens its settings (`ItemTuningMenu`, Interaction.md): wind-up time
  (`fullWindUp`, 0.6 s), how wide a full blow breaks (`breakRadius`, 0.22 m) and a quick click (`tapRadius`, 0.08 m),
  how much each blow destroys (`power`, 1.3: wood studs, baseboards, how far the pieces fly; below about 0.9 a click only
  cracks), how far cracks spread (`crackSpread`, 2.2 x the radius), the swing time (0.2 s), the reach (0.68 m) and its
  weight (2.1 kg). Saved into the project (`Resources/ItemTuning.json`): the defaults everywhere.
- In code: the blow (`Blow`, `WallDamage.BreakBoard`), wood studs (`Damage`: 2.9 full blows; steel does not break), the
  screws (`WallDamage.ScrewSpacing`, `MinJoin`), the steel stud's gap (`WallSpec.PersonGap`), `FracturePattern.Spacing`
  (piece size), the hammer's poses (`Carry`, `Raised`, `Solve`), `WallBreaker.maxDebris`.

## Checking it
`Debug/WallBreakProbe` (Play Mode, a world loaded: add it and call `Run(folder)`): finds the nearest breakable wall,
stands in front of it, picks up a sledgehammer and swings ten times round a spot (logging what each blow met and what is
left), photographs the view before, holding, winding up, at impact, after the blows, into the hole, from the other side,
with that side's lights off and at the foot of the wall; `Revisit(folder, wall)` after saving and loading again.
Screenshots: Docs/screenshots/walls/ (`walls_overview.jpg`; 13-17: the user's World 2 walls loaded under the new rules,
the soft light through a hole, the doorways without the band, the F2 settings). 2026-10-27: a full blow knocks out about
20 pieces (was about 10).

Loading the user's World 2 (copied into World 3) showed that a saved wall naming a part the wall no longer has (rubble of
a second steel stud) threw while its chunk came in, so the chunk never finished and its floor was missing (the player
fell through). Such rubble is now left out (`WallDamage.Fits`), and a broken wall that fails can no longer stop its chunk
(`WallBreaker.Ensure`). A broken panel left over from a script reload in Play Mode removes itself.

## Not done yet
- Ceilings, floors, doors, furniture and lights do not break (the hammer knocks furniture and shoves doors).
- Light through a hole from a light without a shadow map is read from 0.15 m blocks (softened over the light's
  footprint); a peephole much smaller than a block lets little grid light through (the real lights near you do).
- Insulation batts are off (`WallPanelModel.InsulationOn`) until a loose batt can be picked up and moved.
- In the old room-layer lighting mode (not the default), light does not reach from one room into the other through a hole.
- Creatures do not break walls yet.
