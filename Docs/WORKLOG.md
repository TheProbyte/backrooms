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

## 2026-10-24  Claude Code  Crawl holes (first non-Euclidean feature)
- Pairs of low square holes (0.8 m) at the foot of walls. Behind each wall is a shallow boxed duct you can walk around,
  but crawling into one hole brings you out of the other, 12-30 m away in a room you could only reach by a long walk.
- Generation: `CrawlPortals` (placement, `WorldGenSettings.crawlPortals`, default 1.5 pairs per district), hole/duct/colliders in
  `OfficeChunkBuilder.Portals.cs`. Runtime: `CrawlPortalView` (quad over the hole), `CrawlPortalSystem` (portal cameras, oblique
  clipping, warping the player), `CamcorderPlayerController.Warp`, shader `Resources/Shaders/PortalView.shader`,
  `Portal_Renderer` (PC renderer without the camcorder pass; menu Backrooms > Set Up Crawl Hole Renderer).
- Checked: 26 edit-mode tests pass (new `CrawlHolesComeInFarApartPairsOnRealWalls`); in play mode looked into a hole,
  crawled through (arrived at the partner, still crawling, turned to match) and looked back through it.
- Open: both ends must stay within streaming / light-grid range (about 30 m); far-away or cross-storey holes need the
  streamer to load around the far end. Only two hole views render at a time. The URP motion blur may smear one frame on arrival.

## 2026-10-23  Claude Code  Build mode, custom rooms, office re-skin, colour flicker
- Added the Escape menu (`PauseMenu`), build mode (`BuilderApp*`, scene `BuildMode`), custom room blueprints
  (`RoomBlueprint`, `RoomLibrary`), their stamping into the world (`CustomRoomStamp`, `LandmarkKind.Custom`) and Test in world.
  See `Docs/BuildMode.md`.
- Office biome re-skinned (tan wallpaper, supplied carpet, square tiles and lights, no baseboard).
- F5: colour flicker per frame, Set as default (`CamcorderSettings.SaveAsDefault`).
- Checked: 25 edit-mode tests pass (7 new in `CustomRoomTests`); played the builder, a room test in the world, the menu, F5.
- Open: see "Limits of this first version" in `Docs/BuildMode.md`. The builder UI is immediate-mode (`ImUi`); the mouse
  tools were checked with simulated input, not by hand.
