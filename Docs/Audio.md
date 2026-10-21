# Audio: spatial building sound and the camcorder recording chain

All sound is generated in code (`Scripts/Runtime/Audio/ProceduralSounds.cs`); there are no imported or licensed
audio files.

## Signal flow

```
per-sound settings (SoundCatalog, F5 > SOUND)
  -> environment: distance along the sound path, walls, doorways, room reverb (BuildingAudio, AcousticField)
  -> camcorder chain on the AudioListener (CamcorderAudioChain, F5 > AUDIO)
  -> output volume
```

| Piece | File | Job |
| --- | --- | --- |
| Sound catalog | `Audio/SoundCatalog.cs` | Every sound type (stable id, name, category, description, defaults) and the saved global overrides. |
| Clips | `Audio/ProceduralSounds.cs`, `Audio/SoundBank.cs` | Builds all clips once at load and holds each sound's base level. |
| Building audio | `Audio/BuildingAudio.cs` | Fixture hum voice pool, faulty buzz, strike-on and tick one-shots, ambience beds, reverb. |
| Acoustic model | `Audio/AcousticField.cs` | Wall grid around the listener: line of sight, walking paths, doorway direction, visible area. |
| Player | `Audio/PlayerAudio.cs` | Carpet footsteps and the handling signal. |
| Camcorder chain | `Audio/CamcorderAudioChain.cs` | Width, hiss, handling, coloration, AGC, compression, saturation, output (custom DSP on the listener). |
| Tests | `Debug/AudioProbe.cs` | Play-mode measurements (see below). |

There was no AudioMixer in the project, and Unity has no supported API to author mixer effect chains from
code, so the chain uses built-in filters where they exist (`AudioReverbFilter` on the listener,
`AudioLowPassFilter` per voice) and one custom `OnAudioFilterRead` stage for the camcorder. It is allocation-free
and only reads plain float fields written by the main thread.

## Fixture hum: a bounded pool

- Only powered tubes are candidates (unpowered fixtures never create a light source, so they can never hum).
- Every 0.1 s, the powered tubes within the candidate radius (default 20 m) are scored by what the listener
  would actually hear: distance gain along the sound's path x wall / doorway loss x the tube's current power x
  its seeded level. Visibility on screen plays no part, so turning around never changes anything.
- The strongest fixtures get one of the hum voices (default budget 12, plus 4 spare voices so crossfades never
  steal). Fixtures that already have a voice are favoured when ranking (x1.33, and x1.7 during their first
  1.5 s), so ranks shuffling as you walk don't swap voices, but a clearly louder fixture always takes over.
  New voices fade in over 0.8 s, retired ones out over 1.2 s.
- Voices sit on their fixture's direction. They never slide with the player or jump to the nearest fixture.
  Distance and wall loss are baked into one smoothed level per voice (at most 40 dB/s), and the direction is
  smoothed over ~0.25 s, so a change of route (around a corner versus through a wall) can't make it jump.
- Each fixture has a seeded character that is the same every time you hear it: one of four hum timbres
  (different harmonic mixes, lamination buzz and arc sizzle, loop lengths 7-13 s), about +-3 dB of level,
  +-0.15 % detune (slow beating between neighbours) and a random loop phase. No random pitch changes.
- Everything the pool does not voice is summed (power sum) into a quiet 2D "distant building hum" bed, and
  when the total hum is loud (many fixtures) all hum is gently compressed, so big lit halls don't get loud.
- Hum level follows `FixtureLightManager.PowerLevel` every frame, unsmoothed, so the sound strikes, stutters
  and powers down exactly with the light.

## Events driven by the lights

- **Strike-on**: when a fixture in a streaming-in area crosses from dark to lit, a starter click, ballast
  thunk and swell plays at that fixture. Streamed areas start 29 m+ away, past the strike's audible distance,
  so streaming never produces a nearby cue.
- **Faulty tubes**: the two nearest faulty tubes get a buzz voice that opens while the tube is actually
  dipping and closes a quarter second after; each rising edge of the stutter fires a tiny arc tick (rate
  limited). The F5 "Crackle" setting only changes level, never when a stutter happens.

## Walls, doorways and rooms

