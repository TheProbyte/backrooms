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
    /// The settings page (2026-11-04, the owner: "create a settings page"), for whoever plays: from the main menu's
    /// SETTINGS and from the Escape menu in a world. Styled as the main menu (the camcorder's own menu: Resources/UI/
    /// MainMenu.uss). Pages:
    /// <list type="bullet">
    /// <item>SOUND: master, the building's ambience, music (tapes) (GameSettings).</item>
    /// <item>CONTROLS: mouse sensitivity, invert Y.</item>
    /// <item>CAMERA COMFORT: camera shake and motion blur as shares (Xbox accessibility guideline 117: sliders, not only
    /// on and off), brightness, and one REDUCE MOTION (as Resident Evil 4's motion sickness preset).</item>
    /// <item>GRAPHICS: quality (GraphicsOptions' Low .. Ultra), resolution scale, frame rate cap, vsync.</item>
    /// <item>SCREEN: the window and its size (ScreenSettings).</item>
    /// <item>KEYS: what every key does.</item>
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

        enum Page { Top, Sound, Controls, Comfort, Graphics, Screen, Keys }
        Page _page;
        bool _open;
        Action _onClose, _onAdvanced;
        UIDocument _doc;
        VisualElement _root, _panel, _list;
        Label _pageLabel;
        readonly List<(VisualElement el, Action act)> _items = new List<(VisualElement, Action)>();
        readonly Dictionary<int, Action<int>> _steps = new Dictionary<int, Action<int>>();
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
            ShowPage(Page.Top);
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
            _doc.panelSettings = Resources.Load<PanelSettings>("UI/CamcorderPanel");
            _doc.sortingOrder = 20;
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/MainMenu");
            if (sheet) _root.styleSheets.Add(sheet);
            _root.AddToClassList("mm-root");
            _root.pickingMode = PickingMode.Ignore;
            MainMenu.ApplyMenuFont(_root);
            _panel = new VisualElement();
            _panel.AddToClassList("mm-panel");
            _panel.AddToClassList("mm-wide");
            _root.Add(_panel);
            var header = new VisualElement(); header.AddToClassList("mm-header");
            header.Add(Text("SETTINGS", "mm-title"));
            _pageLabel = Text("", "mm-page"); header.Add(_pageLabel);
            _panel.Add(header);
            var rule = new VisualElement(); rule.AddToClassList("mm-rule"); _panel.Add(rule);
            MainMenu.AddStripe(_panel);
            _list = new VisualElement(); _list.AddToClassList("mm-list"); _panel.Add(_list);
            _panel.Add(Text("▲▼ SELECT    ◄ ► CHANGE    ENTER OK    ESC BACK", "mm-hint"));
        }

        void Update()
        {
            if (!_open) return;
            CamcorderPlayerController.InputBlocked = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var kb = Keyboard.current;
            if (kb == null || _items.Count == 0 || Time.frameCount == _openedFrame) return;
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame, right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            if ((left || right) && _steps.TryGetValue(_sel, out var step)) { step(right ? 1 : -1); Beep(0); return; }
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Select((_sel + _items.Count - 1) % _items.Count, true);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Select((_sel + 1) % _items.Count, true);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Activate(_sel);
            else if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) Back();
        }

        void Back()
        {
            if (_page != Page.Top) { Beep(2); ShowPage(Page.Top); }
            else Close();
        }

        // ---------------- Pages ----------------

        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + " %";
        static float Step(float v, int d, float by, float lo, float hi) => Mathf.Clamp(Mathf.Round((v + d * by) / by) * by, lo, hi);

        void ShowPage(Page page)
        {
            _page = page;
            MainMenu.ApplyLook(_root);
            _list.Clear(); _items.Clear(); _steps.Clear();
            switch (page)
            {
                case Page.Top:
                    _pageLabel.text = "";
                    Item("SOUND ►", () => Go(Page.Sound));
                    Item("CONTROLS ►", () => Go(Page.Controls));
                    Item("CAMERA COMFORT ►", () => Go(Page.Comfort));
                    Item("GRAPHICS ►", () => Go(Page.Graphics));
                    Item("SCREEN ►", () => Go(Page.Screen));
                    Item("KEYS ►", () => Go(Page.Keys));
                    if (_onAdvanced != null) Item("CAMCORDER MENU (ADVANCED) ►", () => { var a = _onAdvanced; Close(); a(); });
                    Item("BACK", Back, true);
                    break;

                case Page.Sound:
                    _pageLabel.text = "SOUND";
                    Option("MASTER", () => Pct(GameSettings.Master), d => GameSettings.Master = Step(GameSettings.Master, d, 0.1f, 0f, 1f));
                    Option("AMBIENCE", () => Pct(GameSettings.Ambience), d => GameSettings.Ambience = Step(GameSettings.Ambience, d, 0.1f, 0f, 1f));
                    Option("MUSIC (TAPES)", () => Pct(GameSettings.Music), d => GameSettings.Music = Step(GameSettings.Music, d, 0.1f, 0f, 1f));
                    Option("VOICES (CO-OP)", () => Pct(GameSettings.Voices), d => GameSettings.Voices = Step(GameSettings.Voices, d, 0.1f, 0f, 1f));
                    Option("YOUR MICROPHONE", () => GameSettings.MicOn ? "ON" : "OFF", d => GameSettings.MicOn = !GameSettings.MicOn);
                    Option("TALK", () => GameSettings.PushToTalk ? "HOLD T" : "WHEN YOU SPEAK", d => GameSettings.PushToTalk = !GameSettings.PushToTalk);
                    Desc("AMBIENCE IS THE BUILDING: THE LIGHTS' HUM, THE VENTILATION, THE ROOMS' TONE. IN CO-OP THE OTHERS HEAR YOU NEAR THEM, ROUND WALLS THROUGH THE DOORWAYS.");
                    Item("DEFAULTS", () => { GameSettings.ResetSound(); Beep(1); ShowPage(Page.Sound); }, true);
                    Item("BACK", Back, true);
                    break;

                case Page.Controls:
                    _pageLabel.text = "CONTROLS";
                    Option("MOUSE SENSITIVITY", () => Pct(GameSettings.Sensitivity), d => GameSettings.Sensitivity = Step(GameSettings.Sensitivity, d, 0.1f, 0.2f, 3f));
                    Option("INVERT MOUSE Y", () => GameSettings.InvertY ? "ON" : "OFF", d => GameSettings.InvertY = !GameSettings.InvertY);
                    Item("DEFAULTS", () => { GameSettings.ResetControls(); Beep(1); ShowPage(Page.Controls); }, true);
                    Item("BACK", Back, true);
                    break;

                case Page.Comfort:
                    _pageLabel.text = "CAMERA COMFORT";
                    Option("CAMERA SHAKE", () => Pct(GameSettings.Shake), d => GameSettings.Shake = Step(GameSettings.Shake, d, 0.1f, 0f, 1f));
                    Option("MOTION BLUR", () => Pct(GameSettings.MotionBlur), d => GameSettings.MotionBlur = Step(GameSettings.MotionBlur, d, 0.1f, 0f, 1f));
                    Option("BRIGHTNESS", () => { int b = Mathf.RoundToInt(GameSettings.Brightness * 4f); return (b > 0 ? "+" : "") + b; },
                           d => GameSettings.Brightness = Step(GameSettings.Brightness, d, 0.25f, -1.5f, 1.5f));
                    Item("REDUCE MOTION", () => { GameSettings.ReduceMotion(); Beep(1); ShowPage(Page.Comfort); });
                    Desc("THE CAMCORDER IS HAND-HELD. IF ITS SHAKE OR BLUR MAKES YOU FEEL SICK, TURN THEM DOWN");
                    Desc("(REDUCE MOTION: A QUARTER OF THE SHAKE, NO BLUR).");
                    Item("DEFAULTS", () => { GameSettings.ResetComfort(); Beep(1); ShowPage(Page.Comfort); }, true);
                    Item("BACK", Back, true);
                    break;

                case Page.Graphics:
                {
                    _pageLabel.text = "GRAPHICS";
                    var gc = GraphicsController.Ensure();
                    var o = gc.Options;
                    Option("QUALITY", () => { int p = PresetOf(o); return p >= 0 ? GraphicsOptions.PresetNames[p].ToUpperInvariant() : "CUSTOM"; }, d =>
                    {
                        int p = PresetOf(o);
                        p = p < 0 ? 2 : (p + d + GraphicsOptions.PresetNames.Length) % GraphicsOptions.PresetNames.Length;
                        o.ApplyPreset(p); o.Save();
                        ShowPage(Page.Graphics); Select(0, false);
                    });
                    Option("RESOLUTION", () => Pct(o.renderScale), d => { o.renderScale = Step(o.renderScale, d, 0.1f, 0.5f, 1f); o.Save(); });
                    Option("FRAME RATE", () => o.frameCap > 0 ? o.frameCap + " FPS" : "UNLIMITED", d =>
                    {
                        int i = Array.IndexOf(Caps, o.frameCap); if (i < 0) i = 1;
                        o.frameCap = Caps[(i + d + Caps.Length) % Caps.Length]; o.Save();
                    });
                    Option("VSYNC", () => o.vSync ? "ON" : "OFF", d => { o.vSync = !o.vSync; o.Save(); });
                    Option("ART STYLE", () => Backrooms.Generation.ArtStyle.Current == Backrooms.Generation.ArtStyle.Look.Overflow ? "OVERFLOW" : "CLASSIC", d =>
                    {
                        var s = Backrooms.Generation.ArtStyle.Current;
                        Backrooms.Generation.ArtStyle.Current = s == Backrooms.Generation.ArtStyle.Look.Overflow ? Backrooms.Generation.ArtStyle.Look.Classic : Backrooms.Generation.ArtStyle.Look.Overflow;
                        MainMenu.ApplyLook(_root);                    // the menus follow at once
                    });
                    Desc("QUALITY SETS HOW MANY LIGHTS CAST SHADOWS, HOW FAR, AND THE LIGHT BOUNCED ROUND THE ROOMS.");
                    Desc("ART STYLE: THE NEW PAINTED LOOK OR THE OLD REALISTIC ONE. TAKES EFFECT WHEN A WORLD LOADS.");
                    Item("BACK", Back, true);
                    break;
                }

                case Page.Screen:
                    _pageLabel.text = "SCREEN";
                    Option("SCREEN", () => ScreenSettings.ModeNames[(int)ScreenSettings.CurrentMode], d =>
                    {
                        int m = ((int)ScreenSettings.CurrentMode + d + ScreenSettings.ModeNames.Length) % ScreenSettings.ModeNames.Length;
                        ScreenSettings.Apply((ScreenSettings.Mode)m, ScreenSettings.CurrentSize);
                    });
                    Option("SIZE", () => ScreenSettings.SizeName(ScreenSettings.EffectiveSize), d =>
                    {
                        var sizes = ScreenSettings.Sizes();
                        int i = Mathf.Max(0, sizes.IndexOf(ScreenSettings.EffectiveSize));
                        ScreenSettings.Apply(ScreenSettings.CurrentMode, sizes[(i - d + sizes.Count) % sizes.Count]);
                    });
                    Item("BACK", Back, true);
                    break;

                case Page.Keys:
                    _pageLabel.text = "KEYS";
                    foreach (var (k, what) in Keys) Desc(k.PadRight(14) + what);
                    Item("BACK", Back, true);
                    break;
            }
            Select(0, false);
        }

        static readonly int[] Caps = { 30, 60, 90, 120, 144, 0 };

        static readonly (string key, string what)[] Keys =
        {
            ("W A S D", "WALK"), ("SHIFT", "RUN"), ("C", "CROUCH"), ("CTRL", "CRAWL"), ("SPACE", "JUMP, CLIMB UP"),
            ("MOUSE", "LOOK"), ("LEFT CLICK", "TAKE, USE  (HOLD: PUSH OR CARRY)"), ("RIGHT CLICK", "SHOVE, AN ITEM'S OTHER USE"),
            ("Q", "DROP  (HOLD: THROW)"), ("E", "SET DOWN  ·  SIT, STAND UP"), ("R", "TURN WHAT YOU HOLD"),
            ("1 2 3 4", "POCKETS"), ("B", "BACKPACK ON / OFF"), ("Z  X", "LEAN LEFT / RIGHT"), ("F", "ZOOM"), ("T", "HOLD TO TALK (CO-OP, IF SET)"), ("ESC", "MENU"),
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

        VisualElement Item(string text, Action act, bool small = false)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item");
            if (small) row.AddToClassList("mm-small");
            row.Add(Text("►", "mm-cursor"));
            row.Add(Text(text, "mm-text"));
            Add(row, act);
            return row;
        }

        void Option(string label, Func<string> value, Action<int> step)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item");
            row.Add(Text("►", "mm-cursor"));
            var text = Text("", "mm-text");
            row.Add(text);
            void Refresh() => text.text = label + "   ◄ " + value() + " ►";
            Refresh();
            _steps[_items.Count] = d => { step(d); Refresh(); };
            Add(row, () => { step(1); Refresh(); Beep(0); });
        }

        void Desc(string text) => _list.Add(Text(text, "mm-desc"));

        void Add(VisualElement row, Action act)
        {
            int index = _items.Count;
            _items.Add((row, act));
            row.RegisterCallback<PointerEnterEvent>(_ => { if (_sel != index) Select(index, true); });
            row.RegisterCallback<ClickEvent>(_ => Activate(index));
            _list.Add(row);
        }

        void Select(int i, bool sound)
        {
            if (_items.Count == 0) return;
            _sel = Mathf.Clamp(i, 0, _items.Count - 1);
            for (int k = 0; k < _items.Count; k++) _items[k].el.EnableInClassList("mm-sel", k == _sel);
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
