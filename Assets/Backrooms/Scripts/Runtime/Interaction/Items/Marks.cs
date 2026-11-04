using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Marks the player leaves on the building: chalk lines (<see cref="Chalk"/>) and strips of tape (<see cref="TapeRoll"/>).
    /// Every mark is a flat strip lying on its surface, drawn with Backrooms/Marks, which takes its light from the surface
    /// under it. Strips are batched into meshes of 1024 (only the newest batch is rebuilt) and stay where they were put for
    /// the whole world, saved with it (<see cref="Write"/>).
    /// <see cref="Strip"/> is also used on its own for marks that belong to a chunk (the tape outline of a noclip door).
    /// </summary>
    public class Marks : MonoBehaviour
    {
        static Marks _instance;
        public static Marks Existing => _instance;
        public static Marks Instance
        {
            get
            {
                if (!_instance) _instance = new GameObject("Marks").AddComponent<Marks>();
                return _instance;
            }
        }

        public const float ChalkWidth = 0.011f, TapeWidth = 0.048f;
        /// <summary>A tape strip is laid this much wider each side than the tape: the shader draws its edge shadow there.</summary>
        public const float TapeMargin = 0.003f, TapeStripWidth = TapeWidth + 2f * TapeMargin;
        public const float KindChalk = 0f, KindTape = 1f;
        public static readonly Color32 ChalkColor = new Color32(236, 236, 228, 235);
        public static readonly Color32 TapeColor = new Color32(52, 104, 204, 255);
        const int QuadsPerBatch = 1024;

        static Material s_material;
        /// <summary>The shared Backrooms/Marks material.</summary>
        public static Material Material
        {
            get
            {
                if (!s_material)
                {
                    var sh = Shader.Find("Backrooms/Marks");
                    s_material = new Material(sh ? sh : Shader.Find("Universal Render Pipeline/Unlit")) { name = "Marks", hideFlags = HideFlags.DontSave };
                }
                return s_material;
            }
        }

        public class Batch
        {
            public GameObject go;
            public Mesh mesh;
            public readonly List<Vector3> v = new List<Vector3>(QuadsPerBatch * 4);
            public readonly List<Vector4> uv = new List<Vector4>(QuadsPerBatch * 4);
            public readonly List<Color32> c = new List<Color32>(QuadsPerBatch * 4);
            public readonly List<Vector3> n = new List<Vector3>(QuadsPerBatch * 4);   // the surface's normal (tape ends flutter along it)
            public readonly List<int> idx = new List<int>(QuadsPerBatch * 6);
            public bool dirty;
            public int Quads => v.Count / 4;
            public void Clear() { v.Clear(); uv.Clear(); c.Clear(); n.Clear(); idx.Clear(); dirty = true; }
        }

        readonly List<Batch> _batches = new List<Batch>();
        Batch _current, _preview;

        public int Count { get; private set; }

        void OnDestroy()
        {
            foreach (var b in _batches) if (b.mesh) Destroy(b.mesh);
            if (_preview != null && _preview.mesh) Destroy(_preview.mesh);
            if (_instance == this) _instance = null;
        }

        /// <summary>A batch: a mesh with its own renderer under a parent (used for chunk-owned marks too).</summary>
        public static Batch NewBatch(string name, Transform parent)
        {
            var b = new Batch { go = new GameObject(name), mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt16 } };
            b.mesh.MarkDynamic();
            b.go.transform.SetParent(parent, false);
            b.go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);     // strips are in world space
            b.go.transform.localScale = Vector3.one;
            b.go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
            var r = b.go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = 0xFFFFFFFFu;
            return b;
        }

        public static void Upload(Batch b)
        {
            b.dirty = false;
            b.mesh.Clear();
            b.mesh.SetVertices(b.v);
            b.mesh.SetUVs(0, b.uv);
            b.mesh.SetColors(b.c);
            if (b.n.Count == b.v.Count) b.mesh.SetNormals(b.n);
            b.mesh.SetTriangles(b.idx, 0, true);
        }

        /// <summary>
        /// One strip from a to b lying on a surface with the given normal (a and b on the surface): its corners are
        /// a -/+ side, b +/- side, lifted a hair off the surface.
        /// </summary>
        public static void Strip(Batch b, Vector3 a, Vector3 bEnd, Vector3 normal, float width, float kind, Color32 col)
        {
            Vector3 d = bEnd - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 side = Vector3.Cross(normal, d / len).normalized * (width * 0.5f);
            Vector3 lift = normal * (kind < 0.5f ? 0.0007f : 0.0012f);
            Corners(b, a - side + lift, a + side + lift, bEnd - side + lift, kind, col, normal);
        }

        /// <summary>A strip from three of its corners (start-left, start-right, end-left): the fourth completes the parallelogram.</summary>
        static void Corners(Batch b, Vector3 v0, Vector3 v1, Vector3 v3, float kind, Color32 col, Vector3 normal)
        {
            int s = b.v.Count;
            Vector3 v2 = v1 + v3 - v0;
            float len = (v3 - v0).magnitude;
            b.v.Add(v0); b.v.Add(v1); b.v.Add(v2); b.v.Add(v3);
            b.uv.Add(new Vector4(-1, 0, kind, len)); b.uv.Add(new Vector4(1, 0, kind, len));
            b.uv.Add(new Vector4(1, len, kind, len)); b.uv.Add(new Vector4(-1, len, kind, len));
            for (int k = 0; k < 4; k++) { b.c.Add(col); b.n.Add(normal.normalized); }
            if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), normal) > 0f)
            { b.idx.Add(s); b.idx.Add(s + 1); b.idx.Add(s + 2); b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 3); }
            else { b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 1); b.idx.Add(s); b.idx.Add(s + 3); b.idx.Add(s + 2); }
            b.dirty = true;
        }

        /// <summary>
        /// A strip along a path over surfaces (tape bent a little as it was laid, or wrapped round a corner): points, and the
        /// normal of the surface each piece lies on (normals[i] for the piece from points[i] to points[i + 1]). Pieces on
        /// the same surface share their ends (mitred, no gaps or overlaps on a bend); at a corner each ends on the edge.
        /// The "along" texture coordinate runs on over the whole strip, so it is torn at its two ends only.
        /// </summary>
        public static void Path(Batch b, IReadOnlyList<Vector3> points, IReadOnlyList<Vector3> normals, float width, float kind, Color32 col)
        {
            int n = points.Count;
            if (n < 2) return;
            float total = 0f;
            for (int i = 0; i < n - 1; i++) total += Vector3.Distance(points[i], points[i + 1]);
            if (total < 1e-4f) return;
            float half = width * 0.5f, lift = kind < 0.5f ? 0.0007f : 0.0012f, s = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = points[i], c = points[i + 1], nn = normals[i];
                float len = Vector3.Distance(a, c);
                if (len < 1e-5f) continue;
                Vector3 t = (c - a) / len;
                Vector3 sa = JoinSide(points, normals, i, i - 1, t, nn) * half, sc = JoinSide(points, normals, i, i + 1, t, nn) * half;
                Vector3 up = nn * lift;
                Quad(b, a - sa + up, a + sa + up, c + sc + up, c - sc + up, s, s + len, total, kind, col, nn);
                s += len;
            }
        }

        /// <summary>The side vector at one end of piece i: mitred with the neighbouring piece j if it lies on the same surface.</summary>
        static Vector3 JoinSide(IReadOnlyList<Vector3> p, IReadOnlyList<Vector3> nrm, int i, int j, Vector3 t, Vector3 nn)
        {
            Vector3 own = Vector3.Cross(nn, t).normalized;
            if (j < 0 || j >= p.Count - 1 || Vector3.Dot(nrm[j], nn) < 0.99f) return own;
            Vector3 tj = p[j + 1] - p[j];
            if (tj.sqrMagnitude < 1e-10f) return own;
            Vector3 avg = (t + tj.normalized).normalized;
            Vector3 side = Vector3.Cross(nn, avg).normalized;
            float k = Vector3.Dot(side, own);
            return k > 0.5f ? side / k : own;
        }

        /// <summary>One quad of a strip: corners start-left, start-right, end-right, end-left, and where along the strip it runs.</summary>
        static void Quad(Batch b, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, float s0, float s1, float total, float kind, Color32 col, Vector3 normal)
        {
            int s = b.v.Count;
            b.v.Add(v0); b.v.Add(v1); b.v.Add(v2); b.v.Add(v3);
            b.uv.Add(new Vector4(-1, s0, kind, total)); b.uv.Add(new Vector4(1, s0, kind, total));
            b.uv.Add(new Vector4(1, s1, kind, total)); b.uv.Add(new Vector4(-1, s1, kind, total));
            for (int k = 0; k < 4; k++) { b.c.Add(col); b.n.Add(normal.normalized); }
            if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), normal) > 0f)
            { b.idx.Add(s); b.idx.Add(s + 1); b.idx.Add(s + 2); b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 3); }
            else { b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 1); b.idx.Add(s); b.idx.Add(s + 3); b.idx.Add(s + 2); }
            b.dirty = true;
        }

        /// <summary>Chalk and tape on what is gone (pieces knocked out of a wall): quads within the bounds whose middle is gone are taken away.</summary>
        public void Erase(Bounds within, System.Func<Vector3, bool> gone)
        {
            foreach (var b in _batches)
            {
                int quads = b.Quads, kept = 0;
                for (int q = 0; q < quads; q++)
                {
                    int s = q * 4;
                    Vector3 mid = (b.v[s] + b.v[s + 2]) * 0.5f;
                    bool flip = b.idx[q * 6 + 1] != s + 1;
                    if (within.Contains(mid) && gone(mid)) { Count--; continue; }
                    if (kept != q)
                    {
                        int t = kept * 4;
                        for (int k = 0; k < 4; k++) { b.v[t + k] = b.v[s + k]; b.uv[t + k] = b.uv[s + k]; b.c[t + k] = b.c[s + k]; if (b.n.Count > s + k) b.n[t + k] = b.n[s + k]; }
                        int i = kept * 6;
                        if (!flip) { b.idx[i] = t; b.idx[i + 1] = t + 1; b.idx[i + 2] = t + 2; b.idx[i + 3] = t; b.idx[i + 4] = t + 2; b.idx[i + 5] = t + 3; }
                        else { b.idx[i] = t; b.idx[i + 1] = t + 2; b.idx[i + 2] = t + 1; b.idx[i + 3] = t; b.idx[i + 4] = t + 3; b.idx[i + 5] = t + 2; }
                    }
                    kept++;
                }
                if (kept == quads) continue;
                b.v.RemoveRange(kept * 4, b.v.Count - kept * 4); b.uv.RemoveRange(kept * 4, b.uv.Count - kept * 4);
                b.c.RemoveRange(kept * 4, b.c.Count - kept * 4); if (b.n.Count > kept * 4) b.n.RemoveRange(kept * 4, b.n.Count - kept * 4);
                b.idx.RemoveRange(kept * 6, b.idx.Count - kept * 6);
                b.dirty = true;
            }
        }

        Batch Current()
        {
            if (_current == null || _current.Quads >= QuadsPerBatch)
            {
                _current = NewBatch("Marks_" + _batches.Count, transform);
                _batches.Add(_current);
            }
            return _current;
        }

        /// <summary>A stroke of chalk (round-ended, so strokes join).</summary>
        public void AddChalk(Vector3 a, Vector3 b, Vector3 normal)
        {
            Vector3 d = (b - a).normalized * (ChalkWidth * 0.5f);
            Strip(Current(), a - d, b + d, normal, ChalkWidth, KindChalk, ChalkColor);
            Count++;
        }

        public void AddTape(Vector3 a, Vector3 b, Vector3 normal)
        {
            Strip(Current(), a, b, normal, TapeStripWidth, KindTape, TapeColor);
            Count++;
        }

        /// <summary>A strip of tape along a path (see <see cref="Path"/>).</summary>
        public void AddTape(IReadOnlyList<Vector3> points, IReadOnlyList<Vector3> normals)
        {
            var b = Current();
            if (b.Quads + points.Count > QuadsPerBatch) { _current = null; b = Current(); }
            Path(b, points, normals, TapeStripWidth, KindTape, TapeColor);
            Count++;
        }

        /// <summary>A drag mark on the floor (Atmosphere.DarkChanges, 2026-11-04): a faint chalk-kind strip along a path
        /// (broken by the grain like chalk), the colour of crushed carpet pile or a scuff on concrete. Saved as a mark.</summary>
        public void AddScrape(IReadOnlyList<Vector3> points, float width, Color32 col)
        {
            if (points == null || points.Count < 2) return;
            var b = Current();
            if (b.Quads + points.Count > QuadsPerBatch) { _current = null; b = Current(); }
            var normals = new List<Vector3>(points.Count);
            for (int i = 0; i < points.Count; i++) normals.Add(Vector3.up);
            Path(b, points, normals, width, KindChalk, col);
            Count++;
        }

        /// <summary>The strip being pulled off a roll, as a path.</summary>
        public void SetPreview(IReadOnlyList<Vector3> points, IReadOnlyList<Vector3> normals)
        {
            if (_preview == null) _preview = NewBatch("MarkPreview", transform);
            _preview.Clear();
            Path(_preview, points, normals, TapeStripWidth, KindTape, TapeColor);
            _preview.go.SetActive(true);
        }

        /// <summary>The strip being pulled off a roll right now (one at a time).</summary>
        public void SetPreview(Vector3 a, Vector3 b, Vector3 normal)
        {
            if (_preview == null) _preview = NewBatch("MarkPreview", transform);
            _preview.Clear();
            Strip(_preview, a, b, normal, TapeStripWidth, KindTape, TapeColor);
            _preview.go.SetActive(true);
        }

        public void ClearPreview()
        {
            if (_preview == null) return;
            _preview.Clear();
            _preview.go.SetActive(false);
        }

        void LateUpdate()
        {
            foreach (var b in _batches) if (b.dirty) Upload(b);
            if (_preview != null && _preview.dirty) Upload(_preview);
        }

        // ---------------- Saving ----------------
        // Per strip: kind, colour, and three corners (start-left, start-right, end-left), as laid.

        // Version 2: every quad's four corners, and where along its strip it runs (a strip can be several quads now).
        const int SaveMagic = 0x4B524D42;     // "BMRK"
        const int SaveVersion = 2;

        public static void WriteEmpty(System.IO.BinaryWriter w) { w.Write(SaveMagic); w.Write(SaveVersion); w.Write(0); }

        public void Write(System.IO.BinaryWriter w)
        {
            int count = 0;
            foreach (var b in _batches) count += b.Quads;
            w.Write(SaveMagic); w.Write(SaveVersion); w.Write(count);
            foreach (var b in _batches)
                for (int q = 0; q < b.Quads; q++)
                {
                    int s = q * 4;
                    w.Write((byte)Mathf.RoundToInt(b.uv[s].z));
                    var c = b.c[s]; w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
                    Vec(w, b.v[s]); Vec(w, b.v[s + 1]); Vec(w, b.v[s + 2]); Vec(w, b.v[s + 3]);
                    w.Write(b.uv[s].y); w.Write(b.uv[s + 2].y); w.Write(b.uv[s].w);
                }
        }

        static void Vec(System.IO.BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 Vec(System.IO.BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        public void Read(System.IO.BinaryReader r)
        {
            if (r.BaseStream.Length < 12 || r.ReadInt32() != SaveMagic) return;
            int version = r.ReadInt32();
            if (version > SaveVersion) return;
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                float kind = r.ReadByte();
                var col = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                if (version == 1)
                {
                    Vector3 v0 = Vec(r), v1 = Vec(r), v3 = Vec(r);
                    Vector3 n = Vector3.Cross(v1 - v0, v3 - v0).normalized;
                    Corners(Current(), v0, v1, v3, kind, col, -n);
                }
                else
                {
                    Vector3 v0 = Vec(r), v1 = Vec(r), v2 = Vec(r), v3 = Vec(r);
                    float s0 = r.ReadSingle(), s1 = r.ReadSingle(), total = r.ReadSingle();
                    Vector3 n = Vector3.Cross(v3 - v0, v1 - v0).normalized;
                    Quad(Current(), v0, v1, v2, v3, s0, s1, total, kind, col, n);
                }
                Count++;
            }
        }
    }
}
