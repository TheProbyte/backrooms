using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Walls and wallpaper (Docs/RealismPlan.md, section 3), seeded like the wear:
    ///  - Wallpaper hung in 53 cm rolls: a faint seam at every roll, now and then a seam opened up a little.
    ///  - Bubbles where the paste let go (raised domes of the paper itself, its pattern running over them), in clusters.
    ///  - A long strip come away from the top, bending out into the room, the bare board where it was.
    ///  - Repairs: a rectangle of paper hung out of line with the pattern, or newer paint on a painted wall.
    ///  - Painted walls: roller marks, the odd drip run, paint scraped off at desk height.
    ///  - Baseboards pulled away from the wall, a short piece missing, the caulk cracked at a joint.
    ///  - Outlets and switches painted over with the wall, hung crooked by one screw.
    /// Geometry is a few dozen triangles where it happens (wallpaper and board in the walls' own materials, so it is
    /// rebuilt with them when a wall breaks); marks are wall-wear overlay quads.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Width of a roll of wallpaper: a seam every this many metres along a papered wall.</summary>
        const float RollWidth = 0.53f;

        bool Papered(Room r) => _a.wallpaper && _a.paintedWall && _a.WallMaterial(r) == _a.wallpaper;
        bool PaintedRoom(Room r) => _a.paintedWall && _a.WallMaterial(r) == _a.paintedWall;

        /// <summary>
        /// One wall face's paper or paint, seen from cell (x, z): edge is the middle of the cell's side on the floor,
        /// a0..a1 how far the face reaches along it (WallDirt), top the room's wall top.
        /// </summary>
        void WallPaper(int x, int z, int side, Vector3 edge, Vector3 outDir, Vector3 along, Room r, uint m, float top, float a0, float a1)
        {
            int gx = GX(x), gz = GZ(z);
            Vector3 into = -outDir, up = Vector3.up;
            Vector3 wall = edge - outDir * (T * 0.5f);
            Vector3 P(float a, float y, float off) => wall + along * a + up * y + into * off;
            float yb = NoBaseboard ? 0f : BB;
            float reach0 = a0 + 0.06f, reach1 = a1 - 0.06f;
            if (reach1 - reach0 < 0.3f) return;

            if (Papered(r))
            {
                // Seams, on the world's roll grid along this wall line (each side of a wall hung on its own).
                float worldA = Vector3.Dot(_origin + wall, along);
                int lineId = Mathf.RoundToInt((side < 2 ? _origin.x + edge.x : _origin.z + edge.z) / C);
                float off = Dice(lineId, side & 1, 1100) * RollWidth;
                int k0 = Mathf.CeilToInt((worldA + a0 - off) / RollWidth), k1 = Mathf.FloorToInt((worldA + a1 - off) / RollWidth);
                for (int k = k0; k <= k1; k++)
                {
                    float a = off + k * RollWidth - worldA;
                    _wallWear.Rect(P(a, (yb + top) * 0.5f, 0.0012f), along, up, 0.0017f, (top - yb) * 0.5f, new Color32(116, 108, 96, 60), new Vector2(0f, 0.97f));
                    // Opened up a little along part of it.
                    if (Dice(lineId * 31 + k, side & 1, 1101) < 0.1f * DetailRates.WallWear)
                    {
                        float y = Mathf.Lerp(yb + 0.3f, top - 0.3f, Dice(lineId * 31 + k, side & 1, 1102)), hh = 0.08f + 0.25f * Dice(lineId * 31 + k, side & 1, 1103);
                        _wallWear.Rect(P(a, y, 0.0012f), along, up, 0.0026f, hh, new Color32(84, 76, 66, 150), new Vector2(0.2f, 0.75f));
                        Saw(P(a, y, 0f), into, "seam_open");
                        _wallWear.Rect(P(a + 0.006f, y, 0.0012f), along, up, 0.006f, hh * 0.8f, new Color32(150, 142, 130, 45), new Vector2(0f, 0.6f));
                    }
                }

                var paper = WMask(_a.wallpaper, m);
                var bare = WMask(_a.paintedWall, m);
                // Bubbles where the paste let go, a few together.
                float ww = DetailRates.WallWear;
                bool bubbles = Dice(gx, gz, 1110 + side) < 0.025f * ww;
                if (bubbles)
                {
                    float ca = Mathf.Lerp(reach0 + 0.12f, reach1 - 0.12f, Dice(gx, gz, 1111 + side)), cy = 0.7f + 1.3f * Dice(gx, gz, 1112 + side);
                    int n = 2 + (int)(Dice(gx, gz, 1113 + side) * 4f);
                    Saw(P(ca, cy, 0f), into, "bubbles");
                    for (int j = 0; j < n; j++)
                    {
                        int s = 1120 + side * 16 + j;
                        float rad = 0.025f + 0.04f * Dice(gx, gz, s);
                        float a = Mathf.Clamp(ca + (Dice(gx, gz, s + 100) - 0.5f) * 0.3f, reach0 + rad, reach1 - rad);
                        float y = Mathf.Clamp(cy + (Dice(gx, gz, s + 200) - 0.5f) * 0.3f, yb + 0.2f, top - 0.2f);
                        Bubble(paper, wall + along * a + up * y, along, into, rad, rad * (0.1f + 0.1f * Dice(gx, gz, s + 300)));
                    }
                }
                // A long strip come away from the top (between two seams where the face has a whole roll).
                if (Dice(gx, gz, 1140 + side) < 0.012f * ww && top < 4f && top - yb > 1.6f)
                {
                    float s0 = float.NaN;
                    for (int k = k0 - 1; k <= k1; k++)
                    {
                        float a = off + k * RollWidth - worldA;
                        if (a >= reach0 && a + RollWidth <= reach1) { s0 = a; break; }
                    }
                    if (float.IsNaN(s0)) { s0 = reach0; }
                    float s1 = Mathf.Min(s0 + RollWidth, reach1);
                    float len = Mathf.Min(0.3f + 0.6f * Dice(gx, gz, 1141 + side), top - yb - 0.9f);
                    float bend = Mathf.Lerp(50f, 125f, Dice(gx, gz, 1142 + side)) * Mathf.Deg2Rad;
                    Strip(paper, bare, wall, along, into, s0, s1, top - len, len, bend, gx * 37 + gz * 11 + side);
                    Saw(P((s0 + s1) * 0.5f, top - len * 0.5f, 0f), into, "strip");
                }
                // A repair: a piece of paper hung out of line with the pattern, its edges showing.
                if (!bubbles && Dice(gx, gz, 1150 + side) < 0.02f * ww)
                {
                    float hw = 0.12f + 0.15f * Dice(gx, gz, 1151 + side), hh = 0.1f + 0.12f * Dice(gx, gz, 1152 + side);
                    float a = Mathf.Lerp(reach0 + hw, reach1 - hw, Dice(gx, gz, 1153 + side)), y = 0.5f + 1.1f * Dice(gx, gz, 1154 + side);
                    var shift = new Vector2(0.37f, 0.19f);
                    Vector3 c = P(a, y, 0.0005f);
                    Vector3 q0 = c - along * hw - up * hh, q1 = c + along * hw - up * hh, q2 = c + along * hw + up * hh, q3 = c - along * hw + up * hh;
                    Vector2 UV(Vector3 p) => MeshWriter.WallUV(p + _origin, into) + shift;
                    paper.Quad(q0, q1, q2, q3, into, UV(q0), UV(q1), UV(q2), UV(q3));
                    Outline(P(a, y, 0.0012f), along, hw, hh, new Color32(110, 102, 90, 90));
                    Saw(c, into, "patch");
                }
            }
            else if (PaintedRoom(r))
            {
                // (No roller marks: tall faint stripes read as shadows through the camcorder.)
                float ww = DetailRates.WallWear;
                // A drip run, dried, with its bead at the bottom.
                if (Dice(gx, gz, 1180 + side) < 0.04f * ww)
                {
                    float a = Mathf.Lerp(reach0, reach1, Dice(gx, gz, 1181 + side)), y0 = 1.3f + 0.8f * Dice(gx, gz, 1182 + side);
                    float len = 0.05f + 0.15f * Dice(gx, gz, 1183 + side);
                    var col = new Color32(146, 142, 134, 120);
                    _wallWear.Rect(P(a, y0 - len * 0.5f, 0.0012f), along, up, 0.002f, len * 0.5f, col, new Vector2(0.2f, 0.8f));
                    _wallWear.Rect(P(a, y0 - len, 0.0012f), along, up, 0.0042f, 0.005f, col, new Vector2(-0.3f, 0.4f));
                }
                // Paint scraped off where a desk or a chair back rubbed.
                if (Dice(gx, gz, 1190 + side) < 0.06f * ww)
                {
                    float ha = 0.05f + 0.1f * Dice(gx, gz, 1191 + side), hb = 0.012f + 0.02f * Dice(gx, gz, 1192 + side);
                    float a = Mathf.Lerp(reach0 + ha, reach1 - ha, Dice(gx, gz, 1193 + side)), y = 0.72f + 0.08f * Dice(gx, gz, 1194 + side);
                    _wallWear.Rect(P(a, y, 0.0012f), along, up, ha, hb, new Color32(128, 124, 118, 170), new Vector2(0.5f, 0.3f));
                }
                // (No patches of newer paint: hard-edged brighter rectangles looked like a rendering fault.)
            }
        }

        /// <summary>Thin dark lines round a rectangle on a wall (a patch's edges).</summary>
        void Outline(Vector3 c, Vector3 along, float hw, float hh, Color32 col)
        {
            const float t = 0.0022f;
            Vector3 up = Vector3.up;
            _wallWear.Rect(c + up * hh, along, up, hw, t, col, new Vector2(0.9f, 0f));
            _wallWear.Rect(c - up * hh, along, up, hw, t, col, new Vector2(0.9f, 0f));
            _wallWear.Rect(c + along * hw, along, up, t, hh, col, new Vector2(0f, 0.9f));
            _wallWear.Rect(c - along * hw, along, up, t, hh, col, new Vector2(0f, 0.9f));
        }

        /// <summary>A bubble in the paper: a low dome of the wallpaper itself (its pattern over it), round c on the wall.</summary>
        void Bubble(MeshWriter paper, Vector3 c, Vector3 along, Vector3 into, float rad, float height)
        {
            const int rings = 3, segs = 14;
            Vector3 up = Vector3.up;
            Vector3 Flat(float rho, float phi) => c + along * (rho * Mathf.Cos(phi)) + up * (rho * Mathf.Sin(phi) * 0.85f);
            Vector3 Dome(float rho, float phi)
            {
                float q = rho / rad;
                return Flat(rho, phi) + into * (0.0004f + height * Mathf.Pow(Mathf.Max(0f, 1f - q * q), 1.5f));
            }
            Vector2 UV(Vector3 p) => MeshWriter.WallUV(p + _origin, into);
            for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
            {
                float r0 = rad * i / rings, r1 = rad * (i + 1) / rings;
                float p0 = j * Mathf.PI * 2f / segs, p1 = (j + 1) * Mathf.PI * 2f / segs;
                Vector3 A = Dome(r0, p0), B = Dome(r1, p0), Cc = Dome(r1, p1), D = Dome(r0, p1);
                Vector3 nrm = Vector3.Cross(B - A, Cc - A);
                if (nrm.sqrMagnitude < 1e-12f) nrm = Vector3.Cross(Cc - A, D - A);
                nrm = nrm.normalized;
                if (Vector3.Dot(nrm, into) < 0f) nrm = -nrm;
                paper.Quad(A, B, Cc, D, nrm, UV(Flat(r0, p0)), UV(Flat(r1, p0)), UV(Flat(r1, p1)), UV(Flat(r0, p1)));
            }
        }

        /// <summary>
        /// A strip of wallpaper come away from the top of the wall: held at y0, its upper part (len) bending out into the
        /// room by up to bend radians at its free end, its edges curling a little; its back is plain paper, and the bare
        /// board shows where it was.
        /// </summary>
        void Strip(MeshWriter paper, MeshWriter bare, Vector3 wall, Vector3 along, Vector3 into, float s0, float s1, float y0, float len, float bend, int seed)
        {
            const int segs = 9, across = 4;
            Vector3 up = Vector3.up;
            var rng = new System.Random(seed);
            float curl = 0.006f + 0.012f * (float)rng.NextDouble();
            // The centre line, bending out (in the plane of up and into).
            var spine = new Vector3[segs + 1];
            var nrm = new Vector3[segs + 1];
            spine[0] = up * y0 + into * 0.0006f;
            for (int i = 0; i <= segs; i++)
            {
                float th = bend * Mathf.Pow(i / (float)segs, 1.4f);
                nrm[i] = into * Mathf.Cos(th) - up * Mathf.Sin(th);
                if (i < segs)
                {
                    float thm = bend * Mathf.Pow((i + 0.5f) / segs, 1.4f);
                    spine[i + 1] = spine[i] + (up * Mathf.Cos(thm) + into * Mathf.Sin(thm)) * (len / segs);
                }
            }
            Vector3 Pt(int i, int j)
            {
                float u = j / (float)across, e = (u - 0.5f) * 2f;
                float t = i / (float)segs;
                return wall + along * Mathf.Lerp(s0, s1, u) + spine[i] + nrm[i] * (curl * e * e * t);
            }
            Vector3 Flat(int i, int j) => wall + along * Mathf.Lerp(s0, s1, j / (float)across) + up * (y0 + len * i / segs);
            Vector2 UV(Vector3 p) => MeshWriter.WallUV(p + _origin, into);
            for (int i = 0; i < segs; i++)
            for (int j = 0; j < across; j++)
            {
                Vector3 A = Pt(i, j), B = Pt(i, j + 1), Cc = Pt(i + 1, j + 1), D = Pt(i + 1, j);
                Vector3 n = ((nrm[i] + nrm[i + 1]) * 0.5f).normalized;
                paper.Quad(A, B, Cc, D, n, UV(Flat(i, j)), UV(Flat(i, j + 1)), UV(Flat(i + 1, j + 1)), UV(Flat(i + 1, j)));
                Vector3 back = -n * 0.0005f;
                bare.Quad(A + back, B + back, Cc + back, D + back, -n, MeshWriter.WallUV);
            }
            // The bare board where it hung, with dried paste on it.
            Vector3 b0 = wall + along * s0 + up * y0 + into * 0.0004f, b1 = wall + along * s1 + up * y0 + into * 0.0004f;
            bare.Quad(b0, b1, b1 + up * len, b0 + up * len, into, MeshWriter.WallUV);
            Vector3 mid = wall + along * ((s0 + s1) * 0.5f) + up * (y0 + len * 0.5f) + into * 0.0012f;
            _wallWear.Rect(mid, along, up, (s1 - s0) * 0.5f, len * 0.5f, new Color32(176, 158, 128, 70), new Vector2(0.6f, 0.6f));
        }

        // ---------------- Baseboards ----------------

        /// <summary>
        /// A baseboard along one wall face from a to b (on the floor, at the face; n into the room), now and then pulled off
        /// the wall along part of it, with a short piece missing, or its caulk cracked at the joint (seeded by the cell).
        /// </summary>
        void BaseboardRun(uint mask, Vector3 a, Vector3 b, Vector3 n, int gx, int gz, int salt)
        {
            if (NoBaseboard) return;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.2f || Mathf.Abs(a.y - b.y) > 0.001f) { Baseboard(mask, a, b, n); return; }
            Vector3 dir = d / len, up = Vector3.up;
            float u = Dice(gx, gz, 1200 + salt);
            if (u < 0.035f && len > 0.6f)
            {
                // Pulled off: a piece standing away from the wall, more at the top, the dark gap behind it.
                float pl = Mathf.Min(0.3f + 0.5f * Dice(gx, gz, 1201 + salt), len - 0.2f);
                float s0 = 0.1f + (len - 0.2f - pl) * Dice(gx, gz, 1202 + salt), s1 = s0 + pl;
                Baseboard(mask, a, a + dir * s0, n);
                Baseboard(mask, a + dir * s1, b, n);
                float g0 = 0.002f, g1 = 0.005f + 0.007f * Dice(gx, gz, 1203 + salt);
                var w = WMask(_a.baseboard, mask);
                Vector3 p0 = a + dir * s0, p1 = a + dir * s1;
                Vector3 f0 = p0 + n * (BD + g0), f1 = p1 + n * (BD + g0), t0 = p0 + n * (BD + g1) + up * BB, t1 = p1 + n * (BD + g1) + up * BB;
                Vector3 bt0 = p0 + n * g1 + up * BB, bt1 = p1 + n * g1 + up * BB, bb0 = p0 + n * g0, bb1 = p1 + n * g0;
                Vector3 face = Vector3.Cross(t1 - f0, f1 - f0).normalized;
                if (Vector3.Dot(face, n) < 0f) face = -face;
                w.Quad(f0, f1, t1, t0, face, MeshWriter.MetreUV);
                w.Quad(t0, t1, bt1, bt0, up, MeshWriter.MetreUV);
                w.Quad(bb0, bb1, bt1, bt0, -face, MeshWriter.MetreUV);
                w.Quad(bb0, f0, t0, bt0, -dir, MeshWriter.MetreUV);
                w.Quad(bb1, f1, t1, bt1, dir, MeshWriter.MetreUV);
                // The wall behind it, in shadow (seen down the gap).
                Vector3 g = (p0 + p1) * 0.5f + n * 0.0008f + up * (BB * 0.5f);
                _wallWear.Rect(g, dir, up, pl * 0.5f, BB * 0.5f, new Color32(60, 56, 52, 235), new Vector2(0.97f, 0.7f));
                Saw(g, n, "baseboard_pulled");
            }
            else if (u < 0.055f && len > 0.5f)
            {
                // A short piece missing: the board's cut ends, the wall behind with the old glue and dirt on it.
                float ml = 0.15f + 0.25f * Dice(gx, gz, 1204 + salt);
                float s0 = 0.08f + (len - 0.16f - ml) * Dice(gx, gz, 1205 + salt), s1 = s0 + ml;
                Baseboard(mask, a, a + dir * s0, n);
                Baseboard(mask, a + dir * s1, b, n);
                var w = WMask(_a.baseboard, mask);
                foreach (var (s, o) in new[] { (s0, dir), (s1, -dir) })
                {
                    Vector3 p = a + dir * s;
                    w.Quad(p, p + n * BD, p + n * BD + up * BB, p + up * BB, o, MeshWriter.MetreUV);
                }
                Vector3 c = a + dir * ((s0 + s1) * 0.5f) + n * 0.0012f;
                Saw(c, n, "baseboard_missing");
                _wallWear.Rect(c + up * (BB * 0.5f), dir, up, ml * 0.5f, BB * 0.5f, new Color32(150, 134, 108, 120), new Vector2(0.75f, 0.4f));
                _wallWear.Rect(c + up * 0.012f, dir, up, ml * 0.5f, 0.012f, new Color32(96, 88, 78, 160), new Vector2(0.8f, 0.2f));
            }
            else Baseboard(mask, a, b, n);
            // The caulk cracked where this board meets the next.
            if (Dice(gx, gz, 1206 + salt) < 0.15f)
                _wallWear.Rect(a + n * (BD + 0.0008f) + dir * 0.002f + up * (BB * 0.5f), dir, up, 0.0013f, BB * 0.5f, new Color32(70, 66, 60, 190), new Vector2(0f, 0.85f));
        }
    }
}
