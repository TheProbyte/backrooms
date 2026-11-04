# Plan: faster, better-looking hands, pickups, plugs and doors (2026-10-28)

**Status (2026-10-28): steps 1-5 built and measured (Interaction.md, round 11).** Not done: authored finger poses per item
(waiting for the hand videos), the hammer leaning on a wall, quick-place lines up within 15 degrees only on boxes and walls.

## What is wrong now, and why

The hand is simulated, not animated. Nothing happens until the solved palm physically arrives: the body plans a step, a
squat and a bend; the arm searches its elbow and wrist every frame; the fingers close until they touch. That made it
accurate (no floating items), but:

- **Slow.** A desk pickup takes 0.5-0.7 s to the grab and about 1.1 s to the carry. A floor pickup takes 0.9-1.4 s to
  the grab, then the lift. Plugging in takes 1.5 s, and a door waits for the hand to reach the lever.
- **Fussy aim.** Since round 8, an item is offered only if the body's reach plan says it can get there from where you
  stand. Further away or at an odd angle, nothing is marked, so you have to shuffle around.
- **Odd rotations.** The hand may take an item from any side and then turn it to the carry grip on the way up. A can
  turns up to 135 degrees in the hand. The elbow and wrist are re-searched every frame, so the hand can swing the long
  way round.
- **Weird thumbs.** Each finger is found by closing until it touches, so the result changes from run to run. The
  chicken's thumb lands 6-15 mm off depending on the run, and the thumb lies along the hammer's handle.
- **The drop.** Plugging in sets the lamp or the coil down first, and you have to pick it up again afterwards.
- **Doors.** The slow way is to hold the left mouse button and walk. The quick way is right click, and a pull opens only
  about 45 degrees.

## How top games do it

- **Guess what the player means and do it at once** (Half-Life: Alyx). Reach toward a doorknob and the hand turns it by
  itself. The pull starts only once the game and the player agree on the target. Valve's own phrase is "deduce what
  player intent is and then just do it for them".
- **The action leads and the hand follows.** Uncharted and The Last of Us, and Unreal's Motion Warping, work this way:
  each action runs on a short fixed timeline, and the hand's path is bent so it lands on the target on time. The last
  few centimetres are covered by moving the item into the hand, hidden by motion blur or by being out of frame.
- **Authored grab poses, small corrections.** Alyx and Unity's VR toolkits (HurricaneVR, Auto Hand) store a pose per
  object. A pose solver only adjusts that pose to the object's size. Nothing is searched from scratch, so thumbs come
  out the same every time.
- **One press for a door** (Prey, Dishonored, Alien: Isolation): it unlatches and swings. Amnesia-style dragging, which
  this game copies with hold-and-walk, is kept only as an extra for careful peeking.
- **Aim to place** (House Flipper 2, Gone Home): with an item in hand, you point where it should go and click, and a
  faint outline shows where it will land.
