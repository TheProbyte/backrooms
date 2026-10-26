# Main menu and world saves

## The main menu

The world scene (`Backrooms_Infinite`) starts on the main menu whenever no save slot has been chosen (pressing Play in
the editor, starting a build, or "Main menu" from the pause menu). Build mode's "test in the world" skips it.

- **The picture** is the camcorder's own, lying on its side on the carpet of a fixed world (`MainMenu.MenuSeed` = 42;
  the world uses the current F5 world-gen settings). `MainMenu.Place` picks a lit ceiling light near the spawn and puts
  the lens 7.5 cm above the carpet just behind it. It faces the deepest view that has other lights down it. The shot is
  tipped down 4 degrees and rolled 24 degrees. Focus is fixed on the carpet 0.6 m away (`CamcorderRig.focusOverride`),
  with an aperture of f/2.8, so the room beyond is slightly out of focus. The OSD shows STBY instead of REC
  (`CamcorderOSD.standby`).
- **No one holds the camcorder**: the player controller, character controller, hand, body, footsteps, handheld shake,
  F5 menu and L menu are all switched off, and the cursor is free.
- **The menu** (UI Toolkit, `Resources/UI/MainMenu.uss`) is styled like the F5 camcorder menu. It is a navy box with a
  white frame on the left of the picture, monospaced capitals, and an amber bar with a ► on the item under the cursor.
  It beeps like a camcorder (`ProceduralSounds.MenuBeep`).
  - **PLAY**: world select, with three slots.
    - An empty slot reads "EMPTY — START A NEW SAVE".
    - A used slot shows where you were, the time on tape, the seed and when it was saved.
    - **ERASE** (or Delete) empties a slot after a second press within 4 s.
  - **SETTINGS**: a placeholder that points to F5.
  - **QUIT**: stops Play Mode in the editor, or quits the game.
  - Mouse, or arrow keys / W S, Enter, Escape / Backspace to go back.
- **Between menu and world**, a camcorder blue screen ("PLAY ►", "PLAY ►  NEW WORLD", "STOP ■") covers the scene load,
  then fades out (`TapeTransition`).

## World saves

`GameSession` holds the slot being played. Choosing a slot reloads the world scene with that slot's seed:
- a new slot gets a random seed;
- the WorldStreamer adds a `WorldSaver`.

**Saved** (in `Application.persistentDataPath/Saves`, `world1..3.json` + `.paint`):

| What | How |
| --- | --- |
| Seed | `WorldSaveData.seed`. The world itself is always rebuilt from it. |
| Player | Position, yaw, pitch, stance (crawling in a duct stays crawling), noclip. Restored with `TeleportSafe` before the first chunks are built, so the world is built around where you were. |
| Moved furniture | `MovablePropManager.Export/Import`: every disturbed generated prop and every piece spawned from the L menu, with its spec (model, materials, size), pose and id. Prefabs and materials are named by their field in `OfficeBiomeAssets` (`AssetKeys`). Restored before any chunk builds, so the chunk that generated a moved prop leaves it out. |
| Fixed pieces from the L menu | Floor lamp and wall clock (`PlacedThing`), by catalog name. |
| Items | Spray cans by `WorldItem.itemId`, with the paint left in each; the one in your hand goes back into your hand. |
| Spray paint | Every dot and run (`SprayPaint.Write/Read`, about 46 bytes each). Runs that are still running are saved as they are. |
| Time | Play time; the OSD's tape counter and date carry on from it (`CamcorderOSD.TapeSeconds`). |

**When it saves:**
- when the game quits, including stopping Play Mode in the editor (`OnApplicationQuit`);
- "Save and quit to menu" in the pause menu;
- leaving the world for another scene (Reference room, Build a section);
- every 3 minutes, in case of a crash.

Files are written to a `.tmp` first and then moved over the old one.

**Not saved:**
- F5 settings and world-gen settings (they are global, as before);
- the wet state of paint (it is dry when you come back);
- anything that is generated from the seed anyway.

If the F5 world-gen settings or the seed change mid-game, moved furniture is forgotten, as before.

## Files

- `Scripts/Runtime/Menu/MainMenu.cs`, `TapeTransition.cs`
- `Scripts/Runtime/Saves/WorldSave.cs` (data, `SaveSlots`, `GameSession`), `WorldSaver.cs` (capture and restore,
  `AssetKeys`, `PlacedThing`)
- Hooks: `WorldStreamer` (seed, saver or menu, restore order), `PauseMenu`, `MovablePropManager`, `SprayPaint`,
  `ItemCatalog`, `CamcorderRig`, `CamcorderOSD`
- Tests: `Editor/Tests/WorldSaveTests.cs` (they use a temporary folder, never the real saves)
