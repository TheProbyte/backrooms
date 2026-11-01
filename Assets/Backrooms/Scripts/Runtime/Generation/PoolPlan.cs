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

        // ---------------- Spiral stairs ----------------

        /// <summary>
        /// Where a district's spiral stair tower stands (district-local cells), or none: in about two districts of three,
        /// inside one chunk with a cell to spare on every side (so one chunk builds the whole stair). Independent of the
        /// storey, so both floors reserve the same square.
        /// </summary>
        public static RectInt? SpiralSite(int seed, Vector2Int district)
        {
            var rng = new System.Random(DistrictGenerator.Hash(seed, district.x, district.y, 0x5B1A));
            if (rng.NextDouble() > 0.65) return null;
            int n = OfficeBiome.ChunkCells, s = PoolBiome.SpiralCells, dc = WorldGenSettings.DistrictChunks;
            int cx = rng.Next(dc), cz = rng.Next(dc);
            int x = cx * n + 1 + rng.Next(n - s - 2), z = cz * n + 1 + rng.Next(n - s - 2);
            return new RectInt(x, z, s, s);
        }

        /// <summary>The tower's room: on the ground a dry hall open to the storey above; above, the well (no floor, no
        /// random doors) with its stair landing.</summary>
        public static void MarkSpiral(OfficeLayout L, RectInt site)
        {
            var r = L.rooms.Find(q => q.rect.Equals(site));
            if (r == null) return;
            r.spiral = true;
            CellFlags flag;
            if (L.storey == 0) { r.openAbove = true; r.ceiling = L.storeyPitch + PoolBiome.SpiralUpperCeiling; flag = CellFlags.OpenAbove; }
            else { r.noFloor = true; r.noDoors = true; r.ceiling = PoolBiome.SpiralUpperCeiling; flag = CellFlags.NoFloor; }
            for (int x = site.xMin; x < site.xMax; x++) for (int z = site.yMin; z < site.yMax; z++) L.flags[x, z] |= flag;
        }

        /// <summary>The top of the stair arrives at the middle of the tower's +X side: the well's one door is there.</summary>
        public static void LandingDoor(OfficeLayout L, RectInt site) => L.SetEdge(site.xMax - 1, LandingRow(site), 1, EdgeKind.Door);

        public static int LandingRow(RectInt site) => site.yMin + site.height / 2;

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

            // Curved walls standing free in the big halls (their own dice).
            Arcs(L, new System.Random(rng.Next()), pillarCells);

            // Roof openings, one pattern per room.
            foreach (var r in L.rooms)
            {
                if (!r.Walkable || r.spiral || rng.NextDouble() >= L.RulesAt(r.rect).skylights) continue;
                Skylights(L, r, rng, pillarCells);
            }

            // High windows in the walls the sun comes through.
            Windows(L, new System.Random(rng.Next()));

            // Spiral stair towers: a dry floor round the stair; light comes down the well from four openings at its top.
            foreach (var r in L.rooms)
            {
                if (!r.spiral) continue;
                Fill(L, r.rect, c => c.basin = false);
                if (L.storey > 0)
                {
                    int cx = r.rect.xMin + r.rect.width / 2, cz = r.rect.yMin + r.rect.height / 2;
                    foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    { var c = L.pool[cx + dx, cz + dz]; c.skylight = SkylightShape.Square; L.pool[cx + dx, cz + dz] = c; }
                }
            }
        }

        /// <summary>
        /// Curved walls in big halls (reference: tiled walls that bend round, some full height, some low enough to look
        /// over). Centred well inside the room so no doorway is ever closed off; columns too close are taken out.
        /// </summary>
        static void Arcs(OfficeLayout L, System.Random rng, HashSet<Vector2Int> pillarCells)
        {
            float C = OfficeBiome.CellSize;
            foreach (var r in L.rooms)
            {
                var rc = r.rect;
                if (!r.Walkable || r.spiral || r.kind != RoomKind.BigOffice || Mathf.Min(rc.width, rc.height) < 9) continue;
                if (rng.NextDouble() > 0.45) continue;
                int vx = rng.Next(rc.xMin + 4, rc.xMax - 3), vz = rng.Next(rc.yMin + 4, rc.yMax - 3);
                float radius = C * (2 + rng.Next(3) * 0.25f);
                float from = rng.Next(8) * 45f, sweep = 140f + rng.Next(4) * 25f;
                bool full = rng.NextDouble() < 0.6;
                var centre = new Vector2(vx * C, vz * C);
                L.arcs.Add(new PoolArc { centre = centre, radius = radius, from = from, to = from + sweep, top = full ? r.ceiling : 2.1f });
                L.pillars.RemoveAll(p => Vector2.Distance(new Vector2(p.vertex.x * C, p.vertex.y * C), centre) < radius + 1.3f);
                // Openings over a curved wall would leave it standing in the sun's path through the roof: keep them off it.
                for (int x = vx - 4; x <= vx + 3; x++) for (int z = vz - 4; z <= vz + 3; z++) pillarCells.Add(new Vector2Int(x, z));
            }
        }

        /// <summary>
        /// High windows (2026-10-31, the owner: "some side wall high openings"; references: tall bright openings high in a
        /// tiled wall with the sun pouring through): in some tall rooms, every other cell along the wall on the sun's side
        /// (+X or +Z: the sun comes from there) has a 0.6 m wide window from 1.95 m up to just under the ceiling, with the
        /// sky beyond (this is the Backrooms: the room behind it sees the same sky). The sun comes in through them in
        /// beams (PoolSun / PoolCommon.hlsl: edge code 3). Both rooms must be tall enough; only plain wall edges.
        /// </summary>
        static void Windows(OfficeLayout L, System.Random rng)
        {
            int n = L.N;
            foreach (var r in L.rooms)
            {
                if (!r.Walkable || r.spiral || r.ceiling < PoolBiome.WindowMinCeiling) continue;
                if (rng.NextDouble() > 0.4) continue;
                var rc = r.rect;
                int phase = rng.Next(2);
                bool xWall = rng.NextDouble() < 0.5;
                for (int pass = 0; pass < 2; pass++, xWall = !xWall)
                {
                    int line = xWall ? rc.xMax : rc.yMax;
                    if (line >= n) continue;
                    int lo = xWall ? rc.yMin : rc.xMin, hi = xWall ? rc.yMax : rc.xMax, made = 0;
                    for (int a = lo + 1 + phase; a < hi - 1; a += 2)
                    {
                        int ox = xWall ? line : a, oz = xWall ? a : line;
                        var edge = xWall ? L.vEdges[line, a] : L.hEdges[a, line];
                        if (edge != EdgeKind.Wall) continue;
                        var other = L.RoomAt(ox, oz);
                        if (!other.Walkable || other.spiral || other.ceiling < PoolBiome.WindowMinCeiling) continue;
                        var c = L.pool[ox, oz]; c.windows |= (byte)(xWall ? 1 : 2); L.pool[ox, oz] = c;
                        made++;
                    }
                    if (made > 0 && rng.NextDouble() < 0.6) break;          // usually one wall of them, sometimes both
                }
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
            if (p < 0.45)
            {
                // One big opening over the middle of the room: a wide pool of sun (2026-10-31: larger, after the owner's
                // reference pictures).
                int ow = Mathf.Clamp(w / 2, 1, 6), oh = Mathf.Clamp(h / 2, 1, 6);
                int ox = rc.xMin + (w - ow) / 2 + rng.Next(-1, 2), oz = rc.yMin + (h - oh) / 2 + rng.Next(-1, 2);
                ox = Mathf.Clamp(ox, rc.xMin + 1, Mathf.Max(rc.xMin + 1, rc.xMax - 1 - ow)); oz = Mathf.Clamp(oz, rc.yMin + 1, Mathf.Max(rc.yMin + 1, rc.yMax - 1 - oh));
                // One rectangle, or none: a column under part of it would leave an L whose inside corner opens onto nothing.
                bool clear = true;
                for (int x = ox; x < ox + ow; x++) for (int z = oz; z < oz + oh; z++)
                    if (x >= rc.xMax || z >= rc.yMax || pillars.Contains(new Vector2Int(x, z))) clear = false;
                if (clear) for (int x = ox; x < ox + ow; x++) for (int z = oz; z < oz + oh; z++) Put(x, z, SkylightShape.Full);
            }
            else if (p < 0.65)
            {
                // Parallel slots across the room: bars of light on the floor and up the walls.
                int step = 2;
                if (alongX) { for (int x = rc.xMin + 1; x < rc.xMax - 1; x += step) for (int z = rc.yMin + 1; z < rc.yMax - 1; z++) Put(x, z, SkylightShape.SlotZ); }
                else { for (int z = rc.yMin + 1; z < rc.yMax - 1; z += step) for (int x = rc.xMin + 1; x < rc.xMax - 1; x++) Put(x, z, SkylightShape.SlotX); }
            }
            else if (p < 0.8)
            {
                // A grid of square openings.
                int step = 2;
                for (int x = rc.xMin + 1; x < rc.xMax - 1; x += step) for (int z = rc.yMin + 1; z < rc.yMax - 1; z += step) Put(x, z, SkylightShape.Square);
            }
            else if (p < 0.9)
            {
                // A few round openings.
                int count = Mathf.Clamp(w * h / 16, 2, 6);
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
