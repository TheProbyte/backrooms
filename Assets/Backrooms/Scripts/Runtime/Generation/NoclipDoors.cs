using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Noclip doors: a door-sized section of an ordinary wall that looks solid but can be walked through, into another
    /// zone. The way into the Poolrooms (they replaced its far crawl hole).
    ///  - Level 0: one door per square of <see cref="WorldGenSettings.noclipDoorSpacing"/> x spacing districts (so rare),
    ///    in a district and on a wall picked from the world seed. Each leads to the arrival door of one Poolrooms district
    ///    (also picked from the seed).
    ///  - The Poolrooms: one arrival door per district, on a wall with a dry deck in front. Walking back through it returns
    ///    you to the door you came in by (Interaction.NoclipTravel remembers it).
    /// Pure data on the generator's worker threads; each end is placed by its own district, the Level 0 end finds its
    /// partner's exact door when a chunk is cut from the plan (WorldGenerator.ResolveNoclipDoors). The look of a door
    /// (blue tape, for now) is not part of this: see Interaction.NoclipDoorMarking.
    /// </summary>
    public static class NoclipDoors
    {
        /// <summary>The walkable opening (the wall around it keeps its colliders).</summary>
        public const float Width = 0.95f, Height = 2.15f;
        const float C = OfficeBiome.CellSize;

        static int Hash(int seed, int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)x * 0x85EBCA77u ^ (uint)y * 0xC2B2AE3Du ^ (uint)salt * 0x27D4EB2Fu;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>The pinned zone noclip doors lead to (the Poolrooms), or -1.</summary>
        public static int TargetZone(WorldGenSettings set)
        {
            for (int i = 0; i < set.zones.Count; i++) if (set.zones[i].pinned && set.zones[i].style == ZoneStyle.Pool) return i;
            return -1;
        }

        /// <summary>Does this Level 0 district hold its square's noclip door, and which Poolrooms district does it lead to?</summary>
        public static bool LinkHere(WorldGenSettings set, int seed, Vector2Int district, out Vector2Int partner)
        {
            partner = default;
            int target = TargetZone(set);
            if (target < 0 || set.noclipDoorSpacing <= 0) return false;
            int S = Mathf.Max(2, set.noclipDoorSpacing);
            var sq = new Vector2Int(FloorDiv(district.x, S), FloorDiv(district.y, S));
            int h = Hash(seed, sq.x, sq.y, 0x0C11D);
            var chosen = sq * S + new Vector2Int(h % S, (h / S) % S);
            if (chosen != district) return false;
            if (set.PinnedZoneAt(district) >= 0) return false;
            var zi = set.ZoneAt(seed, district).zone;
            if (zi < 0 || zi >= set.zones.Count || set.zones[zi].style != ZoneStyle.Office) return false;
            var z = set.zones[target];
            int r = z.pinRadius, h2 = Hash(seed, sq.x, sq.y, 0x9001);
            partner = z.pinCentre + new Vector2Int(h2 % (2 * r + 1) - r, (h2 / (2 * r + 1)) % (2 * r + 1) - r);
            return true;
        }

        public static void Place(OfficeLayout L, WorldGenSettings set, int seed)
        {
            if (L.storey != 0 || set == null) return;
            int target = TargetZone(set);
            if (target < 0) return;
            if (L.pool != null)
            {
                if (set.zones[target].PinnedContains(L.coord)) PlaceDoor(L, seed, true, default);
                return;
            }
            if (LinkHere(set, seed, L.coord, out var partner)) PlaceDoor(L, seed, false, partner);
        }

        static Vector2Int Step(int side) => side == 0 ? new Vector2Int(-1, 0) : side == 1 ? new Vector2Int(1, 0) : side == 2 ? new Vector2Int(0, -1) : new Vector2Int(0, 1);

        /// <summary>World frame of a door on the given side of a cell: the middle of the wall line at floor level, normal into the cell.</summary>
        public static void Frame(OfficeLayout L, Vector2Int cell, int side, out Vector3 position, out Vector3 normal)
        {
            float ox = L.coord.x * WorldGenSettings.DistrictCells * C, oz = L.coord.y * WorldGenSettings.DistrictCells * C;
            float cx = (cell.x + 0.5f) * C, cz = (cell.y + 0.5f) * C;
            switch (side)
            {
                case 0: position = new Vector3(cell.x * C, 0f, cz); normal = Vector3.right; break;
                case 1: position = new Vector3((cell.x + 1) * C, 0f, cz); normal = Vector3.left; break;
                case 2: position = new Vector3(cx, 0f, cell.y * C); normal = Vector3.forward; break;
                default: position = new Vector3(cx, 0f, (cell.y + 1) * C); normal = Vector3.back; break;
            }
            position += new Vector3(ox, L.BaseY, oz);
        }

        static void PlaceDoor(OfficeLayout L, int seed, bool arrival, Vector2Int partner)
        {
            var walls = Candidates(L, arrival);
            if (walls.Count == 0) return;
            var rng = new System.Random(Hash(seed, L.coord.x, L.coord.y, 0xD00F));
            var w = walls[rng.Next(walls.Count)];
            Frame(L, w.cell, w.side, out var pos, out var n);
            L.noclipDoors.Add(new NoclipDoor
            {
                cell = w.cell, side = w.side, position = pos, normal = n, arrival = arrival, partnerDistrict = partner,
                id = WorldGenerator.PropId(L.coord, L.storey, 0xE000 + L.noclipDoors.Count),
            });
            // Nothing on either face of that wall (an outlet would sit in the door).
            var o = w.cell + Step(w.side);
            int back = w.side ^ 1;
            L.plates.RemoveAll(p => (p.cell == w.cell && p.side == w.side) || (p.cell == o && p.side == back));
        }

        /// <summary>
        /// Walls that can hold a noclip door: a solid edge between two ordinary walkable rooms tall enough for it, inside a
        /// longer stretch of wall (solid edges on both sides along the line), not on a chunk border, with nothing against
        /// it on either side and no crawl hole. For the Poolrooms' arrival doors the front cell is a dry deck.
        /// </summary>
        static List<(Vector2Int cell, int side)> Candidates(OfficeLayout L, bool dryFront)
        {
            var list = new List<(Vector2Int, int)>();
            int n = L.N, cc = OfficeBiome.ChunkCells;
            var busy = new HashSet<Vector2Int>();
            foreach (var p in L.props) busy.Add(new Vector2Int(Mathf.FloorToInt(p.position.x / C), Mathf.FloorToInt(p.position.y / C)));
            foreach (var pl in L.pillars) { busy.Add(pl.vertex); busy.Add(pl.vertex - Vector2Int.one); busy.Add(pl.vertex - Vector2Int.right); busy.Add(pl.vertex - Vector2Int.up); }
            foreach (var h in L.halfWalls)
                for (int k = 0; k < h.length; k++) busy.Add(h.alongX ? new Vector2Int(h.start + k, h.line) : new Vector2Int(h.line, h.start + k));
            var holes = new HashSet<(Vector2Int, int)>();
            foreach (var p in L.portals) { holes.Add((p.cell, p.side)); holes.Add((p.cell + Step(p.side), p.side ^ 1)); }

            bool Ordinary(int x, int z)
            {
                if (!L.InChunk(x, z)) return false;
                var r = L.RoomAt(x, z);
                return r.Walkable && r.landmark == null && !r.custom && !r.noFloor && r.role == AnnexRole.None && !L.Has(x, z, CellFlags.NoFloor) && r.ceiling >= Height + 0.25f && !r.spiral;
            }
            for (int x = 1; x < n - 1; x++)
            for (int z = 1; z < n - 1; z++)
            {
                if (!Ordinary(x, z) || busy.Contains(new Vector2Int(x, z))) continue;
                if (dryFront && L.Basin(x, z)) continue;
                for (int side = 0; side < 4; side++)
                {
                    if (L.EdgeOf(x, z, side) != EdgeKind.Wall || holes.Contains((new Vector2Int(x, z), side))) continue;
                    var o = new Vector2Int(x, z) + Step(side);
                    if (!Ordinary(o.x, o.y) || busy.Contains(o)) continue;
                    int line = side == 0 ? x : side == 1 ? x + 1 : side == 2 ? z : z + 1;
                    if (line % cc == 0) continue;
                    // Wall on both sides of it along the line, on this side.
                    var along = side < 2 ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
                    var a = new Vector2Int(x, z) - along; var b = new Vector2Int(x, z) + along;
                    if (!L.InChunk(a.x, a.y) || !L.InChunk(b.x, b.y)) continue;
                    if (L.EdgeOf(a.x, a.y, side) != EdgeKind.Wall || L.EdgeOf(b.x, b.y, side) != EdgeKind.Wall) continue;
                    list.Add((new Vector2Int(x, z), side));
                }
            }
            return list;
        }
    }
}
