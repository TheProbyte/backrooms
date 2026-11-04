# The camcorder picture

Everything the player sees goes through a model of a late-1990s consumer camcorder, in the order a real one makes a
picture. Settings live in F5 > CAMERA (`CamcorderSettings`, saved to PlayerPrefs); `CamcorderRig` applies them every
frame.

## Signal chain

| Stage | Where | What happens |
|---|---|---|
| Lens | URP post-processing (driven by `CamcorderRig`) | Lens glare (veiling glare, no threshold), barrel distortion (turns toward pincushion as you zoom), lateral colour fringes, vignette (rounded, weaker as the iris closes), depth of field (only when it would show) |
| Exposure | `CamcorderAutoExposure` | Meters its own picture; iris first, then AGC gain, then (optionally) a 1/30 s slow shutter; auto white balance |
| Sensor | `CamcorderPipeline.shader` pass 0 (Integrate) | Exposure, the shutter's motion blur (1/60 s by default, in linear light, trailing, from the camera's turn and travel and the depth), the pixel footprint and optical softness, onto a fixed recording raster (640 × 480 for 480 lines) |
| Meter | pass 1 | 80 × 60 average of the raster, read back every other frame for the exposure, white balance and focus-hunting |
| CCD smear | pass 2 | Mean of each column (the vertical stripe through very bright lights at high gain) |
| Camera head | pass 3 | Smear, purple fringing, shot + read noise (grows with gain), fixed pattern and hot pixels, black clip, white balance, saturation, Rec.709 transfer curve, per-channel knee, hard white clip, highlight and low-light colour suppression, 3D noise reduction (DV). Out: Y'CbCr |
| Recording + playback | pass 4 (Encode) | Timebase wobble and flagging, head switching, tracking bar, noclip glitch; format luma bandwidth; edge enhancement (horizontal + vertical, cored, level-dependent, limited); colour-under (analog: narrow, trailing right, delayed, comb) or DV 4:1:1; DV compression; tape noise, colour phase noise, dropouts, cross-colour; setup |
| Display | pass 5 | The recording scaled to the screen (smooth, line-doubled or as fields), the viewfinder OSD, optional CRT scanlines |

Passes 0–4 run at the recording raster's size, so their cost does not grow with the screen. On frames that are not
recorded (a recording frame rate, the slow shutter, interlaced fields at 59.94 a second, a noclip door's dropped
frames) only pass 5 runs, on the held recording. The pipeline is a `ScriptableRenderPass` enqueued from
`RenderPipelineManager.beginCameraRendering` for the camcorder's own camera; it disables the old `CamcorderVideo`
full-screen feature and `CamcorderFrameHold` in `PC_Renderer` while it runs (and restores them), switches URP to HDR
colour grading with no tonemapping, and puts the camera's viewport on the 4:3 frame (the whole texture when the
camera renders into one, for stills and probes). The shader has no keywords: one variant per pass.

## Each old effect, and what was done

