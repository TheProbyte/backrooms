# Performance and lighting review â€” 2026-10-24, ChatGPT

Recommendations only. No optimization, lighting algorithm, shader, render-pipeline or quality-setting changes were made
for this review. The feature work is on `chatgpt/build-mode-tools`; see WORKLOG.md for implementation and checks.

## Measurement and limits

- Unity 6000.4.5f1, current PC URP settings, 1920 x 1080, running in the editor.
- A temporary 12 x 10-cell custom test design placed in the procedural world, 62 loaded chunks, 43 enabled/shadowed lights
  at the sampling position. F5 closed for the timing sample. This is one position, not a representative game benchmark.
- 120 distinct valid FrameTimingManager samples: CPU median **16.81 ms**, CPU p95 **31.58 ms**; GPU median **14.40 ms**,
  GPU p95 **27.90 ms**. Editor/tool activity and startup/streaming can affect these numbers.
- Existing light-manager CPU timer averaged **1.28 ms**, including its nested grid submission. Grid CPU submission timer
  averaged **0.25 ms**. The latter is CPU work/submission, NOT measured GPU compute duration; do not add it to the former.
- A separate Render counter snapshot reported 158 SetPass calls and about 704k triangles. Draw Calls/Batches counters were
  unavailable, so no draw-call claim is made. A GPU capture and standalone player profile are still needed.
- Raw timing JSON and smoke captures: `Temp/ChatGPTChecks` (local, not committed).

## Efficiency candidates that can preserve the current picture

1. **Skip unused 3D direct-light computation in per-pixel mode.** `LightGrid.Tick` still dispatches the Direct kernel into
   `_GridA/_GridB` when dirty. `BackroomsLightGrid` bypasses those textures when `perPixelDirect` is on, computing direct
   light from the tiled light list instead. Bounce uses its own source/diffusion kernels. Candidate: omit the unused
   Direct dispatch in this mode, retain occupancy/bounce/buffer bindings, and force a full refresh before switching back
   to texture mode. Check all consumers and mode toggles before claiming an equivalent image. Likely most useful during
   streaming, handovers and flicker; a completely steady frame already has little dirty direct work.

2. **Dirty-driven bounce and light uploads.** `LightGrid.Tick` recomputes the entire 2D BounceSource plus six default
   diffusion passes every frame. `SetLights` also rebuilds tile bins and uploads buffers every frame. Cache these when
   lights, power, window origin, occupancy and tuning parameters have not changed. Separate full-power bounce inputs from
   real/grid handover shares: a handover alone need not alter bounce. Retain immediate faulty-tube/power response and global
   resource re-binding every frame. Do not simply lower update frequency, which can cause lag or flicker artifacts.

3. **Release preview mesh ownership explicitly.** `BuilderApp.ClearPreview` destroys the preview GameObject but does not
   explicitly destroy the combined runtime meshes created by `OfficeChunkBuilder.Instantiate`. The world streamer does
   perform a generated-mesh cleanup on unload. Repeated preview rebuilds should release only generated meshes while
   preserving shared imported prop meshes. Also review the preview line material and thumbnail texture lifetimes.
   Verify mesh counts/memory over hundreds of edits; this is an ownership issue, not a reason to reduce mesh detail.

4. **Reuse portal scratch collections and cache lookups.** `CrawlPortalSystem.UpdateViews` allocates visible/wanted lists
   and frustum planes; `Unobstructed` allocates five test points; `TryWarp` searches for the player on render callbacks.
   Reusable collections/arrays and lifecycle-aware cached references can preserve behavior. `PauseMenu.LateUpdate` also
   performs a scene-wide menu search every frame. Profile allocations and multiple-camera callbacks before/after.

5. **Bound landmark cache growth.** `WorldGenerator.Trim` trims chunk/district caches but not `_landmarks`. Infinite travel
   and F5 landmark searches can keep adding entries. Deterministic eviction outside the useful region should preserve
   generation, provided worker readers are safe. F5 also synchronously scans up to 441 districts per search; consider an
   incremental/background search with a clear searching state if that becomes a visible hitch.

