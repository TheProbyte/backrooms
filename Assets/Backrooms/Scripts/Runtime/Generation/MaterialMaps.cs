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
