using Backrooms.Generation;
using UnityEditor;
using UnityEngine;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Gives the procedural office biome the Reference Match look: the game's wallpaper in tan-yellow, the supplied carpet
    /// photo, square fissured ceiling tiles and no baseboard. It edits the biome's own materials in place, so every chunk
    /// the world builds uses it. The world builder calls this after it (re)creates the materials.
    /// </summary>
    public static class OfficeReferenceLook
    {
        const string Tex = "Assets/Backrooms/Textures/";
        /// <summary>Mineral-fibre tiles are near white (the yellow they show comes from the light bounced onto them, not the
        /// tile). The old tint (0.9, 0.88, 0.55) made them olive.</summary>
        static readonly Color WorldCeilingTint = new Color(0.86f, 0.85f, 0.80f);
        /// <summary>The fittings as you see them. A real panel is many times brighter than the walls it lights, so the camera
        /// clips it to white with a little glare around it; at the old 2.4 it sat just under the clip and took on the white
        /// balance's blue. Absolute values (re-running this does not compound them).</summary>
        static readonly Color PanelGlow = new Color(1f, 0.965f, 0.86f) * 9f, DomeGlow = new Color(1f, 0.93f, 0.8f) * 10f,
                              StripGlow = new Color(0.9f, 0.95f, 1f) * 12f;

        [MenuItem("Backrooms/Apply Reference Look To Office Biome")]
        public static void ApplyMenu()
        {
            var a = AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            if (!a) { Debug.LogError("[Backrooms] OfficeBiome_Assets not found."); return; }
            Apply(a);
            AssetDatabase.SaveAssets();
            Debug.Log("[Backrooms] Office biome now uses the Reference Match look.");
        }

        public static void Apply(OfficeBiomeAssets a)
        {
            if (!a) return;
            BackroomsTextureGenerator.GenerateReferenceMatch();
            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + n + ".png");

            if (a.wallpaper)
            {
                a.wallpaper.SetColor("_BaseColor", ReferenceMatchBuilder.WallTint);   // same maps as ever, tan-yellow
                Finish(a.wallpaper);
            }
            if (a.carpet)
            {
                var m = a.carpet;
                m.SetTexture("_BaseMap", T("RefCarpet_Albedo")); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
                m.SetTexture("_BumpMap", T("RefCarpet_Normal")); m.SetFloat("_BumpScale", 0.6f); m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_DetailAlbedoMap", null); m.SetTexture("_DetailNormalMap", null); m.DisableKeyword("_DETAIL_MULX2");
                m.SetColor("_BaseColor", ReferenceMatchBuilder.CarpetTint);
                m.SetFloat("_Smoothness", 0.05f);
                Finish(m);
            }
            if (a.ceilingTile)
            {
                var m = a.ceilingTile;
                m.SetTexture("_BaseMap", T("RefCeiling_Albedo")); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
                m.SetTexture("_BumpMap", T("RefCeiling_Normal")); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
                m.SetColor("_BaseColor", WorldCeilingTint);
                m.SetFloat("_Smoothness", 0.05f);
                Finish(m);
            }
            void Glow(Material m, Color c) { if (!m) return; m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c); EditorUtility.SetDirty(m); }
            Glow(a.lensOn, PanelGlow); Glow(a.domeOn, DomeGlow); Glow(a.stripOn, StripGlow);
            a.noBaseboard = true;
            // Crawl tunnels: the wallpaper with a trace of self-light.
            if (a.wallpaper)
            {
                const string tunnelPath = "Assets/Backrooms/Materials/M_Tunnel.mat";
                var t = AssetDatabase.LoadAssetAtPath<Material>(tunnelPath);
                if (!t) { t = new Material(a.wallpaper); AssetDatabase.CreateAsset(t, tunnelPath); }
                t.CopyPropertiesFromMaterial(a.wallpaper);
                t.EnableKeyword("_EMISSION");
                t.SetColor("_EmissionColor", new Color(0.075f, 0.068f, 0.045f));
                t.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                Finish(t);
                a.tunnel = t;
            }
            EditorUtility.SetDirty(a);
        }

        static void Finish(Material m)
        {
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetShaderPassEnabled("MotionVectors", false);
            EditorUtility.SetDirty(m);
        }
    }
}
