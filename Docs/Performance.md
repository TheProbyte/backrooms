# Performance: what costs frame time, and how to measure it

## Measuring

- **In game:** F5 > GRAPHICS shows fps, CPU and GPU time live.
- **Editor A/B:** `Backrooms.DebugTools.PerfAB` cycles named settings several times (settle, then sample each), so
  drift from other programs affects every setting equally. Use it from editor scripts / MCP.
- **Build benchmark:** run the built game with `-benchmark [results.txt]` (`DebugTools.Benchmark`). It switches to seed
  1234, visits a long corridor, a crawl hole and a short walk that streams new chunks, waits for each to load, records
  600-900 frames each, writes median / 95th percentile / worst and quits. Default results file:
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\My project\benchmark.txt`.
- `-benchmark-set "a=1,b=false"` overrides graphics options for that run only (GraphicsOptions field names; nothing is
  saved), plus `depthPriming=Disabled|Auto|Forced` and `feature.<renderer feature>=false` for cost A/B in one build.
- The player remembers its last window size: pass `-screen-fullscreen 1 -screen-width 5120 -screen-height 1440` (or the
  size you want) so runs are comparable.
- Close games and other GPU-heavy programs first: anything else using the GPU slows the frame directly. That includes the
  editor in Play Mode (it halved the build's frame rate); an idle editor is fine. Back-to-back runs on a hot GPU are
  ~1.5 ms slower at 5120x1440 than the first run on a cool one.

## Measuring what limits a frame (2026-10-31)

- The benchmark now **plays** (a session of its own, saved in a folder of its own: `SaveSlots.FolderOverride`, the
  player's saves untouched). It used to run behind the title menu, which switches the player off (walking, body, hands,
  footsteps, camera shake): the walk did not walk and the frame missed what playing costs.
- Add `-benchmark-set frameCap=0`: in play the saved 60 fps cap applies (every spot read 16.7 ms).
- Each spot prints main thread, GPU, SetPass calls and triangles beside the frame time (frame timing stats must be on in
  the build: `PlayerSettings.enableFrameTimingStats`, switched on only for benchmark builds).
- `-benchmark-ab "label:key=value|key=value;label2:..."`: at the corridor, each setting in turn, three rounds, median frame,
  main-thread work (without waiting to present), render thread and GPU per setting. Keys: GraphicsOptions fields,
  `camcorderLook`, `hiddenShadows` (hidden chunks cast shadows or are switched off), `farClip`.
- `-benchmark-markers` (a development build): every profiler marker's time and a 90-frame profiler capture
  (`<results>_profile.raw`; open in the Profiler, or `ProfilerDriver.LoadProfile` and read each thread's hierarchy).
- `-benchmark-shots`: a picture of each spot next to the results, to compare two builds.

Read the three numbers together: the frame is as slow as the slowest of main thread, render thread and GPU. A GPU time
that does not move when the pixels change (full resolution, AO off) means the GPU is waiting to be fed, not working.

## The main-thread pass (2026-10-31, RTX 4070, 1080p, seed 1234 corridor, in play)

| | frame | main thread work | render thread | GPU |
| --- | --- | --- | --- | --- |
| Before | 8.82 ms (113 fps) | 8.55 ms | 8.15 ms | 7.53 ms |
| After | 7.02 ms (142 fps) | 3.59 ms | 6.63 ms | 6.16 ms |

Spots after: corridor 7.1 ms (140 fps), crawl hole 6.8 ms (147), walk 7.3 ms (137), crawl through 8.6-8.8 ms. The main
thread was the limit; it is now the render thread (submitting draw calls), the GPU close behind.

What was slow (a development-build capture, main thread, per frame):
- **The furniture contact-occlusion search** (`LightGrid.UpdateOccluders`): 1.7 ms. Every frame it walked every furniture
  owner in the world (1,546 in a warehouse area: every box on the racks) to find the boxes within 14 m. It now searches in
  slices of 256 owners a frame, and only after the eye has moved 0.5 m or 0.5 s has passed; the chosen boxes (at most 160)
  are still read every frame, so a carried box keeps its occlusion: 0.04 ms.
- **Steam Audio** (`SteamAudioManager.LateUpdate`): 1.7 ms. It simulates occlusion and transmission (8 volumetric rays and a
  wall test) for every registered source every frame, sounding or not: 68 registered, 1 playing. `Audio.SteamAudioSleep`
  takes a source out of the simulation after a second of silence and puts it back the frame it plays (it runs early in
  LateUpdate, before Steam Audio's own, so a new sound is simulated that frame): 0 awake while nothing sounds.
- **The fixture lights** (`FixtureLightManager.LateUpdate`): the candidate sort worked out two distances at every compare
  (about 15,000 square roots a frame): now each once, and the arrays sorted by them. Every real light had eight properties
  set every frame (each a call into the engine): now only what changed (`Slot.c*`, cleared on release).
- Profiler markers: `FixtureLights.*`, `LightGrid.*`.

Measured and left alone: switching hidden chunks fully off instead of shadows-only (no change), a 35 m draw distance (no
change), the contact shadows' depth (they read the camera's depth texture; no extra scene draw).

### GPU Resident Drawer (tried, not on)

Unity's own first recommendation for draw-submission cost (BatchRendererGroup instancing, set in the URP asset). A test
build with it (URP asset: Instanced Drawing; Graphics settings: BatchRendererGroup variants Keep All; static batching off)
ran the corridor in 7.4 ms against 11.9, but rendered the world black (only the camcorder's noise): Backrooms/Lit's DOTS
instancing path does not draw correctly yet. Turning it on needs that fixed, and the two settings changed (the Graphics one
is in ProjectSettings). Its first build compiled 2,560 Backrooms/Lit variants (about 20 minutes).

## Editor vs build

The editor adds its own work every frame (its windows, inspector, safety checks), and if the **Scene view** is visible
while you play, it draws the whole world a second time. A build has none of that. Use the build benchmark for real
numbers; in the editor, keep the Scene tab hidden behind the Game tab while playing.

## Chunks behind walls are not drawn (ChunkVisibility)

`Generation/ChunkVisibility.cs` (added by the streamer) works out every frame which loaded chunks the camera could see,
from the layout itself: about 250 rays across the view (wider when looking down) are walked over the 1.2 m cell grid
and stop at solid walls. A chunk is drawn if a ray enters it, if a ray stops at a wall whose far face it might own, or if
it is within 1.5 m of the eye. Hidden chunks keep casting shadows (their shadow casters switch to shadows-only, the rest
are turned off) and come back the frame they can be seen. Items, moved furniture, upper storeys and debug noclip flying
are never hidden. `ChunkVisibility.Enabled` turns it off.
- Spawn view, editor: 44 of 49 chunks hidden; 88 -> 107 fps (GPU 10.4 -> 8.0 ms).
- `Debug/VisibilityProbe.Run(folder, samples)` renders random views with and without it and counts differing pixels:
  150 views, 0 pixels different (a deliberately broken setting is caught).

## What costs what (2026-10-25, RTX 4070, 1080p, seed 1234 corridor, editor)

| Item | Approx. GPU cost |
| --- | --- |
| Each shadow-casting light (shadow map redraw) | ~0.1 ms; 48 lights vs 12 = ~2 ms plus CPU culling |
| SSAO (needs a full extra DepthNormals pass of the scene) | ~2-3 ms; half resolution saves almost nothing |
| Per-pixel distant light, every fixture exact everywhere | ~5 ms; with the 4.5 m near/far split ~1.5 ms |
| Square-panel emitters (4 samples within 6 m) | ~2 ms |
| Furniture contact occlusion | ~0.4 ms |
| Indoor reflections, SMAA | < 0.5 ms each |
| Camcorder picture (since 2026-10-26: all but the last pass at 640 x 480, whatever the screen) | below the editor's measurement noise at 1080p (an A/B of on vs off varied by more than the difference) |
| 4:3 frame | the camera's viewport is the 4:3 part only: 25 % fewer pixels at 16:9, 62 % at 32:9 |
| Removed with the camcorder rebuild | the exposure probe camera (a second scene render with shadows, 6 times a second), URP motion blur, always-on bokeh depth of field (now only when blur would show), HQ half-resolution bloom (now quarter, no HQ) |
| A crawl-hole window | one extra camera, cropped to the window; shadows only for the one you are at |
| Ceiling lights' shadow maps over 120 degrees instead of 150-179 (2026-10-27, World 3 seed 424242, a wide office view, interleaved A/B) | 1.9 ms *less* GPU (11.5 vs 13.4 ms): each map draws fewer casters |
| Panels as rectangles, with the panel-visibility wall test and the panel-shaped shadow filter, vs point fixtures (same view) | 1.6 ms (11.9 vs 10.3 ms); the panel-shaped filter alone ~0.2 ms over URP's |

Defaults (graphics options version 2): 16 shadow lights, 18-20 m shadow distance, exact light radius 4.5 m, SSAO on.
Editor result at the benchmark pose: old saved settings 48 fps, new defaults 66 fps (81 with SSAO off).

## Shader variants and build time

Backrooms/Lit is a copy of URP Lit with the light-grid lighting in its forward pass, so every variant is expensive to
compile. URP Lit declares about 10^11 keyword combinations; the first player build was cancelled after 1 h 41 min. The
forward pass now declares only what the game turns on (recorded in Play Mode with the editor's variant tracking:
24 variants in a full benchmark run) and `BackroomsLitVariantStripper` drops the rest at build time (lightmaps, which only
the unreachable OfficeRoom_Prototype uses; mixed reflection-probe keyword states; soft shadows without shadows). The
24 recorded variants preprocessed to identical code after the first cut; main-light shadows were removed afterwards (their
term is multiplied by the main light's colour, which is zero: there is no directional light). A build now compiles 107 Backrooms/Lit
variants (64 forward fragment): first build 11 min (128 then), rebuilds 1-4 min with the shader cache. The build log prints
"[Backrooms] Backrooms/Lit variants: ..." with the counts.

## Build results (2026-10-25, RTX 4070, AO on, saved settings)

| Spot | 1920x1080 median / p95 | 5120x1440 median / p95 |
| --- | --- | --- |
| Corridor | 109 / 54 fps | 42 / 34 fps |
| Crawl hole | 93 / 48 fps | 43 / 34 fps |
| Walk (streaming) | 120 / 56 fps | 57 / 36 fps |

At 1080p the build is CPU-bound (~9 ms) with spikes (p95 ~18 ms). At 5120x1440 (3.5x the pixels) it is GPU-bound.
Cost by removal at 5120x1440 (median ms, corridor / crawl hole / walk; noise about +-1 ms): with depth priming
19.8 / 20.0 / 16.5; SMAA, camcorder pass, shadow lights 16 -> 8, far light from the texture, contact occlusion and indoor
reflections each change it by 1.5 ms or less; square-panel emitters off 18.1 / 16.4 / 16.4; SSAO off saves about 3.5 ms.
The rest is the base lighting per pixel, so 60 fps at that resolution needs fewer pixels (render scale / upscaling) or
cheaper lighting, both visible changes.

Depth priming (PC_Renderer, Forced or Auto) saves 3.5 ms at 5120x1440 with a pixel-identical image (checked: 0 of 2.07 M
pixels differ), but with it the player segfaults on quit, every time (Unity 6000.4.5f1, URP 17.4, D3D12; nothing in the
player log, no crash dump). It is off until that is understood.


## Unity 6 features checked (2026-10-28)

Measured in play (editor, a typical office): render thread 10.7 ms, GPU 6.6 ms, scripts under 5 ms (Update 0.7,
LateUpdate 3.5), 222 SetPass calls, 0.75 M triangles, 58-60 fps.
- Upscaling (STP / FSR): not needed. The game already draws the 3D view at 1.35 x the camcorder's recording lines (not the
  window's), `GraphicsOptions.matchRecording`; the camcorder then processes at its own lines. STP would rebuild detail the
  camcorder throws away.
- GPU occlusion culling: covered by `ChunkVisibility` (rays over the layout stop drawing chunks behind walls), cheaper and
  exact for this world.
- GPU Resident Drawer: possible (Backrooms/Lit already has the DOTS instancing pragmas; the PC renderer is Forward+) and
  aimed at the render thread, the busiest part of the frame. Not switched on yet: it brings new shader variants that would
  compile mid-play (the stall that crashed the D3D12 editor, see LitShaderWarmup) unless added to the warm-up collection
  first, and the editor's render-thread time overstates a build's. To try: measure a build first, then add the
  DOTS_INSTANCING_ON variants to BackroomsLitVariants, warm them, set the URP asset's GPU Resident Drawer to Instanced
  Drawing and Graphics > BatchRendererGroup variants to Keep All.
