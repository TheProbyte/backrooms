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
            poolFittings.Add(new Vector4(land.x, water, land.z, 6f));
            poolFittingDirs.Add(n);
            Stain(noz + Vector3.down * 0.06f, n, 0.08f, 0.25f);
        }

        // ---------------- Slide towers ----------------

        /// <summary>
        /// A slide tower on the deck beside a plunge pool: four steel legs, a railed platform, a steep stair up the back, and
        /// a flume swinging out and down into the pool (Flume: the same build as the rebuilt slide).
        /// </summary>
        void SlideTower(PoolFeature f)
        {
            var c = V3(f.centre);
            var n = Dir(f.yaw);                         // toward the pool
            var t = Vector3.Cross(Vector3.up, n) * ((f.shape & 4) == 0 ? 1f : -1f);
            float H = f.height;
            var q = Quaternion.LookRotation(n);
            var white = WMask(PoolMaterials.PlasticWhite ? PoolMaterials.PlasticWhite : TileMat, 1u);
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            {
                var leg = c + n * (0.6f * sz) + t * (0.6f * sx);
                Rail(new List<Vector3> { leg, leg + Vector3.up * (H - 0.06f) }, 0.05f);
            }
            var pc = c + Vector3.up * (H - 0.04f);
            white.Box(pc, new Vector3(1.4f, 0.08f, 1.4f), q, 0b111111, MeshWriter.MetreUV);
            _rotBoxes.Add((pc, new Vector3(1.4f, 0.08f, 1.4f), q.eulerAngles.y));
            // Railings on the two sides; the front is the flume's, the back the stair's.
            foreach (float s in new[] { -0.67f, 0.67f })
            {
                var a = c + t * s - n * 0.67f + Vector3.up * H; var b = c + t * s + n * 0.67f + Vector3.up * H;
                Rail(new List<Vector3> { a, a + Vector3.up * 1.0f, b + Vector3.up * 1.0f, b }, 0.024f);
                _rotBoxes.Add(((a + b) * 0.5f + Vector3.up * 0.5f, new Vector3(0.06f, 1.0f, 1.34f), q.eulerAngles.y));
            }
            // The stair: steep, up from the deck behind the tower to the platform's back edge.
            int N = Mathf.CeilToInt(H / 0.2f);
            float run = 2.2f, rise = H / N, tread = run / N;
            var stepMat = WMask(PoolMaterials.Grille ? PoolMaterials.Grille : TileMat, 1u);
            for (int k = 0; k < N; k++)
            {
                float y = rise * (k + 1);
                var sc = c - n * (0.7f + run - tread * (k + 0.5f)) + Vector3.up * (y - 0.03f);
                stepMat.Box(sc, new Vector3(0.8f, 0.05f, tread + 0.02f), q, 0b111111, MeshWriter.MetreUV);
                _rotBoxes.Add((sc + Vector3.down * 0.1f, new Vector3(0.8f, 0.2f, tread + 0.02f), q.eulerAngles.y));
            }
            foreach (float s in new[] { -0.44f, 0.44f })
            {
                var bot = c - n * (0.7f + run) + t * s;
                var top = c - n * 0.7f + t * s + Vector3.up * H;
                Rail(new List<Vector3> { bot, bot + Vector3.up * 0.95f, top + Vector3.up * 0.95f, top + Vector3.up * 0.95f - n * 0.2f }, 0.022f);
                Rail(new List<Vector3> { bot, top }, 0.03f);              // the stringers
            }
            // The flume: off the platform's front, swinging out to the curl's side and down into the pool.
            var start = c + n * 0.72f + Vector3.up * (H + 0.02f);
            float toPool = f.size.x;
            var end = c + n * (toPool - f.size.y * 0.2f) + Vector3.up * (PoolBiome.WaterLevel + 0.25f);
            var p1 = start + n * 1.0f + t * 1.8f + Vector3.down * 0.5f;
            var p2 = end - n * 1.5f + t * 2.0f + Vector3.up * 0.8f;
            Flume(u => { float v = 1f - u; return v * v * v * start + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * end; }, ParkColour(f.shape));
        }

        /// <summary>A flume along a path (u 0..1): a glossy channel with a wall and rolled rims, water running down its
        /// bottom, steel posts with cradles.</summary>
        void Flume(System.Func<float, Vector3> Bz, Material mat)
        {
            var blue = WMask(mat, 1u);
            const int K = 44, R = 16;
            const float rad = 0.32f, wall = 0.035f, open = 80f * Mathf.Deg2Rad;
            var rimA = new List<Vector3>(K + 1); var rimB = new List<Vector3>(K + 1);
            Vector3[] prevIn = null, prevOut = null, prevN = null;
            Vector3 prevW0 = default, prevW1 = default; float vw = 0f;
            var sheet = SheetW;
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
                // The water running down the bottom: a narrow strip just above the gelcoat.
                Vector3 w0 = c - up * (rad - 0.012f) - side * 0.09f, w1 = c - up * (rad - 0.012f) + side * 0.09f;
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
                    float vw1 = vw + (c - Bz((k - 1) / (float)K)).magnitude;
                    sheet.SmoothQuad(prevW0, prevW1, w1, w0, up, up, up, up, new Vector2(0f, vw), new Vector2(0.5f, vw), new Vector2(0.5f, vw1), new Vector2(0f, vw1));
                    vw = vw1;
                }
                prevIn = curIn; prevOut = curOut; prevN = curN; prevW0 = w0; prevW1 = w1;
                if (k > 0 && k % 4 == 0)
                {
                    var a0 = Bz((k - 4) / (float)K);
                    _rotBoxes.Add(((a0 + c) * 0.5f + Vector3.down * 0.2f, new Vector3(0.75f, 0.35f, (c - a0).magnitude + 0.1f), Mathf.Atan2(c.x - a0.x, c.z - a0.z) * Mathf.Rad2Deg));
                }
            }
            Sweep(blue, rimA, wall * 0.9f, 10);
            Sweep(blue, rimB, wall * 0.9f, 10);
            foreach (float u in new[] { 0.33f, 0.62f, 0.86f })
            {
                var c = Bz(u);
                var d = (Bz(u + 0.01f) - Bz(u - 0.01f)).normalized;
                var side = Vector3.Cross(Vector3.up, d).normalized;
                var under = c + Vector3.down * (rad + wall + 0.02f);
                float floor = GroundAt(under.x, under.z);
                if (under.y - floor < 0.3f) continue;
                Rail(new List<Vector3> { new Vector3(under.x, floor, under.z), under }, 0.045f);
                Tube(WMask(ChromeMat, 1u), under - side * 0.3f, under + side * 0.3f, 0.025f, 10);
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
