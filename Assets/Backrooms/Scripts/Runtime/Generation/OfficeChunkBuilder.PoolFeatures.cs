using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The Poolrooms' larger pieces (2026-10-31, the architecture plan; planned in PoolPlan.Features): free-form pools cut
    /// into deck halls (their outline traced cell by cell on an 8 x 8 grid, marching-squares style: deck, a border of accent
    /// tiles, the curved pool wall, the floor and the water), sloping pool floors, barrel vaults, round halls' round roof
    /// openings, galleries with their stairs and railings, chrome ladders and rails,
    /// and the fittings of rooms with a purpose (changing stalls, showers, bleachers, a lifeguard's chair, a slide). Tile
    /// colours by room (ZoneTile).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Pool chunks: the second per-cell map (depth, gallery, lanes and drains, round opening), as poolMap.</summary>
        public Color32[] poolMap2;
        Material _archZone;

        // ---------------- Materials ----------------

        /// <summary>A room's tile colour (PoolMaterials.Zones): mostly sage, some cream, deeper mint or pale aqua.</summary>
        Material ZoneTile(Room r)
        {
            var zones = PoolMaterials.Zones;
            if (zones == null || zones.Length < 4 || r == null) return TileMat;
            var d = WorldGenerator.DistrictOf(_L.coord);
            uint h = (uint)(r.globalId * 73856093) ^ (uint)(d.x * 19349663) ^ (uint)(d.y * 83492791) ^ (uint)(_L.storey * 2654435761u);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            int k = (int)(h % 100);
            var m = k < 55 ? zones[0] : k < 72 ? zones[1] : k < 86 ? zones[2] : zones[3];
            return m ? m : TileMat;
        }
        Material ZoneAt(int x, int z) => ZoneTile(_L.RoomAt(Mathf.Clamp(x, 0, _L.N - 1), Mathf.Clamp(z, 0, _L.N - 1)));
        Material AccentMat => PoolMaterials.Accent ? PoolMaterials.Accent : TileMat;
        Material ChromeMat => PoolMaterials.Chrome ? PoolMaterials.Chrome : TileMat;

        // ---------------- Lookups ----------------

        bool FreePoolOf(int x, int z, out PoolFeature f)
        {
            int gid = _L.RoomAt(x, z).globalId;
            foreach (var p in _L.poolFeatures) if (p.kind == PoolFeatureKind.FreePool && p.room == gid) { f = p; return true; }
            f = default; return false;
        }

        bool FeatureOf(PoolFeatureKind kind, int x, int z, out PoolFeature f)
        {
            int gid = _L.RoomAt(x, z).globalId;
            foreach (var p in _L.poolFeatures) if (p.kind == kind && p.room == gid) { f = p; return true; }
            f = default; return false;
        }

        float BasinFloorY(float mx, float mz) => _L.BasinFloorAt(mx, mz);

        static Quaternion Yaw(float featureYaw) => Quaternion.Euler(0f, -featureYaw, 0f);
        static Vector3 Dir(float featureYaw) { float a = featureYaw * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
        static Vector3 V3(Vector2 v, float y = 0f) => new Vector3(v.x, y, v.y);

        // ---------------- Clipping (marching squares on a polygon) ----------------

        // Scratch for the clipping: per builder (chunks are built on several worker threads at once).
        readonly List<Vector3> s_poly = new List<Vector3>(8), s_in = new List<Vector3>(8), s_out = new List<Vector3>(8), s_tmp = new List<Vector3>(8);
        readonly List<float> s_val = new List<float>(8), s_valIn = new List<float>(8), s_valTmp = new List<float>(8);

        /// <summary>The part of a polygon where v &lt; 0 (keepNeg) or v &gt;= 0, the values interpolated along its edges.</summary>
        static void Clip(List<Vector3> poly, List<float> vals, bool keepNeg, List<Vector3> outPoly, List<float> outVals)
        {
            outPoly.Clear(); outVals?.Clear();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float a = vals[i], b = vals[j];
                bool ia = keepNeg ? a < 0f : a >= 0f, ib = keepNeg ? b < 0f : b >= 0f;
                if (ia) { outPoly.Add(poly[i]); outVals?.Add(a); }
                if (ia != ib)
                {
                    float t = a / (a - b);
                    outPoly.Add(Vector3.Lerp(poly[i], poly[j], t)); outVals?.Add(0f);
                }
            }
        }

        // ---------------- Free pools ----------------

        /// <summary>
        /// One cell of a room with a free pool: deck, the accent border round the pool, the pool's curved wall, its floor
        /// (sloping if it does) and the water, traced on an 8 x 8 grid; colliders on a 4 x 4 one.
        /// </summary>
        void FreePoolCell(int x, int z, PoolFeature f)
        {
            const int S = 8;
            const float band = 0.15f;
            float s = C / S, x0 = x * C, z0 = z * C;
            var deck = WMask(ZoneAt(x, z), 1u);
            var accent = WMask(AccentMat, 1u);
            var water = WMask(WaterMat, 1u);
            var v = new float[S + 1, S + 1];
            bool anyIn = false, allOut = true;
            for (int i = 0; i <= S; i++)
            for (int j = 0; j <= S; j++)
            {
                v[i, j] = PoolBiome.ShapeSD(f, new Vector2(x0 + i * s, z0 + j * s));
                if (v[i, j] < band) allOut = false;
                if (v[i, j] < 0f) anyIn = true;
            }
            if (allOut)
            {
                deck.Quad(new Vector3(x0, 0, z0), new Vector3(x0, 0, z0 + C), new Vector3(x0 + C, 0, z0 + C), new Vector3(x0 + C, 0, z0), Vector3.up, MeshWriter.MetreUV);
                _boxes.Add((new Vector3(x0 + C * 0.5f, -0.15f, z0 + C * 0.5f), new Vector3(C, 0.3f, C)));
                return;
            }
            float wy = PoolBiome.WaterLevel;
            for (int i = 0; i < S; i++)
            for (int j = 0; j < S; j++)
            {
                s_poly.Clear(); s_val.Clear();
                s_poly.Add(new Vector3(x0 + i * s, 0, z0 + j * s)); s_val.Add(v[i, j]);
                s_poly.Add(new Vector3(x0 + i * s, 0, z0 + (j + 1) * s)); s_val.Add(v[i, j + 1]);
                s_poly.Add(new Vector3(x0 + (i + 1) * s, 0, z0 + (j + 1) * s)); s_val.Add(v[i + 1, j + 1]);
                s_poly.Add(new Vector3(x0 + (i + 1) * s, 0, z0 + j * s)); s_val.Add(v[i + 1, j]);
                // Inside: the pool floor (sloping) and the water over it.
                Clip(s_poly, s_val, true, s_in, null);
                if (s_in.Count >= 3)
                {
                    s_tmp.Clear();
                    foreach (var p in s_in) s_tmp.Add(new Vector3(p.x, BasinFloorY(p.x, p.z), p.z));
                    deck.Polygon(s_tmp, Vector3.up, MeshWriter.MetreUV);
                    s_tmp.Clear();
                    foreach (var p in s_in) s_tmp.Add(new Vector3(p.x, wy, p.z));
                    water.Polygon(s_tmp, Vector3.up, MeshWriter.MetreUV);
                }
                // Outside: the accent border, then the deck.
                Clip(s_poly, s_val, false, s_out, s_valIn);
                if (s_out.Count >= 3)
                {
                    s_valTmp.Clear(); foreach (var q in s_valIn) s_valTmp.Add(q - band);
                    Clip(s_out, s_valTmp, true, s_tmp, null);
                    if (s_tmp.Count >= 3) accent.Polygon(s_tmp, Vector3.up, MeshWriter.MetreUV);
                    Clip(s_out, s_valTmp, false, s_tmp, null);
                    if (s_tmp.Count >= 3) deck.Polygon(s_tmp, Vector3.up, MeshWriter.MetreUV);
                }
                // The pool's wall where the outline crosses this square: accent tiles at the top, then the room's.
                if (anyIn && s_in.Count >= 3 && s_out.Count >= 3)
                {
                    Vector3 a = default, b = default; int found = 0;
                    for (int k = 0; k < 4 && found < 2; k++)
                    {
                        float va = s_val[k], vb = s_val[(k + 1) % 4];
                        if ((va < 0f) == (vb < 0f)) continue;
                        var p = Vector3.Lerp(s_poly[k], s_poly[(k + 1) % 4], va / (va - vb));
                        if (found == 0) a = p; else b = p;
                        found++;
                    }
                    if (found == 2 && (a - b).sqrMagnitude > 1e-8f)
                    {
                        var mid = (a + b) * 0.5f;
                        float e = 0.02f;
                        var grad = new Vector3(PoolBiome.ShapeSD(f, new Vector2(mid.x + e, mid.z)) - PoolBiome.ShapeSD(f, new Vector2(mid.x - e, mid.z)), 0f,
                                               PoolBiome.ShapeSD(f, new Vector2(mid.x, mid.z + e)) - PoolBiome.ShapeSD(f, new Vector2(mid.x, mid.z - e)));
                        var n = -grad.normalized;                                // the wall faces into the pool
                        float fa = BasinFloorY(a.x, a.z), fb = BasinFloorY(b.x, b.z);
                        const float lip = 0.2f;
                        accent.Quad(a + Vector3.down * lip, b + Vector3.down * lip, b, a, n, MeshWriter.MetreUV);
                        deck.Quad(new Vector3(a.x, fa, a.z), new Vector3(b.x, fb, b.z), b + Vector3.down * lip, a + Vector3.down * lip, n, MeshWriter.MetreUV);
                    }
                }
            }
            // Colliders: a 4 x 4 grid of deck and pool floor, merged along rows.
            const int K = 4;
            float cs = C / K;
            for (int j = 0; j < K; j++)
            {
                int start = 0;
                bool Kind(int i) => PoolBiome.ShapeSD(f, new Vector2(x0 + (i + 0.5f) * cs, z0 + (j + 0.5f) * cs)) < 0f;
                for (int i = 1; i <= K; i++)
                {
                    if (i < K && Kind(i) == Kind(start)) continue;
                    float cx = x0 + (start + i) * 0.5f * cs, cz = z0 + (j + 0.5f) * cs;
                    if (Kind(start))
                    {
                        float fy = BasinFloorY(cx, cz);
                        _boxes.Add((new Vector3(cx, fy - 0.15f, cz), new Vector3((i - start) * cs, 0.3f, cs)));
                    }
                    else
                    {
                        float deep = -BasinFloorY(cx, cz) + 0.3f;
                        _boxes.Add((new Vector3(cx, -deep * 0.5f, cz), new Vector3((i - start) * cs, Mathf.Max(0.3f, deep), cs)));
                    }
                    start = i;
                }
            }
        }

        /// <summary>A free pool's way in: three steps at its shallow end, with a chrome handrail. Built by the chunk its centre is in.</summary>
        void FreePoolSteps(PoolFeature f)
        {
            var q = Yaw(f.yaw);
            float edgeX = f.shape == 1 ? -f.radius : -f.size.x;
            var edge = V3(f.centre) + q * new Vector3(edgeX, 0f, 0f);
            float depth = -BasinFloorY(edge.x + Dir(f.yaw).x * 0.6f, edge.z + Dir(f.yaw).z * 0.6f);
            float rise = depth / PoolBiome.Steps;
            var w = WMask(ZoneAt(Mathf.FloorToInt(f.centre.x / C), Mathf.FloorToInt(f.centre.y / C)), 1u);
            for (int k = 1; k < PoolBiome.Steps; k++)
            {
                float run = (PoolBiome.Steps - k) * PoolBiome.StepRun, top = -depth + rise * k;
                var c = edge + q * new Vector3(run * 0.5f + 0.02f, 0f, 0f) + Vector3.up * ((-depth + top) * 0.5f);
                var size = new Vector3(run, top + depth, 1.0f);
                w.Box(c, size, q, 0b111111, MeshWriter.MetreUV);
                _rotBoxes.Add((c, size, -f.yaw));
            }
            // A handrail down the steps' side.
            var side = q * new Vector3(0f, 0f, 0.55f);
            var top0 = edge - q * new Vector3(0.35f, 0f, 0f) + side;
            var bottom = edge + q * new Vector3(PoolBiome.Steps * PoolBiome.StepRun + 0.1f, 0f, 0f) + side;
            Rail(new List<Vector3> { top0, top0 + Vector3.up * 0.9f, edge + side + Vector3.up * 0.9f, bottom + Vector3.up * (0.9f - depth), bottom + Vector3.down * depth }, 0.022f);
        }

        // ---------------- Tubes (chrome) ----------------

        readonly List<Vector3> _railPts = new List<Vector3>(48);

        /// <summary>
        /// A chrome tube along a polyline, its corners bent round as a pipe bender does (radius up to 9 cm), swept as one
        /// smooth tube: one ring at every point, carried along the path without twisting (parallel transport), so there
        /// are no gaps or creases at the joints (2026-10-31: straight pieces butted at the corners showed cracks and
        /// pinches the owner saw as glitches).
        /// </summary>
        void Rail(List<Vector3> pts, float r, Material mat = null)
        {
            var w = WMask(mat ? mat : ChromeMat, 1u);
            if (pts.Count < 3) { if (pts.Count == 2) { Tube(w, pts[0], pts[1], r); Cap(w, pts[0], pts[0] - pts[1], r); Cap(w, pts[1], pts[1] - pts[0], r); } return; }
            _railPts.Clear();
            _railPts.Add(pts[0]);
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                Vector3 p = pts[i], da = pts[i - 1] - p, db = pts[i + 1] - p;
                float la = da.magnitude, lb = db.magnitude;
                if (la < 1e-4f || lb < 1e-4f) continue;
                da /= la; db /= lb;
                float cos = Vector3.Dot(da, db);
                if (cos < -0.9995f) { _railPts.Add(p); continue; }               // straight on
                float half = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * 0.5f;        // half the corner's angle
                // The bend: tangent to both legs, as round as the legs leave room for.
                float reach = Mathf.Min(0.45f * la, 0.45f * lb, 0.09f / Mathf.Tan(half));
                float bend = reach * Mathf.Tan(half);
                Vector3 pa = p + da * reach, pb = p + db * reach, centre = p + (da + db).normalized * (bend / Mathf.Sin(half));
                const int K = 6;
                for (int k = 0; k <= K; k++) _railPts.Add(centre + Vector3.Slerp(pa - centre, pb - centre, k / (float)K));
            }
            _railPts.Add(pts[pts.Count - 1]);
            Sweep(w, _railPts, r);
            Cap(w, _railPts[0], _railPts[0] - _railPts[1], r);
            Cap(w, _railPts[_railPts.Count - 1], _railPts[_railPts.Count - 1] - _railPts[_railPts.Count - 2], r);
        }

        /// <summary>A tube through points (corners already rounded), smooth shaded, its rings carried without twisting.</summary>
        static void Sweep(MeshWriter w, List<Vector3> pts, float r, int sides = 10)
        {
            int n = pts.Count;
            Vector3 Seg(int i) => (pts[Mathf.Min(i + 1, n - 1)] - pts[i]).normalized;
            Vector3 prevT = Seg(0);
            Vector3 side = Vector3.Cross(prevT, Mathf.Abs(prevT.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var ringA = new Vector3[sides + 1]; var ringB = new Vector3[sides + 1];
            float v = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 tan = i == 0 ? Seg(0) : i == n - 1 ? Seg(n - 2) : (Seg(i - 1) + Seg(i)).normalized;
                if (tan.sqrMagnitude < 1e-8f) tan = prevT;
                side = Quaternion.FromToRotation(prevT, tan) * side;          // parallel transport: no twist
                side = Vector3.ProjectOnPlane(side, tan).normalized;
                prevT = tan;
                var up = Vector3.Cross(tan, side);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    ringB[s] = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                }
                if (i > 0)
                {
                    float v1 = v + (pts[i] - pts[i - 1]).magnitude;
                    for (int s = 0; s < sides; s++)
                    {
                        float u0 = s / (float)sides, u1 = (s + 1) / (float)sides;
                        w.SmoothQuad(pts[i - 1] + ringA[s] * r, pts[i - 1] + ringA[s + 1] * r, pts[i] + ringB[s + 1] * r, pts[i] + ringB[s] * r,
                            ringA[s], ringA[s + 1], ringB[s + 1], ringB[s],
                            new Vector2(u0, v), new Vector2(u1, v), new Vector2(u1, v1), new Vector2(u0, v1));
                    }
                    v = v1;
                }
                var tmp = ringA; ringA = ringB; ringB = tmp;
            }
        }

        /// <summary>A tube's open end closed (a flat disc facing out).</summary>
        static void Cap(MeshWriter w, Vector3 at, Vector3 outward, float r)
        {
            if (outward.sqrMagnitude < 1e-8f) return;
            w.Disc(at, r, 10, outward.normalized, MeshWriter.MetreUV);
        }

        static void Tube(MeshWriter w, Vector3 a, Vector3 b, float r, int sides = 8)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            d /= len;
            var t = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var u = Vector3.Cross(d, t);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var n0 = t * Mathf.Cos(a0) + u * Mathf.Sin(a0);
                var n1 = t * Mathf.Cos(a1) + u * Mathf.Sin(a1);
                w.SmoothQuad(a + n0 * r, a + n1 * r, b + n1 * r, b + n0 * r, n0, n1, n1, n0,
                    new Vector2(i / (float)sides, 0f), new Vector2((i + 1) / (float)sides, 0f), new Vector2((i + 1) / (float)sides, len), new Vector2(i / (float)sides, len));
            }
        }

        // ---------------- Vaults ----------------

        /// <summary>
        /// One cell's share of a barrel vault (a corridor's ceiling as a half cylinder springing from the side walls), smooth,
        /// tiles following the curve; cut along the crown where the cell has a slot opening.
        /// </summary>
        void VaultCell(int x, int z, PoolFeature f, bool slot)
        {
            var w = WMask(ZoneAt(x, z), 1u);
            bool alongX = Mathf.Abs(f.yaw) < 1f;
            var b = f.bounds;
            float lo = (alongX ? b.yMin : b.xMin) + T * 0.5f, hi = (alongX ? b.yMax : b.xMax) - T * 0.5f, mid = (lo + hi) * 0.5f, r = (hi - lo) * 0.5f;
            float c0 = Mathf.Max(lo, (alongX ? z : x) * C), c1 = Mathf.Min(hi, ((alongX ? z : x) + 1) * C);
            float a0 = (alongX ? x : z) * C, a1 = a0 + C;
            if (c1 <= c0) return;
            int K = Mathf.Max(2, Mathf.CeilToInt((c1 - c0) / 0.08f));
            float spring = f.height;
            Vector3 P(float along, float across, float y) => alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);
            float Y(float u) { float q = Mathf.Clamp((u - mid) / r, -1f, 1f); return spring + r * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
            Vector3 N(float u) { float q = Mathf.Clamp((u - mid) / r, -1f, 1f); float y = Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); return alongX ? new Vector3(0f, -y, -q) : new Vector3(-q, -y, 0f); }
            float Arc(float u) => r * Mathf.Asin(Mathf.Clamp((u - mid) / r, -1f, 1f));
            for (int k = 0; k < K; k++)
            {
                float u0 = Mathf.Lerp(c0, c1, k / (float)K), u1 = Mathf.Lerp(c0, c1, (k + 1) / (float)K);
                if (slot && Mathf.Abs((u0 + u1) * 0.5f - mid) < 0.3f) continue;
                float y0 = Y(u0), y1 = Y(u1);
                w.SmoothQuad(P(a0, u0, y0), P(a1, u0, y0), P(a1, u1, y1), P(a0, u1, y1), N(u0), N(u0), N(u1), N(u1),
                    new Vector2(a0, Arc(u0)), new Vector2(a1, Arc(u0)), new Vector2(a1, Arc(u1)), new Vector2(a0, Arc(u1)));
            }
        }

        // ---------------- Round halls ----------------

        /// <summary>A ceiling cell under a round opening: the ceiling with the circle cut out of it.</summary>
        void DiscCeilingCell(int x, int z, float h, Vector2 centre, float radius)
        {
            var w = WMask(ZoneAt(x, z), 1u);
            const int S = 8;
            float s = C / S, x0 = x * C, z0 = z * C;
            for (int i = 0; i < S; i++)
            for (int j = 0; j < S; j++)
            {
                s_poly.Clear(); s_val.Clear();
                void Add(float px, float pz) { s_poly.Add(new Vector3(px, h, pz)); s_val.Add((new Vector2(px, pz) - centre).magnitude - radius); }
                Add(x0 + i * s, z0 + j * s); Add(x0 + (i + 1) * s, z0 + j * s); Add(x0 + (i + 1) * s, z0 + (j + 1) * s); Add(x0 + i * s, z0 + (j + 1) * s);
                Clip(s_poly, s_val, false, s_out, null);
                if (s_out.Count >= 3) w.Polygon(s_out, Vector3.down, MeshWriter.MetreUV);
            }
        }

        /// <summary>A round hall's opening: the round light well up from its ceiling and the sky over it (by the chunk its centre is in).</summary>
        void RoundHallWell(PoolFeature f, float h)
        {
            var tile = WMask(ZoneAt(Mathf.FloorToInt(f.centre.x / C), Mathf.FloorToInt(f.centre.y / C)), 1u);
            var sky = WMask(SkyMat, 1u);
            float r = f.size.x, top = h + PoolBiome.ShaftHeight;
            var c = V3(f.centre);
            const int K = 48;
            float circ = 2f * Mathf.PI * r;
            for (int i = 0; i < K; i++)
            {
                float a0 = i * Mathf.PI * 2f / K, a1 = (i + 1) * Mathf.PI * 2f / K;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                tile.SmoothQuad(c + d0 * r + Vector3.up * h, c + d1 * r + Vector3.up * h, c + d1 * r + Vector3.up * top, c + d0 * r + Vector3.up * top, -d0, -d1, -d1, -d0,
                    new Vector2(circ * i / K, h), new Vector2(circ * (i + 1) / K, h), new Vector2(circ * (i + 1) / K, top), new Vector2(circ * i / K, top));
                sky.Quad(c + Vector3.up * top, c + d0 * r + Vector3.up * top, c + d1 * r + Vector3.up * top, c + d1 * r + Vector3.up * top, Vector3.down, MeshWriter.MetreUV);
            }
        }

        // ---------------- Galleries ----------------

        /// <summary>A gallery cell (the ring round a tall hall): its slab, its open edge's face and railing.</summary>
        void GalleryCell(int x, int z, float g)
        {
            var w = WMask(ZoneAt(x, z), 1u);
            float x0 = x * C, z0 = z * C, x1 = x0 + C, z1 = z0 + C, under = g - 0.2f;
            w.Quad(new Vector3(x0, g, z0), new Vector3(x0, g, z1), new Vector3(x1, g, z1), new Vector3(x1, g, z0), Vector3.up, MeshWriter.MetreUV);
            w.Quad(new Vector3(x0, under, z0), new Vector3(x1, under, z0), new Vector3(x1, under, z1), new Vector3(x0, under, z1), Vector3.down, MeshWriter.MetreUV);
            _boxes.Add((new Vector3(x0 + C * 0.5f, g - 0.1f, z0 + C * 0.5f), new Vector3(C, 0.2f, C)));
            var room = _L.RoomAt(x, z);
            FeatureOf(PoolFeatureKind.Gallery, x, z, out var gf);
            for (int side = 0; side < 4; side++)
            {
                int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                // Open toward the hall: a neighbour in the same room without a gallery.
                bool open;
                if (_L.InChunk(nx, nz)) open = _L.roomOf[nx, nz] == _L.roomOf[x, z] && _L.pool[nx, nz].gallery <= 0f;
                else { var c = PoolAt(nx, nz, out var o, out int lx, out int lz); open = c.HasValue && c.Value.gallery <= 0f && SameRoom(o, lx, lz, _L, x, z); }
                if (!open) continue;
                Vector3 a = side == 0 ? new Vector3(x0, 0, z0) : side == 1 ? new Vector3(x1, 0, z1) : side == 2 ? new Vector3(x1, 0, z0) : new Vector3(x0, 0, z1);
                Vector3 b = side == 0 ? new Vector3(x0, 0, z1) : side == 1 ? new Vector3(x1, 0, z0) : side == 2 ? new Vector3(x0, 0, z0) : new Vector3(x1, 0, z1);
                var n = side == 0 ? Vector3.left : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.forward;
                w.Quad(a + Vector3.up * under, b + Vector3.up * under, b + Vector3.up * g, a + Vector3.up * g, n, MeshWriter.MetreUV);
                // The stair arrives here: no railing on this edge.
                if (gf.kind == PoolFeatureKind.Gallery && StairArrives(gf, (a + b) * 0.5f)) continue;
                var inset = -n * 0.06f;
                Rail(new List<Vector3> { a + inset + Vector3.up * g, a + inset + Vector3.up * (g + 1.0f) }, 0.025f);
                Rail(new List<Vector3> { a + inset + Vector3.up * (g + 1.0f), b + inset + Vector3.up * (g + 1.0f) }, 0.025f);
                Rail(new List<Vector3> { a + inset + Vector3.up * (g + 0.5f), b + inset + Vector3.up * (g + 0.5f) }, 0.015f);
                var mid = (a + b) * 0.5f + inset;
                _rotBoxes.Add((mid + Vector3.up * (g + 0.5f), new Vector3(0.06f, 1.0f, C), side < 2 ? 0f : 90f));
            }
        }

        bool StairArrives(PoolFeature gf, Vector3 edgeMid)
        {
            bool alongX = Mathf.Abs(gf.yaw) < 1f;
            var top = V3(gf.centre) + (alongX ? Vector3.right : Vector3.forward) * gf.size.x;
            return (new Vector2(edgeMid.x - top.x, edgeMid.z - top.z)).magnitude < 0.7f;
        }

        /// <summary>The gallery's stair: a straight flight inside one long wall, up to the ring's far corner, with a railing.</summary>
        void GalleryStair(PoolFeature gf)
        {
            bool alongX = Mathf.Abs(gf.yaw) < 1f;
            var dir = alongX ? Vector3.right : Vector3.forward;
            var side = alongX ? Vector3.forward : Vector3.right;
            int steps = Mathf.RoundToInt(gf.height / 0.2f);
            float tread = gf.size.x / steps, rise = gf.height / steps;
            var w = WMask(ZoneAt(Mathf.FloorToInt(gf.centre.x / C), Mathf.FloorToInt(gf.centre.y / C)), 1u);
            var foot = V3(gf.centre);
            for (int k = 0; k < steps; k++)
            {
                float top = rise * (k + 1);
                var c = foot + dir * (tread * (k + 0.5f)) + Vector3.up * (top * 0.5f);
                var size = alongX ? new Vector3(tread, top, C) : new Vector3(C, top, tread);
                w.Box(c, size, 0b111111, MeshWriter.MetreUV);
                _boxes.Add((c, size));
            }
            // The railing on the hall side (toward the room's middle).
            int s = gf.shape;
            var toMiddle = (s == 0 || s == 2) ? side : -side;
            var r0 = foot + toMiddle * (C * 0.5f - 0.06f);
            var r1 = r0 + dir * gf.size.x;
            Rail(new List<Vector3> { r0, r0 + Vector3.up * 1.0f, r1 + Vector3.up * (gf.height + 1.0f), r1 + Vector3.up * gf.height }, 0.025f);
        }

        // ---------------- Fittings ----------------

        /// <summary>A pool ladder: two chrome rails from the deck, over the edge and down into the water, with treads.</summary>
        void Ladder(PoolFeature f)
        {
            var at = V3(f.centre);
            var n = Dir(f.yaw);                         // toward the water
            var t = Vector3.Cross(Vector3.up, n);
            float floor = BasinFloorY(at.x + n.x * 0.4f, at.z + n.z * 0.4f);
            foreach (float s in new[] { -0.23f, 0.23f })
            {
                var b = at + t * s;
                var pts = new List<Vector3>
                {
                    b - n * 0.32f, b - n * 0.32f + Vector3.up * 0.75f, b - n * 0.22f + Vector3.up * 0.92f, b - n * 0.02f + Vector3.up * 0.92f,
                    b + n * 0.12f + Vector3.up * 0.75f, b + n * 0.14f + Vector3.up * 0.2f, b + n * 0.14f + Vector3.up * (floor + 0.05f)
                };
                Rail(pts, 0.022f);
                _rotBoxes.Add((b - n * 0.32f + Vector3.up * 0.4f, new Vector3(0.05f, 0.8f, 0.05f), 0f));
            }
            var w = WMask(ChromeMat, 1u);
            for (float y = -0.18f; y > floor + 0.08f; y -= 0.25f)
                Tube(w, at + n * 0.17f - t * 0.23f + Vector3.up * y, at + n * 0.17f + t * 0.23f + Vector3.up * y, 0.018f);
        }

        void Lifeguard(PoolFeature f)
        {
            var at = V3(f.centre);
            var fwd = Dir(f.yaw + 90f);                 // facing the pool (from its side, the pool is at +90)
            var t = Vector3.Cross(Vector3.up, fwd);
            float seat = 1.7f;
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            {
                var foot = at + t * (0.42f * sx) + fwd * (0.45f * sz);
                var top = at + t * (0.26f * sx) + fwd * (0.18f * sz) + Vector3.up * seat;
                Rail(new List<Vector3> { foot, top }, 0.025f);
            }
            for (float y = 0.4f; y < seat - 0.1f; y += 0.4f)
            {
                float k = y / seat;
                var a = at + t * Mathf.Lerp(-0.42f, -0.26f, k) + fwd * Mathf.Lerp(0.45f, 0.18f, k) + Vector3.up * y;
                var b = at + t * Mathf.Lerp(0.42f, 0.26f, k) + fwd * Mathf.Lerp(0.45f, 0.18f, k) + Vector3.up * y;
                Rail(new List<Vector3> { a, b }, 0.018f);
            }
            var plastic = WMask(PoolMaterials.PlasticWhite ? PoolMaterials.PlasticWhite : TileMat, 1u);
            var q = Quaternion.LookRotation(fwd);
            plastic.Box(at + Vector3.up * (seat + 0.03f), new Vector3(0.6f, 0.06f, 0.45f), q, 0b111111, MeshWriter.MetreUV);
            plastic.Box(at - fwd * 0.2f + Vector3.up * (seat + 0.35f), new Vector3(0.6f, 0.6f, 0.05f), q, 0b111111, MeshWriter.MetreUV);
            _rotBoxes.Add((at + Vector3.up * (seat * 0.5f), new Vector3(0.8f, seat, 0.9f), q.eulerAngles.y));
        }

        void Bleachers(PoolFeature f)
        {
            var at = V3(f.centre);
            var n = Dir(f.yaw);                         // facing the pool
            var q = Quaternion.LookRotation(n);
            int cx = Mathf.Clamp(Mathf.FloorToInt(at.x / C), 0, _L.N - 1), cz = Mathf.Clamp(Mathf.FloorToInt(at.z / C), 0, _L.N - 1);
            var w = WMask(ZoneAt(cx, cz), 1u);
            float len = f.size.x * 2f;
            for (int k = 0; k < 3; k++)
            {
                float h = 0.42f * (k + 1), back = 0.62f * k;
                var c = at - n * (back + 0.31f) + n * 0.6f + Vector3.up * (h * 0.5f);
                var size = new Vector3(len, h, 0.62f);
                w.Box(c, size, q, 0b111111, MeshWriter.MetreUV);
                _rotBoxes.Add((c, size, q.eulerAngles.y));
            }
        }

        void Slide(PoolFeature f)
        {
            var start = V3(f.centre, f.height);
            var n = Dir(f.yaw);
            var t = Vector3.Cross(Vector3.up, n) * (f.shape == 0 ? 1f : -1f);
            var end = V3(f.centre) + n * 2.6f + t * 1.3f + Vector3.up * (PoolBiome.WaterLevel + 0.35f);
            var p1 = start + n * 1.4f; var p2 = end - (n * 0.2f + t * 0.9f) + Vector3.up * 0.4f;
            Vector3 B(float u) { float v = 1f - u; return v * v * v * start + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * end; }
            var blue = WMask(PoolMaterials.PlasticBlue ? PoolMaterials.PlasticBlue : TileMat, 1u);
            const int K = 28, R = 12;
            const float rad = 0.36f;
            for (int k = 0; k < K; k++)
            {
                float u0 = k / (float)K, u1 = (k + 1) / (float)K;
                var a = B(u0); var b = B(u1);
                var da = (B(Mathf.Min(1f, u0 + 0.01f)) - B(Mathf.Max(0f, u0 - 0.01f))).normalized;
                var db = (B(Mathf.Min(1f, u1 + 0.01f)) - B(Mathf.Max(0f, u1 - 0.01f))).normalized;
                var sa = Vector3.Cross(Vector3.up, da).normalized; var sb = Vector3.Cross(Vector3.up, db).normalized;
                var ua = Vector3.Cross(da, sa); var ub = Vector3.Cross(db, sb);
                for (int i = 0; i < R; i++)
                {
                    // A half pipe open at the top: from one side round the bottom to the other.
                    float th0 = Mathf.PI * i / R, th1 = Mathf.PI * (i + 1) / R;
                    var na0 = sa * Mathf.Cos(th0) - ua * Mathf.Sin(th0); var na1 = sa * Mathf.Cos(th1) - ua * Mathf.Sin(th1);
                    var nb0 = sb * Mathf.Cos(th0) - ub * Mathf.Sin(th0); var nb1 = sb * Mathf.Cos(th1) - ub * Mathf.Sin(th1);
                    var A0 = a + na0 * rad; var A1 = a + na1 * rad; var B0 = b + nb0 * rad; var B1 = b + nb1 * rad;
                    var uvA = new Vector2(i / (float)R, u0 * 6f); var uvB = new Vector2((i + 1) / (float)R, u1 * 6f);
                    blue.SmoothQuad(A0, A1, B1, B0, -na0, -na1, -nb1, -nb0, uvA, new Vector2(uvB.x, uvA.y), uvB, new Vector2(uvA.x, uvB.y));   // inside
                    blue.SmoothQuad(A1, A0, B0, B1, na1, na0, nb0, nb1, new Vector2(uvB.x, uvA.y), uvA, new Vector2(uvA.x, uvB.y), uvB);        // outside
                }
                if (k % 4 == 0) _rotBoxes.Add(((a + b) * 0.5f + Vector3.down * 0.2f, new Vector3(0.8f, 0.4f, (b - a).magnitude + 0.1f), Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg));
            }
            // Supports from the floor.
            foreach (float u in new[] { 0.45f, 0.8f })
            {
                var p = B(u);
                float floor = BasinFloorY(p.x, p.z);
                Rail(new List<Vector3> { new Vector3(p.x, floor, p.z), p + Vector3.down * rad }, 0.04f);
            }
        }

        void Changing(PoolFeature f)
        {
            var at = V3(f.centre);
            var n = Dir(f.yaw);                         // out from the wall into the room
            var t = Vector3.Cross(Vector3.up, n);
            int cx = Mathf.Clamp(Mathf.FloorToInt((at.x + n.x * 0.6f) / C), 0, _L.N - 1), cz = Mathf.Clamp(Mathf.FloorToInt((at.z + n.z * 0.6f) / C), 0, _L.N - 1);
            var w = WMask(ZoneAt(cx, cz), 1u);
            var q = Quaternion.LookRotation(n);
            float half = f.size.x, depth = Mathf.Min(1.1f, f.size.y * 0.45f);
            // Stall partitions, a stall every metre or so, raised off the floor.
            int stalls = Mathf.Max(1, Mathf.FloorToInt(half * 2f / 0.95f));
            for (int i = 0; i <= stalls; i++)
            {
                float s = -half + i * (half * 2f / stalls);
                var c = at + t * s + n * (depth * 0.5f) + Vector3.up * 1.1f;
                var size = new Vector3(0.05f, 1.8f, depth);
                w.Box(c, size, q, 0b111111, MeshWriter.MetreUV);
                _rotBoxes.Add((c, size, q.eulerAngles.y));
            }
            // A tiled bench across the room from the stalls.
            if (f.size.y > depth + 1.6f)
            {
                var c = at + n * (depth + 1.0f) + Vector3.up * 0.22f;
                var size = new Vector3(half * 1.6f, 0.44f, 0.4f);
                w.Box(c, size, q, 0b111111, MeshWriter.MetreUV);
                _rotBoxes.Add((c, size, q.eulerAngles.y));
            }
        }

        void Showers(PoolFeature f)
        {
            var at = V3(f.centre);
            var n = Dir(f.yaw);
            var t = Vector3.Cross(Vector3.up, n);
            float half = f.size.x;
            int heads = Mathf.Max(1, Mathf.FloorToInt(half * 2f / 1.0f));
            for (int i = 0; i < heads; i++)
            {
                float s = -half + (i + 0.5f) * (half * 2f / heads);
                var b = at + t * s;
                // A pipe up the wall, the arm out and the head; a valve lower down.
                Rail(new List<Vector3> { b + n * 0.03f + Vector3.up * 1.15f, b + n * 0.03f + Vector3.up * 2.05f, b + n * 0.28f + Vector3.up * 2.0f }, 0.016f);
                var w = WMask(ChromeMat, 1u);
                w.Frustum(b + n * 0.3f + Vector3.up * 1.92f, 0.07f, 0.025f, 0.08f, 12, true, MeshWriter.MetreUV);
                w.Disc(b + n * 0.3f + Vector3.up * 1.92f, 0.07f, 12, Vector3.down, MeshWriter.MetreUV);
                Tube(w, b + n * 0.02f + Vector3.up * 1.15f, b + n * 0.09f + Vector3.up * 1.15f, 0.03f);
            }
        }

        // ---------------- Per chunk ----------------

        /// <summary>Builds the pieces this chunk owns (by where their centre is) or reaches (cells it has).</summary>
        void BuildPoolFeatures()
        {
            float span = _L.N * C;
            bool Mine(Vector2 p) => p.x >= 0f && p.y >= 0f && p.x < span && p.y < span;
            foreach (var f in _L.poolFeatures)
            {
                switch (f.kind)
                {
                    case PoolFeatureKind.FreePool: if (Mine(f.centre)) FreePoolSteps(f); break;
                    case PoolFeatureKind.Gallery: if (Mine(f.centre)) GalleryStair(f); break;
                    // (Spouts and cascades are still planned, so worlds stay as they were, but no longer built: the owner
                    // did not like the waterfalls, 2026-10-31.)
                    case PoolFeatureKind.Ladder: if (Mine(f.centre)) Ladder(f); break;
                    case PoolFeatureKind.Lifeguard: if (Mine(f.centre)) Lifeguard(f); break;
                    case PoolFeatureKind.Bleachers: if (Mine(f.centre)) Bleachers(f); break;
                    case PoolFeatureKind.Slide: if (Mine(f.centre)) Slide(f); break;
                    case PoolFeatureKind.Changing: if (Mine(f.centre)) Changing(f); break;
                    case PoolFeatureKind.Showers: if (Mine(f.centre)) Showers(f); break;
                    case PoolFeatureKind.RoundHall:
                        if (Mine(f.centre))
                        {
                            int x = Mathf.Clamp(Mathf.FloorToInt(f.centre.x / C), 0, _L.N - 1), z = Mathf.Clamp(Mathf.FloorToInt(f.centre.y / C), 0, _L.N - 1);
                            RoundHallWell(f, _L.RoomAt(x, z).ceiling);
                        }
                        break;
                }
            }
            int N = _L.N;
            for (int x = 0; x < N; x++) for (int z = 0; z < N; z++) if (_L.pool[x, z].gallery > 0f) GalleryCell(x, z, _L.pool[x, z].gallery);
        }
    }
}
