using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The real world's surfaces (2026-11-07): what the places through the seams are made of (SeamRooms), so they read as
    /// somewhere that is not the Backrooms. Painted by Tools/Textures/make_real_surfaces.py into Resources/Real:
    ///  - <see cref="WallFinish.Blockwork"/>: painted concrete block, blue-grey to 1.2 m, a red line, white above; its
    ///    ceiling the painted steel roof deck.
    ///  - <see cref="WallFinish.Drywall"/>: a shop's smooth white wall with a black cove base; its ceiling smooth white plaster.
    ///  - <see cref="FloorFinish.Polished"/>: a polished concrete slab, saw cuts every 2.4 m.
    ///  - <see cref="FloorFinish.Vinyl"/>: vinyl composition tile, 300 mm, two greys and white.
    /// Made on the main thread when a world starts (as HubMaterials); the chunk builder only reads them.
    /// </summary>
    public static class RealMaterials
    {
        public static Material Block, Drywall, Polished, Vinyl, Deck, Plaster;
        /// <summary>The wall textures are 4.8 m tall (WallUV maps 2.7 m to one repeat).</summary>
        const float WallTextureHeight = 4.8f;

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap"), BumpMap = Shader.PropertyToID("_BumpMap"), BaseColor = Shader.PropertyToID("_BaseColor"),
                            BumpScale = Shader.PropertyToID("_BumpScale"), ParallaxMap = Shader.PropertyToID("_ParallaxMap"), Parallax = Shader.PropertyToID("_Parallax");

        public static bool IsRealWall(WallFinish f) => f == WallFinish.Blockwork || f == WallFinish.Drywall;
        public static bool IsRealFloor(FloorFinish f) => f == FloorFinish.Polished || f == FloorFinish.Vinyl;

        public static void Ensure(OfficeBiomeAssets a)
        {
            if (!a || Block) return;
            var wallScale = new Vector2(1f, OfficeBiome.CeilingHeight / WallTextureHeight);
            if (a.paintedWall)
            {
                Block = Make(a.paintedWall, "Real wall (painted block)", "RW_Block", wallScale, 0.9f, 0.42f);
                Drywall = Make(a.paintedWall, "Real wall (drywall)", "RW_Drywall", wallScale, 0.4f, 0.18f);
            }
            if (a.carpet)
            {
                // Floor UVs run 3 m to a repeat (MeshWriter.FloorUV): the slab's texture is 2.4 m, the tiles' 3 m.
                Polished = Make(a.carpet, "Real floor (polished concrete)", "RW_Polished", Vector2.one * (3f / 2.4f), 0.6f, 0.62f);
                Vinyl = Make(a.carpet, "Real floor (vinyl tile)", "RW_Vinyl", Vector2.one, 0.5f, 0.5f);
            }
            var ceil = a.ceilingPlaster ? a.ceilingPlaster : a.ceilingTile;
            if (ceil)
            {
                // Plaster ceilings map 1 m to a repeat (MeshWriter.MetreUV): both textures are 1.2 m.
                Deck = Make(ceil, "Real ceiling (steel deck)", "RW_Deck", Vector2.one / 1.2f, 1.2f, 0.3f);
                Plaster = Make(ceil, "Real ceiling (plaster)", "RW_Plaster", Vector2.one / 1.2f, 0.3f, 0.1f);
            }
        }

        static Material Make(Material src, string name, string tex, Vector2 scale, float bump, float smoothness)
        {
            var albedo = Resources.Load<Texture2D>("Real/" + tex + "_Albedo");
            if (!albedo) return null;
            var normal = Resources.Load<Texture2D>("Real/" + tex + "_Normal");
            var m = new Material(src) { name = name, hideFlags = HideFlags.DontSave };
            m.SetTexture(BaseMap, albedo);
            m.SetTextureScale(BaseMap, scale); m.SetTextureOffset(BaseMap, Vector2.zero);
            m.SetColor(BaseColor, Color.white);
            if (m.HasProperty(BumpMap))
            {
                m.SetTexture(BumpMap, normal);
                m.SetTextureScale(BumpMap, scale);
                if (m.HasProperty(BumpScale)) m.SetFloat(BumpScale, bump);
                if (normal) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
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
