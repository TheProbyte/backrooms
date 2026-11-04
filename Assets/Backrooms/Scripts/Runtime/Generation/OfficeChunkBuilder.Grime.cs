using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Dirt where dirt really goes (Docs/RealismPlan.md, section 1), seeded like the wear (world seed and cell):
    ///  - The bottom of every wall a little darker (mops, shoes, dust), fading up over 30 cm.
    ///  - Inside corners darker from floor to ceiling, and a dust line along the top of each wall and on the ceiling beside
    ///    it (dust carried by the air settles where it slows).
    ///  - Grubby patches at hand height beside doorways and round light switches; chipped paint on the jambs.
    ///  - The dark halo a supply diffuser leaves on the tiles round it.
    /// Strengths change smoothly over the world (Smear), so a run of wall is never one flat tone and two cells meet without
    /// a step. All are wall-wear overlay quads (Backrooms/FloorWear), except chips in wallpaper, which show the board under
    /// it. Dust on what faces up is Backrooms/Lit's own (a uniform set by GraphicsController).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>How high the grime toward the floor reaches, how wide the corner shadow is, how deep the line under the ceiling.</summary>
        const float GrimeBand = 0.32f, CornerBand = 0.16f, CeilingBand = 0.13f, CeilingStrip = 0.1f;

        enum WallEnd { Runs, Corner, Doorway, Open }

        /// <summary>The rarer wear this chunk has, for F5 > PLACES > WEAR AND DETAIL (RealismSightings; published on the main thread).</summary>
        readonly List<RealismSightings.Sighting> _sightings = new List<RealismSightings.Sighting>();
        /// <summary>The rarer things this chunk's data put down (for tests: what F5 > PLACES will be told).</summary>
        public IReadOnlyList<RealismSightings.Sighting> Sightings => _sightings;

        /// <summary>Notes one: a chunk-local point, the way it faces into the room, its kind (the names CamcorderVisualMenu.Realism uses).</summary>
        void Saw(Vector3 local, Vector3 facing, string kind) =>
            _sightings.Add(new RealismSightings.Sighting { position = _origin + local, facing = facing, kind = kind });

        /// <summary>A smooth 0..1 over the world at a chunk-local point (the same from either chunk), for strengths along walls.</summary>
        float Smear(Vector3 p, int salt) => Field((_origin.x + p.x) * 0.45f, (_origin.z + p.z) * 0.45f, salt);
        /// <summary>The same at a finer grain (features under a metre): where dirt comes and goes along a wall.</summary>
        float Patchy(Vector3 p, int salt) => Field((_origin.x + p.x) * 1.6f, (_origin.z + p.z) * 1.6f, salt);

        static Color32 Tone(Color32 c, float k) => new Color32(c.r, c.g, c.b, (byte)Mathf.Clamp(c.a * k, 0f, 255f));

        /// <summary>
        /// The dirt on one wall face seen from cell (x, z): edge is the middle of the cell's side on the floor, outDir points
        /// out through the wall, along runs along it, top is the room's wall top (its ceiling).
        /// </summary>
        void WallDirt(int x, int z, int side, Vector3 edge, Vector3 outDir, Vector3 along, Room r, uint m, float top)
        {
            int gx = GX(x), gz = GZ(z);
            Vector3 into = -outDir, up = Vector3.up;
            Vector3 face = edge - outDir * (T * 0.5f) + into * 0.0012f;
            Vector3 P(float a, float y) => face + along * a + up * y;
            int ax = side < 2 ? 0 : 1, az = side < 2 ? 1 : 0;

            // How each end of the face finishes (0: the - end, 1: the + end), and how far the face reaches there: on to the
            // next cell's face over the post between them, to an inside corner, or to the edge of an opening.
            var ends = new WallEnd[2];
            var ext = new float[2];
            for (int k = 0; k < 2; k++)
            {
                int s = k == 0 ? -1 : 1;
                int endSide = side < 2 ? (k == 0 ? 2 : 3) : (k == 0 ? 0 : 1);
                if (OfficeLayout.IsSolid(_L.EdgeOf(x, z, endSide))) { ends[k] = WallEnd.Corner; ext[k] = C * 0.5f - T * 0.5f; continue; }
                int nx = x + s * ax, nz = z + s * az;
                if (!_L.InChunk(nx, nz) || OfficeLayout.IsSolid(_L.EdgeOf(nx, nz, side))) { ends[k] = WallEnd.Runs; ext[k] = C * 0.5f; continue; }
                var e2 = _L.EdgeOf(nx, nz, side);
                ends[k] = e2 == EdgeKind.Door || e2 == EdgeKind.DoorHeader ? WallEnd.Doorway : WallEnd.Open;
                ext[k] = C * 0.5f + T * 0.5f;
            }
            float a0 = -ext[0], a1 = ext[1];
            float u0 = ends[0] == WallEnd.Runs ? 0f : -1f, u1 = ends[1] == WallEnd.Runs ? 0f : 1f;

            // Toward the floor: in patches, not a band (a band the same all along read as a shadow), heavier and higher in
            // some stretches, nearly gone in others, in pieces 40 cm long whose strength and height follow smooth world
            // fields, so neighbouring pieces and cells meet without a step. (The same in every room: an open plan made of
            // several rooms has one wall run, so it must not change at a room's edge.)
            float yb = NoBaseboard ? 0f : BB;
            float dirt = DetailRates.Dirt;
            var grime = new Color32(112, 102, 88, 120);
            int pieces = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / 0.4f));
            float GrimeK(float a)
            {
                Vector3 p = P(a, 0f);
                float broad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.8f, Smear(p, 951)));
                return dirt * (0.08f + 0.92f * broad) * (0.35f + 0.65f * Patchy(p, 955));
            }
            float GrimeH(float a) => 0.12f + 0.2f * Patchy(P(a, 0f), 956);
            for (int i = 0; i < pieces; i++)
            {
                float pa = Mathf.Lerp(a0, a1, i / (float)pieces), pb = Mathf.Lerp(a0, a1, (i + 1) / (float)pieces);
                float ua = Mathf.Lerp(u0, u1, i / (float)pieces), ub = Mathf.Lerp(u0, u1, (i + 1) / (float)pieces);
                Color32 ga = Tone(grime, GrimeK(pa)), gb = Tone(grime, GrimeK(pb));
                float ha = GrimeH(pa), hb = GrimeH(pb);
                _wallWear.Quad(P(pa, yb), P(pb, yb), P(pb, yb + hb), P(pa, yb + ha),
                               new Vector2(ua, 0f), new Vector2(ub, 0f), new Vector2(ub, 1f), new Vector2(ua, 1f), ga, gb, gb, ga, new Vector2(0.85f, 0f));
            }
            // Shoe scuffs: short dark streaks just above the floor.
            if (Dice(gx, gz, 957 + side) < 0.45f * Mathf.Min(1f, dirt))
            {
                int n = 1 + (int)(Dice(gx, gz, 958 + side) * 3f);
                for (int j = 0; j < n; j++)
                {
                    int salt = 1020 + side * 8 + j;
                    float hw = 0.025f + 0.06f * Dice(gx, gz, salt), hh = 0.003f + 0.008f * Dice(gx, gz, salt + 50);
                    float a = Mathf.Lerp(a0 + 0.1f + hw, a1 - 0.1f - hw, Dice(gx, gz, salt + 100));
                    float y = yb + 0.025f + 0.11f * Dice(gx, gz, salt + 150);
                    var col = new Color32(66, 58, 50, (byte)Mathf.Clamp((110 + 90 * Dice(gx, gz, salt + 200)) * Mathf.Min(1.5f, dirt), 0f, 255f));
                    _wallWear.Rect(P(a, y), along, up, hw, hh, col, new Vector2(0.35f, 0.1f));
                }
            }

            // The line under the ceiling, on the wall and on the ceiling beside it (not where the ceiling slopes or is open).
            if (r.ceilDrop == 0 && !r.openAbove && top < 6.5f)
            {
                var line = new Color32(116, 110, 100, (byte)Mathf.Clamp(130 * dirt, 0f, 255f));
                Color32 d0 = Tone(line, 0.35f + 1f * Smear(P(a0, top), 952)), d1 = Tone(line, 0.35f + 1f * Smear(P(a1, top), 952));
                _wallWear.Quad(P(a0, top - CeilingBand), P(a1, top - CeilingBand), P(a1, top), P(a0, top),
                               new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f), new Vector2(u0, 0f), d0, d1, d1, d0, new Vector2(0.85f, 0f));
                Vector3 c = edge - outDir * (T * 0.5f) + up * (top - 0.0008f);
                Vector3 Q(float a, float d) => c + along * a + into * d;
                _wallWear.Quad(Q(a0, 0f), Q(a1, 0f), Q(a1, CeilingStrip), Q(a0, CeilingStrip),
                               new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f), Tone(d0, 0.8f), Tone(d1, 0.8f), Tone(d1, 0.8f), Tone(d0, 0.8f), new Vector2(0.85f, 0f));
            }

            for (int k = 0; k < 2; k++)
            {
                float ae = k == 0 ? a0 : a1, sgn = k == 0 ? 1f : -1f;          // the end, and the way back along the face
                float ua = k == 0 ? 0f : 1f, ub = k == 0 ? 1f : 0f;             // profile 0 at the end, 1 inward
                switch (ends[k])
                {
                    case WallEnd.Corner:
                    {
                        // Inside corner: a soft shadow of dirt up the whole height, a little heavier low down.
                        var cc = new Color32(108, 100, 88, (byte)Mathf.Clamp((55 + 60 * Smear(P(ae, 1f), 953)) * dirt, 0f, 255f));
                        Vector3 b0 = P(ae, 0f), b1 = P(ae + sgn * CornerBand, 0f), t0 = P(ae, top), t1 = P(ae + sgn * CornerBand, top);
                        _wallWear.Quad(b0, b1, t1, t0, Vector2.zero, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero,
                                       Tone(cc, 1.3f), Tone(cc, 1.3f), Tone(cc, 0.6f), Tone(cc, 0.6f), Vector2.zero);
                        break;
                    }
                    case WallEnd.Doorway:
                    case WallEnd.Open:
                    {
                        bool door = ends[k] == WallEnd.Doorway;
                        // Hands on the frame going through: a grubby patch at hand height beside the opening.
                        if (Dice(gx, gz, 960 + side * 2 + k) < (door ? 0.6f : 0.2f))
                        {
                            float w = 0.1f + 0.07f * Dice(gx, gz, 964 + side * 2 + k), y = 1.0f + 0.15f * Dice(gx, gz, 968 + side * 2 + k);
                            float hh = 0.15f + 0.1f * Dice(gx, gz, 972 + side * 2 + k);
                            var hand = new Color32(108, 96, 82, (byte)Mathf.Clamp((110 + 60 * Dice(gx, gz, 976 + side * 2 + k)) * dirt, 0f, 255f));
                            Vector3 cen = P(ae + sgn * w, y);
                            _wallWear.Quad(cen - along * w - up * hh, cen + along * w - up * hh, cen + along * w + up * hh, cen - along * w + up * hh,
                                           new Vector2(ua, -1f), new Vector2(ub, -1f), new Vector2(ub, 1f), new Vector2(ua, 1f), hand, hand, hand, hand, new Vector2(0.1f, 0.3f));
                        }
                        // Chipped paint on the edge, at hip and trolley height.
                        if (Dice(gx, gz, 980 + side * 2 + k) < (door ? 0.45f : 0.3f) * DetailRates.WallWear)
                        {
                            int n = 1 + (int)(Dice(gx, gz, 984 + side * 2 + k) * 3f);
                            for (int j = 0; j < n; j++)
                            {
                                int salt = 990 + side * 16 + k * 8 + j;
                                float y = 0.35f + 0.85f * Dice(gx, gz, salt), s = 0.006f + 0.014f * Dice(gx, gz, salt + 100);
                                float a = ae + sgn * (s * 0.6f + 0.003f + 0.008f * Dice(gx, gz, salt + 200));
                                Chip(P(a, y) + into * 0.0002f, along, into, s, r, m, gx * 53 + gz * 17 + salt);
                            }
                        }
                        break;
                    }
                }
            }
            WallPaper(x, z, side, edge, outDir, along, r, m, top, a0, a1);
        }

        /// <summary>A chip out of the paint or paper: the board under wallpaper shows, painted plaster shows grey.</summary>
        void Chip(Vector3 c, Vector3 along, Vector3 into, float s, Room r, uint m, int seed)
        {
            var rng = new System.Random(seed);
            bool paper = _a.wallpaper && _a.paintedWall && _a.WallMaterial(r) == _a.wallpaper;
            if (!paper)
            {
                _wallWear.Rect(c, along, Vector3.up, s, s * (0.5f + 0.4f * (float)rng.NextDouble()), new Color32(122, 116, 108, 215), new Vector2(-0.45f, 0.35f));
                return;
            }
            var w = WMask(_a.paintedWall, m);
            const int k = 7;
            var pts = new Vector3[k];
            float turn = (float)rng.NextDouble() * Mathf.PI;
            for (int i = 0; i < k; i++)
            {
                float t = turn + i * Mathf.PI * 2f / k, rr = s * (0.55f + 0.45f * (float)rng.NextDouble());
                pts[i] = c + along * (Mathf.Cos(t) * rr) + Vector3.up * (Mathf.Sin(t) * rr * 0.7f);
            }
            for (int i = 0; i < k; i++) w.Quad(c, pts[(i + 1) % k], pts[i], pts[i], into, MeshWriter.WallUV);
        }

        /// <summary>Grubby round a light switch (most of it below and to the side the hand comes from).</summary>
        void SwitchGrime(Vector3 pos, Vector3 n, Vector3 right, Vector2Int cell, int side)
        {
            int gx = GX(cell.x), gz = GZ(cell.y);
            if (Dice(gx, gz, 1000 + side) > 0.7f) return;
            var col = new Color32(108, 96, 82, (byte)Mathf.Clamp((120 + 60 * Dice(gx, gz, 1004 + side)) * DetailRates.Dirt, 0f, 255f));
            float lean = (Dice(gx, gz, 1008 + side) - 0.5f) * 0.03f;
            _wallWear.Rect(pos + n * 0.0012f + right * lean - Vector3.up * 0.012f, right, Vector3.up, 0.068f, 0.092f, col, new Vector2(0.25f, 0.25f));
        }

        /// <summary>
        /// The dark halo round a supply diffuser: air blowing out of its four sides over years leaves the tiles round it
        /// grey, darkest at the frame. c: the diffuser's centre on the ceiling (chunk-local), h: the ceiling.
        /// </summary>
        void DiffuserSmudge(Vector3 c, int gx, int gz, int i)
        {
            float k = 0.6f + 0.6f * Dice(gx, gz, 1010 + i);
            float reach = 0.16f + 0.12f * Dice(gx, gz, 1012 + i);
            var col = new Color32(72, 70, 68, (byte)Mathf.Clamp(190 * k * DetailRates.Dirt, 0, 255));
            c.y -= 0.0008f;
            Saw(c, Vector3.down, "smudge");
            for (int s = 0; s < 4; s++)
            {
                var q = Quaternion.Euler(0f, 90f * s, 0f);
                Vector3 outward = q * Vector3.forward, side = q * Vector3.right;
                Vector3 mid = c + outward * (0.3f + reach * 0.5f);
                // u along the frame's side (fading off at its corners), v from the frame (0) outward (1).
                Vector3 p0 = mid - side * 0.3f - outward * reach * 0.5f, p1 = mid + side * 0.3f - outward * reach * 0.5f;
                Vector3 p2 = mid + side * 0.3f + outward * reach * 0.5f, p3 = mid - side * 0.3f + outward * reach * 0.5f;
                _wallWear.Quad(p0, p1, p2, p3, new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 1f), col, new Vector2(0.75f, 0.2f));
            }
        }
    }
}
