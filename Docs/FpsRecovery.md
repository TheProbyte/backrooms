# FPS regression follow-up, 2026-10-25

The lighting pass reproduced about 32 FPS in the editor at the saved high settings. This follow-up removes redundant
shader work while retaining the same emitter samples, BRDF, light/shadow budgets, textures, bounce and reflections.
It does not lower resolution, change the saved quality settings or impose a frame cap.

## Changes

- Real-light material shading previously scanned the complete receiver-tile fixture list for every URP light at every
  pixel. A half-full open-addressed position lookup replaces that nested scan. It rebuilds only when fixture positions,
  order or count change, with empty bindings and explicit ownership for editor/build mode. The observed 735-light list
  uses an 8 KB table; successful lookups averaged 1.22 probes, maximum 6 in that snapshot.
- A conservative test against the entire finite horizontal emitter rectangle rejects receivers outside its range/cone
  before layout/furniture rays. It does not discard a contributing quadrature sample. The GPU regression compares the
  rejection against actual attenuation at the existing sample positions for points, square panels and long tubes.
- Real fixtures blocked by layout walls are rejected before URP samples their soft shadow maps. Accepted fixtures reuse
  that visibility result in the BRDF path. The same architectural, shadow and finite-emitter rules still determine light.
- Distant furniture rays now run after range/cone and architectural rejection, instead of before the light could be
  rejected. The occlusion algorithm/strength is unchanged.
- Validation caught a reflection queue bug on world replacement: invalidating an in-flight probe's key disabled it,
  preventing completion. World replacement now releases those probes/captures. A capture stays enabled through a distant
  move until it finishes, then normal distance culling resumes. There is still at most one capture in flight.

## Measurements

Unity 6000.4.5f1 editor, RTX 4070, 1920 x 1080, render scale 1, no VSync/cap, warmed shaders, time scale 1, menus closed.
Seed 1234, camera approximately (-17.76, 1.61, 36.41), yaw 82, pitch 14. The saved options remain: 48 requested shadow
lights (38 actually shadowed), high shadow resolution, 30 m shadow distance, 44 m light distance, 1.5x reach, panel size 1,
contact 0.65, indoor reflections on. The current view has 735 grid fixtures and 15 local furniture proxies.

| Editor sample | CPU frame median / p95 ms | GPU median / p95 ms | FPS from median CPU frame |
| --- | --- | --- | --- |
| Before, 120 frames | 31.05 / 37.51 | 24.03 / 34.20 | 32.2 |
| After, 180 verified frames | 18.11 / 24.40 | 11.85 / 23.39 | 55.2 |

The final verified sample has active, completed reflection probes; no refresh occurred inside that sample. Settings
before/after are identical. The loaded set drained to 60 chunks versus 61 in the earlier run; these are same-seed/view
editor comparisons, not a perfectly identical whole-world capture. Two preceding same-view samples also measured about
18.0-18.1 ms CPU and 11.8 ms GPU. First-use shader compilation is excluded.

A larger office view measured 20.25 ms CPU median (31.20 p95), 13.92 ms GPU median (25.70 p95), approximately 49.4 FPS.
That view has 48 furniture proxies and included one reflection refresh. It is a separate-room check, not its own
before/after comparison. Do not interpret the corridor result as a guaranteed 55 or 60 FPS everywhere.

Raw local samples and before/after PNGs: `Temp/FpsRecovery/baseline.json`, `final_with_reflections.json`, `office.json`.
Earlier diagnostic samples in that directory are exploratory; use the verified reflection-active sample above.

## Checks and remaining limits

The EditMode GPU lookup regression covers collisions, negative positions, reordered lights, removal/shrink and misses.
The existing stable-grid test also ensures stationary frames do not acquire repeated uploads. The GPU emitter test
checks conservative rejection against contributing finite samples. Reflection reset has a lifecycle regression.
The full 48-test EditMode suite passes. Native Play Mode checks forced a capture before replacing the world and before
a move of over 40 m; the queue recovered in both cases (eight captures completed, one active probe, no pending capture).
No runtime console errors/warnings or Lit shader messages were reported in the final checks. Before/after images were
visually inspected; this was not a pixel-identical comparison of the time-varying camcorder filter.

The editor measurements include editor/render-thread overhead and periodic stalls. A standalone player benchmark and
long streaming/portal routes remain necessary. This is a substantial measured regression fix, not a claim of a locked
60 FPS or a completed low-end-GPU performance target. Keep the earlier unresolved editor allocation diagnostic tracked
in WORKLOG; this follow-up does not establish its original cause.
