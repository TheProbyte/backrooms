using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Destruction
{
    /// <summary>
    /// What the inside of a wall is made of, drawn with the world's lit shader (Backrooms/Lit, no new variants: a base map
    /// and the existing normal-map keyword): the gypsum core of a broken board edge (paper on both faces), the grey kraft
    /// paper on a board's back, galvanised steel studs and tracks, pine studs and plates, fibreglass batts (yellow in the
    /// offices, pink in the older walls), electrical boxes (steel, or blue plastic on wood studs), cable and copper pipe;
    /// and the gypsum dust a blow raises (Backrooms/Dust). Textures are made here at first use.
    /// </summary>
    public static class DemolitionMaterials
    {
        static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();
        static Shader s_lit;
        static Material s_dust;

        public static Material GypsumEdge => Get("GypsumEdge", () => Make("Wall gypsum edge", GypsumEdgeTexture(), Color.white, 0f, 0.08f, "Chalk", 0.6f));
        public static Material BackPaper => Get("BackPaper", () => Make("Wall board back", PaperTexture(), new Color(0.66f, 0.62f, 0.55f), 0f, 0.12f, "Cardboard", 0.35f));
        // Metals are only partly metallic, as the game's own are (racks 0.3-0.35, lamps 0.6): inside a wall there is next to
        // nothing to reflect, and a full metal shows black.
        public static Material Steel => Get("Steel", () => Make("Galvanised steel", SpangleTexture(), new Color(0.66f, 0.68f, 0.7f), 0.45f, 0.5f, "MetalBrushed", 0.4f));
        public static Material Wood => Get("Wood", () => Make("Pine stud", GrainTexture(), new Color(0.86f, 0.71f, 0.5f), 0f, 0.22f, "Cardboard", 0.5f));
        public static Material BattYellow => Get("BattYellow", () => Make("Insulation batt", FluffTexture(), new Color(0.93f, 0.82f, 0.5f), 0f, 0.04f, "Fabric", 0.9f));
        public static Material BattPink => Get("BattPink", () => Make("Insulation batt (pink)", FluffTexture(), new Color(0.95f, 0.64f, 0.66f), 0f, 0.04f, "Fabric", 0.9f));
        public static Material BoxPlastic => Get("BoxPlastic", () => Make("Electrical box", null, new Color(0.14f, 0.3f, 0.7f), 0f, 0.55f, "Plastic", 0.3f));
        public static Material CableYellow => Get("CableYellow", () => Make("NM cable", null, new Color(0.92f, 0.76f, 0.18f), 0f, 0.45f, "Plastic", 0.3f));
        public static Material CableMetal => Get("CableMetal", () => Make("MC cable", SpangleTexture(), new Color(0.6f, 0.62f, 0.63f), 0.4f, 0.45f, "MetalBrushed", 0.6f));
        public static Material Copper => Get("Copper", () => Make("Copper pipe", null, new Color(0.78f, 0.46f, 0.3f), 0.5f, 0.62f, "MetalBrushed", 0.3f));
        // The sledgehammer: a hickory handle, a black-painted head worn bright at its faces and edges.
        public static Material HammerHandle => Get("HammerHandle", () => Make("Sledgehammer handle", GrainTexture(), new Color(0.74f, 0.55f, 0.36f), 0f, 0.42f, "Cardboard", 0.25f));
        public static Material HammerHead => Get("HammerHead", () => Make("Sledgehammer head", SpangleTexture(), new Color(0.07f, 0.07f, 0.075f), 0.35f, 0.38f, "PaintedMetal", 0.6f));
        public static Material HammerFace => Get("HammerFace", () => Make("Sledgehammer face", null, new Color(0.56f, 0.57f, 0.58f), 0.6f, 0.55f, "MetalBrushed", 0.5f));

        /// <summary>Soft round dust puffs lit like the surface behind them.</summary>
        public static Material Dust
        {
            get
            {
                if (s_dust) return s_dust;
                var sh = Shader.Find("Backrooms/Dust") ?? Shader.Find("Backrooms/SprayMist");
                if (sh) s_dust = new Material(sh) { name = "Gypsum dust", hideFlags = HideFlags.DontSave };
                return s_dust;
            }
        }

        public static Material Batt(bool steel) => steel ? BattYellow : BattPink;
        public static Material Box(bool steel) => steel ? Steel : BoxPlastic;
        public static Material Frame(bool steel) => steel ? Steel : Wood;
        public static Material Cable(bool steel) => steel ? CableMetal : CableYellow;

        static Material Get(string key, System.Func<Material> make)
        {
            if (s_mats.TryGetValue(key, out var m) && m) return m;
            m = make();
            s_mats[key] = m;
            return m;
        }

        static Material Make(string name, Texture2D albedo, Color tint, float metal, float smooth, string detail, float bump)
        {
            if (!s_lit) s_lit = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(s_lit) { name = name, hideFlags = HideFlags.DontSave };
            m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (albedo) { m.SetTexture("_BaseMap", albedo); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo); }
            m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            var tex = Resources.Load<Texture2D>("Textures/Generated/Detail_" + detail + "_Normal");
            if (tex && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", tex); m.SetFloat("_BumpScale", bump);
                m.EnableKeyword("_NORMALMAP");
            }
            return m;
        }

        // ---------------- Textures ----------------

        const int Size = 256;

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)s * 0xCB1AB31Fu;
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return (h >> 8) * (1f / (1 << 24));
            }
        }

        /// <summary>Tileable value noise on a period of `cells` across the texture.</summary>
        static float Noise(float x, float y, int cells, int s)
        {
            float fx = x * cells, fy = y * cells;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            float tx = fx - ix, ty = fy - iy;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            int x0 = ((ix % cells) + cells) % cells, y0 = ((iy % cells) + cells) % cells, x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
            float a = Mathf.Lerp(Hash(x0, y0, s), Hash(x1, y0, s), tx), b = Mathf.Lerp(Hash(x0, y1, s), Hash(x1, y1, s), tx);
            return Mathf.Lerp(a, b, ty);
        }

        static float Fbm(float x, float y, int cells, int s, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++) { sum += Noise(x, y, cells << o, s + o * 17) * amp; norm += amp; amp *= 0.5f; }
            return sum / norm;
        }

        static Texture2D Tex(string name, System.Func<float, float, Color> f, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                px[y * Size + x] = f((x + 0.5f) / Size, (y + 0.5f) / Size);
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>A board's broken edge: u along the edge (one repeat per 12.5 cm), v across the board from its face (0) to its back (1).</summary>
        static Texture2D GypsumEdgeTexture() => Tex("GypsumEdge", (u, v) =>
        {
            if (v < 0.05f) return new Color(0.93f, 0.92f, 0.88f);                         // the face paper (under paint or paper)
            if (v > 0.95f) return new Color(0.62f, 0.58f, 0.51f);                         // the back paper
            float pore = Noise(u, v, 48, 3), grain = Fbm(u, v, 12, 9);
            float c = 0.8f + 0.12f * grain - (pore > 0.86f ? 0.18f : 0f);
            return new Color(c, c * 0.99f, c * 0.96f);
        }, TextureWrapMode.Clamp);

        static Texture2D PaperTexture() => Tex("BoardPaper", (u, v) =>
        {
            float fibre = Fbm(u * 4f, v, 16, 21, 3) * 0.6f + Noise(u, v, 96, 5) * 0.4f;
            float c = 0.9f + 0.12f * (fibre - 0.5f);
            // A printed maker's line now and then.
            if (Mathf.Abs(v - 0.5f) < 0.004f && Noise(u, 0.3f, 6, 2) > 0.55f) c *= 0.75f;
            return new Color(c, c, c);
        });

        static Texture2D SpangleTexture() => Tex("Spangle", (u, v) =>
        {
            // Galvanised zinc crystals: patches of slightly different brightness with fine sparkle.
            float cell = Noise(u, v, 20, 31), cell2 = Noise(u + 0.37f, v + 0.11f, 9, 33), fine = Noise(u, v, 128, 35);
            float c = 0.84f + 0.1f * (cell - 0.5f) + 0.07f * (cell2 - 0.5f) + 0.05f * (fine - 0.5f);
            return new Color(c, c, c * 1.01f);
        });

        static Texture2D GrainTexture() => Tex("PineGrain", (u, v) =>
        {
            // Rings along the length (v), wavy, with darker latewood and the odd knot.
            float wave = Fbm(u * 0.5f, v * 0.25f, 4, 41, 3);
            float ring = Mathf.Repeat(u * 9f + wave * 2.5f, 1f);
            float late = Mathf.SmoothStep(0.7f, 0.95f, ring) * (1f - Mathf.SmoothStep(0.95f, 1f, ring));
            float c = 0.94f - 0.2f * late + 0.06f * (Noise(u, v, 64, 43) - 0.5f);
            float knotD = new Vector2(Mathf.Repeat(u - 0.3f, 1f) - 0.5f, (Mathf.Repeat(v - 0.6f, 1f) - 0.5f) * 0.6f).magnitude;
            if (knotD < 0.06f) c *= Mathf.Lerp(0.55f, 1f, knotD / 0.06f);
            return new Color(c, c * 0.97f, c * 0.92f);
        });

        static Texture2D FluffTexture() => Tex("Fluff", (u, v) =>
        {
            float a = Fbm(u, v, 8, 51, 5), b = Noise(u * 2f, v, 64, 53);
            float c = 0.78f + 0.3f * (a - 0.5f) + 0.12f * (b - 0.5f);
            return new Color(c, c, c);
        });
    }
}
