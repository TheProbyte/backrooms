# Work log

Every agent that changes this project (Claude Code, ChatGPT, anyone) adds an entry at the **top**, newest first, in this
format, and commits it with the change:

```
## YYYY-MM-DD HH:MM  <agent>  <one-line summary>
- What changed (files / systems)
- Why
- How it was checked (tests run, scenes played, screenshots)
- Open problems or things the next person must know
```

Keep entries short and factual. Do not rewrite old entries.

---

## 2026-10-23  Claude Code  Build mode, custom rooms, office re-skin, colour flicker
- Added the Escape menu (`PauseMenu`), build mode (`BuilderApp*`, scene `BuildMode`), custom room blueprints
  (`RoomBlueprint`, `RoomLibrary`), their stamping into the world (`CustomRoomStamp`, `LandmarkKind.Custom`) and Test in world.
  See `Docs/BuildMode.md`.
- Office biome re-skinned (tan wallpaper, supplied carpet, square tiles and lights, no baseboard).
- F5: colour flicker per frame, Set as default (`CamcorderSettings.SaveAsDefault`).
- Checked: 25 edit-mode tests pass (7 new in `CustomRoomTests`); played the builder, a room test in the world, the menu, F5.
- Open: see "Limits of this first version" in `Docs/BuildMode.md`. The builder UI is immediate-mode (`ImUi`); the mouse
  tools were checked with simulated input, not by hand.
