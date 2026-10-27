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

Walls and door headers (`LGrid_EmitterVisible`, LightGridSample.hlsl): how much of the panel a point sees past them,
not a yes/no line test. Walls are full height on 1.2 m cell edges and a fitting lies inside its own cell, so the walls
that can hide any part of it lie on the cell lines the line to its centre crosses: the same walk as the old test, and at
each crossing the wall pieces inside the cone to the panel hide the share of its width they cover, seen from the point
(weighted by the panel's area across that direction, so a square seen corner-on fades in an S). A header hides the
share of the panel's depth whose rays pass above the opening's top. The result is the penumbra a real panel casts:
sharp at a wall end and widening away from it (panel width x distance from the wall end / distance from the wall end to
the panel), so there are no hard wedges on the floor past wall ends and doorways. `Editor/Tests/ShadowLightTests`
checks it against counting rays to 1600 points over the panel. Lamps (point sources) keep the line test.

Furniture uses the real lights' shadow maps. URP spreads a spot light's map over its whole cone, and the fixtures' soft
falloff runs to 150-179 degrees, where a 512 map's texels were 17-60 cm on the floor (a chair left almost no shadow).
The maps now cover 120 degrees (`FixtureLightManager.shadowCone`): everything a fixture reaches on the floor (6 m range,
2.6 m up) is within about 128 degrees, and at the 120 degree edge the floor gets about 1 % of the light right under the
fixture; the texels are about 1.8 cm at 512 (0.9 cm at 1024). The real light skips points more than 58 degrees off its
axis and the grid's per-pixel lights draw its share there instead (`BR_InShadowCone`, `_BRShadowCone`), with local box
occlusion for furniture; where that share is under about 0.5 % of the light right under the fixture (the floor several
metres out) it fades out instead of being worked out. Without the per-pixel distant light (F5) the light's own cone is used.
Real shadows independently ease strength and retain atlas capacity until fade-out;
new shadow maps wait if needed instead of oversubscribing the 64-map budget (point lamps cost six maps). Distance fade
and selection hysteresis apply independently of the real/grid energy handover.

### How a panel throws light (2026-10-27)

A flat diffuser panel is Lambertian: its intensity falls off as cos(angle) from straight down. `LGrid_AngleAtten` (shader,
`LightGrid.compute`, `LightGridMath.PanelProfile`) is linear between the cone's two cosines, and `TrofferCone` gives
179 / 0 degrees at full spread, so a spread of 1 is exactly Lambertian; the default 0.8 is within 0.03 of it (tested in
`ReferenceRoomTests`). It used to be a squared (URP spot) falloff, about cos^2: walls beside a panel had round hot spots
halfway down instead of a fade from the top, and rooms were darker and patchier. A rectangular panel's form factor is
already Lambertian, so there the profile now divides out.

### Light as it falls: bounce and AO (2026-10-27)

- `multiBounce` 1 (was 0.7): every bounce passes on all of the light its surface reflects (the surfaces' albedo already
  takes its share). In a room with light walls that is what evens the light out; at 0.7 the wall facing away from the
  lights was 10x darker than the lit one, against about 2-3x in a real room (and in the reference footage).
- `darkSpots` 0 (was 0.35): no extra curve pushing dim surfaces darker than their light.
- Saved options (graphics options version 3) take both values once, if the player had not changed them.
- Screen-space AO (`GraphicsController`, set at runtime; the renderer asset is not edited): direct lighting strength 0
  (it darkened direct light by 60 % in corners: a dark, grainy band where a lit pillar met the ceiling, and the dark
  lines beside the ceiling grid), radius 0.2 m, intensity 0.6. AO stands in for bounced light a nearby surface blocks and
  assumes the blocker is black, while here it is usually a lit wall or ceiling, and the traced bounce already handles
  occlusion at room scale; what is left is the tight darkening where floors meet walls.

Measured against the reference footage in the reference room (ReferenceMatch.md): with the old falloff and bounce the
floor was 1-1.5 stops darker than the photo and the shadowed left wall 3 stops; now every area is within about a quarter
stop.

### Light through broken walls (2026-10-27)
A wall broken with the sledgehammer is still a wall in the occupancy, flagged as holed (bits 29 and 30 for the west and
south edge), with a record in the hole table (`LightGrid.Holes.cs`, `LightGridCommon.hlsl`: which 0.15 m blocks are open
right through, 8 across by 18 up, and which have any opening) found through a per-cell index laid out as the occupancy
(no search inside the walks: FXC compiles loops nested in them very slowly). The shaders test a light's footprint on the
wall, not the one block its centre line meets (`LG_HoleOpenBox`: the share of a box that is open, each block weighted by
how much of it the box covers, rows in four fixed samples so there is no loop): for a ceiling panel the box is its cone
where it crosses the wall (as wide along the wall as the cone, as tall as the panel's depth spreads, at least a block),
for a point test a block round the crossing. Lights with a shadow map pass a holed wall where any of the footprint has
an opening, except at its posts (`LGrid_EmitterVisibleH` / `LGrid_LightVisibleH` with shadowMapped, chosen per light from
its shadow parameters), and the broken panel's pieces cast the shadows (each piece as a block through the whole wall
thickness, so the shadow bias cannot eat the 12.7 mm boards): the real lights near you come through a hole exactly,
broken by the studs; a footprint that is all whole wall stops them. The grid's lights and the far part of real lights
pass the footprint's open share: soft-edged light through a hole, as a panel's would be, not hard 0.15 m squares (what
they drew before, on the wall opposite a hole). The traced bounce (rays go on through an open block), the probe filter
(a wall more than half open joins its two sides) and the CPU mirror `LightGridMath.Visible` read the blocks. A wall that
breaks more is encoded again and the light around it redrawn (14 m). See BreakingWalls.md.

### Doorway headers in the traced bounce (2026-10-27)
The probe lookup (`LGrid_ProbeIrradiance`) blends a sample with the next cell's probes across open floor and openings.
Above a doorway's or arch's top the edge is wall (the header), but it blended there too, so in a dark room the wall
above a doorway to a lit room, and the ceiling in front of it, showed the lit room's bounce: a bright cell-wide band
with sharp sides. Now a door or arch counts as wall above its top.

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

2026-10-27: frames are capped at 60 a second by default (F5 > DISPLAY, `GraphicsOptions.frameCap`; settings saved with no
limit move to 60 once). With vsync on a 240 Hz display there was no limit at all: the graphics card drew every frame it
could (about 146 W, fans loud) for frames nobody sees. With vsync, a build syncs to every Nth refresh nearest the cap
(240 Hz and 60: every 4th); the editor holds the cap with the frame-rate limit.

Measured again at 60 fps (RTX 4070, 1080p, the user's settings: 24 shadowed lights, high shadows): 72 % GPU, 158 W, fans
at 100 %. Most of it was pixels: (1) the 3D view drew the full 1080 lines while the camcorder records and processes
480-540: with the camcorder picture on it now draws at 1.35 x the recording's lines (F5 > DISPLAY > Match the recording,
on by default; render scale stays the most it draws): 67 % -> 46 %; (2) SSAO at full resolution with 12 samples and a
bilateral blur: now at half resolution (set at runtime, `GraphicsController.PhysicalAO`), about a fifth of the card
down to about 6 %; (3) the indoor reflection probes re-captured every 4 s (six faces with full lighting and shadows)
because the lighting revision moves all the time (faulty tubes): a room is now captured when you enter it and again for
a change of light at most every 30 s. Together: 47 % GPU, 117 W, 67 C, fans 53 %.


## Look panel and the light it controls (2026-10-26)

**F5 > LIGHT** is the look of the building's light. Every control lives in `Camcorder/GraphicsOptions.cs` (look fields
carry `[Look]`: presets, "my look" and reset copy only those, never quality settings) and is applied each frame by
`GraphicsController`. Built-in looks: STILL RECORDING (the defaults), EVEN & PALE, DIM & MOODY, HARSH TUBES, WARM & OLD;
SAVE MY LOOK keeps one of your own (PlayerPrefs). Moving a control marks the look as changed.

