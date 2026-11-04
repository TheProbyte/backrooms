using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Materials of the special rooms behind crawl spaces: the building's own textures (painted plaster, ceiling plaster,
    /// dark boards) on URP's Lit shader instead of Backrooms/Lit. These rooms sit above the ceiling, outside the light grid:
    /// only real lights reach them (the room's own bulb or lantern, a lamp you bring). Made on the main thread before any
    /// chunk is built (WorldStreamer.Awake); worker threads only read the references.
    /// </summary>
    public static class SpecialRoomMaterials
    {
        public static Material Wall { get; private set; }
        public static Material Floor { get; private set; }
        public static Material Ceiling { get; private set; }
        public static Material Board { get; private set; }
        /// <summary>The drop ceiling's grid runners and the baseboard (the room is an ordinary small room: 2026-10-31).</summary>
        public static Material Grid { get; private set; }
        public static Material Baseboard { get; private set; }

        /// <summary>
        /// The secret rooms (crawl spaces) are small ordinary rooms (owner, 2026-10-31: "a small room with carpet, normal ceiling
        /// and paint walls (no wallpaper), reusing from other rooms"): the world's carpet, its acoustic drop ceiling and grid,
        /// its painted wall and baseboard, with their normal maps, as plain URP Lit copies (the room is lit only by its own
        /// light, never by the fixtures or the light grid below).
        /// </summary>
        public static void Ensure(OfficeBiomeAssets a)
        {
            if (Wall && Floor && Ceiling && Board && Grid && Baseboard) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit || !a) return;
            Wall = Copy(lit, a.paintedWall ? a.paintedWall : a.wallpaper, "Special room wall", new Color(0.97f, 0.95f, 0.92f), 0.12f);
            Floor = Copy(lit, a.carpet ? a.carpet : a.wood, "Special room carpet", new Color(0.95f, 0.93f, 0.9f), 0.05f);
            Ceiling = Copy(lit, a.ceilingTile ? a.ceilingTile : a.ceilingPlaster, "Special room ceiling", Color.white, 0.05f);
            Board = Copy(lit, a.cardboard ? a.cardboard : a.wood, "Special room cardboard", new Color(0.9f, 0.85f, 0.78f), 0.05f);
            Grid = Copy(lit, a.ceilingGrid ? a.ceilingGrid : a.ceilingTile, "Special room ceiling grid", Color.white, 0.3f);
            Baseboard = Copy(lit, a.baseboard ? a.baseboard : a.paintedWall, "Special room baseboard", Color.white, 0.25f);
        }

        static Material Copy(Shader lit, Material src, string name, Color tint, float smooth)
        {
            var m = new Material(lit) { name = name, hideFlags = HideFlags.DontSave };
            if (src)
            {
                var tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
                if (tex)
                {
                    m.SetTexture("_BaseMap", tex);
                    m.SetTextureScale("_BaseMap", src.GetTextureScale(src.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex"));
                }
                // Its normal map too (the carpet's pile, the tiles' fissures, the wall's plaster).
                var bump = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
                if (bump)
                {
                    m.SetTexture("_BumpMap", bump);
                    m.SetFloat("_BumpScale", src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f);
                    m.EnableKeyword("_NORMALMAP");
                }
                if (src.HasProperty("_BaseColor")) tint *= src.GetColor("_BaseColor");
            }
            tint.a = 1f;
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            return m;
        }
    }
}
