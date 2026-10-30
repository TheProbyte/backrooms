using System;
using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Interaction;
using Backrooms.Saves;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Backrooms.Menu
{
    /// <summary>
    /// The main menu, shown whenever the world scene starts without a save chosen. Behind it is the camcorder's own
    /// picture: it lies on its side on the carpet of a fixed world (<see cref="MenuSeed"/>), focused on the carpet just
    /// in front of the lens, the room beyond out of focus, the OSD on STBY. The menu is the camcorder's menu: PLAY (the
    /// three world saves), SETTINGS (the window and its size, and the F5 menu's picture, sound and light tabs), QUIT. Mouse or keys (arrows / W S, Enter, Escape).
    /// Added by the WorldStreamer in menu mode; the player is switched off while it is up.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class MainMenu : MonoBehaviour
    {
        /// <summary>The world behind the menu.</summary>
        public const int MenuSeed = 42;
        static MainMenu s_instance;
        public static bool IsShowing => s_instance;

        public WorldStreamer streamer;

        [Header("The shot: the camcorder lying on its side on the carpet")]
        [Tooltip("Height of the lens above the carpet (m).")] public float lensHeight = 0.075f;
        [Tooltip("Tipped down toward the carpet (degrees).")] public float tilt = 4f;
        [Tooltip("Lying on its side (degrees of roll).")] public float roll = 24f;
        [Tooltip("Focus distance (m): the carpet just in front.")] public float focus = 0.6f;
        [Tooltip("Aperture (f-number): lower blurs the room more.")] public float aperture = 2.8f;

        Transform _player;
        Camera _cam;
        CamcorderRig _rig;
        CamcorderOSD _osd;
        bool _placed;
        Vector3 _pos;
        Quaternion _rot;
        AudioSource _beep;

        enum Page { Top, Saves, Settings }
        Page _page;
        UIDocument _doc;
        VisualElement _root, _panel, _list;
        Label _pageLabel;
        readonly List<(VisualElement el, Action act)> _items = new List<(VisualElement, Action)>();
        readonly Dictionary<int, Action<int>> _steps = new Dictionary<int, Action<int>>();
        CamcorderVisualMenu _vm;
        bool _vmShown;
        int _sel;
        int _armedSlot = -1;
        float _armedUntil;
        Label _armedLabel;
        bool _leaving;

        void Awake()
        {
            s_instance = this;
            if (!streamer) streamer = GetComponent<WorldStreamer>();
            _player = streamer ? streamer.player : null;
            if (_player)
            {
                // No one is holding the camcorder: no walking, looking, hand, body or footsteps.
                foreach (var b in new Behaviour[] { _player.GetComponent<CamcorderPlayerController>(), _player.GetComponent<PropGrabber>(),
                                                    _player.GetComponent<FirstPersonBody>(), _player.GetComponent<PlayerAudio>() })
                    if (b) b.enabled = false;
                var body = _player.GetComponent<CharacterController>();
                if (body) body.enabled = false;
                _cam = _player.GetComponentInChildren<Camera>();
            }
            if (!_cam) _cam = Camera.main;
            if (_cam)
            {
                var shake = _cam.GetComponent<HandheldCameraShake>();
                if (shake) shake.enabled = false;
                _rig = _cam.GetComponent<CamcorderRig>();
            }
            // The camcorder's own F5 menu and the developer menu wait for the game.
            _vm = FindAnyObjectByType<CamcorderVisualMenu>();
            if (_vm) _vm.enabled = false;
            var items = GetComponent<ItemsMenu>();
            if (items) items.enabled = false;
            _osd = FindAnyObjectByType<CamcorderOSD>();
            if (_osd) _osd.standby = true;

            _beep = gameObject.AddComponent<AudioSource>();
            _beep.playOnAwake = false; _beep.spatialBlend = 0f; _beep.volume = 0.45f; _beep.ignoreListenerPause = true;
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            if (_rig) { _rig.focusOverride = -1f; _rig.apertureOverride = -1f; }
            if (_osd) _osd.standby = false;
        }

        void Start()
        {
            Build();
            Show(Page.Top);
        }

        void Update()
        {
            CamcorderPlayerController.InputBlocked = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (_armedSlot >= 0 && Time.unscaledTime > _armedUntil) Disarm();

            // The F5 menu opened from SETTINGS has the keys until it closes (F5, Escape or END).
            if (_vmShown)
            {
                if (_vm && _vm.enabled && _vm.IsOpen) return;
                _vmShown = false;
                if (_vm) _vm.enabled = false;
                _panel.style.display = DisplayStyle.Flex;
                return;
            }

            var kb = Keyboard.current;
            if (kb == null || _leaving || _items.Count == 0) return;
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame, right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            if ((left || right) && _steps.TryGetValue(_sel, out var step)) { step(right ? 1 : -1); Beep(0); return; }
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Select((_sel + _items.Count - 1) % _items.Count, true);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Select((_sel + 1) % _items.Count, true);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Activate(_sel);
            else if ((kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) && _page != Page.Top) { Beep(2); Show(Page.Top); }
            else if (kb.deleteKey.wasPressedThisFrame && _page == Page.Saves && _sel < SaveSlots.Count && SaveSlots.Exists(_sel)) Erase(_sel, null);
        }

        void LateUpdate()
        {
            if (!_cam) return;
            if (!_placed) Place();
            _cam.transform.SetPositionAndRotation(_pos, _rot);
            if (_rig) { _rig.focusOverride = focus; _rig.apertureOverride = aperture; }
        }

        // ---------------- The shot ----------------

        /// <summary>
        /// On the carpet just behind a lit ceiling light near the spawn (so the carpet in front of the lens is lit), looking
        /// along the deepest view from there, rolled onto its side.
        /// </summary>
        void Place()
        {
            _placed = true;
            Physics.SyncTransforms();
            Vector3 spawn = _player ? _player.position : _cam.transform.position;
            var spots = new List<Vector3>();
            var L = streamer ? streamer.LayoutAt(Vector2Int.zero) : null;
            if (L != null)
                foreach (var f in L.fixtures)
                    if (f.on && !f.faulty)
                        spots.Add(new Vector3((L.coord.x * L.N + f.cell.x + 0.5f) * OfficeBiome.CellSize, 0f, (L.coord.y * L.N + f.cell.y + 0.5f) * OfficeBiome.CellSize));
            if (spots.Count == 0) spots.Add(spawn);

            float best = float.MinValue;
            _pos = spawn; _rot = Quaternion.identity;
            foreach (var s in spots)
            {
                if (!Physics.Raycast(s + Vector3.up * 1.2f, Vector3.down, out var down, 2f, ~0, QueryTriggerInteraction.Ignore) || down.point.y > 0.1f) continue;   // furniture there
                float floor = down.point.y;
                var eye = new Vector3(s.x, floor + 0.3f, s.z);
                for (int a = 0; a < 360; a += 15)
                {
                    var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    var lens = eye - dir * 0.55f;                     // a little behind the light: the lit carpet is in front
                    if (Physics.CheckSphere(new Vector3(lens.x, floor + 0.25f, lens.z), 0.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float d = Physics.Raycast(lens, dir, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 40f;
                    // Deep, with other lights down that way (a lit room beyond, not a dark one), near the spawn.
                    int lit = 0;
                    foreach (var o in spots)
                    {
                        var to = o - lens; to.y = 0f;
                        if (to.magnitude > 2f && to.magnitude < d + 1.5f && Vector3.Angle(to, dir) < 22f) lit++;
                    }
                    float score = Mathf.Min(d, 18f) + 2.5f * Mathf.Min(lit, 4) - 0.15f * Vector3.Distance(s, spawn);
                    if (score > best)
                    {
                        best = score;
                        _pos = new Vector3(lens.x, floor + lensHeight, lens.z);
                        _rot = Quaternion.Euler(tilt, a, roll);
                    }
                }
            }
        }

        // ---------------- UI ----------------

        void Build()
        {
            var go = new GameObject("MainMenu_UI");
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = Resources.Load<PanelSettings>("UI/CamcorderPanel");
            _doc.sortingOrder = 10;
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/MainMenu");
            if (sheet) _root.styleSheets.Add(sheet);
            _root.AddToClassList("mm-root");
            _root.pickingMode = PickingMode.Ignore;
            ApplyMenuFont(_root);

            _panel = new VisualElement();
            _panel.AddToClassList("mm-panel");
            _root.Add(_panel);
            var header = new VisualElement(); header.AddToClassList("mm-header");
            header.Add(Text("MENU", "mm-title"));
            _pageLabel = Text("", "mm-page"); header.Add(_pageLabel);
            _panel.Add(header);
            var rule = new VisualElement(); rule.AddToClassList("mm-rule"); _panel.Add(rule);
            _list = new VisualElement(); _list.AddToClassList("mm-list"); _panel.Add(_list);
            _panel.Add(Text("▲▼ SELECT    ENTER OK    ESC BACK", "mm-hint"));
        }

        void Show(Page page)
        {
            _page = page;
            Disarm();
            _list.Clear(); _items.Clear(); _steps.Clear();
            _panel.EnableInClassList("mm-wide", page != Page.Top);
            switch (page)
            {
                case Page.Top:
                    _pageLabel.text = "";
                    Item("PLAY", () => { Beep(1); Show(Page.Saves); });
                    Item("SETTINGS", () => { Beep(1); Show(Page.Settings); });
                    Item("QUIT", Quit);
                    break;
                case Page.Saves:
                    _pageLabel.text = "WORLD SELECT";
                    for (int i = 0; i < SaveSlots.Count; i++) Slot(i);
                    Item("BACK", () => { Beep(2); Show(Page.Top); }, true);
                    break;
                case Page.Settings:
                    _pageLabel.text = "SETTINGS";
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
                    if (_vm) Item("PICTURE, SOUND AND LIGHT ►", OpenCamcorderMenu);
                    _list.Add(Text("◄ ► CHANGES A SETTING.  IN A WORLD, F5 OPENS THE FULL MENU.", "mm-desc"));
                    Item("BACK", () => { Beep(2); Show(Page.Top); }, true);
                    break;
            }
            Select(0, false);
        }

        VisualElement Item(string text, Action act, bool small = false)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item");
            if (small) row.AddToClassList("mm-small");
            row.Add(Text("►", "mm-cursor"));
            row.Add(Text(text, "mm-text"));
            Add(row, act);
            return row;
        }

        /// <summary>A setting with a value: ◄ ► (or A D) step it, Enter or a click steps it forward.</summary>
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

        /// <summary>The F5 menu's picture, sound, display and light tabs over the menu (the room behind shows each change).</summary>
        void OpenCamcorderMenu()
        {
            if (!_vm) return;
            Beep(1);
            _vm.enabled = true;
            _vm.OpenFromMainMenu();
            _vmShown = true;
            _panel.style.display = DisplayStyle.None;
        }

        void Add(VisualElement row, Action act)
        {
            int index = _items.Count;
            _items.Add((row, act));
            row.RegisterCallback<PointerEnterEvent>(_ => { if (_sel != index) Select(index, true); });
            row.RegisterCallback<ClickEvent>(_ => Activate(index));
            _list.Add(row);
        }

        void Slot(int slot)
        {
            var d = SaveSlots.Load(slot);
            var row = new VisualElement(); row.AddToClassList("mm-item"); row.AddToClassList("mm-slot");
            row.Add(Text("►", "mm-cursor"));
            var body = new VisualElement(); body.AddToClassList("mm-slotbody");
            body.Add(Text($"WORLD {slot + 1}", "mm-slotname"));
            if (d == null) body.Add(Text("EMPTY  —  START A NEW SAVE", "mm-empty"));
            else
            {
                string zone = string.IsNullOrEmpty(d.zone) ? "" : d.zone.ToUpperInvariant() + "    ";
                body.Add(Text($"{zone}{Tape(d.playSeconds)} ON TAPE", "mm-detail"));
                body.Add(Text($"SEED {d.seed}    SAVED {Stamp(d.saved)}", "mm-detail"));
            }
            row.Add(body);
            if (d != null)
            {
                var erase = Text("ERASE", "mm-erase");
                erase.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); Erase(slot, erase); });
                row.Add(erase);
            }
            Add(row, () => PlaySlot(slot));
        }

        static string Tape(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        }

        static string Stamp(string iso)
        {
            if (!DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)) return "?";
            return t.ToString("MMM. dd yyyy  h:mm tt", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
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
            if (_leaving || i < 0 || i >= _items.Count) return;
            Select(i, false);
            _items[i].act?.Invoke();
        }

        void PlaySlot(int slot)
        {
            Beep(1);
            _leaving = true;
            _panel.style.display = DisplayStyle.None;
            GameSession.Play(slot);
        }

        /// <summary>First press arms ERASE, a second one within a few seconds empties the slot.</summary>
        void Erase(int slot, Label label)
        {
            if (label == null && _sel < _items.Count) label = _items[_sel].el.Q<Label>(className: "mm-erase");
            if (_armedSlot == slot)
            {
                SaveSlots.Erase(slot);
                Beep(2);
                int keep = _sel;
                Show(Page.Saves);
                Select(keep, false);
                return;
            }
            Disarm();
            _armedSlot = slot; _armedUntil = Time.unscaledTime + 4f; _armedLabel = label;
            if (label != null) { label.text = "ERASE? AGAIN"; label.AddToClassList("mm-armed"); }
            Beep(0);
        }

        void Disarm()
        {
            if (_armedLabel != null) { _armedLabel.text = "ERASE"; _armedLabel.RemoveFromClassList("mm-armed"); }
            _armedSlot = -1; _armedLabel = null;
        }

        void Quit()
        {
            Beep(2);
            _leaving = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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
            if (cls == "mm-erase") l.pickingMode = PickingMode.Position;
            return l;
        }

        /// <summary>The same monospaced system font as the F5 menu (a camcorder's character generator).</summary>
        static UnityEngine.TextCore.Text.FontAsset s_font;

        static void ApplyMenuFont(VisualElement root)
        {
            // Made once and kept across scene loads (the menu comes back with every load of the world scene).
            if (!s_font)
                foreach (var family in new[] { "Consolas", "Lucida Console", "Courier New" })
                {
                    try
                    {
                        s_font = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(family, "Bold");
                        if (!s_font) s_font = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(family, "Regular");
                        if (s_font) break;
                    }
                    catch (Exception) { }
                }
            if (s_font) root.style.unityFontDefinition = FontDefinition.FromSDFFont(s_font);
        }
    }
}