- **Colour**: light colour in kelvin (4500 = as built) and a green/magenta tube tint shift every fixture's colour
  (tubes, strips and lamps together, brightness kept: `GraphicsOptions.ColourShift`); the fittings' own glow follows
  (`_BREmissionTint`, also scaled by Panel glow).
- **Rectangular panels** (`Shaders/BackroomsAreaLight.hlsl`, `BackroomsRectFixture` in the forward pass): a ceiling
  panel's diffuse light is the exact form factor of its rectangle (edge integrals, Heitz & Hill's fitted arc term);
  where a surface cuts through the panel its far corners are moved onto the surface's plane, which matches clipping
  without branches. Specular uses the point of the panel nearest the mirror reflection with the lobe widened by the
  panel's size (Karis), so glossy floors show long rectangular reflections. Far away (6-8 m) the panel shrinks to a
  point, as before; with the toggle off every fixture is a point. Tested against brute-force integration
  (`LightingLookTests.RectangularPanelMatchesNumericIntegral`).
- **Traced bounce** (`LightGrid.Probes.cs`, kernels FaceLight / ProbeCopy / ProbeTrace in `Resources/LightGrid.compute`,
  sampled by `LGrid_ProbeIrradiance`): two probes per 1.2 m layout cell and storey (low and high), each an L1 spherical
  harmonic per colour. A face cache holds the direct light leaving every cell's floor, ceiling and walls (all powered
  fixtures, real or not, times the surface colour with the colour-bleed setting), refreshed a quarter of the cells per
  frame while anything changes. Each frame a quarter of the probes trace 64 rays through the layout (walls, shut doors,
  slabs, header beams, the nearby furniture boxes), read the face cache where they hit plus that surface's bounce from
  last frame's probes (second bounce), and blend into their history (hysteresis 0.95, and longer the longer the probe
  goes without a real change: up to 0.99 after about a hundred updates, `settle`; the count rides in the top 8 bits of
  `_ProbeKey`, so a still room's bounce no longer shimmers with each update's 64 new rays). An update blends in fast only
  when it differs from the history by more than its own rays' spread explains (the standard error of the estimate from
  the 64 rays' luminance): a light switched or a door opened shows within a few frames, while the noise of a few rays
  finding light in a dark room does not (the first version blended fast on any big relative change, which made dark
  places shimmer and pop like a slow disco; measured in a switched-off room: 1.75% -> 0.18% average change per frame,
  worst 16-pixel block 6.6% -> 0.6%). The shaders read the probes after a small wall-aware smoothing pass (each with its
  open neighbours; `ProbeFilter`), the traced history stays unsmoothed. Bounce and haze use each fixture's power smoothed
  over ~0.35 s (`GridLight.padding.x`, `FixtureLightManager.smoothBounce`): a stuttering faulty tube's direct light keeps
  its exact waveform, but its bounce (face cache in slices) and haze (redrawn in boxes) no longer pop in patches. Probes
  are keyed to their cell, so the moving window never shows stale light. The shader keeps its sample on the room's own
  side of walls and blends across open floor and openings. The far crawl-hole site grid has its own probes.
  **Crawl holes**: a ray that goes into a crawl hole carries the light of the room at the duct's far end (the duct mouth
  light CrawlPortalSystem samples each frame, `_DuctMouthLight`) times what a 1.9 m sheet-metal duct passes
  (`LightGrid.holeTransmission`, 0.15): a lit room glows faintly out of a hole into a dark one. Holes whose duct is not
  among the four lit ones are dark. Off: the older diffused floor-light field.
  `Debug/FlickerProbe.cs` measures frame-to-frame light change in a dark place (the camcorder's sensor noise switched off
  for its renders).
