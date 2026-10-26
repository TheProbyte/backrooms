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

## 2026-10-26 10:40  Claude Code  The Poolrooms: a second zone (one biome), reached through a far crawl hole
- New zone + biome (Resources/WorldGen Zones/Poolrooms, Biomes/Poolrooms; ZoneRules/BiomeRules.Poolrooms). Zones gained
  style (Office/Pool), sealedEdges, pinned placement (square of districts around pinCentre, wins over Voronoi) and an
  entrance (far link entranceFrom -> entranceTo, read by FarLinks). Poolrooms: districts within 5 of (24, 0), ~1.1 km
  east, sealed, entrance from the spawn district. WorldGenSettings.Version 11. Docs/Poolrooms.md has the details.
- Plan: PoolPlan (ceilings, basins/decks/walkways, round columns, roof openings); no lights/props/outlets/landmarks;
  only the far hole, always from a dry deck. Builder: OfficeChunkBuilder.Pool.cs (two floor levels, stairs, water,
  light wells + sky, columns, per-cell map with baked bounce). Shaders: Resources/Shaders/PoolTile, PoolWater, PoolSky,
  Shaders/PoolCommon.hlsl. Sun is traced through the cell map in the shader (PoolSun is the C# copy), no shadow maps.
  Runtime: PoolWorld (map texture, light globals, zone fog per camera), PoolRipples (+ PoolRipples.compute) wave sim
  around the player, water footsteps (ProceduralSounds.WaterStep). F5 > PLACES > POOLROOMS ENTRANCE / POOLROOMS.
- Checked: EditMode 59/59 (PoolroomsTests: pinned/sealed zone, entrance both ways for 3 seeds, walkable plan with stairs,
  sun test; WorldLayerTests now finds its test zone by name). Played: crawled from the spawn district's hole into the
  Poolrooms (window shows them, arrival on a deck), wading ripples, screenshot tours (Debug/PoolTour). Editor 1080p: a
  Poolrooms hall ~3 ms median vs a Level 0 corridor 17.7 ms.
- Open: camera under water when crawling in a basin (surface is one-sided); the Poolrooms hole's duct mouth is dark
  (Backrooms light grid); building hum still plays there. PoolTour captures can come out flipped if the Game view is
  resized while it runs.

## 2026-10-26 06:50  Claude Code  Vent ducts, no outlets in holes, no AO through duct walls, portal hitches removed
- Duct lined with generated galvanized sheet metal (Resources/Textures/DuctMetal.png; CrawlDuct _ObjectUV mapping in
  1.1 m sections, metallic 0.25); room finish only as a fallback.
- CrawlPortals.ClearPlates: low wall plates on either face of a holed wall are dropped (they floated in the opening).
  Pair test asserts it.
- "AO through walls" on entering was the duct reading SSAO at the opening's screen position, which for its walls and
  ceiling showed the room behind the hole until the duct was in the depth prepass. Now only the duct floor seen from
  outside uses it (BACKROOMS_AO_HOOK in BackroomsLitForwardPass; undefined for Backrooms/Lit). Recorded entry: frame
  differences through the opening 0.55-0.86 (noise 0.95).
- Hitches: (1) first-use GPU pipeline builds (first run of a build: 309 ms before the hole, 48 ms entering; editor 53 ms
  entering): CrawlPortalSystem.Warmup draws every portal material state once with a zero-area triangle and renders one
  portal view with shadows in the first frames that have a hole; editor entry afterwards 22 ms. (2) the portal target was
  reallocated 44 times while crawling to the window (30-43 ms frames): sizes now in steps of the screen, 10 reallocations.
  Benchmark gained "crawl through" / "crawl through (far)" spots (slowest frames with where they happened) and
  CamcorderPlayerController.SetStance. Build, 1080p, two runs: crawl through worst 26.8 / 25.9 ms (2-5 frames over
  25 ms; before 11), far crawl worst 25.0 / 23.2 ms (0 over 25 ms).
- Checked: EditMode 54/54. The build runs used shaders already in the driver cache, so the warm-up's first-run effect is
  shown by the editor numbers, not the build. Vent covers (load while unscrewing) were not needed for this.

## 2026-10-25 23:05  Claude Code  Far crawl-hole links: a hole can lead ~200+ m away, loading only what its window shows
- FarLinks (lattice: district (1,1) of every 6x6-district square <-> its (4,4) district, ~210-250 m; FarLinks.Add for
  hand-placed links such as a future Poolrooms entrance). CrawlPortals.PlaceFar places each end from its own layout
  after the ordinary pairs (pairs unchanged); WorldGenerator.ResolveFarLinks finds the partner when a chunk is cut
  (only the other district's plan is generated); unresolved holes are dropped. WorldGenSettings.farLinkSpacing (6),
  Version 10.
- WorldStreamer sites (SetSite/ClearSite/SiteReady): small circles kept loaded away from the player, built after the
  player's rings. CrawlPortalSystem requests one per far hole within 26 m (released at 34 m): 20 m around a point 10 m in
  front of the partner, about 9-12 chunks (the fog is black by 34 m, so that is all the window can show).
- Light: FixtureLightManager.siteGrid, a second LightGrid (optional siteCentre, bindGlobals off, BindGlobals on demand,
  own compute copy) fed with the fixtures near the far site; portal cameras showing a far site bind it for their render
  only; the duct's far-end light samples it (PortalLighting.compute row offset). On the trip the grids swap roles in the
  same frame and the area left behind stays loaded for the way back. Portal cameras clear to the main camera's colour.
- Checked: EditMode 54/54 (new FarLinksJoinDistantDistricts; the pair test now skips far holes). Play Mode, seed 1234:
  hole (111.0,0.4,75.7) -> (252.6,0.4,271.3); site loaded and lit, window live; recorded crawl: frame difference at the
  trip 1.22 vs camcorder noise 1.18; 40 chunks streamed in over ~2 s after arrival; the way back loaded and lit.
- Open: ~10 frames of 31-37 ms (editor) when entering the duct opening (portal shadow maps + real-light handover at the
  far end; a near hole showed a similar hitch). The editor crashed once on leaving Play Mode inside the Steam Audio
  plugin (phonon, audio thread) - unrelated to this change, but a candidate for the player's quit crash with depth
  priming. A Poolrooms zone itself (generator, lighting) does not exist yet; FarLinks only routes to the ordinary world.

## 2026-10-25 22:10  Claude Code  Backrooms/Lit builds in minutes, duct lit like its rooms, AO in portal views, no black windows
- Shader variants: Backrooms/Lit's forward pass declares only keywords the game turns on (recorded in Play Mode: 24
  variants in a benchmark run); Forward+ and light layers are defines; main-light shadows, per-vertex lights, SH modes,
  soft-shadow levels, screen-space irradiance, decals, cookies and LOD cross-fade removed. New
  BackroomsLitVariantStripper (build only) drops lightmap variants (only the unreachable OfficeRoom_Prototype uses them),
  mixed reflection-probe states and soft-without-shadows. Look: the 24 recorded variants preprocessed to identical code
  (76/76 stages) after the first cut; the later main-light-shadow removal only drops a term multiplied by the main light
  colour, which is zero (no directional light exists). Build: 107 Backrooms/Lit variants (64 forward fp); first build 11 min, rebuilds 1-4 min
  (previous attempt: cancelled after 1 h 41 min).
- Benchmark (build, AO on, saved settings, RTX 4070): 1920x1080 median corridor 109 / crawl hole 93 / walk 120 fps (p95
  54 / 48 / 56); 5120x1440 (this machine's native) 42 / 43 / 57 (p95 34 / 34 / 36). 60+ median at 1080p everywhere;
  not at 5120x1440, where the cost is spread over the base per-pixel lighting (breakdown in Docs/Performance.md).
  Depth priming saves 3.5 ms there with an identical image but makes the player segfault on quit every time, so it is off.
  Benchmark gained -benchmark-set (per-run option/feature overrides for A/B in one build).
- Duct entrance line: the duct entrance is shaded with Backrooms/Lit's lighting at the matching point of the opening (real
  lights + shadows, grid, contact occlusion, SSAO read there), with the entry room's rendering-layer mask (real lights
  skip bit 0, so the duct got none before). Far end: light sampled each frame at the partner mouth (PortalLighting.compute,
  up to 4 ducts) instead of a constant. Ambient/far-end terms fade to zero at the lip. Lip at seed 1234 hole: room 27/46,
  duct 54/53 before, 27/42 after (shadowed/lit half).
