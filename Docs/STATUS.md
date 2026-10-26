# Status (where to resume)

The game is called **Still Recording** (working title).

Last updated 2026-10-26 (world version 13).

## Newest (2026-10-26)

- **Main menu and world saves**: the world scene opens on a camcorder-style main menu; three save slots, each world
  keeping its seed, the player, moved and spawned furniture, items (and what is plugged into what), spray paint, chalk
  and tape marks, things taken from special rooms, and the way back through a noclip door. Docs/MainMenuAndSaves.md.
- **Items** (L menu): spray paint, chalk, blue tape, desk lamp, extension cord, rubber chicken. Docs/Interaction.md.
- **Power**: wall outlets are live in Level 0; lamp cord 1.8 m, extension cords 8 m (up to 3 chained), physics ropes.
- **Crawl spaces**: every vent is a portal to a finite special room above the ceiling over it (closet, storage, camp)
  with loot. Docs/CrawlSpacesAndNoclipDoors.md.
- **Noclip doors**: one per 4 x 4 districts in Level 0, taped outlines; walk through into the Poolrooms with a camcorder
  glitch; the Poolrooms' taped walls lead back. They replaced the far crawl hole at spawn. F5 > PLACES > TESTING jumps
  to the nearest vent or door.

Earlier notes (some are now out of date where the list above says otherwise): Read `Docs/ProjectBrief.md` for the vision and `Docs/WorldGeneration.md` for how
generation and lighting work now.

## World generation (world version 7)

- Layered: zones (Voronoi, data assets) > biomes (density field per zone) > landmarks (sparse grid) >
  districts (4 x 4 chunks planned at once) > chunk slices (streaming only). See WorldGeneration.md.
- Rules live in assets under `Assets/Backrooms/Resources/WorldGen` (World, Zones/Level0, Biomes, Landmarks).
  Recreate missing ones with Backrooms > World Gen > Create Default Assets. F5 > World Gen edits the
  first zone live and saves on top (older saved settings are migrated).
- Landmarks: Colonnade hall, and the two-storey Atrium (the multi-storey test volume; the upper storey
  streams only around it). Seed 1234: atrium at cells (251, 363), colonnade halls at (-319, -20) and (357, -16).
- The atrium has a wing with stairs and/or an elevator up to its balcony storey (WorldGeneration.md,
  "Stairs and elevator"); pressing buttons is a hand animation (Interaction.md). Nothing is on the upper
  storey yet to reward getting there.
- Chunk coordinates are (x, storey, z). `WorldStreamer.ChunkLoaded/ChunkUnloaded` are the hook for
  registering Steam Audio geometry per chunk (not done yet).

## Moving things (branch grab-props)

- Sofas, armchairs, office chairs, conference tables and loose cardboard boxes (new, world version 5) can be
  grabbed with the left hand, dragged, swung, lifted at one end, tipped and shoved. See `Docs/Interaction.md`.
- Moved props are remembered for the session (they stay where you left them when their chunk unloads and
  reloads); saving to disk is not done yet.
- Placeholder first-person arm (watch, ring) and body from `Tools/Blender/make_player.py`.

## Lighting (all real-time, live shadows)

- Default **light grid** mode: the nearest 32 tubes (F5 "Shadow-casting lights", capped at 32), up to 6
  faulty tubes and the nearest lamp are real URP lights with shadows that light every layer (shadows stop
  them at walls). Every other fixture lights a GPU light grid (0.6 m texels, ~77 m around you) sampled by
  `Backrooms/Lit`, with wall-aware sampling and a bounce field for ceilings. Fixtures hand over between
  the two over 0.6 s. No doorway or bounce lights.
- F5 > Graphics > **Light grid** off returns to the phase 2 room-layer system (28 rendering layers in 4
  district-parity sets), kept as a fallback.
- All world materials use `Backrooms/Lit` (copy of URP 17.4 Lit + light grid, see shader header). After a
  URP upgrade, re-copy Lit.shader / LitForwardPass.hlsl and re-apply the two marked lines.
- Build Infinite Offices Scene now keeps an existing post profile (the owner's VHS '96 tuning) instead of
  recreating it.

## F5 menu tabs
CAMERA, PLAYER, WORLD, AUDIO, SOUND, DISPLAY, LIGHT (strength, reach, darkness, distant crisp lights, flicker,
pool shape; "Even & pale" preset), PLACES (jump to atriums, balconies, stairs, elevators, colonnade halls,
spawn; press again for the next nearest). Camera preset "Found footage" matches the reference series (sensor motion blur from the camera turn rate,
head-switching band, compression blotches are the new CAMERA sliders; `Debug/SpinProbe` turns the camera for screenshots).
V toggles noclip. Teleports hold you in place until the floor has loaded (`TeleportSafe`).

## Debug tools

- Backrooms > World Map (top-down zones/biomes/districts/rooms/light layers + consistency checks).
- Edit-mode tests in `Scripts/Editor/Tests` (14 tests).
- `LightingTour` (fixed seed, saved views, `compareLightingModes` for A/B shots with frame timings),
  `NavProbe` (long walks across borders).
- Play mode needs `runInBackground` on while Unity is unfocused. The MCP camera screenshot pauses the
  editor; unpause after it.

## Open items (next steps, in order)

0. Owner feel test: chalk, tape, lamp and cord handling, the noclip glitch, the special rooms' look. Special room
   loot is placeholder (whatever items exist); the settings page of the main menu is empty.
0b. Feel test of grabbing (Docs/Interaction.md lists the tuning values); a better hand model (the thumb sticks out);
   creatures moving things while you are away. (Moved props are saved to disk now.)
1. Owner review of the light grid look (Docs/screenshots/phase4_ab_*: `_layers` = old, `_grid` = new).
   Tunables: LightGrid.bounceGain / floorAlbedo / bounceSpread / wrap, FixtureLightManager.handoverTime.
2. The arched-niche/balcony room from references/update11/ref3_arches_balcony.png as a landmark kind
   (the reference image is not in this checkout).
3. Steam Audio geometry per chunk via the ChunkLoaded/ChunkUnloaded events.
4. Something worth finding on the atrium's upper storey. Elevator extras: sound (hum, ding, door), floor
   indicator, a stuck or flickering state. Stairs: a landing light, the camera could smooth the step bob.
5. More zones (the Poolrooms are the second; noclip doors can lead to any pinned zone: see NoclipDoors.TargetZone).
6. Movement (zoom F, crouch C, crawl Ctrl, jump Space) awaits the owner's feel feedback.
