# Texture spec (what to make, and how it maps)

Drop finished files into `Assets/Backrooms/Textures/` using the file names below, replacing the old ones.
The world builder (Backrooms > Build Infinite Offices Scene) picks them up; nothing else needs changing.

## General rules

- **Seamless tiling** on both axes (except the wallpaper height, see below). No baked lighting, shadows or
  ambient occlusion in the colour map: the game lights everything itself.
- **Format:** PNG, 8-bit. Albedo in sRGB. Normal map in **OpenGL style (Y+, green up)**.
- **Roughness:** grayscale, white = rough. The materials use albedo and normal today; roughness and AO
  get wired in (converted to Unity's smoothness) when the first set arrives.
- **AO (optional):** grayscale, only for small-scale crevices (carpet loops, tile fissures).
- **Size:** 2048 is the sweet spot. 4096 is fine for the carpet and ceiling tile. The camcorder softness
  hides anything sharper.
- Keep colours slightly desaturated and mid-bright (albedo roughly 90 to 190 sRGB). Very dark or very
  saturated albedos read wrong under the baked light.

## Per material

| Material | Files | Real-world size of one repeat | Pixel shape | Notes |
|---|---|---|---|---|
| Wallpaper | `Wallpaper_Albedo`, `Wallpaper_Normal`, `Wallpaper_Roughness` | **1.2 m wide × 2.7 m tall** (floor to ceiling) | 1024×2304 or 2048×4608 | Tiles horizontally only. The top and bottom edges are the ceiling and baseboard, so they don't need to match. Classic mono-yellow vertical stripes, faint paper grain, slight water staining near the bottom is welcome. |
| Carpet | `Carpet_Albedo`, `Carpet_Normal`, `Carpet_Roughness` (+ `Carpet_AO`) | **4 m × 4 m** | square | Damp, worn brownish-beige loop pile. Large soft variation across the 4 m, so the repeat doesn't show. |
| Carpet detail | `CarpetDetail_Albedo`, `CarpetDetail_Normal` | about **0.5 m** | 512–1024 square | Close-up fibre texture layered on top. Mostly neutral grey around 50% so it only adds grain. |
| Ceiling tile | `CeilingTile_Albedo`, `CeilingTile_Normal`, `CeilingTile_Roughness` | **2.4 m × 2.4 m = 4 × 4 tiles of 0.6 m** | square | The grid bars are separate geometry. Paint only the tile faces with a hairline gap at each 0.6 m edge. Fissured mineral-fibre look, a few stained tiles. |
| Plaster / painted wall | `Plaster_Albedo`, `Plaster_Normal`, `Plaster_Roughness` | **2.4 m × 2.4 m** | square | Used on painted walls and plaster ceilings in tall halls. Off-white with a subtle roller texture. |
| (Current, 2026-10-26) | `Plaster_*`, `RefCeiling_*`, `Resources/Textures/DuctMetal` | as above | | Made from the player's photos by `Tools/Textures/make_surface_textures.py` (sources in `Tools/Textures/Source`): painted drywall (seamless, 0.6 m repeat, with a normal map), the drop ceiling's fissured tiles (16 tiles turned and flipped differently), the crawl ducts' riveted perforated metal panels (two panels a section). |
| Wood (doors, trim) | `Wood_Albedo` (+ normal, roughness) | **1 m** | square | Optional. |
| (Current, 2026-10-28) | `RefCarpet_*`, `Plaster_*` (new photo), `Cardboard_*` | as above (carpet 3 m, boxes 0.5 m) | | From the player's second set of photos, same tool: the office carpet's pile and specks (a 0.75 m seamless patch laid 4 x 4 with random flips, the old texture's slow patchiness and mean kept, so the tan tint still colours it); the cream painted wall; kraft paper for the boxes (the old corrugation ridges kept in the normal map). |
| (Current, 2026-10-28) | `Resources/Textures/Photo/*_Albedo`, `*_Normal` | per use, below | | Loaded by the code that builds those materials: `Paper` (drywall and tile backs), `Galvanized` (boxes, conduit, hanger wire: 1 m) and `GalvanizedStud` (steel studs: 0.5 m across, 2 m along), `Pine` (wood studs: 0.5 x 2 m, grain along v), `EndGrain` (a broken stud's end: 12.5 cm), `OldWood` (joists and deck above the ceiling), `FlexDuct` (the foil duct up there), `DoorVeneer` (the doors' oak: 1.5 m along the grain x 0.5 m), `Concrete` (warehouse hall floors, 3 m), and the multiply stamps `StainCorner` / `StainCentre` (water stains cut from photographed tiles, stamped on the stained ceiling tiles). Most are detail maps (mean near white) so each material's tint gives the colour. |
| Roughness / AO | none as files | | | The photos' cavities stay in the colour maps (that is the ambient occlusion), and each material's smoothness is set from what it is. Separate roughness or AO maps would need new variants of the lit shader (10-15 s each to compile, and compiling mid-play once crashed the editor), so they are left for a planned shader pass. |
| Upholstery | `Fabric_Albedo`, `Fabric_Normal` | **1 m** | square | Optional. Chair and armchair fabric. |

## Height and cavity maps (2026-11-02)

A material whose normal map is listed in `MaterialMaps.Surfaces` gets surface detail (Docs/RuntimeLighting.md, "Surface
detail"). Its height and cavity map is made from the normal map: add the normal map to `MAPS` in
`Tools/Textures/make_height_maps.py`, run it, import the result linear (not sRGB), and add a row to `Surfaces`.

## Photos lit from one side (2026-11-02)

A close photo of a relief taken under one light (a wall by a window, a lamp to one side) has its shading, not its
height, in its brightness: "dark is lower" turns each bump into a half-lit smear. `orange_peel()` in
`Tools/Textures/make_surface_textures.py` finds the light's direction from the shading's spectrum, integrates the
shading along it into a height, and makes it seamless with a histogram-preserving blend (`hp_seamless`); reuse it for
such photos (set the fitted angle and the photo's size in metres).

## How to check a texture in game

1. Put the files in `Assets/Backrooms/Textures/`.
2. Run **Backrooms > Build Infinite Offices Scene**, then press Play.
3. Use F5 > CAMERA > **Clean** to see it without the camcorder look, then switch back.
