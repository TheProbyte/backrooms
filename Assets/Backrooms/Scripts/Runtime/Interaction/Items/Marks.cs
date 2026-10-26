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
            public readonly List<int> idx = new List<int>(QuadsPerBatch * 6);
            public bool dirty;
            public int Quads => v.Count / 4;
            public void Clear() { v.Clear(); uv.Clear(); c.Clear(); idx.Clear(); dirty = true; }
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
            for (int k = 0; k < 4; k++) b.c.Add(col);
            if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), normal) > 0f)
            { b.idx.Add(s); b.idx.Add(s + 1); b.idx.Add(s + 2); b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 3); }
            else { b.idx.Add(s); b.idx.Add(s + 2); b.idx.Add(s + 1); b.idx.Add(s); b.idx.Add(s + 3); b.idx.Add(s + 2); }
            b.dirty = true;
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

        const int SaveMagic = 0x4B524D42;     // "BMRK"
        const int SaveVersion = 1;

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
                    Vec(w, b.v[s]); Vec(w, b.v[s + 1]); Vec(w, b.v[s + 3]);
                }
        }

        static void Vec(System.IO.BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 Vec(System.IO.BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        public void Read(System.IO.BinaryReader r)
        {
            if (r.BaseStream.Length < 12 || r.ReadInt32() != SaveMagic) return;
            if (r.ReadInt32() > SaveVersion) return;
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                float kind = r.ReadByte();
                var col = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                Vector3 v0 = Vec(r), v1 = Vec(r), v3 = Vec(r);
                Vector3 n = Vector3.Cross(v1 - v0, v3 - v0).normalized;
                Corners(Current(), v0, v1, v3, kind, col, -n);
                Count++;
            }
        }
    }
}
