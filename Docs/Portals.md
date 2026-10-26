# Crawl holes (non-Euclidean ducts)

Pairs of 0.8 m square holes at the foot of walls (`CrawlPortals.Place`). Each hole leads into a 2.2 m crawl duct behind its
wall; at the duct's far end a window shows the partner hole's room, and crossing the window brings you out of the partner
hole, 12-30 m away, facing out of it. Turn round and crawl back in and you are in the partner's own (identical) duct, which
leads back.

## The duct is bigger on the inside

The duct runs through the two free cells behind the wall (placement requires them: same room, no props, open between them),
so physically it overlaps the room behind. That room never sees or touches it:

- **Drawing.** An invisible mask quad over the opening (`Backrooms/CrawlMask`, queue Transparent-410) writes stencil bit 64
  where the opening is visible. The duct (`Backrooms/CrawlDuct`) and the far-end window (`Backrooms/PortalView`) draw after
  the opaque rooms (queue Transparent-400/-390) only on those pixels, so they show through the hole and nowhere else. They
  are not in the depth prepasses.
- **Inside.** When the player's eye crosses into the opening, the hole switches to "inside": duct and window become ordinary
  opaque geometry (stencil Always, opaque queue, depth passes on), and the patch hides.
- **The back of the wall.** Both wall faces are cut. Seen from the room behind, the opening is covered by `BackPatch`, a piece
  of that room's own wall material with the same world UVs, so the wall looks solid. It hides while the player is inside, so
  the way back stays open.
- **Colliders.** The wall thickness over the opening has a seal that only an aligned crawling player passes (`Fits`).
  The duct's side, top, floor and dead-end colliders are enabled only while the player is inside or within 3 m in front of
  the opening. From the room behind you can walk straight through where the duct is.
- **Lighting.** The duct is lit only by its two openings: light falls off with depth from each end (`_Falloff`), so the
  middle of the crawl is dim. The room behind's lights never reach it.
  - Entrance: every duct pixel is shaded with Backrooms/Lit's own lighting (`BackroomsFragmentPBR`: real lights with their
    shadow maps, light grid, contact occlusion, SSAO) evaluated at the matching point of the opening, 2 cm out in the room,
    and read at that point's screen position (SSAO, Forward+ light list). At the lip this is the room floor's shading, so
    there is no line where the duct starts. The duct renderer carries the entry room's rendering-layer mask (real lights
    skip layer bit 0, so with the default mask it got no real light at all).
  - Far end: the light measured just outside the partner hole. Each frame `CrawlPortalSystem.UpdateDuctLight` runs
    `PortalLighting.compute` (the old spill cache) for up to 4 ducts (the one you are in, those with a live view, then the
    nearest in front): six axis directions at both mouths of each, into `_DuctMouthLight` (6 x 8). The duct looks it up in
    the directions each surface faces after the trip (`_DuctExitRot`). This sample has no real-light shadow maps.
  - The faint `_Ambient` and the far-end term fade to zero at the entrance lip (the room can be very dark there).

## Look, outlets, hitches

- **Vent duct.** The duct is lined with galvanized sheet metal (`Resources/Textures/DuctMetal.png`, generated: zinc
  spangle, rolling streaks, a cross-break crease, joint flanges with screws, dust in the corners) on all four sides, in
  1.1 m sections (joints at the entrance, the middle and the window; CrawlDuct `_ObjectUV`, metallic 0.25). If the texture
  is missing the duct falls back to the room's finish.
- **Outlets.** Wall plates are placed before the holes; `CrawlPortals.ClearPlates` drops any low plate on either face of
  a holed wall (light switches sit above the hole and stay). The pair test checks it.
- **AO on entry.** The duct reads SSAO at the opening's screen position only for its floor seen from outside (where that
  position shows the room floor at the lip). Its walls and ceiling no longer pick up the AO of whatever is behind the
  hole while you crawl in (`BACKROOMS_AO_HOOK` in BackroomsLitForwardPass, defined only by CrawlDuct).
- **Hitches.** First use of a material state builds a GPU pipeline (first run of a build: 309 ms just before the hole and
  48 ms on entering, in the benchmark; 53 ms entering in the editor). The first frames that have a hole draw every portal
  material state once, invisibly, and render one portal view with shadows (`CrawlPortalSystem.Warmup`); entering in the
  editor afterwards: 22 ms (normal). Portal render targets grow in size steps (1/8 ... 1 of the screen per axis) instead
  of 32 px: crawling to the window reallocated the target and its camera buffers 44 times (30-43 ms frames), now 10.
  Benchmark spots "crawl through" and "crawl through (far)" report the slowest frames and where they happened.

## Crossing

`CrawlPortalSystem` (late execution order, after movement and handheld shake) tracks each hole's eye position in its frame
(z out of the wall, duct from 0 to -2.2). Both hand-overs happen `near clip + 3 cm` before the plane, because closer than
that the camera's near plane cuts off the mask or the window (that showed as two flat brown frames before the trip). The
trip maps position, yaw and velocity exactly, so the eye lands just as far inside the partner's opening, which is set to
"inside" on arrival. No arrival latch is needed: going back requires crawling the whole partner duct.

## Views

Up to two windows are rendered per frame (`MaxViews`), nearest first, the one you are inside always first. Each is a camera
at the partner hole matching the player camera (same projection, cropped to the window's screen rectangle, pixel density by
angular size), rendered with `Portal_Renderer` (no camcorder pass) and clipped at the partner's wall face by the
`BACKROOMS_PORTAL_CLIP` variant (Backrooms/Lit, the duct and the mask honour it). Window quads are excluded from portal
cameras (no recursion; other holes seen through a window show their duct ending in its end cap). Portal cameras only render
shadow maps for the window you are at or in (each camera re-renders the shadow atlas otherwise).

