using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The Poolrooms' larger pieces (2026-10-31, the architecture plan, world version 18), after the owner's reference
    /// pictures (sunlit tiled halls, a round hall with a curved pool and chrome ladders, a vaulted tunnel with a channel and
    /// a walkway): free-form pools in deck halls, sloping pool floors, vaulted corridors and flooded channels, round halls
    /// with a round opening in the roof, galleries round tall halls, spouts and cascades of running water, and rooms with a
    /// purpose left empty (changing rooms, showers, bleachers, a lifeguard's chair, a slide). Each is a PoolFeature in the
    /// district plan; the cells they change say so too (basin, depth, gallery, solid, disc, lanes, drains) for the light,
    /// the water and the shaders.
    /// </summary>
    public static partial class PoolPlan
    {
        public enum Tag : byte { None, Vault, Round, Gallery, FreePool, Purpose, Park }

        const float C = OfficeBiome.CellSize, T = OfficeBiome.WallThickness;

        static Rect Metres(RectInt r) => new Rect(r.xMin * C, r.yMin * C, r.width * C, r.height * C);

        /// <summary>Plans the pieces; returns each room's tag (rooms with one skip the plain dressing that would clash).</summary>
        public static Dictionary<int, Tag> Features(OfficeLayout L, System.Random rng)
        {
            var tags = new Dictionary<int, Tag>();
            foreach (var r in L.rooms)
            {
                if (!r.Walkable || r.spiral || r.noFloor) continue;
                var rc = r.rect;
                int w = rc.width, h = rc.height, mn = Mathf.Min(w, h), mx = Mathf.Max(w, h);
                bool basin = L.Basin(rc.xMin + w / 2, rc.yMin + h / 2);

                // The water park's rooms (PoolPlan.WaterPark.cs).
                if (IsPark(L, rc)) { if (WaterParkRoom(L, r, rng)) tags[r.id] = Tag.Park; continue; }
                // The channels' rooms (PoolPlan.Channels.cs): vaulted channels and halls with a winding walkway.
                if (IsChannels(L, rc) && ChannelRoom(L, r, rng)) { if (r.kind == RoomKind.Hallway) tags[r.id] = Tag.Vault; continue; }

                if (r.kind == RoomKind.Hallway && mn <= 3 && mx >= 4 && rng.NextDouble() < 0.4)
                { Vault(L, r, rng); tags[r.id] = Tag.Vault; continue; }
                if (r.kind == RoomKind.BigOffice && mn >= 6 && mx <= 12 && Mathf.Abs(w - h) <= 1 && rng.NextDouble() < 0.6 && RoundHall(L, r, rng))
                { tags[r.id] = Tag.Round; continue; }
                if (r.kind == RoomKind.BigOffice && mn >= 7 && rng.NextDouble() < 0.3 && Gallery(L, r, rng))
                { tags[r.id] = Tag.Gallery; continue; }
                if (!basin && r.kind == RoomKind.BigOffice && mn >= 6 && rng.NextDouble() < 0.55)
                { FreePool(L, r, rng, 1.5f + (float)rng.NextDouble() * 0.6f); tags[r.id] = Tag.FreePool; continue; }
                if (basin && mx >= 5 && FullBasin(L, rc))
                {
                    if (rng.NextDouble() < 0.55) Slope(L, r, rng);
                    if (rng.NextDouble() < 0.07) Spouts(L, r, rng);
                    if (rng.NextDouble() < 0.05) Cascade(L, r, rng);
                    if (mn >= 6 && rng.NextDouble() < 0.18) Slide(L, r, rng);
                }
                if (basin && rng.NextDouble() < 0.4) WalkwayLadder(L, r, rng);
                if (!basin && w * h >= 6 && w * h <= 20 && mn >= 2 && rng.NextDouble() < 0.35)
                { Purpose(L, r, rng); tags[r.id] = Tag.Purpose; }
            }
            // Lanes and drains in the bigger rectangular pools.
            foreach (var r in L.rooms)
            {
                var rc = r.rect;
                if (!r.Walkable || tags.ContainsKey(r.id) || !FullBasin(L, rc) || rc.width * rc.height < 24) continue;
                if (rng.NextDouble() < 0.35)
                {
                    byte lanes = (byte)(rc.width >= rc.height ? 1 : 2);
                    FillCells(L, rc, c => { var k = c.cell; k.lanes = lanes; c.cell = k; });
                }
                for (int x = rc.xMin; x < rc.xMax; x++) for (int z = rc.yMin; z < rc.yMax; z++)
                    if (L.pool[x, z].basin && rng.NextDouble() < 0.05) { var k = L.pool[x, z]; k.drain = true; L.pool[x, z] = k; }
            }
            return tags;
        }

        static bool FullBasin(OfficeLayout L, RectInt rc)
        {
            int n = 0, b = 0;
            for (int x = rc.xMin; x < rc.xMax; x++) for (int z = rc.yMin; z < rc.yMax; z++) { n++; if (L.pool[x, z].basin) b++; }
            return b >= n - Mathf.Max(rc.width, rc.height);       // all basin, but for a walkway
        }

        static float Step(float v, float q) => Mathf.Ceil(v / q - 1e-4f) * q;

        // ---------------- Vaults and channels ----------------

        static void Vault(OfficeLayout L, Room r, System.Random rng, bool flood = false)
        {
            var rc = r.rect;
            bool alongX = rc.width >= rc.height;
            int across = alongX ? rc.height : rc.width;
            float radius = (across * C - T) * 0.5f;
            r.ceiling = Mathf.Max(r.ceiling, Step(2.95f + radius, 0.3f));
            L.poolFeatures.Add(new PoolFeature { kind = PoolFeatureKind.Vault, bounds = Metres(rc), centre = Metres(rc).center, yaw = alongX ? 0f : 90f,
                                                 radius = radius, height = r.ceiling - radius, room = r.globalId });
            // A flooded channel with a walkway along one side, or dry.
            if (flood || rng.NextDouble() < 0.6)
            {
                FillCells(L, rc, c => c.basin = true);
                if (across >= 2)
                {
                    bool low = rng.NextDouble() < 0.5;
                    var strip = alongX ? new RectInt(rc.xMin, low ? rc.yMin : rc.yMax - 1, rc.width, 1) : new RectInt(low ? rc.xMin : rc.xMax - 1, rc.yMin, 1, rc.height);
                    FillCells(L, strip, c => c.basin = false);
                }
            }
        }

        /// <summary>After the roof openings: a vaulted corridor's only opening is a slot along its crown (odd widths).</summary>
        public static void VaultSkylights(OfficeLayout L, Dictionary<int, Tag> tags)
        {
            foreach (var r in L.rooms)
            {
                if (!tags.TryGetValue(r.id, out var t) || t != Tag.Vault) continue;
                var rc = r.rect;
                FillCells(L, rc, c => { var k = c.cell; k.skylight = SkylightShape.None; c.cell = k; });
                bool alongX = rc.width >= rc.height;
                int across = alongX ? rc.height : rc.width;
                if ((across & 1) == 0) continue;
                int mid = alongX ? rc.yMin + across / 2 : rc.xMin + across / 2;
                int len = alongX ? rc.width : rc.height, start = alongX ? rc.xMin : rc.yMin;
                for (int i = 1; i < len - 1; i++)
                {
                    int x = alongX ? start + i : mid, z = alongX ? mid : start + i;
                    var k = L.pool[x, z]; k.skylight = alongX ? SkylightShape.SlotX : SkylightShape.SlotZ; L.pool[x, z] = k;
                }
            }
        }

        // ---------------- Round halls ----------------

        static bool RoundHall(OfficeLayout L, Room r, System.Random rng)
        {
            var rc = r.rect;
            var centre = Metres(rc).center;
            float R = Mathf.Min(rc.width, rc.height) * C * 0.5f - T * 0.5f - 0.02f;
            // Every way in must be near the middle of its wall (where the circle touches it), and narrow.
            var gaps = new List<(float deg, float half)>();
            for (int side = 0; side < 4; side++)
            {
                int len = side < 2 ? rc.height : rc.width;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < len; i++)
                {
                    int x = side == 0 ? rc.xMin : side == 1 ? rc.xMax - 1 : rc.xMin + i, z = side < 2 ? rc.yMin + i : side == 2 ? rc.yMin : rc.yMax - 1;
                    if (!OfficeLayout.IsPassable(L.EdgeOf(x, z, side))) continue;
                    int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                    if (L.InChunk(nx, nz) && L.roomOf[nx, nz] == r.id) continue;
                    lo = Mathf.Min(lo, i); hi = Mathf.Max(hi, i + 1);
                }
                if (lo > hi) continue;
                float mid = len * 0.5f;
                if (lo < mid - 2.6f || hi > mid + 2.6f) return false;
                float half = Mathf.Max(mid - lo, hi - mid) * C + 0.1f;
                gaps.Add((side == 0 ? 180f : side == 1 ? 0f : side == 2 ? 270f : 90f, Mathf.Asin(Mathf.Clamp01(half / R)) * Mathf.Rad2Deg));
            }
            if (gaps.Count == 0) return false;
            gaps.Sort((a, b) => a.deg.CompareTo(b.deg));
            for (int i = 0; i < gaps.Count; i++)
            {
                var g0 = gaps[i]; var g1 = gaps[(i + 1) % gaps.Count];
                float from = g0.deg + g0.half, to = g1.deg - g1.half + (i + 1 == gaps.Count ? 360f : 0f);
                if (to - from > 4f) L.arcs.Add(new PoolArc { centre = centre, radius = R, from = from, to = to, top = r.ceiling });
            }
            r.ceiling = Mathf.Max(r.ceiling, 5.1f);
            for (int k = 0; k < L.arcs.Count; k++) if (L.arcs[k].centre == centre) { var a = L.arcs[k]; a.top = r.ceiling; L.arcs[k] = a; }
            // Behind the wall: walled off. Inside: dry, with a round pool in the middle and a round opening over it.
            float discR = Mathf.Clamp(Mathf.Round(R * 0.42f / 0.3f) * 0.3f, 1.2f, 2.4f);
            for (int x = rc.xMin; x < rc.xMax; x++)
            for (int z = rc.yMin; z < rc.yMax; z++)
            {
                var c = L.pool[x, z];
                var cc = new Vector2((x + 0.5f) * C, (z + 0.5f) * C);
                c.basin = false; c.skylight = SkylightShape.None;
                c.solid = (cc - centre).magnitude > R + 0.35f;
                // The opening covers this cell if the disc reaches into its square.
                float dx = Mathf.Max(0f, Mathf.Abs(cc.x - centre.x) - C * 0.5f), dz = Mathf.Max(0f, Mathf.Abs(cc.y - centre.y) - C * 0.5f);
                if (dx * dx + dz * dz < discR * discR)
                {
                    c.skylight = SkylightShape.Disc; c.discR = discR;
                    c.discX = (sbyte)Mathf.RoundToInt((centre.x - x * C) / (0.5f * C)); c.discZ = (sbyte)Mathf.RoundToInt((centre.y - z * C) / (0.5f * C));
                }
                L.pool[x, z] = c;
            }
            L.poolFeatures.Add(new PoolFeature { kind = PoolFeatureKind.RoundHall, bounds = Metres(rc), centre = centre, radius = R, size = new Vector2(discR, discR), room = r.globalId });
            // The pool, round, under the opening (sometimes a ring of deck round a smaller one).
            float pr = Mathf.Round(R * (0.55f + 0.1f * (float)rng.NextDouble()) / 0.15f) * 0.15f;
            AddFreePool(L, r, centre, new Vector2(pr, pr), pr, 1, 0f, 0.45f, rng.NextDouble() < 0.5 ? 0.7f : 0f, rng);
            return true;
        }

        // ---------------- Galleries ----------------

        static bool Gallery(OfficeLayout L, Room r, System.Random rng)
        {
            var rc = r.rect;
            const float height = 3.6f, rise = 0.2f, tread = 0.28f;
            int steps = Mathf.RoundToInt(height / rise);
            float run = steps * tread;
            int cellsNeeded = Mathf.CeilToInt(run / C) + 1;
            // The stair along the inside of one long wall (the row in from it), rising toward a corner.
            bool alongX = rc.width >= rc.height;
            int len = (alongX ? rc.width : rc.height) - 2;
            if (len < cellsNeeded + 1) return false;
            int side = alongX ? (rng.NextDouble() < 0.5 ? 2 : 3) : (rng.NextDouble() < 0.5 ? 0 : 1);
            r.ceiling = 7.2f;
            FillCells(L, rc, c => { var k = c.cell; k.basin = false; c.cell = k; });
            for (int x = rc.xMin; x < rc.xMax; x++)
            for (int z = rc.yMin; z < rc.yMax; z++)
            {
                bool ring = x == rc.xMin || z == rc.yMin || x == rc.xMax - 1 || z == rc.yMax - 1;
                if (!ring) continue;
                var k = L.pool[x, z]; k.gallery = height; L.pool[x, z] = k;
            }
            // The stair's foot (metres) and its direction: up toward the +along end.
            float rowCentre = side == 0 ? (rc.xMin + 1.5f) * C : side == 1 ? (rc.xMax - 1.5f) * C : side == 2 ? (rc.yMin + 1.5f) * C : (rc.yMax - 1.5f) * C;
            float topEnd = (alongX ? rc.xMax - 1 : rc.yMax - 1) * C;             // where the ring's far corner cell begins
            float foot = topEnd - run;
            var start = alongX ? new Vector2(foot, rowCentre) : new Vector2(rowCentre, foot);
            L.poolFeatures.Add(new PoolFeature { kind = PoolFeatureKind.Gallery, bounds = Metres(rc), centre = start, size = new Vector2(run, 0f),
                                                 height = height, shape = side, yaw = alongX ? 0f : 90f, room = r.globalId });
            // A pool in the middle to look down on, kept two cells off the walls (the ring, then deck).
            var inner = Metres(new RectInt(rc.xMin + 2, rc.yMin + 2, rc.width - 4, rc.height - 4));
            if (inner.width > 2.4f && inner.height > 2.4f)
            {
                bool longX = inner.width >= inner.height;
                var size = longX ? new Vector2(inner.width * 0.5f, inner.height * 0.5f) : new Vector2(inner.height * 0.5f, inner.width * 0.5f);
                AddFreePool(L, r, inner.center, size - Vector2.one * 0.3f, Mathf.Min(1.5f, size.y * 0.6f), 0, longX ? 0f : 90f, 0.45f, 0.9f, rng);
            }
            return true;
        }

        // ---------------- Free pools, slopes ----------------

        static void FreePool(OfficeLayout L, Room r, System.Random rng, float inset)
        {
            var m = Metres(r.rect);
            bool longX = m.width >= m.height;
            var size = longX ? new Vector2(m.width * 0.5f - inset, m.height * 0.5f - inset) : new Vector2(m.height * 0.5f - inset, m.width * 0.5f - inset);
            if (size.y < 1.2f) return;
            double p = rng.NextDouble();
            int shape = p < 0.55 ? 0 : p < 0.85 ? 2 : 1;
            if (shape == 1 && size.x > size.y * 1.4f) shape = 0;
            if (shape == 2) size.x = Mathf.Min(size.x, size.y * 1.55f);       // a kidney's two lobes must meet
            float radius = shape == 1 ? size.y : Mathf.Min(size.y, 0.6f + (float)rng.NextDouble() * 1.4f);
            float deep = rng.NextDouble() < 0.55 ? 0.8f + (float)rng.NextDouble() * 0.2f : 0f;
            AddFreePool(L, r, m.center, size, radius, shape, longX ? 0f : 90f, 0.42f, deep, rng);
            // Fittings round it: a lifeguard's chair by its side, bleachers along a long wall with room to spare.
            float yaw = longX ? 0f : 90f;
            if (rng.NextDouble() < 0.45)
            {
                var side = Rot(new Vector2(0f, -(size.y + 0.75f)), yaw);
                L.poolFeatures.Add(Fitting(PoolFeatureKind.Lifeguard, m.center + side, yaw, new Vector2(0.4f, 0.5f), r));
            }
            if (inset >= 1.5f && rng.NextDouble() < 0.35)
            {
                // Along the +local-z long wall, facing the pool.
                var at = m.center + Rot(new Vector2(0f, size.y + inset - 0.6f), yaw);
                L.poolFeatures.Add(Fitting(PoolFeatureKind.Bleachers, at, yaw + 180f, new Vector2(size.x * 0.7f, 0.6f), r));
            }
        }

        static Vector2 Rot(Vector2 v, float yaw)
        {
            float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        static PoolFeature Fitting(PoolFeatureKind k, Vector2 at, float yaw, Vector2 size, Room r)
        {
            float e = Mathf.Max(size.x, size.y) + 0.5f;
            return new PoolFeature { kind = k, centre = at, yaw = yaw, size = size, room = r.globalId, bounds = new Rect(at.x - e, at.y - e, 2f * e, 2f * e) };
        }

        /// <param name="width">A ring's width (shape 3, the lazy river).</param>
        /// <param name="ladders">Ladders on its sides (not in a kiddie pool or a plunge pool).</param>
        static PoolFeature AddFreePool(OfficeLayout L, Room r, Vector2 centre, Vector2 size, float radius, int shape, float yaw, float shallow, float deep, System.Random rng,
            float width = 0f, bool ladders = true)
        {
            var f = new PoolFeature { kind = PoolFeatureKind.FreePool, centre = centre, size = size, radius = radius, shape = shape, yaw = yaw,
                                      depth = shallow, depth2 = deep, room = r.globalId, height = width };
            float e = Mathf.Max(size.x, size.y) + 0.2f;
            f.bounds = new Rect(centre.x - e, centre.y - e, 2f * e, 2f * e);
            L.poolFeatures.Add(f);
            var rc = r.rect;
            for (int x = rc.xMin; x < rc.xMax; x++)
            for (int z = rc.yMin; z < rc.yMax; z++)
            {
                var p = new Vector2((x + 0.5f) * C, (z + 0.5f) * C);
                var k = L.pool[x, z];
                k.basin = PoolBiome.ShapeSD(f, p) < 0f;
                // (No lane lines: a leisure pool's curves cut them off in squares, cell by cell. 2026-11-01.)
                if (k.basin) k.depth = PoolBiome.SlopedDepth(f, p);
                L.pool[x, z] = k;
            }
            // Two chrome ladders on the long sides near the deep end (one on a round pool).
            if (!ladders) return f;
            if (shape != 1)
                foreach (float s in new[] { -1f, 1f }) EdgeLadder(L, r, f, centre + Rot(new Vector2(size.x * 0.45f, s * size.y), yaw));
            else EdgeLadder(L, r, f, centre + Rot(new Vector2(0f, -size.y), yaw + 45f));
            return f;
        }

        /// <summary>
        /// A ladder on a free pool's edge near a point: moved onto the edge itself (Newton steps on the shape's distance) and
        /// turned to face straight into the water (against the distance's gradient). 2026-10-31: they had faced along the
        /// pool (and a round pool's 45 degrees off), their rails crossing the edge askew.
        /// </summary>
        static void EdgeLadder(OfficeLayout L, Room r, PoolFeature f, Vector2 near)
        {
            var p = near;
            for (int i = 0; i < 8; i++) p -= ShapeGrad(f, p) * PoolBiome.ShapeSD(f, p);
            var inward = -ShapeGrad(f, p);
            float yaw = Mathf.Atan2(inward.y, inward.x) * Mathf.Rad2Deg;      // the builder's Dir(yaw) = (cos, sin)
            L.poolFeatures.Add(Fitting(PoolFeatureKind.Ladder, p, yaw, new Vector2(0.3f, 0.3f), r));
        }

        /// <summary>Outward unit normal of a free pool's edge near a point (the distance's gradient).</summary>
        static Vector2 ShapeGrad(PoolFeature f, Vector2 p)
        {
            const float e = 0.01f;
            var g = new Vector2(PoolBiome.ShapeSD(f, p + new Vector2(e, 0f)) - PoolBiome.ShapeSD(f, p - new Vector2(e, 0f)),
                                PoolBiome.ShapeSD(f, p + new Vector2(0f, e)) - PoolBiome.ShapeSD(f, p - new Vector2(0f, e)));
            return g.sqrMagnitude > 1e-10f ? g.normalized : Vector2.right;
        }

        static void Slope(OfficeLayout L, Room r, System.Random rng)
        {
            var rc = r.rect;
            bool alongX = rc.width >= rc.height;
            float yaw = (alongX ? 0f : 90f) + (rng.NextDouble() < 0.5 ? 180f : 0f);
            var f = new PoolFeature { kind = PoolFeatureKind.Slope, bounds = Metres(rc), centre = Metres(rc).center, yaw = yaw,
                                      depth = 0.42f, depth2 = 0.75f + (float)rng.NextDouble() * 0.2f, room = r.globalId };
            L.poolFeatures.Add(f);
            for (int x = rc.xMin; x < rc.xMax; x++)
            for (int z = rc.yMin; z < rc.yMax; z++)
                if (L.pool[x, z].basin) { var k = L.pool[x, z]; k.depth = PoolBiome.SlopedDepth(f, new Vector2((x + 0.5f) * C, (z + 0.5f) * C)); L.pool[x, z] = k; }
        }

        // ---------------- Running water ----------------

        /// <summary>Wall edges of a room that face a basin cell of it, one per entry (cell, side).</summary>
        static List<(int x, int z, int side)> WetWalls(OfficeLayout L, Room r)
        {
            var list = new List<(int, int, int)>();
            var rc = r.rect;
            for (int x = rc.xMin; x < rc.xMax; x++)
            for (int z = rc.yMin; z < rc.yMax; z++)
            {
                if (!L.pool[x, z].basin) continue;
                for (int side = 0; side < 4; side++)
                {
                    bool edge = side == 0 ? x == rc.xMin : side == 1 ? x == rc.xMax - 1 : side == 2 ? z == rc.yMin : z == rc.yMax - 1;
                    if (edge && L.EdgeOf(x, z, side) == EdgeKind.Wall && L.pool[x, z].windows == 0) list.Add((x, z, side));
                }
            }
            return list;
        }

        static (Vector2 at, float yaw) WallPoint(int x, int z, int side, float along = 0.5f)
        {
            float wx = side == 0 ? x * C + T * 0.5f : side == 1 ? (x + 1) * C - T * 0.5f : (x + along) * C;
            float wz = side == 2 ? z * C + T * 0.5f : side == 3 ? (z + 1) * C - T * 0.5f : (z + along) * C;
            float yaw = side == 0 ? 0f : side == 1 ? 180f : side == 2 ? 90f : 270f;        // facing into the room (+x is yaw 0)
            return (new Vector2(wx, wz), yaw);
        }

        static void Spouts(OfficeLayout L, Room r, System.Random rng)
        {
            var walls = WetWalls(L, r);
            int count = Mathf.Min(walls.Count, 1 + rng.Next(2));
            for (int i = 0; i < count; i++)
            {
                var (x, z, side) = walls[rng.Next(walls.Count)];
                var (at, yaw) = WallPoint(x, z, side);
                var f = Fitting(PoolFeatureKind.Spout, at, yaw, new Vector2(0.18f, 0.1f), r);
                f.height = 1.4f + (float)rng.NextDouble() * 0.6f;
                L.poolFeatures.Add(f);
            }
        }

        static void Cascade(OfficeLayout L, Room r, System.Random rng)
        {
            // The longest run of wet wall along one side.
            var walls = WetWalls(L, r);
            for (int tries = 0; tries < 6 && walls.Count > 0; tries++)
            {
                var (x0, z0, side) = walls[rng.Next(walls.Count)];
                int run = 1;
                bool alongZ = side < 2;
                while (run < 5 && walls.Contains(alongZ ? (x0, z0 + run, side) : (x0 + run, z0, side))) run++;
                if (run < 3) continue;
                var (a, yaw) = WallPoint(x0, z0, side, 0f);
                var (b, _) = WallPoint(alongZ ? x0 : x0 + run, alongZ ? z0 + run : z0, side, 0f);
                var f = Fitting(PoolFeatureKind.Cascade, (a + b) * 0.5f, yaw, new Vector2(run * C * 0.5f - 0.1f, 1.1f), r);
                f.height = 0.75f;
                L.poolFeatures.Add(f);
                return;
            }
        }

        static void Slide(OfficeLayout L, Room r, System.Random rng)
        {
            var walls = WetWalls(L, r);
            if (walls.Count == 0) return;
            var (x, z, side) = walls[rng.Next(walls.Count)];
            var (at, yaw) = WallPoint(x, z, side);
            var f = Fitting(PoolFeatureKind.Slide, at, yaw, new Vector2(3.2f, 1.5f), r);
            f.height = Mathf.Min(2.7f, r.ceiling - 0.6f);
            f.shape = rng.Next(2);                  // which way it curls
            L.poolFeatures.Add(f);
        }

        static void WalkwayLadder(OfficeLayout L, Room r, System.Random rng)
        {
            var rc = r.rect;
            var picks = new List<(int x, int z, int side)>();
            for (int x = rc.xMin; x < rc.xMax; x++)
            for (int z = rc.yMin; z < rc.yMax; z++)
            {
                if (L.pool[x, z].basin) continue;
                for (int side = 0; side < 4; side++)
                {
                    int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                    if (nx < rc.xMin || nz < rc.yMin || nx >= rc.xMax || nz >= rc.yMax || !L.pool[nx, nz].basin) continue;
                    picks.Add((x, z, side));
                }
            }
            if (picks.Count == 0) return;
            var (cx, cz, sd) = picks[rng.Next(picks.Count)];
            float ex = sd == 0 ? cx * C : sd == 1 ? (cx + 1) * C : (cx + 0.5f) * C, ez = sd == 2 ? cz * C : sd == 3 ? (cz + 1) * C : (cz + 0.5f) * C;
            float yaw = sd == 0 ? 180f : sd == 1 ? 0f : sd == 2 ? 270f : 90f;       // facing the water
            L.poolFeatures.Add(Fitting(PoolFeatureKind.Ladder, new Vector2(ex, ez), yaw, new Vector2(0.3f, 0.3f), r));
        }

        // ---------------- Rooms with a purpose ----------------

        static void Purpose(OfficeLayout L, Room r, System.Random rng)
        {
            var rc = r.rect;
            // The longest wall with no way through it.
            int best = -1, bestLen = 0;
            for (int side = 0; side < 4; side++)
            {
                int len = side < 2 ? rc.height : rc.width;
                bool clear = true;
                for (int i = 0; i < len && clear; i++)
                {
                    int x = side == 0 ? rc.xMin : side == 1 ? rc.xMax - 1 : rc.xMin + i, z = side < 2 ? rc.yMin + i : side == 2 ? rc.yMin : rc.yMax - 1;
                    if (L.EdgeOf(x, z, side) != EdgeKind.Wall || L.pool[x, z].windows != 0) clear = false;
                }
                if (clear && len > bestLen) { best = side; bestLen = len; }
            }
            if (best < 0 || bestLen < 2) return;
            var m = Metres(rc);
            float yaw = best == 0 ? 0f : best == 1 ? 180f : best == 2 ? 90f : 270f;
            var wallMid = best == 0 ? new Vector2(m.xMin + T * 0.5f, m.center.y) : best == 1 ? new Vector2(m.xMax - T * 0.5f, m.center.y)
                        : best == 2 ? new Vector2(m.center.x, m.yMin + T * 0.5f) : new Vector2(m.center.x, m.yMax - T * 0.5f);
            var kind = rng.NextDouble() < 0.55 ? PoolFeatureKind.Changing : PoolFeatureKind.Showers;
            var f = Fitting(kind, wallMid, yaw, new Vector2(bestLen * C * 0.5f - 0.2f, (best < 2 ? m.width : m.height)), r);
            f.bounds = m;
            L.poolFeatures.Add(f);
            if (kind == PoolFeatureKind.Showers)
                FillCells(L, rc, c => { var k = c.cell; k.drain = false; c.cell = k; });
            // A drain under the showers.
            if (kind == PoolFeatureKind.Showers)
            {
                int dx = best == 0 ? rc.xMin : best == 1 ? rc.xMax - 1 : rc.xMin + rc.width / 2, dz = best < 2 ? rc.yMin + rc.height / 2 : best == 2 ? rc.yMin : rc.yMax - 1;
                var k = L.pool[dx, dz]; k.drain = true; L.pool[dx, dz] = k;
            }
        }

        static void FillCells(OfficeLayout L, RectInt r, System.Action<RefCell> edit)
        {
            for (int x = r.xMin; x < r.xMax; x++)
            for (int z = r.yMin; z < r.yMax; z++)
            {
                var box = new RefCell { cell = L.pool[x, z] };
                edit(box);
                L.pool[x, z] = box.cell;
            }
        }

        sealed class RefCell { public PoolCell cell; public bool basin { get => cell.basin; set => cell.basin = value; } }
    }
}
