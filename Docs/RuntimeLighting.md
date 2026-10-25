# Runtime lighting for the procedural world

The supported procedural-world GI path is the existing compute light grid, extended at runtime. It does not depend
on a prebaked world, APV scenarios or baked-only URP Area lights. No package or pipeline replacement is required.
The handmade ReferenceMatch scene keeps its stand-in lighting; it is not a benchmark for the procedural grid.

## Direct fixtures and shadows

GridLight is an 80-byte record shared by C#, compute and the forward shader. Horizontal half-extents follow each visible
fitting: 0.57 m square diffuser, 1.18 x 0.11 m tube envelope, or a small square approximation to the round dome. Floor
lamps remain point sources. F5 > LIGHT > Panel size scales the source at constant total output (0 restores a point).
Energy-normalised four-point quadrature averages the finite source. Between 6 and 8 m the offsets smoothly converge to
one centre sample to avoid a sampling-distance pop. This is an area approximation, not an LTC/path-traced area light.

Per-pixel grid fixtures and real fixtures both use URP's material BRDF, including roughness/specular, the same cone,
contrast and optional diffuse wrap. Grid direct no longer enters bakedGI. Full-power intensity determines contrast,
so real/grid shares remain complementary during handover. The coarse texture fallback remains diffuse-only by design.

Architectural visibility uses the existing layout ray test. The finite fitting shares its centre visibility/shadow map,
with extra sample visibility tests within 1 m. Nearby furniture uses a central soft URP shadow map: this does not produce
physically exact area-light penumbras. Real shadows independently ease strength and retain atlas capacity until fade-out;
new shadow maps wait if needed instead of oversubscribing the 64-map budget (point lamps cost six maps). Distance fade
and selection hysteresis apply independently of the real/grid energy handover.

## Runtime indirect light

Three half-float atlases contain floor radiance, wall radiance and the wall field's first directional moment. Each has
two separate storey layers; ordinary upper rooms use their own floor, while an upper well can sample the ground well's
floor. Sampling wraps inside a layer without filtering between floors. Occupancy still blocks closed walls and slabs.

Main-thread code downsamples each distinct existing base texture once and multiplies its linear base colour to estimate
surface reflectance; imported texture readability/settings and materials are not modified. Floor, wall and ceiling
finishes therefore tint and attenuate reflected light. Generation workers still publish only managed layout/finish data.
The legacy Floor albedo field scales floor reflectance relative to its old 0.25 default. Reflectance is bounded below 1.

The source kernel samples floor and actual solid-wall patches. The direction moment makes a wall facing a lit side
respond differently from one facing away. Diffusion stays inside open floor edges: doors/arches do not smear indirect
light through their walls. Direct rays passing through an opening create reflected sources in the receiving room.
A restrained ceiling-reflectance term approximates second-order floor-to-ceiling-to-room light. It belongs to the fixed
source, so repeated smoothing cannot accumulate extra energy, and its isotropic energy enters the direction moment. This is low-frequency
transport with architectural occlusion, not a general physical multi-bounce GI solver; small recesses and exact colour
bleeding/energy transport remain approximations. Empty unlit areas receive no invented ambient source.

Sources/diffusion and GPU uploads are invalidated by actual relevant changes (power, geometry, surfaces, window, tuning),
not by an unchanged frame or real/grid share alone. Light records have stable position order independent of distance rank.
The window follows the player locomotion root, so handheld shake at a cell boundary cannot churn its inputs. Faulty
tubes retain their exact power waveform; other distance/power fades retain the existing 64-step quantisation.
Upload timing includes immediate startup chunks as well as accumulated phased upload time. Generated mesh ownership
is serialized so surviving objects retain their mesh list through a Unity script reload.

## Validation and costs

PerformanceLightingTests covers stable caching, mode switches, ownership, phased publication and deterministic eviction.
Fallback globals explicitly release their eight buffers and two empty textures at assembly reload, lifetime reset and
quit, then rebind in editor/gridless scenes. ExitingPlayMode retains bindings until rendering stops. A teardown guard
blocks late OnDisable allocation.
An EditMode regression verifies invalidation, guarded shutdown and valid rebinding; the current full suite has 48 tests.
RuntimeBounceTests dispatches the real GPU source kernel and reads it back: upper-storey illumination, closed-floor
isolation, changed floor reflectance/colour and the direction of west-wall bounce. Shader compilation and fixed-pose
LightingTour views are also checked. Baseline, efficiency and lighting captures are local under Temp/LightingPerformance.

Finite samples and material BRDF lighting cost more than the old grid diffuse shortcut. Four light samples must not be
sold as a free FPS gain. Reused centre architectural visibility and filtered atlas taps reduce redundant shader work;
profile the final warmed player build on representative rooms, streaming routes and portal views before choosing budgets.


## Furniture contact and reflections

LightingOccluder registers existing furniture BoxColliders. A tiled list of up to 160 boxes within 14 m follows actual
transforms, including dragged/rotated props. Stable boxes reuse GPU data. Four short hemisphere rays add restrained
indirect contact; local direct rays also occlude the grid's unshadowed fixtures and floor-bounce sources. Native shadow
maps remain responsible for nearby direct furniture shadows. Existing SSAO and local indirect visibility combine by
minimum rather than multiplying the same contact cue. This uses collision proxies, not exact triangle silhouettes, and
has a finite reach (indirect 1.15 m, direct 2 m). F5 > LIGHT > Furniture contact controls it; zero disables collection.

RuntimeIndoorReflections keeps two 128-pixel HDR probes at most and permits one time-sliced capture at a time. It grows
a loaded-cell rectangle only across completely open edges of the same room identity, so even an internal wall inside
one custom area bounds the reflection volume. Probe volumes include floor/ceiling surfaces. Captures use black clearing,
box projection, a bounded clip distance and exclude portal quads. Portal preparation/warping runs only in a context that
contains the main camera, not reflection-only contexts. Newly captured reflections ease their intensity in.

Captures refresh after light/prop/streaming changes with a four-second minimum interval; unchanged probes remain cached.
They are room reflections, not live mirrors, and moving props/flicker can temporarily differ between the scene and its
cached reflection. Only the current room is refreshed; a nearby previous room keeps the second cache. The world renderer
uses per-pixel probe volumes in the existing Forward+ path with probe blending/box projection already enabled.

Reflective materials with environment reflections disabled get shared temporary variants; base asset materials remain
unchanged. Variants/probes are released and original materials restored on disable, including script reloads. The project disables realtime reflection probes in
quality settings; this component enables that setting in memory during its lifetime and restores its original value.
No ProjectSettings file is edited. F5 > LIGHT > Indoor reflections switches the probes off/on.

Unity 6000.4 Forward+ has a scripted-probe atlas refresh bug. After a completed capture the code calls
Texture.IncrementUpdateCount(), Unity's published workaround for
[UUM-129405](https://issuetracker.unity.com/issues/4781/urp-realtime-reflection-probes-do-not-update-when-renderprobe-is-being-called-once-per-second).


## FPS follow-up

Real fixtures use a cached position index instead of repeatedly scanning the receiver tile's light list. Conservative
finite-emitter range/cone tests precede rays; layout-blocked real fixtures skip URP shadow-map sampling. Contact rays
run only for surviving distant fixtures. Source samples, shadows, bounce, materials and saved budgets are unchanged.
World replacement cancels old probe captures; a time-sliced probe remains enabled until completion across distant moves.
See FpsRecovery.md for verified same-view editor timings, GPU regressions and remaining standalone-player checks.
