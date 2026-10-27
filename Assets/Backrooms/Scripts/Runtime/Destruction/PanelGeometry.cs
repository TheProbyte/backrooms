using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Destruction
{
    /// <summary>
    /// The shapes of a broken wall panel and of what falls out of it, from its model and what is left of it: each board
    /// as its pieces (the wall's own finish on the face, grey paper on the back, the gypsum core with its two papers along
    /// every broken edge; cracked pieces pushed in and tipped a little so the cracks show), the studs (steel C-studs, or
    /// wood that splinters where it broke), tracks or plates, batts, electrical boxes, cable, pipe and baseboards; the
    /// shadow caster; the colliders.
    /// </summary>
    public static class PanelGeometry
    {
        public const int Finish0 = 0, Finish1 = 1, Edge = 2, Paper = 3, Base = 4, Frame = 5, Batt = 6, Box = 7, Cable = 8, Pipe = 9, Slots = 10;

        const float T = WallSpec.T, B = WallSpec.Board, CH = WallSpec.CavityHalf;

        public static Material[] Materials(WallPanelModel m) => new[]
        {
            m.finish[0], m.finish[1], DemolitionMaterials.GypsumEdge, DemolitionMaterials.BackPaper, m.baseboardMaterial,
            DemolitionMaterials.Frame(m.steel), DemolitionMaterials.Batt(m.steel), DemolitionMaterials.Box(m.steel),
            DemolitionMaterials.Cable(m.steel), DemolitionMaterials.Copper,
        };

        static float H01(int a, int b, int c, int d) => Dice.Hash(a, b, c, d) * 0.5f + 0.5f;

        // ---------------- The whole panel ----------------

        /// <summary>Everything still standing in the panel.</summary>
        public static void View(PanelMesh mb, WallPanelModel m, WallPanelState st, float pitch)
        {
            for (int side = 0; side < 2; side++)
            for (int i = 0; i < m.boards[side].pieces.Count; i++)
                if (!st.removed[side][i]) BoardPiece(mb, m, st, side, i, pitch, false);
            for (int side = 0; side < 2; side++)
            for (int i = 0; i < WallPanelModel.BaseboardPieces; i++)
                if (m.HasBaseboard(st, side, i)) Baseboard(mb, m, side, i, pitch, i == 0 || !m.HasBaseboard(st, side, i - 1), i == 2 || !m.HasBaseboard(st, side, i + 1));
            Framing(mb, m, st, pitch);
            if (m.insulated)
                for (int b = 0; b < m.bays.Count; b++)
                for (int r = 0; r < m.rows; r++)
                    if (st.InsulationPresent(m, b, r)) BattLength(mb, m, b, r, pitch, !st.InsulationPresent(m, b, r - 1), !st.InsulationPresent(m, b, r + 1));
            Services(mb, m, pitch);
        }

        /// <summary>
        /// The panel's shadow: plain pieces, frame, batts, baseboards (no cracks or jags). Each board piece left casts as a
        /// block through the whole wall (as the chunks' walls do): light passes only where both boards are open, and a
        /// 12.7 mm plate alone was thin enough for the shadow bias to eat, so light came through the pieces.
        /// </summary>
        public static void Shadow(PanelMesh mb, WallPanelModel m, WallPanelState st, float pitch)
        {
            var k = m.key;
            float w0 = -T * 0.5f, w1 = T * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                foreach (var f in m.boards[side].pieces)
                {
                    if (st.removed[side][f.index]) continue;
                    var c = f.cell;
                    Vector3 s0 = k.ToWorld(f.site.x, f.site.y, w0, pitch), s1 = k.ToWorld(f.site.x, f.site.y, w1, pitch);
                    for (int i = 0; i < c.Length; i++)
                    {
                        Vector2 a = c[i], b = c[(i + 1) % c.Length];
                        mb.Tri(0, s0, k.ToWorld(a.x, a.y, w0, pitch), k.ToWorld(b.x, b.y, w0, pitch), -k.Across, Vector2.zero, Vector2.zero, Vector2.zero);
                        mb.Tri(0, s1, k.ToWorld(b.x, b.y, w1, pitch), k.ToWorld(a.x, a.y, w1, pitch), k.Across, Vector2.zero, Vector2.zero, Vector2.zero);
                        // The edge, out from the piece's centre.
                        Vector2 d = b - a, n2 = new Vector2(d.y, -d.x);
                        if (Vector2.Dot(n2, (a + b) * 0.5f - f.site) < 0f) n2 = -n2;
                        var n = k.DirToWorld(new Vector3(n2.x, n2.y, 0f)).normalized;
                        mb.Quad(0, k.ToWorld(a.x, a.y, w0, pitch), k.ToWorld(b.x, b.y, w0, pitch), k.ToWorld(b.x, b.y, w1, pitch), k.ToWorld(a.x, a.y, w1, pitch), n, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                    }
                }
                for (int i = 0; i < WallPanelModel.BaseboardPieces; i++)
                    if (m.HasBaseboard(st, side, i)) Baseboard(mb, m, side, i, pitch, true, true, 0);
            }
            Framing(mb, m, st, pitch, 0);
            if (m.insulated)
                for (int b = 0; b < m.bays.Count; b++)
                for (int r = 0; r < m.rows; r++)
                    if (st.InsulationPresent(m, b, r)) BattLength(mb, m, b, r, pitch, true, true, 0);
        }

        // ---------------- Boards ----------------

        /// <summary>
        /// One piece of a board. In the wall a cracked piece sits a millimetre or two deeper and tipped by up to a degree
        /// and a half (so the cracks round a hole show), and only its edges facing a gap or another loose piece are drawn.
        /// As debris (loose) it is a whole plate: every edge drawn, nothing moved.
        /// </summary>
        public static void BoardPiece(PanelMesh mb, WallPanelModel m, WallPanelState st, int side, int i, float pitch, bool loose)
        {
            var k = m.key;
            var f = m.boards[side].pieces[i];
            float sgn = side == 0 ? -1f : 1f, wo = sgn * T * 0.5f, wi = sgn * (T * 0.5f - B);
            bool cracked = !loose && st != null && st.cracked[side][i];
            float push = cracked ? Mathf.Lerp(0.0003f, 0.0012f, H01(i, side, k.line, 1)) : 0f;
            Vector2 slope = Vector2.zero;
            if (cracked)
            {
                float ang = H01(i, side, k.along, 2) * Mathf.PI * 2f, tilt = Mathf.Tan(Mathf.Lerp(0.15f, 0.6f, H01(i, side, 3, k.storey)) * Mathf.Deg2Rad);
                slope = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * tilt;
            }
            Vector3 W(Vector2 p, float w) => k.ToWorld(p.x, p.y, w - sgn * push + Vector2.Dot(slope, p - f.centroid), pitch);
            Vector3 nOut = k.DirToWorld(new Vector3(-slope.x, -slope.y, 1f) * sgn).normalized;
            Vector3 flat = k.Across * sgn;
            int finish = side == 0 ? Finish0 : Finish1;
            var o = f.outline;
            Vector3 cO = W(f.site, wo), cI = W(f.site, wi);
            Vector2 uc = MeshWriter.WallUV(k.ToWorld(f.site.x, f.site.y, wo, pitch), flat);
            for (int q = 0; q < o.Length; q++)
            {
                Vector2 a = o[q], b = o[(q + 1) % o.Length];
                // Face: the wall's finish, mapped exactly as the chunk maps it (the pattern runs on across the cracks).
                mb.Tri(finish, cO, W(a, wo), W(b, wo), nOut, uc,
                    MeshWriter.WallUV(k.ToWorld(a.x, a.y, wo, pitch), flat), MeshWriter.WallUV(k.ToWorld(b.x, b.y, wo, pitch), flat));
                // Back: the board's grey paper.
                mb.Tri(Paper, cI, W(b, wi), W(a, wi), -nOut, f.site * 2f, b * 2f, a * 2f);
            }
            // Edges: the gypsum core between the two papers, along where it broke.
            float run = Dice.Hash(i, side, 5) * 4f;
            for (int q = 0; q < o.Length; q++)
            {
                int j = f.label[q];
                Vector2 a = o[q], b = o[(q + 1) % o.Length];
                float len = (b - a).magnitude;
                bool show = loose || (j >= 0 && st != null && (st.removed[side][j] || st.cracked[side][j] || cracked));
                if (show && len > 1e-5f)
                {
                    Vector2 dir = (b - a) / len;
                    Vector3 n = k.DirToWorld(new Vector3(dir.y, -dir.x, 0f));
                    float u0 = run / 0.125f, u1 = (run + len) / 0.125f;
                    mb.Quad(Edge, W(a, wo), W(b, wo), W(b, wi), W(a, wi), n, new Vector2(u0, 0.02f), new Vector2(u1, 0.02f), new Vector2(u1, 0.98f), new Vector2(u0, 0.98f));
                }
                run += len;
            }
        }

        /// <summary>The centre of a board piece in the world: what its loose mesh is built around.</summary>
        public static Vector3 PieceOrigin(WallPanelModel m, int side, int i, float pitch)
        {
            var f = m.boards[side].pieces[i];
            float sgn = side == 0 ? -1f : 1f;
            return m.key.ToWorld(f.centroid.x, f.centroid.y, sgn * (T * 0.5f - B * 0.5f), pitch);
        }

        /// <summary>A loose board piece's size along the world axes (for its collider).</summary>
        public static Vector3 PieceSize(WallPanelModel m, int side, int i)
        {
            var f = m.boards[side].pieces[i];
            var d = m.key.DirToWorld(new Vector3(Mathf.Max(0.02f, f.bounds.width * 0.82f), Mathf.Max(0.02f, f.bounds.height * 0.82f), B));
            return new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
        }

        // ---------------- Baseboards ----------------

        public static void Baseboard(PanelMesh mb, WallPanelModel m, int side, int piece, float pitch, bool startEnd, bool endEnd, int slot = Base)
        {
            var k = m.key; var bb = m.baseboard[piece];
            float sgn = side == 0 ? -1f : 1f, w0 = sgn * T * 0.5f, w1 = w0 + sgn * WallPanelModel.BaseboardDepth;
            float h = WallPanelModel.BaseboardHeight;
            Vector3 P(float u, float v, float w) => k.ToWorld(u, v, w, pitch);
            Vector3 n = k.Across * sgn;
            mb.Quad(slot, P(bb.x, 0, w1), P(bb.y, 0, w1), P(bb.y, h, w1), P(bb.x, h, w1), n, PanelMesh.Metre);
            mb.Quad(slot, P(bb.x, h, w0), P(bb.x, h, w1), P(bb.y, h, w1), P(bb.y, h, w0), Vector3.up, PanelMesh.Metre);
            mb.Quad(slot, P(bb.x, 0, w0), P(bb.x, h, w0), P(bb.y, h, w0), P(bb.y, 0, w0), -n, PanelMesh.Metre);
            if (startEnd) mb.Quad(slot, P(bb.x, 0, w0), P(bb.x, 0, w1), P(bb.x, h, w1), P(bb.x, h, w0), -k.Along, PanelMesh.Metre);
            if (endEnd) mb.Quad(slot, P(bb.y, 0, w0), P(bb.y, h, w0), P(bb.y, h, w1), P(bb.y, 0, w1), k.Along, PanelMesh.Metre);
        }

        public static Vector3 BaseboardOrigin(WallPanelModel m, int side, int piece, float pitch)
        {
            var bb = m.baseboard[piece];
            float sgn = side == 0 ? -1f : 1f;
            return m.key.ToWorld((bb.x + bb.y) * 0.5f, WallPanelModel.BaseboardHeight * 0.5f, sgn * (T * 0.5f + WallPanelModel.BaseboardDepth * 0.5f), pitch);
        }

        public static Vector3 BaseboardSize(WallPanelModel m, int piece)
        {
            var bb = m.baseboard[piece];
            var d = m.key.DirToWorld(new Vector3(bb.y - bb.x, WallPanelModel.BaseboardHeight, WallPanelModel.BaseboardDepth));
            return new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
        }

        // ---------------- Studs, tracks ----------------

        static void Framing(PanelMesh mb, WallPanelModel m, WallPanelState st, float pitch, int slot = Frame)
        {
            var k = m.key;
            float H = m.height, g = WallSpec.SteelGauge;
            for (int s = 0; s < m.studs.Length; s++)
                foreach (var seg in st.studSegments[s])
                {
                    bool low = seg.x > WallSpec.StudBottom(m.steel) + 0.01f, high = seg.y < WallSpec.StudTop(m.steel, H) - 0.01f;
                    StudLength(mb, m, s, seg, pitch, low, high, slot);
                }
            // The studs at the posts (the corner and jamb studs), seen from inside the wall.
            for (int e = 0; e < 2; e++)
            {
                float u = e == 0 ? WallSpec.U0 : WallSpec.U1, inward = e == 0 ? 1f : -1f;
                mb.LocalBox(slot, k, pitch, new Vector3(Mathf.Min(u, u + inward * 0.004f), 0f, -CH), new Vector3(Mathf.Max(u, u + inward * 0.004f), H, CH), PanelMesh.Upright);
                if (m.steel)
                    for (int f = -1; f <= 1; f += 2)
                    {
                        float w = f * (CH - g * 0.5f);
                        mb.LocalBox(slot, k, pitch, new Vector3(Mathf.Min(u, u + inward * WallSpec.SteelFlange), 0f, w - g * 0.5f), new Vector3(Mathf.Max(u, u + inward * WallSpec.SteelFlange), H, w + g * 0.5f), PanelMesh.Upright);
                    }
            }
            // Tracks (steel) or plates (wood), along the floor and the ceiling.
            if (m.steel)
            {
                float leg = WallSpec.SteelTrackLeg;
                mb.LocalBox(slot, k, pitch, new Vector3(WallSpec.U0, 0f, -CH), new Vector3(WallSpec.U1, g, CH), PanelMesh.Metre);
                mb.LocalBox(slot, k, pitch, new Vector3(WallSpec.U0, H - g, -CH), new Vector3(WallSpec.U1, H, CH), PanelMesh.Metre);
                for (int f = -1; f <= 1; f += 2)
                {
                    float w = f * (CH - g * 0.5f);
                    mb.LocalBox(slot, k, pitch, new Vector3(WallSpec.U0, 0f, w - g * 0.5f), new Vector3(WallSpec.U1, leg, w + g * 0.5f), PanelMesh.Metre);
                    mb.LocalBox(slot, k, pitch, new Vector3(WallSpec.U0, H - leg, w - g * 0.5f), new Vector3(WallSpec.U1, H, w + g * 0.5f), PanelMesh.Metre);
                }
            }
            else
            {
                float p = WallSpec.WoodPlate;
                mb.LocalBox(slot, k, pitch, new Vector3(WallSpec.U0, 0f, -CH), new Vector3(WallSpec.U1, p, CH), PanelMesh.Metre);
                mb.LocalBox(slot, k, pitch, new Vector3(WallSpec.U0, H - p * 2f, -CH), new Vector3(WallSpec.U1, H - p, CH), PanelMesh.Metre);
                mb.LocalBox(slot, k, pitch, new Vector3(WallSpec.U0, H - p, -CH), new Vector3(WallSpec.U1, H, CH), PanelMesh.Metre);
            }
        }

        /// <summary>
        /// A length of stud. Steel: a C (web, two flanges, two lips), torn ends ragged. Wood: a plank whose broken ends
        /// splinter.
        /// </summary>
        public static void StudLength(PanelMesh mb, WallPanelModel m, int stud, Vector2 span, float pitch, bool brokenLow, bool brokenHigh, int slot = Frame)
        {
            var k = m.key;
            float us = m.studs[stud];
            if (m.steel)
            {
                float F = WallSpec.SteelFlange, g = WallSpec.SteelGauge, L = WallSpec.SteelLip;
                float ux = us - F * 0.5f;
                // Torn ends: the flanges and lips stop a little short of the web, raggedly.
                float Rag(int part, bool high) => (high ? brokenHigh : brokenLow) ? 0.008f + 0.025f * H01(stud, part, Mathf.RoundToInt(span.x * 100f), high ? 1 : 0) : 0f;
                mb.LocalBox(slot, k, pitch, new Vector3(ux, span.x, -CH), new Vector3(ux + g, span.y, CH), PanelMesh.Upright);
                for (int f = -1; f <= 1; f += 2)
                {
                    float w = f * (CH - g * 0.5f);
                    float a = span.x + Rag(f + 2, false), b = span.y - Rag(f + 4, true);
                    if (b <= a) continue;
                    mb.LocalBox(slot, k, pitch, new Vector3(ux, a, w - g * 0.5f), new Vector3(ux + F, b, w + g * 0.5f), PanelMesh.Upright);
                    float lw = f * (CH - g - L * 0.5f);
                    mb.LocalBox(slot, k, pitch, new Vector3(ux + F - g, a, lw - L * 0.5f), new Vector3(ux + F, b, lw + L * 0.5f), PanelMesh.Upright);
                }
            }
            else
            {
                float W = WallSpec.WoodWidth * 0.5f;
                mb.LocalBox(slot, k, pitch, new Vector3(us - W, span.x, -CH), new Vector3(us + W, span.y, CH), PanelMesh.Upright);
                // Splinters at a broken end: thin slivers standing out of the break.
                for (int e = 0; e < 2; e++)
                {
                    if (e == 0 ? !brokenLow : !brokenHigh) continue;
                    float v = e == 0 ? span.x : span.y, dir = e == 0 ? -1f : 1f;
                    for (int q = 0; q < 4; q++)
                    {
                        float h = Dice.Hash(stud, q, Mathf.RoundToInt(v * 1000f), e);
                        float len = 0.015f + 0.04f * (h * 0.5f + 0.5f), thick = 0.004f + 0.006f * H01(stud, q, 7, e);
                        float u = us + Dice.Hash(stud, q, 11, e) * (W - thick), w = Dice.Hash(stud, q, 13, e) * (CH - thick);
                        float lo = Mathf.Min(v, v + dir * len), hi = Mathf.Max(v, v + dir * len);
                        mb.LocalBox(slot, k, pitch, new Vector3(u - thick * 0.5f, lo, w - thick), new Vector3(u + thick * 0.5f, hi, w + thick), PanelMesh.Upright);
                    }
                }
            }
        }

        public static Vector3 StudOrigin(WallPanelModel m, int stud, Vector2 span, float pitch) =>
            m.key.ToWorld(m.studs[stud], (span.x + span.y) * 0.5f, 0f, pitch);

        public static Vector3 StudSize(WallPanelModel m, Vector2 span)
        {
            var d = m.key.DirToWorld(new Vector3(WallSpec.StudWidth(m.steel), span.y - span.x, CH * 2f));
            return new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
        }

        // ---------------- Insulation ----------------

        /// <summary>One batt length: soft faces, bulging a little between the studs.</summary>
        public static void BattLength(PanelMesh mb, WallPanelModel m, int bay, int row, float pitch, bool bottomFace, bool topFace, int slot = Batt)
        {
            var k = m.key;
            float u0 = m.bays[bay].x + 0.002f, u1 = m.bays[bay].y - 0.002f;
            float v0 = WallSpec.StudBottom(m.steel) + row * m.rowHeight, v1 = v0 + m.rowHeight;
            float w = CH - 0.004f;
            Vector3 P(float u, float v, float ww) => k.ToWorld(u, v, ww, pitch);
            // Front and back as 3 x 3 patches, the middle a few millimetres proud (a batt is pressed in at the studs).
            for (int f = -1; f <= 1; f += 2)
            {
                Vector3 n = k.Across * f;
                for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                {
                    float ua = Mathf.Lerp(u0, u1, a / 3f), ub = Mathf.Lerp(u0, u1, (a + 1) / 3f), va = Mathf.Lerp(v0, v1, b / 3f), vb = Mathf.Lerp(v0, v1, (b + 1) / 3f);
                    float Bulge(float u, float v) => f * (w - 0.006f * Mathf.Abs((u - u0) / (u1 - u0) * 2f - 1f) + 0.003f * Dice.Hash(bay, row, Mathf.RoundToInt(u * 100f), Mathf.RoundToInt(v * 100f)));
                    mb.Quad(slot, P(ua, va, Bulge(ua, va)), P(ub, va, Bulge(ub, va)), P(ub, vb, Bulge(ub, vb)), P(ua, vb, Bulge(ua, vb)), n, PanelMesh.Metre);
                }
            }
            mb.Quad(slot, P(u0, v0, -w), P(u0, v1, -w), P(u0, v1, w), P(u0, v0, w), -k.Along, PanelMesh.Metre);
            mb.Quad(slot, P(u1, v0, -w), P(u1, v0, w), P(u1, v1, w), P(u1, v1, -w), k.Along, PanelMesh.Metre);
            if (bottomFace) mb.Quad(slot, P(u0, v0, -w), P(u0, v0, w), P(u1, v0, w), P(u1, v0, -w), Vector3.down, PanelMesh.Metre);
            if (topFace) mb.Quad(slot, P(u0, v1, -w), P(u1, v1, -w), P(u1, v1, w), P(u0, v1, w), Vector3.up, PanelMesh.Metre);
        }

        public static Vector3 BattOrigin(WallPanelModel m, int bay, int row, float pitch) =>
            m.key.ToWorld((m.bays[bay].x + m.bays[bay].y) * 0.5f, WallSpec.StudBottom(m.steel) + (row + 0.5f) * m.rowHeight, 0f, pitch);

        public static Vector3 BattSize(WallPanelModel m, int bay)
        {
            var d = m.key.DirToWorld(new Vector3(m.bays[bay].y - m.bays[bay].x, m.rowHeight, CH * 2f));
            return new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
        }

        // ---------------- Boxes, cable, pipe ----------------

        static void Services(PanelMesh mb, WallPanelModel m, float pitch)
        {
            var k = m.key;
            foreach (var p in m.plates)
            {
                m.BoxOf(p, out var c, out var h);
                mb.LocalBox(Box, k, pitch, c - h, c + h, PanelMesh.Metre);
                // What holds it: a steel strap across the bay, or a wood block under it.
                int bay = m.BayAt(p.u);
                if (bay < 0) continue;
                var span = m.bays[bay];
                float back = c.z - Mathf.Sign(c.z) * h.z;
                if (m.steel) mb.LocalBox(Frame, k, pitch, new Vector3(span.x, p.v - 0.0125f, back - 0.001f), new Vector3(span.y, p.v + 0.0125f, back + 0.001f), PanelMesh.Metre);
                else mb.LocalBox(Frame, k, pitch, new Vector3(span.x, p.v - h.y - 0.038f, -CH), new Vector3(span.y, p.v - h.y, CH), PanelMesh.Metre);
            }
            foreach (var c in m.cables) mb.Tube(Cable, k.ToWorld(c.a, pitch), k.ToWorld(c.b, pitch), c.radius, 6, false);
            if (m.HasPipe) mb.Tube(Pipe, k.ToWorld(m.pipe.x, 0f, 0f, pitch), k.ToWorld(m.pipe.x, m.height, 0f, pitch), m.pipe.y, 10, false);
        }

        // ---------------- Colliders ----------------

        /// <summary>World boxes (centre, size) for what is left: each board's standing area in 5 cm cells merged into rectangles, studs, tracks, batts, boxes, baseboards.</summary>
        public static void Colliders(List<(Vector3 c, Vector3 s)> into, WallPanelModel m, WallPanelState st, float pitch)
        {
            into.Clear();
            var k = m.key;
            void Add(Vector3 min, Vector3 max)
            {
                Vector3 a = k.ToWorld(min, pitch), b = k.ToWorld(max, pitch);
                Vector3 lo = Vector3.Min(a, b), hi = Vector3.Max(a, b);
                into.Add(((lo + hi) * 0.5f, hi - lo));
            }
            const float cell = 0.05f;
            int cols = Mathf.CeilToInt((WallSpec.U1 - WallSpec.U0) / cell), rows = Mathf.CeilToInt(m.height / cell);
            float cw = (WallSpec.U1 - WallSpec.U0) / cols, rh = m.height / rows;
            var solid = new bool[cols * rows];
            var used = new bool[cols * rows];
            for (int side = 0; side < 2; side++)
            {
                float sgn = side == 0 ? -1f : 1f, wo = sgn * T * 0.5f, wi = sgn * (T * 0.5f - B);
                for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    solid[r * cols + c] = st.BoardAt(m, side, new Vector2(WallSpec.U0 + (c + 0.5f) * cw, (r + 0.5f) * rh));
                    used[r * cols + c] = false;
                }
                // Greedy rectangles: widest run first, then as many rows as share it.
                for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (!solid[r * cols + c] || used[r * cols + c]) continue;
                    int c1 = c;
                    while (c1 + 1 < cols && solid[r * cols + c1 + 1] && !used[r * cols + c1 + 1]) c1++;
                    int r1 = r;
                    while (r1 + 1 < rows)
                    {
                        bool ok = true;
                        for (int x = c; x <= c1 && ok; x++) ok = solid[(r1 + 1) * cols + x] && !used[(r1 + 1) * cols + x];
                        if (!ok) break;
                        r1++;
                    }
                    for (int y = r; y <= r1; y++) for (int x = c; x <= c1; x++) used[y * cols + x] = true;
                    Add(new Vector3(WallSpec.U0 + c * cw, r * rh, Mathf.Min(wo, wi)), new Vector3(WallSpec.U0 + (c1 + 1) * cw, (r1 + 1) * rh, Mathf.Max(wo, wi)));
                }
                for (int i = 0; i < WallPanelModel.BaseboardPieces; i++)
                    if (m.HasBaseboard(st, side, i))
                    {
                        float w1 = wo + sgn * WallPanelModel.BaseboardDepth;
                        Add(new Vector3(m.baseboard[i].x, 0f, Mathf.Min(wo, w1)), new Vector3(m.baseboard[i].y, WallPanelModel.BaseboardHeight, Mathf.Max(wo, w1)));
                    }
            }
            float sw = WallSpec.StudWidth(m.steel) * 0.5f;
            for (int s = 0; s < m.studs.Length; s++)
                foreach (var seg in st.studSegments[s])
                    Add(new Vector3(m.studs[s] - sw, seg.x, -CH), new Vector3(m.studs[s] + sw, seg.y, CH));
            float bottom = Mathf.Max(0.02f, WallSpec.StudBottom(m.steel)), top = WallSpec.StudTop(m.steel, m.height);
            Add(new Vector3(WallSpec.U0, 0f, -CH), new Vector3(WallSpec.U1, bottom, CH));
            Add(new Vector3(WallSpec.U0, top, -CH), new Vector3(WallSpec.U1, m.height, CH));
            if (m.insulated)
                for (int b = 0; b < m.bays.Count; b++)
                for (int r = 0; r < m.rows; r++)
                    if (st.InsulationPresent(m, b, r))
                    {
                        float v0 = WallSpec.StudBottom(m.steel) + r * m.rowHeight;
                        Add(new Vector3(m.bays[b].x, v0, -CH), new Vector3(m.bays[b].y, v0 + m.rowHeight, CH));
                    }
            foreach (var p in m.plates) { m.BoxOf(p, out var c, out var h); Add(c - h, c + h); }
            if (m.HasPipe) Add(new Vector3(m.pipe.x - m.pipe.y, 0f, -m.pipe.y), new Vector3(m.pipe.x + m.pipe.y, m.height, m.pipe.y));
        }
    }
}
