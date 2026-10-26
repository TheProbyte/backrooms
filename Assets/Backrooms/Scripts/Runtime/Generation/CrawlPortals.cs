using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Non-Euclidean crawl holes. A pair of low square holes at the foot of two walls in one district. Each hole leads into a
    /// short crawl duct behind its wall (it overlaps the room behind, which never sees it: see Interaction.CrawlPortalView);
    /// crawling to the duct's far end brings you out of the other hole, in a different room, facing a different way.
    ///
    /// The two ends are kept within streaming and light-grid range of each other (about 12 to 30 m apart in a straight
    /// line), and chosen so that walking between them is long (or impossible), which is what makes the shortcut feel wrong.
    /// Pure data; runs on the generator's worker threads.
    /// </summary>
    public static class CrawlPortals
    {
        public const float HoleSize = 0.8f, HoleHalf = HoleSize * 0.5f;           // square opening (the crawling player is 0.62 m tall, 0.6 m wide)
        public const float MinApart = 12f, MaxApart = 30f;
        /// <summary>Crawl duct length from the wall face to the window at its far end (inside the two free cells behind the wall).</summary>
        public const float DuctLength = 2.2f;
        public const int MinWalkCells = 26;           // shortest walk between the two ends, in cells (about 31 m), unless unreachable

        const float C = OfficeBiome.CellSize, T = OfficeBiome.WallThickness;

        static Vector2Int Step(int side) => side == 0 ? new Vector2Int(-1, 0) : side == 1 ? new Vector2Int(1, 0) : side == 2 ? new Vector2Int(0, -1) : new Vector2Int(0, 1);

        /// <summary>World frame of a hole entered from cell (district-local) on the given side.</summary>
        public static void Frame(Vector2Int district, Vector2Int cell, int side, float baseY, out Vector3 position, out Vector3 normal)
        {
            float ox = district.x * WorldGenSettings.DistrictCells * C, oz = district.y * WorldGenSettings.DistrictCells * C;
            float cx = (cell.x + 0.5f) * C, cz = (cell.y + 0.5f) * C, y = baseY + HoleSize * 0.5f;
            switch (side)
            {
                case 0: position = new Vector3(cell.x * C + T * 0.5f, y, cz); normal = Vector3.right; break;
                case 1: position = new Vector3((cell.x + 1) * C - T * 0.5f, y, cz); normal = Vector3.left; break;
                case 2: position = new Vector3(cx, y, cell.y * C + T * 0.5f); normal = Vector3.forward; break;
                default: position = new Vector3(cx, y, (cell.y + 1) * C - T * 0.5f); normal = Vector3.back; break;
            }
            position += new Vector3(ox, 0f, oz);
        }

        public static void Place(OfficeLayout L, WorldGenSettings set, System.Random rng)
        {
            if (L.storey != 0 || set == null) return;
            float want = Mathf.Max(0f, set.crawlPortals);
            int pairs = Mathf.FloorToInt(want) + (rng.NextDouble() < want - Mathf.Floor(want) ? 1 : 0);
            bool far = FarLinks.PartnerOf(set, L.coord, out var farDistrict);
            if (pairs <= 0 && !far) return;

            var candidates = Candidates(L);
            var used = new HashSet<Vector2Int>();
            PlacePairs(L, candidates, used, pairs, rng);
            if (far) PlaceFar(L, candidates, used, farDistrict);
            ClearPlates(L);
        }

        /// <summary>
        /// Outlets are placed before the holes: drop any low plate on either face of a wall that now has a hole (it would
        /// float in the opening). Light switches sit above the hole and stay. Nothing else of the plan changes.
        /// </summary>
        static void ClearPlates(OfficeLayout L)
        {
            if (L.portals.Count == 0 || L.plates.Count == 0) return;
            L.plates.RemoveAll(plate =>
            {
                if (plate.height > HoleSize + 0.15f) return false;
                foreach (var p in L.portals)
                {
                    var behind = p.cell + Step(p.side);
                    int back = p.side ^ 1;   // 0 <-> 1, 2 <-> 3
                    if ((plate.cell == p.cell && plate.side == p.side) || (plate.cell == behind && plate.side == back)) return true;
                }
                return false;
            });
        }

        /// <summary>
        /// The district's end of a far link (see FarLinks): one hole on a free candidate wall, chosen from its own random
        /// stream (so the ordinary pairs are unchanged). Its partner is the other district's far hole, found when a chunk is
        /// cut from the plan; a link whose other end could not be placed is dropped there.
        /// </summary>
        static void PlaceFar(OfficeLayout L, List<(Vector2Int cell, int side)> candidates, HashSet<Vector2Int> used, Vector2Int partnerDistrict)
        {
            var rng = new System.Random(unchecked(L.coord.x * 73856093 ^ L.coord.y * 19349663 ^ 0x5FA2));
            var free = new List<(Vector2Int cell, int side)>();
            foreach (var c in candidates) if (!used.Contains(c.cell) && !used.Contains(c.cell + Step(c.side))) free.Add(c);
            if (free.Count == 0) return;
            var a = free[rng.Next(free.Count)];
            Frame(L.coord, a.cell, a.side, L.BaseY, out var pa, out var na);
            L.portals.Add(new CrawlPortal { cell = a.cell, side = a.side, position = pa, normal = na, partnerPosition = pa, partnerNormal = na,
                                            far = true, partnerDistrict = partnerDistrict, id = WorldGenerator.PropId(L.coord, L.storey, 0xFF00) });
            used.Add(a.cell); used.Add(a.cell + Step(a.side));
        }

        static void PlacePairs(OfficeLayout L, List<(Vector2Int cell, int side)> candidates, HashSet<Vector2Int> used, int pairs, System.Random rng)
        {
            if (pairs <= 0 || candidates.Count < 2) return;
            for (int p = 0; p < pairs; p++)
            {
                bool done = false;
                for (int attempt = 0; attempt < 24 && !done; attempt++)
                {
                    var a = candidates[rng.Next(candidates.Count)];
                    if (used.Contains(a.cell) || used.Contains(a.cell + Step(a.side))) continue;
                    var walk = Distances(L, a.cell);
                    var options = new List<(Vector2Int cell, int side)>();
                    foreach (var b in candidates)
                    {
                        if (used.Contains(b.cell) || used.Contains(b.cell + Step(b.side))) continue;
                        float apart = Vector2.Distance(b.cell, a.cell) * C;
                        if (apart < MinApart || apart > MaxApart) continue;
                        if (L.roomOf[a.cell.x, a.cell.y] == L.roomOf[b.cell.x, b.cell.y]) continue;
                        int d = walk[b.cell.x, b.cell.y];
                        if (d >= 0 && d < MinWalkCells) continue;
                        options.Add(b);
                    }
                    if (options.Count == 0) continue;
                    var chosen = options[rng.Next(options.Count)];
                    Add(L, a, chosen);
                    foreach (var c in new[] { a.cell, a.cell + Step(a.side), chosen.cell, chosen.cell + Step(chosen.side) }) used.Add(c);
                    done = true;
                }
            }
        }

        static void Add(OfficeLayout L, (Vector2Int cell, int side) a, (Vector2Int cell, int side) b)
        {
            Frame(L.coord, a.cell, a.side, L.BaseY, out var pa, out var na);
            Frame(L.coord, b.cell, b.side, L.BaseY, out var pb, out var nb);
            int i = L.portals.Count;
            L.portals.Add(new CrawlPortal { cell = a.cell, side = a.side, position = pa, normal = na, partnerPosition = pb, partnerNormal = nb, id = WorldGenerator.PropId(L.coord, L.storey, 0xF000 + i) });
            L.portals.Add(new CrawlPortal { cell = b.cell, side = b.side, position = pb, normal = nb, partnerPosition = pa, partnerNormal = na, id = WorldGenerator.PropId(L.coord, L.storey, 0xF001 + i) });
        }

        /// <summary>
        /// Walls that can take a hole: a solid wall between two ordinary walkable rooms (so the far side exists and can
        /// be walked around), not on a chunk border, with nothing in the way on either side.
        /// </summary>
        static List<(Vector2Int cell, int side)> Candidates(OfficeLayout L)
        {
            var list = new List<(Vector2Int, int)>();
            int n = L.N, cc = OfficeBiome.ChunkCells;
            var busy = new HashSet<Vector2Int>();
            foreach (var p in L.props) busy.Add(new Vector2Int(Mathf.FloorToInt(p.position.x / C), Mathf.FloorToInt(p.position.y / C)));
            foreach (var pl in L.pillars) { busy.Add(pl.vertex); busy.Add(pl.vertex - Vector2Int.one); busy.Add(pl.vertex - Vector2Int.right); busy.Add(pl.vertex - Vector2Int.up); }
            foreach (var h in L.halfWalls)
                for (int k = 0; k < h.length; k++)
                {
                    var c = h.alongX ? new Vector2Int(h.start + k, h.line) : new Vector2Int(h.line, h.start + k);
                    busy.Add(c); busy.Add(h.alongX ? c - Vector2Int.up : c - Vector2Int.right);
                }

            bool Ordinary(int x, int z)
            {
                if (!L.InChunk(x, z)) return false;
                var r = L.RoomAt(x, z);
                return r.Walkable && r.landmark == null && !r.custom && !r.noFloor && !r.openAbove && r.role == AnnexRole.None && !L.Has(x, z, CellFlags.NoFloor);
            }

            for (int x = 1; x < n - 1; x++)
            for (int z = 1; z < n - 1; z++)
            {
                if (!Ordinary(x, z) || busy.Contains(new Vector2Int(x, z))) continue;
                for (int side = 0; side < 4; side++)
                {
                    if (L.EdgeOf(x, z, side) != EdgeKind.Wall) continue;
                    var o = new Vector2Int(x, z) + Step(side);
                    if (!Ordinary(o.x, o.y) || busy.Contains(o)) continue;
                    // The edge must not lie on a chunk border (both ends of the hole and its duct are built by one chunk).
                    int line = side == 0 ? x : side == 1 ? x + 1 : side == 2 ? z : z + 1;
                    if (line % cc == 0) continue;
                    // Keep entrances away from adjacent door intersections.
                    bool doorBeside = false;
                    for (int s = 0; s < 4; s++) { var e = L.EdgeOf(o.x, o.y, s); if (e == EdgeKind.Door || e == EdgeKind.DoorHeader) doorBeside = true; }
                    if (doorBeside) continue;
                    // The duct runs through the two cells behind the wall: both free and in one room, so nothing of that room is inside it.
                    var o2 = o + Step(side);
                    if (!Ordinary(o2.x, o2.y) || busy.Contains(o2) || L.roomOf[o.x, o.y] != L.roomOf[o2.x, o2.y]) continue;
                    if (L.EdgeOf(o.x, o.y, side) != EdgeKind.Open) continue;
                    list.Add((new Vector2Int(x, z), side));
                }
            }
            return list;
        }

        /// <summary>Walking distance in cells from a cell to every cell of the layout (-1 = unreachable).</summary>
        static int[,] Distances(OfficeLayout L, Vector2Int from)
        {
            var d = new int[L.N, L.N];
            for (int x = 0; x < L.N; x++) for (int z = 0; z < L.N; z++) d[x, z] = -1;
            var q = new Queue<Vector2Int>();
            d[from.x, from.y] = 0; q.Enqueue(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int side = 0; side < 4; side++)
                {
                    var nb = c + Step(side);
                    if (!L.InChunk(nb.x, nb.y) || d[nb.x, nb.y] >= 0) continue;
                    if (!OfficeLayout.IsPassable(L.EdgeOf(c.x, c.y, side)) || !L.RoomAt(nb.x, nb.y).Walkable) continue;
                    d[nb.x, nb.y] = d[c.x, c.y] + 1; q.Enqueue(nb);
                }
            }
            return d;
        }
    }
}
