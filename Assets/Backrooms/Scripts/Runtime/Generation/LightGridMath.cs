using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// CPU side of the light grid: how layout cells are packed into the occupancy texture, the light
    /// record the compute shader reads, and a reference implementation of the per-texel light sum
    /// (mirrors Shaders/LightGrid.compute) used by tests and the World Map's light field view.
    /// </summary>
    public static class LightGridMath
    {
        public const float Texel = 0.6f, Cell = OfficeBiome.CellSize;

        public const uint Open = 0, Wall = 1, Door = 2, Header = 3;
        public const uint Loaded = 1u << 24, NoFloor = 1u << 25, OpenAbove = 1u << 26;
        /// <summary>The cell's west / south wall is broken (LG_WEST_HOLED / LG_SOUTH_HOLED).</summary>
        public const uint WestHoled = 1u << 29, SouthHoled = 1u << 30;

        // ---------------- The hole table (broken walls), as LightGridCommon.hlsl reads it ----------------

        /// <summary>uints a record: two key words, the open mask (5), the any-opening mask (5), the open share, 3 spare.</summary>
        public const int HoleRecord = 16;

        public static void HoleKey(bool vertical, int line, int along, int storey, out uint k0, out uint k1)
        {
            unchecked
            {
                k0 = ((uint)line & 0xFFFFu) | ((uint)along << 16);
                k1 = (vertical ? 1u : 0u) | ((uint)storey << 1) | 0x80000000u;
            }
        }

        /// <summary>
        /// Packs broken walls into records (16 uints each: two key words, five words of blocks open right through, five of
        /// blocks with any opening, the open share x 65535), in list order, and marks each one's cell (the cell the wall is the west or south edge of) in a per-cell index laid out as
        /// the occupancy is (n cells a side, two storeys, toroidal): record + 1, west wall in the low 16 bits, south wall in
        /// the high 16. cellMin: the window's corner cell (walls outside the window are left out of the index).
        /// </summary>
        public static uint[] BuildHoleTable(List<(bool vertical, int line, int along, int storey, uint[] mask, uint[] maskAny, float share)> holes, uint[] table, uint[] cells, int n, Vector2Int cellMin)
        {
            int records = Mathf.Max(1, holes.Count);
            if (table == null || table.Length < records * HoleRecord) table = new uint[Mathf.NextPowerOfTwo(records) * HoleRecord];
            else Array.Clear(table, 0, table.Length);
            if (cells != null) Array.Clear(cells, 0, cells.Length);
            for (int r = 0; r < holes.Count && r < 65535; r++)
            {
                var h = holes[r];
                HoleKey(h.vertical, h.line, h.along, h.storey, out uint k0, out uint k1);
                int b = r * HoleRecord;
                table[b] = k0; table[b + 1] = k1;
                for (int w = 0; w < 5; w++)
                {
                    table[b + 2 + w] = h.mask != null && w < h.mask.Length ? h.mask[w] : 0u;
                    table[b + 7 + w] = h.maskAny != null && w < h.maskAny.Length ? h.maskAny[w] : 0u;
                }
                table[b + 12] = (uint)Mathf.Clamp(Mathf.RoundToInt(h.share * 65535f), 0, 65535);
                if (cells == null || h.storey < 0 || h.storey > 1) continue;
                int cx = h.vertical ? h.line : h.along, cz = h.vertical ? h.along : h.line;
                if (cx < cellMin.x || cz < cellMin.y || cx >= cellMin.x + n || cz >= cellMin.y + n) continue;
                int i = h.storey * n * n + WrapCell(cz, n) * n + WrapCell(cx, n);
                uint rec = (uint)(r + 1);
                cells[i] = h.vertical ? (cells[i] & 0xFFFF0000u) | rec : (cells[i] & 0xFFFFu) | (rec << 16);
            }
            return table;
        }

        static int WrapCell(int v, int n) { int m = v % n; return m < 0 ? m + n : m; }

        /// <summary>One light as the compute shader reads it (80 bytes).</summary>
        [Serializable]
        public struct GridLight
        {
            public Vector3 pos; public float range;
            public Vector3 color; public float cosOuter;      // colour x intensity x the grid's share
            public Vector3 dir; public float cosInner;        // spot axis, zero for point lights
            public Vector3 colorAll; public float storey;     // full colour x intensity x power (bounce)
            public Vector2 halfSize; public Vector2 padding; // finite horizontal emitter; zero = point. padding.x: bounce and haze
                                                             // use colorAll x (1 + x) (smoothed power of a faulty tube)
            public const int Stride = 80;
        }

        // Matches LGrid_FindFixture. Quarter-metre cells use exact binary scaling on CPU and GPU.
        public static uint FixtureHash(Vector3 position)
        {
            unchecked
            {
                return (uint)Mathf.FloorToInt(position.x * 4f) * 73856093u ^
                       (uint)Mathf.FloorToInt(position.y * 4f) * 83492791u ^
                       (uint)Mathf.FloorToInt(position.z * 4f) * 19349663u;
            }
        }

        /// <summary>Half-full open-addressed fixture index; zero is empty, stored indices are one-based.</summary>
        public static uint[] BuildFixtureLookup(List<GridLight> lights, uint[] table)
        {
            int size = Mathf.NextPowerOfTwo(Mathf.Max(4, lights.Count * 2));
            if (table == null || table.Length != size) table = new uint[size];
            else Array.Clear(table, 0, table.Length);
            uint mask = (uint)(size - 1);
            for (int i = 0; i < lights.Count; i++)
            {
                uint slot = FixtureHash(lights[i].pos) & mask;
                while (table[slot] != 0) slot = (slot + 1) & mask;
                table[slot] = (uint)i + 1;
            }
            return table;
        }

        static uint Kind(EdgeKind e) => e == EdgeKind.Wall ? Wall : e == EdgeKind.Door ? Door : e == EdgeKind.DoorHeader ? Header : Open;
        static uint Tenths(float metres) => (uint)Mathf.Clamp(Mathf.RoundToInt(metres * 10f), 0, 255);

        /// <summary>An opening's top in tenths of a metre: low 6 bits at lowShift, bit 6 at highBit (up to 12.7 m, an atrium well is 11.7).</summary>
        public static uint PackTop(uint tenths, int lowShift, int highBit)
        {
            tenths = Math.Min(127u, tenths);
            return ((tenths & 63u) << lowShift) | (((tenths >> 6) & 1u) << highBit);
        }

        /// <summary>Packs one layout cell: west and south edges (kind and opening top), ceiling, flags.</summary>
        public static uint Encode(OfficeLayout L, int x, int z, Func<int, int, OfficeLayout> neighbour)
        {
            var room = L.RoomAt(x, z);
            uint o = Loaded;
            uint west = Kind(L.vEdges[x, z]), south = Kind(L.hEdges[x, z]);
            // A door is a wall to light when shut, and lets light by as it opens (DoorEdges.LightOpenness, 2026-11-04): its
            // opening's top comes down to that share of its height, so the light from the room behind comes in a little at
            // a time, low first, as a door's gap does. The edge's world cell line and position.
            float openW = 1f, openS = 1f;
            if (DoorEdges.LightCount > 0 || DoorEdges.Count > 0)
            {
                int n = L.N, gx = L.coord.x * n + x, gz = L.coord.y * n + z;
                if (west != Wall) { openW = DoorEdges.LightOpenness(true, gx, gz, L.storey); if (openW <= 0f) west = Wall; }
                if (south != Wall) { openS = DoorEdges.LightOpenness(false, gz, gx, L.storey); if (openS <= 0f) south = Wall; }
            }
            o |= west | (south << 2);
            if (west != Wall) o |= PackTop(Math.Max(1u, Tenths(OfficeChunkBuilder.ViewOpening(L, neighbour, true, x, z, 1).top * openW)), 4, 27);
            if (south != Wall) o |= PackTop(Math.Max(1u, Tenths(OfficeChunkBuilder.ViewOpening(L, neighbour, false, z, x, 1).top * openS)), 10, 28);
            // A broken wall (Destruction.WallDamage): still a wall, flagged as holed (the hole table says where it is open).
            if (Destruction.WallDamage.Count > 0)
            {
                int n = L.N, gx = L.coord.x * n + x, gz = L.coord.y * n + z;
                if (west == Wall && Destruction.WallDamage.Holed(new Destruction.WallKey(true, gx, gz, L.storey), out _)) o |= WestHoled;
                if (south == Wall && Destruction.WallDamage.Holed(new Destruction.WallKey(false, gz, gx, L.storey), out _)) o |= SouthHoled;
            }
            o |= Tenths(room.ceiling) << 16;
            if (L.Has(x, z, CellFlags.NoFloor)) o |= NoFloor;
            if (L.Has(x, z, CellFlags.OpenAbove)) o |= OpenAbove;
            return o;
        }

        public static uint WestKind(uint o) => o & 3u;
        public static uint SouthKind(uint o) => (o >> 2) & 3u;
        public static float WestTop(uint o) => (((o >> 4) & 63u) | (((o >> 27) & 1u) << 6)) * 0.1f;
        public static float SouthTop(uint o) => (((o >> 10) & 63u) | (((o >> 28) & 1u) << 6)) * 0.1f;
        public static float Ceiling(uint o) => ((o >> 16) & 255u) * 0.1f;

        /// <summary>A ceiling fixture's falloff away from straight down (LGrid_AngleAtten): Lambertian, cos(angle), at full
        /// spread (FixtureLightManager.TrofferCone), narrower below it.</summary>
        public static float PanelProfile(float cosAngle, float cosOuter, float cosInner)
        {
            float inv = 1f / Mathf.Max(cosInner - cosOuter, 0.001f);
            return Mathf.Clamp01(cosAngle * inv - cosOuter * inv);
        }

        /// <summary>A fixture's falloff (LGrid_Profile): its photometric cos^k when it has one (k in padding.y), else the cone.</summary>
        public static float FixtureProfile(float cosAngle, float cosOuter, float cosInner, float k)
        {
            // A bare-tube batten (k + 100): a share of its light all round (LGrid_Profile).
            float all = 0f;
            if (k >= 99.5f) { k -= 100f; all = 0.3f; }
            float p = k > 0.001f ? Mathf.Pow(Mathf.Clamp01(cosAngle), k) : PanelProfile(cosAngle, cosOuter, cosInner);
            return all + (1f - all) * p;
        }
        public static bool IsLoaded(uint o) => (o & Loaded) != 0;

        /// <summary>Occupancy lookup: (cell x, cell z, storey) -> packed cell (0 = not loaded).</summary>
        public delegate uint OccupancyLookup(int cellX, int cellZ, int storey);

        static float DistanceAtten(float d2, float range) => LightLayers.DistanceAttenuation(d2, range);

        /// <summary>Line of sight between two points through the layout (same rules as the compute shader).</summary>
        public static bool Visible(Vector3 P, Vector3 Q, OccupancyLookup occ, float pitch)
        {
            int Storey(float y) => y >= pitch ? 1 : 0;
            if (Storey(P.y) != Storey(Q.y))
            {
                float t = (pitch - P.y) / (Q.y - P.y);
                var X = Vector3.Lerp(P, Q, t);
                uint w = occ(Mathf.FloorToInt(X.x / Cell), Mathf.FloorToInt(X.z / Cell), 0);
                if ((w & OpenAbove) == 0) return false;
            }
            float ax = P.x / Cell, az = P.z / Cell, bx = Q.x / Cell, bz = Q.z / Cell;
            int cx = Mathf.FloorToInt(ax), cz = Mathf.FloorToInt(az), ex = Mathf.FloorToInt(bx), ez = Mathf.FloorToInt(bz);
            float dx = bx - ax, dz = bz - az;
            int sx = dx > 0 ? 1 : dx < 0 ? -1 : 0, sz = dz > 0 ? 1 : dz < 0 ? -1 : 0;
            float tdx = sx != 0 ? Mathf.Abs(1f / dx) : 1e30f, tdz = sz != 0 ? Mathf.Abs(1f / dz) : 1e30f;
            float tmx = sx > 0 ? (cx + 1 - ax) * tdx : sx < 0 ? (ax - cx) * tdx : 1e30f;
            float tmz = sz > 0 ? (cz + 1 - az) * tdz : sz < 0 ? (az - cz) * tdz : 1e30f;
            for (int i = 0; i < 48; i++)
            {
                if (cx == ex && cz == ez) return true;
                bool stepX = tmx < tmz;
                float t = stepX ? tmx : tmz;
                if (t > 1f) return true;
                float y = Mathf.Lerp(P.y, Q.y, t);
                int storey = Storey(y);
                int nx = cx + (stepX ? sx : 0), nz = cz + (stepX ? 0 : sz);
                int ox = stepX ? Mathf.Max(cx, nx) : cx, oz = stepX ? cz : Mathf.Max(cz, nz);
                uint o = occ(ox, oz, storey);
                if (!IsLoaded(o)) return false;
                uint kind = stepX ? WestKind(o) : SouthKind(o);
                if (kind == Wall)
                {
                    // A broken wall: through where it is open right through (as the shaders' hole table says).
                    if ((o & (stepX ? WestHoled : SouthHoled)) == 0) return false;
                    var key = stepX ? new Destruction.WallKey(true, ox, oz, storey) : new Destruction.WallKey(false, oz, ox, storey);
                    if (!Destruction.WallDamage.Holed(key, out var panel)) return false;
                    float along = stepX ? Mathf.Lerp(P.z, Q.z, t) - oz * Cell : Mathf.Lerp(P.x, Q.x, t) - ox * Cell;
                    if (!Destruction.WallDamage.MaskOpen(panel.mask, along, y - storey * pitch)) return false;
                }
                float top = (stepX ? WestTop(o) : SouthTop(o)) + storey * pitch;
                if (kind != Wall && y > top + 0.02f) return false;          // (a wall has no opening top: a hole in it is checked above)
                cx = nx; cz = nz;
                if (stepX) tmx += tdx; else tmz += tdz;
            }
            return true;
        }

        /// <summary>Irradiance (facing the light) a grid light puts at Q, with occlusion; zero if blocked.</summary>
        public static float Irradiance(in GridLight L, Vector3 Q, OccupancyLookup occ, float pitch, bool all = false)
        {
            Vector3 toLight = L.pos - Q;
            float d2 = toLight.sqrMagnitude;
            if (d2 >= L.range * L.range) return 0f;
            float att = DistanceAtten(Mathf.Max(d2, 0.36f), L.range);   // near field capped at one texel (as in the shader)
            toLight /= Mathf.Sqrt(Mathf.Max(d2, 1e-8f));
            if (L.dir.sqrMagnitude > 0.5f) att *= FixtureProfile(Vector3.Dot(L.dir, -toLight), L.cosOuter, L.cosInner, L.padding.y);
            if (att <= 1e-6f || !Visible(L.pos, Q, occ, pitch)) return 0f;
            var c = all ? L.colorAll : L.color;
            return (0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z) * att;
        }

        /// <summary>A layout's light (fixture, lamp) as a grid light at full share.</summary>
        public static GridLight FromSpec(in LightLayers.Spec s, Color color, int storey, float baseY)
        {
            var c = new Vector3(color.linear.r, color.linear.g, color.linear.b) * s.intensity;
            return new GridLight
            {
                pos = s.position + Vector3.up * baseY, range = s.range, color = c, colorAll = c, dir = s.direction,
                cosOuter = s.cosOuter, cosInner = s.cosInner, storey = storey,
            };
        }

        /// <summary>Occupancy straight from a world generator (for tests and edit-mode views).</summary>
        public static OccupancyLookup FromWorld(WorldGenerator gen)
        {
            int N = OfficeBiome.ChunkCells;
            var cache = new Dictionary<Vector3Int, uint>();
            return (x, z, s) =>
            {
                var key = new Vector3Int(x, s, z);
                if (cache.TryGetValue(key, out uint v)) return v;
                int cx = Mathf.FloorToInt(x / (float)N), cz = Mathf.FloorToInt(z / (float)N);
                var c = new Vector3Int(cx, s, cz);
                if (s > 0 && gen.TopStoreyAt(new Vector2Int(cx, cz)) < s) return cache[key] = 0u;
                var L = gen.Chunk(c);
                v = Encode(L, x - cx * N, z - cz * N, (dx, dz) => gen.Chunk(new Vector3Int(cx + dx, s, cz + dz)));
                return cache[key] = v;
            };
        }
    }
}
