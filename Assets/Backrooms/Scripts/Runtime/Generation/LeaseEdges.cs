using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A contract floor's edge (LeasePlan, Docs/ContractFloorsPlan.md): the walls along it are solid (the district edges
    /// there are sealed), and dressed so it reads as the end of the rented space, not just a wall. A hall running into
    /// the edge ends at a chained fire door or plastic sheeting taped across (the building goes on behind it); along a
    /// room's wall, now and then, a bricked-up doorway or a NOT LEASED sign. Wall fittings (PropKind.Fitting, pinned),
    /// built by Tools/Blender/build_lease.py. Their own dice, last of the district's props, so nothing else moves.
    /// </summary>
    public static class LeaseEdges
    {
        const float C = OfficeBiome.CellSize, T = OfficeBiome.WallThickness;

        public static void Dress(OfficeLayout L, Vector2Int district, System.Random rng)
        {
            int slot = LeasePlan.FloorOf(district);
            if (slot < 0) return;
            int n = L.N;
            var dirs = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };
            for (int side = 0; side < 4; side++)
            {
                if (LeasePlan.FloorOf(district + dirs[side]) == slot) continue;     // an inside edge (or into a wing: LeaseGates)
                int last = -100;
                for (int i = 0; i < n; i++)
                {
                    if (i - last < 7) continue;
                    Cell(side, i, n, out int x, out int z);
                    if (!Usable(L, x, z, side, out var r)) continue;
                    bool hall = r.kind == RoomKind.Hallway;
                    int across = side < 2 ? r.rect.height : r.rect.width;          // the room's width along the edge
                    // Two cells of the same room along the edge, both walled and clear: room for a door or a sheet.
                    Cell(side, i + 1, n, out int x2, out int z2);
                    bool two = i + 1 < n && Usable(L, x2, z2, side, out var r2) && r2 == r;
                    string model = null; float t = 0.5f;
                    double roll = rng.NextDouble();
                    if (hall && across <= 3)
                    {
                        // A hall that runs into the edge: it went on, once.
                        if (two) { model = roll < 0.55 ? "LeaseFireDoor" : "LeaseSheeting"; t = 1f; }
                        else model = roll < 0.6 ? "LeaseBricked" : "LeaseSign";
                    }
                    else if (roll < 0.06) model = "LeaseBricked";
                    else if (roll < 0.11) model = "LeaseSign";
                    else if (roll < 0.14 && two) { model = "LeaseSheeting"; t = 1f; }
                    if (model == null) continue;
                    var pos = OnWall(x, z, side, t);
                    bool crowded = false;
                    foreach (var pr in L.props)
                        if ((pr.position - pos).sqrMagnitude < 1.3f * 1.3f) { crowded = true; break; }
                    if (crowded) continue;
                    L.props.Add(new Prop
                    {
                        kind = PropKind.Fitting, model = model, position = pos, yaw = Yaw(side), pinned = true, roomColor = r.color,
                        elevation = model == "LeaseSign" ? 1.45f : 0f,
                    });
                    last = i + (t > 0.9f ? 1 : 0);
                }
            }
        }

        /// <summary>The i-th cell along a district side (0 west, 1 east, 2 south, 3 north).</summary>
        static void Cell(int side, int i, int n, out int x, out int z)
        {
            x = side == 0 ? 0 : side == 1 ? n - 1 : i;
            z = side == 2 ? 0 : side == 3 ? n - 1 : i;
        }

        static bool Usable(OfficeLayout L, int x, int z, int side, out Room r)
        {
            r = null;
            if (x < 0 || z < 0 || x >= L.N || z >= L.N || L.roomOf[x, z] < 0) return false;
            r = L.RoomAt(x, z);
            if (r.landmark != null || r.custom || r.noFloor || r.kind == RoomKind.Crawl) return false;
            if (L.EdgeOf(x, z, side) != EdgeKind.Wall || DistrictGenerator.CellHasDoor(L, x, z)) return false;
            return true;
        }

        /// <summary>On the wall's face (a wall fitting's back on it), t along the cell (1: the corner with the next cell).</summary>
        static Vector2 OnWall(int x, int z, int side, float t)
        {
            float off = T * 0.5f + 0.004f;
            return side == 0 ? new Vector2(x * C + off, (z + t) * C) : side == 1 ? new Vector2((x + 1) * C - off, (z + t) * C)
                 : side == 2 ? new Vector2((x + t) * C, z * C + off) : new Vector2((x + t) * C, (z + 1) * C - off);
        }

        static float Yaw(int side) => side == 0 ? 90f : side == 1 ? -90f : side == 2 ? 0f : 180f;
    }
}
