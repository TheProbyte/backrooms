using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Destruction
{
    /// <summary>
    /// Broken walls as a cord (Interaction.Cord) sees them: a slab the wall's whole thickness, solid wherever either board,
    /// a stud, a track or a post is left, and open only where the wall is open right through. A cord is a chain of points
    /// 12 cm apart: against the boards' thin colliders a span straddled a board and was pushed back out of the face it went
    /// in by, again and again, until it slipped through and was loose in the wall. Against the slab a span that cuts
    /// through solid wall is sent through the nearest opening instead (<see cref="NearestOpen"/>), so the cord lies over
    /// the drywall and goes through the hole.
    /// </summary>
    public static class WallSlabs
    {
        static readonly List<BrokenPanel> s_near = new List<BrokenPanel>();

        /// <summary>The broken panels whose slab (grown by r) a segment's bounds touch.</summary>
        public static List<BrokenPanel> Near(Vector3 a, Vector3 b, float r)
        {
            s_near.Clear();
            if (BrokenPanel.Live.Count == 0) return s_near;
            var seg = new Bounds((a + b) * 0.5f, Vector3.zero);
            seg.Encapsulate(a); seg.Encapsulate(b);
            float pitch = WallDamage.Pitch;
            foreach (var kv in BrokenPanel.Live)
            {
                var bp = kv.Value;
                if (!bp || bp.Panel == null) continue;
                var k = bp.Key;
                Vector3 p0 = k.ToWorld(new Vector3(0f, 0f, -WallSpec.T * 0.5f - r), pitch);
                Vector3 p1 = k.ToWorld(new Vector3(WallSpec.C, bp.Panel.model.height, WallSpec.T * 0.5f + r), pitch);
                var box = new Bounds((p0 + p1) * 0.5f, Vector3.zero);
                box.Encapsulate(p0); box.Encapsulate(p1);
                box.Expand(r * 2f);
                if (box.Intersects(seg)) s_near.Add(bp);
            }
            return s_near;
        }

        /// <summary>Is the wall solid at (u along, v up) through its thickness? Posts, the wall above the breakable part, the
        /// tracks or plates, standing studs and any board piece left are.</summary>
        public static bool Solid(WallDamage.Panel p, float u, float v)
        {
            var m = p.model; var st = p.state;
            if (u < WallSpec.U0 || u > WallSpec.U1 || v >= m.height) return true;
            if (v < Mathf.Max(0.02f, WallSpec.StudBottom(m.steel)) || v > WallSpec.StudTop(m.steel, m.height)) return true;
            var uv = new Vector2(u, v);
            if (st.BoardAt(m, 0, uv) || st.BoardAt(m, 1, uv)) return true;
            int s = m.StudAt(u);
            return s >= 0 && st.StudAt(s, v);
        }

        /// <summary>Is a point (a cord's centre, radius r) inside the solid part of a broken wall?</summary>
        public static bool Blocked(BrokenPanel bp, Vector3 world, float r)
        {
            var l = bp.Key.ToLocal(world, WallDamage.Pitch);
            if (l.x < 0f || l.x > WallSpec.C || l.y < 0f || Mathf.Abs(l.z) > WallSpec.T * 0.5f + r) return false;
            return Solid(bp.Panel, l.x, l.y);
        }

        /// <summary>
        /// Which way is out, at a point of a wall just found solid, coming from prev (free): back out of the face it came in
        /// by, or (moving inside a hole) toward the open side of the edge it met.
        /// </summary>
        public static Vector3 Normal(BrokenPanel bp, Vector3 prev, Vector3 at, float r)
        {
            var k = bp.Key; float pitch = WallDamage.Pitch;
            var lp = k.ToLocal(prev, pitch); var la = k.ToLocal(at, pitch);
            if (Mathf.Abs(lp.z) >= WallSpec.T * 0.5f + r - 1e-4f) return k.Across * Mathf.Sign(lp.z);
            const float e = 0.012f;
            float S(float u, float v) => Solid(bp.Panel, u, v) ? 1f : 0f;
            var n = new Vector2(S(la.x - e, la.y) - S(la.x + e, la.y), S(la.x, la.y - e) - S(la.x, la.y + e));
            if (n.sqrMagnitude < 1e-6f) n = new Vector2(lp.x - la.x, lp.y - la.y);
            if (n.sqrMagnitude < 1e-8f) n = Vector2.up;
            return k.DirToWorld(new Vector3(n.x, n.y, 0f)).normalized;
        }

        // A coarse map of each wall's solid and open parts (2 cm), made again when the wall changes: the search for an
        // opening looks at many points.
        const float Cell = 0.02f;
        static readonly Dictionary<WallDamage.Panel, (int version, int cols, int rows, bool[] solid)> s_maps =
            new Dictionary<WallDamage.Panel, (int, int, int, bool[])>();

        static (int version, int cols, int rows, bool[] solid) Map(WallDamage.Panel p)
        {
            if (s_maps.TryGetValue(p, out var m) && m.version == p.state.version) return m;
            if (s_maps.Count > 64) s_maps.Clear();
            int cols = Mathf.CeilToInt(WallSpec.C / Cell), rows = Mathf.CeilToInt(p.model.height / Cell);
            var solid = new bool[cols * rows];
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                solid[y * cols + x] = Solid(p, (x + 0.5f) * Cell, (y + 0.5f) * Cell);
            m = (p.state.version, cols, rows, solid);
            s_maps[p] = m;
            return m;
        }

        /// <summary>
        /// The nearest point of a wall to (u, v) that is open with room for a cord round it (one 2 cm cell all round),
        /// within reach; of equally near ones the lowest (a cord hangs on the lower edge of a hole). False if the wall is
        /// whole there.
        /// </summary>
        public static bool NearestOpen(WallDamage.Panel p, float u, float v, float reach, out Vector2 open)
        {
            var m = Map(p);
            bool Free(int x, int y)
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= m.cols || yy >= m.rows || m.solid[yy * m.cols + xx]) return false;
                }
                return true;
            }
            int cx = Mathf.FloorToInt(u / Cell), cy = Mathf.FloorToInt(v / Cell), R = Mathf.CeilToInt(reach / Cell);
            open = new Vector2(u, v);
            float best = float.MaxValue; bool found = false;
            for (int ring = 0; ring <= R && !found; ring++)
                for (int y = cy - ring; y <= cy + ring; y++)
                for (int x = cx - ring; x <= cx + ring; x++)
                {
                    if (Mathf.Max(Mathf.Abs(x - cx), Mathf.Abs(y - cy)) != ring || !Free(x, y)) continue;
                    float d = (x - cx) * (x - cx) + (y - cy) * (y - cy) + y * 0.01f;
                    if (d < best) { best = d; open = new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell); found = true; }
                }
            return found;
        }
    }
}
