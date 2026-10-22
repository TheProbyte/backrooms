# Status (where to resume)

Last updated 2026-10-21. Read `Docs/ProjectBrief.md` for the vision.

## Lighting system (current design)

- **Walls, floors and ceilings are lit by a baked lightmap per chunk** (`ChunkLightBaker`, CPU, worker
  threads). It sees the chunk plus its 8 neighbours, so light crosses chunk edges with no seam.
  - Visibility: 2.5D grid walk through the wall grid (doors pass below 2.1 m, open edges below the lower
    ceiling) plus furniture/pillar boxes. Rooms are convex, so same-room rays only test that room's boxes.
  - Rooms joined by open-plan edges form a region; a light only reaches its own region or ones joined by a
    doorway. Light spills through doorways with soft penumbras, and never through walls.
  - Direct light + a small fill term + geometric contact AO. Directional lightmaps (dominant direction in a
    second texture) so normal maps still show relief. HDR (RGBAHalf), 4 texels per metre.
  - `RuntimeLightmaps` streams lightmap slots in and out with chunks (`ChunkLightmapHandle`).
  - Project settings needed: lightmap encoding High, lightmap stripping Custom with Plain + DirCombined kept.
- **Real-time lights (`FixtureLightManager`) now light furniture and props only** (rendering layers 1-7;
  architecture is on 9-15). Faulty flickering tubes also light their room's architecture so the flicker shows.
  The nearest 8 cast furniture shadows.
- Debug: `LightingTour` (add in play mode, call `Run()`) screenshots six typical views for review.

## F5 menu

UI Toolkit (Unity 6's runtime UI) built in code. Styles: `Resources/UI/CamcorderMenu.uss`; panel
settings `Resources/UI/CamcorderPanel.asset` with theme `CamcorderTheme.tss`. Helpers in
`CamcorderVisualMenu.cs` (Section, Slider, Toggle, Button, InputBox, hover tips); tabs in the partial files.
The camcorder OSD is still uGUI on purpose: it is rendered to a texture and bent by the lens shader.

## Open items (next steps, in order)

1. Owner is making textures; spec in `Docs/TextureSpec.md`. Wire roughness/AO when the first set lands.
   The CC0 set is kept but off (`InfiniteWorldBuilder.UseScannedTextures = false`).
2. Lighting polish: faint soft blotches on some corridor walls (likely contact AO near door headers);
   bake is about 0.7-1.4 s per chunk on a worker thread, could drop further.
3. Zone system is a placeholder (`WorldGenSettings.ZoneAt` returns "Level 0").
4. Movement (zoom F, crouch C, crawl Ctrl, jump Space) awaits the owner's feel feedback.