| Effect | Was it realistic? | Now |
|---|---|---|
| Auto exposure | Partly. A probe camera rendered the scene (with shadows) 6 times a second for a log average; 40 % compensation; "gain" counted the exposure bias; a second low-light boost | Meters the camera's own picture (arithmetic, centre-weighted, clipped, top third counts less). Iris (7 stops, damped spring, dead band), then gain (+6 dB/s up after the iris is open, down first), then optional slow shutter. Noise follows the real gain only. No probe render |
| Fluorescent flicker beat | No: a 1/60 s shutter averages exactly two 120 Hz flickers | 120 Hz flicker against the shutter time: invisible at 1/60, a slow 0.12 Hz pulse at faster shutters (new *Shutter* setting) |
| Tone (Neutral filmic tonemap, LogC contrast, knee off, pedestal) | No | The camera's Rec.709 transfer curve, per-channel knee (400 % light reaches the clip), hard white clip, clipped lights pure white, setup 7.5 IRE on analog |
| White balance (static, after the tonemap) | Partly | Auto white balance: slow and limited, the way a camcorder's works: the red and blue gains are pulled partway (strength 0.3 keeps most of the yellow) toward the green channel, within a 1.8x range, plus your warm/tint shift, in linear light. (Until 2026-10-27 it corrected along warm/cool, red down as far as blue went up, which tipped yellow walls toward green.) |
| Lens distortion | The model was fine; the sign was wrong in the tooltip and presets (presets "barrel" were pincushion) | Positive = barrel everywhere; old negative values were replaced; turns toward slight pincushion at full zoom |
| Chromatic aberration | About 3× too strong | Default 0.03; shrinks with zoom |
| Vignette | Elliptical on the full screen (different on every monitor), −1.4 EV | Rounded, on the 4:3 frame, half as strong, weaker as the iris closes |
| Bloom | Wrong mechanism (threshold 1, strong) | Lens glare: a few percent of all light, no threshold (follows exposure), quarter resolution, no HQ filter |
| Depth of field + autofocus | About 4× too much blur, running every frame | A real 1/4-inch CCD's depth of field at the iris's f-number (f/1.8 wide open). URP's bokeh runs only when the blur would exceed 0.6 recording pixels (close-ups, zoom, hunting). Focus motor in diopters, refocuses only once blur would show; still hunts in the dark |
| Motion blur (URP) + sensor blur | Two blurs, frame-rate dependent, the sensor blur led the motion, in gamma space after everything | One shutter blur in linear light (tubes streak white), trailing, per pixel from the camera's turn and travel and the depth, the same at any frame rate; the arm and held items do not smear |
| Resolution / softness | Display-resolution by default (the look changed with the screen), ring-shaped kernel | Fixed recording raster; softness in raster pixels |
| Edge enhancement | ±48 % halos | Format-typical detail, horizontal + weaker vertical, cored, less in the dark and at high gain, limited to 10 IRE |
| Chroma bleed / delay / detail | Symmetric, in screen pixels | Analog colour-under (σ ≈ 3.4 raster px, trailing right, delayed 1 px, comb filter) or DV 4:1:1 |
| Sensor noise | 3–6× too strong, value noise added in gamma space | Shot + read noise in linear light, ×2 every 6 dB of gain, partly per colour (chroma blotches come from the colour bandwidth) |
| Grain size | Not physical | Removed |
| Compression blotches | Mixed with VHS effects; aliased point samples | DV only: 8 × 8 dead zone and mosquito noise in busy or noisy blocks |
| Colour flicker per frame | Not a camcorder effect | Removed; analog colour phase noise per line instead |
| Scanlines | Not in a recording; hard-coded 480 | DISPLAY > CRT scanlines (off), at the recording's line count |
| Tracking noise | A soft bright band | A noise bar with dropouts and timebase disturbance; analog only |
| Head switching | A tall blue/magenta band | The bottom ~7 lines skew sideways with garbled colour and a noise line |
| Bottom-edge line | A capture artefact | Kept, on the recording |
| Recording frame rate | Fine, but copied full-screen frames | Holds the small recording and skips the camcorder work on held frames |
| Noclip glitch | Mostly realistic | Ported; colour cuts out completely when sync is lost |
| OSD | Rendered and sampled every frame even when hidden; warped | Its camera is off while hidden; flat by default |
| 4:3 pillarbox | Rendered the hidden sides | The camera's viewport is the 4:3 frame: 25 % fewer pixels at 16:9, 62 % at 32:9 |

New: CCD vertical smear, purple fringing, fixed pattern and hot pixels, 3D noise reduction (ghost trails, DV), colour
loss at high gain, timebase wobble and flagging, tape noise, dropouts, cross-colour, interlaced fields, the shutter
speed and low-lux slow shutter, and the format selector.

## Formats

The format sets the recording's own character; the FORMAT sliders are multipliers on it (1 = typical), so a format
never shows artefacts it could not have.

| | VHS-C SP | VHS-C EP | Hi8 | MiniDV |
|---|---|---|---|---|
| Luma bandwidth (σ, raster px) | 0.75 (~240 TVL) | 1.0 | 0.45 (~400 TVL) | 0.35 (~500 TVL) |
| Colour | colour-under σ 3.4, trailing, delay 1.0, comb | σ 4.0, delay 1.3 | σ 3.2, delay 0.6 | 4:1:1 |
| Edge enhancement | camera 0.6 + VCR 0.3 | 0.5 + 0.35 | 0.7 + 0.2 | 1.0 |
| Setup | 7.5 IRE | 7.5 IRE | 7.5 IRE | 0 |
| Timebase wobble / flagging | 0.5 / 3 px | 0.8 / 5 px | 0.4 / 2 px | none |
| Dropouts (per line per field) | 1/2000 | 4/2000 | 3/2000 | none |
| Tape noise (luma / colour) | 0.007 / 0.02 | 0.012 / 0.035 | 0.005 / 0.015 | none |
| Colour phase noise | 4° | 8° | 3° | none |
| Head switching | 7 lines, 6 px | 8 lines, 8 px | 6 lines, 5 px | none |
| Compression | none | none | none | DV DCT |

## Presets

