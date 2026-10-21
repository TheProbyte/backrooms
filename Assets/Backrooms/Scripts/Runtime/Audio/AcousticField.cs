using UnityEngine;
using Backrooms.Generation;

namespace Backrooms.Audio
{
    /// <summary>
    /// A bounded acoustic model of the building around the listener, built from the generator's own wall
    /// grid (not from colliders). It answers two questions:
    ///
    ///  1. How does a source reach the listener? A straight line is traced through the cell grid, counting
    ///     solid walls it crosses. A shortest walking path (8-way Dijkstra through open edges and doorways)
    ///     gives the length sound travels around walls and the "visible point": the last point on that path
    ///     the listener can see, usually a doorway. Sound that bends around a corner is rendered from the
    ///     direction of that doorway at the path's distance; sound through a wall is quiet and muffled.
    ///  2. What does the listener's space sound like? The cells the listener can see give an open area,
    ///     average ceiling height and how much of it has hard plaster ceilings (versus absorbent drop
    ///     ceiling tiles), which drive the reverb.
    ///
    /// Limits: sound only travels through cells inside the window (<see cref="Radius"/> cells around the
    /// listener) and through loaded chunks; furniture, pillars and half walls are ignored; there is one
    /// path per source (the shortest), not a sum over several doorways.
    ///
    /// Portals later: a portal is just another edge in this graph whose far side is somewhere else. The
    /// path search crosses it and the visible point becomes the portal mouth, so remote audio will appear
    /// at the entrance without changing emitters.
    /// </summary>
    public class AcousticField
    {
        public const int Radius = 18;                       // cells (21.6 m)
        const int W = Radius * 2 + 1;
        const float C = OfficeBiome.CellSize;

        readonly bool[] _valid = new bool[W * W];
        readonly bool[] _passE = new bool[W * W];           // edge between (x,z) and (x+1,z)
        readonly bool[] _passN = new bool[W * W];           // edge between (x,z) and (x,z+1)
        readonly float[] _ceiling = new float[W * W];
        readonly float[] _dist = new float[W * W];
        readonly int[] _parent = new int[W * W];
        readonly int[] _visiblePoint = new int[W * W];      // cached per rebuild (-2 = not computed)

        // Dijkstra heap (allocated once).
        readonly float[] _hk = new float[W * W * 8];
        readonly int[] _hv = new int[W * W * 8];
        int _hn;

        Vector2Int _origin;                                 // world cell at window index (0,0)
        Vector3 _listener;
        public bool Built { get; private set; }

        // Listener space, for reverb.
        public float VisibleArea { get; private set; }       // m^2 of floor the listener can see
        public float VisibleCeiling { get; private set; }    // average ceiling height over it
        public float HardCeilingFraction { get; private set; }
        public int VisibleCells { get; private set; }
        public float RebuildMs { get; private set; }

        public struct Path
        {
            public bool inField, reachable;
            public float direct;          // straight-line distance, m
            public float path;            // walking distance around walls, m
            public int walls;             // solid walls on the straight line
            public Vector3 visiblePoint;  // where the path becomes visible to the listener
        }

        // ---------------- Build ----------------

        public void Rebuild(Vector3 listener, WorldStreamer ws)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _listener = listener;
            var lc = new Vector2Int(Mathf.FloorToInt(listener.x / C), Mathf.FloorToInt(listener.z / C));
            _origin = lc - new Vector2Int(Radius, Radius);
            int n = OfficeBiome.ChunkCells;

