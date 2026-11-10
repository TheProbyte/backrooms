# Plan: the next realism pass (2026-10-29)

**Status (2026-10-29):** built: sections 1 (dirt), 3 (walls and wallpaper), 4 (ceilings), 5 (floors) and 7 (light and
air), described in WorldGeneration.md, "Realism pass". Not built: section 2 (the leak story) and section 6 (things on
walls and in rooms), held back on purpose; from 3, the pattern out of line at a seam and cracked or missing outlet
plates; from 4, a bent grid runner; from 5, frayed carpet edges. The world's look has no baseboards
(`OfficeBiomeAssets.noBaseboard`), so the baseboard wear in 3 is built but never seen. F5 > PLACES > WEAR AND DETAIL
jumps to the nearest of each of the rarer kinds.

**After the first look (2026-10-29, evening):** the band of grime low on walls read as a shadow, so it is now patchy and
fainter with shoe scuffs; roller marks and patches of fresh paint (hard-edged brighter rectangles) are gone; askew lights
open on the real space above the ceiling; cobwebs are gone. How much of each kind there is can be set in F5 > WORLD GEN >
HOW OFTEN (DetailRates); carpet-tile rooms are 5 % by default (were 16 %).

Same idea as the wear and office details already in the world (WorldGeneration.md, "Wear and neglect", "Office
details"): small signs of age and use, decided by the seed and the cell (the same every visit, from either chunk), cheap
(overlays and a few hundred triangles a chunk), and rare. Most cells stay plain; one in twenty or so has something. The
look to aim for is Kane Pixels' Backrooms: a 90s office, clean at a glance, wrong on a second look.

## What is there now
- **Ceilings:** stained, yellowed, sagging, lifted, slid, missing, cracked and crumbled tiles, with the space above
  showing through the gaps. Diffusers, return grilles, sprinklers, smoke detectors. Lights with a tube out, gone pink,
  or with dead bugs in the lens.
- **Walls:** scuffs, nail holes, ghosts of things that hung there, wallpaper peeling at a seam, exit signs.
- **Floors:** carpet darker along the walls and trodden at doorways, worn paths, carpet lifting off its tack strip,
  ripples, coffee spills, threshold strips.
- **Doors:** kick marks and grime round the handle.

## 1. Dirt where dirt really goes (first: biggest change for the least)
- **Corner grime:** a soft darkening up every inside corner where two walls meet, and along the line where the wall meets
  the ceiling (dust carried by air, the "ghosting" line in old offices). An overlay strip in the existing wall-wear mesh.
- **Grime toward the floor:** walls a little darker in their bottom 30 cm (mops, shoes, dust), fading up, broken up by
  a world-space noise so it isn't a band.
- **Dust on what faces up:** a faint light dust on the tops of baseboards, sills, door headers, light housings, the tops
  of furniture and boxes. One uniform in the shared wall/prop shader (no new shader variants), driven by how much a
  surface faces up and a noise.
- **Smudge rings round supply diffusers:** the dark halo real ceiling vents leave on the tiles round them.
- **Handprints and wear by switches and door frames:** a grubby patch round light switches and on door jambs at hand
  height; chipped paint on the edges of jambs and outside corners at hip height.

## 2. The leak story (one cause, all its signs together)
The leak noise already makes stained ceiling tiles cluster. Make everything under a leak agree:
- A brown tide line running down the wallpaper below a stained tile near a wall, the paper bubbled and lifting there.
- A water stain on the carpet below (darker, with a crisp dried edge), sometimes a carpet seam lifting.
- Rust on the ceiling grid round the wettest tile; one runner sagging.
- Rarely, a plastic bucket under it, and now and then a drip (a particle and a small sound).
- Mould: grey-green specks at the bottom of the wall in the dampest spots.

## 3. Walls and wallpaper
- **Seams:** wallpaper comes in rolls 53 cm wide. A faint seam line every roll, the pattern a little out of line at the
  odd seam, a seam opened up a millimetre or two in places.
- **Bubbles:** soft raised patches in the paper (normal-mapped overlay), clustered.
- **Bigger peels:** besides the corner peel there now, now and then a long strip hanging away from the wall, its back
  and the bare plaster behind showing.
- **Patches:** a rectangle of newer paint or mismatched paper where a repair was done.
- **Painted walls:** roller marks, the odd drip run, paint missing in a patch at furniture height.
- **Outlets and switches:** some painted over with the wall, one with a cracked plate, one plate missing (the box and its
  wires showing), one hanging crooked by a screw.
- **Baseboards:** gaps where they have pulled off the wall, a dented corner, caulk cracked at joints, a short missing
  piece.

## 4. Ceilings
- **Tile batches:** whole tiles in a slightly different white here and there (a replaced tile that never matched, a
  run from another batch), very subtle.
- **Cut tiles:** a tile with a round hole where a fitting was removed and never filled.
- **Grid:** a runner bent down a little, the odd missing cross tee (the tile's edge resting crooked).
- **Lights:** a troffer hanging askew on one side, a cracked lens, lenses yellowed with age (a few, a bit dimmer and
  warmer).
- **Sprinklers:** a missing escutcheon ring, one painted over.

## 5. Floors
- **Furniture dents:** four little dents and a cleaner rectangle where a desk or cabinet stood, now gone.
- **Carpet tiles:** some offices in square carpet tiles; one or two missing (the glue and the concrete showing) or
  replaced in a different colour.
- **More stains:** a rust ring from something metal, a dark patch by a door, a cigarette burn (rare).
- **Frayed edges** where carpet meets another floor.
- **Laminate and wood floors:** scratches along the walk paths, a chipped tile corner.

## 6. Things on walls and in rooms (sparse, true to offices)
- Thermostats, fire alarm pull stations, extinguishers in their cabinets, evacuation maps by doors, room numbers beside
  doors, a corkboard with faded notices, a whiteboard with half-wiped writing, a clock that stopped.
- A few loose things: a sheet of paper, a binder, a pen, a chair pushed out at an odd angle, a coat on a hook. Very
  rare: the Backrooms are mostly empty.

## 7. Light and air
- Tubes not all the same white: a slightly green one, a slightly pink one, within a room.
- Dust in the air where light is strong (as the desk lamp has now), very faint.
- Air movement near return grilles (a loose tile edge or a cobweb strand moving).

## Order
1. Dirt where dirt goes (section 1).
2. The leak story (2).
3. Wallpaper and walls (3).
4. Ceilings (4).
5. Floors (5).
6. Things on walls (6).
7. Light and air (7).

Each is its own commit. Each is checked against the reference screenshots (Docs/screenshots/refmatch) and the
LightingTour views, before and after. Frame time and triangle counts are measured on the benchmark route, and the
chunk-order tests are run (everything decided per cell, so chunks still agree). Each number is a setting in one place,
so it can be turned up or down after you have looked.
