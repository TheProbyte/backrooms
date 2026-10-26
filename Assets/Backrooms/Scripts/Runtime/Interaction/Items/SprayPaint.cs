using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Paint on the world's surfaces (from <see cref="SprayCan"/>). Each droplet that lands is a small soft dot in a
    /// mesh of dots (batches of 1024, only the newest batch is rebuilt), drawn with Backrooms/SprayPaint, which
    /// multiplies the colour onto the surface so the paint is lit like its wall.
    ///
    /// Wet paint is counted per 2.5 cm cell. Where a vertical surface gets more than a coat (the can held on one
    /// spot), the excess runs: a drip starts there and crawls down the wall, slowing as it goes; more paint on the same
    /// spot keeps feeding it, so it gets longer. A drip stops where the wall ends (a doorway, the floor). Finished
    /// drips join the dots. Paint stays wherever it was put, and is saved with the world (<see cref="Write"/>).
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

        public const int QuadsPerBatch = 1024;
        [Tooltip("Wet paint (in coats) a spot holds before it starts to run.")] public float runThreshold = 2.4f;
        [Tooltip("Metres of drip per coat of excess paint.")] public float runPerCoat = 0.07f;
        [Tooltip("Longest drip, metres.")] public float maxRun = 0.55f;
        const float Cell = 0.025f;

        Material _material;

        class Batch
        {
            public GameObject go;
            public Mesh mesh;
            public readonly List<Vector3> v = new List<Vector3>(QuadsPerBatch * 4);
            public readonly List<Vector4> uv = new List<Vector4>(QuadsPerBatch * 4);
            public readonly List<Color32> c = new List<Color32>(QuadsPerBatch * 4);
            public readonly List<int> idx = new List<int>(QuadsPerBatch * 6);
            public bool dirty;
            public int Quads => v.Count / 4;
        }

        readonly List<Batch> _batches = new List<Batch>();
        Batch _current;

        class Drip
        {
            public Vector3 top, normal, down, side;
            public float length, target, width, speed, alpha, seed;
            public Vector3Int cell;
            public bool done;
        }

        readonly List<Drip> _drips = new List<Drip>();
        readonly Dictionary<Vector3Int, float> _wet = new Dictionary<Vector3Int, float>();
        readonly Dictionary<Vector3Int, Drip> _dripAt = new Dictionary<Vector3Int, Drip>();
        Batch _dripBatch;           // live drips, rebuilt every frame they move

        public int Dots { get; private set; }
        public int ActiveDrips => _drips.Count;
        public int FinishedDrips { get; private set; }

        void Awake()
        {
            var shader = Shader.Find("Backrooms/SprayPaint");
            _material = new Material(shader ? shader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "Spray paint", hideFlags = HideFlags.DontSave };
            _dripBatch = NewBatch("PaintDrips");
        }

        void OnDestroy()
        {
            foreach (var b in _batches) if (b.mesh) Destroy(b.mesh);
            if (_dripBatch?.mesh) Destroy(_dripBatch.mesh);
            if (_material) Destroy(_material);
            if (_instance == this) _instance = null;
        }

        Batch NewBatch(string name)
        {
            var b = new Batch { go = new GameObject(name), mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt16 } };
            b.mesh.MarkDynamic();
            b.go.transform.SetParent(transform, false);
            b.go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
            var r = b.go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = 0xFFFFFFFFu;
            return b;
        }

        static void Quad(Batch b, Vector3 centre, Vector3 right, Vector3 up, Vector3 normal, float kind, float seed, byte alpha, bool dripShape = false)
        {
            int s = b.v.Count;
            if (dripShape)
            {
                // up spans the whole run (top at centre - up... laid out by the caller: centre is the top, up points down the run).
                b.v.Add(centre - right); b.v.Add(centre + right); b.v.Add(centre + right + up); b.v.Add(centre - right + up);
                b.uv.Add(new Vector4(-1, -0.02f, kind, seed)); b.uv.Add(new Vector4(1, -0.02f, kind, seed));
                b.uv.Add(new Vector4(1, 1.08f, kind, seed)); b.uv.Add(new Vector4(-1, 1.08f, kind, seed));
            }
            else
            {
                b.v.Add(centre - right - up); b.v.Add(centre + right - up); b.v.Add(centre + right + up); b.v.Add(centre - right + up);
                b.uv.Add(new Vector4(-1, -1, kind, seed)); b.uv.Add(new Vector4(1, -1, kind, seed));
                b.uv.Add(new Vector4(1, 1, kind, seed)); b.uv.Add(new Vector4(-1, 1, kind, seed));
            }
            var col = new Color32(255, 255, 255, alpha);
            for (int k = 0; k < 4; k++) b.c.Add(col);
            // Wound to face out along the surface normal.
            if (Vector3.Dot(Vector3.Cross(b.v[s + 1] - b.v[s], b.v[s + 2] - b.v[s]), normal) > 0f)
            { b.idx.Add(s); b.idx.Add(s + 1); b.idx.Add(s + 2); b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 3); }
            else { b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 1); b.idx.Add(s); b.idx.Add(s + 3); b.idx.Add(s + 2); }
            b.dirty = true;
        }

        static void Basis(Vector3 n, float angle, out Vector3 right, out Vector3 up)
        {
            Vector3 t = Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up;
            right = Vector3.Cross(t, n).normalized;
            up = Vector3.Cross(n, right);
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            var r2 = right * c + up * s;
            up = -right * s + up * c;
            right = r2;
        }

        Batch Current()
        {
            if (_current == null || _current.Quads >= QuadsPerBatch)
            {
                _current = NewBatch("PaintDots_" + _batches.Count);
                _batches.Add(_current);
            }
            return _current;
        }

        /// <summary>A droplet of mist lands: a dot of the given radius and strength (0..1 of a full coat).</summary>
        public void Deposit(Vector3 point, Vector3 normal, float radius, float strength)
        {
            Basis(normal, Random.value * Mathf.PI * 2f, out var right, out var up);
            Quad(Current(), point + normal * 0.0015f, right * radius, up * radius, normal, 0f, Random.value, (byte)Mathf.Clamp(Mathf.RoundToInt(strength * 255f), 1, 255));
            Dots++;

            // Wet paint in this spot; on a wall, too much of it runs.
            var key = new Vector3Int(Mathf.FloorToInt(point.x / Cell), Mathf.FloorToInt(point.y / Cell), Mathf.FloorToInt(point.z / Cell));
            float coat = strength * Mathf.Clamp01(radius / 0.02f);
            _wet.TryGetValue(key, out float wet);
            wet += coat;
            if (Mathf.Abs(normal.y) < 0.55f && wet > runThreshold)
            {
                float excess = wet - runThreshold * 0.7f;
                if (_dripAt.TryGetValue(key, out var d) && !d.done) d.target = Mathf.Min(maxRun, d.target + excess * runPerCoat);
                else StartDrip(point, normal, key, excess);
                wet = runThreshold * 0.7f;
            }
            _wet[key] = wet;
        }

        void StartDrip(Vector3 at, Vector3 normal, Vector3Int key, float excess)
        {
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, normal).normalized;
            if (down.sqrMagnitude < 0.5f) return;
            var d = new Drip
            {
                top = at + normal * 0.0018f, normal = normal, down = down, side = Vector3.Cross(normal, down).normalized,
                length = 0.004f, target = Mathf.Min(maxRun, 0.03f + excess * runPerCoat),
                width = Random.Range(0.0035f, 0.0055f), speed = Random.Range(0.05f, 0.08f), alpha = 0.95f, seed = Random.value, cell = key,
            };
            _drips.Add(d);
            _dripAt[key] = d;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool moved = false;
            for (int i = _drips.Count - 1; i >= 0; i--)
            {
                var d = _drips[i];
                if (d.length < d.target)
                {
                    // Runs fast at first and slows as the paint thins; stops where the wall ends.
                    float step = d.speed * dt * Mathf.Lerp(0.25f, 1f, Mathf.Clamp01((d.target - d.length) / 0.08f));
                    var tip = d.top + d.down * (d.length + step);
                    if (!Physics.Raycast(tip + d.normal * 0.03f, -d.normal, out var hit, 0.06f, ~0, QueryTriggerInteraction.Ignore) ||
                        Vector3.Dot(hit.normal, d.normal) < 0.95f)
                        d.target = d.length;                       // edge of the wall: it hangs there
                    else d.length += step;
                    moved = true;
                }
                if (d.length >= d.target - 1e-4f && d.length >= 0.004f)
                {
                    d.done = true;
                    Quad(Current(), d.top, d.side * d.width, d.down * (d.length + d.width * 1.2f), d.normal, 1f, d.seed, 240, true);
                    FinishedDrips++;
                    _drips.RemoveAt(i);
                    moved = true;
                }
            }
            if (moved || _dripBatch.dirty)
            {
                _dripBatch.v.Clear(); _dripBatch.uv.Clear(); _dripBatch.c.Clear(); _dripBatch.idx.Clear();
                foreach (var d in _drips) Quad(_dripBatch, d.top, d.side * d.width, d.down * (d.length + d.width * 1.2f), d.normal, 1f, d.seed, 240, true);
                _dripBatch.dirty = true;
            }
        }

        void LateUpdate()
        {
            foreach (var b in _batches) if (b.dirty) Upload(b);
            if (_dripBatch.dirty) Upload(_dripBatch);
        }

        static void Upload(Batch b)
        {
            b.dirty = false;
            b.mesh.Clear();
            b.mesh.SetVertices(b.v);
            b.mesh.SetUVs(0, b.uv);
            b.mesh.SetColors(b.c);
            b.mesh.SetTriangles(b.idx, 0, true);
        }

        // ---------------- Saving ----------------
        // Every quad is a parallelogram: its first, second and fourth corners, its kind (dot or run), seed, strength,
        // and which way it is wound. Running drips are written as they are now (they stop running).

        const int SaveMagic = 0x54505242;     // "BRPT"
        const int SaveVersion = 1;

        public static void WriteEmpty(System.IO.BinaryWriter w) { w.Write(SaveMagic); w.Write(SaveVersion); w.Write(0); }

        public void Write(System.IO.BinaryWriter w)
        {
            int count = _dripBatch.Quads;
            foreach (var b in _batches) count += b.Quads;
            w.Write(SaveMagic); w.Write(SaveVersion); w.Write(count);
            foreach (var b in _batches) WriteBatch(w, b);
            WriteBatch(w, _dripBatch);
        }

        static void WriteBatch(System.IO.BinaryWriter w, Batch b)
        {
            for (int q = 0; q < b.Quads; q++)
            {
                int s = q * 4;
                var uv = b.uv[s];
                byte flags = (byte)((uv.y > -0.5f ? 1 : 0) | (b.idx[q * 6 + 1] != s + 1 ? 2 : 0));
                w.Write(flags); w.Write(b.c[s].a); w.Write(uv.z); w.Write(uv.w);
                Vec(w, b.v[s]); Vec(w, b.v[s + 1]); Vec(w, b.v[s + 3]);
            }
        }

        static void Vec(System.IO.BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 Vec(System.IO.BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>Adds saved paint (from <see cref="Write"/>).</summary>
        public void Read(System.IO.BinaryReader r)
        {
            if (r.BaseStream.Length < 12 || r.ReadInt32() != SaveMagic) return;
            if (r.ReadInt32() > SaveVersion) return;
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                byte flags = r.ReadByte(), alpha = r.ReadByte();
                float kind = r.ReadSingle(), seed = r.ReadSingle();
                Vector3 v0 = Vec(r), v1 = Vec(r), v3 = Vec(r);
                var b = Current();
                int s = b.v.Count;
                b.v.Add(v0); b.v.Add(v1); b.v.Add(v1 + v3 - v0); b.v.Add(v3);
                bool drip = (flags & 1) != 0;
                float y0 = drip ? -0.02f : -1f, y1 = drip ? 1.08f : 1f;
                b.uv.Add(new Vector4(-1, y0, kind, seed)); b.uv.Add(new Vector4(1, y0, kind, seed));
                b.uv.Add(new Vector4(1, y1, kind, seed)); b.uv.Add(new Vector4(-1, y1, kind, seed));
                var col = new Color32(255, 255, 255, alpha);
                for (int k = 0; k < 4; k++) b.c.Add(col);
                if ((flags & 2) == 0) { b.idx.Add(s); b.idx.Add(s + 1); b.idx.Add(s + 2); b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 3); }
                else { b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 1); b.idx.Add(s); b.idx.Add(s + 3); b.idx.Add(s + 2); }
                b.dirty = true;
                if (drip) FinishedDrips++; else Dots++;
            }
        }

        /// <summary>Removes all paint (a new world).</summary>
        public void Clear()
        {
            foreach (var b in _batches) { Destroy(b.mesh); Destroy(b.go); }
            _batches.Clear(); _current = null;
            _drips.Clear(); _dripAt.Clear(); _wet.Clear();
            _dripBatch.dirty = true;
            Dots = 0; FinishedDrips = 0;
        }
    }
}