- AO pop on coming out: Portal_Renderer now has the PC renderer's SSAO (PortalRendererSetup copies it; GraphicsController
  switches every renderer's SSAO). Backrooms/Lit DepthNormals uses a Backrooms copy honouring the portal clip (CrawlDuct
  depth passes too). Recorded crawl: warp-frame difference 1.32 (noise 1.29); same crawl with portal SSAO off: 3.13.
- Black windows: line of sight to a hole ignores props and the player capsule; a live view keeps rendering 0.5 s after
  losing it and keeps its slot unless another window is 1 m nearer; a window without a picture shows the light measured at
  its partner mouth instead of near-black. No dark window frames in recorded sweeps/crawl (could not reproduce the pop
  directly; these were the paths that produce it).
- Checked: EditMode 53/53. Play Mode recordings in Builds/lookcheck (not in the repo).
- Open: editor crashed (D3D12 fence timeout) when Backrooms/Lit was recompiled synchronously while paused in Play Mode;
  edit shaders outside Play Mode. Depth priming quit crash. 60 fps at 5120x1440 needs a decision (render scale/upscaling,
  panel emitters). Unity serialized Assets/Settings (DefaultVolumeProfile, PC_RPAsset, URP global settings) and
  ProjectSettings changes during the session; left uncommitted.

