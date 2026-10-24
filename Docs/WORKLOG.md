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

## 2026-10-24 14:28  ChatGPT  Performance and realistic-lighting review (recommendations only)
- Added Docs/PerformanceLightingReview.md with source-specific findings, measurement limits and an investigation order.
  Efficiency candidates: unused direct-grid dispatch in per-pixel mode; unchanged bounce/buffer recomputation; preview
  mesh ownership; portal allocations/lookups; unbounded landmark cache; upload/streaming spikes; batching investigation.
  Lighting candidates: finite panel emitters; upper-storey and surface-aware bounce; shadow-strength transitions;
  real/grid specular consistency; furniture contact occlusion; bounded indoor reflections. APV is baked indirect data,
  not automatic runtime GI for arbitrary procedural rooms; standard URP Area lights are baked-only.
- Why: requested audit alongside feature work, without implementing optimization or lighting changes.
- Checked: reviewed actual streamer/grid/compute/shader/portal/builder code and Unity documentation. 120 valid editor
  frames at 1080p: CPU median 16.81 ms / p95 31.58; GPU median 14.40 ms / p95 27.90. 62 chunks, 43 shadowed lights at the
  test pose. Light-manager CPU mean 1.28 ms (includes grid submit mean 0.25 ms). Not a standalone-player benchmark.
- Open: no gains claimed and none of the proposals implemented. GPU capture, multiple views, streaming routes and a
  standalone build are needed before prioritizing by measured cost. Final 18 CustomRoomTests pass; earlier full 35-test
  suite passed. Normal library restored; original Backrooms_Infinite scene reopened with Play Mode stopped. Feature work
  is local commits on chatgpt/build-mode-tools; no push or PR. Existing unrelated dirty files remain outside the commits.

## 2026-10-24 14:25  ChatGPT  Builder icons, tooltips, help and complete area copy/paste
- Added drawn tool icons, wrapped panel hints, hover tooltips, F1 help, and COPY/PASTE AREA buttons with Ctrl+C/Ctrl+V.
  Clipboard snapshots the selected painted area including edges/settings/lights/pillars/props/loot; click to paste with
  a green/red footprint, replacement of destination contents, and one-step undo/redo. Clipboard works across designs.
  Undo/redo now marks restored edits dirty (including after saving); help blocks editing and hides the GL overlay.
  Included the per-area finish picker UI from the preceding data/material commit; updated Docs/BuildMode.md.
- Why: make tools discoverable and let the player duplicate complete areas rather than repaint and replace every object.
- Checked: Unity ran the full 35-test Edit Mode suite successfully; the final 18 CustomRoomTests passed, including undo
  after save and snapshot isolation,
  nonrectangular shape, edge/settings/dressing transfer, repeat-paste deduplication and rejected out-of-bounds paste.
  In Play Mode invoked builder paste/undo/redo and wall placement, inspected palette/finish/help screenshots; tested a
  temporary saved design via Test in world and F5 MY ROOMS, verified arrival inside it with no capsule-obstacle overlap,
  and verified no-match feedback. No new runtime errors. Added a dedicated regression for undo-after-save dirty state.
- Open: UI interactions were exercised through Unity automation, not a human mouse session. Please hand-test wall-side
  dragging, scrolled tooltips, Ctrl+C/V between designs, undo after save, F1/Escape and Save/Test/return. Clipboard has no
  rotation/mirroring and replaces its footprint (undoable). Smoke files/captures are in Temp/ChatGPTChecks, not the library.

## 2026-10-24 14:16  ChatGPT  Custom room destinations and per-area existing finishes
- F5 > PLACES > MY ROOMS now finds the nearest generated custom placement (all designs or one enabled design), picks
  a cell clear of authored furniture/pillars/half walls, and uses TeleportSafe while chunks load. Reports no-match cases.
- Area wall/floor finish enums survive JSON, stamping, rotations and chunk slicing. Default preserves painted/wallpaper
  and carpet behavior; explicit choices reuse existing wallpaper/plaster, Reference Match cream/olive/carpet, wood and laminate.
  Reference material links are stored on OfficeBiome_Assets; Backrooms > Refresh Existing Room Finishes restores them.
- Why: saved designs must be easy to visit and areas need distinct finishes without creating or editing any material.
- Checked: Unity compiled; all 16 CustomRoomTests passed, including clear/blocked arrivals, finish round trips across four
  rotations/slicing, legacy material defaults and worker-thread mesh building. Play-mode checks follow after builder UI work.
- Open: search radius remains the existing ~10 districts. Arrival checks authored objects, not subsequently moved furniture.
  Wood/laminate reuse prop materials as requested; they are not new purpose-built floor textures. No lighting changes made.

## 2026-10-24 14:10  ChatGPT  Wall-placed props and explicit chairs in build mode
- Added racks, wall clocks, armchairs and office chairs to the builder palette. Racks/clocks snap to a solid wall on the
  clicked side (rejecting doorways and too-short runs); dragging reattaches them; clock mounting height is editable.
  Rack geometry, normals, tangents, shadow proxies and collision now rotate together. Existing enum values are preserved.
- Why: custom rooms need the existing prop library, including wall placement, in both preview and streamed chunks.
- Checked: Unity compiled; all 11 CustomRoomTests passed, including wall placement/rejected openings and worker-thread
  mesh building with rotated rack collision, explicit chair specs and clock height. Play-mode UI checks follow later.
- Open: no new models. Rack placement uses the floor projection near a wall, not a wall-surface raycast. World version
  bumped 7 -> 8 for changed generated geometry. Existing unrelated material/ProjectSettings changes are not in this commit.

## 2026-10-24  Claude Code  Real crawl tunnels; broader ceiling light and room fill
- Crawl holes are now real tunnels: each hole opens into a boxed crawlspace about 2.3 m long on the far side of the wall
  (solid, walkable around the outside). The crossing plane lies inside the tunnel (1.75 m in), so you crawl about 2 m in,
  cross, and crawl about 2 m out of the partner hole. Holes need two free cells behind the wall. Tunnel inside has a trace
  of self-light (`OfficeBiomeAssets.tunnel`, material made by Apply Reference Look To Office Biome).
- Lighting: new `FixtureLightManager.spread` / `GraphicsOptions.fixtureSpread` (default 0.8; 0 = old lamp-like cone, 1 = diffuse
  panel) and `LightGrid.roomFill` / `GraphicsOptions.roomFill` (default 0.6; lifts the wall bounce term in `LightGridSample.hlsl`).
  F5 > LIGHT has sliders for both; EVEN & PALE sets both to 1. Old saved settings pick up the new defaults.
- Checked: 27 edit-mode tests (new worker-thread build of a chunk with a tunnel: creating a Material in BuildData broke chunk
  builds once, so never create Unity objects there); played: looked down a tunnel, crawled through, A/B screenshots of the same room.
- Open: wall pools under fixtures remain (inverse-square near a point source); true global illumination (baked or probe-based
  indirect, per-room area-light shadows) is the next step if the look is still not enough.

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
