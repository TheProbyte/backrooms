using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Bakes a lightmap for one chunk's architecture on a worker thread, so the building is lit the same
    /// way at any distance, light never passes through walls, and light flows naturally through doorways
    /// and openings (no lines between rooms, no pop-in).
    ///
    /// Model, per lightmap texel (world position + normal):
    ///  - direct light from every powered fixture within range (inverse square, Lambert, downward
    ///    emission for ceiling fittings, omni for floor lamps), with visibility traced through the
    ///    generator's wall grid in 2.5D: walls block, doors and openings pass below their top, soffits
    ///    block above it; pillars, half walls, racks and furniture block as boxes;
    ///  - a soft, wide fill term with the same visibility, standing in for light bouncing around a room;
    ///  - geometric ambient occlusion at wall/floor/ceiling junctions and under furniture (contact shadows).
    /// Fixtures from the eight neighbouring chunks are included, so chunk borders are seamless.
    /// Faulty tubes are left out: they flicker, so they stay real-time lights.
    /// </summary>
    public class ChunkLightBaker
    {
        public const float TexelsPerMetre = 4f;
        public const int AtlasWidth = 512;
        const float C = OfficeBiome.CellSize;

        public struct Emitter
        {
            public Vector3 pos;
            public Vector3 color;      // linear colour * intensity
            public float range;
            public bool omni;          // floor lamp
            public int shape;          // 0 troffer panel, 1 dome, 2 strip, 3 lamp
            public int region;         // open-plan region the fitting hangs in
        }

        // Points across each emitter's surface; the visible fraction gives soft shadow edges (penumbra).
        static readonly Vector3[][] Samples =
        {
            new[] { new Vector3(-0.4f, 0, -0.17f), new Vector3(0.4f, 0, -0.17f), new Vector3(-0.4f, 0, 0.17f), new Vector3(0.4f, 0, 0.17f) },
            new[] { new Vector3(-0.15f, 0, 0), new Vector3(0.15f, 0, 0), new Vector3(0, 0, 0.15f), new Vector3(0, 0, -0.15f) },
            new[] { new Vector3(-0.45f, 0, 0), new Vector3(-0.15f, 0, 0), new Vector3(0.15f, 0, 0), new Vector3(0.45f, 0, 0) },
            new[] { new Vector3(0, 0, 0) },
        };

        // ---- Grid around the chunk (3 x 3 chunks) ----
        readonly int _n, _w;
        readonly Vector2Int _origin;            // world cell of grid index (0,0)
        readonly bool[] _valid;
        readonly EdgeKind[] _e, _nEdge;         // edge to +X and to +Z of each cell
        readonly float[] _ceil;
        readonly int[] _room;                    // unique room id per cell (rooms are convex rectangles)
        readonly Dictionary<int, List<int>> _roomOcc = new Dictionary<int, List<int>>();
        int[] _region;                           // rooms joined by open-plan edges share a region
        readonly HashSet<long> _doorLinked = new HashSet<long>();   // region pairs joined by a doorway
        int _layoutCount;
        readonly List<Bounds> _occ = new List<Bounds>();
        readonly List<int>[] _occCells;
        int[] _occStamp; int _stamp;
        readonly List<Emitter> _emit = new List<Emitter>();
        List<int>[] _emitCells;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public ushort[] Pixels { get; private set; }   // RGBAHalf
        public byte[] DirPixels { get; private set; }  // RGBA32 directional lightmap
        public float BakeMs { get; private set; }
        // Profiling counters (last bake).
        public int StatEvals, StatVisible, StatFast, StatSteps, StatNear;

        public ChunkLightBaker(OfficeLayout center, OfficeLayout[] neighbours)
        {
            _n = center.N;
            _w = _n * 3;
            _origin = new Vector2Int((center.coord.x - 1) * _n, (center.coord.y - 1) * _n);
            int cells = _w * _w;
            _valid = new bool[cells]; _e = new EdgeKind[cells]; _nEdge = new EdgeKind[cells]; _ceil = new float[cells]; _room = new int[cells];
            for (int i = 0; i < cells; i++) _room[i] = -1;
            _occCells = new List<int>[cells];
            var all = new List<OfficeLayout> { center };
            if (neighbours != null) foreach (var l in neighbours) if (l != null) all.Add(l);
            foreach (var L in all) AddLayout(L);
            BuildRegions();
            // Edges into missing cells are solid.
            for (int z = 0; z < _w; z++)
            for (int x = 0; x < _w; x++)
            {
                int i = z * _w + x;
                if (!_valid[i]) continue;
                if (x == _w - 1 || !_valid[i + 1]) _e[i] = EdgeKind.Wall;
                if (z == _w - 1 || !_valid[i + _w]) _nEdge[i] = EdgeKind.Wall;
            }
        }

        /// <summary>
        /// Regions: rooms joined by open-plan edges (light flows freely between them). Region pairs that
        /// share a doorway are linked; light from a fitting can only reach texels in its own region or a
        /// linked one, which skips the vast majority of visibility traces (two doorways in a straight line
        /// are the only light path this ignores, and that light is faint).
        /// </summary>
        void BuildRegions()
        {
            int cells = _w * _w;
            var parent = new int[cells];
            for (int i = 0; i < cells; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            for (int z = 0; z < _w; z++)
            for (int x = 0; x < _w; x++)
            {
                int i = z * _w + x;
                if (!_valid[i]) continue;
                if (x + 1 < _w && _valid[i + 1] && (_room[i] == _room[i + 1] || _e[i] == EdgeKind.Open)) Union(i, i + 1);
                if (z + 1 < _w && _valid[i + _w] && (_room[i] == _room[i + _w] || _nEdge[i] == EdgeKind.Open)) Union(i, i + _w);
            }
            _region = new int[cells];
            for (int i = 0; i < cells; i++) _region[i] = _valid[i] ? Find(i) : -1;
            for (int z = 0; z < _w; z++)
            for (int x = 0; x < _w; x++)
            {
                int i = z * _w + x;
                if (!_valid[i]) continue;
                if (x + 1 < _w && _valid[i + 1] && (_e[i] == EdgeKind.Door || _e[i] == EdgeKind.DoorHeader)) Link(_region[i], _region[i + 1]);
                if (z + 1 < _w && _valid[i + _w] && (_nEdge[i] == EdgeKind.Door || _nEdge[i] == EdgeKind.DoorHeader)) Link(_region[i], _region[i + _w]);
            }
            for (int k = 0; k < _emit.Count; k++)
            {
                var em = _emit[k];
                em.region = RegionAt(em.pos);
                _emit[k] = em;
            }
        }

        void Link(int a, int b)
        {
            if (a == b) return;
            _doorLinked.Add(((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b));
        }

        bool Reaches(int a, int b) => a == b || (a >= 0 && b >= 0 && _doorLinked.Contains(((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b)));

        int RegionAt(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / C) - _origin.x, z = Mathf.FloorToInt(p.z / C) - _origin.y;
            if (x < 0 || z < 0 || x >= _w || z >= _w) return -1;
            return _region[z * _w + x];
        }

        void AddLayout(OfficeLayout L)
        {
            int ox = L.coord.x * _n - _origin.x, oz = L.coord.y * _n - _origin.y;
            if (ox < 0 || oz < 0 || ox >= _w || oz >= _w) return;
            int roomBase = (_layoutCount++) * 256;
            for (int x = 0; x < _n; x++)
            for (int z = 0; z < _n; z++)
            {
                int i = (oz + z) * _w + ox + x;
                _valid[i] = true;
                _ceil[i] = L.CeilingAt(x, z);
                _e[i] = L.vEdges[x + 1, z];
                _nEdge[i] = L.hEdges[x, z + 1];
                _room[i] = roomBase + L.roomOf[x, z];
                // Crawl spaces are filled solid apart from the slot: treat them as blocked.
                if (L.RoomAt(x, z).kind == RoomKind.Crawl) _ceil[i] = 0.6f;
            }
            Vector3 org = new Vector3(L.coord.x * _n * C, 0f, L.coord.y * _n * C);
            var tube = new Vector3(1f, 0.965f, 0.86f);
            var strip = new Vector3(0.9f, 0.95f, 1f);
            foreach (var f in L.fixtures)
            {
                if (!f.on || f.faulty) continue;
                float h = f.height > 0f ? f.height : OfficeBiome.CeilingHeight;
                float extra = (f.dome || f.strip) ? Mathf.Max(0f, (f.dome ? h : L.CeilingAt(f.cell.x, f.cell.y)) - OfficeBiome.CeilingHeight) : 0f;
                float intensity = 7f;
                if (f.dome) intensity *= (1f + extra * 0.3f) * 1.6f;
                else if (f.strip) intensity *= 1f + extra * 0.3f;
                float y = f.dome ? h - 0.12f : f.strip ? h - 0.04f : h - 0.03f;
                _emit.Add(new Emitter
                {
                    pos = org + new Vector3((f.cell.x + 0.5f) * C, y, (f.cell.y + 0.5f) * C),
                    color = (f.strip ? strip : tube) * intensity,
                    range = (f.dome || f.strip) ? 9f + extra * 1.6f : 8f,
                    shape = f.dome ? 1 : f.strip ? 2 : 0,
                });
            }
            foreach (var p in L.props)
            {
                if (p.kind == PropKind.FloorLamp && p.lit)
                    _emit.Add(new Emitter { pos = org + new Vector3(p.position.x, 1.45f, p.position.y), color = new Vector3(1f, 0.74f, 0.47f) * 1.8f, range = 6f, omni = true, shape = 3 });
            }
        }

        public void AddOccluder(Bounds worldBounds)
        {
            int id = _occ.Count;
            _occ.Add(worldBounds);
            int x0 = Mathf.FloorToInt(worldBounds.min.x / C) - _origin.x, x1 = Mathf.FloorToInt(worldBounds.max.x / C) - _origin.x;
            int z0 = Mathf.FloorToInt(worldBounds.min.z / C) - _origin.y, z1 = Mathf.FloorToInt(worldBounds.max.z / C) - _origin.y;
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(_w - 1, z1); z++)
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(_w - 1, x1); x++)
            {
                int i = z * _w + x;
                (_occCells[i] ??= new List<int>()).Add(id);
                int r = _room[i];
                if (r >= 0)
                {
                    if (!_roomOcc.TryGetValue(r, out var rl)) _roomOcc[r] = rl = new List<int>();
                    if (rl.Count == 0 || rl[rl.Count - 1] != id) rl.Add(id);
                }
            }
        }

        // ---------------- Visibility ----------------

        bool Passable(EdgeKind e, float y, float ceilA, float ceilB)
        {
            switch (e)
            {
                case EdgeKind.Wall: return false;
                case EdgeKind.Door:
                case EdgeKind.DoorHeader: return y < OfficeBiome.DoorHeaderHeight;
                default: return y < Mathf.Min(ceilA, ceilB) - 0.01f;
            }
        }

        /// <summary>True if the straight segment from a to b is not blocked by walls, soffits or occluders.</summary>
        bool Visible(Vector3 a, Vector3 b)
        {
            StatVisible++;
            float ax = a.x / C - _origin.x, az = a.z / C - _origin.y, bx = b.x / C - _origin.x, bz = b.z / C - _origin.y;
            int x = Mathf.FloorToInt(ax), z = Mathf.FloorToInt(az);
            int ex = Mathf.FloorToInt(bx), ez = Mathf.FloorToInt(bz);
            if (x < 0 || z < 0 || x >= _w || z >= _w || ex < 0 || ez < 0 || ex >= _w || ez >= _w) return false;
            // Same room: rooms are convex, so only free-standing blockers in it can get in the way.
            int ra = _room[z * _w + x];
            if (ra >= 0 && ra == _room[ez * _w + ex])
            {
                StatFast++;
                if (!_roomOcc.TryGetValue(ra, out var occ)) return true;
                Vector3 dd = b - a;
                float len = dd.magnitude;
                if (len < 1e-4f) return true;
                var ray = new Ray(a, dd / len);
                foreach (int id in occ)
                {
                    var bb = _occ[id];
                    if (bb.Contains(a) || bb.Contains(b)) continue;
                    if (bb.IntersectRay(ray, out float t) && t < len - 0.02f) return false;
                }
                return true;
            }
            float dx = bx - ax, dz = bz - az;
            int sx = dx > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            float tdx = Mathf.Abs(dx) > 1e-6f ? Mathf.Abs(1f / dx) : float.MaxValue;
            float tdz = Mathf.Abs(dz) > 1e-6f ? Mathf.Abs(1f / dz) : float.MaxValue;
            float tmx = Mathf.Abs(dx) > 1e-6f ? ((sx > 0 ? x + 1 : x) - ax) / dx : float.MaxValue;
            float tmz = Mathf.Abs(dz) > 1e-6f ? ((sz > 0 ? z + 1 : z) - az) / dz : float.MaxValue;
            _stamp++;
            if (_occStamp == null) _occStamp = new int[_occ.Count + 1];
            if (_occStamp.Length < _occ.Count) System.Array.Resize(ref _occStamp, _occ.Count + 16);
            int guard = 0;
            while (guard++ < 200)
            {
                StatSteps++;
                if (OccluderHit(z * _w + x, a, b)) return false;
                if (x == ex && z == ez) return true;
                if (tmx < tmz)
                {
                    float y = a.y + (b.y - a.y) * tmx;
                    int i = z * _w + x, nx = x + sx;
                    if (nx < 0 || nx >= _w) return false;
                    var e = sx > 0 ? _e[i] : _e[z * _w + nx];
                    if (!Passable(e, y, _ceil[i], _ceil[z * _w + nx])) return false;
                    x = nx; tmx += tdx;
                }
                else
                {
                    float y = a.y + (b.y - a.y) * tmz;
                    int i = z * _w + x, nz = z + sz;
                    if (nz < 0 || nz >= _w) return false;
                    var e = sz > 0 ? _nEdge[i] : _nEdge[nz * _w + x];
                    if (!Passable(e, y, _ceil[i], _ceil[nz * _w + x])) return false;
                    z = nz; tmz += tdz;
                }
            }
            return true;
        }

        bool OccluderHit(int cell, Vector3 a, Vector3 b)
        {
            var list = _occCells[cell];
            if (list == null) return false;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return false;
            var ray = new Ray(a, d / len);
            foreach (int id in list)
            {
                if (_occStamp[id] == _stamp) continue;
                _occStamp[id] = _stamp;
                var bb = _occ[id];
                if (bb.Contains(a) || bb.Contains(b)) continue;          // the surface itself sits on it
                if (bb.IntersectRay(ray, out float t) && t < len - 0.02f) return true;
            }
            return false;
        }

        // ---------------- Ambient occlusion ----------------

        float CellCeil(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / C) - _origin.x, z = Mathf.FloorToInt(p.z / C) - _origin.y;
            if (x < 0 || z < 0 || x >= _w || z >= _w) return OfficeBiome.CeilingHeight;
            return _ceil[z * _w + x];
        }

        /// <summary>Distance in the floor plane from p to the nearest solid wall of its cell.</summary>
        float WallDistance(Vector3 p)
        {
            float fx = p.x / C - _origin.x, fz = p.z / C - _origin.y;
            int x = Mathf.FloorToInt(fx), z = Mathf.FloorToInt(fz);
            if (x < 0 || z < 0 || x >= _w || z >= _w) return 10f;
            float lx = (fx - x) * C, lz = (fz - z) * C, half = OfficeBiome.WallThickness * 0.5f, d = 10f;
            int i = z * _w + x;
            if (x > 0 && _e[i - 1] == EdgeKind.Wall) d = Mathf.Min(d, lx - half);
            if (_e[i] == EdgeKind.Wall) d = Mathf.Min(d, C - lx - half);
            if (z > 0 && _nEdge[i - _w] == EdgeKind.Wall) d = Mathf.Min(d, lz - half);
            if (_nEdge[i] == EdgeKind.Wall) d = Mathf.Min(d, C - lz - half);
            return Mathf.Max(0f, d);
        }

        float Occlusion(Vector3 p, Vector3 n)
        {
            float ao = 1f;
            if (n.y > 0.7f)              // floor
            {
                ao *= 1f - 0.4f * Mathf.Exp(-WallDistance(p) / 0.16f);
                ao *= ContactShadow(p);
            }
            else if (n.y < -0.7f)        // ceiling
                ao *= 1f - 0.3f * Mathf.Exp(-WallDistance(p) / 0.22f);
            else                         // walls
            {
                float ceil = CellCeil(p + n * 0.2f);
                ao *= 1f - 0.35f * Mathf.Exp(-p.y / 0.14f);
                ao *= 1f - 0.25f * Mathf.Exp(-Mathf.Max(0f, ceil - p.y) / 0.2f);
                // Inner corners: distance to a perpendicular wall, sampled just in front of the wall.
                ao *= 1f - 0.3f * Mathf.Exp(-WallDistanceAlong(p + n * 0.08f, n) / 0.18f);
            }
            return ao;
        }

        /// <summary>For a wall texel: distance to the nearest wall perpendicular to it.</summary>
        float WallDistanceAlong(Vector3 p, Vector3 n)
        {
            float fx = p.x / C - _origin.x, fz = p.z / C - _origin.y;
            int x = Mathf.FloorToInt(fx), z = Mathf.FloorToInt(fz);
            if (x < 0 || z < 0 || x >= _w || z >= _w) return 10f;
            float lx = (fx - x) * C, lz = (fz - z) * C, half = OfficeBiome.WallThickness * 0.5f, d = 10f;
            int i = z * _w + x;
            if (Mathf.Abs(n.x) > 0.5f)       // wall faces X: perpendicular walls run along X (z lines)
            {
                if (z > 0 && _nEdge[i - _w] == EdgeKind.Wall) d = Mathf.Min(d, lz - half);
                if (_nEdge[i] == EdgeKind.Wall) d = Mathf.Min(d, C - lz - half);
            }
            else
            {
                if (x > 0 && _e[i - 1] == EdgeKind.Wall) d = Mathf.Min(d, lx - half);
                if (_e[i] == EdgeKind.Wall) d = Mathf.Min(d, C - lx - half);
            }
            return Mathf.Max(0f, d);
        }

        /// <summary>Darkening on the floor around and under furniture and other low boxes.</summary>
        float ContactShadow(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / C) - _origin.x, z = Mathf.FloorToInt(p.z / C) - _origin.y;
            float k = 1f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = x + dx, cz = z + dz;
                if (cx < 0 || cz < 0 || cx >= _w || cz >= _w) continue;
                var list = _occCells[cz * _w + cx];
                if (list == null) continue;
                foreach (int id in list)
                {
                    var b = _occ[id];
                    if (b.min.y > 0.5f) continue;
                    float ex = Mathf.Max(0f, Mathf.Max(b.min.x - p.x, p.x - b.max.x));
                    float ez = Mathf.Max(0f, Mathf.Max(b.min.z - p.z, p.z - b.max.z));
                    float d = Mathf.Sqrt(ex * ex + ez * ez);
                    float s = d <= 0f ? 0.45f : 0.4f * Mathf.Exp(-d / 0.12f);
                    k = Mathf.Min(k, 1f - s);
                }
            }
            return k;
        }

        // ---------------- Lighting ----------------

        void IndexEmitters()
        {
            _emitCells = new List<int>[_w * _w];
            for (int i = 0; i < _emit.Count; i++)
            {
                var e = _emit[i];
                int x = Mathf.FloorToInt(e.pos.x / C) - _origin.x, z = Mathf.FloorToInt(e.pos.z / C) - _origin.y;
                if (x < 0 || z < 0 || x >= _w || z >= _w) continue;
                (_emitCells[z * _w + x] ??= new List<int>()).Add(i);
            }
        }

        readonly List<int> _near = new List<int>();

        /// <summary>Emitters whose fill range can reach anywhere within radius of a point.</summary>
        void GatherNear(Vector3 center, float radius)
        {
            _near.Clear();
            for (int i = 0; i < _emit.Count; i++)
            {
                float r = _emit[i].range * 1.1f + radius;
                if ((_emit[i].pos - center).sqrMagnitude < r * r) _near.Add(i);
            }
        }

        Vector3 Irradiance(Vector3 p, Vector3 n)
        {
            Vector3 q = p + n * 0.04f;
            Vector3 sum = Vector3.zero;
            _dirAcc = Vector3.zero;
            StatEvals++; StatNear += _near.Count;
            int qRegion = RegionAt(q);
            {
                foreach (int id in _near)
                {
                    var e = _emit[id];
                    if (!Reaches(e.region, qRegion)) continue;
                    Vector3 L = e.pos - q;
                    float d2 = L.sqrMagnitude;
                    float fillRange = e.range * 1.1f;
                    if (d2 > fillRange * fillRange) continue;
                    float d = Mathf.Sqrt(d2);
                    Vector3 l = L / Mathf.Max(d, 1e-4f);
                    float ndl = Vector3.Dot(n, l);
                    // Direct: inverse square with a smooth range window and Lambert at the surface. Ceiling
                    // fittings emit downward (a recessed panel is nearly cosine; domes and bare strips wider),
                    // so they barely light the ceiling or shine sideways through far doorways.
                    float win = Window(d, e.range);
                    float down = Mathf.Max(0f, -l.y);
                    float emit = e.shape == 3 ? 1f : e.shape == 0 ? Mathf.Pow(down, 1.15f) : e.shape == 1 ? 0.15f + 0.85f * Mathf.Pow(down, 0.7f) : 0.12f + 0.88f * Mathf.Pow(down, 0.8f);
                    float direct = Mathf.Max(0f, ndl) * emit * win / (d2 + 0.08f);
                    // Fill: light that has bounced around the room; soft and wide, still blocked by walls.
                    // Most bounced light comes up off the floor: floors and lower walls get the most fill,
                    // ceilings the least (so ceilings stay darker than the floor, as in a real office).
                    float up = 0.4f + 0.6f * (n.y * 0.5f + 0.5f);
                    float fill = 0.035f * up * (0.6f + 0.4f * Mathf.Max(0f, ndl)) * Window(d, fillRange) / (1f + d2 * 0.09f);
                    float contrib = (direct + fill) * (e.color.x + e.color.y + e.color.z);
                    if (contrib < 0.0015f) continue;                      // too faint to matter
                    // Soft shadows: visibility averaged over points across the fitting.
                    var pts = Samples[e.shape];
                    float vis;
                    if (pts.Length == 1) vis = Visible(e.pos, q) ? 1f : 0f;
                    else
                    {
                        // Opposite corners first; only a texel in the penumbra needs the other two.
                        bool v0 = Visible(e.pos + pts[0], q), v3 = Visible(e.pos + pts[3], q);
                        if (v0 == v3 && e.region == qRegion) vis = v0 ? 1f : 0f;
                        else vis = ((v0 ? 1 : 0) + (v3 ? 1 : 0) + (Visible(e.pos + pts[1], q) ? 1 : 0) + (Visible(e.pos + pts[2], q) ? 1 : 0)) * 0.25f;
                    }
                    if (vis <= 0f) continue;
                    direct *= vis;
                    fill *= Mathf.Sqrt(vis);
                    sum += e.color * (direct + fill);
                    // Dominant light direction (for normal-mapped surfaces), weighted by direct light.
                    _dirAcc += l * (direct * (e.color.x + e.color.y + e.color.z));
                }
            }
            return sum;
        }

        Vector3 _dirAcc;

        /// <summary>Unity's directional lightmap encoding: xyz = 0.5 + dominant direction * 0.5 * strength,
        /// w = the half-Lambert value at the geometric normal (so flat surfaces keep their brightness).</summary>
        static Vector4 EncodeDirection(Vector3 dirSum, float total, Vector3 n)
        {
            float len = dirSum.magnitude;
            Vector3 d = len > 1e-5f ? dirSum / len : n;
            float strength = total > 1e-5f ? Mathf.Clamp01(len / total) * 0.75f : 0f;
            Vector3 enc = d * 0.5f * strength;
            float w = Mathf.Max(0.05f, Vector3.Dot(n, enc) + 0.5f);
            return new Vector4(enc.x + 0.5f, enc.y + 0.5f, enc.z + 0.5f, w);
        }

        static float Window(float d, float r)
        {
            float t = d / r;
            float a = Mathf.Clamp01(1f - t * t * t * t);
            return a * a;
        }

        // ---------------- Atlas + bake ----------------

        struct Chart { public MeshWriter w; public MeshWriter.LmQuad q; public int tw, th, x, y; }

        public void Bake(List<MeshWriter> writers)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var charts = new List<Chart>();
            foreach (var w in writers)
                foreach (var q in w.lmQuads)
                {
                    float uLen = (q.b - q.a).magnitude, vLen = (q.d - q.a).magnitude;
                    int tw = Mathf.Clamp(Mathf.CeilToInt(uLen * TexelsPerMetre) + 1, 2, AtlasWidth - 2);
                    int th = Mathf.Clamp(Mathf.CeilToInt(vLen * TexelsPerMetre) + 1, 2, 256);
                    charts.Add(new Chart { w = w, q = q, tw = tw, th = th });
                }
            // Shelf packing, tallest first.
            charts.Sort((a, b) => b.th.CompareTo(a.th));
            int px = 0, py = 0, shelf = 0;
            for (int i = 0; i < charts.Count; i++)
            {
                var c = charts[i];
                int cw = c.tw + 2, ch = c.th + 2;
                if (px + cw > AtlasWidth) { px = 0; py += shelf; shelf = 0; }
                c.x = px; c.y = py;
                px += cw; shelf = Mathf.Max(shelf, ch);
                charts[i] = c;
            }
            Width = AtlasWidth;
            Height = Mathf.Max(4, (py + shelf + 3) / 4 * 4);
            var col = new Vector3[Width * Height];
            var dir = new Vector4[Width * Height];
            float iw = 1f / Width, ih = 1f / Height;

            foreach (var c in charts)
            {
                var q = c.q;
                Vector3 ab = q.b - q.a, ad = q.d - q.a;
                Vector3 mid = q.a + (ab + ad) * 0.5f;
                GatherNear(mid, (ab.magnitude + ad.magnitude) * 0.5f + 0.1f);
                if (c.tw * c.th <= 9)
                {
                    // Tiny pieces (trim, grid bars, plates): one sample is plenty.
                    Vector3 e1 = Irradiance(mid, q.normal) * Occlusion(mid, q.normal);
                    var d1 = EncodeDirection(_dirAcc, Lum(e1), q.normal);
                    for (int j = 0; j < c.th; j++) for (int i = 0; i < c.tw; i++) { int ix = (c.y + 1 + j) * Width + c.x + 1 + i; col[ix] = e1; dir[ix] = d1; }
                }
                else
                for (int j = 0; j < c.th; j++)
                for (int i = 0; i < c.tw; i++)
                {
                    // Texel centres span the quad edge to edge, so texels on a shared edge of two
                    // neighbouring quads are lit at the same world points (no seams between panels).
                    float s = i / (float)(c.tw - 1), t = j / (float)(c.th - 1);
                    Vector3 p = q.a + ab * s + ad * t;
                    Vector3 e = Irradiance(p, q.normal) * Occlusion(p, q.normal);
                    int idx = (c.y + 1 + j) * Width + c.x + 1 + i;
                    col[idx] = e;
                    dir[idx] = EncodeDirection(_dirAcc, Lum(e), q.normal);
                }
                // Padding: copy the nearest interior texel so filtering never bleeds in black.
                for (int j = -1; j <= c.th; j++)
                for (int i = -1; i <= c.tw; i++)
                {
                    if (i >= 0 && j >= 0 && i < c.tw && j < c.th) continue;
                    int si = Mathf.Clamp(i, 0, c.tw - 1), sj = Mathf.Clamp(j, 0, c.th - 1);
                    col[(c.y + 1 + j) * Width + c.x + 1 + i] = col[(c.y + 1 + sj) * Width + c.x + 1 + si];
                    dir[(c.y + 1 + j) * Width + c.x + 1 + i] = dir[(c.y + 1 + sj) * Width + c.x + 1 + si];
                }
                float u0 = (c.x + 1.5f) * iw, u1 = (c.x + c.tw + 0.5f) * iw, v0 = (c.y + 1.5f) * ih, v1 = (c.y + c.th + 0.5f) * ih;
                c.w.SetLightmapUV(q.first, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }

            Pixels = new ushort[Width * Height * 4];
            ushort one = Mathf.FloatToHalf(1f);
            for (int i = 0; i < col.Length; i++)
            {
                var v = col[i];
                Pixels[i * 4] = Mathf.FloatToHalf(v.x);
                Pixels[i * 4 + 1] = Mathf.FloatToHalf(v.y);
                Pixels[i * 4 + 2] = Mathf.FloatToHalf(v.z);
                Pixels[i * 4 + 3] = one;
            }
            DirPixels = new byte[Width * Height * 4];
            for (int i = 0; i < dir.Length; i++)
            {
                var v = dir[i];
                if (v.w <= 0f) v = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
                DirPixels[i * 4] = (byte)Mathf.Clamp(Mathf.RoundToInt(v.x * 255f), 0, 255);
                DirPixels[i * 4 + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(v.y * 255f), 0, 255);
                DirPixels[i * 4 + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(v.z * 255f), 0, 255);
                DirPixels[i * 4 + 3] = (byte)Mathf.Clamp(Mathf.RoundToInt(v.w * 255f), 1, 255);
            }
            BakeMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        static float Lum(Vector3 c) => c.x + c.y + c.z;
    }
}
