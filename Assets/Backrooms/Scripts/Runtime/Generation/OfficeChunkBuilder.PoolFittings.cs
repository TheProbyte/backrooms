using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// What real pools are built with (2026-11-01, the realism plan 5): a rounded bullnose coping on every pool edge, return
    /// jets (small chrome nozzles that push the water round, PoolCirculation), skimmer mouths at the waterline (they draw
    /// the surface), round underwater niche lights (most dead, a few faintly lit), depth markers and NO DIVING spelled in
    /// small dark tiles on the deck beside the edge (reading the true depth; now and then a wrong one, a Backrooms tell),
    /// vent grilles high on the walls, a pace clock in the lap pools, and rust running down from the metal. The jets,
    /// skimmers and grilles are listed for the water's circulation and the sound (PoolChunk.fittings).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Fittings the runtime uses (layout-local): xyz the point, w the kind (0 jet, 1 skimmer, 2 grille); and
        /// for jets the direction they push (fittingDirs).</summary>
        public readonly List<Vector4> poolFittings = new List<Vector4>();
        public readonly List<Vector3> poolFittingDirs = new List<Vector3>();

        uint FitHash(int a, int b, int c)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791) ^ (uint)(_L.storey * 2654435761u);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        // ---------------- Pieces ----------------

        /// <summary>A rounded bullnose along a pool edge from a to b (at deck level), its round toward the water (n).</summary>
        void Nosing(Vector3 a, Vector3 b, Vector3 n)
        {
            var w = WMask(AccentMat, 1u);
            const float r = 0.025f;
            const int K = 5;
            Vector3 prevA = default, prevB = default, prevN = default;
            for (int k = 0; k <= K; k++)
            {
                float th = Mathf.PI * 0.5f * k / K;                   // from the top (flush with the deck) round to the face
                var dir = Vector3.up * Mathf.Cos(th) + n * Mathf.Sin(th);
                var pa = a + Vector3.down * r + dir * r + n * 0.0f;
                var pb = b + Vector3.down * r + dir * r;
                if (k > 0)
                    w.SmoothQuad(prevA, prevB, pb, pa, prevN, prevN, dir, dir,
                        new Vector2(0f, k - 1), new Vector2(1f, k - 1), new Vector2(1f, k), new Vector2(0f, k));
                prevA = pa; prevB = pb; prevN = dir;
            }
        }

        /// <summary>A rust tear down a wall (or a stain ring round a point on the deck, ring true) of a size.</summary>
        void Stain(Vector3 top, Vector3 n, float width, float length, bool ring = false)
        {
            var mat = ring ? PoolMaterials.StainRing : PoolMaterials.StainTear;
            if (!mat) return;
            var w = WMask(mat, 1u);
            if (ring)
            {
                float s = width;
                var c = top + Vector3.up * 0.0015f;
                w.Quad(c + new Vector3(-s, 0, -s), c + new Vector3(-s, 0, s), c + new Vector3(s, 0, s), c + new Vector3(s, 0, -s), Vector3.up,
                    new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                return;
            }
            var t = Vector3.Cross(Vector3.up, n).normalized * (width * 0.5f);
            var o = top + n * 0.002f;
            w.Quad(o - t, o + t, o + t + Vector3.down * length, o - t + Vector3.down * length, n,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
        }

        /// <summary>A return jet on a pool wall (n into the water): a flange, a short nozzle, a dark bore. It pushes along d.</summary>
        void Jet(Vector3 at, Vector3 n, Vector3 d)
        {
            var w = WMask(ChromeMat, 1u);
            w.Disc(at + n * 0.003f, 0.045f, 14, n, MeshWriter.MetreUV);
            Tube(w, at + n * 0.003f, at + n * 0.035f, 0.017f, 10);
            WMask(PoolMaterials.Dark ? PoolMaterials.Dark : ChromeMat, 1u).Disc(at + n * 0.036f, 0.011f, 10, n, MeshWriter.MetreUV);
            Stain(at + Vector3.down * 0.035f, n, 0.07f, 0.28f);
            poolFittings.Add(new Vector4(at.x, at.y, at.z, 0f));
            poolFittingDirs.Add(d);
        }

        /// <summary>A skimmer's mouth at the waterline on a pool wall: a dark opening in an accent frame.</summary>
        void Skimmer(Vector3 at, Vector3 n)
        {
            var t = Vector3.Cross(Vector3.up, n).normalized;
            float hw = 0.14f, top = PoolBiome.WaterLevel + 0.07f, bot = PoolBiome.WaterLevel - 0.09f;
            var c = new Vector3(at.x, 0f, at.z) + n * 0.004f;
            var dark = WMask(PoolMaterials.Dark ? PoolMaterials.Dark : ChromeMat, 1u);
            dark.Quad(c - t * hw + Vector3.up * bot, c + t * hw + Vector3.up * bot, c + t * hw + Vector3.up * top, c - t * hw + Vector3.up * top, n, MeshWriter.MetreUV);
            var frame = WMask(AccentMat, 1u);
            var q = Quaternion.LookRotation(n);
            frame.Box(c + Vector3.up * (top + 0.012f) + n * 0.006f, new Vector3(2f * hw + 0.05f, 0.025f, 0.014f), q, 0b111111, MeshWriter.MetreUV);
            frame.Box(c + Vector3.up * (bot - 0.012f) + n * 0.006f, new Vector3(2f * hw + 0.05f, 0.025f, 0.014f), q, 0b111111, MeshWriter.MetreUV);
            poolFittings.Add(new Vector4(c.x, PoolBiome.WaterLevel, c.z, 1f));
            poolFittingDirs.Add(n);
        }

        /// <summary>A round underwater niche light: a chrome ring and a lens, dead or faintly lit.</summary>
        void NicheLight(Vector3 at, Vector3 n, bool lit)
        {
            const float R = 0.13f;
            var ring = WMask(ChromeMat, 1u);
            var t = Vector3.Cross(Vector3.up, n).normalized;
            _railPts.Clear();
            for (int k = 0; k <= 20; k++)
            {
                float a = k * Mathf.PI * 2f / 20f;
                _railPts.Add(at + n * 0.01f + (t * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * R);
            }
            Sweep(ring, _railPts, 0.012f, 8);
            var lens = lit ? PoolMaterials.Glow : PoolMaterials.LensDead;
            WMask(lens ? lens : ChromeMat, 1u).Disc(at + n * 0.012f, R - 0.006f, 20, n, MeshWriter.MetreUV);
            Stain(at + Vector3.down * (R + 0.01f), n, 0.12f, 0.32f);
        }

        /// <summary>A vent grille high on a wall: a grey frame and slats over a dark opening.</summary>
        void Grille(Vector3 at, Vector3 n, float width, float height)
        {
            var t = Vector3.Cross(Vector3.up, n).normalized;
            var q = Quaternion.LookRotation(n);
            var dark = WMask(PoolMaterials.Dark ? PoolMaterials.Dark : ChromeMat, 1u);
            var c = at + n * 0.003f;
            dark.Quad(c - t * (width * 0.5f) - Vector3.up * (height * 0.5f), c + t * (width * 0.5f) - Vector3.up * (height * 0.5f),
                      c + t * (width * 0.5f) + Vector3.up * (height * 0.5f), c - t * (width * 0.5f) + Vector3.up * (height * 0.5f), n, MeshWriter.MetreUV);
            var metal = WMask(PoolMaterials.Grille ? PoolMaterials.Grille : ChromeMat, 1u);
            metal.Box(c + Vector3.up * (height * 0.5f + 0.015f) + n * 0.012f, new Vector3(width + 0.06f, 0.03f, 0.024f), q, 0b111111, MeshWriter.MetreUV);
            metal.Box(c - Vector3.up * (height * 0.5f + 0.015f) + n * 0.012f, new Vector3(width + 0.06f, 0.03f, 0.024f), q, 0b111111, MeshWriter.MetreUV);
            metal.Box(c - t * (width * 0.5f + 0.015f) + n * 0.012f, new Vector3(0.03f, height, 0.024f), q, 0b111111, MeshWriter.MetreUV);
            metal.Box(c + t * (width * 0.5f + 0.015f) + n * 0.012f, new Vector3(0.03f, height, 0.024f), q, 0b111111, MeshWriter.MetreUV);
            int slats = Mathf.Max(3, Mathf.RoundToInt(height / 0.045f));
            for (int k = 0; k < slats; k++)
            {
                float y = -height * 0.5f + (k + 0.5f) * height / slats;
                metal.Box(c + Vector3.up * y + n * 0.012f, new Vector3(width, 0.008f, 0.03f), q * Quaternion.Euler(35f, 0f, 0f), 0b111111, MeshWriter.MetreUV);
            }
            Stain(c - Vector3.up * (height * 0.5f + 0.03f), n, width * 0.8f, 0.5f);
            poolFittings.Add(new Vector4(c.x, c.y, c.z, 2f));
            poolFittingDirs.Add(n);
        }

        /// <summary>A pace clock on a wall: a white face, a dark rim, twelve marks and two hands.</summary>
        void PaceClock(Vector3 at, Vector3 n, uint seed)
        {
            const float R = 0.32f;
            var t = Vector3.Cross(Vector3.up, n).normalized;
            var face = WMask(PoolMaterials.PlasticWhite ? PoolMaterials.PlasticWhite : TileMat, 1u);
            face.Disc(at + n * 0.03f, R, 32, n, MeshWriter.MetreUV);
            var dark = WMask(PoolMaterials.Marker ? PoolMaterials.Marker : ChromeMat, 1u);
            _railPts.Clear();
            for (int k = 0; k <= 32; k++) { float a = k * Mathf.PI * 2f / 32f; _railPts.Add(at + n * 0.03f + (t * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * (R + 0.012f)); }
            Sweep(dark, _railPts, 0.018f, 8);
            var q = Quaternion.LookRotation(n);
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6f;
                var d = t * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
                dark.Box(at + n * 0.034f + d * (R - 0.04f), new Vector3(0.012f, 0.012f, 0.006f) + new Vector3(Mathf.Abs(Vector3.Dot(d, t)), Mathf.Abs(d.y), 0f) * (k % 3 == 0 ? 0.05f : 0.025f), q, 0b111111, MeshWriter.MetreUV);
            }
            // The hands, stopped at some time.
            float hA = (seed % 12) * Mathf.PI / 6f, mA = ((seed >> 4) % 60) * Mathf.PI / 30f;
            foreach (var (ang, len, wdt) in new[] { (hA, R * 0.5f, 0.016f), (mA, R * 0.8f, 0.01f) })
            {
                var d = t * Mathf.Sin(ang) + Vector3.up * Mathf.Cos(ang);
                var mid = at + n * 0.04f + d * (len * 0.5f);
                dark.Box(mid, new Vector3(wdt, len, 0.005f), Quaternion.LookRotation(n, d), 0b111111, MeshWriter.MetreUV);
            }
        }

        // ---------------- Tile lettering ----------------

        static readonly Dictionary<char, string> s_glyphs = new Dictionary<char, string>
        {
            ['0'] = "01110100111010111001" + "01110", ['1'] = "00100011000010000100" + "01110", ['2'] = "11110000010111010000" + "11111",
            ['3'] = "11110000010011000001" + "11110", ['4'] = "10010100101111100010" + "00010", ['5'] = "11111100001111000001" + "11110",
            ['6'] = "01110100001111010001" + "01110", ['7'] = "11111000010001000100" + "00100", ['8'] = "01110100010111010001" + "01110",
            ['9'] = "01110100010111100001" + "01110", ['.'] = "00000000000000000000" + "00100", ['M'] = "10001110111010110001" + "10001",
            ['N'] = "10001110011010110011" + "10001", ['O'] = "01110100011000110001" + "01110", ['D'] = "11110100011000110001" + "11110",
            ['I'] = "01110001000010000100" + "01110", ['V'] = "10001100011000101010" + "00100", ['G'] = "01111100001001110001" + "01111",
            [' '] = "0000000000000000000000000",
        };

        /// <summary>Text spelt in small dark tiles on the deck, centred at c, running along t, the letters' tops toward up2.</summary>
        void TileText(string text, Vector3 c, Vector3 t, Vector3 up2, float pix = 0.024f)
        {
            var mat = PoolMaterials.Marker;
            if (!mat) return;
            var w = WMask(mat, 1u);
            float cw = pix * 6f, total = text.Length * cw - pix;
            var start = c - t * (total * 0.5f) + up2 * (pix * 2.5f) + Vector3.up * 0.0012f;
            for (int i = 0; i < text.Length; i++)
            {
                if (!s_glyphs.TryGetValue(text[i], out var g)) continue;
                for (int row = 0; row < 5; row++)
                for (int col = 0; col < 5; col++)
                {
                    if (g[row * 5 + col] != '1') continue;
                    var o = start + t * (i * cw + col * pix) - up2 * (row * pix);
                    float s = pix * 0.47f;
                    w.Quad(o + t * (-s) + up2 * (-s), o + t * (-s) + up2 * s, o + t * s + up2 * s, o + t * s + up2 * (-s), Vector3.up, MeshWriter.MetreUV);
                }
            }
        }

        /// <summary>A depth marker's text for water this deep (m), now and then wrong (a Backrooms tell).</summary>
        static string DepthText(float depth, uint h)
        {
            float d = Mathf.Round(depth * 10f) / 10f;
            if (h % 23 == 0) d = new[] { 0.3f, 2.4f, 3.6f, 9.9f }[(h >> 5) % 4];
            return d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " M";
        }

        // ---------------- Placing them ----------------

        /// <summary>
        /// Free pools: walk the outline (Newton steps back onto it from each step along the tangent) and put the coping's
        /// round, the fittings and the markers along it. Built by the chunk the pool's centre is in.
        /// </summary>
        void FreePoolFittings(PoolFeature f)
        {
            float a0 = f.shape == 1 ? -f.radius : -f.size.x;
            var e3 = V3(f.centre) + Yaw(f.yaw) * new Vector3(a0, 0f, 0f);
            var p = new Vector2(e3.x, e3.z);
            for (int i = 0; i < 8; i++) p -= ShapeGradient(f, p) * PoolBiome.ShapeSD(f, p);
            var start = p;
            var pts = new List<Vector2>();
            const float ds = 0.3f;
            for (int i = 0; i < 400; i++)
            {
                pts.Add(p);
                var g = ShapeGradient(f, p);
                p += new Vector2(-g.y, g.x) * ds;
                for (int k = 0; k < 4; k++) p -= ShapeGradient(f, p) * PoolBiome.ShapeSD(f, p);
                if (i > 4 && (p - start).magnitude < ds * 0.9f) break;
            }
            uint seed = FitHash(Mathf.RoundToInt(f.centre.x * 10f), Mathf.RoundToInt(f.centre.y * 10f), 7);
            float run = 0f; int jets = 0;
            float nextJet = 1.5f + (seed % 7) * 0.2f, nextLight = 2.5f, nextMark = 0.9f, nextSkim = 4.5f;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                var mid = (a + b) * 0.5f;
                var g = ShapeGradient(f, mid);
                var inward = new Vector3(-g.x, 0f, -g.y);
                var tan = new Vector3(b.x - a.x, 0f, b.y - a.y).normalized;
                Nosing(new Vector3(a.x, 0f, a.y), new Vector3(b.x, 0f, b.y), inward);
                run += (b - a).magnitude;
                var wallPt = new Vector3(mid.x, 0f, mid.y);
                float floor = BasinFloorY(mid.x - g.x * 0.3f, mid.y - g.y * 0.3f);
                // Leave the shallow end's steps (round E) clear.
                if ((mid - start).magnitude < 1.6f) continue;
                if (run >= nextJet && floor < -0.6f)
                {
                    Jet(wallPt + Vector3.up * Mathf.Max(floor + 0.2f, -0.38f), inward, (inward * 0.6f + tan * 0.8f).normalized);
                    nextJet = run + 3.2f; jets++;
                }
                else if (run >= nextLight && floor < -0.75f)
                {
                    uint h = FitHash(Mathf.RoundToInt(mid.x * 10f), Mathf.RoundToInt(mid.y * 10f), 3);
                    NicheLight(wallPt + Vector3.up * Mathf.Max(floor + 0.22f, -0.45f), inward, h % 4 == 0);
                    nextLight = run + 4.5f;
                }
                else if (run >= nextSkim)
                {
                    Skimmer(wallPt, inward);
                    nextSkim = run + 9f;
                }
                if (run >= nextMark)
                {
                    // On the deck beside the edge, beyond the accent band, the letters' tops toward the water.
                    var at = wallPt - inward * 0.42f;
                    uint h = FitHash(Mathf.RoundToInt(mid.x * 10f), Mathf.RoundToInt(mid.y * 10f), 11);
                    float depth = -floor + PoolBiome.WaterLevel;
                    string text = h % 3 == 0 ? "NO DIVING" : DepthText(depth, h);
                    TileText(text, at, Vector3.Cross(Vector3.up, inward).normalized, inward);
                    nextMark = run + 3.4f;
                }
            }
        }

        /// <summary>The ordinary basins' fittings, per edge (hashed, so both chunks and every load agree).</summary>
        void BasinFittings(EdgeKind e, bool vertical, int line, int along, Vector3 n, float f, Vector3 p0, Vector3 p1, bool deckEdge)
        {
            uint h = FitHash(vertical ? GX(line) : GZ(line), vertical ? GZ(along) : GX(along), vertical ? 1 : 2);
            var mid = (p0 + p1) * 0.5f;
            mid.y = 0f;
            var tan = (p1 - p0).normalized;
            float floor = BasinFloorY(mid.x + n.x * 0.3f, mid.z + n.z * 0.3f);
            if (deckEdge)
            {
                Nosing(new Vector3(p0.x, 0f, p0.z), new Vector3(p1.x, 0f, p1.z), n);
                if (h % 3 == 0)
                {
                    uint h2 = h >> 8;
                    float depth = -floor + PoolBiome.WaterLevel;
                    TileText(h2 % 3 == 0 ? "NO DIVING" : DepthText(depth, h2), mid - n * 0.42f, Vector3.Cross(Vector3.up, n).normalized, n);
                }
            }
            switch (h % 11)
            {
                case 0: case 1: case 2:
                    if (floor < -0.55f) Jet(mid + Vector3.up * Mathf.Max(floor + 0.2f, -0.36f), n, (n * 0.5f + tan * ((h & 64) != 0 ? 0.85f : -0.85f)).normalized);
                    break;
                case 3: case 4:
                    if (floor < -0.7f) NicheLight(mid + Vector3.up * Mathf.Max(floor + 0.22f, -0.44f), n, (h >> 12) % 4 == 0);
                    break;
                case 5:
                    Skimmer(mid, n);
                    break;
            }
        }

        /// <summary>Vent grilles high on room walls, and a pace clock in each lap pool's room.</summary>
        void PoolWallFittings()
        {
            var clocked = new HashSet<int>();
            foreach (var room in _L.rooms)
            {
                if (!room.Walkable || room.spiral || room.noFloor) continue;
                var rc = room.rect;
                // Walk the room's outline: a grille on about one wall cell in nine, a clock once in a lap pool's room.
                for (int side = 0; side < 4; side++)
                {
                    int len = side < 2 ? rc.height : rc.width;
                    for (int k = 1; k < len - 1; k++)
                    {
                        int x = side == 0 ? rc.xMin : side == 1 ? rc.xMax - 1 : rc.xMin + k;
                        int z = side < 2 ? rc.yMin + k : side == 2 ? rc.yMin : rc.yMax - 1;
                        if (!_L.InChunk(x, z) || _L.roomOf[x, z] != room.id) continue;
                        if (_L.EdgeOf(x, z, side) != EdgeKind.Wall || _L.pool[x, z].gallery > 0f) continue;
                        // Not over a high window (it is kept on the cell on the edge's +X / +Z side).
                        int wx = side == 1 ? x + 1 : x, wz = side == 3 ? z + 1 : z;
                        if (_L.InChunk(wx, wz) && (_L.pool[wx, wz].windows & (side < 2 ? 1 : 2)) != 0) continue;
                        if (HoleAt(side < 2, side == 0 ? x : side == 1 ? x + 1 : z + (side == 3 ? 1 : 0), side < 2 ? z : x, out _)) continue;
                        uint h = FitHash(GX(x), GZ(z), 20 + side);
                        var n = side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.forward : Vector3.back;
                        float fx = side == 0 ? x * C + T * 0.5f : side == 1 ? (x + 1) * C - T * 0.5f : (x + 0.5f) * C;
                        float fz = side == 2 ? z * C + T * 0.5f : side == 3 ? (z + 1) * C - T * 0.5f : (z + 0.5f) * C;
                        float ceil = _L.WallTop(room);
                        if (h % 9 == 0 && ceil > 3.2f)
                            Grille(new Vector3(fx, ceil - 0.55f, fz), n, 0.6f, 0.3f);
                        else if (_L.pool[x, z].lanes != 0 && !clocked.Contains(room.id) && h % 4 == 1 && ceil > 3.4f)
                        {
                            clocked.Add(room.id);
                            PaceClock(new Vector3(fx, 2.5f, fz), n, h >> 8);
                        }
                    }
                }
            }
        }
    }
}
