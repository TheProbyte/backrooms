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

        static void PlaceLeftovers(OfficeLayout L, System.Random rng, List<Room> rooms)
        {
            if (L.style == ZoneStyle.Pool) return;
            foreach (var r in rooms)
            {
                // Two draws a room whatever happens, so each room's dice do not depend on the others'.
                double u = rng.NextDouble(), v = rng.NextDouble();
                if (r.landmark != null || r.noFloor || r.special != GoodRoom.None || r.feature == RoomFeature.Pit || r.ramp != 0 || r.custom || r.home) continue;
                if (StyleOf(r) != RoomStyle.Backrooms || r.kind == RoomKind.Hallway || r.kind == RoomKind.Crawl) continue;
                var sub = new System.Random(Hash((int)(u * 1e9), r.rect.x, r.rect.y, 0x1EF7));
                if (r.rect.width >= 4 && r.rect.height >= 4 && (r.kind == RoomKind.BigOffice || r.kind == RoomKind.Office) && u < PartitionChance)
                    TryPartitions(L, sub, r);
                if (r.rect.width >= 3 && r.rect.height >= 3 && v < ChairChance) TryChair(L, sub, r);
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
