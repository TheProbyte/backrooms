# Build mode, custom rooms, and the menu

## Getting around

**Escape** opens a menu in every scene (the F5 panel still closes first on Escape):

| Button | What it does |
|---|---|
| Resume / Back to building | Close the menu |
| Back to the builder | Only while testing a room in the world: returns to the editor with your edits |
| Play | Loads the procedural world (`Backrooms_Infinite`) |
| Reference room | Loads the Reference Match scene |
| Build a section | Loads build mode (`BuildMode`) |
| Quit | Quits |

The three scenes are in the build settings (`Backrooms > Build Build-Mode Scene` re-lists them).

## The office biome's new look

`OfficeReferenceLook` (run by `Backrooms > Apply Reference Look To Office Biome`, and by the world builder) edits the office
materials in place: tan-yellow wallpaper (the game's own wallpaper maps, tinted), the supplied carpet photo, square 0.6 m
ceiling tiles with square one-tile lights, and no baseboard (`OfficeBiomeAssets.noBaseboard`).

## Build mode

A dark, effect-free scene: no camcorder pass, no post-processing. Rooms are built with the world's own layout and chunk
builder, so the preview is what the world will generate.

**Screens:** the room library (cards with plan thumbnails: edit, spawn on/off, copy, delete) and the editor.

**Tools** (hotkeys 1-0): select, wall, opening (doorway, arch, tall opening, open gap), half wall, erase, floor areas,
pillar, light, prop, loot. Drag along grid lines for long walls and wide openings. Openings in the outer wall are the
room's entrances and exits.

**Views** (Tab): solid, room (ceiling off), cutout (walls between you and the room are cut down), walls down, top.

**Help:** F1 or HELP opens the controls overlay. Hover controls for tooltips (including controls in scrolled panels).

**Props:** box, pallet, sofa, armchair, office chair, conference table, floor lamp, storage rack and wall clock.
Racks and clocks snap beside solid walls, facing into the clicked area. Click the floor near the wall; a wide rack needs
an uninterrupted run clear of openings. Drag a selected wall prop to reposition it on another wall; its facing stays
wall-aligned. A clock has an editable mounting height. Other props can be turned with R or the selected-prop slider.

**Copy/paste areas:** select an area in the right panel, COPY AREA / Ctrl+C, then PASTE AREA / Ctrl+V and click the
lower-left destination cell (blueprint coordinates). The green/red footprint shows whether it fits. Copies the painted
shape, settings, touching walls/openings, lights, pillars, props and loot. Unpainted holes remain untouched. Paste replaces
existing contents in that footprint, adds a new area and is one undo step. Escape cancels. The clipboard survives changing
rooms/variants during the builder session, but is not saved to disk. No rotation/mirroring of pasted areas yet.

**Camera:** right mouse turns, middle mouse or Shift+right mouse or WASD pans, wheel zooms, Q/E rotate, F frames. Ctrl+Z / Ctrl+Y undo and redo.

**Room settings:** name; spawns in the world; how often (chance); weight; may be turned 90 degrees; biomes and zones;
variants (each a layout of its own, with a weight); size (2 to 24 cells of 1.2 m); areas (each with ceiling height, painted or
wallpaper walls, light mode, kind). WALL / FLOOR buttons cycle existing finishes. Default keeps the original painted flag
and carpet; explicit walls: wallpaper, painted plaster, Reference Match cream or olive. Floors: default carpet, Reference
Match carpet, dark wood or laminate (reused existing materials). Explicit wall finishes also apply to tall rooms.
`Backrooms > Refresh Existing Room Finishes` restores reference material links on OfficeBiome_Assets without editing materials.

**Buttons:** Save (to your library), Save to game (and turn spawning on), Test in world, Discard changes, Library.

**Test in world** forces the room into the real procedural world just beyond the spawn hall and puts you in it with the
camcorder filters on; its doors link to the procedural rooms around it. Escape > Back to the builder.

## How rooms get into the world

1. A blueprint is a JSON file in `persistentDataPath/CustomRooms` (`RoomBlueprint`, `RoomVariant`, `RoomLibrary`).
2. Enabled blueprints join the landmark pool (`Landmarks.TryPlace`; kind `LandmarkKind.Custom`). The landmark grid
   (`WorldGenSettings.landmarkSpacing`, default one spot per 3 x 3 districts) decides how many spots exist; `chance` and
   `weight` decide which room takes one. Filters: biomes and zones.
3. `CustomRoomStamp` turns the chosen variant (and turn) into rooms, edges, forced exits, lights, pillars, half walls and
   props in the district plan. Exits only open onto walkable neighbouring rooms; at least one is always made.
4. The normal chunk builder builds it. Random doors, random lights and random dressing skip custom rooms.

`CustomRoomHarness` builds a stand-alone plan around one room (corridor ring); the builder preview and the tests use it.
Tests: `Scripts/Editor/Tests/CustomRoomTests.cs`.

**F5 > PLACES > MY ROOMS:** NEAREST CUSTOM ROOM finds the nearest generated custom placement within about 10 districts;
each enabled design also has a button. Arrival avoids authored props, pillars and half walls, and waits for floor loading.
Save/enable designs before starting or regenerating the world. These buttons search actual seed placements, not library
previews. No match is reported rather than placing an ungenerated room. Moved runtime props are not included in clearance.

## Limits of this first version

- Walls are on the 1.2 m cell grid, full height or half height. Free-angle walls, custom wall heights and window
  openings are not supported by the world's plan format yet.
- Materials: existing finishes per area; individual wall faces and free-form texture imports are not supported.
- Loot spots are stored with an item name, chance and note, and spawn as loose cardboard boxes until the game has loot.
- Rooms only on the ground storey. Up to 24 x 24 cells.

## Crawl holes (non-Euclidean)

Low square holes at the foot of some walls, in pairs. Crouch-crawl (Left Ctrl) into one and you crawl along a 2.2 m duct
behind the wall; at its far end a window shows the other hole's room, and crossing it brings you out of the other hole,
12-30 m away, facing out of it, in a room that is a long walk off. The duct is "bigger on the inside": it overlaps the room
behind the wall, which never sees it (no box, no colliders there; the back of the wall stays solid). How many:
`WorldGenSettings.crawlPortals` (pairs per district). See [Portals.md](Portals.md) for how it works and its limits.
