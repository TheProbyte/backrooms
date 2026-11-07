# Overflow orientation video

The training-room tape, made entirely in code: a flat 1970s corporate film in isometric rooms, faceless pictogram crew,
Boxley the box, a worn VHS finish. 4:3, drawn on a 720 × 540 canvas and captured at 960 × 720. Script: the plan doc's "Orientation script" tab. Style: the style board's isometric row.

| File | What it does |
| --- | --- |
| `video.html`, `anim.js` | Draws any frame: `renderFrame(n)` at 24 fps. `SCENES` holds each scene's length, its line and its drawing. |
| `capture.mjs` | Runs headless Edge over the DevTools protocol and saves every frame as a PNG (about 0.1 s a frame). |
| `audio.py` | The soundtrack: 70s library-music bed (112 BPM, F major), two jingles, about forty sound effects, tape wow and hiss. |
| `build.py` | Adds the VHS finish (colour bleed, noise, a soft picture) and encodes MP4, and WebM for Unity with `--webm`. |

## Making it

```bash
bash render.sh <framesDir>                # every frame, in 60-frame chunks with a fresh Edge each; resumes; add 1 for captions
python audio.py <soundtrack.wav>
python build.py <framesDir> <soundtrack.wav> <outName> --phone   # drafts: the small review copy only
python build.py <framesDir> <soundtrack.wav> <outName> --webm    # the final cut: full MP4 and the WebM for Unity
```

Needs Node 18+, Microsoft Edge, Python with numpy, scipy and imageio-ffmpeg (which brings its own ffmpeg). Nothing else.

## When the voiceover arrives

1. Put the line files (VO01 to VO11, VO99) somewhere and measure their lengths.
2. Set each scene's `dur` in `anim.js` to fit its line (plus the beats), and move the timed moments inside a scene if a
   word should land on an action. Scene starts are added up automatically.
3. Move the matching times in `audio.py` (each block is labelled with its scene and times), place the voice files at
   their scene starts, and lower the music under them (about 8 dB).
4. Capture, build.

## How it moves

Limited animation in the UPA and 1970s educational-film tradition: strong held poses, cut-out pieces turning about their
joints, the characters on twos (12 poses a second) while camera moves and fast actions run on ones, snappy easing with
overshoot, hard cuts and an iris.
