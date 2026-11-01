using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Pool-style chunks (the Poolrooms). Walls, doorways, soffits, posts and crawl holes are the office builder's, in
    /// tiles; this adds what differs: floors at two levels (dry deck and sunken basin) with the walls carried down into
    /// the basins, still water over the basins, stairs where a doorway steps down into water, ceilings with roof openings
    /// and their light wells up to the sky, round columns, and the per-cell map the pool shaders read (walls, ceilings,
    /// openings, water) together with the bounce light baked from where the sun lands.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        bool Pool => _L != null && _L.style == ZoneStyle.Pool;
        bool NoBaseboard => (_a.noBaseboard && !_roomBaseboard) || Pool;
        /// <summary>The wall faces being built are a home's (they have baseboards; set per face in EdgeFaces).</summary>
        bool _roomBaseboard;
        /// <summary>The baseboard's material: a home's is painted off-white (as in the reference photos), the rest the world's.</summary>
        Material BaseboardMat => _roomBaseboard && MaterialMaps.HomeBaseboard ? MaterialMaps.HomeBaseboard : _a.baseboard;
        Material TileMat => PoolMaterials.Tile ? PoolMaterials.Tile : (_a.paintedWall ? _a.paintedWall : _a.wallpaper);
        Material WaterMat => PoolMaterials.Water ? PoolMaterials.Water : TileMat;
        Material SkyMat => PoolMaterials.Sky ? PoolMaterials.Sky : TileMat;

        /// <summary>Pool chunks: per cell map for the shaders (see PoolWorld), row-major [z * N + x].</summary>
        public Color32[] poolMap;
        readonly List<(Vector3 c, float r, float h)> _capsules = new List<(Vector3, float, float)>();

        const float BD_POOL = PoolBiome.BasinDepth;

        /// <summary>Pool cell here or in a neighbouring chunk (null: unknown or not a pool chunk).</summary>
        PoolCell? PoolAt(int x, int z, out OfficeLayout owner, out int lx, out int lz)
        {
            int N = _L.N;
            int dx = x < 0 ? -1 : x >= N ? 1 : 0, dz = z < 0 ? -1 : z >= N ? 1 : 0;
            lx = x - dx * N; lz = z - dz * N;
            owner = null;
            if (lx < 0 || lz < 0 || lx >= N || lz >= N) return null;          // beyond the neighbouring chunks
            owner = dx == 0 && dz == 0 ? _L : Neighbour(dx, dz);
            if (owner?.pool == null) return null;
            return owner.pool[lx, lz];
        }

        /// <summary>Is there a high window in this edge (it belongs to the cell on its +X / +Z side, maybe next door)?</summary>
        bool PoolWindowAt(bool vertical, int line, int along)
        {
            var c = vertical ? PoolAt(line, along, out _, out _, out _) : PoolAt(along, line, out _, out _, out _);
            return c.HasValue && (c.Value.windows & (vertical ? 1 : 2)) != 0;
        }

        /// <summary>
        /// One side of a wall with a high window (PoolPlan.Windows): the wall below, above and either side of it, this side's
        /// half of its reveal (sill, head, jambs) and, from the +X / +Z side only (it shows both ways), the sky in it. False
        /// if the ceilings leave no room for it (then it is a plain wall).
        /// </summary>
        bool WindowFaces(Room room, uint cm, bool vertical, float l, float f, float a0, float a1, int along, Vector3 n, float myCeil, float otherCeil, bool drawSky)
        {
            float sill = PoolBiome.WindowSill, top = PoolBiome.WindowTop(myCeil, otherCeil);
            if (top - sill < 0.6f) return false;
            float w0 = along * C + PoolBiome.WindowInset, w1 = (along + 1) * C - PoolBiome.WindowInset;
            Vector3 A(float pos, float at, float y) => (vertical ? new Vector3(pos, 0, at) : new Vector3(at, 0, pos)) + Vector3.up * y;
            WallFace(room, cm, A(f, a0, 0), A(f, a1, 0), room.floor, sill, n);
            WallFace(room, cm, A(f, a0, 0), A(f, a1, 0), top, myCeil, n);
            WallFace(room, cm, A(f, a0, 0), A(f, w0, 0), sill, top, n);
            WallFace(room, cm, A(f, w1, 0), A(f, a1, 0), sill, top, n);
            var w = WMask(WallMat(room), cm);
            Vector3 alongDir = vertical ? Vector3.forward : Vector3.right;
            w.Quad(A(l, w0, sill), A(f, w0, sill), A(f, w1, sill), A(l, w1, sill), Vector3.up, MeshWriter.MetreUV);
            w.Quad(A(l, w0, top), A(f, w0, top), A(f, w1, top), A(l, w1, top), Vector3.down, MeshWriter.MetreUV);
            w.Quad(A(l, w0, sill), A(f, w0, sill), A(f, w0, top), A(l, w0, top), alongDir, MeshWriter.MetreUV);
            w.Quad(A(l, w1, sill), A(f, w1, sill), A(f, w1, top), A(l, w1, top), -alongDir, MeshWriter.MetreUV);
            if (drawSky) WMask(SkyMat, 1u).Quad(A(l, w0, sill), A(l, w1, sill), A(l, w1, top), A(l, w0, top), n, MeshWriter.MetreUV);
            return true;
        }

        /// <summary>Floor of a cell (deck 0, basin below), here or next door; null if unknown.</summary>
        float? FloorAt(int x, int z)
        {
            var c = PoolAt(x, z, out _, out _, out _);
            return c.HasValue ? PoolBiome.FloorY(c.Value.basin) : (float?)null;
        }

        /// <summary>Same room of one district plan (rooms are numbered per district).</summary>
        static bool SameRoom(OfficeLayout a, int ax, int az, OfficeLayout b, int bx, int bz) =>
            a.RoomAt(ax, az).globalId == b.RoomAt(bx, bz).globalId && WorldGenerator.DistrictOf(a.coord) == WorldGenerator.DistrictOf(b.coord);

        int StairSideOf(int x, int z) => PoolPlan.StairSide(_L, x, z, (nx, nz) =>
        {
            var c = PoolAt(nx, nz, out var owner, out int lx, out int lz);
            if (!c.HasValue) return null;
            if (SameRoom(owner, lx, lz, _L, x, z)) return false;
            return !c.Value.basin;
        });

        void BuildPoolData()
        {
            BuildPoolFloors();
            BuildPoolCeilings();
            BuildWalls();
            BuildPosts();
            BuildPoolPillars();
            BuildPoolFillets();
            BuildPoolArcs();
            foreach (var r in _L.rooms)
                if (r.spiral && !r.noFloor && r.rect.width == r.fullRect.width && r.rect.height == r.fullRect.height) BuildSpiral(r);
            BuildDoorways();
            BuildPortals();
            BuildNoclipDoors();
            BuildCeilingColliders();
            BuildPoolMap();
        }

        // ---------------- Floors, water, stairs ----------------

        void BuildPoolFloors()
        {
            int N = _L.N;
            var tile = WMask(TileMat, 1u);
            var water = WMask(WaterMat, 1u);
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                if (_L.Has(x, z, CellFlags.NoFloor)) continue;            // a stair well from the storey below
                bool basin = _L.Basin(x, z);
                float y = PoolBiome.FloorY(basin), x0 = x * C, z0 = z * C, x1 = x0 + C, z1 = z0 + C;
                tile.Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up, MeshWriter.MetreUV);
                if (!basin) continue;
                float wy = PoolBiome.WaterLevel;
                water.Quad(new Vector3(x0, wy, z0), new Vector3(x0, wy, z1), new Vector3(x1, wy, z1), new Vector3(x1, wy, z0), Vector3.up, MeshWriter.MetreUV);
                int side = StairSideOf(x, z);
                if (side >= 0) Stairs(tile, x, z, side);
            }

            // Floor colliders: runs of one level per row; the deck's are deep enough to make the basin's edges solid.
            for (int z = 0; z < N; z++)
            {
                int start = 0;
                int Kind(int x) => _L.Has(x, z, CellFlags.NoFloor) ? 2 : _L.Basin(x, z) ? 1 : 0;
                for (int x = 1; x <= N; x++)
                {
                    if (x < N && Kind(x) == Kind(start)) continue;
                    if (Kind(start) != 2)
                    {
                        bool basin = _L.Basin(start, z);
                        float top = PoolBiome.FloorY(basin), depth = basin ? 0.3f : BD_POOL + 0.3f;
                        _boxes.Add((new Vector3((start + x) * 0.5f * C, top - depth * 0.5f, (z + 0.5f) * C), new Vector3((x - start) * C, depth, C)));
                    }
                    start = x;
                }
            }
        }

        /// <summary>Three steps up out of a basin to the deck on the given side, filling the cell's width.</summary>
        void Stairs(MeshWriter w, int x, int z, int side)
        {
            float rise = BD_POOL / PoolBiome.Steps;
            Vector3 edgeMid = new Vector3((x + 0.5f) * C, 0f, (z + 0.5f) * C);
            Vector3 outDir = side == 0 ? Vector3.left : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.forward;
            edgeMid += outDir * (C * 0.5f);
            bool alongZ = side < 2;   // the edge runs along Z
            for (int k = 1; k < PoolBiome.Steps; k++)
            {
                // Step k: from the basin floor up to (k) risers, reaching (Steps - k) runs out from the edge.
                float run = (PoolBiome.Steps - k) * PoolBiome.StepRun, top = -BD_POOL + rise * k;
                var c = edgeMid - outDir * (run * 0.5f) + Vector3.up * ((-BD_POOL + top) * 0.5f);
                var s = alongZ ? new Vector3(run, top + BD_POOL, C) : new Vector3(C, top + BD_POOL, run);
                int faces = 8 | (side == 0 ? 2 : side == 1 ? 1 : side == 2 ? 32 : 16) | (alongZ ? 16 | 32 : 1 | 2);
                w.Box(c, s, faces, MeshWriter.MetreUV);
                _boxes.Add((c, s));
            }
        }

        /// <summary>
        /// A basin cell's share of the face below the deck on one edge: the wall carried down to the basin floor, or the
        /// pool edge where a dry deck (or a raised walkway) is beside it.
        /// </summary>
        void BasinEdge(EdgeKind e, bool vertical, int line, int along, int s, Vector3 n, float f)
        {
            int cx = vertical ? (s == 0 ? line - 1 : line) : along, cz = vertical ? along : (s == 0 ? line - 1 : line);
            int ox = vertical ? (s == 0 ? line : line - 1) : along, oz = vertical ? along : (s == 0 ? line : line - 1);
            var tile = WMask(TileMat, 1u);
            Vector3 A(float pos, float at) => vertical ? new Vector3(pos, 0, at) : new Vector3(at, 0, pos);
            if (e == EdgeKind.Wall)
            {
                float a0 = along * C + T * 0.5f, a1 = (along + 1) * C - T * 0.5f;
                tile.Quad(A(f, a0) + Vector3.down * BD_POOL, A(f, a1) + Vector3.down * BD_POOL, A(f, a1), A(f, a0), n, MeshWriter.MetreUV);
                return;
            }
            float? other = FloorAt(ox, oz);
            if (other.HasValue && other.Value < -0.01f) return;      // water continues
            float l = line * C, b0 = along * C, b1 = (along + 1) * C;
            tile.Quad(A(l, b0) + Vector3.down * BD_POOL, A(l, b1) + Vector3.down * BD_POOL, A(l, b1), A(l, b0), n, MeshWriter.MetreUV);
        }

        // ---------------- Ceilings, roof openings, light wells, sky ----------------

        SkylightShape SkyAt(int x, int z)
        {
            var c = PoolAt(x, z, out _, out _, out _);
            return c.HasValue ? c.Value.skylight : SkylightShape.None;
        }

        /// <summary>Joined sides, only within one room (an opening never runs over a wall).</summary>
        int JoinedAt(int x, int z) => Info(x, z).joined;

        void BuildPoolCeilings()
        {
            int N = _L.N;
            var tile = WMask(TileMat, 1u);
            var sky = WMask(SkyMat, 1u);
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                var r = _L.RoomAt(x, z);
                if (_L.Has(x, z, CellFlags.OpenAbove)) continue;           // a stair tower: the storey above closes it
                float h = r.ceiling, x0 = x * C, z0 = z * C;
                var shape = _L.pool[x, z].skylight;
                Vector3 P(float u, float v, float y) => new Vector3(x0 + u, y, z0 + v);
                if (shape == SkylightShape.None)
                {
                    tile.Quad(P(0, 0, h), P(C, 0, h), P(C, C, h), P(0, C, h), Vector3.down, MeshWriter.MetreUV);
                    continue;
                }
                int joined = JoinedAt(x, z);
                float top = h + PoolBiome.ShaftHeight;
                if (shape == SkylightShape.Round)
                {
                    RoundOpening(tile, sky, x0, z0, h, top);
                    continue;
                }
                var hole = PoolBiome.HoleRect(shape, joined);
                // Ceiling around the opening (up to four strips).
                void Strip(float u0, float v0, float u1, float v1) { if (u1 - u0 > 0.001f && v1 - v0 > 0.001f) tile.Quad(P(u0, v0, h), P(u1, v0, h), P(u1, v1, h), P(u0, v1, h), Vector3.down, MeshWriter.MetreUV); }
                Strip(0, 0, C, hole.yMin);
                Strip(0, hole.yMax, C, C);
                Strip(0, hole.yMin, hole.xMin, hole.yMax);
                Strip(hole.xMax, hole.yMin, C, hole.yMax);
                // Light well walls on the sides that do not continue into the next cell's opening.
                void Well(Vector3 a, Vector3 b, Vector3 n) => tile.Quad(a + Vector3.up * h, b + Vector3.up * h, b + Vector3.up * top, a + Vector3.up * top, n, MeshWriter.MetreUV);
                if (hole.xMin > 0.001f || (joined & 1) == 0) Well(P(hole.xMin, hole.yMin, 0), P(hole.xMin, hole.yMax, 0), Vector3.right);
                if (hole.xMax < C - 0.001f || (joined & 2) == 0) Well(P(hole.xMax, hole.yMin, 0), P(hole.xMax, hole.yMax, 0), Vector3.left);
                if (hole.yMin > 0.001f || (joined & 4) == 0) Well(P(hole.xMin, hole.yMin, 0), P(hole.xMax, hole.yMin, 0), Vector3.forward);
                if (hole.yMax < C - 0.001f || (joined & 8) == 0) Well(P(hole.xMin, hole.yMax, 0), P(hole.xMax, hole.yMax, 0), Vector3.back);
                // The sky, closing the top of the well.
                sky.Quad(P(hole.xMin, hole.yMin, top), P(hole.xMax, hole.yMin, top), P(hole.xMax, hole.yMax, top), P(hole.xMin, hole.yMax, top), Vector3.down, MeshWriter.MetreUV);
            }
        }

        /// <summary>A round opening: the ceiling as a square ring, a round well, and a sky disc.</summary>
        void RoundOpening(MeshWriter tile, MeshWriter sky, float x0, float z0, float h, float top)
        {
            const int K = 32;
            float r = PoolBiome.RoundRadius, cx = x0 + C * 0.5f, cz = z0 + C * 0.5f;
            float circ = 2f * Mathf.PI * r;
            for (int i = 0; i < K; i++)
            {
                float a0 = i * Mathf.PI * 2f / K, a1 = (i + 1) * Mathf.PI * 2f / K;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 c0 = new Vector3(cx, h, cz) + d0 * r, c1 = new Vector3(cx, h, cz) + d1 * r;
                // Ceiling: from the circle out to the cell's square edge along the same directions.
                Vector3 s0 = new Vector3(cx, h, cz) + d0 * (C * 0.5f / Mathf.Max(Mathf.Abs(d0.x), Mathf.Abs(d0.z)));
                Vector3 s1 = new Vector3(cx, h, cz) + d1 * (C * 0.5f / Mathf.Max(Mathf.Abs(d1.x), Mathf.Abs(d1.z)));
                tile.Quad(c0, s0, s1, c1, Vector3.down, MeshWriter.MetreUV);
                // Well: faces the axis, tiles wrap around it.
                float u0 = circ * i / K, u1 = circ * (i + 1) / K;
                tile.SmoothQuad(c0, c1, c1 + Vector3.up * (top - h), c0 + Vector3.up * (top - h), -d0, -d1, -d1, -d0,
                    new Vector2(u0, h), new Vector2(u1, h), new Vector2(u1, top), new Vector2(u0, top));
                sky.Quad(new Vector3(cx, top, cz), c0 + Vector3.up * (top - h), c1 + Vector3.up * (top - h), c1 + Vector3.up * (top - h), Vector3.down, MeshWriter.MetreUV);
            }
        }

        // ---------------- Columns ----------------

        void BuildPoolPillars()
        {
            const int K = 32;
            var tile = WMask(TileMat, 1u);
            float r = PoolBiome.PillarRadius;
            // A whole number of tiles around (about 15 cm each), so the joints meet.
            int around = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * r / PoolBiome.TileSize));
            float tileW = PoolBiome.TileSize;
            foreach (var p in _L.pillars)
            {
                float floor = 0f;
                for (int k = 0; k < 4; k++) floor = Mathf.Min(floor, FloorAt(p.vertex.x - (k & 1), p.vertex.y - (k >> 1)) ?? 0f);
                var room = _L.RoomAt(Mathf.Min(p.vertex.x, _L.N - 1), Mathf.Min(p.vertex.y, _L.N - 1));
                float h = room.ceiling;
                var c = new Vector3(p.vertex.x * C, 0f, p.vertex.y * C);
                for (int i = 0; i < K; i++)
                {
                    float a0 = i * Mathf.PI * 2f / K, a1 = (i + 1) * Mathf.PI * 2f / K;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    // u in metres of tile: around * tileW per turn, offset so a joint falls on the grid (see the shader).
                    float u0 = around * tileW * i / K + tileW * 0.5f, u1 = around * tileW * (i + 1) / K + tileW * 0.5f;
                    Vector3 b0 = c + d0 * r + Vector3.up * floor, b1 = c + d1 * r + Vector3.up * floor;
                    tile.SmoothQuad(b0, b1, b1 + Vector3.up * (h - floor), b0 + Vector3.up * (h - floor), d0, d1, d1, d0,
                        new Vector2(u0, floor), new Vector2(u1, floor), new Vector2(u1, h), new Vector2(u0, h));
                }
                _capsules.Add((c + Vector3.up * ((floor + h) * 0.5f), r, h - floor));
            }
        }

        // ---------------- The shaders' map and the baked bounce light ----------------

        /// <summary>Cells of the 3 x 3 chunks around this one, for the sun test (local cell coordinates).</summary>
        sealed class Window : IPoolCells
        {
            public OfficeChunkBuilder b;
            public PoolCellInfo Get(int x, int z) => b.Info(x, z);
        }

        PoolCellInfo Info(int x, int z)
        {
            var pc = PoolAt(x, z, out var owner, out int lx, out int lz);
            if (!pc.HasValue) return default;
            var info = new PoolCellInfo
            {
                valid = true, ceiling = owner.RoomAt(lx, lz).ceiling, sky = pc.Value.skylight, basin = pc.Value.basin,
                west = EdgeCode(owner, owner.vEdges[lx, lz], true, lx, lz), south = EdgeCode(owner, owner.hEdges[lx, lz], false, lz, lx),
            };
            if ((pc.Value.windows & 1) != 0 && info.west == PoolSun.Wall) info.west = PoolSun.Window;
            if ((pc.Value.windows & 2) != 0 && info.south == PoolSun.Wall) info.south = PoolSun.Window;
            if (info.sky != SkylightShape.None)
                info.joined = PoolBiome.Joined((a, b) =>
                {
                    var c = PoolAt(a, b, out var o2, out int ax, out int az);
                    return c.HasValue && SameRoom(o2, ax, az, owner, lx, lz) ? c.Value.skylight : SkylightShape.None;
                }, x, z);
            foreach (var p in owner.pillars)
            {
                if (p.vertex.x == lx && p.vertex.y == lz) info.pillar = true;
                if (p.vertex.x >= lx && p.vertex.x <= lx + 1 && p.vertex.y >= lz && p.vertex.y <= lz + 1) info.pillarNear = true;
            }
            // Columns on this cell's +X / +Z corners are listed by the neighbouring chunk.
            if (!info.pillarNear && (lx == owner.N - 1 || lz == owner.N - 1))
                for (int k = 1; k < 4 && !info.pillarNear; k++)
                {
                    var o = PoolAt(x + (k & 1), z + (k >> 1), out var on, out int vx, out int vz);
                    if (o.HasValue) foreach (var p in on.pillars) if (p.vertex.x == vx && p.vertex.y == vz) { info.pillarNear = true; break; }
                }
            return info;
        }

        static byte EdgeCode(OfficeLayout L, EdgeKind e, bool vertical, int line, int along) =>
            e == EdgeKind.Wall ? PoolSun.Wall : e == EdgeKind.DoorHeader ? PoolSun.Header : PoolSun.Open;

        void BuildPoolMap()
        {
            int N = _L.N, W = N * 3;
            var g = new Window { b = this };
            // Window of 3 x 3 chunks around this one: cell (i - N, j - N).
            var info = new PoolCellInfo[W, W];
            for (int i = 0; i < W; i++) for (int j = 0; j < W; j++) info[i, j] = Info(i - N, j - N);

            // Light sources per cell: sun on the floor and on its walls, and sky through the roof.
            var src = new float[W, W];
            for (int i = 0; i < W; i++)
            for (int j = 0; j < W; j++)
            {
                var c = info[i, j];
                if (!c.valid) continue;
                float fy = PoolBiome.FloorY(c.basin) + 0.02f, x0 = (i - N) * C, z0 = (j - N) * C;
                float sun = 0f;
                for (int k = 0; k < 4; k++) sun += PoolSun.Visibility(g, new Vector3(x0 + C * (0.25f + 0.5f * (k & 1)), fy, z0 + C * (0.25f + 0.5f * (k >> 1))));
                sun *= 0.25f;
                // Sun on the walls of the cell (bars of light from slots near a wall bounce as well).
                float wallSun = 0f;
                if (PoolSun.Solid(c.west)) wallSun += PoolSun.Visibility(g, new Vector3(x0 + T * 0.5f + 0.02f, 1.4f, z0 + C * 0.5f));
                if (PoolSun.Solid(c.south)) wallSun += PoolSun.Visibility(g, new Vector3(x0 + C * 0.5f, 1.4f, z0 + T * 0.5f + 0.02f));
                if (i + 1 < W && PoolSun.Solid(info[i + 1, j].west)) wallSun += PoolSun.Visibility(g, new Vector3(x0 + C - T * 0.5f - 0.02f, 1.4f, z0 + C * 0.5f));
                if (j + 1 < W && PoolSun.Solid(info[i, j + 1].south)) wallSun += PoolSun.Visibility(g, new Vector3(x0 + C * 0.5f, 1.4f, z0 + C - T * 0.5f - 0.02f));
                float skyOpen = c.sky == SkylightShape.None ? 0f : c.sky == SkylightShape.Full ? 0.7f : c.sky == SkylightShape.Round ? 0.45f : 0.25f;
                src[i, j] = sun + 0.6f * wallSun + 0.5f * skyOpen;
            }

            // Spread it through open edges (walls keep it in, and send some back).
            var e = (float[,])src.Clone();
            var next = new float[W, W];
            const float spread = 0.9f, wallBack = 0.55f;
            for (int it = 0; it < 18; it++)
            {
                for (int i = 0; i < W; i++)
                for (int j = 0; j < W; j++)
                {
                    if (!info[i, j].valid) { next[i, j] = 0f; continue; }
                    float sum = 0f;
                    for (int s = 0; s < 4; s++)
                    {
                        int ni = i + (s == 0 ? -1 : s == 1 ? 1 : 0), nj = j + (s == 2 ? -1 : s == 3 ? 1 : 0);
                        byte edge = s == 0 ? info[i, j].west : s == 1 ? (ni < W ? info[ni, j].west : PoolSun.Wall)
                                  : s == 2 ? info[i, j].south : (nj < W ? info[i, nj].south : PoolSun.Wall);
                        bool open = ni >= 0 && nj >= 0 && ni < W && nj < W && info[ni, nj].valid && !PoolSun.Solid(edge);
                        sum += open ? e[ni, nj] : e[i, j] * wallBack;
                    }
                    next[i, j] = src[i, j] + spread * sum * 0.25f;
                }
                var t = e; e = next; next = t;
            }

            poolMap = new Color32[N * N];
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                var c = info[x + N, z + N];
                byte flags = (byte)(c.west | (c.south << 2) | (c.basin ? 16 : 0) | (c.pillar ? 32 : 0) | 64 | (c.pillarNear ? 128 : 0));
                byte ceil = (byte)Mathf.Clamp(Mathf.RoundToInt(c.ceiling * 10f), 0, 255);
                byte sky = (byte)((int)c.sky | (c.joined << 3));
                float fill = 1f - Mathf.Exp(-e[x + N, z + N] * 0.7f);
                poolMap[z * N + x] = new Color32(flags, ceil, sky, (byte)Mathf.Clamp(Mathf.RoundToInt(fill * 255f), 0, 255));
            }
        }
    }
}
