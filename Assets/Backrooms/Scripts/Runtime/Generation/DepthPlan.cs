using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Height and depth in a district's plan (world version 14, Docs/WorldDepthPlan.md). Runs on the district plan after the
    /// rooms are joined up and before they are dressed (Apply), and once more at the end (Chamfers):
    ///  - <b>A sunken wing</b>: one to three joined rooms 0.45-0.9 m below the storey's floor, their ceilings where they were
    ///    (so taller). Every doorway into the wing gets steps down inside the wing's cell beside it (Room.floor, the layout's
    ///    steps). A wing never touches the district's edge, since districts are planned apart and could not agree on a
    ///    floor across it; nor a cell with two doorways out (two flights would cross).
    ///  - <b>A pit room</b> (the reference: Kane Pixels' pit room): a big room's floor becomes a lattice of walkways 0.48 m
    ///    wide on a 2.4 m pitch (0.6 m along the walls, a whole landing at each doorway) over square shafts 1.9 m across,
    ///    mostly 1.8-2.4 m deep, each with a block in a corner half as high as it is deep, so you can climb out in two pulls
    ///    (CamcorderPlayerController's mantle). A few are shallow (0.6-0.9 m) and a few filled in. Storey 0 only (a shaft
    ///    would poke into the ceiling of a room below).
    ///  - <b>Corners cut at 45 degrees</b> (Chamfers): a room's free corners (walls on both outer sides, nothing in the
    ///    corner cell) get a diagonal wall across the corner cell.
    /// Its own dice, so the rest of the plan does not change with it.
    /// </summary>
    public static class DepthPlan
    {
        /// <summary>A step's rise (m).</summary>
        public const float Rise = 0.15f;
        /// <summary>Walkways in a pit room: between shafts (half on each side of a lattice line), and along the walls (m).</summary>
        public const float PitWalk = 0.24f, PitRim = 0.6f;
        /// <summary>The lattice's pitch in cells (2.4 m).</summary>
        public const int PitPitch = 2;

        static readonly float[] Depths = { 0.45f, 0.45f, 0.6f, 0.6f, 0.9f };

        /// <summary>How many rises a sunken floor's steps have.</summary>
        public static int Steps(float depth) => Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(depth) / Rise));

        /// <summary>How far each step reaches (m): 0.3, less for a deep flight so it fits one cell.</summary>
        public static float Tread(float depth) => Mathf.Min(0.3f, (OfficeBiome.CellSize - 0.04f) / Steps(depth));

        public static void Apply(OfficeLayout L, System.Random rng)
        {
            if (L.style == ZoneStyle.Pool) return;
            var R = L.RulesAt(L.N / 2, L.N / 2);
            if (L.storey == 0 && rng.NextDouble() < R.pitRoomChance) TryPitRoom(L, rng);
            if (rng.NextDouble() < R.sunkenChance) TrySunkenWing(L, rng);
        }

        // ---------------- Sunken wing ----------------

        static bool SpawnHall(OfficeLayout L, Room r) => L.coord == Vector2Int.zero && L.storey == 0 && r.rect.Overlaps(new RectInt(0, 0, OfficeBiome.ChunkCells, OfficeBiome.ChunkCells));

        static bool Ordinary(OfficeLayout L, Room r) =>
            r.Walkable && r.landmark == null && !r.custom && !r.noFloor && !r.openAbove && r.role == AnnexRole.None && !r.spiral
            && r.feature == RoomFeature.None && !SpawnHall(L, r);

        static bool SunkenOk(OfficeLayout L, Room r)
        {
            if (!Ordinary(L, r) || r.kind == RoomKind.Conference) return false;
            var rc = r.rect;
            if (Mathf.Min(rc.width, rc.height) < 3 || rc.width * rc.height < 12) return false;
            // Not on the district's edge (cells on the outer lines).
            return rc.xMin > 0 && rc.yMin > 0 && rc.xMax < L.N && rc.yMax < L.N;
        }

        static readonly int[] DX = { -1, 1, 0, 0 }, DZ = { 0, 0, -1, 1 };

        /// <summary>
        /// Picks a room, grows it over passable edges to up to three ordinary rooms, and sinks them if every cell has at most
        /// one doorway out of the wing. The steps go in the cells beside those doorways.
        /// </summary>
        static void TrySunkenWing(OfficeLayout L, System.Random rng)
        {
            var candidates = new List<Room>();
            foreach (var r in L.rooms) if (SunkenOk(L, r)) candidates.Add(r);
            for (int attempt = 0; attempt < 6 && candidates.Count > 0; attempt++)
            {
                var seed = candidates[rng.Next(candidates.Count)];
                var wing = new HashSet<int> { seed.id };
                int want = 1 + (rng.NextDouble() < 0.5 ? 1 : 0) + (rng.NextDouble() < 0.25 ? 1 : 0);
                for (int grow = 0; grow < 4 && wing.Count < want; grow++)
                {
                    var next = new List<int>();
                    foreach (int id in wing) foreach (int nb in PassableNeighbours(L, L.rooms[id]))
                        if (!wing.Contains(nb) && SunkenOk(L, L.rooms[nb])) next.Add(nb);
                    if (next.Count == 0) break;
                    wing.Add(next[rng.Next(next.Count)]);
                }
                if (!PlaceSteps(L, wing, out var steps)) continue;
                // At least one way in (a sealed wing would never be reached).
                if (steps.Count == 0) continue;
                float depth = Depths[rng.Next(Depths.Length)];
                foreach (int id in wing) { L.rooms[id].floor = -depth; L.rooms[id].feature = RoomFeature.Sunken; }
                if (L.steps == null) { L.steps = new sbyte[L.N, L.N]; for (int x = 0; x < L.N; x++) for (int z = 0; z < L.N; z++) L.steps[x, z] = -1; }
                foreach (var s in steps) L.steps[s.x, s.y] = (sbyte)s.z;
                return;
            }
        }

        static IEnumerable<int> PassableNeighbours(OfficeLayout L, Room r)
        {
            var seen = new HashSet<int>();
            var rc = r.rect;
            for (int x = rc.xMin; x < rc.xMax; x++)
            for (int z = rc.yMin; z < rc.yMax; z++)
                for (int s = 0; s < 4; s++)
                {
                    int nx = x + DX[s], nz = z + DZ[s];
                    if (!L.InChunk(nx, nz) || L.roomOf[nx, nz] == r.id || !OfficeLayout.IsPassable(L.EdgeOf(x, z, s))) continue;
                    if (seen.Add(L.roomOf[nx, nz])) yield return L.roomOf[nx, nz];
                }
        }

        /// <summary>Every passable edge out of the wing as (cell x, cell z, side); false if a cell would need two flights.</summary>
        static bool PlaceSteps(OfficeLayout L, HashSet<int> wing, out List<Vector3Int> steps)
        {
            steps = new List<Vector3Int>();
            foreach (int id in wing)
            {
                var rc = L.rooms[id].rect;
                for (int x = rc.xMin; x < rc.xMax; x++)
                for (int z = rc.yMin; z < rc.yMax; z++)
                {
                    int found = -1;
                    for (int s = 0; s < 4; s++)
                    {
                        int nx = x + DX[s], nz = z + DZ[s];
                        if (!L.InChunk(nx, nz) || wing.Contains(L.roomOf[nx, nz]) || !OfficeLayout.IsPassable(L.EdgeOf(x, z, s))) continue;
                        if (found >= 0) return false;
                        found = s;
                    }
                    if (found < 0) continue;
                    // The cell beside it in the wing (the way the steps come down) must be a floor to land on.
                    int bx = x - DX[found], bz = z - DZ[found];
                    if (!L.InChunk(bx, bz) || !wing.Contains(L.roomOf[bx, bz]) || L.EdgeOf(x, z, found ^ 1) == EdgeKind.Wall) return false;
                    steps.Add(new Vector3Int(x, z, found));
                }
            }
            return true;
        }

        // ---------------- Pit room ----------------

        static bool PitOk(OfficeLayout L, Room r)
        {
            if (!Ordinary(L, r) || (r.kind != RoomKind.BigOffice && r.kind != RoomKind.Office)) return false;
            var rc = r.rect;
            if (Mathf.Min(rc.width, rc.height) < 7 || rc.width * rc.height < 56) return false;
            // Enclosed: only doorways out (an open-plan side made a whole row of landings and ran on into the next room).
            int doors = 0;
            for (int x = rc.xMin; x < rc.xMax; x++)
            for (int z = rc.yMin; z < rc.yMax; z++)
                for (int s = 0; s < 4; s++)
                {
                    int nx = x + DX[s], nz = z + DZ[s];
                    if (L.InChunk(nx, nz) && L.roomOf[nx, nz] == r.id) continue;
                    var e = L.EdgeOf(x, z, s);
                    if (e == EdgeKind.Open) return false;
                    if (OfficeLayout.IsPassable(e)) doors++;
                }
            return doors >= 1 && doors <= 6;
        }

        static void TryPitRoom(OfficeLayout L, System.Random rng)
        {
            Room best = null; int bestArea = 0;
            foreach (var r in L.rooms)
            {
                if (!PitOk(L, r)) continue;
                int area = r.rect.width * r.rect.height + rng.Next(20);
                if (area > bestArea) { bestArea = area; best = r; }
            }
            if (best == null) return;
            var shafts = PitShafts(L, best, rng);
            if (shafts.Count < 4) return;
            best.feature = RoomFeature.Pit;
            best.painted = true;
            best.wallFinish = WallFinish.PaintedPlaster;
            L.pits.AddRange(shafts);
        }

        /// <summary>The shafts of a pit room: one per 2 x 2 cell block of the lattice that has no doorway in it and is wide
        /// enough, its depth and its corner block from the dice.</summary>
        public static List<PitShaft> PitShafts(OfficeLayout L, Room r, System.Random rng)
        {
            float C = OfficeBiome.CellSize;
            var list = new List<PitShaft>();
            var rc = r.rect;
            var xs = Lines(rc.xMin, rc.xMax);
            var zs = Lines(rc.yMin, rc.yMax);
            for (int i = 0; i + 1 < xs.Count; i++)
            for (int j = 0; j + 1 < zs.Count; j++)
            {
                int bx0 = xs[i], bx1 = xs[i + 1], bz0 = zs[j], bz1 = zs[j + 1];
                if (bx1 - bx0 < PitPitch || bz1 - bz0 < PitPitch) continue;          // the odd row along a wall: floor
                // A doorway's cell (a passable edge out of the room) stays floor: a landing.
                bool landing = false;
                for (int x = bx0; x < bx1 && !landing; x++)
                for (int z = bz0; z < bz1 && !landing; z++)
                    for (int s = 0; s < 4; s++)
                    {
                        int nx = x + DX[s], nz = z + DZ[s];
                        if (L.InChunk(nx, nz) && L.roomOf[nx, nz] == r.id) continue;
                        if (OfficeLayout.IsPassable(L.EdgeOf(x, z, s))) { landing = true; break; }
                    }
                if (landing) continue;
                float x0 = bx0 * C + (bx0 == rc.xMin ? PitRim : PitWalk), x1 = bx1 * C - (bx1 == rc.xMax ? PitRim : PitWalk);
                float z0 = bz0 * C + (bz0 == rc.yMin ? PitRim : PitWalk), z1 = bz1 * C - (bz1 == rc.yMax ? PitRim : PitWalk);
                if (x1 - x0 < 1.0f || z1 - z0 < 1.0f) continue;
                double roll = rng.NextDouble();
                if (roll < 0.1) continue;                                                // filled in
                var s0 = new PitShaft { rect = Rect.MinMaxRect(x0, z0, x1, z1), roomColor = r.id };
                if (roll < 0.25) s0.depth = Snap(0.6f + (float)rng.NextDouble() * 0.3f);    // shallow: you see its floor
                else
                {
                    // (At most 2.4 m: two pulls of 1.2; 2.55 cannot be split into two of 1.3 or less.)
                    s0.depth = Snap(1.8f + (float)rng.NextDouble() * 0.6f);
                    s0.ledge = Snap(s0.depth * 0.5f);
                    // Both pulls within the mantle with room to spare (1.3 m; it reaches 1.35).
                    if (s0.depth - s0.ledge > 1.3f) s0.ledge += Rise;
                    s0.ledgeCorner = rng.Next(4);
                }
                list.Add(s0);
            }
            return list;
        }

        /// <summary>The lattice's lines across a room: every second cell line from its wall, and its far wall.</summary>
        static List<int> Lines(int min, int max)
        {
            var l = new List<int>();
            for (int v = min; v < max; v += PitPitch) l.Add(v);
            l.Add(max);
            return l;
        }

        static float Snap(float v) => Mathf.Round(v / Rise) * Rise;

        /// <summary>The corner block of a shaft (layout-local metres): its footprint, 0.6 m square.</summary>
        public static Rect LedgeRect(PitShaft s)
        {
            const float B = 0.6f;
            float x = (s.ledgeCorner & 1) == 0 ? s.rect.xMin : s.rect.xMax - B;
            float z = (s.ledgeCorner & 2) == 0 ? s.rect.yMin : s.rect.yMax - B;
            return new Rect(x, z, B, B);
        }

        // ---------------- Corners cut at 45 degrees ----------------

        /// <summary>After the dressing: a room's free corners cut off across the corner cell, where nothing is in that cell.</summary>
        public static void Chamfers(OfficeLayout L, System.Random rng)
        {
            if (L.style == ZoneStyle.Pool) return;
            float C = OfficeBiome.CellSize;
            foreach (var r in L.rooms)
            {
                if (!Ordinary(L, r) || r.kind == RoomKind.Hallway) continue;
                var rc = r.rect;
                if (Mathf.Min(rc.width, rc.height) < 3) continue;
                if (rng.NextDouble() >= L.RulesAt(rc).chamferChance) continue;
                for (int c = 0; c < 4; c++)
                {
                    if (rng.NextDouble() >= 0.65) continue;
                    int x = (c & 1) == 0 ? rc.xMin : rc.xMax - 1, z = (c & 2) == 0 ? rc.yMin : rc.yMax - 1;
                    int sx = (c & 1) == 0 ? 0 : 1, sz = (c & 2) == 0 ? 2 : 3;
                    if (L.EdgeOf(x, z, sx) != EdgeKind.Wall || L.EdgeOf(x, z, sz) != EdgeKind.Wall) continue;
                    if (CornerBusy(L, x, z, C)) continue;
                    r.chamfers |= (byte)(1 << c);
                }
            }
        }

        /// <summary>Anything in a corner cell that a diagonal wall would cut through.</summary>
        static bool CornerBusy(OfficeLayout L, int x, int z, float C)
        {
            if (L.StepSide(x, z) >= 0) return true;
            var cell = new Vector2Int(x, z);
            foreach (var f in L.fixtures) if (f.cell == cell) return true;
            foreach (var p in L.plates) if (p.cell == cell) return true;
            foreach (var p in L.pillars) if (p.vertex.x >= x && p.vertex.x <= x + 1 && p.vertex.y >= z && p.vertex.y <= z + 1) return true;
            var box = new Rect(x * C - 0.4f, z * C - 0.4f, C + 0.8f, C + 0.8f);
            foreach (var p in L.props) if (box.Contains(p.position)) return true;
            foreach (var h in L.halfWalls)
            {
                int hx = h.alongX ? h.start : h.line, hz = h.alongX ? h.line : h.start;
                int ex = h.alongX ? h.start + h.length : h.line, ez = h.alongX ? h.line : h.start + h.length;
                if (Mathf.Max(hx, x) <= Mathf.Min(ex, x + 1) && Mathf.Max(hz, z) <= Mathf.Min(ez, z + 1)) return true;
            }
            foreach (var p in L.portals) if (p.cell == cell) return true;
            foreach (var d in L.noclipDoors) if (d.cell == cell) return true;
            return false;
        }

        /// <summary>A cell steps or a pit room's lattice are in: no furniture, boxes, pillars or crawl holes here.</summary>
        public static bool Busy(OfficeLayout L, int x, int z) => L.StepSide(x, z) >= 0 || L.RoomAt(x, z).feature == RoomFeature.Pit;
    }
}
