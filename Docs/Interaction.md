# Moving things: how it works

## Controls
- **Hold left mouse** on furniture or a box within about 2.4 m. If it is a little too far you step in; if it
  is low you bend down. The left hand takes the nearest grab edge to where you look, a few centimetres
  different each time.
- While holding:
  - **Mouse left/right** swings your end sideways. Past the arm's limit you turn with it.
  - **Mouse up/down** lifts or lowers your end. Lift a long thing high enough and walk into it to stand it on end.
  - **WASD** walks and drags it with you, slower the heavier it is (and very slowly away from something stuck).
  - **Right mouse** shoves it and lets go. **Release left mouse** to let go.
- **Right mouse** without holding: a quick push. Walking into light props nudges them.
- The right hand holds the camcorder: it dips and trembles a little while the left hand works, more for
  heavy things.

## Pieces (`Scripts/Runtime/Interaction`)
| File | What it does |
| --- | --- |
| `MovableSpec.cs` | Plain data for one movable prop (kind, models, size), made by the chunk builder; `MovableShapes` turns it into collider boxes, grab edges and mass on the main thread. |
| `MovableProp.cs` | The prop: frozen (kinematic) until disturbed, simulates until it settles, then freezes and saves its pose. Wakes frozen props it touches or hits. Shadows come from proxy boxes. |
| `MovablePropManager.cs` | The list of moved props (the "edits" on top of the generated world). A moved prop no longer belongs to its chunk; it is stored when the chunk it stands in unloads and rebuilt when that chunk loads. Records last for the session and reset with a new seed. |
| `PropGrabber.cs` | The hand: grab, reach, stoop, hold (a strength-limited spring at the grip point), controls, shove. Exposes the hand pose for the body. |
| `FirstPersonBody.cs` | Placeholder body: two-bone IK for the left arm onto the grip, finger curl, legs that swing while walking, torso that bends with the stoop. |

Movable kinds: sofa, armchair, office chair (casters, low friction), conference table, cardboard box
(new in world version 5: a few against walls in offices). Floor lamps, clocks and racks stay fixed.

Tuning: `PropGrabber` (reach, maxPush, maxLift, stiffness, mouseToMetres, shove), masses and frictions in
`MovableShapes.Build`, grab edges per kind there too.

## Body model
`Tools/Blender/make_player.py` builds the placeholder arm (watch, ring) and body (dark shirt, black
trousers and shoes) from primitives and exports `Resources/Player/PlayerArm.fbx` and `PlayerBody.fbx`:

```
F:\Blender\blender.exe -b --factory-startup -P Tools/Blender/make_player.py -- Tools/Blender
```

Then move the two FBX files into `Assets/Backrooms/Resources/Player/`. Segments are rigid with origins at
the joints; `FirstPersonBody` measures the bone axes at start, so a replacement model only needs the same
object names (UpperArm, Forearm, Hand, Index1..3, Middle1..3, Ring1..3, Pinky1..3, Thumb1..3, WatchCase;
Hips, Torso, ThighL/R, ShinL/R). A skinned model would need the IK moved onto its bones.

## Testing
`Debug/GrabProbe.cs`: in play mode, add it and call `Run()`. For each kind it finds a prop (travelling to
one if needed), grabs, lifts, drags, swings and shoves it with screenshots, then walks away until the
chunk unloads and checks the prop comes back where it was left.

## Not done yet
- Saving moved props to disk (they last the session).
- Creatures moving things while you are away (the manager's records are the place for it).
- Two-handed carrying, throwing, stacking aids, item pickups (inventory).
- Per-finger contact: fingers curl by a set amount and can sink into thin edges.

## Cardboard boxes
Loose boxes (and the boxes on racks and pallets) share one generator, `CardboardBoxGeometry`: bevelled edges
with smooth normals, a taped top seam wrapping down the ends (most boxes; the rest show the flap gap),
shipping and address labels, "this side up" arrows, a FRAGILE stamp and a maker mark from the print atlas
(`BoxPrintAtlas` lists where each sits on `Textures/BoxPrint_Atlas.png`). Three materials: corrugated board
(`M_Cardboard`, 0.5 m repeat, flutes in the normal map), tape (`M_BoxTape`) and print (`M_BoxPrint`, cutout).