- **Hands mostly out of shot** (Kane Pixels' Backrooms, bodycam games like Unrecord): reaches happen below and to the
  right of the frame. You see the result and hear it; the arm itself is barely seen.
- **Newest tech for realistic hands:** open-source models turn ordinary phone video into a 3D hand pose with real
  finger and thumb positions: [WiLoR](https://openaccess.thecvf.com/content/CVPR2025/papers/Potamias_WiLoR_End-to-end_3D_Hand_Localization_and_Reconstruction_in-the-wild_CVPR_2025_paper.pdf)
  (CVPR 2025) and [HaMeR](https://arxiv.org/html/2312.05251v1) (CVPR 2024). This works the same way as the
  camera-motion recording: you film your own hand holding each item, and those poses become the game's grips.

## The plan (five steps, each its own commit, tested in World 3)

### 1. Speed and aim (biggest win, smallest risk)
- **Fixed, short timelines** (the hand's path is bent to fit them, whatever the distance):

  | Action | Hand on it | Done (in carry / in socket / swinging) |
  |---|---|---|
  | Item on a desk or shelf | 0.30 s | 0.55 s |
  | Item on the floor | 0.50 s | 0.85 s |
  | Plug into an outlet | 0.30 s | 0.45 s |
  | Door, one click | 0.15 s (latch clicks) | swinging at once |

- The step, squat and bend still happen, but alongside the reach; the grab never waits for them. If the palm is still
  a few centimetres off when its time is up, the item eases into the palm over a few frames. The hand is moving fast
  and low in the frame then, so the eye doesn't catch it. Nothing ever flies from a distance.
- **Never block.** You can walk, look and click the next thing while a lift or set-down finishes. A click during a
  reach is saved and runs as soon as the reach ends.
- **Forgiving aim again.** Anything within about 2 m and 14 degrees of the view is offered. If the body cannot reach it
  from where it stands, it takes a step of up to about 1 m itself instead of refusing. The focus stays on an item until
  you clearly look away, so it doesn't flicker between two items.

### 2. Doors: one click
- **Left click a shut door:** the hand drops the lever and the door swings about 85 degrees away from you in about
  0.6 s. It starts fast, slows, and taps softly against its stop.
- **On the pull side**, the door swings toward you while the body steps back and aside, and it can rest against you.
- **Click an open door** to swing it shut (it latches). Hold left mouse stays as the careful way (peeking through a
  crack), starting only after a 0.25 s hold so a click never turns into a drag. Walking into an unlatched door still
  pushes it.
- **Right click is no longer needed for doors.**
- Sound: the latch clicks as the hand lands, and the hinge creak follows the speed.

### 3. Plugging in, lamps, cords and placing: one action, no drop
- **Extension cord:** the coil stays in your hand. The same hand pushes the plug in (the plug pinched against the coil,
  as people really do it), then the cord pays out as you walk away. About 0.45 s.
- **Desk lamp:** the lamp stays in your hand. The hand holds it by the rod and pushes the plug in with thumb and index.
  If a lamp turns out too awkward to hold that way, the fallback is one chained motion: set it down beside you, plug
  in, pick it straight back up, about 1 s and never a second click.
- Clicking a cord still takes its plug, and clicking a plug in its socket pulls it out.

### 3b. Putting things down: drop, throw, or place by hand
Three ways, from quickest to most careful:

- **Drop** (tap Q): unchanged.
- **Throw** (hold Q, release): unchanged.
- **Quick place** (tap E): the hand sets the item down where you aim and lets go, in about 0.4 s. It sits upright, or
  as it naturally rests, lined up with a nearby edge if there is one.
- **Careful place** (hold E): past about 0.2 s, the hand brings the item down where you aim and keeps hold of it, a few
  millimetres above the surface. **Release E** and it is let go.
  - While E is held, you can adjust it:
    - **Move:** look around and the item slides along the desk or floor, within the arm's reach. You can lean or step,
      and the arm and the body follow.
    - **Fine move:** hold Shift and the view slows to a quarter, for the last centimetre.
    - **Turn:** the scroll wheel turns it, 5 degrees a notch (with Shift held, 1 degree).
    - **Tip over:** R goes through the ways the item can rest: upright, on its side, upside down, or leaning on a wall
      (a hammer standing on its head against the wall).
    - **Gentle help:** it lines up with a desk edge or a wall when within 3 degrees, sits flush against a wall when
      within 1 cm, and can sit on top of another item. Hold Alt for no help. A faint line along the edge shows when it
      has lined up.
  - Let go: the hand opens, the item settles a millimetre or two with a soft knock (the sound fits what it is on:
    carpet, laminate, metal), and it stays exactly so. Nothing settles or slides afterwards, until something knocks it.
    It is saved exactly as placed.
  - Right click while holding E puts it back in the carry (cancel), and releasing E then does nothing. Q still drops it
    from there.
- **Which items:** every handheld item: the lamp, the cord's coil, the tape, the chalk, the spray can, the chicken and
  the sledgehammer. It is one system, so leaving some out would save nothing, and you can't predict which things you
  will want to arrange. Furniture keeps its own grab-and-drag.
- **Why E, not a click:** a click already does things with an item in hand (it switches the lamp, sprays, draws
  chalk). E is free in play, and one key for "put it down" is easy to remember.
- **The hand shows here:** you are looking straight at the spot, so the hand is in frame even in the Low setting. This
  is the one moment it helps to see the hand, so the grip has to look good here.

### 4. The hand's look: rotation and thumb
- **One authored grip per item** (two or three where needed), with the finger and thumb joints fixed. Touch-checking
  only nudges a finger a few millimetres; it never searches. The result is the same every time:

  | Item | Grip |
  |---|---|
  | Can, lamp rod, coil | Wrap |
  | Plug | Pinch |
  | Chalk | Pencil grip |
  | Tape roll | Fingers hooked through the core |
  | Sledgehammer | Handle grip, thumb over the fingers |
  | Chicken | Neck wrap |

- **The grip it is taken with is the grip it is carried with.** No turning the item in the hand on the way up. A round
  item (a can, a coil) keeps the side it was taken from. The carry pose adapts to the grip, not the other way round.
- **Few, sensible hand angles:** palm down from above for floor items, palm sideways for upright items at waist height.
  The elbow is picked once at the start of a reach and kept, and the forearm's twist never jumps the long way round.
- **Where the poses come from:** your own hand on film (below), or posed by hand in Blender on the MakeHuman hand if
  filming doesn't work out.

#### Hand poses from your phone: how it would work
It is the same idea as the camera walk: you record, I turn it into data.

1. **What you film:** for each item (or something the same shape: a soda can for the spray can, a marker for chalk, a
   roll of tape, a hammer or a stick, a desk lamp or a bottle):
   - Hold it the way you would carry it. Film 60 fps, landscape, in good light, against a plain background, with your
     whole hand and wrist in view.
   - Slowly turn your wrist so the camera sees the palm side, the back and both edges, for 8-10 seconds.
   - Use the right hand, as in the game. Then also film picking it up off a table once (for the hand opening and
     closing).
   - About 15 short clips in all.
2. **Turning video into poses:** WiLoR (open source, CVPR 2025) finds the hand in each frame and gives every finger
   joint's angle in 3D. It runs here on your RTX 4070 in Python, in seconds per clip. Frames where the hand holds
   still are averaged, which removes most of the jitter.
3. **Onto the game's hand:** the model's hand has the same joints as the MakeHuman hand (three per finger, three for
   the thumb), so the joint angles carry over directly. The item is lined up in the posed hand in Blender against a
   video frame. The game then only nudges each finger a few millimetres to touch the item exactly.
4. **Licence:** WiLoR and HaMeR are built on the MANO hand model, which is licensed for non-commercial research only.
   That's fine for a game you aren't selling. If you plan to sell it, Google's MediaPipe Hands (Apache licence, free to
   use commercially) does the same job slightly less precisely, and the steps above don't change.

### 5. Show the hand less (Kane-style)
- New setting, **Hands in view:** Full, Low (the new default) or Off.
  - **Low:** the carry sits lower and further right, so you see the top of what you hold at the bottom edge. Reaches
    come in from below and to the right, and the camera doesn't follow the hand down.
  - **Off:** no hand is drawn. Items go into the carry in 0.25 s with a camcorder jostle and a sound, and doors open
    with the latch sound and a small jolt. This is closest to Kane's videos.
- Fast hand moves blur under the camcorder's shutter, which hides imperfections even at Full.

## Order and checks
1. Speed and aim. 2. Doors. 3. Plugs and lamps, then placing by hand (3b). 4. Hands in view. 5. Authored grips (from
your videos once you have filmed them; until then, posed in Blender).

The order puts the feel first, because every later step depends on it. Showing less hand goes before the grips, since
it shrinks how much of the grips you see. The existing probes (`GaitProbe.PickRun`, `DoorRun`, `PlugInRun`,
`FloorSweep`) are changed to check the times in the table above, and the reviewer looks at the result once, at the end,
not round after round.

## Not changing
- Nothing floats from a distance.
- Dropping and throwing (Q).
- The cord physics.
- Saves.
- The walking body.
- Hold-and-walk doors, kept as the careful option.
