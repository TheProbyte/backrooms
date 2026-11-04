using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Where each print on the box print atlas (Textures/BoxPrint_Atlas.png) sits, in UV space (origin bottom
    /// left). The texture generator draws them here and the box geometry samples them, so the two always agree.
    /// </summary>
    public static class BoxPrintAtlas
    {
        public static readonly Rect ShippingLabel = new Rect(0.008f, 0.62f, 0.292f, 0.37f);    // white label: logo bar, text lines, barcode
        public static readonly Rect AddressLabel = new Rect(0.318f, 0.62f, 0.195f, 0.37f);     // smaller label
        public static readonly Rect ThisSideUp = new Rect(0.53f, 0.62f, 0.195f, 0.37f);        // two arrows, black ink
        public static readonly Rect Fragile = new Rect(0.742f, 0.62f, 0.25f, 0.2f);            // red framed stamp
        public static readonly Rect Logo = new Rect(0.008f, 0.12f, 0.235f, 0.45f);             // stamped maker's mark
        public static readonly Rect SeamLine = new Rect(0.5f, 0.1f, 0.4f, 0.03f);             // dark gap between the top flaps
    }

    /// <summary>
    /// A realistic cardboard box built from quads: bevelled edges with smooth normals, a taped top seam that
    /// wraps down the ends, shipping labels and print marks. Pure data (no engine calls beyond maths), so it
    /// runs on the chunk builder's worker threads; the same code builds the loose movable boxes.
    /// Three writers because three materials: corrugated board (tiled), tape (plain, glossy), print (cutout atlas).
    /// </summary>
    public static class CardboardBoxGeometry
    {
        const float Bevel = 0.006f;

        /// <param name="foot">Centre of the box's bottom face, in the writers' local space.</param>
        /// <param name="size">Width (x), height (y), depth (z) before rotation.</param>
        public static void Build(MeshWriter body, MeshWriter tape, MeshWriter print, Vector3 foot, Vector3 size, float yawDegrees, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yawDegrees, 0f);
            Vector3 centre = foot + Vector3.up * (size.y * 0.5f);
            RoundedBox(body, centre, size * 0.5f, Bevel, rot, MeshWriter.MetreUV);

            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);
            Vector3 N(float x, float y, float z) => rot * new Vector3(x, y, z);
            float hx = size.x * 0.5f, hz = size.z * 0.5f, h = size.y;
            bool seamAlongX = size.x >= size.z;                       // the flaps meet along the longer top side
            const float lift = 0.0007f;                               // decals and tape sit just off the surface

            // ---- Top seam: taped (most boxes) or a dark gap between the flaps ----
            if (rng.NextDouble() < 0.88)
            {
                float tw = 0.024f;                                    // tape half width (48 mm tape)
                float len = seamAlongX ? hx : hz, wrapDown = Mathf.Min(0.11f, h * 0.45f);
                float trim = (float)rng.NextDouble() * 0.015f;        // tape not always flush at the ends
                if (seamAlongX)
                {
                    tape.Quad(P(-hx + trim, h + lift, -tw), P(hx - trim, h + lift, -tw), P(hx - trim, h + lift, tw), P(-hx + trim, h + lift, tw), N(0, 1, 0), MeshWriter.MetreUV);
                    for (int s = -1; s <= 1; s += 2)                  // down the two short ends
                    {
                        float x = s * (hx + lift);
                        tape.Quad(P(x, h, -tw), P(x, h, tw), P(x, h - wrapDown, tw), P(x, h - wrapDown, -tw), N(s, 0, 0), MeshWriter.MetreUV);
                    }
                }
                else
                {
                    tape.Quad(P(-tw, h + lift, -hz + trim), P(tw, h + lift, -hz + trim), P(tw, h + lift, hz - trim), P(-tw, h + lift, hz - trim), N(0, 1, 0), MeshWriter.MetreUV);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float z = s * (hz + lift);
                        tape.Quad(P(-tw, h, z), P(tw, h, z), P(tw, h - wrapDown, z), P(-tw, h - wrapDown, z), N(0, 0, s), MeshWriter.MetreUV);
                    }
                }
            }
            else
            {
                const float gw = 0.003f;                              // the gap between the flaps
                Rect r = BoxPrintAtlas.SeamLine;
                Vector2 u0 = new Vector2(r.xMin + 0.01f, r.center.y), u1 = new Vector2(r.xMax - 0.01f, r.center.y);
                if (seamAlongX) print.Quad(P(-hx + 0.01f, h + lift, -gw), P(hx - 0.01f, h + lift, -gw), P(hx - 0.01f, h + lift, gw), P(-hx + 0.01f, h + lift, gw), N(0, 1, 0), u0, u1, u1, u0);
                else print.Quad(P(-gw, h + lift, -hz + 0.01f), P(gw, h + lift, -hz + 0.01f), P(gw, h + lift, hz - 0.01f), P(-gw, h + lift, hz - 0.01f), N(0, 1, 0), u0, u0, u1, u1);
            }

            // ---- Prints on the side faces (a face = one of the four vertical sides) ----
            // side: 0 = +Z, 1 = -Z, 2 = +X, 3 = -X.
            void Decal(int side, Rect atlas, float width, float yCentre, float along)
            {
                float aspect = atlas.height * 512f / (atlas.width * 1024f);       // height / width in texels
                float w = width * 0.5f, hgt = width * aspect * 0.5f;
                float faceHalf = side < 2 ? hx : hz;
                along = Mathf.Clamp(along, -faceHalf + w + 0.02f, faceHalf - w - 0.02f);
                float y = Mathf.Clamp(yCentre, hgt + 0.02f, h - hgt - 0.02f);
                Vector3 a, b, c, d, n;
                switch (side)
                {
                    case 0: n = N(0, 0, 1); a = P(along + w, y - hgt, hz + lift); b = P(along - w, y - hgt, hz + lift); c = P(along - w, y + hgt, hz + lift); d = P(along + w, y + hgt, hz + lift); break;
                    case 1: n = N(0, 0, -1); a = P(along - w, y - hgt, -hz - lift); b = P(along + w, y - hgt, -hz - lift); c = P(along + w, y + hgt, -hz - lift); d = P(along - w, y + hgt, -hz - lift); break;
                    case 2: n = N(1, 0, 0); a = P(hx + lift, y - hgt, along - w); b = P(hx + lift, y - hgt, along + w); c = P(hx + lift, y + hgt, along + w); d = P(hx + lift, y + hgt, along - w); break;
                    default: n = N(-1, 0, 0); a = P(-hx - lift, y - hgt, along + w); b = P(-hx - lift, y - hgt, along - w); c = P(-hx - lift, y + hgt, along - w); d = P(-hx - lift, y + hgt, along + w); break;
                }
                print.Quad(a, b, c, d, n, new Vector2(atlas.xMin, atlas.yMin), new Vector2(atlas.xMax, atlas.yMin), new Vector2(atlas.xMax, atlas.yMax), new Vector2(atlas.xMin, atlas.yMax));
            }

            int longSide = seamAlongX ? 0 : 2;                         // the big faces
            float faceW = seamAlongX ? size.x : size.z;
            if (rng.NextDouble() < 0.7) Decal(longSide + (rng.NextDouble() < 0.5 ? 0 : 1), BoxPrintAtlas.ShippingLabel, Mathf.Min(0.2f, faceW * 0.4f), h * (0.45f + 0.25f * (float)rng.NextDouble()), (float)(rng.NextDouble() - 0.5) * faceW * 0.5f);
            if (rng.NextDouble() < 0.35) Decal(longSide + (rng.NextDouble() < 0.5 ? 0 : 1), BoxPrintAtlas.AddressLabel, Mathf.Min(0.13f, faceW * 0.3f), h * (0.3f + 0.4f * (float)rng.NextDouble()), (float)(rng.NextDouble() - 0.5) * faceW * 0.7f);
            if (rng.NextDouble() < 0.4) Decal(longSide + (rng.NextDouble() < 0.5 ? 0 : 1), BoxPrintAtlas.ThisSideUp, Mathf.Min(0.1f, faceW * 0.22f), h * 0.5f, (float)(rng.NextDouble() - 0.5) * faceW * 0.8f);
            if (rng.NextDouble() < 0.22) Decal(longSide + (rng.NextDouble() < 0.5 ? 0 : 1), BoxPrintAtlas.Fragile, Mathf.Min(0.16f, faceW * 0.35f), h * 0.7f, (float)(rng.NextDouble() - 0.5) * faceW * 0.6f);
            if (rng.NextDouble() < 0.3) Decal(rng.Next(4), BoxPrintAtlas.Logo, Mathf.Min(0.14f, Mathf.Min(size.x, size.z) * 0.5f), h * 0.55f, 0f);
        }

        /// <summary>Box with chamfered edges: six inset faces, twelve edge strips with smooth normals, eight corner fans.</summary>
        public static void RoundedBox(MeshWriter w, Vector3 c, Vector3 half, float b, Quaternion rot, MeshWriter.UVProjector uv)
        {
            Vector3 ax = rot * Vector3.right, ay = rot * Vector3.up, az = rot * Vector3.forward;
            Vector2 UV(Vector3 p, Vector3 n) => uv(p + w.worldOffset, n);
            Vector3 Pt(float sx, float sy, float sz) => c + ax * (sx * half.x) + ay * (sy * half.y) + az * (sz * half.z);
            float ix = 1f - b / half.x, iy = 1f - b / half.y, iz = 1f - b / half.z;      // inset factors

            // Faces (inset on the other two axes).
            void Face(Vector3 n, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) => w.Quad(p0, p1, p2, p3, n, uv);
            Face(ax, Pt(1, -iy, -iz), Pt(1, -iy, iz), Pt(1, iy, iz), Pt(1, iy, -iz));
            Face(-ax, Pt(-1, -iy, iz), Pt(-1, -iy, -iz), Pt(-1, iy, -iz), Pt(-1, iy, iz));
            Face(ay, Pt(-ix, 1, -iz), Pt(ix, 1, -iz), Pt(ix, 1, iz), Pt(-ix, 1, iz));
            Face(-ay, Pt(-ix, -1, iz), Pt(ix, -1, iz), Pt(ix, -1, -iz), Pt(-ix, -1, -iz));
            Face(az, Pt(-ix, -iy, 1), Pt(ix, -iy, 1), Pt(ix, iy, 1), Pt(-ix, iy, 1));
            Face(-az, Pt(ix, -iy, -1), Pt(-ix, -iy, -1), Pt(-ix, iy, -1), Pt(ix, iy, -1));

            // Edge strips: each joins two faces; the normal blends from one face's to the other's.
            void Strip(Vector3 nA, Vector3 a0, Vector3 a1, Vector3 nB, Vector3 b1, Vector3 b0)
            {
                Vector3 nm = (nA + nB).normalized;
                w.SmoothQuad(a0, a1, b1, b0, nA, nA, nB, nB, UV(a0, nm), UV(a1, nm), UV(b1, nm), UV(b0, nm));
            }
            // Edges parallel to z (between x and y faces).
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2)
                Strip(ax * sx, Pt(sx, sy * iy, -iz), Pt(sx, sy * iy, iz), ay * sy, Pt(sx * ix, sy, iz), Pt(sx * ix, sy, -iz));
            // Edges parallel to y (between x and z faces).
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                Strip(ax * sx, Pt(sx, -iy, sz * iz), Pt(sx, iy, sz * iz), az * sz, Pt(sx * ix, iy, sz), Pt(sx * ix, -iy, sz));
            // Edges parallel to x (between y and z faces).
            for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                Strip(ay * sy, Pt(-ix, sy, sz * iz), Pt(ix, sy, sz * iz), az * sz, Pt(ix, sy * iy, sz), Pt(-ix, sy * iy, sz));
            // Corner fans.
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 A = Pt(sx, sy * iy, sz * iz), B = Pt(sx * ix, sy, sz * iz), C = Pt(sx * ix, sy * iy, sz);
                Vector3 nA = ax * sx, nB = ay * sy, nC = az * sz, nm = (nA + nB + nC).normalized;
                w.SmoothQuad(A, B, C, C, nA, nB, nC, nC, UV(A, nm), UV(B, nm), UV(C, nm), UV(C, nm));
            }
        }
    }
}
