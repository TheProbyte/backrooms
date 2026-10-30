# Moving things: how it works

## Controls
- **Hold left mouse** on furniture or a box within about 2.4 m. If it is a little too far you step in; if it
  is low you bend down. The right hand takes the nearest grab edge to where you look, a few centimetres
  different each time.
- **Furniture starts in push mode** (anything but a box light enough to carry). The camcorder stays at your eye in the
  left hand, one hand on it:
  - **W** pushes it ahead (the hand leads 0.25 m the way you walk, the arm kept bent so it has room), **A/D** slide it
    sideways, **S** walks back and it follows on the hand.
  - **The mouse turns you**, slower the heavier it is, and it turns with you (steering: up to 160 N m about the
    vertical, so it lags round a corner rather than snapping).
  - The push is applied at its balance point's height and the hand does not lift it, so it slides and turns rather
    than tipping. Taken looking down at it, the view comes up to look ahead over it.
  - **A sharp move of the mouse up** (about 300 counts in 0.15 s, `heaveCounts`) or **R** heaves it: the camcorder
    drops onto its strap and both hands take it (below). Lowered onto the floor upright, **a sharp move down** puts
    you back to pushing.
- **Heaved up (both hands)**, and boxes:
  - **Mouse left/right** swings your end sideways. Past the arm's limit you turn with it.
  - **Mouse up/down** lifts or lowers your end. Lift a long thing high enough and walk into it to stand it on end.
  - **WASD** walks and drags it with you, slower the heavier it is (and very slowly away from something stuck).
  - **Hold R**: the mouse turns it instead of moving your end (up/down tip it away or toward you, left/right
    roll it onto a side). Standing on the floor it rocks over the edge of its foot; lifted, one hand turns it about its
    balance point, two about the hands. The hand also holds the angle it took it at, as a wrist would (14 N m: a chair by
    its frame stays put, by a leg it swings, a sofa barely notices; past what it can hold it gives). `PropGrabber.Furniture.cs`.
  - The camcorder hangs on its strap at your chest (it comes back up a moment after you let go or set it down): the
    picture drops to chest height, looks ahead and a little down, swings as you start, stop and turn, bobs with your
    steps, and keeps 30 cm clear of what you hold, rising toward the eye to look over it (`HandheldCameraShake.strap`,
    `avoid`). Held at least 0.38 m out from you. The left hand takes the best grab edge across from the right: twice
    the strength and a lever between the grips, so a chair can be lifted and rolled over; a turn that needs more reach
    steps you toward it. Grab edges go round the sides and undersides too, so a chair is taken however it lies.
- **Right mouse** while holding shoves it and lets go. **Release left mouse** to let go.
- Each hand's pull is sized to the weight felt where it is applied (`PointMass`): pulling far from the middle, a
  chair mostly turns and weighs a few kilos (sized for the whole mass, it overshot every physics step and shook).
- **Right mouse** without holding: a quick push. Walking into light props nudges them.
- The left hand holds the camcorder: it dips and trembles a little while the right hand works, more for
  heavy things. (`PropGrabber.rightHanded` off brings back the old left-handed setup and left arm model.)
- **Items**: **click** one within about 2 m to pick it up in the right hand; **hold left mouse** to use it. There is
  no crosshair, so the aim is forgiving: if the click ray misses, the hand takes the item that makes most sense near
  where you look (within about 14 degrees, or 12 cm of the line, in sight, nearer and more central first). Camcorder
  focus brackets mark the item a click would take.
- **Q** tapped drops the item (with a little toss). **Hold Q** to wind up and release to throw: the longer you hold
  (0.2 to 0.9 s), the harder (3.5 to 13 m/s). While the hand holds an item it does nothing else.
- **E** puts the held item down by hand (2026-10-28, `PropGrabber.Place.cs`). **Tap E**: it is set down where you look,
  as it naturally rests (the sledgehammer on its side, chalk lying, the rest upright), lined up with the edge of the desk
  or a wall near it if within 15 degrees, and let go (about 0.3 s). **Hold E** (past 0.2 s): the hand brings it down there
  and keeps hold of it 4 mm above the surface. While E is held: look about to slide it (within the arm's reach with the
  body squatting and bending at the hips, never stepping; aimed further, the spot comes in toward the feet), **Shift** slows the view to a quarter for the last centimetre, the **scroll wheel** turns it 5 degrees a
  notch (1 with Shift), **R** tips it onto its next way of resting (upright, on its side, on the other side, upside down;
  the hammer: on its side, on its head, on the other side). It lines up with a desk's edge or a wall within 3 degrees and
  sits flush on a wall within 1 cm (brighter focus marks when it has); **Alt** held: no help. **Release E** to let go: a
  soft knock (of what it is and what it is on) and it stays exactly so, asleep until something knocks it, saved as
  placed. **Right click** with E held puts it back in the carry. It may stand on another item; it is not put into
  anything.
- **Hands in view** (Camera menu > HANDHELD): Full (as before), Low (the default: held things sit 7.5 cm lower and 3.5
  cm further out, at the bottom edge of the picture, the hand mostly out of shot) or Off (the working arm and hand are
  never drawn, only their shadow; held things lower still).
- A held item moves with the hand: it lags behind quick turns and bobs with your steps instead of staying fixed to the
  camera.
- **L**: the developer menu "Items and furniture" (see below). **L** or **Esc** closes it.
- **Chalk**: hold the left mouse button on a wall or floor within about 1.15 m and move your view to draw (freehand: an
  arrow, a number, anything). It wears down (30 m of line in a stick) and is gone when used up.
- **Blue tape**: carried on the fingers through its core. **Hold right mouse** at a wall, doorframe or floor within
  1.6 m: the hand raises the roll to it and the spot the tape will start at gets focus brackets and a little tab of
  tape. **Hold left mouse** to stick the end there and drag (you can let go of right mouse now); **release left mouse**
  to tear the strip off (up to 2.4 m a strip, 40 m a roll). **Right mouse again** while pulling cancels (the tape goes
  back on the roll). The strip follows the drag but bends only a little (a gentle curve, no tighter than about 0.45 m
  radius), goes over the edge of a wall end or doorframe onto its other face and up a wall from the floor, and peels
  back up if you move back toward its start. The tape is opaque (tile grout and wallpaper do not show through), with a
  crepe texture, a cut edge and a soft shadow just past its edges; a bent or wrapped strip is one strip (torn only at
  its two ends). The roll gets thinner as you use it; an empty roll is the bare cardboard core with a torn stub.