## 2026-10-25 17:45  Claude Code  Real crawl ducts behind holes, no black windows, lights ready before you arrive, 60 fps pass
- Crawl holes now lead into a 2.2 m duct behind the wall (CrawlPortalView, CrawlDuct/CrawlMask shaders). The duct overlaps
  the room behind but is drawn only through its own hole (stencil bit 64, after opaques) or while the player is inside;
  its colliders exist only at/in the hole; the back of the wall gets a patch of that room's wall (hidden while inside).
  The portal window moved to the duct's far end. Both hand-overs happen near-clip + 3 cm early (the near plane cut off the
  window: two flat brown frames). The old spill light is off (_PortalSpillCount 0): the window now sits in the room behind.
- Light pop on arrival: real shadowed lights only existed around the player, so the far side was grid-lit and real lights
  faded in after the trip. CrawlPortalSystem.TryGetLookahead gives the light pool a second centre at the partner hole.
- Black windows: a window with no picture shows a dim fallback, never black; inside view always gets slot 0; portal
  cameras only render shadow maps for the window you are at (each camera re-rendered the whole shadow atlas).
- Performance (editor, 1080p, seed 1234 corridor pose, interleaved A/B, PerfAB.cs): saved settings 50 fps (GPU 17.4 ms);
  shadow lights 48 -> 12: 59; SSAO off: 65; both: 74. SSAO cost is its extra DepthNormals scene pass (half-res AO barely
  helps). Per-pixel distant light ~4.8 ms, panel emitters ~2 ms, contact occlusion ~0.4, reflections/SMAA/camcorder pass
  < 0.5 each. New: per-light near/far split (LightGrid.exactDistance, F5 LIGHT "Exact light radius" 4.5 m): each fixture
  exact per pixel within 4.5 m of a surface, its weak far part from the 0.6 m texture (matching weights in LightGrid.compute
  and LightGridSample.hlsl). Image vs exact-everywhere: mean abs diff 0.07/255, p99 0.7. Its speed gain is NOT measured yet
  (another game was using the GPU at 97% during the run; re-measure). Direct-texture work is now queued under a texel
  budget per frame (a full refresh spreads over ~4 frames) and handover redraws are coarse in split mode.
