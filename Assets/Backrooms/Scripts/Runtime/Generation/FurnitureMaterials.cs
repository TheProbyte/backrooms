using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Plain-coloured finishes for the good rooms' furniture (SpecialRooms, OfficeChunkBuilder.Special): copies of the
    /// laminate material (Backrooms/Lit, so they take the light grid like everything else) without its texture. Made on
    /// the main thread when the world starts; the chunk builder only refers to them.
    /// </summary>
    public static class FurnitureMaterials
    {
        public static Material Enamel { get; private set; }      // fridge, mop sink
        public static Material Steel { get; private set; }       // painted steel: shelving, the breaker panel
        public static Material Stainless { get; private set; }   // sink, taps
        public static Material Dark { get; private set; }        // screens, handles, rubber, plinths
        public static Material Beige { get; private set; }       // old computer plastic, the microwave
        public static bool Ready => Enamel && Steel && Stainless && Dark && Beige;

        public static void Ensure(OfficeBiomeAssets a)
        {
            if (Ready || !a) return;
            var src = a.laminate ? a.laminate : a.baseboard;
            if (!src) return;
            Enamel = Make(src, "Furniture enamel", new Color(0.84f, 0.83f, 0.78f), 0.55f, 0f);
            Steel = Make(src, "Furniture steel", new Color(0.46f, 0.48f, 0.47f), 0.35f, 0.2f);
            Stainless = Make(src, "Furniture stainless", new Color(0.6f, 0.61f, 0.62f), 0.62f, 0.75f);
            Dark = Make(src, "Furniture dark", new Color(0.045f, 0.045f, 0.05f), 0.7f, 0f);
            Beige = Make(src, "Furniture beige", new Color(0.74f, 0.7f, 0.6f), 0.3f, 0f);
        }

        static Material Make(Material src, string name, Color c, float smooth, float metal)
        {
            var m = new Material(src) { name = name, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            return m;
        }
    }
}
