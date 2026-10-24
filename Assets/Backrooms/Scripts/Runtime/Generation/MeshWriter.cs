using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// Accumulates quads into one mesh. Winding is derived from the supplied normal, and tangents are
    /// computed per quad from its UVs, so callers only describe geometry. UVs come from a projector so
    /// textures stay continuous across cells and chunks (world-space mapping).
    /// </summary>
    public class MeshWriter
    {
        public delegate Vector2 UVProjector(Vector3 world, Vector3 normal);

        readonly List<Vector3> _v = new List<Vector3>(1024);
        readonly List<Vector3> _n = new List<Vector3>(1024);
        readonly List<Vector4> _t = new List<Vector4>(1024);
        readonly List<Vector2> _uv = new List<Vector2>(1024);
        readonly List<int> _i = new List<int>(1536);

        public Vector3 worldOffset;   // added to local positions before UV projection
        public int VertexCount => _v.Count;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, UVProjector uv)
        {
            Quad(a, b, c, d, normal, uv(a + worldOffset, normal), uv(b + worldOffset, normal), uv(c + worldOffset, normal), uv(d + worldOffset, normal));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int s = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            for (int k = 0; k < 4; k++) _n.Add(normal);

            // Tangent from UV gradient across edges ab and ad.
            Vector3 e1 = b - a, e2 = d - a;
            Vector2 d1 = ub - ua, d2 = ud - ua;
            float det = d1.x * d2.y - d2.x * d1.y;
            Vector3 tan = Mathf.Abs(det) > 1e-8f ? (e1 * d2.y - e2 * d1.y) / det : Vector3.Cross(normal, Vector3.up);
            if (tan.sqrMagnitude < 1e-8f) tan = Vector3.Cross(normal, Vector3.forward);
            Vector3 bit = Mathf.Abs(det) > 1e-8f ? (e2 * d1.x - e1 * d2.x) / det : Vector3.Cross(normal, tan);
            tan = (tan - normal * Vector3.Dot(normal, tan)).normalized;
            float w = Vector3.Dot(Vector3.Cross(normal, tan), bit) < 0f ? -1f : 1f;
            var t4 = new Vector4(tan.x, tan.y, tan.z, w);
            for (int k = 0; k < 4; k++) _t.Add(t4);

            bool front = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f;
            if (front) { _i.Add(s); _i.Add(s + 1); _i.Add(s + 2); _i.Add(s); _i.Add(s + 2); _i.Add(s + 3); }
            else { _i.Add(s); _i.Add(s + 2); _i.Add(s + 1); _i.Add(s); _i.Add(s + 3); _i.Add(s + 2); }
        }

        /// <summary>A quad with its own normal at each corner (smooth surfaces such as the curved underside of an arch) and explicit UVs.</summary>
        public void SmoothQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int s = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            _n.Add(na); _n.Add(nb); _n.Add(nc); _n.Add(nd);
            Vector3 avg = (na + nb + nc + nd).normalized;
            Vector3 e1 = b - a, e2 = d - a;
            Vector2 d1 = ub - ua, d2 = ud - ua;
            float det = d1.x * d2.y - d2.x * d1.y;
            Vector3 tan = Mathf.Abs(det) > 1e-9f ? (e1 * d2.y - e2 * d1.y) / det : e1;
            Vector3 bit = Mathf.Abs(det) > 1e-9f ? (e2 * d1.x - e1 * d2.x) / det : e2;
            for (int k = 0; k < 4; k++)
            {
                Vector3 nk = k == 0 ? na : k == 1 ? nb : k == 2 ? nc : nd;
                Vector3 tk = (tan - nk * Vector3.Dot(nk, tan)).normalized;
                if (tk.sqrMagnitude < 1e-8f) tk = Vector3.Cross(nk, Vector3.up).normalized;
                float w = Vector3.Dot(Vector3.Cross(nk, tk), bit) < 0f ? -1f : 1f;
                _t.Add(new Vector4(tk.x, tk.y, tk.z, w));
            }
            bool front = Vector3.Dot(Vector3.Cross(b - a, c - a), avg) > 0f;
            if (front) { _i.Add(s); _i.Add(s + 1); _i.Add(s + 2); _i.Add(s); _i.Add(s + 2); _i.Add(s + 3); }
            else { _i.Add(s); _i.Add(s + 2); _i.Add(s + 1); _i.Add(s); _i.Add(s + 3); _i.Add(s + 2); }
        }

        /// <summary>Faces of an oriented box. faceMask bits: 1 -X, 2 +X, 4 -Y, 8 +Y, 16 -Z, 32 +Z.</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rot, int faceMask, UVProjector uv)
        {
            Vector3 h = size * 0.5f;
            Vector3 ax = rot * Vector3.right, ay = rot * Vector3.up, az = rot * Vector3.forward;
            void F(int bit, Vector3 n, Vector3 u, Vector3 v, float dn, float du, float dv)
            {
                if ((faceMask & bit) == 0) return;
                Vector3 c = center + n * dn;
                Quad(c - u * du - v * dv, c + u * du - v * dv, c + u * du + v * dv, c - u * du + v * dv, n, uv);
            }
            F(1, -ax, az, ay, h.x, h.z, h.y);
            F(2, ax, az, ay, h.x, h.z, h.y);
            F(4, -ay, ax, az, h.y, h.x, h.z);
            F(8, ay, ax, az, h.y, h.x, h.z);
            F(16, -az, ax, ay, h.z, h.x, h.y);
            F(32, az, ax, ay, h.z, h.x, h.y);
        }

        public void Box(Vector3 center, Vector3 size, int faceMask, UVProjector uv) => Box(center, size, Quaternion.identity, faceMask, uv);

        /// <summary>Open-ended frustum (lamp shade): ring of quads between two radii.</summary>
        public void Frustum(Vector3 baseCenter, float rBottom, float rTop, float height, int sides, bool outward, UVProjector uv)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 p0 = baseCenter + d0 * rBottom, p1 = baseCenter + d1 * rBottom;
                Vector3 p2 = baseCenter + d1 * rTop + Vector3.up * height, p3 = baseCenter + d0 * rTop + Vector3.up * height;
                Vector3 mid = (d0 + d1).normalized;
                float slope = (rBottom - rTop) / height;
                Vector3 n = (mid + Vector3.up * slope).normalized * (outward ? 1f : -1f);
                Quad(p0, p1, p2, p3, n, uv);
            }
        }

        /// <summary>Flat polygon (triangle fan written as degenerate quads).</summary>
        public void Disc(Vector3 center, float radius, int sides, Vector3 normal, UVProjector uv)
        {
            Vector3 t = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 b = Vector3.Cross(normal, t);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = center + (t * Mathf.Cos(a0) + b * Mathf.Sin(a0)) * radius;
                Vector3 p1 = center + (t * Mathf.Cos(a1) + b * Mathf.Sin(a1)) * radius;
                Quad(center, p0, p1, p1, normal, uv);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(_v); m.SetNormals(_n); m.SetTangents(_t); m.SetUVs(0, _uv);
            m.SetTriangles(_i, 0, true);
            m.UploadMeshData(true); // free the CPU copy; chunks are static
            return m;
        }

        // ---------- Common projectors ----------

        /// <summary>Wallpaper: U along the wall in 1.2 m repeats, V = height over the 2.7 m wall.</summary>
        public static Vector2 WallUV(Vector3 w, Vector3 n)
        {
            if (Mathf.Abs(n.y) > 0.5f) return new Vector2(w.x / OfficeBiome.CellSize, w.z / OfficeBiome.CellSize);
            float along = Mathf.Abs(n.x) > Mathf.Abs(n.z) ? w.z * -Mathf.Sign(n.x) : w.x * Mathf.Sign(n.z);
            return new Vector2(along / OfficeBiome.CellSize, w.y / OfficeBiome.CeilingHeight);
        }

        public static Vector2 FloorUV(Vector3 w, Vector3 n) => new Vector2(w.x / 3f, w.z / 3f);   // one carpet texture repeat = 3 m
        public static Vector2 CeilingUV(Vector3 w, Vector3 n) => new Vector2(w.x / 2.4f, (w.z - OfficeBiome.TileGridOffset) / 2.4f);

        /// <summary>Box mapping in metres (1 UV unit = 1 m) for trim, fabric and wood.</summary>
        public static Vector2 MetreUV(Vector3 w, Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az) return new Vector2(w.x, w.z);
            if (ax >= az) return new Vector2(w.z, w.y);
            return new Vector2(w.x, w.y);
        }
    }
}
