# Plan: handling chairs and furniture (2026-10-30, revised)

One-hand grabbing works (hold left mouse, the hand takes a grab edge, mouse swings and lifts your end, WASD drags). What
is missing is control over how a chair ends up: lifting it onto its side, turning it upside down onto a table, or
standing a tipped one back up.

Decided: **all physics, no placement mode.** Furniture is heavy and the fun is in wrestling it: the hand only ever
applies forces a hand could, and where the chair ends up is what those forces did. Nothing snaps or lines up.

## Status (2026-10-30): first try built, for the owner to feel

Decided: two hands with the camcorder on its strap (option A).

- Built: the wrist (torque toward the angle held, 14 N m one hand, 60 N m two, gives past 35 degrees); R to turn (about
  the foot's edge on the floor, the balance point one-handed in the air, the hands two-handed); H for the strap (chest
  camera with a pendulum swing, the left arm drawn and posed on the second grip or hanging); the second grip chosen from
  every grab edge; grab edges on sides and undersides of every kind; both hands' targets from one wanted pose (they never
  pull against each other); stepping toward a turn out of reach; no slipping while turning. Probe: `Debug/FurnitureProbe.cs`.
- Measured (World 3): one hand tips an armchair about 20-24 degrees (then out of reach without stepping). Two hands lift
  and roll it about 45 degrees off the floor. An office chair slides on its castors rather than tipping (as a real one
  does). Rough: lowering a rolled chair to the floor often slips the right hand before it lands, so getting it to lie
  on its side is not reliable yet; strain sits near full most of the time (the hands' targets are held near the arm's
  limits); the hands are mostly out of the strap camera's view.

Owner's first look (2026-10-30): no H key needed (taking furniture should lower the camera by itself), and nothing
could be seen while moving it (the face in the furniture, or through it). Now: the strap comes on with every
furniture grab and off after letting go; the strap camera sits higher (0.34 m under the eye), looks down 10 degrees
plus a quarter of the look, and keeps 30 cm clear of the held thing (rising toward the eye); furniture is held at
least 0.38 m out. Checked: the room is in view while holding, tipping and rolling an armchair; both hands show. Open:
the left hand can float short of its grip when the chair is turned away from it.

Owner's second look: holding shook fast side to side. Measured (`Debug/HoldJitterProbe.cs`, a CSV of the camera, body and furniture each frame): the camera was smooth; the armchair flipped 2-3 degrees back and forth every physics step (spinning up to 5.7 rad/s while held still). The hands' springs were sized for the whole mass but pull far from its middle, where it mostly turns and weighs a few kilos, and the wrist's rough inertia was several times the chair's. Now each pull is scaled to the mass felt at its point and the wrist uses the real inertia: held still it does not move (0.01 rad/s), nothing moves more than about 1 mm a frame. Also: the wrist gives freely only on which way the thing faces, on its tilt only past 60 degrees (a quick turn on the spot used to roll an armchair over onto its back and keep it there); the strap camera rises more gently out of the way.

## Why it is hard now

- **One spring at one point.** `PropGrabber.FixedUpdate` pulls the grip point toward the hand with a strength limit
  (`maxPush` 650 N, `maxLift` 480 N). Nothing turns the chair, so it swings round the grip like a pendulum, and you
  cannot choose which way it falls.
- **Grab edges only on top** (`MovableShapes.Build`): the top of a chair's back and the edges of its seat, all facing up
  in the chair's own frame. On its side there is nothing sensible to take, and nothing on its legs or underside.
- **The camcorder is in the left hand**, so everything is one-handed, even a sofa end.

## Phase 1: a wrist (turning what you hold)

- **Wrist strength.** Besides pulling, the hand can twist and tilt what it holds with a limited turning force. It is
  strong close to the chair's balance point and weak far from it, as a real wrist is.
  - A light chair taken by its back frame keeps roughly the angle you hold it at.
  - Held by one leg, it hangs and swings.
  - A sofa barely notices.
- **Turning it on purpose.** Hold **R** while holding and the mouse twists and tilts it about your grip instead of
  moving your end. Up and down tilt it toward or away from you; left and right roll it onto either side. The wrist's
  limit and the chair's weight decide how far it goes. Too far over the balance point and it falls the rest of the way
  by itself.
- **Its weight shows.** Turning a heavy thing is slow, the hand strains and the camcorder dips more. Past the wrist's
  limit, it slips out of your grip.
- The force is applied at the grip (`AddForceAtPosition` plus a capped torque), so the physics engine still decides
  everything: knocks, friction, legs catching on the carpet, castors rolling.

## Phase 2: grab it however it lies

- **Grip points from the model, not a fixed list.** The hand placement that already samples the mesh
  (`MovableProp.PlaceHand`) finds any edge the fingers can hook in the chair's current pose. That means the back, the
  seat's front or side, an armrest, a leg, or the frame under the seat, upside down or on its side. The fixed edges
  become hints for the best spots, not the only ones.
- The edge nearest where you look, as now, in any orientation.

## Phase 3: tipping, righting, lifting

- **Tipping:** take a top edge and pull it toward you or push it away. It rocks about the legs on the far side, and
  past its balance point it goes over. If you keep hold, you feel its weight come onto the hand and can lower it
  slowly.
- **Standing it up:** take the upper side of a chair lying down and lift. It turns about the edge on the floor (the
  lift force is aimed to turn it about that edge, not a straight pull) until it rocks back onto its legs.
- **Lifting it clear:** a light chair lifted by its back comes off the floor. It hangs from your hand at the angle
  your wrist holds, swings as you walk, and knocks door frames and walls. Upside down onto a table is a lift, a turn
  (R) and a lowering.
- **Heavier things:** a table or sofa can be tipped up onto one end or edge with the same pull and lift. It is slow,
  you strain, and you often can't hold it.

## Phase 4: two hands (a design question, below)

- **Option A, camcorder on its strap:** a key lowers the camcorder to hang from its strap at your chest. The picture
  tilts, bobs and looks down, as found footage does when someone needs both hands. The left hand is then free.
  - Two grips: a chair held out in front, a box in both arms.
  - One end of a sofa or table lifted clear (the other end drags).
  - Double the strength, and much better control of the angle (two grips make a lever).
  - The same key raises the camcorder again.
- **Option B: stay one-handed.** The wrist (Phase 1) does the turning; heavy things stay mostly dragged and tipped.

## Phase 5: through doorways and onto things

- Turn a chair onto its side to fit it through a door.
- Stand a sofa on its end and walk it through.
- Stack chairs upside down on a table, put a box on a shelf.
- All done by hand with Phases 1 and 3. Moved furniture already saves its pose (`MovablePropManager`).

## How it is checked

- A probe (like `Debug/HandShots`) performs each move in World 3 with the same inputs a player would give, and takes
  screenshots from the camera and from outside:
  - tip an office chair onto its side, then onto its back;
  - stand it up again;
  - lift it upside down onto a table;
  - carry it through a doorway on its side;
  - tip a table onto its end.
- It logs how long each move takes and whether it worked, and the hand's distance from the surface (the hand
  checks used in earlier rounds).
- EditMode tests: grip points exist in every pose; the wrist never turns more than its strength allows.

## Question for you

Two hands: **A** (the camcorder hangs on its strap and the picture drops while you carry) or **B** (stay
one-handed)?

## Order

Phase 1 and 2 first (the wrist, and grabbing in any pose), since together they answer "you can't lift it on its side".
Then 3, then 5. Phase 4 waits for your answer.
