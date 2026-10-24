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

**Camera:** right mouse turns, middle mouse or Shift+right mouse or WASD pans, wheel zooms, Q/E rotate, F frames. Ctrl+Z / Ctrl+Y undo and redo.

**Room settings:** name; spawns in the world; how often (chance); weight; may be turned 90 degrees; biomes and zones;
variants (each a layout of its own, with a weight); size (2 to 24 cells of 1.2 m); areas (each with ceiling height, painted or
wallpaper walls, light mode, kind).

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

## Limits of this first version

- Walls are on the 1.2 m cell grid, full height or half height. Free-angle walls, custom wall heights and window
  openings are not supported by the world's plan format yet.
- Materials: wallpaper or painted plaster per area (the world's two wall finishes). No per-surface material picker yet.
- Props: box, pallet, sofa, conference table, floor lamp. Racks and wall clocks need wall placement.
- Loot spots are stored with an item name, chance and note, and spawn as loose cardboard boxes until the game has loot.
- Rooms only on the ground storey. Up to 24 x 24 cells.
- Custom rooms are not yet in the F5 PLACES tab, so find one in the world with a high chance and a small landmark spacing, or use Test.

## Crawl holes (non-Euclidean)

Low square holes at the foot of some walls, in pairs. Crouch-crawl (Left Ctrl) into one and you come out of the other,
turned to face out of it, 12-30 m away in a room that is a long walk off. From the far side of the wall the hole is just a
shallow boxed duct. Looking into a hole shows the other end. How many: `WorldGenSettings.crawlPortals` (pairs per district).