`AcousticField` rebuilds a 37 x 37 cell (44 m) window of the generator's own wall grid around the listener
whenever you enter a new cell (and every 0.5 s), in about 0.5 ms:

- A straight line from the listener to a source counts the solid walls it crosses.
- An 8-way shortest walking path through open edges and doorways gives the distance sound travels around
  walls, and the visible point: the last point on that path you can see (usually the doorway).
- Sound with a clear line is direct. Otherwise the louder of two routes wins: around the walls (rendered from
  the doorway's direction at the walking distance, slightly duller the more it bends) or through the walls
  (about -18 dB per wall and heavily low-passed). Position, level and filter changes are smoothed over
  ~0.25 s.
- The cells you can see give the visible floor area, average ceiling height and the share with hard plaster
  ceilings. Small carpeted offices under acoustic drop ceilings are nearly dry (~0.35 s); tall plaster halls
  ring (up to ~2 s). Reverb parameters glide over ~0.6 s, so doorways blend the two.
- Ventilation is louder and brighter in big or tall spaces and drops in dark areas; room tone is constant.

**Limits (approximation):** one path per source (the shortest), not a sum over several doorways; only within
the window and loaded chunks; furniture, pillars and half walls are ignored; the reverb is a single listener-
side room, not per-source. Wall colliders are not used for acoustics at all.

**Portals later:** a portal will be one more edge in the path graph whose far side is elsewhere. The search
crosses it, the visible point becomes the portal mouth, and the remote fixture's sound appears at the entrance
with no change to the emitters.

## Camcorder chain (F5 > AUDIO)

Order: stereo width (mid/side) -> mic hiss and handling noise -> coloration (high-pass up to 210 Hz,
low-pass down to 6.2 kHz) -> AGC -> compression -> saturation -> processed/unprocessed crossfade -> output volume.

- **AGC**: stereo-linked envelope, target -30 dBFS, gain bounded to +15 / -12 dB (scaled by strength). It drops
  in ~0.12 s for a loud sound and recovers over ~3.5 s, rate-limited to 4 dB/s, so it rises gradually in quiet
  rooms (bringing up building sound and hiss) and never runs away in silence.
- **Compression**: linked, above -20 dBFS, ratio up to 4:1. **Saturation**: tanh with unity small-signal gain.
- **Compare**: "Compare: hear unprocessed" crossfades to the dry signal without touching settings and turns off
  when the panel closes. "Camcorder processing" off gives the clean world sound (room acoustics stay on).
- Audio gain is completely separate from the visual auto-exposure.
- Presets: the Audio tab has its own row of the five camcorder presets that changes only audio; the Visual
  tab's presets keep your audio. Clean is a neutral reference (no coloration, AGC, hiss or handling).
- Settings live in `CamcorderSettings` (same PlayerPrefs entry as the look; older saves load with audio
  defaults). Developer values (voice budget, distances, fades, occlusion, reverb, AGC internals) are saved
  separately in `Backrooms.AudioDev.v1`.

## Sound catalog (F5 > SOUND)

Every sound registers in `SoundCatalog.RegisterBuiltIns` with a stable id. The tab lists all of them, loaded
or not, searchable by name or category. Per sound: volume, mute, pitch (where it makes sense), and for
positional sounds min / max distance, attenuation and spatial blend, plus sound-specific values (tone
variation, crackle, airflow drift, step variation). Preview / Stop plays a sound 2.5 m in front of you;
selecting never previews. Reset this sound and Reset ALL sounds are separate buttons.

Edits apply globally and live (emitters read the catalog every frame and rebuild their rolloff curve when it
changes), and are saved as one global profile (`Backrooms.SoundProfile.v1`) when the panel closes. Overrides
store only edited fields, are keyed by id, and are kept even for ids that aren't registered, so adding sounds
never loses tuning. Asking for an unregistered id logs an error, so new sounds must be added to the catalog.

Current sounds: Fluorescent hum, Fluorescent strike-on, Faulty tube buzz, Distant building hum, Ventilation,
Room tone, Footsteps (carpet).

## Testing

`AudioProbe` (add it in play mode, `Run("all")`) measures what Unity will actually play: each looping source's
volume times its rolloff at its rendered position, every frame. Results are in the milestone notes below.
It can't judge how anything sounds; listening checks are listed there too.
