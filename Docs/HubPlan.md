# Plan: the Overflow hub (2026-11-07)

Every world starts in the hub: Overflow's own office, reached from the world by a service lift. Built in the 60s, done up
in the bright 70s company look, and now, in the 90s, run down and run by machines. Nobody works there any more, and the
place keeps going anyway. What are we even working for?

Pictures (a blockout, not the final look): `Docs/screenshots/hub/hub_floorplan.png`, `hub_mood_from_lift.png`,
`hub_mood_lobby.png`, `hub_mood_training.png`. Made by `Tools/Blender/hub/hub_blockout.py`.

## What the owner chose

| Question | Choice |
|---|---|
| Where | A place of its own, linked to the world by a lift. The same building in every world. |
| You start there | On every load, in a new world, and after blacking out |
| Size | A medium company office, one floor, about 22 x 22 m (a minute to walk round) |
| Mood | Built in the 60s, done up in the 70s, run down in the 90s: automated, nobody left |
| Rooms | Lobby (contracts board, pay window), training room, locker room, first-aid room, a locked manager's office |
| Staff | Machines only: a PA voice, a pay window that works by itself, a board that shows the contracts, a punch clock |
| Way out | A service lift, a short ride (8-12 s) with an odd Severance-style moment on the way |
| Safety | Safe, but eerie |
| Saved | Its own copy in each world (what you move there stays in that world). Co-op friends are in the host's hub |
| Changing it | Things can be picked up and moved; no building or breaking walls |
| The tape | Starts when all the players sit in the training room's chairs |
| Classic look | The hub is always in the Overflow look |
| Lift in the world | One lift at the world's start, in every world (Worlds 1-3 too: their start room gets reshaped, after a backup) |
| Wake spot | The first-aid room |
| Lockers | Just decoration for now |
| Board and window | Built and lit, reading NO CONTRACTS POSTED, the window's shutter down |
| PA | A chime and the words on screen, until the lines are recorded (script below) |
| Windows | None, only photos of a sunny car park and posters |
| Extras | Punch clock and cards, vending and coffee machines, crackly muzak from the ceiling speakers, faded posters |
| Story | The locked manager's office |

## How others do it

- **Lethal Company's ship:** the one safe place. Every trip starts and ends there, and it is small enough that the
  crew is always together. Here the lift plays that part.
- **Severance's lift:** the moment the character switches is a dolly zoom. The camera moves one way while the lens
  zooms the other, so the person stays the same size while the room stretches behind them. It is unsettling without
  anything happening.
- **The lift that hides a load** (Half-Life, Portal, Metroid Prime): two identical lift cabs, one at each end. During
  the ride the player is moved from one cab to the other at the same spot in it, while the far end loads. You never
  see a loading screen.
- **Unity:** the hub stays loaded beside the world. The world streams in around its lift while the cab "travels", then
  the doors open.

## The building (floor plan)

On a 1.2 m grid, like Level 0. Ceilings 2.7 m in the rooms, 3 m in the lobby and 3.9 m in the training room.

1. **First-aid room:** four cots, one for each player, a cabinet with a red cross, a machine that beeps. You wake here.
2. **Locker room:** a row of avocado lockers with name cards, a bench. You walk through it to get out, like the start
   of a shift.
3. **Lobby:**
   - The contracts board faces you as you come from the lift: a dark split-flap board reading NO CONTRACTS POSTED.
   - Beside it, the pay window: a hatch with its shutter down, with the pay office's machines behind it (no way in).
   - An empty reception desk with a bell and a dusty chair.
   - Two orange sofas and a low table.
   - Dead plants in the corners, faded posters, two ceiling panels gone dark.
