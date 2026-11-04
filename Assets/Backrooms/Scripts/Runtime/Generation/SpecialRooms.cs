using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Rooms worth making a base (Docs/BaseRoomsPlan.md): a janitor's closet (shelves, a mop sink, the district's breaker
    /// panel), a security office (desk, monitors, a desk lamp, a chair), a break room (counter with a sink and a microwave,
    /// a fridge, a table and chairs) and a stock room (steel shelving and boxes, beside the warehouse).
    /// Each has one way in, through a wooden door (shut when found), outlets that have power, its ceiling lights dead and a
    /// switch by the door. A few per district (WorldGenSettings.goodRooms). Chosen after the rooms are joined up and the
    /// floors decided, with their own dice: an ordinary room is picked, every doorway but one walled up (only if the rest
    /// of the district stays connected) and the one left given a header for its door.
    /// </summary>
    public static class SpecialRooms
    {
        const float C = OfficeBiome.CellSize, T = OfficeBiome.WallThickness;

        /// <summary>How many of each per district at 1x (the fraction is the chance of one more).</summary>
        static float PerDistrict(GoodRoom k) => k == GoodRoom.JanitorCloset ? 0.45f : k == GoodRoom.StockRoom ? 0.4f : 0.3f;

        public static void Choose(OfficeLayout L, WorldGenSettings set, System.Random rng)
        {
            if (L.style == ZoneStyle.Pool || set == null || set.goodRooms <= 0f) return;
            var kinds = new[] { GoodRoom.JanitorCloset, GoodRoom.SecurityOffice, GoodRoom.BreakRoom, GoodRoom.StockRoom };
            foreach (var k in kinds)
            {
                float want = PerDistrict(k) * set.goodRooms;
                int count = Mathf.FloorToInt(want) + (rng.NextDouble() < want - Mathf.Floor(want) ? 1 : 0);
                for (int i = 0; i < count; i++)
                {
                    var cands = new List<Room>();
                    foreach (var r in L.rooms) if (Fits(L, r, k)) cands.Add(r);
                    for (int c = cands.Count - 1; c > 0; c--) { int j = rng.Next(c + 1); (cands[c], cands[j]) = (cands[j], cands[c]); }
                    foreach (var r in cands) if (TryMake(L, r, k)) break;
                }
            }
        }

        /// <summary>Whether a room's outlets have power without a breaker: the good rooms always, a few others (WorldGenSettings.liveOutlets).</summary>
        public static void SetLive(OfficeLayout L, WorldGenSettings set, int sseed)
        {
            float chance = set != null ? set.liveOutlets : 0.15f;
            foreach (var r in L.rooms)
            {
                uint h = (uint)DistrictGenerator.Hash(sseed, L.coord.x, L.coord.y, 0x11FE00 + r.globalId);
                r.live = r.special != GoodRoom.None || (h & 0xFFFF) / 65535f < chance;
            }
        }

        // ---------------- Choosing ----------------

        static bool Fits(OfficeLayout L, Room r, GoodRoom k)
        {
            if (r.special != GoodRoom.None || !r.Walkable || r.landmark != null || r.custom || r.noDoors || r.noFloor || r.openAbove
                || r.role != AnnexRole.None || r.spiral || r.feature != RoomFeature.None || Mathf.Abs(r.floor) > 0.001f || r.ramp != 0
                || r.ceilDrop != 0 || r.Tall || DepthPlan.SpawnHall(L, r)) return false;
            if (r.kind != RoomKind.Office && r.kind != RoomKind.Lounge && r.kind != RoomKind.Conference) return false;
            var rc = r.rect;
            int lo = Mathf.Min(rc.width, rc.height), hi = Mathf.Max(rc.width, rc.height), area = rc.width * rc.height;
            for (int x = rc.xMin; x < rc.xMax; x++)
                for (int z = rc.yMin; z < rc.yMax; z++)
                    if (DepthPlan.Busy(L, x, z)) return false;
            switch (k)
            {
                case GoodRoom.JanitorCloset: return lo == 2 && hi <= 3;
                case GoodRoom.SecurityOffice: return lo >= 2 && hi >= 3 && hi <= 4 && area <= 12 && Beside(L, r, o => o.kind == RoomKind.Hallway);
                case GoodRoom.BreakRoom: return lo >= 3 && hi <= 5;
                default: return lo >= 2 && hi >= 3 && hi <= 6 && (Beside(L, r, OfficeBiomeAssets.ConcreteFloor) || InWarehouse(L, r));
            }
        }

        static bool InWarehouse(OfficeLayout L, Room r)
        {
            if (L.chunkBiomes == null) return L.biomeName == "Warehouse";
            int c = OfficeBiome.ChunkCells;
            var rc = r.rect;
            int cx = Mathf.Clamp((rc.xMin + rc.width / 2) / c, 0, L.chunkBiomes.GetLength(0) - 1), cz = Mathf.Clamp((rc.yMin + rc.height / 2) / c, 0, L.chunkBiomes.GetLength(1) - 1);
            return L.chunkBiomes[cx, cz] == "Warehouse";
        }

        static bool Beside(OfficeLayout L, Room r, System.Func<Room, bool> test)
        {
            foreach (var e in Boundary(L, r))
                if (L.InChunk(e.nx, e.nz) && test(L.RoomAt(e.nx, e.nz))) return true;
            return false;
        }

        struct BEdge { public int x, z, side, nx, nz; }

        static List<BEdge> Boundary(OfficeLayout L, Room r)
        {
            var list = new List<BEdge>();
            var rc = r.rect;
            for (int x = rc.xMin; x < rc.xMax; x++)
                for (int z = rc.yMin; z < rc.yMax; z++)
                    for (int s = 0; s < 4; s++)
                    {
                        int nx = x + (s == 0 ? -1 : s == 1 ? 1 : 0), nz = z + (s == 2 ? -1 : s == 3 ? 1 : 0);
                        if (rc.Contains(new Vector2Int(nx, nz))) continue;
                        list.Add(new BEdge { x = x, z = z, side = s, nx = nx, nz = nz });
                    }
            return list;
        }

        /// <summary>Walls up every doorway but one (into the best neighbour) if the district stays connected; else leaves it.</summary>
        static bool TryMake(OfficeLayout L, Room r, GoodRoom k)
        {
            var edges = Boundary(L, r);
            int keep = -1, best = int.MinValue;
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                var ek = L.EdgeOf(e.x, e.z, e.side);
                if (!OfficeLayout.IsPassable(ek)) continue;
                if (!L.InChunk(e.nx, e.nz)) return false;                 // a district edge opening: both districts agreed on it
                var o = L.RoomAt(e.nx, e.nz);
                if (!o.Walkable || o.special != GoodRoom.None || !L.Plain(e.nx, e.nz) || DepthPlan.Busy(L, e.nx, e.nz)
                    || o.ceiling < OfficeBiome.DoorHeaderHeight + 0.2f || o.noFloor || o.role != AnnexRole.None) continue;
                // Prefer a hallway, an existing doorway, a cell away from the room's corners.
                int score = (o.kind == RoomKind.Hallway ? 4 : 0) + (ek != EdgeKind.Open ? 2 : 0);
                bool corner = (e.side < 2 ? (e.z == r.rect.yMin || e.z == r.rect.yMax - 1) : (e.x == r.rect.xMin || e.x == r.rect.xMax - 1));
                if (!corner) score += 1;
                if (score > best) { best = score; keep = i; }
            }
            if (keep < 0) return false;

            var old = new EdgeKind[edges.Count];
            var oldArch = new bool[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                old[i] = L.EdgeOf(e.x, e.z, e.side);
                oldArch[i] = Arch(L, e);
                if (i == keep) { L.SetEdge(e.x, e.z, e.side, EdgeKind.DoorHeader); L.SetArch(e.x, e.z, e.side, false); }
                else if (OfficeLayout.IsPassable(old[i])) { L.SetEdge(e.x, e.z, e.side, EdgeKind.Wall); L.SetArch(e.x, e.z, e.side, false); }
            }
            if (!AllConnected(L))
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    var e = edges[i];
                    L.SetEdge(e.x, e.z, e.side, old[i]);
                    L.SetArch(e.x, e.z, e.side, oldArch[i]);
                }
                return false;
            }
            r.special = k;
            return true;
        }

        static bool Arch(OfficeLayout L, BEdge e) =>
            e.side == 0 ? L.archV[e.x, e.z] : e.side == 1 ? L.archV[e.x + 1, e.z] : e.side == 2 ? L.archH[e.x, e.z] : L.archH[e.x, e.z + 1];

        static bool AllConnected(OfficeLayout L)
        {
            var seen = DistrictGenerator.Flood(L);
            for (int x = 0; x < L.N; x++)
                for (int z = 0; z < L.N; z++)
                    if (L.RoomAt(x, z).Walkable && !seen[x, z]) return false;
            return true;
        }

        /// <summary>The cell and side of a good room's doorway (there is exactly one).</summary>
        public static bool DoorOf(OfficeLayout L, Room r, out int x, out int z, out int side)
        {
            foreach (var e in Boundary(L, r))
                if (OfficeLayout.IsPassable(L.EdgeOf(e.x, e.z, e.side))) { x = e.x; z = e.z; side = e.side; return true; }
            x = z = side = -1;
            return false;
        }

        // ---------------- Furniture ----------------

        /// <summary>Furnishes a good room (replaces the ordinary dressing).</summary>
        public static void Dress(OfficeLayout L, System.Random rng, Room r, HashSet<Vector2Int> occupied)
        {
            DoorOf(L, r, out int dx, out int dz, out int dside);
            var door = new Vector2Int(dx, dz);
            occupied.Add(door);
            // The cell inside the doorway stays clear for walking in.
            switch (r.special)
            {
                case GoodRoom.JanitorCloset: Janitor(L, rng, r, occupied, dside); break;
                case GoodRoom.SecurityOffice: Security(L, rng, r, occupied, dside); break;
                case GoodRoom.BreakRoom: Break(L, rng, r, occupied, dside); break;
                case GoodRoom.StockRoom: Stock(L, rng, r, occupied, dside); break;
            }
        }

        static void Janitor(OfficeLayout L, System.Random rng, Room r, HashSet<Vector2Int> occ, int doorSide)
        {
            // The breaker panel: on a wall at shoulder height, where it is seen coming in (not on the door's own wall).
            var spot = WallSlots(L, r, occ, 1, s => s != doorSide);
            if (spot.Count > 0)
            {
                var (x, z, side) = spot[rng.Next(spot.Count)];
                var (pos, yaw) = OnWall(x, z, side, 0.1f, 0.5f);
                Add(L, r, PropKind.BreakerPanel, pos, yaw, new Vector3(0.36f, 1.25f, 0.1f), rng.Next(1 << 16));
                occ.Add(new Vector2Int(x, z));                                // nothing in front of it
            }
            // A mop sink in a corner, shelving on a free wall.
            if (Corner(L, r, occ, rng, out var cx, out var cz, out var cs))
            {
                var (pos, yaw) = OnWall(cx, cz, cs, 0.5f, (cs < 2 ? (cz == r.rect.yMin ? 0.3f : 0.7f) : (cx == r.rect.xMin ? 0.3f : 0.7f)));
                Add(L, r, PropKind.MopSink, pos, yaw, new Vector3(0.6f, 0.254f, 0.5f), rng.Next(1 << 16));
                occ.Add(new Vector2Int(cx, cz));
            }
            foreach (var (x, z, side) in Runs(L, r, occ, 1, rng, 2, s => true))
            {
                var (pos, yaw) = OnWall(x, z, side, 0.42f, 0.5f);
                Add(L, r, PropKind.Shelving, pos, yaw, new Vector3(1.0f, 1.85f, 0.42f), rng.Next(1 << 16));
            }
        }

        static void Security(OfficeLayout L, System.Random rng, Room r, HashSet<Vector2Int> occ, int doorSide)
        {
            // The desk faces the room (its monitors on it), against the wall across from the door if it fits.
            int opposite = doorSide ^ 1;
            var runs = Runs(L, r, occ, 2, rng, 1, s => s == opposite);
            if (runs.Count == 0) runs = Runs(L, r, occ, 2, rng, 1, s => s != doorSide);
            if (runs.Count > 0)
            {
                var (x, z, side) = runs[0];
                var (pos, yaw) = OnWall(x, z, side, 0.72f, 1f);                    // 1: the middle of the two cells
                int monitors = 2 + rng.Next(2);
                Add(L, r, PropKind.Desk, pos, yaw, new Vector3(1.7f, 0.74f, 0.72f), 1 | (monitors << 1) | (rng.Next(1 << 12) << 4));
                // The desk lamp at one end (plugged in when the room is made), a chair pulled out in front.
                var fwd = Fwd(yaw); var right = new Vector2(fwd.y, -fwd.x);
                // (Its shade turned across the desk, toward the middle and a little to the front.)
                Add(L, r, PropKind.DeskLamp, pos + right * 0.62f + fwd * 0.05f, yaw - 65f, new Vector3(0f, 0.74f, 0f), 0, true);
                Add(L, r, PropKind.OfficeChair, pos + fwd * 0.85f + right * ((float)rng.NextDouble() - 0.5f) * 0.3f, yaw + 180f + (float)(rng.NextDouble() - 0.5) * 50f,
                    new Vector3(0.6f, 1f, 0.6f), rng.Next(8));
                MarkFront(occ, x, z, side, 2);
                // Outlets behind the desk (the lamp's cord reaches one).
                for (int k = 0; k < 2; k++)
                    L.plates.Add(new WallPlate { cell = side < 2 ? new Vector2Int(x, z + k) : new Vector2Int(x + k, z), side = side, t = 0.5f, height = 0.3f, roomColor = r.color, live = true });
            }
            foreach (var (x, z, side) in Runs(L, r, occ, 1, rng, 1, s => s != doorSide))
            {
                var (pos, yaw) = OnWall(x, z, side, 0.42f, 0.5f);
                Add(L, r, PropKind.Shelving, pos, yaw, new Vector3(1.0f, 1.85f, 0.42f), rng.Next(1 << 16));
            }
        }

        static void Break(OfficeLayout L, System.Random rng, Room r, HashSet<Vector2Int> occ, int doorSide)
        {
            // A counter along one wall (sink in it, microwave on it), the fridge at its end, a table in the middle.
            int len = Mathf.Min(3, Mathf.Max(r.rect.width, r.rect.height) - 1);
            var runs = new List<(int, int, int)>();
            for (int l = len; l >= 2 && runs.Count == 0; l--) { runs = Runs(L, r, occ, l, rng, 1, s => s != doorSide); if (runs.Count > 0) len = l; }
            if (runs.Count > 0)
            {
                var (x, z, side) = runs[0];
                var (pos, yaw) = OnWall(x, z, side, 0.62f, len * 0.5f);
                Add(L, r, PropKind.Counter, pos, yaw, new Vector3(len * C - 0.08f, 0.9f, 0.62f), 1 | 2 | (rng.Next(1 << 12) << 4));
                // The fridge in the next cell along the wall, if it is free.
                bool alongZ = side < 2;
                int fx = alongZ ? x : x + len, fz = alongZ ? z + len : z;
                var fc = new Vector2Int(fx, fz);
                if (!(r.rect.Contains(fc) && Free(L, occ, fx, fz, side))) { fx = alongZ ? x : x - 1; fz = alongZ ? z - 1 : z; fc = new Vector2Int(fx, fz); }
                if (r.rect.Contains(fc) && Free(L, occ, fx, fz, side))
                {
                    var (fp, fyaw) = OnWall(fx, fz, side, 0.7f, 0.5f);
                    Add(L, r, PropKind.Fridge, fp, fyaw, new Vector3(0.72f, 1.72f, 0.7f), rng.Next(1 << 16));
                    occ.Add(fc);
                }
                MarkFront(occ, x, z, side, len);
            }
            // The table: in the middle, clear of the counter's cells.
            var rc = r.rect;
            if (Mathf.Min(rc.width, rc.height) >= 3)
            {
                var mid = new Vector2((rc.xMin + rc.width * 0.5f) * C, (rc.yMin + rc.height * 0.5f) * C);
                float tyaw = rc.width >= rc.height ? 0f : 90f;
                Add(L, r, PropKind.DiningTable, mid, tyaw, new Vector3(1.2f, 0.74f, 0.8f), rng.Next(1 << 16));
                var f = Fwd(tyaw);
                int chairs = 2 + rng.Next(2);
                for (int i = 0; i < chairs; i++)
                {
                    float s = i % 2 == 0 ? 1f : -1f;
                    var along = new Vector2(f.y, -f.x) * (i >= 2 ? 0.35f : -0.25f + (float)rng.NextDouble() * 0.2f);
                    var at = mid + f * (0.7f * s) + along;
                    Add(L, r, PropKind.OfficeChair, at, tyaw + (s > 0 ? 180f : 0f) + (float)(rng.NextDouble() - 0.5) * 40f, new Vector3(0.6f, 1f, 0.6f), rng.Next(8));
                }
            }
        }

        static void Stock(OfficeLayout L, System.Random rng, Room r, HashSet<Vector2Int> occ, int doorSide)
        {
            // Loaded steel shelving along every free stretch of wall (a gap here and there), and in a wide room a
            // back-to-back row down the middle with aisles round it.
            var rc = r.rect;
            var size = new Vector3(1.1f, 2.0f, 0.46f);
            for (int s = 0; s < 4; s++)
            {
                bool alongZ = s < 2;
                int n = alongZ ? rc.height : rc.width;
                int fixedC = s == 0 ? rc.xMin : s == 1 ? rc.xMax - 1 : s == 2 ? rc.yMin : rc.yMax - 1;
                int a0 = alongZ ? rc.yMin : rc.xMin;
                for (int i = 0; i < n; i++)
                {
                    int x = alongZ ? fixedC : a0 + i, z = alongZ ? a0 + i : fixedC;
                    if (!Free(L, occ, x, z, s) || rng.NextDouble() < 0.12) continue;
                    var (pos, yaw) = OnWall(x, z, s, size.z, 0.5f);
                    Add(L, r, PropKind.Shelving, pos, yaw, size, rng.Next(1 << 16) | (1 << 16));
                    occ.Add(new Vector2Int(x, z));                        // (a corner takes one unit, not two)
                }
            }
            if (Mathf.Min(rc.width, rc.height) < 4) return;
            bool alongX = rc.width >= rc.height;
            int len = alongX ? rc.width : rc.height;
            float mid = alongX ? (rc.yMin + rc.height * 0.5f) * C : (rc.xMin + rc.width * 0.5f) * C;
            float half = size.z * 0.5f + 0.01f;
            for (int i = 1; i < len - 1; i++)
            {
                float a = ((alongX ? rc.xMin : rc.yMin) + i + 0.5f) * C;
                for (int k = -1; k <= 1; k += 2)
                {
                    var pos = alongX ? new Vector2(a, mid + k * half) : new Vector2(mid + k * half, a);
                    float yaw = alongX ? (k > 0 ? 0f : 180f) : (k > 0 ? 90f : -90f);
                    Add(L, r, PropKind.Shelving, pos, yaw, size, rng.Next(1 << 16) | (1 << 16));
                }
            }
        }

        // ---------------- Helpers ----------------

        static void Add(OfficeLayout L, Room r, PropKind k, Vector2 pos, float yaw, Vector3 size, int variant, bool lit = false)
        {
            L.props.Add(new Prop { kind = k, position = pos, yaw = yaw, size = size, variant = variant, lit = lit, roomColor = r.color });
        }

        static Vector2 Fwd(float yaw) { float a = yaw * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)); }

        /// <summary>The foot of something backed against the wall on a cell's side: depth deep, at fraction t along the
        /// wall (from the cell's low corner; 1 = the boundary with the next cell). Yaw turns its front (+Z) into the room.</summary>
        static (Vector2 pos, float yaw) OnWall(int x, int z, int side, float depth, float t)
        {
            float off = T * 0.5f + depth * 0.5f + 0.01f;
            Vector2 pos = side == 0 ? new Vector2(x * C + off, (z + t) * C) : side == 1 ? new Vector2((x + 1) * C - off, (z + t) * C)
                        : side == 2 ? new Vector2((x + t) * C, z * C + off) : new Vector2((x + t) * C, (z + 1) * C - off);
            float yaw = side == 0 ? 90f : side == 1 ? -90f : side == 2 ? 0f : 180f;
            return (pos, yaw);
        }

        static bool Free(OfficeLayout L, HashSet<Vector2Int> occ, int x, int z, int side) =>
            L.EdgeOf(x, z, side) == EdgeKind.Wall && !occ.Contains(new Vector2Int(x, z)) && !DistrictGenerator.CellHasDoor(L, x, z);

        /// <summary>Single wall cells (for wall-mounted things).</summary>
        static List<(int x, int z, int side)> WallSlots(OfficeLayout L, Room r, HashSet<Vector2Int> occ, int len, System.Func<int, bool> sideOk)
        {
            var list = new List<(int, int, int)>();
            var rc = r.rect;
            for (int x = rc.xMin; x < rc.xMax; x++)
                for (int z = rc.yMin; z < rc.yMax; z++)
                    for (int s = 0; s < 4; s++)
                    {
                        if (!sideOk(s) || !OnSide(rc, x, z, s)) continue;
                        if (Free(L, occ, x, z, s)) list.Add((x, z, s));
                    }
            return list;
        }

        static bool OnSide(RectInt rc, int x, int z, int s) =>
            s == 0 ? x == rc.xMin : s == 1 ? x == rc.xMax - 1 : s == 2 ? z == rc.yMin : z == rc.yMax - 1;

        /// <summary>Up to <paramref name="count"/> runs of <paramref name="len"/> free wall cells (marked taken), each as its
        /// first cell (lowest along the wall) and side.</summary>
        static List<(int x, int z, int side)> Runs(OfficeLayout L, Room r, HashSet<Vector2Int> occ, int len, System.Random rng, int count, System.Func<int, bool> sideOk)
        {
            var found = new List<(int, int, int)>();
            var rc = r.rect;
            var order = new List<int> { 0, 1, 2, 3 };
            for (int i = 3; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            foreach (int s in order)
            {
                if (!sideOk(s)) continue;
                bool alongZ = s < 2;
                int n = alongZ ? rc.height : rc.width;
                int fixedC = s == 0 ? rc.xMin : s == 1 ? rc.xMax - 1 : s == 2 ? rc.yMin : rc.yMax - 1;
                int a0 = alongZ ? rc.yMin : rc.xMin;
                var starts = new List<int>();
                for (int st = 0; st + len <= n; st++) starts.Add(st);
                for (int i = starts.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (starts[i], starts[j]) = (starts[j], starts[i]); }
                foreach (int st in starts)
                {
                    if (found.Count >= count) return found;
                    bool ok = true;
                    for (int k = 0; k < len && ok; k++)
                    {
                        int x = alongZ ? fixedC : a0 + st + k, z = alongZ ? a0 + st + k : fixedC;
                        ok = Free(L, occ, x, z, s);
                    }
                    if (!ok) continue;
                    for (int k = 0; k < len; k++) occ.Add(alongZ ? new Vector2Int(fixedC, a0 + st + k) : new Vector2Int(a0 + st + k, fixedC));
                    found.Add(alongZ ? (fixedC, a0 + st, s) : (a0 + st, fixedC, s));
                    break;                                                   // one run a wall
                }
            }
            return found;
        }

        /// <summary>A free corner cell with walls on both outer sides: its cell and one of the two walls.</summary>
        static bool Corner(OfficeLayout L, Room r, HashSet<Vector2Int> occ, System.Random rng, out int x, out int z, out int side)
        {
            var rc = r.rect;
            int first = rng.Next(4);
            for (int i = 0; i < 4; i++)
            {
                int c = (first + i) & 3;
                x = (c & 1) == 0 ? rc.xMin : rc.xMax - 1; z = (c & 2) == 0 ? rc.yMin : rc.yMax - 1;
                int sx = (c & 1) == 0 ? 0 : 1, sz = (c & 2) == 0 ? 2 : 3;
                if (Free(L, occ, x, z, sx) && L.EdgeOf(x, z, sz) == EdgeKind.Wall) { side = sx; return true; }
            }
            x = z = side = -1;
            return false;
        }

        /// <summary>Keeps the cells in front of a run clear (a chair goes there, you stand there).</summary>
        static void MarkFront(HashSet<Vector2Int> occ, int x, int z, int side, int len)
        {
            int dx = side == 0 ? 1 : side == 1 ? -1 : 0, dz = side == 2 ? 1 : side == 3 ? -1 : 0;
            for (int k = 0; k < len; k++)
                occ.Add(side < 2 ? new Vector2Int(x + dx, z + k) : new Vector2Int(x + k, z + dz));
        }
    }
}
