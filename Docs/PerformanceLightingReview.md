# Performance and lighting review — 2026-10-24, ChatGPT

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
