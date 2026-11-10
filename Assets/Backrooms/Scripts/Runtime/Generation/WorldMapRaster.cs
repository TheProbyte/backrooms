using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Draws a top-down map of generated chunks into a pixel buffer (row 0 = south, so +Z is up).
    /// Used by the World Map editor window. Pure CPU, works in edit and play mode.
    /// </summary>
    public static class WorldMapRaster
    {
        public enum Mode { Zone, Biome, District, Rooms, LightLayers, LightField }

        const int N = OfficeBiome.ChunkCells;

        /// <summary>Optional light-field value per global cell (0..1+) for <see cref="Mode.LightField"/>.</summary>
        public delegate float CellValue(Vector2Int globalCell);

        public static Color32[] Draw(WorldGenerator gen, RectInt chunks, Mode mode, int px, ICollection<Vector2Int> marks = null, CellValue field = null)
        {
            int W = chunks.width * N * px, H = chunks.height * N * px;
            var buf = new Color32[W * H];
            for (int cx = chunks.xMin; cx < chunks.xMax; cx++)
            for (int cz = chunks.yMin; cz < chunks.yMax; cz++)
            {
                var coord = new Vector2Int(cx, cz);
                var L = gen.Chunk(coord);
                var sample = gen.Sample(coord);
                int ox = (cx - chunks.xMin) * N * px, oz = (cz - chunks.yMin) * N * px;
                var lit = new HashSet<Vector2Int>();
                foreach (var f in L.fixtures) if (f.on) lit.Add(f.cell);
                for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                {
                    var room = L.RoomAt(x, z);
                    var g = new Vector2Int(cx * N + x, cz * N + z);
                    Color c = CellColor(gen, mode, L, sample, room, x, z, g, field);
                    Fill(buf, W, ox + x * px, oz + z * px, px, px, c);
                    if (px >= 4 && lit.Contains(new Vector2Int(x, z)) && mode != Mode.LightField)
                        Fill(buf, W, ox + x * px + px / 2 - 1, oz + z * px + px / 2 - 1, 2, 2, new Color(1f, 0.97f, 0.75f));
                }
                // Edges: walls dark, doors/headers as a gap with a light tick.
                var wall = new Color(0.08f, 0.07f, 0.06f);
                for (int x = 0; x <= N; x++)
                for (int z = 0; z < N; z++)
                {
                    var e = L.vEdges[x, z];
                    if (e == EdgeKind.Open) continue;
                    int sx = ox + x * px - (x == N ? 1 : 0);
                    if (e == EdgeKind.Wall) Fill(buf, W, sx, oz + z * px, 1, px, wall);
                    else Fill(buf, W, sx, oz + z * px, 1, 1, wall);
                }
                for (int x = 0; x < N; x++)
                for (int z = 0; z <= N; z++)
                {
                    var e = L.hEdges[x, z];
                    if (e == EdgeKind.Open) continue;
                    int sz = oz + z * px - (z == N ? 1 : 0);
                    if (e == EdgeKind.Wall) Fill(buf, W, ox + x * px, sz, px, 1, wall);
                    else Fill(buf, W, ox + x * px, sz, 1, 1, wall);
                }
            }
            if (marks != null)
                foreach (var g in marks)
                {
                    int x = (g.x - chunks.xMin * N) * px, z = (g.y - chunks.yMin * N) * px;
                    if (x < 0 || z < 0 || x >= W || z >= H) continue;
                    var red = new Color(1f, 0.1f, 0.1f);
                    for (int i = 0; i < px; i++) { Set(buf, W, H, x + i, z + i, red); Set(buf, W, H, x + px - 1 - i, z + i, red); }
                }
            return buf;
        }

        static Color CellColor(WorldGenerator gen, Mode mode, OfficeLayout L, WorldGenSettings.Sample s, Room room, int x, int z, Vector2Int g, CellValue field)
        {
            float shade = room.lights == LightMode.Dark ? 0.55f : room.lights == LightMode.Mixed ? 0.8f : 1f;
            switch (mode)
            {
                case Mode.Zone:
                {
                    var zr = gen.Settings.zones[Mathf.Clamp(s.zoneIndex, 0, gen.Settings.zones.Count - 1)];
                    return zr.mapColor * (s.zoneEdge ? 0.6f : 1f);
                }
                case Mode.Biome:
                    return s.mapColor * (room.Walkable ? 1f : 0.5f);
                case Mode.District:
                {
                    var d = DistrictOf(L.coord);
                    return Hashed(d.x * 92821 ^ d.y * 68917, 0.35f, 0.85f) * (((L.coord.x + L.coord.y) & 1) == 0 ? 1f : 0.9f);
                }
                case Mode.Rooms:
                {
                    Color k = room.kind switch
                    {
                        RoomKind.Hallway => new Color(0.55f, 0.66f, 0.82f),
                        RoomKind.BigOffice => new Color(0.86f, 0.78f, 0.5f),
                        RoomKind.Lounge => new Color(0.82f, 0.56f, 0.5f),
                        RoomKind.Conference => new Color(0.56f, 0.76f, 0.56f),
                        RoomKind.Crawl => new Color(0.3f, 0.3f, 0.3f),
                        _ => new Color(0.78f, 0.74f, 0.6f),
                    };
                    if (room.Tall) k = Color.Lerp(k, Color.white, 0.25f);
                    if (room.landmark != null) k = Color.Lerp(k, new Color(0.95f, 0.35f, 0.9f), 0.6f);
                    if (room.noFloor || room.openAbove) k = Color.Lerp(k, new Color(0.3f, 0.9f, 1f), 0.5f);
                    return k * shade;
                }
                case Mode.LightLayers:
                {
                    uint m = LightLayers.LayerBit(room.color) & ~1u;
                    return Hashed(unchecked((int)(m * 2654435761u)), 0.55f, 0.9f) * shade;
                }
                case Mode.LightField:
                {
                    float v = field != null ? field(g) : 0f;
                    v = 1f - Mathf.Exp(-v);
                    return new Color(v, v * 0.95f, v * 0.8f);
                }
            }
            return Color.magenta;
        }

        /// <summary>District of a chunk (4 x 4 chunks).</summary>
        public static Vector2Int DistrictOf(Vector2Int chunk) => WorldGenerator.DistrictOf(chunk);

        static Color Hashed(int h, float sat, float val)
        {
            unchecked { uint u = (uint)h; u ^= u >> 15; u *= 0x2C1B3C6Du; u ^= u >> 12; h = (int)u; }
            return Color.HSVToRGB((h & 0xFFFF) / 65535f, sat, val);
        }

        static void Fill(Color32[] buf, int W, int x0, int y0, int w, int h, Color c)
        {
            Color32 c32 = c;
            int H = buf.Length / W;
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y0 + h); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x0 + w); x++)
                buf[y * W + x] = c32;
        }

        static void Set(Color32[] buf, int W, int H, int x, int y, Color c)
        {
            if (x >= 0 && y >= 0 && x < W && y < H) buf[y * W + x] = c;
        }
    }
}
