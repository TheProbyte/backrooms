# Crawl spaces and noclip doors (world version 13)

## Crawl spaces: vents into special rooms

(2026-10-31) The special room is now an ordinary small room (the owner: "a small room with carpet, normal ceiling and
paint walls, reusing from other rooms"): the world's carpet, its acoustic drop ceiling on its grid, its painted wall and
baseboards (URP Lit copies with their normal maps: `SpecialRoomMaterials`), and a round flush-mount light in the middle of
the ceiling. The shelves, stacked boxes, sleeping bag, lantern and chalk tally are gone; what lies in the room is chosen
with the same dice as before (the same things, and the same ones already taken), on the floor along the walls. The vent
ducts (every crawl hole's and the rooms') are lined with the owner's galvanised sheet-metal photo
(`Textures/Photo/Galvanized_Albedo`).

Every crawl vent is a crawl portal (the existing `CrawlPortalView` / `CrawlPortalSystem`). Crawl in (Ctrl) and you come
out of the vent of a small special room. Crawl back through that vent and you come out where you went in.

**Where the room is.** It is real geometry in the space above the drop ceiling, right over the vent. The vent's own
room and its neighbours have ceilings of 2.7 to 4 m, and the next storey is 9 m up.

Why the space above the ceiling:
- It is always there over ordinary rooms. No room ever needs to be made smaller or moved.
- Generation can check it from the district plan alone: nothing open above (atria, wells, stair halls), no tall
  volume, ceilings at most 4 m.
- The room sits inside the vent's own chunk, so that chunk builds it and it loads and unloads with it.
- No player can see into that space: drop ceilings are closed. The rooms' surfaces use only the Default rendering
  layer, so no fixture light reaches them either.

Leftover space behind rooms would have meant shrinking rooms and checking walkability again. The space above the
ceiling needs neither.

**Placement** (`CrawlPortals.PlaceSpaces`, deterministic from seed and district):
- Each vent is a hole at the foot of a solid wall between two ordinary rooms, as before (`Candidates`).
- Its room covers a footprint of 2-3 x 2-3 cells, starting at the vent's cell and running away from the wall, inside
  the same chunk.
- Every room under the footprint must be ordinary.
- The room's floor is 0.4 m above the highest ceiling under it, and the room is 2.3 m high inside. "Under it" includes
  the up to 3 cells behind the hole, which the room's own duct runs over: a taller ceiling there would put its collider
  across the duct and catch a crawling player.
- The duct is 1.9 m long, so it ends inside those cells with room to spare before their far wall (with 2.2 m, a wall
  right there could stop the crawling player before the eye reached the crossing point).
- Its vent is in its back wall, straight above the hole.
- Footprints and ducts never overlap.
- There are as many vents as the old pairs had holes: 2 x `crawlPortals` (3) per district.
- The Poolrooms have none.

**Building** (`OfficeChunkBuilder.CrawlSpaces.cs`, `Interaction/SpecialRoom.cs`):
- The shell is a sealed box: inward faces, colliders, and the back wall open over the vent.
- The wall collider over the hole below stops under the room's floor, so the room's duct can pass back through that
  wall line.
- The room's vent is a second `CrawlPortalView` partnered with the hole.
- Materials (`SpecialRoomMaterials`) are the building's textures on URP Lit.
- The light is the room's own: a bare bulb, or a lantern in the camp. It casts shadows, so it never shines down
  through the floor, and it is on only while the camera is within 16 m.
- The passage is the vent's duct: short, tight and dark, lit only by what spills from either end.

**Templates** (from the room's own dice):

| Template | Contents |
| --- | --- |
| Maintenance closet | A steel shelf against the front wall with 2-4 supplies |
| Sealed storage | A shelf on a side wall, stacked boxes, and 1-3 things on the shelf |
| Old camp | A sleeping bag, a battery lantern left on, a few things on the floor, and a chalk tally of days on the wall |

Models (`Tools/Blender/build_items2.py`): UtilityShelf, Lantern, SleepingBag.

**Loot** is placeholder for now: the existing items. Things lying in a room belong to its chunk and are made each time
it loads. Picking one up takes it for good (`Saves.LootRegistry`, saved with the world): it becomes an ordinary item
and the room never makes it again.

## Noclip doors

`Generation/NoclipDoors.cs` places them:
- In Level 0 there is one door per square of `noclipDoorSpacing` x spacing districts (4 x 4, so about one per 230 m
  square). The district and the wall are picked from the seed.
- A door's wall is solid between two ordinary rooms, inside a longer stretch of wall, with nothing in front of it.
- Each Level 0 door leads to the arrival door of one Poolrooms district, also picked from the seed. A chunk finds
  that door's exact place when it is cut from its plan (`WorldGenerator.ResolveNoclipDoors`).
- Every Poolrooms district has one arrival door, on a wall with a dry deck in front.

**What a door is** (`OfficeChunkBuilder.Noclip.cs`):
- The wall is drawn as usual.
- The merged wall colliders leave its edge out. Only the wall around a 0.95 x 2.15 m opening stays solid.
- `NoclipPassage` marks the opening for the travel code.

**How it is marked** is a separate, swappable piece (`NoclipDoorMarking.Current`, an `IMarker`). Today it is
`TapeOutline`: two uprights and a top strip of blue tape on both faces. They sit just outside the opening, overlapping
at the corners and slightly crooked, made with the same marks as the tape roll. Set `Current` to another marker (or
null) for subtler doors.

**Walking through** (`Interaction/NoclipTravel.cs`):
- **Preload:** within 16 m of a door, the place it leads to is kept loaded as a streaming site.
- **Approach:** within 0.8 m of the wall inside the opening, the camcorder picture starts to break up and the mic
  starts to drop.
- **Crossing:** when the camera crosses the wall line:
  - the picture is at its worst;
  - frames are dropped: `CamcorderFrameHold.Freeze` holds the last one;
  - the mic cuts out and crackles (`CamcorderAudioChain.dropout` plus a crackle clip).
- **Arrival:**
  - The player is moved to the same place relative to the other door, just out of its face, turned to match.
  - The held frame stays until the floor there is loaded (normally it already is), and a moment longer.
  - The picture then rolls back into frame and settles over about a second.
  - The world left behind unloads as the streamer follows the player, out of sight.
- **The glitch** is part of the camcorder picture: `CamcorderRig.Glitch` / `GlitchRoll` go to `_CamGlitch` in
  `CamcorderPipeline.shader` (the recording's playback pass). It gives:
  - a vertical roll with the black blanking bar;
  - bands of lines torn sideways;
  - line jitter and wild tracking noise;
  - dropout dashes;
  - colour falling apart.
- **Two-way:** a Poolrooms door leads back to the face of the door you came in by (`NoclipTravel.Origin`, saved). If
  there is none, it leads to the spawn.

**Testing:** F5 > PLACES > TESTING has NEAREST VENT and NEAREST NOCLIP DOOR (press again for the next nearest).
ZONES > POOLROOMS ENTRANCE now goes to the nearest noclip door.

## What changed

- The old portal placements (pairs within a district, the far-link lattice, the Poolrooms' far crawl hole at spawn)
  are no longer used. Their code stays: `CrawlPortals.PlacePortals`, `FarLinks`.
- `ZoneRules.entrance` is unused.
- World version 13 (`WorldGenSettings.Version`): every seed's layout changed where vents and doors went.
- Tests: `CustomRoomTests` (crawl spaces lead to sealed rooms above ordinary rooms; noclip doors are rare, resolve and
  build on a worker thread), `PoolroomsTests` (every Poolrooms district has a dry door back).

## Known gaps

- From inside a special room's vent duct you can see the duct pass through the world's wall line. It is drawn only
  inside the duct, so it is never seen from the rooms.
- The light grid's indirect light knows nothing of special rooms. They use URP Lit and their own light, so it has no
  effect.
- Noclip doors only lead to the Poolrooms, the only pinned zone (`NoclipDoors.TargetZone`). More zones need a choice of
  target.
