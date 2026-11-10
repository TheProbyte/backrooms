using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The hub's own surfaces (2026-11-07, Docs/HubPlan.md), always in the Overflow look whatever ART STYLE says:
    ///  - the wall (<see cref="WallFinish.HubPanel"/>): dark wood boards to 0.95 m, a cap rail, the company stripe (orange,
    ///    gold, avocado) to 1.15 m, painted cream above. One painted texture over 4 m of height
    ///    (Tools/Textures/make_hub_wall.py), mapped by the height from the floor, so it sits right in a room of any height.
    ///  - the floor (<see cref="FloorFinish.Concrete"/> in the hub): the Overflow look's painted slab.
    /// Made on the main thread when a world starts; the chunk builder only reads them.
    /// </summary>
    public static class HubMaterials
    {
        public static Material Wall, Concrete;
        /// <summary>How tall the wall texture is (m): WallUV maps 2.7 m to one repeat, so the texture is scaled 2.7 / this.</summary>
        public const float WallTextureHeight = 4f;

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap"), BumpMap = Shader.PropertyToID("_BumpMap"), BaseColor = Shader.PropertyToID("_BaseColor"),
                            BumpScale = Shader.PropertyToID("_BumpScale"), ParallaxMap = Shader.PropertyToID("_ParallaxMap"), Parallax = Shader.PropertyToID("_Parallax");

        public static void Ensure(OfficeBiomeAssets hub)
        {
            if (!hub || (Wall && Concrete)) return;
            var wallTex = Resources.Load<Texture2D>("Hub/OF_HubWall_Albedo");
            var wallNrm = Resources.Load<Texture2D>("Hub/OF_HubWall_Normal");
            if (!Wall && hub.paintedWall && wallTex)
            {
                Wall = Plain(hub.paintedWall, "Hub wall (panelled)", wallTex, wallNrm, 0.45f);
                var scale = new Vector2(1f, OfficeBiome.CeilingHeight / WallTextureHeight);
                Wall.SetTextureScale(BaseMap, scale);
                if (Wall.HasProperty(BumpMap)) Wall.SetTextureScale(BumpMap, scale);
                Wall.SetFloat("_Smoothness", 0.22f);
            }
            var style = Resources.Load<StyleSet>("Style/Overflow");
            if (!Concrete && hub.carpet && style && style.concreteAlbedo)
            {
                Concrete = Plain(hub.carpet, "Hub floor (concrete, painted)", style.concreteAlbedo, style.concreteNormal, style.concreteBump);
                Concrete.SetColor(BaseColor, style.concreteTint);
                Concrete.SetFloat("_Smoothness", style.concreteSmoothness);
            }
        }

        /// <summary>A copy of a world material with its own maps and none of the extras (parallax, detail grain, sheen).</summary>
        static Material Plain(Material src, string name, Texture2D albedo, Texture2D normal, float bump)
        {
            var m = new Material(src) { name = name, hideFlags = HideFlags.DontSave };
            m.SetTexture(BaseMap, albedo);
            m.SetTextureScale(BaseMap, Vector2.one); m.SetTextureOffset(BaseMap, Vector2.zero);
            m.SetColor(BaseColor, Color.white);
            if (m.HasProperty(BumpMap))
            {
                m.SetTexture(BumpMap, normal);
                if (m.HasProperty(BumpScale)) m.SetFloat(BumpScale, bump);
                if (normal) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            }
            if (m.HasProperty(ParallaxMap)) { m.SetTexture(ParallaxMap, null); m.SetFloat(Parallax, 0.005f); }
            if (m.HasProperty("_SheenMask")) m.SetFloat("_SheenMask", 0f);
            if (m.HasProperty("_DetailNormalMap")) { m.SetTexture("_DetailNormalMap", null); m.SetFloat("_DetailNormalMapScale", 0f); }
            if (m.HasProperty("_DetailAlbedoMap")) m.SetTexture("_DetailAlbedoMap", null);
            m.DisableKeyword("_DETAIL_MULX2");
            m.DisableKeyword("_PARALLAXMAP");
            return m;
        }
    }
}