            OfficeLayout cached = null; Vector2Int cachedChunk = new Vector2Int(int.MinValue, 0);
            for (int z = 0; z < W; z++)
            for (int x = 0; x < W; x++)
            {
                int i = z * W + x;
                int gx = _origin.x + x, gz = _origin.y + z;
                var ch = new Vector2Int(FloorDiv(gx, n), FloorDiv(gz, n));
                if (ch != cachedChunk) { cached = ws ? ws.LayoutAt(ch) : null; cachedChunk = ch; }
                var L = cached;
                _valid[i] = L != null;
                _passE[i] = _passN[i] = false;
                _dist[i] = float.MaxValue; _parent[i] = -1; _visiblePoint[i] = -2;
                if (L == null) continue;
                int lx = gx - ch.x * n, lz = gz - ch.y * n;
                _ceiling[i] = L.CeilingAt(lx, lz);
                _passE[i] = OfficeLayout.IsPassable(L.vEdges[lx + 1, lz]);
                _passN[i] = OfficeLayout.IsPassable(L.hEdges[lx, lz + 1]);
            }
            // An edge is only open if the cell beyond it is loaded too.
            for (int z = 0; z < W; z++)
            for (int x = 0; x < W; x++)
            {
                int i = z * W + x;
                if (x == W - 1 || !_valid[i + 1]) _passE[i] = false;
                if (z == W - 1 || !_valid[i + W]) _passN[i] = false;
            }

            Dijkstra(Radius * W + Radius, listener);
            ComputeSpace();
            Built = true;
            RebuildMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        bool Open(int x, int z, int dx, int dz)
        {
            // Passability of the step from (x,z) to (x+dx, z+dz) for orthogonal steps.
            if (dx == 1) return _passE[z * W + x];
            if (dx == -1) return x > 0 && _passE[z * W + x - 1];
            if (dz == 1) return _passN[z * W + x];
            return z > 0 && _passN[(z - 1) * W + x];
        }