6. **Budget main-thread mesh uploads, not just worker preparation.** `WorldStreamer.Refresh` synchronously prepares and
   instantiates missing inner-ring chunks, and `Update` can only check its frame budget after an entire Instantiate call.
   `LastBuildMs` measures worker preparation, not the complete main-thread upload/collider cost. Instrument both. A phased
   upload or pooled object scheme could reduce spikes without reducing detail, while maintaining collision before access
   and avoiding visible partial rooms. Preserve immediate coverage after teleportation.

7. **Audit existing batching before changing materials.** Shared prop meshes do not automatically mean GPU-instanced draw
   calls; many materials have instancing off. PC renderer already uses Forward+ and SRP Batcher is enabled. Inspect actual
   batches, rendering-layer splits and shadow passes in Frame Debugger. Do not switch on instancing everywhere: compare it
   against SRP batching on these actual prop/material combinations. This is an investigation, not a promised FPS win.

## Lighting improvements worth prototyping

1. **Finite fluorescent-panel emission.** Current spots still behave like point emitters near walls; broadening the cone
   cannot fully remove the inverse-square hotspot. A rectangular-emitter approximation (analytic or a few energy-normalized
   samples) could produce broader wall washes and more convincing near-field illumination. Match the visible fitting's
   dimensions; keep total emitted energy consistent. Shadow treatment must match, and extra samples have a cost. Prototype
   on a small reference scene and compare at equal exposure before integrating it into world lighting.

2. **Bounce for upper storeys.** `LightGrid.compute/BounceSource` reads ground-storey occupancy and evaluates floor light at
   y = 0.05. `LightGridSample.hlsl` limits bounce to ground-level spaces and wells open above. Ordinary upper-floor rooms
   therefore do not receive equivalent generated indirect light. A per-storey bounce representation is a concrete
   improvement for stairs/atriums and future multi-floor environments.

3. **Surface-aware, directional indirect light.** Bounce currently uses one global `floorAlbedo` and a heuristic wall/ceiling
   facing term. A dark wood floor and pale carpet do not contribute their actual differing reflectance/colour to the bounce
   field. Room/material reflectance data and eventually directional multi-surface transport would improve colour bleeding
   and depth. Use lightweight published data on generation workers; never sample Material/Texture APIs there. Keep darkness
   and wall-occlusion tests intact. Raising roomFill alone cannot reproduce this transport.

4. **Fade shadow changes independently.** In `FixtureLightManager.Grid.cs`, `sh.shadowed` selects Soft/None directly and
   `L.shadowStrength` receives the global strength. The real/grid light share fades, but changing between shadowed and
   unshadowed real lights does not have its own strength ramp. Fade the shadow contribution with hysteresis and retain a
   slot until fade-out completes, within the shadow-atlas budget. Check moving furniture shadows and faulty tubes.

5. **Consistent material response across lighting tiers.** Grid direct light is added to bakedGI, while real lights use
   the PBR direct-light loop. Their diffuse/specular responses are not identical. Matte wallpaper conceals much of this,
   but laminate, metal and future wet pool surfaces can reveal changes during handover. Investigate a consistent
   roughness/specular response instead of solving it by continually increasing the real-light count.

6. **Local contact and occlusion cues.** The layout-aware grid sees architectural edges, while nearby prop shadow proxies
   provide furniture shadows. Distant/unshadowed lights and indirect light lack corresponding fine furniture occlusion.
   Evaluate restrained contact occlusion or dynamic local occupancy where needed, without double-darkening corners or
   making furniture look outlined. A view-dependent effect alone will not solve offscreen light transport.

7. **Indoor reflections where materials need them.** World mesh renderers disable reflection probes, and the environment
   has no useful indoor reflection setup. Local probes/another bounded reflection approach could improve metal and future
   polished or wet surfaces. Matte offices need much less. Avoid global sky reflections and expensive full-world updates;
   validate lighting-layer/portal boundaries and moving furniture behavior.