- **Shadow softness** (`BackroomsSoftShadow`): for real spot-light fixtures, a blocker search in URP's own shadow atlas
  (linear depths recovered from the shadow matrix; the fitting's own body within 0.3 m is ignored), then a filter the
  shape of the panel scaled by (receiver - blocker) / blocker depth on each of its axes: a uniform rectangle of light
  casts the occluder's shadow averaged over its own shape, so a square panel gives a square filter and a tube a long,
  narrow one (soft along the tube, sharp across it). A jittered 3 x 3 grid for the search and 4 x 4 for the filter (one
  shift per pixel, which the camcorder's raster averages), capped at 0.6 m and kept inside the map's own atlas tile.
  Each sample's depth reference is moved by the receiver's slope (otherwise a wall the light skims along shadows
  itself). Lamps (point-light shadows) keep URP's filter. Shadow maps are not cached (URP redraws them every frame;
  there is no supported way to keep static ones).
- **Contact shadows** (`Generation/ContactShadows.cs`, `Resources/Shaders/ContactShadowMask.shader`): a render pass of its
  own on the player's camera, after the depth prepass, marches 8 steps toward the two strongest fixtures at each pixel
  through the camera depth and writes a mask; Backrooms/Lit looks it up per light. The pass declares the depth texture
  it reads, as the render graph requires: reading `_CameraDepthTexture` inside the opaque pass itself is not declared
  there and is not safe. Portal views, probes and the scene view get none.
- **Haze**: the direct-light pass also stores every fixture's whole light (luminance) in `_LGridB.z` (real lights too:
  their power changes redraw their reach, flicker at most every 0.15 s); Backrooms/Lit marches it in 8 jittered steps
  from the camera (`BackroomsHaze`). Isotropic, tinted by the fixtures' colour. 0.25 scatters about 2.5% per 10 m.