        void Dijkstra(int start, Vector3 listener)
        {
            if (!_valid[start]) return;
            // Start distance accounts for where the listener stands inside its cell.
            _dist[start] = 0f;
            _hn = 0; Push(0f, start);
            while (_hn > 0)
            {
                Pop(out float d, out int i);
                if (d > _dist[i]) continue;
                int x = i % W, z = i / W;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= W || nz >= W) continue;
                    bool ok;
                    float cost;
                    if (dx == 0 || dz == 0) { ok = Open(x, z, dx, dz); cost = C; }
                    else
                    {
                        // Diagonal: both L-shaped routes must be open (no squeezing past a corner).
                        ok = Open(x, z, dx, 0) && Open(x + dx, z, 0, dz) && Open(x, z, 0, dz) && Open(x, z + dz, dx, 0);
                        cost = C * 1.41421356f;
                    }
                    if (!ok) continue;
                    int j = nz * W + nx;
                    float nd = d + cost;
                    if (nd < _dist[j]) { _dist[j] = nd; _parent[j] = i; Push(nd, j); }
                }
            }
        }

        void Push(float k, int v)
        {
            if (_hn >= _hk.Length) return;
            int c = _hn++;
            while (c > 0)
            {
                int p = (c - 1) >> 1;
                if (_hk[p] <= k) break;
                _hk[c] = _hk[p]; _hv[c] = _hv[p]; c = p;
            }
            _hk[c] = k; _hv[c] = v;
        }

        void Pop(out float k, out int v)
        {
            k = _hk[0]; v = _hv[0];
            float lk = _hk[--_hn]; int lv = _hv[_hn];
            int c = 0;
            while (true)
            {
                int a = c * 2 + 1;
                if (a >= _hn) break;
                if (a + 1 < _hn && _hk[a + 1] < _hk[a]) a++;
                if (_hk[a] >= lk) break;
                _hk[c] = _hk[a]; _hv[c] = _hv[a]; c = a;
            }
            _hk[c] = lk; _hv[c] = lv;
        }

        // ---------------- Line of sight through the wall grid ----------------

        /// <summary>Solid walls crossed by the straight line between two points (window cell units).</summary>
        int WallsBetween(Vector2 a, Vector2 b, int cap = 4)
        {
            int x = Mathf.FloorToInt(a.x), z = Mathf.FloorToInt(a.y);
            int ex = Mathf.FloorToInt(b.x), ez = Mathf.FloorToInt(b.y);
            float dx = b.x - a.x, dz = b.y - a.y;
            int sx = dx > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            float tdx = Mathf.Abs(dx) > 1e-6f ? Mathf.Abs(1f / dx) : float.MaxValue;
            float tdz = Mathf.Abs(dz) > 1e-6f ? Mathf.Abs(1f / dz) : float.MaxValue;
            float tmx = Mathf.Abs(dx) > 1e-6f ? ((sx > 0 ? x + 1 : x) - a.x) / dx : float.MaxValue;
            float tmz = Mathf.Abs(dz) > 1e-6f ? ((sz > 0 ? z + 1 : z) - a.y) / dz : float.MaxValue;
            int walls = 0, guard = 0;
            while ((x != ex || z != ez) && guard++ < W * 3)
            {
                if (tmx < tmz)
                {
                    int nx = x + sx;
                    if (nx < 0 || nx >= W) return cap;
                    if (!Open(x, z, sx, 0)) { if (++walls >= cap) return cap; }
                    x = nx; tmx += tdx;
                }
                else
                {
                    int nz = z + sz;
                    if (nz < 0 || nz >= W) return cap;
                    if (!Open(x, z, 0, sz)) { if (++walls >= cap) return cap; }
                    z = nz; tmz += tdz;
                }
            }
            return walls;
        }

        Vector2 ToCell(Vector3 p) => new Vector2(p.x / C - _origin.x, p.z / C - _origin.y);
        Vector3 CellCenter(int i, float y) => new Vector3((_origin.x + i % W + 0.5f) * C, y, (_origin.y + i / W + 0.5f) * C);

        void ComputeSpace()
        {
            Vector2 l = ToCell(_listener);
            int count = 0, hard = 0; float ceil = 0f;
            for (int i = 0; i < W * W; i++)
            {
                if (!_valid[i] || _dist[i] == float.MaxValue) continue;
                var c = new Vector2(i % W + 0.5f, i / W + 0.5f);
                if ((c - l).sqrMagnitude > Radius * Radius) continue;
                if (WallsBetween(l, c, 1) > 0) continue;
                count++; ceil += _ceiling[i];
                if (_ceiling[i] > OfficeBiome.CeilingHeight + 0.01f) hard++;
            }
            VisibleCells = count;
            VisibleArea = count * C * C;
            VisibleCeiling = count > 0 ? ceil / count : OfficeBiome.CeilingHeight;
            HardCeilingFraction = count > 0 ? hard / (float)count : 0f;
        }

        // ---------------- Queries ----------------

        public Path Query(Vector3 source)
        {
            var r = new Path { direct = Vector3.Distance(source, _listener), visiblePoint = source };
            if (!Built) { r.path = r.direct; r.reachable = true; return r; }
            Vector2 s = ToCell(source), l = ToCell(_listener);
            int sx = Mathf.FloorToInt(s.x), sz = Mathf.FloorToInt(s.y);
            if (sx < 0 || sz < 0 || sx >= W || sz >= W) { r.path = r.direct * 2f; r.walls = 4; return r; }
            r.inField = true;
            int i = sz * W + sx;
            r.walls = WallsBetween(l, s);
            float dy = source.y - _listener.y;
            if (_dist[i] == float.MaxValue) { r.path = r.direct * 2f; return r; }
            r.reachable = true;
            // Walking distance from cell centres, plus the leftover offsets, never shorter than straight.
            float horiz = _dist[i] + (s - new Vector2(sx + 0.5f, sz + 0.5f)).magnitude * C * 0.5f;
            r.path = Mathf.Max(r.direct, Mathf.Sqrt(horiz * horiz + dy * dy));
            if (r.walls == 0) return r;

            if (_visiblePoint[i] == -2)
            {
                // Walk back toward the listener until a cell on the path is in plain view.
                int v = i, found = -1, guard = 0;
                while (v >= 0 && guard++ < W * W)
                {
                    var c = new Vector2(v % W + 0.5f, v / W + 0.5f);
                    if (WallsBetween(l, c, 1) == 0) { found = v; break; }
                    v = _parent[v];
                }
                _visiblePoint[i] = found;
            }
            if (_visiblePoint[i] >= 0) r.visiblePoint = CellCenter(_visiblePoint[i], source.y);
            return r;
        }
    }
}