4. **Vending alcove:** two drinks machines (one is the game's existing machine), a coffee machine, one out of order.
5. **Training room:** taller than the rest (3.9 m), greyer, on a bare concrete floor with saw-cut joints and darker
   patches, its walls a paler grey-beige above the company stripe. Three rows of stacking chairs and a TV on a trolley. When every player in the game is sitting
   down, the lights dim and the tape plays. Standing up pauses it.
6. **Manager's office:** a locked door with a nameplate, and blinds over its window onto the lobby that you can see
   through a little: dust on the desk, a calendar stuck on a day in the 70s, a chair turned to face the wall.
7. **Lift hall and the service lift:**
   - The punch clock and the card rack are on the way to the lift. Each player has a card with their name, and you
     clock in by pushing it in.
   - A freight lift with a folding gate, big enough for four and a cart.

The way through: wake on a cot, then the lockers, the lobby, the punch clock, and the lift.

## The lift ride (8-12 s)

1. Pull the gate shut and press the button. The motor starts, the cab shakes, and the floor dial turns.
2. **The odd moment, halfway down, about 3 s:**
   - the view stretches in a dolly zoom: your field of view widens while the cab seems to draw in
   - the picture warbles: it bends in and out and squeezes one way then the other (the lens, not the tape: no static)
   - the hum warbles down to one low tone, and the cab light dims
3. In that moment you are moved to the other cab, at the same spot in it.
4. Things slow and settle, there is a thunk, and the gate opens on the other side.

- **In co-op:** each end always has a cab waiting. Whoever is in the cab rides, alone or together; nobody waits.
- **The world's end:** a lift lobby built into each world's start room, with the same cab.
- **Your stuff:** carts and items in the cab ride with you.

## Spawning

- **Loading a world, or a new one:** you wake on a first-aid cot, in a short blur and fade-in. In co-op, friends
  joining take cots 2-4.
- **A new world, the first time:** the chime, then: "Good morning. Please report to the training room for orientation."
- **Blacking out:** the game has no blackouts yet; they come with office hours. For now, the hub only needs a wake-up
  that they can call later.
- **Your things:** what you left in the world stays there. What you carry comes up in the lift with you.

## Sound

- **Muzak:** a crackly easy-listening loop from the ceiling speakers, made in code like the orientation video's music.
  No downloads.
- The hum of the lights, the odd tick of a cooling pipe, and the vending machine's compressor.
- **PA:** a two-note chime, then the line on screen. Lines to record later, in the tape's narrator voice, 20 years older
  and tired:
  - "Good morning. Please report to the training room for orientation."
  - "Contracts are posted on the board. There are no contracts posted."
  - "Please remember to clock in. Time is money. Whose money is not your concern."
  - "Welcome back. You were found. Overflow thanks you for your continued attendance."
  - "The manager is in a meeting."

## Building it

**Phase A: the shell, the lift and spawning**
- The hub is built by code, as the world's chunks are, with the Level 0 parts already made: walls, panels, doors and
  ceiling. It is placed well away from anywhere the world builds.
- Its lights go into the game's lighting system, and it is always in the Overflow look.
- The freight lift: its cab, the gate, the ride and the switch between cabs. Its sounds come from today's lift.
- A lift lobby in chunk (0, 0) of every world, and the world's version number raised. Worlds 1-3 are backed up first.
- Spawning: on a cot on load, and on the cots by player number in co-op.
- Build mode and wall breaking are turned off inside the hub. Moved things are saved with the world.

**Phase B: the rooms**
- New Blender scripts, made with `--style overflow`:
  - the freight lift and its gate
  - the punch clock and the card rack
  - the split-flap board
  - the pay window's hatch and shutter
  - lockers, cots and the first-aid cabinet
  - the stacking chair, and the TV trolley with its CRT
  - the reception desk, the coffee machine and the ceiling speaker
  - dead plants and posters, with the company's slogans
- Reused: sofas, the low table, the drinks machine, the office chair, and the doors.
- The manager's office, dressed and locked.

**Phase C: sound, the PA and the tape**
- The muzak, the PA chime and its words on screen.
- The training room plays when everyone is seated. The tape plays once its cut is locked; until then the TV shows a
  test card and "TAPE NOT LOADED".

**Phase D: checks**
- Pictures through the camcorder and the benchmark.
- A co-op test: four players on the cots, the tape when everyone is seated, and a shared lift ride.
- World 3 backed up first and restored after.

## Not in this plan

- Contracts, pay, office hours and blacking out. These are next, and the hub is built ready for them.
- Using the lockers to store things.

## Status

### Phase A: the shell, the lift and spawning: built (2026-11-07)

- **The hub** is a build-mode blueprint made in code (`CustomRooms/OverflowRooms.cs`, never in the player's library),
  stamped into district (0, 0) of its own storey (-44, about 396 m down: `Generation/HubPlan.cs`) and sealed in by dark
  void. The world's chunk builder builds it, so its walls, lights, colliders, doorways, marks and sounds are the game's own.
  - Rooms as planned. The lobby is 2.7 m, not 3 m, so it keeps the drop ceiling and its panels (taller rooms get plaster
    and round lights); the training room is 3.9 m on concrete (a new `FloorFinish.Concrete`, which build mode has too).
  - Furniture the game already has for now: sofas, a table, the reception desk and chair, the clock, two drinks machines
    (one unplugged), twelve school chairs in rows, the manager's desk with the chair turned to the wall, shelving for
    lockers. Faded notices and damp on the walls, a hum and air in the vents. Two lobby lights dead, one stuttering.
  - The streamer loads the hub's four chunks while you are in it, or while a lift is going down (`WorldStreamer.PreloadHub`).
- **Waking in the hub:** a world being played always starts in the first-aid room (`WorldStreamer.PlaceInHub`, a spot per
  player number for co-op). The main menu's world and build mode's test are as before.
- **The service lift** (`Interaction/HubLift.cs`): a freight cab (2.8 x 2.1 m) with a folding gate, landing doors in the
  company's amber with the stripe, a call button outside and a button inside, in both shafts. The ride is 10 s: motor, a
  tremble, and halfway the odd moment, during which you, and anything lying in the cab, move to the other cab at the
  same place.
  - **Shared by everyone (the owner, 2026-11-07):** each end always has a cab waiting, open. Press the button and whoever
    is inside rides: alone or all together, and you all come out at the same place. The cab at the far end shuts while
    riders arrive (nobody pops in), then opens with them; the one they left opens again, empty. In co-op the button press
    is shared (Net: Poke), so every player's game runs the same lift and moves its own player.
  - **The odd moment is the lens, not the tape (the owner: "a warble like stretching ... like Severance"):** the lens
    widens by about half while the view dollies forward to keep what is a metre off its size (a dolly zoom: the cab and a
    friend's face stretch round each other), with a slow warble in the zoom, the picture bending in and out and squeezing
    one way then the other; the hum warbles down to one low tone and the cab light dims. No static, no frozen frame.
    `CamcorderRig.FovScale`, `Dolly`, `LensWarp`, `LensStretch`.
- **The world's end:** the shaft stands at the north of the spawn hall in every world (the spawn hall is now 12 x 9 cells,
  the shaft 3 x 3 north of it with rooms either side). Worlds 1-3 change there.
- **Safe:** the tension, the shadow and things moving in the dark are off in the hub; its walls knock but never break.
  Items and furniture in it stay put (the fall-through checks know about its storey). Footsteps and the building's
  settling read the hub's floor there.
- **Checked in World 3** (backed up, restored after): wake spot, lobby, training room, a ride down and back up with a
  roll of tape on the cab floor, the main menu's world. Edit-mode tests 187 of 187 (one old failure fixed: the art style
  read from a worker thread).
- **Co-op checked** (this PC, the editor hosting World 3 and a test copy joining): the guest woke on its own spot in
  first aid; both rode down together and came out side by side; the guest rode back up alone while the host stayed.
- **Not yet:** the Overflow look in the hub when ART STYLE is Classic (it comes with its own materials in phase B); the
  hub's own models, the board, the pay window, the
  manager's window, the PA, the muzak and the tape (phases B and C).

### The owner's round of fixes (2026-11-07)

- **Lighting:** the hub stood on its own storey 396 m down, outside the light grid (it covers the ground storey and the
  one above): no bounce light (black ceilings), real lights not blocked properly. It now stands on the ground storey about
  5 km east (`HubPlan.District` = (88, 0)) in a 3 x 3-district zone that is the hub and sealed dark void only; nothing of
  the world is built there and the streamer loads only the hub's four chunks while you are in it (it was 49 more).
- **Always the Overflow look:** the hub's chunks are built with the look forced to Overflow (`ArtStyle.Forcing`, a
  per-thread override) and an Overflow copy of the biome (`ArtStyle.OverflowBiome`); FittingModels keeps the Overflow
  remakes as well when the world is Classic.
