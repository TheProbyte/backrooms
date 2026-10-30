# Plan: rooms worth making a base (2026-10-29)

The brief's loop is explore → find something → bring it home → improve the refuge → go farther. Exploring and carrying
things work; "home" does not exist yet, so nothing gives a reason to bring anything back. This plan adds places you want
to stay in, then the home itself.

## What already exists to build on

- Items you carry are yours and saved wherever you leave them (`Saves.LootRegistry`, free items via `ItemFactory`).
- Power: wall outlets per chunk (`Power/Sockets.cs`, `ChunkOutlets`: a chunk's outlets are all live or all dead), plugs,
  extension cords, desk and floor lamps that light only when plugged into a live outlet.
- Light switches per room, saved (`LightZones`): a room can be dark by choice.
- Doors with a latch and lever (`Interaction/Door.cs`), now fewer (F5 > WORLD > HOW OFTEN > Doors, half by default).
- Room styles in Level 0 (`RoomStyle`: backrooms, living room, warehouse), crawl spaces to small special rooms, mezzanines,
  sunken rooms, the Poolrooms.

## What makes a room a base

1. **Control:** one way in, with a door that shuts (later: something to wedge or bolt it).
2. **Power:** live outlets, made rare, so a room with them is worth something.
3. **Your own light and quiet:** dead ceiling lights but working outlets, so you light it yourself, away from the hum.
4. **A view or a landmark** you remember and can find again.
5. **Something to work with:** shelves, counters, a sink, furniture.
6. **It stays put:** if the building ever shifts, bases are the rooms that never do.

## Phase 1: good rooms (rare, one door, live outlets)

New room kinds, placed by the district generator like living rooms, each with one door, live outlets and its own props,
and a HOW OFTEN slider (about one per few districts):

- **Storage cage** (warehouse): chain-link cage with a gate, shelving inside, its own outlet strip.
- **Security office**: a small room off a hall, desk, monitors (dark at first), a window into the hall.
- **Break room**: counter and cupboards, sink, fridge (off), table and chairs, a microwave on a counter.
- **Janitor's closet**: deep shelves, mop sink, a breaker panel (phase 3).
- **Mezzanine office**: an office up a stair overlooking a tall hall (uses the mezzanine code).

Checked by: the generator tests (each kind turns up at its rate, always one door, outlets live) and screenshots of each
in World 3.

## Phase 2: home

- **Claiming:** a room becomes home when you leave a lamp lit in it with its door shut (no menu, nothing to press). The
  camcorder OSD stamps "HOME" once. One home at a time to start, so it means something (decision below).
- **It keeps what you do:** items stay exactly where you put them (they already do), its lights stay as you set them,
  its door as you left it.
- **Finding it again:** decision below. Options: the F6 map shows home and where you have walked; a "way home" noise
  through the camcorder mic gets louder toward it; or nothing, and you mark the way yourself with spray paint.
- **Comfort:** the hum fades inside with the ceiling lights off; a warmer room tone; your lamps' warm light.

## Phase 3: power as something to find

- Most outlets dead; live ones rare (in the good rooms, and a few elsewhere).
- **Breaker panels** (janitor's closets, electrical rooms): flipping one makes a district's outlets live and saves it.
  Extension cords carry power into the room you chose.
- Later: lights you can run from a panel too.

## Phase 4: making it yours

- Moving furniture in (sofas and armchairs already move; carrying them through doors).
- Wedging a door shut (a chair under the lever), a bolt.
- Later, with the brief's impossible architecture: home rooms are the ones that never change.

## Decisions needed

1. Claiming: automatic (lamp lit, door shut) or an explicit act (e.g. writing on the wall with the spray can)?
2. One home, or several?
3. Finding home: map, sound, or only your own marks?

## Order

Phase 1 first (visible right away and good to explore for), then 2, 3, 4. Each phase is played and checked in World 3
before the next.
