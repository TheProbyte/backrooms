# Project brief

This is the owner's brief, copied word for word (2026-10-20). Headings were added for navigation only; the text is
unchanged. Part 1 is the overall vision and future requirements. Part 2 is the audio milestone that was the
immediate task when the brief was written.

---

# Part 1: Project brief: physical backrooms exploration and survival

Work in my existing Unity 6 project. Inspect the current implementation before making changes and preserve working systems.

This game is separate from CRAFT 1979. It is a realistic, camcorder-view backrooms game with procedural environments, impossible architecture, physical interaction, and player-made refuges.

## Core experience

The player begins recording in the normal world, performs a few ordinary actions, then accidentally falls into the backrooms. The opening is a future feature.

Initially, the character wants to escape. Over time, exploration and making a home may become equally important. An eventual exit and ending are undecided.

The central loop is:

Explore → discover something useful or interesting → work out how to bring it home → improve a refuge → venture farther.

Exploration should offer beautiful scenes, unsettling spaces, practical discoveries, and opportunities to understand the building. Procedural variety needs distinct room identities and meaningful connections.

There are no other living people planned, and no monsters for the current scope. Sparse evidence of previous occupants is welcome.

Avoid hunger, routinely depleted flashlight/camcorder batteries, and repetitive maintenance meters. Survival should come from navigation, preparation, environmental obstacles, transporting equipment, and establishing trustworthy places.

## Existing implementation, as reported

Verify these in the project rather than assuming exact implementation details:

- Procedural rooms, including tall halls and some offices/lounges at 3.9–5.4 m, with plaster ceilings and round dome fixtures.
- Soffits at height changes; posts and colliders extend fully to prevent gaps into the void.
- Wallpaper or painted plaster depending on room.
- Fixtures within 14 m remain lit regardless of visibility.
- Up to 26 nearest lights cast shadows, with hysteresis and fading shadow strength.
- Newly streamed areas have fluorescent strike-on behavior; unloading areas stutter off.
- Approximately 2.5% of tubes are faulty and occasionally stutter.
- Blender props generated headlessly by: blender -b --factory-startup --python Tools/Blender/build_props.py -- Assets/Backrooms/Models
- Existing props include sofas, an armchair, floor lamp, office chair, conference table modules, and wall clock, approximately 200–1800 triangles each, using shared meshes and prefabs.
- Backrooms > Build Office Room Scene rebuilds the handmade 7.2 × 6 m milestone room with baked bounce lighting.
- Textures are procedural and seeded through BackroomsTextureGenerator.
- Audio is currently synthesized through ProceduralAmbience.

## Existing camcorder system

The player effectively is the camcorder: slow grounded walking, running, and handheld camera motion.

The F5 panel edits CamcorderSettings, saved to PlayerPrefs and applied through CamcorderRig, post-processing, auto-iris, handheld motion, and Shaders/CamcorderVideo.shader after post-processing in PC_Renderer.

Existing presets:

- Default
- Hi8 ’98
- MiniDV ’04
- Low light gain
- Clean

Existing controls cover lens, focus, exposure, sensor noise, image processing, colour, handheld motion, and format/OSD.

## World and atmosphere

Eventually include varied environments: offices, lounges, maintenance areas, malls, poolrooms, and stranger places such as a field beneath a suspended office ceiling, with air conditioning moving the grass.

Spaces should feel copied or transferred from an ordinary world, with people absent. They are not necessarily spotless or uniformly ruined. Some retain normal power; others are unpowered.

New environments should offer distinct spatial experiences, useful discoveries, or connections. Not every beautiful discovery needs a resource reward or explanation.

## Physical interaction and inventory

The world should feel tangible.

### Large objects

Clicking near a reachable part of a couch or cabinet selects a nearby grip. The player moves a short, unobstructed distance into position and enters a push/pull interaction.

Movement applies force at that grip:

- Push, pull, and pivot.
- Change grip to maneuver around corners.
- Weight, friction, scraping sounds, and restrained camera movement communicate effort.
- Release immediately to regain normal movement.
- Use assisted physics where needed to prevent jitter, snagging, or objects launching.

The free hand handles objects while the other holds the camcorder. Some furniture is too heavy to move.

### Smaller objects

- Hand-sized objects occupy the free hand and can be deliberately placed.
- Keys 1–4 represent two front and two back pockets.
- Each pocket holds a small item or sensible bundle.
- A later backpack can be physically removed, placed down, and opened to access simple internal storage.
- Containers and trolleys eventually make transporting supplies easier.

These are design directions, not instructions to implement now.

## Refuges and building

Players choose locations because they are beautiful, useful, fun, or personally appealing. Do not prescribe a single ideal base location.

Start with moving existing furniture and blocking entrances. Later, a hammer and pack of nails could fasten a pallet or board into place. Full wall construction is undecided.

