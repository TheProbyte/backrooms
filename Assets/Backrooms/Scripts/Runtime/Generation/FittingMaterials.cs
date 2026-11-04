using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Finishes for the rarer things left in Level 0 (2026-11-03; OfficeChunkBuilder.Damp, .Fittings, .Leftovers): red
    /// painted steel (fire gear), brass (floor boxes), rubber, an orange extension cord and grey duct tape, cubicle fabric,
    /// standing water, and the printed faces of the signs (FIRE, PULL, FIRE EXTINGUISHER, a thermostat's display) drawn
    /// with a 5 x 7 block font. Copies of the laminate (Backrooms/Lit, so they take the light grid like everything else),
    /// made on the main thread when the world starts; the chunk builder only refers to them.
    /// </summary>
    public static class FittingMaterials
    {
        public static Material RedPaint { get; private set; }
        public static Material Brass { get; private set; }
        public static Material Rubber { get; private set; }
        public static Material Cord { get; private set; }
        public static Material Tape { get; private set; }
        public static Material Fabric { get; private set; }
        public static Material Water { get; private set; }
        public static Material Bucket { get; private set; }
        public static Material WindowPaint { get; private set; }
        public static Material SignExtinguisher { get; private set; }
        public static Material SignFire { get; private set; }
        public static Material PullFace { get; private set; }
        public static Material Display { get; private set; }
        public static Material TubeEnd { get; private set; }
        public static bool Ready => RedPaint && Brass && Rubber && Cord && Tape && Fabric && Water && Bucket && SignFire;

        public static void Ensure(OfficeBiomeAssets a)
        {
            if (Ready || !a) return;
            var src = a.laminate ? a.laminate : a.baseboard;
            if (!src) return;
            RedPaint = Make(src, "Fire red", new Color(0.52f, 0.04f, 0.035f), 0.5f, 0f);
            Brass = Make(src, "Floor box brass", new Color(0.5f, 0.38f, 0.17f), 0.55f, 0.85f);
            Rubber = Make(src, "Rubber", new Color(0.05f, 0.05f, 0.05f), 0.18f, 0f);
            Cord = Make(src, "Extension cord", new Color(0.78f, 0.26f, 0.04f), 0.42f, 0f);
            Tape = Make(src, "Duct tape", new Color(0.5f, 0.5f, 0.49f), 0.38f, 0.15f);
            Bucket = Make(src, "Bucket plastic", new Color(0.62f, 0.5f, 0.1f), 0.35f, 0f);
            Water = Make(src, "Standing water", new Color(0.04f, 0.045f, 0.04f), 0.96f, 0f);
            TubeEnd = Make(src, "Tube end (blackened)", new Color(0.09f, 0.085f, 0.08f), 0.8f, 0f);
            // Cubicle panels: blue-grey, with the office chair fabric's weave in relief (its colour is charcoal: tinted, it
            // came out black).
            Fabric = Make(src, "Partition fabric", new Color(0.31f, 0.35f, 0.4f), 0.06f, 0f);
            var weave = a.chairFabric && a.chairFabric.HasProperty("_BumpMap") ? a.chairFabric.GetTexture("_BumpMap") : null;
            if (weave)
            {
                Fabric.SetTexture("_BumpMap", weave);
                Fabric.SetTextureScale("_BumpMap", a.chairFabric.GetTextureScale("_BumpMap"));
                Fabric.SetFloat("_BumpScale", 1f);
                Fabric.EnableKeyword("_NORMALMAP");
            }
            // A filled-in window's panel: the painted wall, a shade off (painted later, another batch).
            var wall = a.paintedWall ? MaterialMaps.For(a.paintedWall) : src;
            WindowPaint = new Material(wall) { name = "Window infill paint", hideFlags = HideFlags.DontSave };
            WindowPaint.SetColor("_BaseColor", wall.GetColor("_BaseColor") * new Color(1.04f, 1.03f, 0.98f, 1f));

            var red = new Color(0.62f, 0.05f, 0.04f);
            SignExtinguisher = Printed(src, "Sign: fire extinguisher", Paint(256, 128, red, Color.white, new[] { "FIRE", "EXTINGUISHER" }, 0.08f));
            SignFire = Printed(src, "Sign: fire", Paint(128, 64, red, Color.white, new[] { "FIRE" }, 0.18f));
            PullFace = Printed(src, "Pull station face", PullTexture());
            Display = Printed(src, "Thermostat display", Paint(64, 32, new Color(0.55f, 0.6f, 0.5f), new Color(0.12f, 0.14f, 0.11f), new[] { "68" }, 0.18f));
            if (Display) Display.SetFloat("_Smoothness", 0.7f);
        }

        static Material Make(Material src, string name, Color c, float smooth, float metal)
        {
            var m = new Material(src) { name = name, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            return m;
        }

        static Material Printed(Material src, string name, Texture2D tex)
        {
            var m = Make(src, name, Color.white, 0.4f, 0f);
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            return m;
        }

        // ---------------- Printing ----------------

        /// <summary>5 x 7 block capitals and digits, a row a string (top first).</summary>
        static readonly string[] Glyphs =
        {
            "A01110100011000111111100011000110001", "B11110100011000111110100011000111110", "C01110100011000010000100001000101110",
            "D11110100011000110001100011000111110", "E11111100001000011110100001000011111", "F11111100001000011110100001000010000",
            "G01110100011000010111100011000101111", "H10001100011000111111100011000110001", "I01110001000010000100001000010001110",
            "J00111000100001000010000101001001100", "K10001100101010011000101001001010001", "L10000100001000010000100001000011111",
            "M10001110111010110101100011000110001", "N10001100011100110101100111000110001", "O01110100011000110001100011000101110",
            "P11110100011000111110100001000010000", "Q01110100011000110001101011001001101", "R11110100011000111110101001001010001",
            "S01111100001000001110000010000111110", "T11111001000010000100001000010000100", "U10001100011000110001100011000101110",
            "V10001100011000110001100010101000100", "W10001100011000110101101011010101010", "X10001100010101000100010101000110001",
            "Y10001100010101000100001000010000100", "Z11111000010001000100010001000011111",
            "001110100011001110101110011000101110", "100100011000010000100001000010001110", "201110100010000100010001000100011111",
            "311111000100010000010000011000101110", "400010001100101010010111110001000010", "511111100001111000001000011000101110",
            "600110010001000011110100011000101110", "711111000010001000100010000100001000", "801110100011000101110100011000101110",
            "901110100011000101111000010001001100",
        };

        static bool Dot(char c, int col, int row)
        {
            foreach (var g in Glyphs)
                if (g[0] == c) return g[1 + row * 5 + col] == '1';
            return false;
        }

        /// <summary>Lines of text in block capitals, centred, ink on a ground (margin: share of the height kept clear).</summary>
        public static Texture2D Paint(int w, int h, Color ground, Color ink, string[] lines, float margin, bool readable = false)
        {
            const int S = 4;                                   // supersampled, then averaged: soft edges
            int W = w * S, Hh = h * S;
            var mask = new float[W * Hh];
            int longest = 1;
            foreach (var l in lines) longest = Mathf.Max(longest, l.Length);
            // Dot size: the text as big as fits, lines a dot and a half apart.
            float usableH = Hh * (1f - 2f * margin), usableW = W * (1f - 2f * margin);
            float dot = Mathf.Min(usableH / (lines.Length * 7f + (lines.Length - 1) * 2.5f), usableW / (longest * 6f - 1f));
            float y0 = (Hh - (lines.Length * 7f + (lines.Length - 1) * 2.5f) * dot) * 0.5f;
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                float x0 = (W - (line.Length * 6f - 1f) * dot) * 0.5f;
                float ly = y0 + li * 9.5f * dot;
                for (int ci = 0; ci < line.Length; ci++)
                for (int row = 0; row < 7; row++)
                for (int col = 0; col < 5; col++)
                {
                    if (!Dot(line[ci], col, row)) continue;
                    int px0 = Mathf.RoundToInt(x0 + (ci * 6 + col) * dot), px1 = Mathf.RoundToInt(x0 + (ci * 6 + col + 1) * dot);
                    // (Texture rows run bottom up: the first line at the top.)
                    int py1 = Hh - Mathf.RoundToInt(ly + row * dot), py0 = Hh - Mathf.RoundToInt(ly + (row + 1) * dot);
                    for (int y = Mathf.Max(0, py0); y < Mathf.Min(Hh, py1); y++)
                    for (int x = Mathf.Max(0, px0); x < Mathf.Min(W, px1); x++) mask[y * W + x] = 1f;
                }
            }
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = 0f;
                for (int j = 0; j < S; j++) for (int i = 0; i < S; i++) s += mask[(y * S + j) * W + x * S + i];
                px[y * w + x] = Color.Lerp(ground, ink, s / (S * S));
            }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = string.Join(" ", lines), wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            t.SetPixels(px);
            t.Apply(true, !readable);
            return t;
        }

        /// <summary>A manual pull station's face (128 x 192): FIRE across the top, a white bar with PULL on it, DOWN below.</summary>
        static Texture2D PullTexture()
        {
            var red = new Color(0.62f, 0.05f, 0.04f);
            var top = Paint(128, 64, red, Color.white, new[] { "FIRE" }, 0.16f, true);
            var bar = Paint(128, 64, new Color(0.92f, 0.91f, 0.88f), red, new[] { "PULL" }, 0.2f, true);
            var low = Paint(128, 64, red, Color.white, new[] { "DOWN" }, 0.26f, true);
            var t = new Texture2D(128, 192, TextureFormat.RGBA32, true) { name = "Pull station", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            t.SetPixels(0, 128, 128, 64, top.GetPixels());
            t.SetPixels(0, 64, 128, 64, bar.GetPixels());
            t.SetPixels(0, 0, 128, 64, low.GetPixels());
            t.Apply(true, true);
            foreach (var x in new[] { top, bar, low }) { if (Application.isPlaying) Object.Destroy(x); else Object.DestroyImmediate(x); }
            return t;
        }
    }
}
