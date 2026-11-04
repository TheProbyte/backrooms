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
        public static Material Drop { get; private set; }
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
            s_src = src;
            RedPaint = Make(src, "Fire red", new Color(0.52f, 0.04f, 0.035f), 0.5f, 0f);
            Brass = Make(src, "Floor box brass", new Color(0.5f, 0.38f, 0.17f), 0.55f, 0.85f);
            Rubber = Make(src, "Rubber", new Color(0.05f, 0.05f, 0.05f), 0.18f, 0f);
            Cord = Interaction.ItemModels.Mat("CordOrange");          // (the same as the plug-in cords' and their sockets')
            BucketWater = Make(src, "Bucket water", new Color(0.11f, 0.095f, 0.07f), 0.9f, 0f);
            Tape = Make(src, "Duct tape", new Color(0.5f, 0.5f, 0.49f), 0.38f, 0.15f);
            Bucket = Make(src, "Bucket plastic", new Color(0.62f, 0.5f, 0.1f), 0.35f, 0f);
            // Standing water on a hard floor: darker than the floor, glossy (it shows what is round it, not a black hole).
            Water = Make(src, "Standing water", new Color(0.2f, 0.19f, 0.16f), 0.97f, 0f);
            // A drop of water: a speck catching the light.
            Drop = Make(src, "Water drop", new Color(0.62f, 0.64f, 0.66f), 0.98f, 0f);
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

        // ---------------- The modelled fittings' slots (FittingModels) ----------------

        static Material s_src;
        static readonly System.Collections.Generic.Dictionary<string, Material> s_slots = new System.Collections.Generic.Dictionary<string, Material>();
        /// <summary>The bucket's water: dirty, grey-brown, glossy.</summary>
        public static Material BucketWater { get; private set; }
        public static Material Stainless => FurnitureMaterials.Stainless;

        /// <summary>
        /// The finish for a model's material slot (Tools/Blender/build_fittings.py names them): its colour, gloss and
        /// metalness, a fine detail normal map by what it is made of (ItemModels' maps at the models' 0.25 m UVs: brushed
        /// metal, painted metal, moulded plastic, rubber, card), or its printed face. Labels a little glossier than the
        /// paint under them (a printed sticker). Main thread (made as the models load).
        /// </summary>
        public static Material ForSlot(string slot)
        {
            if (s_slots.TryGetValue(slot, out var m) && m) return m;
            var src = s_src;
            if (!src) return null;
            Material M(Color c, float smooth, float metal, string detail)
            {
                var x = Make(src, "Fitting " + slot, c, smooth, metal);
                if (detail != null) Interaction.ItemModels.AddDetail(x, detail);
                return x;
            }
            Color red = new Color(0.55f, 0.03f, 0.028f), dark = new Color(0.025f, 0.025f, 0.025f);
            switch (slot)
            {
                case "ExtRed": case "PullRed": case "StrobeRed": m = M(red, 0.6f, 0f, "lampbody"); break;
                case "ExtChrome": m = M(new Color(0.8f, 0.8f, 0.78f), 0.82f, 1f, "screw"); break;
                case "ExtBlack": case "PullDark": case "StrobeDark": case "ThermoDark": case "CloserDark": case "PanelDark":
                case "GrilleDark": case "FountainDark": case "BoxDark":
                    m = M(dark, slot == "GrilleDark" || slot == "BoxDark" ? 0.1f : 0.4f, 0f, "chicken"); break;
                case "ExtSeal": m = M(new Color(0.1f, 0.5f, 0.15f), 0.5f, 0f, "plastic"); break;
                case "ExtTag": m = M(new Color(0.92f, 0.82f, 0.32f), 0.2f, 0f, "cardboard"); break;
                case "ExtLabel": m = Printed(src, "Fitting label", LabelTexture()); m.SetFloat("_Smoothness", 0.62f); break;
                case "ExtGauge": m = Printed(src, "Fitting gauge", GaugeTexture()); m.SetFloat("_Smoothness", 0.9f); break;
                case "CabinetPaint": m = FurnitureMaterials.Enamel; break;
                case "CabinetFrame": case "WindowFrame": case "PanelFrame": m = M(new Color(0.68f, 0.68f, 0.66f), 0.6f, 0.85f, "screw"); break;
                case "GlassShard": m = M(new Color(0.42f, 0.47f, 0.47f), 0.97f, 0f, null); break;
                case "CabinetLabel": m = Printed(src, "Fitting cabinet strip", Paint(256, 32, new Color(0.94f, 0.93f, 0.9f), new Color(0.62f, 0.05f, 0.04f), new[] { "FIRE EXTINGUISHER" }, 0.18f)); break;
                case "BracketSteel": m = M(new Color(0.09f, 0.09f, 0.09f), 0.45f, 0.5f, "lampbody"); break;
                case "PullHandle": m = M(new Color(0.9f, 0.89f, 0.86f), 0.5f, 0f, "plastic"); break;
                case "BoxGrey": m = M(new Color(0.35f, 0.35f, 0.34f), 0.45f, 0.4f, "lampbody"); break;
                case "PullFire": m = Printed(src, "Fitting FIRE", Paint(128, 48, new Color(0.62f, 0.05f, 0.04f), new Color(0.95f, 0.94f, 0.92f), new[] { "FIRE" }, 0.12f)); break;
                case "PullText": m = Printed(src, "Fitting PULL DOWN", Paint(128, 40, new Color(0.92f, 0.91f, 0.88f), new Color(0.62f, 0.05f, 0.04f), new[] { "PULL DOWN" }, 0.16f)); break;
                case "StrobeLens": m = M(new Color(0.9f, 0.9f, 0.88f), 0.88f, 0f, null); break;
                case "StrobeFire": m = Printed(src, "Fitting strobe FIRE", Paint(128, 32, new Color(0.62f, 0.05f, 0.04f), new Color(0.95f, 0.94f, 0.92f), new[] { "FIRE" }, 0.1f)); break;
                case "ThermoBeige": m = M(new Color(0.74f, 0.7f, 0.6f), 0.45f, 0f, "plastic"); break;
                case "ThermoLcd": m = Printed(src, "Fitting LCD", Paint(128, 64, new Color(0.55f, 0.6f, 0.5f), new Color(0.12f, 0.14f, 0.11f), new[] { "68", "HEAT" }, 0.1f)); m.SetFloat("_Smoothness", 0.85f); break;
                case "ThermoGold": m = M(new Color(0.72f, 0.55f, 0.25f), 0.65f, 0.9f, "screw"); break;
                case "ThermoScale": m = Printed(src, "Fitting dial", ScaleTexture()); m.SetFloat("_Smoothness", 0.7f); break;
                case "GrillePaint": m = M(new Color(0.86f, 0.85f, 0.82f), 0.45f, 0.2f, "lampbody"); break;
                case "FountainSteel": m = M(new Color(0.62f, 0.63f, 0.63f), 0.62f, 0.95f, "screw"); break;
                case "WindowBoard": m = WindowPaint ? WindowPaint : FurnitureMaterials.Enamel; break;
                case "WindowJoint": m = M(new Color(0.88f, 0.86f, 0.81f), 0.15f, 0f, "chalk"); break;
                case "WindowSill": m = FurnitureMaterials.Enamel; break;
                case "ScrewHead": m = M(new Color(0.5f, 0.5f, 0.48f), 0.5f, 0.6f, "screw"); break;
                case "GuardSteel": m = FurnitureMaterials.Stainless; break;
                case "CloserPaint": m = M(new Color(0.62f, 0.62f, 0.6f), 0.55f, 0.7f, "lampbody"); break;
                case "KickSteel": m = M(new Color(0.7f, 0.7f, 0.7f), 0.6f, 1f, "screw"); break;
                case "WedgeRubber": m = M(new Color(0.045f, 0.042f, 0.04f), 0.25f, 0f, "chicken"); break;
                case "FloorBrass": m = M(new Color(0.5f, 0.38f, 0.17f), 0.58f, 1f, "screw"); break;
                case "Receptacle": m = M(new Color(0.78f, 0.74f, 0.64f), 0.5f, 0f, "plate"); break;
                case "CableGrey": m = M(new Color(0.45f, 0.45f, 0.44f), 0.5f, 0f, "plastic"); break;
                case "CableBlack": m = M(dark, 0.5f, 0f, "plastic"); break;
                case "Copper": m = M(new Color(0.72f, 0.38f, 0.2f), 0.6f, 1f, null); break;
                case "BucketPlastic": m = M(new Color(0.62f, 0.5f, 0.1f), 0.5f, 0f, "plastic"); break;
                case "HandleWire": m = M(new Color(0.6f, 0.6f, 0.58f), 0.6f, 0.9f, "screw"); break;
                case "HandleGrip": m = M(new Color(0.07f, 0.07f, 0.07f), 0.45f, 0f, "plastic"); break;
                case "PanelFabric": m = Fabric; break;
                case "PanelTrim": m = M(new Color(0.22f, 0.22f, 0.22f), 0.4f, 0f, "plastic"); break;
                case "CarpetInsert": m = M(new Color(0.6f, 0.55f, 0.4f), 0.1f, 0f, "fabric"); break;
                default: m = Interaction.ItemModels.Mat(slot); break;
            }
            s_slots[slot] = m;
            return m;
        }

        /// <summary>Copies a readable texture's pixels into a pixel array at (x, y).</summary>
        static void Blit(Color[] dst, int dw, Texture2D src, int x, int y)
        {
            var p = src.GetPixels();
            for (int j = 0; j < src.height; j++)
            for (int i = 0; i < src.width; i++)
            {
                int dx = x + i, dy = y + j;
                if (dx < 0 || dy < 0 || dx >= dw || dy * dw + dx >= dst.Length) continue;
                dst[dy * dw + dx] = p[j * src.width + i];
            }
            if (Application.isPlaying) Object.Destroy(src); else Object.DestroyImmediate(src);
        }

        static Texture2D Finish(Color[] px, int w, int h, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>
        /// The extinguisher's label (256 x 512, round the front of its body): FIRE EXTINGUISHER on a red band, ABC large,
        /// the three classes (a green triangle, a red square, a blue circle) with their letters, lines of small print, 5 LB.
        /// </summary>
        static Texture2D LabelTexture()
        {
            const int w = 256, h = 512;
            var white = new Color(0.94f, 0.93f, 0.9f);
            var red = new Color(0.62f, 0.05f, 0.04f);
            var ink = new Color(0.08f, 0.08f, 0.08f);
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = white;
            Blit(px, w, Paint(w, 120, red, white, new[] { "FIRE", "EXTINGUISHER" }, 0.08f, true), 0, h - 120);
            Blit(px, w, Paint(w, 110, white, ink, new[] { "ABC" }, 0.1f, true), 0, h - 240);
            // The classes.
            var cols = new[] { new Color(0.1f, 0.5f, 0.2f), new Color(0.75f, 0.08f, 0.06f), new Color(0.1f, 0.25f, 0.65f) };
            for (int k = 0; k < 3; k++)
            {
                int cx = 48 + k * 80, cy = h - 300, r = 30;
                for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    bool inside = k == 0 ? (dy >= -r * 0.8f && Mathf.Abs(dx) <= (r * 0.9f - dy) * 0.6f && dy <= r * 0.9f)
                                : k == 1 ? (Mathf.Abs(dx) < r * 0.8f && Mathf.Abs(dy) < r * 0.8f)
                                : dx * dx + dy * dy < r * r * 0.75f;
                    if (inside && x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = cols[k];
                }
            }
            Blit(px, w, Paint(w, 40, white, ink, new[] { "A    B    C" }, 0.12f, true), 0, h - 370);
            // Small print: grey lines of varying length.
            var rng = new System.Random(7);
            for (int line = 0; line < 9; line++)
            {
                int y0 = h - 392 - line * 11, len = 120 + rng.Next(110);
                for (int y = y0; y < y0 + 4; y++)
                for (int x = 14; x < 14 + len && x < w - 14; x++)
                    if (((x / 3) % 7) != 0) px[y * w + x] = Color.Lerp(white, ink, 0.55f);
            }
            Blit(px, w, Paint(w, 46, white, red, new[] { "5 LB" }, 0.12f, true), 0, 6);
            return Finish(px, w, h, "Extinguisher label");
        }

        /// <summary>The gauge's dial (128 x 128): red, green in the middle, red; ticks; the needle in the green; PSI.</summary>
        static Texture2D GaugeTexture()
        {
            const int s = 128;
            var px = new Color[s * s];
            var face = new Color(0.95f, 0.95f, 0.92f);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - 63.5f, dy = y - 63.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;                     // 90 up
                float t = Mathf.InverseLerp(210f, -30f, a > 210f || a < -90f ? (a < -90f ? a + 360f : a) : a);
                var c = face;
                if (r > 38f && r < 50f && a < 210f && a > -30f)
                    c = t > 0.38f && t < 0.62f ? new Color(0.15f, 0.6f, 0.2f) : new Color(0.75f, 0.1f, 0.08f);
                if (r > 50f && r < 58f && a < 212f && a > -32f && Mathf.Repeat(a + 30f, 24f) < 2.5f) c = new Color(0.1f, 0.1f, 0.1f);
                // The needle, up into the green.
                if (Mathf.Abs(dx) < 2.2f - dy * 0.03f && dy > -6f && dy < 46f) c = new Color(0.05f, 0.05f, 0.05f);
                if (r < 7f) c = new Color(0.2f, 0.2f, 0.2f);
                if (r > 62f) c = new Color(0.5f, 0.5f, 0.5f);
                px[y * s + x] = c;
            }
            Blit(px, s, Paint(48, 16, face, new Color(0.1f, 0.1f, 0.1f), new[] { "PSI" }, 0.1f, true), 40, 18);
            return Finish(px, s, s, "Extinguisher gauge");
        }

        /// <summary>The round thermostat's scale (128 x 128): ticks round the rim, longer every fifth, a blue-to-red band inside.</summary>
        static Texture2D ScaleTexture()
        {
            const int s = 128;
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - 63.5f, dy = y - 63.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                var c = new Color(0.9f, 0.88f, 0.82f);
                int tick = Mathf.RoundToInt(a / 7.5f);
                bool onTick = Mathf.Abs(a - tick * 7.5f) < 1.1f;
                if (onTick && r > (tick % 5 == 0 ? 46f : 52f) && r < 59f && a > -60f && a < 240f) c = new Color(0.12f, 0.12f, 0.12f);
                if (r > 36f && r < 41f && a > -60f && a < 240f)
                    c = Color.Lerp(new Color(0.75f, 0.12f, 0.08f), new Color(0.15f, 0.3f, 0.75f), Mathf.InverseLerp(-60f, 240f, a));
                if (r > 62f) c = new Color(0.6f, 0.58f, 0.52f);
                px[y * s + x] = c;
            }
            return Finish(px, s, s, "Thermostat scale");
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
