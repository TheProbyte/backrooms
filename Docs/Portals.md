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
  middle of the crawl is dim. The room behind's lights never reach it. Defaults live in the shader (`_EntryLight`,
  `_ExitLight`, `_Ambient`).

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
shadow maps for the window you are at or in (each camera re-renders the shadow atlas otherwise). A window that has no
picture yet shows a dim fallback colour, never black.

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

Limits: two live windows; windows seen through windows are not drawn; the duct's light is a simple two-ended falloff, not
sampled from the rooms; partners must be within streaming range (12-30 m).
