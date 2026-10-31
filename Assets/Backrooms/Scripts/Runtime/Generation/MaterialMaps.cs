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
            DomeFitting.Materials(a);
            Ceilings(a);
            Floors(a);
            Homes(a);
        }

        // ---------------- Homes (the Living rooms biome) ----------------

        /// <summary>
        /// A home's walls (Room.home, WallFinish.OrangePeel): sprayed orange-peel drywall, eggshell off-white paint. A copy of
        /// the painted wall (its keywords: no new shader variant) with Resources/Textures/Home/OrangePeel_* (generated: fine
        /// droplets 2-6 mm over a gentle wave, one tile = 0.6 m).
        /// </summary>
        public static Material OrangePeel;
        /// <summary>The rarer papered home (WallFinish.SkyWallpaper): the owner's blue-sky clouds pattern (Textures/Home/SkyClouds,
        /// one repeat = 1.6 m), on a copy of the wallpaper with a little paper tooth.</summary>
        public static Material SkyWallpaper;
        /// <summary>A home's carpet (FloorFinish.HomeCarpet), one of a few early-2000s shades by room: sage, beige, mushroom, slate.</summary>
        public static Material[] HomeCarpets = new Material[0];
        /// <summary>A home's baseboard: the world's baseboard painted off-white.</summary>
        public static Material HomeBaseboard;

        static void Homes(OfficeBiomeAssets a)
        {
            OrangePeel = SkyWallpaper = HomeBaseboard = null; HomeCarpets = new Material[0];
            if (a.baseboard && a.baseboard.HasProperty(BaseColor)) HomeBaseboard = Tinted(For(a.baseboard), " (home, painted)", new Color(0.82f, 0.8f, 0.76f, 1f));
            var peelN = Resources.Load<Texture2D>("Textures/Home/OrangePeel_Normal");
            var peelA = Resources.Load<Texture2D>("Textures/Home/OrangePeel_Albedo");
            var sky = Resources.Load<Texture2D>("Textures/Home/SkyClouds");
            void Bump(Material m, float strength)
            {
                if (!peelN || !m.HasProperty(BumpMap)) return;
                m.SetTexture(BumpMap, peelN); m.SetFloat(BumpScale, strength); m.EnableKeyword("_NORMALMAP");
            }
            var paint = a.paintedWall ? a.paintedWall : a.wallpaper;
            if (paint && paint.HasProperty(BaseMap))
            {
                OrangePeel = Copy(paint, " (orange peel)", peelA);
                OrangePeel.SetTextureScale(BaseMap, new Vector2(2f, 4.5f));            // 0.6 m tiles (WallUV: 1.2 m by 2.7 m)
                OrangePeel.SetColor(BaseColor, new Color(0.86f, 0.84f, 0.78f, 1f));      // off-white, a little warm
                OrangePeel.SetFloat("_Smoothness", 0.2f);                               // eggshell
                Bump(OrangePeel, 0.55f);
            }
            if (a.wallpaper && sky && a.wallpaper.HasProperty(BaseMap))
            {
                SkyWallpaper = Copy(a.wallpaper, " (sky clouds)", sky);
                SkyWallpaper.SetTextureScale(BaseMap, new Vector2(1.2f / 1.6f, 2.7f / 1.6f));   // one repeat = 1.6 m: clouds about 0.45 m across
                SkyWallpaper.SetTextureOffset(BaseMap, Vector2.zero);
                SkyWallpaper.SetColor(BaseColor, new Color(0.86f, 0.87f, 0.86f, 1f));      // a little faded
                SkyWallpaper.SetFloat("_Smoothness", 0.22f);                            // vinyl-coated paper
                Bump(SkyWallpaper, 0.15f);
            }
            if (a.carpet && a.carpet.HasProperty(BaseColor))
            {
                // (The carpet's own colour is a yellow 0.8 0.71 0.44; these replace it.)
                var shades = new[] { new Color(0.56f, 0.6f, 0.5f), new Color(0.78f, 0.72f, 0.6f), new Color(0.6f, 0.53f, 0.45f), new Color(0.5f, 0.53f, 0.55f) };
                HomeCarpets = new Material[shades.Length];
                for (int i = 0; i < shades.Length; i++) HomeCarpets[i] = Tinted(a.carpet, " (home " + i + ")", shades[i]);
            }
        }

        // ---------------- Ceiling tiles ----------------

        /// <summary>
        /// The acoustic ceiling's variants (OfficeChunkBuilder.Wear): two copies whose texture has a stain on every one of
        /// its 16 tiles (water rings with tide lines, mould specks; the texture is 4 x 4 tiles, so a tile drawn with a copy
        /// shows its own stain: 32 different ones), a yellowed copy, and the dark space above the tiles (the plenum, seen
        /// where a tile is lifted, shifted or missing). Same shader keywords as the ceiling: no new variants. Null without
        /// the ceiling material.
        /// </summary>
        public static Material CeilingStainedA, CeilingStainedB, CeilingYellowed, Plenum;
        /// <summary>Tiles from other batches (a replaced run that never matched): a shade whiter, a shade greyer.</summary>
        public static Material CeilingBatchA, CeilingBatchB;
        static Texture2D s_stainedA, s_stainedB;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static Material PlenumWood, PlenumDeck, PlenumBatt, PlenumMetal, PlenumDuct, TileBack;
        /// <summary>A tile with a crack across it (each of the 16 in its texture a different one).</summary>
        public static Material CeilingCracked;
        /// <summary>Small things on the ceiling and floor: an exit sign's glowing face, a smoke detector's red light, aluminium (threshold strips).</summary>
        public static Material ExitSign, LedRed, Aluminium;
        static Texture2D s_cracked, s_exit, s_exitFace;

        /// <summary>One of the world's dressing copies (none of them casts a shadow-map shadow).</summary>
        public static bool IsCeilingVariant(Material m) =>
            m && (m == CeilingStainedA || m == CeilingStainedB || m == CeilingYellowed || m == CeilingBatchA || m == CeilingBatchB || m == Plenum || m == PlenumWood || m == PlenumDeck
                  || m == PlenumBatt || m == PlenumMetal || m == PlenumDuct || m == TileBack || m == CeilingCracked || m == ExitSign
                  || m == LedRed || m == Aluminium || m == PaintFresh || m == CarpetOdd || m == CarpetOddRef);

        /// <summary>A photo's colour and normal maps (Resources/Textures/Photo) on a material, if they are there.</summary>
        static void PhotoMaps(Material m, string photo, float bump)
        {
            if (!m) return;
            var a = Destruction.DemolitionMaterials.Photo(photo);
            if (!a) return;
            m.SetTexture(BaseMap, a);
            var n = Destruction.DemolitionMaterials.Photo(photo, true);
            if (n && m.HasProperty(BumpMap)) { m.SetTexture(BumpMap, n); m.SetFloat(BumpScale, bump); m.EnableKeyword("_NORMALMAP"); }
        }

        // ---------------- Floors ----------------

        /// <summary>
        /// The warehouse halls' floor (OfficeBiomeAssets.FloorMaterial): trowelled concrete from a photo, a copy of the carpet
        /// material (the same keywords: no new shader variant) with the concrete's maps, white, a little sheen. One texture
        /// = 3 m, as the carpet's. Null without the photo or the carpet.
        /// </summary>
        public static Material Concrete;

        /// <summary>Newer paint over a repair on a painted wall (OfficeChunkBuilder.Paper): the wall's paint, a little brighter.</summary>
        public static Material PaintFresh;

        /// <summary>A carpet tile replaced from another lot (OfficeChunkBuilder.FloorMarks): the carpet (or the reference carpet), another shade.</summary>
        public static Material CarpetOdd, CarpetOddRef;

        static void Floors(OfficeBiomeAssets a)
        {
            Concrete = null;
            PaintFresh = CarpetOdd = CarpetOddRef = null;
            if (a.carpet && a.carpet.HasProperty(BaseColor)) CarpetOdd = Tinted(a.carpet, " (odd tile)", a.carpet.GetColor(BaseColor) * new Color(0.86f, 0.83f, 0.78f, 1f));
            if (a.referenceCarpet && a.referenceCarpet.HasProperty(BaseColor)) CarpetOddRef = Tinted(a.referenceCarpet, " (odd tile)", a.referenceCarpet.GetColor(BaseColor) * new Color(0.86f, 0.83f, 0.78f, 1f));
            if (a.paintedWall && a.paintedWall.HasProperty(BaseColor))
                PaintFresh = Tinted(a.paintedWall, " (fresh paint)", a.paintedWall.GetColor(BaseColor) * new Color(1.07f, 1.06f, 1.03f, 1f));
            var src = a.carpet;
            var tex = Destruction.DemolitionMaterials.Photo("Concrete");
            if (!src || !tex || !src.HasProperty(BaseMap)) return;
            Concrete = Copy(src, " (concrete)", tex);
            Concrete.SetColor(BaseColor, Color.white);
            Concrete.SetTextureScale(BaseMap, Vector2.one);
            Concrete.SetFloat("_Smoothness", 0.3f);
            var n = Destruction.DemolitionMaterials.Photo("Concrete", true);
            if (n && Concrete.HasProperty(BumpMap)) { Concrete.SetTexture(BumpMap, n); Concrete.SetFloat(BumpScale, 1f); }
        }

        static Material Tinted(Material src, string suffix, Color tint)
        {
            if (!src) return null;
            var m = new Material(src) { name = src.name + suffix, hideFlags = HideFlags.DontSave };
            m.SetColor(BaseColor, tint);
            return m;
        }

        static void Ceilings(OfficeBiomeAssets a)
        {
            CeilingStainedA = CeilingStainedB = CeilingYellowed = Plenum = CeilingBatchA = CeilingBatchB = null;
            var src = a.ceilingTile;
            if (!src || !src.HasProperty(BaseMap)) return;
            var tex = src.GetTexture(BaseMap) as Texture2D;
            if (tex)
            {
                if (!s_stainedA) s_stainedA = Stained(tex, 7);
                if (!s_stainedB) s_stainedB = Stained(tex, 31);
                CeilingStainedA = Copy(src, " (stained)", s_stainedA);
                CeilingStainedB = Copy(src, " (stained B)", s_stainedB);
                if (!s_cracked) s_cracked = Cracked(tex, 53);
                CeilingCracked = Copy(src, " (cracked)", s_cracked);
            }
            Color c = src.HasProperty(BaseColor) ? src.GetColor(BaseColor) : Color.white;
            CeilingBatchA = Tinted(src, " (new batch)", c * new Color(1.05f, 1.05f, 1.055f, 1f));
            CeilingBatchB = Tinted(src, " (old batch)", c * new Color(0.93f, 0.915f, 0.87f, 1f));
            CeilingYellowed = Copy(src, " (yellowed)", null);
            CeilingYellowed.SetColor(BaseColor, c * new Color(0.97f, 0.93f, 0.8f, 1f));
            Plenum = Copy(src, " (plenum)", null);
            Plenum.SetColor(BaseColor, new Color(0.016f, 0.015f, 0.014f, 1f));       // dark: the room's light barely gets in
            // What is up there (seen past a missing or slid tile): old joists and the boards they carry, dusty fibreglass,
            // grey metal (pipe, hanger wire, cable), a foil flex duct, and the tiles' own fibrous backs. The broken walls'
            // materials, aged, dusty and dark: the world's light reaches up there as it does the room, so they are dimmed
            // to a little over half (what little comes up through a gap: dark, but the shapes show). Same shader and keywords.
            PlenumWood = Tinted(Destruction.DemolitionMaterials.Wood, " (old joist)", new Color(0.24f, 0.18f, 0.12f));
            PlenumDeck = Tinted(Destruction.DemolitionMaterials.Wood, " (deck)", new Color(0.16f, 0.125f, 0.09f));
            // Old, weathered wood up there (a photo), not the new pine of the walls; laid with metre UVs.
            PhotoMaps(PlenumWood, "OldWood", 1f);
            PhotoMaps(PlenumDeck, "OldWood", 1f);
            PlenumBatt = Tinted(Destruction.DemolitionMaterials.BattYellow, " (dusty)", new Color(0.37f, 0.31f, 0.17f));
            PlenumMetal = Tinted(Destruction.DemolitionMaterials.Steel, " (dusty)", new Color(0.24f, 0.245f, 0.25f));
            PlenumDuct = Tinted(Destruction.DemolitionMaterials.Steel, " (foil duct)", new Color(0.33f, 0.33f, 0.32f));
            PhotoMaps(PlenumDuct, "FlexDuct", 1f);
            if (PlenumDuct) PlenumDuct.SetFloat("_Smoothness", 0.55f);
            TileBack = Tinted(Destruction.DemolitionMaterials.BackPaper, " (tile back)", new Color(0.28f, 0.27f, 0.25f));
            Aluminium = Tinted(Destruction.DemolitionMaterials.Steel, " (aluminium)", new Color(0.7f, 0.7f, 0.68f));
            Small(a);
        }

        /// <summary>The exit sign's face (red letters that glow, on emergency power: never switched) and the detector's light.</summary>
        static void Small(OfficeBiomeAssets a)
        {
            ExitSign = LedRed = null;
            if (!a.lensOn || !a.lensOn.HasProperty(EmissionMap)) return;
            if (!s_exit) s_exit = ExitTexture(false);
            if (!s_exitFace) s_exitFace = ExitTexture(true);
            // Red letters on a white face, lit from inside: the letters red in the face too, and a deep red glow (a bright
            // one goes white in the camcorder's highlights).
            ExitSign = new Material(a.lensOn) { name = "Exit sign", hideFlags = HideFlags.DontSave };
            ExitSign.SetTexture(BaseMap, s_exitFace);
            ExitSign.SetColor(BaseColor, Color.white);
            ExitSign.SetTexture(EmissionMap, s_exit);
            ExitSign.SetColor(EmissionColor, new Color(2.2f, 0.07f, 0.045f));
            LedRed = new Material(a.lensOn) { name = "Indicator light", hideFlags = HideFlags.DontSave };
            LedRed.SetTexture(BaseMap, Texture2D.whiteTexture);
            LedRed.SetColor(BaseColor, new Color(0.5f, 0.05f, 0.04f));
            LedRed.SetTexture(EmissionMap, Texture2D.whiteTexture);
            LedRed.SetColor(EmissionColor, new Color(3f, 0.12f, 0.06f));
        }

        /// <summary>EXIT in block letters, 256 x 128: the emission (letters bright, the panel faintly lit) or the face (red on white).</summary>
        static Texture2D ExitTexture(bool face)
        {
            const int W = 256, Hh = 128;
            var v = new float[W * Hh];
            for (int i = 0; i < v.Length; i++) v[i] = 0.05f;
            void Rect(float x0, float y0, float x1, float y1)
            {
                for (int y = Mathf.Max(0, (int)y0); y < Mathf.Min(Hh, (int)y1); y++)
                for (int x = Mathf.Max(0, (int)x0); x < Mathf.Min(W, (int)x1); x++) v[y * W + x] = 1f;
            }
            void Line(float x0, float y0, float x1, float y1, float th)
            {
                Vector2 a0 = new Vector2(x0, y0), d = new Vector2(x1 - x0, y1 - y0);
                float l2 = d.sqrMagnitude;
                for (int y = 0; y < Hh; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f) - a0;
                    float t = Mathf.Clamp01(Vector2.Dot(p, d) / l2);
                    if ((p - d * t).magnitude < th * 0.5f) v[y * W + x] = 1f;
                }
            }
            const float lw = 44f, gap = 12f, st = 11f, y0 = 28f, y1 = 100f;
            float x = (W - (4 * lw + 3 * gap)) * 0.5f;
            // E
            Rect(x, y0, x + st, y1); Rect(x, y1 - st, x + lw, y1); Rect(x, (y0 + y1 - st) * 0.5f, x + lw * 0.82f, (y0 + y1 + st) * 0.5f); Rect(x, y0, x + lw, y0 + st);
            x += lw + gap;
            // X
            Line(x + st * 0.5f, y0 + st * 0.4f, x + lw - st * 0.5f, y1 - st * 0.4f, st); Line(x + st * 0.5f, y1 - st * 0.4f, x + lw - st * 0.5f, y0 + st * 0.4f, st);
            x += lw + gap;
            // I
            Rect(x + (lw - st) * 0.5f, y0, x + (lw + st) * 0.5f, y1);
            x += lw + gap;
            // T
            Rect(x, y1 - st, x + lw, y1); Rect(x + (lw - st) * 0.5f, y0, x + (lw + st) * 0.5f, y1);
            var px = new Color[v.Length];
            var white = new Color(0.86f, 0.86f, 0.84f, 1f); var red = new Color(0.75f, 0.04f, 0.03f, 1f);
            for (int i = 0; i < v.Length; i++)
            {
                if (face) { var c = Color.Lerp(white, red, Mathf.InverseLerp(0.05f, 1f, v[i])); px[i] = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), 1f); }
                else { float g = Mathf.LinearToGammaSpace(v[i]); px[i] = new Color(g, g, g, 1f); }
            }
            var tex = new Texture2D(W, Hh, TextureFormat.RGBA32, true, false) { name = face ? "ExitSignFace" : "ExitSign", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>The ceiling texture with a crack across each tile: a dark wandering line, sometimes branching.</summary>
        static Texture2D Cracked(Texture2D src, int seed)
        {
            var t = Readable(src, " (cracked)", out var px);
            int w = t.width, tile = w / 4;
            var rng = new System.Random(seed);
            for (int ty = 0; ty < 4; ty++)
            for (int tx = 0; tx < 4; tx++)
            {
                int ox = tx * tile, oy = ty * tile, inset = tile / 20;
                // From a point on one edge, heading across, wandering.
                int e = rng.Next(4);
                float a = (float)(inset + rng.NextDouble() * (tile - 2 * inset));
                Vector2 p = e == 0 ? new Vector2(inset, a) : e == 1 ? new Vector2(tile - inset, a) : e == 2 ? new Vector2(a, inset) : new Vector2(a, tile - inset);
                float head = e == 0 ? 0f : e == 1 ? Mathf.PI : e == 2 ? Mathf.PI * 0.5f : -Mathf.PI * 0.5f;
                head += ((float)rng.NextDouble() - 0.5f) * 0.9f;
                float len = tile * (0.45f + 0.6f * (float)rng.NextDouble());
                CrackLine(px, w, ox, oy, tile, inset, p, head, len, 1.3f + (float)rng.NextDouble() * 0.6f, rng, true);
            }
            return Finish(t, px);
        }

        static void CrackLine(Color32[] px, int w, int ox, int oy, int tile, int inset, Vector2 p, float head, float len, float width, System.Random rng, bool branch)
        {
            float walked = 0f;
            while (walked < len)
            {
                head += ((float)rng.NextDouble() - 0.5f) * 0.5f;
                p += new Vector2(Mathf.Cos(head), Mathf.Sin(head)) * 1.5f;
                walked += 1.5f;
                if (p.x < inset || p.y < inset || p.x > tile - inset || p.y > tile - inset) break;
                int r = Mathf.CeilToInt(width + 1f);
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int X = ox + (int)p.x + dx, Y = oy + (int)p.y + dy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float k = Mathf.Clamp01(width + 0.5f - d) * 0.55f;
                    if (k > 0.01f) Multiply(px, Y * w + X, 1f - k, 1f - k * 1.02f, 1f - k * 1.05f);
                }
                if (branch && walked > len * 0.35f && rng.NextDouble() < 0.012)
                {
                    CrackLine(px, w, ox, oy, tile, inset, p, head + (rng.NextDouble() < 0.5 ? 0.9f : -0.9f), len * 0.3f, width * 0.7f, rng, false);
                    branch = false;
                }
            }
        }

        /// <summary>A CPU copy of a texture that is not readable (through the GPU), and its pixels.</summary>
        static Texture2D Readable(Texture2D src, string suffix, out Color32[] px)
        {
            int w = src.width, h = src.height;
            var was = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = src.name + suffix, wrapMode = TextureWrapMode.Repeat, filterMode = src.filterMode,
                anisoLevel = src.anisoLevel, hideFlags = HideFlags.DontSave,
            };
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            px = t.GetPixels32();
            return t;
        }

        static Texture2D Finish(Texture2D t, Color32[] px)
        {
            t.SetPixels32(px);
            t.Apply(true, false);
            t.Compress(true);
            t.Apply(false, true);
            return t;
        }

        static Material Copy(Material src, string suffix, Texture2D baseMap)
        {
            var m = new Material(src) { name = src.name + suffix, hideFlags = HideFlags.DontSave };
            if (baseMap) m.SetTexture(BaseMap, baseMap);
            return m;
        }

        /// <summary>The ceiling texture (read back from the GPU: it is not readable) with a stain painted into each tile.</summary>
        static Texture2D Stained(Texture2D src, int seed)
        {
            var t = Readable(src, " (stained)", out var px);
            int w = t.width;
            var rng = new System.Random(seed);
            int tile = w / 4;
            bool photos = LoadStamps();
            for (int ty = 0; ty < 4; ty++)
            for (int tx = 0; tx < 4; tx++)
            {
                double kind = rng.NextDouble();
                // Most water stains from the photographed tiles (turned, flipped, scaled, placed at random), some drawn.
                if (kind < 0.8)
                {
                    if (photos && rng.NextDouble() < 0.75) PhotoStain(px, w, tx * tile, ty * tile, tile, rng);
                    else WaterStain(px, w, tx * tile, ty * tile, tile, rng);
                }
                if (kind > 0.55) Mould(px, w, tx * tile, ty * tile, tile, rng);
            }
            return Finish(t, px);
        }

        // Two stains cut from photos of stained tiles (Resources/Textures/Photo/StainCorner, StainCentre: multiply stamps,
        // white = no change, 512 px = one tile; Tools/Textures/make_surface_textures.py).
        static Color32[] s_stampCorner, s_stampCentre;
        static int s_stampW;

        static bool LoadStamps()
        {
            if (s_stampCorner != null && s_stampCentre != null) return true;
            var a = Resources.Load<Texture2D>("Textures/Photo/StainCorner");
            var b = Resources.Load<Texture2D>("Textures/Photo/StainCentre");
            if (!a || !b || a.width != b.width) return false;
            var ta = Readable(a, " (read)", out s_stampCorner);
            var tb = Readable(b, " (read)", out s_stampCentre);
            s_stampW = a.width;
            if (Application.isPlaying) { Object.Destroy(ta); Object.Destroy(tb); } else { Object.DestroyImmediate(ta); Object.DestroyImmediate(tb); }
            return true;
        }

        /// <summary>
        /// A photographed stain on one tile: the corner one stays in a corner (it ran in from the tile's edge, turned to any
        /// of the four), the centre one at 55-95 % of the tile anywhere on it; a little fainter or full strength.
        /// </summary>
        static void PhotoStain(Color32[] px, int w, int ox, int oy, int tile, System.Random rng)
        {
            bool corner = rng.NextDouble() < 0.45;
            var sp = corner ? s_stampCorner : s_stampCentre;
            int rot = rng.Next(4);
            bool flip = rng.NextDouble() < 0.5;
            float scale = corner ? 0.8f + 0.2f * (float)rng.NextDouble() : 0.55f + 0.4f * (float)rng.NextDouble();
            float strength = 0.65f + 0.35f * (float)rng.NextDouble();
            int size = Mathf.Max(8, (int)(tile * scale));
            int inset = tile / 48;
            int room = Mathf.Max(0, tile - 2 * inset - size);
            // The corner stain sits against the corner its own corner turns to; the centre one anywhere.
            int sx = corner ? ((rot == 1 || rot == 2) ^ flip ? tile - inset - size : inset) : inset + rng.Next(room + 1);
            int sy = corner ? (rot >= 2 ? tile - inset - size : inset) : inset + rng.Next(room + 1);
            sx = Mathf.Clamp(sx, 0, tile - size); sy = Mathf.Clamp(sy, 0, tile - size);
            int W = s_stampW;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                if (flip) u = 1f - u;
                for (int r = 0; r < rot; r++) { float t = u; u = v; v = 1f - t; }
                // Bilinear in the stamp.
                float fx = u * W - 0.5f, fy = v * W - 0.5f;
                int x0 = Mathf.Clamp((int)fx, 0, W - 1), y0 = Mathf.Clamp((int)fy, 0, W - 1);
                int x1 = Mathf.Min(x0 + 1, W - 1), y1 = Mathf.Min(y0 + 1, W - 1);
                float ax = Mathf.Clamp01(fx - x0), ay = Mathf.Clamp01(fy - y0);
                Color32 c00 = sp[y0 * W + x0], c10 = sp[y0 * W + x1], c01 = sp[y1 * W + x0], c11 = sp[y1 * W + x1];
                float S(byte p00, byte p10, byte p01, byte p11) =>
                    Mathf.Lerp(Mathf.Lerp(p00, p10, ax), Mathf.Lerp(p01, p11, ax), ay) / 255f;
                float mr = S(c00.r, c10.r, c01.r, c11.r), mg = S(c00.g, c10.g, c01.g, c11.g), mb = S(c00.b, c10.b, c01.b, c11.b);
                if (mr > 0.995f && mg > 0.995f && mb > 0.995f) continue;
                Multiply(px, (oy + sy + y) * w + ox + sx + x, Mathf.Lerp(1f, mr, strength), Mathf.Lerp(1f, mg, strength), Mathf.Lerp(1f, mb, strength));
            }
        }

        static void Multiply(Color32[] px, int i, float r, float g, float b)
        {
            var c = px[i];
            px[i] = new Color32((byte)Mathf.Clamp(c.r * r, 0f, 255f), (byte)Mathf.Clamp(c.g * g, 0f, 255f), (byte)Mathf.Clamp(c.b * b, 0f, 255f), c.a);
        }

        /// <summary>A dried water stain: a ragged blot, faintly tinted inside, with one to three darker brown tide lines.</summary>
        static void WaterStain(Color32[] px, int w, int ox, int oy, int tile, System.Random rng)
        {
            int inset = tile / 24;
            float R = tile * (0.12f + (float)rng.NextDouble() * 0.2f);
            float cx = ox + inset + R * 0.5f + (float)rng.NextDouble() * (tile - 2 * inset - R);
            float cy = oy + inset + R * 0.5f + (float)rng.NextDouble() * (tile - 2 * inset - R);
            float p1 = (float)rng.NextDouble() * 6.3f, p2 = (float)rng.NextDouble() * 6.3f, p3 = (float)rng.NextDouble() * 6.3f;
            float n0 = (float)rng.NextDouble() * 100f;
            int rings = 1 + rng.Next(3);
            float ringW = 0.035f + (float)rng.NextDouble() * 0.03f, dark = 0.55f + (float)rng.NextDouble() * 0.35f;
            int x0 = Mathf.Max(ox + inset, (int)(cx - R * 1.5f)), x1 = Mathf.Min(ox + tile - inset, (int)(cx + R * 1.5f));
            int y0 = Mathf.Max(oy + inset, (int)(cy - R * 1.5f)), y1 = Mathf.Min(oy + tile - inset, (int)(cy + R * 1.5f));
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float dx = x - cx, dy = y - cy;
                float ang = Mathf.Atan2(dy, dx);
                float r = R * (1f + 0.17f * Mathf.Sin(3f * ang + p1) + 0.09f * Mathf.Sin(5f * ang + p2) + 0.05f * Mathf.Sin(11f * ang + p3));
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r + (Fbm(x / 22f + n0, y / 22f) - 0.5f) * 0.42f
                                                              + (Fbm(x / 6f, y / 6f + n0) - 0.5f) * 0.12f;
                if (d > 1.15f) continue;
                // The faint tint inside, stronger toward the rim (the water dried from the middle out).
                float k = d < 1f ? (0.05f + 0.2f * d * d * d) * (0.55f + 0.45f * Fbm(x / 11f, y / 11f + n0)) : 0f;
                float uneven = 0.45f + 0.75f * Fbm(x / 15f + 40f, y / 15f + n0);           // the tide line comes and goes
                for (int j = 0; j < rings; j++)
                {
                    float rho = 1f - j * 0.19f;
                    float e = (d - rho) / (ringW * (1f + j * 0.4f));
                    k = Mathf.Max(k, Mathf.Exp(-e * e) * dark * uneven * (1f - j * 0.25f));
                }
                if (k <= 0.003f) continue;
                Multiply(px, y * w + x, 1f - k * 0.3f, 1f - k * 0.42f, 1f - k * 0.6f);
            }
        }

        /// <summary>A patch of mould: a grey haze and a cluster of dark, slightly green specks.</summary>
        static void Mould(Color32[] px, int w, int ox, int oy, int tile, System.Random rng)
        {
            int inset = tile / 24;
            float s = tile * (0.05f + (float)rng.NextDouble() * 0.1f);
            float cx = ox + inset + s * 2f + (float)rng.NextDouble() * (tile - 2 * inset - s * 4f);
            float cy = oy + inset + s * 2f + (float)rng.NextDouble() * (tile - 2 * inset - s * 4f);
            int x0 = Mathf.Max(ox + inset, (int)(cx - s * 3f)), x1 = Mathf.Min(ox + tile - inset, (int)(cx + s * 3f));
            int y0 = Mathf.Max(oy + inset, (int)(cy - s * 3f)), y1 = Mathf.Min(oy + tile - inset, (int)(cy + s * 3f));
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float dx = (x - cx) / (s * 1.6f), dy = (y - cy) / (s * 1.6f);
                float k = Mathf.Exp(-(dx * dx + dy * dy)) * 0.26f * Fbm(x / 6f, y / 6f);
                if (k > 0.003f) Multiply(px, y * w + x, 1f - k, 1f - k * 0.95f, 1f - k * 1.05f);
            }
            int specks = 150 + rng.Next(300);
            for (int i = 0; i < specks; i++)
            {
                // Gaussian spread (Box-Muller), a few strays further out.
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                double g = System.Math.Sqrt(-2.0 * System.Math.Log(u1));
                float sx = cx + (float)(g * System.Math.Cos(6.2832 * u2)) * s, sy = cy + (float)(g * System.Math.Sin(6.2832 * u2)) * s;
                float rad = 0.5f + (float)rng.NextDouble() * (rng.NextDouble() < 0.08 ? 1.8f : 0.9f);
                float a = 0.15f + (float)rng.NextDouble() * 0.3f;
                for (int y = (int)(sy - rad - 1); y <= (int)(sy + rad + 1); y++)
                for (int x = (int)(sx - rad - 1); x <= (int)(sx + rad + 1); x++)
                {
                    if (x < ox + inset || x >= ox + tile - inset || y < oy + inset || y >= oy + tile - inset) continue;
                    float d = Mathf.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
                    float k = a * Mathf.Clamp01(rad + 0.5f - d);
                    if (k > 0.003f) Multiply(px, y * w + x, 1f - k * 0.62f, 1f - k * 0.58f, 1f - k * 0.66f);
                }
            }
        }

        static float Fbm(float x, float y) => ValueNoise(x, y) * 0.6f + ValueNoise(x * 2.3f + 17f, y * 2.3f) * 0.4f;

        static float ValueNoise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash01(ix, iy), b = Hash01(ix + 1, iy), c = Hash01(ix, iy + 1), d = Hash01(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash01(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
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
        /// <summary>
        /// Old troffers: one tube out (its band dark, a third less light), old tubes gone pink and dim, dead bugs collected in
        /// the lens (dark specks along its low edge). Chosen per fixture in OfficeChunkBuilder.BuildFixtures.
        /// </summary>
        public static Material TrofferDead, TrofferPink, TrofferBugs;
        /// <summary>A lens yellowed with age: warmer and a little dimmer (its light too: LightSource.tint).</summary>
        public static Material TrofferYellow;
        /// <summary>Tubes of another white (replaced from another box): a little green, a little cooler (LightSource.tint too).</summary>
        public static Material TrofferGreen, TrofferCool;
        static Texture2D s_glow, s_prism, s_glowDead, s_glowBugs;
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap"), EmissionColor = Shader.PropertyToID("_EmissionColor"),
                            BaseMap = Shader.PropertyToID("_BaseMap");

        /// <summary>Is this one of the troffer lens copies?</summary>
        public static bool IsTroffer(Material m) => m && (m == TrofferOn || m == TrofferOff || m == TrofferDead || m == TrofferPink || m == TrofferBugs || m == TrofferYellow || m == TrofferGreen || m == TrofferCool);

        static void Troffers(OfficeBiomeAssets a)
        {
            TrofferOn = TrofferOff = TrofferYellow = TrofferGreen = TrofferCool = null;
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
            if (!s_glowDead) s_glowDead = TrofferGlow(512, out _, 0);
            if (!s_glowBugs) s_glowBugs = TrofferGlow(512, out _, -1, 17);
            TrofferDead = new Material(TrofferOn) { name = a.lensOn.name + " (one tube out)", hideFlags = HideFlags.DontSave };
            TrofferDead.SetTexture(EmissionMap, s_glowDead);
            TrofferPink = new Material(TrofferOn) { name = a.lensOn.name + " (old tubes)", hideFlags = HideFlags.DontSave };
            TrofferPink.SetColor(EmissionColor, TrofferOn.GetColor(EmissionColor) * new Color(0.84f, 0.66f, 0.72f, 1f));
            TrofferBugs = new Material(TrofferOn) { name = a.lensOn.name + " (bugs)", hideFlags = HideFlags.DontSave };
            TrofferBugs.SetTexture(EmissionMap, s_glowBugs);
            TrofferYellow = new Material(TrofferOn) { name = a.lensOn.name + " (yellowed)", hideFlags = HideFlags.DontSave };
            TrofferYellow.SetColor(EmissionColor, TrofferOn.GetColor(EmissionColor) * new Color(0.95f, 0.82f, 0.56f, 1f));
            if (TrofferYellow.HasProperty(BaseColor)) TrofferYellow.SetColor(BaseColor, TrofferOn.GetColor(BaseColor) * new Color(1f, 0.93f, 0.74f, 1f));
            TrofferGreen = new Material(TrofferOn) { name = a.lensOn.name + " (green tubes)", hideFlags = HideFlags.DontSave };
            TrofferGreen.SetColor(EmissionColor, TrofferOn.GetColor(EmissionColor) * new Color(0.9f, 1f, 0.8f, 1f));
            TrofferCool = new Material(TrofferOn) { name = a.lensOn.name + " (cool tubes)", hideFlags = HideFlags.DontSave };
            TrofferCool.SetColor(EmissionColor, TrofferOn.GetColor(EmissionColor) * new Color(0.86f, 0.95f, 1.1f, 1f));
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
        static Texture2D TrofferGlow(int size, out float mean, int deadTube = -1, int bugs = 0)
        {
            var px = new Color[size * size];
            var lin = new float[size * size];
            float max = 0f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float tube = 0f, lit = 0f;
                for (int k = 0; k < Tubes.Length; k++)
                {
                    float d = u - Tubes[k];
                    float b = Mathf.Exp(-d * d / (2f * 0.06f * 0.06f)) + 0.3f * Mathf.Exp(-d * d / (2f * 0.16f * 0.16f));
                    tube += b;
                    if (k != deadTube) lit += b;
                }
                float ends = Mathf.Lerp(0.55f, 1f, Smooth(0.02f, 0.12f, v) * Smooth(0.02f, 0.12f, 1f - v));
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float rim = Mathf.Lerp(0.5f, 1f, Smooth(0f, 0.07f, edge));
                float sparkle = 1f + 0.16f * Mathf.Clamp01(tube) * Pyramids(u, v);
                float full = (0.55f + tube * ends) * rim * sparkle;
                // Peak from all three tubes (so a dead tube's lens is dimmer, not stretched back to full).
                float fill = deadTube >= 0 ? 0.55f * 0.78f : 0.55f;
                lin[y * size + x] = (fill + lit * ends) * rim * (1f + 0.16f * Mathf.Clamp01(lit) * Pyramids(u, v));
                if (full > max) max = full;
            }
            if (bugs != 0) Bugs(lin, size, bugs);
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

        /// <summary>
        /// Dead insects in the lens: most lie along its lowest edge (where they fell and slid), a few scattered; each a dark
        /// elongated speck, 5-15 mm, against the glow.
        /// </summary>
        static void Bugs(float[] lin, int size, int seed)
        {
            var rng = new System.Random(seed);
            int edge = rng.Next(4), n = 22 + rng.Next(16);
            for (int i = 0; i < n; i++)
            {
                float along = 0.06f + 0.88f * (float)rng.NextDouble();
                float across = i < n * 0.75f ? 0.035f + 0.14f * (float)(rng.NextDouble() * rng.NextDouble()) : 0.1f + 0.8f * (float)rng.NextDouble();
                float u = edge == 0 ? across : edge == 1 ? 1f - across : along, v = edge < 2 ? along : edge == 2 ? across : 1f - across;
                float cx = u * size, cy = v * size;
                float len = 4.5f + 8f * (float)rng.NextDouble(), wid = 1.4f + 1.8f * (float)rng.NextDouble();
                float ang = (float)rng.NextDouble() * Mathf.PI, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                float dark = 0.55f + 0.4f * (float)rng.NextDouble();
                int r = Mathf.CeilToInt(len + 1f);
                for (int y = Mathf.Max(0, (int)cy - r); y < Mathf.Min(size, (int)cy + r); y++)
                for (int x = Mathf.Max(0, (int)cx - r); x < Mathf.Min(size, (int)cx + r); x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float lx = (dx * ca + dy * sa) / len, ly = (-dx * sa + dy * ca) / wid;
                    float d = lx * lx + ly * ly;
                    if (d >= 1f) continue;
                    lin[y * size + x] *= 1f - dark * Smooth(1f, 0.35f, d);
                }
            }
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
