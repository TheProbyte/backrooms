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
- **Blue tape**: press on a wall, doorframe or floor within 1.6 m, drag along it, release to tear the strip off (up to
  1.6 m a strip, 40 m a roll). A strip stays on one flat surface and stops where it ends. The tape is opaque (tile grout
  and wallpaper do not show through), with a crepe texture, a cut edge and a soft shadow just past its edges. The roll
  gets thinner as you use it; an empty roll is the bare cardboard core with a torn stub.
- **Desk lamp**: its cord starts wound up in a bundle behind the base. Click an outlet (or an extension cord's box)
  with the lamp in hand to unwind it and plug in (1.8 m of cord), or click the bundle to take the plug in your hand.
  Right mouse winds the cord back up (not while it is plugged in). Click anywhere else to switch the lamp. Q sets it
  down upright. It lights (a shadowed warm spot) when on and powered. Walk off with it too far and the plug yanks out
  of the socket: a pop, the plug flies toward you and the camera jolts.
- **Extension cord**: as found it is coiled; click an outlet with it in hand and the plug goes in while you carry the
  box (three sockets) away on 8 m of physics cord. Right mouse coils it up again. Walking further than a cord reaches
  pops its plug out. Pick up a plug to pull it out; with a plug in hand, click a socket to push it in. Up to 3 cords
  in a row.
- **Rubber chicken**: like a real one. Hold the left mouse button to squeeze it: it squashes and wheezes as it takes
  in air. Release and it squawks; the longer the squeeze (up to about half a second), the longer and louder the squawk.
  It squeaks when thrown and when it lands hard.
- Items lying in the world have a soft contact shadow under them (`Items/ContactShadow.cs`) instead of shadow-map
  shadows, which flickered on small moving things.
- Cords no longer pass through floors and walls: each particle sweeps from where it last was and slides along what it
  hits, and each span is checked against the world.

## Pieces (`Scripts/Runtime/Interaction`)
| File | What it does |
| --- | --- |
| `MovableSpec.cs` | Plain data for one movable prop (kind, models, size), made by the chunk builder; `MovableShapes` turns it into collider boxes, grab edges and mass on the main thread. |
| `MovableProp.cs` | The prop: frozen (kinematic) until disturbed, simulates until it settles, then freezes and saves its pose. Wakes frozen props it touches or hits. Shadows come from proxy boxes. |
| `MovablePropManager.cs` | The list of moved props (the "edits" on top of the generated world). A moved prop no longer belongs to its chunk; it is stored when the chunk it stands in unloads and rebuilt when that chunk loads. Records last for the session and reset with a new seed. |
| `PropGrabber.cs` | The hand: grab, reach, stoop, hold (a strength-limited spring at the grip point), controls, shove. Exposes the hand pose for the body. |
| `FirstPersonBody.cs` | Placeholder body: two-bone IK for the right arm (`Resources/Player/PlayerArmRight.fbx`) onto the grip, finger curl, legs that swing while walking, torso that bends with the stoop. |
| `PropGrabber.Items.cs` | Holding items: pick up, carry low on the right of the view (raised while in use), use, drop. Places the item after the camera has moved, and the hand on its grip. |
| `Items/WorldItem.cs` | Base of a held item: light physics lying in the world (frozen while the ground under it is unloaded), its hold pose and grip, `Hold(use, aimAt, dt)` every frame while held. |
| `Items/SprayCan.cs` | Red spray paint (model: `Tools/Blender/build_items.py`, `Resources/Items/SprayCan.fbx`). Hold to spray where you look: ~300 traced droplets a second in a 4.5 degree cone, reaching 1.8 m; mist particles; hiss; the ball rattles when picked up. `SprayCan.Lifespan` = 45 s of spraying, sputters for the last 10 %, then only hisses. |
| `Items/SprayPaint.cs` | The paint: soft speckled dots in batched meshes (Backrooms/SprayPaint multiplies the colour onto the surface, so it is lit like the wall; red channel kept at 1 so coats never go black). Wet paint per 2.5 cm cell: past about 2.4 coats on a wall it runs; more paint on the spot lengthens the run (up to 0.55 m); runs stop where the wall ends. Paint goes on the building, not on movable props or items, and lasts the session. |
| `Items/Marks.cs`, `Resources/Shaders/Marks.shader` | Chalk lines and tape strips: flat strips on surfaces, batched (1024 a mesh), saved with the world. The shader lights each mark by the surface under it (luminance of the camera's opaque copy), so marks match their wall in every zone and under any lamp. `Marks.Strip` also builds chunk-owned marks (a noclip door's tape outline, a camp's tally). |
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
nails, palm pads; sleeve, watch, ring; about 13k triangles) and body (shirt with buttons, belt and buckle, trousers,
shoes; about 18k triangles) as smooth surfaces on an armature, weighted by calculation, and writes
`Resources/Player/PlayerArmRight.fbx`, `PlayerArm.fbx` (`--left`, a mirror) and `PlayerBody.fbx`:

```
F:\Blender\blender.exe -b --factory-startup --python Tools/Blender/make_player_rig.py -- Assets/Backrooms/Resources/Player
```

The bones have the old segment names and joint positions (UpperArm, Forearm, Hand, Index1..3, Middle1..3,
Ring1..3, Pinky1..3, Thumb1..3; WatchCase is a mesh on the Forearm bone; Hips, Torso, ThighL/R, ShinL/R, FootL/R),
so `FirstPersonBody`'s IK and finger curl drive them unchanged. Skinned renderers update their bounds with the pose
(`updateWhenOffscreen`). The thumb folds across the palm on its own axis. The older rigid-segment builder
`make_player.py` is kept for reference.

## Testing
`Debug/GrabProbe.cs`: in play mode, add it and call `Run()`. For each kind it finds a prop (travelling to
one if needed), grabs, lifts, drags, swings and shoves it with screenshots, then walks away until the
chunk unloads and checks the prop comes back where it was left.

## Buttons
Left click on a button (`ElevatorButton`, an elevator call button or the cab's button) within 2 m: the hand
steps you in if needed, the arm reaches out with the index finger extended (the other fingers curl), taps
the button, pulls back (`PropGrabber.Poke.cs`; the body keeps the pointing pose while the arm lowers). The
button lights briefly when pressed, and stays lit while its elevator is on the way.

`Debug/WalkTrace.cs`: a bot that walks (or stands) while recording height, grounding and bumps, used to check
stairs and the elevator are smooth.

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
