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

1. Owner to confirm the leak-proof lighting (rooms only lit by their own lights, doorway lights for
   openings) against `references/update9` in their own world. Checked 2026-10-22 in play mode: dark room
   next to a lit one shows no corner/baseboard bleed; wall light pools stay steady while walking.
2. CC0 PBR textures are in (carpet, ceiling tile, plaster, wallpaper relief; `Textures/CC0/LICENSES.md`).
   Tints live in `InfiniteWorldBuilder.ApplyScannedTextures`. The striped wallpaper pattern is still
   procedural (no CC0 match found).
3. Zone system is a placeholder (`WorldGenSettings.ZoneAt` returns "Level 0").
4. Movement (zoom F, crouch C, crawl Ctrl, jump Space) awaits the owner's feel feedback.
