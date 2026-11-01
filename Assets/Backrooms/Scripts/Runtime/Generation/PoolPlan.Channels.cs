using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The channels biome of the Poolrooms zone (2026-11-01, the review's item 15; reference image 5 of the known gaps):
    /// narrow tiled walkways winding beside water. Its corridors are all vaulted channels, flooded, with the walkway on
    /// one side (the side changing from one corridor to the next, so the way winds from bank to bank).
    /// Its halls (2026-11-01, the owner: "connections of curves", the first walkways turned in square steps of whole
    /// cells, their gutters meeting in black crosses) are flooded wall to wall, with a walkway curving through them: a
    /// smooth tiled path 1.2 m wide from one doorway to the farthest other, swinging from side to side as it goes, and a
    /// branch from each other doorway curving in to join it (PoolFeatureKind.Walkway, built by
    /// OfficeChunkBuilder.Walkway). The water is the basins' usual depth, so it can be waded where the walkway does not go.
    /// </summary>
    public static partial class PoolPlan
    {
        public static bool IsChannels(OfficeLayout L, RectInt rc) => L.RulesAt(rc).name == "Channels";

        public const float WalkwayHalf = 0.6f;

        /// <summary>Plans a room of the channels biome; false leaves it to the usual rules.</summary>
        static bool ChannelRoom(OfficeLayout L, Room r, System.Random rng)
        {
            var rc = r.rect;
            int w = rc.width, h = rc.height, mn = Mathf.Min(w, h), mx = Mathf.Max(w, h);
            if (r.kind == RoomKind.Hallway && mn <= 3 && mx >= 4) { Vault(L, r, rng, true); return true; }
            if (mn < 4 || mx < 5) return false;
            FillCells(L, rc, c => c.basin = true);
            var doors = Doorways(L, r);
            if (doors.Count == 0) return true;
            // The main way: between the two doorways farthest apart (or from the only one to the far wall's middle).
            int ia = 0, ib = -1; float best = -1f;
            for (int i = 0; i < doors.Count; i++)
            for (int j = i + 1; j < doors.Count; j++)
            {
                float d = (doors[i].at - doors[j].at).sqrMagnitude;
                if (d > best) { best = d; ia = i; ib = j; }
            }
            var m = Metres(rc);
            (Vector2 at, Vector2 n) B;
            if (ib >= 0) B = doors[ib];
            else
            {
                // A dead end: out to the opposite wall's middle.
                var a0 = doors[ia];
                B = (a0.at + a0.n * ((Mathf.Abs(a0.n.x) > 0.5f ? m.width : m.height) - 1.4f), -a0.n);
            }
            var main = WalkwayFeature(r, doors[ia].at, doors[ia].n, B.at, B.n, false, m, rng);
            L.poolFeatures.Add(main);
            // Branches from the other doorways, curving in to meet the main way at its nearest point.
            for (int i = 0; i < doors.Count; i++)
            {
                if (i == ia || i == ib) continue;
                var d = doors[i];
                Vector2 bestP = main.centre; float bd = float.MaxValue;
                for (int k = 0; k <= 40; k++)
                {
                    var q = WalkwayPoint(main, k / 40f, out _);
                    float dd = (q - d.at).sqrMagnitude;
                    if (dd < bd) { bd = dd; bestP = q; }
                }
                if (bd < 1.5f * 1.5f) continue;                       // it already passes close
                var dir = (d.at - bestP).normalized;
                L.poolFeatures.Add(WalkwayFeature(r, d.at, d.n, bestP, dir, true, m, rng));
            }
            return true;
        }

        /// <summary>The room's doorways: where its outline is open to another room, each a run of open cell sides as one, at
        /// its middle on the wall's line, with the way into the room.</summary>
        static List<(Vector2 at, Vector2 n)> Doorways(OfficeLayout L, Room r)
        {
            var rc = r.rect;
            var list = new List<(Vector2, Vector2)>();
            for (int side = 0; side < 4; side++)
            {
                int len = side < 2 ? rc.height : rc.width;
                int run = -1;
                for (int i = 0; i <= len; i++)
                {
                    bool open = false;
                    if (i < len)
                    {
                        int x = side == 0 ? rc.xMin : side == 1 ? rc.xMax - 1 : rc.xMin + i, z = side < 2 ? rc.yMin + i : side == 2 ? rc.yMin : rc.yMax - 1;
                        int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                        open = OfficeLayout.IsPassable(L.EdgeOf(x, z, side)) && L.InChunk(nx, nz) && L.roomOf[nx, nz] != r.id && L.RoomAt(nx, nz).Walkable;
                    }
                    if (open && run < 0) run = i;
                    if (!open && run >= 0)
                    {
                        float mid = (run + i) * 0.5f;
                        Vector2 at, n;
                        switch (side)
                        {
                            case 0: at = new Vector2(rc.xMin * C, (rc.yMin + mid) * C); n = Vector2.right; break;
                            case 1: at = new Vector2(rc.xMax * C, (rc.yMin + mid) * C); n = Vector2.left; break;
                            case 2: at = new Vector2((rc.xMin + mid) * C, rc.yMin * C); n = Vector2.up; break;
                            default: at = new Vector2((rc.xMin + mid) * C, rc.yMax * C); n = Vector2.down; break;
                        }
                        list.Add((at, n));
                        run = -1;
                    }
                }
            }
            return list;
        }

        static PoolFeature WalkwayFeature(Room r, Vector2 a, Vector2 na, Vector2 b, Vector2 nb, bool spur, Rect m, System.Random rng)
        {
            float len = (b - a).magnitude;
            var f = new PoolFeature
            {
                kind = PoolFeatureKind.Walkway, bounds = m, centre = a, size = na, to = b, toNormal = nb, shape = spur ? 1 : 0,
                height = spur ? 1f : Mathf.Max(1f, Mathf.Round(len / 4.5f)), room = r.globalId,
            };
            // Swinging from side to side as far as the room leaves room for (a walkway's width and a little water clear of
            // the walls), less near the ends.
            float amp = spur ? 0f : 0.6f + (float)rng.NextDouble() * 1.4f;
            var inner = new Rect(m.xMin + WalkwayHalf + 0.35f, m.yMin + WalkwayHalf + 0.35f, m.width - 2f * (WalkwayHalf + 0.35f), m.height - 2f * (WalkwayHalf + 0.35f));
            for (int tries = 0; tries < 8 && amp > 0.05f; tries++)
            {
                f.radius = amp;
                bool ok = true;
                for (int k = 2; k <= 38 && ok; k++)
                {
                    var p = WalkwayPoint(f, k / 40f, out _);
                    if ((p - a).magnitude < 1.2f || (p - b).magnitude < 1.2f) continue;
                    if (!inner.Contains(p)) ok = false;
                }
                if (ok) break;
                amp *= 0.7f;
            }
            f.radius = amp > 0.05f ? amp : 0f;
            return f;
        }

        /// <summary>A point along a walkway (t 0..1) and the way it runs there: a cubic from one doorway to the other,
        /// leaving and arriving square to the walls, swung from side to side (zero swing and zero turn of it at the ends).</summary>
        public static Vector2 WalkwayPoint(PoolFeature f, float t, out Vector2 dir)
        {
            Vector2 a = f.centre, b = f.to;
            float len = (b - a).magnitude;
            Vector2 p1 = a + f.size * (len * 0.35f), p2 = f.shape == 1 ? b + (a - b) * 0.25f : b + f.toNormal * (len * 0.35f);
            Vector2 Bez(float u) { float v = 1f - u; return v * v * v * a + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * b; }
            var c = Bez(t);
            var d = Bez(Mathf.Min(1f, t + 0.005f)) - Bez(Mathf.Max(0f, t - 0.005f));
            d = d.sqrMagnitude > 1e-10f ? d.normalized : (b - a).normalized;
            if (f.radius > 0f)
            {
                var perp = new Vector2(-d.y, d.x);
                float s = Mathf.Sin(Mathf.PI * f.height * t), w = Mathf.Sin(Mathf.PI * t);
                c += perp * (f.radius * s * w * w);
                var c2 = Bez(Mathf.Min(1f, t + 0.005f)); var d2 = Bez(Mathf.Min(1f, t + 0.01f)) - Bez(t);
                float t2 = Mathf.Min(1f, t + 0.005f);
                d2 = d2.sqrMagnitude > 1e-10f ? d2.normalized : d;
                var e2 = c2 + new Vector2(-d2.y, d2.x) * (f.radius * Mathf.Sin(Mathf.PI * f.height * t2) * Mathf.Sin(Mathf.PI * t2) * Mathf.Sin(Mathf.PI * t2));
                var dd = e2 - c;
                if (t < 0.995f && dd.sqrMagnitude > 1e-10f) d = dd.normalized;
            }
            dir = d;
            return c;
        }
    }
}
