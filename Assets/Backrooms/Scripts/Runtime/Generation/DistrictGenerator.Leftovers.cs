using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// What was left in Level 0's empty offices (2026-11-03), rare: a few cubicle partitions still standing (a run, an L or
    /// a T, now and then one fallen flat), and a wooden chair on its own in the middle of a room. Their own dice and placed
    /// after everything else, so nothing else in the plan moves or renumbers. Built by OfficeChunkBuilder.Partition and
    /// .WoodenChair.
    /// </summary>
    public static partial class DistrictGenerator
    {
        /// <summary>A partition panel's width, height (53 or 65 inches) and thickness, m.</summary>
        static readonly float[] PartitionWidths = { 1.2f, 1.2f, 0.9f };
        static readonly float[] PartitionHeights = { 1.35f, 1.35f, 1.65f };
        public const float PartitionThickness = 0.06f;
        /// <summary>How often an office has partitions left in it, and a room a chair in its middle.</summary>
        const double PartitionChance = 0.16, ChairChance = 0.05;
        /// <summary>How often a hallway, or another room of the backrooms, has a drinks machine against its wall.</summary>
        const double VendingHallway = 0.01, VendingOffice = 0.004;

        /// <summary>How often a room of the backrooms has a lawn chair, or a few school chairs, left in it.</summary>
        const double LawnChance = 0.02, SchoolChance = 0.015;

        static void PlaceLeftovers(OfficeLayout L, System.Random rng, List<Room> rooms)
        {
            if (L.storey > 0) UpstairsFinds(L, rooms);
            if (L.style == ZoneStyle.Pool) { PoolChairs(L, rooms); return; }
            foreach (var r in rooms)
            {
                // Two draws a room whatever happens, so each room's dice do not depend on the others'.
                double u = rng.NextDouble(), v = rng.NextDouble();
                // (2026-11-04) A backpack left by a wall: in some offices, often in a break room. Dice of its own.
                TryBackpack(L, r);
                // A cart: a utility cart in stock rooms (and now and then an office or a break room), a janitor's cart by the
                // closets and in a hallway now and then. Dice of its own.
                TryCart(L, r);
                if (r.landmark != null || r.noFloor || r.special != GoodRoom.None || r.feature == RoomFeature.Pit || r.ramp != 0 || r.custom || r.home) continue;
                // Now and then a drinks machine humming against a hallway's or an office's wall (2026-11-04; dice of its own,
                // from the room's draws, so nothing else moves).
                if (StyleOf(r) == RoomStyle.Backrooms && r.kind != RoomKind.Crawl)
                {
                    var vr = new System.Random(Hash((int)(v * 1e9), r.rect.x, r.rect.y, 0x7E4D));
                    if (vr.NextDouble() < (r.kind == RoomKind.Hallway ? VendingHallway : VendingOffice))
                        SpecialRooms.TryVending(L, r, vr, new HashSet<Vector2Int>(), -1, false);
                }
                if (StyleOf(r) != RoomStyle.Backrooms || r.kind == RoomKind.Hallway || r.kind == RoomKind.Crawl) continue;
                var sub = new System.Random(Hash((int)(u * 1e9), r.rect.x, r.rect.y, 0x1EF7));
                if (r.rect.width >= 4 && r.rect.height >= 4 && (r.kind == RoomKind.BigOffice || r.kind == RoomKind.Office) && u < PartitionChance)
                    TryPartitions(L, sub, r);
                if (r.rect.width >= 3 && r.rect.height >= 3 && v < ChairChance) TryChair(L, sub, r);
                // (2026-11-04) A lawn chair on its own, or two or three school chairs: dice of their own from the room's.
                var cr = new System.Random(Hash((int)(u * 1e9), r.rect.x, r.rect.y, 0xC4A1));
                double c1 = cr.NextDouble(), c2 = cr.NextDouble();
                if (r.rect.width >= 2 && r.rect.height >= 2 && c1 < LawnChance) TryPlastic(L, cr, r, PropKind.LawnChair, 1);
                if (r.rect.width >= 3 && r.rect.height >= 3 && c2 < SchoolChance) TryPlastic(L, cr, r, PropKind.SchoolChair, 2 + cr.Next(2));
            }
        }

        const double BackpackOffice = 0.03, BackpackBreak = 0.25;
        const double CartStock = 0.5, CartOffice = 0.004, CartBreak = 0.12, JanitorCloset = 0.45, JanitorHall = 0.006;

        /// <summary>
        /// A cart left in a room (2026-11-04): a two-shelf utility cart in half the stock rooms, now and then in an office or a
        /// break room; a janitor's cart in some janitor's closets (if it fits beside the sink) and very now and then in a
        /// hallway. Along a wall, its length with it, a little off it; never across a doorway.
        /// </summary>
        static void TryCart(OfficeLayout L, Room r)
        {
            if (r.noFloor || r.custom || r.home || r.landmark != null || r.ramp != 0 || r.feature == RoomFeature.Pit) return;
            var rr = new System.Random(Hash(r.rect.x * 7 + 3, r.rect.y, L.storey * 17 + r.rect.height, 0xCA27));
            double roll = rr.NextDouble();
            PropKind kind;
            if (r.special == GoodRoom.StockRoom && roll < CartStock) kind = PropKind.UtilityCart;
            else if (r.special == GoodRoom.BreakRoom && roll < CartBreak) kind = PropKind.UtilityCart;
            else if (r.special == GoodRoom.JanitorCloset && roll < JanitorCloset) kind = PropKind.JanitorCart;
            else if (r.special == GoodRoom.None && StyleOf(r) == RoomStyle.Backrooms && r.kind == RoomKind.Hallway && roll < JanitorHall) kind = PropKind.JanitorCart;
            else if (r.special == GoodRoom.None && StyleOf(r) == RoomStyle.Backrooms && (r.kind == RoomKind.Office || r.kind == RoomKind.BigOffice) && roll < CartOffice) kind = PropKind.UtilityCart;
            else return;
            var size = kind == PropKind.UtilityCart ? new Vector2(0.56f, 1.03f) : new Vector2(0.62f, 1.24f);
            TryAlongWall(L, r, rr, kind, size, 0.12f, rr.Next(1 << 20));
        }

        /// <summary>A thing of this footprint (across, along) along one of the room's walls, a gap off it; false if none fits.</summary>
        static bool TryAlongWall(OfficeLayout L, Room r, System.Random rr, PropKind kind, Vector2 size, float gap, int variant)
        {
            float C = OfficeBiome.CellSize;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int side = rr.Next(4);
                float t = (float)rr.NextDouble();
                float x0 = r.rect.xMin * C, x1 = r.rect.xMax * C, z0 = r.rect.yMin * C, z1 = r.rect.yMax * C;
                float off = size.x * 0.5f + gap, run = size.y * 0.5f + 0.1f;
                Vector2 c; float yaw;
                // its length along the wall (its front one way or the other)
                bool flip = rr.NextDouble() < 0.5;
                switch (side)
                {
                    case 0: c = new Vector2(Mathf.Lerp(x0 + run, x1 - run, t), z0 + off); yaw = flip ? 90f : 270f; break;
                    case 1: c = new Vector2(Mathf.Lerp(x0 + run, x1 - run, t), z1 - off); yaw = flip ? 90f : 270f; break;
                    case 2: c = new Vector2(x0 + off, Mathf.Lerp(z0 + run, z1 - run, t)); yaw = flip ? 0f : 180f; break;
                    default: c = new Vector2(x1 - off, Mathf.Lerp(z0 + run, z1 - run, t)); yaw = flip ? 0f : 180f; break;
                }
                if (x1 - x0 < 2f * run && (side == 0 || side == 1)) continue;
                if (z1 - z0 < 2f * run && (side == 2 || side == 3)) continue;
                yaw += ((float)rr.NextDouble() - 0.5f) * 10f;
                FootBounds(yaw, new Vector2(size.x, size.y), out float hx, out float hz);
                if (!RugFits(L, r, c, size, yaw, hx, hz)) continue;
                if (!ClearOfProps(L, c, hx, hz, 0.12f)) continue;
                L.props.Add(new Prop { kind = kind, position = c, yaw = yaw, size = new Vector3(size.x, 1f, size.y), variant = variant, roomColor = r.color });
                return true;
            }
            return false;
        }

        /// <summary>No other prop's footprint within margin of this one's (both as boxes square to the grid).</summary>
        static bool ClearOfProps(OfficeLayout L, Vector2 c, float hx, float hz, float margin)
        {
            foreach (var pr in L.props)
            {
                FootBounds(pr.yaw, new Vector2(pr.size.x, pr.size.z), out float px, out float pz);
                if (Mathf.Abs(pr.position.x - c.x) < hx + px + margin && Mathf.Abs(pr.position.y - c.y) < hz + pz + margin) return false;
            }
            return true;
        }

        /// <summary>A platform trolley left in a warehouse (2026-11-04): in about one rack room in five, along a wall or at an aisle's end.</summary>
        internal static void TryTruck(OfficeLayout L, Room r)
        {
            var rr = new System.Random(Hash(r.rect.x * 13 + 1, r.rect.y * 5, L.storey * 29 + r.rect.width, 0x7A0C));
            if (rr.NextDouble() >= 0.2) return;
            if (TryAlongWall(L, r, rr, PropKind.PlatformTruck, new Vector2(0.62f, 0.95f), 0.25f, rr.Next(1 << 20))) return;
            // Else anywhere clear on the floor (an aisle's end, a cross aisle), square to the racks.
            float C = OfficeBiome.CellSize;
            var size = new Vector2(0.62f, 0.95f);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                var c = new Vector2((r.rect.xMin + 0.6f + (float)rr.NextDouble() * (r.rect.width - 1.2f)) * C,
                                    (r.rect.yMin + 0.6f + (float)rr.NextDouble() * (r.rect.height - 1.2f)) * C);
                float yaw = rr.Next(4) * 90f + ((float)rr.NextDouble() - 0.5f) * 14f;
                FootBounds(yaw, size, out float hx, out float hz);
                if (!RugFits(L, r, c, size, yaw, hx, hz) || !ClearOfProps(L, c, hx, hz, 0.35f)) continue;
                L.props.Add(new Prop { kind = PropKind.PlatformTruck, position = c, yaw = yaw, size = new Vector3(size.x, 1f, size.y), variant = rr.Next(1 << 20), roomColor = r.color });
                return;
            }
        }

        /// <summary>
        /// A backpack left in a room (2026-11-04, the owner: "found fairly common"): in about one office in thirty and a
        /// break room in four, standing against a wall with its back to it, or now and then lying on its back by it.
        /// Its own dice from the room's place and storey (nothing else moves).
        /// </summary>
        static void TryBackpack(OfficeLayout L, Room r)
        {
            if (r.noFloor || r.custom || r.home || r.landmark != null || r.ramp != 0 || r.feature == RoomFeature.Pit) return;
            bool office = r.special == GoodRoom.None && StyleOf(r) == RoomStyle.Backrooms
                          && (r.kind == RoomKind.Office || r.kind == RoomKind.BigOffice || r.kind == RoomKind.Lounge || r.kind == RoomKind.Conference);
            bool breakRoom = r.special == GoodRoom.BreakRoom;
            if (!office && !breakRoom) return;
            var rr = new System.Random(Hash(r.rect.x, r.rect.y, L.storey * 31 + r.rect.width, 0xBA6B));
            if (rr.NextDouble() >= (breakRoom ? BackpackBreak : BackpackOffice)) return;
            float C = OfficeBiome.CellSize;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int side = rr.Next(4);
                float t = (float)rr.NextDouble();
                bool lying = rr.NextDouble() < 0.3;
                float off = lying ? 0.45f : 0.2f;
                float x0 = r.rect.xMin * C, x1 = r.rect.xMax * C, z0 = r.rect.yMin * C, z1 = r.rect.yMax * C;
                Vector2 c; float yaw;
                switch (side)
                {
                    case 0: c = new Vector2(Mathf.Lerp(x0 + 0.5f, x1 - 0.5f, t), z0 + off); yaw = 0f; break;
                    case 1: c = new Vector2(Mathf.Lerp(x0 + 0.5f, x1 - 0.5f, t), z1 - off); yaw = 180f; break;
                    case 2: c = new Vector2(x0 + off, Mathf.Lerp(z0 + 0.5f, z1 - 0.5f, t)); yaw = 90f; break;
                    default: c = new Vector2(x1 - off, Mathf.Lerp(z0 + 0.5f, z1 - 0.5f, t)); yaw = 270f; break;
                }
                yaw += ((float)rr.NextDouble() - 0.5f) * (lying ? 70f : 18f);
                int cx = Mathf.FloorToInt(c.x / C), cz = Mathf.FloorToInt(c.y / C);
                if (!L.InChunk(cx, cz) || L.roomOf[cx, cz] != r.id) continue;
                bool clear = true;
                foreach (var pr in L.props) if ((pr.position - c).sqrMagnitude < 0.6f * 0.6f) { clear = false; break; }
                if (!clear) continue;
                int variant = rr.Next(3) | (lying ? 4 : 0) | (rr.Next(1 << 20) << 3);
                L.props.Add(new Prop { kind = PropKind.Backpack, position = c, yaw = yaw, size = new Vector3(0.31f, 0.44f, 0.2f), variant = variant, roomColor = r.color });
                return;
            }
        }

        /// <summary>How often each side of an atrium's balcony has a boombox left on it (about two atriums in five).</summary>
        const double BoomboxChance = 0.12;

        /// <summary>
        /// Upstairs (2026-11-04): on an atrium's upper storey, its balcony's four sides each now and then have something left
        /// on them, rare: a boombox (PropKind.Boombox, an item). Dice of its own from the room's place and storey.
        /// </summary>
        static void UpstairsFinds(OfficeLayout L, List<Room> rooms)
        {
            float C = OfficeBiome.CellSize;
            foreach (var r in rooms)
            {
                if (r.noFloor || r.landmark == null || r.kind != RoomKind.Hallway || r.role != AnnexRole.None || r.custom) continue;
                bool atrium = false;
                foreach (var lm in L.landmarks) if (lm.kind == LandmarkKind.Atrium && lm.name == r.landmark) { atrium = true; break; }
                if (!atrium) continue;
                var fr = r.fullRect.width > 0 ? r.fullRect : r.rect;
                var rr = new System.Random(Hash(fr.x, fr.y, L.storey, 0xB00B5));
                if (rr.NextDouble() >= BoomboxChance) continue;
                var c = new Vector2((r.rect.xMin + 0.5f + (float)rr.NextDouble() * Mathf.Max(0, r.rect.width - 1)) * C,
                                    (r.rect.yMin + 0.5f + (float)rr.NextDouble() * Mathf.Max(0, r.rect.height - 1)) * C);
                bool clear = true;
                foreach (var pr in L.props) if ((pr.position - c).sqrMagnitude < 0.8f * 0.8f) { clear = false; break; }
                if (!clear) continue;
                L.props.Add(new Prop { kind = PropKind.Boombox, position = c, yaw = (float)rr.NextDouble() * 360f, size = new Vector3(0.5f, 0.21f, 0.3f),
                    variant = rr.Next(1 << 20), roomColor = r.color });
            }
        }

        /// <summary>A plastic chair or a few, somewhere clear in the room, each turned its own way (a row, more or less).</summary>
        static void TryPlastic(OfficeLayout L, System.Random rng, Room r, PropKind kind, int count)
        {
            float C = OfficeBiome.CellSize;
            var size = kind == PropKind.LawnChair ? new Vector2(0.62f, 0.58f) : new Vector2(0.47f, 0.58f);
            int colour = rng.Next(4) == 0 ? 1 : 0;
            float rowYaw = (float)rng.NextDouble() * 360f;
            var start = new Vector2((r.rect.xMin + 0.5f + (float)rng.NextDouble() * (r.rect.width - 1)) * C, (r.rect.yMin + 0.5f + (float)rng.NextDouble() * (r.rect.height - 1)) * C);
            var step = new Vector2(Mathf.Cos(rowYaw * Mathf.Deg2Rad), -Mathf.Sin(rowYaw * Mathf.Deg2Rad)) * 0.62f;
            for (int i = 0; i < count; i++)
            {
                var c = start + step * i + new Vector2(((float)rng.NextDouble() - 0.5f) * 0.12f, ((float)rng.NextDouble() - 0.5f) * 0.12f);
                float yaw = rowYaw + 90f + ((float)rng.NextDouble() - 0.5f) * (count > 1 ? 20f : 360f);
                FootBounds(yaw, size, out float hx, out float hz);
                if (!RugFits(L, r, c, size, yaw, hx, hz)) continue;
                bool clear = true;
                foreach (var pr in L.props) if ((pr.position - c).sqrMagnitude < 0.7f * 0.7f) { clear = false; break; }
                if (!clear) continue;
                L.props.Add(new Prop { kind = kind, position = c, yaw = yaw, size = new Vector3(size.x, 0.85f, size.y), variant = colour | (rng.Next(1 << 20) << 1), roomColor = r.color });
            }
        }

        /// <summary>
        /// Lawn chairs on the Poolrooms' decks (2026-11-04): now and then a pool room gets one or two, a step back from the
        /// water's edge and facing it, clear of the room's fittings (ladders, a lifeguard's chair, showers...).
        /// </summary>
        static void PoolChairs(OfficeLayout L, List<Room> rooms)
        {
            float C = OfficeBiome.CellSize;
            foreach (var r in rooms)
            {
                if (!r.Walkable || r.noFloor) continue;
                var rng = new System.Random(Hash(L.coord.x * 7 + r.rect.x, L.coord.y * 13 + r.rect.y, r.rect.width, 0x9A1C));
                if (rng.NextDouble() > 0.35) continue;
                // Deck cells beside the water, the way to the water from each.
                var edge = new List<(int x, int z, Vector2 toWater)>();
                for (int x = r.rect.xMin; x < r.rect.xMax; x++)
                for (int z = r.rect.yMin; z < r.rect.yMax; z++)
                {
                    if (!L.InChunk(x, z) || L.roomOf[x, z] != r.id || L.pool == null || L.pool[x, z].basin) continue;
                    for (int s = 0; s < 4; s++)
                    {
                        int nx = x + (s == 0 ? -1 : s == 1 ? 1 : 0), nz = z + (s == 2 ? -1 : s == 3 ? 1 : 0);
                        if (L.InChunk(nx, nz) && L.pool[nx, nz].basin) { edge.Add((x, z, new Vector2(nx - x, nz - z))); break; }
                    }
                }
                if (edge.Count == 0) continue;
                int want = 1 + rng.Next(2);
                for (int k = 0; k < 8 && want > 0; k++)
                {
                    var (ex, ez, tw) = edge[rng.Next(edge.Count)];
                    var c = new Vector2((ex + 0.5f) * C, (ez + 0.5f) * C) - tw * 0.15f + new Vector2(tw.y, -tw.x) * (((float)rng.NextDouble() - 0.5f) * 0.6f);
                    float yaw = Mathf.Atan2(tw.x, tw.y) * Mathf.Rad2Deg + ((float)rng.NextDouble() - 0.5f) * 30f;
                    bool clear = true;
                    foreach (var f in L.poolFeatures)
                    {
                        var b = f.bounds;
                        if (b.width > 0f && new Rect(b.xMin - 0.9f, b.yMin - 0.9f, b.width + 1.8f, b.height + 1.8f).Contains(c)) { clear = false; break; }
                        if ((f.centre - c).sqrMagnitude < 1.6f * 1.6f) { clear = false; break; }
                    }
                    foreach (var pr in L.props) if ((pr.position - c).sqrMagnitude < 0.8f * 0.8f) { clear = false; break; }
                    if (!clear) continue;
                    L.props.Add(new Prop { kind = PropKind.LawnChair, position = c, yaw = yaw, size = new Vector3(0.62f, 0.85f, 0.58f), variant = (rng.Next(3) == 0 ? 1 : 0) | (rng.Next(1 << 20) << 1), roomColor = r.color });
                    want--;
                }
            }
        }

        static void FootBounds(float yaw, Vector2 size, out float hx, out float hz)
        {
            float cs = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)), sn = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad));
            hx = cs * size.x * 0.5f + sn * size.y * 0.5f; hz = sn * size.x * 0.5f + cs * size.y * 0.5f;
        }

        static void TryChair(OfficeLayout L, System.Random rng, Room r)
        {
            float C = OfficeBiome.CellSize;
            var size = new Vector2(0.48f, 0.52f);
            var mid = new Vector2((r.rect.xMin + r.rect.xMax) * 0.5f * C, (r.rect.yMin + r.rect.yMax) * 0.5f * C);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float spread = 0.15f + 0.25f * attempt;
                var c = mid + new Vector2(((float)rng.NextDouble() - 0.5f) * 2f * spread, ((float)rng.NextDouble() - 0.5f) * 2f * spread);
                float yaw = (float)rng.NextDouble() * 360f;
                FootBounds(yaw, size, out float hx, out float hz);
                if (!RugFits(L, r, c, size, yaw, hx, hz)) continue;
                L.props.Add(new Prop { kind = PropKind.WoodenChair, position = c, yaw = yaw, size = new Vector3(size.x, 0.9f, size.y), variant = rng.Next(1 << 30), roomColor = r.color });
                return;
            }
        }

        static void TryPartitions(OfficeLayout L, System.Random rng, Room r)
        {
            float C = OfficeBiome.CellSize;
            int pick = rng.Next(PartitionWidths.Length);
            float W = PartitionWidths[pick], H = PartitionHeights[rng.Next(PartitionHeights.Length)], Tk = PartitionThickness;
            // The layout in its own frame: centre and yaw (0: the panel's face toward +z) of each panel.
            var panels = new List<(Vector2 c, float yaw)>();
            int shape = rng.Next(3);
            if (shape == 0)
            {
                int n = 2 + rng.Next(3);
                for (int i = 0; i < n; i++) panels.Add((new Vector2((i - (n - 1) * 0.5f) * W, 0f), 0f));
            }
            else if (shape == 1)
            {
                int n1 = 2 + rng.Next(2), n2 = 1 + rng.Next(2);
                float x0 = -(n1 - 1) * 0.5f * W;
                for (int i = 0; i < n1; i++) panels.Add((new Vector2(x0 + i * W, 0f), 0f));
                float xe = x0 + (n1 - 0.5f) * W;
                for (int j = 0; j < n2; j++) panels.Add((new Vector2(xe, (j + 0.5f) * W), 90f));
            }
            else
            {
                for (int i = -1; i <= 1; i++) panels.Add((new Vector2(i * W, 0f), 0f));
                panels.Add((new Vector2(0f, 0.5f * W), 90f));
                if (rng.NextDouble() < 0.5) panels.Add((new Vector2(0f, -0.5f * W), 90f));
            }
            int fallen = rng.NextDouble() < 0.15 ? rng.Next(panels.Count) : -1;
            var mid = new Vector2((r.rect.xMin + r.rect.xMax) * 0.5f * C, (r.rect.yMin + r.rect.yMax) * 0.5f * C);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float baseYaw = (rng.NextDouble() < 0.5 ? 0f : 90f) + ((float)rng.NextDouble() - 0.5f) * 3f;
                var off = new Vector2(((float)rng.NextDouble() - 0.5f) * 1.2f, ((float)rng.NextDouble() - 0.5f) * 1.2f);
                var q = Quaternion.Euler(0f, baseYaw, 0f);
                var placed = new List<Prop>();
                bool ok = true;
                for (int i = 0; i < panels.Count && ok; i++)
                {
                    var (lc, ly) = panels[i];
                    var w3 = q * new Vector3(lc.x, 0f, lc.y);
                    var c = mid + off + new Vector2(w3.x, w3.z);
                    float yaw = baseYaw + ly;
                    var foot = new Vector2(W, Tk + 0.24f);
                    if (i == fallen)
                    {
                        // Fallen flat on its face, its foot where it stood: its middle half its height out.
                        var n = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                        c += new Vector2(n.x, n.z) * (H * 0.5f);
                        foot = new Vector2(W, H);
                    }
                    FootBounds(yaw, foot, out float hx, out float hz);
                    // (The panels of one cluster touch each other: each is checked against the room and what was there.)
                    if (!RugFits(L, r, c, foot, yaw, hx, hz)) { ok = false; break; }
                    // (variant bit 1: fallen; bit 2: the first of its cluster, where F5 > PLACES takes you.)
                    placed.Add(new Prop { kind = PropKind.Partition, position = c, yaw = yaw, size = new Vector3(W, H, Tk), variant = (i == fallen ? 1 : 0) | (i == 0 ? 2 : 0), roomColor = r.color });
                }
                if (!ok) continue;
                L.props.AddRange(placed);
                return;
            }
        }
    }
}
