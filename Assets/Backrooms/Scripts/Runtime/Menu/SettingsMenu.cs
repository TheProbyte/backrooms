using System;
using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Camcorder;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Backrooms.Menu
{
    /// <summary>
    /// The settings page (2026-11-04, the owner: "create a settings page"; laid out again 2026-11-10, Docs/UiPlan.md U10),
    /// for whoever plays: from the main menu's SETTINGS and from the pause menu. The pages are tabs along the top (Q and E,
    /// Tab, or a click); each line is a label and its value in two aligned columns, a bar under a value that is an amount;
    /// what the chosen line does is said in one place under the list.
    /// <list type="bullet">
    /// <item>SOUND: master, the building, music (tapes), voices, your microphone, how you talk.</item>
    /// <item>CONTROLS: mouse sensitivity, invert Y, crouch and crawl held or pressed.</item>
    /// <item>COMFORT: camera shake and motion blur as shares (Xbox accessibility guideline 117), brightness, REDUCE MOTION.</item>
    /// <item>ACCESSIBILITY: TEXT SIZE (every menu, panel and prompt), FLASHING, SUBTITLES.</item>
    /// <item>GRAPHICS: quality, resolution scale, frame rate cap, vsync; the camcorder's own menu (advanced).</item>
    /// <item>SCREEN: the window and its size.</item>
    /// <item>KEYS: what every key does; DEVELOPER TOOLS where they exist.</item>
    /// </list>
    /// Mouse, or arrows / W S to choose, ◄ ► (A D) to change, Enter, Escape back. Everything is saved as it changes.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public class SettingsMenu : MonoBehaviour
    {
        static SettingsMenu s_instance;
        public static bool IsOpen => s_instance && s_instance._open;
        /// <summary>The frame it closed (Escape that closed it is not also the pause menu's).</summary>
        public static int ClosedFrame { get; private set; } = -10;

        enum Page { Sound, Controls, Comfort, Access, Graphics, Screen, Keys }
        static readonly string[] PageNames = { "SOUND", "CONTROLS", "COMFORT", "ACCESSIBILITY", "GRAPHICS", "SCREEN", "KEYS" };
        Page _page;
        bool _open;
        Action _onClose, _onAdvanced;
        UIDocument _doc;
        VisualElement _root, _panel, _list, _tabs;
        Label _desc;
        readonly List<(VisualElement el, Action act, string desc)> _items = new List<(VisualElement, Action, string)>();
        readonly Dictionary<int, Action<int>> _steps = new Dictionary<int, Action<int>>();
        readonly List<Label> _tabLabels = new List<Label>();
        int _sel;
        AudioSource _beep;
        int _openedFrame;

        /// <summary>Opens the page; onClose when it is left; onAdvanced (the main menu's F5 tabs) offered if given.</summary>
        public static void Open(Action onClose, Action onAdvanced = null)
        {
            if (!s_instance)
            {
                var go = new GameObject("SettingsMenu");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<SettingsMenu>();
            }
            s_instance.Show(onClose, onAdvanced);
        }

        /// <summary>For the picture run: which page shows (0 SOUND .. 6 KEYS).</summary>
        public static void ShowPageForShots(int page) { if (s_instance && s_instance._open) s_instance.ShowPage((Page)Mathf.Clamp(page, 0, PageNames.Length - 1)); }
        public static string PageName(int page) => PageNames[Mathf.Clamp(page, 0, PageNames.Length - 1)];
        public static int PageCount => PageNames.Length;
        /// <summary>For the picture run: closes it as Escape would.</summary>
        public static void CloseForShots() { if (s_instance) s_instance.Close(); }

        void Awake()
        {
            _beep = gameObject.AddComponent<AudioSource>();
            _beep.playOnAwake = false; _beep.spatialBlend = 0f; _beep.volume = 0.45f; _beep.ignoreListenerPause = true;
        }

        void Show(Action onClose, Action onAdvanced)
        {
            _onClose = onClose; _onAdvanced = onAdvanced;
            if (_doc == null) Build();
            _open = true;
            _openedFrame = Time.frameCount;
            _panel.style.display = DisplayStyle.Flex;
            ShowPage(Page.Sound);
        }

        void Close()
        {
            if (!_open) return;
            _open = false;
            ClosedFrame = Time.frameCount;
            _panel.style.display = DisplayStyle.None;
            Beep(2);
            var cb = _onClose; _onClose = null;
            cb?.Invoke();
        }

        void Build()
        {
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = Backrooms.UI.UiKit.Panel;
            _doc.sortingOrder = 20;
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/MainMenu");
            if (sheet) _root.styleSheets.Add(sheet);
            _root.AddToClassList("mm-root");
            _root.pickingMode = PickingMode.Ignore;
            MainMenu.ApplyMenuFont(_root);
            _panel = new VisualElement();
            _panel.AddToClassList("mm-panel");
            _panel.AddToClassList("st-panel");
            _root.Add(_panel);
            var header = new VisualElement(); header.AddToClassList("mm-header");
            header.Add(Text("SETTINGS", "mm-title"));
            _panel.Add(header);
            var rule = new VisualElement(); rule.AddToClassList("mm-rule"); _panel.Add(rule);
            MainMenu.AddStripe(_panel);
            _tabs = new VisualElement(); _tabs.AddToClassList("st-tabs"); _panel.Add(_tabs);
            for (int i = 0; i < PageNames.Length; i++)
            {
                int p = i;
                var t = Text(PageNames[i], "st-tab");
                t.pickingMode = PickingMode.Position;
                t.RegisterCallback<ClickEvent>(_ => { if (_page != (Page)p) Go((Page)p); });
                _tabs.Add(t); _tabLabels.Add(t);
            }
            _list = new VisualElement(); _list.AddToClassList("mm-list"); _list.AddToClassList("st-list"); _panel.Add(_list);
            _desc = Text("", "st-desc"); _panel.Add(_desc);
            _panel.Add(Text("▲▼ CHOOSE    ◄ ► CHANGE    Q E PAGE    ENTER OK    ESC BACK", "mm-hint"));
        }

        void Update()
        {
            if (!_open) return;
            CamcorderPlayerController.InputBlocked = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // Inside the picture, not across the bar's edge.
            _panel.style.left = new Length((Backrooms.UI.UiKit.BarShare + 0.03f) * 100f, LengthUnit.Percent);
            var kb = Keyboard.current;
            if (kb == null || Time.frameCount == _openedFrame) return;
            bool prevPage = kb.qKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && kb.shiftKey.isPressed);
            bool nextPage = kb.eKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && !kb.shiftKey.isPressed);
            if (prevPage || nextPage) { Go((Page)(((int)_page + (nextPage ? 1 : -1) + PageNames.Length) % PageNames.Length)); return; }
            if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) { Close(); return; }
            if (_items.Count == 0) return;
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame, right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            if ((left || right) && _steps.TryGetValue(_sel, out var step)) { step(right ? 1 : -1); Beep(0); return; }
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Select((_sel + _items.Count - 1) % _items.Count, true);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Select((_sel + 1) % _items.Count, true);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Activate(_sel);
        }

        // ---------------- Pages ----------------

        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + " %";
        static float Step(float v, int d, float by, float lo, float hi) => Mathf.Clamp(Mathf.Round((v + d * by) / by) * by, lo, hi);
        static string OnOff(bool b) => b ? "ON" : "OFF";

        void ShowPage(Page page)
        {
            _page = page;
            MainMenu.ApplyLook(_root);
            _list.Clear(); _items.Clear(); _steps.Clear();
            for (int i = 0; i < _tabLabels.Count; i++) _tabLabels[i].EnableInClassList("st-on", i == (int)page);
            switch (page)
            {
                case Page.Sound:
                    Option("MASTER", () => Pct(GameSettings.Master), d => GameSettings.Master = Step(GameSettings.Master, d, 0.1f, 0f, 1f), "EVERYTHING YOU HEAR.", () => GameSettings.Master);
                    Option("BUILDING", () => Pct(GameSettings.Ambience), d => GameSettings.Ambience = Step(GameSettings.Ambience, d, 0.1f, 0f, 1f),
                           "THE BUILDING ITSELF: THE LIGHTS' HUM, THE VENTILATION, THE ROOMS' TONE.", () => GameSettings.Ambience);
                    Option("MUSIC (TAPES)", () => Pct(GameSettings.Music), d => GameSettings.Music = Step(GameSettings.Music, d, 0.1f, 0f, 1f), "WHAT A TAPE PLAYS.", () => GameSettings.Music);
                    Option("VOICES (CO-OP)", () => Pct(GameSettings.Voices), d => GameSettings.Voices = Step(GameSettings.Voices, d, 0.1f, 0f, 1f),
                           "YOUR FRIENDS' VOICES. THEY ARE HEARD WHERE THEY STAND, ROUND WALLS THROUGH THE DOORWAYS.", () => GameSettings.Voices);
                    Option("YOUR MICROPHONE", () => OnOff(GameSettings.MicOn), d => GameSettings.MicOn = !GameSettings.MicOn, "OFF: NOTHING YOU SAY IS SENT.");
                    Option("TALK", () => GameSettings.PushToTalk ? "HOLD T" : "WHEN YOU SPEAK", d => GameSettings.PushToTalk = !GameSettings.PushToTalk,
                           "WHEN YOU SPEAK: YOUR MICROPHONE OPENS BY ITSELF. HOLD T: ONLY WHILE T IS HELD.");
                    Item("DEFAULTS", () => { GameSettings.ResetSound(); Beep(1); ShowPage(Page.Sound); }, "SOUND AS IT COMES.", true);
                    break;

                case Page.Controls:
                    Option("MOUSE SENSITIVITY", () => Pct(GameSettings.Sensitivity), d => GameSettings.Sensitivity = Step(GameSettings.Sensitivity, d, 0.1f, 0.2f, 3f),
                           "HOW FAR THE VIEW TURNS FOR THE MOUSE.", () => Mathf.InverseLerp(0.2f, 3f, GameSettings.Sensitivity));
                    Option("INVERT MOUSE Y", () => OnOff(GameSettings.InvertY), d => GameSettings.InvertY = !GameSettings.InvertY, "MOUSE UP LOOKS DOWN.");
                    Option("CROUCH (C)", () => GameSettings.HoldCrouch ? "HOLD" : "PRESS", d => GameSettings.HoldCrouch = !GameSettings.HoldCrouch,
                           "PRESS: C ONCE TO CROUCH, AGAIN TO STAND. HOLD: CROUCHED WHILE C IS HELD.");
                    Option("CRAWL (CTRL)", () => GameSettings.HoldCrawl ? "HOLD" : "PRESS", d => GameSettings.HoldCrawl = !GameSettings.HoldCrawl,
                           "PRESS: CTRL ONCE TO CRAWL, AGAIN TO GET UP. HOLD: CRAWLING WHILE CTRL IS HELD.");
                    Item("DEFAULTS", () => { GameSettings.ResetControls(); Beep(1); ShowPage(Page.Controls); }, "CONTROLS AS THEY COME.", true);
                    break;

                case Page.Comfort:
                    Option("CAMERA SHAKE", () => Pct(GameSettings.Shake), d => GameSettings.Shake = Step(GameSettings.Shake, d, 0.1f, 0f, 1f),
                           "THE CAMCORDER IS HAND-HELD. IF ITS SHAKE MAKES YOU FEEL SICK, TURN IT DOWN.", () => GameSettings.Shake);
                    Option("MOTION BLUR", () => Pct(GameSettings.MotionBlur), d => GameSettings.MotionBlur = Step(GameSettings.MotionBlur, d, 0.1f, 0f, 1f),
                           "THE SMEAR WHEN THE VIEW TURNS FAST.", () => GameSettings.MotionBlur);
                    Option("BRIGHTNESS", () => { int b = Mathf.RoundToInt(GameSettings.Brightness * 4f); return (b > 0 ? "+" : "") + b; },
                           d => GameSettings.Brightness = Step(GameSettings.Brightness, d, 0.25f, -1.5f, 1.5f),
                           "THE CAMCORDER'S EXPOSURE: BRIGHTER OR DARKER THAN IT CHOOSES.", () => Mathf.InverseLerp(-1.5f, 1.5f, GameSettings.Brightness));
                    Item("REDUCE MOTION", () => { GameSettings.ReduceMotion(); Beep(1); ShowPage(Page.Comfort); }, "IN ONE GO: A QUARTER OF THE SHAKE, NO BLUR.");
                    Item("DEFAULTS", () => { GameSettings.ResetComfort(); Beep(1); ShowPage(Page.Comfort); }, "COMFORT AS IT COMES.", true);
                    break;

                case Page.Access:
                    Option("TEXT SIZE", () => GameSettings.TextSize == 2 ? "LARGER" : GameSettings.TextSize == 1 ? "LARGE" : "NORMAL",
                           d => { GameSettings.TextSize = (GameSettings.TextSize + d + 3) % 3; },
                           "EVERY MENU, THE SIDE PANELS AND THE PROMPTS. THE CAMCORDER'S OWN MARKS KEEP THEIR SIZE.");
                    Option("FLASHING", () => GameSettings.Steady ? "STEADY" : "ON", d => GameSettings.Steady = !GameSettings.Steady,
                           "STEADY: NOTHING BLINKS. REC, NOTICES, LOW LIGHT AND THE SHELF LAMPS' RED STAY LIT.");
                    Option("SUBTITLES", () => OnOff(GameSettings.Subtitles), d => GameSettings.Subtitles = !GameSettings.Subtitles,
                           "WHAT IS SAID, WRITTEN AT THE FOOT OF THE PICTURE (THE PA VOICE AND THE TAPES, WHEN THEY SPEAK).");
                    Item("DEFAULTS", () => { GameSettings.ResetAccess(); Beep(1); ShowPage(Page.Access); }, "AS IT COMES.", true);
                    break;

                case Page.Graphics:
                {
                    var gc = GraphicsController.Ensure();
                    var o = gc.Options;
                    Option("QUALITY", () => { int p = PresetOf(o); return p >= 0 ? GraphicsOptions.PresetNames[p].ToUpperInvariant() : "CUSTOM"; }, d =>
                    {
                        int p = PresetOf(o);
                        p = p < 0 ? 2 : (p + d + GraphicsOptions.PresetNames.Length) % GraphicsOptions.PresetNames.Length;
                        o.ApplyPreset(p); o.Save();
                        ShowPage(Page.Graphics); Select(0, false);
                    }, "HOW MANY LIGHTS CAST SHADOWS, HOW FAR, AND THE LIGHT BOUNCED ROUND THE ROOMS.");
                    Option("RESOLUTION", () => Pct(o.renderScale), d => { o.renderScale = Step(o.renderScale, d, 0.1f, 0.5f, 1f); o.Save(); },
                           "THE PICTURE IS DRAWN SMALLER AND SCALED UP: FASTER, SOFTER.", () => Mathf.InverseLerp(0.5f, 1f, o.renderScale));
                    Option("FRAME RATE", () => o.frameCap > 0 ? o.frameCap + " FPS" : "UNLIMITED", d =>
                    {
                        int i = Array.IndexOf(Caps, o.frameCap); if (i < 0) i = 1;
                        o.frameCap = Caps[(i + d + Caps.Length) % Caps.Length]; o.Save();
                    }, "THE MOST FRAMES A SECOND THE GAME DRAWS.");
                    Option("VSYNC", () => OnOff(o.vSync), d => { o.vSync = !o.vSync; o.Save(); }, "WAITS FOR THE SCREEN: NO TEARING, A LITTLE MORE DELAY.");
                    if (_onAdvanced != null) Item("CAMCORDER MENU (ADVANCED) ►", () => { var a = _onAdvanced; Close(); a(); }, "THE CAMCORDER'S OWN MENU: PICTURE, TAPE, SOUND AND LIGHT, IN DETAIL.");
                    break;
                }

                case Page.Screen:
                    Option("SCREEN", () => ScreenSettings.ModeNames[(int)ScreenSettings.CurrentMode], d =>
                    {
                        int m = ((int)ScreenSettings.CurrentMode + d + ScreenSettings.ModeNames.Length) % ScreenSettings.ModeNames.Length;
                        ScreenSettings.Apply((ScreenSettings.Mode)m, ScreenSettings.CurrentSize);
                    }, "A WINDOW, OR THE WHOLE SCREEN.");
                    Option("SIZE", () => ScreenSettings.SizeName(ScreenSettings.EffectiveSize), d =>
                    {
                        var sizes = ScreenSettings.Sizes();
                        int i = Mathf.Max(0, sizes.IndexOf(ScreenSettings.EffectiveSize));
                        ScreenSettings.Apply(ScreenSettings.CurrentMode, sizes[(i - d + sizes.Count) % sizes.Count]);
                    }, "THE WINDOW'S SIZE IN PIXELS.");
                    break;

                case Page.Keys:
                    // The developer's tools (DevTools): only where they exist at all (the editor, a development build, -dev).
                    if (DevTools.Available)
                        Option("DEVELOPER TOOLS", () => OnOff(DevTools.On), d => { DevTools.On = !DevTools.On; ShowPage(Page.Keys); },
                               "THE DEVELOPER'S KEYS (L, K, V, F2) AND BUILD MODE. NOT IN A PLAYER'S GAME.");
                    {
                        // Two columns, filled down the left then the right.
                        var all = new List<(string, string)>(Keys);
                        if (DevTools.On) all.AddRange(DevKeys);
                        var cols = new VisualElement(); cols.AddToClassList("st-keycols");
                        var a = new VisualElement(); a.AddToClassList("st-keycol");
                        var b = new VisualElement(); b.AddToClassList("st-keycol");
                        cols.Add(a); cols.Add(b); _list.Add(cols);
                        int half = (all.Count + 1) / 2;
                        for (int i = 0; i < all.Count; i++) KeyRow(i < half ? a : b, all[i].Item1, all[i].Item2);
                    }
                    break;
            }
            Select(0, false);
        }

        static readonly int[] Caps = { 30, 60, 90, 120, 144, 0 };

        static readonly (string key, string what)[] Keys =
        {
            ("WASD", "WALK"), ("SHIFT", "RUN"), ("C", "CROUCH"), ("CTRL", "CRAWL"), ("SPACE", "JUMP, CLIMB UP"),
            ("MOUSE", "LOOK"), ("LEFT CLICK", "TAKE, USE  (HOLD: PUSH OR CARRY)"), ("RIGHT CLICK", "SHOVE, AN ITEM'S OTHER USE"),
            ("Q", "DROP  (HOLD: THROW)"), ("E", "SET DOWN  ·  SIT, STAND UP"), ("R", "TURN WHAT YOU HOLD"),
            ("1 2 3 4", "POCKETS"), ("B", "BACKPACK ON / OFF"), ("Z  X", "LEAN LEFT / RIGHT"), ("F", "ZOOM"), ("J", "THE WORK: SHOW OR HIDE"),
            ("T", "HOLD TO TALK (CO-OP, IF SET)"), ("TAB", "PLAYERS (CO-OP)"), ("F5", "THE CAMCORDER'S MENU"), ("ESC", "PAUSE"),
        };

        static readonly (string key, string what)[] DevKeys =
        {
            ("L", "ITEMS, FURNITURE, PROGRESSION"), ("K", "ENEMIES"), ("V", "FLY THROUGH WALLS"), ("F2", "TUNE THE ITEM IN HAND"),
        };

        /// <summary>Which quality preset the graphics are on (-1: changed by hand since, CUSTOM).</summary>
        static int PresetOf(GraphicsOptions o)
        {
            for (int i = 0; i < GraphicsOptions.PresetNames.Length; i++)
            {
                var t = new GraphicsOptions();
                t.ApplyPreset(i);
                if (t.shadowLights == o.shadowLights && Mathf.Approximately(t.shadowDistance, o.shadowDistance) && t.shadowResolution == o.shadowResolution
                    && t.bounceLights == o.bounceLights && Mathf.Approximately(t.lightDistance, o.lightDistance) && t.ambientOcclusion == o.ambientOcclusion)
                    return i;
            }
            return -1;
        }

        void Go(Page p) { Beep(1); ShowPage(p); }

        VisualElement Item(string text, Action act, string desc = null, bool small = false)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item"); row.AddToClassList("st-row");
            if (small) row.AddToClassList("mm-small");
            row.Add(Text("►", "mm-cursor"));
            row.Add(Text(text, "mm-text"));
            Add(row, act, desc);
            return row;
        }

        /// <summary>A line with a value: the label in the first column, ◄ value ► in the second (a click on an arrow steps
        /// that way), and a bar under the value when it is an amount (<paramref name="fill"/>, 0..1).</summary>
        void Option(string label, Func<string> value, Action<int> step, string desc = null, Func<float> fill = null)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item"); row.AddToClassList("st-row");
            row.Add(Text("►", "mm-cursor"));
            row.Add(Text(label, "st-label"));
            var col = new VisualElement(); col.AddToClassList("st-value");
            var line = new VisualElement(); line.AddToClassList("st-valrow");
            var less = Text("◄", "st-arrow"); less.pickingMode = PickingMode.Position;
            var val = Text("", "st-val");
            var more = Text("►", "st-arrow"); more.pickingMode = PickingMode.Position;
            line.Add(less); line.Add(val); line.Add(more);
            col.Add(line);
            VisualElement bar = null;
            if (fill != null)
            {
                var track = new VisualElement(); track.AddToClassList("st-bar");
                bar = new VisualElement(); bar.AddToClassList("st-fill"); track.Add(bar);
                col.Add(track);
            }
            row.Add(col);
            void Refresh()
            {
                val.text = value();
                if (bar != null) bar.style.width = new Length(Mathf.Clamp01(fill()) * 100f, LengthUnit.Percent);
            }
            Refresh();
            int index = _items.Count;
            _steps[index] = d => { step(d); Refresh(); };
            less.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); Select(index, false); step(-1); Refresh(); Beep(0); });
            more.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); Select(index, false); step(1); Refresh(); Beep(0); });
            Add(row, () => { step(1); Refresh(); Beep(0); }, desc);
        }

        /// <summary>A key and what it does (the KEYS page; not chosen with the cursor).</summary>
        void KeyRow(VisualElement column, string key, string what)
        {
            var row = new VisualElement(); row.AddToClassList("st-keyrow");
            row.Add(Text(key, "st-key"));
            row.Add(Text(what, "st-keywhat"));
            column.Add(row);
        }

        void Add(VisualElement row, Action act, string desc)
        {
            int index = _items.Count;
            _items.Add((row, act, desc));
            row.RegisterCallback<PointerEnterEvent>(_ => { if (_sel != index) Select(index, true); });
            row.RegisterCallback<ClickEvent>(_ => Activate(index));
            _list.Add(row);
        }

        void Select(int i, bool sound)
        {
            if (_items.Count == 0) { _desc.text = _page == Page.Keys ? "WHAT EVERY KEY DOES." : ""; return; }
            _sel = Mathf.Clamp(i, 0, _items.Count - 1);
            for (int k = 0; k < _items.Count; k++) _items[k].el.EnableInClassList("mm-sel", k == _sel);
            _desc.text = _items[_sel].desc ?? (_page == Page.Keys ? "WHAT EVERY KEY DOES." : "");
            if (sound) Beep(0);
        }

        void Activate(int i)
        {
            if (i < 0 || i >= _items.Count) return;
            Select(i, false);
            _items[i].act?.Invoke();
        }

        void Beep(int kind)
        {
            if (_beep) _beep.PlayOneShot(SoundBank.MenuBeep(kind), SoundCatalog.Volume(SoundIds.MenuBeep));
        }

        static Label Text(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }
    }
}
