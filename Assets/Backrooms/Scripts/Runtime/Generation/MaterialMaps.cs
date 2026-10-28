using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Normal maps for the world materials that had none (Resources/Textures/Generated, made by a script from their albedo
    /// or procedurally: wood, laminate, painted metal, plastic, box print, wall plates...). The project's .mat files are
    /// left as they are: at startup each such material gets an in-memory copy with its normal map, and everything that
    /// builds the world asks <see cref="For"/> for the copy (chunk meshes, props, furniture). The copies use the same
    /// shader keyword (_NORMALMAP) as the carpet and walls, so no new shader variant is needed.
    /// Built on the main thread before any chunk is prepared (WorldStreamer.Awake); <see cref="For"/> is a plain lookup,
    /// safe on the generator's worker threads.
    /// </summary>
    public static class MaterialMaps
    {
        static readonly Dictionary<Material, Material> s_map = new Dictionary<Material, Material>();
        static OfficeBiomeAssets s_for;

        /// <summary>Material name, generated normal map, normal strength.</summary>
        static readonly (string mat, string tex, float scale)[] Table =
        {
            ("M_Pallet", "M_Pallet_Normal", 1.0f), ("M_WoodDark", "M_WoodDark_Normal", 0.8f), ("M_HalfWallCap", "M_HalfWallCap_Normal", 0.7f),
            ("M_TableLaminate", "M_TableLaminate_Normal", 0.6f), ("M_BoxPrint", "M_BoxPrint_Normal", 1.0f), ("M_WallPlate", "M_WallPlate_Normal", 0.8f),
            ("M_Baseboard", "M_Baseboard_Normal", 0.6f), ("M_CeilingGrid", "M_CeilingGrid_Normal", 0.5f), ("M_FixtureHousing", "M_FixtureHousing_Normal", 0.5f),
            ("M_RackBeam", "M_RackBeam_Normal", 0.7f), ("M_LampMetal", "M_LampMetal_Normal", 0.6f), ("M_PropPlastic", "M_PropPlastic_Normal", 0.5f),
            ("M_BoxTape", "M_BoxTape_Normal", 0.6f), ("M_ClockFace", "M_ClockFace_Normal", 0.4f),
        };

        static readonly int BumpMap = Shader.PropertyToID("_BumpMap"), BumpScale = Shader.PropertyToID("_BumpScale");

        public static void Ensure(OfficeBiomeAssets a)
        {
            if (!a || a == s_for) return;
            s_for = a;
            s_map.Clear();
            var byName = new Dictionary<string, (Texture2D tex, float scale)>();
            foreach (var (mat, tex, scale) in Table)
            {
                var t = Resources.Load<Texture2D>("Textures/Generated/" + tex);
                if (t) byName[mat] = (t, scale);
            }
            void Add(Material m)
            {
                if (!m || s_map.ContainsKey(m) || !byName.TryGetValue(m.name, out var n)) return;
                if (!m.HasProperty(BumpMap) || m.GetTexture(BumpMap)) { s_map[m] = m; return; }
                var c = new Material(m) { name = m.name + " (normal map)", hideFlags = HideFlags.DontSave };
                c.SetTexture(BumpMap, n.tex);
                c.SetFloat(BumpScale, n.scale);
                c.EnableKeyword("_NORMALMAP");
                s_map[m] = c;
            }
            foreach (var f in typeof(OfficeBiomeAssets).GetFields())
            {
                var v = f.GetValue(a);
                if (v is Material m) Add(m);
                else if (v is Material[] ms) foreach (var x in ms) Add(x);
                else if (v is GameObject g) AddPrefab(g, Add);
                else if (v is GameObject[] gs) foreach (var x in gs) AddPrefab(x, Add);
            }
            Troffers(a);
            Ceilings(a);
        }

        // ---------------- Ceiling tiles ----------------

        /// <summary>
        /// The acoustic ceiling's variants (OfficeChunkBuilder.Wear): two copies whose texture has a stain on every one of
        /// its 16 tiles (water rings with tide lines, mould specks; the texture is 4 x 4 tiles, so a tile drawn with a copy
        /// shows its own stain: 32 different ones), a yellowed copy, and the dark space above the tiles (the plenum, seen
        /// where a tile is lifted, shifted or missing). Same shader keywords as the ceiling: no new variants. Null without
        /// the ceiling material.
        /// </summary>
        public static Material CeilingStainedA, CeilingStainedB, CeilingYellowed, Plenum;
        static Texture2D s_stainedA, s_stainedB;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static Material PlenumWood, PlenumDeck, PlenumBatt, PlenumMetal, PlenumDuct, TileBack;

        public static bool IsCeilingVariant(Material m) =>
            m && (m == CeilingStainedA || m == CeilingStainedB || m == CeilingYellowed || m == Plenum || m == PlenumWood || m == PlenumDeck
                  || m == PlenumBatt || m == PlenumMetal || m == PlenumDuct || m == TileBack);

        static Material Tinted(Material src, string suffix, Color tint)
        {
            if (!src) return null;
            var m = new Material(src) { name = src.name + suffix, hideFlags = HideFlags.DontSave };
            m.SetColor(BaseColor, tint);
            return m;
        }

        static void Ceilings(OfficeBiomeAssets a)
        {
            CeilingStainedA = CeilingStainedB = CeilingYellowed = Plenum = null;
            var src = a.ceilingTile;
            if (!src || !src.HasProperty(BaseMap)) return;
            var tex = src.GetTexture(BaseMap) as Texture2D;
            if (tex)
            {
                if (!s_stainedA) s_stainedA = Stained(tex, 7);
                if (!s_stainedB) s_stainedB = Stained(tex, 31);
                CeilingStainedA = Copy(src, " (stained)", s_stainedA);
                CeilingStainedB = Copy(src, " (stained B)", s_stainedB);
            }
            Color c = src.HasProperty(BaseColor) ? src.GetColor(BaseColor) : Color.white;
            CeilingYellowed = Copy(src, " (yellowed)", null);
            CeilingYellowed.SetColor(BaseColor, c * new Color(0.97f, 0.93f, 0.8f, 1f));
            Plenum = Copy(src, " (plenum)", null);
            Plenum.SetColor(BaseColor, new Color(0.016f, 0.015f, 0.014f, 1f));       // dark: the room's light barely gets in
            // What is up there (seen past a missing or slid tile): old joists and the boards they carry, dusty fibreglass,
            // grey metal (pipe, hanger wire, cable), a foil flex duct, and the tiles' own fibrous backs. The broken walls'
            // materials, aged, dusty and dark: the world's light reaches up there as it does the room, so they are dimmed
            // to a little over half (what little comes up through a gap: dark, but the shapes show). Same shader and keywords.
            PlenumWood = Tinted(Destruction.DemolitionMaterials.Wood, " (old joist)", new Color(0.24f, 0.18f, 0.12f));
            PlenumDeck = Tinted(Destruction.DemolitionMaterials.Wood, " (deck)", new Color(0.16f, 0.125f, 0.09f));
            PlenumBatt = Tinted(Destruction.DemolitionMaterials.BattYellow, " (dusty)", new Color(0.37f, 0.31f, 0.17f));
            PlenumMetal = Tinted(Destruction.DemolitionMaterials.Steel, " (dusty)", new Color(0.24f, 0.245f, 0.25f));
            PlenumDuct = Tinted(Destruction.DemolitionMaterials.Steel, " (foil duct)", new Color(0.33f, 0.33f, 0.32f));
            TileBack = Tinted(Destruction.DemolitionMaterials.BackPaper, " (tile back)", new Color(0.28f, 0.27f, 0.25f));
        }

        static Material Copy(Material src, string suffix, Texture2D baseMap)
        {
            var m = new Material(src) { name = src.name + suffix, hideFlags = HideFlags.DontSave };
            if (baseMap) m.SetTexture(BaseMap, baseMap);
            return m;
        }

        /// <summary>The ceiling texture (read back from the GPU: it is not readable) with a stain painted into each tile.</summary>
        static Texture2D Stained(Texture2D src, int seed)
        {
            int w = src.width, h = src.height;
            var was = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = src.name + " (stained)", wrapMode = TextureWrapMode.Repeat, filterMode = src.filterMode,
                anisoLevel = src.anisoLevel, hideFlags = HideFlags.DontSave,
            };
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            var rng = new System.Random(seed);
            int tile = w / 4;
            for (int ty = 0; ty < 4; ty++)
            for (int tx = 0; tx < 4; tx++)
            {
                double kind = rng.NextDouble();
                if (kind < 0.8) WaterStain(px, w, tx * tile, ty * tile, tile, rng);
                if (kind > 0.55) Mould(px, w, tx * tile, ty * tile, tile, rng);
            }
            t.SetPixels32(px);
            t.Apply(true, false);
            t.Compress(true);
            t.Apply(false, true);
            return t;
        }

        static void Multiply(Color32[] px, int i, float r, float g, float b)
        {
            var c = px[i];
            px[i] = new Color32((byte)Mathf.Clamp(c.r * r, 0f, 255f), (byte)Mathf.Clamp(c.g * g, 0f, 255f), (byte)Mathf.Clamp(c.b * b, 0f, 255f), c.a);
        }

        /// <summary>A dried water stain: a ragged blot, faintly tinted inside, with one to three darker brown tide lines.</summary>
        static void WaterStain(Color32[] px, int w, int ox, int oy, int tile, System.Random rng)
        {
            int inset = tile / 24;
            float R = tile * (0.12f + (float)rng.NextDouble() * 0.2f);
            float cx = ox + inset + R * 0.5f + (float)rng.NextDouble() * (tile - 2 * inset - R);
            float cy = oy + inset + R * 0.5f + (float)rng.NextDouble() * (tile - 2 * inset - R);
            float p1 = (float)rng.NextDouble() * 6.3f, p2 = (float)rng.NextDouble() * 6.3f, p3 = (float)rng.NextDouble() * 6.3f;
            float n0 = (float)rng.NextDouble() * 100f;
            int rings = 1 + rng.Next(3);
            float ringW = 0.035f + (float)rng.NextDouble() * 0.03f, dark = 0.55f + (float)rng.NextDouble() * 0.35f;
            int x0 = Mathf.Max(ox + inset, (int)(cx - R * 1.5f)), x1 = Mathf.Min(ox + tile - inset, (int)(cx + R * 1.5f));
            int y0 = Mathf.Max(oy + inset, (int)(cy - R * 1.5f)), y1 = Mathf.Min(oy + tile - inset, (int)(cy + R * 1.5f));
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float dx = x - cx, dy = y - cy;
                float ang = Mathf.Atan2(dy, dx);
                float r = R * (1f + 0.17f * Mathf.Sin(3f * ang + p1) + 0.09f * Mathf.Sin(5f * ang + p2) + 0.05f * Mathf.Sin(11f * ang + p3));
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r + (Fbm(x / 22f + n0, y / 22f) - 0.5f) * 0.42f
                                                              + (Fbm(x / 6f, y / 6f + n0) - 0.5f) * 0.12f;
                if (d > 1.15f) continue;
                // The faint tint inside, stronger toward the rim (the water dried from the middle out).
                float k = d < 1f ? (0.05f + 0.2f * d * d * d) * (0.55f + 0.45f * Fbm(x / 11f, y / 11f + n0)) : 0f;
                float uneven = 0.45f + 0.75f * Fbm(x / 15f + 40f, y / 15f + n0);           // the tide line comes and goes
                for (int j = 0; j < rings; j++)
                {
                    float rho = 1f - j * 0.19f;
                    float e = (d - rho) / (ringW * (1f + j * 0.4f));
                    k = Mathf.Max(k, Mathf.Exp(-e * e) * dark * uneven * (1f - j * 0.25f));
                }
                if (k <= 0.003f) continue;
                Multiply(px, y * w + x, 1f - k * 0.3f, 1f - k * 0.42f, 1f - k * 0.6f);
            }
        }

        /// <summary>A patch of mould: a grey haze and a cluster of dark, slightly green specks.</summary>
        static void Mould(Color32[] px, int w, int ox, int oy, int tile, System.Random rng)
        {
            int inset = tile / 24;
            float s = tile * (0.05f + (float)rng.NextDouble() * 0.1f);
            float cx = ox + inset + s * 2f + (float)rng.NextDouble() * (tile - 2 * inset - s * 4f);
            float cy = oy + inset + s * 2f + (float)rng.NextDouble() * (tile - 2 * inset - s * 4f);
            int x0 = Mathf.Max(ox + inset, (int)(cx - s * 3f)), x1 = Mathf.Min(ox + tile - inset, (int)(cx + s * 3f));
            int y0 = Mathf.Max(oy + inset, (int)(cy - s * 3f)), y1 = Mathf.Min(oy + tile - inset, (int)(cy + s * 3f));
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float dx = (x - cx) / (s * 1.6f), dy = (y - cy) / (s * 1.6f);
                float k = Mathf.Exp(-(dx * dx + dy * dy)) * 0.26f * Fbm(x / 6f, y / 6f);
                if (k > 0.003f) Multiply(px, y * w + x, 1f - k, 1f - k * 0.95f, 1f - k * 1.05f);
            }
            int specks = 150 + rng.Next(300);
            for (int i = 0; i < specks; i++)
            {
                // Gaussian spread (Box-Muller), a few strays further out.
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                double g = System.Math.Sqrt(-2.0 * System.Math.Log(u1));
                float sx = cx + (float)(g * System.Math.Cos(6.2832 * u2)) * s, sy = cy + (float)(g * System.Math.Sin(6.2832 * u2)) * s;
                float rad = 0.5f + (float)rng.NextDouble() * (rng.NextDouble() < 0.08 ? 1.8f : 0.9f);
                float a = 0.15f + (float)rng.NextDouble() * 0.3f;
                for (int y = (int)(sy - rad - 1); y <= (int)(sy + rad + 1); y++)
                for (int x = (int)(sx - rad - 1); x <= (int)(sx + rad + 1); x++)
                {
                    if (x < ox + inset || x >= ox + tile - inset || y < oy + inset || y >= oy + tile - inset) continue;
                    float d = Mathf.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
                    float k = a * Mathf.Clamp01(rad + 0.5f - d);
                    if (k > 0.003f) Multiply(px, y * w + x, 1f - k * 0.62f, 1f - k * 0.58f, 1f - k * 0.66f);
                }
            }
        }

        static float Fbm(float x, float y) => ValueNoise(x, y) * 0.6f + ValueNoise(x * 2.3f + 17f, y * 2.3f) * 0.4f;

        static float ValueNoise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash01(ix, iy), b = Hash01(ix + 1, iy), c = Hash01(ix, iy + 1), d = Hash01(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash01(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        // ---------------- Square troffers ----------------

        /// <summary>
        /// The square ceiling panels' lens, lit and unlit: a copy of lensOn / lensOff with a prismatic acrylic diffuser over
        /// three tubes (<see cref="TrofferGlow"/>). Lit, it glows brightest over the tubes and less between them and toward
        /// the frame, so a camera that stops down for it sees the tubes through the lens instead of a flat white tile; its
        /// emission is scaled so the lens as a whole is as bright as before. Same shader keywords: no new variants. Drawn
        /// with <see cref="TrofferUV"/> (0..1 across each lens). Null without the biome's lens materials.
        /// </summary>
        public static Material TrofferOn, TrofferOff;
        static Texture2D s_glow, s_prism;
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap"), EmissionColor = Shader.PropertyToID("_EmissionColor"),
                            BaseMap = Shader.PropertyToID("_BaseMap");

        /// <summary>Is this one of the troffer lens copies?</summary>
        public static bool IsTroffer(Material m) => m && (m == TrofferOn || m == TrofferOff);

        static void Troffers(OfficeBiomeAssets a)
        {
            TrofferOn = TrofferOff = null;
            if (!a.lensOn || !a.lensOff || !a.lensOn.HasProperty(EmissionMap)) return;
            if (!s_glow) s_glow = TrofferGlow(512, out s_glowMean);
            if (!s_prism) s_prism = Prism(512);
            TrofferOn = new Material(a.lensOn) { name = a.lensOn.name + " (troffer)", hideFlags = HideFlags.DontSave };
            TrofferOn.SetTexture(EmissionMap, s_glow);
            TrofferOn.SetTexture(BaseMap, s_prism);
            TrofferOn.SetTextureScale(BaseMap, Vector2.one); TrofferOn.SetTextureOffset(BaseMap, Vector2.zero);
            TrofferOn.SetColor(EmissionColor, a.lensOn.GetColor(EmissionColor) / Mathf.Max(0.2f, s_glowMean));
            TrofferOff = new Material(a.lensOff) { name = a.lensOff.name + " (troffer)", hideFlags = HideFlags.DontSave };
            TrofferOff.SetTexture(BaseMap, s_prism);
            TrofferOff.SetTextureScale(BaseMap, Vector2.one); TrofferOff.SetTextureOffset(BaseMap, Vector2.zero);
        }

        static float s_glowMean = 1f;

        /// <summary>UVs 0..1 across a square lens of this size centred at c (world): the glow map lies the same in each.</summary>
        public static MeshWriter.UVProjector TrofferUV(Vector3 c, float size)
        {
            float inv = 1f / size;
            return (w, n) => new Vector2((w.x - c.x) * inv + 0.5f, (w.z - c.z) * inv + 0.5f);
        }

        /// <summary>Tube centres across the lens (three T8 tubes, 15 cm apart behind a 57 cm lens).</summary>
        static readonly float[] Tubes = { 0.5f - 0.263f, 0.5f, 0.5f + 0.263f };

        /// <summary>
        /// The light leaving the lens (linear, peak 1): each tube a soft band (the diffuser spreads it about 3.5 cm, with a
        /// wide halo), the reflector's fill between them (about 60 % of the bands: from across a room it is all white), darker at the tubes' ends (lampholders) and toward the frame,
        /// all broken up by the lens's little pyramids, which sparkle most over the tubes. Mean returned for scaling.
        /// </summary>
        static Texture2D TrofferGlow(int size, out float mean)
        {
            var px = new Color[size * size];
            var lin = new float[size * size];
            float max = 0f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float tube = 0f;
                foreach (float t in Tubes)
                {
                    float d = u - t;
                    tube += Mathf.Exp(-d * d / (2f * 0.06f * 0.06f)) + 0.3f * Mathf.Exp(-d * d / (2f * 0.16f * 0.16f));
                }
                float ends = Mathf.Lerp(0.55f, 1f, Smooth(0.02f, 0.12f, v) * Smooth(0.02f, 0.12f, 1f - v));
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float rim = Mathf.Lerp(0.5f, 1f, Smooth(0f, 0.07f, edge));
                float g = (0.55f + tube * ends) * rim;
                g *= 1f + 0.16f * Mathf.Clamp01(tube) * Pyramids(u, v);
                lin[y * size + x] = g;
                if (g > max) max = g;
            }
            float sum = 0f;
            for (int i = 0; i < lin.Length; i++)
            {
                float g = lin[i] / max;
                sum += g;
                float s = Mathf.LinearToGammaSpace(g);
                px[i] = new Color(s, s, s, 1f);
            }
            mean = sum / lin.Length;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "TrofferGlow", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>The unlit lens: pale acrylic with its pyramids catching the room's light.</summary>
        static Texture2D Prism(int size)
        {
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float s = Mathf.LinearToGammaSpace(0.93f + 0.07f * Pyramids(u, v));
                px[y * size + x] = new Color(s, s, s, 1f);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "TrofferPrism", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>A diagonal grid of small pyramids (about 7 mm on a 57 cm lens), -1..1.</summary>
        static float Pyramids(float u, float v)
        {
            const float N = 80f;
            float a = (u + v) * N, b = (u - v) * N;
            return Mathf.Cos(a * Mathf.PI * 2f) * Mathf.Cos(b * Mathf.PI * 2f);
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static void AddPrefab(GameObject g, System.Action<Material> add)
        {
            if (!g) return;
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) add(m);
        }

        /// <summary>The material to draw with: its copy with a normal map, or itself.</summary>
        public static Material For(Material m) => m && s_map.TryGetValue(m, out var c) ? c : m;

        /// <summary>Swaps a built object's materials for their copies (props and furniture made from prefabs).</summary>
        public static void Apply(GameObject go)
        {
            if (s_map.Count == 0 || !go) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++) { var c = For(mats[i]); if (c != mats[i]) { mats[i] = c; changed = true; } }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
