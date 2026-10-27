# Moving things: how it works

## Controls
- **Hold left mouse** on furniture or a box within about 2.4 m. If it is a little too far you step in; if it
  is low you bend down. The right hand takes the nearest grab edge to where you look, a few centimetres
  different each time.
- While holding:
  - **Mouse left/right** swings your end sideways. Past the arm's limit you turn with it.
  - **Mouse up/down** lifts or lowers your end. Lift a long thing high enough and walk into it to stand it on end.
  - **WASD** walks and drags it with you, slower the heavier it is (and very slowly away from something stuck).
  - **Right mouse** shoves it and lets go. **Release left mouse** to let go.
- **Right mouse** without holding: a quick push. Walking into light props nudges them.
- The left hand holds the camcorder: it dips and trembles a little while the right hand works, more for
  heavy things. (`PropGrabber.rightHanded` off brings back the old left-handed setup and left arm model.)
- **Items**: **click** one within about 2 m to pick it up in the right hand; **hold left mouse** to use it. There is
  no crosshair, so the aim is forgiving: if the click ray misses, the hand takes the item that makes most sense near
  where you look (within about 14 degrees, or 12 cm of the line, in sight, nearer and more central first). Camcorder
  focus brackets mark the item a click would take.
- **Q** tapped drops the item (with a little toss). **Hold Q** to wind up and release to throw: the longer you hold
  (0.2 to 0.9 s), the harder (3.5 to 13 m/s). While the hand holds an item it does nothing else.
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
- **Extension cord**: as found it is coiled; click an outlet with it in hand and the plug goes in while you carry the
  box (three sockets) away on 8 m of physics cord. Tap right mouse to coil it up again, or hold it to reel the cord in
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
- Items lying in the world have a soft contact shadow under them (`Items/ContactShadow.cs`) instead of shadow-map
  shadows, which flickered on small moving things.
- Cords no longer pass through floors and walls: each particle sweeps from where it last was and slides along what it
  hits. A span that cuts through a corner puts its nearer particle on the corner's outside edge (where the two faces it
  went in and out of meet), so a cord wraps round door frames and wall ends instead of through them.
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
  A shut door (under 2.5 degrees open) is a wall to light and sound (`Generation/DoorEdges.cs`): the light grid encodes
  its edge as a wall, so neither the bounce field nor real lights pass it; the building audio's paths cannot go through
  it and the straight line through it costs about -9 dB and muffles (a wall: -18 dB); Steam Audio gets a wooden slab in
  the doorway. Light from a lit room shows as a bright line under the door on the dark side (`Resources/Shaders/
  LightSpill.shader`, strength from `FixtureLightManager.RoomLight`); once the door opens the light comes through the
  gap for real. An unlatched door is never quite still: air moves it a degree or two now and then (a gust a little
  more, with a creak). It never latches itself.
- **Leaning**: hold **Z** or **X** to lean the head out to the left or right (0.32 m, the view rolls 9 degrees, the
  torso tilts) to look round a corner. It stops short of walls; not while running or crawling.
- **Steadying hand**: walking past a door jamb or the end of a wall on the hand's side, the free hand sometimes reaches
  out and rests on it for a moment (palm flat, a soft pat), then lets go (`PropGrabber.Touch.cs`).
- **Fingers**: every finger and the thumb curl only until they touch what the hand holds (the held item's own shape,
  the furniture's colliders, a door's lever, the wall), so they close round a stem, lie on a flat top and never sink in
  (`FirstPersonBody`, "Fingers"; `PropGrabber.ContactDistance`, `WorldItem.ContactDistance`).
- **Weight**: a heavy item in the hand (the lamp, a coiled cord) slows your steps and turns a little and lags the view
  more. Picking something up off the floor, you bend down for it first.

## Pieces (`Scripts/Runtime/Interaction`)
| File | What it does |
| --- | --- |
| `MovableSpec.cs` | Plain data for one movable prop (kind, models, size), made by the chunk builder; `MovableShapes` turns it into collider boxes, grab edges and mass on the main thread. |
| `MovableProp.cs` | The prop: frozen (kinematic) until disturbed, simulates until it settles, then freezes and saves its pose. Wakes frozen props it touches or hits. Shadows come from proxy boxes. |
| `MovablePropManager.cs` | The list of moved props (the "edits" on top of the generated world). A moved prop no longer belongs to its chunk; it is stored when the chunk it stands in unloads and rebuilt when that chunk loads. Records last for the session and reset with a new seed. |
| `PropGrabber.cs` | The hand: grab, reach, stoop, hold (a strength-limited spring at the grip point), controls, shove. Exposes the hand pose for the body. Where the hand goes is worked out on the model (`MovableProp.PlaceHand`): the surface round the grip is sampled with rays onto the mesh's triangles, every way the fingers could point is tried, and the palm lies on the highest point under it (nothing inside padding), fingers hooking over an edge where the surface drops away, avoiding a rise under the fingers and fingers pointing back at the shoulder. |
| `PropGrabber.Door.cs`, `Door.cs` | Doors: taking the lever, unlatching, the door following the hand, momentum, latching, the player blocking its swing, `DoorStates` (saved angles). |
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

## Testing
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
