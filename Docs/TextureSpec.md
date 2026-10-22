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
| Wood (doors, trim) | `Wood_Albedo` (+ normal, roughness) | **1 m** | square | Optional. |
| Upholstery | `Fabric_Albedo`, `Fabric_Normal` | **1 m** | square | Optional. Chair and armchair fabric. |

## How to check a texture in game

1. Put the files in `Assets/Backrooms/Textures/`.
2. Run **Backrooms > Build Infinite Offices Scene**, then press Play.
3. Use F5 > CAMERA > **Clean** to see it without the camcorder look, then switch back.
