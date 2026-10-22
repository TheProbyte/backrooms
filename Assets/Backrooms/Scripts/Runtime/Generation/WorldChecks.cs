using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Whole-world consistency checks over a block of chunks: border agreement, reachability and light
    /// leaks. Used by the edit-mode tests and drawn by the World Map window. Cells are global cell
    /// coordinates (chunk * N + local).
    /// </summary>
    public static class WorldChecks
    {
        const int N = OfficeBiome.ChunkCells;
        const float C = OfficeBiome.CellSize;

        public struct Issue
        {
            public Vector2Int cell;      // global cell where it shows
            public Vector2Int from;      // global cell of the source (light leaks), else = cell
            public bool crossChunk;      // involves two chunks
            public string what;
            public override string ToString() => $"{what} at cell {cell} (from {from}){(crossChunk ? " [chunk border]" : "")}";
        }

        static Vector2Int ChunkOfCell(Vector2Int g) => new Vector2Int(FloorDiv(g.x, N), FloorDiv(g.y, N));
        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>Edge between global cell g and its neighbour on side (0 -X, 1 +X, 2 -Z, 3 +Z), both chunks' copies combined.</summary>
        public static EdgeKind EdgeBetween(WorldGenerator gen, Vector2Int g, int side)
        {
            var ch = ChunkOfCell(g);
            var L = gen.Chunk(ch);
            int x = g.x - ch.x * N, z = g.y - ch.y * N;
            var mine = L.EdgeOf(x, z, side);
            bool border = (side == 0 && x == 0) || (side == 1 && x == N - 1) || (side == 2 && z == 0) || (side == 3 && z == N - 1);
            if (!border) return mine;
            var o = g + Step(side);
            var oc = ChunkOfCell(o);
            var theirs = gen.Chunk(oc).EdgeOf(o.x - oc.x * N, o.y - oc.y * N, side ^ 1);
            return OfficeChunkBuilder.Combine(mine, theirs);
        }

        public static Vector2Int Step(int side) => side == 0 ? Vector2Int.left : side == 1 ? Vector2Int.right : side == 2 ? Vector2Int.down : Vector2Int.up;

        public static Room RoomAt(WorldGenerator gen, Vector2Int g)
        {
            var ch = ChunkOfCell(g);
            return gen.Chunk(ch).RoomAt(g.x - ch.x * N, g.y - ch.y * N);
        }

        // ---------------- Borders ----------------

        /// <summary>
        /// Both chunks of every border in the block must agree: same solid/passable state on every edge,
        /// and the same opening top seen from either side.
        /// </summary>
        public static List<Issue> BorderMismatches(WorldGenerator gen, RectInt chunks)
        {
            var list = new List<Issue>();
            for (int cx = chunks.xMin; cx < chunks.xMax; cx++)
            for (int cz = chunks.yMin; cz < chunks.yMax; cz++)
            {
                var c = new Vector2Int(cx, cz);
                var L = gen.Chunk(c);
                var east = gen.Chunk(c + Vector2Int.right);
                var north = gen.Chunk(c + Vector2Int.up);
                OfficeLayout NbL(int dx, int dz) => gen.Chunk(new Vector2Int(cx + dx, cz + dz));
                OfficeLayout NbE(int dx, int dz) => gen.Chunk(new Vector2Int(cx + 1 + dx, cz + dz));
                OfficeLayout NbN(int dx, int dz) => gen.Chunk(new Vector2Int(cx + dx, cz + 1 + dz));
                for (int i = 0; i < N; i++)
                {
                    Compare(list, L.vEdges[N, i], east.vEdges[0, i], new Vector2Int(cx * N + N - 1, cz * N + i), "east");
                    if (OfficeLayout.IsPassable(L.vEdges[N, i]) && OfficeLayout.IsPassable(east.vEdges[0, i]))
                    {
                        float a = OfficeChunkBuilder.ViewOpening(L, NbL, true, N, i, 0).top;
                        float b = OfficeChunkBuilder.ViewOpening(east, NbE, true, 0, i, 1).top;
                        if (Mathf.Abs(a - b) > 0.001f)
                            list.Add(new Issue { cell = new Vector2Int(cx * N + N - 1, cz * N + i), crossChunk = true, what = $"east opening top {a:0.00} vs {b:0.00}" });
                    }
                    Compare(list, L.hEdges[i, N], north.hEdges[i, 0], new Vector2Int(cx * N + i, cz * N + N - 1), "north");
                    if (OfficeLayout.IsPassable(L.hEdges[i, N]) && OfficeLayout.IsPassable(north.hEdges[i, 0]))
                    {
                        float a = OfficeChunkBuilder.ViewOpening(L, NbL, false, N, i, 0).top;
                        float b = OfficeChunkBuilder.ViewOpening(north, NbN, false, 0, i, 1).top;
                        if (Mathf.Abs(a - b) > 0.001f)
                            list.Add(new Issue { cell = new Vector2Int(cx * N + i, cz * N + N - 1), crossChunk = true, what = $"north opening top {a:0.00} vs {b:0.00}" });
                    }
                }
            }
            return list;
        }

        static void Compare(List<Issue> list, EdgeKind a, EdgeKind b, Vector2Int cell, string where)
        {
            if (OfficeLayout.IsPassable(a) != OfficeLayout.IsPassable(b))
                list.Add(new Issue { cell = cell, from = cell, crossChunk = true, what = $"{where} border {a} vs {b}" });
        }

        // ---------------- Reachability ----------------

        /// <summary>
        /// Flood fill over the block from a walkable cell in its centre chunk. Returns walkable cells in the
        /// block (minus a one-chunk margin, which may only connect through chunks outside it) never reached.
        /// </summary>
        public static List<Issue> Unreachable(WorldGenerator gen, RectInt chunks)
        {
            var cells = new RectInt(chunks.xMin * N, chunks.yMin * N, chunks.width * N, chunks.height * N);
            var seen = new HashSet<Vector2Int>();
            var q = new Queue<Vector2Int>();
            var mid = new Vector2Int(chunks.xMin + chunks.width / 2, chunks.yMin + chunks.height / 2);
            for (int i = 0; i < N * N && q.Count == 0; i++)
            {
                var g = new Vector2Int(mid.x * N + i % N, mid.y * N + i / N);
                if (RoomAt(gen, g).Walkable) { q.Enqueue(g); seen.Add(g); }
            }
            while (q.Count > 0)
            {
                var g = q.Dequeue();
                for (int side = 0; side < 4; side++)
                {
                    var o = g + Step(side);
                    if (!cells.Contains(o) || seen.Contains(o)) continue;
                    if (!RoomAt(gen, o).Walkable || !OfficeLayout.IsPassable(EdgeBetween(gen, g, side))) continue;
                    seen.Add(o); q.Enqueue(o);
                }
            }
            var list = new List<Issue>();
            var inner = new RectInt((chunks.xMin + 1) * N, (chunks.yMin + 1) * N, (chunks.width - 2) * N, (chunks.height - 2) * N);
            for (int x = inner.xMin; x < inner.xMax; x++)
            for (int z = inner.yMin; z < inner.yMax; z++)
            {
                var g = new Vector2Int(x, z);
                if (RoomAt(gen, g).Walkable && !seen.Contains(g)) list.Add(new Issue { cell = g, from = g, what = "unreachable " + RoomAt(gen, g).kind });
            }
            return list;
        }

        // ---------------- Light leaks ----------------

        /// <summary>
        /// Light zones: cells joined by open (wall-less) edges, across chunk borders too. Returns a zone id
        /// per cell of the block (row-major in the block's cell rectangle).
        /// </summary>
        public static int[] LightZones(WorldGenerator gen, RectInt cells)
        {
            int W = cells.width, H = cells.height;
            var parent = new int[W * H];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int x = 0; x < W; x++)
            for (int z = 0; z < H; z++)
            {
                var g = new Vector2Int(cells.xMin + x, cells.yMin + z);
                if (x + 1 < W && EdgeBetween(gen, g, 1) == EdgeKind.Open) parent[Find(z * W + x)] = Find(z * W + x + 1);
                if (z + 1 < H && EdgeBetween(gen, g, 3) == EdgeKind.Open) parent[Find(z * W + x)] = Find((z + 1) * W + x);
            }
            for (int i = 0; i < parent.Length; i++) parent[i] = Find(i);
            return parent;
        }

        /// <summary>
        /// Every unshadowed light (ceiling fixture, its bounce fill, doorway lights) reaches surfaces within its
        /// range that carry its layer. A leak is such a surface outside the light's own zone: it would be lit
        /// through a wall. Only lights in the block's inner chunks are checked; cells anywhere in the block.
        /// </summary>
        public static List<Issue> LightLeaks(WorldGenerator gen, RectInt chunks)
        {
            var cells = new RectInt(chunks.xMin * N, chunks.yMin * N, chunks.width * N, chunks.height * N);
            var zone = LightZones(gen, cells);
            var masks = new Dictionary<Vector2Int, uint[,]>();
            for (int cx = chunks.xMin; cx < chunks.xMax; cx++)
            for (int cz = chunks.yMin; cz < chunks.yMax; cz++)
            {
                var c = new Vector2Int(cx, cz);
                masks[c] = LightLayers.CellMasks(gen.Chunk(c), (dx, dz) => gen.Chunk(new Vector2Int(c.x + dx, c.y + dz)));
            }
            uint MaskAt(Vector2Int g) { var ch = ChunkOfCell(g); return masks[ch][g.x - ch.x * N, g.y - ch.y * N]; }
            int ZoneAt(Vector2Int g) => zone[(g.y - cells.yMin) * cells.width + (g.x - cells.xMin)];

            var list = new List<Issue>();
            var seenPairs = new HashSet<(Vector2Int, int)>();
            void Check(Vector2 pos, float reach, uint bits, Vector2Int srcCell, string kind, Vector2? frontOf)
            {
                bits &= ~1u;
                int srcZone = ZoneAt(srcCell);
                int r = Mathf.CeilToInt(reach / C) + 1;
                var pc = new Vector2Int(Mathf.FloorToInt(pos.x / C), Mathf.FloorToInt(pos.y / C));
                for (int x = pc.x - r; x <= pc.x + r; x++)
                for (int z = pc.y - r; z <= pc.y + r; z++)
                {
                    var g = new Vector2Int(x, z);
                    if (!cells.Contains(g)) continue;
                    // Distance from the light to the nearest point of the cell.
                    float nx = Mathf.Clamp(pos.x, x * C, (x + 1) * C), nz = Mathf.Clamp(pos.y, z * C, (z + 1) * C);
                    if ((new Vector2(nx, nz) - pos).sqrMagnitude >= reach * reach) continue;
                    if (frontOf.HasValue && Vector2.Dot(new Vector2((x + 0.5f) * C, (z + 0.5f) * C) - pos, frontOf.Value) <= 0f) continue;
                    if ((MaskAt(g) & bits) == 0 || ZoneAt(g) == srcZone) continue;
                    int pairZone = ZoneAt(g);
                    if (!seenPairs.Add((srcCell, pairZone))) continue;      // one report per light and zone
                    list.Add(new Issue { cell = g, from = srcCell, crossChunk = ChunkOfCell(g) != ChunkOfCell(srcCell), what = kind + " light leaks into another zone" });
                }
            }

            var doors = new List<Doorway>();
            for (int cx = chunks.xMin + 1; cx < chunks.xMax - 1; cx++)
            for (int cz = chunks.yMin + 1; cz < chunks.yMax - 1; cz++)
            {
                var L = gen.Chunk(new Vector2Int(cx, cz));
                foreach (var f in L.fixtures)
                {
                    if (!f.on) continue;
                    var g = new Vector2Int(cx * N + f.cell.x, cz * N + f.cell.y);
                    var p = new Vector2((g.x + 0.5f) * C, (g.y + 0.5f) * C);
                    Check(p, LightLayers.FixtureRange(f), LightLayers.LayerBit(f.roomColor), g, "fixture", null);
                }
                foreach (var pr in L.props)
                {
                    if (pr.kind != PropKind.FloorLamp || !pr.lit) continue;
                    var p = new Vector2(cx * OfficeBiome.ChunkSize + pr.position.x, cz * OfficeBiome.ChunkSize + pr.position.y);
                    var g = new Vector2Int(Mathf.FloorToInt(p.x / C), Mathf.FloorToInt(p.y / C));
                    Check(p, LightLayers.LampRange, LightLayers.LayerBit(pr.roomColor), g, "lamp", null);
                }
                doors.Clear();
                OfficeChunkBuilder.ComputeDoorways(L, doors);
                foreach (var d in doors)
                {
                    var p = new Vector2(d.position.x, d.position.z) + new Vector2(d.inward.x, d.inward.z) * 0.05f;
                    var g = new Vector2Int(Mathf.FloorToInt(p.x / C), Mathf.FloorToInt(p.y / C));
                    Check(p, LightLayers.DoorwayRange, d.layer, g, "doorway", new Vector2(d.inward.x, d.inward.z));
                }
            }
            return list;
        }
    }
}
