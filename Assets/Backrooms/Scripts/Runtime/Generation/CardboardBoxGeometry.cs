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
        /// <param name="mark">A client's pictogram (0-3) printed big on the two large faces in the tape's colour, or -1.</param>
        public static void Build(MeshWriter body, MeshWriter tape, MeshWriter print, Vector3 foot, Vector3 size, float yawDegrees, System.Random rng, int mark = -1)
        {
            var rot = Quaternion.Euler(0f, yawDegrees, 0f);
            Vector3 centre = foot + Vector3.up * (size.y * 0.5f);
            // The Overflow look (2026-11-06): rounder edges and wider tape that follows them (the style sheet's chunkier
            // shapes); its print is the Overflow atlas (the style set's swap for the print material).
            bool overflow = ArtStyle.Current == ArtStyle.Look.Overflow;
            float bevel = overflow ? Mathf.Min(0.014f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.2f) : Bevel;
            RoundedBox(body, centre, size * 0.5f, bevel, rot, MeshWriter.MetreUV);

            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);
            Vector3 N(float x, float y, float z) => rot * new Vector3(x, y, z);
            float hx = size.x * 0.5f, hz = size.z * 0.5f, h = size.y;
            bool seamAlongX = size.x >= size.z;                       // the flaps meet along the longer top side
            const float lift = 0.0007f;                               // decals and tape sit just off the surface

            // ---- Top seam: taped (most boxes) or a dark gap between the flaps ----
            if (overflow && rng.NextDouble() < 0.88)
            {
                // Over the top, round the rounded edge and down each end: a top quad, a chamfer quad each end, a side quad.
                float tw = 0.03f;                                     // (60 mm tape)
                float wrapDown = Mathf.Min(0.12f, h * 0.45f), b = bevel;
                for (int s = -1; s <= 1; s += 2)
                {
                    // In the seam's own frame: a along the seam, c across it.
                    Vector3 Q(float a, float y, float c) => seamAlongX ? P(a, y, c) : P(c, y, a);
                    Vector3 Nq(float a, float y) => seamAlongX ? N(a, y, 0) : N(0, y, a);
                    float half = seamAlongX ? hx : hz;
                    if (s < 0)
                        tape.Quad(Q(-half + b, h + lift, -tw), Q(half - b, h + lift, -tw), Q(half - b, h + lift, tw), Q(-half + b, h + lift, tw), Nq(0, 1), MeshWriter.MetreUV);
                    Vector3 nc = Nq(s, 1).normalized;
                    tape.Quad(Q(s * (half - b), h + lift, -tw), Q(s * (half - b), h + lift, tw), Q(s * (half + lift), h - b, tw), Q(s * (half + lift), h - b, -tw), nc, MeshWriter.MetreUV);
                    tape.Quad(Q(s * (half + lift), h - b, -tw), Q(s * (half + lift), h - b, tw), Q(s * (half + lift), h - wrapDown, tw), Q(s * (half + lift), h - wrapDown, -tw), Nq(s, 0), MeshWriter.MetreUV);
                }
            }
            else if (!overflow && rng.NextDouble() < 0.88)
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
            // (2026-11-08, the owner: stickers that overlap flicker. Each one a little further off the board than the one
            // before, over the tape too, so one simply lies on another.)
            int layer = 0;
            void Decal(int side, Rect atlas, float width, float yCentre, float along)
            {
                float dl = 0.0007f + 0.0012f * ++layer;
                float aspect = atlas.height * 512f / (atlas.width * 1024f);       // height / width in texels
                float w = width * 0.5f, hgt = width * aspect * 0.5f;
                float faceHalf = side < 2 ? hx : hz;
                along = Mathf.Clamp(along, -faceHalf + w + 0.02f, faceHalf - w - 0.02f);
                float y = Mathf.Clamp(yCentre, hgt + 0.02f, h - hgt - 0.02f);
                Vector3 a, b, c, d, n;
                switch (side)
                {
                    case 0: n = N(0, 0, 1); a = P(along + w, y - hgt, hz + dl); b = P(along - w, y - hgt, hz + dl); c = P(along - w, y + hgt, hz + dl); d = P(along + w, y + hgt, hz + dl); break;
                    case 1: n = N(0, 0, -1); a = P(along - w, y - hgt, -hz - dl); b = P(along + w, y - hgt, -hz - dl); c = P(along + w, y + hgt, -hz - dl); d = P(along - w, y + hgt, -hz - dl); break;
                    case 2: n = N(1, 0, 0); a = P(hx + dl, y - hgt, along - w); b = P(hx + dl, y - hgt, along + w); c = P(hx + dl, y + hgt, along + w); d = P(hx + dl, y + hgt, along - w); break;
                    default: n = N(-1, 0, 0); a = P(-hx - dl, y - hgt, along + w); b = P(-hx - dl, y - hgt, along - w); c = P(-hx - dl, y + hgt, along - w); d = P(-hx - dl, y + hgt, along + w); break;
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

            // ---- The client's mark (2026-11-09, Docs/ImprovementPlan.md V4) ----
            // Boxes differed by tape colour and shape, and tape colours drift under the yellow light. So each client has
            // one bold pictogram, printed on both large faces in their tape's colour (the style sheet's print rule: big,
            // flat, few shapes). Readable without colour: TALLMORE three bars rising, QUILLBY a diamond, PAPERLARK a
            // chevron, BELLWEATHER a bell.
            if (mark < 0) return;
            float u = Mathf.Min(h * 0.62f, faceW * 0.4f) * 0.5f;          // half the mark's box
            float my = Mathf.Clamp(h * 0.5f, u + 0.015f, h - u - 0.015f);
            float md = 0.0007f + 0.0012f * (layer + 2);
            for (int s2 = 0; s2 < 2; s2++)
            {
                int side = longSide + s2;
                // The face's frame: o its middle, ex along it (so the mark reads the right way round from outside), ey up.
                Vector3 o, ex, n;
                switch (side)
                {
                    case 0: n = N(0, 0, 1); o = P(0, my, hz + md); ex = N(-1, 0, 0); break;
                    case 1: n = N(0, 0, -1); o = P(0, my, -hz - md); ex = N(1, 0, 0); break;
                    case 2: n = N(1, 0, 0); o = P(hx + md, my, 0); ex = N(0, 0, 1); break;
                    default: n = N(-1, 0, 0); o = P(-hx - md, my, 0); ex = N(0, 0, -1); break;
                }
                Vector3 ey = Vector3.up;
                void Shape(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3) =>
                    tape.Quad(o + ex * (x0 * u) + ey * (y0 * u), o + ex * (x1 * u) + ey * (y1 * u), o + ex * (x2 * u) + ey * (y2 * u), o + ex * (x3 * u) + ey * (y3 * u), n, MeshWriter.MetreUV);
                foreach (var q in MarkShapes[Mathf.Clamp(mark, 0, MarkShapes.Length - 1)])
                    Shape(q[0], q[1], q[2], q[3], q[4], q[5], q[6], q[7]);
            }
        }

        /// <summary>
        /// The four clients' pictograms, as quads (x0, y0 .. x3, y3) in a box from -1 to 1, y up: TALLMORE three bars rising,
        /// QUILLBY a diamond, PAPERLARK a chevron (a bird's wings), BELLWEATHER a bell (wide at the foot) and its clapper.
        /// The boxes print them; the interface (UI.UiKit.Mark) draws the same shapes.
        /// </summary>
        public static readonly float[][][] MarkShapes =
        {
            new[] { new[] { -0.95f, -1f, -0.45f, -1f, -0.45f, -0.25f, -0.95f, -0.25f }, new[] { -0.25f, -1f, 0.25f, -1f, 0.25f, 0.35f, -0.25f, 0.35f }, new[] { 0.45f, -1f, 0.95f, -1f, 0.95f, 1f, 0.45f, 1f } },
            new[] { new[] { 0f, -1f, 0.8f, 0f, 0f, 1f, -0.8f, 0f } },
            new[] { new[] { -1f, 0.55f, 0f, -0.35f, 0f, -1f, -1f, -0.1f }, new[] { 0f, -0.35f, 1f, 0.55f, 1f, -0.1f, 0f, -1f } },
            new[] { new[] { -0.9f, -0.55f, 0.9f, -0.55f, 0.42f, 1f, -0.42f, 1f }, new[] { -0.22f, -1f, 0.22f, -1f, 0.22f, -0.7f, -0.22f, -0.7f } },
        };

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
