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

        public static void Ensure(OfficeBiomeAssets a)
        {
            if (Wall && Floor && Ceiling && Board) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit || !a) return;
            Wall = Copy(lit, a.paintedWall ? a.paintedWall : a.wallpaper, "Special room wall", new Color(0.78f, 0.74f, 0.64f), 0.12f);
            Floor = Copy(lit, a.wood ? a.wood : a.pallet, "Special room floor", new Color(0.55f, 0.5f, 0.44f), 0.2f);
            Ceiling = Copy(lit, a.ceilingPlaster ? a.ceilingPlaster : a.paintedWall, "Special room ceiling", new Color(0.72f, 0.7f, 0.64f), 0.05f);
            Board = Copy(lit, a.cardboard ? a.cardboard : a.wood, "Special room cardboard", new Color(0.9f, 0.85f, 0.78f), 0.05f);
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
