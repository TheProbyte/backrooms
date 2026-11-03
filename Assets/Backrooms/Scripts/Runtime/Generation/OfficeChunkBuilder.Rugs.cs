using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A children's road rug on the floor (PropKind.Rug, placed by DistrictGenerator.PlaceRugs), built into the chunk's
    /// meshes like the carpet's own lifted edges:
    ///  - the printed top (MaterialMaps.PlayRug: the owner's tileable road map, one repeat = 2.7 x 1.8 m, so its roads
    ///    are about 8 cm wide; each rug starts somewhere else in it), 7 mm above the floor (low pile on a rubber backing);
    ///  - the bound edge all round: overlocked yarn (MaterialMaps.RugBindings, navy, charcoal or green) 18 mm wide, a little
    ///    proud of the pile, rolling over the edge and tucked under, round at the corners;
    ///  - now and then one corner come up 2-4.5 cm off the floor (kicked, or curled from being rolled), its backing showing.
    /// Its collider is a thin box (named "Rug": footsteps and dropped things sound of carpet on it). It never moves.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Rugs' colliders: chunk-local centre, size, yaw.</summary>
        readonly List<(Vector3 c, Vector3 s, float yaw)> _rugs = new List<(Vector3, Vector3, float)>();

        const float RugBinding = 0.018f, RugPrintU = 2.7f, RugPrintV = 1.8f, RugBindingRepeat = 0.05f;

        /// <summary>The binding's cross-section: (out from the pile's edge, in binding widths; height, m; with h the rug's top).</summary>
        static Vector2[] RugProfile(float h) => new[]
        {
            new Vector2(0f, h), new Vector2(0.18f, h + 0.0012f), new Vector2(0.45f, h + 0.0017f), new Vector2(0.7f, h + 0.0011f),
            new Vector2(0.86f, h * 0.66f), new Vector2(0.96f, h * 0.28f), new Vector2(1f, 0.0004f), new Vector2(0.95f, 0f),
            new Vector2(0.6f, 0f),
        };

        void Rug(Prop p, Vector3 at, Quaternion rot)
        {
            var print = MaterialMaps.PlayRug;
            if (!print || MaterialMaps.RugBindings.Length == 0) return;
            float L = p.size.x, Wd = p.size.z, h = p.size.y > 0.001f ? p.size.y : DistrictGenerator.RugThickness;
            var rng = new System.Random(p.variant);
            float a = L * 0.5f - RugBinding, b = Wd * 0.5f - RugBinding;   // the pile's half size, inside the binding
            float ou = (float)rng.NextDouble(), ov = (float)rng.NextDouble();
            bool turned = rng.NextDouble() < 0.5;                          // the print the other way round (never mirrored)
            var bindMat = MaterialMaps.RugBindings[rng.Next(MaterialMaps.RugBindings.Length)];
            int curl = rng.NextDouble() < 0.3 ? rng.Next(4) : -1;
            float curlR = 0.16f + 0.14f * (float)rng.NextDouble(), curlH = 0.02f + 0.025f * (float)rng.NextDouble();
            float sx = (curl & 1) == 0 ? -1f : 1f, sz = (curl & 2) == 0 ? -1f : 1f;
            float reach = a + b - curlR;                                   // (fully up at the pile's corner, the binding with it)

            // The corner come up: rising across a diagonal line near it, bending more toward the tip.
            float Lift(float x, float z)
            {
                if (curl < 0) return 0f;
                float t = Mathf.Clamp01((x * sx + z * sz - reach) / curlR);
                return curlH * t * t;
            }
            Vector3 LiftNormal(float x, float z)
            {
                const float e = 0.005f;
                float dx = (Lift(x + e, z) - Lift(x - e, z)) / (2f * e), dz = (Lift(x, z + e) - Lift(x, z - e)) / (2f * e);
                return new Vector3(-dx, 1f, -dz).normalized;
            }
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);
            Vector2 PrintUV(float x, float z)
            {
                float u = (x + L * 0.5f) / RugPrintU, v = (z + Wd * 0.5f) / RugPrintV;
                return turned ? new Vector2(ou - u, ov - v) : new Vector2(ou + u, ov + v);
            }

            uint mask = LayerBit(p.roomColor);
            var top = WMask(print, mask);
            var under = MaterialMaps.RugBacking ? WMask(MaterialMaps.RugBacking, mask) : null;
            var bind = WMask(bindMat, mask);

            // The top: one quad when it lies flat, a grid where a corner is up.
            float step = curl >= 0 ? 0.05f : 10f;
            int nx = Mathf.Max(1, Mathf.CeilToInt(2f * a / step)), nz = Mathf.Max(1, Mathf.CeilToInt(2f * b / step));
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float xa = -a + 2f * a * i / nx, xb = -a + 2f * a * (i + 1) / nx;
                float za = -b + 2f * b * j / nz, zb = -b + 2f * b * (j + 1) / nz;
                float l00 = Lift(xa, za), l10 = Lift(xb, za), l11 = Lift(xb, zb), l01 = Lift(xa, zb);
                top.SmoothQuad(P(xa, h + l00, za), P(xb, h + l10, za), P(xb, h + l11, zb), P(xa, h + l01, zb),
                    rot * LiftNormal(xa, za), rot * LiftNormal(xb, za), rot * LiftNormal(xb, zb), rot * LiftNormal(xa, zb),
                    PrintUV(xa, za), PrintUV(xb, za), PrintUV(xb, zb), PrintUV(xa, zb));
                // Its backing where it is off the floor.
                if (under != null && Mathf.Max(Mathf.Max(l00, l10), Mathf.Max(l11, l01)) > 0.0004f)
                    under.SmoothQuad(P(xa, l00, za), P(xb, l10, za), P(xb, l11, zb), P(xa, l01, zb),
                        -(rot * LiftNormal(xa, za)), -(rot * LiftNormal(xb, za)), -(rot * LiftNormal(xb, zb)), -(rot * LiftNormal(xa, zb)),
                        new Vector2(xa, za), new Vector2(xb, za), new Vector2(xb, zb), new Vector2(xa, zb));
            }

            // The binding: the profile swept round the pile's edge (straight sides, a quarter turn about each corner).
            var path = new List<(Vector2 q, Vector2 o, float d)>();
            var corners = new[] { new Vector2(-a, -b), new Vector2(a, -b), new Vector2(a, b), new Vector2(-a, b) };
            var outs = new[] { new Vector2(0f, -1f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f) };
            float dist = 0f;
            path.Add((corners[0], outs[0], 0f));
            for (int k = 0; k < 4; k++)
            {
                Vector2 c0 = corners[k], c1 = corners[(k + 1) & 3];
                float len = (c1 - c0).magnitude;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.05f));
                for (int i = 1; i <= n; i++) path.Add((Vector2.Lerp(c0, c1, i / (float)n), outs[k], dist + len * i / n));
                dist += len;
                float a0 = Mathf.Atan2(outs[k].y, outs[k].x), da = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, Mathf.Atan2(outs[(k + 1) & 3].y, outs[(k + 1) & 3].x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                const int arc = 5;
                for (int j = 1; j <= arc; j++)
                {
                    float t = j / (float)arc, ang = a0 + da * t;
                    path.Add((c1, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), dist + RugBinding * Mathf.Abs(da) * t));
                }
                dist += RugBinding * Mathf.PI * 0.5f;
            }
            var prof = RugProfile(h);
            int np = prof.Length;
            // The profile's normals (out, up), from its neighbours.
            var pn = new Vector2[np];
            for (int j = 0; j < np; j++)
            {
                var tng = prof[Mathf.Min(j + 1, np - 1)] - prof[Mathf.Max(j - 1, 0)];
                tng.x *= RugBinding;
                pn[j] = new Vector2(tng.y, -tng.x).normalized * -1f;    // (-dy, ds): up on the top, out down the edge, down below
            }
            Vector3 Pt(int i, int j, out Vector3 n)
            {
                var (q, o, _) = path[i];
                float x = q.x + o.x * prof[j].x * RugBinding, z = q.y + o.y * prof[j].x * RugBinding;
                n = rot * (new Vector3(o.x, 0f, o.y) * pn[j].x + Vector3.up * pn[j].y).normalized;
                return P(x, prof[j].y + Lift(x, z), z);
            }
            for (int i = 0; i + 1 < path.Count; i++)
            {
                float u0 = path[i].d / RugBindingRepeat, u1 = path[i + 1].d / RugBindingRepeat;
                for (int j = 0; j + 1 < np; j++)
                {
                    var A = Pt(i, j, out var na); var B = Pt(i + 1, j, out var nb);
                    var Cc = Pt(i + 1, j + 1, out var nc); var D = Pt(i, j + 1, out var nd);
                    float v0 = j / (float)(np - 1), v1 = (j + 1) / (float)(np - 1);
                    bind.SmoothQuad(A, B, Cc, D, na, nb, nc, nd, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
                }
            }

            _rugs.Add((at + Vector3.up * (h * 0.5f), new Vector3(L, h, Wd), p.yaw));
        }

        /// <summary>A point on the floor is on (or within r of) a rug.</summary>
        bool OnRug(Vector3 p, float r)
        {
            foreach (var (c, s, yaw) in _rugs)
            {
                var l = Quaternion.Euler(0f, -yaw, 0f) * (p - c);
                if (Mathf.Abs(l.x) < s.x * 0.5f + r && Mathf.Abs(l.z) < s.z * 0.5f + r) return true;
            }
            return false;
        }

        void InstantiateRugs(Transform root)
        {
            foreach (var (c, s, yaw) in _rugs)
            {
                var go = new GameObject("Rug");
                go.transform.SetParent(root, false);
                go.transform.localPosition = c;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                go.AddComponent<BoxCollider>().size = s;
            }
        }
    }
}
