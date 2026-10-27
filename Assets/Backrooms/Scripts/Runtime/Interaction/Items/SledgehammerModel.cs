using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The sledgehammer's model, made in code: a 6 lb (2.7 kg) head on a 24 inch hickory handle. The handle is oval (deeper
    /// in the swing than across), swells into a knob at the butt and toward the head; the head is a chamfered steel block
    /// painted black, its two striking faces bevelled and worn bright. Frame: the handle's butt at the origin, the handle
    /// up +Y, the head's faces along +Z and -Z.
    /// </summary>
    public static class SledgehammerModel
    {
        public const float HeadY = 0.625f, HeadHalfX = 0.0325f, HeadHalfY = 0.0325f, HeadHalfZ = 0.0825f;
        public const float HandleA = 0.0135f, HandleB = 0.0185f;       // the handle's half widths across (x) and in the swing (z)
        static Mesh s_mesh;

        public static Mesh Mesh => s_mesh ? s_mesh : (s_mesh = Build());

        public static Material[] Materials => new[]
        {
            Destruction.DemolitionMaterials.HammerHandle, Destruction.DemolitionMaterials.HammerHead, Destruction.DemolitionMaterials.HammerFace,
        };

        static Mesh Build()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var sub = new[] { new List<int>(), new List<int>(), new List<int>() };
            void Tri(int s, int a, int b, int c) { sub[s].Add(a); sub[s].Add(b); sub[s].Add(c); }

            // ---- Handle: rings of an oval along y, smooth.
            (float y, float k)[] profile =
            {
                (0f, 0.78f), (0.005f, 1.1f), (0.016f, 1.18f), (0.032f, 1.12f), (0.06f, 1.0f), (0.2f, 0.95f), (0.38f, 0.96f),
                (0.5f, 1.03f), (0.585f, 1.12f), (0.59f, 0.86f), (HeadY + HeadHalfY + 0.002f, 0.86f),
            };
            const int seg = 20;
            int ring0 = v.Count;
            for (int r = 0; r < profile.Length; r++)
            {
                var (y, k) = profile[r];
                for (int i = 0; i <= seg; i++)
                {
                    float t = i / (float)seg * Mathf.PI * 2f;
                    float a = HandleA * k, b = HandleB * k;
                    v.Add(new Vector3(Mathf.Cos(t) * a, y, Mathf.Sin(t) * b));
                    n.Add(new Vector3(Mathf.Cos(t) / a, 0f, Mathf.Sin(t) / b).normalized);
                    uv.Add(new Vector2(i / (float)seg * 0.12f, y * 1.6f));
                }
            }
            for (int r = 0; r < profile.Length - 1; r++)
            for (int i = 0; i < seg; i++)
            {
                int a = ring0 + r * (seg + 1) + i, b = a + 1, c = a + seg + 1, d = c + 1;
                Tri(0, a, c, b); Tri(0, b, c, d);
            }
            // Butt and top caps.
            void Cap(float y, float k, bool up)
            {
                int centre = v.Count;
                v.Add(new Vector3(0f, y, 0f)); n.Add(up ? Vector3.up : Vector3.down); uv.Add(new Vector2(0.06f, y));
                for (int i = 0; i <= seg; i++)
                {
                    float t = i / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(t) * HandleA * k, y, Mathf.Sin(t) * HandleB * k)); n.Add(up ? Vector3.up : Vector3.down);
                    uv.Add(new Vector2(0.06f + Mathf.Cos(t) * 0.02f, y + Mathf.Sin(t) * 0.02f));
                }
                for (int i = 0; i < seg; i++) { if (up) Tri(0, centre, centre + 2 + i, centre + 1 + i); else Tri(0, centre, centre + 1 + i, centre + 2 + i); }
            }
            Cap(0f, 0.78f, false);
            Cap(HeadY + HeadHalfY + 0.002f, 0.86f, true);

            // ---- Head: an octagonal (chamfered) block along z, its faces bevelled.
            const float chamfer = 0.006f, bevel = 0.004f;
            var oct = new Vector2[8];
            {
                float x = HeadHalfX, y = HeadHalfY;
                float c = chamfer;
                oct[0] = new Vector2(x - c, y); oct[1] = new Vector2(x, y - c); oct[2] = new Vector2(x, -(y - c)); oct[3] = new Vector2(x - c, -y);
                oct[4] = new Vector2(-(x - c), -y); oct[5] = new Vector2(-x, -(y - c)); oct[6] = new Vector2(-x, y - c); oct[7] = new Vector2(-(x - c), y);
            }
            Vector3 P(Vector2 q, float z, float inset)
            {
                Vector2 d = q.normalized;
                return new Vector3(q.x - d.x * inset, HeadY + q.y - d.y * inset, z);
            }
            void Quad(int s, Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 normal)
            {
                int i0 = v.Count;
                v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                Vector2 U(Vector3 p) => Mathf.Abs(normal.x) > 0.5f ? new Vector2(p.z, p.y) : Mathf.Abs(normal.y) > 0.5f ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                uv.Add(U(a) * 4f); uv.Add(U(b) * 4f); uv.Add(U(cc) * 4f); uv.Add(U(d) * 4f);
                if (Vector3.Dot(Vector3.Cross(b - a, cc - a), normal) >= 0f) { Tri(s, i0, i0 + 1, i0 + 2); Tri(s, i0, i0 + 2, i0 + 3); }
                else { Tri(s, i0, i0 + 2, i0 + 1); Tri(s, i0, i0 + 3, i0 + 2); }
            }
            float z0 = -HeadHalfZ + bevel, z1 = HeadHalfZ - bevel;
            for (int i = 0; i < 8; i++)
            {
                Vector2 a = oct[i], b = oct[(i + 1) % 8];
                Vector2 mid = (a + b) * 0.5f;
                Vector3 nrm = new Vector3(mid.x, mid.y, 0f).normalized;
                // Sides: paint, except the narrow chamfers (worn to bare steel along the edges).
                bool worn = i % 2 == 0;
                Quad(worn ? 2 : 1, P(a, z0, 0f), P(b, z0, 0f), P(b, z1, 0f), P(a, z1, 0f), nrm);
                // Bevels round the faces (bright).
                for (int e = -1; e <= 1; e += 2)
                {
                    float zi = e < 0 ? z0 : z1, zo = e < 0 ? -HeadHalfZ : HeadHalfZ;
                    Vector3 bn = (nrm + Vector3.forward * e).normalized;
                    Quad(2, P(a, zi, 0f), P(b, zi, 0f), P(b, zo, bevel), P(a, zo, bevel), bn);
                }
            }
            // The striking faces.
            for (int e = -1; e <= 1; e += 2)
            {
                float z = e * HeadHalfZ;
                int centre = v.Count;
                v.Add(new Vector3(0f, HeadY, z)); n.Add(Vector3.forward * e); uv.Add(new Vector2(0f, 0f));
                for (int i = 0; i < 8; i++) { v.Add(P(oct[i], z, bevel)); n.Add(Vector3.forward * e); uv.Add(oct[i] * 4f); }
                for (int i = 0; i < 8; i++)
                {
                    int a = centre + 1 + i, b = centre + 1 + (i + 1) % 8;
                    // The octagon runs clockwise in x-y (as seen from +z), so the +z face takes it reversed.
                    if (e > 0) Tri(2, centre, b, a); else Tri(2, centre, a, b);
                }
            }

            var mesh = new Mesh { name = "Sledgehammer", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) mesh.SetTriangles(sub[s], s, true);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
