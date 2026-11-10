using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The company's floor tape (2026-11-08, the owner: "how you supposed to find what the first contract asks for?"): a
    /// line of the first client's tape colour on the floor from the lift's door to the first seam (LeaseSeams.FirstSeamDistrict),
    /// with a chevron every few metres pointing the way, as warehouses and hospitals tape their floors. It is the one seam
    /// the company shows you; the rest are found by exploring (and later marked with your own tape and chalk).
    /// <para>The route goes from cell to cell through doorways (never through a wall or a seam), on level floor, keeping
    /// straight where it can and off the walls in open rooms. Each piece is lit by its own room's lights (its light layer),
    /// so the tape never glows through a wall. Built from the floor's plan, so it is the same every time it loads.</para>
    /// </summary>
    public static class GuideTape
    {
        const float C = OfficeBiome.CellSize;
        const int N = OfficeBiome.ChunkCells;
        const float Width = 0.09f, Lift = 0.006f, ChevronEvery = 4.8f;

        /// <summary>Lays the tape from <paramref name="from"/> to <paramref name="to"/> (world m, both on the floor) and
        /// returns its object, or null if there is no way through the leased floor.</summary>
        public static GameObject Build(WorldGenerator gen, int slot, Vector3 from, Vector3 to, Color colour)
        {
            var path = Route(gen, slot, Cell(from), Cell(to));
            if (path == null || path.Count < 2) return null;
            return Draw(gen, path, from, to, colour);
        }

        static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / C), Mathf.FloorToInt(p.z / C));
        static Vector3 Centre(Vector2Int g) => new Vector3((g.x + 0.5f) * C, 0f, (g.y + 0.5f) * C);
        static Vector2Int ChunkOf(Vector2Int g) => new Vector2Int(Mathf.FloorToInt((float)g.x / N), Mathf.FloorToInt((float)g.y / N));

        static bool Level(Room r) => r != null && r.Walkable && !r.noFloor && r.ramp == 0 && Mathf.Abs(r.floor) < 0.01f;

        // ---------------- The route ----------------

        /// <summary>The cells from a to b: Dijkstra over (cell, heading), a step costing 10, a turn 12 more, and a cell beside
        /// a wall 3 more (so it runs down the middle of open rooms, and straight). Searched in the box around a and b first,
        /// then the whole leased floor.</summary>
        static List<Vector2Int> Route(WorldGenerator gen, int slot, Vector2Int a, Vector2Int b)
        {
            var leased = new HashSet<Vector2Int>(LeasePlan.Chunks(slot));
            var all = new RectInt(a.x, a.y, 1, 1);
            foreach (var ch in leased) { all.xMin = Mathf.Min(all.xMin, ch.x * N); all.yMin = Mathf.Min(all.yMin, ch.y * N); all.xMax = Mathf.Max(all.xMax, ch.x * N + N); all.yMax = Mathf.Max(all.yMax, ch.y * N + N); }
            const int margin = 30;
            var box = new RectInt(Mathf.Min(a.x, b.x) - margin, Mathf.Min(a.y, b.y) - margin, Mathf.Abs(a.x - b.x) + 2 * margin + 1, Mathf.Abs(a.y - b.y) + 2 * margin + 1);
            box.xMin = Mathf.Max(box.xMin, all.xMin); box.yMin = Mathf.Max(box.yMin, all.yMin);
            box.xMax = Mathf.Min(box.xMax, all.xMax); box.yMax = Mathf.Min(box.yMax, all.yMax);
            return Search(gen, leased, box, a, b) ?? Search(gen, leased, all, a, b);
        }

        static List<Vector2Int> Search(WorldGenerator gen, HashSet<Vector2Int> leased, RectInt box, Vector2Int a, Vector2Int b)
        {
            if (!box.Contains(a) || !box.Contains(b)) return null;
            int w = box.width, h = box.height, n = w * h * 4;
            var cost = new int[n];
            var prev = new int[n];
            for (int i = 0; i < n; i++) { cost[i] = int.MaxValue; prev[i] = -1; }
            // Per cell: 0 unknown, 1 open, 2 blocked; and how many walls it has.
            var state = new byte[w * h];
            var walls = new byte[w * h];
            bool Open(Vector2Int g)
            {
                int k = (g.x - box.xMin) + (g.y - box.yMin) * w;
                if (state[k] == 0)
                {
                    bool ok = leased.Contains(ChunkOf(g)) && Level(WorldChecks.RoomAt(gen, g));
                    state[k] = ok ? (byte)1 : (byte)2;
                    if (ok) { int c = 0; for (int s = 0; s < 4; s++) if (!OfficeLayout.IsPassable(WorldChecks.EdgeBetween(gen, g, s))) c++; walls[k] = (byte)c; }
                }
                return state[k] == 1;
            }
            if (!Open(a) || !Open(b)) return null;
            var heap = new MinHeap();
            int Idx(Vector2Int g, int dir) => ((g.x - box.xMin) + (g.y - box.yMin) * w) * 4 + dir;
            for (int d = 0; d < 4; d++) { cost[Idx(a, d)] = 0; heap.Push(0, Idx(a, d)); }
            int goal = -1;
            while (heap.Count > 0)
            {
                heap.Pop(out int cst, out int s0);
                if (cst > cost[s0]) continue;
                int cell = s0 >> 2, dir = s0 & 3;
                var g = new Vector2Int(box.xMin + cell % w, box.yMin + cell / w);
                if (g == b) { goal = s0; break; }
                for (int side = 0; side < 4; side++)
                {
                    var o = g + WorldChecks.Step(side);
                    if (!box.Contains(o) || !Open(o)) continue;
                    if (!OfficeLayout.IsPassable(WorldChecks.EdgeBetween(gen, g, side))) continue;
                    int k = (o.x - box.xMin) + (o.y - box.yMin) * w;
                    int nc = cst + 10 + (side != dir && g != a ? 12 : 0) + (walls[k] > 0 ? 3 : 0);
                    int s1 = k * 4 + side;
                    if (nc >= cost[s1]) continue;
                    cost[s1] = nc; prev[s1] = s0;
                    heap.Push(nc, s1);
                }
            }
            if (goal < 0) return null;
            var path = new List<Vector2Int>();
            for (int s = goal; s >= 0; s = prev[s])
            {
                int cell = s >> 2;
                var g = new Vector2Int(box.xMin + cell % w, box.yMin + cell / w);
                if (path.Count == 0 || path[path.Count - 1] != g) path.Add(g);
            }
            path.Reverse();
            return path;
        }

        /// <summary>A binary min-heap of (cost, state).</summary>
        class MinHeap
        {
            readonly List<long> _h = new List<long>();
            public int Count => _h.Count;
            public void Push(int cost, int item)
            {
                _h.Add(((long)cost << 32) | (uint)item);
                int i = _h.Count - 1;
                while (i > 0) { int p = (i - 1) >> 1; if (_h[p] <= _h[i]) break; (_h[p], _h[i]) = (_h[i], _h[p]); i = p; }
            }
            public void Pop(out int cost, out int item)
            {
                long top = _h[0];
                cost = (int)(top >> 32); item = (int)(uint)top;
                _h[0] = _h[_h.Count - 1]; _h.RemoveAt(_h.Count - 1);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _h.Count && _h[l] < _h[m]) m = l;
                    if (r < _h.Count && _h[r] < _h[m]) m = r;
                    if (m == i) break;
                    (_h[m], _h[i]) = (_h[i], _h[m]); i = m;
                }
            }
        }

        // ---------------- The tape ----------------

        sealed class Piece { public readonly List<Vector3> v = new List<Vector3>(); public readonly List<int> t = new List<int>(); }

        static GameObject Draw(WorldGenerator gen, List<Vector2Int> path, Vector3 from, Vector3 to, Color colour)
        {
            // The line's points: the lift's door, each cell's middle, the seam's front.
            var pts = new List<Vector3> { new Vector3(from.x, 0f, from.z) };
            for (int i = 0; i < path.Count; i++) pts.Add(Centre(path[i]));
            pts.Add(new Vector3(to.x, 0f, to.z));
            // Each half of a step belongs to the cell it is in, lit by that cell's room's lights.
            var pieces = new Dictionary<uint, Piece>();
            Piece In(Vector3 p)
            {
                var room = WorldChecks.RoomAt(gen, Cell(p));
                uint mask = LightLayers.LayerBit(room != null ? room.color : -1);
                if (!pieces.TryGetValue(mask, out var pc)) pieces[mask] = pc = new Piece();
                return pc;
            }
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector3 p = pts[i], q = pts[i + 1], m = (p + q) * 0.5f;
                if ((q - p).sqrMagnitude < 1e-4f) continue;
                Strip(In(p), p, m, true, false);
                Strip(In(q), m, q, false, true);
            }
            // Chevrons along it, pointing the way.
            float run = ChevronEvery * 0.5f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector3 p = pts[i], q = pts[i + 1];
                float len = Vector3.Distance(p, q);
                if (len < 1e-3f) continue;
                var dir = (q - p) / len;
                while (run < len)
                {
                    Chevron(In(p + dir * run), p + dir * run, dir);
                    run += ChevronEvery;
                }
                run -= len;
            }
            // A bar across its end, at the seam.
            var last = (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized;
            var across = new Vector3(last.z, 0f, -last.x);
            var end = pts[pts.Count - 1];
            Strip(In(end), end - across * 0.35f, end + across * 0.35f, true, true);

            var root = new GameObject("GuideTape");
            var mat = Material(colour);
            foreach (var kv in pieces)
            {
                var mesh = new Mesh { name = "GuideTape" };
                mesh.indexFormat = kv.Value.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(kv.Value.v);
                mesh.SetTriangles(kv.Value.t, 0);
                var nrm = new Vector3[kv.Value.v.Count];
                for (int i = 0; i < nrm.Length; i++) nrm[i] = Vector3.up;
                mesh.normals = nrm;
                mesh.RecalculateBounds();
                var go = new GameObject("Tape");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
                r.renderingLayerMask = kv.Key;
            }
            return root;
        }

        /// <summary>A flat strip of tape from p to q, half its width longer at the ends asked for (so corners close).</summary>
        static void Strip(Piece pc, Vector3 p, Vector3 q, bool padStart, bool padEnd)
        {
            var d = q - p; d.y = 0f;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            d /= len;
            var side = new Vector3(d.z, 0f, -d.x) * (Width * 0.5f);
            if (padStart) p -= d * (Width * 0.5f);
            if (padEnd) q += d * (Width * 0.5f);
            p.y = q.y = Lift;
            int i = pc.v.Count;
            pc.v.Add(p - side); pc.v.Add(p + side); pc.v.Add(q + side); pc.v.Add(q - side);
            pc.t.Add(i); pc.t.Add(i + 2); pc.t.Add(i + 1);
            pc.t.Add(i); pc.t.Add(i + 3); pc.t.Add(i + 2);
        }

        /// <summary>A chevron (two short strips) across the line at p, its point toward dir.</summary>
        static void Chevron(Piece pc, Vector3 p, Vector3 dir)
        {
            var across = new Vector3(dir.z, 0f, -dir.x);
            var tip = p + dir * 0.22f;
            foreach (float s in new[] { 1f, -1f })
            {
                var tail = p - dir * 0.12f + across * (0.3f * s);
                Strip(pc, tail, tip, true, true);
            }
        }

        static Material Material(Color colour)
        {
            var sh = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh) { name = "GuideTape", hideFlags = HideFlags.DontSave };
            m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
            return m;
        }
    }
}
