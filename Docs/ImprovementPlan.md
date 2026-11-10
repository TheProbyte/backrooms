# Improvement plan: fun, look and efficiency (2026-11-09)

A review of the game against its own outline (`Docs/GameDesign.md`), with what to improve and in what order. Nothing
here is built yet. Where an item changes the design, `GameDesign.md` changes with the work, as always.

**How to read it**
- Every item says what was found, what to do, how big it is (**S** under a session, **M** a session or two, **L**
  several) and how to tell it worked.
- **Measured** means a number from the build or a picture from the game today. **Judgement** means my reading of the
  design and of how similar games work; only playing by hand settles those.
- Nothing in the plan uses what the outline rules out: no quota, no meters, no monsters on a timer, no random events,
  no outline or band pass over the picture.

## What was looked at

- The docs: GameDesign, DesignHistory, STATUS, WORKLOG, Performance, FpsRecovery, PerformanceLightingReview, StyleSheet,
  StyleRollout, MultiplayerPlan.
- The build, measured today (RTX 4070, Ryzen 7 5800X, 1920 x 1080, frame cap off):

| Spot | Frame (median) | Worst frame |
|---|---|---|
| Hub, lift hall | 9.9 ms (101 fps) | 14.6 ms |
| Hub, walking | 9.2 ms (109 fps) | 14.6 ms |
| Lift down (first ride) | 8.6 ms (116 fps) | **321 ms**, 1.1 s after arriving |
| Lift up | 8.2 ms (123 fps) | 64 ms |
| Corridor, standing | 10.9 ms (92 fps) | **238 ms**; 5 frames over 50 ms |
| Walking (new rooms loading) | 11.4 ms (87 fps) | 17.1 ms |
| Crawl through a hole | 9.7 ms (103 fps) | 23.9 ms |

- What limits a frame (corridor, each setting three rounds):

| Setting | Frame | Script work | Draw submission | Graphics card |
|---|---|---|---|---|
| As shipped | 10.0 ms | 7.5 ms | **9.2 ms** | 6.8 ms |
| Ray-traced shadows off | 8.9 ms | **3.9 ms** | 8.4 ms | 7.7 ms |
| Ray-traced reflections and occlusion off | 10.4 ms | 7.9 ms | 9.6 ms | 7.0 ms |
| Screen light off | 10.4 ms | 8.1 ms | 9.5 ms | 6.9 ms |
| Volumetric light off | 9.9 ms | 8.1 ms | 9.1 ms | 7.0 ms |
| Surface detail off | 10.2 ms | 7.4 ms | 9.4 ms | 6.9 ms |
| All of the last four off, and indoor reflections | 10.2 ms | 8.3 ms | 9.3 ms | 7.1 ms |

- Pictures from the game today: `Docs/screenshots/plan/`.
- How similar games do it (sources at the end).

## The short version

1. **The loop has never been played by hand.** Every batch feature is "checked by script". Playing slice 1 with a
   stopwatch is worth more than any item below, and several items exist to make that session readable.
2. **The game tells you its state in a list, not in the world.** The work list covers the picture (it sits over
   TALLMORE's own sign in today's picture), while shelves, boxes and seams say little by themselves. Moving the state
   onto the things (shelf tickets, client marks, a pay moment) serves the camcorder pillar and the "is safe easy to
   read" question at once.
3. **Co-op for the work is the largest missing piece of fun**, and it gets dearer with every feature built before it.
4. **Performance is comfortable at 60 fps on this PC** (about 10 ms of a 16.7 ms frame). What is felt is the stutters
   (321 ms and 238 ms frames). The steady frame is limited by draw submission, not by the graphics card, and one
   script cost stands out (3.5 ms). The newer lighting features cost almost nothing here: there is no gain in a
   cut-down preset.

---

## 1. Fun

### F0. Play slice 1 by hand, with a sheet (S, the owner)
- **Found:** `GameDesign.md` section 15 lists every part of the batch loop as checked by script only.
- **Do:** one sitting of 30 to 40 minutes on a new world, noting:
  - minutes until the hand truck is in your hands (aim: 10 or less; a review of Supermarket Simulator names its
    slow first stretch, before the first upgrades, as the point where people give up)
  - trips for one lot by hand and with the truck (section 16 already asks this)
  - how often you stood with nothing to do, and for how long
  - could you say what is due next, and where it is, without pressing J
  - after each payment, could you say what it was for
- **Worked when:** the answers are written into section 15, and the amounts in section 14.1 get their first tuning.
- **One thing to watch (measured):** in the test world the first seam is about 15 m from the lift and the starter
  shelf. At that distance a 4-box lot is two short walks, so the hand truck may not feel like relief until the second
  client. If so, either the first lot grows or the truck's first job is the second seam.

