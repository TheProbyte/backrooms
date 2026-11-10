using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Backrooms.UI
{
    /// <summary>
    /// Prompts (Docs/UiPlan.md U5, 2026-11-10): what the hands would do, drawn as a key cap (or the mouse, its button
    /// filled) and a verb, "[G] HOLD  PACK IT BACK INTO ITS CRATE", up to four side by side, always in the same place over
    /// the bottom of the picture. The game's hints are still written as one line ("Q DROP · E SET DOWN · 1-4 POCKET",
    /// "CLICK TO PICK UP CHALK", "HOLD G TO PACK IT BACK"); <see cref="Parse"/> reads them into prompts, so every hint in
    /// the game goes through here without each being rewritten.
    /// <para>Key caps are labelled from one table (<see cref="KeyLabel"/>): when keys can be rebound, or a controller is
    /// added, that table is the one place to change (decided 2026-11-10: no controller yet).</para>
    /// </summary>
    public static class Prompts
    {
        public struct Prompt
        {
            /// <summary>-1: a key (see <see cref="key"/>); 0: the mouse moved; 1: its left button; 2: its right.</summary>
            public int mouse;
            public string key;
            /// <summary>"HOLD", "LET GO", or null.</summary>
            public string tag;
            public string verb;
            /// <summary>No key at all: a note ("TOO HEAVY TO TAKE MORE").</summary>
            public bool plain;
        }

        static readonly Regex KeyStart = new Regex(@"^(?<k>[A-Z0-9](?:/[A-Z0-9])*|1-4|TAB|ESC|SPACE|SHIFT|CTRL|ENTER|F\d{1,2})\s+(?<rest>\S.*)$");
        static readonly Dictionary<string, List<Prompt>> Cache = new Dictionary<string, List<Prompt>>();

        /// <summary>A key's label on its cap (the one table of key names).</summary>
        public static string KeyLabel(string token)
        {
            switch (token)
            {
                case "W/A/S/D": return "WASD";
                case "CTRL": return "CTRL";
                case "SPACE": return "SPACE";
                case "ESC": return "ESC";
                default: return token;
            }
        }

        /// <summary>A hint line as prompts (cached: hints repeat every frame).</summary>
        public static List<Prompt> Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            if (Cache.TryGetValue(line, out var list)) return list;
            list = new List<Prompt>();
            bool lastMouse = false;
            foreach (var raw in line.Split('·'))
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                var p = new Prompt { mouse = -1 };
                string rest;
                if (s.StartsWith("RIGHT CLICK")) { p.mouse = 2; rest = s.Substring(11); }
                else if (s.StartsWith("CLICK")) { p.mouse = 1; rest = s.Substring(5); }
                else if (s.StartsWith("LET GO")) { p.mouse = 1; p.tag = "LET GO"; rest = s.Substring(6); }
                else if (s.StartsWith("HOLD "))
                {
                    p.tag = "HOLD";
                    var m = KeyStart.Match(s.Substring(5));
                    if (m.Success && !s.Substring(5).StartsWith("ON ") && !s.Substring(5).StartsWith("TO ")) { p.key = m.Groups["k"].Value; rest = m.Groups["rest"].Value; }
                    else { p.mouse = 1; rest = s.Substring(5); }
                }
                else if (s.StartsWith("MOUSE")) { p.mouse = 0; rest = s.Substring(5); }
                else if (lastMouse && (s.StartsWith("DOWN ") || s.StartsWith("UP "))) { p.mouse = 0; rest = s; }
                else
                {
                    var m = KeyStart.Match(s);
                    if (m.Success) { p.key = m.Groups["k"].Value; rest = m.Groups["rest"].Value; }
                    else { p.plain = true; rest = s; }
                }
                rest = rest.Trim().TrimStart(':').Trim();
                if (rest.StartsWith("TO ")) rest = rest.Substring(3);
                p.verb = rest;
                lastMouse = p.mouse >= 0;
                list.Add(p);
            }
            if (Cache.Count > 400) Cache.Clear();
            Cache[line] = list;
            return list;
        }

        static GUIStyle _verb, _cap, _tag;

        /// <summary>Draws a hint line as prompts, centred on <paramref name="centreX"/> with its middle at <paramref name="y"/>
        /// (screen pixels). <paramref name="note"/>: a short note in the warning colour (something did not work).</summary>
        public static void Draw(string line, float centreX, float y, float scale, float alpha, bool note)
        {
            var list = Parse(line);
            if (list == null || list.Count == 0) return;
            if (_verb == null)
            {
                _verb = new GUIStyle(GUI.skin.label) { font = UiKit.Font(UiKit.Face.Paper, true), alignment = TextAnchor.MiddleLeft, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
                _cap = new GUIStyle(_verb) { alignment = TextAnchor.MiddleCenter };
                _tag = new GUIStyle(GUI.skin.label) { font = UiKit.Font(UiKit.Face.Paper), alignment = TextAnchor.MiddleLeft, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) };
            }
            // Sizes from the kit (a 1080-line screen's pixels), shrunk as a whole if the row would not fit.
            float s = scale;
            for (int pass = 0; pass < 2; pass++)
            {
                _verb.fontSize = Mathf.Max(9, Mathf.RoundToInt(20f * s));
                _cap.fontSize = Mathf.Max(9, Mathf.RoundToInt(17f * s));
                _tag.fontSize = Mathf.Max(9, Mathf.RoundToInt(17f * s));
                float total = Width(list, s);
                float max = Screen.width * 0.86f;
                if (total <= max || pass == 1) break;
                s *= max / total;
            }
            float x = centreX - Width(list, s) * 0.5f;
            float h = 32f * s;
            var old = GUI.color;
            foreach (var p in list)
            {
                if (p.plain)
                {
                    x = Text(x, y, p.verb, _verb, note ? UiKit.Gold : UiKit.Cream, alpha, s);
                }
                else
                {
                    if (p.mouse >= 0)
                    {
                        var t = UiKit.Mouse(p.mouse);
                        float mh = h * 1.05f, mw = mh * t.width / t.height;
                        var r = new Rect(x, y - mh * 0.5f, mw, mh);
                        GUI.color = new Color(0, 0, 0, 0.6f * alpha); GUI.DrawTexture(new Rect(r.x + 1.5f * s, r.y + 1.5f * s, r.width, r.height), t);
                        GUI.color = new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, alpha); GUI.DrawTexture(r, t);
                        x += mw + 8f * s;
                    }
                    else
                    {
                        string label = KeyLabel(p.key);
                        float w = Mathf.Max(h, _cap.CalcSize(new GUIContent(label)).x + 16f * s);
                        var r = new Rect(x, y - h * 0.5f, w, h);
                        UiKit.GuiRound(r, new Color(UiKit.Ground.r, UiKit.Ground.g, UiKit.Ground.b, 0.6f * alpha));
                        UiKit.GuiRound(r, new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, alpha), true);
                        GUI.color = new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, alpha);
                        GUI.Label(new Rect(r.x, r.y + 1f * s, r.width, r.height), label, _cap);
                        x += w + 8f * s;
                    }
                    if (!string.IsNullOrEmpty(p.tag)) x = Text(x, y, p.tag, _tag, UiKit.Gold, alpha, s) + 6f * s;
                    x = Text(x, y, p.verb, _verb, UiKit.Cream, alpha, s);
                }
                x += 28f * s;
            }
            GUI.color = old;
        }

        static float Text(float x, float y, string text, GUIStyle st, Color c, float alpha, float s)
        {
            if (string.IsNullOrEmpty(text)) return x;
            var size = st.CalcSize(new GUIContent(text));
            var r = new Rect(x, y - size.y * 0.5f, size.x + 2f, size.y);
            GUI.color = new Color(0, 0, 0, 0.75f * alpha);
            GUI.Label(new Rect(r.x + 1.5f * s, r.y + 1.5f * s, r.width, r.height), text, st);
            GUI.color = new Color(c.r, c.g, c.b, alpha);
            GUI.Label(r, text, st);
            return x + size.x;
        }

        static float Width(List<Prompt> list, float s)
        {
            float w = 0f, h = 32f * s;
            foreach (var p in list)
            {
                if (!p.plain)
                {
                    if (p.mouse >= 0) w += h * 1.05f * 36f / 48f + 8f * s;
                    else w += Mathf.Max(h, _cap.CalcSize(new GUIContent(KeyLabel(p.key))).x + 16f * s) + 8f * s;
                    if (!string.IsNullOrEmpty(p.tag)) w += _tag.CalcSize(new GUIContent(p.tag)).x + 6f * s;
                }
                if (!string.IsNullOrEmpty(p.verb)) w += _verb.CalcSize(new GUIContent(p.verb)).x;
                w += 28f * s;
            }
            return Mathf.Max(0f, w - 28f * s);
        }
    }
}
