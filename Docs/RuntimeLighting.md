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
A restrained ceiling-reflectance term approximates second-order floor-to-ceiling-to-room light. This is low-frequency
transport with architectural occlusion, not a general physical multi-bounce GI solver; small recesses and exact colour
bleeding/energy transport remain approximations. Empty unlit areas receive no invented ambient source.

Sources/diffusion and GPU uploads are invalidated by actual relevant changes (power, geometry, surfaces, window, tuning),
not by an unchanged frame or real/grid share alone. Light records have stable position order independent of distance rank.

## Validation and costs

PerformanceLightingTests covers stable caching, mode switches, ownership, phased publication and deterministic eviction.
RuntimeBounceTests dispatches the real GPU source kernel and reads it back: upper-storey illumination, closed-floor
isolation, changed floor reflectance/colour and the direction of west-wall bounce. Shader compilation and fixed-pose
LightingTour views are also checked. Baseline, efficiency and lighting captures are local under Temp/LightingPerformance.

Finite samples and material BRDF lighting cost more than the old grid diffuse shortcut. Four light samples must not be
sold as a free FPS gain. Reused centre architectural visibility and filtered atlas taps reduce redundant shader work;
profile the final warmed player build on representative rooms, streaming routes and portal views before choosing budgets.
