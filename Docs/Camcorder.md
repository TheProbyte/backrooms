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
| White balance (static, after the tonemap) | Partly | Auto white balance: slow, limited, along warm/cool (strength 0.3 keeps most of the yellow), plus your warm/tint shift, in linear light |
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
worn tape, wide converter), Reference Match (536 lines line-doubled, DV, see `ReferenceMatch.md`).

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
