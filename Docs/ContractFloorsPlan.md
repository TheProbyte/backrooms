# Plan: contract floors, a world with edges for each job (2026-11-07)

For each job, the hub's lift takes you down to a floor of rented space with walls at its edges. A new floor is
made for every contract, sized for the job, with its edges sealed off. Out there are the seams: places where the walls
are thin and goods come through from the real world. The infinite world stays as a free-roam mode.

## What the owner chose

| Question | Choice |
|---|---|
| Each trip | A new place for every contract, made from its own seed. What you moved there is gone after the job |
| Size | Varies by contract: small (about 100 m across), medium (about 200 m), large (about 350 m). Bigger pays more |
| Edges | A sealed-off building: chained fire doors, bricked-up doorways, taped-off halls, NOT LEASED signs |
| The infinite world | Kept as a free-roam mode |
| Storeys | One storey to start |
| Jobs | Storage through the seams (the orientation video's story): goods come in from the real world, get shelved here, or get carried through to another seam |
| Time | An office-hours clock, shown on the camcorder's screen (its time stamp and battery) |
| Danger | The building only for now (blackouts, getting lost, the tension and dark changes). Creatures come in a later plan |

## The story (the orientation video's script)

- The world is full. Behind it there's more world, and Overflow rents that space out as storage.
- "Out there, the walls get thin. We call those seams." With your ear to one you hear a warehouse, a print shop or
  somebody's lunch break. Push a chute in, and the goods come through.
- The board's contracts: "Store this. Ship that. Furnish these." Do the job, then collect your pay at the window.
- Office hours: at the closing bell the lights go down and "low activity" begins. Be back before then.
- "If it isn't on tape, it didn't happen": the camcorder's tape is the proof that gets you paid.

## How others do it

- **Lethal Company:** the ship is the safe base. Each landing makes a fresh interior from a seed, sized for that moon. A
  quota and the clock end the day.
- **R.E.P.O.:** a level is a set number of rooms built from ready-made modules, graded by difficulty, with a set number
  of extraction points. By default that's 24 rooms and 4 extraction points. The host's settings decide the level
  for everyone in co-op ([Difficulty Editor](https://thunderstore.io/c/repo/p/CbIPOK_Studio/Difficulty_Editor/),
  [Harder Generation](https://www.nexusmods.com/repo/mods/76)).
- **DunGen** (a Unity asset that Lethal Company modders use): a level follows a "flow". The main path's length is a
  range, branches come off it, and each section has its own set of tiles
  ([dungeon flow](https://dungen-docs.aegongames.com/2.18/core-concepts/dungeon-flow/)). Here, that's how to place
  the lift at the start and the seams at set distances along the way.
- **Satisfactory:** materials come from fixed nodes and get carried to storage. Here the seams are the nodes, and
  the shelves are the storage.
- **Unity:** release builds measure frame times with `ProfilerRecorder` and `FrameTimingManager`
  ([docs](https://docs.unity3d.com/6000.4/Documentation/Manual/frame-timing-manager-enable.html)). The game's
  `-benchmark` uses both.

## Where it stands now (measured)

The built game's `-benchmark` (RTX 4070, Ryzen 7 5800X, 960 x 540 window, 60 fps cap, the saved settings). It now also
visits the hub and rides the lift both ways:

| Where | Median | Frames over 50 ms | Worst |
|---|---|---|---|
| Hub, standing in the lift hall | 60 fps | 0 | 17 ms |
| Hub, walking | 60 fps | 0 | 17 ms |
| Lift ride down (the world loads at the far end) | 60 fps | 28 of 702 | 108 ms |
| Lift ride up | 60 fps | 6 of 826 | 138 ms |
| Just arrived in a corridor | 60 fps | 3 | 123 ms |
| Just arrived at a crawl hole | 60 fps | 2 | 117 ms |
| Walking through the world, streaming new chunks | 60 fps | 0 | 22 ms |

What this means:

- Walking through the infinite world is already smooth. Streaming one chunk at a time isn't the problem.
- The stutters come when many chunks get built at once: arriving somewhere new, and above all the lift ride, when the
  far end loads.
- A floor with edges won't fix this on its own. What fixes it is spreading the building across the ride, a few
  milliseconds each frame, so the doors open on a finished floor. Phase A does that first. It helps the hub's lift
  in the infinite world as well.

## The design

1. **The rented floor.** The contract's seed and size make one floor, about 7 x 7 chunks (small), 14 x 14 (medium) or
   24 x 24 (large), one storey. Inside, it's the existing generator's rooms, halls, landmarks and biomes. Everything
   outside the floor is sealed void, the way the hub's zone already works (`HubPlan.Generate`).
2. **The edges.** Wherever a room meets the edge of the floor, the wall gets an edge treatment: a fire door chained
   shut, a bricked-up doorway, a hall taped off with plastic sheeting, a NOT LEASED sign. You never get a way through.
3. **Loading on the ride down.** While the cab "travels", every chunk's layout on the floor is worked out on worker
   threads, which is cheap. Meshes are still built near you, as now. A small floor stays fully loaded (49 chunks).
   On medium and large floors meshes stream in near you, but nothing new gets generated while you play.
4. **Seams.** Walls where the world is thin, placed by distance from the lift (the far ones pay more). Each has a
   kind (warehouse, print shop, office supplies, furniture showroom), its own sound behind the wall, and a chute that
   the goods slide out of.
5. **Storage.** Racks and shelving in the floor's storage rooms (the warehouse biome already has racks). A contract
   counts its boxes on shelves within the floor.
6. **Contracts.** The board posts a few contracts, each with a kind, size, seams, amount and pay:
   - **Store this:** bring the boxes in from a seam and shelve them.
   - **Ship that:** carry the goods from one seam to another seam's chute.
   - **Furnish these:** put furniture in marked rooms.
   Picking one sets where the lift goes. When you get back, the pay window pays if the tape shows the work.
7. **Office hours.** The shift clock is the camcorder's time stamp in the corner of its screen, and the battery runs
   down with it. At the closing bell the lights go down and low activity starts (the tension director and dark changes
   get stronger). Be back at the lift before then.
8. **Co-op.** The host picks the contract. Its seed and size go to the friends with the world, so every game makes the
   same floor. The world check that already runs when a friend joins covers the floor's chunks too. A floor with edges
   also makes it simpler to keep track of who owns which item.
9. **Saves.** The world save keeps the hub, the pay and the contracts done. A contract floor is thrown away after
   its job. Old saves' free-roam things (paint, marks, items near the world's start) stay in the save untouched.
10. **Free roam:** set aside for now (the owner's call). The infinite world's code stays; nothing in the main game leads
    there.

## Phases

- **A. The floor:** first, spread out the chunk building during the lift ride (a time budget each frame), and aim for
  no frames over 50 ms on the ride in the benchmark. Then a floor with edges, sealed off, and the lift going down to a
  test floor from the board (no contracts yet), loaded during the ride. Benchmark it against the infinite world.
- **B. Seams and chutes:** the thin walls, their sounds, and a chute that delivers boxes. Shelves that count what's on
  them.
- **C. Contracts:** the board, store/ship/furnish, the pay window and checking the tape.
- **D. Office hours:** the camcorder's clock and battery, the closing bell, low activity.
- **E. Free roam:** the infinite world as a mode in the menu.

## The owner's further answers (2026-11-07)

| Question | Choice |
|---|---|
| Free roam | Its own thing per world, not part of the main game for now |
| Where the lift comes out | In the middle of the floor |
| Sizes | 115 / 230 / 345 m (2, 4, 6 districts across) |
| A seam | A crack or tear in the wall |
| Jobs | Storage: goods come through seams from the real world, get shelved in the floor's storage or carried to another seam |
| Shift | 15 minutes, 9 to 5 on the camcorder's clock |
| Closing | Lights out, then a minute later you black out and wake on your cot; the job is lost |
| Pay | A running total for now |
| Tape | Not checked yet |

## Built (2026-11-07)

**The lift ride (phase A's first step).** A development build's profile of the ride down (`-benchmark-spikes-lift`,
read with ProfilerDriver) showed the slow frames were neither scripts nor the graphics card being busy: every new
mesh needs its own ray-tracing structure and vertex buffers, each a fresh block of graphics memory, and making those
holds up the driver's present. Fixed by:
- loading the whole far end (36 m round the lift) before the swap, not 8 m (the rest streamed in after the doors
  opened);
- building the far end's ray-tracing scene during the ride, after it has loaded, a fixed floor area a frame
  (`RayTracedLighting.Prewarm`), and starting the camera's own scene at that size when it lands in it;
- the camera's own growth after any jump now adds a fixed area a frame (about a second to full) instead of a radius;
- destroying old chunks two a frame (the world's 40 going at once took 150 ms in one frame on the ride up).

| Lift ride (release build, 60 fps cap) | Frames over 50 ms before | After | Worst before | After |
|---|---|---|---|---|
| Down | 23-28 | 1-2 | 108-121 ms | 61-76 ms |
| Up | 6 | 1 | 138 ms | 61-70 ms |

The ride is about 1.5 s longer while the far end warms up (the odd moment holds). `-benchmark-lift` times just the
rides; `-benchmark` now also visits the hub and rides both ways.

**Contract floors (A).** `Generation/LeasePlan` (slots, sizes, sealing), hooks in WorldGenSettings.SealedBetween,
DistrictGenerator (the spawn hall and lift in each floor's centre district), Landmarks (the lift; nothing two storeys
tall), WorldGenerator (one storey; crawl holes and noclip doors that would lead out are dropped), WorldStreamer (only
the current floor's chunks load), HubPlan.WorldCab (the world's cab follows the current floor; HubLift moves it).
`Generation/LeaseEdges` dresses the edge: LeaseFireDoor, LeaseSheeting, LeaseBricked, LeaseSign (Tools/Blender/build_lease.py).

**Seams and storage (B).** `Generation/LeaseSeams`: seams (SeamCrack, a pinned wall fitting, its kind the variant) and
storage rooms (cleared, lit, lined with empty shelving whose shelves are solid, a STORAGE sign). `Interaction/Seam`:
the glow (Backrooms/SeamGlow, cut to the crack's shape), its light, the muffled sound of the place behind it, the
INTAKE chute (SeamChute) and its button. Boxes slide out four at a time, onto the floor in front, stacked once full.

**Contracts (C).** `Interaction/Contracts`: the board (painted while the game runs, `Generation/PixelFont`), a TAKE
button under each row, PAY beside the pay window, the save's `board`, `active`, `pay`, `jobsDone`, `nextSlot`, `shift`
(ContractData). Store counts the contract's boxes inside the floor's storage rooms; Ship takes a box set down in the
destination chute's mouth. The lift will not go down without a contract.

**Office hours (D).** The shift starts at the first step out on the floor; the camcorder's clock shows 9 AM to 5 PM over
15 minutes; at 5 a bell, the floor's lights stutter out (LightZones.ClosingLevel), and a minute later anyone still down
there drops what they hold, blacks out and wakes on their cot (HubWake). The job is lost; the pay window clears it.

**Checked in Play** (a test world in its own save folder; the owner's saves untouched): the board posts three jobs;
taking one, the ride down lands in the floor's lit arrival hall with the shift on; a seam glows and its chute sends
boxes out; seven boxes in storage finish a Store job; the ride up and PAY add $125 and post a new board on three new
floors; closing at 5 PM puts the floor's lights out, and a minute later the player wakes on their cot with the job lost;
on a Ship job, boxes set down in the destination chute are taken and counted. EditMode tests 187 of 187; the build runs
the benchmark (now taking a contract before the ride) with no errors.

**Not checked:** co-op with contracts (two games should post the same board and share the presses, untested); the tape
(REC) check; the lens glow of fittings in rooms after closing on a floor reloaded later.
Pictures: `Docs/screenshots/contracts/`.

## Walk-in seams (2026-11-07, the owner's second round)

| Question | Choice |
|---|---|
| Inside a seam | A hand-built room per kind, about 8 x 10 m |
| Danger | Safe, but the tears shut at closing: anyone inside is trapped and blacks out |
| Boxes | Stacked on a pallet under an OVERFLOW PICKUP sign |
| Ship | Set the boxes down in the destination place's taped DELIVERIES bay |
| The chute | Built (it pulls the stack through by itself), not handed out yet: by hand only for now |

Built: a seam's tear is a noclip door the tear's size (`NoclipDoor.seam`, 0.74 x 2.2 m, no tape; never on a chunk
border; its back face shows a tear too) into its kind's room. The rooms (`CustomRooms/SeamRooms`: TALLMORE WHOLESALE,
QUILLBY PRINT & COPY, PAPERLARK OFFICE SUPPLY, BELLWEATHER HOME FURNISHINGS, invented) stand in a sealed zone 350 m
north of the hub (`Generation/SeamPlan`), each with its tear back (an arrival door: it leads to the seam you came in by).
Taking a contract stacks its boxes on the source room's pallet (fixed ids, moved props). Carrying something through a
tear brings it along in your hands (NoclipTravel). From closing the tears go dim and solid (`NoclipPassage.SeamsShut`).
New models in build_lease.py: PrintPress, PaperStack, SeamPickupSign, SeamBay, SeamShop0-3; the tear widened to the floor.

Checked in Play: walking into a seam on the floor arrives in its room; the boxes stand on the pallet; picking one up
and walking out through the tear brings it back onto the floor still in hand; walking back in and out again works.
Not checked: a Ship job's bay count, the tears shutting at closing with someone inside, co-op.

## One floor a world, the working day, improvements (2026-11-07, the owner's third round)

The owner: "the world is the same so you still have the day limit for getting in and out but each day you can improve one
of your outputs ... like satisfactory"; the seams "more like noclip zones even visually, so just duct taped doorway zones",
their insides "clearly non backrooms", and everything in the Backrooms in the hub's style.

| Question | Choice |
|---|---|
| The floor | One per world, random from its seed, kept for good (`LeasePlan.ForSeed`) |
| Bigger | A 4 x 4-district core (about 230 m) and four wings (4 x 2 districts each), leased one at a time |
| Further out | Past the first wing leased, a noclip door into the Poolrooms once that is leased; more levels later |
| Improvements | Paid from the day's pay, optional, at most one a night |
| Stored boxes | Stay for good and earn rent every day; RETURN jobs take a client's boxes back |
| Style | All the Backrooms furniture remade in the hub's style |

Research (Satisfactory's HUB milestones and storage tiers, the day-quota loop of shift games, level-art contrast between
an uncanny space and an ordinary one) pointed the same way: progress you build into the place and keep, a limited daily
window that makes you choose, and the ordinary world drawn with the opposite light and colour to the uncanny one.

**The day** (`Interaction/Contracts`): the board posts three jobs a day from the leased parts' seams and storage rooms:
STORE (a client's boxes from their place into storage), SHIP (place to place), RETURN (a client's stored boxes back to
their DELIVERIES bay). Take any of them before the shift; the shift is 9 to 5 (later with overtime). PAY pays what is
done; when nothing is open, or closing has come, it ends the day: rent on the boxes kept in storage ($2 a box, more with
rent reviews), tomorrow's board. Each client's boxes carry their tape's colour (TALLMORE blue, QUILLBY red, PAPERLARK
green, BELLWEATHER plum), the same colour down their row on the board. The camcorder's date is the working day's (from
MAR 11 1996; 8:30 in the hub before the shift).

**Improvements** (`Interaction/Improvements`, the board on the lobby's south wall: NEXT and ORDER): an intake chute kit
(a crate by the lift on the floor; set it down in front of a seam and the chute is fitted there for good), a platform
truck (boxes ride on it), overtime (to 6, 7, 8 PM), backup lights (a minute more after closing), rent reviews, a wing
(north, east, south, west: its hoarding comes down, its seams and storage join the board from the next day), Poolrooms
access. Never during the shift; one a night.

**The floor's plan never depends on what is leased**: core and wings are one plan, a wing not leased is just not
streamed, and every way into it from the core is boarded up with plywood hoarding (NOT LEASED; `LeaseGates`,
`Interaction/LeaseGate`). So leasing a wing moves nothing already there. Only the Poolrooms door's district is planned
again when that is leased (`WorldGenerator.Forget`, `WorldStreamer.ForgetDistrict`).

**Seams** are door-sized noclip doors marked out in silver duct tape (`NoclipDoorMarking.DuctTape`) with Overflow's
printed label (OVERFLOW / the place / SEAM - KEEP CLEAR), no crack or glow; the muffled place behind it. Inside, the four
places are made of the real world's surfaces (`RealMaterials`: painted block or white drywall, polished slab or vinyl
tile, a steel deck or plaster ceiling, from `Tools/Textures/make_real_surfaces.py`), lit by LED high-bays and battens and
daylight through wired-glass windows and under a loading door (`OfficeChunkBuilder.RealLight`), with a radio and traffic
outside (`SoundKind.Radio`, `Traffic`), and their own things (`Tools/Blender/build_real.py`: safety signs, a pallet jack,
hi-vis vests, shop shelving full of stock, a counter with a register, a showroom with a SALE banner).

**The hub's style everywhere**: `Tools/Blender/build_furniture_hub.py` remakes Level 0's sofas, armchairs, office chair,
floor lamp, conference table, wall clock and wooden chair in the hub's language (same names, sizes and material slots, so
the Overflow prefabs take them as they are); the security office's desk is the hub's `HubDesk`.

## Open questions

- More levels past the Poolrooms; what is out there to do.
- The tape: a REC check on deliveries.
