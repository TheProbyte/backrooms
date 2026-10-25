# Crawl portal continuity

The generated crawl openings are 0.8 m squares. Their entrance and crossing plane sit on the room's wall face. The reverse face is a complete ordinary wall; neither a duct nor its colliders occupy the neighbouring room. World generation version 9 records this change. Existing placement spacing and 12-30 m links are retained.

`CrawlPortalView` owns a seal collider contained entirely inside the wall. Only the aligned crawling player's capsule can ignore that seal, and only while the destination endpoint is loaded. Other colliders and approaches from behind remain blocked. Leaving/disabling the endpoint restores collision. Traversal maps position, yaw and velocity at the eye crossing, with an arrival guard that rejects handheld jitter while allowing an intentional immediate retreat.

`CrawlPortalSystem` prepares views in a late execution order after movement and handheld motion. Up to two auxiliary cameras render through URP's ordinary camera loop before the player camera. Portal quads are excluded from these views, so there is no recursive camera expansion. The post-processing/camcorder effects apply once, in the player view. Render textures crop to the opening's projected screen rectangle, using quarter/half/full pixel density according to angular coverage. Size quantization and hysteresis reduce reallocation. The cropped perspective retains depth coordinates for Forward+ lighting. A view becomes visible after its camera actually renders.

The destination camera uses an ordinary, off-centre perspective crop of the player's projection. `_PortalClipPlane` clips the destination wall in `Backrooms/Lit`; begin/end camera callbacks clear it for every other camera. Only the portal camera enables the `BACKROOMS_PORTAL_CLIP` shader variant; ordinary world and builder shading has no portal discard. This avoids changing the projection used by Forward+ lighting. New custom shaders used for world geometry must support the same clipping contract. Portal quads disable near-plane depth clipping so they remain visible as the eye reaches the wall.

## Light transport and cost

`Resources/PortalLighting.compute` samples the ordinary destination lighting into a 6 x 8 half-float texture: six directional samples for at most eight nearby mouths. `LightGrid.BindPortalLighting` explicitly binds current occupancy, fixtures, bounce and local furniture occlusion. Samples use full fixture output, so real/grid handover does not dim them; power changes update on the next dispatch. No CPU lighting readback is used at runtime.

The material shader reads this cache for soft diffuse spill up to 3 m from the opening. A narrow 0.6 m reciprocal blend joins illumination at the threshold. Local walls and furniture still restrict spill. The cache is intentionally a coarse directional approximation; it does not provide recursive GI, transported specular reflections, or fully physical area shadows. Texture/material changes between rooms can still be visible.

The cache replaces nested destination-fixture searches inside every affected material pixel. Camera count, cache samples and render texture ownership remain bounded independently of the number of generated portals. Traversable destination pairs remain limited to the existing streaming/light-grid range; arbitrary distant links need additional streaming interests and independent lighting fields. Props are not transported through portals by this change.

Fallback bindings clear portal-only globals in editor/build mode. Portal cameras release their render textures and irradiance cache on disable. Wait for shader compilation to finish before assessing the picture: Unity's temporary loading shader can appear blue while newly edited shader variants compile, and cold synchronous compilation can stall the editor. Warmed frame measurements must exclude those stalls. The editor's persistent async setting was preserved.

## Validation

See the newest `WORKLOG.md` entry for test results, live captures, measured frame costs and remaining checks.

At seed 1234, eye (37.80, 0.45, 19.93), yaw 180, pitch 0, 1920 x 1080, Direct3D 12, saved high settings retained (48 shadow-light setting, contact/reflections/SSAO enabled), 180-frame warmed editor samples:

| Change | CPU median / p95 | GPU median / p95 | Approx. FPS from CPU median |
| --- | --- | --- | --- |
| Cached portal lighting, full-screen portal camera | 32.64 / 41.42 ms | 29.33 / 39.07 ms | 30.6 |
| Cropped 896 x 896 camera | 24.83 / 33.20 ms | 23.35 / 31.01 ms | 40.3 |
| Cropped camera and clipping variant only for portal views | 22.63 / 30.17 ms | 21.15 / 28.73 ms | 44.2 |

These are editor samples from separate Play Mode runs at the same deterministic pose, not a standalone benchmark or a locked-60-FPS guarantee. They isolate the camera crop and clipping follow-up; no measured speedup is claimed for the earlier irradiance-cache replacement. The previous per-pixel draft was not retained as a benchmark build.
