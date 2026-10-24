using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// A small flat dark theme for the immediate-mode screens (the pause menu and build mode), scaled so the layout is the same
    /// at every resolution: positions are in "virtual pixels" of a 1080-line screen.
    /// </summary>
    public static class ImUi
    {
        public static readonly Color Back = new Color(0.07f, 0.075f, 0.08f, 1f);
        public static readonly Color Panel = new Color(0.11f, 0.115f, 0.12f, 1f);
        public static readonly Color Raised = new Color(0.17f, 0.175f, 0.185f, 1f);
        public static readonly Color Hover = new Color(0.24f, 0.245f, 0.26f, 1f);
        public static readonly Color Accent = new Color(0.93f, 0.78f, 0.28f, 1f);
        public static readonly Color Text = new Color(0.9f, 0.9f, 0.88f, 1f);
        public static readonly Color Dim = new Color(0.62f, 0.62f, 0.6f, 1f);
        public static readonly Color Bad = new Color(0.9f, 0.38f, 0.32f, 1f);
        public static readonly Color Good = new Color(0.45f, 0.78f, 0.45f, 1f);

        static GUIStyle _label, _title, _small, _button, _active, _box, _field, _toggle, _header, _card;
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
            if (_label != null && _white) return;
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
            _title = Make(Color.clear, Accent, 30, FontStyle.Bold);
            _header = Make(Raised, Accent, 14, FontStyle.Bold);
            _box = Make(Panel, Text, 16); _box.padding = new RectOffset(0, 0, 0, 0);
            _card = Make(Raised, Text, 16);
            _button = Make(Raised, Text, 16, FontStyle.Normal, TextAnchor.MiddleCenter);
            _button.hover.background = Solid(Hover); _button.active.background = Solid(Accent); _button.active.textColor = Color.black;
            _active = Make(Accent, Color.black, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
            _active.hover.background = Solid(Accent); _active.active.background = Solid(Accent);
            _field = Make(new Color(0.05f, 0.05f, 0.055f, 1f), Text, 16);
            _field.focused.background = Solid(new Color(0.08f, 0.08f, 0.09f, 1f)); _field.normal.background = Solid(new Color(0.05f, 0.05f, 0.055f, 1f));
            _field.hover.background = _field.normal.background; _field.focused.textColor = Text;
            _toggle = Make(Color.clear, Text, 16);
        }

        public static GUIStyle Label => _label;
        public static GUIStyle Small => _small;
        public static GUIStyle Title => _title;

        public static void Fill(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = old; }

        public static void PanelBox(Rect r) { Fill(r, Panel); }

        public static void Header(Rect r, string text) { GUI.Label(r, text.ToUpperInvariant(), _header); }

        public static bool Button(Rect r, string text, bool active = false, string tip = null)
        {
            bool hover = r.Contains(Pointer);
            if (hover && tip != null) Tip = tip;
            return GUI.Button(r, text, active ? _active : _button);
        }

        public static void Text_(Rect r, string text) { GUI.Label(r, text, _label); }
        public static void Dimmed(Rect r, string text) { GUI.Label(r, text, _small); }

        public static bool Toggle(Rect r, string text, bool value, string tip = null)
        {
            if (tip != null && r.Contains(Pointer)) Tip = tip;
            Fill(new Rect(r.x, r.y + r.height * 0.5f - 9, 18, 18), value ? Accent : Raised);
            if (value) Fill(new Rect(r.x + 5, r.y + r.height * 0.5f - 4, 8, 8), Color.black);
            GUI.Label(new Rect(r.x + 24, r.y, r.width - 24, r.height), text, _toggle);
            return GUI.Button(r, GUIContent.none, GUIStyle.none) ? !value : value;
        }

        public static float Slider(Rect r, string label, float value, float min, float max, string format = "0.00", string tip = null)
        {
            if (tip != null && r.Contains(Pointer)) Tip = tip;
            float lw = r.width * 0.42f;
            GUI.Label(new Rect(r.x, r.y, lw, r.height), label, _label);
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