- **Poolrooms**: sun, sky (and ambient), tile bounce and sun warmth scale `PoolWorld`'s own light.

All of these are uniform branches (`_BRLook`, `_LGridGI`, `_LGridHaze`, `_BRContact`), never shader keywords; zero (what
the fallbacks bind) is off. Measured together in the editor (RTX 4070, same views): about 0.3-1.5 ms of GPU time on
Direct3D 12 (`Debug/LightingProbe.cs` captures each feature off/on and every look, with timings).

### Shader compile times and the Direct3D 12 crash (2026-10-26)

A Backrooms/Lit variant took 9 s to compile before this work and 28 s with the first version of it (a 4-step unrolled
wall test inside the panel code, inlined twice, and a second copy of URP's shadow filter); now about 13 s. Cameras that
render into textures (the exposure probe, portal views, reflection probes) compile shaders synchronously, so after a
shader change Play Mode stalled for minutes mid-frame, and on Direct3D 12 the editor then lost its graphics device and
crashed ("failed to wait for fence", "command lists ... corrupted"), even with every new feature switched off. On
Direct3D 11 the same stall survived. `Editor/LitShaderWarmup.cs` compiles the variants a play session used
(`Shaders/BackroomsLitVariants.shadervariants`) in Edit Mode whenever the shader changes, and holds Play until they are
in the shader cache (Backrooms > Compile Lit Shader Variants runs it by hand). Re-record the list (Project Settings >
Graphics > Save to asset, same path) after adding materials with new keyword sets. 2026-10-27: the crawl holes' portal
views drew 2 variants the list lacked (soft shadows off), which compiled mid-play (the "Compiling shader" box while
walking around); every portal variant now also has its plain-shadow twin, and the plain (no keyword) forward and shadow
passes are in (76 variants). The warmup now takes about 17 minutes here after a lit shader change.
