using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Materials for models with a baked colour texture (Tools/Blender/bake_kit.py, 2026-11-04): the model's own
    /// &lt;name&gt;_Albedo.png carries its colour, the dirt in its creases, its worn edges, grime and prints, read through
    /// its uv0 (packed 0..1 at one texel density); each material slot keeps its own gloss and metalness, and the fine
    /// grain of what it is made of (brushed metal, moulded plastic, weave...) is the shader's detail normal map, tiled
    /// over the same UVs so it repeats every 0.25 m as on the other models. Uses Backrooms/Lit's existing _NORMALMAP and
    /// _DETAIL_MULX2 variants (the carpet's), so no new shader variants.
    ///
    /// Slots are named finish first ("Enamel_Fridge", "Rubber_Gasket", "Print_Notice"): <see cref="Finish"/> reads it.
    /// </summary>
    public static class BakedMaterials
    {
        static readonly Dictionary<(string, Texture2D), Material> s_cache = new Dictionary<(string, Texture2D), Material>();
        static readonly Dictionary<string, Texture2D> s_detail = new Dictionary<string, Texture2D>();
        static Shader s_lit;

        /// <summary>A slot's gloss, metalness, detail map (Resources/Textures/Generated/Detail_*_Normal) and its strength.</summary>
        public static (float smooth, float metal, string detail, float strength) Finish(string slot)
        {
            string s = slot.ToLowerInvariant();
            int cut = s.IndexOf('_');
            string f = cut > 0 ? s.Substring(0, cut) : s;
            switch (f)
            {
                case "enamel": return (0.62f, 0f, "Plastic", 0.18f);
                case "porcelain": return (0.78f, 0f, null, 0f);
                case "paint": return (0.45f, 0.1f, "PaintedMetal", 0.5f);
                case "satin": return (0.32f, 0.1f, "PaintedMetal", 0.45f);
                case "stainless": return (0.6f, 0.9f, "MetalBrushed", 0.55f);
                case "chrome": return (0.88f, 1f, null, 0f);
                case "steel": return (0.42f, 0.85f, "MetalBrushed", 0.6f);
                case "galv": return (0.38f, 0.75f, "MetalBrushed", 0.45f);
                case "alu": return (0.55f, 0.85f, "MetalBrushed", 0.5f);
                case "brass": return (0.6f, 0.95f, "MetalBrushed", 0.4f);
                case "plastic": return (0.42f, 0f, "Plastic", 0.45f);
                case "gloss": return (0.7f, 0f, "Plastic", 0.2f);
                case "matte": return (0.18f, 0f, "Plastic", 0.55f);
                case "rubber": return (0.22f, 0f, "Rubber", 0.6f);
                case "laminate": return (0.38f, 0f, "Plastic", 0.25f);
                case "wood": return (0.3f, 0f, null, 0f);
                case "fabric": return (0.06f, 0f, "Fabric", 0.7f);
                case "card": return (0.1f, 0f, "Cardboard", 0.6f);
                case "paper": return (0.12f, 0f, "Cardboard", 0.25f);
                case "print": return (0.5f, 0f, "Plastic", 0.12f);
                case "glass": return (0.92f, 0f, null, 0f);
                case "screen": return (0.86f, 0f, null, 0f);
                case "fibreglass": return (0.55f, 0f, "Plastic", 0.35f);
                case "concrete": return (0.12f, 0f, "Chalk", 0.6f);
                case "tape": return (0.3f, 0f, "Tape", 0.5f);
                case "stone": return (0.32f, 0f, "Chalk", 0.3f);
                case "board": return (0.1f, 0f, "Cardboard", 0.5f);
                case "beige": return (0.36f, 0f, "Plastic", 0.45f);
                default: return (0.4f, 0f, "Plastic", 0.4f);
            }
        }

        /// <summary>
        /// The material for a baked model's slot. uvPerMetre: the bake's texel density in UV units per metre of surface
        /// (<see cref="UvPerMetre"/>). Main thread.
        /// </summary>
        public static Material For(string slot, Texture2D albedo, float uvPerMetre, Material template = null)
        {
            if (!albedo) return null;
            if (s_cache.TryGetValue((slot, albedo), out var m) && m) return m;
            if (!s_lit) s_lit = Shader.Find("Backrooms/Lit");
            if (!s_lit && !template) return null;
            var (smooth, metal, detail, strength) = Finish(slot);
            // A copy of the world's own lit material when given (its settings), its maps cleared.
            m = template ? new Material(template) : new Material(s_lit);
            m.name = "Baked " + albedo.name + " " + slot;
            m.hideFlags = HideFlags.DontSave;
            foreach (var t in new[] { "_BumpMap", "_DetailNormalMap", "_DetailAlbedoMap", "_DetailMask", "_OcclusionMap", "_MetallicGlossMap", "_EmissionMap", "_ParallaxMap" })
                if (m.HasProperty(t)) m.SetTexture(t, null);
            foreach (var k in new[] { "_NORMALMAP", "_DETAIL_MULX2", "_DETAIL_SCALED", "_EMISSION", "_OCCLUSIONMAP", "_METALLICSPECGLOSSMAP", "_PARALLAXMAP", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A" })
                m.DisableKeyword(k);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            m.SetTexture("_BaseMap", albedo);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            Texture2D d = null;
            if (detail != null && !s_detail.TryGetValue(detail, out d))
            {
                d = Resources.Load<Texture2D>("Textures/Generated/Detail_" + detail + "_Normal");
                s_detail[detail] = d;
            }
            if (d && uvPerMetre > 1e-4f)
            {
                // The grain repeats every 0.25 m: 4 repeats a metre over UVs of uvPerMetre a metre.
                float k = 4f / uvPerMetre;
                m.SetTexture("_BumpMap", null);
                m.SetTexture("_DetailNormalMap", d);
                m.SetFloat("_DetailNormalMapScale", strength);
                m.SetTexture("_DetailAlbedoMap", null);          // (its default, linear grey: no change to the colour)
                m.SetTexture("_DetailMask", null);
                m.SetTextureScale("_DetailAlbedoMap", new Vector2(k, k));
                m.EnableKeyword("_NORMALMAP");
                m.EnableKeyword("_DETAIL_MULX2");
            }
            s_cache[(slot, albedo)] = m;
            return m;
        }

        static readonly Dictionary<(Material, Texture2D), Material> s_fromTemplate = new Dictionary<(Material, Texture2D), Material>();

        /// <summary>
        /// A baked copy of an existing material (an item's finish from its slot: ItemModels.Mat): the same gloss and metal
        /// and the same name (code finds the bulb or the globe by it), its colour from the bake, its fine normal map moved to
        /// the detail slot and tiled over the bake's UVs at its old 0.25 m.
        /// </summary>
        public static Material FromTemplate(Material template, Texture2D albedo, float uvPerMetre)
        {
            if (!template || !albedo) return template;
            if (s_fromTemplate.TryGetValue((template, albedo), out var m) && m) return m;
            m = new Material(template) { name = template.name, hideFlags = HideFlags.DontSave };
            Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            float scale = m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 1f;
            m.SetTexture("_BaseMap", albedo);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            if (bump && uvPerMetre > 1e-4f)
            {
                float k = 4f / uvPerMetre;
                m.SetTexture("_DetailNormalMap", bump);
                m.SetFloat("_DetailNormalMapScale", scale);
                m.SetTexture("_DetailAlbedoMap", null);
                m.SetTexture("_DetailMask", null);
                m.SetTextureScale("_DetailAlbedoMap", new Vector2(k, k));
                m.EnableKeyword("_NORMALMAP");
                m.EnableKeyword("_DETAIL_MULX2");
            }
            else m.DisableKeyword("_NORMALMAP");
            s_fromTemplate[(template, albedo)] = m;
            return m;
        }

        /// <summary>
        /// The bake's texel density written beside its texture (&lt;resource&gt;_Bake.json, {"uv_per_m": ...}), for models
        /// whose meshes are not readable (the items). 0 if there is none.
        /// </summary>
        public static float Density(string resource)
        {
            var t = Resources.Load<TextAsset>(resource + "_Bake");
            if (!t) return 0f;
            var m = System.Text.RegularExpressions.Regex.Match(t.text, @"""uv_per_m""\s*:\s*([0-9.eE+-]+)");
            return m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        /// <summary>UV units per metre of surface over some triangles (area weighted), for the detail map's tiling.</summary>
        public static float UvPerMetre(IList<Vector3> v, IList<Vector2> uv, IList<int> tris)
        {
            double a3 = 0, a2 = 0;
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (a >= uv.Count || b >= uv.Count || c >= uv.Count) continue;
                a3 += Vector3.Cross(v[b] - v[a], v[c] - v[a]).magnitude * 0.5;
                Vector2 e1 = uv[b] - uv[a], e2 = uv[c] - uv[a];
                a2 += Mathf.Abs(e1.x * e2.y - e1.y * e2.x) * 0.5;
            }
            return a3 > 1e-9 ? (float)System.Math.Sqrt(a2 / a3) : 0f;
        }
    }
}