- Graphics options v2 migration: shadowLights capped to 16, shadowDistance to 20 (were 48/30 in saved prefs); presets
  updated. Tooltip explains ~16 looks the same (walls already stop light exactly).
- Checked: EditMode 53/53. Play Mode: 600-frame recording of a full crawl (FrameRecorder.cs) has no frame-to-frame jump
  above camcorder noise at either hand-over; from the room behind, the wall is solid and nothing of the duct shows.
- Open: Unity crashed once (D3D12 fence timeout) on entering Play right after the shader edits, with shaders compiling;
  not reproduced on restart (that start stalled ~90 s compiling Backrooms/Lit variants, then ran). Assets/_Recovery is
  Unity's crash backup, not committed. Re-run the A/B with the GPU free and in a player build.
- 18:30 follow-up (GPU free): editor A/B at the corridor pose: old saved settings 48 fps (GPU 18.6 ms), shadows trimmed
  but exact light everywhere 53 (17.3), new defaults 66 (13.8), new defaults without SSAO 81 (12.2). The near/far split
  saves ~3.5 ms GPU. Added DebugTools.Benchmark (-benchmark [file]) and Docs/Performance.md. A player build was started but
  the editor was closed after 1 h 41 min still compiling Backrooms/Lit variants (build cancelled): the shader has a very
  large variant count; first builds are slow until that is stripped.

## 2026-10-25 16:11  ChatGPT  Wall-contained portals, bounded views and cached light transport
- Crawl portals now cross/render at the entrance wall face; removed the 2.3 m protruding duct. Reverse faces are complete
  ordinary walls. A wall-contained seal collider ignores only an aligned crawling capsule with a loaded counterpart;
  leaving/disabling restores collision. Position/yaw/velocity mapping and an arrival latch prevent immediate re-entry.
  World version 9 records the geometry change; existing placement spacing and 12-30 m links are retained.
- Views prepare after movement/shake and use URP's ordinary camera loop (two-view budget, no recursion). Destination
  geometry clips in a portal-only shader variant, preserving perspective depth for Forward+ and keeping discards out
  of normal world/builder shading. Actual camera completion reveals a new view; near-plane crossings stay visible.
  Cropped off-centre render targets cover only the opening, with projected-size density/quantization/hysteresis.
- Added PortalLighting.compute and explicit live-grid bindings: six directional samples for at most eight nearby mouths
  in a 6x8 half-float GPU cache. Replaced expensive per-pixel destination-light scans from the intermediate draft.
  Soft diffuse spill has a 3 m extent; a 0.6 m reciprocal threshold blend joins illumination. Local walls/props restrict
  it; real/grid share changes do not dim it. Fallbacks clear portal globals and bind an empty irradiance texture.
  Docs/Portals.md describes implementation, costs, approximations and checks; BuildMode.md describes the new geometry.
- Checked in Unity 6000.4.5f1 / DX12: final EditMode suite 53/53 passes (aa98d3504f4043dba7ca4f69c41abfca). New tests cover
  all orientation/negative-coordinate frame mappings, contained seals/collision cleanup, crop UV/depth identity, actual
  GPU full-output/power-off/out-of-window samples, fallback clip-state reset, and initial crossing/jitter/immediate-retreat
  regression. The arrival guard permits intentional reversal before its 8 cm clearance. Final shader messages empty.
  Native Play Mode: both crossing directions passed; view remained live 0.02 m from the plane; reverse capsule stopped
  at depth -0.48 m with collisions enabled. Inspected portal/threshold/reverse-wall and an unsaved BuildMode preview.
  Final builder: no portal RTs left, clip keyword off, spill count 0, no console errors/warnings. No room saved.