- **Desk lamp**: its cord starts wound up in a bundle behind the base. Click an outlet (or an extension cord's box)
  with the lamp in hand to unwind it and plug in (1.8 m of cord), or click the bundle to take the plug in your hand.
  Tap right mouse to wind the cord back up (not while it is plugged in); hold right mouse to reel it in hand over hand
  (it pulls the plug out of its socket and drags it in, then winds it up). Click anywhere else to switch the lamp. Q sets it
  down upright. It lights (a shadowed warm spot) when on and powered. Walking away with it plugged in, the cord goes
  taut with a small tug and holds you back (`CamcorderPlayerController.Leash`); keep walking against it for about half
  a second and the plug comes out of the socket: a pop, the plug flies toward you and the camera jolts.
- **Extension cord**: as found it is coiled; click an outlet with it in hand and the plug goes in while you keep the
  coil in hand: the cord pays out of it as you walk away (`Cord.Feed`: as much as the way from the outlet down to the
  floor, along it and up to the coil needs, plus 25 cm; at most 5 m/s, so it slides off rather than appearing), lying on
  the floor behind you, and the coil gets thinner. When all 8 m are out the coil is gone and you hold the box (three
  sockets) at its end. Let go of, the box falls from the hand (it used to be set down on the floor in front of you at
  once). Saved with how much is out. Tap right mouse to coil it up again, or hold it to reel the cord in
  (the plug comes out and is dragged back, then it coils). Reeling works from either end: holding the plug, hold right
  mouse and the box (or the desk lamp, for its plug) is dragged in along the cord, then coiled (wound up) and put in your
  hand. Walking further than a cord reaches holds you back, then pops its plug out, as for the lamp. Pick up a plug to
  pull it out; with a plug in hand, click a socket to push it in. Up to 3 cords in a row. Q with a plug in hand just
  lets it fall. A dragged end follows the cord (round a corner rather than into it).
- **Plugging in**: holding a plug, the lamp or an extension cord, the socket you would plug into gets camcorder focus
  brackets once you aim near it (within about 10 degrees, 2.2 m, in sight, and within the cord's reach). Clicking plugs
  into that socket, so you do not have to hit it exactly.
- **Grabbing a cord**: look at a cord (the lamp's or an extension cord's) and click to take its plug, wherever the cord
  is (the brackets sit on the cord). It is pulled out of its socket if it was in one.
- **Rubber chicken**: like a real one. Hold the left mouse button to squeeze it: it squashes and wheezes as it takes
  in air. Release and it squawks; the longer the squeeze (up to about half a second), the longer and louder the squawk.
  It squeaks when thrown and when it lands hard.
- **Sledgehammer**: a short-handled one (a 4 lb club hammer, 43 cm). Carried choked up near the head; hold left mouse to
  wind up (the hand slides to the end of the handle and lifts it over the shoulder, up to 0.6 s for the hardest and widest
  blow; you step in if the wall is a little out of reach), release to swing at what the view is on. It breaks walls
  (boards, wood studs, baseboards; steel studs ring and hold), knocks furniture, shoves doors; thrown hard into a wall it
  breaks that too. Found in maintenance closets and sealed storage, or from the L menu. How walls break:
  BreakingWalls.md (`Items/Sledgehammer.cs`, model `Items/SledgehammerModel.cs`).
- Items lying in the world have a soft contact shadow under them (`Items/ContactShadow.cs`) instead of shadow-map
  shadows, which flickered on small moving things.
- Cords no longer pass through floors and walls: each particle sweeps from where it last was and slides along what it
  hits. A span that cuts through a corner puts its nearer particle on the corner's outside edge (where the two faces it
  went in and out of meet), so a cord wraps round door frames and wall ends instead of through them.
- **Cords and broken walls** (2026-10-27, `Destruction/WallSlabs.cs`): a cord meets a broken wall as a slab the wall's
  whole thickness, solid wherever a board, a stud, a track or a post is left and open where the wall is open right
  through (not its thin board colliders: a span straddled a board, was pushed back out of the face it went in by
  until it slipped through, and was then loose in the wall). A span that cuts through solid wall is moved out of it by
  as little as will do (`Cord.ThroughWall`): over a sill (open just above, as along the bottom of a hole) it is lifted
  straight up to the edge, as a floor holds it; otherwise out of the nearer face, or (its ends either side) to the edge
  of the nearest opening, whichever leaves the whole span clear. A particle inside solid wall (the wall broke round it,
  an older save) goes back out through the nearer face; a cord resting on a wall that is struck again wakes and can fall.
- **Still and smooth** (2026-10-27): the cord used to jiggle where it lay over the drywall: the particle on the edge was
  put back on it at the end of each step, slid off with the next, and was put back (pushed sideways or up at a slant,
  against gravity at an angle, and once to the middle of the opening's 2 cm map cells, 3 cm up). Now edges are put right
  inside the solver too (`Wraps`, every few rounds, for spans that lay over an edge the step before), so the cord's
  length and its edges agree before the step ends, a corrected particle loses its speed, and a cord whose particles all
  stay within 1.5 cm for 0.8 s with its ends still goes to sleep (`Settle`). Laid through a hole, it settles in about
  2.5 s and lies still (measured: was 4 to 7 mm of movement every frame, forever). Particles are 7 cm apart (were 12),
  no joint folds more than about 50 degrees (`Bend`), and it is drawn through them as a curve, three points a span, eight
  sides round, so it lies in curves instead of 12 cm kinks. Cost, 8 m of cord, editor: about 0.3 ms a step lying still,
  0.5 ms with an end in the hand (particles resting on the floor skip the physics), 1 ms falling; two steps a frame.
- Cords lie through rubble (it is drawn, not solid).
- **Throwing through a noclip door**: a thrown (or falling) item that reaches the wall inside a noclip door's opening
  comes out of the other door, moving the same way (`NoclipTravel.ItemStep`). Items with a cord out stay on this side.
- **Light switches**: click one (focus brackets show it) and the hand reaches out and flicks the toggle with a finger:
  down puts out the ceiling lights of the room it is in, up switches them on again (the tubes strike with a flicker).
  "The room" is that room's part of the chunk the switch is in (`Generation/LightZones.cs`; each room's lit lenses are
  their own mesh, `RoomLens`). Every switch of the room shows the same position. Saved with the world.
- **Doors**: some square-topped doorways in the Backrooms (not the Poolrooms) have an old six-panel wooden door with a
  lever handle (`Generation/OfficeChunkBuilder.Doors.cs`, `Interaction/Door.cs`, model `Tools/Blender/build_door.py`).
  **Hold left mouse** on one: the hand takes the lever on your side (stepping in if needed) and pushes it down, which
  unlatches it. Keep holding and walk: the hand stays in front of you and the door goes with it, so a short step
  forward or back opens it a crack (to peek) and walking on swings it wide; let go and it keeps swinging (a quick
  push sends it wide open, it bumps the stop; pulled shut hard it bangs and latches). It only opens to one side (push
  from one side, pull from the other), never swings through you, and walking into an unlatched door nudges it.
  Shut doors are latched until you use the lever. Doors remember where they were left (saved with the world). About
  4 in 10 such doorways in the Hallways, 3 in 10 in the Offices.
  **Left click** (2026-10-28) is the quick way through, the same as right click below: a shut door is opened (pushed
  wide from the side it opens away from, tugged open from its swing side, the body stepping back and to its latch side,
  or straight back if a wall is right there); an open door (30 degrees or more) is swung shut and latches; one ajar is
  opened on. Held past 0.22 s before the hand is on it, the click becomes the slow way (hold and walk). The door goes once
  the palm is on it, or after a third of a second with the palm within 4 cm.
  **Right click** on a door within reach is the quick way through (`PropGrabber.QuickDoor.cs`, `Door.Shove`): from the
  side it opens away from, the hand presses the lever (a latched door) or slaps the door itself and shoves it, and it
  swings wide (about 80 degrees); from its swing side the hand takes the lever and tugs it part way open (about 30
  degrees, stopping at you); an open door is shoved the way you face (wider, against its stop, or toward shut). A latched
  door is unlatched on the way. The slow way (hold left mouse and walk) is unchanged.
  A shut door (under 2.5 degrees open) is a wall to light and sound (`Generation/DoorEdges.cs`): the light grid encodes
  its edge as a wall, so neither the bounce field nor real lights pass it; the building audio's paths cannot go through
  it and the straight line through it costs about -9 dB and muffles (a wall: -18 dB); Steam Audio gets a wooden slab in
  the doorway. Light from a lit room shows as a bright line under the door on the dark side (`Resources/Shaders/
  LightSpill.shader`, strength from `FixtureLightManager.RoomLight`); once the door opens the light comes through the
  gap for real. An unlatched door is never quite still: air moves it a degree or two now and then (a gust a little
  more, with a creak). It never latches itself.
- **Climbing up** (2026-10-28): **Space** at a ledge 0.45-1.35 m above the feet, close ahead, with room on top, pulls you
  up onto it (up the face, then over, about half a second; `CamcorderPlayerController.TryMantle`). A pit room's shaft is
  two pulls (the block in its corner first). Lower, Space is a jump. A drop of about a metre or more jolts the camcorder.
- **Leaning**: hold **Z** or **X** to lean the head out to the left or right (0.32 m, the view rolls 9 degrees, the
  torso tilts) to look round a corner. It stops short of walls; not while running or crawling.
- **Steadying hand**: walking past a door jamb or the end of a wall on the hand's side, the free hand sometimes reaches
  out and rests on it for a moment (palm flat, a soft pat), then lets go (`PropGrabber.Touch.cs`).
- **Fingers**: every finger and the thumb close joint by joint until they touch what the hand holds (the held item's
  own shape, the furniture's colliders, a door's lever, the wall or rail under the hand), so they wrap a can, lie flat on
  a wall and pinch a stick of chalk (`FirstPersonBody`, "Fingers"; `PropGrabber.ContactDistance`,
  `WorldItem.ContactDistance`). Nothing is taken until the palm is on it (see "Hands" below).
- **Weight**: a heavy item in the hand (the lamp, a coiled cord) slows your steps and turns a little and lags the view
  more. Picking something up off the floor, you bend down for it first.

## Item tuning (F2)
F2 with an item in your hand, or looking at one (within 4 m), opens a small panel of that kind of item's settings on the
right of the screen (`Items/ItemTuningMenu.cs`, the F5 menu's look). Sliders for numbers (or type an exact value in the
box beside each: past the slider's ends widens it), boxes for on/off; under each, what it does and its built-in value.
Every change applies at once to every item of that kind already out, and to every one made from then on, in every world;
RESET puts the kind back to its built-in values. Escape or F2 closes it.

- **What is listed** (`Items/ItemTuning.cs`): the item class's own public fields of type float, int or bool, found by
  reflection, plus its weight (the Rigidbody's mass). A new item shows up with no extra code; give a field [Range] for its
  slider's range (otherwise 0 to three times its built-in value) and [Tooltip] for its explanation, or [NotTuned] to
  leave it out (live state such as the paint or tape left, a lamp's switch).
- **Where it is kept**: per item kind (its class name), applied as each item wakes (`WorldItem.Awake`). In the editor it
  is saved into the project, `Assets/Backrooms/Resources/ItemTuning.json` (a moment after the last change, and on
  closing), so it is part of the game and builds ship with it; in a build, changes are saved on that machine
  (persistentDataPath/ItemTuning.json) over the shipped file. Not part of a world's save.
- The sledgehammer's: wind-up time, break radius (full blow) and tap radius (quick click), power, crack spread, swing
  time, reach, weight (BreakingWalls.md, Tuning).

## Pieces (`Scripts/Runtime/Interaction`)
| File | What it does |
| --- | --- |
| `MovableSpec.cs` | Plain data for one movable prop (kind, models, size), made by the chunk builder; `MovableShapes` turns it into collider boxes, grab edges and mass on the main thread. |
| `MovableProp.cs` | The prop: frozen (kinematic) until disturbed, simulates until it settles, then freezes and saves its pose. Wakes frozen props it touches or hits. Shadows come from proxy boxes. |
| `MovablePropManager.cs` | The list of moved props (the "edits" on top of the generated world). A moved prop no longer belongs to its chunk; it is stored when the chunk it stands in unloads and rebuilt when that chunk loads. Records last for the session and reset with a new seed. |
| `PropGrabber.cs` | The hand: grab, reach, stoop, hold (a strength-limited spring at the grip point), controls, shove. Exposes the hand pose for the body. Where the hand goes is worked out on the model (`MovableProp.PlaceHand`): the surface round the grip is sampled with rays onto the mesh's triangles, every way the fingers could point is tried, and the palm lies on the highest point under it (nothing inside padding), fingers hooking over an edge where the surface drops away, avoiding a rise under the fingers and fingers pointing back at the shoulder. |
| `PropGrabber.Door.cs`, `Door.cs` | Doors: taking the lever, unlatching, the door following the hand, momentum, latching, the player blocking its swing, `DoorStates` (saved angles). |
| `PropGrabber.QuickDoor.cs` | Right click on a door: the quick shove or tug (`Door.Shove`). |
| `BodyPresence.cs` | The body as camera and mic feel it: exertion and breathing (deeper and faster after running, heard close to the mic; shallow when crouched still), clothes rustling, a shoulder brushing a wall (a rub and a small knock to the camera). Presentation only. |
| `PropGrabber.Touch.cs` | The steadying hand on door jambs and wall ends. |
| `LightSwitch.cs` | A wall switch's toggle (its own object, the plate is baked into the chunk) and what a flick does (`LightZones`). |
| `FirstPersonBody.cs` | The body. Arm: two-bone IK with a skeleton's limits: the elbow's swing round the shoulder-wrist line is chosen (searched every frame, smoothed) so the forearm can turn and the wrist bend to the hand's angle without going past a forearm's turn (95 degrees either way from thumb-up) or a wrist's bend (70 toward the palm, 60 back, 28 sideways), keeping the elbow down and out; anything still past a limit is not done (the palm stays on its point); a long reach brings the shoulder forward a little. Legs: feet planted where they land and stepping (lifted, swung, landing where the body's motion says) once left behind, one at a time, so starting, stopping, turning on the spot and changing direction come out as natural steps instead of a snap; hips bob over each step and sway over the standing foot; hips drop with the eye (crouching, stooping, getting up and down bend the knees and lean the torso); hidden while the eye is too low for a body to be standing under it. |
| `PropGrabber.Items.cs` | Holding items: pick up, carry low on the right of the view (raised while in use), use, drop. Places the item after the camera has moved, and the hand on its grip. |
| `Items/WorldItem.cs` | Base of a held item: light physics lying in the world (frozen while the ground under it is unloaded), its hold pose and grip, `Hold(use, aimAt, dt)` every frame while held. |
| `Items/SprayCan.cs` | Red spray paint (model: `Tools/Blender/build_items.py`, `Resources/Items/SprayCan.fbx`). Hold to spray where you look: ~300 traced droplets a second in a 4.5 degree cone, reaching 1.8 m; mist particles; hiss; the ball rattles when picked up. `SprayCan.Lifespan` = 45 s of spraying, sputters for the last 10 %, then only hisses. |
| `Items/SprayPaint.cs` | The paint: soft speckled dots in batched meshes (Backrooms/SprayPaint multiplies the colour onto the surface, so it is lit like the wall; red channel kept at 1 so coats never go black). Wet paint per 2.5 cm cell: past about 2.4 coats on a wall it runs; more paint on the spot lengthens the run (up to 0.55 m); runs stop where the wall ends. Paint goes on the building, not on movable props or items, and lasts the session. |
| `Items/Marks.cs`, `Resources/Shaders/Marks.shader` | Chalk lines and tape strips: flat strips on surfaces (tape as a path of joined pieces, `Marks.Path`), batched (1024 a mesh), saved with the world (save version 2: four corners and the place along its strip per quad). The shader lights each mark by the surface under it (luminance of the camera's opaque copy), so marks match their wall in every zone and under any lamp. `Marks.Strip` also builds chunk-owned marks (a noclip door's tape outline, a camp's tally). |
| `Items/Chalk.cs`, `Items/TapeRoll.cs`, `Items/RubberChicken.cs` | The marking items and the chicken (its squeeze is the model's "Squeeze" blend shape). |
| `Items/DeskLamp.cs`, `Items/CordBox.cs`, `Items/Plug.cs`, `Power/Cord.cs`, `Power/Sockets.cs` | Power: wall outlets register their two sockets per chunk (`ChunkOutlets`), plugs and boxes, a verlet cord with world collisions and a length limit (taut: a plugged end pops out, a free end is dragged). A lamp's bulb glows with the fixtures' existing lit-lens material (no new shader variant). |
| `Items/ItemModels.cs`, `Items/ItemFactory.cs` | Item models from Resources/Items and their materials by slot name (each with a fine normal map by what it is made of: `Resources/Textures/Generated/Detail_*_Normal`); making an item from its id (saves, loot). |
| `Items/ContactShadow.cs`, `Resources/Shaders/ContactShadow.shader` | A soft dark blob under a lying item (a raycast down, sized from the item's footprint, fading with height; hidden while held). |
| `Items/CordBundleHandle.cs` | The desk lamp's wound-up cord (`Resources/Items/CordBundle.fbx`): clicking it lets the cord out with the plug in your hand. |
| `Items/ItemCatalog.cs`, `Items/ItemsMenu.cs` | The developer menu (L): a grid with a rendered picture of every item and furniture piece (each sofa model and fabric, armchairs, office chair, table, three boxes, floor lamp, wall clock). Click to spawn in front of you (the clock on the wall you look at); shift-click keeps the menu open. Spawned furniture joins the moved-props list, so it stays put through chunk unloads. |

Movable kinds: sofa, armchair, office chair (casters, low friction), conference table, cardboard box
(new in world version 5: a few against walls in offices). Floor lamps, clocks and racks stay fixed.

Tuning: `PropGrabber` (reach, maxPush, maxLift, stiffness, mouseToMetres, shove), masses and frictions in
`MovableShapes.Build`, grab edges per kind there too.

## Body model
`Tools/Blender/make_player_rig.py` builds the skinned arm (a modelled hand with tapered fingers, knuckles, creases,
nails, palm pads; sleeve; no watch or ring since 2026-10-26; about 11k triangles) and body (shirt with buttons, belt and buckle, trousers,
shoes; about 18k triangles) as smooth surfaces on an armature, weighted by calculation, and writes
`Resources/Player/PlayerArmRight.fbx`, `PlayerArm.fbx` (`--left`, a mirror) and `PlayerBody.fbx`:

```
F:\Blender\blender.exe -b --factory-startup --python Tools/Blender/make_player_rig.py -- Assets/Backrooms/Resources/Player
```

The bones have the old segment names and joint positions (UpperArm, Forearm, Hand, Index1..3, Middle1..3,
Ring1..3, Pinky1..3, Thumb1..3; WatchCase is now an empty on the Forearm bone at the back of the wrist, kept because
the code measures the back of the forearm from it; Hips, Torso, ThighL/R, ShinL/R, FootL/R),
so `FirstPersonBody`'s IK and finger curl drive them unchanged. Skinned renderers update their bounds with the pose
(`updateWhenOffscreen`). The thumb folds across the palm on its own axis. The older rigid-segment builder
`make_player.py` is kept for reference.

Since 2026-10-27 the body also has a head and neck (`Neck`, `Head` bones; a separate skinned `HeadMesh`: skin with a
face, ears and short hair, eyes at about 1.62 m), drawn into the shadows only (the camera is inside it), and the left
arm (`PlayerArm.fbx`) holds the camcorder to the eye, also shadow-only, with a simple camcorder of boxes. Rebuild the body
alone with `--no-arm`.

## The MakeHuman body (2026-10-27)

The player's body is now a MakeHuman character (MPFB, the MakeHuman add-on for Blender; everything used is CC0), with the
procedural one above kept as the fallback (`FirstPersonBody.useMakeHumanBody`, or the files missing).

- **Source**: `Tools/Blender/mpfb/PlayerMPFB.blend`, the character as tuned by hand (a man of 25, 1.76 m, slim; a
  striped shirt and jeans, worn sneakers, short hair). `Tools/Blender/make_player_mpfb.py` built the first version and
  will not build over the file (`--force` to start again).
- **Export** (after any change in Blender; it reads the saved file and never changes it):
  ```
  F:\Blender\blender.exe -b Tools/Blender/mpfb/PlayerMPFB.blend --python Tools/Blender/export_player_mpfb.py -- "F:/Users/kadej/My project/Assets/Backrooms/Resources/Player/MPFB"
  ```
  It bakes MPFB's masks and slider targets into a copy, splits every mesh by the bones that move it (`.Head`: head and
  neck, hair, eyes, brows, lashes; `.ArmL` / `.ArmR`: each arm and its sleeve; `.Torso` (since 2026-10-28): hips, spine
  and collarbones, so the shirt and the top of the jeans; the rest: legs and feet), and writes `PlayerMPFB.fbx`
  and the textures (colour, and the clothes' normal maps) to `Textures/`. Unity imports it as Generic, no avatar.
- **Changing clothes or the look**: open the .blend, MPFB tab, *Apply assets* (clothes, shoes, hair, skin: one click
  each, fitted to the body and skeleton) or *Model* (sliders), save, run the export. No code changes: the game drives
  bones, not meshes, and finds each material's texture by name (`Textures/<asset>_diffuse.png`, `_normal.png`).
- **Rig** (`FirstPersonBody.Rig.cs`): MPFB's 53-bone game-engine skeleton. Pelvis, spine_01-03 (the torso's bend,
  twist and lean spread 40/35/25 %), neck_01, head, thigh/calf/foot (ball: the toes), clavicle/upperarm/lowerarm/hand
  and three bones a finger. The arm code is the same as for the separate arm (`ArmRig`: measured from the rest pose,
  the back of the hand from across the knuckles); the body's shoulder is carried toward a reach by turning the
  collarbone (at most 28 degrees). The working arm is the body's own; the other holds the camcorder to the eye. While
  crawling (the body is hidden) the separate arm model takes a held thing as before.
- **Drawn**: the legs and shoes; the working arm only while it reaches, holds, points or touches (since 2026-10-28,
  `firstPersonTrim`); the head parts, the trunk (`.Torso`) and the camcorder arm cast shadows only (the shadow is the
  whole body). Materials are Backrooms/Lit with the textures (normal maps on the clothes: an existing shader variant).

Fitting the legs to it: MakeHuman's legs are nearly straight at rest (hips 0.95 m, legs 0.87 m), so they run out of
reach where the old body did not. A planted foot the leg cannot reach now lifts its heel, turning about the ball of the
foot (the toes stay put), solved exactly and eased down; a step leaves the floor with that tilt. A trailing foot closing
up behind a side-step stops 15 cm from the planted one (it came within 1 cm), side-steps are quicker and shorter and do
not roll the heel. On stairs the feet alternate, one tread past the other foot (as people climb them; one tread past
itself made both feet stop on every tread, half the walking speed, so the feet fell behind and the legs trailed: the
"Octodad" look), stair steps are quicker (at most 0.27 s) and may start as the other foot lands, stairs are walked at
0.8 of the flat's speed (`CamcorderPlayerController.stairSpeed`, `OnStairs`), the landing height always arrives by the
end of a step (a short step down could land in the air), and no stride is longer than 1.45 m walking.

Checked in play (World 3, erased after): outside views (filming pose, textures), first person looking down (shirt and
shoe tips, nothing in the way), HandShots items (spray can, tape, desk lamp looked at: fingers round them, the sleeve
with the arm), the right hand flat on a wall when leaning (palm 1-7 cm from its point). Movement test (look 25 degrees
each way, turn 90, walk forward and back, strafe both ways, stop): no foot moved while looking, toes of planted feet
moved at most 0.26 m/s (reversing; 0.03 turning), feet at least 0.10 m apart turning and 0.15 m strafing. Test
staircase (4 up, landing, 4 down): every tread, feet alternating, every landing mid-tread (11-14 cm from the edges) and
at the tread's height, the feet within 0.3 m of the body, the legs never hanging.

## Body and movement (2026-10-27)

`Interaction/FirstPersonBody.Full.cs`, `Motion/Springs.cs`, `CamcorderPlayerController` (Momentum).

- **Springs** (`Springs.Damper`, critically damped, set by half-life; `Springs.Oscillator`, a damped pendulum): changes
  of pose keep their velocity (inertialization), so a crouch, a landing, a turn or a stop blends like a body.
- **Pose stack**: each limb is taken by the highest layer that wants it. Body (the legs' stepping; the right arm hanging
  and swinging opposite its leg as a pendulum), IK (the free right hand laid flat on a wall or round a rail: when leaning
  round a corner, walking slowly right beside a wall now and then, or after standing close to one), Override (the hand on
  what it holds, PropGrabber). The arm is always there now (the shadow has it); in first person it hangs out of view.
- **Feet**: planted and stepping as before, now: never in each other (a swinging foot goes round the standing one, at
  least 12 cm apart centre to centre; two crowded feet after a turn make one step out), landings put on a stair tread
  whole (`OnTread`: heel and toe clear of its edges), the hips coming down so both legs reach (pelvis offset), a gait for
  the direction (shorter steps sideways and backward, a wider stance sideways, toes first going backward).
- **Lower body heading** (since 2026-10-28): the hips and feet have their own heading. Walking, it follows the view a
  moment behind. Standing, it stays put while you look around (the torso takes three quarters of the turn back, the neck
  and head the rest) until the view is 40 degrees round; then it turns to the view and the feet step round with it
  (quicker steps, 0.28 s). A planted foot never slides: only a foot in the air is moved to keep the feet apart, and that
  push eases in and out over its step (it used to switch on at lift-off and off at landing, a jump of the foot in one
  frame: up to 15 m/s strafing, now at most 5, a normal swing).
- **Steps and stairs** (since 2026-10-28): the body's height is eased over steps (the controller drops or climbs a whole
  step in a frame); the legs only let go of the floor and hang after 0.14 s off it or when rising (a jump), not each
  time the controller leaves a step's edge. Stepping down, the foot carries over the edge first and then lowers, landing
  toes first; stepping up, it rises first and then goes over the nosing; a small lift either way (it used to add the
  whole step's height on top, throwing the knee up). On stairs a foot goes at most one tread past the other foot
  (`OneTread`; since the MakeHuman body the feet alternate, see above).
- **Momentum**: speed follows the keys through a damper (half-lives: walk 0.05 s, run 0.08 s, stop or reverse 0.07 s,
  air 0.45 s): measured 95 % of walking speed in 0.17 s and a 12 cm slide over 0.32 s when you let go. The view rolls
  about 1 degree into a turn (`turnRoll`) and the torso leans with it. Coyote time 0.12 s, jump buffer 0.15 s (held, it
  charges from the landing; let go, it is a hop).
- **Knocks** (`FirstPersonBody.Bump`): walking into something at over 1.1 m/s (`CamcorderPlayerController.Bumped`) or
  a hard landing shoves the struck part (torso high, hips and a catching step low) through pendulum springs that pull
  it back, with a jolt of the view and a small slide. Never a fall (no ragdoll, by request).
- **Carried things** swing on the arm as pendulums (`PropGrabber`, by weight: lighter faster and stiffer).

Checked in play: the feet stayed at least 12 cm apart strafing, turning on the spot and walking backward (16 steps); on a
test staircase every step up landed mid-tread (13-17 cm from either edge); walking into a wall shoved the torso about
4 degrees, back within about 0.4 s. Outside views (shadow-only parts shown for the picture): standing with the camcorder
to the eye, the right arm hanging.

Checked again (2026-10-28, play): looking side to side (25 degrees each way) left the hips and feet still; a 90 degree
turn on the spot took two steps; strafing back and forth with the view swaying, no foot moved more than 5 m/s in a frame;
down a test staircase (three 17 cm steps) the controller stayed grounded, the legs never went to hanging, and the feet
landed on 0.68, 0.34, 0.51, 0.34, 0.17, 0.17, 0 (the first step off the top still skipped a tread).

## Gait and the first-person view (2026-10-28)

Research (what studios use, and what fits a body with no animation clips): motion matching (Ubisoft For Honor, The Last
of Us Part II, Unreal 5 Pose Search) needs a mocap database; what carries over is driving the gait from the planned
motion with timing from data. Inertialization (Bollo, Gears of War, GDC 2018; Holden's springs and dead blending) is
already the basis (`Springs.Damper`). Procedural IK rigs (Bereznyak, Ubisoft, GDC 2016; Rosen, Overgrowth, GDC 2014),
foot locking, soft IK and first-person bodies drawn split (the parts at the camera in the shadows only) were applied
with published adult gait data: cadence and step length by speed, stance and swing shares, Perry's three rockers (heel,
ankle, forefoot), knee angles, pelvis rotation and obliquity, centre-of-mass motion, arm swing.

- **Gait numbers** (`Motion/Gait.cs`, tested in `GaitTests`): steps a second from speed (walking 1.25 + 0.47 v, running
  2.35 + 0.11 v, blended over 1.9-3.2 m/s); a step is the speed over that; a foot is down 63 % of the cycle walking
  slowly, 58 % fast, 38 % running. Side-steps are quicker (x2.1) and shorter, backward steps x1.25, crouched x1.15.
- **Even steps**: walking forward or back, a foot leaves no sooner than 0.95 of a step's time after the other one left
  (0.9 running) unless it is far behind. Stepping only when left behind made a limp: steps of 0.43 and 0.60 s.
- **Foot roll**: the heel strikes toes up (14 degrees walking, 8 running) and rolls flat about the back of the heel
  (`AnkleAt` turns about the heel for toes-up, measured on the shoe: 7 cm behind the ankle); the heel rises about the
  ball from a little behind the hip to toe-off (`Gait.HeelRise`, 38 degrees walking, 50 running); the toes (MakeHuman's
  ball bone) bend so they stay flat on the floor; the swing leaves at the toe-off tilt, levels, and lifts highest early
  (`Gait.SwingLift`, `SwingPitch`). A planted foot's tilt goes through a fast spring (it switched in a frame at landing,
  starting and stopping). A reach-limited heel lift (a foot left behind) is only for a foot behind the hip, at most
  500 degrees a second (1200 running).
- **Shoes on the floor**: MakeHuman's shoe soles reach below the model's ground; the ankle height is now measured on the
  shoe (9 cm over the sole). They stood 2-3 cm in the floor.
- **Knees**: legs on the floor are kept about 9 degrees bent standing (it was 20: a slight squat), 16 walking, 30
  running, by the hips coming down; soft IK (`Gait.SoftReach`, after Andy Nicholas) eases the last 3 % before straight
  for a swinging leg (and the camcorder arm), so a knee never snaps straight. The hips come down for the gait's ankle,
  not the heel-lifted one: a lifted heel let the hips stay up, which kept the heel lifted (standing on tiptoe after a
  spawn). A leg early in its swing no longer pulls the hips down (a foot just lifted far behind dropped them 20 cm
  running).
- **Pelvis and thorax**: the pelvis turns with the stride (the leading leg's hip forward, 4 degrees each way walking,
  6 running) and drops on the swing side (obliquity 4-5 degrees); the thorax turns the other way (3-6); the spine takes
  the difference and keeps the shoulders level. The hips sit 1.5 cm lower walking and rise 1 cm as the feet pass; a foot
  lands at 0.88 of half the stance's travel ahead (it was further: the hips dropped 9 cm a step).
- **Arms**: the free arm swings opposite its leg about 40 cm front to back (it was 46), the elbow bending as it comes
  forward; running the arms are held bent. The forearm bone takes 55 % of the hand's turn (`forearmTwistShare`: no
  twist bones, so elbow and wrist share it). The camcorder arm's forearm roll comes from the thumb (well defined) instead
  of the back of the hand, which lay along the forearm and flipped (2400 degrees a second).
- **Fixes**: a swinging foot was reset to its home when its old plant fell 2.5 m behind (running: a foot jumped and a
  step was lost: 72 steps a minute); a toes-up heel strike was flattened to level by the heel-lift check; side-steps land
  flat; stair landings fit the measured shoe (toe clear of the riser going up, heel on the tread going down).
- **First person**: the camcorder is held a little out in front of the eye (`CamcorderPlayerController.holdOut` 9 cm,
  2 cm down; 11 and 4 looking fully down), in the body's frame, pulled in when something is right in front (a sphere
  cast); `HandheldCameraShake` adds the shake on top of `CamcorderHold`. The eye (the pitch pivot) stays the head: reach
  and aim work from it. The camcorder model and the left hand are at the camera. Held items sit half as far out (their
  framing nearly as before, the arm still bent). The trunk is in the shadows only; the working arm is drawn while it
  reaches, holds, points or touches (`SetWorkingArmShown`), hanging only its shadow.

Measured (`Debug/GaitProbe.cs`, World 3, a flat test floor; before -> after): walking at 1.34 m/s 125 -> 117 steps a
minute (data ~113), steps 0.64 -> 0.69 m (data 0.72), step times 0.40-0.64 -> 0.48-0.52 s, double support 17 -> 20 %
(data ~20); running at 3.58 m/s 202 -> 173 steps a minute (data ~165), steps 1.06 -> 1.24 m (data ~1.3), flight 30 %;
slow walk (0.74 m/s) 97 a minute. Walking: shoes into the floor 2.8-4.6 -> 0.5 cm; planted foot pivot slip max 4.3 ->
0.27 m/s (mean 0.12 -> 0.05); biggest one-frame spike of any bone 2040 -> 47 degrees/s; fastest knee 1270 -> 470
degrees/s; knee in swing 54 -> 60 degrees (data ~60), at heel strike 20 -> 16; foot at heel strike level -> 14 degrees
toes up (data 15-20), at toe-off 1-20 -> 38 heel up; toes bend 0 -> 38; pelvis rotation 0 -> 7 and obliquity 0 -> 8
degrees (ranges; data ~9 and ~8), the thorax against the pelvis (correlation -0.99); hips up and down 8.8 -> 4.2 cm
(data 4-5), side to side 2.8 cm. Standing knee 20 -> 9 degrees. Strafing: hips 6.3 cm. Stairs: a shoe at most 5 cm into
a riser. First person (pitch 0 / 45 / 62, the game's limit / 70; share of the frame that is body): standing 0 / 0 /
4.5 / 9.6 % -> 0 / 0 / 0.8 (two shoe tips) / 1.8 %; walking 0 / 0.4 / 5.5 / 10.1 % -> 0 / 0.2 / 1.8 / 2.8 %. Held items:
the hand and sleeve as before in the eye view. Open: at 70 degrees (past the game's limit) the cut top of the jeans can
show; running, a planted foot sometimes slips (mean 0.25 m/s, as before); stairs checked only on the test staircase.

## Hands (2026-10-28)

A reviewer scored the hands holding things 3/10: things held from up to 0.4 m away, fingers inside a spray can and a
rubber chicken, a thumb that never closed, a sledgehammer that left the hand mid-swing, the upper arm rolled 60-70
degrees off its elbow, a wall lean with the fingers 8-11 cm in the wall. What changed:

- **No telekinesis.** The reach is the rig's own (`FirstPersonBody.ArmReach`: upper arm + forearm x 0.96 + wrist to palm,
  about 0.63 m) from the body's real shoulder (`ShoulderNow`; `PropGrabber.ReachShoulder`, `ArmReach`, `HandArrived`).
  Furniture, a door's lever, a quick shove and a light switch are taken only once the solved palm (or fingertip) is
  within 2 cm of its point (`PalmError`); short of it the body steps in and bends lower (`ComeCloser`), held back (by the
  prop, a wall) with it still out of reach the hand comes back after 0.6 s. Holding, the grip is kept within the arm's
  reach; the hand lets go when the palm is 5 cm (a door, for 0.12 s) or 10 cm (furniture, 0.4 s) off it.
- **Stooping.** The body is placed so the shoulder keeps its place under the eye however far the torso bends (measured
  on the rig, `MeasureBend`; a rigid 0.65 m lever put the shoulder 0.5 m behind the eye, the seat of a sofa out of
  reach), and a squat's hips going back no longer carry it. The top of a sofa's back from in front of the seat is 1.2 m
  from the shoulder: refused.
- **Held items go with the hand.** After the arm is solved the item is put on the palm as it ended up (a wrist at its
  limit no longer leaves the item floating off it): the sledgehammer's swing left the hand by up to 186 mm; now 1 mm.
- **Arm.** The upper arm's roll is the elbow's hinge (it was 61-71 degrees off); `ArmSolverTests` puts the rest pose
  through the solver (0.35 degrees). The elbow's swing is chosen every frame with at most 0.2 of a penalty for moving
  (it stuck on a way 20 degrees off the hand's angle).
- **Fingers** close joint by joint from the knuckle out until what lies beyond touches (one curl for the whole finger
  stopped them all at the first touch, tips 1.5 cm off), at a finger's half thickness (8.5 mm), each phalanx and the tip
  checked. A finger already inside straightens back (to 30 degrees past straight at the knuckle: fingers flat on a wall)
  or curls part way, whichever is clear and nearest what is wanted. The thumb opens out (50 degrees) and closes across
  the palm (opposition) and down to its contact. Items say how each finger and the thumb close (`IHandGrip`:
  `HandGrip.cs`). Boxes (walls, furniture) report how deep a point is (`CollidersDistance`; "inside" alone hid a
  finger 3 cm in a wall).
- **Grips.** Spray can and rubber chicken: palm on the surface, fingers round, thumb opposed. Chalk and plug: placed by
  trying the closing fingers against the shape (index pad on top, thumb pad on the side). Desk lamp: on its real lower
  rod. Coil: by its near rim, fingers over and under the strands. Sledgehammer: the handle across the palm on the slant a
  hand holds a handle (26 degrees from square: the wrist could not turn square to it).
- **Touching the world.** The steadying hand turns the palm round the face's normal to the way the arm reaches best
  (`ReachableFingers`) and touches with the edge of the hand where it cannot lie flat; a wall lean and a rail rest the
  palm on the surface (the rail: on top, over its middle) and the fingers close on the wall or round the rail.
- **Quick door.** A shove from 0.8 m steps in first (it was refused); a shoved door swings off the hand.

Measured (World 3; before -> after; reviewer's numbers before): spray can 878 skin vertices inside the can (31.7 mm
deep) -> 90 (6.9 mm), fingers touching (joints 9-13 mm from the surface: a finger's half thickness), palm 2 mm, hand
7 degrees off; rubber chicken 1583 (58.8 mm) -> 48 (4.9 mm), palm 2.4 mm; coil palm -25 mm and fingers through it ->
palm 2 mm, hand 0.6 degrees off, 72 vertices (6.4 mm); plug inside the fist -> index, middle and thumb pads on it (tips
9-13 mm), 4 vertices (2.6 mm); chalk tips 32-69 mm off -> index 14 mm and thumb 21 mm (joints 9-11); desk lamp grip
25 mm from the rod, thumb 35 mm -> palm 2 mm, hand 0 degrees off, thumb tip 13 mm; tape 165 vertices (7 mm) -> 165
(7.2 mm), thumb on it (9 mm); sledgehammer carry hand 33.5 degrees off -> 9, palm -0.7 mm; swing hammer off the hand
186 mm -> 1 mm (IK miss 3 mm); door lever: palm 122 mm short and turning by itself -> turned only with the palm within
2 cm (15 mm worst, 2.3 mean), held mean 12 mm; quick door shoved at 12-16 mm; sofa arm and seat taken with the palm
0 mm off (were 236-371 mm, and the seat from 0.37 m), the back refused; wall lean fingers 76-111 mm in the wall, back of
hand 74 degrees off -> fingers on it (tips 10-15 mm), 6 degrees off, 1080 -> 141 vertices in; jamb 104 degrees off ->
15, palm 7 mm; rail no contact, 435 vertices in -> palm 3 mm, fingers round it, 78; upper arm roll 61-71 -> 0 degrees.
Walking and running with an item the palm stays within 2 mm; crawling (the separate arm) 0 mm; a throw's wind-up 1 mm.
Round 3 (2026-10-28, reviewer 6/10):
- **Touch layer**: one reach for finding and keeping a touch (`TouchReachOf`: 0.95 of upper arm + forearm, kept 4 cm
  past it, `ArmSolverTests.TouchIsKeptWhereItIsFound`); a wall lean found at 0.543 m and dropped past 0.532 m flickered
  eight times a second and never arrived (now steady, touch weight 1.0, standing 0.35-0.65 m from the wall). A box rail is
  closed on by its own shape; a steadying hand that cannot lie flat comes out as far as its straightest fingers need.
- **Plugging in** (`Plug.BeginPlugIn`): the hand carries the plug to the socket, square to it 5 cm out (bending and
  stepping in: `PropGrabber.ReachToward`), pushes it in along the socket's axis over 0.18 s and lets go; out of reach it
  gives up after 3 s. With the lamp in hand the hand takes its plug first (the lamp is put down). It jumped 1.14 m in one
  frame; now in at 1.7 s from 0.95 m off a socket 0.28 m up, at most 69 mm a frame.
- **Reeling**: hand over hand (25 cm along the cord, 1.5 a second, the fingers opening on the way out: `ReelStroke`,
  `GripOpen`). An item taken with the hand already out (the reeled-in lamp) waits where it lies until the palm is on it
  (the hand eases over in 0.3 s, the body bending and stepping in), then comes up with the hand: it flew a metre.
- **Fingers**: drawn in toward the middle finger as they curl (no gaps); a held item's fingers bend back at most 10
  degrees (a lamp's and a tape roll's little finger were bent back 30); the thumb tries turns across the palm and bends
  until it touches (`CloseThumb`); pointing tucks the thumb. The palm's middle, heel and both sides push a held item out.
- **Doors**: the hold keeps the lever within 0.9 of the reach (the elbow bends); a bot's hold presses once (it took the
  lever again mid-pull and re-grabbed an out-of-reach sofa forever); a quick door goes when the palm is within 1.5 cm and
  the hand stays on a pushed lever 0.12 s.
Measured (reviewer's probe `GaitProbe.MeasureR2`): quick door push closest 44 -> 2.8 mm; door hold mean 8.6 mm, 13
frames over 20 mm (was 31), no re-grab, elbow 4-43 degrees; thumb tips chicken 23 -> 11 mm, lamp 13 -> 9, plug 9, coil
12, tape 9, spray can 12-15, chalk 16, sledgehammer 18; lamp little finger -30 -> 44 degrees at the knuckle; rail 777 ->
86 skin vertices inside; walking, running, crawling and a throw's wind-up with an item: palm within 2 mm.
Round 3b: a held item is turned at most 20 degrees onto the solved hand (the hammer's head swept sideways up to 94
mid-swing); the palm push-out also checks the finger bases; the thumb tries 9 turns and bends further, and keeps the
turn it had among near-equals (chalk thumb tip 16 -> 13 mm, spray can 12-15 -> 10.5, chicken 9.5); a pushed lever's
follow-through stops at the end of the arm's reach.
Open after round 3: the sledgehammer's swing is not re-planned (the item may still turn up to ~90 degrees onto the
palm mid-swing) and its carry twist is -104 (limit 95), palm still 10 mm in at the ring finger's base; the chicken's
twist 97; the coil's contact ring is fatter than its strands; the extension cord's coil still plugs in from the hand
without the hand going to the socket; the steadying hand at a jamb was not re-measured (no wall end found in the
test world); tape laying holds the roll with the palm up to 9 cm off its grip while raised (the roll follows the hand).
Round 4 (2026-10-28):
- **Poses the arm can hold** (`FirstPersonBody.ArmStrain`: the solver's strain and how far the hand would miss, for a
  palm point and hand turn). The 20-degree cap on turning a held item onto the hand is gone: it left the hammer's handle
  74 degrees off the palm after a blow. The item goes fully onto the hand again, and items plan poses the arm can reach.
- **Sledgehammer.** The blow is searched over the handle's tip toward the wall and its lean about the blow's line, for the
  least strain at the strike and at the full wind-up, the arm well out (planned a few times a second, afresh on release;
  about half a millisecond). The follow-through turns the whole arm on round the shoulder instead of the hammer about the
  hand. The carry is turned about its handle as the arm best holds it (searched on pick-up, then kept up to date). The
  grip is 18 degrees across the palm about the palm's normal only (26.5 about a tilted axis lifted the handle 6 mm into
  the heel of the hand and put it under the index knuckle). The fingers close on the handle the model draws: oval, with
  its knob (`SledgehammerModel.HandleScale`).
- **Palm skin.** A held item is pushed out of the palm's real skin: the arm mesh baked each frame, its palm, heel and
  finger-base pads (picked once in the rest pose; `FindHandSkin`). Only for what the palm holds (not chalk in the
  fingers). The steadying hand on a wall uses the same skin.
- **Steadying hand.** It turns flat to the wall before it arrives and comes in from 6 cm off it; fingers eased into a
  surface go straight to where they stop. Fingers swept up to 37 mm into a jamb on the way in; now 1 mm.
- **Fingers.** A finger whose base lies on something still curls over its edge, joint by joint (the rail's little finger).
- **Coil.** Its contact is the bundle's real section (an oval 36 by 28 mm, leaning in at the top). Clicking an outlet with
  the coil in hand: the hand takes the plug and carries it in (`Plug.BeginPlugIn`).
- **Tape.** The raised roll stays within the arm's reach from the shoulder; the body bends toward a low spot.
- **Rubber chicken.** Rolled 8 degrees in the view.
Measured (reviewer's probe, World 3; before -> after): hammer swing hand off its planned turn 94 -> 15 degrees at most
(1.4 through the swing), handle off the palm after a blow 74 -> 0, elbow at the blow 62 -> 28-39; hammer carry forearm
twist -104 -> -59, skin in the handle 10.6 -> 9.3 mm (the palm itself under 2 mm; the rest is finger skin); wind-up
little finger 39 -> 22 mm and no handle mesh outside the contact; jamb skin 542 -> 0 vertices over 2 mm, tips 1 mm at
most on the way in, twist 89; rail little finger tip 38 -> 15 mm; tape raised: palm off grip 93 -> 7.5 mm, no frame over
20 mm; coil mesh 109 -> 51 vertices deep in the contact; coil plug-in carried 1.34 m by hand in 1.45 s; chalk thumb
13 -> 10.4 mm; chicken twist 97 -> 95. Walking 0.9 mm, running 4.1 mm, throw wind-up 1.1 mm; wall lean steady.
Open after round 4: the hammer's thumb lies 14 mm off the handle and its index finger barely wraps; finger pads still
sink 6-9 mm into held items (the fingers stop at a joint's 8.5 mm, the pads are thicker); the chicken's twist is at the
95 limit; the tape roll counts more skin in its core (165 -> 149-260 vertices, 7.2 mm deep); a door push, the light
switch and reeling were not re-measured.
Round 5 (2026-10-28, reviewer 7/10):
- **Lamp plug-in.** Clicking an outlet with the lamp in hand set the lamp down where the view met the floor, under the
  outlet. The body stepped onto it, the plug stopped 15 cm short, and the lamp was later launched. Now the lamp is set down
  beside the feet, off the way to the outlet and near enough for its cord (`PropGrabber.SpotBeside`, `SetDownBeside`), and
  the body passes through it until it has stepped away. The plug starts in the hand (`PlaceInHand`), not at the lamp's
  back. A coiled cord is set down the same way (it was tossed toward the wall).
- **Carrying a plug in.** The hand's target is kept within 0.86 of the arm's reach from the shoulder on the way to the
  socket (`Plug.WithinComfort`), so it moves on as the body bends and steps in and the arm stays bent. Once the body can
  get no closer, the arm reaches on out. The push is one eased stroke, and the hand waits up to 0.15 s for the plug to
  arrive before letting go. With no headway for 1 s it gives up and the hand comes back (`Plug.GaveUp`). The body bends
  down to 0.22 m above the socket (0.3 left a low socket out of reach).
- **Finger pads.** Each finger bone's skin is measured once on the arm's mesh (`MeasurePads`): a round about the bone,
  moved toward the palm, that holds the skin (less 1 mm, as skin gives). The bones lie near the back of the finger, so
  the pads reach 12-16 mm toward the palm. Fingers stop on that round, tested at a third, two thirds and the end of each
  bone, the stop found by halving three times. The thumb's turn is refined about the best one (12-degree steps missed a
  stick of chalk).
- **Wrist and forearm.** The arm solver charges extra past 70% of a limit. A held item is turned about the palm (up to 24
  degrees, about the back of the hand and the fingers) to where the arm holds it best (`PropGrabber.EaseForArm`), except
  the sledgehammer, which now also tips its head when it picks its carry. Holding an item, the fingers stay drawn together.
- **Chalk.** A three-finger grip, found by trying 336 grips against the stick: under the index and middle pads, turned 18
  degrees across the hand, the tip 3-4 cm past the fingertips, the thumb beside it (4 mm off).
- **Reeling.** The hand's curl follows the grip opening on the way out (0.45 -> 0.19 -> 0.45 each stroke).
- **Wall lean.** The hand's turn on the wall is picked once per touch by `ReachableFingers`.
Measured (World 3, `GaitProbe.PlugInRun`, `MeasureItems`, `FingerSkin`, `TouchTest`; round 3 -> round 5): lamp plug-in
never -> in at 1.5 s, palm error max 23 mm, elbow never below 51, lamp moved 0 m; cord plug-in palm error 472 -> 15 mm,
elbow min 4 -> 57, final snap 30 -> 8 mm; deepest skin in a held item: hammer 9.4 -> 1.0 mm, chicken 7.5 -> 1.3, lamp 6.8
-> 1.1, spray can 6.3 -> 1.0, finger skin 0.2-2.8 mm off what it holds; rail 169 skin vertices inside -> 0, middle joints
0-18 -> 34-74 degrees; chalk middle finger 25 -> 1.5 mm, index 0.9; wrist to the side: hammer carry 29 -> 12, plug pinch
27 ulnar -> 16 radial, chicken twist 95 -> 45; hammer swing elbow 114 -> 19, hand within 12 degrees of its plan, palm 0.4
mm; walking with the lamp palm 0.0 mm.
Open after round 5: the tape roll's little finger is still 7 mm in its core; the chalk's thumb is 4 mm off and the thumb
on a rail stands out straight; the wall lean's forearm is still turned 93 degrees and the middle of the palm 9-17 mm off
the wall (one edge touches); the plug's last 7-10 mm into a socket is a snap (the wrist cannot square the plug exactly);
the light switch, door pull, jamb and the camcorder hand were not re-measured.
Round 6 (2026-10-28, reviewer 7.5/10):
- **Fingers wrap.** All of a finger's joints now bend together in its own proportions (`WrapClose`). When a bone
  touches, it and the joints before it stop and the joints beyond go on. Joint by joint from the knuckle out, the knuckle
  took all the bend first: fingers lay with the middle joint near straight (lamp 59/45/36, can 42/2/42, hammer 32/29/48).
  Now lamp 47/58/37, can 28/35/22, hammer index 31/39/48. The thumb closes the same way.
- **Real skin.** Each finger bone keeps 12 points of its own skin on the palm's side (`MeasurePads`), carried with the
  bone. Fingers stop on these, less 0.5 mm as skin gives. The old round about the bone stood 4-7 mm proud of the
  thumb's wide pad.
- **Thumb.** In a fist round a handle the thumb closes over the backs of the index and middle fingers (`FingersBack`). It
  also prefers to lie on the handle and to bend about as much as the grip asks: it lay straight along a spray can. A thumb
  that starts inside something tries its other turns first (on the rail it stayed straight and inside).
- **Fingers together.** Holding something, the fingers come all the way in to the middle finger. At 0.85 of their fan
  there were gaps on a can.
- **Bending back.** A held item's fingers and a rail's straighten back at most 5 degrees (10, and 15 on the rail's index).
- **Tape roll.** It is now held with the index, middle and ring fingers through the core, the roll resting on their backs,
  and the thumb outside on its edge. The palm is 3 cm off the roll's side and the little finger curls beside it. Found by
  trying 1,000 grips against the ring. The old grip had the little finger 7 mm and the palm 5.5 mm in the roll.
- **Chalk.** It is held as a pencil is: pinched between the thumb and index pads and resting on the middle finger. It runs
  from the web of the thumb out past the index tip, 35 degrees to the fingers, its tip 4 cm beyond. Found by trying 1,300
  grips. Its contact is now the stick as drawn: the collider sits a radius higher, and fingers on it went 7.5 mm into the
  stick seen.
- **Limits.** The solver's extra cost past 70% of a limit doubled (0.025 to 0.05 a degree). A spray can's wrist went from
  43 to 41 degrees bent back. The steadying hand's turn on a wall also pays for a forearm turned past 70% of its limit:
  twist 93 to 75.
- **Rail.** The hand may turn up to 40 degrees toward the way the body faces, never with the forearm on the rail, and the
  palm is pushed straight up off the rail's top. Fingers that meet nothing stay at a relaxed curl (0.75).
- **Plugs.** A plug the hand lets go short of its socket slides the rest of the way in over 0.1 s (`Plug.SeatTime`). It
  jumped 8-9 mm in one frame.
- **Camcorder hand.** The left fingers close on the camcorder's body from under it; those that meet nothing stay loose.
  They used to make a fist on nothing.
Measured (World 3, `GaitProbe.MeasureItems`, `TouchTest`, `PlugInRun`; round 4 -> round 6), finger skin nearest the
item, negative in: tape little finger -7.2 (114 verts) -> outside it, palm -5.5 -> 0.0, no vertex over 2 mm in; chalk
thumb 4.4 -> -0.8, index -1.0, middle -0.4, stick 35 degrees to the fingers, tip 4 cm past the index; lamp thumb tip
20.7 -> 3.7 mm; hammer thumb tip 29 -> 14 mm (on the fingers, skin -0.4); all held items' fingers and thumbs 0.0 to
-1.7 mm (most were 1-3.5 mm off); wall lean twist 93 -> 75, middle finger 5.1 -> -0.4 mm, palm -0.4; rail thumb straight
and inside -> closed on it (-0.6 mm), index bent back 15 -> 5 degrees; plug seating 9 mm in a frame -> 2 mm a frame over
5 frames; walking with a can, palm 2.0 mm. New tools: `GaitProbe.Settle`, `GripReport`, `TouchTurns`; test hooks
`TapeRoll.TestGrip`, `Chalk.TestHold`, `FirstPersonBody.TestRailTurn`.
Open after round 6: the rail beside the hip still needs the forearm turned 96 degrees with the wrist 26 to the side. Turned
further toward the front, the forearm lay on the rail. Its little finger hangs 8 mm off and some forearm skin is 4 mm in.
The hammer's follow-through is not planned against the limits. Door pull, quick door, jamb, chicken squeeze, hammer
wind-up and tape laying were not re-measured, and the camcorder hand was not looked at (its arm draws only a shadow).

Round 7 (2026-10-28, reviewer 7.5/10):
- **Doors open.** Holding a lever and walking into a door did nothing: the hand stayed where it took the lever and the
  body stopped at the shut door. Now walking the way the door opens pushes it on ahead of the hand, and the hand lets go
  when the lever leaves its reach; the door swings on and the body pushes it wider. A quick pull swung the door 6 degrees
  into the player, the body's last step in pushed it shut and it latched. Now the body steps back and to the latch side as
  it pulls, a door swinging open into you rests against you, and a door just tugged is not pushed shut for 0.5 s. If the
  hand cannot get to a lever, the door is shoved open after 1.1-1.6 s instead of nothing (`GaitProbe.DoorRun`).
- **Reaching for a door.** The hand goes out with the elbow bent 25-35 degrees while the body steps in (`BentReach`, 0.93
  of the reach), and onto the lever once it is that near. Pressed against the door, the body leans in and the arm may
  straighten to 0.98. The hand takes a lever turned and on a slant as the arm holds it best (`LeverTurn`).
- **Fingers set by the item** (`IHandPose`). Closing until the first touch cannot make a pencil grip or a pad pinch, so an
  item can set some fingers' joints. A posed finger is eased back toward where it closed until it is no more than 1.5 mm
  into the item (`PoseClear`). Chalk: a tripod, index and middle bent 45 and 52 at the middle joint, ring and little at 40,
  the stick crossing the index's middle bone at 42 degrees in the palm's plane, thumb, index and middle 0-0.7 mm off it
  (fitted to the posed fingers' skin). Plug: a pad pinch, index and middle 48 and 52, the thumb's end joint 25; going into
  a socket the hand turns to the old push grip over 0.15 s (pinched, it could not square the plug and never went in).
  Tape roll: the fingers in the core hooked 9-16 degrees (25 went 7 mm into the roll). Sledgehammer: the thumb keeps the
  carry's wrap in the wind-up and swing.
- **Rail.** The hand may turn up to 60 degrees along the rail, liked best at about 52, and pays steeply past 75 degrees of
  forearm turn. The hand pushed out of a rail or wall runs twice.
- **Touch.** A lean never picks a door's leaf or a loose prop (`FirstPersonBody.Swings`), and lets go if what it rests on
  moves more than a centimetre under the hand.
- **Reeling.** The cord comes in as far as the hand pulls it (30 cm a pull, 1.7 a second; it came in 1.2 m a stroke). The
  hand keeps hold of the coil (curl 0.9 throughout), rolls 18 degrees about the fingers and tips with the stroke.
- **Hammer.** Wound up and following through, the hammer turns up to 16 degrees about the back of the hand as the wrist
  holds it best, back to none by the blow.
- **Desk lamp.** The hinge's knob (a drum 13.5 mm round) is in the contact; the fingers may wrap past a fist's curl.
- **Chicken squeeze.** The rubber gives 4.5 mm under the fingers and the fist closes further.
- **Tape laying.** The strip is laid no faster than 1.2 m/s and no further than the arm holds the roll.
Measured (World 3; reviewer's round 5 -> round 7): door push held while walking 0 -> 55 degrees, quick pull 0 -> 27, hold
pull opens fully (86); reach elbow 4-5 -> 28-52; rail twist 96 -> 76, ulnar 26 -> 19; chalk index and middle middle joints
17-21 -> 45/52, ring and little 59-77 -> 40, stick to the index's middle bone 16 -> 42 degrees in the palm's plane; plug
pinch middle joints 19-27 -> 48/52, thumb (3,9,11) -> (5,15,25), wrist bent 21 -> 3; hammer wind-up thumb pad 16 -> 1 mm,
follow-through ulnar 22-27 -> 12-22; reel cord per stroke 0.75 m -> 0.30 m for a 0.28 m pull, curl 0.37-0.9 -> 0.9; tape
middle joints 1-6 -> 8-16; squeeze fingers +3-4 degrees a joint. Lamp and cord plug-in still in at 1.5 s.
Open after round 7: the chicken's thumb still lands 6-15 mm off from run to run; the rail's thumb stays straight and its
index lies flat, its palm 4 mm in; the lamp's thumb is 11 mm off; the reel's wrist still swings -46 to 36; a cord plug's
carry has a 3-frame palm error of 35 mm when it leaves the coil; the quick pull opens only 27 degrees; tape laying, the
camcorder hand and a lean onto a door leaf were not re-measured in play.

Round 8: pickups by hand (2026-10-28, `PropGrabber.Pickup.cs`):
- **Nothing flies.** A clicked item is not touched until the palm is on its grip. It used to fly to the hand: at once from
  further than the arm and 0.6 m (anything picked up 1.3-2.1 m away), or after waiting 0.9 s for a hand that was not there.
- **The hand goes out first.** It reaches with the elbow bent (0.88 of the reach) while the body bends and steps in, as it
  does for a plug. The step stops once the arm can reach from where the body will be (it used to walk on until the
  shoulder was over a can on the floor). Then the arm goes on out.
- **Lean.** With no step left (against a desk, a shelf) the torso leans out up to 26 degrees (`ReachLean`). The hips and the
  view stay; the shoulder comes about 7 cm nearer.
- **Hand turned to reach.** The hand may tilt about the palm, roll and pitch a little as the arm reaches best
  (`ReachTurn`). A can on the floor held square wants the wrist 10 cm off the floor. The turn is taken back off as the
  item comes up, about the palm, so the palm stays on the grip.
- **Closing, then up.** The hand is open going out. On the grip the fingers close, then the item comes up with the hand at
  a hand's pace (0.4-0.95 s). It is eased onto the palm over its first frames (`ItemOnPalm`).
- **Only what the body can reach is offered.** The focus marks and the click use `BodyReaches`: the real shoulder as high as
  it stands, as low as the body stoops (the shoulder stops at 0.65 m), a step of up to 0.55 m where the body has room, and
  the lean. The arm's way must be clear (not through a shelf board). Further, nothing is marked; the player walks closer.
- **Blocked.** No nearer for a second (or 5 s in all): the hand comes back and the item stays where it was. An item taken
  by code (a reeled-in lamp, a loaded save) waits in the hand's place the same way and is left where it lay if the hand
  cannot get there. Tests and tools put an item straight in the hand with `PutInHand`.
- **Cord plug-in.** The plug is now made in the palm as the hand will hold it (`PlaceInHand`). The carry to the socket waits
  until the hand has it. Before, the body bent for the socket while the plug still waited for the hand. The arm folded
  up and the palm was 30-35 mm off for three frames.
Measured (World 3, `GaitProbe.PickRun`; time to the carry, item move before the hand had it, palm to grip when it first
moved, least elbow reaching): floor can 2.0 s, 0 mm, 0 mm, 29; floor tape 1.9 s, 0, 0.1, 27; floor chalk 2.1 s, 0, 0.4, 23;
floor lamp 2.0 s, 0, 0, 28; desk can 1.7 s, desk tape 1.7 s, desk lamp 2.2 s (all 0 mm, 0-0.2 mm, 25-28); shelf 0.61 m
can 2.2 s, tape 1.3 s, chalk 1.2 s; shelf 1.69 m can 1.4 s (all 0 mm, under 1 mm, 25-27). Fastest the item moves up:
0.8-1.7 m/s, no jumps (frames of 60-70 mm are where a probe picture held up a frame). Before: a can 1.7 m away flew in
0.02 s after the click with the palm 1.25 m off, 220 mm in a frame. Floor 1.3 m out, desk 0.7 m in, shelf bottom deck: not
offered. A tape 0.7 m into a table taken by code: the hand came back after 2.6 s, the tape moved 0 mm. Cord plug-in palm
error 35 -> 3.8 mm, in at 1.5 s. Doors (a new world 3): hold and walk in 88 degrees, quick push 88, hold pull 24 and
quick pull 26 (a wall 1.0 m behind the pull side; the body cannot step back).
Open after round 8: under a table there is nothing to reach: furniture colliders are solid boxes to the floor, and a
body crawling stops at the table's edge. A flat item (tape, chalk) on a shelf above the shoulder is refused: the arm
would pass through the board's edge. Items on a desk are reached only up to about 0.2 m in from its edge. Floor pickups
end with the arm nearly straight down, as the body squats no lower than a 0.65 m shoulder. The item is pushed out of the
palm's skin as it rises (up to 14-20 mm for a can). Furniture is still reached with a straight arm (elbow 3-4 degrees).

Round 9: the body's way to an item (2026-10-28, reviewer 7.5/10, pickups 6.5):
- **One plan for what is offered and what the body does.** At the click the body plans its reach (`PropGrabber.PlanReach`):
  how far to step (one step, up to 0.65 m), how far to squat and how far to bend forward from the hips. A step costs most,
  then the bend; a squat is cheap only for a grip lower than 0.42 m above the feet. Waist-high, the knees stay nearly
  straight. The grip should end within 0.95 of the arm's reach, so the elbow is bent. The focus marks use the same plan
  with the arm straight (1.0). Standing closer can no longer lose an item.
- **Bending at the hips** (`FirstPersonBody.HingeShoulder`, `HingeHead`, `ReachHinge`). The pelvis tips forward over the
  thighs (65 % of the bend), the spine takes the rest and turns the reaching shoulder forward up to 20 degrees, and the
  hips stay. The feet come up to 0.1 m forward under the view. At 60 degrees the shoulder goes about 0.45 m forward and
  0.25 m down. The view goes with the head (`ApplyHingeView`), else it would sit over the body's own back. The moves are
  measured on the rig for a spine already bent by a squat (bent over already, a bend at the hips takes the shoulder down,
  not forward). The head must be clear of walls and boards, and the arm's line is checked from the bent shoulder; the
  edge of what the item stands on is left out.
- **Alongside, not before.** The step, the squat and the bend run while the arm goes out. Once the grip is within 0.95 of
  the reach from the real shoulder, the body goes no lower. The step stops short of the item (the body pushed a can 4 cm).
- **The grip it took is held.** The item stays in the hand as the hand took it until the lift is a third done. The change
  to the carry grip then runs on 0.3 s past the lift (`HoldAsTaken`). Before, the item was put onto the palm and out of its
  skin from the first frame and slid 2-3.5 cm. `Mathf.SmoothStep(a, b, t)` returns a value between a and b, not a ramp
  from a to b; it was used that way and read 0.5 at the start of a lift (`Ease` now).
- **Desk aim.** Looking at furniture, an item on it within 0.1 m of the view's line is offered (chalk on a desk never was).
- **Furniture** is reached with the same plan, so the elbow is bent when the hand gets there (it was 3-4 degrees).
- **Quick pull.** The hand keeps the lever and pulls the door round while the body steps back and to the latch side (up to
  0.9 s, the arm no further than 0.97 of its reach). The door rests on the body and follows it out (`Door.Tug`). A hand
  that lets go fades out at once, kept where it was from the shoulder.
- **Lever wrist.** A hand on a lever is turned a little about the forearm's back so the wrist bends no more than 20 degrees
  to the side (`WithWristSide`, from `FirstPersonBody.WristSide`). A held door's hand lets go once the palm is 3 cm off the
  lever for two frames (it was 5 cm for 0.12 s, the arm out straight). A tug steps to the latch side first, then back.
- **Grips.** Rubber chicken: the thumb closes fully (curl 1, was 0.8), and squeezing gives 11 mm (4.5 closed the fist 3-4
  degrees; now 11-19 degrees a joint). Chalk: the index's end joint bends 16 degrees. Reeling: the wrist tips 11 degrees
  each way with the stroke and rolls 8 (60 and 18 flapped the wrist -37..+31; not re-measured).
Measured (World 3, my own desk 0.75 m, boards 0.8 and 1.6 m; `GaitProbe.PickRun` now logs the torso and knee, the hinge,
the shoulder, and the grip's slide and turn in the hand): every desk stand 0.25-0.8 m takes a can 0.2-0.45 m in (0.75 m in
is not offered), grab 0.50-0.67 s, carry 0.96-1.14 s, torso 32-53 degrees with the knees 9-36, elbow at the grab 28-54
(least on the way 27-40); boards 0.8 m and 1.6 m (from 0.45 and 0.65 m) taken in 0.55-0.61 s; floor 0.6-0.9 m ahead taken
in 0.85-0.92 s, carry 1.30-1.38 s, a squat (knee 122-140) with the torso 73-78; 1.3 m out not offered. The item moved 0 mm
before the hand had it in every run, and 0 mm and 0 degrees in the hand through the first third of the lift. Doors: quick
pull 24 -> 42-46 degrees in the open (49-87 with a wall 1 m behind), held pull 75 degrees with the wrist at most 22 to the
side (was 26-31), push 57-88, quick push 88.
Open after round 9: the change from the grip taken to the carry's is 15-50 degrees in the hand over the end of the lift; the
quick pull's hand still lets go within a few frames in the open (the door then follows the body out); the chicken's thumb
is on the neck in some runs and 15 mm off in others, and 9-17 mm off while squeezing; the lamp's wrap, the tape's hook, the
rail, the plug pinch's wrist and the hammer carry's thumb are unchanged; side views draw no torso, so the bend shows only
through the arm and the legs.

Round 10: floor pickups for every item (2026-10-28, reviewer 7.5/10, pickups 7):
- **Bug: the coil and the desk lamp were never offered off the floor.** The body's capsule kept the feet 0.6 m from any item
  heavier than 0.5 kg, so the grip was out of reach. Now the capsule passes over the item the hand goes for (`PassOver`). It
  is solid again once the body steps away. The step stops when the toes are 0.15 m (`ToeClear`) from the item's footprint:
  its boxes at the feet, seen from the middle and from each foot (`StepToBox`). A round about the item's middle kept the
  body 0.45 m from a hammer lying across the way.
- **Bug: the sledgehammer was offered but never taken.** The offer looked only at the distance. Now the arm is solved from
  the planned shoulder as it is each frame, wrist limits included (`FirstPersonBody.ArmStrainFrom`, `HandGets`). The hand may
  be at most 25 degrees off its turn, and the palm may miss by at most 12 mm. The reach's own turn (`ReachTurn`) avoids
  turns that miss.
- **Any side, any way up** (`IGripRound`): a can, a lamp's rod, a hammer's handle, a coil, a chicken's neck, a tape roll and
  a stick of chalk can be taken from any side round their axis. The side is chosen at the click: the back of the hand toward
  the shoulder and up, never with the hand in the floor. A handle may also be taken end for end. As the item comes up, it
  turns in the hand to the carry grip.
- **The lift follows the hand.** The palm goes in a straight eased line from where it took the item to the carry, and the
  hand turns the shortest way. The item goes with the hand. Before, the item's middle was eased and the hand put on it:
  a long item's grip swung out of reach and a hammer jumped 32 cm in one frame. The blends that put the item on the palm
  now turn it about its grip, not its middle. The palm stays within 1-5 mm of the grip through the lift. It was 23-60 mm.
- **Low grips.** Below 0.15 m the body may bend 10 degrees more at the hips. At the lowest view (62 degrees down), an
  item lying further down toward the feet is still offered: a can 0.45 m ahead was out of the aim.
- **Held off.** A reach no nearer goes on out. It used to wait for a bend the body could not make, kept the arm bent and
  gave up 45 mm from a lamp's rod.
- **Held door pull, wall behind.** Pulling with no room to walk back, the hand tugs as the quick pull does
  (`TugFromHold`). The body steps to the latch side and the door rests on it. It stalled at 26 degrees and sagged to 14.
- **Cord plug-in.** The real flow does not move the coil box: it is set down and stays (0.00 m). The 0.2 m in the review
  was the probe's box falling 0.18 m after it was spawned in the air. `PlugInRun` now counts from the click.
Measured (World 3, `GaitProbe.FloorSweep`: 7 item types, stands 0.45, 0.6 and 0.9 m, upright and lying): every upright item
offered and taken from every stand, grab 0.85-1.42 s, item moved 0 mm before the grab, least elbow 24-36, palm off the grip
in the lift 0-5 mm, fastest the item moves 1.4-3.5 m/s. Lying: can, tape, chalk, coil and chicken taken in all tested
poses; hammer in 4 of 6; lamp in 2 of 6. Desk can 0.58 s (unchanged). Doors: wall 1.0 m and 0.86 m behind, held pull 66-68
degrees; open room 76; quick pull 46; push 88; quick push 88. Lamp plug-in 1.51 s; coil plug-in 1.52 s, box 0 m.
Open after round 10: a lamp lying on its side is not taken from some sides (offered and given up at yaw 0; not offered
with the shade toward you), and a hammer or a chicken lying with its grip on the far side, behind its own bulk, is not
offered from that side (it is from the other side). The can turns up to 135 degrees about its axis in the hand on the way
up. The "cordplug" probe's deployed cord lifts the player 0.7 m at some sockets.

Round 11: the handling plan (2026-10-28, Docs/HandlingPlan.md; no reviewer rounds, measured in World 3):
- **On time instead of exact.** A reach has 0.32 s (0.5 s for something low) to get the palm on its grip; from then a palm
  within 4.5 cm takes it and the hand closes the gap as the fingers close (`TakeBy`, `TakeSlack`, `TakeGap`; given up on,
  within 8 cm still takes it). The arm comes out in 0.22 s (it was 0.45), the hand eases to a new target in 0.18 s (0.3),
  the fingers close in 0.11 s (0.25), the lift takes 0.28-0.45 s (0.45-0.75), the squat goes 3.2 m/s down, 2.6 up (1.9,
  1.6), the bend at the hips settles in 0.08 s (0.13), and the arm goes on out while the body finishes its last 10
  degrees and 10 cm. The item still never moves before the hand has it.
- **Forgiving aim.** One long step of up to 0.95 m (0.65). An item the arm reaches but not at the exact turn it asks for is
  offered too (`PlanSideLoose`). What was aimed at keeps the focus (half as much aim again, and it wins near-ties).
- **Doors on a left click** (`PropGrabber.QuickDoor`): the quick way; held past 0.22 s, the careful way. A door open 30
  degrees or more is swung shut (`Door.ShoveShut`). It goes once the palm is on it or after a third of a second within 4
  cm. A tug steps straight back when a wall is right there on the latch side (`LatchSideOpen`).
- **Plugging in without dropping.** The extension cord's coil stays in the hand: the hand takes the whole coil to the
  outlet, the plug on its edge into the socket, and comes back with it as the cord pays out (`CordBox.CoilGoingIn`). The
  lamp is set down, its plug carried in, and the lamp picked straight back up, all from the one click
  (`DeskLamp.TakeBackAfterPlug`). A plug carried in gets square in a third of a second, pushes in 0.16 s, and one held
  short (by up to 6.5 cm, the arm at its reach) is pushed and slides in the rest of the way (`Plug.SeatFrom`).
- **The grip it is taken by is the grip it is carried by** (`KeepsSide`): taken from another side round its axis, an item
  is carried turned so; it used to turn up to 135 degrees in the fingers on the way up. Not the hammer, nor a handle
  taken end for end.
- **Placing with E** (`PropGrabber.Place.cs`), above. The body only squats and bends for it (a reach plan with no step:
  told to step toward where the view met the floor, it walked on and on). The item comes over and down in a small arc
  (6 cm) no faster than the body bends, and once let go it glides the last of the way onto its spot (up to 0.15 s) and
  sleeps. `WorldItem.RestTurns`, `WorldItem.Busy`, `ImpactAudio.SetDown`.
- **Hands in view** (Camera menu): above.
Measured (World 3): floor pick-ups up 0.67-0.85 s after the click (0.85-1.42), in the carry 0.99-1.14 s (1.30-1.38); a
floor item 1.2 m out now offered; desk can up 0.33-0.40 s (0.50-0.67), carry 0.62-0.70 s (0.96-1.14), turning at most 31-40
degrees in the hand (102). Door click from the push side: swinging at 0.36 s, open 88 at 1.0 s; click shut from the pull
side latched at 0.75 s; pull: 43 degrees at 0.9 s, 62 at 1.4 s on four doors. Coil plug-in 1.05 s (1.52), held throughout;
lamp plug-in 1.45 s and back in the hand at 2.6 s. Placing, lamp, can and hammer: tap placed at 0.58-0.63 s, hold-and-release
0.94-1.09 s, the player moved 0 m, the arm drawn throughout, no frame of the item over 11 cm (it jumped 39-45 cm onto its
spot before). EditMode 135/135.
Open after round 11: the body's step in for a floor item still takes about half a second; a cardboard box in front of a
socket is climbed onto by the step in (the body rose 0.57 m and the plug gave up); the hammer cannot yet lean on a wall;
fixed finger poses per item wait for the user's hand videos (Docs/HandlingPlan.md).

Open: furniture's colliders are boxes that do not match the model everywhere (a sofa's rolled arm: the fingers close on
the box, 15 cm under the visible top, so they stay as wanted); the thumb lies along a hammer's handle rather than round
it; the jamb's touch still has the little-finger side 1 cm in for a moment (the forearm at its turn limit); fingers on
the tape roll's core 7 mm in.

## Testing
`Debug/GaitProbe.cs` (2026-10-28): `GaitProbe.Run(folder)` measures the body's gait on a flat test floor 60 m above the
player with a test staircase (stand, walk, stop, run, slow walk, strafe, back up, diagonal, turn on the spot, look
round, crouch, crouch-walk, stand up, stairs): cadence and step times, stance share, planted-foot slip, shoes into the
floor, bone spikes, knee angles, pelvis and thorax, arm swing, foot roll (`gait.txt`, `gait.csv`). `Film(folder)`: side
and front frames of a walk, run, strafe and backward cycle in the office (shadow-only parts drawn). `View(folder)`:
first-person frames at pitch 0, 45, the limit and 70, standing and walking, with how much of each is the body.
`GaitProbe.MeasureHand(folder, tag, next, render)`: the hand reviewer's numbers for what the hand holds (elbow, wrist,
forearm turn, upper arm roll, palm to surface and to its point, each finger's joints and tip, skin vertices inside) and
seven renders; then picks up `next`.

`Debug/GrabProbe.cs`: in play mode, add it and call `Run()`. For each kind it finds a prop (travelling to
one if needed), grabs, lifts, drags, swings and shoves it with screenshots, then walks away until the
chunk unloads and checks the prop comes back where it was left.

## Buttons and switches
Left click on a button (`ElevatorButton`, an elevator call button or the cab's button) or a light switch
(`LightSwitch`) within 2 m (focus brackets show which): the hand steps you in if needed, the arm reaches out with
the index finger extended (the other fingers curl), and the fingertip makes the target's stroke (`IPokeTarget`): a
button is pushed in, a switch's toggle is flicked from above (down) or below (up). Then it pulls back
(`PropGrabber.Poke.cs`; the body keeps the pointing pose while the arm lowers). A button lights briefly when pressed,
and stays lit while its elevator is on the way.

`Debug/WalkTrace.cs`: a bot that walks (or stands) while recording height, grounding and bumps, used to check
stairs and the elevator are smooth.

`Debug/HandShots.cs` (`Run(folder, "items" | "sofa" | "switch" | "door" | "tape" | "all")`): pictures of the hand
holding each item, grabbing a sofa at three spots, flicking a switch (and the room before and after), opening doors
(pushed and pulled) and laying tape. `Debug/CordProbe.cs`: walking with the lamp (speed steady), walking away from an
outlet with it plugged in (held back, then out), reeling an extension cord in from its plug.

## Not done yet
- Saving moved props to disk (they last the session).
- Creatures moving things while you are away (the manager's records are the place for it).
- Two-handed carrying, stacking aids, an inventory.
- Per-finger contact: fingers curl by a set amount and can sink into thin edges.

## Cardboard boxes
Loose boxes (and the boxes on racks and pallets) share one generator, `CardboardBoxGeometry`: bevelled edges
with smooth normals, a taped top seam wrapping down the ends (most boxes; the rest show the flap gap),
shipping and address labels, "this side up" arrows, a FRAGILE stamp and a maker mark from the print atlas
(`BoxPrintAtlas` lists where each sits on `Textures/BoxPrint_Atlas.png`). Three materials: corrugated board
(`M_Cardboard`, 0.5 m repeat, flutes in the normal map), tape (`M_BoxTape`) and print (`M_BoxPrint`, cutout).
