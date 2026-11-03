# Recording real camcorder motion with a phone

Big studios drive handheld cameras with motion captured from a real camera instead of generated noise. A phone's gyroscope
records the same thing: how a camera held in a hand turns and bobs. Record it once; the game then plays it back in layers,
matched to what the player is doing.

## Status

- **Walking forward: in the game (2026-10-28).** Two takes (`Tools/CameraMotion/Recordings/walk_normal_1.zip`, `_2.zip`;
  a Pixel 10 Pro XL in Sensor Logger 1.68, rubber-banded to a 0.98 kg Bible: about 1.2 kg in the hand, like a camcorder;
  held in the left hand, as the game's camcorder is, so nothing is mirrored. Keep using the left hand for new takes).
  98 steps a minute; four straight stretches, 34 strides (42 s) once the pick-up, the turn-around and the set-down are cut
  out. Camera menu > HANDHELD > Recorded walk (on; off is the generated bob). See Camcorder.md, "Recorded walk".
- **Standing, side-steps, turning, a third walk: in the game (2026-11-03).** `idle_stand_1`, `strafe_left_1`,
  `strafe_right_1`, `look_slow_1`, `look_fast_1`, `walk_normal_3`. See Camcorder.md, "Standing, side-steps, turning and
  the walk jitter". The walk's irregularity can be turned down without new takes (Camera menu > Walk jitter).
- Still generated: running, crouching, stairs, backing up, a startle, being out of breath. Most useful next:
  `walk_slow` (creeping), `run` (and stopping), `walk_back`, `stairs_up`/`stairs_down`, `crouch_walk`, a second
  `idle_stand` and `strafe_*` take each (the side-step loops are only 15 s), and `idle_tired`.
- For turning, the overshoot as a turn stops could not be measured from free looking. A take that would measure it:
  `turn_stop`: stand still, turn quickly to a mark on the wall and hold on it, ten times each way.

## What you need

- A phone, and a free sensor-logging app that exports CSV:
  - **phyphox** (free, open source, iOS and Android): use its *Gyroscope* and *Acceleration (without g)* experiments, or a
    custom one with both. Export as CSV.
  - or **Sensor Logger** (free, iOS and Android): turn on *Gyroscope*, *Accelerometer* and *Orientation*, set the rate to
    100 Hz. Export as a zip of CSVs.
- Something to give the phone a camcorder's weight. A 90s camcorder weighed around 1 kg, and the weight is what makes its
  movement slow and heavy. Tape the phone to a small hardback book or a full 500 ml bottle.

## How to hold it

Like the camcorder in the game: in one hand at eye level, about a hand's width in front of your face, elbow bent and tucked
in, phone in landscape with the screen facing you. Use the same hand each time. For a few takes, also hold it out further in
front (the game now holds the camcorder a little out).

## Before every take

1. Lay the phone flat on a table and start recording. Leave it still for **3 seconds**: this measures the sensor's drift.
2. Pick it up and **tap it twice** with a finger: a sync mark.
3. Do the action for **40-60 seconds**, then put it back on the table for 3 seconds before stopping.

## The takes (one file each)

| File name | What to do |
|---|---|
| `idle_breathing` | Stand still, look at one spot, breathe normally. |
| `idle_tired` | The same right after running up stairs (heavy breathing). |
| `look_slow` | Stand still and slowly look around the room. |
| `look_fast` | Snap your view to things, as if you heard something. |
| `walk_normal` | Walk at a normal pace down a corridor, turning at the end. |
| `walk_slow` | Creep forward carefully. |
| `walk_back` | Walk backward slowly. |
| `strafe` | Side-step left, then right. |
| `run` | Run (safely), then stop suddenly. |
| `crouch_walk` | Walk crouched. |
| `stairs_up`, `stairs_down` | A flight of stairs each way. |
| `turn_in_place` | Turn on the spot, both ways. |
| `startle` | Stand still, then flinch as if something banged nearby. |
| `held_out_walk` | Walk with the phone held further out in front. |

Two takes of each is better than one; any subset helps (start with `idle_breathing`, `walk_normal`, `look_slow` and `run`).

## Handing it over

Put the exported files in `Tools/CameraMotion/Recordings/` with the names above (e.g. `walk_normal_1.csv`, or the zip
renamed `walk_normal_1.zip`) and tell me. Also say which phone and app you used, and roughly how heavy the setup was.
A turn-around in the middle of a take is fine (it is found and cut out); no trimming is needed. With one phone the double
tap is not needed for syncing (all its sensors share one clock), but it does no harm.

Rebuilding the clips from the recordings (Python with numpy and scipy; every kind of take it knows, by name):

    python Tools/CameraMotion/build_camera_motion.py Tools/CameraMotion/Recordings --report --out-dir Assets/Backrooms/Resources/CameraMotion

## What I'll do with it

- Read the gyroscope (and the orientation, if it is there), take out the drift measured at the start, and line the takes up
  on the double tap.
- Remove the slow, deliberate turns (the player's mouse does those) and keep only the hand's own movement: the tremor, the
  sway, the bob of each step and the jolt of stopping.
- Cut each take into a seamless loop and split it into layers: breathing, walking bob, tremor.
- Play the layers back in the game, blended by what the player is doing (standing, walking, running, crouching, out of
  breath, startled) and timed to the feet, in place of the generated shake (`HandheldCameraShake`). The camcorder menu's
  shake settings keep working on top.
