using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The water park's attractions (2026-11-01; planned in PoolPlan.WaterPark). Falling water is drawn with
    /// Backrooms/PoolSheet (a thin sheet: glassy where it leaves the lip, breaking into ropes and drops as it falls); where
    /// it lands is listed for the runtime (PoolChunk.fittings: 5 a mushroom's ring, 6 an arch's landing, 4 a wave pool's
    /// chambers), which stirs the water, throws spray and plays the falling water's sound there (PoolFalls).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        Material ParkColour(int i)
        {
            switch (i & 3)
            {
                case 0: return PoolMaterials.PlasticRed ? PoolMaterials.PlasticRed : TileMat;
                case 1: return PoolMaterials.PlasticYellow ? PoolMaterials.PlasticYellow : TileMat;
                case 2: return PoolMaterials.PlasticBlue ? PoolMaterials.PlasticBlue : TileMat;
                default: return PoolMaterials.PlasticWhite ? PoolMaterials.PlasticWhite : TileMat;
            }
        }
        MeshWriter SheetW => WMask(PoolMaterials.Sheet ? PoolMaterials.Sheet : WaterMat, 1u);

        /// <summary>The surface under a point: the water's where there is water, else the deck.</summary>
        float SurfaceAt(Vector3 p) => _L.WaterAtPoint(p.x, p.z) ? PoolBiome.WaterLevel : 0f;
        /// <summary>What stands under a point: the pool's floor over water, else the deck.</summary>
        float GroundAt(float x, float z) => _L.WaterAtPoint(x, z) ? BasinFloorY(x, z) : 0f;

        // ---------------- Where falling water lands ----------------

        /// <summary>The crown: a short skirt of churned white water thrown up all round where a sheet goes in (PoolSheet's
        /// uv.y -1 at the water to 0 at its top), leaning out a little.</summary>
        void Crown(Vector3 c, float r, float h, int sides)
        {
            var sheet = SheetW;
            float repeats = Mathf.Max(2f, Mathf.Round(2f * Mathf.PI * r / 0.35f));
            for (int j = 0; j < sides; j++)
            {
                float p0 = 2f * Mathf.PI * j / sides, p1 = 2f * Mathf.PI * (j + 1) / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(p0), 0f, Mathf.Sin(p0)), d1 = new Vector3(Mathf.Cos(p1), 0f, Mathf.Sin(p1));
                Vector3 n0 = (d0 + Vector3.up * 0.6f).normalized, n1 = (d1 + Vector3.up * 0.6f).normalized;
                sheet.SmoothQuad(c + d0 * (r - 0.02f) + Vector3.down * 0.01f, c + d1 * (r - 0.02f) + Vector3.down * 0.01f, c + d1 * (r + h * 0.45f) + Vector3.up * h, c + d0 * (r + h * 0.45f) + Vector3.up * h,
                    n0, n1, n1, n0, new Vector2(repeats * j / sides, -1f), new Vector2(repeats * (j + 1) / sides, -1f), new Vector2(repeats * (j + 1) / sides, -0.001f), new Vector2(repeats * j / sides, -0.001f));
            }
        }

        /// <summary>A ring of foam on the surface round a landing, rIn to rOut (PoolSheet's uv.y -2 inside to -3 out).</summary>
        void LandingFoam(Vector3 c, float rIn, float rOut, int sides)
        {
            var sheet = SheetW;
            rIn = Mathf.Max(0.01f, rIn);
            float repeats = Mathf.Max(2f, Mathf.Round(2f * Mathf.PI * (rIn + rOut) * 0.5f / 0.5f));
            const int RS = 3;
            for (int j = 0; j < sides; j++)
            for (int k = 0; k < RS; k++)
            {
                float p0 = 2f * Mathf.PI * j / sides, p1 = 2f * Mathf.PI * (j + 1) / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(p0), 0f, Mathf.Sin(p0)), d1 = new Vector3(Mathf.Cos(p1), 0f, Mathf.Sin(p1));
                float ra = Mathf.Lerp(rIn, rOut, k / (float)RS), rb = Mathf.Lerp(rIn, rOut, (k + 1) / (float)RS);
                float va = -2f - k / (float)RS, vb = -2f - (k + 1) / (float)RS;
                sheet.SmoothQuad(c + d0 * rb, c + d1 * rb, c + d1 * ra, c + d0 * ra, Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                    new Vector2(repeats * j / sides, vb), new Vector2(repeats * (j + 1) / sides, vb), new Vector2(repeats * (j + 1) / sides, va), new Vector2(repeats * j / sides, va));
            }
        }

        // ---------------- Mushroom fountains ----------------

        /// <summary>
        /// A mushroom fountain: a white stem, a domed cap (red with white spots, yellow, blue or white) with a rolled rim,
        /// and water pouring off the whole rim in a curtain that arcs out a little and falls to the water.
        /// </summary>
        void Mushroom(PoolFeature f)
        {
            var c = V3(f.centre);
            float R = f.radius, H = f.height;
            float floor = GroundAt(c.x, c.z);
            var stem = WMask(PoolMaterials.PlasticWhite ? PoolMaterials.PlasticWhite : TileMat, 1u);
            Tube(stem, new Vector3(c.x, floor, c.z), c + Vector3.up * (H + 0.1f), 0.11f, 16);
            WMask(ChromeMat, 1u).Disc(new Vector3(c.x, floor + 0.004f, c.z), 0.2f, 16, Vector3.up, MeshWriter.MetreUV);
            _capsules.Add((new Vector3(c.x, (floor + H) * 0.5f, c.z), 0.12f, H - floor));
            var cap = WMask(ParkColour(f.shape), 1u);
            const int NA = 32, NR = 10;
            float dome = R * 0.42f;
            Vector3 Dome(float th, float ph) => c + new Vector3(Mathf.Cos(ph) * Mathf.Sin(th) * R, H + Mathf.Cos(th) * dome, Mathf.Sin(ph) * Mathf.Sin(th) * R);
            Vector3 DomeN(float th, float ph) => new Vector3(Mathf.Cos(ph) * Mathf.Sin(th) / R, Mathf.Cos(th) / dome, Mathf.Sin(ph) * Mathf.Sin(th) / R).normalized;
            for (int i = 0; i < NR; i++)
            for (int j = 0; j < NA; j++)
            {
                float t0 = 0.5f * Mathf.PI * i / NR, t1 = 0.5f * Mathf.PI * (i + 1) / NR;
                float p0 = 2f * Mathf.PI * j / NA, p1 = 2f * Mathf.PI * (j + 1) / NA;
                cap.SmoothQuad(Dome(t0, p0), Dome(t0, p1), Dome(t1, p1), Dome(t1, p0), DomeN(t0, p0), DomeN(t0, p1), DomeN(t1, p1), DomeN(t1, p0),
                    new Vector2(p0, t0), new Vector2(p1, t0), new Vector2(p1, t1), new Vector2(p0, t1));
            }
            // The underside: a shallow cone up to the stem.
            for (int j = 0; j < NA; j++)
            {
                float p0 = 2f * Mathf.PI * j / NA, p1 = 2f * Mathf.PI * (j + 1) / NA;
                Vector3 d0 = new Vector3(Mathf.Cos(p0), 0f, Mathf.Sin(p0)), d1 = new Vector3(Mathf.Cos(p1), 0f, Mathf.Sin(p1));
                cap.Quad(c + d0 * R + Vector3.up * H, c + d1 * R + Vector3.up * H, c + d1 * 0.12f + Vector3.up * (H + 0.14f), c + d0 * 0.12f + Vector3.up * (H + 0.14f), Vector3.down, MeshWriter.MetreUV);
            }
            _railPts.Clear();
            for (int j = 0; j <= NA; j++) { float p = 2f * Mathf.PI * j / NA; _railPts.Add(c + new Vector3(Mathf.Cos(p) * R, H, Mathf.Sin(p) * R)); }
            Sweep(cap, _railPts, 0.03f, 8);
            // White spots on a red cap.
            if ((f.shape & 3) == 0)
            {
                var spot = WMask(PoolMaterials.PlasticWhite ? PoolMaterials.PlasticWhite : TileMat, 1u);
                uint h = FitHash(Mathf.RoundToInt(c.x * 10f), Mathf.RoundToInt(c.z * 10f), 41);
                for (int k = 0; k < 9; k++)
                {
                    h = h * 1664525u + 1013904223u;
                    float th = 0.25f + 1.1f * ((h >> 8) & 255) / 255f, ph = 6.2832f * ((h >> 16) & 255) / 255f;
                    var at = Dome(th, ph); var nn = DomeN(th, ph);
                    spot.Disc(at + nn * 0.004f, R * (0.07f + 0.06f * ((h >> 4) & 15) / 15f), 12, nn, MeshWriter.MetreUV);
                }
            }
            // The curtain: off the rim at 0.45 m/s outward, falling to the surface.
            float surf = SurfaceAt(c + Vector3.right * (R + 0.3f));
            float fall = H - surf, tLand = Mathf.Sqrt(2f * fall / 9.81f);
            const float vh = 0.45f;
            const int K = 16;
            var sheet = SheetW;
            float repeats = Mathf.Round(2f * Mathf.PI * R / 0.5f);
            for (int k = 0; k < K; k++)
            {
                float ta = tLand * (k / (float)K) * (k / (float)K), tb = tLand * ((k + 1) / (float)K) * ((k + 1) / (float)K);
                float ra = R + 0.035f + vh * ta, rb = R + 0.035f + vh * tb, ya = H - 4.905f * ta * ta, yb = H - 4.905f * tb * tb;
                var na2 = new Vector2(9.81f * ta, vh).normalized; var nb2 = new Vector2(9.81f * tb, vh).normalized;
                for (int j = 0; j < NA; j++)
                {
                    float p0 = 2f * Mathf.PI * j / NA, p1 = 2f * Mathf.PI * (j + 1) / NA;
                    Vector3 d0 = new Vector3(Mathf.Cos(p0), 0f, Mathf.Sin(p0)), d1 = new Vector3(Mathf.Cos(p1), 0f, Mathf.Sin(p1));
                    sheet.SmoothQuad(c + d0 * ra + Vector3.up * ya, c + d1 * ra + Vector3.up * ya, c + d1 * rb + Vector3.up * yb, c + d0 * rb + Vector3.up * yb,
                        d0 * na2.x + Vector3.up * na2.y, d1 * na2.x + Vector3.up * na2.y, d1 * nb2.x + Vector3.up * nb2.y, d0 * nb2.x + Vector3.up * nb2.y,
                        new Vector2(repeats * j / NA, H - ya), new Vector2(repeats * (j + 1) / NA, H - ya), new Vector2(repeats * (j + 1) / NA, H - yb), new Vector2(repeats * j / NA, H - yb));
                }
            }
            float rLand = R + 0.035f + vh * tLand;
            Crown(new Vector3(c.x, surf, c.z), rLand, 0.16f, NA);
            LandingFoam(new Vector3(c.x, surf + 0.004f, c.z), rLand - 0.14f, rLand + 0.5f, NA);
            poolFittings.Add(new Vector4(c.x, surf, c.z, 5f));
            poolFittingDirs.Add(new Vector3(rLand, fall, 0f));
        }

        // ---------------- Water arches ----------------

        /// <summary>A water arch: a chrome nozzle low on a pool wall, a stream arcing out into the pool and falling in.</summary>
        void Arch(PoolFeature f)
        {
            var at = V3(f.centre);
            var n = Dir(f.yaw);
            float water = PoolBiome.WaterLevel, y0 = water + 0.35f, D = f.size.x, hp = f.height;
            var noz = at + Vector3.up * y0;
            var chrome = WMask(ChromeMat, 1u);
            chrome.Disc(noz + n * 0.003f, 0.05f, 14, n, MeshWriter.MetreUV);
            var tip = noz + n * 0.07f + Vector3.up * 0.03f;
            Tube(chrome, noz + n * 0.003f, tip, 0.02f, 10);
            Vector3 Path(float s) => tip + n * (D * s) + Vector3.up * (4f * hp * s * (1f - s) - (y0 + 0.03f - water) * s);
            var sheet = SheetW;
            const int K = 24, S = 8;
            float v = 0f;
            var prev = Path(0f);
            for (int k = 0; k < K; k++)
            {
                float s0 = k / (float)K, s1 = (k + 1) / (float)K;
                Vector3 a = Path(s0), b = Path(s1);
                var da = (Path(Mathf.Min(1f, s0 + 0.01f)) - Path(Mathf.Max(0f, s0 - 0.01f))).normalized;
                var db = (Path(Mathf.Min(1f, s1 + 0.01f)) - Path(Mathf.Max(0f, s1 - 0.01f))).normalized;
                var side = Vector3.Cross(Vector3.up, n).normalized;
                Vector3 ua = Vector3.Cross(da, side).normalized, ub = Vector3.Cross(db, side).normalized;
                float ra = Mathf.Lerp(0.018f, 0.034f, s0), rb = Mathf.Lerp(0.018f, 0.034f, s1);
                float v1 = v + (b - a).magnitude;
                for (int i = 0; i < S; i++)
                {
                    float q0 = 2f * Mathf.PI * i / S, q1 = 2f * Mathf.PI * (i + 1) / S;
                    Vector3 na0 = side * Mathf.Cos(q0) + ua * Mathf.Sin(q0), na1 = side * Mathf.Cos(q1) + ua * Mathf.Sin(q1);
                    Vector3 nb0 = side * Mathf.Cos(q0) + ub * Mathf.Sin(q0), nb1 = side * Mathf.Cos(q1) + ub * Mathf.Sin(q1);
                    sheet.SmoothQuad(a + na0 * ra, a + na1 * ra, b + nb1 * rb, b + nb0 * rb, na0, na1, nb1, nb0,
                        new Vector2(i / (float)S, v), new Vector2((i + 1) / (float)S, v), new Vector2((i + 1) / (float)S, v1), new Vector2(i / (float)S, v1));
                }
                v = v1;
            }
            var land = Path(1f);
            Crown(new Vector3(land.x, water, land.z), 0.07f, 0.1f, 12);
            LandingFoam(new Vector3(land.x, water + 0.004f, land.z), 0.03f, 0.42f, 16);
            poolFittings.Add(new Vector4(land.x, water, land.z, 6f));
            poolFittingDirs.Add(n);
            Stain(noz + Vector3.down * 0.06f, n, 0.08f, 0.25f);
        }

        // ---------------- Slide towers ----------------

        // (2026-11-01, the owner on the first tower: "the slides have the base poles in the slide itself", "model problems
        // with slides and railings", "improve realism, and models very much".) Rebuilt as indoor water park towers are
        // (WhiteWater's, ProSlide's): a painted steel frame on four square columns with base plates, X-braced, carrying a
        // platform of open grating with toe boards and guard rails filled with upright bars (1.1 m, as codes ask); a steel
        // stair with channel stringers, grating treads and a handrail on each side; the flume's start sitting on the platform
        // (its bottom flush with the deck, a closed back, a grab bar over it); the flume in flanged fibreglass sections,
        // each joint carried by a post with a saddle under it.
        const float TowerHalf = 0.85f, GuardH = 1.1f;
        MeshWriter SteelW => WMask(PoolMaterials.Steel ? PoolMaterials.Steel : ChromeMat, 1u);
        MeshWriter GrateW => WMask(PoolMaterials.Grille ? PoolMaterials.Grille : ChromeMat, 1u);

        /// <summary>A straight member from a to b, w wide and h deep, its depth toward upHint (a box along the line).</summary>
        static void Beam(MeshWriter m, Vector3 a, Vector3 b, float w, float h, Vector3 upHint)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            d /= len;
            var up = Vector3.ProjectOnPlane(upHint, d);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.right, d);
            m.Box((a + b) * 0.5f, new Vector3(w, h, len), Quaternion.LookRotation(d, up.normalized), 0b111111, MeshWriter.MetreUV);
        }

        /// <summary>A guard rail from a to b (both on the deck): end posts, a chrome top rail, a toe board and upright bars.</summary>
        void GuardRun(Vector3 a, Vector3 b, bool postA = true, bool postB = true)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.05f) return;
            var t = d / len;
            var steel = SteelW;
            if (postA) steel.Box(a + Vector3.up * (GuardH * 0.5f), new Vector3(0.05f, GuardH, 0.05f), Quaternion.LookRotation(t), 0b111111, MeshWriter.MetreUV);
            if (postB) steel.Box(b + Vector3.up * (GuardH * 0.5f), new Vector3(0.05f, GuardH, 0.05f), Quaternion.LookRotation(t), 0b111111, MeshWriter.MetreUV);
            // The top rail, round, a little proud of the posts; the toe board; the bars between.
            var chrome = WMask(ChromeMat, 1u);
            Tube(chrome, a + Vector3.up * (GuardH + 0.02f), b + Vector3.up * (GuardH + 0.02f), 0.025f, 12);
            Cap(chrome, a + Vector3.up * (GuardH + 0.02f), -t, 0.025f); Cap(chrome, b + Vector3.up * (GuardH + 0.02f), t, 0.025f);
            Beam(steel, a + Vector3.up * 0.06f, b + Vector3.up * 0.06f, 0.012f, 0.1f, Vector3.up);
            Beam(steel, a + Vector3.up * (GuardH - 0.04f), b + Vector3.up * (GuardH - 0.04f), 0.04f, 0.03f, Vector3.up);
            int bars = Mathf.Max(1, Mathf.FloorToInt(len / 0.11f));
            for (int i = 1; i < bars; i++)
            {
                var p = a + t * (len * i / bars);
                Tube(steel, p + Vector3.up * 0.11f, p + Vector3.up * (GuardH - 0.055f), 0.008f, 6);
            }
            _rotBoxes.Add(((a + b) * 0.5f + Vector3.up * (GuardH * 0.5f), new Vector3(0.06f, GuardH, len), Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg));
        }

        /// <summary>
        /// A slide tower on the deck beside a plunge pool: the steel frame, platform, guards and stair, and a flume swinging out
        /// to the curl's side and down into the pool.
        /// </summary>
        void SlideTower(PoolFeature f)
        {
            var c = V3(f.centre);
            var n = Dir(f.yaw);                         // toward the pool
            var t = Vector3.Cross(Vector3.up, n) * ((f.shape & 4) == 0 ? 1f : -1f);
            float H = f.height, P = TowerHalf;
            var q = Quaternion.LookRotation(n);
            float yawDeg = q.eulerAngles.y;
            var steel = SteelW;
            var grate = GrateW;
            var chrome = WMask(ChromeMat, 1u);
            // Columns, square tube on base plates with four bolts each.
            var cols = new Vector3[4];
            int ci = 0;
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                var col = c + n * ((P - 0.07f) * sz) + t * ((P - 0.07f) * sx);
                cols[ci++] = col;
                steel.Box(col + Vector3.up * ((H - 0.06f) * 0.5f), new Vector3(0.12f, H - 0.06f, 0.12f), q, 0b111111, MeshWriter.MetreUV);
                steel.Box(col + Vector3.up * 0.01f, new Vector3(0.26f, 0.02f, 0.26f), q, 0b111111, MeshWriter.MetreUV);
                foreach (var (bx, bz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                    Tube(chrome, col + n * (0.095f * bz) + t * (0.095f * bx) + Vector3.up * 0.02f, col + n * (0.095f * bz) + t * (0.095f * bx) + Vector3.up * 0.045f, 0.012f, 6);
                _rotBoxes.Add((col + Vector3.up * (H * 0.5f), new Vector3(0.12f, H, 0.12f), yawDeg));
                Stain(col, Vector3.up, 0.2f, 0f, true);
            }
            // Bracing: an X on each side and at the back, two tiers when tall; a tie between the tiers.
            int tiers = H > 2.7f ? 2 : 1;
            for (int s = 0; s < 4; s++)
            {
                if (s == 2 || s == 0) continue;          // the front (under the flume's start) and the back (the stair) stay open
                Vector3 a = cols[s], b = cols[(s + 1) % 4];
                var outward = ((a + b) * 0.5f - c); outward.y = 0f; outward.Normalize();
                for (int k = 0; k < tiers; k++)
                {
                    float y0 = 0.3f + (H - 0.55f) * k / tiers, y1 = 0.3f + (H - 0.55f) * (k + 1) / tiers;
                    Beam(steel, a + Vector3.up * y0 + outward * 0.07f, b + Vector3.up * y1 + outward * 0.07f, 0.045f, 0.045f, outward);
                    Beam(steel, b + Vector3.up * y0 + outward * 0.07f, a + Vector3.up * y1 + outward * 0.07f, 0.045f, 0.045f, outward);
                    if (k > 0) Beam(steel, a + Vector3.up * y0, b + Vector3.up * y0, 0.07f, 0.07f, Vector3.up);
                }
            }
            // The deck: a frame of beams round it, open grating on top (its top at H).
            for (int s = 0; s < 4; s++)
                Beam(steel, cols[s] + Vector3.up * (H - 0.13f), cols[(s + 1) % 4] + Vector3.up * (H - 0.13f), 0.1f, 0.16f, Vector3.up);
            grate.Box(c + Vector3.up * (H - 0.025f), new Vector3(2f * P, 0.05f, 2f * P), q, 0b111111, MeshWriter.MetreUV);
            _rotBoxes.Add((c + Vector3.up * (H - 0.06f), new Vector3(2f * P, 0.12f, 2f * P), yawDeg));
            // Guards: both sides whole; the front open where the flume starts; the back open at the stair.
            Vector3 D(float sx, float sz) => c + t * (sx * (P - 0.03f)) + n * (sz * (P - 0.03f)) + Vector3.up * H;
            const float entry = 0.42f, stairW = 0.48f;
            GuardRun(D(-1f, -1f), D(-1f, 1f));
            GuardRun(D(1f, -1f), D(1f, 1f));
            GuardRun(D(-1f, 1f), c + t * -entry + n * (P - 0.03f) + Vector3.up * H, false, true);
            GuardRun(c + t * entry + n * (P - 0.03f) + Vector3.up * H, D(1f, 1f), true, false);
            GuardRun(D(-1f, -1f), c + t * -stairW - n * (P - 0.03f) + Vector3.up * H, false, true);
            GuardRun(c + t * stairW - n * (P - 0.03f) + Vector3.up * H, D(1f, -1f), true, false);
            // A grab bar over the start, between two tall posts, as riders pull themselves off.
            {
                var l = c + t * -entry + n * (P - 0.03f) + Vector3.up * H; var r = c + t * entry + n * (P - 0.03f) + Vector3.up * H;
                steel.Box(l + Vector3.up * 0.7f, new Vector3(0.05f, 1.4f, 0.05f), q, 0b111111, MeshWriter.MetreUV);
                steel.Box(r + Vector3.up * 0.7f, new Vector3(0.05f, 1.4f, 0.05f), q, 0b111111, MeshWriter.MetreUV);
                Tube(chrome, l + Vector3.up * 1.32f, r + Vector3.up * 1.32f, 0.022f, 12);
                Cap(chrome, l + Vector3.up * 1.32f, -t, 0.022f); Cap(chrome, r + Vector3.up * 1.32f, t, 0.022f);
            }

            // The stair: steel channel stringers, open grating treads (rise about 19 cm, steep as tower stairs are), handrails
            // on both sides running on past the top and the foot, posts from the stringers.
            int N = Mathf.CeilToInt(H / 0.19f);
            float run = H * 0.82f, rise = H / N, tread = run / N;
            var back = c - n * P;                                    // the platform's back edge, at the stair's middle
            var foot = back - n * run;
            for (int k = 0; k < N - 1; k++)
            {
                float y = rise * (k + 1);
                var sc = foot + n * (tread * (k + 0.5f)) + Vector3.up * (y - 0.02f);
                grate.Box(sc, new Vector3(2f * stairW - 0.08f, 0.04f, tread + 0.03f), q, 0b111111, MeshWriter.MetreUV);
                steel.Box(sc - n * (tread * 0.5f - 0.015f) + Vector3.down * 0.03f, new Vector3(2f * stairW - 0.08f, 0.05f, 0.025f), q, 0b111111, MeshWriter.MetreUV);   // the nosing angle
                _rotBoxes.Add((sc + Vector3.down * 0.12f, new Vector3(2f * stairW - 0.08f, 0.24f, tread + 0.03f), yawDeg));
            }
            var slopeUp = Vector3.Cross(n, t).normalized;
            if (slopeUp.y < 0f) slopeUp = -slopeUp;
            foreach (float s in new[] { -1f, 1f })
            {
                var sd = t * (s * stairW);
                // The stringer: from the floor at the foot up to the platform's frame.
                Beam(steel, foot + sd + Vector3.up * 0.12f - n * 0.05f, back + sd + Vector3.up * (H - 0.1f), 0.04f, 0.24f, slopeUp);
                steel.Box(foot + sd + Vector3.up * 0.01f - n * 0.04f, new Vector3(0.12f, 0.02f, 0.22f), q, 0b111111, MeshWriter.MetreUV);
                // The handrail: up from the floor beyond the foot, along the stair 0.95 m over the nosings, on past the top
                // to the guard's post.
                var rs = sd + t * (s * 0.05f);
                var r0 = foot + rs - n * 0.3f;
                var r3 = back + rs + n * 0.06f + Vector3.up * (H + 0.95f);
                Rail(new List<Vector3> { r0, r0 + Vector3.up * 0.95f, foot + rs + n * tread + Vector3.up * (rise + 0.95f), back + rs - n * (tread * 0.5f) + Vector3.up * (H + 0.95f), r3, r3 + Vector3.down * 0.95f }, 0.022f);
                // Posts and a middle rail, from the stringer.
                for (float u = 0.33f; u < 0.9f; u += 0.33f)
                {
                    var on = Vector3.Lerp(foot + rs, back + rs, u) + Vector3.up * (H * u);
                    steel.Box(on + Vector3.up * 0.5f, new Vector3(0.04f, 0.95f, 0.04f), q, 0b111111, MeshWriter.MetreUV);
                }
                Beam(steel, foot + rs + Vector3.up * 0.5f, back + rs + Vector3.up * (H + 0.5f), 0.025f, 0.025f, Vector3.up);
                var mid = (foot + back) * 0.5f + rs + Vector3.up * (H * 0.5f + 0.5f);
                _rotBoxes.Add((mid, new Vector3(0.06f, 1.0f, (back + Vector3.up * H - foot).magnitude), yawDeg));
            }

            // The flume: its start sitting on the platform (the bottom of its channel flush with the deck), straight out a
            // little, then swinging out to the curl's side and down into the pool.
            const float rad = 0.32f;
            var start = c + n * (P - 0.42f) + Vector3.up * (H + rad + 0.01f);
            float toPool = f.size.x;
            var end = c + n * (toPool - f.size.y * 0.2f) + Vector3.up * (PoolBiome.WaterLevel + 0.3f);
            var p1 = start + n * 1.7f;
            var p2 = end - n * 1.4f + t * 2.3f + Vector3.up * 1.0f;
            Flume(u => { float v = 1f - u; return v * v * v * start + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * end; }, ParkColour(f.shape));
        }

        /// <summary>A flume along a path (u 0..1): a glossy channel with rolled rims and a closed start, in sections joined by
        /// flanges, water running down its bottom and pouring off its end, a post with a saddle under each joint.</summary>
        void Flume(System.Func<float, Vector3> Bz, Material mat)
        {
            var blue = WMask(mat, 1u);
            const int K = 56, R = 18;
            const float rad = 0.32f, wall = 0.035f, open = 80f * Mathf.Deg2Rad;
            var rimA = new List<Vector3>(K + 1); var rimB = new List<Vector3>(K + 1);
            Vector3[] prevIn = null, prevOut = null, prevN = null;
            Vector3 prevW0 = default, prevW1 = default; float vw = 0f, length = 0f, nextJoint = 1.6f;
            var sheet = SheetW;
            var steel = SteelW;
            Vector3 prevC = Bz(0f);
            for (int k = 0; k <= K; k++)
            {
                float u = k / (float)K;
                var c = Bz(u);
                var d = (Bz(Mathf.Min(1f, u + 0.01f)) - Bz(Mathf.Max(0f, u - 0.01f))).normalized;
                var side = Vector3.Cross(Vector3.up, d).normalized;
                var up = Vector3.Cross(d, side).normalized;
                var curIn = new Vector3[R + 1]; var curOut = new Vector3[R + 1]; var curN = new Vector3[R + 1];
                for (int i = 0; i <= R; i++)
                {
                    float th = -Mathf.PI + open + (2f * Mathf.PI - 2f * open) * i / R;
                    var dir = side * Mathf.Sin(th) - up * Mathf.Cos(th);
                    curN[i] = dir; curIn[i] = c + dir * rad; curOut[i] = c + dir * (rad + wall);
                }
                rimA.Add(c + curN[0] * (rad + wall * 0.5f));
                rimB.Add(c + curN[R] * (rad + wall * 0.5f));
                if (k == 0)
                {
                    // The closed start: a wall across the channel, faced inside and out.
                    var inner = new List<Vector3>(curIn); var outer = new List<Vector3>(curOut);
                    blue.Polygon(inner, d, MeshWriter.MetreUV);
                    outer.Reverse(); blue.Polygon(outer, -d, MeshWriter.MetreUV);
                }
                // The water running down the bottom: a strip just above the gelcoat.
                Vector3 w0 = c - up * (rad - 0.012f) - side * 0.1f, w1 = c - up * (rad - 0.012f) + side * 0.1f;
                if (k > 0)
                {
                    for (int i = 0; i < R; i++)
                    {
                        Vector2 uA = new Vector2(i / (float)R, (k - 1) * 0.25f), uB = new Vector2((i + 1) / (float)R, k * 0.25f);
                        blue.SmoothQuad(prevIn[i], prevIn[i + 1], curIn[i + 1], curIn[i], -prevN[i], -prevN[i + 1], -curN[i + 1], -curN[i],
                            uA, new Vector2(uB.x, uA.y), uB, new Vector2(uA.x, uB.y));
                        blue.SmoothQuad(prevOut[i + 1], prevOut[i], curOut[i], curOut[i + 1], prevN[i + 1], prevN[i], curN[i], curN[i + 1],
                            new Vector2(uB.x, uA.y), uA, new Vector2(uA.x, uB.y), uB);
                    }
                    float seg = (c - prevC).magnitude;
                    float vw1 = vw + seg;
                    if (k > 2) sheet.SmoothQuad(prevW0, prevW1, w1, w0, up, up, up, up, new Vector2(0f, vw), new Vector2(0.5f, vw), new Vector2(0.5f, vw1), new Vector2(0f, vw1));
                    vw = vw1;
                    length += seg;
                    // A joint between sections: a flange round the outside, and a post with a saddle under it. (The post
                    // goes down from the channel's own lowest point, in its frame: dropped straight down from the middle,
                    // as before, it came up inside a steep channel.)
                    if (length >= nextJoint && k < K - 2)
                    {
                        nextJoint = length + 1.6f;
                        _railPts.Clear();
                        for (int i = 0; i <= R; i++) _railPts.Add(c + curN[i] * (rad + wall + 0.012f));
                        Sweep(blue, _railPts, 0.016f, 8);
                        var under = c - up * (rad + wall + 0.03f);
                        float floor = GroundAt(under.x, under.z);
                        if (under.y - floor > 0.35f && Mathf.Abs(d.y) < 0.62f)
                        {
                            // The saddle: a curved strap round the lower part of the channel, on a cap plate.
                            _railPts.Clear();
                            for (int i = R / 2 - 4; i <= R / 2 + 4; i++) _railPts.Add(c + curN[i] * (rad + wall + 0.02f));
                            Sweep(steel, _railPts, 0.018f, 8);
                            var flat = new Vector3(d.x, 0f, d.z);
                            steel.Box(under + Vector3.down * 0.02f, new Vector3(0.2f, 0.03f, 0.2f), Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat : Vector3.forward), 0b111111, MeshWriter.MetreUV);
                            var foot = new Vector3(under.x, floor, under.z);
                            Tube(steel, foot, under + Vector3.down * 0.035f, 0.055f, 12);
                            steel.Box(foot + Vector3.up * 0.01f, new Vector3(0.22f, 0.02f, 0.22f), 0b111111, MeshWriter.MetreUV);
                            _capsules.Add(((foot + under) * 0.5f, 0.06f, under.y - floor));
                        }
                    }
                }
                if (k > 0 && k % 4 == 0)
                {
                    var a0 = Bz((k - 4) / (float)K);
                    _rotBoxes.Add(((a0 + c) * 0.5f - up * 0.25f, new Vector3(0.75f, 0.3f, (c - a0).magnitude + 0.1f), Mathf.Atan2(c.x - a0.x, c.z - a0.z) * Mathf.Rad2Deg));
                }
                prevIn = curIn; prevOut = curOut; prevN = curN; prevW0 = w0; prevW1 = w1; prevC = c;
            }
            Sweep(blue, rimA, wall * 0.9f, 10);
            Sweep(blue, rimB, wall * 0.9f, 10);
            // The water pouring off the end into the pool: a short sheet, falling from the lip.
            {
                var e = Bz(1f);
                var de = (Bz(1f) - Bz(0.98f)).normalized;
                var flat = new Vector3(de.x, 0f, de.z).normalized;
                var sidE = Vector3.Cross(Vector3.up, flat);
                var lip = e - Vector3.up * (rad - 0.012f);
                float drop = lip.y - PoolBiome.WaterLevel;
                if (drop > 0.02f)
                {
                    float tl = Mathf.Sqrt(2f * drop / 9.81f), vh = 1.8f;
                    const int S = 8;
                    for (int s = 0; s < S; s++)
                    {
                        float ta = tl * s / S, tb = tl * (s + 1) / S;
                        Vector3 a = lip + flat * (vh * ta) + Vector3.down * (4.905f * ta * ta), b = lip + flat * (vh * tb) + Vector3.down * (4.905f * tb * tb);
                        float ya = lip.y - a.y, yb = lip.y - b.y;
                        sheet.SmoothQuad(a - sidE * 0.1f, a + sidE * 0.1f, b + sidE * 0.13f, b - sidE * 0.13f, -flat, -flat, -flat, -flat,
                            new Vector2(0f, ya), new Vector2(0.5f, ya), new Vector2(0.5f, yb), new Vector2(0f, yb));
                    }
                    var land = lip + flat * (vh * tl);
                    Crown(new Vector3(land.x, PoolBiome.WaterLevel, land.z), 0.16f, 0.12f, 16);
                    LandingFoam(new Vector3(land.x, PoolBiome.WaterLevel + 0.004f, land.z), 0.05f, 0.7f, 20);
                    poolFittings.Add(new Vector4(land.x, PoolBiome.WaterLevel, land.z, 6f));
                    poolFittingDirs.Add(flat);
                }
            }
        }

        // ---------------- Wave pools ----------------

        /// <summary>A wave pool's chambers: three wide grated openings low in the deep end's wall, under the water.</summary>
        void WaveMaker(PoolFeature f)
        {
            var c = V3(f.centre);
            var n = Dir(f.yaw);                         // toward the beach (into the pool)
            var t = Vector3.Cross(Vector3.up, n);
            float floor = BasinFloorY(c.x + n.x * 0.5f, c.z + n.z * 0.5f);
            float y = Mathf.Max(floor + 0.4f, -0.7f);
            var dark = WMask(PoolMaterials.Dark ? PoolMaterials.Dark : TileMat, 1u);
            var metal = WMask(PoolMaterials.Grille ? PoolMaterials.Grille : ChromeMat, 1u);
            var q = Quaternion.LookRotation(n);
            for (int i = -1; i <= 1; i++)
            {
                var o = c + t * (i * f.size.x * 0.6f) + n * 0.004f + Vector3.up * y;
                float hw = 0.45f, hh = 0.22f;
                dark.Quad(o - t * hw - Vector3.up * hh, o + t * hw - Vector3.up * hh, o + t * hw + Vector3.up * hh, o - t * hw + Vector3.up * hh, n, MeshWriter.MetreUV);
                for (float s = -hw + 0.05f; s < hw; s += 0.08f)
                    metal.Box(o + t * s + n * 0.01f, new Vector3(0.018f, 2f * hh, 0.02f), q, 0b111111, MeshWriter.MetreUV);
            }
            poolFittings.Add(new Vector4(c.x, PoolBiome.WaterLevel, c.z, 4f));
            poolFittingDirs.Add(n * f.size.x);
        }
    }
}
