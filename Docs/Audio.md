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

**Shut doors** (`DoorEdges`): a shut door's edge is closed to the walking paths, and the straight line through it is
counted apart from walls: about -9 dB and a 1.6 kHz low-pass each (a wall: -18 dB, 700 Hz).

**Limits (approximation):** one path per source (the shortest), not a sum over several doorways; only within
the window and loaded chunks; furniture, pillars and half walls are ignored; the reverb is a single listener-
side room, not per-source. Wall colliders are not used for acoustics at all.

**Portals later:** a portal will be one more edge in the path graph whose far side is elsewhere. The search
crosses it, the visible point becomes the portal mouth, and the remote fixture's sound appears at the entrance
with no change to the emitters.

## Steam Audio and the world's walls (2026-10-26)

`Audio/SteamAudioWorld.cs` (added by the streamer in play mode) gives Steam Audio the world: each loaded chunk's wall,
post, floor and ceiling collider boxes become one static mesh with acoustic materials (painted drywall walls, carpet
floors, mineral-fibre ceilings), and every shut door (`DoorEdges`) a wooden slab in its doorway. Typical load: about
140 meshes, 34k triangles. Sounds of things in the world (everything made with `ItemModels.Source`: items, doors, light
switches, cords, the settling sounds below) use Steam Audio's direct path (`SteamAudioWorld.Attach`): HRTF, volumetric
occlusion by those meshes and frequency-dependent transmission through them, so a door shutting in the next room is
muffled by the wall. The building's own hum, buzz, ventilation and room tone keep the walking-path model below (which
now also knows shut doors), so nothing is muffled twice. No baked data and no Steam Audio reflections: the reverb is
still the listener-side room reverb.

