# Status (where to resume)

Last updated 2026-10-22. Read `Docs/ProjectBrief.md` for the vision.

## Lighting system (current design)

- **All real-time** (owner rejected the per-chunk bake on 2026-10-22: wants live shadows, and a player
  model with shadows is coming). The bake was removed; it lives in git history (commit f74d06a).
- Ceiling lights (`FixtureLightManager`) light their own light zone via rendering layers (7 colours).
  A zone is all rooms joined by open-plan edges, and since 2026-10-22 it also includes the open-plan zone
  across a chunk border (`OfficeChunkBuilder.ComputeCellMasks`, using neighbour layouts from
  `WorldStreamer.CachedLayout`). That removed the straight dark lines at chunk borders.
- The nearest lights (24 by default) cast real shadows and then light every layer, so their light spills
  through doorways and is stopped by walls. Doorway lights and upward bounce fill are back on.
- Debug: `LightingTour` (add in play mode, call `Run()`) screenshots typical views including `7_arch`.
  Play mode needs `runInBackground` on while Unity is unfocused, or frames (and screenshots) stall.

## Look

- Wallpaper: owner's chevron stripe (`Textures/Wallpaper_Chevron_*`, 0.6 m repeat, normal derived from
  colour) applied by `InfiniteWorldBuilder.ApplyChevronWallpaper`. Rebuild with
  Backrooms > Build Infinite Offices Scene.
- Arched doorways: `BiomeRules.archedDoors` (offices 0.35, hallways 0.12, warehouse 0), round top with
  apex 2.3 m (`OfficeChunkBuilder.ArchFaces`). Interior edges only so both sides agree. World version 3.
- Camcorder preset "VHS '96" (index 5) is tuned to `references/update11`.

## F5 / F6

F5: UI Toolkit, styled as a 90s camcorder on-screen menu (navy box, white frame, amber highlight bar,
monospaced system font if installed). Styles in `Resources/UI/CamcorderMenu.uss`, panel settings
`Resources/UI/CamcorderPanel.asset`. F6: biome minimap under REC, playable while shown
(`CamcorderVisualMenu.Minimap.cs`). The OSD stays uGUI: it is rendered to a texture and bent by the lens.

## Open items (next steps, in order)

1. HD 3D audio: Steam Audio (HRTF + occlusion) proposed; waiting on the owner's OK to download it.
2. Reference room features not done yet: arched wall niches, curved walls, balcony (ref3_arches_balcony).
3. Owner may supply more textures; spec in `Docs/TextureSpec.md`.
4. Zone system is a placeholder (`WorldGenSettings.ZoneAt` returns "Level 0").
5. Movement (zoom F, crouch C, crawl Ctrl, jump Space) awaits the owner's feel feedback.
