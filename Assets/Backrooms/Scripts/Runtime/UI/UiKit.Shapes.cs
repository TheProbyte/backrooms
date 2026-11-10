using UnityEngine;

namespace Backrooms.UI
{
    /// <summary>
    /// The kit's drawn shapes (UI pass 1, 2026-11-10), made in code once and kept: a rounded panel and its outline (both
    /// nine-sliced, for uGUI cards and IMGUI key caps), the mouse glyphs for prompts, and the four clients' pictograms
    /// (the same quads the boxes print, Generation.CardboardBoxGeometry.MarkShapes).
    /// </summary>
    public static partial class UiKit
    {
        const int RoundSize = 32, RoundRadius = 8;
        static Texture2D _fill, _ring;
        static Sprite _fillSprite, _ringSprite;
        static readonly Texture2D[] _mice = new Texture2D[3];
        static readonly Texture2D[] _marks = new Texture2D[4];
        static readonly Sprite[] _markSprites = new Sprite[4];
        static Texture2D _white;

        /// <summary>A filled rounded rectangle, white (tint it), 32 px with 8 px corners.</summary>
        public static Texture2D RoundFill => _fill ? _fill : (_fill = MakeRound(false));
        /// <summary>The same rounded rectangle's 2 px outline.</summary>
        public static Texture2D RoundRing => _ring ? _ring : (_ring = MakeRound(true));
        public static Sprite RoundFillSprite => _fillSprite ? _fillSprite : (_fillSprite = Sliced(RoundFill));
        public static Sprite RoundRingSprite => _ringSprite ? _ringSprite : (_ringSprite = Sliced(RoundRing));
        public static Texture2D White
        {
            get
            {
                if (_white) return _white;
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixel(0, 0, Color.white); _white.Apply();
                return _white;
            }
        }

        static Sprite Sliced(Texture2D t) =>
            Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(RoundRadius, RoundRadius, RoundRadius, RoundRadius));

        static Texture2D NewTex(int w, int h, string name) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

        /// <summary>Signed distance from p to a rounded box (centre c, half size h, corner radius r): negative inside.</summary>
        static float RoundBox(Vector2 p, Vector2 c, Vector2 h, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x) - (h.x - r), Mathf.Abs(p.y - c.y) - (h.y - r));
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        static Texture2D MakeRound(bool ring)
        {
            var t = NewTex(RoundSize, RoundSize, ring ? "KitRing" : "KitFill");
            var px = new Color32[RoundSize * RoundSize];
            var c = new Vector2(RoundSize * 0.5f, RoundSize * 0.5f);
            var h = c - new Vector2(0.5f, 0.5f);
            for (int y = 0; y < RoundSize; y++)
                for (int x = 0; x < RoundSize; x++)
                {
                    float d = RoundBox(new Vector2(x + 0.5f, y + 0.5f), c, h, RoundRadius);
                    float a = Mathf.Clamp01(0.5f - d);
                    if (ring) a *= Mathf.Clamp01(d + 2.5f);          // 2 px wide
                    px[y * RoundSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        /// <summary>A mouse, outlined, white: 0 just the mouse (move it), 1 its left button filled (click), 2 its right.</summary>
        public static Texture2D Mouse(int button)
        {
            button = Mathf.Clamp(button, 0, 2);
            if (_mice[button]) return _mice[button];
            const int W = 36, H = 48;
            var t = NewTex(W, H, "KitMouse" + button);
            var px = new Color32[W * H];
            var c = new Vector2(W * 0.5f, H * 0.5f);
            var half = new Vector2(W * 0.5f - 3f, H * 0.5f - 2f);
            float split = H * 0.6f;                                   // the buttons' line (y up)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = RoundBox(p, c, half, 13f);
                    float inside = Mathf.Clamp01(0.5f - d);
                    float a = inside * Mathf.Clamp01(d + 3.5f);       // the body's outline, 3 px
                    if (inside > 0f)
                    {
                        if (p.y > split) a = Mathf.Max(a, inside * Mathf.Clamp01(1.5f - Mathf.Abs(p.x - c.x)));   // between the buttons
                        a = Mathf.Max(a, inside * Mathf.Clamp01(1.5f - Mathf.Abs(p.y - split)));                    // under them
                        bool left = p.x < c.x - 1.5f, right = p.x > c.x + 1.5f;
                        if (p.y > split + 1.5f && ((button == 1 && left) || (button == 2 && right))) a = inside;     // the button pressed
                    }
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            t.SetPixels32(px); t.Apply();
            return _mice[button] = t;
        }

        /// <summary>A client's pictogram (0 TALLMORE .. 3 BELLWEATHER), white on clear, 64 px: tint it with the client's colour.</summary>
        public static Texture2D Mark(int client)
        {
            client = Mathf.Clamp(client, 0, 3);
            if (_marks[client]) return _marks[client];
            const int S = 64, SS = 4;
            var t = NewTex(S, S, "KitMark" + client);
            var shapes = Generation.CardboardBoxGeometry.MarkShapes[client];
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            // -1..1 with a pixel's margin all round.
                            float u = ((x + (sx + 0.5f) / SS) / S * 2f - 1f) * 1.06f, v = ((y + (sy + 0.5f) / SS) / S * 2f - 1f) * 1.06f;
                            foreach (var q in shapes) if (InQuad(u, v, q)) { hits++; break; }
                        }
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
                }
            t.SetPixels32(px); t.Apply();
            return _marks[client] = t;
        }

        public static Sprite MarkSprite(int client)
        {
            client = Mathf.Clamp(client, 0, 3);
            if (_markSprites[client]) return _markSprites[client];
            var t = Mark(client);
            return _markSprites[client] = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Inside a convex quad (either winding).</summary>
        static bool InQuad(float x, float y, float[] q)
        {
            int sign = 0;
            for (int i = 0; i < 4; i++)
            {
                float ax = q[i * 2], ay = q[i * 2 + 1], bx = q[(i + 1) % 4 * 2], by = q[(i + 1) % 4 * 2 + 1];
                float cross = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
                int s = cross > 0 ? 1 : cross < 0 ? -1 : 0;
                if (s == 0) continue;
                if (sign == 0) sign = s; else if (s != sign) return false;
            }
            return true;
        }

        // ---- IMGUI: nine-sliced drawing (GUI.DrawTexture stretches; a style with a border slices) ----

        static GUIStyle _sliceFill, _sliceRing;

        /// <summary>A rounded panel in IMGUI, filled (or outlined), in a colour.</summary>
        public static void GuiRound(Rect r, Color c, bool ring = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_sliceFill == null || !_sliceFill.normal.background)
            {
                _sliceFill = new GUIStyle { border = new RectOffset(RoundRadius, RoundRadius, RoundRadius, RoundRadius) };
                _sliceFill.normal.background = RoundFill;
                _sliceRing = new GUIStyle { border = new RectOffset(RoundRadius, RoundRadius, RoundRadius, RoundRadius) };
                _sliceRing.normal.background = RoundRing;
            }
            var old = GUI.color; GUI.color = c;
            (ring ? _sliceRing : _sliceFill).Draw(r, false, false, false, false);
            GUI.color = old;
        }
    }
}