Portal cameras have SSAO (`Portal_Renderer` carries a copy of the PC renderer's SSAO; `GraphicsController` switches every
renderer's SSAO with the AO option), so the room seen through a window has the AO it has once you are through. Before
this, AO appeared all at once on the frame you came out. Backrooms/Lit's DepthNormals pass is a Backrooms copy
(`BackroomsLitDepthNormalsPass.hlsl`) that honours the portal clip, as do CrawlDuct's depth passes; otherwise the SSAO of a
portal view would see the partner's wall.

Keeping pictures live: a view starts only when its opening is in line of sight, and only fixed scenery counts (props,
which are rigidbodies, and the player's capsule do not hide a hole). Once live, a view keeps rendering for 0.5 s after
losing line of sight, and keeps its slot unless another window is more than 1 m nearer. A window without a picture shows
the light measured at its partner's mouth (on a 0.45 albedo), not a near-black colour.

## Lights through the hole

The real (shadowed) light pool is centred on the player. While you are at or in a duct, `CrawlPortalSystem.TryGetLookahead`
gives a second centre just outside the partner hole, and `FixtureLightManager` chooses real lights by the nearer of the two
distances. The room you are about to come out into is already lit by real, shadowed lights, so nothing pops when you arrive.

The old diffuse "spill" (PortalLighting.compute, `_PortalSpill*` in LightGridSample.hlsl) is switched off: the window now
sits inside the room behind, where it would have lit that room's floor. The code is still there; `_PortalSpillCount` is 0.

## Checks

`CustomRoomTests`: window-to-partner mapping for every orientation pair, a full traversal (in, along, out, jitter at the
partner, back in and back through), seal ownership, worker-thread chunk build. In Play Mode (seed 1234, hole at
37.80, 0.40, 19.28) a 600-frame recording of a crawl through showed no frame-to-frame jump larger than camcorder noise at
either hand-over, and the room behind the partner wall showed a solid wall with nothing of the duct.
2026-10-25 (FrameRecorder, 960x540): crawl through with portal SSAO, mean frame difference at the warp 1.32 (camcorder
noise median 1.29); the same crawl with the portal SSAO switched off, 3.13 (AO appearing in every corner at once).
Entrance lip at that hole (shadowed half / lit half, 0-255, room floor just outside vs duct floor just inside): before
27 / 46 vs 54 / 53 (flat default light); after 27 / 46 vs 27 / 42, with the room's shadow edge running on into the duct.

## Far links

A hole can lead anywhere in the world, far outside the load radius (for example into a far zone such as a future
Poolrooms). Only what the window can show is loaded at the far end; nothing in between.

- **Which holes.** `FarLinks.PartnerOf`: the lattice links district (1, 1) of every `farLinkSpacing` x `farLinkSpacing`
  square (world settings, default 6) to its (spacing - 2, spacing - 2) district, about 210-250 m away; `FarLinks.Add`
  links two districts by hand (register before the world generates them). Each district places its own far hole from its
  own layout (`CrawlPortals.PlaceFar`, its own random stream, after the ordinary pairs, which are unchanged). When a chunk
  is cut from its plan, `WorldGenerator.ResolveFarLinks` finds the partner in the other district's plan (only that plan
  is generated, not its chunks); if the other end could not be placed, the hole is dropped and the wall stays whole.
  `CrawlPortal.far` / `partnerDistrict` carry this; at runtime a far link is simply a partner more than 36 m away.
- **The far site.** While the player is within 26 m of a far hole (released at 34 m, or kept while inside it),
  `CrawlPortalSystem.UpdateSites` asks `WorldStreamer.SetSite` for a circle of 20 m centred 10 m in front of the partner:
  what the window can show (the fog is black by 34 m). That is about 9-12 chunks, built through the normal worker
  pipeline after the player's own rings, and never unloaded while requested. Seed 1234 test: 61 chunks loaded with the
  site, against ~51 without.
- **Its light.** `FixtureLightManager` keeps a second `LightGrid` (`siteGrid`, its own compute shader copy, created on
  demand, switched off 20 s after the last far site) centred on the partner, fed with the fixtures around it; its real
  light pool also counts the far site (lookahead). The portal camera that shows a far site binds the site grid's globals
  for its render only (`LightGrid.BindGlobals`, main grid restored at the camera's end). The duct's far-end light is
  sampled from the site grid. Until the site is loaded and covered by the site grid, the window shows its fallback.
- **The trip.** On crossing, the site grid (already filled) becomes the main grid that same frame and the old main grid
  becomes the site grid of the place just left (`SwapGridsIfSiteCovers`); the sites are re-requested before the streamer's
  next update, so the area behind the hole stays loaded for the way back. The player's own rings then stream in around
  the new position (40 chunks over about 2 s in the test; the area in view was already there).
- **Checked** (seed 1234, hole (111.0, 0.4, 75.7) -> (252.6, 0.4, 271.3), 214 m): the window shows the far room; a
  recorded crawl through has a frame difference of 1.22 at the trip (camcorder noise 1.18); afterwards the old area is
  still loaded and lit by the swapped grid. EditMode test `FarLinksJoinDistantDistricts`.
- **Costs.** Near a far hole: the site's chunks and a second grid (two 128 x 24 x 128 half-float textures plus small
  buffers, about 8 MB, and its incremental compute). Nothing at all elsewhere.

Limits: two live windows; windows seen through windows are not drawn; the duct's middle is a simple falloff between the
two ends; a far link's far end is the ordinary generator's world at that place (a Poolrooms zone needs its own zone /
generator there, then a FarLinks rule or `FarLinks.Add` pointing into it).
