using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Paint on the world's surfaces (from <see cref="SprayCan"/>), remade 2026-11-04 (the owner: "I don't like how the spray
    /// paint looks ... on the wall": it was a band of separate soft pink circles, too many identical straight drips, and a
    /// blotchy cloud from far off). Now, as paint simulators and texture painting do (a 2025 Unity graffiti painter keeps
    /// the paint's amount in a texture and lets drips grow from it; automotive spray simulators spread each traced ray's
    /// paint over a small area rather than tracing every droplet):
    /// <list type="bullet">
    /// <item>Paint builds up in canvases: a 0.5 m square texture (256 texels, about 2 mm each) on each flat surface it lands
    /// on, made where the first paint lands (a plane of the surface, tiles over it), the amount in coats (a byte: 64 a coat,
    /// up to four). Each traced ray of the spray lays a small Gaussian of paint (Deposit): the can's cone and distance give
    /// the pattern, so close it is a tight line, at a hand's span an even stroke, from far a wide dusting.</item>
    /// <item>Drawn by Backrooms/SprayCanvas: covering as paint does (one coat nearly hides the wall, several do), thin paint
    /// a speckle of separate droplets (each fine cell of the surface has its droplet, bigger the more paint there is), lit by
    /// the light on the wall under it (as chalk and tape are: the camera's opaque copy), and not past the wall's edge (the
    /// depth under it).</item>
    /// <item>Wet paint is kept per 1.6 cm cell and dries over seconds; where a vertical surface gets more than it holds (the
    /// can held still and close), the excess runs: a drip crawls down, slowing, wavering a little, laying its paint into the
    /// canvas as it goes, and ends in a bead (it stops where the wall does).</item>
    /// </list>
    /// Saved with the world (Write: the canvases, compressed); older saves' dots and drips are painted into canvases.
    /// </summary>
    public class SprayPaint : MonoBehaviour
    {
        static SprayPaint _instance;
        /// <summary>The paint, if any has been made in this scene (null otherwise; never creates it).</summary>
        public static SprayPaint Existing => _instance;
        public static SprayPaint Instance
        {
            get
            {
                if (!_instance) _instance = new GameObject("SprayPaint").AddComponent<SprayPaint>();
                return _instance;
            }
        }

        public const float Tile = 0.5f;
        public const int Res = 256;
        const float Texel = Tile / Res;
        const float CoatUnit = 64f;                 // byte value of one coat
        const int WetCells = 32;                    // wet paint per 1.56 cm cell
        const float WetCell = Tile / WetCells;
        [Tooltip("Wet paint (coats over a cell) a vertical surface holds before it runs.")] public float runThreshold = 3.4f;
        [Tooltip("Metres of drip per coat of excess paint.")] public float runPerCoat = 0.05f;
        [Tooltip("Longest drip, metres.")] public float maxRun = 0.4f;
        [Tooltip("Seconds for wet paint to set (it can run only while wet).")] public float dryTime = 5f;

        struct Key : System.IEquatable<Key>
        {
            public int nx, ny, nz, d, u, v;
            public bool Equals(Key o) => nx == o.nx && ny == o.ny && nz == o.nz && d == o.d && u == o.u && v == o.v;
            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode() { unchecked { int h = nx * 73856093 ^ ny * 19349663 ^ nz * 83492791; h = h * 31 + d; h = h * 31 + u; return h * 31 + v; } }
        }

        class Canvas
        {
            public Key key;
            public Vector3 n, t, b, origin;
            public byte[] px;
            public float[] wet;
            public float wetUntil;
            public Texture2D tex;
            public GameObject go;
            public bool dirty;
            public Bounds bounds;
            public int netX0 = Res, netY0 = Res, netX1 = -1, netY1 = -1;      // painted here since last sent (co-op)
        }

        readonly Dictionary<Key, Canvas> _canvases = new Dictionary<Key, Canvas>();
        readonly List<Canvas> _wetList = new List<Canvas>();
        Material _material;
        Mesh _quad;
        MaterialPropertyBlock _mpb;
        static readonly int CoverageId = Shader.PropertyToID("_Coverage");

        class Drip
        {
            public Vector3 head, n, down, side;
            public float length, target, width, speed, seed;
            public Canvas canvas;
        }
        readonly List<Drip> _drips = new List<Drip>();
        bool _inDrip;

        public int Dots { get; private set; }
        public int ActiveDrips => _drips.Count;
        public int FinishedDrips { get; private set; }
        public int Canvases => _canvases.Count;

        void Awake()
        {
            var shader = Shader.Find("Backrooms/SprayCanvas");
            _material = new Material(shader ? shader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "Spray paint", hideFlags = HideFlags.DontSave };
            _mpb = new MaterialPropertyBlock();
            _quad = new Mesh { name = "PaintCanvas" };
            _quad.SetVertices(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) });
            _quad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _quad.RecalculateBounds();
        }

        void OnDestroy()
        {
            foreach (var c in _canvases.Values) { if (c.tex) Destroy(c.tex); }
            if (_material) Destroy(_material);
            if (_quad) Destroy(_quad);
            if (_instance == this) _instance = null;
        }

        // ---------------- Canvases ----------------

        /// <summary>The plane a point on a surface is on: its normal (to 1/20) and distance (to 3 mm), and its frame.</summary>
        static void Plane(Vector3 point, Vector3 normal, out int nx, out int ny, out int nz, out Vector3 n, out int dq)
        {
            nx = Mathf.RoundToInt(normal.x * 20f); ny = Mathf.RoundToInt(normal.y * 20f); nz = Mathf.RoundToInt(normal.z * 20f);
            n = new Vector3(nx, ny, nz).normalized;
            if (n.sqrMagnitude < 0.5f) n = normal.normalized;
            dq = Mathf.RoundToInt(Vector3.Dot(n, point) / 0.003f);
        }

        static void Frame(Vector3 n, out Vector3 t, out Vector3 b)
        {
            t = Vector3.Cross(Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up, n).normalized;
            b = Vector3.Cross(n, t);
        }

        Canvas Get(int nx, int ny, int nz, Vector3 n, int dq, int u, int v, bool make)
        {
            var key = new Key { nx = nx, ny = ny, nz = nz, d = dq, u = u, v = v };
            if (_canvases.TryGetValue(key, out var c) || !make) return c;
            Frame(n, out var t, out var b);
            c = new Canvas { key = key, n = n, t = t, b = b, px = new byte[Res * Res], wet = new float[WetCells * WetCells] };
            c.origin = n * (dq * 0.003f) + t * (u * Tile) + b * (v * Tile);
            c.tex = new Texture2D(Res, Res, TextureFormat.R8, true, true) { name = "Paint", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2, hideFlags = HideFlags.DontSave };
            c.go = new GameObject("PaintCanvas");
            c.go.transform.SetParent(transform, false);
            // the quad: its x along t, y along b, a millimetre off the surface
            c.go.transform.position = c.origin + n * 0.0012f;
            c.go.transform.rotation = Quaternion.LookRotation(-n, b);
            c.go.transform.localScale = new Vector3(-Tile, Tile, 1f);
            c.go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = c.go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = 0xFFFFFFFFu;
            _mpb.SetTexture(CoverageId, c.tex);
            r.SetPropertyBlock(_mpb);
            c.bounds = new Bounds(c.origin + (t + b) * (Tile * 0.5f), Vector3.zero);
            c.bounds.Encapsulate(c.origin); c.bounds.Encapsulate(c.origin + t * Tile + b * Tile); c.bounds.Expand(0.02f);
            _canvases[key] = c;
            return c;
        }

        /// <summary>
        /// A ray of the spray lands: a Gaussian of paint sigma (m) wide, coats deep at its middle (coats: of a full coat).
        /// </summary>
        public void Deposit(Vector3 point, Vector3 normal, float sigma, float coats)
        {
            Plane(point, normal, out int nx, out int ny, out int nz, out var n, out int dq);
            Frame(n, out var t, out var b);
            float pu = Vector3.Dot(point, t), pv = Vector3.Dot(point, b);
            float reach = sigma * 2.6f;
            int u0 = Mathf.FloorToInt((pu - reach) / Tile), u1 = Mathf.FloorToInt((pu + reach) / Tile);
            int v0 = Mathf.FloorToInt((pv - reach) / Tile), v1 = Mathf.FloorToInt((pv + reach) / Tile);
            for (int u = u0; u <= u1; u++)
                for (int v = v0; v <= v1; v++)
                    Stamp(Get(nx, ny, nz, n, dq, u, v, true), pu - u * Tile, pv - v * Tile, sigma, coats);
            Dots++;
            if (_inDrip) return;                    // (a drip's own paint does not start drips)
            // Wet paint here; on a wall, more than it holds runs.
            var home = Get(nx, ny, nz, n, dq, Mathf.FloorToInt(pu / Tile), Mathf.FloorToInt(pv / Tile), true);
            float lu = pu - home.key.u * Tile, lv = pv - home.key.v * Tile;
            int cx = Mathf.Clamp((int)(lu / WetCell), 0, WetCells - 1), cy = Mathf.Clamp((int)(lv / WetCell), 0, WetCells - 1);
            float added = coats * 2f * Mathf.PI * sigma * sigma / (WetCell * WetCell);
            ref float wet = ref home.wet[cy * WetCells + cx];
            wet += added;
            if (home.wetUntil < Time.time) { if (!_wetList.Contains(home)) _wetList.Add(home); }
            home.wetUntil = Time.time + dryTime * 3f;
            if (Mathf.Abs(n.y) < 0.55f && wet > runThreshold)
            {
                float excess = wet - runThreshold * 0.6f;
                wet = runThreshold * 0.6f;
                StartDrip(point, n, home, excess);
            }
        }

        /// <summary>Adds paint into one canvas around (lu, lv) metres from its corner.</summary>
        void Stamp(Canvas c, float lu, float lv, float sigma, float coats)
        {
            float cxp = lu / Texel - 0.5f, cyp = lv / Texel - 0.5f;
            float s = sigma / Texel, inv = 1f / (2f * s * s);
            int r = Mathf.CeilToInt(s * 2.6f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cxp) - r), x1 = Mathf.Min(Res - 1, Mathf.CeilToInt(cxp) + r);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cyp) - r), y1 = Mathf.Min(Res - 1, Mathf.CeilToInt(cyp) + r);
            if (x0 > x1 || y0 > y1) return;
            float peak = coats * CoatUnit;
            for (int y = y0; y <= y1; y++)
            {
                float dy = y - cyp;
                int row = y * Res;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - cxp;
                    float g = Mathf.Exp(-(dx * dx + dy * dy) * inv);
                    if (g < 0.02f) continue;
                    int i = row + x;
                    // (a little random in what each texel gets: paint arrives as droplets, not a smooth film)
                    // (rounded at random, so the faint edge of a far spray keeps its paint as a sparse speckle)
                    int add = (int)(peak * g * (0.88f + 0.24f * Hash01(i ^ Dots)) + Hash01(i * 7 + Dots * 13 + 5));
                    if (add <= 0) continue;
                    int vnew = c.px[i] + add;
                    c.px[i] = (byte)(vnew > 255 ? 255 : vnew);
                }
            }
            c.dirty = true;
            if (Net.NetHooks.On)
            {
                if (x0 < c.netX0) c.netX0 = x0; if (y0 < c.netY0) c.netY0 = y0;
                if (x1 > c.netX1) c.netX1 = x1; if (y1 > c.netY1) c.netY1 = y1;
                _netDirty.Add(c);
            }
        }

        // ---------------- Co-op (2026-11-04) ----------------
        // What is painted here goes to the other players as the changed rectangle of each canvas (deflated, a few times a
        // second); what comes from them is merged texel by texel taking the larger (paint only builds up), so two people
        // spraying one wall at once both keep their paint, in any order. Drips run in the painter's game; their paint comes
        // as paint.

        public struct NetPatch { public int nx, ny, nz, d, u, v, x0, y0, w, h; public byte[] packed; }
        readonly HashSet<Canvas> _netDirty = new HashSet<Canvas>();

        public void TakeNetPatches(List<NetPatch> into)
        {
            foreach (var c in _netDirty)
            {
                if (c.netX1 < c.netX0 || c.netY1 < c.netY0) continue;
                int w = c.netX1 - c.netX0 + 1, h = c.netY1 - c.netY0 + 1;
                var block = new byte[w * h];
                for (int y = 0; y < h; y++) System.Buffer.BlockCopy(c.px, (c.netY0 + y) * Res + c.netX0, block, y * w, w);
                into.Add(new NetPatch { nx = c.key.nx, ny = c.key.ny, nz = c.key.nz, d = c.key.d, u = c.key.u, v = c.key.v, x0 = c.netX0, y0 = c.netY0, w = w, h = h, packed = Pack(block) });
                c.netX0 = c.netY0 = Res; c.netX1 = c.netY1 = -1;
            }
            _netDirty.Clear();
        }

        public void MergeNetPatch(NetPatch p)
        {
            if (p.packed == null || p.w <= 0 || p.h <= 0 || p.x0 < 0 || p.y0 < 0 || p.x0 + p.w > Res || p.y0 + p.h > Res) return;
            var n = new Vector3(p.nx, p.ny, p.nz).normalized;
            if (n.sqrMagnitude < 0.5f) return;
            var c = Get(p.nx, p.ny, p.nz, n, p.d, p.u, p.v, true);
            var block = new byte[p.w * p.h];
            Unpack(p.packed, block);
            for (int y = 0; y < p.h; y++)
            {
                int row = (p.y0 + y) * Res + p.x0, k = y * p.w;
                for (int x = 0; x < p.w; x++) if (block[k + x] > c.px[row + x]) c.px[row + x] = block[k + x];
            }
            c.dirty = true;
        }

        static float Hash01(int x)
        {
            unchecked { uint h = (uint)x * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 0xFFFF) / 65535f; }
        }

        // ---------------- Drips ----------------

        void StartDrip(Vector3 at, Vector3 n, Canvas home, float excess)
        {
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, n).normalized;
            if (down.sqrMagnitude < 0.5f) return;
            // a drip already running from just here takes the paint
            foreach (var d0 in _drips)
                if ((d0.head - at).sqrMagnitude < 0.03f * 0.03f || ((at - d0.head) - Vector3.Project(at - d0.head, d0.down)).sqrMagnitude < 0.006f * 0.006f && Vector3.Dot(at - d0.head, d0.down) < 0f)
                { d0.target = Mathf.Min(maxRun, d0.target + excess * runPerCoat); return; }
            _drips.Add(new Drip
            {
                head = at, n = n, down = down, side = Vector3.Cross(n, down).normalized, canvas = home,
                length = 0f, target = Mathf.Min(maxRun, 0.02f + excess * runPerCoat),
                width = Random.Range(0.0016f, 0.0026f), speed = Random.Range(0.06f, 0.1f), seed = Random.value * 100f,
            });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // Wet paint sets.
            float k = Mathf.Exp(-dt / Mathf.Max(0.1f, dryTime));
            for (int i = _wetList.Count - 1; i >= 0; i--)
            {
                var c = _wetList[i];
                if (c == null || c.wet == null) { _wetList.RemoveAt(i); continue; }
                for (int j = 0; j < c.wet.Length; j++) c.wet[j] *= k;
                if (Time.time > c.wetUntil) { System.Array.Clear(c.wet, 0, c.wet.Length); _wetList.RemoveAt(i); }
            }
            // Drips crawl down, laying their paint as they go.
            _inDrip = true;
            for (int i = _drips.Count - 1; i >= 0; i--)
            {
                var d = _drips[i];
                float left = d.target - d.length;
                float step = d.speed * dt * Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(left / 0.06f));
                bool stop = left <= 0.0005f;
                if (!stop)
                {
                    var next = d.head + d.down * step;
                    // the wall goes on under it (else it hangs at the edge)
                    if (!Physics.Raycast(next + d.n * 0.03f, -d.n, out var hit, 0.06f, ~0, QueryTriggerInteraction.Ignore) || Vector3.Dot(hit.normal, d.n) < 0.95f)
                        stop = true;
                    else
                    {
                        int sub = Mathf.Max(1, Mathf.CeilToInt(step / (Texel * 0.7f)));
                        for (int s = 1; s <= sub; s++)
                        {
                            float along = d.length + step * s / sub;
                            float wob = (Mathf.PerlinNoise(d.seed, along * 18f) - 0.5f) * 0.004f;
                            float w = d.width * Mathf.Lerp(1.15f, 0.75f, along / Mathf.Max(0.02f, d.target)) * (0.85f + 0.3f * Mathf.PerlinNoise(along * 40f, d.seed));
                            Vector3 p = d.head + d.down * (step * s / sub) + d.side * wob;
                            Deposit(p, d.n, w * 0.55f, 1.2f);
                            Dots--;
                        }
                        d.head = next;
                        d.length += step;
                    }
                }
                if (stop)
                {
                    // the bead where it stops
                    Deposit(d.head + d.side * ((Mathf.PerlinNoise(d.seed, d.length * 18f) - 0.5f) * 0.004f), d.n, d.width * 0.85f, 2f);
                    Dots--;
                    FinishedDrips++;
                    _drips.RemoveAt(i);
                }
            }
            _inDrip = false;
        }

        void LateUpdate()
        {
            foreach (var c in _canvases.Values)
            {
                if (!c.dirty) continue;
                c.dirty = false;
                c.tex.SetPixelData(c.px, 0);
                c.tex.Apply(true);
            }
        }

        // ---------------- Saving ----------------
        // Version 2: every canvas (its plane, its tile, its texels deflated). Version 1 (dots and runs as quads) is read
        // by painting each into the canvases.

        const int SaveMagic = 0x54505242;     // "BRPT"
        const int SaveVersion = 2;

        public static void WriteEmpty(System.IO.BinaryWriter w) { w.Write(SaveMagic); w.Write(SaveVersion); w.Write(0); }

        public void Write(System.IO.BinaryWriter w)
        {
            // drips still running are finished first (as a save stops them)
            while (_drips.Count > 0) { foreach (var d in _drips) d.target = d.length; Update(); }
            int count = 0;
            foreach (var c in _canvases.Values) if (HasPaint(c)) count++;
            w.Write(SaveMagic); w.Write(SaveVersion); w.Write(count);
            foreach (var c in _canvases.Values)
            {
                if (!HasPaint(c)) continue;
                w.Write((sbyte)c.key.nx); w.Write((sbyte)c.key.ny); w.Write((sbyte)c.key.nz);
                w.Write(c.key.d); w.Write(c.key.u); w.Write(c.key.v);
                var packed = Pack(c.px);
                w.Write(packed.Length); w.Write(packed);
            }
        }

        static bool HasPaint(Canvas c) { foreach (var p in c.px) if (p != 0) return true; return false; }

        static byte[] Pack(byte[] data)
        {
            using (var ms = new System.IO.MemoryStream())
            {
                using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) ds.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        static void Unpack(byte[] packed, byte[] into)
        {
            using (var ms = new System.IO.MemoryStream(packed))
            using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
            {
                int off = 0, n;
                while (off < into.Length && (n = ds.Read(into, off, into.Length - off)) > 0) off += n;
            }
        }

        static Vector3 Vec(System.IO.BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>Adds saved paint (from <see cref="Write"/>, or an older save's dots and runs).</summary>
        public void Read(System.IO.BinaryReader r)
        {
            if (r.BaseStream.Length < 12 || r.ReadInt32() != SaveMagic) return;
            int version = r.ReadInt32();
            if (version > SaveVersion) return;
            int count = r.ReadInt32();
            if (version >= 2)
            {
                for (int i = 0; i < count; i++)
                {
                    int nx = r.ReadSByte(), ny = r.ReadSByte(), nz = r.ReadSByte();
                    int dq = r.ReadInt32(), u = r.ReadInt32(), v = r.ReadInt32();
                    var packed = r.ReadBytes(r.ReadInt32());
                    var n = new Vector3(nx, ny, nz).normalized;
                    var c = Get(nx, ny, nz, n, dq, u, v, true);
                    var px = new byte[Res * Res];
                    Unpack(packed, px);
                    for (int k = 0; k < px.Length; k++) { int s = c.px[k] + px[k]; c.px[k] = (byte)(s > 255 ? 255 : s); }
                    c.dirty = true;
                }
                return;
            }
            // Version 1: each dot a Gaussian of its size and strength, each run a line of them ending in a bead.
            for (int i = 0; i < count; i++)
            {
                byte flags = r.ReadByte(), alpha = r.ReadByte();
                r.ReadSingle(); r.ReadSingle();
                Vector3 v0 = Vec(r), v1 = Vec(r), v3 = Vec(r);
                Vector3 n = Vector3.Cross(v1 - v0, v3 - v0).normalized;
                if ((flags & 2) != 0) n = -n;
                if ((flags & 1) == 0)
                {
                    Vector3 mid = (v1 + v3) * 0.5f;
                    float radius = (v1 - v0).magnitude * 0.5f;
                    Deposit(mid, n, Mathf.Max(0.0015f, radius * 0.45f), alpha / 255f * 0.9f);
                }
                else
                {
                    Vector3 top = (v0 + v1) * 0.5f, run = v3 - v0;
                    float w = (v1 - v0).magnitude * 0.5f;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(run.magnitude / 0.0015f));
                    for (int s = 0; s <= steps; s++) Deposit(top + run * (s / (float)steps), n, w * 0.5f, 1.2f);
                    FinishedDrips++;
                }
            }
        }

        /// <summary>
        /// Paint on what is gone (pieces knocked out of a wall): the texels within the bounds whose place `gone` says is no
        /// longer there are wiped (in 4 x 4 blocks), and a run going there stops.
        /// </summary>
        public void Erase(Bounds within, System.Func<Vector3, bool> gone)
        {
            foreach (var c in _canvases.Values)
            {
                if (!c.bounds.Intersects(within)) continue;
                bool any = false;
                for (int by = 0; by < Res; by += 4)
                    for (int bx = 0; bx < Res; bx += 4)
                    {
                        Vector3 p = c.origin + c.t * ((bx + 2f) * Texel) + c.b * ((by + 2f) * Texel);
                        if (!within.Contains(p) || !gone(p)) continue;
                        for (int y = by; y < by + 4; y++) for (int x = bx; x < bx + 4; x++) c.px[y * Res + x] = 0;
                        any = true;
                    }
                if (any) c.dirty = true;
            }
            for (int i = _drips.Count - 1; i >= 0; i--)
                if (within.Contains(_drips[i].head) && gone(_drips[i].head)) _drips.RemoveAt(i);
        }

        /// <summary>Removes all paint (a new world).</summary>
        public void Clear()
        {
            foreach (var c in _canvases.Values) { if (c.tex) Destroy(c.tex); if (c.go) Destroy(c.go); }
            _canvases.Clear(); _wetList.Clear(); _drips.Clear();
            Dots = 0; FinishedDrips = 0;
        }
    }
}