### F1. Getting paid is a moment (S)
- **Found:** a payment is a line of text, a beep, and later the slip in the hub.
- **Do:** at the instant the last box of a batch is safe: the shelf's ticket (F2) stamps STORED with a thunk and the
  camcorder's IN HAND figure counts up. On the last box back behind the seam: a receipt prints from a small printer
  under the OVERFLOW PICKUP sign, with a chunk and a tear. Nothing floats in the air; it all happens on things.
- **Why:** job games are carried by the "job well done" beat (writing on PowerWash Simulator and Hardspace:
  Shipbreaker names it as their main reward).
- **Worked when:** in the play sheet you can say what each payment was for without the slip.

### F2. Shelf tickets: the state on the shelf (M)
- **Found:** what a shelf holds, whether it is lit enough and when it is due are only in the J list and on the board.
- **Do:** a ticket holder on the end of every shelf (the same pixel display as the seam's card): the client's colour,
  boxes on it out of the batch, STORED 2:10 or TAKE BACK 1:40, and one small lamp: green safe, amber low light, red
  blinking when due. LOW LIGHT on the camcorder stays as the second cue.
- **Why:** it answers section 16's "is safe easy to read" in the world, and Hardspace: Shipbreaker's team gave a GDC
  talk on doing exactly this (a fully diegetic interface).
- **Worked when:** standing in a storage room with three batches, you know which to take first without the list.

### F3. Co-op for the work, moved up (L)
- **Found:** batches, the slip, placing shelves, machines and the hand truck are not shared (section 13). The games the
  look is modelled on (Lethal Company, PEAK, R.E.P.O.) are friend games; their reviews credit physical cargo that can
  go wrong plus proximity voice for the stories people tell. This game already has both.
- **Do:** share the work before building packing and powered racks, so those are made shared from the start instead
  of converted later. Needs two answers from section 14.6: who may spend (suggest: anyone, one wallet, the host's save)
  and how lots grow (suggest: half as many boxes again for each extra player).
- **With it, two small things that only exist with friends:**
  - **Throw and catch (S):** a box thrown at a friend who clicks as it arrives lands in their arms. Chains of people
    passing boxes down a corridor become the fast way to move a lot.
  - **Two-person loads (M, already planned):** BELLWEATHER's biggest flat-packs carried at both ends.
- **Worked when:** two players complete a batch together and the slip, board and tickets agree on both screens.

### F4. Exploring changes your route (M, this is slice 2)
- **Found:** "what exploring finds" is planned as a direction with no first item.
- **Do, in this order:**
  1. **Thin walls.** The sledgehammer and breakable walls are built. Mark a few walls between a seam's corridor and a
     good room with an old pencil survey mark; knocking one through halves a route for good. It is progress you can
     walk through (pillar 3).
  2. **Rooms worth having:** big, fully lit rooms with outlets carry a faded company tag, SURVEYED - SUITABLE, so you
     learn what a good storage room looks like.
  3. **Company records:** memos and a tape or two, each a line of the mystery (section 14.9 needs material anyway).
  4. **The dark's nudges** as already planned.
- **Worked when:** in a play session you changed where you store because of something you found.

### F5. Rush lots and audits: pressure you choose (S each)
- **Found:** every lot is the same kind of offer; "express" and "audits" are planned but undefined.
- **Do:** now and then a waiting lot carries a red RUSH tag on its card: half the storage time, half as much pay again.
  Never forced; ignoring it costs nothing. Audits as agreed (part of a batch wanted back early).
- **Why:** the overlap is the game (section 6); these let the player ask for more of it. They are offers on the card,
  not events.

### F6. The company answers back at each stage (S a stage)
- **Found:** finishing a stage shows a line of text.
- **Do:** each stage ends with the PA voice and one physical change in the hub: the counter's shutter rolling up on
  the new stock, a new poster, a memo under the manager's door, a light on behind his blinds. Five stages, five changes.
- **Why:** Satisfactory makes each milestone a small ceremony; here it also feeds the mystery for almost no work.

### Held back (ideas I would not build yet)
- Pay depending on the recording (section 14.13): risks turning the camcorder into a chore. Revisit after hand play.
- More clients, more box kinds: the four are enough until the loop is tuned.

---

## 2. The look

### V1. The work list moves out of the picture (M)
- **Found (measured):** the list fills the picture's top right corner and sat on top of TALLMORE's sign today
  (`plan/tracker_over_sign.png`). The picture is 4:3: on a 16:9 screen a quarter of the screen is black bar, on the
  owner's 5120 x 1440 screen about 62 %.
- **Do:** put the work list in the right bar and the batches in the left, drawn as the camcorder's side panel. Inside
  the picture only the camcorder's own marks stay (REC, counter, date, LOW LIGHT, notices). On a narrow window, or
  with the bars off, today's overlay stays as the fallback, cut down to the one next step.
- **Worked when:** the same picture shows the sign, and J is needed only on a narrow window.

### V2. The seam becomes the game's image (S to M)
- **Found (measured):** a seam is a thin silver rectangle on a plain wall (`plan/seam_from_floor.png`). Behind it is the
  strongest contrast in the game (the client's room is cool, white and blue, and reads very well), but nothing of it
  shows on this side.
- **Do:** cool daylight leaking round the tape's edges and along the carpet in front (as the staff door has the shop's
  light under it), the client's colour on the label and as a flag of tape, and a faint cool cast on the nearest wall.
- **Worked when:** a seam can be picked out from the far end of a corridor, by its light.

### V3. The new wallpaper print reads in the game (S)
- **Found (measured):** under the bright panels the walls come out very bright and the print is barely there.
- **Do:** raise the ink's contrast in `make_overflow_textures.py` (about half as much again), check three rooms.
  Leave the camera's exposure alone.

### V4. A client's mark on its boxes (S)
- **Found:** boxes differ by tape colour and shape. Under yellow light and the camera's white balance the tape
  colours drift (TALLMORE's blue read pink in its own room today; worth a check).
- **Do:** one bold pictogram for each client, printed on two faces, as the style sheet's print rule says. Readable
  without colour.

### V5. The clients' rooms show time passing (S to M)
- **Found:** the rooms look right but nothing in them moves.
- **Do:** a tear-off calendar in each showing the camcorder's date (so "weeks pass in minutes" is seen, not only
  said), daylight shifting under the loading door, a fan turning, the pallet jack in a different place after a lot is
  brought in.

### V6. What you set up looks like yours (S, then M)
- **Do first:** check the colour of placed lamps. If they match the ceiling tubes, make them company amber-warm, so a
  lit storage room reads as yours from the corridor.
- **Then:** a sign kit at the counter (arrows and client colours) and floor tape you lay yourself. The company's guide
  tape already draws itself from the lift to the first seam; the same drawing can serve the player's.

### V7. The rollout's leftovers (M)
- **Found (`StyleRollout.md`):** the hand-held items' proportions are still the old ones under new bevels, and the
  Classic against Overflow build benchmark was never run.

---

## 3. Efficiency

### E1. The stutters first (S to M)
- **Found (measured):** 321 ms on the first arrival on the floor, 238 ms and four more frames over 50 ms in the first
  seconds at the corridor. Second visits are clean (64 to 71 ms), so it is first-use work: most likely the graphics
  card's pipeline states being made the first time each is needed, perhaps first-time asset loads.
- **Do:** capture it with the benchmark's own `-benchmark-spikes-lift` (a development build), name the cause, then do
  that work during the lift ride. If it is pipeline states, Unity 6 has tracing and pre-warming for exactly this
  (GraphicsStateCollection); I found no warm-up of that kind in the build's own code.
- **Worked when:** no frame over 50 ms on a first ride down or a first seam trip.

### E2. The ray-tracing scene list is rebuilt every frame (S to M)
- **Found (measured):** ray-traced shadows cost 3.5 ms of script time a frame. `RayTracedLighting` clears its list of
  everything round the camera and gathers it again every frame.
- **Do:** keep the list; gather again only when the eye has moved a metre or a chunk loads or unloads; update only what
  moved.
- **Worked when:** script work at the corridor is about 4.5 ms (7.5 now), same picture.

### E3. Measure the real game (S)
- **Found:** the benchmark visits a corridor, a crawl hole and the lift. It has no seam trip, no storage room with
  boxes, no client room.
- **Do:** three spots: a seam trip there and back, a client's room (the floor is now kept loaded behind you: its cost
  has not been measured), and a storage room with 100 boxes on 12 shelves. If the kept floor costs, switch its chunks
  off while you are away instead of unloading them.
- **Why first among the measuring:** every later slice adds boxes and machines; without this spot nobody will see it
  slow down.

### E4. Try the compiled script backend (S)
- **Found:** the build runs its scripts on Mono (the folder is in the build). Script work is the second limit.
- **Do:** one IL2CPP build, run the same benchmark, compare. Watch for reflection (the F2 item tuning) and Steam Audio.
  Keep it only if the numbers and a play test agree.

### E5. Draw submission: the GPU Resident Drawer (M to L)
- **Found (measured):** draw submission (9.2 ms) is what sets the frame. `Performance.md` records a test of Unity's
  own answer that took the corridor from 11.9 to 7.4 ms but drew the world black, because the lit shader's forward
  pass lost all its variants in that build.
- **Do:** the clean shader build with the variant audit that the doc already describes, then switch it on.
- **Worked when:** the same benchmark with the picture intact.

### E6. A slower PC to aim at (a decision, then S)
- **Found:** every number is from an RTX 4070. Friends in co-op will have less.
- **Do:** name the weakest card the game should hold 60 fps on, run the benchmark as that card would run it (no ray
  tracing, the low preset), and fix what it shows.

### E7. Build size (S to M)
- **Found (measured):** 718 MB, of which 425 MB is textures loaded from Resources; both looks ship.
- **Do:** list the twenty largest textures and compress or shrink what the camcorder's 640 x 480 picture cannot show.
  If the Classic look is ever retired, its photo textures go with it (a decision).

### E8. Working efficiency (S)
- **Uncommitted work:** everything since `cb369a5` (about 50 files) is only in the working copy.
- **Tests and the owner's saves:** an editor Play test relies on a save-folder override set by hand each time. Make it
  automatic: when Play starts while an agent's lock file is present, the test folder is used.
- **Scripts edited during Play:** the rule was broken three times on 2026-11-09. A hook that refuses script and shader
  edits while the lock file exists would enforce it.
- **Stale docs:** `STATUS.md` stops at 2026-11-04 and describes free roam and the monster as current. Either fold it
  into `GameDesign.md` section 15 or bring it up to date.

### Measured and not worth doing
- A cut-down lighting preset: turning five features off together changed nothing (within the noise).
- Upscaling: the 3D view is already drawn at the camcorder's own resolution.

---

## 4. The order

Each pass ends with the owner playing what it changed.

| Pass | What | Items | Done when |
|---|---|---|---|
| **0. Safe ground** | Commit; test saves automatic; the edit guard; STATUS brought up to date | E8 | A clean working copy; a Play test cannot reach a real save |
| **1. Play it, and make it read** | The owner's sitting with the sheet, then the state moved onto things | F0, then F1, F2, V1, V2, V3, V4 | "What is due next, and where" is answered without J |
| **2. Smooth** | Measure the real game, remove the stutters, the script cost | E3, E1, E2, E4 | No frame over 50 ms on a first ride or seam trip; script work near 4.5 ms |
| **3. Friends** | The work shared; throw and catch; two-person loads | F3 | Two players finish a batch and every screen agrees |
| **4. Out in the maze** | Slice 2 with thin walls first; rush lots and audits; the hub answering back; the rooms showing time | F4, F5, F6, V5, V6 | You changed where you store because of a find |
| **5. Headroom** | Draw submission, a slower PC, build size, the rollout's leftovers | E5, E6, E7, V7 | 60 fps on the PC named in E6 |
| **Then** | The outline's slice 3 (one packing job) and slice 4 (the powered rack), built shared from the start | | |

**How this differs from section 16's order** (batch loop, exploration, packing, powered racks, co-op): co-op for the work
comes before exploration and packing. Four systems already wait to be shared; each new one adds to that.

## 5. For the owner to decide

1. Co-op for the work before exploration and packing (pass 3), or keep section 16's order?
2. The weakest PC the game should run well on (E6).
3. Does the Classic look stay? It doubles the upkeep of every texture and model, and much of the build's size.
4. The work list in the side bars (V1): yes, or keep it in the picture?
5. Rush lots (F5): are optional offers on the card inside "no random events"?

## Sources

- R.E.P.O.'s physical cargo and proximity voice: https://www.gamesradar.com/games/horror/lethal-company-creator-shouts-out-another-viral-co-op-hit-that-already-has-28-000-steam-reviews-says-it-has-the-most-funny-objective-for-a-horror-game ,
  https://www.gamingonlinux.com/2025/03/r-e-p-o-is-a-new-co-op-horror-game-with-silly-physics-currently-exploding-on-steam/page=1/
- Lethal Company's loop: https://unwinnable.com/2024/02/16/lethal-company-and-the-ethos-of-the-bad-job/
- Supermarket Simulator's slow first stretch: https://gametyrant.com/news/supermarket-simulator-review-why-are-these-games-so-addicting
- Hardspace: Shipbreaker's diegetic interface (GDC 2021): https://gdconf.com/news/go-behind-fully-diegetic-ui-hardspace-shipbreaker-gdc-2021
- Job games and the job well done: https://filmstories.co.uk/features/job-simulators-find-magic-in-the-mundane/
- Unity's GPU Resident Drawer and what it needs: https://docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-resident-drawer.html ,
  https://docs.unity3d.com/Manual/urp/make-object-compatible-gpu-rendering.html
