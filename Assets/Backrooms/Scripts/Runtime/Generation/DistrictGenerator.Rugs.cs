using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Rugs (2026-11-02): now and then a living room has a children's road rug on its floor (the owner's design, a car play
    /// mat), at one of the sizes such rugs are sold in, lying in the open part of the room: clear of the walls (and the
    /// carpet's loose edges along them), doorways, steps, pillars, half walls and the furniture, square to the room or
    /// kicked a little askew. Built by OfficeChunkBuilder.Rug; it never moves.
    /// </summary>
    public static partial class DistrictGenerator
    {
        /// <summary>Length x width, m: the sizes road rugs are sold in (80 x 120 to 133 x 200 cm), the commonest twice.</summary>
        static readonly Vector2[] RugSizes =
        {
            new Vector2(1.2f, 0.8f), new Vector2(1.33f, 0.95f), new Vector2(1.5f, 1.0f), new Vector2(1.5f, 1.0f),
            new Vector2(1.65f, 1.0f), new Vector2(2.0f, 1.33f),
        };
        /// <summary>A rug's thickness: low printed pile on a rubber backing, m.</summary>
        public const float RugThickness = 0.007f;
        /// <summary>How far a rug stays from the walls, m (the carpet's lifted edges and ripples are within 0.3 m of them).</summary>
        const float RugWallGap = 0.45f;
        /// <summary>How often a living room has one: an emptied home, a furnished living room.</summary>
        const double RugChanceHome = 0.22, RugChanceLiving = 0.25;

        /// <summary>
        /// Lays a rug in some of these rooms (living rooms only). custom: build mode's FURNISH FOR ME, whose rooms are the
        /// player's own (the generator leaves those alone).
        /// </summary>
        static void PlaceRugs(OfficeLayout L, System.Random rng, List<Room> rooms, bool custom)
        {
            if (L.style == ZoneStyle.Pool) return;
            foreach (var r in rooms)
            {
                if (r.landmark != null || r.noFloor || r.special != GoodRoom.None || r.feature == RoomFeature.Pit || r.ramp != 0) continue;
                if (r.custom != custom) continue;
                if (StyleOf(r) != RoomStyle.LivingRoom) continue;
                if (rng.NextDouble() >= (r.home ? RugChanceHome : RugChanceLiving)) continue;
                TryRug(L, rng, r);
            }
        }

        static bool TryRug(OfficeLayout L, System.Random rng, Room r)
        {
            float C = OfficeBiome.CellSize;
            var rc = r.rect;
            float x0 = rc.xMin * C + RugWallGap, x1 = rc.xMax * C - RugWallGap;
            float z0 = rc.yMin * C + RugWallGap, z1 = rc.yMax * C - RugWallGap;
            bool longX = rc.width >= rc.height;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                // Later tries take smaller rugs (the first ones may not fit).
                int pick = attempt < 12 ? rng.Next(RugSizes.Length) : rng.Next(Mathf.Max(1, RugSizes.Length - 1 - (attempt - 12) / 3));
                var size = RugSizes[pick];
                // Its long side along the room's, mostly; square to the room or kicked a little askew.
                bool alongX = rng.NextDouble() < 0.8 ? longX : !longX;
                float jitter = rng.NextDouble() < 0.7 ? 3f : 14f;
                float yaw = (alongX ? 0f : 90f) + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                float cs = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)), sn = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad));
                float hx = cs * size.x * 0.5f + sn * size.y * 0.5f, hz = sn * size.x * 0.5f + cs * size.y * 0.5f;
                if (x1 - x0 < 2f * hx || z1 - z0 < 2f * hz) continue;
                // Near the middle of the room's open floor.
                float mx = (x0 + x1) * 0.5f, mz = (z0 + z1) * 0.5f;
                float cx = mx + ((float)rng.NextDouble() - 0.5f) * (x1 - x0 - 2f * hx) * 0.8f;
                float cz = mz + ((float)rng.NextDouble() - 0.5f) * (z1 - z0 - 2f * hz) * 0.8f;
                // In one chunk where it can (its chunk draws it whole).
                if (attempt < 14 && !OneChunk(cx - hx, cz - hz, cx + hx, cz + hz)) continue;
                if (!RugFits(L, r, new Vector2(cx, cz), size, yaw, hx, hz)) continue;
                L.props.Add(new Prop { kind = PropKind.Rug, position = new Vector2(cx, cz), yaw = yaw,
                    size = new Vector3(size.x, RugThickness, size.y), variant = rng.Next(1 << 30), roomColor = r.color });
                return true;
            }
            return false;
        }

        static bool OneChunk(float ax, float az, float bx, float bz)
        {
            float S = OfficeBiome.ChunkSize;
            return Mathf.FloorToInt(ax / S) == Mathf.FloorToInt(bx / S) && Mathf.FloorToInt(az / S) == Mathf.FloorToInt(bz / S);
        }

        static bool RugFits(OfficeLayout L, Room r, Vector2 c, Vector2 size, float yaw, float hx, float hz)
        {
            float C = OfficeBiome.CellSize;
            // Every cell under it: this room's, flat, with a floor, no doorway, steps or corner cut off.
            int ax = Mathf.FloorToInt((c.x - hx - 0.02f) / C), bx = Mathf.FloorToInt((c.x + hx + 0.02f) / C);
            int az = Mathf.FloorToInt((c.y - hz - 0.02f) / C), bz = Mathf.FloorToInt((c.y + hz + 0.02f) / C);
            var fr = r.rect;
            for (int x = ax; x <= bx; x++)
            for (int z = az; z <= bz; z++)
            {
                if (!L.InChunk(x, z) || L.roomOf[x, z] != r.id || L.Has(x, z, CellFlags.NoFloor)) return false;
                if (L.StepSide(x, z) >= 0 || CellHasDoor(L, x, z)) return false;
                for (int k = 0; k < 4; k++)
                {
                    if ((r.chamfers & (1 << k)) == 0) continue;
                    int kx = (k & 1) == 0 ? fr.xMin : fr.xMax - 1, kz = (k & 2) == 0 ? fr.yMin : fr.yMax - 1;
                    if (x == kx && z == kz) return false;
                }
            }
            // Pillars and half walls.
            foreach (var p in L.pillars)
                if (Overlaps(c, size, yaw, new Vector2(p.vertex.x * C, p.vertex.y * C), new Vector2(p.size + 0.1f, p.size + 0.1f), 0f)) return false;
            foreach (var h in L.halfWalls)
            {
                float len = h.length * C;
                var mid = h.alongX ? new Vector2((h.start * C) + len * 0.5f, h.line * C) : new Vector2(h.line * C, (h.start * C) + len * 0.5f);
                var sz = h.alongX ? new Vector2(len + 0.2f, 0.3f) : new Vector2(0.3f, len + 0.2f);
                if (Overlaps(c, size, yaw, mid, sz, 0f)) return false;
            }
            // The furniture (sofas, armchairs, lamps, boxes) and any other rug.
            foreach (var p in L.props)
            {
                if (p.kind == PropKind.WallClock) continue;
                var ps = new Vector2(Mathf.Max(p.size.x, 0.5f) + 0.1f, Mathf.Max(p.size.z, 0.5f) + 0.1f);
                if (Overlaps(c, size, yaw, p.position, ps, p.yaw)) return false;
            }
            return true;
        }

        /// <summary>Two rectangles on the floor (centre, full size, yaw in degrees) overlap (separating axes).</summary>
        public static bool Overlaps(Vector2 ca, Vector2 sa, float yawA, Vector2 cb, Vector2 sb, float yawB)
        {
            // Unity's yaw turns +X toward -Z: local X = (cos, -sin), local Z = (sin, cos).
            float ra = yawA * Mathf.Deg2Rad, rb = yawB * Mathf.Deg2Rad;
            var axA = new Vector2(Mathf.Cos(ra), -Mathf.Sin(ra)); var azA = new Vector2(Mathf.Sin(ra), Mathf.Cos(ra));
            var axB = new Vector2(Mathf.Cos(rb), -Mathf.Sin(rb)); var azB = new Vector2(Mathf.Sin(rb), Mathf.Cos(rb));
            var d = cb - ca;
            foreach (var n in new[] { axA, azA, axB, azB })
            {
                float pa = Mathf.Abs(Vector2.Dot(axA, n)) * sa.x * 0.5f + Mathf.Abs(Vector2.Dot(azA, n)) * sa.y * 0.5f;
                float pb = Mathf.Abs(Vector2.Dot(axB, n)) * sb.x * 0.5f + Mathf.Abs(Vector2.Dot(azB, n)) * sb.y * 0.5f;
                if (Mathf.Abs(Vector2.Dot(d, n)) > pa + pb) return false;
            }
            return true;
        }
    }
}
