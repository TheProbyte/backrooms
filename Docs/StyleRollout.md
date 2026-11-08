# Plan: the new style across the game (2026-11-06)

The style lab settled the look: chunky, rounded, low-poly shapes in the company colours, simple painted textures with
baked dirt and wear, the game's real lighting and fog kept, and the camcorder picture over it all. This plan takes it from
the lab into the game, starting with what is already built and what is sure to carry over into Overflow (the company that
leases this space out, its contracts, the hub). World generation changes, the hub and the training room come after.

## How others do it

- Borderlands' late switch to its "concept art" look (GDC 2010, *Borderlands and the 11th Hour*): the team wrote style
  sheets (each idea in words beside examples of it) so every artist made the same thing, then redid everything in order.
- The usual lesson from games that changed style (Baldur's Gate 3 twice, Bully, small indie postmortems): try the new
  look in one place first. The lab has done that.
- Lethal Company: plain Unity rendering, low-poly models, deep shadows and fog, with posterising and outlines on the
  finished picture. The look comes from shapes, colour and lighting more than from detailed textures.
- Simple props and buildings share trim sheets and small palette textures. Characters and hero props get their own.

So: one style sheet, one switch, and the work in order of what the player sees most, each step checked in the game
against the old look and measured.

## Phase 0: groundwork (first, small)

1. **Style sheet** (`Docs/StyleSheet.md`, with pictures from the lab):
   - the palette: company amber #F0A21E; the stripe in orange, gold and avocado; the camcorder's chocolate brown; the
     Level 0 yellows, made a little warmer and simpler
   - the shapes: bevel size, how round, polygon budgets per kind of thing (small item about 1-3k triangles, furniture
     about 3-6k, carts and machines about 6-10k)
   - the textures: one texel density, texture sizes (512 for small things, 1k for big ones), painted patterns instead of
     photos, baked AO, dirt in creases and worn edges (the suit's bake)
   - brands: Overflow and invented makers only
2. **One switch for the whole look.** The world takes its materials from a few places: `OfficeBiomeAssets`,
   `MaterialMaps.For`, `BakedMaterials`, `FittingMaterials`, `FurnitureMaterials`. A `StyleSet` asset puts a stylised
   twin beside each old material and model there.
   - a setting (and an F5 tab) flips between the old and new look live, for side-by-side pictures
   - the benchmark's `-benchmark-ab` can compare the two looks
   - nothing old is deleted until the new look is approved
3. **A baseline benchmark** of today's build (`-benchmark`), so every phase can show what it saved or cost.
4. **The picture:** keep the camcorder look, as approved over the lab. The lab's own bands and outlines don't stack
   under the camcorder's lens distortion. They are left out, and banding inside the camcorder chain gets a test later.

## Phase 1: the Level 0 shell (seen everywhere, every second)

The surfaces the generator builds every chunk from:
- walls (wallpaper, painted wall), carpet, ceiling tiles and the T-bar grid, baseboards, half-wall caps
- the light panels (lens and housing) and strip lights
- doors and frames, wall plates, outlets and switches

The work:
- New painted textures from the lab's `SL_Wallpaper`, `SL_Carpet` and `SL_CeilingTile`: a bigger, simpler pattern,
  at lower resolution than now.
- The stylised materials skip the parallax and relief step (`MaterialMaps` surfaces). That should save GPU time; the
  benchmark will show how much.
- The world's own marks (wear, grime, damp, paper, floor marks, ceiling stains) toned to match: fewer and bolder, not
  photo dirt.
- Checked on the lighting tour spots and in the biomes that use the shell: Hallways, Offices, Living Rooms, Channels.

## Phase 2: what you carry and use (certain in Overflow)

1. **The camcorder:** the lab's model as the one in your hands and on co-op players.
2. **Carts:** the lab's platform cart replaces the platform truck. The utility and janitor carts are remade to match.
3. **Boxes, pallets, racks and shelving:** the storage contracts depend on them.
   - boxes: `CardboardBoxGeometry` with chunkier flaps and tape, and an Overflow print on the box atlas
   - pallets: `PalletGeometry`
   - the rack uprights, beams, labels and base plates
4. **Items:** backpack, tape, spray can, hammer, nails, chalk, lantern, boombox, cords and plugs, sleeping bag, rubber
   chicken. Their Blender scripts are re-run with the style sheet's rules.
5. **The glove grip:** held items fitted to the suit's bigger glove.

## Phase 3: fittings throughout Level 0

Exit signs, extinguishers and cabinets, fountains, vending machines, the breaker panel, thermostats, door closers, floor
boxes, partitions, the counter and the sinks: about 90 models from `build_fittings.py` and friends through `bake_kit.py`.
- Give `bake_kit.py` a stylised mode: flat colour per slot plus the suit's baked AO, creases and wear, at the style sheet's
  texel density.
- Then re-running each build script remakes its fittings in the new style, mostly by tuning shapes, not starting again.
- Biggest and most seen first: vending, exit signs, extinguishers, doors.

## Phase 4: furniture and the rest

Sofas, armchairs, chairs, tables, lamps, the office chair, the clock. They belong to the offices and living rooms, which
probably carry over. Done after phase 3, with the same bake.

## Phase 5: people and screens

- A face picker (eyes, nose, brows, hair, facial hair, glasses, skin and hair tone, mask) in the menu, sent to the other
  players in co-op (today a friend's face comes from their player number).
- The menus and camcorder screen in the company type and colours.
- Later: voice chat moving the mouth (`HazmatAppearance.talk`), monsters in the style. (Both 2026-11-07, Phase 6.)

## Not now (after Overflow's design is settled)

- World generation for Overflow (new layouts, contract spaces, the noclip import and export walls)
- the hub and the training room
- (done 2026-11-07: the Poolrooms and the water park, the orientation video's sets)

## How each phase is checked

1. Build it behind the switch.
2. Pictures of the same spots, old and new, through the camcorder.
3. The benchmark, old against new.
4. Your yes, then commit, then the old assets for that phase are retired.

## Decisions (2026-11-06)

1. **The picture:** the camcorder look only.
2. **Textures:** simple painted patterns with baked dirt.
3. **Carry-over:** everything may carry over; the remake starts with the Level 0 halls and the warehouse.

## Status

### Phase 0 and the halls and warehouse: built (2026-11-06)

- **Style sheet:** `Docs/StyleSheet.md`.
- **The switch:** `Generation/ArtStyle.cs` and `Generation/StyleSet.cs`, with the set at `Resources/Style/Overflow.asset`.
  - When a world loads, `WorldStreamer` (and build mode's `BuilderApp`) take a copy of the biome's material list with the
    style set's materials in its slots. Everything made from them follows: the stained, cracked and other-batch ceiling
    tiles, the odd carpet tiles, the homes' carpets, the troffers, the light grid's surface colours.
  - Saves name materials by slot, so a save loads in either look.
  - Settings > Graphics > ART STYLE (Overflow, the default, or Classic), taking effect when a world loads.
  - `-art-style classic|overflow` on the command line picks one for a run (for the build benchmark).
- **The new materials:** `Materials/Overflow/OF_*.mat` (14): wallpaper and the crawl tunnels' copy, painted wall,
  plaster ceiling, carpet, ceiling tiles, the T-bar grid, the troffer housing, wood (half-wall caps, pallets, dark wood),
  cardboard, rack uprights and beams.
  - The warehouse's concrete is made in code, from the set's `concreteAlbedo` and `concreteNormal`.
  - The old `M_*.mat` are untouched.
- **The textures:** `Tools/Textures/make_overflow_textures.py` paints them into `Textures/Overflow/`, at 1024 px (512 for
  wood) against the old 2048. In detail:
  - wallpaper: one broad stripe a tile with bold chevrons; the world adds the roll seams
  - carpet: soft mottling and flecks
  - ceiling tiles: grooves, a bevel, bold fissure dots, the odd yellowed tile
  - plaster: roller laps
  - concrete: patches, stains, two cracks, saw-cut joints every 3 m
  - wood grain, and kraft board
  - the carpet and ceiling textures are near neutral and coloured by their materials, as the old ones were, so the
    world's tinted copies come out right
- **Dirt:** the world's own wear (darker wall bottoms, worn carpet paths, grubby door edges, roll seams, bubbles, stains)
  was drawn in code already and suits the painted look. In Overflow the ceiling tiles' water stains are drawn only, with
  no photographed stamps.
- **Checked** in World 3 (erased after), same spots in both looks: `Docs/screenshots/overflow/compare_halls_warehouse.jpg`.
  - Brightness within a few percent of Classic; a little more saturated.
  - GPU time, editor, one run per spot:

    | Spot | Overflow | Classic |
    |---|---|---|
    | Hall | 4.26 ms | 4.32 ms |
    | Warehouse | 4.19 ms | 5.14 ms |

  - The build benchmark (`-benchmark -art-style classic` against `overflow`) is not run yet.
- **Not yet seen up close:** racks, pallets and boxes in the new materials (none in the checked spots); their models are
  still the old shapes (phase 2).

### Phase 2: what you carry and use: built (2026-11-06)

- **Remakes beside the old models:** `Resources/Overflow/...` holds the new look's version of a model under the same
  name. `ArtStyle.Path` / `ArtStyle.Load` pick it in Overflow (else the old one), so a model, its bake and its texel
  density always come from one place. The fittings, cart layouts, items, spray can, tape, boombox and backpack loaders go
  through it, and their caches reload when the look changes.
- **The Blender scripts' `--style overflow`** (in `build_items2.py`'s helpers, so every script built on them has it):
  - every box bevelled (bigger and rounder: 1.8x the old bevel, at least about 1 cm on big parts), tubes 15 % thicker
  - the palette: named slots to the style sheet's colours (`bake_kit.OVERFLOW_COLOURS`), the rest warmed a little and
    made a little more saturated
  - `bake_kit`'s painted bake: four flat patches instead of the soft mottle (the darker ones warmer), bolder worn edges
    with fewer gaps, firmer dirt in the creases, less grime, streaks and dust
  - run each script with `-- Assets/Backrooms/Resources/Overflow/<Fittings|Items> --style overflow`; then give the new
    files the old ones' import settings (copy the old `.meta` with the new guid: readable meshes, the textures' settings)
- **The camcorder:** the lab's model (`Resources/Overflow/Player/Camcorder.prefab`, origin at the eyecup, lens +Z) in
  co-op players' hands and as your own body's shadow (`Camcorder/CamcorderModel.cs`); the old box shapes in Classic.
- **Carts:** the trolley is the lab's teal platform cart at the truck's size, on the same casters and layout (it rolls
  and steers the same); the utility cart (warm dark brown) and the janitor cart (mustard) are the same designs remade.
- **Boxes:** rounder edges (1.4 cm), 60 mm amber Overflow tape that follows the edge over the top, and the Overflow print
  atlas (`Tools/Textures/make_overflow_boxprint.py`: the OVERFLOW label, the stripe, the company's mark, a fragile stamp).
- **Pallets and racks:** the pallets and rack hardware remade; the rack beams, braces and deck boards built with rounded
  edges (`MeshWriter.RoundBox`) in Overflow.
- **Items:** hammer, nails, chalk, tape, plug, cords, rubber chicken, lantern, sleeping bag, spray can, backpack, boombox
  remade.
- **The glove grip:** a co-op player's hand now puts its palm (not its wrist) on what it holds: six tenths of the way
  from the wrist to the knuckles, measured on the rig (about 7.5 cm in the suit's glove).
- **Checked** in World 3 in the Overflow look (`Docs/screenshots/overflow/phase2_*.jpg`): the three carts, the boxes,
  a pallet, a stand-in co-op player holding the camcorder, a warehouse rack (lit by a test lamp: that warehouse's lights
  were off), the items on the floor, the spray can in hand, the boombox. No errors.
- **Triangles, old -> new:** the boxy models rounder for a little more (casters 616 -> 1016, pallet 2.8k -> 3.4k,
  utility cart 2.3k -> 2.9k, janitor cart 5.6k -> 6.7k; the trolley 2.7k -> 2.6k). Most items are turned or swept shapes,
  not boxes, so they keep their shapes and counts and take only the new paint and colours (hammer, tape, sleeping bag,
  spray can). Over the style sheet's budgets already before: the spray can (18k), backpack (21k), boombox (23k).
- **Not yet:** the build benchmark; the items' own shapes made chunkier (their scripts' proportions, beyond the bevels);
  the held items through the first-person hands (the hands are the MakeHuman body's, not the suit's glove).

### Phases 3 and 4, the chunky shapes and your hands: built and checked (2026-11-06)

- **Chunky shapes** in the scripts' `--style overflow` (Docs/StyleSheet.md, "Chunkification"); every model re-run with
  it: fittings (`build_fittings`, `build_rooms`, `build_lights`, `build_vending`, `build_signs`, `build_chairs`), carts,
  warehouse, items, spray can, backpack, boombox, the door (`build_door.py` given its own rounded edges and weighted
  normals), furniture (`build_furniture.py` to `Models/Overflow`).
  - `build_fittings.py`'s models are not baked in this look (the game paints their labels itself): shapes only.
- **Furniture:** 14 prefabs in `Prefabs/Props/Overflow` (sofas, armchairs, lamp, office chair, table, clock) with the
  remade meshes and painted fabrics (`OF_Fabric_*`, `OF_LampShade_*`, `OF_TableLaminate`; the weave texture from
  `make_overflow_textures.py fabric`, colours darkened to keep the rooms' brightness). The style set swaps prefabs as well
  as materials now (`StyleSet.prefabs`, `ArtStyle.Biome`); saves still name them by slot.
- **Loaders:** the door (`OfficeChunkBuilder.Doors`) and wall plates (`WallPlateModels`) go through `ArtStyle.Path` too.
- **Hands:** the suit is your first-person body in Overflow and the MakeHuman body in Classic (you and co-op players);
  the camcorder hand and the free hand's grip scale with the hand (the glove is 9 % longer and thicker), and the camcorder
  hand sits under the new camcorder's body.
- **The last two:** the floral sofa (`build_floral_sofa.py --style overflow`: cushions a quarter rounder, piping as
  thick as the other scripts' cords; the owner's floral pattern kept, its cream upholstery the painted weave tinted to the
  old cream, `OF_FloralSofa_Cream`) and the wooden chair (`build_fittings.py`'s, moved to `Models/Overflow`, its worn
  wood `OF_WoodWorn`). Both in `Prefabs/Props/Overflow` and swapped by the style set (16 prefab swaps, 25 materials).
  Run the sofa script with `--tex` in a scratch folder: it writes its textures there.
- **The door:** its panels' sticking bead (7 mm) is left off in the new look. The 8 mm rounding mangled it and it showed
  through the frame's edge as dashed bright lines round every panel (seen in the game, fixed, the preview clean).
- **Checked** in World 3 in the new look (`LeftoversTour` and close-ups, `Docs/screenshots/overflow/phase3_fittings.jpg`):
  door and closer, kick plate, wooden chair (carry and sit work), ceiling lights, fountain, extinguisher cabinet, pull
  station, thermostat, strobe, partition, vending machine, armchair; furniture (`phase3_furniture.jpg`). No errors.
- **The glove** (`phase4_glove.jpg`, body mode set to Full for the test only, not saved): the suit's arm and glove take
  the spray can by the hand's reach and hold it with the fingers round it, from the eye, the side and the front.
- **Not seen in the game:** the fixed door (only its preview); exit signs up close (none came near a test spot).

### The build benchmark, old against new (2026-11-06)

A build (`Builds/StyleBench`, phases 0-4) run twice with `-benchmark -benchmark-set frameCap=0` and `-art-style classic`
then `overflow` (RTX 4070, Ryzen 7 5800X, 960 x 540, the shipped settings: ray-traced shadows and reflections on):

| Spot | Classic: median / p95, GPU | Overflow: median / p95, GPU | Triangles | SetPass |
|---|---|---|---|---|
| Corridor | 9.4 / 10.9 ms, 5.4 ms | 9.0 / 10.2 ms, 5.1 ms | 640k → 890k | 112 → 136 |
| Crawl hole | 8.1 / 9.2 ms, 5.4 ms | 8.0 / 9.7 ms, 5.0 ms | 344k → 435k | 95 → 121 |
| Walk (streaming) | 9.8 / 11.4 ms, 5.8 ms | 9.5 / 11.2 ms, 5.1 ms | 579k → 828k | 112 → 139 |
| Crawl through | 8.6 / 11.0 ms | 9.6 / 11.1 ms | | |

The new look costs nothing measurable: the GPU a little faster (the smaller painted textures), the frame the same, about
40 % more triangles (the rounded models) and a few more material changes (the new materials beside the old). No frame over
25 ms in either. The worst single frames (a chunk streaming in, the crawl) are within the run-to-run spread.

### Phase 5, people and screens: built, checked in the editor (2026-11-06)

(What was built is described below the checks.)

- **Checked** in Play mode (`Docs/screenshots/overflow/phase5_*.jpg`): the main menu and YOUR LOOK in the company
  colours (and the navy camcorder menu in Classic); six looks drawn on the ID card (each choice shows; skin and hair
  colour read only faintly through the visor's glare, as on the suit in the game); a stand-in co-op player wearing the
  look it was sent, and taking a changed one on the next frame; the look packed and unpacked unchanged. No errors.
- **Not checked:** two real games over the network (protocol 2), and the keys on the page by hand.

- **YOUR LOOK** (`Menu/LookMenu.cs`), from the main menu and the Escape menu: the name on the chest tape, eyes, nose,
  brows, hair, facial hair, glasses, mask, skin, hair colour and crew number, ◄ ► each, and RANDOM. Beside them the
  company ID card with your suited head and shoulders turning slowly, drawn live by a camera of its own on a stage 2 km
  below the world (no post-processing, two short-ranged lamps). Saved as it changes.
- **The look as data** (`StyleLab/PlayerLook.cs`): one number (2 bits a feature, 3 a tone, 7 the crew number), in
  PlayerPrefs `Backrooms.Look`. Your own suit takes it (and again when it changes).
- **Co-op:** sent with your name when you join (`Hello`), in the host's welcome and `PeerJoined` for everyone, and as
  `Msg.Look` when someone changes it in a game (the host passes it on): small shared state, as co-op games keep
  cosmetics, so a late joiner gets everyone's current look. A player who sent none (protocol 1) gets one from their
  player number as before. Protocol 2: both players need this version.
- **The menus in the company's colours** (Overflow look only; Classic keeps the camcorder's navy menu): chocolate
  panels with the amber frame and selection bar, warm cream type, the orange, gold and avocado stripe under the title
  (`MainMenu.uss`, `.mm-ov`; `MainMenu.ApplyLook`, `AddStripe`). The main menu, the settings page and YOUR LOOK; they
  follow the ART STYLE setting at once.
- **Then (2026-11-07):** the camcorder's F5 menu, its on-screen text and the Escape menu took the company look too
  (Phase 6 below).

### Phase 6, the rest of the look: built (2026-11-07)

(The owner, 2026-11-07: "do all except making the hub or new rooms".)

- **The Escape menu and the room builder** (`ImUi`): its colours follow the art style (chocolate panels, the amber
  accent, cream type, the company stripe under MENU and an amber frame round the dialogs); the builder's backdrop, cards
  and picture wells too. The Escape menu's box had been one row short since YOUR LOOK was added (QUIT hung below it):
  fixed. `Docs/screenshots/overflow/phase6_menus.jpg`.
- **The camcorder's F5 menu** (`CamcorderMenu.uss`, `.cc-ov`) in the same colours, and **its on-screen text** (REC, the
  counter, the date, the battery) in the company's warm cream with a chocolate shadow (`CamcorderOSD.ApplyInk`); the REC
  dot stays red. The navy menu and white text in Classic.
- **Checked in the game** (`phase6_level0_checks.jpg`): an exit sign up close, and the door with its bead left off (its
  panels clean, no dashed lines).
- **The shadow** (the one monster) checked in the new look and left as it is: a tall dark silhouette that reads against
  the painted walls; an outline or painted edge would make it a cartoon.
- **The Poolrooms and the water park** (`phase6_poolrooms.jpg`), behind the same switch (`_BROverflow`, set by
  `ArtStyle`):
  - the tiles (`PoolTile.shader`): painted in big soft patches (noise cut into four soft levels, the darker ones warmer),
    joints half again as wide with the dirt baked in, rounder pillowed edges, less set off true, the glaze a little softer;
    the light through the water as bold soft shapes in two levels (its average kept);
  - the water (`PoolWater.shader`): its colour in soft bands by depth (as stylised water takes its colour from the depth
    through a stepped gradient), the floor through it a little flatter, and a solid creamy foam line where it meets walls,
    rails and legs;
  - the rails, ladders, pipes and posts built in code 15 % thicker, thin ones a fifth (`ChunkyR`, as the style sheet's
    chunkification); the noodles and the mushroom's stem as they were;
  - the modelled fittings remade (`build_pool.py --style overflow`: lifeguard chair, shower, ladder treads, stall
    brackets, coat hooks, bleacher caps and brackets).
- **Voice chat** in co-op (`Net/NetWorld.Voice.cs`, `VoiceCodec.cs`): proximity voice from each suit's head, round walls
  through the doorways (`SoundRoute`) and muffled through them (Steam Audio); the suit's mouth moves with it, yours and
  theirs. Sent when you speak (a gate over the room's noise) or while T is held; SETTINGS > SOUND: VOICES (CO-OP), YOUR
  MICROPHONE, TALK. Built-in IMA ADPCM at 16 kHz (64 kbit/s while speaking, nothing when quiet); Opus through Concentus
  (a third of the size) can replace it later, but is a download.
- **The orientation video's Level 0** (`Tools/OrientationVideo/anim.js`, `phase6_video_sets.jpg`): the wallpaper as the
  game's (mustard with lighter stripes and chevrons), the darker mustard carpet, a wooden door.
- **A co-op test on one machine** (`DebugTools.NetAutoTest`): a build started with `-net-join 127.0.0.1 -voice-test`
  joined the editor's World 3 and both sent a test voice. The guest arrived wearing its look, named on its chest, its
  mouth moving with the voice (`phase6_coop_guest.jpg`: its camcorder hides the face); each side heard the other from the
  other's head, about 0.13 s behind, nothing dropped once in. The first playback (a streamed clip) starved and chopped
  the voice; it plays now from a looping clip written just ahead of where it plays (as UniVoice does).
- **Not checked:** a real microphone (the test voice stood in), two machines over Hamachi, voices heard round walls in
  practice (it uses the boombox's routing), the menu keys by hand.

### Phase 7, everything in the hub's style: built (2026-11-07 to 08)

The owner liked the hub's look and asked for all of Level 0 in it. The hub's language (`build_hub.py`): seventies,
chunky boxes and fat tubes with big soft rounded edges, walnut plinths, few parts, the company's colours where the piece
is the company's.
- `build_furniture_hub.py` (`Models/Overflow`): both sofas, the armchair, the office chair, the floor lamp, the
  conference table, the wall clock and the wooden chair. Same names, sizes and slot orders.
- `build_leftovers_hub.py` (`Resources/Overflow/Fittings`): the shelving (avocado posts, amber beams, decks at the same
  heights so stock and stored boxes still sit on them), the lawn chair (a fat monobloc), the school chair (the hub's
  stacking chair) and the four cubicle partitions (fat channelled fabric, chunky frame and base).
- `build_floral_sofa.py --style hub`: the floral sofa on a walnut plinth with fat square arms, the pattern kept.
- `build_hub.py desk()` (HubDesk) for the desks; the real-world rooms' clock has a round face now.

### Next

Committed (2026-11-07). Left: the hub and the training room, and world generation for Overflow (new layouts, contract
spaces, the noclip walls), after the owner's go-ahead; a real two-machine co-op game with microphones.
