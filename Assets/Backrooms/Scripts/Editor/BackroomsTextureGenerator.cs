using System.IO;
using UnityEditor;
using UnityEngine;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Generates the tileable surface textures for the office room (albedo + normal).
    /// Everything is procedural and seeded, so the result is reproducible and license-free.
    /// Each texture's physical size is noted; mesh UVs are authored in the same units so
    /// texel density stays consistent across modules.
    /// </summary>
    public static class BackroomsTextureGenerator
    {
        public const string Folder = "Assets/Backrooms/Textures";

        // Physical size covered by one texture repeat, in metres.
        public const float CarpetBaseMeters = 4.0f;
        public const float CarpetDetailMeters = 0.25f;
        public const float WallpaperWidthMeters = 1.2f;   // full wall height in V
        public const float CeilingMeters = 2.4f;          // 4 x 4 tiles of 0.6 m

        public static void GenerateAll()
        {
            Directory.CreateDirectory(Folder);
            Carpet();
            CarpetDetail();
            Wallpaper();
            Ceiling();
            GenerateProps();
            AssetDatabase.Refresh();
        }

        /// <summary>Textures for the infinite-world dressing: wall plates, upholstery and wood.</summary>
        public static void GenerateProps()
        {
            Directory.CreateDirectory(Folder);
            WallPlates();
            Fabric();
            Wood();
            Plaster();
            AssetDatabase.Refresh();
        }

        // ---------- Painted plaster / drywall: soft mottling and orange-peel texture, neutral (tinted by material) ----------
        static void Plaster()
        {
            const int S = 1024;
            var albedo = new Color[S * S];
            var height = new float[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float mottle = Fbm(u, v, 3, 5, 171);
                float peel = VNoise(u * 300, v * 300, 300, 300, 173) * 0.6f + VNoise(u * 140, v * 140, 140, 140, 179) * 0.4f;
                float scuff = Mathf.SmoothStep(0.62f, 0.8f, Fbm(u, v, 6, 4, 181));
                float g = 0.93f + (mottle - 0.5f) * 0.035f + (peel - 0.5f) * 0.018f - scuff * 0.02f;
                albedo[y * S + x] = new Color(g, g * 0.995f, g * 0.985f, 1f);
                height[y * S + x] = peel * 0.8f + mottle * 0.2f;
            }
            Save("Plaster_Albedo", S, S, albedo, true);
            Save("Plaster_Normal", S, S, NormalFromHeight(height, S, S, 0.9f), false, true);
        }

        // ---------- Outlet (left half) and light switch (right half) plate atlas ----------
        static void WallPlates()
        {
            const int W = 256, H = 256;
            var px = new Color[W * H];
            var ivory = new Color(0.86f, 0.83f, 0.74f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool sw = x >= W / 2;
                float u = (x % (W / 2)) / (float)(W / 2), v = y / (float)H;   // 0..1 within the half
                float edge = Mathf.Min(Mathf.Min(u, 1 - u) * 0.61f, Mathf.Min(v, 1 - v));
                float bevel = Mathf.SmoothStep(0f, 0.05f, edge);
                Color c = ivory * (0.82f + 0.18f * bevel);
                if (!sw)
                {
                    // Two receptacle faces with slots and a ground hole.
                    for (int k = 0; k < 2; k++)
                    {
                        float cy = k == 0 ? 0.3f : 0.7f;
                        float fx = Mathf.Abs(u - 0.5f) / 0.36f, fy = Mathf.Abs(v - cy) / 0.13f;
                        float face = Mathf.Pow(Mathf.Pow(fx, 4) + Mathf.Pow(fy, 4), 0.25f);
                        if (face < 1f) c = Color.Lerp(c, ivory * 0.93f, 0.6f) * (face > 0.9f ? 0.85f : 1f);
                        bool slotL = Mathf.Abs(u - 0.38f) < 0.025f && Mathf.Abs(v - (cy + 0.025f)) < 0.05f;
                        bool slotR = Mathf.Abs(u - 0.62f) < 0.032f && Mathf.Abs(v - (cy + 0.025f)) < 0.055f;
                        float gd = Vector2.Distance(new Vector2(u, v * 0.5f), new Vector2(0.5f, (cy - 0.06f) * 0.5f));
                        bool ground = gd < 0.03f && v > cy - 0.09f;
                        if (slotL || slotR || ground) c = new Color(0.07f, 0.065f, 0.06f);
                    }
                    if (Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) < 0.035f) c *= 0.7f; // screw
                }
                else
                {
                    float lx = Mathf.Abs(u - 0.5f) / 0.13f, ly = Mathf.Abs(v - 0.5f) / 0.16f;
                    if (Mathf.Max(lx, ly) < 1f) c = ivory * (0.75f + 0.25f * (1f - v)); // toggle, lit from above
                    if (Mathf.Max(lx * 0.75f, ly * 0.75f) < 1f && Mathf.Max(lx, ly) >= 1f) c *= 0.78f;
                    foreach (float sy in new[] { 0.18f, 0.82f })
                        if (Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, sy)) < 0.035f) c *= 0.7f;
                }
                c.a = 1f;
                px[y * W + x] = c;
            }
            Save("WallPlate_Atlas", W, H, px, true);
        }

        // ---------- Upholstery weave, neutral grey (tinted per sofa material) ----------
        static void Fabric()
        {
            const int S = 512;
            var albedo = new Color[S * S];
            var height = new float[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float warp = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f * 128f);
                float weft = 0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 2f * 128f);
                bool over = ((x / 4) + (y / 4)) % 2 == 0;
                float weave = over ? warp : weft;
                float slub = VNoise(u * 24, v * 96, 24, 96, 131);
                float wear = Fbm(u, v, 4, 4, 137);
                float g = 0.5f + (weave - 0.5f) * 0.16f + (slub - 0.5f) * 0.1f + (wear - 0.5f) * 0.18f;
                albedo[y * S + x] = new Color(g, g, g, 1f);
                height[y * S + x] = weave * 0.7f + slub * 0.3f;
            }
            Save("Fabric_Albedo", S, S, albedo, true);
            Save("Fabric_Normal", S, S, NormalFromHeight(height, S, S, 2.5f), false, true);
        }

        // ---------- Wood laminate (light oak), 1 m repeat ----------
        static void Wood()
        {
            const int S = 512;
            var albedo = new Color[S * S];
            Color a = new Color(0.62f, 0.47f, 0.31f), b = new Color(0.50f, 0.36f, 0.22f);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float warpN = Fbm(u, v, 3, 4, 151);
                float rings = 0.5f + 0.5f * Mathf.Sin((v * 38f + warpN * 6f) * Mathf.PI * 2f);
                float grain = VNoise(u * 8, v * 256, 8, 256, 157);
                Color c = Color.Lerp(a, b, rings * 0.55f + grain * 0.25f);
                c.a = 1f;
                albedo[y * S + x] = c;
            }
            Save("Wood_Albedo", S, S, albedo, true);
        }

        // ---------- Carpet: large-scale mottling and wear (4 m repeat) ----------
        static void Carpet()
        {
            const int S = 1024;
            var albedo = new Color[S * S];
            var height = new float[S * S];
            var baseCol = new Color(0.47f, 0.40f, 0.25f);     // tired mustard-tan office carpet (sRGB)
            var wornCol = new Color(0.55f, 0.50f, 0.37f);     // flattened, lighter traffic areas
            var stainCol = new Color(0.36f, 0.31f, 0.21f);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float mottle = Fbm(u, v, 6, 5, 11);
                float broad = Fbm(u, v, 2, 3, 23);
                float stain = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.64f, 0.78f, Fbm(u, v, 3, 4, 37)));
                float wear = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.75f, broad));
                Color c = Color.Lerp(baseCol, wornCol, wear * 0.45f);
                c = Color.Lerp(c, stainCol, stain * 0.35f);
                c *= 0.93f + mottle * 0.14f;
                c.a = 1f;
                albedo[y * S + x] = c;
                height[y * S + x] = mottle * 0.5f - wear * 0.3f;
            }
            Save("Carpet_Albedo", S, S, albedo, true);
            Save("Carpet_Normal", S, S, NormalFromHeight(height, S, S, 1.2f), false, true);
        }

        // ---------- Carpet detail: cut-loop pile fibres (0.25 m repeat), neutral grey for MulX2 ----------
        static void CarpetDetail()
        {
            const int S = 512;
            var albedo = new Color[S * S];
            var height = new float[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float tufts = VNoise(u * 96, v * 96, 96, 96, 5);
                float fibres = VNoise(u * 256, v * 256, 256, 256, 7);
                float rows = 0.5f + 0.5f * Mathf.Sin((u * 64f + VNoise(u * 8, v * 8, 8, 8, 9) * 0.6f) * Mathf.PI * 2f);
                float h = tufts * 0.5f + fibres * 0.35f + rows * 0.15f;
                float g = 0.5f + (h - 0.5f) * 0.22f + (Hash(x, y, 3) - 0.5f) * 0.05f;
                albedo[y * S + x] = new Color(g, g, g, 1f);
                height[y * S + x] = h;
            }
            Save("CarpetDetail_Albedo", S, S, albedo, true);
            Save("CarpetDetail_Normal", S, S, NormalFromHeight(height, S, S, 3f), false, true);
        }

        // ---------- Wallpaper: aged, faintly patterned, 1.2 m wide x full 2.7 m height ----------
        static void Wallpaper()
        {
            const int W = 1024, H = 2304;
            var albedo = new Color[W * H];
            var height = new float[W * H];
            var paper = new Color(0.80f, 0.72f, 0.47f);   // yellowed wallpaper (sRGB)
            var stripe = new Color(0.76f, 0.68f, 0.43f);
            var nicotine = new Color(0.66f, 0.56f, 0.33f);
            var scuff = new Color(0.62f, 0.57f, 0.44f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;          // v = 0 at floor, 1 at ceiling
                float mx = u * WallpaperWidthMeters, my = v * 2.7f;

                // 6 cm vertical stripe pairs with a faint chevron motif inside the darker stripe.
                float sPhase = Mathf.Repeat(mx / 0.06f, 1f);
                float inStripe = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.52f, sPhase))
                               * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.94f, 0.98f, sPhase)));
                float chev = Mathf.Abs(Mathf.Repeat(my / 0.06f + Mathf.Abs(sPhase - 0.73f) * 1.4f, 1f) - 0.5f);
                float motif = inStripe * Mathf.SmoothStep(0.12f, 0.06f, chev);

                Color c = Color.Lerp(paper, stripe, inStripe * 0.9f);
                c = Color.Lerp(c, stripe * 0.97f, motif * 0.6f);

                // Paper grain.
                float grain = VNoise(u * 384, v * 864, 384, 864, 41) * 0.6f + Hash(x, y, 43) * 0.4f;
                c *= 0.975f + grain * 0.05f;

                // Ageing: blotchy yellowing, darker under the ceiling, faint drips, scuffs near floor.
                float blotch = Fbm(u, v * 2.25f, 3, 4, 47);
                c = Color.Lerp(c, nicotine, Mathf.Clamp01((blotch - 0.45f) * 0.5f));
                float top = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.80f, 1f, v));
                c = Color.Lerp(c, nicotine, top * 0.28f);
                float dripMask = VNoise(u * 40, 0.5f, 40, 4, 53);
                float drip = Mathf.SmoothStep(0.78f, 0.9f, dripMask) * Mathf.SmoothStep(0.35f, 1f, v) * (0.6f + 0.4f * VNoise(u * 40, v * 6, 40, 16, 59));
                c = Color.Lerp(c, nicotine, drip * 0.22f);
                float low = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.14f, v));
                float scuffN = Fbm(u * 2f, v * 6f, 8, 3, 61);
                c = Color.Lerp(c, scuff, low * Mathf.Clamp01((scuffN - 0.42f) * 2f) * 0.35f);

                // Strip seams every 0.6 m (wallpaper roll width), very slightly lifted.
                float seamDist = Mathf.Abs(Mathf.Repeat(mx + 0.3f, 0.6f) - 0.3f);
                float seam = 1f - Mathf.SmoothStep(0.0008f, 0.0022f, seamDist);
                c *= 1f - seam * 0.10f;

                c.a = 1f;
                albedo[y * W + x] = c;
                height[y * W + x] = grain * 0.35f + inStripe * 0.12f + motif * 0.1f - seam * 0.6f + blotch * 0.1f;
            }
            Save("Wallpaper_Albedo", W, H, albedo, true);
            Save("Wallpaper_Normal", W, H, NormalFromHeight(height, W, H, 1.4f), false, true);
        }

        // ---------- Ceiling: 4 x 4 fissured mineral-fibre tiles (2.4 m repeat) ----------
        static void Ceiling()
        {
            const int S = 1024;
            const int Tiles = 4;
            var albedo = new Color[S * S];
            var height = new float[S * S];
            var tileCol = new Color(0.80f, 0.78f, 0.71f);
            var water = new Color(0.62f, 0.52f, 0.34f);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                int tx = (int)(u * Tiles), ty = (int)(v * Tiles);
                float lu = u * Tiles - tx, lv = v * Tiles - ty;
                float tileVar = Hash(tx, ty, 71) - 0.5f;

                // Fissures: thin worm-like valleys from ridged noise, plus pinholes.
                float n1 = VNoise(u * 160, v * 160, 160, 160, 73 + tx * 7 + ty * 13);
                float n2 = VNoise(u * 72, v * 72, 72, 72, 79 + tx * 3 + ty * 5);
                float fiss = Mathf.SmoothStep(0.06f, 0.0f, Mathf.Abs(n1 - 0.5f)) * Mathf.SmoothStep(0.45f, 0.65f, n2);
                float pin = Hash(x, y, 83) > 0.9965f ? 1f : 0f;
                float speck = VNoise(u * 512, v * 512, 512, 512, 89);

                Color c = tileCol * (1f + tileVar * 0.05f);
                c *= 0.97f + speck * 0.05f;
                c *= 1f - fiss * 0.22f - pin * 0.3f;

                // Faint sag shading toward tile centre and edge bevel.
                float edge = Mathf.Min(Mathf.Min(lu, 1f - lu), Mathf.Min(lv, 1f - lv));
                float bevel = 1f - Mathf.SmoothStep(0f, 0.012f, edge);
                c *= 1f - bevel * 0.15f;

                // One old water stain on one tile in the set.
                if (tx == 2 && ty == 1)
                {
                    float d = Vector2.Distance(new Vector2(lu, lv), new Vector2(0.45f, 0.55f)) + (Fbm(u, v, 12, 3, 97) - 0.5f) * 0.18f;
                    float ring = Mathf.SmoothStep(0.05f, 0f, Mathf.Abs(d - 0.32f)) * 0.5f + Mathf.SmoothStep(0.32f, 0.15f, d) * 0.25f;
                    c = Color.Lerp(c, water, ring * 0.45f);
                }

                c.a = 1f;
                albedo[y * S + x] = c;
                height[y * S + x] = speck * 0.2f - fiss * 0.8f - pin * 0.8f - bevel * 0.7f;
            }
            Save("CeilingTile_Albedo", S, S, albedo, true);
            Save("CeilingTile_Normal", S, S, NormalFromHeight(height, S, S, 2.2f), false, true);
        }

        // ---------- Helpers ----------
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        /// <summary>Periodic value noise: wraps every (px, py) cells so textures tile seamlessly.</summary>
        static float VNoise(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int xa = Mod(x0, px), xb = Mod(x0 + 1, px), ya = Mod(y0, py), yb = Mod(y0 + 1, py);
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f; int p = basePeriod;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * VNoise(u * p, v * p, p, p, seed + i * 101);
                norm += amp; amp *= 0.5f; p *= 2;
            }
            return sum / norm;
        }

        static Color[] NormalFromHeight(float[] h, int w, int hgt, float strength)
        {
            var o = new Color[w * hgt];
            for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                float l = h[y * w + Mod(x - 1, w)], r = h[y * w + Mod(x + 1, w)];
                float d = h[Mod(y - 1, hgt) * w + x], u = h[Mod(y + 1, hgt) * w + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                o[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            return o;
        }

        static void Save(string name, int w, int h, Color[] px, bool srgb, bool normal = false)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 4096;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }
    }
}
