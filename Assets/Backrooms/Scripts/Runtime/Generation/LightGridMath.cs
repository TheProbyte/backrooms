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

        /// <summary>One light as the compute shader reads it (64 bytes).</summary>
        [Serializable]
        public struct GridLight
        {
            public Vector3 pos; public float range;
            public Vector3 color; public float cosOuter;      // colour x intensity x the grid's share
            public Vector3 dir; public float cosInner;        // spot axis, zero for point lights
            public Vector3 colorAll; public float storey;     // full colour x intensity x power (bounce)
            public const int Stride = 64;
        }

        static uint Kind(EdgeKind e) => e == EdgeKind.Wall ? Wall : e == EdgeKind.Door ? Door : e == EdgeKind.DoorHeader ? Header : Open;
        static uint Tenths(float metres) => (uint)Mathf.Clamp(Mathf.RoundToInt(metres * 10f), 0, 255);

        /// <summary>Packs one layout cell: west and south edges (kind and opening top), ceiling, flags.</summary>
        public static uint Encode(OfficeLayout L, int x, int z, Func<int, int, OfficeLayout> neighbour)
        {
            var room = L.RoomAt(x, z);
            uint o = Loaded;
            uint west = Kind(L.vEdges[x, z]), south = Kind(L.hEdges[x, z]);
            o |= west | (south << 2);
            if (west != Wall) o |= Math.Min(63u, Tenths(OfficeChunkBuilder.ViewOpening(L, neighbour, true, x, z, 1).top)) << 4;
            if (south != Wall) o |= Math.Min(63u, Tenths(OfficeChunkBuilder.ViewOpening(L, neighbour, false, z, x, 1).top)) << 10;
            o |= Tenths(room.ceiling) << 16;
            if (L.Has(x, z, CellFlags.NoFloor)) o |= NoFloor;
            if (L.Has(x, z, CellFlags.OpenAbove)) o |= OpenAbove;
            return o;
        }

        public static uint WestKind(uint o) => o & 3u;
        public static uint SouthKind(uint o) => (o >> 2) & 3u;
        public static float WestTop(uint o) => ((o >> 4) & 63u) * 0.1f;
        public static float SouthTop(uint o) => ((o >> 10) & 63u) * 0.1f;
        public static float Ceiling(uint o) => ((o >> 16) & 255u) * 0.1f;
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
                if (kind == Wall) return false;
                float top = (stepX ? WestTop(o) : SouthTop(o)) + storey * pitch;
                if (y > top + 0.02f) return false;
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
            if (L.dir.sqrMagnitude > 0.5f) att *= LightLayers.AngleAttenuation(Vector3.Dot(L.dir, -toLight), L.cosOuter, L.cosInner);
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
