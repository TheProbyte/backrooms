using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The Poolrooms part of a district plan (pool-style zones; see DistrictGenerator). The office partition gives the
    /// rooms and doorways; this decides what makes them Poolrooms: tall ceilings, which rooms are flooded basins and
    /// which are dry decks (with raised walkways along some basin walls), round tiled columns, and the openings in
    /// the roof the sun comes through. No lights, furniture or outlets. Pure data, worker threads.
    /// </summary>
    public static class PoolPlan
    {
        /// <summary>Ceiling heights by room size, in 0.3 m steps (whole rows of 15 cm tiles).</summary>
        public static void Ceilings(OfficeLayout L, System.Random rng)
        {
            foreach (var r in L.rooms)
            {
                var R = L.RulesAt(r.rect);
                int area = r.rect.width * r.rect.height;
                float lo, hi;
                if (r.kind == RoomKind.Hallway) { lo = 3.3f; hi = 3.9f; }
                else if (area >= 80) { lo = 4.5f; hi = R.tallMax; }
                else if (area >= 30) { lo = 3.9f; hi = Mathf.Min(R.tallMax, 4.8f); }
                else { lo = Mathf.Max(3.3f, R.tallMin); hi = Mathf.Min(R.tallMax, 4.2f); }
                hi = Mathf.Max(lo, hi);
                int steps = Mathf.RoundToInt((hi - lo) / 0.3f);
                r.ceiling = Mathf.Min(OfficeBiome.MaxCeilingHeight, lo + 0.3f * rng.Next(steps + 1));
                r.painted = true;
                r.lights = LightMode.Dark;
            }
        }

        /// <summary>Basins and decks, walkways, columns and roof openings.</summary>
        public static void Dress(OfficeLayout L, System.Random rng)
        {
            int n = L.N;
            L.pool = L.pool ?? new PoolCell[n, n];

            // Flooded rooms. Every walkable room is either all basin or all deck, except walkways (below).
            foreach (var r in L.rooms)
            {
                if (!r.Walkable) continue;
                bool basin = rng.NextDouble() < L.RulesAt(r.rect).flooded;
                Fill(L, r.rect, c => c.basin = basin);
            }

            // Raised walkways: a one-cell deck along one wall of a big basin.
            foreach (var r in L.rooms)
            {
                var rc = r.rect;
                if (!r.Walkable || !L.Basin(rc.xMin, rc.yMin) || Mathf.Min(rc.width, rc.height) < 4) continue;
                if (rng.NextDouble() >= L.RulesAt(rc).ledges) continue;
                int side = rng.Next(4);
                RectInt strip = side == 0 ? new RectInt(rc.xMin, rc.yMin, 1, rc.height)
                              : side == 1 ? new RectInt(rc.xMax - 1, rc.yMin, 1, rc.height)
                              : side == 2 ? new RectInt(rc.xMin, rc.yMin, rc.width, 1)
                              : new RectInt(rc.xMin, rc.yMax - 1, rc.width, 1);
                Fill(L, strip, c => c.basin = false);
            }

            // Round columns on a grid in the big rooms, two cells clear of the walls.
            var pillarCells = new HashSet<Vector2Int>();
            foreach (var r in L.rooms)
            {
                var rc = r.rect;
                if (r.kind != RoomKind.BigOffice || Mathf.Min(rc.width, rc.height) < 6) continue;
                var R = L.RulesAt(rc);
                if (rng.NextDouble() >= R.pillarChance) continue;
                int spacing = Mathf.Max(2, Mathf.RoundToInt(R.pillarSpacing + (float)(rng.NextDouble() - 0.5)));
                int sx = rc.xMin + Mathf.Max(2, (rc.width % spacing) / 2 + 1), sz = rc.yMin + Mathf.Max(2, (rc.height % spacing) / 2 + 1);
                bool oneRow = rng.NextDouble() < 0.3;                        // a single colonnade down the middle
                int midZ = rc.yMin + rc.height / 2, midX = rc.xMin + rc.width / 2;
                bool rowAlongX = rc.width >= rc.height;
                for (int x = sx; x <= rc.xMax - 2; x += spacing)
                for (int z = sz; z <= rc.yMax - 2; z += spacing)
                {
                    int px = x, pz = z;
                    if (oneRow) { if (rowAlongX) { if (z != sz) continue; pz = midZ; } else { if (x != sx) continue; px = midX; } }
                    L.pillars.Add(new Pillar { vertex = new Vector2Int(px, pz), size = PoolBiome.PillarRadius * 2f, roomColor = r.color });
                    for (int dx = -1; dx <= 0; dx++) for (int dz = -1; dz <= 0; dz++) pillarCells.Add(new Vector2Int(px + dx, pz + dz));
                }
            }

            // Roof openings, one pattern per room.
            foreach (var r in L.rooms)
            {
                if (!r.Walkable || rng.NextDouble() >= L.RulesAt(r.rect).skylights) continue;
                Skylights(L, r, rng, pillarCells);
            }
        }

        static void Skylights(OfficeLayout L, Room r, System.Random rng, HashSet<Vector2Int> pillars)
        {
            var rc = r.rect;
            void Put(int x, int z, SkylightShape s)
            {
                if (x < rc.xMin || z < rc.yMin || x >= rc.xMax || z >= rc.yMax || pillars.Contains(new Vector2Int(x, z))) return;
                var c = L.pool[x, z]; c.skylight = s; L.pool[x, z] = c;
            }
            int w = rc.width, h = rc.height;
            bool alongX = w >= h;
            double p = rng.NextDouble();
            if (r.kind == RoomKind.Hallway || Mathf.Min(w, h) <= 2)
            {
                // Corridors: a long slot down the middle, or a run of square openings.
                bool slot = rng.NextDouble() < 0.5;
                int across = alongX ? rc.yMin + h / 2 : rc.xMin + w / 2;
                int len = alongX ? w : h, start = alongX ? rc.xMin : rc.yMin;
                for (int i = 1; i < len - 1; i++)
                {
                    if (!slot && i % 2 == 0) continue;
                    int x = alongX ? start + i : across, z = alongX ? across : start + i;
                    Put(x, z, slot ? (alongX ? SkylightShape.SlotX : SkylightShape.SlotZ) : SkylightShape.Square);
                }
                return;
            }
            if (p < 0.3)
            {
                // One big opening over the middle of the room: a wide pool of sun.
                int ow = Mathf.Clamp(w / 3, 1, 4), oh = Mathf.Clamp(h / 3, 1, 4);
                int ox = rc.xMin + (w - ow) / 2 + rng.Next(-1, 2), oz = rc.yMin + (h - oh) / 2 + rng.Next(-1, 2);
                ox = Mathf.Clamp(ox, rc.xMin + 1, Mathf.Max(rc.xMin + 1, rc.xMax - 1 - ow)); oz = Mathf.Clamp(oz, rc.yMin + 1, Mathf.Max(rc.yMin + 1, rc.yMax - 1 - oh));
                // One rectangle, or none: a column under part of it would leave an L whose inside corner opens onto nothing.
                bool clear = true;
                for (int x = ox; x < ox + ow; x++) for (int z = oz; z < oz + oh; z++)
                    if (x >= rc.xMax || z >= rc.yMax || pillars.Contains(new Vector2Int(x, z))) clear = false;
                if (clear) for (int x = ox; x < ox + ow; x++) for (int z = oz; z < oz + oh; z++) Put(x, z, SkylightShape.Full);
            }
            else if (p < 0.55)
            {
                // Parallel slots across the room: bars of light on the floor and up the walls.
                int step = rng.Next(2, 4);
                if (alongX) { for (int x = rc.xMin + 1; x < rc.xMax - 1; x += step) for (int z = rc.yMin + 1; z < rc.yMax - 1; z++) Put(x, z, SkylightShape.SlotZ); }
                else { for (int z = rc.yMin + 1; z < rc.yMax - 1; z += step) for (int x = rc.xMin + 1; x < rc.xMax - 1; x++) Put(x, z, SkylightShape.SlotX); }
            }
            else if (p < 0.75)
            {
                // A grid of square openings.
                int step = rng.Next(2, 4);
                for (int x = rc.xMin + 1; x < rc.xMax - 1; x += step) for (int z = rc.yMin + 1; z < rc.yMax - 1; z += step) Put(x, z, SkylightShape.Square);
            }
            else if (p < 0.88)
            {
                // A few round openings.
                int count = Mathf.Clamp(w * h / 25, 1, 4);
                for (int i = 0; i < count; i++) Put(rc.xMin + 1 + rng.Next(Mathf.Max(1, w - 2)), rc.yMin + 1 + rng.Next(Mathf.Max(1, h - 2)), SkylightShape.Round);
            }
            else
            {
                // A row of slots along one wall: the light falls in bars down that wall.
                int side = rng.Next(4);
                if (side < 2) { int x = side == 0 ? rc.xMin : rc.xMax - 1; for (int z = rc.yMin + 1; z < rc.yMax - 1; z += 2) Put(x, z, SkylightShape.SlotX); }
                else { int z = side == 2 ? rc.yMin : rc.yMax - 1; for (int x = rc.xMin + 1; x < rc.xMax - 1; x += 2) Put(x, z, SkylightShape.SlotZ); }
            }
        }

        static void Fill(OfficeLayout L, RectInt r, System.Action<Ref> edit)
        {
            for (int x = r.xMin; x < r.xMax; x++)
            for (int z = r.yMin; z < r.yMax; z++)
            {
                var box = new Ref { cell = L.pool[x, z] };
                edit(box);
                L.pool[x, z] = box.cell;
            }
        }

        /// <summary>Mutable wrapper so lambdas can edit a cell.</summary>
        sealed class Ref { public PoolCell cell; public bool basin { get => cell.basin; set => cell.basin = value; } }

        /// <summary>Makes a whole room a dry deck (a far crawl hole needs one).</summary>
        public static void MakeDeck(OfficeLayout L, Room r) => Fill(L, r.rect, c => c.basin = false);

        /// <summary>
        /// Stairs out of a basin: in the basin cell beside a passable edge to a deck cell of another room (doorways and
        /// open spans). One flight per cell; side order -X, +X, -Z, +Z. deckAt answers for cells outside the layout
        /// (null: unknown, treated as no stairs). Returns the side of the deck, or -1.
        /// </summary>
        public static int StairSide(OfficeLayout L, int x, int z, System.Func<int, int, bool?> deckAt)
        {
            if (L.pool == null || !L.pool[x, z].basin) return -1;
            for (int side = 0; side < 4; side++)
            {
                var e = L.EdgeOf(x, z, side);
                if (!OfficeLayout.IsPassable(e)) continue;
                int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                bool? deck;
                bool sameRoom = false;
                if (L.InChunk(nx, nz)) { deck = !L.pool[nx, nz].basin; sameRoom = L.roomOf[nx, nz] == L.roomOf[x, z]; }
                else deck = deckAt?.Invoke(nx, nz);
                if (deck == true && !sameRoom) return side;
            }
            return -1;
        }
    }
}