- Warmed same-pose 1080p seed-1234 editor samples (180 frames, saved quality retained): full-camera draft CPU median/p95
  32.64/41.42 ms, GPU 29.33/39.07; final cropped 896x896 view CPU 22.63/30.17, GPU 21.15/28.73 (~31 -> 44 FPS).
  Separate Play Mode runs; not standalone or a general locked-60 claim. Captures/raw results are outside the repo in
  C:/Users/kadej/Documents/Codex/2026-10-19/i-wan/outputs/portal-continuity (before-crop, after-crop, final JSON/PNGs).
- Cyan was reproduced as the editor's loading shader during cold compilation; the warmed builder and portals render
  normally. EditorSettings.asyncShaderCompilation remains true/unmodified; temporary ShaderUtil flag returned to false.
  A preview command-buffer workaround produced scope warnings and was removed. Cold shader edits can still show cyan
  or stall until compilation completes. No ProjectSettings/material edits were included. Unity serialized additional
  material/TimeManager changes while validating; these remain unstaged with the earlier dirty assets.
- Open: manually test angled crawls/turning/backing out, long streaming routes and multiple visible pairs in a player
  build. Two live views/eight lit mouths are budgets, not unlimited recursive portals. Light transport is a coarse
  diffuse approximation; distant links outside existing streaming/light-grid range, recursive specular/GI and prop
  traversal need more work. Lighting/material boundaries are reduced, not a claim of physically exact transport.
  Branch chatgpt/portal-continuity, local commit only; no push or PR.


## 2026-10-25 05:32  ChatGPT  Recover FPS without lowering lighting quality
- Replaced the per-pixel/per-real-light fixture-list scan with a cached half-full position lookup (735 lights: 8 KB,
  mean 1.22 probes, max 6). Rebuilds on position/count/order changes, owns/releases its buffer and binds editor fallback.
  Added conservative finite-emitter range/cone rejection before rays; layout-blocked real fixtures skip soft shadow-map
  samples. Deferred distant furniture rays until a light survives those checks. Same BRDF, quadrature and quality budgets.
- Native validation exposed a separate reflection queue deadlock: world replacement disabled an unfinished probe, and
  distant moves could do the same. Replacement now cancels old captures; moving keeps the current capture enabled until
  completion. Added lifecycle regression. Details/costs/caveats: Docs/FpsRecovery.md and RuntimeLighting.md.
- Checked Unity 6000.4.5f1: full EditMode suite 48/48 passes (a2e67eac87144c9790745de632ed342d), including actual GPU lookup
  collisions/reordering/removal/misses and finite-sample rejection checks. No Lit messages or final runtime warnings.
  Native capture-before-reset and capture-before-40m-move recovered (8 captures, 1 active probe, none pending). Inspected
  before/after corridor and office images. Final stop retains 8 valid fallback buffers; original seed 0/settings restored.
- Same-view 1080p editor sample, seed 1234, saved high options retained: CPU median/p95 31.05/37.51 -> 18.11/24.40 ms;
  GPU 24.03/34.20 -> 11.85/23.39 ms (roughly 32 -> 55 FPS). Final 180 samples with reflections active/contact 0.65/panel 1;
  settings before/after identical. Same 735 fixtures, 38 shadowed; loaded chunks 61 before / 60 after, queues empty.
  Larger office: CPU 20.25 ms, GPU 13.92 ms median (~49 FPS); one reflection refresh. Raw data/screens: Temp/FpsRecovery.
- Open: editor measurements are not a locked-60-FPS or standalone benchmark. Still manually check a player build, long
  streaming/portal routes and lower-end GPUs. Earlier unresolved native allocation diagnostic remains open (see previous entries).
  Unrelated material/ProjectSettings edits preserved. Local branch chatgpt/lighting-performance; no push/PR.