Properly secured objects and repaired power should remain dependable. Avoid routine loosening, breakdown, or upkeep chores.

Bases, stored belongings, and player markings persist. Do not randomly delete bases.

Comfort comes through lighting, furnishing, music, decorating, map-making, organizing equipment, and sitting down. No comfort meter is needed.

Sleep is deferred. Voluntary sitting/resting provides a pause without fatigue mechanics. Saving should not require returning home.

Death and recovery remain undecided; prototype exploration before designing punishment.

## Power, light, and darkness

Power is primarily about discovering, repairing, and distributing existing electricity rather than repeatedly refueling generators.

Eventually support physical extension cords that pay out behind the player, drag along the floor, and bend around corners. Established cable routes should be reasonably stable and manageable. Portal-crossing cables are a later technical challenge.

Light provides visibility, navigation landmarks, atmosphere, and stability.

Unattended dark areas can occasionally undergo persistent disturbances:

- Furniture moves, leaving scrape marks.
- A cabinet the player cannot move has shifted.
- A displaced ceiling panel leaves debris.
- Rare distant scraping can correspond to an actual change.

Most visits should be uneventful. Avoid announcing changes with scare music.

Dependable illumination prevents these disturbances, with a grace period for momentary flickering. Respect the player’s investment: established refuges, supplies, markings, and secured equipment should remain trustworthy.

The exact relationship between barricades and supernatural safety still needs refinement.

## Navigation and impossible architecture

Use real rendered portals for openings that visibly reveal physically impossible depth.

Example: a crawlspace in a wall extends farther than the wall could contain, despite both sides being accessible ordinary rooms.

Portals should support correct perspective, clipping, bidirectional viewing and traversal, and preservation of movement and orientation. Avoid glowing borders or overt portal effects.

Also eventually include wall anomalies: apparently solid wall portions that can be walked through, perhaps identified by old tape outlining a doorway. Crossing can briefly disrupt the camera image.

Anomalies are rare:

- Some exist during initial generation.
- Some may appear later in unattended darkness.
- Discovered connections generally remain stable.
- They do not conveniently connect every player base.
- Players may choose to establish refuges near useful connections.
- Deliberate player-controlled linking is undecided.

Routes can differ in usefulness: a crawlspace works for a person but not a trolley or couch.

Navigation tools may include finite but generous chalk, red spray paint, blue tape, and eventually a physical map/notebook. Maps should accommodate connections between places rather than assume geometrically consistent floor plans.

---

# Part 2: Audio milestone

## Immediate task: build the spatial audio and camcorder recording system

Implement only this milestone now. The preceding brief explains future requirements and tone.

Make the building acoustically convincing while keeping source counts and processing bounded in an endlessly streamed world.

### 1. Inspect and integrate

Inspect:

- ProceduralAmbience
- Room generation and streaming lifecycle
- Fixture placement, power state, and flicker events
- Player camera and AudioListener
- Existing AudioMixer setup
- F5 settings, presets, and persistence

Reuse and extend working systems. Avoid duplicate listeners, doubled ambience, and competing audio managers.

### 2. Fluorescent sound with a bounded source pool

Do not run an audible looping AudioSource on every fluorescent in the world.

Build a configurable pool of nearby audible fixture emitters. Select candidates using distance, room connectivity, power, and likely audibility—not whether the fixture is on screen.

The listener follows the player. Fixture sound should remain anchored to fixtures. Avoid a single hum that slides along with the player or jumps abruptly to whichever fixture is currently closest.

Use:

- A limited set of nearby fixture sources for positional detail.
- Hysteresis and smooth crossfades when assigning or retiring emitters.
- Sensible attenuation and overlapping coverage.
- Seeded variation in level, tonal character, and phase to avoid identical synchronized loops.
- A subtle room-level ambience layer where helpful for distant aggregate building sound.

Do not let fixture density make large rooms excessively loud. Keep overall ambience balanced.

Expose source budget and relevant distances as developer settings. Select reasonable initial values based on the existing project and profile them.

### 3. Sound connected to the actual building

Provide distinct, restrained layers:

- Fluorescent electrical hum/buzz.
- Ventilation or air-conditioning ambience.
- Quiet room tone.
- Fixture strike-on and faulty-tube sounds driven by actual fixture state/events.

Unpowered fixtures should stop humming smoothly. A distant streaming event should not create an obvious nearby sound cue.

Prevent repetitive short loops and conspicuous randomized pitch changes.

Keep audio generated in the existing procedural approach where practical. If more convincing sound requires assets later, report the limitation rather than adding unlicensed material.

### 4. Spatial acoustics

Make movement through a room and toward fixtures clearly audible in stereo.

Add a practical first version of:

- Wall obstruction/muffling with smoothed transitions.
- Different acoustic character for smaller carpeted offices versus tall plaster halls.
- Smooth room/reverb transitions.
- Sensible doorway audibility.