## Broken walls (2026-10-27)
A wall broken with the sledgehammer (BreakingWalls.md) lets sound through its holes: the building's sound paths
(`AcousticField`) go through a wall open by 12 % or more, a smaller hole muffles like a shut door, and Steam Audio gets
what is left of its boards as a mesh of its own (the chunk's mesh is rebuilt without it). Its sounds are procedural
(`ProceduralSounds.Demolition.cs`): the swing, drywall thump and crunch, crumbling, a steel stud's bang and tearing, a
wood stud's thock and crack, a dead thud into a post, clanks, a batt's whump, bits landing (at most three a tenth of a
second). They play through a small pool of plain 3D voices (`WallBreaker.Play`), not Steam Audio ones: eight
spatialised voices still playing as the editor left Play Mode crashed Steam Audio's mixer thread once.

## Footsteps, the body and the building at rest (2026-10-26)

- **Footsteps** come from the feet: with the legs shown, each step plays when and where a foot comes down
  (`FirstPersonBody.FootLanded`), on what is under it (`Audio/Surfaces.cs`: carpet, wood or laminate from the room's
  floor finish, the Poolrooms' tile, water, sheet metal in a crawl duct, plywood above the ceilings). Hard floors have
  their own synthesized steps (`ProceduralSounds.Body.cs`). Stopping from a walk scuffs the shoe. Crawling steps by
  distance (hands and knees on metal in the ducts).
- **Body** (`Interaction/BodyPresence.cs`): breaths close to the mic once you are out of breath, clothes rustling with
  turns, crouching and walking, a sleeve rubbing along a wall. Breaths (2026-10-27, `ProceduralSounds.Breath`): air
  through a half-open mouth, soft pink noise shaped by the mouth's resonances (out about 1 kHz, a warm "haaa" with a
  low puff on the microphone; in about 1.9 kHz, quieter), a little flutter, in swelling and out trailing away; heard
  from exertion 0.3, at most 0.16 (was white noise through 1.5 and 2.8 kHz filters, a hiss, up to 0.5).
- **Mic**: grabbing, picking up, throwing and letting go knock the camcorder (handling thumps); running or whipping the
  camera round puts wind on the mic (low buffeting in gusts, in `CamcorderAudioChain`).
- **The building at rest** (`Audio/BuildingSettling.cs`): every 10-35 s a ceiling tile shifts somewhere near you (over a
  real drop ceiling, 2.5-11 m away) or a sofa, chair or table nearby settles with a knock. The building's supply sags a
  few percent now and then for a second or two (`FixtureLightManager.Mains`): the nearby lights dim together and the hum
  drops and slurs.

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

## Milestone test results (2026-10-21)

Measured in play mode with `AudioProbe` (numbers, not listening):

- **Walking toward, under and past a troffer**: its hum rose smoothly from -52 dB at 5 m to -35 dB beneath it
  and fell to -55 dB at 9 m past. Largest one-frame level change 1.2 dB, no level steps over 3 dB.
- **Turning 720 degrees in place**: total hum level unchanged (0.0151 throughout), no voices started or stopped.
- **Walking through walls and rooms (noclip, 30 m)**: largest one-frame change 2.2 dB, no steps over 3 dB, no
  cut-offs; occluded fixtures resolve to the doorway they are heard through.
- **Faulty tube**: over 6 stutters the buzz voice was open during dips and released afterwards, with ticks on
  the rising edges. Hum on faulty tubes follows the stutter (the only intended level jumps).
- **Streaming 80 m**: voices swap about once per metre at walking pace, always by crossfade; at most 20 sources
  playing, hum pool fixed at budget + 4, no orphaned voices after unloading.
- **Auto gain**: in a quiet hall it sits at its bounded maximum; a loud sound 1 m away dropped it in under
  0.5 s, then it recovered at about 4 dB/s over ~8 s.
- **F5**: raising Fluorescent hum to 2x doubled every active hum voice; Compare crossfaded to unprocessed;
  Preview / Stop worked; saved overrides loaded on the next run. No console errors.

Not established by these tests, needs listening: whether the hum, buzz and ventilation timbres sound
convincing, the reverb character of small offices versus tall halls, the amount of hiss and handling noise,
and whether the AGC breathing is pleasant. A more convincing fluorescent or HVAC sound may eventually need
recorded (licensed) material; everything here is synthesised.

## Impacts and scraping (2026-10-28)

`Audio/ImpactAudio.cs`, `Audio/ProceduralSounds.Impacts.cs`. Every loose item (WorldItem) and movable piece of furniture
(MovableProp) gets an `ImpactSounder`; the sledgehammer, the rubber chicken and wall debris keep their own sounds.

- A contact's loudness is its energy: the lighter body's mass and the speed along the contact normal (0.01 J faint, a can
  dropped from the hand clear, furniture slammed the loudest), mapped logarithmically.
- Two layers, as physically based game audio does: the struck thing's own ring (modal synthesis: damped partials per
  material, pitched by size: can, plastic, chalk, tape, lamp metal, wood, cardboard, upholstery, office chair) and the
  answer of what it hit (carpet, hard floor, concrete, tile, drywall, metal; or the other thing's own ring). Carpet damps
  the ring. Surfaces come from `Surfaces.At` (floors) and the collider (doors wood, racks and ducts metal, walls drywall).
- Sliding along the floor scrapes: dragged over carpet, scraped over a hard floor (stick-slip), rolling on casters; level
  and pitch follow the slip speed; the three nearest slipping things get a voice.
- Through Steam Audio (ItemModels.Source); 12 one-shot voices, at most 4 impacts a frame, nothing beyond 28 m, nothing in
  the first 1.5 s after a thing appears (settling as the world loads). Sound settings: Physics > Impacts, Scraping.

## The tension director (2026-10-28)

`Atmosphere/TensionDirector.cs`, after Left 4 Dead's AI Director and Alien: Isolation's. It estimates how tense the player
is (rises with what they hear, nearer counts more, in the dark and while running; falls in calm) and cycles Relax (nothing
of its own; 70-160 s) -> Build-up (50-110 s: rarer and fainter at first, then nearer) -> Peak (one set piece) -> Fade (the
ambience drops away, `BuildingAudio.Duck`) -> Relax. Already on edge: it backs off or ends the build-up early.

Events, never in plain sight: an open door out of sight swinging shut, the lights of a nearby room going out
(`LightZones`: saved, it stays dark until you find the switch), your room's lights blinking or stuttering, the power
sagging (`FixtureLightManager.DirectorSag`). (2026-10-31: the knocks in walls, things in ducts and footsteps are gone, the
owner did not like them; the director makes no sound of its own now.) Off in the Poolrooms, the menus and noclip.
Strength: audio menu > ATMOSPHERE > Tension director (0 off, 1 normal, 2 intense; saved).
`TensionDirector.Instance.Force("shadow" | "bold" | "leave" | "dark" | "flicker" | "door" | "sag" | "peak")` for testing;
`Status` for a one-line state (with the shadow's).

## The shadow (2026-10-31)

`Atmosphere/ShadowStalker.cs`. The owner: "like Lethal Company's Bracken", "maybe not a physical thing; in the shadows,
when it nears, lights turn off in its path". The director is its macro brain (Alien: Isolation's split): it sends it 12 s
into a build-up, makes it **bold** at the peak, calls it off in the fade. The shadow itself only knows where you are and
where you look.
- **Lights**: the room it is in and the room it is about to step into go dark, tube by tube with a stutter
  (`LightZones.Drain`: not a switch, not saved); a room it has left strikes again 2.5-4 s later. It makes no sound: you
  hear the building's hum cut out and tubes strike behind it.
- **Seen only in the dark**: a tall (2 m), thin, black figure, long arms, a slight hunch, facing you; drawn only where its
  room has gone dark and more than 1.3 m off.
- **Stalk**: cell by cell through the building (through doors: it is not solid), toward a spot 5.5 m behind you (1.1 m
  when bold); it never steps into view: its route goes round what you can see, and if there is none it waits behind the
  corner and leans out now and then. Not bold, it keeps 4.5 m off.
- **Looked at** (in view, in its dark, within 22 m): it freezes 0.4 s, then flees out of sight. Each look adds anger
  (0.3 within 6 m, else 0.15), staring adds 0.3/s close or 0.1/s far; it ebbs 0.03/s. Full: **angry**, it comes straight
  at you at 5.6 m/s for 2.5-5 s, lights dying ahead of it.
- **Caught** (bold and right behind you, unseen, or angry and on you): the tape breaks up and cuts (the last frame held,
  crackle), and the recording picks up 20-45 m away in a room whose lights are off (saved: find its switch). The director
  goes to its fade.
- Near it the picture breaks up (`CamcorderRig.Interference`, through the noclip glitch: tracking, tearing, sound
  dropouts) and the power sags. Route planning: about 1.5 ms in the editor, every 0.8 s.
