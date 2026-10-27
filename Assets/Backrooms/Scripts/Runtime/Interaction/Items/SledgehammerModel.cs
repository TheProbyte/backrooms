using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The sledgehammer's model, made in code: a short-handled one (a club or drilling hammer, about 43 cm long), a 4 lb
    /// forged head on a hickory handle, after a photograph of an old one. The head is a long block with chamfered edges, a
    /// little fuller in the middle round the eye, its ends stepped down and bevelled into the two striking faces, worn
    /// bright; the rest painted black and worn. The handle is oval (deeper in the swing than across), slimmer in the middle,
    /// swelling into a rounded knob at the butt and a little toward the head. Frame: the handle's butt at the origin, the
    /// handle up +Y, the head's faces along +Z and -Z.
    /// </summary>
    public static class SledgehammerModel
    {
        public const float HeadY = 0.4f, HeadHalfX = 0.0245f, HeadHalfY = 0.0275f, HeadHalfZ = 0.06f;
        public const float HandleA = 0.0135f, HandleB = 0.0175f;       // the handle's half widths across (x) and in the swing (z)
        public const float Length = HeadY + HeadHalfY + 0.003f;        // butt to the top of the head
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

            // ---- Handle: rings of an oval along y, smooth. (height, size)
            float top = HeadY + HeadHalfY + 0.003f;
            (float y, float k)[] profile =
            {
                (0f, 0.62f), (0.004f, 1.02f), (0.012f, 1.2f), (0.026f, 1.18f), (0.05f, 1.05f), (0.09f, 0.95f), (0.2f, 0.9f),
                (0.3f, 0.95f), (0.35f, 1.04f), (HeadY - HeadHalfY - 0.002f, 1.08f), (HeadY - HeadHalfY, 0.9f), (top, 0.9f),
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
            // The butt (rounded over) and the top of the handle in the eye (end grain, with its wedge).
            void Cap(float y, float k, bool up, float dome)
            {
                int centre = v.Count;
                v.Add(new Vector3(0f, y + (up ? dome : -dome), 0f)); n.Add(up ? Vector3.up : Vector3.down); uv.Add(new Vector2(0.06f, y));
                for (int i = 0; i <= seg; i++)
                {
                    float t = i / (float)seg * Mathf.PI * 2f;
                    var rim = new Vector3(Mathf.Cos(t) * HandleA * k, y, Mathf.Sin(t) * HandleB * k);
                    v.Add(rim); n.Add((rim.normalized * 0.6f + (up ? Vector3.up : Vector3.down)).normalized);
                    uv.Add(new Vector2(0.06f + Mathf.Cos(t) * 0.02f, y + Mathf.Sin(t) * 0.02f));
                }
                for (int i = 0; i < seg; i++) { if (up) Tri(0, centre, centre + 2 + i, centre + 1 + i); else Tri(0, centre, centre + 1 + i, centre + 2 + i); }
            }
            Cap(0f, 0.62f, false, 0.004f);
            Cap(top, 0.9f, true, 0f);

            // ---- Head: an octagonal (chamfered) block along z, fuller round the eye, its ends stepped down and bevelled.
            // Stations along z: (z, grow: added to the half widths, slot: 1 paint / 2 bright).
            const float chamfer = 0.0065f;
            (float z, float grow, int slot)[] st =
            {
                (-HeadHalfZ, -0.0065f, 2), (-HeadHalfZ + 0.0055f, 0f, 2), (-HeadHalfZ + 0.011f, 0f, 1), (-0.03f, 0f, 1), (-0.025f, 0.0018f, 1),
                (0.025f, 0.0018f, 1), (0.03f, 0f, 1), (HeadHalfZ - 0.011f, 0f, 1), (HeadHalfZ - 0.0055f, 0f, 2), (HeadHalfZ, -0.0065f, 2),
            };
            Vector2[] Oct(float grow)
            {
                float x = HeadHalfX + grow, y = HeadHalfY + grow, c = chamfer + grow * 0.5f;
                return new[]
                {
                    new Vector2(x - c, y), new Vector2(x, y - c), new Vector2(x, -(y - c)), new Vector2(x - c, -y),
                    new Vector2(-(x - c), -y), new Vector2(-x, -(y - c)), new Vector2(-x, y - c), new Vector2(-(x - c), y),
                };
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
            Vector3 At(Vector2 q, float z) => new Vector3(q.x, HeadY + q.y, z);
            for (int k = 0; k < st.Length - 1; k++)
            {
                var o0 = Oct(st[k].grow); var o1 = Oct(st[k + 1].grow);
                float z0 = st[k].z, z1 = st[k + 1].z;
                // The bevels into the faces and the steps are bright; the body is paint, its narrow chamfers worn to bare steel.
                for (int i = 0; i < 8; i++)
                {
                    int j = (i + 1) % 8;
                    Vector3 a = At(o0[i], z0), b = At(o0[j], z0), c = At(o1[j], z1), d = At(o1[i], z1);
                    Vector3 nrm = Vector3.Cross(b - a, d - a);
                    Vector3 mid = (a + b + c + d) * 0.25f - new Vector3(0f, HeadY, (z0 + z1) * 0.5f);
                    if (Vector3.Dot(nrm, mid) < 0f) nrm = -nrm;
                    bool bevel = st[k].slot == 2 && st[k + 1].slot == 2;
                    Quad(bevel || i % 2 == 0 ? 2 : 1, a, b, c, d, nrm.normalized);
                }
            }
            // The striking faces.
            for (int e = -1; e <= 1; e += 2)
            {
                float z = e * HeadHalfZ;
                var o = Oct(-0.0065f);
                int centre = v.Count;
                v.Add(new Vector3(0f, HeadY, z)); n.Add(Vector3.forward * e); uv.Add(new Vector2(0f, 0f));
                for (int i = 0; i < 8; i++) { v.Add(At(o[i], z)); n.Add(Vector3.forward * e); uv.Add(o[i] * 4f); }
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
