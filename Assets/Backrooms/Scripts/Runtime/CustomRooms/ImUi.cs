using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// A small flat theme (dark grey, or the company's colours in the Overflow art style) for the immediate-mode screens (the pause menu and build mode), scaled so the layout is the same
    /// at every resolution: positions are in "virtual pixels" of a 1080-line screen.
    /// </summary>
    public static class ImUi
    {
        // Two palettes: the camcorder's dark grey, and in the Overflow art style the company's (2026-11-07, as the main
        // menu's .mm-ov in MainMenu.uss: chocolate panels, the amber accent, cream type). The styles are rebuilt when the
        // ART STYLE setting changes.
        static bool Ov => Backrooms.Generation.ArtStyle.Current == Backrooms.Generation.ArtStyle.Look.Overflow;
        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
        /// <summary>The full-screen backdrop of the builder's library.</summary>
        public static Color Deep => Ov ? C(20, 13, 9) : new Color(0.045f, 0.047f, 0.05f, 1f);
        public static Color Back => Ov ? C(38, 26, 18) : new Color(0.07f, 0.075f, 0.08f, 1f);
        public static Color Panel => Ov ? C(59, 42, 30) : new Color(0.11f, 0.115f, 0.12f, 1f);
        public static Color Raised => Ov ? C(84, 61, 43) : new Color(0.17f, 0.175f, 0.185f, 1f);
        public static Color Hover => Ov ? C(110, 80, 55) : new Color(0.24f, 0.245f, 0.26f, 1f);
        public static Color Accent => Ov ? C(240, 162, 30) : new Color(0.93f, 0.78f, 0.28f, 1f);
        /// <summary>Type and marks drawn on the accent (black, or the company's chocolate).</summary>
        public static Color OnAccent => Ov ? C(59, 42, 30) : Color.black;
        public static Color Text => Ov ? C(243, 230, 200) : new Color(0.9f, 0.9f, 0.88f, 1f);
        public static Color Dim => Ov ? C(205, 180, 138) : new Color(0.62f, 0.62f, 0.6f, 1f);
        public static Color Bad => Ov ? C(226, 92, 52) : new Color(0.9f, 0.38f, 0.32f, 1f);
        public static Color Good => Ov ? C(168, 184, 72) : new Color(0.45f, 0.78f, 0.45f, 1f);
        static Color FieldBack => Ov ? C(44, 31, 21) : new Color(0.05f, 0.05f, 0.055f, 1f);
        static Color FieldFocus => Ov ? C(66, 47, 32) : new Color(0.08f, 0.08f, 0.09f, 1f);
        static bool _builtOv;

        static GUIStyle _label, _title, _small, _button, _active, _box, _field, _toggle, _header, _card, _tooltip, _smallWrap;
        static Texture2D _white;
        static readonly Dictionary<Color, Texture2D> Tex = new Dictionary<Color, Texture2D>();

        public static float Scale => Screen.height / 1080f;
        public static float Width => Screen.width / Scale;
        public static float Height => 1080f;

        /// <summary>Call first in OnGUI: scales everything to the 1080-line layout.</summary>
        public static void Begin()
        {
            Init();
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
        }

        public static void End() { GUI.matrix = Matrix4x4.identity; }

        /// <summary>Pointer position in virtual pixels (y down).</summary>
        public static Vector2 Pointer
        {
            get
            {
                var m = Mouse.current;
                if (m == null) return new Vector2(-1000, -1000);
                var p = m.position.ReadValue();
                return new Vector2(p.x / Scale, (Screen.height - p.y) / Scale);
            }
        }

        public static Texture2D Solid(Color c)
        {
            if (!Tex.TryGetValue(c, out var t) || !t)
            {
                t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                t.SetPixel(0, 0, c); t.Apply();
                Tex[c] = t;
            }
            return t;
        }

        static void Init()
        {
            if (_label != null && _white && _builtOv == Ov) return;
            _builtOv = Ov;
            _white = Solid(Color.white);
            GUIStyle Make(Color bg, Color fg, int size, FontStyle fs = FontStyle.Normal, TextAnchor a = TextAnchor.MiddleLeft)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a, wordWrap = false, richText = false, clipping = TextClipping.Clip };
                s.normal.textColor = fg; s.normal.background = bg.a > 0 ? Solid(bg) : null;
                s.hover.textColor = fg; s.active.textColor = fg; s.focused.textColor = fg;
                s.padding = new RectOffset(8, 8, 2, 2);
                return s;
            }
            _label = Make(Color.clear, Text, 16);
            _small = Make(Color.clear, Dim, 13);
            _smallWrap = new GUIStyle(_small) { wordWrap = true };
            _title = Make(Color.clear, Accent, 30, FontStyle.Bold);
            _header = Make(Raised, Accent, 14, FontStyle.Bold);
            _box = Make(Panel, Text, 16); _box.padding = new RectOffset(0, 0, 0, 0);
            _card = Make(Raised, Text, 16);
            _button = Make(Raised, Text, 16, FontStyle.Normal, TextAnchor.MiddleCenter);
            _button.hover.background = Solid(Hover); _button.active.background = Solid(Accent); _button.active.textColor = OnAccent;
            _active = Make(Accent, OnAccent, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
            _active.hover.background = Solid(Accent); _active.active.background = Solid(Accent);
            _field = Make(FieldBack, Text, 16);
            _field.focused.background = Solid(FieldFocus); _field.normal.background = Solid(FieldBack);
            _field.hover.background = _field.normal.background; _field.focused.textColor = Text;
            _toggle = Make(Color.clear, Text, 16);
            _tooltip = Make(Color.clear, Text, 15); _tooltip.wordWrap = true;
        }

        public static GUIStyle Label => _label;
        public static GUIStyle Small => _small;
        public static GUIStyle Title => _title;

        public static void Fill(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = old; }

        public static void PanelBox(Rect r) { Fill(r, Panel); }

        /// <summary>A panel; in the Overflow art style with the company's amber frame.</summary>
        public static void FramedBox(Rect r)
        {
            if (!Ov) { PanelBox(r); return; }
            Fill(r, Accent);
            Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), Panel);
        }

        /// <summary>The company's orange, gold and avocado stripe under a title (the Overflow art style only, as the main menu's).</summary>
        public static void Stripe(Rect r)
        {
            if (!Ov) return;
            float h = r.height / 3f;
            Fill(new Rect(r.x, r.y, r.width, h), C(217, 102, 31));
            Fill(new Rect(r.x, r.y + h, r.width, h), C(232, 178, 58));
            Fill(new Rect(r.x, r.y + 2 * h, r.width, h), C(124, 138, 46));
        }

        public static void Header(Rect r, string text) { GUI.Label(r, text.ToUpperInvariant(), _header); }

        public static bool Button(Rect r, string text, bool active = false, string tip = null)
        {
            bool hover = r.Contains(Pointer);
            if (hover && tip != null) Tip = tip;
            return GUI.Button(r, new GUIContent(text, tip ?? ""), active ? _active : _button);
        }

        public static bool ToolButton(Rect r, string text, int icon, bool active, string tip)
        {
            bool clicked = GUI.Button(r, new GUIContent("", tip), active ? _active : _button);
            var colour = active ? OnAccent : Text;
            ToolIcon(new Rect(r.x + 8, r.y + 7, 22, r.height - 14), icon, colour);
            GUI.Label(new Rect(r.x + 36, r.y, r.width - 62, r.height), text, active ? _active : _label);
            return clicked;
        }

        static void IconLine(Rect r, float ax, float ay, float bx, float by, Color colour)
        {
            var a = new Vector2(r.x + ax * r.width, r.y + ay * r.height);
            var b = new Vector2(r.x + bx * r.width, r.y + by * r.height);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - 1, Vector2.Distance(a, b), 2), colour);
            GUI.matrix = matrix;
        }

        static void ToolIcon(Rect r, int icon, Color c)
        {
            void L(float ax, float ay, float bx, float by) => IconLine(r, ax, ay, bx, by, c);
            void Box(float x, float y, float w, float h) { L(x,y,x+w,y); L(x+w,y,x+w,y+h); L(x+w,y+h,x,y+h); L(x,y+h,x,y); }
            switch (icon)
            {
                case 0: L(0.15f,0,0.15f,1); L(0.15f,0,0.9f,0.65f); L(0.15f,1,0.45f,0.65f); L(0.45f,0.65f,0.9f,0.65f); break;
                case 1: Box(0,0.05f,1,0.9f); L(0,0.5f,1,0.5f); L(0.5f,0.05f,0.5f,0.5f); L(0.25f,0.5f,0.25f,0.95f); L(0.75f,0.5f,0.75f,0.95f); break;
                case 2: L(0.1f,1,0.1f,0); L(0.1f,0,0.9f,0); L(0.9f,0,0.9f,1); L(0,1,0.3f,1); L(0.7f,1,1,1); break;
                case 3: Box(0.05f,0.5f,0.9f,0.45f); L(0,0.35f,1,0.35f); break;
                case 4: L(0.1f,0.7f,0.6f,0); L(0.6f,0,1,0.3f); L(1,0.3f,0.5f,1); L(0.5f,1,0.1f,0.7f); L(0.35f,0.35f,0.75f,0.65f); break;
                case 5: Box(0.05f,0.05f,0.9f,0.9f); L(0.5f,0.05f,0.5f,0.95f); L(0.05f,0.5f,0.95f,0.5f); break;
                case 6: Box(0.2f,0.05f,0.6f,0.9f); L(0,1,1,1); L(0,0,1,0); break;
                case 7: Box(0.3f,0.25f,0.4f,0.45f); L(0.3f,0.85f,0.7f,0.85f); L(0.5f,0,0.5f,0.12f); L(0,0.4f,0.15f,0.4f); L(0.85f,0.4f,1,0.4f); break;
                case 8: Box(0.15f,0.15f,0.7f,0.55f); Box(0,0.5f,1,0.35f); L(0.15f,0.85f,0.15f,1); L(0.85f,0.85f,0.85f,1); break;
                case 9: L(0.5f,0,1,0.5f); L(1,0.5f,0.5f,1); L(0.5f,1,0,0.5f); L(0,0.5f,0.5f,0); L(0.5f,0.25f,0.5f,0.75f); L(0.25f,0.5f,0.75f,0.5f); break;
            }
        }

        /// <summary>Draw after scroll views so native GUI tooltips use the correct clipped control coordinates.</summary>
        public static void DrawTooltip()
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(GUI.tooltip)) return;
            Tip = GUI.tooltip;
            float width = Mathf.Min(420f, Width - 24f);
            float height = _tooltip.CalcHeight(new GUIContent(Tip), width - 16f) + 12f;
            var p = Pointer;
            var r = new Rect(Mathf.Clamp(p.x + 18f, 12f, Width - width - 12f), Mathf.Clamp(p.y + 24f, 12f, Height - height - 12f), width, height);
            Fill(r, Accent); PanelBox(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2));
            GUI.Label(new Rect(r.x + 6, r.y + 5, r.width - 12, r.height - 10), Tip, _tooltip);
        }

        public static void Text_(Rect r, string text) { GUI.Label(r, text, _label); }
        public static void Dimmed(Rect r, string text) { GUI.Label(r, text, _smallWrap); }

        public static bool Toggle(Rect r, string text, bool value, string tip = null)
        {
            if (tip != null && r.Contains(Pointer)) Tip = tip;
            Fill(new Rect(r.x, r.y + r.height * 0.5f - 9, 18, 18), value ? Accent : Raised);
            if (value) Fill(new Rect(r.x + 5, r.y + r.height * 0.5f - 4, 8, 8), OnAccent);
            GUI.Label(new Rect(r.x + 24, r.y, r.width - 24, r.height), new GUIContent(text, tip ?? ""), _toggle);
            return GUI.Button(r, GUIContent.none, GUIStyle.none) ? !value : value;
        }

        public static float Slider(Rect r, string label, float value, float min, float max, string format = "0.00", string tip = null)
        {
            if (tip != null && r.Contains(Pointer)) Tip = tip;
            float lw = r.width * 0.42f;
            GUI.Label(new Rect(r.x, r.y, lw, r.height), new GUIContent(label, tip ?? ""), _label);
            var track = new Rect(r.x + lw, r.y + r.height * 0.5f - 3, r.width - lw - 56, 6);
            Fill(track, Raised);
            float t = Mathf.InverseLerp(min, max, value);
            Fill(new Rect(track.x, track.y, track.width * t, track.height), Accent);
            var hit = new Rect(track.x - 6, r.y, track.width + 12, r.height);
            value = GUI.HorizontalSlider(hit, value, min, max, GUIStyle.none, GUIStyle.none);
            Fill(new Rect(track.x + track.width * Mathf.InverseLerp(min, max, value) - 5, track.y - 5, 10, 16), Text);
            GUI.Label(new Rect(r.xMax - 54, r.y, 54, r.height), value.ToString(format), _small);
            return value;
        }

        /// <summary>Text field; its control name starts with "tf_" so screens can tell when typing owns the keyboard.</summary>
        public static string TextField(Rect r, string value, string id, int maxLength = 60)
        {
            GUI.SetNextControlName("tf_" + id);
            return GUI.TextField(r, value ?? "", maxLength, _field);
        }

        public static string TextArea(Rect r, string value, string id, int maxLength = 400)
        {
            var s = new GUIStyle(_field) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            GUI.SetNextControlName("tf_" + id);
            return GUI.TextArea(r, value ?? "", maxLength, s);
        }

        // ---- tooltip shown by the screens at the bottom ----
        public static string Tip = "";

        /// <summary>A simple vertical layout helper: hands out rows of a column.</summary>
        public struct Column
        {
            public float x, y, w, rowH, gap;
            public Column(float x, float y, float w, float rowH = 30f, float gap = 6f) { this.x = x; this.y = y; this.w = w; this.rowH = rowH; this.gap = gap; }
            public Rect Row(float h = 0f) { var r = new Rect(x, y, w, h > 0 ? h : rowH); y += r.height + gap; return r; }
            public Rect Cols(int n, int i, float h = 0f, float spacing = 6f)
            {
                float cw = (w - spacing * (n - 1)) / n;
                return new Rect(x + i * (cw + spacing), y, cw, h > 0 ? h : rowH);
            }
            public void Advance(float h = 0f) { y += (h > 0 ? h : rowH) + gap; }
            public void Space(float h) { y += h; }
        }
    }
}