8. **Do not treat APV as automatic runtime GI.** ReferenceMatch.md mentions APV alongside real-time GI, but APV principally
   stores baked indirect lighting; changing baked scenarios at runtime does not bake arbitrary newly generated rooms.
   A runtime procedural world needs a deliberate GI solution or a validated precomputed modular scheme. Standard URP Area
   lights are also baked-only, so changing a Light component to Area will not solve runtime panel lighting.

   Official references: [Unity APV introduction](https://docs.unity.com/en-us/engine/6000.3/manual/lighting-overview/lighting/probevolumes/concept),
   [Unity URP Light reference](https://docs.unity.com/en-us/engine/6000.3/manual/lighting-overview/lighting/light-component).

## Suggested investigation order

First profile/verify unused direct-grid work, unchanged bounce work and preview mesh ownership; these target unnecessary
cost while preserving the image. Then prototype finite panel emission and upper-storey/material-aware bounce against fixed
reference views. Evaluate shadow transitions and specular continuity while moving. Keep true darkness, faulty fixtures,
portal views and saved camcorder exposure among the regression cases. None of these proposals has been implemented.


## Implementation follow-up — chatgpt/lighting-performance

The recommendations above describe the original audit; implementation now lives on `chatgpt/lighting-performance`.
See `RuntimeLighting.md` for the algorithms, quality controls and approximation limits, and WORKLOG.md for commit-by-commit
validation. Efficiency items 1–6 and the eight lighting items now have implementations/architectural decisions. Item 7
was an audit: the existing SRP Batcher remains in use rather than enabling instancing globally without evidence.

A live Frame Debugger capture at the final seed-1234 tour pose contained 164 events: 104 SRPBatch, 27 Mesh, 29 procedural,
2 GL and 2 clears. Both opaque geometry and additional-light shadow passes used SRP batches. No instanced event was
observed. This confirms the shared meshes are not automatically instanced. Detail fields were unavailable for most events
through the debugger API, so no instance/draw count or specific batch-break reason is inferred from an SRPBatch event.
Light-layer/material/keyword and per-renderer emission-property differences remain reasons to inspect individual draws.
The PC pipeline already enables Forward+ and SRP batching; its rendering settings and base materials were not changed.

The runtime contact and reflection paths add bounded work. Finite emitters also add shading cost; all lighting changes
must be profiled together rather than advertising the efficiency changes as an overall FPS gain. Early 1080p warmed
editor samples (120 distinct FrameTimingManager frames each, same random-seed spawn pose, 2 local boxes) measured:

| Same new lighting path | CPU median / p95 ms | GPU median / p95 ms |
| --- | --- | --- |
| Panel size 1, contact 0.65 | 35.54 / 41.91 | 28.57 / 38.87 |
| Panel size 0, contact 0.65 | 31.97 / 38.58 | 25.33 / 35.50 |
| Panel size 0, contact 0 | 31.73 / 37.97 | 25.21 / 34.87 |

These are feature-cost A/B samples, not a before/after benchmark against the original audit (different seed, pose and
loaded world). Reflection captures are cached and not guaranteed to occur equally in each short sample. First-use shader
compilation caused large editor startup stalls and is excluded by warm-up; standalone startup/variant warm-up needs its
own profile. Raw local files: `Temp/LightingPerformance/final_panel*.json`. Final tour/profile results follow below.


## Delivery map

| # | Recommendation | Result |
| --- | --- | --- |
| 1 | Skip unused Direct compute | Per-pixel mode does not dispatch it; texture mode forces a refresh. |
| 2 | Reuse stable bounce/buffers | Separate power from shares, stable position ordering, dirty inputs and locomotion-anchored window. |
| 3 | Generated-resource ownership | Owned/serialized chunk meshes, preview/thumbnail/material cleanup; shared assets retained. |
| 4 | Portal/menu allocations | Reused scratch storage/cached references; main-camera contexts only. |
| 5 | Landmark growth/search hitch | Deterministic eviction and cancellable incremental F5 search. |
| 6 | Main-thread upload budget | Hidden phased chunk publication; startup remains immediate; both upload paths are timed. |
| 7 | Batching audit | Verified SRP batches; retained the current batching path. No speculative material/instancing rewrite. |
| 8 | Finite emitters | Energy-normalised fitting-size quadrature, shared across tiers. |
| 9 | Upper-storey bounce | Separate floor layers, floorless-well handling and slab isolation. |
| 10 | Surface/direction-aware bounce | Cached existing material reflectance and directional wall patches. |
| 11 | Independent shadow fades | Strength ramp, distance/hysteresis and atlas admission during fade-out. |
| 12 | Material continuity | One URP BRDF for real/per-pixel grid direct light. |
| 13 | Furniture contact | Bounded, tiled oriented collider rays; follows moved furniture. |
| 14 | Indoor reflections | Two cached room-bounded probes; no sky/portal capture; temporary shared material variants. |
| 15 | Runtime GI choice | Extended compute grid; APV/baked Area limitations documented. |

Final validation: full 44-test EditMode suite passes, including GPU checks for bounded diffusion/ceiling energy and
isotropic direction moments; Lit and compute shaders have no messages. All 11 seed-1234 tour
poses were captured after implementation. An incremental F5 atrium search jumped 486 m onto an upper floor, drained to
65 chunks/zero queued work and kept the player above its floor. Readback of the live upper-floor bounce field was
RGB (0.518, 0.365, 0.099). A 40-frame shadow transition used at most 54 of 64 maps, recorded 12 strength changes and no
fade-rate violations. Two hundred preview-sized rebuilds tracked 1,000 generated meshes: zero remained alive afterward.
A moved box advanced the occlusion revision; toggling reflections off/on changed active probes from zero back to one,
completed captures and advanced native texture updates. The runtime quality flag restored to its original false value
on exiting Play Mode. Local raw reports/screenshots are in Temp/LightingPerformance.

Still test in a standalone player: long rapid travel, repeated portal crossings, reflective materials while moving
between rooms, physical dragging near the 160-box cap, low-end GPU budgets, shader startup and longer streaming routes.
The editor checks do not establish an overall FPS improvement or physically exact area shadows/GI/reflections.


### Final controlled editor sample

After warming shaders, the final replay pose was sampled with menus disabled, time scale 1, VSync/frame cap off and the
Game view visible. All 150 warm-up/sample frames rendered the main camera. The 120 distinct timing samples were at
1920 x 1080, seed 1234, 61 chunks/zero queued, 15 local furniture boxes and 38 shadowed lights. No reflection capture
occurred during the sample. The player's saved quality choices were retained (48 requested shadow lights, 30 m shadow
distance, 1.5x light reach, 2x fill, room fill/spread 1; actual atlas admission remains bounded).

Finite panels/contact/reflections on: CPU median **31.03 ms**, p95 **37.66**; GPU median **24.08 ms**, p95 **34.10**.
This still is not the original audit's custom-room pose or a standalone-player result. It establishes a final cost on
one warmed view, not an overall speedup. Raw metadata and timings: `verified_seed1234_default.json` in the local folder.


At the same final pose with only Panel size changed to 0, CPU median/p95 was **29.62 / 36.55 ms** and GPU
**22.78 / 32.70 ms** (120 samples, 150 main-camera renders). One cached reflection refresh occurred in this second sample,
versus none in the first, so the difference is an approximate feature-cost observation rather than an isolated GPU pass
time. All other saved quality choices stayed the same. Raw file: `verified_seed1234_point.json`. Panel size was restored
to 1 afterward without saving experimental preferences.


A live warmed portal view rendered its destination into an active render texture and crossing advanced LastWarpFrame
at the paired opening; no portal shader errors. The initial cyan image was shader warm-up, resolved after compilation.
A native Persistent-allocation warning appeared during editor cycles; fallback global resource teardown is being checked
before final delivery. This warning is not being classified as an FPS result or hidden by lowering leak detection.