## 2026-10-24 19:46  ChatGPT  Close fallback resource lifetime and finish lighting delivery
- LightGridGlobals now releases seven fallback buffers/two textures before reload, lifetime reset and quit; guards late
  OnDisable allocation and rebinds safely in gridless/editor scenes. ExitingPlayMode retains bindings until rendering
  stops (an earlier release caused two unbound-SRV warnings). Added release/rebind regression. PauseMenu recaches its
  visual menu after reload; new occluder stable sorting uses Unity 6 EntityId. Updated runtime/review documentation.
- Why: finish ownership recommendation and avoid native storage escaping managed reload without breaking builder draws.
- Checked in Unity 6000.4.5f1: final full EditMode suite 45/45 passes (job f78460c92c7d429a90dacb6f627ffa25). Final isolated
  world -> BuildMode new unsaved preview -> stop: zero console warnings with stack-trace leak detection enabled; all
  seven fallback buffers valid, grid disabled in builder, preview geometry rendered. No blueprint saved. Original seed 0,
  async compilation false, leak detection Enabled and reflection quality false restored; original scene not dirty.
  Earlier 11-view tour, portal render/warp, upper-storey bounce, mesh stress, shadow fades and timing data are in the
  previous entry and Temp/LightingPerformance. Review documents the complete 15-item delivery map and measured costs.
- Open: an earlier reload dumped native leak reports with unresolved managed symbols; exact cause is not conclusively
  isolated despite the clean final cycle. Check repeated reloads in a fresh editor session. No standalone benchmark:
  manually test long travel/teleports, repeated portals, reflections between rooms, furniture near the 160-box cap and
  low-end GPU/shader startup. Area lighting/GI/reflections remain approximations and do add cost. Unrelated dirty assets
  retained. Local commits only on chatgpt/lighting-performance; no push or PR.


## 2026-10-24 16:08  ChatGPT  Furniture contact, bounded indoor reflections and full lighting validation
- Added LightingOccluder/LightGrid.Occlusion: tiled oriented furniture boxes (up to 160 within 14 m), local indirect/direct
  occlusion and floor-source blocking; follows moved props. F5 LIGHT saves Furniture contact and Indoor reflections.
- RuntimeIndoorReflections owns two 128px room-bounded probes, one time-sliced capture at a time; excludes portal quads,
  shares temporary reflective material variants, restores/releases them on disable, and restores the original runtime
  realtime-probe quality flag. Includes Unity 6000.4 UUM-129405 texture-update workaround. No base material/settings edits.
- Finalized bounce isotropic moments/fixed ceiling source (no repeated energy injection), locomotion-root grid anchoring,
  exact faulty-tube waveform, serialized generated-mesh ownership, startup upload timing and cancelled-search feedback.
  Docs/RuntimeLighting.md and PerformanceLightingReview.md explain all 15 recommendations, costs and approximation limits.
- Checked: Unity full 44-test EditMode suite passes, including real GPU source/diffusion readback, moved-box invalidation,
  room-boundary and shared-material tests. Lit/compute have zero messages. Replayed all 11 seed-1234 tour poses; F5 search
  safely jumped 486 m upstairs (65 chunks, queue 0); live upper bounce readback >0. 200 rebuilds released all 1,000 meshes.
  Forty shadow-transition frames: 54 maps maximum, 12 strength changes, zero rate violations. Moved-prop revision, probe
  toggle/capture/texture updates and disable-time material/quality restoration passed. Live portal view and warp passed.
- Profiling: SRP batches confirmed; kept batching unchanged. Final 1080p, 120-frame seeded editor sample: CPU median/p95
  31.03/37.66 ms, GPU 24.08/34.10; point-emitter comparison 29.62/36.55 and 22.78/32.70. Different from original audit pose;
  no overall FPS gain or standalone benchmark claimed. Raw captures/reports: Temp/LightingPerformance.
