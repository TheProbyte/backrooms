# Performance: what costs frame time, and how to measure it

## Measuring

- **In game:** F5 > GRAPHICS shows fps, CPU and GPU time live.
- **Editor A/B:** `Backrooms.DebugTools.PerfAB` cycles named settings several times (settle, then sample each), so
  drift from other programs affects every setting equally. Use it from editor scripts / MCP.
- **Build benchmark:** run the built game with `-benchmark [results.txt]` (`DebugTools.Benchmark`). It switches to seed
  1234, visits a long corridor, a crawl hole and a short walk that streams new chunks, waits for each to load, records
  600-900 frames each, writes median / 95th percentile / worst and quits. Default results file:
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\My project\benchmark.txt`.
- Close games and other GPU-heavy programs first: anything else using the GPU slows the frame directly.

## Editor vs build

The editor adds its own work every frame (its windows, inspector, safety checks), and if the **Scene view** is visible
while you play, it draws the whole world a second time. A build has none of that. Use the build benchmark for real
numbers; in the editor, keep the Scene tab hidden behind the Game tab while playing.

## What costs what (2026-10-25, RTX 4070, 1080p, seed 1234 corridor, editor)

| Item | Approx. GPU cost |
| --- | --- |
| Each shadow-casting light (shadow map redraw) | ~0.1 ms; 48 lights vs 12 = ~2 ms plus CPU culling |
| SSAO (needs a full extra DepthNormals pass of the scene) | ~2-3 ms; half resolution saves almost nothing |
| Per-pixel distant light, every fixture exact everywhere | ~5 ms; with the 4.5 m near/far split ~1.5 ms |
| Square-panel emitters (4 samples within 6 m) | ~2 ms |
| Furniture contact occlusion | ~0.4 ms |
| Indoor reflections, SMAA, camcorder video pass | < 0.5 ms each |
| A crawl-hole window | one extra camera, cropped to the window; shadows only for the one you are at |

Defaults (graphics options version 2): 16 shadow lights, 18-20 m shadow distance, exact light radius 4.5 m, SSAO on.
Editor result at the benchmark pose: old saved settings 48 fps, new defaults 66 fps (81 with SSAO off).
