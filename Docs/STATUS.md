# Status (where to resume)

Last updated 2026-10-21. Read `Docs/ProjectBrief.md` for the vision.

## Lighting system (current design)

- Ceiling lights (`FixtureLightManager`) only light their own room/zone via rendering layers, so light can
  never pass through a wall regardless of shadow resolution. Every powered fixture within the light
  distance is on (nearest first, pool of 168); far ones dim into the fog. No strike-on for streamed areas.
- The nearest lights also cast shadows (ease in over 0.5 s) for furniture and pillar detail only.
- Light through openings comes from **doorway lights**: one soft spot per opening near the player, placed
  in the receiving room, lighting only that room, as bright as the lit fixtures just behind the opening.
  Doorways are produced by `OfficeChunkBuilder.BuildDoorways` and registered by `WorldStreamer`.
- Bounce fill points up (ceiling, upper walls) so it never draws lines on floors; it fades smoothly by
  distance toward the nearest-N edge.
- Walls cast shadows through inset proxy boxes (shadow-only) to avoid self-shadow banding.
- SSAO: intensity 1.0, radius 0.3, falloff 22 m (contact darkening no longer grows with distance).
- Budget: URP Forward+ allows 256 visible lights; ceiling 168 + bounce ~32 + doorway 32 + lamps 10.

## Open items (next steps, in order)

1. Verify the 2026-10-21 lighting rework in play mode against the owner's screenshots in
   `references/update9` (corner/baseboard bleed, wall pools popping, far floor-corner smudges).
2. CC0 PBR textures added 2026-10-21 (carpet, ceiling tile, plaster, wallpaper relief; see Textures/CC0/LICENSES.md). Check the look in play mode and tune tints in InfiniteWorldBuilder.ApplyScannedTextures.
   roughness [+ AO]): wallpaper, carpet, acoustic ceiling tile, painted plaster, baseboard. Owner
   approved downloading CC0 textures. Keep a `Assets/Backrooms/Textures/CC0/LICENSES.md` listing sources.
3. Zone system is a placeholder (`WorldGenSettings.ZoneAt` returns "Level 0").
4. Owner wants movement polish feedback (zoom F, crouch C, crawl Ctrl, jump Space were added).