- **The new wall** (`WallFinish.HubPanel`, `HubMaterials`, `Tools/Textures/make_hub_wall.py`): dark walnut boards to
  0.95 m, a cap rail, the company stripe to 1.15 m, painted cream above, in every room. **Concrete floors** in every room
  (the Overflow painted slab).
- **The hub's own models** (`Tools/Blender/build_hub.py`, `--style overflow`, in `Resources/Overflow/Fittings`), placed as
  `PropKind.Fitting`: lockers (four banks) and a bench, four cots and the first-aid cabinet, the AV cart with its CRT and
  VCR, the contracts board (NO CONTRACTS POSTED), the reception counter with the stripe and a bell, a coffee table, the
  punch clock and the time cards, a coffee machine, dead plants, three posters (WORK HARD. WORK FAST. BE BACK BY
  CLOSING. / THERE'S ALWAYS MORE ROOM. / IF IT ISN'T ON TAPE, IT DIDN'T HAPPEN.). You wake at the foot of your cot.
- **The training seats are bolted down** (pinned: "IT WON'T BUDGE").
- **Shadows:** the ray-traced shadows took each ceiling panel at its true size (wide, soft penumbras) and blended over 12
  frames; now `RayTracedLighting.penumbra` 0.4 (crisper) and 8 frames (less smear). For the whole game.

### Round 2: everything visual (2026-11-07, the owner: "do it all")

- **Arrival view:** the OVERFLOW sign (amber block letters on a chocolate plate, the stripe under) over the contracts
  board; the pay window beside it (frame, ledge, roller shutter down, a CLOSED card, a PAY plate, a speaker grille); the
  manager's locked door (MANAGER, PRIVATE); his window with half-open blinds, two slats pushed flat where someone peeked;
  a rubber runner down the middle from the lift; a mat at the lift. Signs with arrows: TRAINING, LOCKERS / FIRST AID,
  LIFT, FIRST AID.
- **Ceiling and light:** the hub's ceiling tiles three and a half times as worn as Level 0's (stained, sagging, missing);
  each room its own light (the lobby and vending corner warm and yellowed, first aid and the lockers sickly green, one
  stuttering tube that buzzes in the lockers, the manager's office one dim tube); a desk lamp on the reception counter;
  ceiling speakers in each room (for the PA and the muzak to come).
- **Doors:** the world's wooden doors in the hub's doorways (first aid, lockers, the training room's double doors). The
  door builder now makes the hub's doors on a chunk's edge too (the training room's sit on one).
- **Run-down 90s:** a bucket under a leak (a stain above it), OUT OF ORDER taped to the coffee machine, the clock stopped
  at five (closing time), uncollected time cards on the floor, the manager's desk and chair under dust sheets and his
  calendar stopped at March 1979, the posters sun-faded, the spare chairs stacked in the training room.
- **The 70s sofa:** mustard with orange cushions on a chocolate plinth (HubSofa), sat on and moved like any sofa (a sofa
  can now be built from a named model: `MovableSpec.fitting`, kept in saves).
- Models: `Tools/Blender/build_hub.py` (22 more, 37 in all), placed in `CustomRooms/OverflowRooms.cs`.
- **Not done:** the light from the desk lamp and the TV's static (the lamp is the game's desk lamp; whether it lights
  without power there is not checked); faint dashed lines show on the world's door model in the hub (from the doors
  themselves, as in Level 0).

### Round 3: the wake-up, the TV, the dial (2026-11-07)

- **Brought out onto your cot** (the owner: "a small opening leading into darkness with the floor being conveyor and we
  will be conveyored onto our cots ... like the orientation video"): behind each cot's head a return chute (HubChute: a
  dark tunnel through the wall, a rubber belt on rollers, an amber mouth, RETURNS), shut by a steel shutter. On every load
  your shutter is up and you lie on the belt in the dark looking up; a clunk, and it carries you out feet first (rumbling,
  a jolt over each roller) until your head is on the pillow and you look up at the ceiling tiles. Space, E or a walk key
  gets you up at the foot of the cot; the shutter drops behind you (solid: nobody climbs back in).
  `Interaction/HubWake.cs` (the controller's seat holds you lying; the body half reclining).
- **The TV:** snow, a rolling band, a flickering blue glow and a hiss (`Interaction/HubTvScreen.cs`).
- **The lift's floor dial:** a brass half-moon beside each lift's doors (HUB on the left, L0 on the right); its needle
  swings across while a cab travels to or from there, trembling as it goes (HubLift).
- **More of the building:** a YOU ARE HERE map in the lift hall (with a cafeteria that is not there), a drinking fountain,
  lit EXIT signs over the first-aid and training-room doors, an extinguisher cabinet, a thermostat.
- **The desk lamp lights:** reception moved into the lobby's north-east corner, facing the lift, its lamp on the desk
  behind the counter plugged into an outlet on the wall behind (a desk lamp is an item: lit only from a live outlet in
  its cord's reach).
- **Not fixed:** the faint bright lines on the doors' panels are the light catching the rounded top edges of the rails
  (a highlight, not gaps: running the panel boards in under the frame changed nothing, and was undone). A flatter edge or
  a duller finish on the door would take them out.
- **Co-op, not checked:** a friend's shutter opens only in their own game, so you see them come through a shut shutter.