- Open: a native Persistent allocation warning (7 allocations) appeared during editor cycles. Fallback globals still need
  explicit teardown; this is being investigated next. Area shadows/GI/reflections remain bounded approximations; long
  travel, repeated portal routes and low-end/standalone profiling remain manual checks. Unrelated dirty assets retained.


## 2026-10-24 15:43  ChatGPT  Runtime finite fixtures, consistent PBR, two-storey surface-aware bounce
- Extended the runtime grid/shaders with finite panel/tube emitters at constant output and a saved F5 Panel size control.
  Real and per-pixel grid shares use the same URP BRDF/contrast/diffuse wrap instead of grid direct being bakedGI.
  Shadow strength now fades independently with distance/hysteresis and an explicit 64-map admission budget.
- Added separate floor/wall/direction bounce atlases for both storeys, with wall-aware filtering across atlas seams.
  Existing floor/wall/ceiling finishes supply cached linear reflectance on the main thread; wall patches retain direction.
  Grid records use stable position order so distance sorting does not invalidate unchanged full-power bounce.
- Why: implement lighting recommendations 1–5 and choose the compute grid as the procedural runtime GI path (8).
  Added Docs/RuntimeLighting.md and corrected ReferenceMatch.md's APV claim; no baked-only Area light workaround.
- Checked: Unity full EditMode suite 41/41 passed, including a GPU kernel/readback test for upper-floor bounce, floor-slab
  isolation, coloured/darker floors and west-wall direction. Compute/Lit compiled; all 11 seed-1234 fixed-pose tour views
  captured in Play Mode with no runtime errors. A Frame Debugger capture confirmed SRP shadow batches (Temp/.../batching.tsv).
- Open: four-sample finite lighting has measurable GPU cost; first tour snapshots ranged roughly 18–33 ms GPU in editor,
  not comparable to the earlier single-pose benchmark. Reduced repeated architectural visibility and atlas taps after
  that tour; final warmed profiling remains. Shadows use central soft maps, dome shape and indirect transport are
  approximations. A shader warning about a potentially uninitialized atlas sample needs checking. Furniture indirect
  occlusion, indoor reflections, batching detail and final profiling remain for subsequent commits. No push/PR.


## 2026-10-24 15:24  ChatGPT  Remove redundant lighting work and phase streamed chunk uploads
- LightGrid skips unused Direct compute in per-pixel mode; stable lights reuse uploads, bins and bounce. Full refresh
  is retained on texture-mode switches, power/shape/window/occupancy changes. Added CPU submission/dispatch counters.
- GeneratedChunkResources owns combined meshes in chunks/previews (imported meshes stay shared); thumbnail/line-material
  lifetimes are explicit. Portal lists/frustum points/player lookups and pause-menu lookups are reused/cached.
- Landmark caches evict deterministically; F5 searches scan incrementally with feedback/cancellation. WorldStreamer builds
  streamed chunks while inactive, yields between mesh/prop/collider uploads, then publishes complete geometry; startup
  stays immediate. DISPLAY/WORLD distinguish worker preparation from accumulated main-thread upload time per chunk.
- Why: implement efficiency recommendations 1–6 without reducing geometry, light range, resolution or shadow quality.
- Checked: Unity full EditMode suite 40/40 passed (four new ownership/cache/hidden-upload/eviction regressions). Played
  seed 1234 through all 11 saved LightingTour poses; queue drained (61 chunks), no runtime errors. Stable final-pose grid
  submission 0.054 ms with zero bounce dispatches; accumulated upload mean 3.56 ms per chunk (single observation, not FPS).
  Matching baseline/efficiency captures are in Temp/LightingPerformance; no image-quality/FPS gain inferred from one pose.
- Open: an individual mesh/collider upload cannot be preempted; initial spawn remains synchronous. Standalone profiling,
  rapid teleports, long travel, portal crossings and repeated builder edits still need broader validation. Lighting and
  batching recommendations remain for subsequent commits. Branch chatgpt/lighting-performance; unrelated dirty assets
  and ProjectSettings preserved. No push/PR.

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
