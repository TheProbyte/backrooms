using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Destruction
{
    /// <summary>
    /// Builds a mesh of several materials from triangles given in world space (less an origin). Winding comes from the
    /// normal, tangents from the texture coordinates (as MeshWriter does), so callers only describe the shapes.
    /// </summary>
    public sealed class PanelMesh
    {
        public readonly List<Vector3> v = new List<Vector3>(4096);
        readonly List<Vector3> _n = new List<Vector3>(4096);
        readonly List<Vector4> _t = new List<Vector4>(4096);
        readonly List<Vector2> _uv = new List<Vector2>(4096);
        readonly List<int>[] _sub;
        public Vector3 origin;

        public PanelMesh(int slots) { _sub = new List<int>[slots]; for (int i = 0; i < slots; i++) _sub[i] = new List<int>(1024); }

        public int Slots => _sub.Length;
        public bool Empty => v.Count == 0;

        public void Clear()
        {
            v.Clear(); _n.Clear(); _t.Clear(); _uv.Clear();
            foreach (var s in _sub) s.Clear();
        }

        public void Tri(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            Vector3 e1 = b - a, e2 = c - a;
            Vector2 d1 = ub - ua, d2 = uc - ua;
            float det = d1.x * d2.y - d2.x * d1.y;
            Vector3 tan = Mathf.Abs(det) > 1e-10f ? (e1 * d2.y - e2 * d1.y) / det : Vector3.Cross(normal, Vector3.up);
            if (tan.sqrMagnitude < 1e-10f) tan = Vector3.Cross(normal, Vector3.forward);
            Vector3 bit = Mathf.Abs(det) > 1e-10f ? (e2 * d1.x - e1 * d2.x) / det : Vector3.Cross(normal, tan);
            tan = (tan - normal * Vector3.Dot(normal, tan)).normalized;
            float w = Vector3.Dot(Vector3.Cross(normal, tan), bit) < 0f ? -1f : 1f;
            var t4 = new Vector4(tan.x, tan.y, tan.z, w);
            int s = v.Count;
            v.Add(a - origin); v.Add(b - origin); v.Add(c - origin);
            _n.Add(normal); _n.Add(normal); _n.Add(normal);
            _t.Add(t4); _t.Add(t4); _t.Add(t4);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc);
            var idx = _sub[slot];
            if (Vector3.Dot(Vector3.Cross(e1, e2), normal) >= 0f) { idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); }
            else { idx.Add(s); idx.Add(s + 2); idx.Add(s + 1); }
        }

        public void Quad(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Tri(slot, a, b, c, normal, ua, ub, uc);
            Tri(slot, a, c, d, normal, ua, uc, ud);
        }

        public delegate Vector2 Projector(Vector3 world, Vector3 normal);

        public void Quad(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Projector uv) =>
            Quad(slot, a, b, c, d, normal, uv(a, normal), uv(b, normal), uv(c, normal), uv(d, normal));

        /// <summary>A world axis-aligned box. Face bits: 1 -X, 2 +X, 4 -Y, 8 +Y, 16 -Z, 32 +Z.</summary>
        public void Box(int slot, Vector3 min, Vector3 max, int faces, Projector uv)
        {
            Vector3 P(float x, float y, float z) => new Vector3(x, y, z);
            if ((faces & 1) != 0) Quad(slot, P(min.x, min.y, min.z), P(min.x, min.y, max.z), P(min.x, max.y, max.z), P(min.x, max.y, min.z), Vector3.left, uv);
            if ((faces & 2) != 0) Quad(slot, P(max.x, min.y, min.z), P(max.x, max.y, min.z), P(max.x, max.y, max.z), P(max.x, min.y, max.z), Vector3.right, uv);
            if ((faces & 4) != 0) Quad(slot, P(min.x, min.y, min.z), P(max.x, min.y, min.z), P(max.x, min.y, max.z), P(min.x, min.y, max.z), Vector3.down, uv);
            if ((faces & 8) != 0) Quad(slot, P(min.x, max.y, min.z), P(min.x, max.y, max.z), P(max.x, max.y, max.z), P(max.x, max.y, min.z), Vector3.up, uv);
            if ((faces & 16) != 0) Quad(slot, P(min.x, min.y, min.z), P(min.x, max.y, min.z), P(max.x, max.y, min.z), P(max.x, min.y, min.z), Vector3.back, uv);
            if ((faces & 32) != 0) Quad(slot, P(min.x, min.y, max.z), P(max.x, min.y, max.z), P(max.x, max.y, max.z), P(min.x, max.y, max.z), Vector3.forward, uv);
        }

        /// <summary>A box in a panel's frame (u, v, w), drawn in the world (walls are axis-aligned).</summary>
        public void LocalBox(int slot, WallKey k, float pitch, Vector3 min, Vector3 max, Projector uv, int faces = 63)
        {
            Vector3 a = k.ToWorld(min, pitch), b = k.ToWorld(max, pitch);
            Box(slot, Vector3.Min(a, b), Vector3.Max(a, b), faces, uv);
        }

        /// <summary>A round tube (cable, pipe) between two points.</summary>
        public void Tube(int slot, Vector3 a, Vector3 b, float radius, int sides, bool caps)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            axis /= len;
            Vector3 x = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, y = Vector3.Cross(axis, x);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 r0 = x * Mathf.Cos(a0) + y * Mathf.Sin(a0), r1 = x * Mathf.Cos(a1) + y * Mathf.Sin(a1);
                Vector3 n = (r0 + r1).normalized;
                float u0 = i / (float)sides, u1 = (i + 1) / (float)sides;
                Quad(slot, a + r0 * radius, b + r0 * radius, b + r1 * radius, a + r1 * radius, n,
                    new Vector2(u0 * 0.1f, 0f), new Vector2(u0 * 0.1f, len), new Vector2(u1 * 0.1f, len), new Vector2(u1 * 0.1f, 0f));
                if (caps)
                {
                    Tri(slot, a, a + r0 * radius, a + r1 * radius, -axis, Vector2.zero, Vector2.right * 0.01f, Vector2.up * 0.01f);
                    Tri(slot, b, b + r1 * radius, b + r0 * radius, axis, Vector2.zero, Vector2.right * 0.01f, Vector2.up * 0.01f);
                }
            }
        }

        /// <summary>Adds another mesh's triangles moved by a pose (rubble: a piece's own mesh where it came to rest).</summary>
        public void Append(PanelMesh o, Vector3 position, Quaternion rotation)
        {
            int start = v.Count;
            for (int i = 0; i < o.v.Count; i++)
            {
                v.Add(position + rotation * o.v[i] - origin);
                _n.Add(rotation * o._n[i]);
                var t = o._t[i]; var r = rotation * new Vector3(t.x, t.y, t.z);
                _t.Add(new Vector4(r.x, r.y, r.z, t.w));
                _uv.Add(o._uv[i]);
            }
            for (int s = 0; s < _sub.Length && s < o._sub.Length; s++)
                foreach (int i in o._sub[s]) _sub[s].Add(start + i);
        }

        /// <summary>Writes into a mesh (made if null); only the material slots in use become submeshes.</summary>
        public Mesh Apply(Mesh mesh, Material[] slotMaterials, out Material[] used, string name)
        {
            if (!mesh) mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.Clear();
            mesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(v); mesh.SetNormals(_n); mesh.SetTangents(_t); mesh.SetUVs(0, _uv);
            var mats = new List<Material>();
            int count = 0;
            for (int i = 0; i < _sub.Length; i++) if (_sub[i].Count > 0 && slotMaterials[i]) count++;
            mesh.subMeshCount = Mathf.Max(1, count);
            int k = 0;
            for (int i = 0; i < _sub.Length; i++)
            {
                if (_sub[i].Count == 0 || !slotMaterials[i]) continue;
                mesh.SetTriangles(_sub[i], k++, false);
                mats.Add(slotMaterials[i]);
            }
            mesh.RecalculateBounds();
            used = mats.ToArray();
            return mesh;
        }

        // ---------------- Projectors ----------------

        public static Vector2 Metre(Vector3 w, Vector3 n) => Generation.MeshWriter.MetreUV(w, n);
        /// <summary>Metres, but along the length for long upright parts (grain runs up a stud).</summary>
        public static Vector2 Upright(Vector3 w, Vector3 n)
        {
            if (Mathf.Abs(n.y) > 0.5f) return new Vector2(w.x, w.z);
            return Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector2(w.z * 2f, w.y * 0.5f) : new Vector2(w.x * 2f, w.y * 0.5f);
        }
    }
}
