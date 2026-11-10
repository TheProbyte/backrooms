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
        /// One side of a wall with a high window (PoolPlan.Windows, a clerestory): from the taller room, the wall below,
        /// above and either side of it, its reveal (sill, head, jambs) and the sky in it. False from the lower room (the
        /// window is above its ceiling) or if the ceilings leave no room for it: then it is a plain wall.
        /// </summary>
        bool WindowFaces(Room room, uint cm, bool vertical, float l, float f, float a0, float a1, int along, Vector3 n, float myCeil, float otherCeil)
        {
            float sill = PoolBiome.WindowSill(myCeil, otherCeil), top = PoolBiome.WindowTop(myCeil, otherCeil);
            if (myCeil <= otherCeil + 0.001f || !PoolBiome.WindowFits(myCeil, otherCeil)) return false;
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
            // Some are glass block (the same hash as PoolCommon.hlsl's PoolGlassBlock: the cell on the edge's +X / +Z side).
            int line = Mathf.RoundToInt(l / C);
            int gx = _L.coord.x * _L.N + (vertical ? line : along), gz = _L.coord.y * _L.N + (vertical ? along : line);
            var pane = GlassBlockAt(gx, gz, vertical ? 1u : 2u) && PoolMaterials.GlassBlock ? PoolMaterials.GlassBlock : SkyMat;
            WMask(pane, 1u).Quad(A(l, w0, sill), A(l, w1, sill), A(l, w1, top), A(l, w0, top), n, MeshWriter.MetreUV);
            return true;
        }

        static bool GlassBlockAt(int x, int z, uint side) => PoolBiome.GlassBlock(x, z, side);

        /// <summary>Floor of a cell (deck 0, basin below), here or next door; null if unknown.</summary>
        float? FloorAt(int x, int z)
        {
            var c = PoolAt(x, z, out _, out _, out _);
            if (!c.HasValue) return null;
            return c.Value.basin ? -(c.Value.depth > 0f ? c.Value.depth + 0.06f : PoolBiome.BasinDepth) : 0f;
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
            poolFittings.Clear(); poolFittingDirs.Clear(); poolRopes.Clear(); poolRopeEnds.Clear();
            BuildPoolFloors();
            BuildPoolCeilings();
            BuildWalls();
            BuildPosts();
            BuildPoolPillars();
            BuildPoolFillets();
            BuildPoolArcs();
            BuildPoolFeatures();
            PoolWallFittings();
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
            var water = WMask(WaterMat, 1u);
            var accent = WMask(AccentMat, 1u);
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                if (_L.Has(x, z, CellFlags.NoFloor) || _L.pool[x, z].solid) continue;   // a stair well from below; behind a round wall
                if (FreePoolOf(x, z, out var fp)) { FreePoolCell(x, z, fp); continue; }
                var tile = WMask(ZoneAt(x, z), 1u);
                bool basin = _L.Basin(x, z);
                float x0 = x * C, z0 = z * C, x1 = x0 + C, z1 = z0 + C;
                if (!basin)
                {
                    // The deck, with a border of accent tiles along the edge of the water (the coping).
                    float b = 0.15f;
                    bool W = EdgeWater(x, z, 0), E = EdgeWater(x, z, 1), S = EdgeWater(x, z, 2), Nn = EdgeWater(x, z, 3);
                    float ix0 = x0 + (W ? b : 0f), ix1 = x1 - (E ? b : 0f), iz0 = z0 + (S ? b : 0f), iz1 = z1 - (Nn ? b : 0f);
                    tile.Quad(new Vector3(ix0, 0, iz0), new Vector3(ix0, 0, iz1), new Vector3(ix1, 0, iz1), new Vector3(ix1, 0, iz0), Vector3.up, MeshWriter.MetreUV);
                    void Band(float a0, float c0, float a1, float c1) { if (a1 - a0 > 1e-4f && c1 - c0 > 1e-4f) accent.Quad(new Vector3(a0, 0, c0), new Vector3(a0, 0, c1), new Vector3(a1, 0, c1), new Vector3(a1, 0, c0), Vector3.up, MeshWriter.MetreUV); }
                    if (W) Band(x0, z0, x0 + b, z1);
                    if (E) Band(x1 - b, z0, x1, z1);
                    if (S) Band(ix0, z0, ix1, z0 + b);
                    if (Nn) Band(ix0, z1 - b, ix1, z1);
                    continue;
                }
                // The pool floor (sloping where it does) and the water.
                float f00 = BasinFloorY(x0, z0), f01 = BasinFloorY(x0, z1), f11 = BasinFloorY(x1, z1), f10 = BasinFloorY(x1, z0);
                tile.Quad(new Vector3(x0, f00, z0), new Vector3(x0, f01, z1), new Vector3(x1, f11, z1), new Vector3(x1, f10, z0), Vector3.up, MeshWriter.MetreUV);
                float wy = PoolBiome.WaterLevel;
                water.Quad(new Vector3(x0, wy, z0), new Vector3(x0, wy, z1), new Vector3(x1, wy, z1), new Vector3(x1, wy, z0), Vector3.up, MeshWriter.MetreUV);
                int side = StairSideOf(x, z);
                if (side >= 0) Stairs(tile, x, z, side);
                if (_L.pool[x, z].lanes != 0) LaneRopes(x, z, _L.pool[x, z].lanes);
            }

            // Floor colliders: runs of one level per row; the deck's are deep enough to make the basin's edges solid. Free
            // pools make their own; a sloping pool floor, one per cell.
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                if (!_L.Basin(x, z) || _L.Has(x, z, CellFlags.NoFloor) || _L.pool[x, z].solid || _L.pool[x, z].depth <= 0f || FreePoolOf(x, z, out _)) continue;
                float fy = BasinFloorY((x + 0.5f) * C, (z + 0.5f) * C);
                _boxes.Add((new Vector3((x + 0.5f) * C, fy - 0.15f, (z + 0.5f) * C), new Vector3(C, 0.3f, C)));
            }
            for (int z = 0; z < N; z++)
            {
                int start = 0;
                int Kind(int x) => _L.Has(x, z, CellFlags.NoFloor) || _L.pool[x, z].solid || FreePoolOf(x, z, out _) || (_L.Basin(x, z) && _L.pool[x, z].depth > 0f) ? 2 : _L.Basin(x, z) ? 1 : 0;
                for (int x = 1; x <= N; x++)
                {
                    if (x < N && Kind(x) == Kind(start)) continue;
                    if (Kind(start) != 2)
                    {
                        bool basin = _L.Basin(start, z);
                        float top = PoolBiome.FloorY(basin), depth = basin ? 0.3f : 1.3f;
                        _boxes.Add((new Vector3((start + x) * 0.5f * C, top - depth * 0.5f, (z + 0.5f) * C), new Vector3((x - start) * C, depth, C)));
                    }
                    start = x;
                }
            }
        }

        /// <summary>Three steps up out of a basin to the deck on the given side, filling the cell's width.</summary>
        void Stairs(MeshWriter w, int x, int z, int side)
        {
            float BD_POOL = -BasinFloorY((x + 0.5f) * C, (z + 0.5f) * C);
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
            tile = WMask(ZoneAt(cx, cz), 1u);
            var accent = WMask(AccentMat, 1u);
            const float band = 0.25f;                                 // a band of accent tiles round the waterline
            float Fy(Vector3 p) => Mathf.Min(-band - 0.02f, BasinFloorY(p.x, p.z) - 0.02f);
            if (e == EdgeKind.Wall)
            {
                float a0 = along * C + T * 0.5f, a1 = (along + 1) * C - T * 0.5f;
                Vector3 p0 = A(f, a0), p1 = A(f, a1);
                tile.Quad(p0 + Vector3.up * Fy(p0), p1 + Vector3.up * Fy(p1), p1 + Vector3.down * band, p0 + Vector3.down * band, n, MeshWriter.MetreUV);
                accent.Quad(p0 + Vector3.down * band, p1 + Vector3.down * band, p1, p0, n, MeshWriter.MetreUV);
                BasinFittings(e, vertical, line, along, n, f, p0, p1, false);
                return;
            }
            float? other = FloorAt(ox, oz);
            if (other.HasValue && other.Value < -0.01f) return;      // water continues
            float l = line * C, b0 = along * C, b1 = (along + 1) * C;
            Vector3 q0 = A(l, b0), q1 = A(l, b1);
            tile.Quad(q0 + Vector3.up * Fy(q0), q1 + Vector3.up * Fy(q1), q1 + Vector3.down * band, q0 + Vector3.down * band, n, MeshWriter.MetreUV);
            accent.Quad(q0 + Vector3.down * band, q1 + Vector3.down * band, q1, q0, n, MeshWriter.MetreUV);
            BasinFittings(e, vertical, line, along, n, l, q0, q1, true);
        }

        /// <summary>Does the water lie across this deck cell's side (a basin cell through a passable edge)?</summary>
        bool EdgeWater(int x, int z, int side)
        {
            if (!OfficeLayout.IsPassable(_L.EdgeOf(x, z, side))) return false;
            int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
            var c = PoolAt(nx, nz, out var o, out int lx, out int lz);
            return c.HasValue && c.Value.basin && !c.Value.solid && !(o.FreePoolRoom(lx, lz));
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
            var sky = WMask(SkyMat, 1u);
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                var r = _L.RoomAt(x, z);
                if (_L.Has(x, z, CellFlags.OpenAbove) || _L.pool[x, z].solid) continue;   // a stair tower; behind a round wall
                var tile = WMask(ZoneTile(r), 1u);
                float h = r.ceiling, x0 = x * C, z0 = z * C;
                var shape = _L.pool[x, z].skylight;
                Vector3 P(float u, float v, float y) => new Vector3(x0 + u, y, z0 + v);
                if (shape == SkylightShape.Disc)
                {
                    var pc = _L.pool[x, z];
                    DiscCeilingCell(x, z, h, new Vector2(x0 + pc.discX * 0.5f * C, z0 + pc.discZ * 0.5f * C), pc.discR);
                    continue;
                }
                bool vault = FeatureOf(PoolFeatureKind.Vault, x, z, out var vf);
                if (vault) VaultCell(x, z, vf, shape == SkylightShape.SlotX || shape == SkylightShape.SlotZ);
                if (shape == SkylightShape.None)
                {
                    if (!vault) tile.Quad(P(0, 0, h), P(C, 0, h), P(C, C, h), P(0, C, h), Vector3.down, MeshWriter.MetreUV);
                    continue;
                }
                int joined = JoinedAt(x, z);
                float top = h + PoolBiome.Shaft(h, _L.storey, PoolWorld.MapStoreys, _L.storeyPitch);
                if (shape == SkylightShape.Round)
                {
                    RoundOpening(tile, sky, x0, z0, h, top);
                    continue;
                }
                var hole = PoolBiome.HoleRect(shape, joined);
                // Ceiling around the opening (up to four strips).
                void Strip(float u0, float v0, float u1, float v1) { if (!vault && u1 - u0 > 0.001f && v1 - v0 > 0.001f) tile.Quad(P(u0, v0, h), P(u1, v0, h), P(u1, v1, h), P(u0, v1, h), Vector3.down, MeshWriter.MetreUV); }
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
                // A glazed roof light's frame (2026-11-01, the review: it was painted in the sky): bars with depth every
                // 0.6 m (world-aligned, as the sky shader and the sun's trace have them), just under the pane.
                if (PoolBiome.Glazed(_L.coord.x * _L.N + x, _L.coord.y * _L.N + z, shape))
                {
                    var bar = WMask(PoolMaterials.Grille ? PoolMaterials.Grille : TileMat, 1u);
                    float by = top - 0.035f;
                    for (float u = Mathf.Ceil((x0 + hole.xMin) / 0.6f + 0.001f) * 0.6f; u < x0 + hole.xMax - 0.001f; u += 0.6f)
                        bar.Box(new Vector3(u, by, z0 + (hole.yMin + hole.yMax) * 0.5f), new Vector3(0.04f, 0.06f, hole.yMax - hole.yMin), 0b001111 | 0b110000, MeshWriter.MetreUV);
                    for (float v = Mathf.Ceil((z0 + hole.yMin) / 0.6f + 0.001f) * 0.6f; v < z0 + hole.yMax - 0.001f; v += 0.6f)
                        bar.Box(new Vector3(x0 + (hole.xMin + hole.xMax) * 0.5f, by, v), new Vector3(hole.xMax - hole.xMin, 0.06f, 0.04f), 0b111111, MeshWriter.MetreUV);
                }
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
                tile = WMask(ZoneTile(room), 1u);
                floor -= 0.06f;
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
            if (!pc.HasValue || pc.Value.solid) return default;         // (behind a round wall: not part of the rooms)
            var info = new PoolCellInfo
            {
                valid = true, ceiling = owner.RoomAt(lx, lz).ceiling, sky = pc.Value.skylight, basin = pc.Value.basin,
                gallery = pc.Value.gallery, depth = pc.Value.depth, lanes = pc.Value.lanes, drain = pc.Value.drain,
                discX = pc.Value.discX * 0.5f * C, discZ = pc.Value.discZ * 0.5f * C, discR = pc.Value.discR,
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
                for (int k = 0; k < 4; k++) sun += PoolSun.Visibility(g, new Vector3(x0 + C * (0.25f + 0.5f * (k & 1)), fy, z0 + C * (0.25f + 0.5f * (k >> 1))), _L.storey < PoolWorld.MapStoreys - 1 ? _L.storeyPitch - 1.15f : float.MaxValue, new Vector2Int(_L.coord.x * _L.N, _L.coord.y * _L.N));
                sun *= 0.25f;
                // Sun on the walls of the cell (bars of light from slots near a wall bounce as well).
                float wallSun = 0f;
                if (PoolSun.Solid(c.west)) wallSun += PoolSun.Visibility(g, new Vector3(x0 + T * 0.5f + 0.02f, 1.4f, z0 + C * 0.5f), _L.storey < PoolWorld.MapStoreys - 1 ? _L.storeyPitch - 1.15f : float.MaxValue, new Vector2Int(_L.coord.x * _L.N, _L.coord.y * _L.N));
                if (PoolSun.Solid(c.south)) wallSun += PoolSun.Visibility(g, new Vector3(x0 + C * 0.5f, 1.4f, z0 + T * 0.5f + 0.02f), _L.storey < PoolWorld.MapStoreys - 1 ? _L.storeyPitch - 1.15f : float.MaxValue, new Vector2Int(_L.coord.x * _L.N, _L.coord.y * _L.N));
                if (i + 1 < W && PoolSun.Solid(info[i + 1, j].west)) wallSun += PoolSun.Visibility(g, new Vector3(x0 + C - T * 0.5f - 0.02f, 1.4f, z0 + C * 0.5f), _L.storey < PoolWorld.MapStoreys - 1 ? _L.storeyPitch - 1.15f : float.MaxValue, new Vector2Int(_L.coord.x * _L.N, _L.coord.y * _L.N));
                if (j + 1 < W && PoolSun.Solid(info[i, j + 1].south)) wallSun += PoolSun.Visibility(g, new Vector3(x0 + C * 0.5f, 1.4f, z0 + C - T * 0.5f - 0.02f), _L.storey < PoolWorld.MapStoreys - 1 ? _L.storeyPitch - 1.15f : float.MaxValue, new Vector2Int(_L.coord.x * _L.N, _L.coord.y * _L.N));
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
            poolMap2 = new Color32[N * N];
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                var c = info[x + N, z + N];
                if (!c.valid) continue;
                // The second map (PoolCommon.hlsl, PoolLoad): depth, gallery, lanes and drain, the round opening.
                var pc = _L.pool[x, z];
                bool extras = pc.depth > 0f || pc.gallery > 0f || pc.lanes != 0 || pc.drain || pc.skylight == SkylightShape.Disc;
                if (extras)
                {
                    int rCode = Mathf.Clamp(Mathf.RoundToInt(pc.discR / 0.3f), 0, 15);
                    poolMap2[z * N + x] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(pc.depth / 0.005f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(pc.gallery / 0.05f), 0, 255),
                        (byte)((pc.lanes & 3) | (pc.drain ? 4 : 0) | (rCode << 4)), (byte)(((pc.discX + 8) & 15) | (((pc.discZ + 8) & 15) << 4)));
                }
                byte flags = (byte)(c.west | (c.south << 2) | (c.basin ? 16 : 0) | (c.pillar ? 32 : 0) | 64 | (c.pillarNear ? 128 : 0));
                byte ceil = (byte)Mathf.Clamp(Mathf.RoundToInt(c.ceiling * 10f), 0, 255);
                byte sky = (byte)((int)c.sky | (c.joined << 3) | (extras ? 128 : 0));
                float fill = 1f - Mathf.Exp(-e[x + N, z + N] * 0.7f);
                poolMap[z * N + x] = new Color32(flags, ceil, sky, (byte)Mathf.Clamp(Mathf.RoundToInt(fill * 255f), 0, 255));
            }
        }
    }
}