Use available room connectivity information if suitable. If full doorway propagation is beyond this milestone, implement a bounded approximation and explain its limits. Do not claim ordinary wall colliders automatically provide acoustic occlusion.

Design the system so future portal-aware sound can be added: destination audio will eventually need to appear through portal entrances rather than originate from its remote scene coordinates. Do not build the full portal system now.

### 5. Camcorder recording chain

Route environmental sound through a shared camcorder audio path, preserving useful directional cues.

Provide adjustable:

- Microphone frequency coloration: bass reduction and softened highs.
- Automatic gain control with smooth attack/release and bounded gain.
- Gentle compression and restrained saturation.
- Microphone hiss/self-noise.
- Stereo width.
- Optional subtle handling noise associated with player movement.

The desired behavior: when the player stops in a quiet room, recording gain gradually raises subtle building sound and hiss. A louder nearby sound briefly reduces gain, then it recovers smoothly.

Avoid aggressive pumping, harsh distortion, runaway gain in silence, or loud hiss.

Keep audio gain independent of visual auto-exposure. Settings may share presets, but exposure should not directly control microphone amplification.

Preserve stereo balance with linked dynamics where appropriate. Keep UI sounds outside the recording chain.

Use suitable built-in mixer effects and only add custom DSP where needed. Keep real-time audio processing allocation-free and thread-safe.

### 6. F5 Audio controls

Add an Audio section to the existing F5 camcorder panel, following its existing UI and persistence patterns.

Include:

- Output volume
- Camcorder processing enable/bypass
- Microphone coloration
- Auto-gain strength
- Hiss level
- Handling-noise level
- Stereo width

Keep advanced tuning in developer settings unless it meaningfully helps the player.

Give existing camcorder presets suitable audio defaults. Clean should offer a clean reference. Provide a way to compare processed and unprocessed sound without losing current settings.

Use smooth parameter changes to avoid clicks. Keep settings backward-compatible with existing saved values.

### 7. Verify in Play Mode

Test and report:

- Walking toward, beneath, and away from fixtures.
- Turning away without sounds stopping.
- Moving between rooms and behind walls.
- Changes in powered and faulty fixtures.
- Crossing emitter selection boundaries without audible source jumps.
- Streaming and unloading without orphaned sources or abrupt cutoffs.
- Standing still long enough to evaluate repetition and automatic gain behavior.
- F5 changes, presets, bypass, and saved settings.
- Bounded source counts and no new console errors.

Provide a small developer diagnostic view or counters for active emitters and current audio state if useful.

Summarize what was implemented, how it was tested, and any approximations or remaining listening checks. If you cannot actually audition the output, state that clearly; runtime checks alone do not establish sound quality.

Finish with a working, saved audio milestone. Do not start inventory, furniture interaction, portals, or other future systems during this task.

## Additional requirement: F5 “Sound” tab for individual sound tuning

Alongside the Audio tab for overall output and camcorder processing, add a separate Sound tab for tuning individual game sounds.

This must be a searchable catalog of all registered sound types in the game, including sounds whose rooms or emitters are not currently loaded. Examples include fluorescent hum, fluorescent strike-on, faulty-tube buzz, ventilation, footsteps, and furniture scraping as those systems are introduced.

Selecting an entry displays its settings:

- Volume multiplier.
- Mute toggle.
- Pitch, where appropriate.
- For positional sounds: minimum and maximum audible distance, attenuation controls, and spatial blend.
- Relevant sound-specific parameters, such as variation or event frequency, only where supported and appropriate.
- Reset to default.

Use clear names, categories, and short descriptions. Search should match names and categories. Include an explicit preview/stop control where practical so sounds can be auditioned without finding their source in the world. Do not preview automatically when selecting an entry.

Edits apply globally to that sound type. Changing “Fluorescent Hum” affects all existing and future fluorescent hum emitters throughout the game—not just the nearest fixture or current room. Apply changes live with smoothing to prevent clicks.

Persist these overrides across scene changes, streaming, and application restarts. Treat them as a global audio tuning profile rather than per-room or per-save settings. Use stable sound IDs, preserve overrides when new sounds are added, and supply defaults for new entries. Provide per-sound reset and a clearly labeled reset-all action.

Keep sound definitions and saved overrides centralized. Require newly implemented sounds to register with this catalog so the list stays complete. Do not build the list by scanning currently active AudioSources.

The processing order should remain understandable:

Individual sound settings → environmental/spatial processing → shared camcorder processing → output volume.

Per-sound settings must coexist with power state, distance attenuation, procedural variation, and fixture events. Raising a hum’s volume must not make an unpowered fixture audible. Sound controls must not independently change gameplay event timing—for example, fluorescent audio should remain synchronized with the actual light.

Implement this Sound tab as part of the current audio milestone, and verify that changes affect active emitters, newly streamed emitters, and subsequent application sessions.