Default (VHS-C SP), Hi8 '98, MiniDV '04 (3D noise reduction, no setup), Low light gain (low-lux slow shutter, noise
reduction), Clean (no camcorder picture), VHS '96 (heavier glare, warm-green cast left in), Found footage (VHS-C EP,
worn tape, wide converter), Reference Match (536 lines line-doubled, DV, the footage's highlight colour 0.6 and tint -20, see `ReferenceMatch.md`).

**Highlight colour** (CAMERA PROCESSING, `highlightColour`): highlight chroma suppression. From this level of the
recorded signal the colour fades out to none at the white clip, so clipped lights are white and bright walls go pale.
0.8 by default; the reference footage measures about 0.6 (its walls at 80 % have half their colour). Saved settings from
the Reference Match preset took 0.6 once (2026-10-27). It travels in `_CamLowLight.z`, which the head pass sets per
pass (`CamcorderPipeline.cs`): keep z when changing that line.

Saves from before this rebuild (no `lookVersion`) are converted on load: the picture settings take the new values of
the preset that was in use; the field of view, frame shape, OSD, autofocus and auto exposure toggles, handheld motion,
recording frame rate, line doubling, bottom line, recording lines and audio are kept. The old values are kept in
PlayerPrefs (`Backrooms.CamcorderSettings.v1.BeforeLook2`). Saved presets in the old units are ignored.

## Calibration

* `CamcorderAutoExposure.openExposure` 3.2: a lit office room sits about 2–2.6 stops closed (f/3.5–4.5), leaving two
  stops of iris and three of gain for dark rooms.
* Target level 0.18 (about 45 IRE average); compensation 0.85 around that nominal exposure.
* Noise: read 0.002, shot 1.6e-5 (linear): about 0.3–0.9 % of white at 0 dB, about 2–4 % at +18 dB.
* Smear: 0.003 × the column mean (visible only with a light far over white and the gain up).

## Checking it

`Backrooms.DebugTools.CamcorderProbe.Run(folder, preset, extraPresets)` (Play Mode) shoots the finished picture: a lit
room two ways, looking up at the tubes, a 240°/s pan, the lights going out (0.6 s and 7 s later, and the other way),
then one shot per extra preset, logging iris, gain, exposure, meter, white balance, f-number, focus and whether depth
of field ran. With `measureCost = true` it also times the GPU frame with the camcorder picture on and off, and with
the 4:3 viewport and full width. `CamcorderTests` (EditMode) checks the transfer curve and knee, the formats, the
presets, the migration, the white balance limits and that the shader compiles without keyword variants.

## Focus breathing and busier compression (2026-10-28)

- Focus breathing: a video zoom lens's angle of view tightens about 3.5 % as it focuses from infinity to half a metre, so a
  refocus on the hand creeps the frame in and autofocus hunting in the dark pulses it (`CamcorderRig`, field of view x
  (1 - 0.035 x breathing x focus in diopters / 2)). Only with autofocus on. Camera menu > Focus breathing (0-3, default 1).
- DV blocks grow with the noise: the codec's bit budget per frame is fixed, so with the gain up in low light every block is
  coarser (x 1 to 2.2 with the gain). Not with motion: that is MPEG (DVD and HDV camcorders, later); DV is intra-frame.
- Not added: rolling shutter. 1990s camcorders used CCD sensors, which expose the whole frame at once (CMOS phones skew).

## Recorded walk (2026-10-28)

Walking moves the camera the way a real camera carried in the hand did: a phone held like the camcorder (landscape, eye
level, weighted to about 1.2 kg) and recorded with its gyroscope and accelerometer (Docs/CameraMotionCapture.md).
`Tools/CameraMotion/build_camera_motion.py` turns the takes into `Resources/CameraMotion/Walk.json`:

- Only straight walking is kept: the phone held (gravity along its long edge), stepping (the up-down rhythm), not turning
  (the turn-around and a second either side cut out), less the first and last steps of each stretch.
- The look direction is the player's mouse, so it is filtered out: rotation below 0.4 Hz and movement below 0.5 Hz go.
  What is left is the hand's own motion: the sway, the bob of each step and the tremor (up to 15 Hz). Movement is the
  accelerometer, turned into the walking direction's frame with the phone's orientation and integrated twice in the
  frequency domain.
- The heel strikes are the upward push as a foot takes the weight; which foot is the side the body sways to (it moves
  over the standing leg), decided over each unbroken run of steps since the feet alternate. Cut at the left heel strikes
  into strides, 64 samples each; where the strides do not follow one another (between stretches, and at the loop's end)
  the end of one is eased onto the start of the next.
- Result (two takes, 98 steps a minute): 34 strides, 42 s. The camera is lowest as each foot lands and highest over the
  standing leg, about 5-7 cm up and down; it sways toward the standing foot (about 1.2 cm each way) and turns about 2
  degrees left and right with the stride; rotation rms pitch 0.75, yaw 1.6, roll 1.1 degrees. (The generated walk
  bobbed about 1.2 cm with 0.45 degrees of sway.)

Playback (`CameraMotionClip`, `HandheldCameraShake`): the recording runs by stride, not by time. Each time a foot lands
(`FirstPersonBody.FootLanded`) it is drawn toward that foot's landing in the recording (the left at the start of a stride,
the right about half way), eased in over about a tenth of a second, never backward and at most three times the pace; the
pace is the time between the last two steps. It takes the place of the generated walking bob and sway, and of the
roughness's walking part (the recording has the hand's own shake in it); it is weighted by walking speed like the bob
was, and running, standing and breathing are unchanged. Shake intensity and image stabilisation apply to it.
Camera menu > CAMERA SHAKE > Real handheld recordings (on by default; off is the generated bob; it was "Real walking
recording" until 2026-11-03). `CameraMotionTests` (EditMode).

## Standing, side-steps, turning and the walk jitter (2026-11-03)

Six more takes (`Tools/CameraMotion/Recordings`: `idle_stand_1`, `walk_normal_3`, `look_slow_1`, `look_fast_1`,
`strafe_left_1`, `strafe_right_1`; same phone, weight and left hand). `build_camera_motion.py` now builds every clip from
the folder by the takes' names (`python Tools/CameraMotion/build_camera_motion.py Tools/CameraMotion/Recordings --out-dir
Assets/Backrooms/Resources/CameraMotion --report`):

- **Walk**: the third take adds 18 strides: 52 strides, 64 s (it repeats less often).
- **Side-steps** (`StrafeLeft`, `StrafeRight`): cut into strides like the walk (their steps are gentler, so the step
  detector's threshold is lower for them): 11 and 12 strides, about 15 s each, 85-97 steps a minute. They sway more than
  walking forward: rotation rms yaw 1.6-2.5 degrees, movement 2.3-2.5 cm sideways. Played by how much of the movement is
  sideways (eased over a quarter second), at their own recorded pace times the speed, not in step with the feet: the body
  side-steps about four times a second at walking speed (FirstPersonBody), and the recording sped up that far read as a
  shudder.
- **Standing** (`Idle`): 52 s of standing still, a seamless loop played by seconds: the hand's slow drift, its
  corrections and its tremor, rotation from 0.05 Hz (rms about 0.6 degrees each axis, three to four times the old
  generated idle sway) and movement from 0.25 Hz (rms about half a centimetre). It takes the generated idle wander's place
  and gives way to the walk as you set off. Breathing stays generated: its sounds keep time with the camera's rise and
  fall (BodyPresence), and the recording has no one clear breathing rhythm to take out.
- **Turning** (`Look`): the mouse does the turn, so what is kept is the tremor that comes with it (3-15 Hz): 0.25 degrees
  rms turning at 4 degrees a second, 0.33 at 18, 0.48 at 69, 0.98 at 171, 1.08 at 270 (the three axes together), and its
  texture (the tremor over its own envelope: one degree rms, 62 s). Turning adds the part above standing still's, at
  Turn wobble 0.5 (its default) as recorded. Not taken from these takes: the overshoot when a turn stops (it cannot be told
  apart from where the hand meant to stop), and the roll with a turn (it grew the longer after the turn it was looked for,
  so it follows where the camera has turned to rather than the turn, and the fast whips were tipped on purpose); the
  generated overshoot and lean stay.
- **Walk jitter** (Camera menu > CAMERA SHAKE > Walk jitter, `CamcorderSettings.walkJitter`, 1 as recorded): every stride
  of the walk and the side-steps is the rhythm they all share (their average, sample by sample) plus how this one differs
  from it; the slider scales only the second part. At 0 each step is the same (the bob and sway kept, the irregular shake
  gone), at 2 double. No new recording is needed to change it.

Checked with `DebugTools.CameraMotionProbe` in World 3 (stand, walk, side-step each way, slow turns, whips; recordings on,
walk jitter 0, recordings off): camera rotation rms, at the owner's Walking shake of 0.47, standing 0.2-0.3 degrees (0.1
generated), walking 0.3-0.6, side-steps 0.4-0.95 (0.2-0.3 at walk jitter 0), turning 0.1-0.5. `CameraMotionTests`.
